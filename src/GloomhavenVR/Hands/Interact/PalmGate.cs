using System;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm rolled toward the face" detector with hysteresis (FROZEN Phase-2 API;
/// P6 additive: tunable thresholds + device-pose normal).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// DEMEO MEASURE (roll gate v3, hardware feedback round 3): the gate reads
/// <c>dot(side · handRight, worldUp)</c> — exactly Demeo's own reveal test
/// (decompiled-demeo CardHandController.cs:474-477: left hand
/// <c>Dot(leftHand.right, up) &gt; 0.6</c>, right hand <c>Dot(-rightHand.right, up)</c>).
/// This is PITCH-PROOF BY CONSTRUCTION:
///  - PITCHING the hand rotates it about its own right axis → the right vector itself
///    is unchanged → the dot is unchanged.
///  - YAWING keeps the right vector horizontal → the dot stays ~0.
///  - Only ROLLING (forearm supination/pronation) tilts the right vector toward
///    world-up/down.
/// The previous v2 measure (back-of-hand U vs a pitch-neutral Uref about the finger
/// axis F) degenerated when F approached world-up: as the fingers pitched toward
/// vertical, Uref collapsed and the projected angle swept, so a pure PITCH of the arm
/// falsely fired the gate (hardware log: "PalmGate OPEN (Left): roll 97°" while only
/// pitching). The v1 measures mixed pitch in via the head reference. Both replaced.
///
/// The signed dot maps to degrees as <c>asin(clamp(dot, -1, 1))</c>: 0° = knuckles-up
/// flat hand, 90° = palm fully rolled toward the face, negative = pronation (which
/// never opens the fan). SUPINATION IS POSITIVE FOR BOTH HANDS via the per-side sign
/// (left +right, right −right, matching Demeo): with the device grip frame's −Y out of
/// the palm and +Z along the fingers on BOTH hands, a palm-down hand has +X pointing
/// world-right for both — the LEFT thumb sits at +X and supination lifts +X toward up,
/// the RIGHT thumb sits at −X and supination lifts −X toward up.
///
/// DEGENERATE CASES: none — the measure reads one world-space dot product of an always
/// unit-length axis; there is no projection, no reference vector to collapse, and no
/// pose in which it is undefined. (Known Demeo-identical quirk: with the fingers
/// pointing straight UP, rotating the forearm spins the right axis around the vertical
/// and the dot sweeps ± — but nobody holds cards fingers-up, and Demeo behaves the
/// same by construction.)
///
/// Opens when the roll exceeds <see cref="EnterDegrees"/> (default 60° — a comfortable
/// supination, dot ≈ 0.87; Demeo's own 0.6 threshold ≈ 37°), closes below
/// <see cref="ExitDegrees"/> (default 45°); the dead band prevents flicker at the
/// boundary. Ticked every frame by <see cref="VRHand"/>; state is queryable
/// (<see cref="IsOpen"/>) and edge-observable (<see cref="Changed"/>, main thread).
///
/// Vector sources (P6, hardware test #8): with <see cref="UseDevicePalmNormal"/>
/// (default on) the hand frame is the raw DEVICE grip pose — −Y out of the physical
/// palm, +Z along the fingers, +X the grip right axis — which excludes the [Hands]
/// GripPitchOffsetDegrees visual-rig rotation; otherwise the visual rig's
/// PalmNormal/PalmCenter.forward (the simulated-hands path, which poses the rig
/// directly) with the right axis reconstructed as cross(−palmNormal, forward).
/// </summary>
internal sealed class PalmGate
{
    // Roll gate v3 defaults (Demeo-measure scale): open at 60° of supination
    // (dot ≈ 0.87 — comfortable, palm well past vertical but no full wrist crank),
    // close at 45° (15° dead band so the gate cannot chatter at the boundary). The
    // Cards driver overwrites both every frame from [Cards] RevealEnterDegrees /
    // RevealExitDegrees.
    private const float DefaultEnterDegrees = 60f;
    private const float DefaultExitDegrees = 45f;

    /// <summary>Minimum enter-exit dead band (degrees) enforced in <see cref="Tick"/> — a
    /// hand-edited config can never invert the hysteresis into an every-frame flicker.</summary>
    private const float MinHysteresisDegrees = 3f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _busySuppressed;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>Signed roll (degrees, asin of the Demeo dot) above which the gate opens. 0 = knuckles-up flat hand, 90 = palm fully toward the face.</summary>
    public float EnterDegrees = DefaultEnterDegrees;

    /// <summary>Signed roll (degrees) below which the gate closes (hysteresis; clamped below <see cref="EnterDegrees"/>).</summary>
    public float ExitDegrees = DefaultExitDegrees;

    /// <summary>
    /// Evaluate the hand frame from the DEVICE (grip-pose) rotation instead of the
    /// visual hand rig — removes the [Hands] GripPitchOffsetDegrees penalty (class doc).
    /// </summary>
    public bool UseDevicePalmNormal = true;

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

    /// <summary>True while the palm is rolled toward the headset.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Latest SIGNED roll in DEGREES (asin of the Demeo roll dot: 0 knuckles-up
    /// flat, 90 palm fully toward the face; negative = pronation) — for debug overlays and
    /// tuning. Name kept for the frozen Phase-2 surface (DevConsole prints it); the unit
    /// changed from dot to degrees with the roll-only rework.</summary>
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

        // Hand RIGHT axis — same pose sources as v2: the raw device grip pose (−Y out of
        // the physical palm, +Z along the fingers, +X the grip right axis), or the visual
        // rig for sim hands (right reconstructed from the same-chirality frame:
        // X = cross(Y, Z) with Y = back-of-hand = −palmNormal, Z = fingers).
        Vector3 right;
        if (UseDevicePalmNormal)
        {
            right = _hand.transform.rotation * Vector3.right;
        }
        else
        {
            right = Vector3.Cross(-_hand.Rig.PalmNormal, _hand.Rig.PalmCenter.forward);
            if (right.sqrMagnitude < 1e-6f)
                return; // corrupt rig frame (palm ∥ fingers) — hold state
            right.Normalize();
        }

        // Demeo's reveal measure (CardHandController.cs:474-477): signed roll dot of the
        // per-side right axis against WORLD up. Left hand +right, right hand −right, so
        // SUPINATION (palm toward face) is positive for both hands. Pitch-proof by
        // construction; no degenerate pose exists (class doc).
        float side = _hand.Side == HandSide.Right ? -1f : 1f;
        float rollDot = Vector3.Dot(side * right, Vector3.up);
        CurrentDot = Mathf.Asin(Mathf.Clamp(rollDot, -1f, 1f)) * Mathf.Rad2Deg;

        // Driver-set thresholds ([Cards] RevealEnterDegrees/RevealExitDegrees). Exit is
        // clamped below enter so a hand-edited config can never invert the hysteresis.
        float enter = EnterDegrees;
        float exit = Mathf.Min(ExitDegrees, enter - MinHysteresisDegrees);

        if (!IsOpen && CurrentDot > enter)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate OPEN ({_hand.Side}): roll {CurrentDot:F0}° (dot {rollDot:F2}; enter {enter:F0}°, exit {exit:F0}°).");
            SetOpen(true);
        }
        else if (IsOpen && CurrentDot < exit)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate CLOSE ({_hand.Side}): roll {CurrentDot:F0}° (dot {rollDot:F2}; enter {enter:F0}°, exit {exit:F0}°).");
            SetOpen(false);
        }
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
