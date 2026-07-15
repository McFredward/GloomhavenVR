using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Core.Events;

// ---------------------------------------------------------------------------
// Harmony observation patches feeding VREvents. Postfix-only, read-only —
// game behavior is never altered. They are applied by VREventsModule when VR
// runs (or in dev mode) and removed collectively via Plugin.OnDestroy
// (Harmony.UnpatchSelf), satisfying the hot-reload contract.
// ---------------------------------------------------------------------------

/// <summary>
/// The single place every engine event reaches the main thread (CARDS.md §7).
///
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0) with ilspycmd 8.2 (2026-07-15):
/// <code>
///   // global namespace, GH.Runtime.dll, Choreographer.cs:3395
///   private void ProcessMessage(CMessageData message)   // IL 128,029 B — the giant switch
/// </code>
/// PATCH-TARGETS.md §1.2: ✅, private (Harmony fine), far above any inlining risk.
/// Runs inside Choreographer.Update's 8 ms/frame pump — postfix must stay cheap.
/// </summary>
[HarmonyPatch(typeof(Choreographer), "ProcessMessage")]
internal static class Choreographer_ProcessMessage_Patch
{
    private static void Postfix(CMessageData message)
    {
        if (message != null)
            VREvents.Raise(new ChoreoMessageEvent(message));
    }
}

/// <summary>
/// Reliable "interaction mode changed" hook (BOARD-INPUT §7).
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   // Choreographer.cs:2277 — IL 324 B
///   public void SetChoreographerState(ChoreographerStateType eState, int waitTickFrame, CActor waitActor)
/// </code>
/// PATCH-TARGETS.md §1.2: ✅. The nested enum Choreographer.ChoreographerStateType
/// (27 members, NA..WaitingForLoseGoalChestRewardSelection) was re-dumped from the
/// real DLL and matches the decompiled source exactly.
/// </summary>
[HarmonyPatch(typeof(Choreographer), nameof(Choreographer.SetChoreographerState))]
internal static class Choreographer_SetChoreographerState_Patch
{
    private static void Postfix(Choreographer.ChoreographerStateType eState)
    {
        VREvents.Raise(new ChoreoStateEvent(eState));
    }
}

/// <summary>
/// Observes the game's UI modality switch so VR interactors can respect modal locks
/// even where they do not go through a GraphicRaycaster (UI-ARCH §9.3).
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   // UIManager.cs:306 — IL 59 B (above Mono's ~20 B inline threshold, PATCH-TARGETS §5)
///   public void ToggleLockUI(bool active)
///   {
///       graphicRaycaster.enabled = !active;
///       for (int i = 0; i &lt; graphicRaycasters.Count; i++) graphicRaycasters[i].enabled = !active;
///   }
/// </code>
/// Note: the ref-counted RequestToggleLockUI funnels through this method, so a single
/// postfix sees every lock/unlock. The bool reflects the EFFECTIVE lock state.
/// </summary>
[HarmonyPatch(typeof(UIManager), nameof(UIManager.ToggleLockUI))]
internal static class UIManager_ToggleLockUI_Patch
{
    private static void Postfix(bool active)
    {
        VREvents.Raise(new UiLockEvent(active));
    }
}
