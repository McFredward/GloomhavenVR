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

    // ---- THE REEL (user request 2026-08-23) -------------------------------------------------
    //
    // VERBATIM: "Ich hatte vor ein paar Runden auch darum gebeten, dass man die Fenster die man mit
    // dem Laser festhält mit dem Controller zu einem ziehen kann (als Option an/ausschaltbar). Ich
    // finde diese Option nicht. Falls sie noch nicht existiert, implementiere sie. Wenn hoch/runter
    // mit dem joystick auch aktiviert ist dann overruled das ranziehen diese Option so lange man
    // ein Fenster mit dem Laser festhält."
    //
    // NOTHING NEW IS CARRIED HERE. The laser carry above already holds the window at a captured
    // along-ray distance; the reel is a DIAL ON THAT ONE NUMBER and nothing else. It writes
    // _carryDistance and never touches the position, the rotation, the scale or the offset — so
    // every property the carry already guarantees (translate only, the owner keeps authoring the
    // rotation, the beam-strike point is preserved) survives untouched, and a window a peer sees
    // moves for exactly the reason it already moved.
    //
    // THE UNIT IS THE PERCEIVED METRE, converted at USE time through the live rig scale
    // (VRHand.WorldScale — "Diorama scale at this hand … Multiply 'real meters' by this"). A dial
    // written in world units would mean something different in a scenario (~198x) than in the map
    // room (~4.4x) — a factor of 45 — and this project has shipped exactly that bug before (the
    // aim laser drawn 0.15 mm wide because a "…Meters" bound was clamped against a world-unit
    // product). The clamp WINDOW is likewise stored in perceived metres and multiplied by the live
    // scale each frame, so a mid-carry world re-scale moves the bounds with the picture.
    //
    // MULTIPLAYER: NOTHING NEW GOES ON THE WIRE, and this is a FINDING rather than a decision to
    // send less. The two cases were checked and only one of them is real:
    //
    //   * A PRIVATE window's distance is local presentation — no peer has an opinion about it and
    //     none is told.
    //   * A SHARED window (SharedWindows.IsShared — the blue-barred ones whose pose is synced 1:1)
    //     IS movable by the reel, and its pose ALREADY TRAVELS, by the very path a laser drag
    //     already uses. GrabbableModal's IPanelGrabOwner.GrabRoot is its `_frame`
    //     (GrabbableModal.cs:249), the laser carry writes `root.position`, and the reel writes only
    //     `_carryDistance`, which that same write consumes one line later. Net/RemoteStorySync's
    //     TrackFrame (RemoteStorySync.cs:399) and Net/RemoteMapStory's watch THAT TRANSFORM drift
    //     from a baseline and publish the settled pose (records 19 / 21) in seat-anchor-local real
    //     metres; TrackFrame's own doc names its three legitimate writers and the grab handle is
    //     the first of them. So a reeled shared window is, on the wire, indistinguishable from one
    //     dragged by sweeping the arm — which is the requirement. Opening a second channel for the
    //     distance would be a second source of truth for one position, and the two could only ever
    //     disagree.
    //
    // AND IT STILL CANNOT TURN TO A PLAYER. The reel is translation along the aim ray, exactly like
    // the carry it drives: it never writes root.rotation, so the pose that gets published carries a
    // new position and a bit-identical rotation. The one path that re-faces a window on release
    // (GrabbableModal.OnGrabFinished) is gated as it was and is not touched here.
    private float _reelMinMeters;      // perceived-metre floor for _carryDistance/scale
    private float _reelMaxMeters;      // perceived-metre ceiling
    private float _reelFloorMeters;    // the DERIVED near bound, before the engage-distance widening
    private float _reelCeilingMeters;  // the DERIVED far bound, before the engage-distance widening
    private float _reelStartMeters;    // engage distance, for the release travel readout
    private float _reelReachMeters;    // measured panel geometry around the root (see ArmReel)
    private float _reelPalmGapMeters;  // pointer-origin -> palm on the carrying hand (see ArmReel)
    private float _reelHeadClearMeters;// how close the panel may come to the EYE (see the head guard)
    private float _reelHalfWidthMeters;  // panel half-width  about the root, in the root's own plane
    private float _reelHalfHeightMeters; // panel half-height about the root, in the root's own plane
    private int _reelHeadBlocks;       // frames the head guard refused a step this carry
    private long _reelSelfTicks;       // Stopwatch ticks spent inside TickCarryReel this carry
    private int _reelSelfFrames;       // frames TickCarryReel ran this carry (self-cost readout)

    /// <summary>
    /// Stick-Y deadzone for the reel. MIRRORS <c>RayUguiDriver.ScrollDeadzone</c>
    /// (RayUguiDriver.cs:48 = 0.3f, applied at :319) rather than <see cref="Rig.Flight"/>'s 0.2,
    /// and the choice is deliberate: the reel is a POINTING-HAND control performed with the same
    /// thumb that is holding the trigger down, which is exactly the posture the scroll deadzone was
    /// tuned for. <c>ModalFallback.ResultsScrollDeadzone</c> (ModalFallback.5.ResultsScroll.cs:16)
    /// is a third copy of the same number for the same reason. Flight's smaller value belongs to a
    /// stick the player is holding at rest, which is a different thumb posture.
    /// </summary>
    private const float ReelDeadzone = 0.3f;

    /// <summary>
    /// How much clear air the panel keeps in front of the EYE, counted in head near-plane depths.
    ///
    /// <para><b>THIS IS NO LONGER THE NEAR BOUND</b> (ModBuild 231). Until 230 the same number was
    /// added to the panel's own half-diagonal and used as the reel's floor, which is what stopped a
    /// typical window around 0.6 perceived metres and provoked the user's second request
    /// ("Weiterhin soll das Fenster bis kurz vor der Hand zu einem ziehbar sein das man es dann
    /// direkt greifen kann"). The floor is now a REACH number
    /// (<c>ProximityGrabber.ReachMeters</c>, see <see cref="ArmReel"/>); this constant survives as
    /// the radius of a HEAD guard that only ever bites when the panel is actually being driven at
    /// the player's face, which a floor measured along the hand's ray cannot see.</para>
    /// </summary>
    private const float ReelHeadClearStandoffs = 6f;

    /// <summary>
    /// Degeneracy floor for the along-ray distance, perceived metres. NOT a comfort number and not
    /// a tuning site: it only keeps the carried panel in FRONT of the pointer when the derived grab
    /// floor (<see cref="ArmReel"/>) comes out at or below zero, which happens if a hand rig ever
    /// seats its pointer origin a full palm reach away from its palm. Two centimetres is small
    /// enough to be invisible next to the 13 cm it guards and large enough that
    /// <c>rayOrigin + rayDir * d</c> never degenerates onto the origin itself.
    /// </summary>
    private const float ReelPointerFloorMeters = 0.02f;

    /// <summary>
    /// The mod's DESIGN near plane in PERCEIVED metres — a documented read of
    /// <c>Rig.VRRigDriver.BaseNearMeters</c> (VRRigDriver.cs:98 = 0.05f), used as a FLOOR under the
    /// live <see cref="Camera.nearClipPlane"/> reading.
    ///
    /// <para>WHY THE LIVE VALUE ALONE IS NOT USABLE, which is the non-obvious half. The head camera
    /// sets <c>near = clamp(BaseNearMeters × rigScale, MinNearClip, MaxNearClip)</c>
    /// (VRRigDriver.HeadCamera.cs:257) with <c>MaxNearClip = 0.5</c> world units. At the scenario's
    /// ~198x rig scale the unclamped value would be 9.9, so the clamp bites and the live near plane
    /// is 0.5 world units — <b>2.5 perceived millimetres</b>. That clamp is a DEPTH-PRECISION
    /// compromise (a near plane that far out would wreck the z-buffer ratio against the far plane),
    /// not a statement that a window may sit 2.5 mm from the player's eye. Taking the live reading
    /// at face value would collapse the reel's HEAD CLEARANCE
    /// (<see cref="ReelHeadClearStandoffs"/>) to nothing at exactly the scale the player spends the
    /// game in.</para>
    ///
    /// <para>Deliberately NOT added to <c>scripts/check-mirrors.sh</c>: this is not a second TUNING
    /// site for the near plane, it is a lower bound on a comfort clamp. If VRRigDriver's base ever
    /// moves, the live reading moves with it and the max() below simply stops selecting this floor
    /// — nothing silently drifts out of agreement.</para>
    /// </summary>
    private const float ReelDesignNearMeters = 0.05f;

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
            //
            // THE REEL STANDS DOWN WITH IT, and this is the WHOLE two-hand rule — there is no
            // second arbitration anywhere. The two-hand gesture already owns distance: it carries
            // the root to the palm MIDPOINT (:565 below) and pinch-scales it against
            // _anchorDistance, so a reel writing _carryDistance at the same time would be a second
            // claimant on the one quantity the pinch exists to control, and the frame after the
            // second hand let go the window would jump to whatever the reel had wound to. Clearing
            // the carry mode is therefore enough by construction: TickCarryReel is only ever
            // reached from the `_laserCarry && _handB == null` branch, and LaserCarryReel.OwnsStick
            // re-derives its answer from the same two fields, so Flight's stick comes back to it in
            // the SAME frame the pinch starts. If the palm hand later lets go, the carry continues
            // one-handed as a plain palm carry (OnRelease re-anchors) — it does NOT re-arm the
            // laser reel, because the gesture is no longer a laser hold and the player's laser hand
            // may by then be pointing somewhere else entirely.
            _laserCarry = false;
            // Reported against _handA, the hand that was DOING the carrying — `hand` here is the
            // palm that just joined and never held the reel.
            ReportReelReleased(_handA, "a second hand joined — the two-hand pinch owns distance now");
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
        ArmReel(hand);
        LaserCarryReel.Claim(this);
        VRLog.Info(_logChannel, $"{_logName} grab: LASER-CARRY armed ({hand.Side}, "
                                + $"{_reelStartMeters:F2} m). REEL "
                                + (Rig.ComfortSettings.IsBound
                                    ? Rig.ComfortSettings.LaserCarryReel.Value
                                        ? $"ON at {Rig.ComfortSettings.LaserCarryReelSpeed.Value:F2} perceived m/s "
                                          + "(ModBuild 231 convention: stick BACK pulls in, FORWARD pushes out), "
                                          + $"window {_reelMinMeters:F2}..{_reelMaxMeters:F2} m. "
                                          + $"NEAR {_reelFloorMeters:F2} m = ProximityGrabber palm reach "
                                          + $"{ProximityGrabber.ReachMeters:F2} m minus this hand's "
                                          + $"{_reelPalmGapMeters:F2} m pointer-to-palm gap, i.e. the distance at "
                                          + "which a grip press already takes the window. FAR "
                                          + $"{_reelCeilingMeters:F2} m = RayGrabDriver reach "
                                          + $"{Hands.Interact.RayGrabDriver.MaxDistanceMeters:F2} m minus the "
                                          + $"panel's own {_reelReachMeters:F2} m around the root"
                                          + (_reelMinMeters < _reelFloorMeters || _reelMaxMeters > _reelCeilingMeters
                                              ? "; WIDENED to include the engage distance (a grab never moves what it grabs)"
                                              : "")
                                          + $". The face guard keeps the panel's nearest point {_reelHeadClearMeters:F2} m "
                                          + "off the eye and only ever refuses a step that would make that worse. This "
                                          + "hand's stick Y is the reel's for the whole hold and vertical flight stands down"
                                        : "OFF ([Comfort] LaserCarryReel) — the stick keeps whatever it does today"
                                    : "unavailable (ComfortSettings not bound)")
                                + ".");
    }

    /// <summary>
    /// Derive this carry's reel window and reset its accounting. Runs ONCE per engage — the whole
    /// per-frame cost of the feature is <see cref="TickCarryReel"/>, which reads these numbers.
    ///
    /// <para><b>THE NEAR BOUND IS A REACH NUMBER, NOT A READABILITY NUMBER</b> (user request
    /// 2026-08-23, verbatim: <i>"Weiterhin soll das Fenster bis kurz vor der Hand zu einem ziehbar
    /// sein das man es dann direkt greifen kann."</i>). It is THE DISTANCE AT WHICH THE MOD'S OWN
    /// PROXIMITY GRAB WOULD ALREADY TAKE THE WINDOW, derived rather than tuned so that "close
    /// enough to then grab it directly" is true by construction:
    /// <list type="number">
    /// <item><see cref="ProximityGrabber"/> takes a grip when
    /// <c>Distance(palm, collider.ClosestPoint(palm)) &lt;= ReachMeters * WorldScale</c>
    /// (ProximityGrabber.cs:265, reach = 0.13 perceived m at :38 — the value
    /// <c>scripts/check-mirrors.sh</c> keeps equal across the grabber, the figure grab and the fan
    /// sweep). It measures from the PALM to the collider's nearest SURFACE point.</item>
    /// <item>The reel's number is an along-ray distance from the hand's POINTER ORIGIN
    /// (<c>VRHand.GetAimRay</c>, VRHand.cs:316) to the beam's strike point. Those are two different
    /// origins, so the conversion is the measured gap between them on THIS hand,
    /// <c>|rayOrigin - PalmCenter|</c> — a few perceived centimetres, read live rather than
    /// assumed, because the hand rig is user-seatable.</item>
    /// <item>By the triangle inequality <c>|palm - strike| &lt;= gap + d</c>, so
    /// <c>d &lt;= ReachMeters - gap</c> GUARANTEES the strike point is inside palm reach. And the
    /// grabber measures to the ZONE, which strictly CONTAINS that point — zone and bar share a
    /// centre and the zone is the larger box in every axis (0.62 vs 0.55 of the window width, 0.05
    /// vs 0.024 thick, GrabbableModal.cs:43-44/1085-1089) — so
    /// <c>ClosestPoint</c> can only be nearer still. The guarantee is conservative in the safe
    /// direction: at the floor, a grip press takes the window.</item>
    /// </list>
    /// <b>THE PANEL'S OWN SIZE IS DELIBERATELY NOT IN THIS BOUND ANY MORE.</b> Until 230 the floor
    /// was <c>|_carryOffset| + struck.bounds.extents.magnitude</c> plus 0.30 m of near-plane
    /// standoff, i.e. the panel modelled as a BALL around the strike point, which stopped a typical
    /// modal at ~0.6 m and a large one further out — the exact complaint. A window is a flat rect:
    /// its extent lies IN its own plane, not toward the player, so charging that extent against a
    /// distance measured along the aim ray over-pays by up to a metre. The panel's extent still
    /// matters, and it is still charged — but against the HEAD, where it is the right term, and by
    /// the live guard in <see cref="TickCarryReel"/> rather than by a bound taken once at engage.
    /// <see cref="_reelReachMeters"/> is still measured here because the FAR bound needs it.</para>
    ///
    /// <para><b>AND THE FACE IS PROTECTED SEPARATELY, BECAUSE THIS BOUND CANNOT SEE IT.</b> The
    /// floor is measured along the HAND's ray; where the player's EYE is relative to that ray is not
    /// knowable at engage and changes every frame afterwards. So "do not drive the window into the
    /// player's face" is a per-frame guard on the STEP (<see cref="HeadGuardRefuses"/>), which
    /// measures the panel AS A RECT against the head, and the two bounds never have to be traded
    /// against each other: pointing forward the guard is silent and the window comes to the hand;
    /// pointing at your own face it stops the approach and nothing else.</para>
    ///
    /// <para><b>THE FAR BOUND IS THE LASER'S OWN REACH</b>,
    /// <see cref="Hands.Interact.RayGrabDriver.MaxDistanceMeters"/> minus the same panel reach, so
    /// the drag bar can never be pushed past the distance at which the ray that pushed it would
    /// still find it. That is the honest definition of "lost": a window beyond it cannot be
    /// re-grabbed, hovered or clicked by any far-ray path in the mod. Readability further out is
    /// the player's own business — they can push a window to twenty metres and then reel it back,
    /// and they can pinch it larger with two hands.</para>
    ///
    /// <para><b>THE WINDOW IS WIDENED TO INCLUDE THE ENGAGE DISTANCE.</b> A grab must never move
    /// the thing it grabs: if the player laser-grabs a window that already sits closer than the
    /// derived floor (or further than the ceiling), snapping it on the first carry frame would be a
    /// visible jerk caused by a comfort clamp they did not ask for. Widening instead means the reel
    /// can always give back exactly where the window came from and refuses only to make an
    /// out-of-window situation WORSE.</para>
    /// </summary>
    private void ArmReel(VRHand hand)
    {
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        _reelStartMeters = _carryDistance / scale;
        _reelSelfTicks = 0L;
        _reelSelfFrames = 0;
        _reelHeadBlocks = 0;

        // The struck collider — the same one RayGrabDriver ray-tested (RayGrabDriver.cs:94): the
        // narrow bar strip when the owner published one, else the registered grab zone.
        Collider? struck = BarCollider != null ? BarCollider : GetComponent<Collider>();
        float reachWorld = _carryOffset.magnitude;
        if (struck != null)
            reachWorld += struck.bounds.extents.magnitude;
        _reelReachMeters = reachWorld / scale;

        // THE PANEL AS A RECT IN THE ROOT'S OWN PLANE, for the face guard. Both numbers come out of
        // the geometry this grab already produced, and NEITHER may be read off the registered grab
        // zone: for a floated modal that zone is a thin slab AT THE BAR
        // (GrabbableModal.SyncBar sets size = (width*ZoneWidthFraction, 0.05, 0.05) centred on the
        // bar, GrabbableModal.cs:1088), so its bounds say nothing about the drawn window at all.
        //   * HALF-HEIGHT — the drag bar hangs a gap below the panel's bottom edge
        //     (`y = -(halfHeight + gap)`, GrabbableModal.cs:1085), so the root-up component of
        //     `_carryOffset` (root minus strike point) IS the panel's half-height plus that gap:
        //     measured, and over-measured by the gap, which is the safe direction.
        //   * HALF-WIDTH — the struck collider's own half-diagonal. The bar spans
        //     BarWidthFraction of the window, so this UNDER-measures a very wide window; the guard
        //     is a comfort floor, not an invariant, and under-measuring the width only makes it
        //     quieter in the axis that points sideways past the player's head.
        // Owners with no separate bar collider (tray, combat log) measure their zone the same way.
        Transform? geomRoot = _owner?.GrabRoot;
        float halfHeightWorld = geomRoot != null
            ? Mathf.Abs(Vector3.Dot(_carryOffset, geomRoot.up))
            : _carryOffset.magnitude;
        _reelHalfHeightMeters = halfHeightWorld / scale;
        _reelHalfWidthMeters = struck != null ? struck.bounds.extents.magnitude / scale : 0f;

        // NEAR = "a grip press would already take it here". See the class-level derivation above.
        hand.GetAimRay(out Vector3 rayOrigin, out _);
        Transform? palm = hand.Rig != null ? hand.Rig.PalmCenter : null;
        _reelPalmGapMeters = palm != null ? Vector3.Distance(rayOrigin, palm.position) / scale : 0f;
        float floor = Mathf.Max(ProximityGrabber.ReachMeters - _reelPalmGapMeters, ReelPointerFloorMeters);

        // The radius of the per-frame HEAD guard — not a bound on this axis, recorded here so the
        // engage line can name it and so the guard does not re-read the camera's clip plane every
        // frame. The live near plane needs ReelDesignNearMeters under it; see that constant.
        Camera? head = Rig.VRRigDriver.HeadCamera;
        float nearMeters = head != null && head.nearClipPlane > 0f
            ? Mathf.Max(head.nearClipPlane / scale, ReelDesignNearMeters)
            : ReelDesignNearMeters;
        _reelHeadClearMeters = nearMeters * ReelHeadClearStandoffs;

        float ceiling = Hands.Interact.RayGrabDriver.MaxDistanceMeters - _reelReachMeters;
        // Degenerate geometry (a panel whose own reach exceeds the laser's) must not invert the
        // window; a one-centimetre band is still a usable, monotone clamp.
        ceiling = Mathf.Max(ceiling, floor + 0.01f);

        _reelFloorMeters = floor;
        _reelCeilingMeters = ceiling;
        _reelMinMeters = Mathf.Min(floor, _reelStartMeters);
        _reelMaxMeters = Mathf.Max(ceiling, _reelStartMeters);
    }

    /// <summary>Disarm a laser-carry that never took (ForceGrab refused).</summary>
    internal void CancelLaserCarry()
    {
        _laserCarry = false;
        LaserCarryReel.Release(this);
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
            bool wasLaser = _laserCarry;
            _laserCarry = false;
            if (wasLaser)
                ReportReelReleased(hand, "the trigger was let go");
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
        LaserCarryReel.Release(this);
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
            // CLOSE THE REEL'S BOOKS HERE TOO. This exit does not run OnRelease, so without it a
            // tracking dropout mid-carry would leave an "armed" line in the log with no matching
            // "closed" line — and an unpaired engage is exactly the shape that makes a reader
            // suspect a latch. The stick claim itself was never at risk (LaserCarryReel.OwnsStick
            // re-derives from _handA, which is about to be cleared); this is about the log telling
            // the truth about how the carry ended.
            if (_laserCarry && _handB == null)
                ReportReelReleased(_handA, "the carrying hand lost tracking");
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
            if (_laserCarry && _handB == null)
                ReportReelReleased(_handA, "the grip slot was healed away (the grabber no longer holds this bar)");
            _handA = _handB;
            _handB = null;
            _laserCarry = false;
            LaserCarryReel.Release(this); // the claim dies with the carry, healed slot included
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
            // The ray is read FIRST because the reel now needs its direction too: the face guard
            // (HeadGuardRefuses) has to know which way a step would move the panel. One
            // GetAimRay per carry frame, exactly as before.
            _handA.GetAimRay(out Vector3 rayOrigin, out Vector3 rayDir);
            TickCarryReel(rayDir); // 2026-08-23: the grabbing hand's stick Y winds _carryDistance
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

    // ------------------------------------------------------------------ the reel --

    /// <summary>
    /// Wind the laser carry's along-ray distance from the GRABBING hand's stick Y. Called only from
    /// the one-hand laser branch of <see cref="Update"/>, so every precondition the reel has
    /// (a live laser carry, exactly one hand, that hand tracked) is already proven by the caller.
    ///
    /// <para><b>DIRECTION — AND WHICH CONVENTION EACH SIDE OF THE ARGUMENT MEANT.</b> User,
    /// 2026-08-23, verbatim: <i>"Das ranziehen der Fenster mit dem Laser ist invertiert, wird der
    /// stick weg von mir gezogen geht es weg und umgekehrt. Dreh das um."</i></para>
    ///
    /// <para>WHAT <c>Thumbstick.y</c> IS ON THIS RIG, established from source and cross-checked
    /// against consumers that have survived hardware rather than assumed: <see cref="VRHand"/>
    /// assigns it raw from <c>CommonUsages.primary2DAxis</c> with no sign applied (VRHand.cs:660),
    /// and three independent readers all treat <b>+y as the stick pushed FORWARD, away from the
    /// player</b> — <c>Flight.TickVerticalLift</c> steps the rig along
    /// <c>Vector3.up * Mathf.Sign(stick.y)</c> and its own setting text says "Push the TURN stick
    /// forward to rise" (Flight.cs:515, ComfortSettings.cs:384); <c>Flight.Update</c> composes
    /// <c>dir * unit.y</c> where <c>dir</c> is the flight forward (Flight.cs:332); and the campaign
    /// map's zoom comments its own line "Stick UP (y&gt;0) → zoom IN"
    /// (FlatScreenStereo.3.Map.cs:369). There is no inversion anywhere between the device and this
    /// method.</para>
    ///
    /// <para>SO 230 WAS NOT WRONG ABOUT ITS OWN AXIS, IT PICKED THE OTHER CONVENTION. It shipped
    /// <c>_carryDistance -= meters</c>, i.e. <b>stick forward = pull in</b>, and said so in its log
    /// line. That is genuinely what the code did — which is why the user's sentence cannot be a
    /// description of the shipped behaviour, and reading it as one is what makes it look
    /// self-contradictory. It is the mapping he is ASKING FOR, stated right after the verdict:
    /// stick away ⇒ window away, stick back ⇒ window back to me. "Dreh das um" is then exactly one
    /// sign, and after it the control is the fishing-reel one: <b>you pull the stick toward you to
    /// pull the window toward you.</b></para>
    ///
    /// <para><b>THE CONVENTION FROM ModBuild 231 ON: +y (forward) PUSHES OUT, −y (back) PULLS IN.</b>
    /// A positive stick Y therefore ADDS to the along-ray distance. Written down here so the next
    /// person does not have to re-derive it from three files, and named in the engage log so a
    /// build can be judged from one line.</para>
    ///
    /// <para><b>THE RESPONSE CURVE MIRRORS <c>RayUguiDriver.TickStickScroll</c></b>
    /// (RayUguiDriver.cs:318-325) rather than inventing a second feel: deadzone test on |y|,
    /// deadzone-NORMALIZED linear response so speed ramps from 0 at the deadzone edge to the full
    /// dial at full deflection, signed by the stick, integrated against
    /// <see cref="Time.unscaledDeltaTime"/>. Linear and not squared, again following the scroll and
    /// deliberately NOT <see cref="Rig.Flight"/> (Flight.cs:244-246, which squares): flight is
    /// ambient travel where creep near centre is the point, while this is an aimed placement
    /// gesture on a surface the player is looking at — the same argument that gave the scroll its
    /// linear curve. Unscaled time for the same reason every other stick path in the mod uses it:
    /// the game pauses behind dialogs, and a window that stops reeling exactly when a dialog is up
    /// would read as the feature being broken.</para>
    ///
    /// <para><b>THE CLAMP RUNS ONLY WHEN THE REEL IS ON.</b> With the option off this method's only
    /// effect is the two timestamp reads: it must not quietly re-position a carry that is behaving
    /// exactly as it did before this feature existed.</para>
    ///
    /// <para><b>SELF-COST</b> is measured rather than asserted — see the release line, which reports
    /// microseconds per frame over the whole hold. The two <c>Stopwatch.GetTimestamp</c> calls are
    /// themselves inside the measurement, so the number is an over-report of the work, and the whole
    /// method only ever runs while a window is being laser-carried.</para>
    /// </summary>
    private void TickCarryReel(Vector3 rayDir)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _reelSelfFrames++;

        VRHand hand = _handA!;
        if (Rig.ComfortSettings.IsBound && Rig.ComfortSettings.LaserCarryReel.Value)
        {
            // Apparent metres -> world units through the LIVE rig scale, AT USE TIME. Reading it
            // here rather than caching the engage value is what makes the dial mean the same thing
            // in a ~198x scenario and in the ~4.4x map room, and keeps it meaning that if the other
            // hand re-scales the world mid-carry.
            float scale = Mathf.Max(hand.WorldScale, 1e-4f);
            float y = hand.Thumbstick.y;
            if (Mathf.Abs(y) >= ReelDeadzone)
            {
                float response = (Mathf.Abs(y) - ReelDeadzone) / (1f - ReelDeadzone);
                float meters = Mathf.Sign(y) * response
                               * Rig.ComfortSettings.LaserCarryReelSpeed.Value * Time.unscaledDeltaTime;
                float step = meters * scale; // + stick (FORWARD) = further away; see DIRECTION above
                if (!HeadGuardRefuses(step, rayDir, scale))
                    _carryDistance += step;
            }
            _carryDistance = Mathf.Clamp(_carryDistance, _reelMinMeters * scale, _reelMaxMeters * scale);
        }

        _reelSelfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>
    /// THE FACE GUARD: would this reel step drive the panel further INTO the player's head, while
    /// it is already inside <see cref="_reelHeadClearMeters"/> of the eye? Then refuse the step.
    ///
    /// <para><b>WHY A STEP GATE AND NOT A BOUND.</b> A near bound lives on the along-ray axis, and
    /// where the head sits relative to that ray is not knowable at engage — it changes with every
    /// wrist movement. A bound big enough to be safe for every aim is the 0.6 m floor the user just
    /// rejected. A gate on the STEP costs the same two distances, is silent for the aim the player
    /// actually uses (pointing away from themselves), and bites only in the case it exists for.</para>
    ///
    /// <para><b>IT NEVER MOVES THE WINDOW BY ITSELF, AND IT NEVER TRAPS IT.</b> The test is
    /// "does this step make it WORSE", so the escape direction is always available even from inside
    /// the clearance (a window already at the face can still be pushed out), and a frame with no
    /// stick input writes nothing. A positional clamp would have had to choose an exit point on a
    /// sphere around the head — which, for a ray aimed at the player's own face, is BEHIND them.</para>
    ///
    /// <para><b>THIS IS WHERE THE PANEL'S HALF-HEIGHT AND HALF-WIDTH ARE CHARGED</b>, and it is the
    /// right place for them — but as a RECT, never as a ball. The measure is the distance from the
    /// eye to the nearest point of the panel's own rectangle (<see cref="PanelDistanceToHead"/>,
    /// half-extents measured in <see cref="ArmReel"/>). Charging the extent as a RADIUS, which is
    /// what a <c>reach</c>-style term does, is wrong by up to a metre for exactly the windows this
    /// request is about: a tall window standing 0.5 m in front of the player has every point of it
    /// about 0.5 m from the eye, because its extent runs across the view and not toward it, while a
    /// ball model reports 0.5 − 1.0 and declares the face hit. That single mis-model is the whole
    /// reason 230 stopped a window at 0.6 m.</para>
    ///
    /// <para>The panel actually moves by a LERP toward the new target (see the carry branch in
    /// <see cref="Update"/>), i.e. by less than <paramref name="stepWorld"/> in the frame the step is
    /// taken — so this reads early rather than late.</para>
    /// </summary>
    private bool HeadGuardRefuses(float stepWorld, Vector3 rayDir, float scale)
    {
        Camera? head = Rig.VRRigDriver.HeadCamera;
        Transform? root = _owner?.GrabRoot;
        if (head == null || root == null)
            return false; // no head or no root to measure: the guard has nothing to say
        Vector3 headPos = head.transform.position;
        float after = PanelDistanceToHead(root, root.position + rayDir * stepWorld, headPos, scale);
        if (after >= _reelHeadClearMeters * scale)
            return false; // still outside the clearance after the step — nothing to guard
        if (after >= PanelDistanceToHead(root, root.position, headPos, scale))
            return false; // inside it, but the step is not making it worse (the way out)
        _reelHeadBlocks++;
        return true;
    }

    /// <summary>
    /// Distance from <paramref name="headPos"/> to the nearest point of the panel, modelled as the
    /// rectangle centred on <paramref name="center"/> spanning
    /// ±<see cref="_reelHalfWidthMeters"/> along the root's right axis and
    /// ±<see cref="_reelHalfHeightMeters"/> along its up axis (both perceived metres, taken to world
    /// units through the LIVE <paramref name="scale"/> like every other length the reel handles).
    /// Two dot products, two clamps — the exact closest-point formula for a rect, which is what a
    /// window is.
    /// </summary>
    private float PanelDistanceToHead(Transform root, Vector3 center, Vector3 headPos, float scale)
    {
        Vector3 right = root.right;
        Vector3 up = root.up;
        Vector3 d = headPos - center;
        Vector3 closest = center
                        + right * Mathf.Clamp(Vector3.Dot(d, right), -_reelHalfWidthMeters * scale, _reelHalfWidthMeters * scale)
                        + up * Mathf.Clamp(Vector3.Dot(d, up), -_reelHalfHeightMeters * scale, _reelHalfHeightMeters * scale);
        return Vector3.Distance(headPos, closest);
    }

    /// <summary>
    /// One line per finished reel: where the window ended up, how far it travelled, and what the
    /// per-frame work actually cost. Written from BOTH ways a laser carry can end (trigger release
    /// and a second hand joining) so the log never shows an engage without its matching close.
    /// </summary>
    private void ReportReelReleased(VRHand? hand, string why)
    {
        LaserCarryReel.Release(this);
        float scale = hand != null ? Mathf.Max(hand.WorldScale, 1e-4f) : 1f;
        float endMeters = _carryDistance / scale;
        float travel = endMeters - _reelStartMeters;
        // ticks -> microseconds; Stopwatch.Frequency is ticks per second.
        double microsPerFrame = _reelSelfFrames > 0
            ? _reelSelfTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency / _reelSelfFrames
            : 0.0;
        VRLog.Info(_logChannel, $"{_logName} grab: LASER-CARRY reel closed ({hand?.Side.ToString() ?? "?"}, {why}) — "
                                + $"{_reelStartMeters:F2} m -> {endMeters:F2} m "
                                + $"({(travel <= 0f ? "pulled in" : "pushed out")} {Mathf.Abs(travel):F2} m, "
                                + $"window {_reelMinMeters:F2}..{_reelMaxMeters:F2} m; NEAR {_reelFloorMeters:F2} m "
                                + $"from ProximityGrabber palm reach {ProximityGrabber.ReachMeters:F2} m less the "
                                + $"{_reelPalmGapMeters:F2} m pointer-to-palm gap, FAR {_reelCeilingMeters:F2} m from "
                                + $"RayGrabDriver reach {Hands.Interact.RayGrabDriver.MaxDistanceMeters:F2} m less the "
                                + $"panel's {_reelReachMeters:F2} m reach). The face guard "
                                + (_reelHeadBlocks > 0
                                    ? $"refused {_reelHeadBlocks} step(s) at its {_reelHeadClearMeters:F2} m eye clearance"
                                    : $"never bit ({_reelHeadClearMeters:F2} m eye clearance)")
                                + $". Reel self-cost {microsPerFrame:F2} us/frame over {_reelSelfFrames} frames "
                                + "(measured, includes the two timestamp reads that measure it).");
        _reelSelfTicks = 0L;
        _reelSelfFrames = 0;
        _reelHeadBlocks = 0;
    }

    /// <summary>
    /// Is THIS handle's reel the live claimant on its hand's stick Y right now? Re-derived from the
    /// carry state on every read — never a latch. See <see cref="LaserCarryReel"/> for why that
    /// matters and what it is queried by.
    /// </summary>
    internal bool ReelLive =>
        _laserCarry && _handA != null && _handB == null && isActiveAndEnabled
        && Rig.ComfortSettings.IsBound && Rig.ComfortSettings.LaserCarryReel.Value;

    /// <summary>The hand whose stick this handle's reel claims (valid while <see cref="ReelLive"/>).</summary>
    internal VRHand? ReelHand => _handA;

    /// <summary>Attribution for the suppression log: what is holding the stick, and where it is.</summary>
    internal string ReelDescription
    {
        get
        {
            float scale = _handA != null ? Mathf.Max(_handA.WorldScale, 1e-4f) : 1f;
            return $"'{_logName}' held by the {(_handA != null ? _handA.Side.ToString() : "?")} laser "
                   + $"at {_carryDistance / scale:F2} m";
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

/// <summary>
/// THE ONE PLACE ANYONE ASKS "is a laser-carry reel holding this hand's stick Y right now?" —
/// the arbitration seam for the user's 2026-08-23 override ruling: <i>"Wenn hoch/runter mit dem
/// joystick auch aktiviert ist dann overruled das ranziehen diese Option so lange man ein Fenster
/// mit dem Laser festhält."</i>
///
/// <para><b>STICK Y ONLY, AND THAT IS NON-NEGOTIABLE.</b> TURNING MAY NEVER BE BLOCKED (standing
/// user ruling, ModBuild 138). Turning is stick X — <c>SnapTurn.cs:147</c> reads
/// <c>hand.Thumbstick.x</c> and nothing else drives the yaw; AoE pattern rotation is X as well
/// (<c>AoeControl.cs:196</c>). The reel reads <c>Thumbstick.y</c> (PanelGrabHandle.TickCarryReel)
/// and this class publishes a claim on the Y axis alone. Nothing on this path can reach turning:
/// the reel does not stamp <c>UiScrollFocus</c>, which is the only signal <c>SnapTurn</c>'s
/// <c>ScrollTurnGate</c> (SnapTurn.cs:154-156) can ever be suppressed by, so a laser carry leaves
/// the yaw exactly as it was before this feature existed.</para>
///
/// <para><b>IT IS A QUERY, NOT A LATCH.</b> The slot below holds a handle, and every read
/// RE-DERIVES the verdict from that handle's live carry state (<see cref="PanelGrabHandle.ReelLive"/>
/// — laser carry armed, exactly one hand, component enabled, the option on) and clears the slot the
/// moment the derivation fails. A boolean written on engage and cleared on release would be one
/// missed teardown path away from a stick that never comes back: this mod has paid for that class
/// of bug more than once (the 2026-08-04 sentinel latch, and the grab-slot heal a hundred lines
/// above, which exists because a stale hold froze a bar's grabbability forever). Here the worst a
/// missed <see cref="Release"/> can do is cost one extra field read.</para>
///
/// <para><b>WHY A SINGLE SLOT IS EXACT.</b> A laser carry can only be started by
/// <c>RayGrabDriver</c>, which runs on the DOMINANT hand only (RayGrabDriver.cs:56,
/// <c>VRHands.Primary != _hand</c>) and only while that hand holds nothing
/// (<c>_hand.Grabber.Held == null</c>, :141). At most one laser carry can therefore exist at a
/// time, and the arriving claim legitimately supersedes any stale one.</para>
///
/// <para><b>EVERY OTHER CONSUMER OF THE SAME STICK WHILE A WINDOW IS LASER-HELD</b>, checked
/// against the source rather than assumed, with the verdict for each:</para>
/// <list type="bullet">
/// <item><b>Snap / smooth turn</b> — <c>SnapTurn.cs:147</c>, <c>Thumbstick.x</c>. NOT A CONSUMER OF
/// Y and therefore untouched, unconditionally. (SnapTurn also passes <c>Thumbstick.y</c> to its
/// <c>ScrollTurnGate</c> at :155, but only as a re-arm input to a suppression that is itself keyed
/// on <c>UiScrollFocus.IsScrolling</c> — which the reel never stamps. So even that path cannot see
/// the reel.) TURN NEVER.</item>
/// <item><b>AoE pattern rotation</b> — <c>AoeControl.cs:196</c>, <c>Thumbstick.x</c>. Same: not a Y
/// consumer, untouched.</item>
/// <item><b>The world grab</b> — <c>WorldGrab.cs:199</c> reads <c>ThumbstickClick</c>, the stick
/// pushed straight DOWN, not the analog axis at all. Untouched.</item>
/// <item><b>Generic uGUI stick scroll</b> — <c>RayUguiDriver.TickStickScroll</c>, Y. CANNOT BE LIVE
/// on the carrying hand, by construction and not by arbitration: <c>RayUguiDriver.Tick</c> returns
/// at its first gate when <c>_hand.Ray.Active</c> is false (RayUguiDriver.cs:112), and
/// <c>RayInteractor.Active</c> is <c>… &amp;&amp; !IsHolding</c> (RayInteractor.cs:459) — a laser
/// carry IS a hold. The pointer is <c>Cancel()</c>ed, so there is no hovered ScrollRect for the
/// scroll to find. Nothing to decide.</item>
/// <item><b>The flat-screen stick scroll</b> — <c>FlatScreen.6.Pointer.cs:658</c>, Y. Dead for the
/// same reason one level up: its tick bails when <c>pick.TryGetPick</c> returns false
/// (FlatScreen.6.Pointer.cs:29-34), and <c>TryGetPick</c> returns <c>Active</c>. Nothing to
/// decide.</item>
/// <item><b>The results-window scroll</b> — <c>ModalFallback.5.ResultsScroll.cs:175</c>, Y (another
/// lane's file, read only). Its laser half is dead for the same reason as the two above
/// (<c>hand.RayUgui.Hovered</c> is null while holding). Its POKE half
/// (<c>hand.Poke.HoveredUi</c>) is not gated on the ray, so in principle the carrying hand's
/// FINGERTIP could be inside a results window while its laser carries a different one at range.
/// THE REEL DOES NOT CONTEST THAT and both would run: it is geometrically self-contradictory (the
/// carried window sits at the captured ray distance, metres from the fingertip), it costs a scroll
/// nothing to also move, and buying it would mean editing a file this lane does not own. Recorded
/// as a known, unreachable-in-practice overlap rather than left unexamined.</item>
/// <item><b>Forward/backward stick flight</b> — <c>Flight.Update</c>, Y on
/// <c>[Comfort] FlightHand</c>. GENUINELY COLLIDES, and with the shipped defaults on the SAME
/// controller. THE REEL WINS, deliberately and out loud — see the gate in Flight.Update for the
/// argument and the log line.</item>
/// <item><b>Vertical lift</b> — <c>Flight.TickVerticalLift</c>, Y on <c>[Comfort] TurnHand</c>.
/// GENUINELY COLLIDES. THE REEL WINS: that is the user's ruling verbatim. See
/// <c>Flight.LiftAllowed</c>.</item>
/// <item><b>Level strafe</b> — <c>Flight.Update</c>, <c>stick.x</c>. Not a Y consumer; unaffected,
/// so a player can still slide sideways while reeling a window in.</item>
/// <item><b>Campaign-map zoom</b> — <c>FlatScreenStereo.3.Map.cs:355</c>, the RIGHT hand's Y, and
/// the ONE reader in the mod with no holding gate. It is inert in the 3D map room
/// (<c>MapRoomOwnsParchment</c> forces <c>_mapBaseCapture</c> false, FlatScreenStereo.3.Map.cs:38),
/// so the overlap can only arise on the FLAT campaign map with a floated window laser-held over it.
/// SETTLED, and this sentence used to say it was only reported: the guard is in the file
/// (<c>if (LaserCarryReel.OwnsStick(rh)) return;</c>, FlatScreenStereo.3.Map.cs:362), decided the
/// way every other contest here is — the hand that is carrying something owns its own stick.</item>
/// </list>
///
/// <para><b>COST TO THE CALLER.</b> <see cref="OwnsStick"/> in the common case (nothing is being
/// laser-carried, which is nearly every frame of a session) is one static field read and one
/// Unity-null compare — no config read, no scene lookup, no allocation. Only while a carry really
/// is live does it reach the handle's property, which is five field reads and two static config
/// reads. It is safe to call unconditionally from a per-frame path such as
/// <c>Rig.Flight.Update</c>.</para>
/// </summary>
internal static class LaserCarryReel
{
    private static PanelGrabHandle? _owner;

    /// <summary>A laser carry just engaged on <paramref name="handle"/>; it becomes the claimant.</summary>
    internal static void Claim(PanelGrabHandle handle) => _owner = handle;

    /// <summary>That carry ended. Idempotent, and a no-op if some other handle has claimed since.</summary>
    internal static void Release(PanelGrabHandle handle)
    {
        if (ReferenceEquals(_owner, handle))
            _owner = null;
    }

    /// <summary>
    /// Does a live reel own <paramref name="hand"/>'s stick Y this frame? False for every hand
    /// while nothing is laser-carried, false for the OTHER hand while one is, and false whenever
    /// <c>[Comfort] LaserCarryReel</c> is off — the option's whole job is to hand the axis back.
    /// </summary>
    internal static bool OwnsStick(VRHand? hand)
    {
        PanelGrabHandle? owner = _owner;
        // Unity-null aware on purpose: the handle's GameObject can be destroyed under us (window
        // closed, mode teardown, hot reload) without any release path running.
        if (owner == null)
        {
            _owner = null;
            return false;
        }
        if (!owner.ReelLive)
        {
            _owner = null;
            return false;
        }
        return hand != null && ReferenceEquals(owner.ReelHand, hand);
    }

    /// <summary>Attribution for a suppressor's log line — never call it per frame.</summary>
    internal static string Describe()
    {
        PanelGrabHandle? owner = _owner;
        return owner == null || !owner.ReelLive ? "no laser carry" : owner.ReelDescription;
    }
}
