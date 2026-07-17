using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Test #14 ("Controllboard"): grip-grab handle for the <see cref="PlayTray"/>.
///
/// - ONE hand gripping the handle bar carries the tray: position follows the palm,
///   rotation follows the hand's yaw (yaw-only — the configured tilt is preserved,
///   the tray can never end up rolled/upside down).
/// - TWO hands gripping resize it (spread = grow, pinch = shrink; clamped 0.5×–2×)
///   while also moving/yawing with the pair midpoint/heading.
/// - On final release the pose is persisted (<see cref="PlayTray.PersistPoseToConfig"/>)
///   so the layout survives sessions.
///
/// Arbitration: this is a plain registered <see cref="IGrabbable"/> — the P2
/// <see cref="ProximityGrabber"/> only highlights it within palm reach of the handle
/// collider, and <c>Rig.WorldGrab</c> yields any grip that starts on a highlighted or
/// held grabbable (its documented grip-contention rule). So the tray grab wins exactly
/// when the grip starts in the tray's grab zone, and never fights the world grab.
/// No re-parenting, no physics; per-frame math only, zero allocations.
/// </summary>
internal sealed class TrayGrabHandle : MonoBehaviour, IGrabbable, IGrabHighlight
{
    private const float MinHandDistance = 0.03f;  // world units (already diorama-scaled)
    private const float Smoothing = 18f;          // 1/s exponential
    private const float MinScale = 0.5f;
    private const float MaxScale = 2f;

    private PlayTray? _owner;
    private MeshRenderer? _bar;
    private Color _barBaseColor;

    private VRHand? _handA;
    private VRHand? _handB;

    // Gesture anchors (captured on every hand-count change).
    private Vector3 _anchorPos;        // palm (one-hand) or midpoint (two-hand) at engage
    private float _anchorHeading;      // hand yaw (one-hand) or pair heading (two-hand), deg
    private float _anchorDistance;     // palm distance at engage (two-hand)
    private Vector3 _rootPos0;
    private Quaternion _rootRot0 = Quaternion.identity;
    private float _rootScale0 = 1f;
    private float _lastHeading;        // last valid heading (degenerate-pose fallback)

    internal void Init(PlayTray owner, MeshRenderer bar)
    {
        _owner = owner;
        _bar = bar;
        _barBaseColor = bar.sharedMaterial != null ? bar.sharedMaterial.color : Color.white;
    }

    // ------------------------------------------------------------------ IGrabbable --

    public bool CanGrab => _owner != null && _owner.IsVisible && (_handA == null || _handB == null);

    public void OnGrab(VRHand hand)
    {
        if (_handA == null)
            _handA = hand;
        else if (_handB == null && hand != _handA)
            _handB = hand;
        else
            return;
        ReAnchor();
        VRLog.Info("Cards", $"Tray grab: engaged ({hand.Side}, {(_handB != null ? "two-hand resize" : "one-hand move")}).");
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        if (hand == _handA)
        {
            _handA = _handB;
            _handB = null;
        }
        else if (hand == _handB)
        {
            _handB = null;
        }
        else
        {
            return;
        }

        if (_handA != null)
        {
            ReAnchor(); // continue as a one-hand carry from the current pose
            VRLog.Info("Cards", $"Tray grab: {hand.Side} released — continuing one-hand.");
        }
        else
        {
            VRLog.Info("Cards", $"Tray grab: released ({hand.Side}).");
            _owner?.PersistPoseToConfig();
        }
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        if (_bar != null && _bar.sharedMaterial != null)
            _bar.sharedMaterial.color = highlighted ? new Color(0.95f, 0.8f, 0.4f) : _barBaseColor;
    }

    // ------------------------------------------------------------------ lifecycle --

    private void OnEnable()
    {
        Collider? collider = GetComponent<Collider>();
        if (collider != null)
            VRInteractables.RegisterGrabbable(this, collider);
    }

    private void OnDisable()
    {
        VRInteractables.UnregisterGrabbable(this);
        _handA = _handB = null;
    }

    // ------------------------------------------------------------------ per-frame --

    private void Update()
    {
        Transform? root = _owner?.Root;
        if (root == null)
            return;

        // Defensive: a hand can vanish (tracking loss/hot reload) without OnRelease.
        if (_handA != null && !_handA.HasPose)
        {
            _handA = _handB;
            _handB = null;
            if (_handA != null) ReAnchor();
        }
        if (_handB != null && !_handB.HasPose)
        {
            _handB = null;
            ReAnchor();
        }
        if (_handA == null)
            return;

        float k = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);

        if (_handB == null)
        {
            // One hand: rigid yaw-only carry.
            Vector3 palm = _handA.Rig.PalmCenter.position;
            float dYaw = Mathf.DeltaAngle(_anchorHeading, HandHeading(_handA));
            Quaternion spin = Quaternion.Euler(0f, dYaw, 0f);
            Vector3 targetPos = palm + spin * (_rootPos0 - _anchorPos);
            Quaternion targetRot = spin * _rootRot0;
            root.position = Vector3.Lerp(root.position, targetPos, k);
            root.rotation = Quaternion.Slerp(root.rotation, targetRot, k);
        }
        else
        {
            // Two hands: midpoint carry + pair-heading yaw + pinch scale (0.5×–2×).
            Vector3 pA = _handA.Rig.PalmCenter.position;
            Vector3 pB = _handB.Rig.PalmCenter.position;
            Vector3 mid = (pA + pB) * 0.5f;
            float d = Mathf.Max(Vector3.Distance(pA, pB), MinHandDistance);
            float dYaw = Mathf.DeltaAngle(_anchorHeading, HeadingDegrees(pB - pA));

            float targetScale = Mathf.Clamp(_rootScale0 * (d / _anchorDistance), MinScale, MaxScale);
            float newScale = Mathf.Lerp(root.localScale.x, targetScale, k);
            float ratio = newScale / _rootScale0;

            Quaternion spin = Quaternion.Euler(0f, dYaw, 0f);
            Vector3 targetPos = mid + spin * ((_rootPos0 - _anchorPos) * ratio);
            Quaternion targetRot = spin * _rootRot0;

            root.localScale = Vector3.one * newScale;
            root.position = Vector3.Lerp(root.position, targetPos, k);
            root.rotation = Quaternion.Slerp(root.rotation, targetRot, k);
        }
    }

    // ------------------------------------------------------------------ helpers --

    private void ReAnchor()
    {
        Transform? root = _owner?.Root;
        if (root == null || _handA == null)
            return;
        _rootPos0 = root.position;
        _rootRot0 = root.rotation;
        _rootScale0 = root.localScale.x;
        if (_handB == null)
        {
            _anchorPos = _handA.Rig.PalmCenter.position;
            _anchorHeading = HandHeading(_handA);
        }
        else
        {
            Vector3 pA = _handA.Rig.PalmCenter.position;
            Vector3 pB = _handB.Rig.PalmCenter.position;
            _anchorPos = (pA + pB) * 0.5f;
            _anchorDistance = Mathf.Max(Vector3.Distance(pA, pB), MinHandDistance);
            _anchorHeading = HeadingDegrees(pB - pA);
        }
    }

    /// <summary>Horizontal heading of the hand's pointing direction (stable fallback near vertical).</summary>
    private float HandHeading(VRHand hand)
    {
        Vector3 fwd = hand.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
            return _lastHeading; // pointing straight up/down — keep the last stable value
        _lastHeading = HeadingDegrees(fwd);
        return _lastHeading;
    }

    private static float HeadingDegrees(Vector3 dir) =>
        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
}
