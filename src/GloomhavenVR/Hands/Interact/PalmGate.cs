using System;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm turned toward the face" detector with hysteresis (FROZEN Phase-2 API;
/// P6 additive: tunable thresholds + device-pose normal).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// Opens when dot(palmNormal, toHMD) &gt; <see cref="EnterThreshold"/>, closes when it
/// drops below <see cref="ExitThreshold"/> — the dead band prevents flicker at the
/// boundary. Ticked every frame by <see cref="VRHand"/>; state is queryable
/// (<see cref="IsOpen"/>) and edge-observable (<see cref="Changed"/>, main thread).
///
/// P6 (hardware test #8): the gate used the VISUAL rig's palm normal, which includes
/// the [Hands] GripPitchOffsetDegrees rotation (default -60°) between the tracked
/// grip pose and the hand model — so opening the fan demanded ~60° of wrist
/// supination BEYOND "palm faces me". <see cref="UseDevicePalmNormal"/> (default on)
/// evaluates the raw device pose instead (-Y of the grip pose = out of the physical
/// palm), matching what the wrist actually does. Thresholds are set per frame by the
/// Cards driver from [Cards] SupinationThreshold.
///
/// P7 (hardware test #10): even the raw-pose cone test mixed PITCH into the measure —
/// dot(palmNormal, toHead) also grows when the wrist pitches toward the face, so the
/// gesture only felt reliable when players rolled AND pitched. With
/// <see cref="RollAxisOnly"/> the gate instead measures pure SUPINATION: the roll
/// angle of the palm normal around the forearm/controller axis. Both reference
/// directions (world up and the horizontal to-head direction) are projected into the
/// plane perpendicular to the roll axis, so pitching or yawing the arm cannot move
/// the measure — only turning the palm up / toward you does.
/// </summary>
internal sealed class PalmGate
{
    private const float DefaultEnterDot = 0.6f;
    private const float DefaultExitDot = 0.35f;

    // G5 (DEMEO-HANDS-CARDS §4 row G5, §5 Group C): Demeo reveals the hand at a tight
    // ~53° cone — Dot(hand.right, avatarUp) > 0.6, closing at the same 0.6 with no
    // hysteresis gap (CardHandController.cs:452-456,474). We keep a hair of dead band
    // (0.6 enter / 0.5 exit) so the roll-axis measure doesn't chatter at the boundary,
    // vs our deliberately generous P6 default (SupinationThreshold 0.2 / exit -0.15).
    private const float DemeoEnterDot = 0.6f;
    private const float DemeoExitDot = 0.5f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _demeoPreset;
    private bool _busySuppressed;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>Dot(palmNormal, toHMD) above which the gate opens. Default = P2 constant 0.6.</summary>
    public float EnterThreshold = DefaultEnterDot;

    /// <summary>Dot(palmNormal, toHMD) below which the gate closes. Default = P2 constant 0.35.</summary>
    public float ExitThreshold = DefaultExitDot;

    /// <summary>
    /// Evaluate the palm normal in the DEVICE (grip-pose) frame instead of the visual
    /// hand rig — removes the [Hands] GripPitchOffsetDegrees penalty (see class doc).
    /// </summary>
    public bool UseDevicePalmNormal = true;

    /// <summary>
    /// P7 (hardware test #10): measure pure supination (roll around the forearm/
    /// controller axis) instead of the full 3D palm-to-head cone — pitch and yaw of
    /// the arm no longer contribute, only turning the palm up/toward you does.
    /// <see cref="CurrentDot"/> then reads: -1 palm fully down, 0 palm vertical
    /// (thumb up), +1 palm fully up / toward the face.
    /// </summary>
    public bool RollAxisOnly;

    /// <summary>
    /// G5 busy-hand gate (DEMEO-HANDS-CARDS §5 Group C): when true and the gate hand is
    /// mid-grab, the gate is force-closed and its open transition suppressed — you cannot
    /// reveal the fan with the same hand you are grabbing a card with (mirrors
    /// CardHandController.cs:475-476). The grab state is read from this gate's own hand
    /// (<c>_hand.Grabber.Held</c>), so the driver only forwards the on/off toggle
    /// ([Cards] RevealIgnoreWhenGrabbing). Auto-property: no external assignment required
    /// for a clean build before the driver wires it.
    /// </summary>
    public bool IgnoreWhenHandBusy { get; set; }

    /// <summary>
    /// G5 reveal preset (DEMEO-HANDS-CARDS §4 row G5): forward [Cards] RevealPreset via
    /// <c>CardsConfig.RevealDemeo</c>. <c>false</c> = generous P6 default (uses the
    /// driver-set <see cref="EnterThreshold"/>/<see cref="ExitThreshold"/>, byte-identical
    /// to today); <c>true</c> = Demeo's tight cone (<see cref="DemeoEnterDot"/>/
    /// <see cref="DemeoExitDot"/>), ignoring the driver-set thresholds.
    /// </summary>
    public void ApplyDemeoPreset(bool demeo)
    {
        if (_demeoPreset == demeo)
            return;
        _demeoPreset = demeo;
        Core.VRLog.Debug("Interact", $"PalmGate reveal preset → {(demeo ? "demeo" : "generous")}.");
    }

    /// <summary>True while the palm faces the headset.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Latest dot(palmNormal, toHMD) — for debug overlays and tuning.</summary>
    public float CurrentDot { get; private set; }

    /// <summary>Fired on open/close transitions.</summary>
    public event Action<VRHand, bool>? Changed;

    /// <summary>Enable/disable (mode policy). Disabling force-closes the gate.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
                SetOpen(false);
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
            return;

        // G5 busy-hand gate (mirrors CardHandController.cs:475-476): the hand currently
        // grabbing a card must not also reveal the fan. Force-close and skip evaluation
        // while busy; normal behavior resumes the moment the grab ends (next tick).
        if (IgnoreWhenHandBusy && _hand.Grabber.Held != null)
        {
            if (!_busySuppressed)
            {
                _busySuppressed = true;
                Core.VRLog.Debug("Interact", "PalmGate reveal suppressed — gate hand busy grabbing.");
            }
            SetOpen(false);
            return;
        }
        if (_busySuppressed)
        {
            _busySuppressed = false;
            Core.VRLog.Debug("Interact", "PalmGate reveal restored — gate hand free.");
        }

        // Head position: the rig's head camera in VR; Camera.main in the desktop sim.
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;

        Vector3 palm = _hand.Rig.PalmCenter.position;
        Vector3 toHead = head.transform.position - palm;
        if (toHead.sqrMagnitude < 1e-8f)
            return;

        // Palm normal: -Y of the grip pose (physical palm; simulated hands have no
        // grip-pitch offset applied to the device transform either) or the visual rig.
        Vector3 normal = UseDevicePalmNormal
            ? _hand.transform.rotation * Vector3.down
            : _hand.Rig.PalmNormal;

        if (RollAxisOnly)
        {
            // Roll axis ≈ forearm/controller axis: +Z of the same frame the normal
            // came from (the normal is perpendicular to it by construction).
            Vector3 axis = UseDevicePalmNormal
                ? _hand.transform.rotation * Vector3.forward
                : _hand.Rig.PalmCenter.forward;

            // Project the references into the roll plane so pitch/yaw drop out.
            Vector3 upRef = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            Vector3 headRef = toHead.normalized;
            headRef -= axis * Vector3.Dot(headRef, axis);

            float upSq = upRef.sqrMagnitude;
            float headSq = headRef.sqrMagnitude;
            if (upSq < 1e-4f && headSq < 1e-4f)
                return; // arm points straight up/down at the head — roll undefined, hold state

            // Supination = palm normal rolled toward "up" OR toward the face — take
            // the friendlier of the two so both palm-up and palm-at-face count.
            float dot = float.NegativeInfinity;
            if (upSq >= 1e-4f)
                dot = Vector3.Dot(normal, upRef / Mathf.Sqrt(upSq));
            if (headSq >= 1e-4f)
                dot = Mathf.Max(dot, Vector3.Dot(normal, headRef / Mathf.Sqrt(headSq)));
            CurrentDot = dot;
        }
        else
        {
            CurrentDot = Vector3.Dot(normal, toHead.normalized);
        }

        // Preset selects the cone: generous uses the driver-set thresholds (byte-identical
        // to the P6 default); demeo uses the tight constants and ignores those thresholds.
        float enter = _demeoPreset ? DemeoEnterDot : EnterThreshold;
        float exit = _demeoPreset ? DemeoExitDot : ExitThreshold;

        if (!IsOpen && CurrentDot > enter)
            SetOpen(true);
        else if (IsOpen && CurrentDot < exit)
            SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open)
            return;
        IsOpen = open;
        try
        {
            Changed?.Invoke(_hand, open);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Interact", $"PalmGate.Changed subscriber threw: {ex}");
        }
    }
}
