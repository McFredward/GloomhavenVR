using System;
using System.Collections.Generic;
using GLOO.Introduction;
using HarmonyLib;
using ScenarioRuleLibrary.YML;

namespace GloomhavenVR.WorldUI;

/// <summary>Capture native provenance at opening edges, without replacing native reward flow.</summary>
[HarmonyPatch(typeof(MapChoreographer), nameof(MapChoreographer.ShowQueuedQuestRewards))]
internal static class PostQuestRewardQueuePatch
{
    private static void Prefix(out uint __state) => __state = PostQuestRewardSync.BeginQueuedRewards();
    private static Exception? Finalizer(Exception? __exception, uint __state)
    {
        PostQuestRewardSync.EndQueuedRewards(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(CampaignRewardsManager), nameof(CampaignRewardsManager.ShowRewards))]
internal static class PostQuestCampaignRewardManagerPatch
{
    private static void Prefix(CampaignRewardsManager __instance) => PostQuestRewardSync.CampaignStarting(__instance);
}

[HarmonyPatch(typeof(UICampaignRewardWindow), nameof(UICampaignRewardWindow.Show))]
internal static class PostQuestCampaignRewardShowPatch
{
    private static void Prefix(UICampaignRewardWindow __instance, List<Reward> rewards) =>
        PostQuestRewardSync.CampaignShowing(__instance, rewards);
}

[HarmonyPatch(typeof(UICampaignRewardWindow), "OnContinueButtonClick")]
internal static class PostQuestCampaignRewardContinuePatch
{
    private static bool Prefix(UICampaignRewardWindow __instance, out object? __state) =>
        PostQuestRewardSync.BeforeCampaignContinue(__instance, out __state);
    private static void Postfix(object? __state) => PostQuestRewardSync.NativeSucceeded(__state);
    private static Exception? Finalizer(Exception? __exception, object? __state)
    {
        if (__exception != null) PostQuestRewardSync.NativeFailed(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(UIGuildmasterAdventureRewardsManager), nameof(UIGuildmasterAdventureRewardsManager.ShowRewards))]
internal static class PostQuestGuildmasterRewardShowPatch
{
    private static void Prefix(UIGuildmasterAdventureRewardsManager __instance, List<Reward> rewards) =>
        PostQuestRewardSync.GuildmasterShowing(__instance, rewards);
}

[HarmonyPatch(typeof(UIGuildmasterAdventureRewardsManager), nameof(UIGuildmasterAdventureRewardsManager.Hide))]
internal static class PostQuestGuildmasterRewardContinuePatch
{
    private static void Prefix(UIGuildmasterAdventureRewardsManager __instance, out object? __state) =>
        __state = PostQuestRewardSync.BeforeGuildmasterHide(__instance);
    private static void Postfix(object? __state) => PostQuestRewardSync.NativeSucceeded(__state);
    private static Exception? Finalizer(Exception? __exception, object? __state)
    {
        if (__exception != null) PostQuestRewardSync.NativeFailed(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(UIIntroductionRewardsProcess), "Process", new[] { typeof(EIntroductionConcept) })]
internal static class PostQuestRewardIntroductionScopePatch
{
    private static void Prefix(UIIntroductionRewardsProcess __instance, out uint __state) =>
        __state = PostQuestRewardSync.BeginIntroductionScope(__instance);
    private static Exception? Finalizer(Exception? __exception, uint __state)
    {
        PostQuestRewardSync.EndIntroductionScope(__state);
        return __exception;
    }
}
[HarmonyPatch(typeof(UIIntroductionManager), "AddMessage")]
internal static class PostQuestRewardIntroductionMessagePatch
{
    private static void Prefix(UIIntroductionManager.MessageInfo __0) => PostQuestRewardSync.CaptureIntroduction(__0);
}
[HarmonyPatch(typeof(UIIntroductionManager), "ShowMessageImmediately")]
internal static class PostQuestRewardIntroductionShowPatch
{
    private static void Prefix(UIIntroductionManager.MessageInfo __0) => PostQuestRewardSync.ShowIntroduction(__0);
}

[HarmonyPatch(typeof(MapChoreographer), "OnDestroy")]
internal static class PostQuestRewardSceneEndPatch
{
    private static void Postfix() => PostQuestRewardSync.Reset();
}
