using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Owner seam for <see cref="PanelGrabHandle"/> — the shared grip-grab core moves
/// whatever transform the owner exposes and hands the final release back for
/// persistence. Implemented by <see cref="Cards.PlayTray"/> (the original test #14
/// "Controllboard" handle) and by grabbable WorldUI panels (test #19: combat log).
/// </summary>
internal interface IPanelGrabOwner
{
    /// <summary>Transform the grab moves/scales (null while not built).</summary>
    Transform? GrabRoot { get; }

    /// <summary>False blocks NEW grips (hidden panel); running grips end via release/pose loss.</summary>
    bool GrabVisible { get; }

    /// <summary>
    /// True (tray, combat log): the carry also yaws the root with the hand/pair
    /// heading. False (owner-rotated panels, e.g. a billboard): the grab drives
    /// position + scale ONLY — the owner keeps authoring the rotation (two
    /// writers on the same rotation would jitter).
    /// </summary>
    bool GrabCarriesYaw { get; }

    /// <summary>The LAST gripping hand let go — persist the layout.</summary>
    void OnGrabFinished();
}

/// <summary>
/// Grip-grab handle core (test #14 "Controllboard", generalized in test #19 so any
/// panel can be moved/scaled/persisted exactly like the control board):
///
/// - ONE hand gripping the handle bar carries the owner root: position follows the
///   palm; with <see cref="IPanelGrabOwner.GrabCarriesYaw"/> the rotation follows
///   the hand's yaw (yaw-only — the configured tilt is preserved, the root can
///   never end up rolled/upside down).
/// - TWO hands gripping resize it (spread = grow, pinch = shrink; clamped 0.5×–2×)
///   while also moving (and, with yaw carry, heading-yawing) with the pair midpoint.
/// - On final release the owner persists the pose
///   (<see cref="IPanelGrabOwner.OnGrabFinished"/>) so the layout survives sessions.
///
/// Arbitration: this is a plain registered <see cref="IGrabbable"/> — the P2
/// <see cref="ProximityGrabber"/> only highlights it within palm reach of the handle
/// collider, and <c>Rig.WorldGrab</c> yields any grip that starts on a highlighted or
/// held grabbable (its documented grip-contention rule). So the panel grab wins exactly
/// when the grip starts in its grab zone, and never fights the world grab.
/// No re-parenting, no physics; per-frame math only, zero allocations.
/// </summary>
internal sealed class PanelGrabHandle : MonoBehaviour, IGrabbable, IGrabHighlight
{
    private const float MinHandDistance = 0.03f;  // world units (already diorama-scaled)
    private const float Smoothing = 18f;          // 1/s exponential
    // Item 4: two-hand resize floor. Lowered from 0.5 so grabbable panels (the VR options
    // panel + floated menu windows especially) can be pinched MUCH smaller — the user could
    // not shrink them enough. internal so the panel owners (GrabbableModal, SettingsPanel)
    // reuse the SAME range for their own per-frame factor clamps (single source of truth);
    // clamping to a higher per-panel min would silently re-cap what this handle just shrank.
    // Applies to every PanelGrabHandle user (tray, combat log, modals, settings) — the user
    // wants to shrink windows freely.
    internal const float MinScale = 0.15f;
    internal const float MaxScale = 2f;

    private IPanelGrabOwner? _owner;
    private MeshRenderer? _bar;
    private Color _barBaseColor;
    private string _logChannel = "WorldUI";
    private string _logName = "Panel";

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

    /// <summary><paramref name="logName"/>/<paramref name="logChannel"/> keep the owner's log identity ("Tray grab: …" etc.).</summary>
    internal void Init(IPanelGrabOwner owner, MeshRenderer bar, string logChannel, string logName)
    {
        _owner = owner;
        _bar = bar;
        _barBaseColor = bar.sharedMaterial != null ? bar.sharedMaterial.color : Color.white;
        _logChannel = logChannel;
        _logName = logName;
    }

    /// <summary>True while at least one hand grips the handle (owners skip their own pose writes).</summary>
    internal bool IsGrabbed => _handA != null;

    // ------------------------------------------------------------------ IGrabbable --

    public bool CanGrab => _owner != null && _owner.GrabVisible && (_handA == null || _handB == null);

    /// <summary>Boards/world panels are always grip-grabbed (test #27), ignoring [Cards] GrabButton.</summary>
    public bool GrabWithGrip => true;

    public void OnGrab(VRHand hand)
    {
        if (_handA == null)
            _handA = hand;
        else if (_handB == null && hand != _handA)
            _handB = hand;
        else
            return;
        ReAnchor();
        VRLog.Info(_logChannel, $"{_logName} grab: engaged ({hand.Side}, {(_handB != null ? "two-hand resize" : "one-hand move")}).");
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
            VRLog.Info(_logChannel, $"{_logName} grab: {hand.Side} released — continuing one-hand.");
        }
        else
        {
            VRLog.Info(_logChannel, $"{_logName} grab: released ({hand.Side}).");
            _owner?.OnGrabFinished();
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
        Transform? root = _owner?.GrabRoot;
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
        bool carryYaw = _owner!.GrabCarriesYaw;

        if (_handB == null)
        {
            // One hand: rigid carry (yaw-only spin when the owner lets us rotate).
            Vector3 palm = _handA.Rig.PalmCenter.position;
            Quaternion spin = Quaternion.identity;
            if (carryYaw)
            {
                float dYaw = Mathf.DeltaAngle(_anchorHeading, HandHeading(_handA));
                spin = Quaternion.Euler(0f, dYaw, 0f);
            }
            Vector3 targetPos = palm + spin * (_rootPos0 - _anchorPos);
            root.position = Vector3.Lerp(root.position, targetPos, k);
            if (carryYaw)
                root.rotation = Quaternion.Slerp(root.rotation, spin * _rootRot0, k);
        }
        else
        {
            // Two hands: midpoint carry + pinch scale (0.5×–2×) + optional pair-heading yaw.
            Vector3 pA = _handA.Rig.PalmCenter.position;
            Vector3 pB = _handB.Rig.PalmCenter.position;
            Vector3 mid = (pA + pB) * 0.5f;
            float d = Mathf.Max(Vector3.Distance(pA, pB), MinHandDistance);
            Quaternion spin = Quaternion.identity;
            if (carryYaw)
            {
                float dYaw = Mathf.DeltaAngle(_anchorHeading, HeadingDegrees(pB - pA));
                spin = Quaternion.Euler(0f, dYaw, 0f);
            }

            float targetScale = Mathf.Clamp(_rootScale0 * (d / _anchorDistance), MinScale, MaxScale);
            float newScale = Mathf.Lerp(root.localScale.x, targetScale, k);
            float ratio = newScale / _rootScale0;

            Vector3 targetPos = mid + spin * ((_rootPos0 - _anchorPos) * ratio);

            root.localScale = Vector3.one * newScale;
            root.position = Vector3.Lerp(root.position, targetPos, k);
            if (carryYaw)
                root.rotation = Quaternion.Slerp(root.rotation, spin * _rootRot0, k);
        }
    }

    // ------------------------------------------------------------------ helpers --

    private void ReAnchor()
    {
        Transform? root = _owner?.GrabRoot;
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
