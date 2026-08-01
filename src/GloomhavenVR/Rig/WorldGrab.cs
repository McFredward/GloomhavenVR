using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Demeo-style world grab (ARCHITECTURE §3): click the THUMBSTICK to manipulate the
/// diorama. Rebound off the grip (P8, figure-grab): the grip now grabs board figures,
/// so the world drag lives on the stick-push button (<c>primary2DAxisClick</c>).
///
/// - One stick clicked (held down) → drag the table freely in ALL directions (test #10,
///   <c>[Comfort] FreeMovement</c> default ON; with it off the drag is horizontal-only
///   unless <c>[Comfort] VerticalDrag</c>).
/// - Both sticks clicked → rotate the table around the point between the hands (yaw
///   only) and pinch-scale it (spread hands = board grows), clamped to the EFFECTIVE
///   scale limits (at least 0.1×–12× of base WorldScale while FreeMovement; otherwise
///   <c>[Comfort] ScaleMin/ScaleMax</c>), with haptic detents at every 25% scale step.
///
/// All motion is applied INVERSELY to the rig root — game objects are never moved
/// (game camera code is already prefix-skipped by the P1 patches, so nothing fights us).
///
/// STICK CONTENTION (documented rule): the stick CLICK is a distinct button from the
/// stick AXIS — SnapTurn and AoE targeting read the axis (<c>hand.Thumbstick.x</c>),
/// world grab reads the click, so the two never fight. A stick-click starts a world grab
/// regardless of what the hand holds: figure/card grabs live on the grip/trigger
/// (<see cref="Hands.Interact.ProximityGrabber"/>) and never contend for the stick, so
/// world locomotion stays available WHILE a mini or card is in hand — the held object is
/// parented to the hand and simply rides the rig as the world moves (see
/// <see cref="UpdateStickOwnership"/>). World grab runs in EVERY scenario mode —
/// including <see cref="VRMode.ModalUI"/> since test #13: floating dialogs must not freeze
/// the diorama (the player reads the story box AND repositions the table). Only
/// <see cref="VRMode.Menu2D"/> is excluded (no table exists).
///
/// MATH (tracking-space anchored, feedback-free): with the rig mapping
/// <c>world = rigPos + rigRot · (s · t)</c> for a tracking-space point <c>t</c>, anchors
/// are captured in world space at engage and the rig is re-solved each frame so the
/// world point under the hand(s) stays glued to them. Deadzones latch off per gesture;
/// exponential smoothing keeps micro-jitter from swimming the world.
/// </summary>
internal sealed class WorldGrab : MonoBehaviour
{
    private enum GrabState { None, OneHand, TwoHand }

    // Tuning — real meters/degrees (scale-independent).
    private const float DragDeadzoneMeters = 0.015f;
    private const float RotateDeadzoneDegrees = 2.5f;
    private const float ScaleDeadzoneFraction = 0.04f;
    private const float PositionSmoothing = 18f;      // 1/s exponential
    private const float RotateScaleSmoothing = 14f;   // 1/s exponential
    private const float MinHandDistanceMeters = 0.05f;
    private const float ScaleDetentStep = 0.25f;

    internal static WorldGrab? Instance { get; private set; }

    private GrabState _state;
    private bool _leftStick;   // this hand's stick-click is owned by world grab
    private bool _rightStick;

    // Gesture anchors.
    private VRHand? _dragHand;
    private Vector3 _anchorWorld;      // one-hand: world point under the palm at engage
    private Vector3 _midAnchorWorld;   // two-hand: world point under the midpoint
    private float _d0;                 // tracking-space hand distance at engage (meters)
    private float _theta0;             // tracking-space pair heading at engage (deg)
    private float _yaw0;               // rig yaw at engage
    private float _s0;                 // rig scale at engage
    private Vector3 _prevMid;          // two-hand: last frame's tracking-space midpoint (real
                                       // meters) — its per-frame displacement IS the applied
                                       // world drag (the mid world point is glued to the
                                       // hands), reported to NotifyWorldGrabMotion (round 7)
    private int _lastDetent;

    // Deadzone latches (per gesture).
    private bool _dragLive;
    private bool _rotateLive;
    private bool _scaleLive;

    // ---- read-only state for gizmos/tests ---------------------------------------------

    internal bool IsGrabbing => _state != GrabState.None;
    internal bool IsTwoHand => _state == GrabState.TwoHand;

    /// <summary>Current scale multiplier relative to the base WorldScale (1 when idle/no rig).</summary>
    internal float CurrentMultiplier
    {
        get
        {
            Transform? rig = RigTarget.Current;
            return rig == null ? 1f : rig.localScale.x / RigTarget.BaseScale;
        }
    }

    /// <summary>True while the given hand's stick-click is consumed by the world grab (SnapTurn contention check).</summary>
    internal bool IsHandGrabbing(VRHand hand) =>
        hand.Side == HandSide.Left ? _leftStick : _rightStick;

    // ---- lifecycle -----------------------------------------------------------------------

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        Transform? rig = RigTarget.Current;
        VRMode mode = VRModeStateMachine.CurrentMode;
        // P5: Menu2D is excluded — the menu rig (VRRigDriver menu fallback) exists
        // there since P5, but there is no table to manipulate; grabbing air must not
        // drag the menu view. The dev proxy stays exempt so grab math is testable flat.
        // Test #13: ModalUI is deliberately NOT excluded anymore — the diorama stays
        // fully manipulable while a dialog floats (see class doc, grip contention).
        if (rig == null || !ComfortSettings.IsBound || !ComfortSettings.WorldGrabEnabled.Value
            || (mode == VRMode.Menu2D && !RigTarget.IsDevProxy))
        {
            Disengage(rig);
            return;
        }

        UpdateStickOwnership(VRHands.Left, ref _leftStick);
        UpdateStickOwnership(VRHands.Right, ref _rightStick);

        GrabState desired =
            _leftStick && _rightStick ? GrabState.TwoHand :
            _leftStick || _rightStick ? GrabState.OneHand :
            GrabState.None;

        if (desired != _state)
        {
            // Leaving the two-hand gesture persists the reached scale multiplier.
            if (_state == GrabState.TwoHand)
                ComfortSettings.PersistScaleMultiplier(rig.localScale.x / RigTarget.BaseScale);
            // Grab press/release is deliberately NOT a masked re-aim event (round 7): at
            // these instants the grab has not moved the world (yet/anymore), so nothing
            // masks an instant re-aim — the old NotifyTiltAxisSnap here consumed the whole
            // accumulated view error in one frame and visibly jumped the scene after
            // room-scale movement. Any re-aim a gesture earns is consumed continuously,
            // in proportion to the motion it actually applies (NotifyWorldGrabMotion in
            // ApplyOneHand/ApplyTwoHand); leftover error stays frozen, exactly like
            // head-only motion (see VRRigDriver.TickWorldTilt).
            _state = desired;
            Anchor(rig);
        }
        else if (_state == GrabState.OneHand && _dragHand != null
                 && !(_dragHand.Side == HandSide.Left ? _leftStick : _rightStick))
        {
            // Drag hand swapped within a single frame (old released + new gripped):
            // same state, but the gesture must re-anchor on the new hand.
            Anchor(rig);
        }

        switch (_state)
        {
            case GrabState.OneHand:
                ApplyOneHand(rig);
                break;
            case GrabState.TwoHand:
                ApplyTwoHand(rig);
                break;
        }
    }

    // ---- grip ownership ---------------------------------------------------------------

    /// <summary>
    /// A stick-click becomes a WORLD grab at click-down and stays one until the stick is
    /// released.
    ///
    /// WHY no held-object gate: world locomotion and the figure/card grab live on DIFFERENT
    /// buttons — world grab reads the stick CLICK (<c>primary2DAxisClick</c>), figure/card
    /// grab reads the trigger/grip (<see cref="Hands.Interact.ProximityGrabber"/>) — so they
    /// never contend for the same input. The old <c>Grabber.Held == null</c> guard was purely
    /// defensive and wrongly froze locomotion whenever a hand held something: the player could
    /// not pull the table closer while carrying a mini or a card. It is removed so world drag
    /// engages even with an object in hand. The held object is parented to the hand and the
    /// hand rides the rig root, so dragging/rotating/scaling the world simply carries the held
    /// object along — exactly what "move the world while I hold a figure" should feel like; the
    /// object is never dropped (its own trigger/grip still owns release) nor duplicated.
    /// </summary>
    private static void UpdateStickOwnership(VRHand? hand, ref bool owned)
    {
        if (hand == null || !hand.HasPose)
        {
            owned = false;
            return;
        }

        if (owned)
        {
            // Held state is deliberately NOT checked here — a world grab that becomes a hold
            // mid-gesture (or vice versa) keeps driving the world until the stick releases.
            if (!hand.ThumbstickClick)
                owned = false;
            return;
        }

        if (hand.ThumbstickClickDown)
        {
            owned = true;
            // Diagnostic: world locomotion now co-exists with a held object (previously this
            // engage was suppressed while Grabber.Held != null). Trace the coexistence so a
            // hardware log shows the world drag starting with a figure/card in hand.
            if (hand.Grabber.Held != null)
                Core.VRLog.Debug("WorldGrab",
                    $"{hand.Side} world-grab engaged while holding '{hand.Grabber.Held.GetType().Name}' — held object rides the rig.");
        }
    }

    // ---- anchoring -----------------------------------------------------------------------

    /// <summary>Tracking-space position of a hand (rig-motion invariant — see class docs).</summary>
    private static Vector3 TrackingPos(VRHand hand) => hand.transform.localPosition;

    private void Anchor(Transform rig)
    {
        _dragLive = _rotateLive = _scaleLive = false;

        if (_state == GrabState.OneHand)
        {
            _dragHand = _leftStick ? VRHands.Left : VRHands.Right;
            if (_dragHand == null)
            {
                _state = GrabState.None;
                return;
            }
            _anchorWorld = rig.TransformPoint(TrackingPos(_dragHand));
        }
        else if (_state == GrabState.TwoHand)
        {
            VRHand? left = VRHands.Left;
            VRHand? right = VRHands.Right;
            if (left == null || right == null)
            {
                _state = GrabState.None;
                return;
            }
            Vector3 tL = TrackingPos(left);
            Vector3 tR = TrackingPos(right);
            _d0 = Mathf.Max(Vector3.Distance(tL, tR), MinHandDistanceMeters);
            _theta0 = HeadingDegrees(tR - tL);
            // Yaw TWIST about world up, not eulerAngles.y: under an active world tilt the rig
            // rotation is tilt ∘ yaw, whose euler y is NOT the yaw twist (garbage targets fed
            // into LerpAngle were half of Bug A). Same extraction the tilt heal uses, so both
            // sides agree on what "the rig's yaw" means.
            _yaw0 = VRRigDriver.YawOnly(rig.rotation).eulerAngles.y;
            _s0 = rig.localScale.x;
            _midAnchorWorld = rig.TransformPoint((tL + tR) * 0.5f);
            _prevMid = (tL + tR) * 0.5f; // grab-motion baseline: zero drag on the engage frame
            _lastDetent = Mathf.FloorToInt(_s0 / RigTarget.BaseScale / ScaleDetentStep);
        }
    }

    // ---- one-hand drag ---------------------------------------------------------------------

    private void ApplyOneHand(Transform rig)
    {
        VRHand? hand = _dragHand;
        if (hand == null || !hand.HasPose)
        {
            Disengage(rig);
            return;
        }

        Vector3 w = rig.TransformPoint(TrackingPos(hand));
        Vector3 delta = _anchorWorld - w; // move the rig so the world returns under the hand
        // Test #10: vertical drag is always on while [Comfort] FreeMovement (default);
        // the legacy horizontal-only behavior needs FreeMovement=false AND VerticalDrag=false.
        if (!ComfortSettings.EffectiveVerticalDrag)
            delta.y = 0f;

        float scale = rig.localScale.x;
        if (!_dragLive)
        {
            float deadzone = DragDeadzoneMeters * scale;
            if (delta.sqrMagnitude < deadzone * deadzone)
                return;
            _dragLive = true;
        }

        float k = 1f - Mathf.Exp(-PositionSmoothing * Time.deltaTime);
        Vector3 applied = delta * k;
        // Grab-motion-masked tilt re-aim (round 7): report the APPLIED world slide this
        // frame in real meters (world units / rig scale) so the tilt may consume view
        // error in proportion to it. A still hand reports ~0 → the error stays frozen;
        // press/release alone can never re-aim the tilt.
        VRRigDriver.NotifyWorldGrabMotion(applied.magnitude / scale, 0f, 0f);
        // Tutorial camera step: the same applied-motion report feeds the tutorial bridge —
        // deliberate VR locomotion IS the "got familiar with the camera" the flat tutorial
        // waits for (Compat.TutorialVR; cold path = two static reads outside tutorials).
        Compat.TutorialVR.NotifyLocomotion(applied.magnitude / scale, 0f, 0f);
        // Position-only write — tilt-safe by construction: the tilt heal (TickWorldTilt) keys
        // solely on ROTATION error (desired vs current rotation) and neither we nor RigClamp
        // (vertical lift only) touch the rotation here, so the only heal a drag can cause is
        // the AXIS-ONLY step from the re-aim above (or the head-rate channel) — healed about
        // the HEAD pivot, zero player translation — and the drag can never enter the Bug-A
        // flatten/re-tilt loop.
        rig.position += applied;
        RigClamp.Apply(rig);
    }

    // ---- two-hand rotate + scale ------------------------------------------------------------

    private void ApplyTwoHand(Transform rig)
    {
        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        if (left == null || right == null || !left.HasPose || !right.HasPose)
        {
            Disengage(rig);
            return;
        }

        Vector3 tL = TrackingPos(left);
        Vector3 tR = TrackingPos(right);
        float d = Mathf.Max(Vector3.Distance(tL, tR), MinHandDistanceMeters);
        Vector3 mid = (tL + tR) * 0.5f;

        float baseScale = RigTarget.BaseScale;
        float k = 1f - Mathf.Exp(-RotateScaleSmoothing * Time.deltaTime);

        // Pinch scale: spreading the hands stretches the world larger, i.e. FEWER world
        // units per real meter → rig scale shrinks by d0/d.
        float s = rig.localScale.x;
        float sBefore = s; // for the applied-motion report below
        if (ComfortSettings.ScaleEnabled.Value)
        {
            if (!_scaleLive && Mathf.Abs(d - _d0) > ScaleDeadzoneFraction * _d0)
                _scaleLive = true;
            if (_scaleLive)
            {
                // Effective limits (test #10): at least 0.1×–12× while FreeMovement.
                float target = Mathf.Clamp(_s0 * (_d0 / d),
                    baseScale * ComfortSettings.EffectiveScaleMin,
                    baseScale * ComfortSettings.EffectiveScaleMax);
                s = Mathf.Lerp(s, target, k);
            }
        }

        // Yaw: keep the world direction between the hands constant → rig yaw counters the
        // tracking-space heading change (the grab only ever AUTHORS yaw; any pitch on the rig
        // is the world tilt's, preserved below). Yaw twist via YawOnly, not eulerAngles.y —
        // see the Anchor() comment.
        float yaw = VRRigDriver.YawOnly(rig.rotation).eulerAngles.y;
        float yawBefore = yaw; // for the applied-motion report below
        if (ComfortSettings.RotateEnabled.Value)
        {
            float theta = HeadingDegrees(tR - tL);
            float dTheta = Mathf.DeltaAngle(theta, _theta0);
            if (!_rotateLive && Mathf.Abs(dTheta) > RotateDeadzoneDegrees)
                _rotateLive = true;
            if (_rotateLive)
                yaw = Mathf.LerpAngle(yaw, _yaw0 + dTheta, k);
        }

        rig.localScale = Vector3.one * s;
        // Grab-motion-masked tilt re-aim (round 7) — MUST run BEFORE CurrentTiltSwing below
        // so that the swing we compose (and this frame's TickWorldTilt reconstruction) both
        // read the post-consumption aim (lockstep, see CurrentTiltSwing). Reported motion is
        // what this frame APPLIES: real meters the glued midpoint moved (= how far the world
        // slid under the hands), world-yaw degrees, octaves of scale change.
        VRRigDriver.NotifyWorldGrabMotion(
            Vector3.Distance(mid, _prevMid),
            Mathf.DeltaAngle(yawBefore, yaw),
            Mathf.Log(s / sBefore, 2f));
        // Tutorial camera step: rotate/zoom count as camera familiarization too — same
        // applied-motion values as the tilt report above (Compat.TutorialVR).
        Compat.TutorialVR.NotifyLocomotion(
            Vector3.Distance(mid, _prevMid),
            Mathf.DeltaAngle(yawBefore, yaw),
            Mathf.Log(s / sBefore, 2f));
        _prevMid = mid;
        // TILT-CONSISTENT write (Bug A). The old bare yawRot write FLATTENED an actively
        // tilted rig every frame; TickWorldTilt (LateUpdate) then re-tilted it about a
        // DIFFERENT pivot (FocusPoint, not the hand midpoint), so each frame's flatten+re-tilt
        // displaced the midpoint, the next frame's solve chased it through the exponential
        // smoothing, and the closed loop flung the player. Composing the CURRENT desired tilt
        // swing T onto the new yaw Y and solving the position with the FULL rotation breaks
        // the loop algebraically: we write rot = T ∘ Y with T = AngleAxis(_tiltApplied,
        // (Y ∘ R_up(aim)) · right) — exactly the pose TickWorldTilt reconstructs, because
        // its yaw extraction YawOnly(T ∘ Y) = Y is exact (T's axis is horizontal) and both
        // sides read the aim from the SAME field, _tiltAimYawDeg, which we just finished
        // updating above (round 7 — the tick no longer re-seeds it during a grab). Desired
        // == current → the heal writes NOTHING → 'rig-pose-heal' is unreachable from a grab
        // (the tick's own head-rate re-aim step is the one bounded exception, healed about
        // the HEAD pivot, attributed 'world-grab'), and the midpoint stays genuinely glued
        // between the hands (position solved with rot, so rigPos + rot·(s·mid) =
        // _midAnchorWorld holds in the pose that survives the frame).
        // At tilt 0 the swing is identity → bit-identical to the old yaw-only behavior.
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Quaternion rot = VRRigDriver.CurrentTiltSwing(yawRot) * yawRot;
        rig.rotation = rot;
        // Solve position so the world midpoint stays glued between the hands.
        rig.position = _midAnchorWorld - rot * (mid * s);
        RigClamp.Apply(rig);

        // Haptic detent every 25% of base scale.
        int detent = Mathf.FloorToInt(s / baseScale / ScaleDetentStep);
        if (detent != _lastDetent)
        {
            _lastDetent = detent;
            left.SendHaptic(HapticPreset.ClickPulse);
            right.SendHaptic(HapticPreset.ClickPulse);
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Horizontal heading of a tracking-space direction, degrees.</summary>
    private static float HeadingDegrees(Vector3 dir) =>
        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    private void Disengage(Transform? rig)
    {
        if (_state == GrabState.TwoHand && rig != null)
            ComfortSettings.PersistScaleMultiplier(rig.localScale.x / RigTarget.BaseScale);
        // No NotifyTiltAxisSnap here (round 7): disengage applies no world motion, so an
        // instant tilt re-aim would be unmasked — see the release comment in Update().
        _state = GrabState.None;
        _leftStick = _rightStick = false;
        _dragHand = null;
        _dragLive = _rotateLive = _scaleLive = false;
    }
}
