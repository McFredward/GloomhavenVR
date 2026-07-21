using System;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm rolled toward the face" detector with hysteresis (FROZEN Phase-2 API;
/// P6 additive: tunable thresholds + device-pose normal).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// ROLL-ONLY measure (hardware feedback, reveal-angle round 2): the gate reads pure
/// FOREARM/HAND ROLL — the hand's rotation around its own forward (finger) axis —
/// so PITCH and YAW of the arm are irrelevant by construction. Earlier measures
/// (dot(palmNormal, toHead); then a roll-plane dot against world-up OR the head
/// direction) still mixed pitch in via the head reference and effectively demanded
/// close to a full 180° supination, which the user rejected as uncomfortable.
///
/// Math: let F = the hand's forward (finger) axis and U = the back-of-hand "up"
/// (−palm normal). The PITCH-NEUTRAL up is Uref = normalize(worldUp − F·dot(worldUp, F))
/// (≡ normalize(cross(F, cross(worldUp, F)))) — exactly where U would point at zero
/// roll for the CURRENT pitch/yaw. The roll angle is the angle between U (projected
/// into the plane ⊥ F) and Uref: 0° = palm flat down, 90° = palm vertical (thumb up),
/// 180° = palm fully up/toward the face. Pitch-invariant by construction. The angle
/// is SIGNED by roll direction (supination positive, pronation negative, mirrored
/// per hand) so twisting the palm DOWN-and-out can never open the fan.
///
/// Opens when the roll exceeds <see cref="EnterDegrees"/> (default 95° — a
/// comfortable supination just past vertical), closes below <see cref="ExitDegrees"/>
/// (default 80°); the dead band prevents flicker at the boundary. Ticked every frame
/// by <see cref="VRHand"/>; state is queryable (<see cref="IsOpen"/>) and
/// edge-observable (<see cref="Changed"/>, main thread).
///
/// Vector sources (P6, hardware test #8): with <see cref="UseDevicePalmNormal"/>
/// (default on) the hand frame is the raw DEVICE grip pose — −Y out of the physical
/// palm, +Z along the fingers — which excludes the [Hands] GripPitchOffsetDegrees
/// visual-rig rotation; otherwise the visual rig's PalmNormal/PalmCenter.forward
/// (the simulated-hands path, which poses the rig directly).
/// </summary>
internal sealed class PalmGate
{
    // Reveal-angle round 2 defaults: open at 95° of pure supination (just past palm-
    // vertical — comfortable, no full wrist crank), close at 80° (15° dead band so the
    // measure cannot chatter at the boundary). The Cards driver overwrites both every
    // frame from [Cards] RevealEnterDegrees / RevealExitDegrees.
    private const float DefaultEnterDegrees = 95f;
    private const float DefaultExitDegrees = 80f;

    /// <summary>Minimum enter-exit dead band (degrees) enforced in <see cref="Tick"/> — a
    /// hand-edited config can never invert the hysteresis into an every-frame flicker.</summary>
    private const float MinHysteresisDegrees = 3f;

    /// <summary>Above this roll the direction SIGN is ignored: near 180° the roll-plane
    /// cross product degenerates (U ≈ −Uref) and the sign gets noisy, while pronating
    /// this far past palm-down is anatomically impossible anyway — a hand up here is
    /// always supinated.</summary>
    private const float SignSaturationDegrees = 150f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _busySuppressed;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>Signed roll (degrees) above which the gate opens. 0 = palm down, 90 = palm vertical (thumb up), 180 = palm fully up/toward the face.</summary>
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

    /// <summary>Latest SIGNED roll in DEGREES (0 palm down, 90 thumb up, 180 palm fully up;
    /// negative = pronation) — for debug overlays and tuning. Name kept for the frozen
    /// Phase-2 surface (DevConsole prints it); the unit changed from dot to degrees with
    /// the roll-only rework.</summary>
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

        // Hand frame — SAME sources as before the rework: raw device grip pose (−Y out
        // of the physical palm, +Z along the fingers) or the visual rig for sim hands.
        Vector3 palmNormal;
        Vector3 forward;
        if (UseDevicePalmNormal)
        {
            Quaternion device = _hand.transform.rotation;
            palmNormal = device * Vector3.down;
            forward = device * Vector3.forward;
        }
        else
        {
            palmNormal = _hand.Rig.PalmNormal;
            forward = _hand.Rig.PalmCenter.forward;
        }

        // Pitch-neutral up: the world-up component perpendicular to the finger axis —
        // where the back-of-hand would point at ZERO roll for the current pitch/yaw.
        Vector3 upRef = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
        if (upRef.sqrMagnitude < 1e-4f)
            return; // fingers point straight up/down — roll undefined, hold state

        // Back-of-hand up, projected into the same roll plane (⊥ finger axis).
        Vector3 up = -palmNormal;
        Vector3 upProj = up - forward * Vector3.Dot(up, forward);
        if (upProj.sqrMagnitude < 1e-6f)
            return; // degenerate rig frame — hold state

        upRef.Normalize();
        upProj.Normalize();

        // Roll magnitude (0..180°, pitch/yaw-invariant) + direction sign. Rotating the
        // back-of-hand from Uref toward the thumb side is SUPINATION; the cross-product
        // sign of that rotation is mirrored between hands (right-hand supination turns
        // negative around +F, left-hand positive), hence the per-side flip. Past the
        // saturation angle the sign is forced positive (see SignSaturationDegrees).
        float rollDeg = Vector3.Angle(upRef, upProj);
        float turn = Vector3.Dot(Vector3.Cross(upRef, upProj), forward);
        float side = _hand.Side == HandSide.Right ? -1f : 1f;
        bool supinating = turn * side >= 0f || rollDeg >= SignSaturationDegrees;
        CurrentDot = supinating ? rollDeg : -rollDeg;

        // Driver-set thresholds ([Cards] RevealEnterDegrees/RevealExitDegrees). Exit is
        // clamped below enter so a hand-edited config can never invert the hysteresis.
        float enter = EnterDegrees;
        float exit = Mathf.Min(ExitDegrees, enter - MinHysteresisDegrees);

        if (!IsOpen && CurrentDot > enter)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate OPEN ({_hand.Side}): roll {CurrentDot:F0}° (enter {enter:F0}°, exit {exit:F0}°).");
            SetOpen(true);
        }
        else if (IsOpen && CurrentDot < exit)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate CLOSE ({_hand.Side}): roll {CurrentDot:F0}° (enter {enter:F0}°, exit {exit:F0}°).");
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
