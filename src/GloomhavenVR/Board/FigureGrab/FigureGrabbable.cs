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
    // rigRoot.localScale; VRRigDriver: "its lossyScale is the diorama scale"), and the hand anchor
    // hangs under that rig — HandVisuals normalises Socket_Grab against handRoot.parent.lossyScale,
    // so GrabAnchor.lossyScale IS the rig scale exactly, free of any per-style hand scale. The old
    // hold re-derived the mini's anchor-LOCAL scale from a CONSTANT world size every frame
    // (homeWorldScale / anchor.lossyScale), i.e. it pinned the mini's size in WORLD space. But the
    // player's eyes are scaled by that same rig, so a world-constant object changes apparent size
    // with every zoom: pinning the world size is exactly what makes the mini in the hand grow and
    // shrink. Proven from the ModBuild 108 log rather than inferred — three grabs in one session
    // printed `boardWorld=1 anchorScale=41.368`, `41.368` and `10.149`, so the same board-size mini
    // was rendered at three anchor-local sizes a factor of ~4 apart purely because of the zoom.
    //
    // WHAT THE OLD BEHAVIOUR WAS FOR (do not simply revert it): it was the fix for the earlier MP
    // defect "Die Figuren-Größen ändern sich wenn man sie in die Hand nimmt … so sehe ich beim
    // Remote-Spieler eine andere Größe der Figur in der Hand als er selbst" — the hold used to
    // multiply by the old [FigureGrab] HeldScale family (1.5x, bound per hand style) while the
    // wire carries POSE ONLY, so no two clients could agree. That multiplier stays gone (its
    // config family has since been deleted — see the note in FigureGrabConfig). What
    // this change touches is only the SECOND half of that fix — "pin the world size" — which is a
    // stronger statement than "start from the board size" and is the half the user is reporting.
    // At the instant of the grab the two are identical, so the mini still ENTERS the hand at
    // exactly its board size; it simply stops chasing the zoom afterwards.
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
    // MULTIPLAYER — what a peer sees, and the one thing this change owes Net/. Nothing about the
    // wire changes here (no scale has ever ridden it: NetFigures.TrySampleHeldSlot sends world
    // position + rotation, NetFigures.EaseSlot writes exactly those two and never a localScale), and
    // nothing in Net/ was touched. The consequence is precise and worth stating rather than
    // smoothing over:
    //   - Position and rotation still cross exactly as before, so WHERE the peer sees the mini is
    //     unchanged. Only its SIZE is at issue.
    //   - A peer renders the held mini at its own board size, and it renders the holder's HANDS at
    //     the holder's live rig scale (RemoteAvatar applies state.WorldScale to the part holders).
    //     So today, while a holder zooms, a peer ALREADY sees that holder's hand grow or shrink
    //     around a board-size mini — the peer side has always shown the "unlatched" picture.
    //   - After this change the holder's mini world size is boardSize × (rigScaleNow /
    //     rigScaleAtGrab). The peer still draws boardSize. The two therefore differ by exactly the
    //     zoom the holder applied SINCE grabbing — 1.0 unless they zoom mid-hold, and it resolves
    //     itself on release, but it is a real divergence and it is new.
    //   - The fix costs ZERO wire bytes and is entirely receive-side, because the receiver already
    //     has both numbers: the sender's live rig scale arrives every rig packet as
    //     AvatarState.WorldScale (LocalRigSampler.cs:50 samples rigRoot.lossyScale.x — the SAME
    //     quantity as GrabAnchor.lossyScale here, since HandVisuals.NormalizeSocket compensates
    //     Socket_Grab against handRoot.parent.lossyScale), and the receiver already knows the frame
    //     a hold BEGINS (NetFigures.ApplyRemoteHeld's fresh-hold guard, the one that spawns the home
    //     ghost). Latching the sender's WorldScale there and multiplying the figure's home local
    //     scale by WorldScaleNow / WorldScaleAtHoldStart reproduces the holder's picture exactly.
    //     The receiver MUST restore the home local scale when the slot is released — the game never
    //     writes a figure's scale, so unlike position and rotation it will not heal itself. Exact
    //     call sites are in this round's report; it is a Net/-owned change and was NOT made here.
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
    // glide doc, the peer-side ratio reconstruction in Net/NetFigures). Folding the gesture into it
    // would make "the size the mini entered the hand at" unrecoverable mid-hold, and the factor is
    // ALSO precisely the ONE number that has to cross the wire for a peer to reproduce the picture
    // (NetProtocol.ExtIdHeldStretch — a manual stretch is derivable from nothing already synced).
    // Keeping it separate makes the wire sample a field read instead of a division.
    //
    // SCOPE — THIS HOLD ONLY, deliberately. The factor resets to 1 at every grab and is never
    // persisted: the user asked to change "die Größe der Figur in der Hand", not a standing
    // preference, and the one config family that ever meant "preferred held size"
    // ([FigureGrab] HeldScale) is retired LEGACY with its own note explaining why a local-only
    // multiplier was a multiplayer defect. Release is untouched by construction: both release
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
    // TWO ENFORCEMENT POINTS, both pop-free by placement:
    //   (1) the GRAB (ApplyGrabTimeStretchClamp, called from OnGrab between the latch and the
    //       first ApplyHeldPose): a ratio outside Min/Max scales the latch so the mini ENTERS the
    //       hand at exactly the bound — before the first held frame renders, so nothing on screen
    //       ever jumps;
    //   (2) the GESTURE (GetStretchFactorBounds): the total bounds converted to per-hold factor
    //       bounds at latch time — Min/ratio .. Max/ratio — so a figure grabbed at 2× total with
    //       Max 3 can only be stretched to factor 1.5, never to the 6× total the old
    //       factor-in-isolation clamp allowed.
    // A mid-hold ZOOM is deliberately NOT re-clamped: the latch is zoom-independent by design
    // (the report above this one), and re-clamping a standing size is a pop. Likewise a mid-hold
    // dial change (Min/Max/StretchLimits) affects the next gesture frame and the next grab only.
    //
    // MULTIPLAYER: invisible on the wire by construction. Only _stretch is sampled (StretchOf);
    // the latch clamp changes _heldLocalScale, which never leaves this machine — a peer keeps
    // reconstructing boardSize × (their observed zoom ratio) × factor, unclamped by OUR local
    // bounds, per the standing "bounds are a local presentation choice" ruling. The one residue:
    // while OUR latch was clamped, the peer's picture differs from ours by exactly the clamp —
    // same class of accepted divergence as a mid-hold zoom itself, and it heals on release.
    private float _latchTotalRatio = 1f;

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

    // HELD-FIGURE STRETCH capture bounds — the held visual's renderers, cached per hold for the
    // stretch gesture's surface-distance capture test (see HeldRenderers()). Reset on grab and
    // dropped with the material bookkeeping on release, so a rebuilt visual on the NEXT hold is
    // re-walked.
    private Renderer[]? _stretchBoundsRenderers;

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
    /// <para>IT IS ALSO THE AUTO-RELEASE FOR THE TURN-DEADLOCK GATE. A figure that was picked up
    /// while the game was idle can still BECOME load-bearing under the hand (an attack starts and
    /// targets it). Refusing the holder here is not a new mechanism: <c>ProximityGrabber.HealDeadHeld</c>
    /// already force-releases any hold whose target stops allowing its hand, through the normal
    /// <see cref="OnRelease"/> path and with a Warn line. So the mini leaves the hand the same frame
    /// the game starts depending on it, which is what stops <c>ActorBars</c>' host hide from killing
    /// the bar coroutine that the choreographer's untimed wait is blocked on. See
    /// <see cref="FigureBusy"/>.</para>
    /// </summary>
    public bool AllowsHand(VRHand hand)
        => !NetHeldFigures.Owns(_actor)
           && !FigureBusy.IsBusy(_actor)
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

        // The board WORLD size, sampled while the mini is still standing on the board. Note this is
        // read AFTER the re-grab FinishGlide above, so a re-grab mid-glide latches the true board
        // size and not a mid-glide sample.
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

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board world-scale as
        // it enters the hand (no scale pop). The held ROTATION is NOT snapshotted from the board —
        // it is a fixed constant anchor-local rotation (FigureGrabConfig.HeldUprightRotation), so
        // the mini snaps to the same orientation in the palm regardless of the grab approach angle.
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

        // SIZE probe (the line that PROVED this defect — three grabs in one ModBuild 108 session
        // printed the same boardWorld=1 against anchorScale 41.368 / 41.368 / 10.149, i.e. the same
        // mini at three in-hand sizes a factor of ~4 apart, purely from the zoom). anchorScale is
        // the LIVE diorama scale; heldLocal is the frozen latch. From here on the two are allowed to
        // disagree: heldLocal stays put while anchorScale follows every pinch-zoom, and that is the
        // fix, not a drift. heldWorld is therefore boardWorld × (anchorScale / anchorScale-at-grab)
        // — equal to boardWorld on this frame by construction, UNLESS the grab-time size clamp
        // trimmed the latch (its own [Size] CLAMP line directly above says so when it did).
        // latchRatio is the total held size in default-zoom units — the number the bounds govern.
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
        // (mirror-correct); legacy mode lays it flat (tilt only, mirror-invariant).
        // _uprightBase is identity unless the grab captured a world-upright start, so the tuned
        // angles keep meaning exactly what they meant: offsets, applied on top of whatever the
        // base is. With the option on they are offsets from "standing up"; with it off they are
        // offsets from the hand, as before.
        t.localRotation = _uprightBase * (FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(side)
            : FigureGrabConfig.HeldPalmRotation());

        // SIZE — the value LATCHED at the grab (see _heldLocalScale for the report, the root cause
        // and the rejected alternatives). Grabbing still never resizes a mini: the latch is taken
        // from the board world size at the grab instant, so the figure enters the hand at exactly
        // the size it stood on the board — the old [FigureGrab] ActiveHeldScale multiplier (1.5x,
        // bound PER HAND STYLE, so two players could not even agree on the factor) stays gone and
        // stays marked LEGACY (CHARTER §5). What changed is only that the size is no longer
        // RE-DERIVED from the live anchor every frame, so a pinch-zoom mid-hold leaves it alone.
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
            + "this). Peers keep their own unclamped reconstruction — local presentation only.");
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

    /// <summary>The stretch factor of the hold on <paramref name="actor"/>, or 1 when it is not in
    /// a hand here (unknown, gliding, or remote). This is the SEND-side sample for
    /// <c>NetProtocol.ExtIdHeldStretch</c>: a glide reads as neutral, so the wire eases the peer's
    /// copy back toward board ratio over the same window the local glide plays.</summary>
    internal static float StretchOf(ActorBehaviour actor)
    {
        if (actor == null)
            return 1f;
        foreach (FigureGrabbable g in Live)
        {
            if (g._attached && ReferenceEquals(g._actor, actor))
                return g._stretch;
        }
        return 1f;
    }

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
    /// <para>KEPT, with a new subject. It used to re-derive a constant WORLD size from the live
    /// anchor, which is precisely the zoom-follows-the-hand defect (see
    /// <see cref="_heldLocalScale"/>); it now re-writes the frozen latch, so it is idempotent and a
    /// zoom moves nothing. Its REASON for existing is untouched and still needed: it sits above the
    /// config gate because a mini still in the hand when GrabFigures is toggled off is released by
    /// the gate's ReleaseAll on THIS frame, and it must not be rendered at a size some other writer
    /// has touched for the frame in between. It is also the only thing that would reveal such a
    /// writer at all — the game's own transform writers are prefix-skipped for held actors
    /// (<see cref="ActorBehaviour_HeldTransform_Patch"/>) and none of them writes scale, so today
    /// this is cheap insurance rather than a correction. One vector store per held figure (at most
    /// two).</para>
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
        // Piggybacked renderer bookkeeping teardown: the stretch capture cache must not pin a
        // released figure's renderers (Unity objects) across the rest of the session.
        _stretchBoundsRenderers = null;
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        // TURN-DEADLOCK GATE — a release forced because the game started depending on this figure
        // does NOT glide. The glide deliberately keeps the actor in HeldFigures until it lands
        // (that is what keeps the ghost and the net stream alive), and HeldFigures membership is
        // exactly what makes ActorBars hide the actor's bar host — so a 0.28 s glide would leave
        // 0.28 s in which the bar coroutine the choreographer is blocked on can still be killed.
        // The instant path hands the actor back on THIS frame. It is the same instant restore the
        // authoritative-cell auto-release already uses (FigureGrabDriver.AutoReleaseMovedFigures)
        // and it is not the "popping" the project forbids: that rule governs what the mod ANIMATES,
        // and this is a safety release whose whole value is that it takes no time. See FigureBusy.
        if (FigureBusy.IsBusy(_actor, out string busyWhy))
        {
            Restore();
            VRLog.Info("FigureGrab",
                $"{hand.Side} released figure ({Describe()}) INSTANTLY (no glide) — {busyWhy}. "
                + "The game regains this actor on this frame so nothing of the mod's can be holding "
                + "its bar down while the choreographer waits on it.");
            return;
        }

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
    /// <para>So the size eases home over the same 0.28 s as the position and rotation, on the same
    /// ease-out curve, and the mini never changes size in a single frame.</para>
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
                // Local (not world) scale, so the mini lands at the board's LIVE size even if the
                // board moved or rescaled during the hold — same reasoning as the glide's landing.
                // This path changes the size in one frame, which is deliberate and is NOT the
                // "popping" the project forbids: it is reached only by a SAFETY release whose whole
                // value is that it takes no time (turn-deadlock gate, authoritative cell moved,
                // teardown, config gate) or as TryBeginGlide's fallback when a glide is impossible
                // at all (dead root/parent). The ordinary player release glides. See OnRelease.
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

    /// <summary>The figure's class id for log lines written by the driver (same vocabulary every
    /// other FigureGrab line uses, so a hardware log reads as one story).</summary>
    internal string Label => Describe();
}
