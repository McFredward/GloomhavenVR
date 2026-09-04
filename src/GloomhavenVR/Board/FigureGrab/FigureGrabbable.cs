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
/// "closing fist (grip) grabs the highlighted candidate" fallback for figures — a fist over a
/// crowded board is the canonical accidental gesture (see <c>ProximityGrabber.IsTriggerOnly</c>).
/// The pre-grab hover highlight (<see cref="OnGrabHighlight"/>) is untouched.
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
    /// The PICK VOLUME this figure was adopted on — the SAME collider the driver measures the
    /// election with and the one the <see cref="ProximityGrabber"/> measures its palm reach with.
    /// Held for diagnostics only (the distances printed by <see cref="OnGrabHighlight"/>); nothing
    /// decides anything from it here. Unity-nullable: the figure may die under the grabbable.
    ///
    /// <para>NOT NECESSARILY THE GAME'S OWN COLLIDER. On a figure whose authored pick collider
    /// stops well below the miniature the player can see (the boss dragon: a 1x2x1 capsule over a
    /// 6.7 wu body, ModBuild 291 hardware), the driver builds a taller mod-owned volume and adopts
    /// the figure on THAT — see <c>FigureReachVolume</c>. This field must then hold the volume the
    /// election actually uses, or the highlight's own distance line would report a number no gate
    /// in the system consults. <see cref="SetPickVolume"/> is how the driver swaps it in.</para>
    /// </summary>
    private Collider? _collider;

    private VRHand? _holder;

    // Item 3 — offset-anchor nearest selection: only the figure nearest the OFFSET ANCHOR (where
    // the held mini will appear) stays grabbable for a hand; every loser is suppressed so the
    // ProximityGrabber (which otherwise picks nearest-to-palm) can only take the winner. It carries
    // the PICK VOLUME too (2026-08 accidental-grab report), so a figure nobody has actually reached
    // is a loser as well. Written per hand per frame by FigureGrabDriver.SelectByOffsetAnchor;
    // consumed by AllowsHand below.
    //
    // THE FLAG IS AN UNCONDITIONAL PER-FRAME VETO, not a hint about nearby figures: the driver
    // writes it for EVERY adopted figure on every frame it ticks a hand, however far away. A figure
    // it declined to write is one the grabber (which reads the flags one frame in arrears) was free
    // to highlight on its own 13 cm palm reach — the whole of the "highlighting blitzt bei
    // verschiedenen Figuren auf" defect. See FigureGrabDriver.ApplySuppression for the full account.
    private bool _suppressLeft;
    private bool _suppressRight;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    // Live-pose bases captured at grab (so re-applying the config pose never compounds): the mini's
    // anchor-local scale at board size, and the hand anchor it rides. The held ROTATION is NOT one
    // of these bases — it is a FIXED CONSTANT anchor-LOCAL rotation, never derived from world up,
    // the head or the approach angle, so the mini rides the hand (user #3: "fixed relative to the
    // hand, not the world"). See FigureGrabConfig.HeldUprightRotation.
    private Transform? _anchor;

    // The mini's WORLD (lossy) scale as it stood ON THE BOARD, sampled before the reparent into the
    // hand. It is the SOURCE of the grab-time latch below (and the number the [Size] log prints);
    // it is no longer what the hold re-asserts every frame — see _heldLocalScale.
    private Vector3 _homeWorldScale = Vector3.one;

    // HELD SIZE IS LATCHED AT THE GRAB — the mini's scale in the HAND ANCHOR's frame, captured once
    // when the hold begins and then never recomputed until the next grab.
    //
    // USER REPORT (hardware, 2026-08-11, ModBuild 108, verbatim): "Wenn ich eine oder zwei Figuren
    // in der Hand halte und dann zoome verändern auch die Figuren in meiner Hand ihre Größe - das
    // soll nicht der Fall sein - die Größe soll nur abhängig sein wann sie greift und dann fix in
    // der Hand sein - auch wenn man dabei zoomed."
    //
    // ROOT CAUSE. The diorama zoom is the RIG's own scale (Rig/WorldGrab writes
    // rigRoot.localScale), and the hand anchor hangs under that rig — HandVisuals normalises
    // Socket_Grab against handRoot.parent.lossyScale, so GrabAnchor.lossyScale IS the rig scale
    // exactly, free of any per-style hand scale. The old hold re-derived the mini's anchor-LOCAL
    // scale from a CONSTANT world size every frame (homeWorldScale / anchor.lossyScale), i.e. it
    // pinned the mini's size in WORLD space — but the player's eyes are scaled by that same rig, so
    // a world-constant object changes apparent size with every zoom. Proven from the ModBuild 108
    // log rather than inferred: three grabs in one session printed `boardWorld=1 anchorScale=41.368`,
    // `41.368` and `10.149`, i.e. the same board-size mini rendered at three anchor-local sizes a
    // factor of ~4 apart purely because of the zoom.
    //
    // WHAT THE OLD BEHAVIOUR WAS FOR (do not simply revert it): it was the fix for the earlier MP
    // defect "Die Figuren-Größen ändern sich wenn man sie in die Hand nimmt … so sehe ich beim
    // Remote-Spieler eine andere Größe der Figur in der Hand als er selbst" — the hold used to
    // multiply by the old [FigureGrab] HeldScale family (1.5x, bound per hand style) while the
    // wire carries POSE ONLY, so no two clients could agree. That multiplier stays gone (its
    // config family has since been DELETED — see the note in FigureGrabConfig). Only the SECOND
    // half of that fix — "pin the world size" — changed here; at the instant of the grab the two
    // are identical, so the mini still ENTERS the hand at exactly its board size and simply stops
    // chasing the zoom afterwards.
    //
    // REJECTED ALTERNATIVES.
    //   (a) Delete the per-frame re-assert (TickHeldScale) instead of changing what it asserts.
    //       The tick is above the config gate on purpose: a mini still in the hand when
    //       GrabFigures is toggled off is released by the gate's ReleaseAll on THAT frame and must
    //       not be rendered at a drifted size for the frame in between. Kept, now idempotent.
    //   (b) Latch the WORLD scale at grab. That is what the code already did (the board does not
    //       rescale), i.e. it is the defect.
    //   (c) Latch the raw rig scale number and rebuild a factor from it. Latching the anchor-LOCAL
    //       TRS component is the same value with one fewer assumption: it needs no opinion about
    //       WHICH transform in the chain carries the zoom, so re-posing or rescaling the board (or
    //       anything else moving between the board and the mini) cannot desynchronise it.
    //
    // MULTIPLAYER — the peer does NOT reproduce this latch, and since ModBuild 157 it deliberately
    // makes no attempt to. Position and rotation cross exactly as before
    // (NetFigures.TrySampleHeldSlot / EaseSlot); the SIZE is MEASURED off this transform
    // (HeldSizeFactorOf = lossyScale ÷ homeWorldScale), sent on NetProtocol.ExtIdHeldStretch, and
    // multiplied into the peer's own copy of the figure's board-home scale — one transmitted number
    // in, one size out. Builds through 156 rebuilt it receive-side as
    // homeLocalScale × (senderRigScaleNow / senderRigScaleAtHoldStart) × stretch, and that
    // reconstruction could not see the grab-time size CLAMP below, so peers rendered a
    // deep-zoom grab up to 3.33× too large for the whole hold (three-log evidence, 2026-08-15).
    // NetFigures.RestoreHomeScale puts the home scale back on release: the game never writes a
    // figure's scale, so unlike position and rotation it does NOT heal itself. That half lives in
    // Net/ — see NetFigures.EaseSlot.
    private Vector3 _heldLocalScale = Vector3.one;

    // HELD-FIGURE STRETCH — the manual in-hand scale factor, multiplied ON TOP of the latch above.
    //
    // USER REQUEST (2026-08-11, verbatim): "Ich möchte, dass die Größe der Figur in der Hand
    // änderbar ist. Dabei stelle ich mir vor, dass ich mit der anderen Hand zu der Figur gehe und
    // dann Trigger gedrückt halte und nach innen oder außen schiebe (nach außen heißt größer, nach
    // innen kleiner) und somit die Größe der Figur skaliert."
    //
    // WHY A SEPARATE FACTOR AND NOT A WRITE INTO _heldLocalScale: the latch is the BOARD size at
    // the grab instant and several consumers reason about it as exactly that (the [Size] log, the
    // glide doc, TotalHeldSizeRatio and the capture ceiling it feeds). Folding the gesture into it
    // would make "the size the mini entered the hand at" unrecoverable mid-hold, and the size
    // BOUNDS are stated against that product, so it has to stay factorable. What crosses the wire
    // is neither of the two halves but their rendered PRODUCT (HeldSizeFactorOf → record 30) — see
    // the _latchTotalRatio note for the desync that taught us to send the result rather than a
    // part of it.
    //
    // SCOPE — THIS HOLD ONLY, deliberately. The factor resets to 1 at every grab and is never
    // persisted: the user asked to change "die Größe der Figur in der Hand", not a standing
    // preference, and the one config family that ever meant "preferred held size"
    // ([FigureGrab] HeldScale) was DELETED, with a note in FigureGrabConfig explaining why a
    // local-only multiplier was a multiplayer defect. Release is untouched by construction: both release
    // paths write _origLocalScale / glide toward it (board frame), so the stretch can never leak
    // onto the board — the glide simply starts from the stretched in-hand size (_glideFromScale is
    // read off the transform) and eases home like any other release.
    //
    // Written by FigureStretch (the two-handed gesture driver), clamped THERE against the
    // per-hold factor bounds this class derives from [FigureGrab] StretchScaleMin/Max
    // (GetStretchFactorBounds) before it ever reaches this field.
    private float _stretch = 1f;

    // STRETCH BOUNDS, TOTAL-BASED — the zoom ratio the latch stands at, taken (and possibly
    // clamped) once per hold.
    //
    // USER REQUEST (hardware report 2026-08-11, verbatim): "lass mich die mindestgröße und
    // maximalgröße einer Figur im Debugmenu einstellen. Wenn ich so nah in der Welt reingezommed
    // habe, dass dei figur größer als die maximalgröße ist und ich sie in die Hand nehme solle
    // sie die Maximalgrößer in der Hand haben (selbes Prinzip für die Minimalgröße."
    //
    // WHAT THE NUMBER IS. The latch above freezes the mini's size RELATIVE TO THE HAND at
    // whatever the diorama zoom was at the grab — that is the whole point of the latch, and it is
    // also exactly how a deep zoom-in puts an oversized mini into the hand. This field is the
    // latch's size expressed against the one zoom-independent yardstick available: the size the
    // mini would show next to the hand at the DEFAULT diorama zoom (RigTarget.BaseScale), i.e.
    //     _latchTotalRatio = BaseScale / anchorLossyAtGrab   (…after the grab-time clamp, below).
    // 1.0 for a grab at the default zoom; 4.0 for a grab zoomed in 4× ("die figur größer als die
    // maximalgröße"). The figure's TOTAL held size in those units is _latchTotalRatio × _stretch,
    // and [FigureGrab] StretchScaleMin/Max bound THAT product — the user's words are the
    // Mindest-/Maximalgröße of the FIGURE, not of the gesture, so the bound must catch a size
    // that arrived via zoom exactly as one dragged there.
    //
    // TWO ENFORCEMENT POINTS, both pop-free by placement and each documented at its own method:
    // the GRAB (ApplyGrabTimeStretchClamp — the latch enters the hand already trimmed to the bound)
    // and the GESTURE (GetStretchFactorBounds — the total bounds rebased to this latch's factor
    // envelope). A mid-hold ZOOM is deliberately NOT re-clamped: the latch is zoom-independent by
    // design (the report above this one), and re-clamping a standing size is a pop. Likewise a
    // mid-hold dial change (Min/Max/StretchLimits) affects the next gesture frame and next grab only.
    //
    // MULTIPLAYER — THIS WAS THE DESYNC (user, 3-player hardware session 2026-08-15: "Die Größen
    // der Figuren synchronisieren nicht richtig … hier gab es oft einen Desync"). Until ModBuild
    // 156 only _stretch was sampled for the wire, so the trim the clamp applies to _heldLocalScale
    // never left this machine, and the peer's reconstruction differed from the holder's hand by
    // EXACTLY the clamp for the whole hold. The old note here called that an "accepted divergence
    // class"; the 1:1 ruling has since been restated as absolute, and the divergence was large —
    // all three logs of that session carry the clamp line, worst case a grab implying 10× trimmed
    // to 3×, i.e. peers 3.33× too large. FIXED BY MEASUREMENT: the wire now carries the rendered
    // result (HeldSizeFactorOf), so the clamp is inside the transmitted number and no peer has to
    // know this field exists. It is once again invisible on the wire — because it is already
    // accounted for, not because it is being hidden.
    private float _latchTotalRatio = 1f;

    // Issue B — render-on-top state. The approach is REVERTED: ApplyRenderOnTop is a no-op today
    // (the queue bump punched held minis through walls — the full record is at that method), so
    // these two fields stay null in practice. Kept because RestoreRenderers still runs on both
    // release paths and is idempotent, so grab/release symmetry survives a re-enable. What the
    // approach did: snapshot each held renderer's ORIGINAL shared materials and swap in
    // per-renderer INSTANCE materials whose renderQueue is pushed just past the control board's
    // on-top HUD widgets (queue 4000, ZTest Always, ZWrite off), for the held mini only.
    private const int HeldRenderQueue = 4100;
    private Renderer[]? _heldRenderers;
    private Material[][]? _origSharedMats;

    // HELD-FIGURE STRETCH capture bounds — the held visual's renderers, cached per hold for the
    // stretch gesture's surface-distance capture test (see HeldRenderers()). Reset on grab and
    // dropped with the material bookkeeping on release, so a rebuilt visual on the NEXT hold is
    // re-walked.
    private Renderer[]? _stretchBoundsRenderers;

    // The stretch gesture's capture volume as last measured for THIS hold — diagnostic bookkeeping
    // only, written by FigureStretch's per-frame capture test. See NoteCaptureVolume.
    private float _captureBodyRadiusReal = float.NaN;
    private float _captureCeilingReal = float.NaN;

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

    // R2 hardening: the actor's authoritative board cell at grab time — see
    // AuthoritativeCellChanged for what a mismatch means and who polls it.
    private Point _grabCell;

    /// <summary>Set by OnGrab when a health prop's body rides this hold; consumed on the FIRST
    /// held frame by <see cref="TickHeldScale"/>, which hands the hover's glow count, the home
    /// ghost and the held leaf to <c>ActorPropBody.LogHoldPicture</c>. False for every ordinary
    /// miniature.</summary>
    private bool _holdPictureDue;

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

    /// <summary>
    /// Re-point the diagnostics at the volume the driver actually elects on, after it has decided
    /// whether this figure needs a mod-owned reach extension. Called at most once per adoption,
    /// immediately after construction and before the figure is registered.
    /// </summary>
    internal void SetPickVolume(Collider volume) => _collider = volume;

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
            // TURN-DEADLOCK GATE (user, 2026-08-11: "DEADLOCK … die Gegner haben nicht mehr
            // weitergemacht - sowas darf unter keinen Umständen passieren"). A figure the game's
            // turn machine currently depends on cannot be picked up at all — see FigureBusy for the
            // reconstructed chain and for why the dangerous figure is the attack's TARGET rather
            // than the one whose turn it is. Same shape as the MP grab-lock directly above: the
            // figure simply is not a candidate, so nothing highlights and nothing is broadcast.
            if (FigureBusy.IsBusy(_actor))
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
    ///
    /// <para>IT IS ALSO THE AUTO-RELEASE FOR THE HOLD GATE — but with the PER-FIGURE predicate,
    /// not the grab gate's. For the hand that currently HOLDS this figure the answer is
    /// <c>!FigureBusy.HoldMustEnd</c>: the hold survives other figures' turns and attacks (user
    /// ruling 2026-08-11, "Solange diese eine figure idle its soll sie auch in der Hand bleiben
    /// können, egal was passiert") and is refused only when the game depends on THIS figure or the
    /// figure itself leaves idle. Asking the grab-gate <see cref="FigureBusy.IsBusy"/> here — as
    /// this method originally did — is exactly what dumped a held idle mini out of the hand the
    /// moment any attack resolved anywhere: <c>ProximityGrabber.HealDeadHeld</c> force-releases any
    /// hold whose target stops allowing its hand, through the normal <see cref="OnRelease"/> path.
    /// That heal mechanism is unchanged and is precisely how the per-figure release reaches the
    /// normal glide the ruling asks for. See <see cref="FigureBusy"/> for the predicate split.</para>
    /// </summary>
    public bool AllowsHand(VRHand hand)
    {
        if (_holder != null && ReferenceEquals(_holder, hand))
            return !FigureBusy.HoldMustEnd(_actor);
        return !NetHeldFigures.Owns(_actor)
               && !FigureBusy.IsBusy(_actor)
               && !(hand.Side == HandSide.Left ? _suppressLeft : _suppressRight);
    }

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

        // THE HOVER IS RECORDED SEPARATELY FROM THE GLOW, and that separation is the whole of the
        // walk-in edge handling. The grab system is EDGE-DRIVEN — ProximityGrabber.SetHighlighted
        // raises this on the hover change and never polls — so if the suppression below simply
        // returned, entering walk-in mid-hover would leave a glow standing until the hand moved
        // away, and leaving walk-in would need a re-hover to get it back. Knowing WHO is hovered
        // even while suppressed is what lets TickHighlightMode drive both edges.
        _hovered[(int)hand.Side] = highlighted ? this : null;

        if (highlighted)
        {
            // MP lock: never highlight a figure a REMOTE player is holding (it is grab-locked here).
            if (_highlight.Active || NetHeldFigures.Owns(_actor))
                return;

            // WALK-IN SUPPRESSION (user, ModBuild 286: "möchte ich optional das highlighting der
            // figuren deaktivieren können … Grabbing soll noch ganz normal möglich sein"). Only the
            // glow is skipped: the election that chose this figure, the hover haptic and every grab
            // path already ran and are untouched.
            if (!FigureGrabConfig.HighlightAllowedHere)
            {
                if (!_loggedWalkInSuppress)
                {
                    _loggedWalkInSuppress = true;
                    VRLog.Info("FigureGrab",
                        $"pre-grab highlight SUPPRESSED IN WALK-IN ({hand.Side} near {Describe()}) — "
                        + "the player is standing inside the board, where a proximity glow answers a "
                        + "question nobody is asking. The hover, its haptic and grabbing itself are "
                        + "unchanged; only the glow is skipped, and it returns the moment he steps "
                        + "back out WITHOUT needing a re-hover. Dial: [FigureGrab] "
                        + "HighlightWhileWalkIn (default off). Logged once per session.");
                }
                return;
            }
            // Overlay the figure's OWN meshes with an animated additive glow — NO scale change,
            // occlusion-correct, riding the live animation (see FigureHighlight / FigureOverlay).
            //
            // THE LINE NOW CARRIES THE OVERLAY'S OWN MEASUREMENT, not a boolean. In the ModBuild 293
            // log ElderDrakeID engaged six times and every line said the glow was applied, while the
            // user reported no highlight on that figure at all — a pair a boolean cannot separate.
            // See FigureHighlight for what the report says and why it is the term that settles it.
            bool glow = ApplyHighlightOverlay(root, out string overlay);
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("FigureGrab",
                $"pre-grab highlight ENGAGED ({hand.Side} near {Describe()}, {DescribeReach(hand)}) — "
                + "animated additive glow "
                + (glow ? $"overlaid on the figure's own meshes (wall-occluded, no scale change): {overlay}."
                        : $"UNAVAILABLE — no highlight: {overlay}."));
        }
        else
        {
            ClearHighlight();
        }
    }

    /// <summary>
    /// Build the hover overlay over WHATEVER THIS ACTOR'S BODY ACTUALLY IS. One method, because two
    /// call sites raise the same glow and a body rule that lived in only one of them would light a
    /// door on hover but not when walk-in mode released under a standing hover.
    ///
    /// <para><b>THE ORDINARY CASE IS UNCHANGED</b> — the actor root, restricted to
    /// <c>m_AnimatedGameObject</c>, with the selection ring excluded.</para>
    ///
    /// <para><b>THE EXCEPTION (2026-09-03)</b> is an actor whose subtree is empty: a prop
    /// configured for health is given an invisible <c>PropDummyObject</c> actor, and the ModBuild
    /// 396 log's "NOTHING TO GLOW — no clonable renderer survived the filters" is that emptiness
    /// reported correctly and consumed by nobody. Its body is its host prop's visual, so the glow
    /// goes there. <see cref="ActorPropBody"/> returns null for every ordinary miniature and for
    /// every ordinary door (which has no actor at all), so this branch is unreachable for them.</para>
    ///
    /// <para>No ring is excluded on that path: the ring belongs to the actor, not to the prop, and
    /// the prop's subtree cannot contain it.</para>
    /// </summary>
    private bool ApplyHighlightOverlay(GameObject root, out string overlay)
    {
        // ModBuild 400: glow the part that would actually ride the hand — the door LEAF — and not
        // the arch it stands in. A glow is a promise of what the hand takes; it must not promise
        // the frame. Falls back to the whole body only when no leaf could be isolated, in which
        // case the hold is refused too (see ActorPropBody.HeldPartFor) and the glow is the one
        // signal left that the object is a health prop.
        GameObject? propBody = ActorPropBody.HeldPartFor(_actor) ?? ActorPropBody.BodyFor(_actor);
        if (propBody != null)
            return _highlight.Apply(propBody, propBody, null, Describe(), out overlay);
        return _highlight.Apply(root, _actor.m_AnimatedGameObject,
                                _actor.m_Hilight != null ? _actor.m_Hilight.transform : null,
                                Describe(), out overlay);
    }

    /// <summary>
    /// WHERE THE HAND ACTUALLY WAS when this figure lit up — the two distances that tell an
    /// ELECTION apart from a LEAK, in the same real millimetres the dial is set in.
    ///
    /// <para>Written because the ModBuild 106 log proved highlights and elections were not the same
    /// event but not which distance the extra ones fired at (that census is in
    /// <c>FigureGrabDriver.ApplySuppression</c>). The pinch distance is the one the driver's radius
    /// gates; the palm distance is the one the <see cref="ProximityGrabber"/>'s own 13 cm reach
    /// gates. So the line is binary: a highlight whose pinch distance is inside the printed radius
    /// came from an election and the radius is simply set too wide; one that engages FAR outside
    /// it, near the palm reach instead, means something is highlighting past the driver's veto
    /// again.</para>
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
        // The hover record goes even when there was no glow to clear: this is also the path a grab
        // and a release-glide take (Restore / TryBeginGlide), and a stale entry there would have
        // TickHighlightMode re-light a figure that is no longer under anybody's hand.
        for (int i = 0; i < _hovered.Length; i++)
            if (ReferenceEquals(_hovered[i], this))
                _hovered[i] = null;

        if (!_highlight.Active)
            return;
        _highlight.Clear();
        VRLog.Info("FigureGrab", $"pre-grab highlight CLEARED ({Describe()}).");
    }

    /// <summary>
    /// The figure each hand is currently hovering, indexed by <see cref="HandSide"/> — recorded
    /// whether or not the glow was actually applied. See <see cref="OnGrabHighlight"/> for why the
    /// two are separate, and <see cref="TickHighlightMode"/> for what reads it.
    /// </summary>
    private static readonly FigureGrabbable?[] _hovered = new FigureGrabbable?[2];

    /// <summary>The walk-in verdict this pass last acted on, so only the EDGES do work.</summary>
    private static bool _highlightAllowedLast = true;

    private static bool _loggedWalkInSuppress;

    /// <summary>
    /// Drive the pre-grab glow across a WALK-IN EDGE, in both directions.
    ///
    /// <para>The grab system raises <see cref="OnGrabHighlight"/> on hover changes only and never
    /// polls, so nothing else in the pipeline would notice the player stepping into or out of the
    /// board while a figure is already under their hand. Both directions matter and both are the
    /// part that is easy to get wrong: entering walk-in must CLEAR a glow that is already standing,
    /// and leaving it must bring the glow back WITHOUT requiring a re-hover.</para>
    ///
    /// <para>Edge-gated, so the steady state is one bool compare — this runs every frame. It reads
    /// the same narrow latch the wall fade uses (<c>WallSegmentFade.WalkInsideEngaged</c>), which
    /// each client evaluates from its own head, so there is nothing here to synchronise: the
    /// pre-grab glow has never been a wire field and no peer has ever seen it (a remote hold is a
    /// local VETO on it, via <c>NetHeldFigures.Owns</c>, and that is the only net term involved).</para>
    /// </summary>
    private static void TickHighlightMode()
    {
        bool allowed = FigureGrabConfig.HighlightAllowedHere;
        if (allowed == _highlightAllowedLast)
            return;
        _highlightAllowedLast = allowed;

        for (int i = 0; i < _hovered.Length; i++)
        {
            FigureGrabbable? g = _hovered[i];
            if (g == null)
                continue;

            if (!allowed)
            {
                // ClearHighlight would drop the hover record with it, and the hand has NOT stopped
                // hovering — it is the mode that changed. Clear the visual only.
                if (g._highlight.Active)
                {
                    g._highlight.Clear();
                    VRLog.Info("FigureGrab",
                        $"pre-grab highlight CLEARED ({g.Describe()}) — walk-in mode engaged under a "
                        + "standing hover; the hover itself is untouched and grabbing still works.");
                }
                continue;
            }

            GameObject? root = g.Root;
            if (root == null || g._highlight.Active || NetHeldFigures.Owns(g._actor))
                continue;
            if (g.ApplyHighlightOverlay(root, out string overlay))
                // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
                // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
                VRLog.Note("FigureGrab",
                    $"pre-grab highlight ENGAGED ({g.Describe()}) — walk-in mode released under a "
                    + $"standing hover, so the glow returns without needing a re-hover: {overlay}.");
        }
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

        // The board WORLD size, sampled while the mini is still standing on the board. Note this is
        // read AFTER the re-grab FinishGlide above, so a re-grab mid-glide latches the true board
        // size and not a mid-glide sample.
        _homeWorldScale = t.lossyScale;

        // THE BODY, FOR AN ACTOR THAT HAS NONE (2026-09-03 ruling). A prop configured for health is
        // given an invisible PropDummyObject actor; its host prop's visual is what the player sees.
        // Bringing it into the actor root here — BEFORE the ghost is cloned and before the hand
        // reparent below — is the whole of "die tür ist sichtbar in der Hand" AND "hinterlässt ein
        // ghost": the ghost is Instantiated from this very object (FigureGhosts.GhostSource) and the
        // hand reparents this very object, so one attachment serves both and there is no second
        // writer of the door's pose to drift. Strict no-op for every ordinary miniature, and an
        // ordinary door has no actor at all. Undone on every release path — see Restore and
        // FinishGlide. See ActorPropBody for the link and for the Apparance freeze it carries.
        ActorPropBody.Hold(_actor, t);

        // TASK #3 — leave a translucent ghost at the figure's HOME board pose while it is held.
        // Capture the pose from the visual (animated) object BEFORE we reparent it into the hand, and
        // build the frozen snapshot from its current (board) pose. FigureGhosts reconciles teardown
        // off HeldFigures/NetHeldFigures, so any release path removes it. (Multiplayer: a REMOTE
        // player's grab spawns the same ghost via NetFigures.)
        GameObject ghostSrc = FigureGhosts.GhostSource(_actor) ?? root;
        FigureGhosts.NotifyHeld(_actor, ghostSrc.transform.position, ghostSrc.transform.rotation);

        // Suppress the game's per-frame transform writes for THIS actor only. The HAND is recorded
        // with it: a player may hold one figure per hand, and the two-handed figure sync has to say
        // which mini is in which hand (NetProtocol.ExtIdSecondFigure).
        HeldFigures.Add(_actor, hand.Side);

        // Snapshot the authoritative cell so we can auto-release if the game moves the figure
        // on the board while it is held (R2 hardening).
        CActor? ca = Character;
        _grabCell = ca != null ? ca.ArrayIndex : default;

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board world-scale as
        // it enters the hand (no scale pop); the rotation is re-written by ApplyHeldPose below.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;

        // THE SIZE LATCH (see _heldLocalScale for the user report and the root cause). This is the
        // whole of "die Größe soll nur abhängig sein wann sie greift": the mini's size in the hand's
        // own frame, taken at this instant and frozen. Because the grab-time anchor scale IS the
        // zoom at this instant, the value equals the board size right now — and holding it fixed is
        // what makes a later zoom leave the mini in the hand alone.
        _heldLocalScale = AnchorLocalScale(anchor, _homeWorldScale);
        _stretch = 1f; // the manual stretch is per-hold: every grab starts at the board size
        ApplyGrabTimeStretchClamp(anchor); // …then the TOTAL size bound may trim the latch itself
        _stretchBoundsRenderers = null; // per-hold too: the visual may differ between holds
        _captureBodyRadiusReal = float.NaN; // "never measured" until a free hand runs the test
        _captureCeilingReal = float.NaN;
        _uprightBase = CaptureUprightBase(anchor);
        _attached = true;

        ApplyHeldPose();
        ApplyRenderOnTop(); // Issue B — REVERTED, a no-op today (see the method)
        Live.Add(this);
        // The picture of a health-prop hold is read ONE FRAME IN, not here: a writer that lands on
        // the leaf's materials after the grab (the ghost build, the highlight teardown) would be
        // invisible to a sample taken on the grab frame itself.
        _holdPictureDue = ActorPropBody.IsHeld(_actor);

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character, hand.Side);

        // Issue A — one-shot grab diagnostic: the FIXED anchor-LOCAL rotation chosen for the hold
        // (grab-angle-independent; rides the hand), plus WHERE THE THREE ANGLES PUT THE MINI'S OWN
        // AXES in the anchor's frame — the numbers that let "yaw feels like tilt" be checked
        // against a quaternion instead of against a sensation. up.y near +/-1 means the mini stands
        // along the palm normal, so yaw is a clean spin about the vertical you see; the further
        // up.y is from that, the more all three angles read as a tumble and feel alike.
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

        // SIZE probe — the line that PROVED the zoom-follows-the-hand defect (see _heldLocalScale).
        // anchorScale is the LIVE diorama scale; heldLocal is the frozen latch, and the two are
        // ALLOWED to disagree from here on — that is the fix, not a drift. heldWorld equals
        // boardWorld on this frame by construction, UNLESS the grab-time size clamp trimmed the
        // latch (its own [Size] CLAMP line directly above says so when it did). latchRatio is the
        // total held size in default-zoom units — the number the bounds govern.
        VRLog.Info("FigureGrab",
            $"[Size] {Describe()} boardWorld={_homeWorldScale.x:0.####} heldWorld={t.lossyScale.x:0.####} " +
            $"anchorScale={anchor.lossyScale.x:0.###} heldLocal={_heldLocalScale.x:0.######} " +
            $"latchRatio={_latchTotalRatio:0.###} — size LATCHED "
            + "at the grab and fixed in the hand from now on; a zoom mid-hold no longer resizes it, and "
            + "the release glide eases it back to the board's live size.");
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
        // (mirror-correct); legacy mode lays it flat (tilt only, mirror-invariant). _uprightBase is
        // identity unless the grab captured a world-upright start, so the tuned angles stay OFFSETS
        // either way — from "standing up" with the option on, from the hand with it off.
        t.localRotation = _uprightBase * (FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(side)
            : FigureGrabConfig.HeldPalmRotation());

        // SIZE — the value LATCHED at the grab (see _heldLocalScale for the report, the root cause
        // and the rejected alternatives): the latch is the board world size at the grab instant, so
        // the figure enters the hand at exactly the size it stood on the board, and it is never
        // RE-DERIVED from the live anchor again, so a pinch-zoom mid-hold leaves it alone.
        // Assigned here as well as in ReassertHeldScale so the live-tune path (ReapplyAll) writes a
        // complete pose; both write the same frozen vector, so this is idempotent.
        t.localScale = HeldLocalScale();
    }

    /// <summary>
    /// The size this mini is rendered at while held: the anchor-LOCAL scale LATCHED at the grab
    /// (<see cref="_heldLocalScale"/>), never re-derived from the live anchor, times the manual
    /// two-hand stretch (<see cref="_stretch"/>, 1 unless the player stretched THIS hold). Constant
    /// between gesture frames, per figure — two minis grabbed at two different zooms keep two
    /// different latches, which is what the report's "eine oder zwei Figuren" needs.
    /// </summary>
    private Vector3 HeldLocalScale() => _heldLocalScale * _stretch;

    /// <summary>The manual in-hand stretch factor of this hold (1 = untouched). Read by
    /// <c>Net.NetFigures</c> as the wire sample for <c>NetProtocol.ExtIdHeldStretch</c>.</summary>
    internal float Stretch => _stretch;

    /// <summary>
    /// GRAB-TIME half of the total size bound (see <see cref="_latchTotalRatio"/> for the request
    /// and the semantics). Computes the zoom ratio the fresh latch stands at — the mini's held
    /// size relative to its board-home size at the DEFAULT diorama zoom — and, while
    /// [FigureGrab] StretchLimits is on, scales the latch so that ratio lands exactly ON the
    /// violated bound ("solle sie die Maximalgrößer in der Hand haben"). Runs BEFORE the first
    /// <see cref="ApplyHeldPose"/> of the hold, so the clamped size is the first held frame ever
    /// rendered — no pop, only a grab transition the player asked for. Degenerate inputs (dead
    /// rig, zero anchor scale, non-finite ratio) leave the latch alone: a bounds feature must
    /// never be the thing that breaks a grab.
    /// </summary>
    private void ApplyGrabTimeStretchClamp(Transform anchor)
    {
        _latchTotalRatio = 1f;
        float baseScale = Rig.RigTarget.BaseScale;
        float anchorScale = anchor.lossyScale.x;
        if (baseScale <= 1e-6f || anchorScale <= 1e-6f)
            return;
        float ratio = baseScale / anchorScale;
        if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f)
            return;
        _latchTotalRatio = ratio;

        if (!FigureGrabConfig.StretchLimitsEnabled)
            return; // limits off: the latch keeps the true grab-zoom size, whatever it is
        float min = FigureGrabConfig.StretchScaleMinValue;
        float max = FigureGrabConfig.StretchScaleMaxValue;
        float clamped = Mathf.Clamp(ratio, min, max);
        if (Mathf.Approximately(clamped, ratio))
            return;
        _heldLocalScale *= clamped / ratio; // uniform trim — the latch's own frame, no reparent
        _latchTotalRatio = clamped;
        VRLog.Info("FigureGrab",
            $"[Size] {Describe()} grab-time size CLAMP: the zoom at grab implies "
            + $"{ratio:0.###}× of the figure's default-zoom size, outside the total bound "
            + $"[{min:0.##} .. {max:0.##}] — latch trimmed so it enters the hand at exactly "
            + $"{clamped:0.###}× ([FigureGrab] StretchScaleMin/Max; StretchLimits=false disables "
            + "this). Peers now see the trimmed size too — record 30 carries the MEASURED held "
            + "size, so the trim is inside the transmitted number (ModBuild 157; through 156 this "
            + "line marked a real desync of exactly clamped/implied).");
    }

    /// <summary>
    /// The GESTURE half of the total size bound: the per-hold factor envelope
    /// <see cref="FigureStretch"/> must clamp into, derived by converting the TOTAL bounds
    /// ([FigureGrab] StretchScaleMin/Max) at the latch's own ratio — total = ratio × factor, so
    /// factor ∈ [Min/ratio .. Max/ratio]. Recomputed per call, so a live dial change (Min, Max,
    /// or the StretchLimits switch itself) governs the very next gesture frame. With limits OFF
    /// only the technical floor remains (<see cref="FigureGrabConfig.StretchHardFloor"/> — the
    /// scale must stay positive and finite, nothing else).
    /// </summary>
    internal void GetStretchFactorBounds(out float min, out float max)
    {
        if (!FigureGrabConfig.StretchLimitsEnabled)
        {
            min = FigureGrabConfig.StretchHardFloor;
            max = float.MaxValue;
            return;
        }
        float ratio = Mathf.Max(_latchTotalRatio, 1e-6f);
        min = FigureGrabConfig.StretchScaleMinValue / ratio;
        max = FigureGrabConfig.StretchScaleMaxValue / ratio;
    }

    /// <summary>
    /// Write the manual stretch factor and re-assert the rendered size in the same call, so the
    /// mini tracks the gesture hand within the frame. The CALLER (<see cref="FigureStretch"/>)
    /// owns the clamp — this is a dumb store on purpose, so the wire sampler and the renderer can
    /// never see two differently-clamped values.
    /// </summary>
    internal void SetStretch(float factor)
    {
        _stretch = factor;
        ReassertHeldScale();
    }

    /// <summary>
    /// The held visual's renderers, for <see cref="FigureStretch"/>'s bounds-based capture test —
    /// every Renderer under the held root (the same walk the render-on-top path used), fetched
    /// ONCE per hold on first use and cached, because the capture test runs every frame for a
    /// free hand and <c>GetComponentsInChildren</c> allocates. Renderer WORLD bounds are read by
    /// the caller per frame, so the cached array stays correct as the mini stretches — bounds
    /// grow with the scale on their own. Entries can go Unity-null mid-hold if the game rebuilds
    /// the visual; the consumer must null-check each. Null when not attached to a hand.
    /// </summary>
    internal Renderer[]? HeldRenderers()
    {
        GameObject? root = Root;
        if (!_attached || root == null)
            return null;
        return _stretchBoundsRenderers ??= root.GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>The held mini's centre in world space (its root position — deliberately NOT a
    /// collider surface point, which would shrink and grow WITH the gesture and feed the scale
    /// back into the distance that drives it). False when not attached to a hand.</summary>
    internal bool TryGetHeldCenter(out Vector3 world)
    {
        world = default;
        GameObject? root = Root;
        if (!_attached || root == null)
            return false;
        world = root.transform.position;
        return true;
    }

    /// <summary>The grabbable currently ATTACHED to <paramref name="side"/>'s hand, or null. Scans
    /// <see cref="Live"/> (≤ 2 entries). Gliding minis are deliberately absent — a released figure
    /// cannot be stretched.</summary>
    internal static FigureGrabbable? HeldBy(HandSide side)
    {
        foreach (FigureGrabbable g in Live)
        {
            if (g._attached && g._holder != null && g._holder.Side == side)
                return g;
        }
        return null;
    }

    /// <summary>
    /// THE SEND-SIDE SAMPLE for <c>NetProtocol.ExtIdHeldStretch</c>: the size the hold on
    /// <paramref name="actor"/> is rendered at, as a multiple of that figure's OWN board-home size.
    /// 1 when it is not in a hand here (unknown, gliding, or remote) — a glide reads as "board
    /// size", so the wire eases the peer's copy home over the same window the local glide plays.
    ///
    /// <para>IT IS MEASURED, NOT ASSEMBLED (user, 3-player hardware session 2026-08-15: "Die Größe
    /// einer Figur MUSS zwingend immer 1:1 genau die sein die der Spieler auch in der Hand hat").
    /// The number is read straight off the transform the holder is looking at — live world size ÷
    /// board world size — so it carries the grab-time latch, the grab-time size CLAMP
    /// (<see cref="ApplyGrabTimeStretchClamp"/>), the live diorama zoom and the manual stretch
    /// TOGETHER, and it cannot fall out of step with any of them. Every previous version sent
    /// <see cref="_stretch"/> alone and let the peer rebuild the rest; the clamp was invisible to
    /// that rebuild, and all three logs of the 2026-08-15 session show it firing (a grab implying
    /// 10× trimmed to 3× ⇒ peers 3.33× too large). Measuring is also the only shape that survives a
    /// future size input nobody has thought of yet: it reports the RESULT.</para>
    ///
    /// <para>Uniform by construction — every writer on this path scales all three axes by the same
    /// factor (<see cref="HeldLocalScale"/>, <see cref="ApplyGrabTimeStretchClamp"/>) — so X speaks
    /// for the vector, exactly as <see cref="NoteClothScale"/> already assumes.</para>
    /// </summary>
    internal static float HeldSizeFactorOf(ActorBehaviour actor)
    {
        FigureGrabbable? g = AttachedTo(actor);
        if (g == null)
            return 1f;
        GameObject? root = g.Root;
        float home = g._homeWorldScale.x;
        if (root == null || home <= 1e-6f)
            return 1f; // degenerate: report "board size" rather than a division
        float factor = root.transform.lossyScale.x / home;
        return float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f ? 1f : factor;
    }

    /// <summary>The grabbable currently ATTACHED to <paramref name="actor"/>'s hand, or null when
    /// this figure is not in a local hand (unknown, gliding, or remote). Scans <see cref="Live"/>
    /// (≤ 2 entries), the same walk <see cref="HeldBy"/> does from the other end.</summary>
    internal static FigureGrabbable? AttachedTo(ActorBehaviour actor)
    {
        if (actor == null)
            return null;
        foreach (FigureGrabbable g in Live)
        {
            if (g._attached && ReferenceEquals(g._actor, actor))
                return g;
        }
        return null;
    }

    /// <summary>The zoom ratio this hold's size latch stands at, in default-zoom units, AFTER the
    /// grab-time size clamp — see <see cref="_latchTotalRatio"/>. Diagnostic read: it is the half of
    /// <see cref="TotalHeldSizeRatio"/> that the wire used to be blind to.</summary>
    internal float LatchTotalRatio => _latchTotalRatio;

    /// <summary>
    /// This hold's TOTAL size in default-zoom units — <see cref="_latchTotalRatio"/> ×
    /// <see cref="_stretch"/>, i.e. the very product <c>[FigureGrab] StretchScaleMin/Max</c> bound.
    /// 1 for a hold at the default zoom with no stretch.
    ///
    /// <para>It is the ZOOM-INDEPENDENT size variable, which is why it — and not the board-relative
    /// wire factor — is what <see cref="FigureStretch"/>'s capture test scales its sanity ceiling
    /// by: that test works in REAL metres at the hand (world ÷ rig scale), and real size is
    /// proportional to exactly this product.</para>
    /// </summary>
    internal float TotalHeldSizeRatio => _latchTotalRatio * _stretch;

    /// <summary>
    /// Record the stretch gesture's capture volume for this hold, as
    /// <see cref="FigureStretch.CaptureDistanceReal"/> last computed it: the radius of the visible
    /// body it trusted, and the sanity ceiling that decided what "trusted" meant — both in REAL
    /// metres at the hand.
    ///
    /// <para>Bookkeeping only; nothing reads it to make a decision. It exists so the one
    /// <c>[SizeSync]</c> diagnostic can print the volume the player is actually reaching into,
    /// measured rather than re-derived, and so the failing state has a name in the log:
    /// <paramref name="bodyRadiusRealMeters"/> is <see cref="float.PositiveInfinity"/> exactly when
    /// EVERY renderer was excluded and the test fell back to the centre distance — the shape of the
    /// 2026-08-15 report. Written by the free hand's per-frame capture test, so it is stale (and
    /// starts <see cref="float.NaN"/> = "never measured") whenever no hand is free to gesture.</para>
    /// </summary>
    internal void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters)
    {
        _captureBodyRadiusReal = bodyRadiusRealMeters;
        _captureCeilingReal = ceilingRealMeters;
    }

    /// <summary>Radius of the visible body the capture test last trusted, real metres at the hand.
    /// NaN = not measured this hold; +Inf = every renderer was excluded (centre fallback).</summary>
    internal float CaptureBodyRadiusRealMeters => _captureBodyRadiusReal;

    /// <summary>The sanity ceiling the capture test last applied, real metres — grows with
    /// <see cref="TotalHeldSizeRatio"/>, see <see cref="FigureStretchMath"/>.</summary>
    internal float CaptureCeilingRealMeters => _captureCeilingReal;

    /// <summary>
    /// Convert a WORLD scale into the scale a child of <paramref name="anchor"/> needs to render at
    /// that world size, using the anchor's <c>lossyScale</c> AT THE MOMENT OF THE CALL. Used exactly
    /// once per hold, to take the grab-time latch. Degenerate/zero anchor axes fall back to the
    /// world scale rather than dividing by zero.
    /// </summary>
    private static Vector3 AnchorLocalScale(Transform anchor, Vector3 worldScale)
    {
        Vector3 a = anchor.lossyScale;
        return new Vector3(
            Mathf.Abs(a.x) > 1e-6f ? worldScale.x / a.x : worldScale.x,
            Mathf.Abs(a.y) > 1e-6f ? worldScale.y / a.y : worldScale.y,
            Mathf.Abs(a.z) > 1e-6f ? worldScale.z / a.z : worldScale.z);
    }

    /// <summary>
    /// Re-assert every held mini's LATCHED grab-time size. Called once per frame from
    /// <see cref="FigureGrabDriver"/>.
    ///
    /// <para>It re-writes the FROZEN latch (<see cref="_heldLocalScale"/>), so it is idempotent and
    /// a zoom moves nothing. WHY IT STILL EXISTS: it sits above the config gate because a mini
    /// still in the hand when GrabFigures is toggled off is released by the gate's ReleaseAll on
    /// THIS frame, and it must not be rendered at a size some other writer has touched for the
    /// frame in between. It is also the only thing that would reveal such a writer at all — the
    /// game's own transform writers are prefix-skipped for held actors
    /// (<see cref="ActorBehaviour_HeldTransform_Patch"/>) and none of them writes scale, so today
    /// this is cheap insurance rather than a correction. One vector store per held figure (at most
    /// two).</para>
    /// </summary>
    internal static void TickHeldScale()
    {
        foreach (FigureGrabbable g in Live)
        {
            g.ReassertHeldScale();
            if (g._holdPictureDue)
            {
                g._holdPictureDue = false;
                ActorPropBody.LogHoldPicture(g._actor, g._highlight.LastCloned,
                                             g._highlight.LastContainerActive,
                                             FigureGhosts.GhostFor(g._actor));
            }
        }

        // FIGURE RESCALE — keep the SIMULATED parts (capes/cloth) in step with whatever size was
        // just written, here and on the peer's mirror alike. Deliberately ridden on this step
        // rather than made a new one: it is the correction that belongs to the size write, it must
        // run in the same place (above the config gate, so a figure the gate releases on THIS frame
        // still gets its cloth back), and a new step would edit the locked frame order. Strict
        // no-op unless something is being resized. See FigureCloth for the whole account.
        FigureCloth.Tick();

        // THE FREE HAND DISTURBS THE CLOTH (user, ModBuild 286: "sie sollen auch auf meine andere
        // Hand reagieren, wenn ich mit der freien VR hand diese elemente berühre"). Rides here for
        // the same three reasons the line above does: it belongs to the per-frame job that owns
        // held figures, it must run above the config gate, and a new step would edit the locked
        // frame order. Strict no-op with nothing held. See FigureClothHands.
        FigureClothHands.Tick();

        // WALK-IN HIGHLIGHT EDGES. Rides here for the same reason as the two above, and because it
        // must run above the config gate: a figure hovered on the frame the gate releases every
        // grabbable must still have its glow taken off it. One bool compare in the steady state.
        TickHighlightMode();
    }

    /// <summary>The figure's root GameObject, for the sibling passes that need the subtree
    /// (<see cref="FigureClothHands"/>). Null once the actor is gone.</summary>
    internal GameObject? RootObject => Root;

    private void ReassertHeldScale()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null)
            return;
        Transform t = root.transform;
        t.localScale = HeldLocalScale();
        NoteClothScale(t);
    }

    /// <summary>
    /// Report this figure's rendered size RELATIVE to the board size it was grabbed at, so
    /// <see cref="FigureCloth"/> can re-seed the cape/cloth simulation that does not follow a
    /// transform scale on its own (user, ModBuild 137: "Alle Teile der Figur sollen korrekt
    /// mitskallieren"). World scale, not local: the mini is reparented into the hand, so its LOCAL
    /// scale means nothing across the grab boundary, while <see cref="_homeWorldScale"/> is the same
    /// board world size the latch itself is derived from. A diorama zoom is therefore included by
    /// construction — it genuinely does change the figure's world size, which is the only size the
    /// cloth solver knows about.
    /// </summary>
    private void NoteClothScale(Transform t)
    {
        float home = _homeWorldScale.x;
        if (home > 1e-6f)
            FigureCloth.Note(t.gameObject, t.lossyScale.x / home);
    }

    /// <summary>
    /// Issue B — NO-OP TODAY (reverted; the body says why, and that record must not be deleted).
    /// It WOULD push the held mini's renderers just past the control board's on-top HUD widgets so
    /// a mini held in FRONT of the opaque board is never occluded by it: snapshot each renderer's
    /// original SHARED materials, swap in per-renderer INSTANCE materials (so no shared bundle
    /// material is mutated globally) with the renderQueue bumped to <see cref="HeldRenderQueue"/>,
    /// leaving ZTest/ZWrite at the shader's defaults so only the draw ORDER changes. Undone by
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
        // Piggybacked renderer bookkeeping teardown: the stretch capture cache must not pin a
        // released figure's renderers (Unity objects) across the rest of the session.
        _stretchBoundsRenderers = null;
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        // The release may already have happened this frame: the driver's hold-gate auto-release
        // (AutoReleaseToBoard) begins the glide, and ProximityGrabber.HealDeadHeld then delivers
        // its own OnRelease for the same figure when it notices AllowsHand refusing. A gliding
        // figure is already on its way home — nothing left to do, and falling through to the
        // Restore below would CANCEL the glide into an instant snap.
        if (_glideActive)
            return;

        // Stale-hand hardening: an auto-release clears _holder, and the ex-holder's grabber may
        // still deliver a trigger-up OnRelease afterwards — after the OTHER hand legitimately
        // re-grabbed the mini. Acting on that stale release would tear down the new hand's hold.
        // A release is only honoured from the hand that owns the hold (or when no hand does —
        // the belt-path cleanups pass through Restore's idempotence below).
        if (_holder != null && !ReferenceEquals(_holder, hand))
            return;

        // HOLD GATE (user ruling 2026-08-11): a release forced because the game started depending
        // on this figure takes the SAME glide as a user release — "soll sie zurück aufs Feld
        // gehen, aber auch mit der üblichen Animation als hätte der User sie losgelassen". The
        // glide's 0.28 s of continued bar-hide is safe because a coroutine kill needs a
        // DEACTIVATION EDGE and the glide has none (the host is already inactive and is only ever
        // re-ACTIVATED at the landing) — see the FigureBusy class doc, deadlock-safety §2. The only
        // paths that still restore instantly are the authoritative-cell release (the game already
        // moved the figure elsewhere; gliding to the STALE home pose would be wrong) and the
        // teardown/fallback paths below.
        bool forced = FigureBusy.HoldMustEnd(_actor, out string forcedWhy);

        // GLIDE-BACK: ease the mini from the hand back to its home pose (~0.28 s, ease-out).
        // Falls back to the exact old instant path whenever a safe glide is impossible (dead
        // root/parent, teardown mid-hold).
        if (TryBeginGlide())
        {
            VRLog.Info("FigureGrab", forced
                ? $"{hand.Side} released figure ({Describe()}) — game-forced ({forcedWhy}) — "
                  + $"gliding home ({GlideDurationSeconds:0.00}s), the usual release glide."
                : $"{hand.Side} released figure ({Describe()}) — gliding home ({GlideDurationSeconds:0.00}s).");
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
    ///
    /// <para>SIZE ON RELEASE — the glide is also what makes the grab-time size latch safe. Because
    /// the hold freezes the mini's size relative to the HAND, a player who zooms while holding is
    /// carrying a mini whose WORLD size no longer matches the board; it has to come back to the
    /// board's size, and the project forbids doing that by popping. Both halves fall out of this
    /// method's existing shape and need no size-specific code:</para>
    /// <list type="bullet">
    ///   <item>the reparent below keeps the world pose, so <c>_glideFromScale</c> is exactly the
    ///   size the mini was rendered at in the hand — the glide starts where the eye left it;</item>
    ///   <item><c>_origLocalScale</c> is a LOCAL scale under <c>_origParent</c>, i.e. under the
    ///   board's own hierarchy. Writing a local value reproduces whatever world size that hierarchy
    ///   currently has, so the mini lands at the board's LIVE size — including when the board was
    ///   re-posed or rescaled during the hold. Latching a world size here instead would be the same
    ///   mistake this round removed from the hold.</item>
    /// </list>
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
        _glideFromScale = t.localScale; // the in-hand size, preserved by the worldPositionStays reparent
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
        // Size rides the same curve as the position: a mini released after a mid-hold zoom eases
        // from its latched in-hand size back to the board's live size instead of snapping there.
        t.localScale = Vector3.LerpUnclamped(_glideFromScale, _origLocalScale, e);
        NoteClothScale(t); // the glide is a size ANIMATION — the cape rides it home too
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
            NoteClothScale(t); // back at board size → FigureCloth restores the authored coefficients
        }

        // The prop body goes home LAST, after the actor root has landed on its own home pose, so it
        // is restored from a settled parent rather than mid-glide. Idempotent and a no-op for every
        // ordinary miniature. Both release paths call it — this one and Restore — because a hold can
        // end through either and a door left parented under a dead actor never comes back.
        ActorPropBody.Release(_actor);

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
                // Local (not world) scale, so the mini lands at the board's LIVE size even if the
                // board moved or rescaled during the hold — same reasoning as the glide's landing.
                // This path changes the size in one frame, which is deliberate and is NOT the
                // "popping" the project forbids: it is reached only by a SAFETY release whose whole
                // value is that it takes no time (authoritative cell moved, teardown, config gate)
                // or as TryBeginGlide's fallback when a glide is impossible at all (dead
                // root/parent). The ordinary player release glides, and since the 2026-08-11 hold
                // ruling the game-forced hold-gate release glides too. See OnRelease.
                t.localScale = _origLocalScale;
                NoteClothScale(t); // back at board size → FigureCloth restores the authored coefficients
            }
            _attached = false;
            _anchor = null;
        }

        // The prop body of a health-bearing prop goes home too (idempotent; a no-op for every
        // ordinary miniature). It is OUTSIDE the `_attached` block on purpose: this method is also
        // the safety path — teardown, an authoritative move, the config gate — and a body that was
        // attached must be put back whether or not the actor root still is.
        ActorPropBody.Release(_actor);

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
    /// HOLD-GATE auto-release (user ruling 2026-08-11): send the held mini home because the game
    /// now depends on it — its own animation started, a choreographer wait names it, or its own
    /// action is being resolved (<see cref="FigureBusy.HoldMustEnd"/>). Takes the NORMAL release
    /// glide ("mit der üblichen Animation als hätte der User sie losgelassen") via
    /// <see cref="TryBeginGlide"/>, which also frees the hand and undocks the stat panel exactly
    /// like a trigger-up release; falls back to the instant <see cref="Restore"/> only when a
    /// glide is impossible at all (dead root/parent, teardown mid-hold). Called by
    /// <c>FigureGrabDriver.AutoReleaseMovedFigures</c>; the ex-holder's ProximityGrabber notices
    /// the ended hold on its next tick (AllowsHand/heal) and its follow-up OnRelease is absorbed
    /// by the glide guard there. MULTIPLAYER: nothing new on the wire — peers stream the glide and
    /// see the held slot clear on arrival, exactly as for a user release.
    /// </summary>
    internal void AutoReleaseToBoard(string why)
    {
        HandSide? side = _holder != null ? _holder.Side : (HandSide?)null;
        if (TryBeginGlide())
        {
            VRLog.Info("FigureGrab",
                $"AUTO-RELEASE (hold gate{(side != null ? $", {side}" : string.Empty)}): {Describe()} "
                + $"returned to the board — {why}. Gliding home ({GlideDurationSeconds:0.00}s), the "
                + "usual release glide, as if the user had let go.");
            return;
        }
        Restore();
        VRLog.Info("FigureGrab",
            $"AUTO-RELEASE (hold gate{(side != null ? $", {side}" : string.Empty)}): {Describe()} "
            + $"returned to the board INSTANTLY (glide impossible — dead root/parent or teardown) — {why}.");
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

    /// <summary>The figure's class id for log lines written by the driver (same vocabulary every
    /// other FigureGrab line uses, so a hardware log reads as one story).</summary>
    internal string Label => Describe();
}

/// <summary>
/// THE REAL, ANIMATED TOP OF A MINIATURE — one rule, shared by the two subsystems that both have
/// to answer "how tall is this figure, right now?" (the health-bar anchor in
/// <c>WorldUI.ActorBars</c> and the grab reach in <see cref="FigureGrabDriver"/>).
///
/// <para><b>WHAT THE ModBuild 293 HARDWARE LOG SETTLED, AND WHAT IT DEMOLISHED.</b> Every version
/// of this rule up to ModBuild 293 started from <c>Renderer.bounds</c> and then tried to correct
/// it. The 293 log ends that line of work with two readings that no correction survives:</para>
/// <list type="bullet">
///   <item><description><b>The box does not contain the figure.</b> <c>SpittingDrakeID</c> reported
///   the SAME box, <c>y -0.04..1.52</c>, in every sample of the session — it never moved by a
///   millimetre — while its own head joint travelled from <c>0.57</c> to <c>2.16</c> wu as the
///   drake took off. A box whose top is 1.52 while the character's head is at 2.16 is not a loose
///   box, it is a box that is measuring something else. That is why the two flying drakes' bars sat
///   INSIDE them (health_bars_drachen.jpg): the old rule capped the head floor at the box top
///   (<c>Mathf.Min(top, boundsMaxY)</c>) and parked the bar 0.4 wu BELOW the drake's own head.
///   </description></item>
///   <item><description><b>The box's underhang and its top are one baked number.</b> ModBuild 292
///   subtracted "the slack the box admits below the base" from the top, and ModBuild 293 shipped
///   the <c>LOWEST</c> field expressly to falsify that. It fired: on all three drakes the lowest
///   and the tallest renderer are the SAME object (<c>MO_ElderDrake_MESH</c>,
///   <c>MO_Spitting_Drake_Mesh</c>, <c>MO_Rending_Drake_Elite</c>), so the underhang and the
///   overhang are two corners of ONE authored box, and subtracting one from the other is arithmetic
///   on a single stale number, not a correction. The subtraction is gone.</description></item>
/// </list>
///
/// <para><b>THE MECHANISM, STATED EXACTLY.</b> A <c>SkinnedMeshRenderer</c> with
/// <c>updateWhenOffscreen = false</c> — which is every character in the log — does not report the
/// silhouette on screen. It reports its AUTHORED <c>localBounds</c> carried by the ROOT BONE's
/// transform. Two consequences, and the log shows both: a figure whose root bone does not move
/// (the small drakes — their flight is driven by bones below the root) reports a box frozen on the
/// ground however high it flies; and a figure whose root bone bobs (the boss: <c>y -1.29..5.57</c>,
/// <c>-1.07..5.65</c>, <c>-1.20..5.50</c>, <c>-1.16..5.67</c>, ... in one session) reports a box
/// that jitters ±0.35 wu without its silhouette changing at all. Neither number is the dragon, and
/// that jitter is the whole of the boss bar's visible bobbing.</para>
///
/// <para><b>THE RULE THAT REPLACES IT: MEASURE THINGS THAT MOVE.</b> The figure's extent is the
/// union, over its renderers, of
/// <list type="bullet">
///   <item><description>for a skinned renderer, the world-space extent of its LIVE BONE TRANSFORMS
///   (<c>SkinnedMeshRenderer.bones</c>). Bones are driven by the Animator every frame, so a wing
///   that is up is measured up and a drake that is flying is measured in the air. It costs one
///   <c>Transform.position</c> read per bone and touches no game state whatsoever — nothing is
///   written, no flag is flipped, no culling behaviour changes. That last property is why this was
///   chosen over the obvious alternative of toggling <c>updateWhenOffscreen</c> around the read:
///   that alternative writes a culling-relevant flag on a GAME renderer, and whether Unity
///   recomputes the bounds synchronously on the following <c>bounds</c> get is undocumented, so it
///   is a remedy that could silently return the same stale number it was added to
///   replace;</description></item>
///   <item><description>for a plain <c>MeshRenderer</c> (a weapon, a prop, a shield), its own
///   <c>bounds</c> — which for a non-skinned mesh IS live, because it is the mesh's authored box
///   under the object's current transform, and that transform is bone-parented.</description></item>
/// </list>
/// The character's head joint then survives only as a FLOOR — never a cap. The old cap is exactly
/// what buried the drakes' bars.</para>
///
/// <para><b>WHAT THE BONE BOX DOES NOT COVER, STATED HONESTLY.</b> Skin extends past the bones it
/// is weighted to: the Brute's helmet horns reach above his head joint and no bone is up there. The
/// bone box is therefore a small under-estimate of the silhouette on humanoids, and the caller's
/// own clearance margin (12 % of the figure's height in <c>ActorBars</c>) absorbs it. The
/// alternative — padding by a bind-pose-derived per-mesh constant — was rejected as a second
/// unmeasured term stacked on a first; if a hardware log ever shows a bar clipping a helmet, that
/// is the term to add, and the anchor line now prints the bone box and the baked box side by side
/// so the size of the miss is readable rather than inferred.</para>
///
/// <para><b>AND THE HARDWARE LOG SHOWED IT (2026-09-04, healtbar_demon.jpg).</b> The paragraph
/// above named the condition under which the missing term becomes real work — "if a hardware log
/// ever shows a bar clipping a helmet" — and <c>FrostDemonID</c> is it: head joint 1.78 wu above
/// the track point, bone extent 0.00..1.95, bar parked at 2.00, and the figure's ice crystals
/// clearly rising past it. So the term exists now, as <see cref="TryMeshTopY"/> — but NOT as the
/// bind-pose constant that was rejected. That rejection stands and for the same reason: a
/// bind-pose pad is a second unmeasured number stacked on a first. This one bakes the skin IN THE
/// CURRENT POSE and reads its actual vertices, which is a measurement, and the caller
/// (<c>ActorBars.MeasureAnchorOffsetWU</c>) owns every policy question about it — the column that
/// keeps a wingtip out, the band that refuses an implausible reading, the bound on how far it may
/// raise anything, and the per-mesh cache that keeps the bake off the frame.</para>
///
/// <para>Pure functions apart from <see cref="TryMeshTopY"/>, which owns two pieces of reused
/// scratch (one bake target, one vertex list) and allocates nothing per call in steady state.
/// </para>
/// </summary>
internal static class FigureBody
{
    /// <summary>
    /// The LIVE vertical extent of one renderer, in world units.
    ///
    /// <para><paramref name="liveBones"/> is the number of bone transforms that produced the
    /// answer: 0 means this renderer fell back to its baked <c>Renderer.bounds</c> box (a plain
    /// <c>MeshRenderer</c>, for which that box is live anyway, or a skinned renderer with no bone
    /// array). A caller that prints its measurement MUST print this count, because "0 live bones on
    /// a SkinnedMeshRenderer" is the one state in which this rule silently degrades back to the
    /// stale box it exists to replace.</para>
    ///
    /// <para>Returns false only for a renderer that is neither a mesh nor a skinned mesh, or one
    /// folded into a static batch (whose <c>bounds</c> are the whole batch — documented Unity
    /// behaviour) — both of which the callers already filter, so the guard is a belt.</para>
    /// </summary>
    internal static bool TryLiveExtentY(Renderer r, out float minY, out float maxY, out int liveBones)
    {
        minY = 0f;
        maxY = 0f;
        liveBones = 0;
        if (r == null || r.isPartOfStaticBatch)
            return false;

        if (r is SkinnedMeshRenderer smr)
        {
            Transform[] bones = smr.bones;
            if (bones != null && bones.Length > 0)
            {
                float lo = float.MaxValue;
                float hi = float.MinValue;
                int seen = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform b = bones[i];
                    if (b == null)
                        continue;
                    float y = b.position.y;
                    if (y < lo) lo = y;
                    if (y > hi) hi = y;
                    seen++;
                }
                if (seen > 0)
                {
                    minY = lo;
                    maxY = hi;
                    liveBones = seen;
                    return true;
                }
            }
            Bounds sb = smr.bounds;
            minY = sb.min.y;
            maxY = sb.max.y;
            return true;
        }

        if (r is MeshRenderer)
        {
            Bounds mb = r.bounds;
            minY = mb.min.y;
            maxY = mb.max.y;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The figure's top: the live extent, raised (NEVER lowered) to the character's head joint.
    ///
    /// <para><paramref name="fromHead"/> is true when the head joint won — the field that says the
    /// live extent under-reported, which on a hardware log separates "the skeleton does not reach
    /// the head" from "this renderer had no bones at all".</para>
    /// </summary>
    internal static float LiveTopY(float liveMaxY, float headY, bool headKnown, out bool fromHead)
    {
        fromHead = false;
        float top = liveMaxY;
        if (headKnown && headY > top)
        {
            top = headY;
            fromHead = true;
        }
        return top;
    }

    /// <summary>
    /// How many vertices this class will walk for ONE renderer's mesh top.
    ///
    /// <para>A belt, not a dial. A board miniature's skin is a few thousand vertices; a number an
    /// order of magnitude past that says the renderer in hand is not a miniature — a map chunk
    /// that slipped past the static-batch filter, a foreign mesh dropped into the subtree — and
    /// walking it would put a scene-sized loop on the adopt path, which is the one defect this
    /// project has shipped more than any other. Refusing costs nothing: the caller's mesh-top term
    /// is one-sided, so a refusal is exactly the answer the caller already had.</para>
    /// </summary>
    private const int MeshTopVertexCap = 60000;

    /// <summary>
    /// The reused bake target for <see cref="TryMeshTopY"/> — ONE <c>Mesh</c> for the process,
    /// created on first use and never destroyed. <c>BakeMesh</c> overwrites it in place, so a
    /// per-call <c>new Mesh()</c> would hand the GC a mesh per measurement and leak native memory
    /// until a collection ran.
    /// </summary>
    private static Mesh? s_bakeScratch;

    /// <summary>
    /// The reused vertex buffer for <see cref="TryMeshTopY"/>. <c>Mesh.GetVertices(List&lt;T&gt;)</c>
    /// fills an existing list rather than returning a fresh array the way <c>Mesh.vertices</c>
    /// does, so after the first figure of a session this allocates nothing at all.
    /// </summary>
    private static readonly List<Vector3> BakedVertices = new(8192);

    /// <summary>
    /// The topmost point of one renderer's REAL SKIN, in world units, inside a vertical COLUMN of
    /// radius <paramref name="columnRadius"/> around <paramref name="axis"/>.
    ///
    /// <para>WHY THIS IS NOT <see cref="TryLiveExtentY"/>. That method reads BONE TRANSFORMS, which
    /// sit inside the silhouette by construction: a crystal, a horn or a crest whose tip extends
    /// past the last bone weighted to it is invisible to every term built on it. On
    /// <c>FrostDemonID</c> (2026-09-04) the whole defect lives in that gap — the bones stop at 1.95
    /// wu and the ice spikes do not.</para>
    ///
    /// <para><b>WHICH OVERLOAD, AND WHICH MATRIX — read this before touching either.</b>
    /// <c>SkinnedMeshRenderer.BakeMesh(Mesh)</c>, the ONE-ARGUMENT overload, writes vertices in the
    /// renderer transform's LOCAL space with that transform's own scale NOT applied; changing that
    /// is precisely what the Unity 2020.2 <c>BakeMesh(Mesh, bool useScale)</c> overload was added
    /// for. So the matrix that carries those vertices to the world is the renderer transform's full
    /// <c>localToWorldMatrix</c> — translation, rotation AND scale — and the two choices are a
    /// PAIR: <c>useScale: true</c> with the same matrix applies the figure's scale twice, and on
    /// this board's figure scales that is a mesh top metres out of the room. This paragraph is a
    /// claim about an engine API, not a measurement, which is exactly why the caller wraps the
    /// answer in a plausibility band: get it wrong and the band refuses the term in words on the
    /// next hardware line instead of launching a bar into the sky.</para>
    ///
    /// <para>THE COLUMN IS THE CALLER'S POLICY and is applied here only because doing it per vertex
    /// inside the one loop that already touches every vertex is free, where handing the caller a
    /// vertex list would not be. A vertex outside the column is COUNTED in
    /// <paramref name="excluded"/> and not otherwise mentioned: exclusion is the normal case, not a
    /// failure. A plain <c>MeshRenderer</c> has no vertices to filter — its
    /// <c>Renderer.bounds</c> is already live world geometry — so its box is admitted whole, and
    /// only when it is BOTH centred inside the column AND narrower than the column is wide. That
    /// pair is deliberately conservative: it takes a crest or a horn parented to the head and
    /// refuses a banner, a spear or a wing panel, which is the same distinction the column itself
    /// is drawing.</para>
    ///
    /// <para>Returns false — with <paramref name="why"/> EMPTY — when this renderer simply had
    /// nothing inside the column, and false with <paramref name="why"/> set when the measurement
    /// itself could not be taken. The caller prints the second and counts the first.</para>
    /// </summary>
    internal static bool TryMeshTopY(
        Renderer r, Vector3 axis, float columnRadius,
        out float topY, out int considered, out int excluded, out string why)
    {
        topY = 0f;
        considered = 0;
        excluded = 0;
        why = string.Empty;
        if (r == null || r.isPartOfStaticBatch)
        {
            why = "static-batched (its box is the whole batch)";
            return false;
        }

        float r2 = columnRadius * columnRadius;

        if (r is SkinnedMeshRenderer smr)
        {
            Mesh? shared = smr.sharedMesh;
            if (shared == null)
            {
                why = "a SkinnedMeshRenderer with no sharedMesh";
                return false;
            }
            int vertexCount = shared.vertexCount;
            if (vertexCount <= 0)
            {
                why = "a SkinnedMeshRenderer whose sharedMesh has no vertices";
                return false;
            }
            if (vertexCount > MeshTopVertexCap)
            {
                why = $"refused: {vertexCount} vertices is past the {MeshTopVertexCap} cap, so this "
                      + "renderer is not a miniature and walking it would be a scene-sized loop";
                return false;
            }

            // The bake and the read are the only two calls here that can throw, and a throw on the
            // adopt path would take the whole anchor measurement with it. Catch, name it, and let
            // the caller keep the answer it already had — a mesh-top term that cannot measure is a
            // term that does nothing, which is the design.
            try
            {
                s_bakeScratch ??= new Mesh { name = "GVR_FigureBody_BakeScratch" };
                smr.BakeMesh(s_bakeScratch);
                s_bakeScratch.GetVertices(BakedVertices);
            }
            catch (System.Exception e)
            {
                why = $"BakeMesh threw ({e.GetType().Name}: {e.Message})";
                return false;
            }

            // See the overload note above: one-argument bake => LOCAL space without the renderer
            // transform's scale => the full localToWorldMatrix is the correct carrier.
            Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
            float hi = float.MinValue;
            for (int i = 0; i < BakedVertices.Count; i++)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(BakedVertices[i]);
                float dx = w.x - axis.x;
                float dz = w.z - axis.z;
                if (dx * dx + dz * dz > r2)
                {
                    excluded++;
                    continue;
                }
                considered++;
                if (w.y > hi)
                    hi = w.y;
            }
            if (considered == 0)
                return false;
            topY = hi;
            return true;
        }

        if (r is MeshRenderer)
        {
            Bounds b = r.bounds;
            if (b.size.sqrMagnitude <= 1e-8f)
            {
                why = "a MeshRenderer with a degenerate box";
                return false;
            }
            float bx = b.center.x - axis.x;
            float bz = b.center.z - axis.z;
            if (bx * bx + bz * bz > r2 || Mathf.Max(b.extents.x, b.extents.z) > columnRadius)
            {
                excluded++;
                return false;
            }
            considered++;
            topY = b.max.y;
            return true;
        }

        why = "neither a MeshRenderer nor a SkinnedMeshRenderer";
        return false;
    }
}
