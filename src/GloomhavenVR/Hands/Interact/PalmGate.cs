using System;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm rolled toward the face" detector with hysteresis (FROZEN Phase-2 API;
/// P6 additive: tunable thresholds).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// ROLL MEASURE (v4, hardware feedback round 4 — TRUE roll-only, pitch-invariant at
/// EVERY pitch, including fingers straight up): the v3 Demeo measure
/// (<c>dot(side · handRight, worldUp)</c>) is pitch-proof only while the fingers stay
/// off vertical — with the hand pointing UP the right axis lies horizontal and twisting
/// the wrist merely SPINS it around the vertical, so the dot pins near 0 and the fan can
/// never open (user requirement: hand straight up + wrist twist MUST reveal). v4 keeps a
/// PARALLEL-TRANSPORTED reference-up instead, which has no degenerate pose:
///  - F = the hand's finger axis, U = the back-of-hand up (both from the VISUAL hand
///    frame, see below).
///  - Each frame the previous reference <c>Uref</c> is transported onto the plane
///    perpendicular to the CURRENT F: <c>Uref = normalize(UrefPrev − F·dot(UrefPrev, F))</c>.
///    Transport is continuous at every pitch: pure PITCH rotates U and Uref identically
///    about the pitch axis (their angle about F is untouched), pure YAW likewise, and
///    pure ROLL rotates U about a fixed F while Uref stays put — so roll, and ONLY roll,
///    moves the measure, at any arm pitch including straight up/down.
///  - Transport alone would drift (holonomy: closed wrist paths accumulate a residual
///    twist), so while F is FAR from vertical (<c>|dot(F, up)| &lt; 0.7</c>) Uref is eased
///    back onto the pitch-neutral world anchor <c>normalize(up − F·dot(up, F))</c> —
///    exactly the construction that is well-defined there — killing any accumulated
///    drift within a fraction of a second. Near vertical (where that anchor degenerates)
///    the transported value carries the reference through, which is the whole point.
///  - roll = signed angle from Uref to U about F, sign per hand as before (left +,
///    right −) so SUPINATION (palm toward the face) is positive for both hands: 0° =
///    knuckles-up flat hand, 90° = palm fully rolled toward the face, negative =
///    pronation (never opens the fan).
///
/// MEASURED FRAME (v4): +Z along the fingers, +Y out of the BACK of the hand, both hands
/// (the <c>HandRig.Root</c> convention; HandRig doc) — but BUILT from the device rotation
/// times the SHIPPED seat, never from the live, user-tuned hand transform. Reading
/// <c>HandRig.Root</c> itself was tried and rejected: a cosmetic re-seat then silently
/// re-tuned the gesture. Root cause and the exact construction: the frame block in
/// <see cref="Tick"/> and <c>HandsConfig.ShippedSeatRotation</c>. (v3 selected a raw
/// device pose through a flag on this class; the flag was retired once nothing read it,
/// so the name is deliberately not repeated here.)
///
/// Opens when the roll exceeds <see cref="EnterDegrees"/> (default 60° — a comfortable
/// supination), closes below <see cref="ExitDegrees"/> (default 45°); the dead band
/// prevents flicker at the boundary. Open/close logs include the current PITCH of the
/// finger axis to prove pitch-invariance — at the DEBUG tier (<c>VRLog.Info</c> gates on
/// <c>VRLogLevel.Debug</c> since ModBuild 331 and the shipped level is Info), so a capture
/// needs <c>[General] LogLevel = Debug</c>; v4 is accepted and the line is kept as the
/// bisection tool for a future roll-gate report, not as standing evidence. Ticked every frame by
/// <see cref="VRHand"/>; state is queryable (<see cref="IsOpen"/>) and edge-observable
/// (<see cref="Changed"/>, main thread).
/// </summary>
internal sealed class PalmGate
{
    // Roll gate defaults (unchanged since v3): open at 60° of supination (comfortable,
    // palm well past vertical but no full wrist crank), close at 45° (15° dead band so
    // the gate cannot chatter at the boundary). The Cards driver overwrites both every
    // frame from [Cards] RevealEnterDegrees / RevealExitDegrees.
    private const float DefaultEnterDegrees = 60f;
    private const float DefaultExitDegrees = 45f;

    /// <summary>Minimum enter-exit dead band (degrees) enforced in <see cref="Tick"/> — a
    /// hand-edited config can never invert the hysteresis into an every-frame flicker.</summary>
    private const float MinHysteresisDegrees = 3f;

    /// <summary>|dot(F, worldUp)| below which the finger axis counts as far from vertical:
    /// the pitch-neutral world anchor is well-conditioned there, so the transported Uref is
    /// eased back onto it (drift kill) and may be (re)initialized from it. 0.7 ≈ 45° of
    /// pitch margin on either side of vertical.</summary>
    private const float VerticalAnchorDot = 0.7f;

    /// <summary>Re-anchor ease rate (1/s, exponential): while far from vertical the
    /// transported Uref converges onto the pitch-neutral world anchor in a few hundred ms —
    /// fast enough that transport drift never survives a trip through vertical and back,
    /// slow enough to stay continuous (no roll-reading pop crossing the 0.7 boundary).</summary>
    private const float ReanchorSharpness = 8f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _busySuppressed;

    /// <summary>Parallel-transported reference up (world space, unit, ⊥ last frame's finger
    /// axis). Null when the chain is broken (startup, pose loss, gate disabled) — it then
    /// re-anchors from the world construction the next far-from-vertical frame.</summary>
    private Vector3? _uref;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>Signed roll (degrees) above which the gate opens. 0 = knuckles-up flat hand, 90 = palm fully toward the face.</summary>
    public float EnterDegrees = DefaultEnterDegrees;

    /// <summary>Signed roll (degrees) below which the gate closes (hysteresis; clamped below <see cref="EnterDegrees"/>).</summary>
    public float ExitDegrees = DefaultExitDegrees;

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

    /// <summary>Latest SIGNED roll in DEGREES (v4 parallel-transport measure: 0 knuckles-up
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
            {
                _uref = null; // transport chain broken — re-anchor cleanly on re-enable
                SetOpen(false);
            }
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
        {
            _uref = null; // pose lost — the transported reference is stale; re-anchor on resume
            return;
        }

        // G5 busy-hand gate (mirrors CardHandController.cs:475-476): the hand currently
        // grabbing a card must not also reveal the fan. Force-close and skip the threshold
        // logic while busy — but keep the roll measure (and its transported reference)
        // ticking below, so the gate resumes with a live, un-drifted reference the moment
        // the grab ends.
        bool busy = IgnoreWhenHandBusy && _hand.Grabber.Held != null;
        if (busy)
        {
            if (!_busySuppressed)
            {
                _busySuppressed = true;
                Core.VRLog.Debug("Interact", "PalmGate reveal suppressed — gate hand busy grabbing.");
            }
            SetOpen(false);
        }
        else if (_busySuppressed)
        {
            _busySuppressed = false;
            Core.VRLog.Debug("Interact", "PalmGate reveal restored — gate hand free.");
        }

        // THE GESTURE IS A PROPERTY OF THE CONTROLLER, NOT OF HOW THE HANDS ARE SEATED. The frame
        // still has +Z along the fingers and +Y out of the back of the hand (v4, class doc), but it
        // is built from the DEVICE rotation plus the SHIPPED seat — never the tuned one.
        //
        // It used to read HandRig.Root, which carries whatever the player dialled in. That made
        // re-seating the hands silently re-tune the gesture: turning them to sit right on the
        // controller moved the wrist angle at which the fan opens, which is not something a
        // cosmetic setting may do. Anchoring on the shipped seat keeps "how far do I turn my
        // wrist" the same for everyone and at every hand tuning, while leaving the frame exactly
        // what v4 chose for the default seat.
        int style = (int)(_hand.Rig != null ? _hand.Rig.VisualStyle : HandVisuals.LocalStyle());
        Quaternion frame = _hand.transform.rotation * HandsConfig.ShippedSeatRotation(style);
        Vector3 f = frame * Vector3.forward; // finger axis
        Vector3 u = frame * Vector3.up;      // back-of-hand up

        float fDotUp = Mathf.Clamp(Vector3.Dot(f, Vector3.up), -1f, 1f);
        float pitchDeg = Mathf.Asin(fDotUp) * Mathf.Rad2Deg; // for the invariance logs
        bool anchorable = Mathf.Abs(fDotUp) < VerticalAnchorDot;

        // Parallel transport: project the previous reference onto the plane ⊥ current F.
        Vector3 uref;
        if (_uref is { } prev)
        {
            Vector3 transported = prev - f * Vector3.Dot(prev, f);
            if (transported.sqrMagnitude > 1e-8f)
                uref = transported.normalized;
            else if (anchorable)
                uref = PitchNeutralUp(f); // stale reference collapsed onto F — rebuild
            else
                return; // collapsed AND vertical: nothing sane to measure — hold state
        }
        else if (anchorable)
        {
            uref = PitchNeutralUp(f); // first frame / re-anchor after a break
        }
        else
        {
            return; // no reference yet and too vertical to build one — hold state
        }

        // Drift kill: far from vertical the pitch-neutral world anchor is well-defined and
        // IS the correct reference — ease the transported value onto it so holonomy drift
        // from wrist excursions can never accumulate. Near vertical, transport rules alone.
        if (anchorable)
        {
            Vector3 anchor = PitchNeutralUp(f);
            float blend = 1f - Mathf.Exp(-ReanchorSharpness * Time.deltaTime);
            uref = Vector3.Slerp(uref, anchor, blend).normalized;
        }
        _uref = uref;

        // Signed roll about the finger axis; per-side sign (left +, right −) makes
        // SUPINATION positive for both hands, exactly as before.
        float side = _hand.Side == HandSide.Right ? -1f : 1f;
        CurrentDot = side * Vector3.SignedAngle(uref, u, f);

        if (busy)
            return; // measure kept warm; gate held closed above

        // Driver-set thresholds ([Cards] RevealEnterDegrees/RevealExitDegrees). Exit is
        // clamped below enter so a hand-edited config can never invert the hysteresis.
        float enter = EnterDegrees;
        float exit = Mathf.Min(ExitDegrees, enter - MinHysteresisDegrees);

        if (!IsOpen && CurrentDot > enter)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate OPEN ({_hand.Side}): roll {CurrentDot:F0}° (pitch {pitchDeg:F0}°; enter {enter:F0}°, exit {exit:F0}°).");
            SetOpen(true);
        }
        else if (IsOpen && CurrentDot < exit)
        {
            Core.VRLog.Info("Interact",
                $"PalmGate CLOSE ({_hand.Side}): roll {CurrentDot:F0}° (pitch {pitchDeg:F0}°; enter {enter:F0}°, exit {exit:F0}°).");
            SetOpen(false);
        }
    }

    /// <summary>Pitch-neutral reference up: world up projected onto the plane ⊥ the finger
    /// axis. Only called while <c>|dot(F, up)| &lt; VerticalAnchorDot</c>, where the
    /// projection is well-conditioned (magnitude ≥ √(1−0.7²) ≈ 0.71).</summary>
    private static Vector3 PitchNeutralUp(Vector3 f) =>
        (Vector3.up - f * Vector3.Dot(Vector3.up, f)).normalized;

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
