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
    /// How the carry may ROTATE the root (item 12 — the tray's movement schemes; every other
    /// owner keeps its historic behavior: <see cref="PanelCarryMode.Level"/> replaces the old
    /// <c>GrabCarriesYaw == true</c>, <see cref="PanelCarryMode.Slide"/> the old <c>false</c>).
    /// Read per-frame, so a settings change applies to the very next carry frame.
    /// </summary>
    PanelCarryMode CarryMode { get; }

    /// <summary>
    /// The owner's LEVEL frame: the world rotation whose up axis the Level / LevelPitch carries
    /// keep the root level against. Identity = plain world level — what EVERY current owner
    /// returns, including the tray since the user decoupled the board from the world tilt
    /// (decision 2026-08, supersedes item 11; the tray briefly returned the rig's
    /// <c>WorldTiltRotation</c> here). The seam stays: a future owner that must read as level
    /// in some other frame plugs it in here and the whole carry/persist pipeline follows.
    /// </summary>
    Quaternion GrabLevelFrame { get; }

    /// <summary>
    /// ABSOLUTE pitch window for <see cref="PanelCarryMode.LevelPitch"/>, degrees of level-frame
    /// pitch (x/min, y/max as extracted by <see cref="LevelPose.Decompose"/>). Ignored by every
    /// other mode — return something permissive like (-180, 180).
    /// </summary>
    Vector2 GrabPitchLimits { get; }

    /// <summary>
    /// ABSOLUTE localScale window (x/min, y/max) the two-hand resize may write, applied AFTER
    /// (and overriding) the handle's generic factor range — read LIVE each resize frame.
    ///
    /// <para>WHY THE OWNER MUST SAY THIS (user report 2026-08-04: "mit der Skalierung des
    /// 'Folgen'-Modus kann man über die Grenzen schieben"): the handle's own
    /// [<see cref="PanelGrabHandle.MinScale"/>, <see cref="PanelGrabHandle.MaxScale"/>] clamp
    /// bounds a raw FACTOR, but what the tray's size ruling limits is the APPARENT width in
    /// perceived centimetres — and the mapping between the two moves with the world zoom
    /// (rig scale vs. the tray's parent-chain scale, see PlayTray.ClampApparentSize). The tray's
    /// release-time safety clamp is deliberately skipped while a hand grips the bar ("never
    /// touch it mid-carry"), so the pinch itself was the one scale writer with no apparent-size
    /// bound: at a zoomed-out rig one factor unit is metres of apparent width, and the gesture
    /// sailed visibly past min/max for as long as it was held. Bounding the TARGET here clamps
    /// exactly the player's own gesture, live, in every anchor mode — the board never moves or
    /// resizes on its own (standing ruling), it simply stops following the pinch at the limit.
    /// Owners without an apparent-size ruling return the handle's generic range verbatim.</para>
    /// </summary>
    Vector2 GrabScaleLimits { get; }

    /// <summary>The LAST gripping hand let go — persist the layout.</summary>
    void OnGrabFinished();
}

/// <summary>
/// What a <see cref="PanelGrabHandle"/> carry may do to the owner root's ROTATION (item 12).
/// Position + two-hand resize behave identically in every mode.
/// </summary>
internal enum PanelCarryMode
{
    /// <summary>Position + scale only — the owner keeps authoring the rotation (old <c>GrabCarriesYaw=false</c>).</summary>
    Slide,

    /// <summary>Yaw about the owner's level-frame up only; the root stays level (old <c>GrabCarriesYaw=true</c>,
    /// the tray's "Begrenzt"). The rotation is REBUILT from heading+pitch each frame, so no
    /// combination of grabs can ever roll or flip the root.</summary>
    Level,

    /// <summary>Like <see cref="Level"/>, plus wrist pitch tilts the root inside the owner's
    /// <see cref="IPanelGrabOwner.GrabPitchLimits"/> window (the tray's "Begrenzt mit Neigung").</summary>
    LevelPitch,

    /// <summary>Full 1:1 hand rotation, no leveling, no clamps (the tray's "Frei").</summary>
    Free,
}

/// <summary>
/// Heading/pitch decomposition in a LEVEL frame — the shared math of the Level/LevelPitch
/// carries (PanelGrabHandle) and the tray's pose persistence (PlayTray.PersistPoseToConfig).
/// A "level rotation" here is a world rotation already expressed IN the owner's level frame
/// (<c>inverse(GrabLevelFrame) * worldRotation</c>): heading is its twist about +Y, pitch the
/// remaining rotation about the heading-local +X. Compose(Decompose(r)) drops any ROLL — that
/// loss is the point: it is the sanitizer that makes an upside-down Level-mode pose unreachable.
/// </summary>
internal static class LevelPose
{
    /// <summary>Signed twist of <paramref name="rotation"/> about an arbitrary unit
    /// <paramref name="axis"/>, degrees in (-180, 180] — the swing-twist projection
    /// (PanelGrabHandle's old world-up-only TwistYawDegrees, generalized).</summary>
    internal static float TwistDegrees(Quaternion rotation, Vector3 axis)
    {
        float d = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
        float w = rotation.w;
        float mag = Mathf.Sqrt(d * d + w * w);
        if (mag < 1e-6f)
            return 0f; // pure 180° swing about an orthogonal axis — no usable twist
        float twist = 2f * Mathf.Atan2(d / mag, w / mag) * Mathf.Rad2Deg;
        if (twist > 180f) twist -= 360f;
        else if (twist < -180f) twist += 360f;
        return twist;
    }

    /// <summary>Extract heading (twist about +Y) and pitch (about the heading-local +X) from a
    /// level-frame rotation. Any roll the rotation carries is discarded.</summary>
    internal static void Decompose(Quaternion levelRotation, out float headingDeg, out float pitchDeg)
    {
        headingDeg = TwistDegrees(levelRotation, Vector3.up);
        // Undo the heading, then read the pitch off the residual's forward: for a canonical
        // yaw∘pitch pose the residual is a pure X rotation and forward = (0, -sin p, cos p).
        Quaternion residual = Quaternion.AngleAxis(-headingDeg, Vector3.up) * levelRotation;
        Vector3 f = residual * Vector3.forward;
        pitchDeg = Mathf.Atan2(-f.y, f.z) * Mathf.Rad2Deg;
    }

    /// <summary>The canonical level-frame rotation for a heading + pitch (roll-free by construction).</summary>
    internal static Quaternion Compose(float headingDeg, float pitchDeg) =>
        Quaternion.AngleAxis(headingDeg, Vector3.up) * Quaternion.Euler(pitchDeg, 0f, 0f);
}

/// <summary>
/// Grip-grab handle core (test #14 "Controllboard", generalized in test #19 so any
/// panel can be moved/scaled/persisted exactly like the control board):
///
/// - ONE hand gripping the handle bar carries the owner root: position follows the
///   palm; the rotation follows the owner's <see cref="IPanelGrabOwner.CarryMode"/> —
///   Level rebuilds heading+pitch in the owner's level frame each frame (the root can
///   never end up rolled/upside down), LevelPitch adds a clamped wrist pitch, Free
///   rides the wrist 1:1, Slide leaves the rotation to the owner.
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
    // not shrink them enough. internal so the panel owners (GrabbableModal, Cards.PlayTray)
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
    /// Null (tray / combat log, which never set it) keeps the old behavior:
    /// the laser tests the registered collider.
    /// </summary>
    internal Collider? BarCollider { get; private set; }

    /// <summary>Split the laser target off the palm zone: the ray grabs ONLY this bar strip.</summary>
    internal void SetBarCollider(Collider bar) => BarCollider = bar;

    /// <summary>
    /// HOW THE CURRENT (or most recent) CARRY WAS STARTED: true = the hand LASER, false = a direct
    /// palm grab. Survives the release, so the owner can still read it in
    /// <see cref="IPanelGrabOwner.OnGrabFinished"/>.
    ///
    /// <para><b>USER REQUEST 8 (2026-08-22, verbatim):</b> "Das automatische Drehen zum Spieler soll
    /// einstellbar sein: Default soll sein, dass es nur sich dreht, wenn es mit dem Laser gegriffen
    /// wurde, beim Greifen nicht. Aber beides oder gar nicht soll auch eine mögliche Einstellung
    /// sein." The distinction is HOW IT WAS GRABBED, so the grab has to record its own modality —
    /// which is what this is.</para>
    ///
    /// <para><b>WHY IT IS NOT <see cref="_laserCarry"/>.</b> That field is a live carry MODE, not a
    /// record of the gesture: a second (palm) hand joining clears it (two-hand resize is a pure palm
    /// gesture), and <see cref="OnRelease"/> clears it BEFORE the owner's
    /// <see cref="IPanelGrabOwner.OnGrabFinished"/> runs. It is therefore false at exactly the
    /// moment the question is asked. This flag is latched ONCE, when the FIRST hand engages, and is
    /// never re-written mid-carry.</para>
    ///
    /// <para><b>IT COMES FROM THE GRABBER'S OWN IDENTITY AND IS NEVER GUESSED FROM DISTANCE.</b>
    /// <c>RayGrabDriver</c> calls <see cref="BeginLaserCarry"/> IMMEDIATELY before
    /// <c>ProximityGrabber.ForceGrab</c> (RayGrabDriver.cs, the TriggerDown branch), so by the time
    /// <see cref="OnGrab"/> runs for a ray grab <see cref="_laserCarry"/> is already true; a palm
    /// grab reaches <see cref="OnGrab"/> through <c>ProximityGrabber</c>'s own candidate scan with
    /// it false. A distance threshold would have been a guess, and a wrong one for exactly the case
    /// the setting is about (a laser grab at arm's length reads as "near").</para>
    ///
    /// <para>MIXED GESTURES KEEP THE MODALITY THEY STARTED WITH — a palm hand joining a laser carry
    /// does not flip the answer under the player, and neither does the laser joining a palm carry
    /// (that second case cannot even arise: <c>RayGrabDriver</c> only fires while
    /// <c>_hand.Grabber.Held == null</c>). Which one "started it" is the only reading that is stable
    /// for the whole carry, and the release is judged on the gesture as a whole.</para>
    /// </summary>
    internal bool LastGrabWasLaser { get; private set; }

    private IPanelGrabOwner? _owner;
    private MeshRenderer? _bar;
    private Color _barBaseColor;

    /// <summary>
    /// True while at least one hand has this handle HIGHLIGHTED (hover, and — on the palm path —
    /// for the duration of the hold as well). Tracked only so <see cref="SetBarBaseColor"/> can
    /// stay out of the highlight's way: the highlight owns the material colour while it is lit,
    /// and the base colour is what it falls back to when it goes out.
    /// </summary>
    private bool _barHighlighted;

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
    private float _anchorHeading;      // pair heading at engage (two-hand), LEVEL-frame deg
    private Quaternion _anchorHandRot = Quaternion.identity; // hand rotation at engage (one-hand)
    private float _anchorDistance;     // palm distance at engage (two-hand)
    private Vector3 _rootPos0;
    private Quaternion _rootRot0 = Quaternion.identity;
    private float _rootScale0 = 1f;
    // Level/LevelPitch anchors (item 12): the root's heading+pitch in the owner's level frame at
    // engage. The carry REBUILDS the rotation from these instead of composing deltas onto the raw
    // captured rotation, so accumulated roll can never survive a Level-mode grab.
    private float _anchorRootHeading;
    private float _anchorRootPitch;

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

    /// <summary>Boards/world panels are always grip-grabbed (test #27) — the [Cards] GrabButton
    /// dial that once switched this is retired.</summary>
    public bool GrabWithGrip => true;

    public void OnGrab(VRHand hand)
    {
        if (_handA == null)
        {
            _handA = hand;
            // GESTURE START — latch how it was started (see LastGrabWasLaser). RayGrabDriver armed
            // _laserCarry one statement before the ForceGrab that lands here, so this reads the
            // grabber's own identity rather than guessing from how far away the hand is.
            LastGrabWasLaser = _laserCarry;
        }
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
        _barHighlighted = highlighted;
        if (_bar != null && _bar.sharedMaterial != null)
            _bar.sharedMaterial.color = highlighted ? new Color(0.95f, 0.8f, 0.4f) : _barBaseColor;
    }

    /// <summary>
    /// Re-point the bar's RESTING colour — the colour the hover/held highlight falls back to when
    /// it goes out. Used by <see cref="GrabbableModal"/> to paint a SHARED window's bar blue
    /// (see that class's SHARED-WINDOW BAR COLOUR block for the user request and the whole design).
    ///
    /// <para>THIS COMPOSES WITH THE HIGHLIGHT INSTEAD OF FIGHTING IT, and that is the entire reason
    /// the method exists rather than a caller writing <c>sharedMaterial.color</c> directly. The
    /// highlight is a state machine with exactly one writer (<see cref="OnGrabHighlight"/>) and it
    /// remembers nothing — it re-derives the colour from <c>_barBaseColor</c> every time it goes
    /// out. A caller that wrote the material while a hand hovered would be overwritten by the very
    /// next un-highlight; a caller that wrote <c>_barBaseColor</c> alone would not repaint a bar
    /// nobody is touching. So: always update the base, and touch the material ONLY while the
    /// highlight is not lit. A blue bar therefore still turns gold under the hand, and turns back
    /// to BLUE (not brass) when the hand leaves — because the highlight reads the base it was
    /// given, which is the one place this fact is stored.</para>
    ///
    /// <para>The material is PER BAR (<c>WorldUIAssets.CreateFlatMaterial</c> constructs a new
    /// <see cref="Material"/> per call), so this write can only ever tint the one window it was
    /// called for.</para>
    /// </summary>
    internal void SetBarBaseColor(Color color)
    {
        _barBaseColor = color;
        if (!_barHighlighted && _bar != null && _bar.sharedMaterial != null)
            _bar.sharedMaterial.color = color;
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
        // owner keeps authoring the window's rotation (GrabbableModal's Level carry mode would
        // otherwise fight a second rotation writer).
        if (_laserCarry && _handB == null)
        {
            _handA.GetAimRay(out Vector3 rayOrigin, out Vector3 rayDir);
            Vector3 laserTarget = rayOrigin + rayDir * _carryDistance + _carryOffset;
            root.position = Vector3.Lerp(root.position, laserTarget, k);
            return;
        }

        PanelCarryMode mode = _owner!.CarryMode;
        // The owner's level frame, read LIVE each frame: the Level/LevelPitch carries yaw about
        // ITS up axis, never bare world up. Every current owner returns identity (the tray was
        // decoupled from the world tilt, user decision 2026-08), but the seam stays — see
        // IPanelGrabOwner.GrabLevelFrame.
        Quaternion frame = _owner.GrabLevelFrame;
        Vector3 up = frame * Vector3.up;

        if (_handB == null)
        {
            // One hand: rigid carry.
            //
            // Cards task #4 fix (vertical move rotated the board): the yaw used to be
            // derived from the HORIZONTAL PROJECTION of the hand's forward
            // (HandHeading). Raising or lowering the arm PITCHES the controller, and
            // as forward approaches vertical its horizontal projection shrinks — tiny
            // wrist noise then swings the projected heading by tens of degrees, so a
            // purely vertical carry spun the board although the wrist never yawed.
            // The yaw is now the TWIST of the actual wrist rotation delta about the
            // level-frame up (swing-twist decomposition): pure pitch/roll contributes
            // exactly zero, a deliberate wrist yaw still turns the board 1:1.
            Vector3 palm = _handA.Rig.PalmCenter.position;
            Quaternion handDelta = _handA.transform.rotation * Quaternion.Inverse(_anchorHandRot);

            if (mode == PanelCarryMode.Free)
            {
                // Item 12 "Frei": the root rides the wrist 1:1 in ALL axes — position orbits the
                // palm with the full rotation delta, exactly as if bolted to the hand.
                Vector3 freePos = palm + handDelta * (_rootPos0 - _anchorPos);
                root.position = Vector3.Lerp(root.position, freePos, k);
                root.rotation = Quaternion.Slerp(root.rotation, handDelta * _rootRot0, k);
                return;
            }

            float dYaw = mode == PanelCarryMode.Slide ? 0f : LevelPose.TwistDegrees(handDelta, up);
            Quaternion spin = Quaternion.AngleAxis(dYaw, up);
            float pitch = _anchorRootPitch;
            if (mode == PanelCarryMode.LevelPitch)
            {
                // Item 12 "Begrenzt mit Neigung": wrist pitch (twist about the root's own
                // level-frame right axis, post-yaw) tilts the root — clamped ABSOLUTELY to
                // the owner's window, so repeated grabs can never walk past it.
                Vector3 pitchAxis = frame * (Quaternion.AngleAxis(_anchorRootHeading + dYaw, Vector3.up) * Vector3.right);
                float dPitch = LevelPose.TwistDegrees(handDelta, pitchAxis);
                Vector2 limits = _owner.GrabPitchLimits;
                pitch = Mathf.Clamp(_anchorRootPitch + dPitch, limits.x, limits.y);
                // Hand-anchored pitch (user bug: the bar climbed above/below the gripping hand):
                // the palm-relative offset must orbit the palm by the FULL level-frame rotation
                // delta — yaw AND the pitch actually written below — not the yaw alone, or the
                // pitch part rotates the root in place about its own pivot and the edge-mounted
                // bar arcs away from the palm. delta = [frame∘Compose(h0+dYaw,p)] ∘
                // [frame∘Compose(h0,p0)]⁻¹, i.e. anchor level pose → target level pose, matching
                // the rotation target exactly. Using the CLAMPED pitch keeps hand and bar
                // consistent at the limits too: when the clamp freezes the board, the bar
                // freezes with it. With p == p0 the pitch terms cancel — Compose(h0+dYaw,p) =
                // Yaw(dYaw)∘Compose(h0,p) — leaving frame∘Yaw(dYaw)∘frame⁻¹ =
                // AngleAxis(dYaw, frame·up): exactly the Level/Slide spin above, which is why
                // those modes keep the untouched plain-yaw path (identical behavior; Free
                // already orbits the palm with the full handDelta — this matches it per-axis).
                spin = frame * LevelPose.Compose(_anchorRootHeading + dYaw, pitch)
                     * Quaternion.Inverse(frame * LevelPose.Compose(_anchorRootHeading, _anchorRootPitch));
            }
            Vector3 targetPos = palm + spin * (_rootPos0 - _anchorPos);
            root.position = Vector3.Lerp(root.position, targetPos, k);
            if (mode != PanelCarryMode.Slide)
            {
                // REBUILD the rotation from heading+pitch (roll-free by construction) instead of
                // composing the delta onto the captured rotation — the structural guarantee that
                // Level-mode grabbing can never end upside down, tilt or no tilt.
                Quaternion target = frame * LevelPose.Compose(_anchorRootHeading + dYaw, pitch);
                root.rotation = Quaternion.Slerp(root.rotation, target, k);
            }
        }
        else
        {
            // Two hands: midpoint carry + pinch scale ([MinScale, MaxScale] = 0.15×–2×) +
            // pair-heading yaw (all modes but Slide; Free keeps its full orientation and only
            // yaws with the pair — the resize gesture stays predictable).
            Vector3 pA = _handA.Rig.PalmCenter.position;
            Vector3 pB = _handB.Rig.PalmCenter.position;
            Vector3 mid = (pA + pB) * 0.5f;
            float d = Mathf.Max(Vector3.Distance(pA, pB), MinHandDistance);
            float dYaw = 0f;
            if (mode != PanelCarryMode.Slide)
                dYaw = Mathf.DeltaAngle(_anchorHeading, HeadingDegrees(Quaternion.Inverse(frame) * (pB - pA)));
            Quaternion spin = Quaternion.AngleAxis(dYaw, up);

            float targetScale = Mathf.Clamp(_rootScale0 * (d / _anchorDistance), MinScale, MaxScale);
            // Owner's ABSOLUTE window on top of the generic factor range (and overriding it —
            // the tray's apparent-size limits are a user ruling, the factor range is only
            // gesture semantics). See IPanelGrabOwner.GrabScaleLimits for the full root cause;
            // the lerp below then converges monotonically toward the clamped target, so the
            // write can never overshoot a limit the target respects.
            Vector2 scaleLimits = _owner.GrabScaleLimits;
            targetScale = Mathf.Clamp(targetScale, scaleLimits.x, scaleLimits.y);
            float newScale = Mathf.Lerp(root.localScale.x, targetScale, k);
            float ratio = newScale / _rootScale0;

            Vector3 targetPos = mid + spin * ((_rootPos0 - _anchorPos) * ratio);

            root.localScale = Vector3.one * newScale;
            root.position = Vector3.Lerp(root.position, targetPos, k);
            if (mode == PanelCarryMode.Free)
                root.rotation = Quaternion.Slerp(root.rotation, spin * _rootRot0, k);
            else if (mode != PanelCarryMode.Slide)
                root.rotation = Quaternion.Slerp(root.rotation,
                    frame * LevelPose.Compose(_anchorRootHeading + dYaw, _anchorRootPitch), k);
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
        // Level/LevelPitch anchors: the root's heading+pitch in the owner's CURRENT level frame.
        // Decompose∘Compose drops any roll the pose may carry (a Free-mode leftover, or damage
        // from before the item-11 axis fix), so the first Level-mode grab self-heals it. The
        // pitch is additionally pulled into the window for the clamped mode — an out-of-window
        // pose can then never be "kept" by grabbing it.
        Quaternion frame = _owner!.GrabLevelFrame;
        LevelPose.Decompose(Quaternion.Inverse(frame) * _rootRot0, out _anchorRootHeading, out _anchorRootPitch);
        if (_owner.CarryMode == PanelCarryMode.LevelPitch)
        {
            Vector2 limits = _owner.GrabPitchLimits;
            _anchorRootPitch = Mathf.Clamp(_anchorRootPitch, limits.x, limits.y);
        }
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
            _anchorHeading = HeadingDegrees(Quaternion.Inverse(frame) * (pB - pA)); // level-frame pair heading
        }
    }

    private static float HeadingDegrees(Vector3 dir) =>
        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
}
