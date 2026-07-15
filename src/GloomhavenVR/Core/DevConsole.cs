using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GloomhavenVR.Core;

/// <summary>
/// Dev harness module ([Dev] Enabled): installs the <see cref="DevConsole"/> overlay
/// and desktop test hooks so Phase-2+ logic is exercisable without an HMD.
/// </summary>
internal sealed class DevModule : IVRModule
{
    public string Name => "Dev";

    private GameObject? _consoleGo;

    public void Init()
    {
        if (!Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "Dev mode off — console not installed.");
            return;
        }

        _consoleGo = new GameObject("GloomhavenVR.DevConsole");
        Object.DontDestroyOnLoad(_consoleGo);
        _consoleGo.hideFlags = HideFlags.HideAndDontSave;
        _consoleGo.AddComponent<DevConsole>();

        VRLog.Info(Name, "Dev console installed (F8 sim hands, F9 poke Ready, F10 overlay).");
    }

    public void Shutdown()
    {
        if (_consoleGo != null)
        {
            Object.Destroy(_consoleGo);
            _consoleGo = null;
        }
    }
}

/// <summary>
/// Debug overlay + desktop test drivers (dev builds only, [Dev] Enabled):
/// - OnGUI overlay (F10 / [Dev] Overlay): VR session, current <see cref="VRMode"/>,
///   per-hand tracking/pose/curl/palm-gate state, recent bus events.
/// - Mode-transition logging (always while dev console runs).
/// - Periodic <c>UnityEngine.XR.InputDevices</c> dump ([Dev] InputDeviceDumpInterval).
/// - F8: toggle [Dev] SimulateHands — <see cref="HandsDriver"/> animates fake hands
///   on the desktop (hold T = trigger, G = grip).
/// - F9: click the game's Ready button through the SAME synthesized-pointer path the
///   fingertip poke uses (real GraphicRaycaster raycast + ExecuteEvents) — proves the
///   poke/modality pipeline against live game UI without VR.
///
/// OnGUI allocates strings per frame — that is fine here; this component exists only
/// when dev mode is on and the overlay is visible.
/// </summary>
internal sealed class DevConsole : MonoBehaviour
{
    private const int MaxEventLines = 8;

    private readonly Queue<string> _recentEvents = new(MaxEventLines + 1);
    private readonly List<UnityEngine.XR.InputDevice> _deviceList = new(8);
    private readonly StringBuilder _sb = new(1024);

    private UguiPointer? _readyPointer;
    private bool _overlayVisible;
    private float _nextDeviceDump;

    private void OnEnable()
    {
        _overlayVisible = Plugin.DevOverlay.Value;
        VRModeStateMachine.ModeChanged += OnModeChanged;
        VREvents.ChoreographerMessage += OnChoreoMessage;
        VREvents.ChoreographerStateChanged += OnChoreoState;
        VREvents.NavigationStateChanged += OnNavState;
        VREvents.UiLockChanged += OnUiLock;
    }

    private void OnDisable()
    {
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        VREvents.ChoreographerMessage -= OnChoreoMessage;
        VREvents.ChoreographerStateChanged -= OnChoreoState;
        VREvents.NavigationStateChanged -= OnNavState;
        VREvents.UiLockChanged -= OnUiLock;
    }

    private void Update()
    {
        Keyboard? kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.f8Key.wasPressedThisFrame)
            {
                Plugin.SimulateHands.Value = !Plugin.SimulateHands.Value;
                VRLog.Info("Dev", $"SimulateHands = {Plugin.SimulateHands.Value}");
            }
            if (kb.f9Key.wasPressedThisFrame)
                PokeReadyButton();
            if (kb.f10Key.wasPressedThisFrame)
                _overlayVisible = !_overlayVisible;
        }

        float interval = Plugin.InputDeviceDumpInterval.Value;
        if (interval > 0f && Time.unscaledTime >= _nextDeviceDump)
        {
            _nextDeviceDump = Time.unscaledTime + interval;
            DumpInputDevices();
        }
    }

    // ---- event log --------------------------------------------------------------------

    private void OnModeChanged(VRModeChange change)
    {
        VRLog.Info("Dev", $"VRMode {change.From} -> {change.To}");
        PushEvent($"Mode {change.From}->{change.To}");
    }

    private void OnChoreoMessage(ChoreoMessageEvent e) => PushEvent($"Msg {e.Type}");
    private void OnChoreoState(ChoreoStateEvent e) => PushEvent($"ChoreoState {e.State}");
    private void OnNavState(NavStateEvent e) => PushEvent($"NavState {e.State?.GetType().Name}");
    private void OnUiLock(UiLockEvent e) => PushEvent($"UiLock {(e.Locked ? "LOCKED" : "unlocked")}");

    private void PushEvent(string line)
    {
        _recentEvents.Enqueue($"[{Time.frameCount}] {line}");
        while (_recentEvents.Count > MaxEventLines)
            _recentEvents.Dequeue();
    }

    // ---- desktop poke test ---------------------------------------------------------------

    /// <summary>
    /// Drives the poke/ExecuteEvents pipeline against the live Ready button:
    /// screen point → the button canvas's own GraphicRaycaster (respects
    /// UIManager.ToggleLockUI) → pointerDown/Up/Click. Logs each step.
    /// </summary>
    private void PokeReadyButton()
    {
        Choreographer? choreographer = Choreographer.s_Choreographer;
        ReadyButton? ready = choreographer != null ? choreographer.readyButton : null;
        if (ready == null)
        {
            VRLog.Warn("Dev", "PokeReady: no Choreographer/ReadyButton (not in a scenario?).");
            return;
        }

        Canvas? canvas = ready.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            VRLog.Warn("Dev", "PokeReady: ReadyButton has no parent Canvas.");
            return;
        }

        Camera? uiCamera = canvas.worldCamera != null
            ? canvas.worldCamera
            : UIManager.Instance != null ? UIManager.Instance.UICamera : null;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCamera, ready.transform.position);

        _readyPointer ??= new UguiPointer(HandSide.Right);
        if (!_readyPointer.TryRaycast(canvas.rootCanvas, screenPos, out RaycastResult top))
        {
            VRLog.Warn("Dev", $"PokeReady: raycast at {screenPos} produced no hit " +
                              "(button hidden, or UI locked via ToggleLockUI — modality respected).");
            return;
        }

        VRLog.Info("Dev", $"PokeReady: hit '{top.gameObject.name}' at {screenPos} — synthesizing click.");
        _readyPointer.SetHovered(top.gameObject);
        _readyPointer.Press(screenPos);
        _readyPointer.Release(screenPos);
        _readyPointer.SetHovered(null);
    }

    private void DumpInputDevices()
    {
        _deviceList.Clear();
        UnityEngine.XR.InputDevices.GetDevices(_deviceList);
        if (_deviceList.Count == 0)
        {
            VRLog.Info("Dev", "XR InputDevices: none.");
            return;
        }
        foreach (UnityEngine.XR.InputDevice device in _deviceList)
            VRLog.Info("Dev", $"XR device: '{device.name}' chars={device.characteristics}");
    }

    // ---- overlay -----------------------------------------------------------------------------

    private void OnGUI()
    {
        if (!_overlayVisible)
            return;

        _sb.Length = 0;
        _sb.Append("GloomhavenVR DEV  |  VR ")
           .Append(VRSession.IsRunning ? $"RUNNING ({VRSession.RuntimeName})" : "off")
           .Append("  |  Mode ").Append(VRModeStateMachine.CurrentMode)
           .Append("\nF8 sim hands (")
           .Append(Plugin.SimulateHands.Value ? "ON" : "off")
           .Append(", hold T=trigger G=grip)   F9 poke Ready   F10 overlay\n");

        AppendHand(VRHands.Left);
        AppendHand(VRHands.Right);

        _sb.Append("Pokeables ").Append(VRInteractables.Pokeables.Count)
           .Append("  Grabbables ").Append(VRInteractables.Grabbables.Count)
           .Append("  PokeCanvases ").Append(UguiPokeSurfaces.Surfaces.Count)
           .Append('\n');

        foreach (string line in _recentEvents)
            _sb.Append(line).Append('\n');

        GUI.Label(new Rect(10, 10, 900, 400), _sb.ToString());
    }

    private void AppendHand(VRHand? hand)
    {
        if (hand == null)
        {
            _sb.Append("hand: —\n");
            return;
        }
        _sb.Append(hand.Side)
           .Append(hand.IsSimulated ? " [SIM]" : string.Empty)
           .Append(hand.IsTracked ? " tracked" : " NOT TRACKED")
           .Append("  pose ").Append(hand.Pose)
           .Append("  trig ").Append(hand.TriggerValue.ToString("F2"))
           .Append("  grip ").Append(hand.GripValue.ToString("F2"))
           .Append("  palmGate ").Append(hand.PalmGate.IsOpen ? "OPEN" : "closed")
           .Append('(').Append(hand.PalmGate.CurrentDot.ToString("F2")).Append(')')
           .Append("  rayHit ").Append(hand.Ray.Current.HasHit)
           .Append('\n');
    }
}
