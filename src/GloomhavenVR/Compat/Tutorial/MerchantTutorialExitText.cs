using System;
using System.Runtime.CompilerServices;
using Assets.Script.Misc;
using GLOO.Introduction;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>Explain the actual VR merchant exit without changing the native first-save lesson.
/// The original BuyItem config (sharedassets1.assets/2109) contains one HelpText step,
/// FTUE_9.3 / Consoles/FTUE_9.3_CONTROLLER. Its flat wording asks for WorldMap, whereas
/// the converted shop closes through its X or the merchant toggle. Tag only messages
/// constructed from that exact serialized step, and replace their visible text after
/// native painting. Configs, keys, queues, conditions and continuation promises stay native.
/// Language/controller repaint revalidates the current producer and serialized row before
/// resolving the replacement anew. Both converted 2D and nonimmersive 3D shops use this
/// exit; a retained immersive setting has no effect while the 3D room is inactive.
/// The immersive resident's flat-step skip is a separate native progression adapter.</summary>
internal static class MerchantTutorialExitText
{
    private sealed class Marker
    {
        internal readonly UIMapFTUEStep Producer;
        internal readonly IntroductionStepUI Source;
        internal Marker(UIMapFTUEStep producer, IntroductionStepUI source) { Producer = producer; Source = source; }
    }
    private static readonly ConditionalWeakTable<CLevelMessage, Marker> Messages = new();
    private static readonly ConditionalWeakTable<CLevelMessagePage, Marker> Pages = new();

    private static bool Active => WorldUIConfig.ConversionActive
        && (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value)
        && AdventureState.MapState?.IsCampaign == true && MapFTUEManager.IsPlaying
        && Singleton<MapFTUEManager>.Instance?.CurrentStep == EMapFTUEStep.BuyItem;

    internal static void Record(IntroductionStepUI step, CLevelMessage message)
    {
        if (!Active) return;
        UIMapFTUEStep? buy = Singleton<UIShopItemWindow>.Instance?.ftueStep;
        if (!IsOriginal(step, buy)) return;
        var marker = new Marker(buy!, step);
        Messages.Remove(message); Messages.Add(message, marker);
        foreach (CLevelMessagePage page in message.Pages)
        {
            Pages.Remove(page); Pages.Add(page, marker);
        }
    }

    private static bool IsOriginal(IntroductionStepUI step, UIMapFTUEStep? buy)
    {
        if (buy?.config == null || buy.Step != EMapFTUEStep.BuyItem
            || !ReferenceEquals(Singleton<MapFTUEManager>.Instance?.currentStep, buy)
            || step.LayoutType != CLevelMessage.ELevelMessageLayoutType.HelpText
            || !string.Equals(step.LocalizationTextKey, "FTUE_9.3", StringComparison.Ordinal)) return false;
        foreach (IntroductionStepUI candidate in buy.config.GetSteps())
            if (ReferenceEquals(candidate, step)) return true;
        return false;
    }

    private static bool Current(Marker marker) =>
        ReferenceEquals(Singleton<UIShopItemWindow>.Instance?.ftueStep, marker.Producer)
        && IsOriginal(marker.Source, marker.Producer);

    internal static void ApplyTitle(LevelMessageUILayout ui, CLevelMessage? message)
    {
        if (Active && message != null && Messages.TryGetValue(message, out Marker? marker) && Current(marker)
            && ui.title != null && ui.title.gameObject.activeSelf)
            ui.title.text = Loc.MerchantTutorialExit;
    }

    internal static void ApplyBody(LevelMessagePageUI ui)
    {
        if (Active && ui.page != null && Pages.TryGetValue(ui.page, out Marker? marker) && Current(marker)
            && ui.information != null)
            ui.information.text = Loc.MerchantTutorialExit;
    }
}

[HarmonyPatch(typeof(IntroductionStepUI), nameof(IntroductionStepUI.ToMessage))]
internal static class MerchantTutorialExitMessagePatch
{
    private static void Postfix(IntroductionStepUI __instance, CLevelMessage __result)
        => MerchantTutorialExitText.Record(__instance, __result);
}

[HarmonyPatch(typeof(LevelMessageUILayout))]
internal static class MerchantTutorialExitTitlePatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch("Init")]
    private static void InitPostfix(LevelMessageUILayout __instance, CLevelMessage message)
        => MerchantTutorialExitText.ApplyTitle(__instance, message);

    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch("OnLanguageChanged")]
    private static void LanguagePostfix(LevelMessageUILayout __instance)
        => MerchantTutorialExitText.ApplyTitle(__instance, __instance._message);
}

[HarmonyPatch(typeof(LevelMessagePageUI), "OnLanguageChanged")]
internal static class MerchantTutorialExitPagePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(LevelMessagePageUI __instance) => MerchantTutorialExitText.ApplyBody(__instance);
}
