using System;
using System.Reflection;
using Assets.Script.Misc;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using UnityEngine.UI;

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
/// complete native lesson alone. The converted flat shop's real exit also finishes its exact
/// pending BuyItem promise after native selection/listener cleanup. Map-surface changes and
/// temporary presentation hides are not exits and cannot consume the lesson.</summary>
internal static class TownServiceTutorialPatches
{
    private static readonly FieldInfo? ShopStepField = AccessTools.Field(typeof(UIShopItemWindow), "ftueStep");
    private static readonly FieldInfo? CurrentStepField = AccessTools.Field(typeof(MapFTUEManager), "currentStep");
    private static readonly FieldInfo? StepPromiseField = AccessTools.Field(typeof(UIMapFTUEStep), "promise");
    private static MapFTUEManager? _pendingManager;
    private static EMapFTUEStep _pendingStep;
    private static CallbackPromise? _pendingPromise;
    [ThreadStatic] private static UIShopItemWindow? _nativeExitShop;
    private static CallbackPromise? _closingPromise;
    private static bool _reportedCloseFailure;

    private static bool PlayingCampaign(MapFTUEManager manager) => WorldUIConfig.ConversionActive
        && MapFTUEManager.IsPlaying && AdventureState.MapState?.IsCampaign == true
        && ReferenceEquals(Singleton<MapFTUEManager>.Instance, manager);
    private static bool Active(MapFTUEManager manager) => PlayingCampaign(manager)
        && MapRoomDriver.Active && WorldUIConfig.ImmersiveTownServices.Value;

    internal readonly struct ShopExitScope : IDisposable
    {
        private readonly UIShopItemWindow? _previous;
        private readonly bool _entered;
        internal ShopExitScope(UIShopItemWindow shop)
        { _previous = _nativeExitShop; _entered = true; _nativeExitShop = shop; }
        public void Dispose() { if (_entered) _nativeExitShop = _previous; }
    }

    internal sealed class MerchantClose
    {
        internal readonly MapFTUEManager Manager;
        internal readonly UIShopItemWindow Shop;
        internal readonly UIMapFTUEStep Step;
        internal readonly CallbackPromise Promise;
        internal readonly object Map;
        internal MerchantClose(MapFTUEManager manager, UIShopItemWindow shop,
            UIMapFTUEStep step, CallbackPromise promise, object map)
        { Manager = manager; Shop = shop; Step = step; Promise = promise; Map = map; }
    }

    private static bool PendingNativeBuy(MapFTUEManager manager, UIShopItemWindow shop,
        UIMapFTUEStep step, CallbackPromise promise) => PlayingCampaign(manager)
        && ReferenceEquals(Singleton<UIShopItemWindow>.Instance, shop)
        && manager.CurrentStep == EMapFTUEStep.BuyItem
        && manager.HasCompletedStep(EMapFTUEStep.VisitMerchant)
        && !manager.HasCompletedStep(EMapFTUEStep.BuyItem)
        && step.Step == EMapFTUEStep.BuyItem && promise.IsPending
        && ReferenceEquals(CurrentStepField?.GetValue(manager), step)
        && ReferenceEquals(ShopStepField?.GetValue(shop), step)
        && ReferenceEquals(StepPromiseField?.GetValue(step), promise);

    internal static MerchantClose? CaptureMerchantClose(UIShopItemWindow shop)
    {
        try { return CaptureMerchantCloseCore(shop); }
        catch (Exception ex) { ReportCloseFailure(ex); return null; }
    }

    // A borrowed shop may already be native-hidden while its converted VR surface
    // remains visible. Its later real X close must still finish the pending lesson;
    // no additional OnHidden event runs for this already-cleaned-up branch.
    internal static MerchantClose? CaptureConvertedMerchantClose(UIWindow window)
    {
        try
        {
            UIShopItemWindow? shop = window != null ? window.GetComponent<UIShopItemWindow>() : null;
            return shop != null ? CaptureMerchantCloseCore(shop) : null;
        }
        catch (Exception ex) { ReportCloseFailure(ex); return null; }
    }

    internal static void CompleteConvertedMerchantClose(UIWindow window, MerchantClose? close)
    {
        try
        {
            // A refused or failed close which left the native window open cannot
            // progress the lesson. Ordinary OnHidden already completes/deduplicates it.
            if (window != null && !window.IsOpen) CompleteMerchantClose(close);
        }
        catch (Exception ex) { ReportCloseFailure(ex); }
    }

    private static MerchantClose? CaptureMerchantCloseCore(UIShopItemWindow shop)
    {
        // UIWindow invokes onHidden BEFORE changing IsOpen. Only an actual native Exit
        // (including the shop's exit button) or a scoped VR X/second-cap close is eligible;
        // hiding the borrowed original behind another purchase/intro panel is not a close.
        if (!TownWindowCloseScope.Active && !ReferenceEquals(_nativeExitShop, shop)) return null;
        MapFTUEManager? manager = Singleton<MapFTUEManager>.Instance;
        UIMapFTUEStep? step = ShopStepField?.GetValue(shop) as UIMapFTUEStep;
        CallbackPromise? promise = step != null ? StepPromiseField?.GetValue(step) as CallbackPromise : null;
        return manager != null && step != null && promise != null
            && PendingNativeBuy(manager, shop, step, promise) && AdventureState.MapState is { } map
            ? new MerchantClose(manager, shop, step, promise, map) : null;
    }

    internal static void CompleteMerchantClose(MerchantClose? close)
    {
        try { CompleteMerchantCloseCore(close); }
        catch (Exception ex)
        {
            // A failed native FinishStep must not permanently consume this pending
            // promise. Reentrancy remains guarded while dispatch is in progress.
            if (close != null && ReferenceEquals(_closingPromise, close.Promise)) _closingPromise = null;
            ReportCloseFailure(ex);
        }
    }

    private static void CompleteMerchantCloseCore(MerchantClose? close)
    {
        if (close == null || ReferenceEquals(_closingPromise, close.Promise)
            || ReferenceEquals(_pendingPromise, close.Promise)
            || !ReferenceEquals(AdventureState.MapState, close.Map)
            || !PendingNativeBuy(close.Manager, close.Shop, close.Step, close.Promise)) return;
        // Native OnHidden has now disabled selection, detached shop listeners and run
        // onExit. A callback may already have completed/replaced the lesson: revalidate
        // its exact promise, and mark it before FinishStep can reenter this same exit.
        _closingPromise = close.Promise;
        close.Manager.CompleteStep(EMapFTUEStep.BuyItem);
        VRLog.Note("Tutorial", "Converted merchant exit requested native BuyItem completion after shop cleanup.");
        if (!close.Promise.IsPending && ReferenceEquals(_closingPromise, close.Promise)) _closingPromise = null;
    }

    private static void ReportCloseFailure(Exception ex)
    {
        if (_reportedCloseFailure) return;
        _reportedCloseFailure = true;
        // Do not let cosmetic diagnostics replace an exception from the original shop.
        try { VRLog.Warn("Tutorial", "Native merchant onboarding close could not be completed: " + ex); }
        catch { }
    }

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
        if (manager == null || !MapFTUEManager.IsPlaying)
        { _pendingManager = null; _pendingStep = EMapFTUEStep.None; _pendingPromise = null; return; }
        EMapFTUEStep current = manager.CurrentStep;
        if (_closingPromise?.IsPending == false) _closingPromise = null;
        var original = CurrentStepField?.GetValue(manager) as UIMapFTUEStep;
        var promise = original != null ? StepPromiseField?.GetValue(original) as CallbackPromise : null;
        if (!ReferenceEquals(manager, _pendingManager) || current != _pendingStep
            || !ReferenceEquals(promise, _pendingPromise))
        { _pendingManager = null; _pendingStep = EMapFTUEStep.None; _pendingPromise = null; }
        // Turning the option off cannot revoke a native FinishStep already waiting
        // for its hide callback. Keep that exact promise's one-shot marker so an
        // off/on transition cannot restart the same pending close animation.
        if (!Active(manager) || OriginalBuyStep() == null) return;
        // The player may enable immersive residents after the flat lesson has already
        // started. StartStep's prefix cannot intercept that earlier promise, so complete
        // the current native step once and let its own Hide/OnCompleteStep callback finish.
        // A delayed native hide must not receive CompleteStep every frame.
        if (current == EMapFTUEStep.VisitMerchant || current == EMapFTUEStep.BuyItem)
        {
            if (_pendingManager == null && promise?.IsPending == true && !ReferenceEquals(_closingPromise, promise))
            {
                _pendingManager = manager; _pendingStep = current; _pendingPromise = promise;
                try { manager.CompleteStep(current); }
                catch (Exception ex)
                {
                    _pendingManager = null; _pendingStep = EMapFTUEStep.None; _pendingPromise = null;
                    ReportCloseFailure(ex);
                }
            }
            return;
        }
        _pendingManager = null; _pendingStep = EMapFTUEStep.None; _pendingPromise = null;
        if (current == EMapFTUEStep.None && manager.HasCompletedStep(EMapFTUEStep.VisitMerchant)
            && !manager.HasCompletedStep(EMapFTUEStep.BuyItem)) StartNativeBuyStep(manager);
    }
}

[HarmonyPatch(typeof(UIShopItemWindow), nameof(UIShopItemWindow.Exit))]
internal static class TownServiceTutorialShopExitPatch
{
    private static void Prefix(UIShopItemWindow __instance, out TownServiceTutorialPatches.ShopExitScope __state)
        => __state = new TownServiceTutorialPatches.ShopExitScope(__instance);
    // Finalizer restores nested scopes even if the game's cleanup throws.
    private static void Finalizer(TownServiceTutorialPatches.ShopExitScope __state)
    {
        var scope = __state;
        scope.Dispose();
    }
}

[HarmonyPatch(typeof(UIShopItemWindow), "OnHidden")]
internal static class TownServiceTutorialShopHiddenPatch
{
    private static void Prefix(UIShopItemWindow __instance, out TownServiceTutorialPatches.MerchantClose? __state)
        => __state = TownServiceTutorialPatches.CaptureMerchantClose(__instance);
    private static void Postfix(TownServiceTutorialPatches.MerchantClose? __state)
        => TownServiceTutorialPatches.CompleteMerchantClose(__state);
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
