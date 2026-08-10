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
/// TRIGGER-ONLY (hardware MP test 2026-08, requirement (b)): the marker
/// <see cref="ITriggerOnlyGrabbable"/> additionally withholds the ProximityGrabber's
/// "closing fist (grip) grabs the highlighted candidate" fallback for figures — a fist
/// over a crowded board is the canonical accidental gesture (and the grip half of the
/// fingertip-ping chord), so a figure hold can ONLY start on the trigger edge and ends
/// on trigger-up, exactly the card semantics. The pre-grab hover highlight
/// (<see cref="OnGrabHighlight"/>) is untouched.
///
/// The held pose (offset / rotation / scale) is LIVE-TUNABLE: every currently-held
/// grabbable registers in <see cref="Live"/> and re-applies its pose from
/// <see cref="FigureGrabConfig"/> whenever a tunable changes (<see cref="ReapplyAll"/>,
/// wired to each entry's SettingChanged in <see cref="FigureGrabConfig.Bind"/>), so the
/// in-headset debug-menu steppers nudge the mini in your hand in real time.
/// </summary>
internal sealed class FigureGrabbable : IGrabbable, IGrabHighlight, IGrabbableHandFilter, ITriggerOnlyGrabbable
{
    /// <summary>Every grabbable currently held in a hand — the live-tune broadcast target.</summary>
    private static readonly HashSet<FigureGrabbable> Live = new();

    // GLIDE-BACK — on release the mini no longer snaps home instantly: it GLIDES (fast,
    // ease-out, unscaled time) from the release pose in the hand back to its home board pose
    // (where the ghost stands), and only on ARRIVAL does the game regain transform control
    // (HeldFigures.Remove) — which also tears the ghost down (FigureGhosts reconciles on the
    // held-sets) and flips the net send from "held @ gliding pose" to "released", so PEERS see
    // the same mini-glide home streamed frame by frame (their existing ease smooths it) with
    // only a sub-frame residual snap at the end. Registry of every in-flight glide, ticked
    // from FigureGrabDriver.Update via TickGlides.
    private static readonly List<FigureGrabbable> Gliding = new();

    /// <summary>Glide time from hand to home — fast but visible (ease-out, unscaled).</summary>
    private const float GlideDurationSeconds = 0.28f;

    private readonly ActorBehaviour _actor;

    /// <summary>
    /// The interactable collider this figure was adopted on — the SAME one the driver measures the
    /// election with and the one the <see cref="ProximityGrabber"/> measures its palm reach with.
    /// Held for diagnostics only (the distances printed by <see cref="OnGrabHighlight"/>); nothing
    /// decides anything from it here. Unity-nullable: the figure may die under the grabbable.
    /// </summary>
    private readonly Collider? _collider;

    private VRHand? _holder;

    // Item 3 — offset-anchor nearest selection. When the hand hovers over MULTIPLE figures in
    // proximity reach, only the one nearest the OFFSET ANCHOR (where the held mini will appear)
    // should be grabbable; the losers are suppressed for THAT hand so the ProximityGrabber (which
    // otherwise picks nearest-to-palm) can only highlight/grab the offset-anchor winner. Set every
    // frame per hand by FigureGrabDriver.SelectByOffsetAnchor; consumed by AllowsHand below.
    // It carries the PICK VOLUME too (2026-08 accidental-grab report): a figure that no hand has
    // actually reached — nothing within [FigureGrab] PickRadiusMillimeters of the pinch point — is
    // suppressed for every hand, so "no winner" and "loser" are the same state here.
    //
    // THE FLAG IS AN UNCONDITIONAL PER-FRAME VETO, not a hint about nearby figures. The driver
    // writes it for EVERY adopted figure on every frame it ticks a hand, however far away that
    // figure is, because the ProximityGrabber reads it one frame in arrears: a figure the driver
    // declined to write was a figure the grabber was free to highlight on its own 13 cm palm reach.
    // That is the whole of the "highlighting blitzt bei verschiedenen Figuren auf" defect — see
    // FigureGrabDriver.ApplySuppression for the full account.
    private bool _suppressLeft;
    private bool _suppressRight;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    // Live-pose bases captured at grab (so re-applying the config pose never compounds):
    // the mini's anchor-local scale at board size, and the hand anchor it rides. NOTE: the
    // held ROTATION is a FIXED CONSTANT local rotation relative to the anchor
    // (FigureGrabConfig.HeldUprightRotation) — NOT derived from world up, the head, the figure's
    // board rotation, or the grab-moment anchor orientation. So the mini sits the SAME way in the
    // palm regardless of the grab approach angle AND rides the hand — turn the hand and the mini
    // turns with it (user #3: "fixed relative to the hand, not the world"), while HOW it was
    // grabbed never changes the resting hold and it never clips into a downward-pointing palm.
    private Transform? _anchor;

    // SIZE PARITY — the mini's WORLD (lossy) scale as it stood ON THE BOARD, captured before the
    // reparent into the hand. It is the ONLY size the held mini is ever rendered at; see
    // ApplyHeldPose / HeldLocalScale for why that is what makes holder and peer agree.
    private Vector3 _homeWorldScale = Vector3.one;

    // Issue B — render-on-top state so a mini held in FRONT of the opaque control board (PlayTray)
    // is not painted over by the board's on-top HUD widgets (queue 4000, ZTest Always, ZWrite off).
    // On grab we snapshot each held renderer's ORIGINAL shared materials and swap in per-renderer
    // INSTANCE materials whose renderQueue is pushed just past those widgets; the shader's own ZTest
    // (LEqual) + ZWrite are left intact so the 3D mini still self-occludes correctly and is hidden
    // naturally when moved BEHIND real world geometry. Restored verbatim on release. ONLY the held
    // mini is affected — never the rest of the board's figures.
    private const int HeldRenderQueue = 4100;
    private Renderer[]? _heldRenderers;
    private Material[][]? _origSharedMats;

    // TASK #2 (pre-grab highlight) — a subtle warm-gold EMISSIVE glow on the figure's OWN materials,
    // shown while a hand is in proximity reach of the figure it WOULD grab (the offset-anchor winner;
    // see FigureGrabDriver.SelectByOffsetAnchor, which already suppresses every non-winner so this
    // hover callback only ever fires on the single grab candidate). Deliberately NOT the game's
    // m_Hilight ring: that draws ZTest Always and shows through walls, whereas a glow on the figure's
    // own material inherits the figure's shader ZTest (LEqual) and is occluded by terrain exactly
    // like the mini — occlusion-correct by construction under this mod's Forward rendering. See
    // FigureHighlight for the snapshot/restore + no-leak mechanics.
    private readonly FigureHighlight _highlight = new();

    // GLIDE-BACK per-instance state: the LOCAL pose (under the restored original parent) the
    // mini had at release, glided toward the captured original local pose (_origLocalPos/Rot/
    // Scale — the exact end state of the old instant path, so zero drift by construction).
    private bool _glideActive;
    private float _glideStartTime;
    private Vector3 _glideFromPos;
    private Quaternion _glideFromRot = Quaternion.identity;
    private Vector3 _glideFromScale = Vector3.one;

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

    private static string Fmt(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({e.x:0.#},{e.y:0.#},{e.z:0.#})";
    }

    internal FigureGrabbable(ActorBehaviour actor, Collider? collider = null)
    {
        _actor = actor;
        _collider = collider;
    }

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
            // MP grab-lock: a figure a REMOTE player currently holds behaves as if it does not exist
            // for the local grab (proximity + laser both consult CanGrab) until they release it.
            if (NetHeldFigures.Owns(_actor))
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
    /// hand's offset anchor (the point where the held mini appears), OR the hand has not actually
    /// reached this one at all ([FigureGrab] PickRadiusMillimeters, real millimetres at the hand —
    /// the interactor's own 13 cm palm reach is a card's reach, not a mini's). Set each frame by
    /// <see cref="FigureGrabDriver"/>; the <see cref="ProximityGrabber"/> then skips the losers,
    /// leaving only the offset-anchor-nearest figure grabbable. EVERY adopted figure is written
    /// every frame, near or far — a figure that is merely out of reach is a LOSER, not an
    /// exception (a distance-gated veto is what made the highlight flash on distant figures; see
    /// <c>FigureGrabDriver.ApplySuppression</c>). The far laser grab is untouched: it clears its
    /// own target's veto at the moment of the pluck.
    /// </summary>
    public bool AllowsHand(VRHand hand)
        => !NetHeldFigures.Owns(_actor)
           && !(hand.Side == HandSide.Left ? _suppressLeft : _suppressRight);

    /// <summary>Driver hook: mark this figure suppressed (proximity loser) for a hand, or clear it.</summary>
    internal void SetProximitySuppressed(HandSide side, bool suppressed)
    {
        if (side == HandSide.Left)
            _suppressLeft = suppressed;
        else
            _suppressRight = suppressed;
    }

    /// <summary>
    /// TASK #2 — pre-grab proximity highlight. Driven by the grab system's hover callback
    /// (<see cref="IGrabHighlight"/>): the <see cref="ProximityGrabber"/> raises this with
    /// <c>true</c> when this figure becomes the hand's nearest grab candidate and <c>false</c>
    /// when it stops. Because <see cref="FigureGrabDriver.SelectByOffsetAnchor"/> suppresses every
    /// figure except the offset-anchor winner for that hand, this only ever fires on the single
    /// figure that would actually be grabbed. On highlight we apply a subtle emissive glow to the
    /// figure's own materials (occlusion-correct — see <see cref="FigureHighlight"/>); on un-hover
    /// (and on grab, since the grabber clears the highlight before <see cref="OnGrab"/>) we restore.
    /// </summary>
    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        GameObject? root = Root;
        if (root == null)
            return;

        if (highlighted)
        {
            // MP lock: never highlight a figure a REMOTE player is holding (it is grab-locked here).
            if (_highlight.Active || NetHeldFigures.Owns(_actor))
                return;
            // Overlay the figure's OWN meshes with an animated additive glow — NO scale change,
            // occlusion-correct, riding the live animation (see FigureHighlight / FigureOverlay).
            GameObject animated = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
            bool glow = _highlight.Apply(root, animated);
            VRLog.Info("FigureGrab",
                $"pre-grab highlight ENGAGED ({hand.Side} near {Describe()}, {DescribeReach(hand)}) — "
                + "animated additive glow "
                + (glow ? "overlaid on the figure's own meshes (wall-occluded, no scale change)."
                        : "UNAVAILABLE (bundle Overlay shader missing) — no highlight."));
        }
        else
        {
            ClearHighlight();
        }
    }

    /// <summary>
    /// WHERE THE HAND ACTUALLY WAS when this figure lit up — the two distances that tell an
    /// ELECTION apart from a LEAK, in the same real millimetres the dial is set in.
    ///
    /// <para>Written because the ModBuild 106 log could not answer that question: it carried 137
    /// "highlight ENGAGED" lines against 9 "pinch candidate" elections, which proved the two were
    /// not the same event but not which distance the extra ones fired at. The pinch distance is the
    /// one the driver's radius gates; the palm distance is the one the
    /// <see cref="ProximityGrabber"/>'s own 13 cm reach gates. So on the next hardware run the line
    /// is binary: a highlight whose pinch distance is inside the printed radius came from an
    /// election and the radius is simply set too wide; one that engages FAR outside it, near the
    /// palm reach instead, means something is highlighting past this driver's veto again.</para>
    ///
    /// <para>Costs two ClosestPoint calls on the highlight EDGE only — never per frame.</para>
    /// </summary>
    private string DescribeReach(VRHand hand)
    {
        if (_collider == null)
            return "distance unknown (no collider)";
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        Vector3 palm = hand.Rig.PalmCenter.position;
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));
        float pinchMm = Vector3.Distance(pinch, _collider.ClosestPoint(pinch)) / scale * 1000f;
        float palmMm = Vector3.Distance(palm, _collider.ClosestPoint(palm)) / scale * 1000f;
        return $"{pinchMm:F0} mm from the pinch point / {palmMm:F0} mm from the palm, real at the "
               + $"hand; pick radius {FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm";
    }

    /// <summary>
    /// Clear the pre-grab highlight and restore the figure's materials (idempotent). Called on
    /// un-hover AND from <see cref="Restore"/> so the glow can never persist past a grab/release.
    /// </summary>
    private void ClearHighlight()
    {
        if (!_highlight.Active)
            return;
        _highlight.Clear();
        VRLog.Info("FigureGrab", $"pre-grab highlight CLEARED ({Describe()}).");
    }

    public void OnGrab(VRHand hand)
    {
        GameObject? root = Root;
        if (_actor == null || root == null)
            return;

        // Re-grab during a release glide: complete the glide instantly FIRST (snap to the home
        // local pose, resume game control for one call's breadth) so the original pose captured
        // below is the true board pose, never a mid-glide sample. HeldFigures.Add below re-enters
        // the held set in the same call, so FigureGhosts never sees a released frame — the ghost
        // (and its captured home pose) survives the re-grab untouched.
        if (_glideActive)
            FinishGlide(root.transform);

        _holder = hand;
        Transform t = root.transform;
        _origParent = t.parent;
        _origLocalPos = t.localPosition;
        _origLocalRot = t.localRotation;
        _origLocalScale = t.localScale;

        // SIZE PARITY: the board WORLD size, sampled while the mini is still standing on the board.
        // Everything the hold does to the mini's scale is expressed as "reproduce exactly this".
        _homeWorldScale = t.lossyScale;

        // TASK #3 — leave a translucent ghost at the figure's HOME board pose while it is held.
        // Capture the pose from the visual (animated) object BEFORE we reparent it into the hand, and
        // build the frozen snapshot from its current (board) pose. FigureGhosts reconciles teardown
        // off HeldFigures/NetHeldFigures, so any release path removes it. (Multiplayer: a REMOTE
        // player's grab spawns the same ghost via NetFigures.)
        GameObject ghostSrc = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        FigureGhosts.NotifyHeld(_actor, ghostSrc.transform.position, ghostSrc.transform.rotation);

        // Suppress the game's per-frame transform writes for THIS actor only. The HAND is recorded
        // with it: a player may hold one figure per hand, and the two-handed figure sync has to say
        // which mini is in which hand (NetProtocol.ExtIdSecondFigure).
        HeldFigures.Add(_actor, hand.Side);

        // Snapshot the authoritative cell so we can auto-release if the game moves the figure
        // on the board while it is held (R2 hardening).
        CActor? ca = Character;
        _grabCell = ca != null ? ca.ArrayIndex : default;

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board
        // world-scale as it enters the hand (no scale pop) — and ApplyHeldPose below then RE-DERIVES
        // that same world scale from the anchor every frame, so a rig pinch-zoom mid-hold cannot
        // drag the mini's size along with it either. The held ROTATION is NOT snapshotted from the
        // board — it is a fixed constant anchor-local rotation (FigureGrabConfig.HeldUprightRotation),
        // so the mini snaps to the same orientation in the palm regardless of the grab approach angle.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;
        _uprightBase = CaptureUprightBase(anchor);
        _attached = true;

        ApplyHeldPose();
        ApplyRenderOnTop(); // Issue B — draw the held mini over the opaque control board
        Live.Add(this);

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character, hand.Side);

        // Issue A — one-shot grab diagnostic: the FIXED anchor-LOCAL rotation chosen for the hold
        // (grab-angle-independent; rides the hand). World rotation shown for reference only.
        // The three angles and WHERE THEY PUT THE MINI'S OWN AXES, in the anchor's frame. Without
        // this we were both describing sensations: "yaw feels like tilt" cannot be checked against
        // a quaternion. up.y near +/-1 means the mini stands along the palm normal, so yaw is a
        // clean spin about the vertical you see; the further up.y is from that, the more every one
        // of the three angles reads as a tumble, which is exactly when two of them feel alike.
        Quaternion lr = t.localRotation;
        Vector3 up = lr * Vector3.up, fwd = lr * Vector3.forward;
        VRLog.Info("FigureGrab",
            $"{hand.Side} grabbed figure ({Describe()}); localRot={Fmt(lr)} " +
            $"(fixed constant relative to the hand anchor; grab-angle-independent, rides the hand) " +
            $"from pitch={FigureGrabConfig.ActiveHeldTilt:0.#}° " +
            $"yaw={FigureGrabConfig.HeldFaceYawFor(hand.Side):0.#}° " +
            $"roll={FigureGrabConfig.HeldRollFor(hand.Side):0.#}° " +
            $"upright={FigureGrabConfig.HeldUpright.Value}; mini axes in anchor space: " +
            $"up=({up.x:0.00},{up.y:0.00},{up.z:0.00}) fwd=({fwd.x:0.00},{fwd.y:0.00},{fwd.z:0.00}) " +
            $"— anchor +Y is the palm normal, so up.y=+1 is 'standing straight out of the palm'. " +
            $"hand={Fmt(anchor.rotation)} worldHeld={Fmt(t.rotation)}.");

        // SIZE PARITY probe (next-test anchor). boardWorld is the size the mini had ON THE BOARD and
        // heldWorld the size it is rendered at IN THE HAND — they must be equal to the printed digits,
        // on BOTH machines, which is the whole of "the peer sees what the holder sees" (the peer
        // renders this same figure at its own, identical, board scale; no scale is on the wire).
        VRLog.Info("FigureGrab",
            $"[Size] {Describe()} boardWorld={_homeWorldScale.x:0.####} heldWorld={t.lossyScale.x:0.####} " +
            $"anchorScale={anchor.lossyScale.x:0.###} — held size IS board size (no inspection zoom; " +
            "peers render the same mini at their own board scale, so holder and peer match 1:1).");
    }

    /// <summary>
    /// The anchor-local rotation that stands the mini HEAD UP IN THE WORLD at this instant, or
    /// identity when the option is off.
    ///
    /// <para>Captured ONCE, at the moment of the grab, and then left alone. That is the whole
    /// point: the mini starts upright however you reached for it — palm down, from the side,
    /// upside down — and from then on it is an ordinary fixed rotation relative to the hand, so
    /// turning your wrist still turns it through every angle. It is not a constraint that keeps
    /// re-righting the mini, which would fight you the moment you tried to look at its base.</para>
    ///
    /// <para>The facing comes from the hand's own forward flattened onto the horizontal, not from
    /// the head: it keeps the mini's front pointing the way you were reaching, and it does not make
    /// the result depend on where anyone is standing. Grabbing with the hand pointing near-vertical
    /// leaves that forward undefined, so the hand's UP is used instead — some horizontal direction
    /// is always available and any of them is better than a NaN.</para>
    ///
    /// <para>MULTIPLAYER: nothing extra is needed. The held figure's WORLD rotation is what goes on
    /// the wire (Net.NetFigures), so a peer sees whatever this produces, exactly.</para>
    /// </summary>
    private static Quaternion CaptureUprightBase(Transform anchor)
    {
        if (!FigureGrabConfig.HeldUprightAtGrab.Value)
            return Quaternion.identity;

        Vector3 flat = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.ProjectOnPlane(anchor.up, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.forward;

        Quaternion world = Quaternion.LookRotation(flat.normalized, Vector3.up);
        return Quaternion.Inverse(anchor.rotation) * world;
    }

    private Quaternion _uprightBase = Quaternion.identity;

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

        // Issue A: a FIXED CONSTANT anchor-LOCAL rotation (grab-angle-independent) that rides the
        // hand — never a world rotation. Upright mode stands the mini out of the palm and faces it
        // (mirror-correct); legacy mode lays it flat (tilt only, mirror-invariant).
        // _uprightBase is identity unless the grab captured a world-upright start, so the tuned
        // angles keep meaning exactly what they meant: offsets, applied on top of whatever the
        // base is. With the option on they are offsets from "standing up"; with it off they are
        // offsets from the hand, as before.
        t.localRotation = _uprightBase * (FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(side)
            : FigureGrabConfig.HeldPalmRotation());

        // SIZE PARITY (hardware MP defect 2026-08: "Die Figuren-Größen ändern sich wenn man sie in
        // die Hand nimmt … so sehe ich beim Remote-Spieler eine andere Größe der Figur in der Hand
        // als er selbst"). The held mini keeps EXACTLY its board world size. Both halves of that
        // defect were the same line:
        //   (a) the mini was rendered at `boardScale * FigureGrabConfig.ActiveHeldScale` — 1.5x by
        //       default, and bound PER HAND STYLE, so two players could not even agree on the
        //       factor. Grabbing therefore always resized the figure. That multiplier is gone (the
        //       config entries stay bound and are marked LEGACY — no effect; CHARTER §5);
        //   (b) the wire carries the held figure's POSE ONLY (NetFigures.TrySampleHeldSlot sends
        //       position + rotation; the receive side eases exactly those two and never writes a
        //       scale), so a peer always rendered the mini at ITS OWN board scale. With the holder
        //       rendering board scale too, the two sizes are identical BY CONSTRUCTION and no scale
        //       has to ride the wire at all — the board is unscaled game world space on every
        //       client, only the RIG is pinch-zoomed (Rig/WorldGrab) and that is per player.
        // Re-derived from the anchor's CURRENT lossyScale rather than snapshotted, because the hand
        // anchor hangs under the rig root: zooming the rig while holding a mini would otherwise
        // scale the mini with it and re-open (b). Convention-agnostic — it reproduces whatever world
        // scale the figure had on the board, so the project's 100x armature scale needs no case here.
        t.localScale = HeldLocalScale();
    }

    /// <summary>
    /// The anchor-LOCAL scale that renders the held mini at exactly its HOME BOARD world size,
    /// given the hand anchor's current <c>lossyScale</c> (which carries the rig pinch-zoom).
    /// Degenerate/zero anchor axes fall back to the home world scale rather than dividing by zero.
    /// </summary>
    private Vector3 HeldLocalScale()
    {
        Vector3 s = _homeWorldScale;
        if (_anchor == null)
            return s;
        Vector3 a = _anchor.lossyScale;
        return new Vector3(
            Mathf.Abs(a.x) > 1e-6f ? s.x / a.x : s.x,
            Mathf.Abs(a.y) > 1e-6f ? s.y / a.y : s.y,
            Mathf.Abs(a.z) > 1e-6f ? s.z / a.z : s.z);
    }

    /// <summary>
    /// Re-assert every held mini's BOARD world size. Called once per frame from
    /// <see cref="FigureGrabDriver"/>: the mini is parented to the hand anchor, so a rig pinch-zoom
    /// (Rig/WorldGrab scales the rig root) would otherwise resize it mid-hold — visible to the
    /// holder as the figure growing in the hand, and invisible to peers, who render the same mini
    /// at their own board scale. One vector divide per held figure (at most two).
    /// </summary>
    internal static void TickHeldScale()
    {
        foreach (FigureGrabbable g in Live)
            g.ReassertHeldScale();
    }

    private void ReassertHeldScale()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null)
            return;
        root.transform.localScale = HeldLocalScale();
    }

    /// <summary>
    /// Issue B — push the held mini's renderers just past the control board's on-top HUD widgets so
    /// a mini held in FRONT of the opaque board is never occluded by it. Snapshots each renderer's
    /// original SHARED materials, then swaps in per-renderer INSTANCE materials (so no shared bundle
    /// material is mutated globally → the rest of that class's board minis are untouched) with the
    /// renderQueue bumped to <see cref="HeldRenderQueue"/>. ZTest/ZWrite are deliberately left at
    /// the shader's defaults (LEqual + on), so the mini still self-occludes correctly and is hidden
    /// when moved BEHIND real world geometry — only the draw ORDER changes, letting the nearer mini
    /// win over the board widgets (which draw ZWrite-off, so they never own the depth). Restored by
    /// <see cref="RestoreRenderers"/> on release.
    /// </summary>
    private void ApplyRenderOnTop()
    {
        // REVERTED (perspective fix): bumping the held mini to HeldRenderQueue (4100) made it draw
        // OVER the control-board widgets, but it also punched the mini THROUGH walls/floors/health-
        // bars and the effect persisted after release ("figure visible through walls once grabbed").
        // The user wants perspective respected for every element except the sky, so held minis now
        // keep their native depth-correct render. Left as a no-op (RestoreRenderers stays safe) so the
        // grab/release call sites are unchanged; the board-widget occlusion is handled proud-seat +
        // LEqual on the widget side in the mod-wide occlusion pass, not by pulling the mini forward.
        return;
        // KEEP — DO NOT DELETE THIS BLOCK (refactor Batch D, verified at HEAD).
        // It is unreachable on purpose: a tested-and-REJECTED approach kept as the record of
        // WHY not to re-add it (4ef1d76). Deleting it saves ~18 lines and discards the answer
        // to "why don't held minis just draw on top?", which is the question that produced the
        // bug above. Note also:
        //   - RestoreRenderers is NOT dead. It is still called from two release paths and is
        //     idempotent, so grab/release symmetry survives a future re-enable.
        //   - the CS0162 suppression below is why this does not show up in the repo's build
        //     warnings. A future "eliminate every #pragma warning disable" pass must skip it.
#pragma warning disable CS0162 // unreachable — retained for quick re-enable if ever needed
        GameObject? root = Root;
        if (root == null)
            return;
        _heldRenderers = root.GetComponentsInChildren<Renderer>(true);
        _origSharedMats = new Material[_heldRenderers.Length][];
        for (int i = 0; i < _heldRenderers.Length; i++)
        {
            Renderer r = _heldRenderers[i];
            if (r == null)
                continue;
            _origSharedMats[i] = r.sharedMaterials;      // snapshot the ORIGINAL shared assets
            Material[] instances = r.materials;          // per-renderer INSTANCES (no global mutation)
            for (int m = 0; m < instances.Length; m++)
            {
                if (instances[m] != null)
                    instances[m].renderQueue = HeldRenderQueue;
            }
        }
#pragma warning restore CS0162
    }

    /// <summary>Issue B — restore the renderers' original shared materials (idempotent).</summary>
    private void RestoreRenderers()
    {
        if (_heldRenderers != null && _origSharedMats != null)
        {
            for (int i = 0; i < _heldRenderers.Length; i++)
            {
                Renderer r = _heldRenderers[i];
                if (r != null && _origSharedMats[i] != null)
                    r.sharedMaterials = _origSharedMats[i];
            }
        }
        _heldRenderers = null;
        _origSharedMats = null;
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        // GLIDE-BACK: instead of the instant restore, ease the mini from the hand back to its
        // home pose (~0.28 s, ease-out). Falls back to the exact old instant path whenever a
        // safe glide is impossible (dead root/parent, teardown mid-hold).
        if (TryBeginGlide())
        {
            VRLog.Info("FigureGrab",
                $"{hand.Side} released figure ({Describe()}) — gliding home ({GlideDurationSeconds:0.00}s).");
            return;
        }
        Restore();
        VRLog.Info("FigureGrab", $"{hand.Side} released figure ({Describe()}).");
    }

    /// <summary>
    /// Begin the release glide: reparent the mini back under its original parent KEEPING the
    /// current world pose (the release pose in the hand), then let <see cref="TickGlide"/> ease
    /// the LOCAL pose to the captured original local TRS — whose end state is bit-identical to
    /// the old instant restore (same parent, same local pos/rot/scale), so no drift is possible.
    /// The hand is freed immediately (stat panel undocked, holder cleared → re-grab works), but
    /// the actor STAYS in <see cref="HeldFigures"/> until arrival, which (a) keeps the game's
    /// transform writers suppressed (ActorBehaviour_HeldTransform_Patch), (b) keeps the home
    /// ghost alive (FigureGhosts reconciles on the held-sets), and (c) keeps the net send
    /// streaming the gliding pose so peers watch the same glide. False when a glide is unsafe
    /// (caller falls back to the instant <see cref="Restore"/>).
    /// </summary>
    private bool TryBeginGlide()
    {
        if (!_attached || _glideActive)
            return false;
        GameObject? root = Root;
        // Unity-null checks: a destroyed original parent (scene teardown mid-hold) or dead root
        // means the instant path's own hardening should run instead.
        if (root == null || _origParent == null || _actor == null || !HeldFigures.Owns(_actor))
            return false;

        Live.Remove(this);
        RestoreRenderers(); // Issue B — undo the render-on-top swap (idempotent)
        ClearHighlight();

        Transform t = root.transform;
        t.SetParent(_origParent, worldPositionStays: true); // keep the in-hand world pose
        _attached = false;
        _anchor = null;

        _glideFromPos = t.localPosition;
        _glideFromRot = t.localRotation;
        _glideFromScale = t.localScale;
        _glideStartTime = Time.unscaledTime;
        _glideActive = true;
        Gliding.Add(this);

        if (_holder != null)
        {
            StatPanelSurface.ClearHeldFigure(_holder.Side, Character);
            _holder = null;
        }
        return true;
    }

    /// <summary>Advance every in-flight release glide. Called once per frame from
    /// <see cref="FigureGrabDriver"/> (before the config gate, so an in-flight glide always
    /// completes). Iterates backwards — a finished glide removes itself from the list.</summary>
    internal static void TickGlides()
    {
        for (int i = Gliding.Count - 1; i >= 0; i--)
            Gliding[i].TickGlide();
    }

    /// <summary>Complete every in-flight glide instantly (driver teardown / module shutdown) so no
    /// static registry entry survives a scene change with the game's writes still suppressed.</summary>
    internal static void FinishAllGlides()
    {
        for (int i = Gliding.Count - 1; i >= 0; i--)
        {
            FigureGrabbable g = Gliding[i];
            GameObject? root = g.Root;
            g.FinishGlide(root != null ? root.transform : null);
        }
    }

    private void TickGlide()
    {
        GameObject? root = Root;
        if (root == null)
        {
            FinishGlide(null); // actor torn down mid-glide — just resume game control
            return;
        }

        Transform t = root.transform;
        float u = (Time.unscaledTime - _glideStartTime) / GlideDurationSeconds; // unscaled: pause-proof
        if (u >= 1f)
        {
            FinishGlide(t);
            return;
        }

        float e = 1f - (1f - u) * (1f - u) * (1f - u); // cubic ease-out — fast start, soft landing
        t.localPosition = Vector3.LerpUnclamped(_glideFromPos, _origLocalPos, e);
        t.localRotation = Quaternion.SlerpUnclamped(_glideFromRot, _origLocalRot, e);
        t.localScale = Vector3.LerpUnclamped(_glideFromScale, _origLocalScale, e);
    }

    /// <summary>
    /// Complete (or cancel) the glide instantly: snap to the exact end pose of the old instant
    /// path and hand the actor back to the game (<see cref="HeldFigures"/> removal → next
    /// ActorBehaviour.Update re-asserts the authoritative cell pose; the ghost is torn down by
    /// the next FigureGhosts.Tick). Idempotent; <paramref name="t"/> may be null when the root
    /// died mid-glide.
    /// </summary>
    private void FinishGlide(Transform? t)
    {
        if (!_glideActive)
            return;
        _glideActive = false;
        Gliding.Remove(this);

        if (t != null)
        {
            t.localPosition = _origLocalPos;
            t.localRotation = _origLocalRot;
            t.localScale = _origLocalScale;
        }

        if (_actor != null)
            HeldFigures.Remove(_actor);
    }

    /// <summary>Restore the real transform and resume the game's transform writes (idempotent).</summary>
    internal void Restore()
    {
        Live.Remove(this);
        RestoreRenderers(); // Issue B — undo the render-on-top swap (idempotent)

        // Cancel any in-flight release glide by completing it instantly (scenario teardown,
        // driver prune/disable, authoritative-move auto-release): same end state, no drift.
        if (_glideActive)
        {
            GameObject? glideRoot = Root;
            FinishGlide(glideRoot != null ? glideRoot.transform : null);
        }

        // Guarantee the pre-grab highlight never persists past release, even if OnGrabHighlight(false)
        // was not delivered (e.g. the grab consumed the highlight, or the actor was torn down under
        // us). ClearHighlight is idempotent and restores the figure's original materials.
        ClearHighlight();
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
            StatPanelSurface.ClearHeldFigure(_holder.Side, Character);
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
