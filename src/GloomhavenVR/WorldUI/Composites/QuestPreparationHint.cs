using System;
using GLOO.Introduction;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Suppresses the exact quest-selection preparation introduction in VR. Build 507 shows its
/// "review cards and inventory" instruction over the first story while the party UI is hidden.
/// The user explicitly permits omitting this hint (2026-09-15); the later battle-goal picker
/// already presents a separate native introduction, so moving this one there would compete
/// with that queue. Do not suppress by translated text, object name or the general Quest enum.
///
/// QuestManager.ShowIntroduction passes no continuation and marks its own introduction done
/// after Show returns. Still honor any supplied callback exactly once, just as the native
/// empty-step path does. No pending message, process promise or UI lock is created, removed
/// or completed here; the original tutorial and quest-selection callbacks continue normally.
/// </summary>
[HarmonyPatch(typeof(UIIntroduceBase), "Show", new[] { typeof(IntroductionConfigUI), typeof(Action) })]
internal static class QuestPreparationHint
{
    private static bool Prefix(UIIntroduceBase __instance, Action? __1)
    {
        if (!VRSession.IsRunning || !Singleton<QuestManager>.IsInitialized)
            return true;
        QuestManager manager = Singleton<QuestManager>.Instance;
        if (manager == null || !ReferenceEquals(manager.questIntroduction, __instance))
            return true;

        VRLog.Note("WorldUI", "QUEST PREPARATION HINT OMITTED: the native quest-selection producer "
            + "describes unavailable controls during VR story playback. User-approved omission; "
            + "the later battle-goal introduction and native quest progression remain intact.");
        __1?.Invoke();
        return false;
    }
}
