using System.Reflection;
using Assets.Script.Misc;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using MapRuleLibrary.Adventure;

namespace GloomhavenVR.WorldUI;

/// <summary>Adapt the campaign map's first-save merchant lesson to the physical station.
/// The native VisitMerchant step is completed by the flat merchant toggle and BuyItem is only
/// started by UIShopItemWindow.FinishEnterShop. With a persistent physical resident and no
/// merchant toggle, simply hiding VisitMerchant strands the map at its travel lock. Resolve
/// these two flat-only step promises through MapFTUEManager.StartStep so its own completion
/// callbacks, party-display restoration, map unlocks and next InteractWithMap step still run.
/// Native BuyItem is completed by the WorldMap toggle on shop exit, without testing whether
/// UIShopItemInventory.BuyItem succeeded (see the build-507 savegame flow evidence). It is a
/// flat merchant navigation step, not a purchase requirement. The immersive equivalent has no
/// flat shop-entry/exit toggle; resolving it preserves the native progression contract.
/// Never mark saved FTUE state directly; if the original BuyItem step is unavailable, leave the
/// complete native lesson alone. Turning the immersive option off retains the 1.0.6 flow.</summary>
internal static class TownServiceTutorialPatches
{
    private static readonly FieldInfo? ShopStepField = AccessTools.Field(typeof(UIShopItemWindow), "ftueStep");
    private static MapFTUEManager? _pendingManager;
    private static EMapFTUEStep _pendingStep;

    private static bool Active(MapFTUEManager manager) => MapRoomDriver.Active
        && WorldUIConfig.ImmersiveTownServices.Value
        && MapFTUEManager.IsPlaying && AdventureState.MapState?.IsCampaign == true
        && ReferenceEquals(Singleton<MapFTUEManager>.Instance, manager);

    private static UIMapFTUEStep? OriginalBuyStep()
    {
        UIShopItemWindow? shop = Singleton<UIShopItemWindow>.Instance;
        UIMapFTUEStep? step = shop != null ? ShopStepField?.GetValue(shop) as UIMapFTUEStep : null;
        return step != null && step.Step == EMapFTUEStep.BuyItem ? step : null;
    }

    internal static bool SkipFlatMerchantStep(MapFTUEManager manager, EMapFTUEStep step)
        => Active(manager) && OriginalBuyStep() != null
           && (step == EMapFTUEStep.VisitMerchant || step == EMapFTUEStep.BuyItem);

    internal static void ContinueAfterMerchant(MapFTUEManager manager, IMapFTUEStep step)
    {
        if (step.Step != EMapFTUEStep.VisitMerchant || !SkipFlatMerchantStep(manager, step.Step)
            || !manager.HasCompletedStep(EMapFTUEStep.VisitMerchant)
            || manager.HasCompletedStep(EMapFTUEStep.BuyItem)) return;
        StartNativeBuyStep(manager);
    }

    private static void StartNativeBuyStep(MapFTUEManager manager)
    {
        // The flat window normally starts this serialized step after EnterShop. If an
        // OnFinishedStep listener already started it, HasCompletedStep above makes this inert.
        // StartStep calls the game's own CheckStartedStep and completion listeners; our patch
        // below resolves only the flat UI promise, never MapFTUECompleted or the save directly.
        UIMapFTUEStep? buy = OriginalBuyStep();
        if (buy == null) return;
        manager.StartStep(buy);
    }

    internal static void Tick()
    {
        MapFTUEManager? manager = Singleton<MapFTUEManager>.Instance;
        if (manager == null || !Active(manager) || OriginalBuyStep() == null)
        { _pendingManager = null; _pendingStep = EMapFTUEStep.None; return; }
        EMapFTUEStep current = manager.CurrentStep;
        // The player may enable immersive residents after the flat lesson has already
        // started. StartStep's prefix cannot intercept that earlier promise, so complete
        // the current native step once and let its own Hide/OnCompleteStep callback finish.
        // A delayed native hide must not receive CompleteStep every frame.
        if (current == EMapFTUEStep.VisitMerchant || current == EMapFTUEStep.BuyItem)
        {
            if (!ReferenceEquals(manager, _pendingManager) || current != _pendingStep)
            {
                _pendingManager = manager; _pendingStep = current;
                manager.CompleteStep(current);
            }
            return;
        }
        _pendingManager = null; _pendingStep = EMapFTUEStep.None;
        if (current == EMapFTUEStep.None && manager.HasCompletedStep(EMapFTUEStep.VisitMerchant)
            && !manager.HasCompletedStep(EMapFTUEStep.BuyItem)) StartNativeBuyStep(manager);
    }
}

[HarmonyPatch(typeof(UIMapFTUEStep), nameof(UIMapFTUEStep.StartStep))]
internal static class TownServiceTutorialStepPatch
{
    private static bool Prefix(UIMapFTUEStep __instance, ref ICallbackPromise __result)
    {
        MapFTUEManager? manager = Singleton<MapFTUEManager>.Instance;
        if (manager == null || !TownServiceTutorialPatches.SkipFlatMerchantStep(manager, __instance.Step))
            return true;
        __result = CallbackPromise.Resolved();
        VRLog.Note("Tutorial", "Immersive merchant onboarding completed native flat-only step "
            + __instance.Step + "; map tutorial continuation stays with MapFTUEManager.");
        return false;
    }
}

[HarmonyPatch(typeof(MapFTUEManager), nameof(MapFTUEManager.StartStep))]
internal static class TownServiceTutorialSequencePatch
{
    private static void Postfix(MapFTUEManager __instance, IMapFTUEStep step)
        => TownServiceTutorialPatches.ContinueAfterMerchant(__instance, step);
}
