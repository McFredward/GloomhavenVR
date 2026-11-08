#if NATIVE_STORY_PROOF
// Native window, message scheduling, roster and gameplay queues are explicit ports.
// NativeStory.cs imports the original OnFinishShow -> ShowNext and balanced lock
// bodies. The shared occurrence/host receipt is qualified by the story lifecycle
// suite; NativeFinish below is that native callback boundary, not a permission grant.
using ScenarioRuleLibrary;

internal sealed partial class StoryController
{
    internal sealed class DialogInfo
    {
        internal Action? OnClosedAction;
        internal object? LevelMsg = null;
    }
    internal sealed class Window
    {
        internal bool IsVisible;
        internal void Hide() => IsVisible = false;
    }
    private bool isBlockedUpdate;
    private readonly object _stateFilter = new();
    private readonly Queue<DialogInfo> m_PendingMessages = new();
    private DialogInfo? m_CurrentMessage;
    internal readonly Window window = new();
    internal bool IsVisible => window.IsVisible;
    internal static bool DisplayDelayInEffect;
    internal int NativeCompletions;
    internal bool FailCompletion { get; set; }
    internal void OpenNative(int pending = 1)
    {
        for (int i = 0; i < pending; ++i)
            m_PendingMessages.Enqueue(new DialogInfo { OnClosedAction = () =>
            {
                if (FailCompletion) throw new InvalidOperationException("Native finish failed");
                NativeCompletions++;
            } });
        ShowImmediately(m_PendingMessages.Peek());
    }
    private void ShowImmediately(DialogInfo message)
    {
        // Presentation scheduling port: no fabricated native page/selection state.
        m_CurrentMessage = message;
        window.IsVisible = true;
        TryBlockedUpdate();
    }
    private void ShowOtherGUI(bool show) { }
    internal void NativeFinish() => OnFinishShow();
}
internal sealed class UIResultsManager { internal bool IsShown { get; set; } }
internal sealed class LevelEventsController
{
    internal static LevelEventsController s_Instance = new();
    internal void MessageWasDismissed(object message) { }
}
internal static class CoroutineHelper
{
    // The deferred callback affects only other-GUI presentation in this boundary.
    internal static void RunNextFrame(Action callback) => callback();
}
internal sealed class UINavigation
{
    internal sealed class Navigation
    {
        internal void ToNonMenuPreviousState(object filter) { }
    }
    internal readonly Navigation StateMachine = new();
}
internal enum EGameState { Scenario }
internal sealed class SaveData
{
    internal sealed class GlobalState { internal EGameState CurrentGameState = EGameState.Scenario; }
    internal static SaveData Instance = new();
    internal readonly GlobalState Global = new();
}
internal static class FFSNetwork
{
    internal static bool IsOnline = true;
    internal static bool IsClient = true;
}
internal sealed class SceneController
{
    internal static SceneController Instance = new();
    internal bool GameLoadedAndClientReady { get; set; }
}
namespace FFSNet
{
    internal enum ActionPhaseType { StartOfRound, ScenarioEnded }
    internal enum ActionProcessorStateType { ProcessFreely }
    internal enum GameActionType { GameLoadedAndClientReady }
    internal static class ActionProcessor
    {
        internal static ActionPhaseType CurrentPhase;
        internal static int Locks;
        internal static void LockProcessingAction() => Locks++;
        internal static void UnlockProcessingAction() => Locks--;
        internal static void SetState(ActionProcessorStateType state, ActionPhaseType phase) => CurrentPhase = phase;
    }
    internal sealed class NetworkControllable
    {
        internal void ApplyState() { }
    }
    internal static class ControllableRegistry
    {
        internal static readonly List<NetworkControllable> AllControllables = new();
    }
    internal static class Synchronizer
    {
        internal static int ReadyNotifications;
        internal static void SendSideAction(GameActionType type, object? data,
            bool canBeUnreliable, bool sendToHostOnly) => ReadyNotifications++;
    }
}
#endif
