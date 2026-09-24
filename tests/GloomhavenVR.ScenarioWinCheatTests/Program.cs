using System;
using GloomhavenVR.WorldUI;
using ScenarioRuleLibrary;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        ++_assertions;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static ScenarioState Reset()
    {
        Calls.Events.Clear();
        FFSNetwork.IsOnline = false;
        Choreographer.s_Choreographer = new Choreographer();
        SceneController.Instance = new SceneController();
        ScenarioManager.Scenario = new CScenario();
        ScenarioManager.CurrentScenarioState = new ScenarioState();
        ScenarioRuleClient.ScenarioRuleClientStopped = false;
        GameState.WaitingForPlayerToSelectDamageResponse = false;
        GameState.WaitingForPlayerActorToAvoidDamageResponse = false;
        Singleton<StoryController>.Instance = new StoryController();
        Singleton<ESCMenu>.Instance = new ESCMenu();
        return ScenarioManager.CurrentScenarioState;
    }

    private static void Main()
    {
        var refusals = new (string Reason, Action Block)[]
        {
            ("cheat_win_online", () => FFSNetwork.IsOnline = true),
            ("cheat_win_no_scenario", () => ScenarioManager.CurrentScenarioState = null),
            ("cheat_win_no_scenario", () => Choreographer.s_Choreographer = null),
            ("cheat_win_no_scenario", () => Choreographer.s_Choreographer!.isActiveAndEnabled = false),
            ("cheat_win_no_scenario", () => ScenarioManager.Scenario = null),
            ("cheat_win_no_scenario", () => SceneController.Instance = null),
            ("cheat_win_ending", () => Choreographer.s_Choreographer!.IsRestarting = true),
            ("cheat_win_ending", () => ScenarioRuleClient.ScenarioRuleClientStopped = true),
            ("cheat_win_ending", () => ScenarioManager.Scenario!.CurrentScenarioResult = SEventActorFinishedScenario.EScenarioResult.Win),
            ("cheat_win_ending", () => ScenarioManager.Scenario!.CurrentScenarioResult = SEventActorFinishedScenario.EScenarioResult.Lose),
            ("cheat_win_busy", () => SceneController.Instance!.IsLoading = true),
            ("cheat_win_busy", () => SceneController.Instance!.ScenarioIsLoading = true),
            ("cheat_win_busy", () => GameState.WaitingForPlayerToSelectDamageResponse = true),
            ("cheat_win_busy", () => GameState.WaitingForPlayerActorToAvoidDamageResponse = true),
            ("cheat_win_busy", () => Singleton<StoryController>.Instance.IsVisible = true),
        };
        foreach (var refusal in refusals)
        {
            ScenarioState state = Reset();
            refusal.Block();
            Check(ScenarioWinCheat.UnavailableReason() == refusal.Reason,
                "refusal reason: " + refusal.Reason);
            Check(!ScenarioWinCheat.TryWin(state, Close), "unavailable native win refused");
            Check(Calls.Events.Count == 0, "refusal never closes menus or starts native victory");
        }

        ScenarioState stale = Reset();
        ScenarioManager.CurrentScenarioState = new ScenarioState();
        Check(!ScenarioWinCheat.TryWin(stale, Close), "stale confirmation refused");
        Check(Calls.Events.Count == 0, "stale confirmation has no side effects");

        foreach (Action change in new Action[]
        {
            () => FFSNetwork.IsOnline = true,
            () => ScenarioManager.CurrentScenarioState = new ScenarioState(),
            () => SceneController.Instance!.IsLoading = true,
            () => Choreographer.s_Choreographer!.IsRestarting = true,
        })
        {
            ScenarioState state = Reset();
            Check(!ScenarioWinCheat.TryWin(state, () => { Close(); change(); }),
                "closing callbacks cannot bypass revalidation");
            Check(!Calls.Events.Contains("win"), "changed session or scenario never wins");
        }

        ScenarioState ready = Reset();
        Check(ScenarioWinCheat.UnavailableReason() == "", "ready offline scenario available");
        Check(ScenarioWinCheat.TryWin(ready, Close), "ready native victory accepted");
        Check(string.Join(",", Calls.Events) == "options-close,float-release,esc-hide,win",
            "options close and floated native ESC release precede victory exactly once");
        Calls.Events.Clear();
        Check(ScenarioWinCheat.UnavailableReason() == "cheat_win_ending", "requested scenario remains unavailable");
        Check(!ScenarioWinCheat.TryWin(ready, Close), "duplicate request refused before native coroutine catches up");
        Check(Calls.Events.Count == 0, "duplicate request never closes menus or starts another victory");

        ready = Reset();
        Singleton<ESCMenu>.Instance.Window.IsOpen = false;
        Check(ScenarioWinCheat.TryWin(ready, Close), "hidden native ESC still permits victory");
        Check(string.Join(",", Calls.Events) == "options-close,float-release,win",
            "already hidden ESC still releases its sticky float before victory");

        ready = Reset();
        Singleton<StoryController>.Instance = null!;
        Singleton<ESCMenu>.Instance = null!;
        Check(ScenarioWinCheat.TryWin(ready, Close), "new scenario needs no story or ESC singleton");
        Check(string.Join(",", Calls.Events) == "options-close,win", "new scenario wins once after old request");
        Console.WriteLine($"Scenario win cheat: {_assertions} assertions passed (production helper).");
    }

    private static void Close() => Calls.Events.Add("options-close");
}
