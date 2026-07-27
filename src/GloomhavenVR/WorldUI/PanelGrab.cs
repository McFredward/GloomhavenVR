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
/// - TWO hands gripping resize it (spread = grow, pinch = shrink; clamped to
///   [<see cref="PanelGrabHandle.MinScale"/> = 0.15×, <see cref="PanelGrabHandle.MaxScale"/> = 2×)
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

    /// <summary>
    /// LOST-MENU FIX (laser swallows board clicks): optional NARROW collider matching the
    /// VISIBLE drag-bar strip only. When set, the far ray (<see cref="RayGrabDriver"/>) tests
    /// EXCLUSIVELY this collider — never the wider registered grab ZONE — so a trigger aimed
    /// past the window at the board/cards no longer starts a laser-carry of the window. The
    /// registered zone collider stays as-is for the near-hand palm grab
    /// (<see cref="ProximityGrabber"/> highlight/grip range), which is deliberately generous.
    /// Null (tray / combat log / settings panel, which never set it) keeps the old behavior:
    /// the laser tests the registered collider.
    /// </summary>
    internal Collider? BarCollider { get; private set; }

    /// <summary>Split the laser target off the palm zone: the ray grabs ONLY this bar strip.</summary>
    internal void SetBarCollider(Collider bar) => BarCollider = bar;

    private IPanelGrabOwner? _owner;
    private MeshRenderer? _bar;
    private Color _barBaseColor;
    private string _logChannel = "WorldUI";
    private string _logName = "Panel";

    private VRHand? _handA;
    private VRHand? _handB;

    // Laser-carry (feature #8): while grabbed by the hand LASER (not palm proximity) the
    // window must STAY at range and slide ALONG the aim ray instead of teleporting to the
    // palm. Captured once on grab: the along-ray distance and the world offset between the
    // window root and the ray hit point (preserves where the beam struck the bar). One-hand
    // only — a second (palm) hand joining clears it and hands back to the normal pair carry.
    private bool _laserCarry;
    private float _carryDistance;
    private Vector3 _carryOffset;

    // Gesture anchors (captured on every hand-count change).
    private Vector3 _anchorPos;        // palm (one-hand) or midpoint (two-hand) at engage
    private float _anchorHeading;      // pair heading at engage (two-hand), deg
    private Quaternion _anchorHandRot = Quaternion.identity; // hand rotation at engage (one-hand)
    private float _anchorDistance;     // palm distance at engage (two-hand)
    private Vector3 _rootPos0;
    private Quaternion _rootRot0 = Quaternion.identity;
    private float _rootScale0 = 1f;

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
        {
            _handB = hand;
            // A second (palm) hand joining ends laser-carry: two-hand resize is a pure
            // palm gesture (both midpoints), so hand it back to the normal pair carry.
            _laserCarry = false;
        }
        else
            return;
        ReAnchor();
        VRLog.Info(_logChannel, $"{_logName} grab: engaged ({hand.Side}, {(_handB != null ? "two-hand resize" : "one-hand move")}).");
    }

    /// <summary>
    /// Feature #8: arm LASER-CARRY for the NEXT grab (the ray driver calls this immediately
    /// before <see cref="ProximityGrabber.ForceGrab"/>). Captures the along-ray distance and
    /// the world offset from the ray hit point to the window root, so the carry keeps the
    /// window at range and preserves where the beam struck the bar (translate only — the
    /// window's orientation is left to the owner). No-op path: <see cref="CancelLaserCarry"/>
    /// if the ForceGrab is refused.
    /// </summary>
    internal void BeginLaserCarry(VRHand hand, float distance, Vector3 hitPoint)
    {
        Transform? root = _owner?.GrabRoot;
        if (hand == null || root == null)
            return;
        _laserCarry = true;
        _carryDistance = distance;
        _carryOffset = root.position - hitPoint;
        VRLog.Info(_logChannel, $"{_logName} grab: LASER-CARRY armed ({hand.Side}, {distance / Mathf.Max(hand.WorldScale, 1e-4f):F2} m).");
    }

    /// <summary>Disarm a laser-carry that never took (ForceGrab refused).</summary>
    internal void CancelLaserCarry() => _laserCarry = false;

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
            bool wasLaser = _laserCarry;
            _laserCarry = false;
            VRLog.Info(_logChannel, $"{_logName} grab: released ({hand.Side}{(wasLaser ? ", laser-carry" : "")}).");
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
        _laserCarry = false;
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
        // GRAB STATE heal (user bug A, structural): a slot must only stay engaged while
        // that hand's grabber still holds THIS handle — the grabber sets Held BEFORE
        // OnGrab, so during any legitimate hold the check is always true. A missed
        // release (grabber healed a dead hold, hot reload, mode teardown ordering) would
        // otherwise latch a stale slot: the phantom hand keeps carrying the window, and
        // with BOTH slots stale CanGrab stays false forever (bar refuses every grab).
        if (_handA != null && !ReferenceEquals(_handA.Grabber.Held, this))
        {
            VRLog.Warn(_logChannel, $"GRAB STATE heal: {_logName} dropped stale grip slot ({_handA.Side} no longer holds the bar).");
            _handA = _handB;
            _handB = null;
            _laserCarry = false;
            if (_handA != null) ReAnchor();
            else _owner?.OnGrabFinished();
        }
        if (_handB != null && !ReferenceEquals(_handB.Grabber.Held, this))
        {
            VRLog.Warn(_logChannel, $"GRAB STATE heal: {_logName} dropped stale grip slot ({_handB.Side} no longer holds the bar).");
            _handB = null;
            ReAnchor();
        }
        if (_handA == null)
            return;

        float k = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);

        // Feature #8: LASER-CARRY (one hand, grabbed via the ray). The window slides ALONG
        // the live aim ray at its captured distance instead of snapping to the palm; the
        // captured offset preserves where the beam struck the bar. Translate only — the
        // owner keeps authoring the window's rotation (GrabbableModal.GrabCarriesYaw would
        // otherwise fight a second rotation writer).
        if (_laserCarry && _handB == null)
        {
            _handA.GetAimRay(out Vector3 rayOrigin, out Vector3 rayDir);
            Vector3 laserTarget = rayOrigin + rayDir * _carryDistance + _carryOffset;
            root.position = Vector3.Lerp(root.position, laserTarget, k);
            return;
        }

        bool carryYaw = _owner!.GrabCarriesYaw;

        if (_handB == null)
        {
            // One hand: rigid carry (yaw-only spin when the owner lets us rotate).
            //
            // Cards task #4 fix (vertical move rotated the board): the yaw used to be
            // derived from the HORIZONTAL PROJECTION of the hand's forward
            // (HandHeading). Raising or lowering the arm PITCHES the controller, and
            // as forward approaches vertical its horizontal projection shrinks — tiny
            // wrist noise then swings the projected heading by tens of degrees, so a
            // purely vertical carry spun the board although the wrist never yawed.
            // The yaw is now the TWIST of the actual wrist rotation delta about world
            // up (swing-twist decomposition): pure pitch/roll contributes exactly
            // zero, a deliberate wrist yaw still turns the board 1:1.
            Vector3 palm = _handA.Rig.PalmCenter.position;
            Quaternion spin = Quaternion.identity;
            if (carryYaw)
            {
                float dYaw = TwistYawDegrees(_handA.transform.rotation * Quaternion.Inverse(_anchorHandRot));
                spin = Quaternion.Euler(0f, dYaw, 0f);
            }
            Vector3 targetPos = palm + spin * (_rootPos0 - _anchorPos);
            root.position = Vector3.Lerp(root.position, targetPos, k);
            if (carryYaw)
                root.rotation = Quaternion.Slerp(root.rotation, spin * _rootRot0, k);
        }
        else
        {
            // Two hands: midpoint carry + pinch scale ([MinScale, MaxScale] = 0.15×–2×) +
            // optional pair-heading yaw.
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
            _anchorHandRot = _handA.transform.rotation; // wrist-twist yaw reference (task #4)
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

    /// <summary>
    /// The TWIST (yaw) component of a rotation delta about world up, in degrees
    /// (-180..180) — swing-twist decomposition: project the quaternion's vector part
    /// onto the up axis and renormalize. Pure pitch/roll deltas return 0, so a
    /// vertically carried board no longer picks up phantom yaw (cards task #4).
    /// </summary>
    private static float TwistYawDegrees(Quaternion delta)
    {
        float y = delta.y; // dot(vector part, world up)
        float w = delta.w;
        float mag = Mathf.Sqrt(y * y + w * w);
        if (mag < 1e-6f)
            return 0f; // pure 180° swing about a horizontal axis — no usable twist
        float yaw = 2f * Mathf.Atan2(y / mag, w / mag) * Mathf.Rad2Deg;
        if (yaw > 180f) yaw -= 360f;
        else if (yaw < -180f) yaw += 360f;
        return yaw;
    }

    private static float HeadingDegrees(Vector3 dir) =>
        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
}
