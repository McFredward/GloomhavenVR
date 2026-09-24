using System;
using ScenarioRuleLibrary;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Explicit test action through the game's victory flow, never a synthetic reward screen.</summary>
internal static class ScenarioWinCheat
{
    private static ScenarioState? _requested;

    internal static string UnavailableReason()
    {
        if (FFSNetwork.IsOnline) return "cheat_win_online";
        var state = ScenarioManager.CurrentScenarioState;
        var choreographer = Choreographer.s_Choreographer;
        if (state == null || choreographer == null || !choreographer.isActiveAndEnabled
            || ScenarioManager.Scenario == null || SceneController.Instance == null)
            return "cheat_win_no_scenario";
        if (ReferenceEquals(_requested, state) || choreographer.IsRestarting
            || ScenarioRuleClient.ScenarioRuleClientStopped
            || ScenarioManager.Scenario.CurrentScenarioResult != SEventActorFinishedScenario.EScenarioResult.None)
            return "cheat_win_ending";
        if (SceneController.Instance.IsLoading || SceneController.Instance.ScenarioIsLoading
            || GameState.WaitingForPlayerToSelectDamageResponse
            || GameState.WaitingForPlayerActorToAvoidDamageResponse
            || (Singleton<StoryController>.IsInitialized && Singleton<StoryController>.Instance.IsVisible))
            return "cheat_win_busy";
        return "";
    }

    internal static bool TryWin(ScenarioState expected, Action closeOptions)
    {
        // Revalidate on the confirming press: the menu may outlive a scene or session change.
        if (!ReferenceEquals(expected, ScenarioManager.CurrentScenarioState)
            || UnavailableReason().Length != 0) return false;
        closeOptions();
        // Native Hide alone leaves a sticky VR menu floated. Use its normal close
        // route even if the native window is already hidden but its host remains.
        if (Singleton<ESCMenu>.IsInitialized)
            ModalFallback.CloseFloatedWindow(Singleton<ESCMenu>.Instance.GetComponent<UIWindow>());
        if (!ReferenceEquals(expected, ScenarioManager.CurrentScenarioState)
            || UnavailableReason().Length != 0) return false;
        // Do not submit twice while the native coroutine is waiting for its safe end.
        _requested = expected;
        DebugMenu.WinNoToggle();
        return true;
    }
}
