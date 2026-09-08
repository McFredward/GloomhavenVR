using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core.Events;

/// <summary>
/// Wires the VR event bus: applies the observation patches
/// (<see cref="Choreographer_ProcessMessage_Patch"/>,
/// <see cref="Choreographer_SetChoreographerState_Patch"/>,
/// <see cref="UIManager_ToggleLockUI_Patch"/>,
/// <see cref="UIWindow_Transition_Patch"/>), subscribes the
/// <see cref="GameEventBridge"/> and runs the <see cref="VRModeStateMachine"/>.
///
/// Active when VR runs, and also in Dev mode ([Dev] Enabled) so the whole event/mode
/// pipeline is exercisable on a flat desktop without an HMD. Patches are postfix
/// observers only — they never change game behavior.
/// </summary>
internal sealed class VREventsModule : IVRModule
{
    public string Name => "Events";

    private GameObject? _driverGo;
    private bool _active;

    public void Init()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — event bus not wired.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(Choreographer_ProcessMessage_Patch));
        VRSession.Harmony?.PatchAll(typeof(Choreographer_SetChoreographerState_Patch));
        VRSession.Harmony?.PatchAll(typeof(UIManager_ToggleLockUI_Patch));
        VRSession.Harmony?.PatchAll(typeof(UIWindow_Transition_Patch));

        GameEventBridge.Subscribe();
        VRModeStateMachine.Attach();
        SceneManager.sceneLoaded += OnSceneLoaded;

        _driverGo = new GameObject("GloomhavenVR.Events");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<VREventsDriver>();

        VRLog.Info(Name, "Event bus wired (Choreographer pump + UI events + mode state machine).");
    }

    public void Shutdown()
    {
        if (!_active)
            return;
        _active = false;

        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        VRModeStateMachine.Detach();
        GameEventBridge.Unsubscribe();
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }

    /// <summary>SceneManager.sceneLoaded → bus relay (plain C# event, no patch needed).</summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
        VREvents.Raise(new SceneLoadedEvent(scene, mode));

    /// <summary>Per-frame pump for the mode state machine's scenario-presence poll.</summary>
    private sealed class VREventsDriver : MonoBehaviour
    {
        private void Update() => VRModeStateMachine.Tick();
    }
}
