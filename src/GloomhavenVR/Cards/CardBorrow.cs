// BORROWING A TEAMMATE'S HAND CARD — a purely LOCAL, READ-ONLY copy.
//
// User report 7 (2026-08-15, verbatim): "Ich will auch in der Lage sein, dass man die fremden
// Handkarten jederzeit auch in der Hand nehmen kann (inklusive Hand wechsel etc) damit man sie
// näher betrachten kann. Nur interagieren oder umsortieren etc soll man nicht können. Es geht hier
// rein um die Info."
//
// ─── CARD IDENTITY STILL NEVER GOES ON THE WIRE ──────────────────────────────────────────────
// This file adds NOTHING to the wire — no field, no record, no flag, not one bit. It cannot: it
// never learns a card identity from the network in the first place. The mechanism it works inside
// is the one Net/RemoteHandFan already uses for the ghost fan's FRONTS, and it is worth restating
// because it is the whole reason this feature is buildable at all:
//
//   * The wire carries a card COUNT and nothing else about a peer's hand.
//   * The card CONTENT is read LOCALLY, off this client's own host-replicated CPlayerActor hand
//     (CardsHandManager.GetHand(actor).cardsUI) — the game already replicates the model; what the
//     mod adds is only whether it is DRAWN.
//   * It is drawn only when the game's own reveal rule permits: RevealGate.ShowRoundCardFronts,
//     which mirrors vanilla AbilityCardUI (hidden iff online && scenario && !IsUnderMyControl &&
//     phase == SelectAbilityCardsOrLongRest).
//   * The face itself is a THROWAWAY CLONE of the widget (Net/RemoteCardArt), never the live one.
//
// A borrow therefore asks the same question the fan already asked, one slab at a time, and gets
// its answer from the same gate. If the gate is shut there is no front to borrow and the gesture
// is refused; if the gate SHUTS while a card is borrowed the copy is dissolved in that same frame
// (see BorrowedCardWatch). An animation — or a hold — must never be a window in which a rule is
// briefly not enforced.
//
// PHASES BORROWING IS ALLOWED IN — exactly the phases the peer's fan already shows fronts in, and
// not one more:
//   * ALLOWED: every phase in which RevealGate.ShowRoundCardFronts(theirActor) is true — offline
//     play, out of a scenario, a character under my own control, and every in-scenario phase that
//     is NOT the secret card selection (action execution, the round's play-out, loot, rest…).
//   * REFUSED: the secret window (online + in scenario + not under my control + phase ==
//     SelectAbilityCardsOrLongRest). There the fan is showing BACKS and there is nothing to read;
//     the gesture is refused rather than handing over a back, so the affordance never promises
//     information it may not give. This is also the standing exception's exact boundary: card
//     faces DURING SELECTION are the one thing the 1:1 mirroring rule does not extend to.
//
// ─── WHY THE COPY IS A REAL VRCard ───────────────────────────────────────────────────────────
// The user asked for "in die Hand nehmen … inklusive Hand wechsel", i.e. it must FEEL like holding
// one of your own cards. The project has exactly one piece of machinery for that — VRCard on
// GrabbableBehaviour (pinch pose, head billboard, release glide, the hand-to-hand transfer gesture
// in CardsDriver.UpdateHeldCardTransfer) — so the borrowed copy IS a VRCard and inherits all of it
// instead of a second, half-right imitation.
//
// It is, deliberately, a VRCard that NOBODY OWNS:
//   * it is NOT created through VRCardFactory, so it never enters `_factory.All` and is therefore
//     invisible to CardsDriver's park sweep, its zone stamping and BlockCardInteractions;
//   * CardsDriver never HOOKS it (HookCard is what subscribes OnCardReleased/OnCardGrabbed/
//     OnCardPoked), so no release of it can reach the drop state machine, a tray slot, the local
//     hand fan, a pile, or any game seam — the routing simply does not exist for this object;
//   * it holds NO AbilityCardUI (AttachGameCard is never called), so the peer's real widget is
//     never adopted, re-parented or locked. Its face is a RemoteCardArt clone, exactly like the
//     ghost fan's.
// The one shared system it deliberately DOES join is the hand-to-hand transfer detector, which
// type-tests `hand.Grabber.Held is VRCard` and is otherwise ownerless — see OnBorrowedReleased for
// how the adoption is completed without touching CardsDriver.
//
// EVERYTHING SUPPRESSED ON THE COPY is enumerated on BeginBorrow.

using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// What a surface that OWNS borrowable card slabs (today: <c>Net.RemoteHandFan</c>) has to answer.
/// Deliberately phrased so that nothing in <c>Cards/</c> ever names a type in <c>Net/</c>: the
/// gate, the label, the tint and the cloned face all arrive through this interface.
/// </summary>
internal interface IBorrowedCardSource
{
    /// <summary>Who the cards belong to, for the log and the copy's object name ("player 3").</summary>
    string BorrowOwnerLabel { get; }

    /// <summary>The owner's identifying colour — the copy wears it on its rim so a borrowed card is
    /// never mistaken for one of your own (see <see cref="CardBorrow.TintRim"/>).</summary>
    Color BorrowTint { get; }

    /// <summary>
    /// May slot <paramref name="slot"/> be READ right now? Asked on the grab AND on every frame of
    /// the hold. This is the surface's own front-art gate — the same
    /// <c>RevealGate.ShowRoundCardFronts</c> verdict the slab's face is drawn under — so a borrow
    /// can never outlive, or out-permit, the fan it came from.
    /// </summary>
    bool BorrowAllowed(int slot);

    /// <summary>The gate's name, for the diagnostic line.</summary>
    string BorrowGateLabel { get; }

    /// <summary>Build (or refresh, per frame) the CLONED face of <paramref name="slot"/> under
    /// <paramref name="host"/>, fitted to a <paramref name="cardWidth"/> x
    /// <paramref name="cardHeight"/> card. False = nothing to show (the copy stays a card back).</summary>
    bool ShowBorrowedFace(int slot, Transform host, float cardWidth, float cardHeight);

    /// <summary>Release whatever <see cref="ShowBorrowedFace"/> built. Idempotent.</summary>
    void ReleaseBorrowedFace();
}

/// <summary>
/// The reach surface of ONE borrowable slab — a component the owning fan puts on each of its card
/// slabs together with a trigger collider. It exists so the borrow sweep can reuse
/// <see cref="FanSweep"/>'s single-winner election verbatim rather than inventing a second
/// "which card is the hand on" rule: the peer's slabs overlap exactly like the local fan's, and
/// FanSweep is the file that documents why overlapping full-width colliders make a sweep skip
/// cards.
/// </summary>
internal sealed class BorrowTarget : MonoBehaviour, IFanSweepTarget
{
    private IBorrowedCardSource? _source;
    private int _slot = -1;
    private Collider? _collider;
    private float _faceWidthLocal;

    /// <summary>The fan that owns this slab (null once the slab is being torn down).</summary>
    internal IBorrowedCardSource? Source => _source;

    /// <summary>This slab's index in the owner's fan — the slot the clone is read from.</summary>
    internal int Slot => _slot;

    /// <summary>Every live borrow target, in creation order. A plain list: there are at most
    /// MaxCards per peer and a handful of peers, and it is swept once per frame.</summary>
    internal static readonly List<BorrowTarget> Live = new(32);

    /// <summary>Configure the target. <paramref name="stripWidthLocal"/> is the VISIBLE strip of
    /// this slab in slab-local metres (<see cref="FanSweep.StripWidth"/>) — the collider is shrunk
    /// to it and anchored at the exposed edge so the per-card regions TILE instead of overlap,
    /// which is the whole of "one card at a time" (see FanSweep's class doc).</summary>
    internal void Configure(IBorrowedCardSource source, int slot, float fullWidthLocal,
        float heightLocal, float stripWidthLocal)
    {
        _source = source;
        _slot = slot;
        _faceWidthLocal = fullWidthLocal;

        var box = gameObject.GetComponent<BoxCollider>();
        if (box == null)
            box = gameObject.AddComponent<BoxCollider>();
        float strip = Mathf.Clamp(stripWidthLocal, fullWidthLocal * 0.25f, fullWidthLocal);
        box.size = new Vector3(strip, heightLocal, 0.02f);
        box.center = new Vector3(FanSweep.StripOffset(fullWidthLocal, strip), 0f, 0f);
        box.isTrigger = true;   // never touches game physics; only ClosestPoint is used
        _collider = box;
    }

    /// <summary>Detach from the registry ahead of the GameObject's destruction, so a fan rebuild
    /// never leaves a dead entry to be swept.</summary>
    internal void Retire()
    {
        _source = null;
        Live.Remove(this);
    }

    private void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    private void OnDisable() => Live.Remove(this);

    // ---- IFanSweepTarget ---------------------------------------------------------------

    /// <summary>A slab is a borrow candidate only while its owner says the slot may be READ. A
    /// closed gate therefore removes the affordance itself — no hover pop, no haptic, no trigger
    /// claim — rather than letting the player reach for something that will be refused.</summary>
    bool IFanSweepTarget.SweepEligible =>
        _source != null && _collider != null && _collider.enabled && _source.BorrowAllowed(_slot);

    float IFanSweepTarget.SweepFaceWidthWorld => _faceWidthLocal * transform.lossyScale.x;

    bool IFanSweepTarget.TrySweepDistance(Vector3 worldPoint, out float distance)
    {
        distance = 0f;
        if (_collider == null || !_collider.enabled || !_collider.gameObject.activeInHierarchy)
            return false;
        distance = Vector3.Distance(worldPoint, _collider.ClosestPoint(worldPoint));
        return true;
    }

    string IFanSweepTarget.SweepName => name;
}

/// <summary>
/// The borrow gesture and the life of the borrowed copy. Static because there is at most ONE
/// borrowed card at a time (see <see cref="BeginBorrow"/>) and the sweep is a single election over
/// every peer's slabs at once — two fans in reach of the same hand must not each elect a winner.
/// </summary>
internal static class CardBorrow
{
    /// <summary>Reach hysteresis on the hover, verbatim from
    /// <c>CardsDriver.TransferHoverExitScale</c> — once hovering, the hand must move this factor
    /// beyond the enter reach to lose it, so the haptic cannot buzz at the boundary.</summary>
    private const float HoverExitScale = 1.35f;

    /// <summary>How long the released copy glides back onto its slab before it is destroyed. Longer
    /// than <c>VRCard.ReleaseGlideSeconds</c> (0.35 s) by a hair so the glide is never cut off
    /// mid-flight.</summary>
    internal const float ReturnSeconds = 0.45f;

    /// <summary>The slab currently hovered by <see cref="_hoverHand"/>, and the hand hovering it.
    /// Edge-tracked for the haptic tick and the hysteresis.</summary>
    private static BorrowTarget? _hover;
    private static VRHand? _hoverHand;

    /// <summary>The one live borrow (null = none).</summary>
    private static VRCard? _card;
    private static IBorrowedCardSource? _cardSource;
    private static int _cardSlot = -1;
    private static Transform? _cardSlab;

    /// <summary>Frame guard: <see cref="Tick"/> is called by every remote fan (there is no
    /// module-level driver in this lane's files), so the first caller of a frame does the work.</summary>
    private static int _tickedFrame = -1;

    /// <summary>Throttle clock for the hover log line.</summary>
    private static float _nextHoverLogAt;

    /// <summary>True while a borrow is up — read by the owning fan so it can leave the slab's own
    /// front alone (the copy is the thing being looked at).</summary>
    internal static bool Active => _card != null;

    /// <summary>The fan a live borrow came from, or null.</summary>
    internal static IBorrowedCardSource? ActiveSource => _cardSource;

    /// <summary>
    /// Is <paramref name="card"/> the borrowed copy? Exposed for ONE caller that does not exist
    /// yet and belongs to another lane's file.
    ///
    /// <para>THE ONE WIRE-VISIBLE SIDE EFFECT OF THIS FEATURE, stated plainly.
    /// <c>Net/LocalRigSampler.TryHeldCard</c> samples "this hand holds a <see cref="VRCard"/>" onto
    /// the rig packet's <c>FlagHeldCard</c> slot, POSE ONLY — peers draw an anonymous card-BACK
    /// slab in that hand. A borrowed copy is a VRCard, so while it is held peers see that back
    /// slab. NO IDENTITY LEAKS (the slab is a back and carries no card data whatsoever, exactly as
    /// it does for a card of your own), and it is a truthful statement about the observer's body —
    /// their hand really is holding a card shape. It is nevertheless MISLEADING to the owner, who
    /// sees their own fan intact and a card in someone else's hand, so the intended fix is one line
    /// at <c>LocalRigSampler.TryHeldCard</c>:
    /// <code>if (hand.Grabber.Held is Cards.VRCard v &amp;&amp; Cards.CardBorrow.IsBorrowed(v)) return false;</code>
    /// That file is not this lane's to edit; this predicate is here so the change is a single line
    /// whenever its owner takes it.</para>
    /// </summary>
    internal static bool IsBorrowed(VRCard? card) => card != null && ReferenceEquals(card, _card);

    // ------------------------------------------------------------------ per frame --

    /// <summary>
    /// One frame of the borrow gesture: elect the slab the free hand is on, give it the standard
    /// hover tick, and turn a trigger pull into a borrow. Idempotent within a frame.
    ///
    /// <para>Shape and every constant are the hand-fan gesture's, not a new one: candidacy and the
    /// single winner come from <see cref="FanSweep.Score"/> against
    /// <see cref="FanSweep.ResolveReach"/>, the feedback is <c>HapticPreset.HoverTick</c> on the
    /// entry edge with the 1.35x exit hysteresis, the pull is claimed with
    /// <c>RayInteractor.SuppressFarClick</c> so the same trigger cannot also far-click the board,
    /// and a live game-UI hit keeps the trigger as it does everywhere else.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_tickedFrame == Time.frameCount)
            return;
        _tickedFrame = Time.frameCount;

        // A live borrow owns the gesture: no second card may be lifted out of a peer's fan while
        // one is in the air (the hand-swap moves THIS card between hands — see OnBorrowedReleased).
        if (_card != null)
        {
            _hover = null;
            _hoverHand = null;
            return;
        }

        if (BorrowTarget.Live.Count == 0)
        {
            _hover = null;
            _hoverHand = null;
            return;
        }

        BorrowTarget? bestTarget = null;
        VRHand? bestHand = null;
        float bestScore = float.MaxValue;

        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
                continue;

            Vector3 tip = hand.Rig.IndexTip.position;
            Vector3 palm = hand.Rig.PalmCenter.position;
            FanSweepPick<BorrowTarget> pick = FanSweepPick<BorrowTarget>.Empty;
            BorrowTarget? incumbent = ReferenceEquals(hand, _hoverHand) ? _hover : null;
            for (int i = 0; i < BorrowTarget.Live.Count; i++)
            {
                BorrowTarget target = BorrowTarget.Live[i];
                FanReach reach = FanSweep.ResolveReach(
                    hand.WorldScale, ((IFanSweepTarget)target).SweepFaceWidthWorld);
                FanSweep.Score(target, tip, palm, reach, incumbent, tipFirst: true, ref pick);
            }
            if (pick.Winner != null && pick.BestScore < bestScore)
            {
                bestScore = pick.BestScore;
                bestTarget = pick.Winner;
                bestHand = hand;
            }
        }

        // The hysteresis is applied against the INCUMBENT's own reach, after the election, exactly
        // as the transfer detector applies it: a target that has the hover keeps it until the hand
        // is 1.35x past the enter reach.
        if (bestTarget == null && _hover != null && _hoverHand != null
            && _hoverHand.HasPose && _hoverHand.Grabber.Held == null
            && ((IFanSweepTarget)_hover).SweepEligible)
        {
            var incumbentTarget = (IFanSweepTarget)_hover;
            FanReach reach = FanSweep.ResolveReach(_hoverHand.WorldScale, incumbentTarget.SweepFaceWidthWorld);
            if (incumbentTarget.TrySweepDistance(_hoverHand.Rig.IndexTip.position, out float tipDistance)
                && incumbentTarget.TrySweepDistance(_hoverHand.Rig.PalmCenter.position, out float palmDistance)
                && (tipDistance <= reach.Tip * HoverExitScale || palmDistance <= reach.Palm * HoverExitScale))
            {
                bestTarget = _hover;
                bestHand = _hoverHand;
            }
        }

        if (bestTarget == null || bestHand == null)
        {
            _hover = null;
            _hoverHand = null;
            return;
        }

        if (!ReferenceEquals(bestTarget, _hover) || !ReferenceEquals(bestHand, _hoverHand))
        {
            _hover = bestTarget;
            _hoverHand = bestHand;
            bestHand.SendHaptic(HapticPreset.HoverTick);
            if (Time.unscaledTime >= _nextHoverLogAt)
            {
                _nextHoverLogAt = Time.unscaledTime + 1f;
                VRLog.Info("Cards", $"Card borrow hover: the {bestHand.Side} hand is on slot "
                    + $"{bestTarget.Slot} of {bestTarget.Source?.BorrowOwnerLabel ?? "?"}'s hand fan — "
                    + "the trigger takes a LOCAL, READ-ONLY copy of it. The owner's card is not "
                    + "touched, their fan order is not changed, and nothing is transmitted.");
            }
        }

        // Claim this hand's trigger while it is on a peer's card, so the same pull can neither
        // far-click the board nor proximity-grab a bystander.
        if (bestHand.Ray.Enabled)
            bestHand.Ray.SuppressFarClick();
        if (bestHand.RayUgui.HasHit)
            return; // genuinely nearer game UI keeps the trigger, as everywhere else
        if (!bestHand.TriggerDown)
            return;

        BeginBorrow(bestTarget, bestHand);
    }

    // ------------------------------------------------------------------ begin / end --

    /// <summary>
    /// Take a LOCAL, READ-ONLY copy of one of the owner's cards into <paramref name="hand"/>.
    ///
    /// <para>WHAT IS SUPPRESSED ON THE COPY, exhaustively — the user's "Nur interagieren oder
    /// umsortieren etc soll man nicht können":
    /// <list type="bullet">
    /// <item><b>No play, no slot, no commit.</b> The copy is not registered with
    /// <c>VRCardFactory</c> and is never hooked by <c>CardsDriver.HookCard</c>, so its
    /// <c>Released</c>/<c>Grabbed</c>/<c>Poked</c> events reach NO driver: the drop state machine,
    /// the tray slots, the pile returns, <c>SelectCard</c>/<c>UnselectCard</c> and the initiative
    /// reconcile are not merely refused, they are unreachable. <see cref="VRCard.InspectOnly"/> is
    /// stamped as well, so any future reader of this object sees the same verdict.</item>
    /// <item><b>No reorder.</b> It is never added to the local <c>CardFan</c> (nothing calls
    /// <c>_fan.Add</c> for it), so it has no fan membership to reorder and its release cannot
    /// insert it into one. The OWNER's fan is untouched by construction: the copy is a separate
    /// GameObject, and their slab stays in their arc for the whole hold.</item>
    /// <item><b>No game widget.</b> <c>AttachGameCard</c> is never called, so no
    /// <c>AbilityCardUI</c> is adopted, re-parented, locked or restored. The face is a throwaway
    /// clone (<c>Net.RemoteCardArt</c>) which is itself made non-interactive — raycasters
    /// destroyed, a blocking <c>CanvasGroup</c> added — so a poke or laser cannot reach a remote
    /// card's action buttons through it.</item>
    /// <item><b>No poke-select.</b> <see cref="VRCard.PokeSelectEnabled"/> stays false, so the copy
    /// never registers as an <c>IPokeable</c> and cannot be click-selected.</item>
    /// <item><b>No laser action.</b> The fan laser only picks cards out of the LOCAL
    /// <c>CardFan</c> (<c>CardFan.TryRaycast</c> walks its own member list), which this copy is not
    /// in; the only thing the laser can do to it is nothing.</item>
    /// <item><b>Nothing on the wire.</b> The local player's own broadcast state — hand card count,
    /// highlight index, held-card pose — is sampled from the local fan and the local tray, neither
    /// of which contains this object. Zero bytes.</item>
    /// </list></para>
    ///
    /// <para>AND IT IS VISIBLY NOT YOURS: the copy's rim is tinted with the owner's own avatar
    /// colour through a <c>MaterialPropertyBlock</c> (per-renderer, so the SHARED card materials
    /// every other card wears are not touched), and it cannot be put down anywhere — releasing it
    /// glides it back onto the owner's slab and it is gone. Those two together were chosen over a
    /// floating name label because they cost one property block and no text layout, and because
    /// "it will not stay in your hand or your tray" is the honest statement of what it is.</para>
    /// </summary>
    private static void BeginBorrow(BorrowTarget target, VRHand hand)
    {
        IBorrowedCardSource? source = target.Source;
        if (source == null)
            return;
        int slot = target.Slot;
        if (!source.BorrowAllowed(slot))
            return;

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var go = new GameObject($"BorrowedCard[{source.BorrowOwnerLabel}#{slot}]");
        // BUILT INACTIVE, then activated. GrabbableBehaviour.OnEnable demands a Collider on the
        // same GameObject and warns (and declines to register) when there is none — and VRCard
        // creates its own collider in Build(), which cannot run before AddComponent. Building under
        // an inactive object is exactly what VRCardFactory does (its PoolRoot is inactive).
        go.SetActive(false);
        // Seed at the slab's world pose so the copy is BORN where the peer's card is and flies to
        // the pinch from there — VRCard.OnGrab carries the world pose through the re-parent, so
        // there is no teleport and the gesture reads as lifting THAT card.
        go.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
        var card = go.AddComponent<VRCard>();
        card.Build(null);          // procedural body: the shared punched-out Ability contour mesh
        card.InspectOnly = true;   // belt: no game seam may ever be reached from this object
        card.PokeSelectEnabled = false;
        card.AllowsGateHand = true; // either hand may hold it, including the fan-carrying one
        TintRim(card, source.BorrowTint);
        // …and ACTIVE before the face clone is built: RemoteCardArt's FitClone is deliberately the
        // final pose write AFTER the clone's OnEnable, and OnEnable does not run under an inactive
        // root — building the clone first would let the widget's own reposition win.
        go.SetActive(true);

        if (!source.ShowBorrowedFace(slot, card.transform, w, h))
        {
            // The clone could not be built (widget gone, gate raced shut between the two checks
            // above). Fail to NOTHING rather than to a mystery card back in the player's hand.
            Object.Destroy(go);
            VRLog.Info("Cards", $"Card borrow refused: slot {slot} of {source.BorrowOwnerLabel}'s hand "
                + "has no readable face this frame (the gate closed, or the widget is gone) — "
                + "nothing was created and nothing was transmitted.");
            return;
        }

        if (!hand.Grabber.ForceGrab(card, releaseOnTriggerUp: true))
        {
            source.ReleaseBorrowedFace();
            Object.Destroy(go);
            // Info, not Warn: the ordinary cause is benign and races nothing important — the
            // grabber took something else with the SAME trigger pull earlier in the frame (its own
            // Tick runs independently of this sweep, and this sweep's "hand is free" test is one
            // step older than ForceGrab's). The copy is destroyed either way, so the only cost is
            // that the player pulls again.
            VRLog.Info("Cards", $"Card borrow not taken: the {hand.Side} hand did not adopt the copy "
                + "(ProximityGrabber.ForceGrab refused — most likely it grabbed something else with "
                + "the same pull). Nothing is left in the world and nothing was transmitted.");
            return;
        }

        _card = card;
        _cardSource = source;
        _cardSlot = slot;
        _cardSlab = target.transform;
        card.Released += OnBorrowedReleased;
        BorrowedCardWatch watch = go.AddComponent<BorrowedCardWatch>();
        watch.Bind(card, source, slot, target);

        _hover = null;
        _hoverHand = null;

        VRLog.Info("Cards", $"Card borrow: the {hand.Side} hand took a LOCAL READ-ONLY copy of slot "
            + $"{slot} of {source.BorrowOwnerLabel}'s hand fan. Gate: {source.BorrowGateLabel}. "
            + "ZERO BYTES TRANSMITTED — the face is a throwaway clone of THIS client's own "
            + "host-replicated widget (Net.RemoteCardArt), no wire field exists for it, and the "
            + "local player's broadcast state (hand count, highlight index, held-card pose) is "
            + "sampled from the local fan and tray, which this copy is not in. The owner's card, "
            + "their fan order and their game state are untouched; play, reorder, slot targeting, "
            + "poke-select and every laser action are unreachable on the copy (see BeginBorrow).");
    }

    /// <summary>
    /// The borrowed copy left a hand. Two outcomes, and only two:
    ///
    /// <para>(1) HAND SWAP. <c>CardsDriver.UpdateHeldCardTransfer</c> is ownerless — it type-tests
    /// <c>hand.Grabber.Held is VRCard</c> and knows nothing about who made the card — so the
    /// borrowed copy already gets the full transfer gesture for free: the reach test against its
    /// own collider, the hover haptic with the same hysteresis, the trigger claim, and the
    /// release+re-grab in ONE call stack (<c>TransferHeldCard</c> → <c>from.Grabber.CancelAll()</c>
    /// → this handler). What it CANNOT do is complete the adoption, because the adoption lives in
    /// <c>CardsDriver.OnCardReleased</c>, which is only subscribed for cards the driver hooked —
    /// and this one is deliberately unhooked. So the adoption is completed HERE, under exactly the
    /// conditions the driver's own gesture creates: the other hand is empty, its trigger is
    /// pressed, and it is within the shared <see cref="FanSweep.ResolveReach"/> envelope of this
    /// card. Anything less is a normal release. Because this runs INSIDE the driver's call stack,
    /// its own `adopted` check sees the card in the receiving hand and no refusal is logged.</para>
    ///
    /// <para>(2) RETURN. Anything else ends the borrow: the copy glides back onto the owner's slab
    /// and is destroyed (see <see cref="BorrowedCardWatch"/>). It is never left standing in the
    /// world, never dropped on a tray, never inserted into any fan.</para>
    /// </summary>
    private static void OnBorrowedReleased(VRCard card, VRHand from, Vector3 velocity)
    {
        if (!ReferenceEquals(card, _card))
            return;

        VRHand? other = ReferenceEquals(from, VRHands.Left) ? VRHands.Right : VRHands.Left;
        if (other != null && other.HasPose && other.Grabber.Held == null && other.TriggerPressed
            && WithinTransferReach(card, other) && other.Grabber.ForceGrab(card, releaseOnTriggerUp: true))
        {
            VRLog.Info("Cards", $"Card borrow hand swap: the borrowed copy of slot {_cardSlot} of "
                + $"{_cardSource?.BorrowOwnerLabel ?? "?"}'s hand moved from the {from.Side} to the "
                + $"{other.Side} hand. Same gesture, same detector and same reach envelope as a card "
                + "of your own (CardsDriver.UpdateHeldCardTransfer); only the ADOPTION is completed "
                + "here, because the copy is deliberately not hooked by the driver. Still zero bytes.");
            return;
        }

        End("released");
    }

    /// <summary>Is <paramref name="hand"/> touching <paramref name="card"/> on the same envelope
    /// <c>CardsDriver.UpdateHeldCardTransfer</c> requires before it offers the swap? Asked so a
    /// plain release cannot be mistaken for a handover just because the other hand happens to be
    /// holding its trigger down somewhere else in the room.</summary>
    private static bool WithinTransferReach(VRCard card, VRHand hand)
    {
        var target = (IFanSweepTarget)card;
        if (!target.TrySweepDistance(hand.Rig.IndexTip.position, out float tipDistance)
            || !target.TrySweepDistance(hand.Rig.PalmCenter.position, out float palmDistance))
            return false;
        FanReach reach = FanSweep.ResolveReach(hand.WorldScale, target.SweepFaceWidthWorld);
        return tipDistance <= reach.Tip * HoverExitScale || palmDistance <= reach.Palm * HoverExitScale;
    }

    /// <summary>
    /// End the live borrow: cancel any hold, release the cloned face, and hand the copy to its own
    /// watch component to glide home and die. Safe to call from anywhere, including from the owning
    /// fan's teardown — which is why the fan calls it on rebuild, hide and destroy.
    /// </summary>
    internal static void End(string reason)
    {
        VRCard? card = _card;
        IBorrowedCardSource? source = _cardSource;
        int slot = _cardSlot;
        Transform? slab = _cardSlab;
        _card = null;
        _cardSource = null;
        _cardSlot = -1;
        _cardSlab = null;
        if (card == null)
            return;

        card.Released -= OnBorrowedReleased;
        card.Holder?.Grabber.CancelAll();

        // The cloned face rides the copy all the way HOME and is released when the copy dies — a
        // card that lost its print half a second before it lost its body would read as a bug, not
        // as a return. The one path that cannot wait is the owning fan being destroyed outright,
        // and RemoteHandFan.Destroy releases it there explicitly.
        var watch = card.GetComponent<BorrowedCardWatch>();
        if (watch != null)
        {
            watch.BeginReturn(slab, source);
        }
        else
        {
            source?.ReleaseBorrowedFace();
            Object.Destroy(card.gameObject);
        }

        VRLog.Info("Cards", $"Card borrow ended ({reason}): the copy of slot {slot} of "
            + $"{source?.BorrowOwnerLabel ?? "?"}'s hand glides back onto their slab and is "
            + "destroyed. Nothing is left in the world, the owner never saw it, and no game state "
            + "or wire byte was written at any point of the hold.");
    }

    /// <summary>End the borrow if it came from <paramref name="source"/> — the call a fan makes
    /// when its slabs are rebuilt, hidden or destroyed, and when the peer leaves. A borrowed copy
    /// must never outlive the fan it was read from.</summary>
    internal static void EndIfFrom(IBorrowedCardSource source, string reason)
    {
        if (_cardSource != null && ReferenceEquals(_cardSource, source))
            End(reason);
    }

    // ------------------------------------------------------------------ tint --

    /// <summary>
    /// Wear the owner's colour on the copy's body through a <see cref="MaterialPropertyBlock"/>.
    /// A property block is a PER-RENDERER override: it does not instantiate, mutate or replace the
    /// shared <c>CardBodyKind.Ability</c> materials, so no other card in the scene changes colour
    /// and the shared alpha-clip silhouette keeps working. A failure here is cosmetic and must not
    /// cost the player the card, so it is swallowed.
    /// </summary>
    private static void TintRim(VRCard card, Color tint)
    {
        try
        {
            MeshRenderer[] renderers = card.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            if (renderers.Length == 0)
                return;
            var block = new MaterialPropertyBlock();
            // Toward the owner's hue but not fully: the card must still read as a CARD.
            Color rim = Color.Lerp(Color.white, tint, 0.65f);
            block.SetColor(ColorProperty, rim);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].SetPropertyBlock(block);
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Cards", $"Card borrow rim tint skipped: {ex.Message}");
        }
    }

    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
}

/// <summary>
/// The borrowed copy's own upkeep, on the copy itself so it is SELF-DRIVING. That is not a style
/// choice: every other candidate driver (the owning fan's Tick, the cards module) stops running in
/// exactly the situations this component has to survive — the peer's fan hiding, the peer leaving,
/// the scenario tearing down. A copy whose driver went away would freeze in the player's hand.
/// </summary>
internal sealed class BorrowedCardWatch : MonoBehaviour
{
    private VRCard? _card;
    private IBorrowedCardSource? _source;
    private int _slot = -1;
    private BorrowTarget? _target;
    private bool _returning;
    private float _returnElapsed;

    internal void Bind(VRCard card, IBorrowedCardSource source, int slot, BorrowTarget target)
    {
        _card = card;
        _source = source;
        _slot = slot;
        _target = target;
    }

    /// <summary>The fan whose cloned face this copy is wearing — released in
    /// <see cref="OnDestroy"/> so the print survives the whole return glide.</summary>
    private IBorrowedCardSource? _faceOwner;

    /// <summary>Stop being a borrowed card and start being a card going home: glide onto
    /// <paramref name="slab"/> (or simply vanish where it is, when the slab has died with the
    /// fan) and destroy on arrival. <paramref name="faceOwner"/> keeps the cloned print alive
    /// until then.</summary>
    internal void BeginReturn(Transform? slab, IBorrowedCardSource? faceOwner)
    {
        if (_returning)
            return;
        _returning = true;
        _returnElapsed = 0f;
        _source = null;
        _target = null;
        _faceOwner = faceOwner;
        if (_card == null)
        {
            Object.Destroy(gameObject);
            return;
        }
        _card.Grabbable = false;   // it is on its way out; nothing may pick it up again
        if (slab != null && slab.gameObject.activeInHierarchy)
            _card.SetHome(slab, Vector3.zero, Quaternion.identity, 1f);
        else
            Object.Destroy(gameObject);
    }

    /// <summary>The clone dies with the copy — never before it, never after it. Idempotent on the
    /// owning fan's side, so the fan's own teardown releasing it first costs nothing here.</summary>
    private void OnDestroy()
    {
        _faceOwner?.ReleaseBorrowedFace();
        _faceOwner = null;
        _source?.ReleaseBorrowedFace();
        _source = null;
    }

    private void Update()
    {
        if (_card == null)
        {
            Object.Destroy(gameObject);
            return;
        }

        if (_returning)
        {
            _returnElapsed += Time.unscaledDeltaTime;
            if (_returnElapsed >= CardBorrow.ReturnSeconds)
                Object.Destroy(gameObject);
            return;
        }

        // THE GATE IS RE-ASKED EVERY FRAME, and it is the OWNER'S gate — the same
        // RevealGate.ShowRoundCardFronts verdict the peer's own slab is drawn under. A phase that
        // turns secret mid-hold therefore dissolves the copy in the frame it turns, exactly as it
        // turns the fan's slabs to backs in that frame. A hold must never be a window in which the
        // rule is briefly not enforced.
        if (_source == null || _target == null || _target.Source == null || !_source.BorrowAllowed(_slot))
        {
            CardBorrow.End(_source == null || _target == null || _target.Source == null
                ? "the owner's fan went away"
                : "the owner's reveal gate closed (RevealGate.ShowRoundCardFronts turned false)");
            return;
        }

        // The card must stay HELD to stay borrowed. A hold lost to anything other than the
        // hand-swap (tracking loss, a mode change cancelling the grabbers) ends the borrow rather
        // than leaving a peer's card floating in the room.
        if (!_card.IsHeld)
        {
            CardBorrow.End("the hold was lost");
            return;
        }

        // Refresh the clone: this is the dedup path in RemoteCardArt.ShowFront, which is also where
        // its mip-bake cadence lives, so a borrowed face gets the same sharpening upkeep a fan face
        // does. Cheap once warm (one instance-id compare).
        _source.ShowBorrowedFace(_slot, _card.transform, CardsConfig.CardWidth.Value, CardsConfig.CardHeight);
    }
}
