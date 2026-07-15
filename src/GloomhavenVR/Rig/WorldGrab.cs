using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Demeo-style world grab (ARCHITECTURE §3): grip the air to manipulate the diorama.
///
/// - One grip held (away from any <c>IGrabbable</c>) → drag the table on the horizontal
///   plane (vertical opt-in via <c>[Comfort] VerticalDrag</c>).
/// - Both grips held → rotate the table around the point between the hands (yaw only)
///   and pinch-scale it (spread hands = board grows), clamped to
///   <c>[Comfort] ScaleMin/ScaleMax</c> × base WorldScale, with haptic detents at every
///   25% scale step.
///
/// All motion is applied INVERSELY to the rig root — game objects are never moved
/// (game camera code is already prefix-skipped by the P1 patches, so nothing fights us).
///
/// GRIP CONTENTION (documented rule): object grabs win. A grip only starts a world grab
/// if that hand's <see cref="Hands.Interact.ProximityGrabber"/> neither holds nor
/// highlights a grabbable at grip-down; a grip that grabbed a card is ignored here until
/// released. World grab runs in every scenario mode except <see cref="VRMode.ModalUI"/>
/// (ARCHITECTURE §8).
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
    private bool _leftGrip;   // this hand's grip is owned by world grab
    private bool _rightGrip;

    // Gesture anchors.
    private VRHand? _dragHand;
    private Vector3 _anchorWorld;      // one-hand: world point under the palm at engage
    private Vector3 _midAnchorWorld;   // two-hand: world point under the midpoint
    private float _d0;                 // tracking-space hand distance at engage (meters)
    private float _theta0;             // tracking-space pair heading at engage (deg)
    private float _yaw0;               // rig yaw at engage
    private float _s0;                 // rig scale at engage
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

    /// <summary>True while the given hand's grip is consumed by the world grab (SnapTurn contention check).</summary>
    internal bool IsHandGrabbing(VRHand hand) =>
        hand.Side == HandSide.Left ? _leftGrip : _rightGrip;

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
        if (rig == null || !ComfortSettings.IsBound || !ComfortSettings.WorldGrabEnabled.Value
            || VRModeStateMachine.CurrentMode == VRMode.ModalUI)
        {
            Disengage(rig);
            return;
        }

        UpdateGripOwnership(VRHands.Left, ref _leftGrip);
        UpdateGripOwnership(VRHands.Right, ref _rightGrip);

        GrabState desired =
            _leftGrip && _rightGrip ? GrabState.TwoHand :
            _leftGrip || _rightGrip ? GrabState.OneHand :
            GrabState.None;

        if (desired != _state)
        {
            // Leaving the two-hand gesture persists the reached scale multiplier.
            if (_state == GrabState.TwoHand)
                ComfortSettings.PersistScaleMultiplier(rig.localScale.x / RigTarget.BaseScale);
            _state = desired;
            Anchor(rig);
        }
        else if (_state == GrabState.OneHand && _dragHand != null
                 && !(_dragHand.Side == HandSide.Left ? _leftGrip : _rightGrip))
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
    /// A grip becomes a WORLD grip only at grip-down with nothing held/highlighted by the
    /// proximity grabber (card/object grabs win — frozen P2 contract). It stays a world
    /// grip until physically released, and is dropped defensively if an object somehow
    /// becomes held mid-gesture.
    /// </summary>
    private static void UpdateGripOwnership(VRHand? hand, ref bool owned)
    {
        if (hand == null || !hand.HasPose)
        {
            owned = false;
            return;
        }

        if (owned)
        {
            if (!hand.GripPressed || hand.Grabber.Held != null)
                owned = false;
            return;
        }

        if (hand.GripDown && hand.Grabber.Held == null && hand.Grabber.Highlighted == null)
            owned = true;
    }

    // ---- anchoring -----------------------------------------------------------------------

    /// <summary>Tracking-space position of a hand (rig-motion invariant — see class docs).</summary>
    private static Vector3 TrackingPos(VRHand hand) => hand.transform.localPosition;

    private void Anchor(Transform rig)
    {
        _dragLive = _rotateLive = _scaleLive = false;

        if (_state == GrabState.OneHand)
        {
            _dragHand = _leftGrip ? VRHands.Left : VRHands.Right;
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
            _yaw0 = rig.eulerAngles.y;
            _s0 = rig.localScale.x;
            _midAnchorWorld = rig.TransformPoint((tL + tR) * 0.5f);
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
        if (!ComfortSettings.VerticalDrag.Value)
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
        rig.position += delta * k;
        RigClamp.Apply(rig);

        // Motion intensity in real meters of remaining correction.
        ComfortVignette.NotifyMotion(delta.magnitude / scale * 4f);
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
        if (ComfortSettings.ScaleEnabled.Value)
        {
            if (!_scaleLive && Mathf.Abs(d - _d0) > ScaleDeadzoneFraction * _d0)
                _scaleLive = true;
            if (_scaleLive)
            {
                float target = Mathf.Clamp(_s0 * (_d0 / d),
                    baseScale * ComfortSettings.ScaleMin.Value,
                    baseScale * ComfortSettings.ScaleMax.Value);
                s = Mathf.Lerp(s, target, k);
            }
        }

        // Yaw: keep the world direction between the hands constant → rig yaw counters the
        // tracking-space heading change (yaw-only by design; the P1 rig has no pitch/roll).
        float yaw = rig.eulerAngles.y;
        if (ComfortSettings.RotateEnabled.Value)
        {
            float theta = HeadingDegrees(tR - tL);
            float dTheta = Mathf.DeltaAngle(theta, _theta0);
            if (!_rotateLive && Mathf.Abs(dTheta) > RotateDeadzoneDegrees)
                _rotateLive = true;
            if (_rotateLive)
                yaw = Mathf.LerpAngle(yaw, _yaw0 + dTheta, k);
        }

        float prevYaw = rig.eulerAngles.y;
        float prevScale = rig.localScale.x;

        rig.localScale = Vector3.one * s;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
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

        float motion = Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw)) / 15f
                       + Mathf.Abs(s - prevScale) / (baseScale * 0.05f);
        ComfortVignette.NotifyMotion(motion);
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Horizontal heading of a tracking-space direction, degrees.</summary>
    private static float HeadingDegrees(Vector3 dir) =>
        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    private void Disengage(Transform? rig)
    {
        if (_state == GrabState.TwoHand && rig != null)
            ComfortSettings.PersistScaleMultiplier(rig.localScale.x / RigTarget.BaseScale);
        _state = GrabState.None;
        _leftGrip = _rightGrip = false;
        _dragHand = null;
        _dragLive = _rotateLive = _scaleLive = false;
    }
}
