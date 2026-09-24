using System.Collections.Generic;
using UnityEngine.UI;

// Only the invocation boundary is simulated. The production helper is compiled unchanged;
// native reward calculation, animation and headset presentation are outside this fixture.
internal static class Calls
{
    internal static readonly List<string> Events = new();
}
internal static class FFSNetwork { internal static bool IsOnline; }
internal sealed class Choreographer
{
    internal static Choreographer? s_Choreographer;
    internal bool isActiveAndEnabled = true;
    internal bool IsRestarting;
}
internal sealed class SceneController
{
    internal static SceneController? Instance;
    internal bool IsLoading;
    internal bool ScenarioIsLoading;
}
internal static class Singleton<T> where T : class
{
    internal static T Instance = null!;
    internal static bool IsInitialized => Instance != null;
}
internal sealed class StoryController { internal bool IsVisible; }
internal sealed class ESCMenu
{
    internal readonly UIWindow Window = new();
    internal bool IsOpen => Window.IsOpen;
    internal void Hide() => Window.Hide();
    internal T GetComponent<T>() where T : class => (Window as T)!;
}
internal static class DebugMenu
{
    internal static void WinNoToggle() => Calls.Events.Add("win");
}
namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        internal bool IsOpen = true;
        internal void Hide() { Calls.Events.Add("esc-hide"); IsOpen = false; }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ModalFallback
    {
        internal static void CloseFloatedWindow(UIWindow window)
        {
            Calls.Events.Add("float-release");
            if (window.IsOpen) window.Hide();
        }
    }
}
namespace ScenarioRuleLibrary
{
    internal sealed class ScenarioState { }
    internal sealed class CScenario
    {
        internal SEventActorFinishedScenario.EScenarioResult CurrentScenarioResult;
    }
    internal static class SEventActorFinishedScenario
    {
        internal enum EScenarioResult { None, Win, Lose }
    }
    internal static class ScenarioManager
    {
        internal static ScenarioState? CurrentScenarioState;
        internal static CScenario? Scenario;
    }
    internal static class ScenarioRuleClient { internal static bool ScenarioRuleClientStopped; }
    internal static class GameState
    {
        internal static bool WaitingForPlayerToSelectDamageResponse;
        internal static bool WaitingForPlayerActorToAvoidDamageResponse;
    }
}
