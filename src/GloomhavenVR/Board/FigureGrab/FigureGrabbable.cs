using System.Collections.Generic;
using AStar;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.Surfaces;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A grabbable board figure (hero OR monster) — grip-grab it into the hand to inspect it
/// (P8, immersion only, no gameplay effect). Implemented directly on
/// <see cref="IGrabbable"/> (NOT via <see cref="GrabbableBehaviour"/>) because the object
/// being moved is a live GAME object, not a mod-owned MonoBehaviour: we reparent the real
/// <c>m_RootGameObject</c> and suppress the game's own transform writes for the held actor
/// via <see cref="HeldFigures"/> / <see cref="ActorBehaviour_HeldTransform_Patch"/> — the
/// user's confirmed "move the real figure" choice (Approach A).
///
/// Grab paths (both the TRIGGER, since <see cref="GrabWithGrip"/> is false — the user's
/// hardware pass moved figures onto the trigger, exactly like the hand-card fan, and the
/// grip is now free): near reach-and-close is handled automatically by
/// <see cref="ProximityGrabber"/> (its trigger path, arbitrated vs a UI/board click via
/// <c>Ray.HasFreshUiHit</c>); the far laser point-and-grab is driven by
/// <see cref="FigureGrabDriver"/> via <c>hand.Grabber.ForceGrab</c> (the same trigger
/// pluck the card fan uses). Release (trigger-up) restores the real transform; the game
/// snaps the mini back to its cell on the next frame.
///
/// The held pose (offset / rotation / scale) is LIVE-TUNABLE: every currently-held
/// grabbable registers in <see cref="Live"/> and re-applies its pose from
/// <see cref="FigureGrabConfig"/> whenever a tunable changes (<see cref="ReapplyAll"/>,
/// wired to each entry's SettingChanged in <see cref="FigureGrabConfig.Bind"/>), so the
/// in-headset debug-menu steppers nudge the mini in your hand in real time.
/// </summary>
internal sealed class FigureGrabbable : IGrabbable, IGrabHighlight, IGrabbableHandFilter
{
    /// <summary>Every grabbable currently held in a hand — the live-tune broadcast target.</summary>
    private static readonly HashSet<FigureGrabbable> Live = new();

    private readonly ActorBehaviour _actor;

    private VRHand? _holder;

    // Item 3 — offset-anchor nearest selection. When the hand hovers over MULTIPLE figures in
    // proximity reach, only the one nearest the OFFSET ANCHOR (where the held mini will appear)
    // should be grabbable; the losers are suppressed for THAT hand so the ProximityGrabber (which
    // otherwise picks nearest-to-palm) can only highlight/grab the offset-anchor winner. Set every
    // frame per hand by FigureGrabDriver.SelectByOffsetAnchor; consumed by AllowsHand below.
    private bool _suppressLeft;
    private bool _suppressRight;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    // Live-pose bases captured at grab (so re-applying the config pose never compounds):
    // the mini's anchor-local scale at board size, and the hand anchor it rides. NOTE: the
    // figure's grab-time world rotation is deliberately NOT captured — the held orientation is
    // a FIXED canonical upright-facing-player pose (see ApplyUprightPose), RE-DERIVED every
    // frame (FigureGrabDriver → DriveHeldPoses) from the live hand + head + config, so it never
    // depends on how the figure was oriented on the board, the angle it was grabbed from, or the
    // wrist rotation the hand happens to settle into after the grab.
    private Transform? _anchor;
    private Vector3 _heldBaseScale = Vector3.one;

    // Diagnostic throttle: while held, log the hand/computed/actual rotations ~once per second so
    // a hardware log proves the hold is the fixed canonical pose and no other path is overwriting it.
    private float _nextPoseLogTime;

    // R2 hardening: the actor's authoritative board cell at grab time. If the game moves the
    // figure to a different cell while it is held (a remote player's or the server's networked
    // action on its turn), the held mini would otherwise ride the hand at a now-stale board
    // position and jump on release; we auto-release instead (polled by FigureGrabDriver).
    private Point _grabCell;

    /// <summary>
    /// Re-apply the held pose from <see cref="FigureGrabConfig"/> to every held mini — the
    /// live-tune hook (wired to the config entries' SettingChanged). Called on the main
    /// thread from a stepper write, so it may touch transforms.
    /// </summary>
    internal static void ReapplyAll()
    {
        foreach (FigureGrabbable g in Live)
            g.ApplyHeldPose();
    }

    /// <summary>
    /// Per-frame driver hook (called from <see cref="FigureGrabDriver"/>.Update): RE-DERIVE the
    /// held pose of every currently-held mini from the LIVE hand + head + config this frame.
    ///
    /// This is the actual fix for the "grab angle influences the hold" bug. The pose used to be
    /// baked into the mini's localRotation ONCE at grab and then rode the moving hand anchor, so
    /// the resting orientation was frozen to whatever wrist pose the hand settled into at grab
    /// time — perceived as the grab/approach angle leaking into the hold. Re-deriving the WORLD
    /// pose every frame (<see cref="ApplyUprightPose"/> builds it purely from world-up + the
    /// horizontal direction to the head) makes the mini snap to the exact same upright,
    /// player-facing orientation regardless of approach angle OR wrist rotation.
    /// </summary>
    internal static void DriveHeldPoses()
    {
        if (Live.Count == 0)
            return;
        foreach (FigureGrabbable g in Live)
            g.DriveHeldPose();
    }

    /// <summary>Re-derive this held mini's pose this frame and emit the throttled diagnostic.</summary>
    private void DriveHeldPose()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null || _holder == null)
            return;

        // Snapshot the orientation the figure enters this frame with (BEFORE we re-correct it):
        // if any other path — a stale grab-time bake riding the wrist, the game's writers, the MP
        // interpolator — moved it since our last write, this diverges from the fixed pose below.
        Quaternion actualBefore = root.transform.rotation;

        ApplyHeldPose(); // re-derive the fixed canonical pose from live hand + head + config

        Quaternion applied = root.transform.rotation;
        if (Time.unscaledTime >= _nextPoseLogTime)
        {
            _nextPoseLogTime = Time.unscaledTime + 1f;
            VRLog.Info("FigureGrab",
                $"hold[{_holder.Side}] hand={Fmt(_anchor.rotation)} computed={Fmt(applied)} " +
                $"actualBefore={Fmt(actualBefore)} (upright={FigureGrabConfig.HeldUpright.Value} " +
                $"tilt={FigureGrabConfig.HeldTiltDegrees.Value:0.#} yaw={FigureGrabConfig.HeldFaceYawFor(_holder.Side):0.#}).");
        }
    }

    private static string Fmt(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({e.x:0.#},{e.y:0.#},{e.z:0.#})";
    }

    internal FigureGrabbable(ActorBehaviour actor) => _actor = actor;

    internal ActorBehaviour Actor => _actor;

    internal bool IsHeld => _holder != null;

    private CActor? Character => _actor != null ? _actor.Actor : null;

    private GameObject? Root => _actor != null ? _actor.m_RootGameObject : null;

    public bool CanGrab
    {
        get
        {
            if (!FigureGrabConfig.GrabFigures.Value || _holder != null)
                return false;
            if (_actor == null || Root == null)
                return false;
            CActor? actor = Character;
            return actor != null && !actor.IsDead;
        }
    }

    /// <summary>
    /// False → figures obey the shared card grab button (the TRIGGER by default, per the
    /// user's hardware pass), so the <see cref="ProximityGrabber"/> near-grab and the laser
    /// pluck use the SAME trigger + <c>Ray.HasFreshUiHit</c> arbitration as the hand cards.
    /// The grip is now free.
    /// </summary>
    public bool GrabWithGrip => false;

    /// <summary>
    /// Item 3: per-hand gate (<see cref="IGrabbableHandFilter"/>). Returns false while this figure
    /// is a proximity-grab LOSER for <paramref name="hand"/> — i.e. another figure sits nearer the
    /// hand's offset anchor (the point where the held mini appears). Set each frame by
    /// <see cref="FigureGrabDriver"/>; the <see cref="ProximityGrabber"/> then skips the losers,
    /// leaving only the offset-anchor-nearest figure grabbable. Uncontested figures (single figure,
    /// or a far laser target out of proximity reach) are never suppressed, so far-grab is untouched.
    /// </summary>
    public bool AllowsHand(VRHand hand)
        => !(hand.Side == HandSide.Left ? _suppressLeft : _suppressRight);

    /// <summary>Driver hook: mark this figure suppressed (proximity loser) for a hand, or clear it.</summary>
    internal void SetProximitySuppressed(HandSide side, bool suppressed)
    {
        if (side == HandSide.Left)
            _suppressLeft = suppressed;
        else
            _suppressRight = suppressed;
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        // Reuse the game's own actor highlight ring — no new outline plumbing.
        GameObject? root = Root;
        if (root != null)
            ActorBehaviour.SetHilighted(root, highlighted);
    }

    public void OnGrab(VRHand hand)
    {
        GameObject? root = Root;
        if (_actor == null || root == null)
            return;

        _holder = hand;
        Transform t = root.transform;
        _origParent = t.parent;
        _origLocalPos = t.localPosition;
        _origLocalRot = t.localRotation;
        _origLocalScale = t.localScale;

        // Suppress the game's per-frame transform writes for THIS actor only.
        HeldFigures.Add(_actor);

        // Snapshot the authoritative cell so we can auto-release if the game moves the figure
        // on the board while it is held (R2 hardening).
        CActor? ca = Character;
        _grabCell = ca != null ? ca.ArrayIndex : default;

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board
        // world-scale as it enters the hand (no scale pop). Snapshot that scale as the LIVE-TUNE
        // base: HeldScale zooms on top of the board scale, re-derived (never compounded) every
        // time a tunable changes. The held ROTATION is NOT snapshotted from the board — it is a
        // fixed canonical upright-facing-player pose computed in ApplyUprightPose, so the mini
        // snaps to the same orientation regardless of the grab approach angle.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;
        _heldBaseScale = t.localScale;
        _attached = true;

        ApplyHeldPose();
        Live.Add(this);
        _nextPoseLogTime = 0f; // force the first per-frame hold diagnostic on the next drive tick

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character, hand.Side);

        VRLog.Info("FigureGrab",
            $"{hand.Side} grabbed figure ({Describe()}); hand={Fmt(anchor.rotation)} " +
            $"held={Fmt(t.rotation)} (the held pose is re-derived every frame, wrist-independent).");
    }

    /// <summary>
    /// (Re-)apply the held pose from <see cref="FigureGrabConfig"/> — offset, rotation and
    /// scale — off the bases captured at grab. Idempotent, so it doubles as the live-tune
    /// path: a debug-menu stepper writes a config entry and this re-poses the mini in-hand.
    /// </summary>
    private void ApplyHeldPose()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null || _holder == null)
            return;
        Transform t = root.transform;

        // Item 2: the tuned offsets/rotation are canonical for the RIGHT hand; the LEFT hand gets
        // the MIRROR IMAGE (lateral offset + yaw/roll flip sign; forward/up/tilt unchanged) so the
        // user only tunes once and the mini sits in the left hand exactly mirrored.
        HandSide side = _holder.Side;

        // Pinch position: a small grab-anchor-local offset toward the thumb–index fingertips
        // (mirrored across the hand's left-right axis for the left hand).
        t.localPosition = FigureGrabConfig.HeldOffsetFor(side);

        if (FigureGrabConfig.HeldUpright.Value)
            ApplyUprightPose(t, _anchor, side);
        else
            t.localRotation = Quaternion.Euler(FigureGrabConfig.HeldEuler); // legacy flat-on-palm (tilt only, mirror-invariant)

        t.localScale = _heldBaseScale * FigureGrabConfig.HeldScale.Value;
    }

    /// <summary>
    /// Stand the mini UPRIGHT in WORLD space (feet→head along world up) and yaw it to face the
    /// player — a FIXED canonical hold. The base rotation is built purely from world up and the
    /// horizontal direction to the player's head, so it is <b>independent of the figure's board
    /// rotation and of the angle the hand grabbed it from</b>: the mini snaps to the exact same
    /// orientation in the hand no matter how it was approached or plucked. (Assumes model local
    /// +Z = front / +Y = up; <see cref="FigureGrabConfig.HeldFaceYawDegrees"/> corrects models
    /// whose readable side differs — e.g. 180 if it faces away.) Written as a WORLD rotation and
    /// RE-DERIVED every frame (<see cref="DriveHeldPoses"/>), so the mini stands the same way in
    /// the hand no matter the approach angle OR the wrist rotation — a truly fixed canonical hold,
    /// not one frozen to the grab-moment wrist pose. (The mini still translates with the hand; only
    /// its orientation is world-fixed: always upright, always facing the player.)
    /// </summary>
    private static void ApplyUprightPose(Transform t, Transform anchor, HandSide side)
    {
        // Direction from the pinch point to the player's head (horizontal) → where the mini's
        // readable front should point so it faces the player. Fall back to the hand's forward if
        // the head pose is momentarily unavailable, then to world forward.
        Camera? head = GloomhavenVR.Rig.VRRigDriver.HeadCamera;
        Vector3 toHead = head != null ? head.transform.position - t.position : anchor.forward;
        toHead.y = 0f;
        if (toHead.sqrMagnitude < 1e-6f)
        {
            toHead = anchor.forward;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 1e-6f)
                toHead = Vector3.forward;
        }
        toHead.Normalize();

        // Canonical upright pose: local +Y along WORLD UP (upright, exactly as it stands on the
        // board) and local +Z along the horizontal to-head direction (readable front faces the
        // player). Derived ONLY from world up + head, NOT from the grab-time figure rotation —
        // this is what makes the hold identical regardless of the grab approach angle.
        Quaternion worldRot = Quaternion.LookRotation(toHead, Vector3.up);

        // User inspection adjustments, in the mini's own frame: tilt tips it toward the face,
        // yaw spins the readable front toward the player. Item 2: the yaw is MIRRORED for the left
        // hand (negated) while the tilt (pitch about X) is mirror-invariant, so the left-hand pose
        // is the mirror image of the tuned right-hand pose. The auto-facing above is world-geometry
        // (faces the head regardless of hand), so it needs no mirroring.
        worldRot *= Quaternion.Euler(
            FigureGrabConfig.HeldTiltDegrees.Value,
            FigureGrabConfig.HeldFaceYawFor(side),
            0f);

        t.rotation = worldRot; // baked into localRotation (child of the moving anchor)
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        Restore();
        VRLog.Info("FigureGrab", $"{hand.Side} released figure ({Describe()}).");
    }

    /// <summary>Restore the real transform and resume the game's transform writes (idempotent).</summary>
    internal void Restore()
    {
        Live.Remove(this);
        if (_attached)
        {
            GameObject? root = Root;
            if (root != null)
            {
                Transform t = root.transform;
                // R2: the original parent may have been destroyed while the figure was held
                // (actor removed / scene teardown). Unity's `!= null` catches a destroyed object,
                // so we unparent to the scene root instead of passing a dead Transform to
                // SetParent (which would throw).
                Transform? parent = _origParent != null ? _origParent : null;
                t.SetParent(parent, worldPositionStays: false);
                t.localPosition = _origLocalPos;
                t.localRotation = _origLocalRot;
                t.localScale = _origLocalScale;
            }
            _attached = false;
            _anchor = null;
        }

        // Resume the game's transform writes → next Update snaps the mini back to its cell.
        if (_actor != null)
            HeldFigures.Remove(_actor);

        if (_holder != null)
        {
            StatPanelSurface.ClearHeldFigure(Character);
            _holder = null;
        }
    }

    /// <summary>
    /// R2 hardening: true when the game has moved this held figure to a DIFFERENT authoritative
    /// board cell since it was grabbed (a networked move on a remote/enemy turn). Polled by
    /// <see cref="FigureGrabDriver"/> each frame; a true result triggers an immediate
    /// <see cref="Restore"/> so the mini snaps to its real cell instead of riding the hand stale.
    /// Also true when the actor/character was destroyed under us. Cheap (one struct compare).
    /// </summary>
    internal bool AuthoritativeCellChanged()
    {
        if (!_attached)
            return false;
        CActor? ca = Character;
        if (ca == null || _actor == null || _actor.m_RootGameObject == null)
            return true; // actor/root gone — release and let the driver prune
        return ca.ArrayIndex != _grabCell;
    }

    private string Describe()
    {
        CActor? actor = Character;
        return actor != null && actor.Class != null ? actor.Class.ID : "?";
    }
}
