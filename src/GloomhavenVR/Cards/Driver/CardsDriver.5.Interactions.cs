using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 5 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: interactions (the drop state machine), pick flows, short rest.

internal sealed partial class CardsDriver
{
    // ------------------------------------------------------------------ interactions --

    /// <summary>
    /// Drop state machine (test #14): a slot placement may fire EXACTLY ONCE per
    /// real user release. Cards enter on OnCardGrabbed (the only way a hand gets a
    /// card) and leave on the matching OnCardReleased — any Released event without
    /// a live grab session (double-fire, stale event after a rebuild/hot reload) is
    /// dropped before it can reach the slot logic.
    /// </summary>
    private readonly HashSet<VRCard> _liveGrabs = new();

    // ------------------------------------------------------- hand-to-hand card transfer --

    /// <summary>The free hand currently in touch reach of the OTHER hand's held card (null =
    /// none) — the hover state of the hand-to-hand transfer. Edge-tracked for the haptic tick
    /// and hysteresis (see <see cref="UpdateHeldCardTransfer"/>).</summary>
    private VRHand? _transferHoverHand;

    /// <summary>The card mid-handover and the hand adopting it — non-null ONLY inside
    /// <see cref="TransferHeldCard"/>'s release+grab call stack. Read by
    /// <see cref="OnCardReleased"/> (adopt instead of routing the drop) and
    /// <see cref="OnCardGrabbed"/> (a handover is not a foreign interaction / pick reopen).</summary>
    private VRCard? _transferCard;
    private VRHand? _transferTo;

    /// <summary>Throttle clock (unscaled seconds) for the transfer hover log line.</summary>
    private float _nextTransferLogAt;

    /// <summary>Reach hysteresis for the transfer hover: once hovering, the free hand must move
    /// this factor beyond the enter reach to lose it — the same enter-smaller-than-exit idea every
    /// other hover in this driver uses, so the haptic tick cannot buzz at the boundary.</summary>
    private const float TransferHoverExitScale = 1.35f;

    /// <summary>
    /// Hand-to-hand card transfer (user addendum 2026-08-04: "dass man die Karte aus einer Hand
    /// in die andere Hand nimmt … wenn er dann den Trigger der freien Hand drückt, während er an
    /// der Karte in der anderen Hand ist, soll die Hand wechseln").
    ///
    /// While one hand HOLDS a VRCard and the other hand is EMPTY, the free hand touching the held
    /// card (index tip / palm against the card's live collider, the same scale-aware
    /// <see cref="FanSweep.ResolveReach"/> envelope the fan sweeps use) gets the standard hover
    /// haptic (<see cref="HapticPreset.HoverTick"/> — the exact feedback a proximity highlight
    /// gives), edge-triggered on reach entry with hysteresis so it cannot buzz. A TriggerDown of
    /// the free hand while in reach hands the card over: see <see cref="TransferHeldCard"/> for
    /// the release+grab ordering. Works in both directions, repeatedly.
    ///
    /// A held card is invisible to every existing hover system on purpose (<c>CanGrab</c> is false
    /// while attached), so this is a dedicated detector rather than a ProximityGrabber candidate;
    /// it CLAIMS the free hand's trigger while in reach (<c>Ray.SuppressFarClick</c> — the exact
    /// mechanism <see cref="ClaimHandFanTrigger"/> uses) so the same pull can neither far-click
    /// the board nor proximity-grab a bystander card, and it yields to a live game-UI hit like
    /// every other trigger owner. Runs BEFORE <see cref="UpdatePalmGate"/> in the tick so a
    /// dominant→gate handover blocks the fan the very same frame. Allocation-free.
    ///
    /// <para>ITEM CARDS TOO (user ruling 2026-08-08: "Auch Item-Karten sollen (wie die normalen
    /// Karten auch) in die linke Hand genommen werden können und Hände getauscht werden können. Sie
    /// sollen also wie normale Karten reagieren"). The detector used to type-test <see cref="VRCard"/>
    /// and nothing else, so a held <c>ItemsPile.ItemChip</c> was simply not a transferable object —
    /// there was no gesture at all, in either direction. Everything this method needs is already on
    /// the shared <see cref="IFanSweepTarget"/> surface both card kinds implement (a live collider
    /// to touch, a world face width for the reach), so the whole gesture — hover, haptic,
    /// hysteresis, trigger claim — is now literally the same code for both; only the COMMIT forks,
    /// because the two kinds route their release through different owners
    /// (<see cref="TransferHeldCard"/> vs <c>ItemsPile.TransferHeldChip</c>).</para>
    /// </summary>
    private void UpdateHeldCardTransfer()
    {
        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        // The transferable shapes: an ability card or an item chip, in EITHER hand. Both are
        // GrabbableBehaviours implementing IFanSweepTarget, which is the only surface used below.
        IFanSweepTarget? leftCard = HeldTransferable(left);
        IFanSweepTarget? rightCard = HeldTransferable(right);
        // Exactly one hand must hold a card and the other must be free — two held cards
        // (possible since the both-hands rule) simply means no hand is free to receive.
        VRHand? holder = leftCard != null ? left : rightCard != null ? right : null;
        IFanSweepTarget? held = leftCard ?? rightCard;
        VRHand? free = ReferenceEquals(holder, left) ? right : left;
        if (held == null || holder == null || free == null || !free.HasPose
            || free.Grabber.Held != null || _modalInputBlocked)
        {
            _transferHoverHand = null;
            return;
        }

        // Touch test against the held card's own collider (enabled while held — the grab keeps
        // it live for exactly this kind of physical query), scale-aware via the shared reach.
        IFanSweepTarget target = held;
        if (!target.TrySweepDistance(free.Rig.IndexTip.position, out float tipDist)
            || !target.TrySweepDistance(free.Rig.PalmCenter.position, out float palmDist))
        {
            _transferHoverHand = null;
            return;
        }
        FanReach reach = FanSweep.ResolveReach(free.WorldScale, target.SweepFaceWidthWorld);
        bool wasHovering = ReferenceEquals(_transferHoverHand, free);
        float exit = wasHovering ? TransferHoverExitScale : 1f;
        if (tipDist > reach.Tip * exit && palmDist > reach.Palm * exit)
        {
            _transferHoverHand = null;
            return;
        }

        if (!wasHovering)
        {
            _transferHoverHand = free;
            // The established hover feedback, on the hand that can act: one HoverTick on the
            // reach-entry edge (rate-limited by the hysteresis above), same preset and strength
            // as every proximity highlight.
            free.SendHaptic(HapticPreset.HoverTick);
            if (Time.unscaledTime >= _nextTransferLogAt)
            {
                _nextTransferLogAt = Time.unscaledTime + 1f;
                VRLog.Info("Cards", $"Hand transfer hover: {free.Side} hand at '{target.SweepName}' held by " +
                                    $"{holder.Side} (tip {reach.Cm(tipDist):F1} cm / palm " +
                                    $"{reach.Cm(palmDist):F1} cm) — trigger hands the card over.");
            }
        }

        // Claim the free hand's trigger while it is on the held card: no board far-click, no
        // proximity grab of a bystander card with the very pull that means "take THIS card".
        if (free.Ray.Enabled)
            free.Ray.SuppressFarClick();
        if (free.RayUgui.HasHit)
            return; // genuinely nearer game UI keeps the trigger, as everywhere else
        if (!free.TriggerDown)
            return;
        // ONE gesture, two owners: an ability card is handed over by this driver (it owns the
        // release routing that must be skipped), an item chip by its pile (same reason — its
        // OnRelease is where the clip-in / glide-home routing lives). See both methods.
        if (held is VRCard card)
            TransferHeldCard(card, holder, free);
        else if (held is ItemsPile.ItemChip chip)
            chip.Owner?.TransferHeldChip(chip, holder, free);
    }

    /// <summary>The held object of <paramref name="hand"/> when it is a card the player may hand to
    /// the other hand — an ability <see cref="VRCard"/> or an <c>ItemsPile.ItemChip</c> — else null.
    /// Both expose the physical surface the transfer gesture needs through
    /// <see cref="IFanSweepTarget"/>, so the detector never needs their concrete types again.</summary>
    private static IFanSweepTarget? HeldTransferable(VRHand? hand)
    {
        IGrabbable? held = hand != null ? hand.Grabber.Held : null;
        return held is VRCard or ItemsPile.ItemChip ? held as IFanSweepTarget : null;
    }

    /// <summary>
    /// Execute the handover: release from <paramref name="from"/> and adopt into
    /// <paramref name="to"/> in ONE call stack. The release (<c>CancelAll</c> → card
    /// <c>OnRelease</c> → <see cref="OnCardReleased"/>) finds <see cref="_transferCard"/> set and
    /// ForceGrabs the card into <paramref name="to"/> INSTEAD of running the drop routing — so
    /// the hands are never observed empty (no fan flash, no net sample of a card-less frame) and
    /// the card keeps its world pose through both re-parents (the existing grab/release pose
    /// carry). The receiving hold is trigger-held (<c>releaseOnTriggerUp</c>), exactly like a
    /// pluck: keep the trigger to keep the card, release it to drop/dock through the normal,
    /// hand-agnostic release routing. If the adoption was refused, <see cref="OnCardReleased"/>
    /// ABORTS the handover by re-adopting the card into the ORIGINAL hand (a refusal must never
    /// route a mid-transfer card into a hidden fan — the "card disappears" bug); only when both
    /// hands refuse does it fall through to the normal routing — either way, somewhere legal.
    /// </summary>
    private void TransferHeldCard(VRCard card, VRHand from, VRHand to)
    {
        _transferCard = card;
        _transferTo = to;
        // A HELD CARD IS NOT A FAN CARD — re-assert it at the adoption seam (user report
        // 2026-08-08, "das Wechseln der Hand … soll immer möglich sein"). This is the SAME stamp
        // OnCardGrabbed makes at every other point where a card becomes held, moved one step
        // earlier for the one grab that starts while the card is already in a hand. The receiving
        // hand is usually the GATE hand (the fan hangs off it), so a stale FALSE here is the one
        // flag that can refuse the adoption outright (VRCard.AllowsHand → ProximityGrabber.
        // ForceGrab). The root cause is fixed at its source (CardFan.StampMembership no longer
        // writes fan verdicts onto a held card); this is the belt, and it is safe by construction:
        // AllowsGateHand is a per-hand HOVER/GRAB filter only — it can never reach a game seam, so
        // it cannot widen what may be COMMITTED (VRCard.InspectOnly, untouched here, still decides
        // that).
        card.AllowsGateHand = true;
        try
        {
            from.Grabber.CancelAll();
        }
        finally
        {
            bool adopted = ReferenceEquals(to.Grabber.Held, card);
            bool aborted = !adopted && ReferenceEquals(from.Grabber.Held, card);
            _transferCard = null;
            _transferTo = null;
            _transferHoverHand = null; // roles flipped; re-derive the hover next frame
            // Refusal outcomes (see OnCardReleased's transfer branch): the primary fallback
            // re-adopts into the ORIGINAL hand (abort — the card visibly stays put); only when
            // both hands refuse does the release route normally. Named apart so a hardware log
            // can tell the safe abort from an actual routed drop.
            if (aborted)
                VRLog.Warn("Cards", $"Hand transfer: adoption into the {to.Side} hand was refused — " +
                                    $"ABORTED, the card stays held in the {from.Side} hand.");
            else if (!adopted)
                VRLog.Warn("Cards", $"Hand transfer: adoption into the {to.Side} hand AND the " +
                                    $"re-adoption into the {from.Side} hand were refused — the card " +
                                    "took the normal release routing instead (no limbo).");
        }
    }

    /// <summary>
    /// Spell out WHICH gate refused a hand-to-hand adoption, in the exact order
    /// <see cref="ProximityGrabber.ForceGrab"/> evaluates them. Called ONLY on the refusal path
    /// (never per frame), with the card un-held, so every field reads the value ForceGrab saw.
    ///
    /// The original hardware hunt for this bug cost a whole session because the refusal was
    /// invisible: ForceGrab's own diagnostic shares a 1 s per-hand throttle with the grabber's
    /// no-candidate line, which the receiving hand's trigger press had just consumed. Naming the
    /// gate HERE, in the Cards log, makes the next occurrence self-explaining.
    /// </summary>
    private static string DescribeAdoptionRefusal(VRCard card, VRHand to)
    {
        string blocked = VRCard.InteractionBlockedHand != null
            ? VRCard.InteractionBlockedHand.Side.ToString()
            : "none";
        return $"receiver grabber enabled={to.Grabber.Enabled}, receiver pose={to.HasPose}, "
             + $"receiver already holds={(to.Grabber.Held != null ? "yes" : "no")}, "
             + $"CanGrab={card.CanGrab} (Grabbable={card.Grabbable}, held={card.IsHeld}, "
             + $"vrMode={Core.Events.VRModeStateMachine.CurrentMode}), "
             + $"AllowsHand({to.Side})={card.AllowsHand(to)} (AllowsGateHand={card.AllowsGateHand}, "
             + $"fan/gate hand={blocked}), InspectOnly={card.InspectOnly}";
    }

    private void OnCardGrabbed(VRCard card, VRHand hand)
    {
        _liveGrabs.Add(card);
        // Hand-to-hand transfer (user addendum 2026-08-04): the adopting grab of a handover is
        // NOT a new player interaction — the card never left the hands. It must neither dismiss
        // an open browse (ForeignInteraction) nor poke the pick-reopen seam; the trigger that
        // caused it was consumed by the transfer (see UpdateHeldCardTransfer).
        bool transferring = ReferenceEquals(card, _transferCard);
        // More-card-sounds: soft pick tick on every card grab (fan pluck, slot pluck, pile/
        // active read-grab — proximity and laser alike; also the adopting grab of a hand-to-hand
        // transfer, where it doubles as the audible handover feedback). Edge-triggered by nature:
        // Grabbed fires exactly once per grab session. The game plays nothing of its own here (a
        // physical VR grab has no game call), so no stacking.
        PlayCardSound(CardsConfig.CardGrabSound.Value, card.transform);
        // Item 8: grabbing a hand/tray/field card while a browse is open is a foreign
        // interaction. Grabbing a BROWSE card is part of the browse (read close), so
        // it is exempt — only the arc's own cards may be plucked without dismissing.
        if (!_browser.Contains(card) && !transferring)
            ForeignInteraction("card grabbed");
        // Accident window (test #19): a pluck FROM a slot or the pick field means
        // the hand is working right next to CONFIRM — arm the suppression guard.
        if (_tray.SlotOf(card) >= 0 || _fieldCards.Contains(card))
            _tray.NoteSlotActivity();
        // Task #11 (free swap): grabbing ANY pick card (a placed one off the tray OR a
        // fresh candidate from the fan) while the game's burn/lose CONFIRM popup is
        // open re-opens the selection through the game's own "choose another card"
        // seam — see ReopenPickSelection. Without this the take-back ran UnselectCard
        // under a live popup, a state the 2D game forbids (it locks all cards while
        // the popup shows), and the stale popup's commit then indexed an empty
        // selectedCardsUI → GlobalErrorMessage → dumped to the main menu.
        if (!transferring)
            MaybeReopenPickSelection(card);
        if (_fan.Contains(card))
        {
            _fanOriginCards.Add(card); // reorder: eligible for a fan-gap commit on release
            _fan.Remove(card);
        }
        // A HELD card is never a fan card, so the gate-hand veto lifts NOW for EVERY grab —
        // this is THE single stamping point where any card becomes held (all pluck paths —
        // proximity, laser, T2 rescue, pull-jerk grace, fingertip poke, transfer adoption —
        // funnel through VRCard.OnGrab -> Grabbed -> here). Previously stamped only inside
        // the fan branch above; unconditional now (general rule 2026-08-04, see
        // VRCard.AllowsGateHand): the held card must be hand-to-hand transferable to the
        // gate hand immediately, and a stale FALSE would make that hand's
        // ProximityGrabber.HealDeadHeld force-drop it mid-hold. Idempotent for every
        // non-fan zone (their Rebuild verdict is TRUE anyway); the fan re-entry seams
        // (CardFan.Add/SetCards) stamp it back the moment the card returns.
        card.AllowsGateHand = true;
        // Tray occupancy stays until the release decides select/unselect/swap.
    }

    private void OnCardReleased(VRCard card, VRHand hand, Vector3 velocity)
    {
        if (!_liveGrabs.Remove(card))
        {
            VRLog.Warn("Cards", $"Release without live grab ignored ({card.name}) — drop path is once-per-release.");
            return;
        }

        // Hand-to-hand transfer (user addendum 2026-08-04): this release is the FIRST half of a
        // handover — adopt the card into the receiving hand INSIDE the release call stack, so no
        // frame (and no net rig sample) can ever observe it un-held, and none of the drop routing
        // below runs (the card never left the hands; docking/fan-return/pick seams would all be
        // wrong). The fan-origin marker deliberately survives: a card plucked from the fan and
        // handed over still commits into a fan gap when its FINAL release is a void release.
        if (ReferenceEquals(card, _transferCard) && _transferTo != null)
        {
            if (_transferTo.Grabber.ForceGrab(card, releaseOnTriggerUp: true))
            {
                // INSPECT-ONLY PROOF (user report 2026-08-08): this line is once per handover — no
                // per-frame spam — and it is the one that says an inspect-only card really changed
                // hands INSTEAD of taking the home-return branch further down. Seeing
                // "handed … INSPECT-ONLY" without an "Inspect release" in the same breath is the
                // whole fix in one grep.
                VRLog.Info("Cards", $"Hand transfer: '{card.name}' handed {hand.Side} → " +
                                    $"{_transferTo.Side} (trigger on the held card) — release routing " +
                                    "skipped, hold continues on the receiving hand's trigger. " +
                                    (card.InspectOnly
                                        ? "Card is INSPECT-ONLY: it STAYS inspect-only in the receiving hand and " +
                                          "the home-return branch was NOT taken (no _fan.Add, no game call at all) " +
                                          "— its FINAL release will return it home."
                                        : "Card is committable: its FINAL release routes normally."));
                return;
            }
            // ADOPTION REFUSED — ABORT the transfer instead of releasing (fan-transfer vanish,
            // hardware log 5216-5297): a refusal used to fall through to the normal drop routing,
            // and for a fan-origin card that routing is "return to fan" — a fan that is CLOSED at
            // this very moment (receiving a card is the same wrist roll that shuts the palm gate),
            // so the card visually vanished mid-handover. Whatever refused the adoption (mode
            // policy edge, a filter re-armed mid-release), the strictly safer outcome is that the
            // card simply STAYS in the hand that was holding it: re-adopt into the RELEASING hand
            // in this same call stack (its Grabber.Held is already null — CancelAll cleared it
            // before dispatching this release). The re-adopted hold keeps the button that is
            // PHYSICALLY still pressed: trigger-held when the trigger is down (the pluck norm),
            // grip-held otherwise (a grip-hold re-adopted trigger-held would release itself the
            // very next Tick and vanish through the routing after all). No frame observes the
            // card un-held either way.
            // NAME THE GATE while the card is genuinely un-held (the re-adoption below flips
            // IsHeld/CanGrab back and would make every field read misleading). ProximityGrabber's
            // own ForceGrab refusal is Info-throttled 1/s PER HAND and the receiving hand has
            // usually just spent that budget on its own no-candidate trigger diagnostic, so the
            // reason was swallowed on hardware every single time — this is the line that survives.
            string gate = DescribeAdoptionRefusal(card, _transferTo);
            if (hand.Grabber.ForceGrab(card, releaseOnTriggerUp: hand.TriggerPressed))
            {
                VRLog.Warn("Cards", $"Hand transfer ABORTED: adoption of '{card.name}' into the " +
                                    $"{_transferTo.Side} hand was refused ({gate}) — the card stays in the " +
                                    $"{hand.Side} hand (no release routing, nothing vanishes).");
                return;
            }
            // Both hands refused (the card itself became ungrabbable this very frame): fall
            // through to the normal routing as the last honest resort — never limbo.
        }

        // Hand reorder: was this card plucked out of the fan? (consumed here, used by the void
        // release branch below to commit into a gap or cancel to origin).
        bool fanOrigin = _fanOriginCards.Remove(card);

        if (_fakeActive)
        {
            RouteFakeRelease(card, hand);
            return;
        }

        // Pile-browse card (item 5): plucked out for a close read — return it to the
        // reading arc, NEVER into the select/slot seams below (these are discard/burnt
        // cards, not hand cards; committing them would be wrong). Purely informational.
        if (_browser.IsOpen && _browser.Contains(card))
        {
            _browser.Add(card);
            return;
        }

        // Active-card (feature 6): plucked out to read close — return it to the active
        // column, NEVER into the select/slot seams below (active cards are informational,
        // not selectable; committing one would be wrong). Same read-only contract as the
        // pile browse arc.
        if (_active.IsShown && _active.Contains(card))
        {
            _active.Add(card);
            return;
        }

        // PILE-ORIGIN FALLBACK (user report 2026-08-04, "abgeworfene Karte im Handfaecher"): a
        // card the browse arc borrowed from the discard/burnt pile whose browser CLOSED while the
        // card was held. The branch above never catches it - PileBrowser.Close() cleared the list
        // (and IsOpen) mid-hold, which the close ledger proves ("borrowed 1 ... returned 0", the
        // collapse skips held cards) - so the release used to fall through to the HAND-card
        // routing below, whose void case is "_fan.Add(card)": the hardware log's "Drop (Right):
        // ... rule=none -> return to fan" put a DISCARDED card into the hand fan (n=4 -> n=5),
        // and the fan count is what peers receive as the hand-card count. The marker lives on the
        // card (VRCard.PileOrigin) precisely so it survives every hold path - grab, T2 rescue,
        // hand-to-hand transfer, transfer abort - and this single branch routes ALL of them back
        // to their pile (arc if open, else the stack). Runs BEFORE the pick-mode branch: a card
        // still marked at release was never re-homed into a pick fan by a rebuild (the zone loop
        // retires the marker there), so committing it through a pick seam would be wrong too.
        if (card.PileOrigin is PileKind pileOrigin)
        {
            ReturnCardToPile(card, pileOrigin, hand);
            return;
        }

        // INSPECTION RELEASE (user ruling 2026-08-08: "Ich möchte das man jederzeit auch eine Karte
        // aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die Karte nirgendwo
        // ablegen kann. Das soll also niemals blockiert sein"). The card was picked up purely to be
        // READ — the fan was in CardFan.FanMode.Inspect when it was grabbed (VRCard.InspectOnly is
        // stamped by the zone funnel and by the fan's own membership seams). It returns HOME to the
        // fan, animated, and NOTHING else happens: no SelectCard, no UnselectCard, no slot
        // occupancy, no initiative reconcile, not even a fan-reorder commit.
        //
        // POSITION IN THE ROUTING IS LOAD-BEARING — this sits BEFORE CurrentHand() on purpose, so
        // no game hand is even resolved for an inspect-only card. That matters most for a focus
        // view of one of our OWN characters: CurrentHand() is the hand the GAME presents, which in
        // a focus view is a DIFFERENT character, so every branch below would evaluate this card's
        // ability against the wrong hand. The branch above it (PileOrigin) is deliberately kept
        // first: a browse-borrowed card must go back to its PILE, not into a hand fan.
        //
        // The fan-origin marker has already been consumed above, so nothing leaks; ClearFanInsertion
        // drops any glowing gap so the return-home is unambiguous.
        if (card.InspectOnly)
        {
            ClearFanInsertion();
            _fan.Add(card); // animated return HOME — the same glide every refused drop uses
            VRLog.Info("Cards", $"Inspect release ({hand.Side}): '{card.name}' returns HOME to the hand fan — " +
                                $"the GRAB was allowed, the PLACEMENT is refused by: {_placementRefusal}. " +
                                "No game state was written (no SelectCard/UnselectCard, no slot occupancy, no " +
                                "reorder commit); the card is immediately grabbable again for another look.");
            return;
        }

        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null || card.GameCard == null)
        {
            _fan.Add(card);
            return;
        }

        // WRONG-HAND BELT (structural, not a convention). Every branch below reaches a game seam
        // that names `gameHand` — CardsGameApi.SelectCard / UnselectCard / ReconcileInitiative — and
        // `gameHand` is the hand the GAME presents, which is NOT always the hand this card came out
        // of (a focus view renders another character's fan; a hand teardown/rebuild can swap the
        // presented hand mid-hold). Committing a card the presented hand does not own would write
        // the wrong character's round pile. The game's own widget list is the authority on
        // ownership, so ask it: a card the presented hand does not list simply goes home.
        // Never a silent no-op: any physical bookkeeping the card still carries (tray occupancy, a
        // pick-field seat) is released too and a rebuild is requested, so the board re-derives
        // everything from authoritative state instead of keeping a card that is both "in a slot"
        // and "in the fan".
        if (!CardsGameApi.HandOwnsWidget(gameHand, card.GameCard))
        {
            ClearFanInsertion();
            if (_tray.ContainsCard(card))
                _tray.RemoveCard(card);
            if (_fieldCards.Remove(card))
            {
                _pickExitFlown.Remove(card);
                RelayoutField();
            }
            _fan.Add(card);
            _dirty = true;
            VRLog.Warn("Cards", $"Drop REFUSED ({hand.Side}): '{card.name}' is not a card of the hand the game " +
                                $"presents ('{Board.CharacterFocus.Describe(gameHand.PlayerActor)}') — the grab was " +
                                "allowed, but committing it would write ANOTHER character's state. Returned home " +
                                "and a rebuild requested; no game call was made.");
            return;
        }

        // Pick modes (test #21 B): the drop field is the only target — the slot
        // logic below is CardsSelection-only.
        // ITEM 6b: LIVE, not the latched mode. A card released after the burn flow ended must fall
        // through to the ordinary hand routing (which returns it to the fan) instead of being
        // seated into a pick field nobody is asking to fill.
        if (PickFlowLive(gameHand))
        {
            HandlePickRelease(card, hand, gameHand);
            return;
        }

        // ...AND A PICK MODE WHOSE FLOW IS DEAD ROUTES NOWHERE ELSE. The slot logic below is
        // CardsSelection's, and it can reach CardsGameApi.SelectCard — so a card the player was
        // still HOLDING when the burn flow ended (the field occupants are dropped by that same
        // rebuild, and the zone loop skips held cards, so such a card carries neither a field seat
        // nor the fan's InspectOnly stamp) would fall through into the ROUND-CARD commit while the
        // hand's mode is LoseCard. That is a game-state write in a phase nobody is selecting in.
        // It goes home instead, exactly as the inspect release does, and a rebuild is requested so
        // the board re-derives the hand from authoritative state.
        if (CardsGameApi.IsPickMode(CardsGameApi.Mode(gameHand)))
        {
            ClearFanInsertion();
            if (_tray.ContainsCard(card))
                _tray.RemoveCard(card);
            if (_fieldCards.Remove(card))
            {
                _pickExitFlown.Remove(card);
                RelayoutField();
            }
            _fan.Add(card);
            _dirty = true;
            VRLog.Info("Cards", $"Drop ({hand.Side}): '{card.name}' returns HOME — the hand's mode is still " +
                                $"{CardsGameApi.Mode(gameHand)} but that pick flow has ENDED (the game latches " +
                                "CardsHandUI.currentMode; CardsGameApi.PickFlowLive is the live term). No " +
                                "SelectCard, no slot occupancy, nothing on the wire — the round-card commit " +
                                "below belongs to CardsSelection alone.");
            return;
        }

        // Test #15 accept rules, in priority order:
        // 1. HIGHLIGHT: the slot that was GLOWING for this card at release wins —
        //    hardware logs showed the release gesture consistently moving the hand
        //    just out of radius (3.2–6 m vs 2.74 m at diorama scale ~23) while the
        //    glow HAD triggered; what glows is what drops, guaranteed.
        // 2. RADIUS fallback: generous dual-sample capture (test #13) — card center
        //    AND holding-hand palm both make a recess ELIGIBLE, but WHICH of the two
        //    eligible recesses wins is the card centre's answer alone (2026-08-11, see
        //    PlayTray.SlotNear's remarks). Rule 1 above ranks through the very same call
        //    (UpdateSlotHighlight → SlotNear), so the glow and this fallback cannot disagree.
        int highlightSlot = ReferenceEquals(_snapHighlightCard, card) ? _snapHighlightSlot : -1;
        int slot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out PlayTray.SlotProbe probe);
        bool wasInTray = _tray.ContainsCard(card);
        CAbilityCard ability = card.GameCard.AbilityCard;

        // Item 2 (return-to-origin): a card plucked OUT of a slot may be dropped back
        // onto the SAME slot it came from to RESTORE it there — the origin slot is now
        // ALWAYS a valid drop target for its own card (the glow telegraphs it too, see
        // UpdateSlotHighlight). The OLD rule force-excluded the origin here (highlight/
        // radius reset to -1 whenever they resolved to the source slot), so the only way
        // back into the tray was the OTHER slot — restoring a card to its exact original
        // slot was impossible (the reported bug). To UNSELECT / take a card back to the
        // fan the player releases it AWAY from BOTH slots (slot < 0 → the take-back path
        // below), which the free-placement flow already implies. No self-exclusion now:
        // origin, other slot and the void are all reachable, so free movement is intact.

        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot;

        // Task #4b GATE: card selection needs TWO cards (or a declared rest). When the
        // hand+round pool has fewer than 2 playable cards the requirement is
        // unsatisfiable (exact game rule mirrored in CardsGameApi.MustRestInsteadOfPlay:
        // IsCardSelectionReady needs RoundAbilityCards >= 2 || LongRest,
        // CPlayerActorExtensions.cs:5; hand+round < 2 is the rule engine's own
        // exhaustion formula, CCharacterClass.cs:1766) — the player must rest instead.
        // Refuse the fan→slot drop with the existing return-home glide. Tray-origin
        // drops (reorder / take-back) stay allowed: they never grow the round pile.
        if (slot >= 0 && !wasInTray && CardsGameApi.MustRestInsteadOfPlay(gameHand))
        {
            VRLog.Info("Cards", $"Drop REFUSED ({hand.Side}): only " +
                                $"{CardsGameApi.PlayableCardCount(gameHand)} playable card(s) left — " +
                                "selection needs TWO cards; rest instead (short/long). " +
                                $"'{ability.Name}' returns to the fan.");
            _fan.Add(card); // refuse/return-home path — the card glides back
            return;
        }

        // THE one log line per real drop (test #14; #15 adds the accepting rule;
        // item 27.1 adds the fan→occupied swap outcome). A fan card landing on an
        // occupied slot swaps: the newcomer takes the slot, the occupant → hand.
        string outcome =
            slot < 0 ? (wasInTray ? "take back to fan." : "return to fan.")
            : wasInTray ? $"reorder to slot {slot + 1}."
            : _tray.Occupant(slot) != null ? $"swap into slot {slot + 1} (occupant → hand)."
            : $"play into slot {slot + 1}.";
        VRLog.Info("Cards", $"Drop ({hand.Side}): {probe.Describe()}, rule={rule} → {outcome}");

        // Accident window (test #19): every drop/take-back touching the slots arms
        // the tray's CONFIRM guard — the release gesture is exactly what brushed
        // CONFIRM in the hardware log.
        if (slot >= 0 || wasInTray)
            _tray.NoteSlotActivity();

        if (slot >= 0 && !wasInTray && _tray.Occupant(slot) == null)
        {
            // Fan → empty slot: play the card. The snap itself is PlaceCard's SetHome —
            // a quick local lerp into the slot (CardLerpSpeed) — plus a click pulse
            // so the zap is felt, not just seen (test #13).
            // Task #5 (double sound): NO mod place sound here — the queued SelectCard's
            // AbilityCardUI.ToggleSelect plays the card's serialized profile click for a
            // locally-controlled hand (mouseDownAudioItem, AbilityCardUI.cs:1182-1185),
            // and our thunk on top made every slot placement sound doubled. The game's
            // click IS the placement sound on all Select/Unselect paths.
            hand.SendHaptic(HapticPreset.ClickPulse);
            // SelectCard is the spin-wait path — queued; outcome verified against
            // the authoritative round pile afterwards.
            _tray.PlaceCard(card, slot);
            // HAND<->SLOT FLIGHTS ARE NOT REPORTED (user ruling 2026-08-03: "Ich will immer die
            // echte Position der Karten sehen … Wenn der Mitspieler eine Karte auf das Board legt
            // kommt danach direkt eine Animation wie eine Karte von der Hand auf das Board fliegt.
            // Das ist aber doppelt … entferne die wieder."). A peer already WATCHES the real thing:
            // the remote hand carries the card and the slot occupancy is mirrored, so replaying a
            // second, synthetic flight afterwards shows the same move twice, out of step with the
            // first. Only flights a peer CANNOT otherwise see stay reported (card -> discard/burnt
            // pile). DO NOT RE-ADD: the absence of the call is the fix.
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.SelectCard(handRef, ability),
                () =>
                {
                    if (!CardsGameApi.IsInRound(handRef, ability))
                    {
                        VRLog.Info("Cards", $"Select rejected for {ability.Name} — returning to fan.");
                        _tray.RemoveCard(card);
                        _fan.Add(card);
                    }
                    ReconcileInitiative(handRef);
                    _dirty = true;
                });
        }
        else if (slot >= 0 && !wasInTray)
        {
            // (b) Fan → OCCUPIED slot: SWAP. The newcomer takes the slot and the card
            // it displaces returns to the hand — the whole point being a swap even when
            // BOTH slots are full (trade one of two played cards). Unselect the occupant
            // FIRST so the round pile (max two) has room, THEN select the newcomer; both
            // queued so they serialize one-per-frame in that order. Verified against the
            // authoritative round pile like the plain play, newcomer bounced to the fan
            // on rejection.
            hand.SendHaptic(HapticPreset.ClickPulse);
            // Task #5: no mod sound — the queued Unselect+Select pair below already plays
            // the game's own profile clicks (ToggleSelect both ways); ours made a third.
            VRCard displaced = _tray.Occupant(slot)!;
            CAbilityCard? displacedAbility = displaced.GameCard?.AbilityCard;
            _tray.RemoveCard(displaced);
            _fan.Add(displaced);
            _tray.PlaceCard(card, slot);
            // HAND<->SLOT FLIGHTS ARE NOT REPORTED (user ruling 2026-08-03: "Ich will immer die
            // echte Position der Karten sehen … Wenn der Mitspieler eine Karte auf das Board legt
            // kommt danach direkt eine Animation wie eine Karte von der Hand auf das Board fliegt.
            // Das ist aber doppelt … entferne die wieder."). A peer already WATCHES the real thing:
            // the remote hand carries the card and the slot occupancy is mirrored, so replaying a
            // second, synthetic flight afterwards shows the same move twice, out of step with the
            // first. Only flights a peer CANNOT otherwise see stay reported (card -> discard/burnt
            // pile). DO NOT RE-ADD: the absence of the call is the fix.
            CardsHandUI handRef = gameHand;
            if (displacedAbility != null)
                CardActionQueue.Enqueue(
                    () => CardsGameApi.UnselectCard(handRef, displacedAbility),
                    () => _dirty = true);
            CardActionQueue.Enqueue(
                () => CardsGameApi.SelectCard(handRef, ability),
                () =>
                {
                    if (!CardsGameApi.IsInRound(handRef, ability))
                    {
                        VRLog.Info("Cards", $"Swap select rejected for {ability.Name} — returning to fan.");
                        _tray.RemoveCard(card);
                        _fan.Add(card);
                    }
                    ReconcileInitiative(handRef);
                    _dirty = true;
                });
        }
        else if (slot >= 0 && wasInTray)
        {
            // Tray → tray: physical reorder. If the other slot is occupied this is an
            // initiative swap; a lone card just changes slots visually.
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            PlayCardSound(CardsConfig.CardPlaceSound.Value, card.transform); // more-card-sounds: placing thunk
            int oldSlot = _tray.SlotOf(card);
            if (slot != oldSlot && _tray.Occupant(slot) != null)
            {
                VRCard other = _tray.Occupant(slot)!;
                _tray.PlaceCard(other, oldSlot);
                _tray.PlaceCard(card, slot);
                OnSwapRequested();
            }
            else
            {
                _tray.PlaceCard(card, slot);
                CardsHandUI handRef = gameHand;
                CardActionQueue.Enqueue(() => ReconcileInitiative(handRef), () => _dirty = true);
            }
        }
        else if (wasInTray)
        {
            // HAND<->SLOT FLIGHTS ARE NOT REPORTED (user ruling 2026-08-03: "Ich will immer die
            // echte Position der Karten sehen … Wenn der Mitspieler eine Karte auf das Board legt
            // kommt danach direkt eine Animation wie eine Karte von der Hand auf das Board fliegt.
            // Das ist aber doppelt … entferne die wieder."). A peer already WATCHES the real thing:
            // the remote hand carries the card and the slot occupancy is mirrored, so replaying a
            // second, synthetic flight afterwards shows the same move twice, out of step with the
            // first. Only flights a peer CANNOT otherwise see stay reported (card -> discard/burnt
            // pile). DO NOT RE-ADD: the absence of the call is the fix.

            // Tray → elsewhere: take the card back.
            // Task #5: no mod sound — the queued UnselectCard plays the card's serialized
            // profile click via AbilityCardUI.ToggleSelect (deselect path, AbilityCardUI.cs:1184);
            // the soft undo click on top doubled it.
            // T1 (tray → fan position): if the fan's insertion gap was glowing for THIS card at
            // release, the take-back lands at that gap instead of the game-sorted position —
            // same "what glows is what drops" contract as the fan-origin reorder. The gap and its
            // neighbour ids are captured NOW (the fan may change while the unselect is queued);
            // the _fanOrder splice runs in the unselect COMPLETION so the rebuild it triggers
            // sees the card back in the game hand — splicing earlier would race ReorderFanBuffer's
            // prune (the id is not in the hand until the unselect lands) and lose the position.
            int gap = ReferenceEquals(_insertHighlightCard, card) ? _insertGap : -1;
            int insertBeforeId = int.MinValue; // id of the card right of the gap (insert before it)
            int insertAfterId = int.MinValue;  // id of the last fan card (gap past the end)
            if (gap >= 0)
            {
                IReadOnlyList<VRCard> fanNow = _fan.Cards;
                if (gap < fanNow.Count)
                    insertBeforeId = FanId(fanNow[gap]);
                else if (fanNow.Count > 0)
                    insertAfterId = FanId(fanNow[fanNow.Count - 1]);
                ClearFanInsertion();
                hand.SendHaptic(HapticPreset.ClickPulse);
                VRLog.Info("Cards", $"Fan reorder ({hand.Side}): TRAY card take-back committed to fan " +
                                    $"gap {gap} (unselect queued; session-only VR order).");
            }
            _tray.RemoveCard(card);
            _fan.Add(card);
            int cardId = FanId(card);
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.UnselectCard(handRef, ability),
                () =>
                {
                    if (gap >= 0 && cardId != int.MinValue)
                    {
                        _fanOrder.Remove(cardId);
                        int at = _fanOrder.Count;
                        if (insertBeforeId != int.MinValue)
                        {
                            int idx = _fanOrder.IndexOf(insertBeforeId);
                            at = idx >= 0 ? idx : _fanOrder.Count;
                        }
                        else if (insertAfterId != int.MinValue)
                        {
                            int idx = _fanOrder.IndexOf(insertAfterId);
                            at = idx >= 0 ? idx + 1 : _fanOrder.Count;
                        }
                        _fanOrder.Insert(at, cardId);
                    }
                    _dirty = true;
                });
        }
        else if (fanOrigin && ReferenceEquals(_insertHighlightCard, card) && _insertGap >= 0)
        {
            // Hand reorder COMMIT: released over an open gap — insert at that index. "What glows
            // is what drops," identical to the slot rule. Pure VR presentation (no game call).
            int gap = _insertGap;
            ClearFanInsertion();
            hand.SendHaptic(HapticPreset.ClickPulse);
            CommitFanInsertion(card, gap);
            VRLog.Info("Cards", $"Fan reorder ({hand.Side}): card committed to fan gap {gap} (session-only VR order).");
        }
        else
        {
            // Released in the void. A fan-originating card NOT over a gap CANCELS: _fanOrder still
            // holds its original position, so the rebuild re-seats it at its origin index (return-
            // to-origin). Non-fan cards just animate back into the fan as before.
            ClearFanInsertion();
            _fan.Add(card); // animated return
            if (fanOrigin)
                _dirty = true; // rebuild re-applies _fanOrder → snaps back to the original index
        }
    }

    private void OnCardPoked(VRCard card, VRHand hand)
    {
        if (_fakeActive || card.GameCard == null)
            return;
        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null)
            return;
        // Modal pick fallback (test #21 B): poke commits through the SAME seam as
        // the drop field — one guard, no double-commit.
        TryCommitPick(card, gameHand, "poke");
    }

    // ------------------------------------------------------------------ pick flows --

    /// <summary>Cards physically laid onto the pick drop field (selected candidates).</summary>
    private readonly List<VRCard> _fieldCards = new(4);

    /// <summary>
    /// EVENT-DISCARD BATCHING (pre-scenario "Begegnungen" mali): when a pick demands MORE
    /// than the two physical slot recesses (e.g. two stacked road events → "discard 3"),
    /// the selection runs in BATCHES OF TWO: fill the recesses, press the tray CONFIRM
    /// ("WEITER") to LOCK the batch, then the recesses free up for the next batch. The
    /// first <see cref="_pickLockedCount"/> entries of <see cref="_fieldCards"/> are the
    /// locked cards — they stay game-selected (the game only counts selections; the
    /// batching is pure VR presentation) and stack BESIDE Slot2 on the overflow seats.
    /// The game's own confirm DialogPopup still opens the moment the TOTAL count is
    /// selected, and the tray CONFIRM then presses ITS commit option. Reset on leaving
    /// the pick mode; decremented whenever a locked card is taken back / pruned.
    /// </summary>
    private int _pickLockedCount;

    /// <summary>
    /// Short-rest sacrifice display (test #25, item 1d): the randomly lost card laid
    /// physically at the board centre while the docked burn/redraw DialogPopup decides
    /// its fate — the same sacrifice display as the avoid-damage burn. Its OWN path,
    /// deliberately separate from <see cref="_fieldCards"/>: DISPLAY-ONLY, never
    /// grabbable / poke-select / droppable (the docked choice commits burn/redraw).
    /// <see cref="_shortRestPresented"/> is the change-dedup key (the CAbilityCard
    /// currently shown) — on REDRAW the game swaps it for the alternate card and this
    /// path re-adopts the new one.
    /// </summary>
    private VRCard? _shortRestCard;
    private CAbilityCard? _shortRestPresented;

    /// <summary>Change-dedup for the pick-fan source line (item 9): (mode, source pile).</summary>
    private (CardHandMode mode, CardPileType source)? _loggedPickSource;

    /// <summary>Change-dedup for the pick-fill GATE line (item 11d): (mode, open, refused, field).</summary>
    private (CardHandMode mode, bool open, bool refused, int field)? _loggedPickGate;

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 item 11d, "eine bereits verbrannte Karte kam wieder zurueck aus dem
    /// Stapel auf das Board ... war aber garnicht mehr am Zug"): state, once per change, whether a
    /// modal pick is REALLY open and whether this fill refused any candidate for sitting in a pile
    /// its mode may not draw from. Grep token: <c>PICK GATE</c>.
    ///
    /// <para>Change-gated on (mode, open, refused&gt;0), so it is at most a handful of lines per
    /// pick and NEVER per frame even though the fill it guards runs on every rebuild.</para>
    ///
    /// <para>WHERE IT BIT, AND IT IS A DEATH. The 2026-09-05 host log strands one card across the
    /// session's only player death (<c>Player.log:255544</c>, <c>MindthiefID ... -2 health</c> /
    /// <c>ActorDead</c>): <c>Rebuild: mode=LoseCard</c> runs UNBROKEN through EndTurnLoot, EndTurn,
    /// EndRound, StartRoundEffects, PlayerExhausted, Autosave and into the next round's
    /// SelectAbilityCardsOrLongRest, where the banner asks for a burn again (<c>:256719</c>) and
    /// the pick field re-seats a card parked in the burnt stack 4 000 lines earlier
    /// (<c>:256759</c>). The game never re-drives <c>currentMode</c> on death, so nothing on our
    /// side may treat that mode as evidence a pick is live.</para>
    ///
    /// <para>PROOF the fix landed: after a burn commits, one line with <c>pick=CLOSED</c> and
    /// <c>cardsLyingInThePickField=0</c> - the game's own <c>maxCardsSelected</c> read 0, so
    /// nothing was seated and the banner stood down, and the parked occupant left the field. Read
    /// it together with the absence of any later <c>Pick fan source (LoseCard): burnt pile</c> or
    /// <c>Fly-to-pile REFUSED [pick field]</c>, which are the two pre-fix lines this gate exists to
    /// stop.</para>
    ///
    /// <para>FALSIFIERS - three, and they say different things. (1) <c>pick=OPEN</c> standing for
    /// minutes after the last <c>Pick commit</c>: the game leaves a nonzero count behind too, so
    /// the count is not the right term. (2) A <c>Pick fan source (LoseCard): burnt pile</c> line
    /// again WITH <c>refusedFromIllegalPile=0</c>: the burnt widgets reached the fill through a
    /// pile this test does not name. (3) <c>cardsLyingInThePickField</c> staying nonzero across a
    /// death or a round boundary: the PARKED term in the field prune did not catch the stranded
    /// occupant, and the card is still being re-seated onto the board.</para>
    /// </summary>
    private void LogPickFillGate(CardHandMode mode, bool pickOpen, int refusedPile, CardsHandUI? hand)
    {
        bool flowLive = Patches.PickFlowWatch.Live;
        bool presented = CardsGameApi.PickHandIsPresented(hand);
        var key = (mode, pickOpen, refusedPile > 0, _fieldCards.Count);
        if (_loggedPickGate.HasValue && _loggedPickGate.Value == key)
            return;
        _loggedPickGate = key;
        int want = hand != null ? hand.MaxSelectedCards : -1;
        // HW-VERIFY: grep token "PICK GATE". PROOF = a pick=CLOSED line after each burn commits,
        // and no later "Pick fan source (LoseCard): burnt pile". FALSIFIER = pick=OPEN standing for
        // minutes after the last "Pick commit", or the burnt-pile source line back with
        // refusedFromIllegalPile=0. See this method's doc for what each of those means. The
        // openEdgeOutstanding / gameHandIsPresented pair is items 9+10's addition: the FIRST is
        // now a gate term, the SECOND reports only.
        VRLog.Note("Cards", $"PICK GATE ({mode}): pick={(pickOpen ? "OPEN" : "CLOSED")} " +
                            $"(the game's own maxCardsSelected = {want}, " +
                            $"openEdgeOutstanding={flowLive}, flowArmedOn=" +
                            $"'{Patches.PickFlowWatch.OpenedOnName}', thisHandOwnsTheFlow=" +
                            $"{Patches.PickFlowWatch.LiveFor(hand)}, gameHandIsPresented={presented}), " +
                            $"refusedFromIllegalPile={refusedPile}, cardsLyingInThePickField=" +
                            $"{_fieldCards.Count}. CLOSED means the hand's mode is " +
                            "still a pick mode but nothing is being asked for - the game leaves " +
                            "CardsHandUI.currentMode latched and TakeDamagePanel's ResetAndHide clears " +
                            "NEITHER that mode NOR maxCardsSelected (it touches no CardsHandUI at all), " +
                            "so both read as a live pick for as long as nobody re-Shows the hand. " +
                            "openEdgeOutstanding=False with maxCardsSelected>0 is exactly that leftover " +
                            "and is the reading items 9+10 were fixed by. No candidate is seated on the board and " +
                            "no banner is raised while this reads CLOSED — and since item 6b (2026-09-06) the " +
                            "HAND FAN is shown instead of nothing, so a CLOSED reading may no longer be followed " +
                            "by 'Hand fan WITHHELD by NoCards'. ONE NEW FALSIFIER, for the ownership test item 6b " +
                            "added: openEdgeOutstanding=True together with thisHandOwnsTheFlow=False and " +
                            "maxCardsSelected>0 means the flow was armed on a DIFFERENT CardsHandUI than the one " +
                            "the board is presenting (flowArmedOn names it) — the pick is then refused on the " +
                            "safe side (the hand fan shows) but the player cannot place a card, and the fix is in " +
                            "PickFlowWatch's owner identification, not here.");
    }

    /// <summary>
    /// Item 9: name where the pick fan's candidates come from — the REAL HAND
    /// (avoid-damage lose-1, card-limit) vs the DISCARD pile (burn-two-discarded,
    /// recover-discard) vs the BURNT pile (recover-lost) — change-deduped to one line
    /// per (mode, source) change. Proves from the log alone that "burn two discarded"
    /// really turned the discard pile into the selectable hand fan.
    /// </summary>
    private void LogPickSource(CardHandMode mode, CardPileType source)
    {
        var key = (mode, source);
        if (_loggedPickSource.HasValue && _loggedPickSource.Value == key)
            return;
        _loggedPickSource = key;
        string name = source switch
        {
            CardPileType.Hand => "real hand",
            CardPileType.Discarded => "discard pile",
            CardPileType.Lost or CardPileType.Permalost => "burnt pile",
            CardPileType.None => "none (no selectable cards)",
            _ => source.ToString(),
        };
        VRLog.Info("Cards", $"Pick fan source ({mode}): {name} — the selectable cards ARE the hand fan " +
                            "(picked through the one TryCommitPick → CardsHandUI.SelectCard seam).");
    }

    /// <summary>
    /// One pick commit may be in flight at a time (test #21 B): poke and drop both
    /// funnel into <see cref="TryCommitPick"/>, and a second request is dropped
    /// until the queued SelectCard resolved — the once-per-action guard pattern of
    /// the test #19 half-selection accident fixes.
    /// </summary>
    private bool _pickCommitBusy;

    /// <summary>
    /// Task #11 (free swap): true while a pick REOPEN — the queued cancel of the
    /// game's burn/lose confirm popup plus the re-select of the cards that stay
    /// placed — is in flight. Guards the Rebuild field prune (everything reads
    /// deselected mid-cancel) and lets <see cref="TryCommitPick"/> queue a select for
    /// a card whose stale IsSelected has not been cleared yet.
    /// </summary>
    private bool _pickReopenBusy;

    /// <summary>Scratch for the abilities that must be re-selected after a reopen cancel.</summary>
    private readonly List<CAbilityCard> _reopenKeep = new(4);

    /// <summary>
    /// Task #11 (crash + free swap): the game's 2D pick flow shows the
    /// "Karten verbrennen" / "Wähle eine andere Karte" popup the moment the required
    /// number of cards is selected and LOCKS every card until an option resolves — so
    /// its commit callback may safely index <c>selectedCardsUI[0]/[1]</c>. VR free
    /// placement broke that invariant: plucking a card back off the tray while the
    /// popup was open ran UnselectCard underneath it, and the popup's commit then
    /// crashed (IndexOutOfRange → GlobalErrorMessage → main menu; Player.log 59952-60089).
    ///
    /// The honest VR equivalent of that pluck is the popup's own CANCEL option
    /// ("choose another card"): the instant a pick card is grabbed while the popup is
    /// open, queue the game's <c>DialogPopup.Cancel()</c> (which deselects ALL cards
    /// and restores selectability — the exact 2D path) and then re-select the cards
    /// that remain physically placed, excluding the grabbed one. Result: the popup can
    /// NEVER be open with an incomplete selection, swapping works indefinitely (place
    /// the last card → popup reopens through the game's own OnCardSelected), and the
    /// commit affordance only exists while the required cards are actually placed.
    /// All game calls are the game's own local-selection seams (network-synced by the
    /// game itself) — no game state is faked.
    /// </summary>
    private void MaybeReopenPickSelection(VRCard card)
    {
        CardsHandUI? hand = CurrentHand();
        if (hand == null || _pickReopenBusy || card.GameCard == null)
            return;
        if (!PickFlowLive(hand))
            return;
        if (!CardsGameApi.IsPickConfirmDialogOpen(hand))
            return;
        // Grab-time reopen applies to PLACED field cards only (take it back / re-seat
        // it): the choice must reopen the instant the placement is physically undone.
        // Grabbing a FAN card leaves the popup alone, and SINCE 2026-09-07 SO DOES RELEASING
        // ONE INTO A SLOT — that release is REFUSED outright (HandlePickRelease's
        // `target < 0` branch, report item 11), so a fan card can no longer displace a
        // committed one by any route and this is the ONLY reopen the player's hand can start.
        // The sentence here used to read "a swap only commits on the release INTO a slot
        // (BeginPickSwapReopen from HandlePickRelease)"; that method no longer exists.
        // Browse/active cards are read-only and never reach this.
        if (!_fieldCards.Contains(card))
            return;

        // Keep every OTHER placed card selected.
        _reopenKeep.Clear();
        for (int i = 0; i < _fieldCards.Count; i++)
        {
            VRCard placed = _fieldCards[i];
            if (placed == null || ReferenceEquals(placed, card) || placed.GameCard == null)
                continue;
            _reopenKeep.Add(placed.GameCard.AbilityCard);
        }
        EnqueuePickReopen(hand, "placed card grabbed back");
    }

    /// <summary>
    /// THE PICK FIELD, NAMED, for the refusal line — the cards this flow is holding COMMITTED
    /// right now, in seat order, with the locked prefix marked. Never null, never throws, and
    /// deliberately not a count: item 11's whole symptom was a count and a picture disagreeing,
    /// so the line that reports the refusal has to say WHICH card stayed, not how many did.
    /// Allocates only on the refusal edge (one drop), never per frame.
    /// </summary>
    private string DescribeFieldCards()
    {
        if (_fieldCards.Count == 0)
            return "(none)";
        var sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < _fieldCards.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            VRCard placed = _fieldCards[i];
            sb.Append(placed != null && placed.GameCard != null
                ? CardsGameApi.CardName(placed.GameCard)
                : "?");
            sb.Append('@').Append(PickSeatOfIndex(i));
            if (i < _pickLockedCount)
                sb.Append("[locked]");
        }
        return sb.ToString();
    }

    /// <summary>Queue the actual reopen: the game's own "choose another card"
    /// (DialogPopup.Cancel → deselect all + restore selectability) followed by the
    /// re-selects of <see cref="_reopenKeep"/>. Serialized one call per frame by
    /// <see cref="CardActionQueue"/> (the select paths spin-wait).</summary>
    private void EnqueuePickReopen(CardsHandUI hand, string why)
    {
        _pickReopenBusy = true;
        VRLog.Info("Cards", $"Pick reopen ({why}): burn/lose confirm popup open → pressing the game's " +
                            "own \"choose another card\" (DialogPopup.Cancel) and re-selecting " +
                            $"{_reopenKeep.Count} still-placed card(s). The popup can never stay open " +
                            "with an incomplete selection.");
        CardsHandUI handRef = hand;
        // THE CANCEL'S OUTCOME IS REPORTED, not assumed (the ModBuild 247 defect: DialogPopup.Cancel
        // silently refuses a hidden option button, and this seam looked identical whether it worked
        // or not — see CardsGameApi.CancelPickConfirmDialog). A refused cancel means the popup is
        // still open with the OLD selection, so the re-selects below are no-ops and the swap the
        // player asked for did not happen: that has to be visible in the log, once, on the spot.
        CardActionQueue.Enqueue(() =>
        {
            if (!CardsGameApi.CancelPickConfirmDialog())
                VRLog.Warn("Cards", $"Pick reopen ({why}): the game's \"choose another card\" did NOT fire — " +
                                    "no cancel option was pressable on the open DialogPopup. The selection " +
                                    "is unchanged and the re-selects that follow are no-ops; the popup is " +
                                    "still showing the old set.");
        });
        for (int i = 0; i < _reopenKeep.Count; i++)
        {
            CAbilityCard keep = _reopenKeep[i];
            CardActionQueue.Enqueue(() => CardsGameApi.SelectCard(handRef, keep));
        }
        CardActionQueue.Enqueue(() => { }, () =>
        {
            _pickReopenBusy = false;
            _dirty = true;
        });
    }

    /// <summary>
    /// Release routing for the modal pick modes (test #28: the candidate homes into
    /// the LEFT slot recess, not a centre field). Accept rules mirror the play slots
    /// (test #15): the glow that telegraphed the drop wins, the generous capture
    /// radius is the fallback. Accepting a fan card lays it into the wanted slot and
    /// commits the selection; releasing a SLOTTED pick card anywhere else takes the
    /// pick back through the game's own UnselectCard seam.
    /// </summary>
    private void HandlePickRelease(VRCard card, VRHand hand, CardsHandUI gameHand)
    {
        bool wasOnField = _fieldCards.Contains(card);
        // NOTE (2026-08-11): here SlotNear is used purely as an ELIGIBILITY test — the landing
        // recess is `target` below, which the pick FLOW decides (PickTargetSlot / PickSeatOfIndex),
        // not the geometry. The card-vs-hand ranking change therefore cannot move a pick card; the
        // dual-sample reach this path depends on is untouched.
        int nearSlot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out PlayTray.SlotProbe probe);
        bool highlight = ReferenceEquals(_fieldHighlightCard, card) && _fieldHighlightSlot >= 0;
        bool accept = highlight || nearSlot >= 0;
        string rule = highlight ? "highlight" : nearSlot >= 0 ? "radius" : "none";
        // Landing slot: a fresh candidate goes to the wanted (next empty) slot; a
        // re-dropped pick card keeps its own seat (recess for the live batch, the
        // beside-Slot2 stack for a locked one — see PickSeatOfIndex).
        int target = wasOnField ? PickSeatOfIndex(_fieldCards.IndexOf(card)) : PickTargetSlot();

        // A FRESH CANDIDATE THE FLOW HAS NO SEAT FOR IS REFUSED, AND SINCE 2026-09-07 THAT
        // INCLUDES THE CASE WHERE THE GAME'S CONFIRM POPUP IS ALREADY UP (report item 11).
        //
        // Two states share one rule because they are one state: `target < 0` means the pick
        // flow is not asking for another card right now — either the current batch of two is
        // full and the tray CONFIRM ("WEITER") has to lock it first, or the FULL requirement
        // is already lying on the board and the game has opened its own
        // "verbrennen / Wähle eine andere Karte" popup over it.
        //
        // ─── THE `!IsPickConfirmDialogOpen` TERM WAS THE DEFECT, AND IT IS MEASURED ──────────
        // Verbatim (2026-09-07): "Wenn ich bei der langen Rast eine Karte hinlege und dann mit
        // einer weiteren Karte dort hingehe ohne den Button 'Wähle eine weitere Karte' gedrückt
        // zu haben - ist das board leer aber die Karte ist noch eingeloggt."
        //
        // With the term present, a long rest (PickCardsWanted() == 1, one card laid, popup
        // open) fell through to BeginPickSwapReopen, which COMMITTED THE VISUAL HALF OF A SWAP
        // BEFORE THE MODEL HALF COULD LAND: it removed the laid card from _fieldCards and
        // _fan.Add()-ed it home on the spot, then queued the game's cancel and, behind it, the
        // incoming card's select. CardActionQueue runs ONE entry per frame, and the reopen's
        // last entry clears _pickReopenBusy and sets _dirty — so the rebuild it triggers lands
        // a WHOLE FRAME BEFORE the incoming card's SelectCard, with the incoming card in
        // _fieldCards and its widget still unselected. Rebuild's field prune
        // (`!_pickReopenBusy && !occupant.GameCard.IsSelected`, CardsDriver.4.Rebuild.cs:1063-1066)
        // is then exactly true and fans the incoming card home too. The select lands one frame
        // later and is ACCEPTED. Board empty, model holding a card: his sentence, verbatim.
        //
        // ALL SEVEN of the host log's swap-ins read that way, with no exception (ModBuild 472,
        // .planning/debug/Player.log 232073 / 232343 / 232561 / 233863 / 234075 / 234289 /
        // 234619). Each one is the same four readings in the same order:
        //   cardsLyingInThePickField=1  →  cardsLyingInThePickField=0
        //   →  "Pick commit (LoseCard): '<card>' via drop-slot → CardsHandUI.SelectCard accepted."
        //   →  Pick banner: "Testo: Alle Karten liegen — mit der Board-Taste abschließen …"
        // A banner saying every card is laid, over two empty recesses. And it is NOT the
        // "cancel did not fire" failure the swap path documents: "Pick confirm CANCEL:
        // optionButtons[cancelOption] is ACTIVE" precedes every one of them and the Warn is
        // absent from the whole session, so the swap worked exactly as written and the written
        // thing is what produced the picture.
        //
        // 1:1 IS THE REFUSAL, NOT THE SWAP. While that popup stands the GAME has every widget
        // unselectable (CardsHandUI.OnCardSelected's LoseCard branch, CardsHandUI.cs:2051, plus
        // LockCard on each shown card, :2064-2065) — the flat player cannot lay a second card
        // either. The free swap was the mod's own invention, so removing it removes a
        // divergence rather than adding one. And it is what the user asked for in the same
        // breath: "Der Kartentausch muss in dem Fall abgelehnt werden und die Karte geloggt bis
        // dieser Button die freigibt."
        //
        // NOBODY IS STRANDED, WHICH IS WHY THIS IS A REFUSAL AND NOT A DEAD END. Two releases
        // already exist and both are the game's own "choose another card" seam: the tray UNDO
        // keycap, which carries the popup's own GUI_CHOOSE_OTHER_CARD label
        // (CardsDriver.6.Flows.cs:631 → OnUndoRequested → CancelPickConfirmDialog), and
        // GRABBING THE LAID CARD BACK (MaybeReopenPickSelection), which the 2026-08-04 ruling
        // made the dock's substitute for that button. The banner the board is already showing
        // names the second one in words.
        //
        // Checked BEFORE the drop log so the one line per drop tells the true outcome.
        if (accept && !wasOnField && target < 0 && !_pickReopenBusy)
        {
            bool confirmStanding = CardsGameApi.IsPickConfirmDialogOpen(gameHand);
            // HW-VERIFY: report item 11 (2026-09-07). Grep token: PICK SWAP REFUSED.
            //
            // WORKING = one line per second card brought to an occupied pick flow, naming a
            // NON-EMPTY `committed=` list, and NO "Pick commit … accepted" line following it for
            // the refused card. Zero lines in a session where he never brings a second card is
            // correct, not inert.
            // INERT = a `Pick reopen (fan card swap-in)` line anywhere in the log: the swap path
            // is reachable again and the field/board pair below will go 1 → 0 as it did seven
            // times on 472. That string is the falsifier; it should now be UNREACHABLE, and
            // grepping for its ABSENCE is the cheaper test than grepping for this line's presence.
            // STILL BEYOND THE INSTRUMENT = `committed=` printing the card that stayed while the
            // RECESS is empty. This line reads _fieldCards, i.e. the mod's bookkeeping; a card
            // held there whose visual is not in a recess is a SEATING defect and the lead is
            // RelayoutField / PlacePickCard, not this gate.
            VRLog.Note("Cards", $"PICK SWAP REFUSED ({hand.Side}): {probe.Describe()}, rule={rule} → " +
                                (confirmStanding
                                    ? "the full pick is already laid and the game's own confirm popup is up"
                                    : $"the current batch of {Mathf.Min(2, CardsGameApi.PickCardsWanted() - _pickLockedCount)} is full") +
                                $", so this card goes home. committed={DescribeFieldCards()} " +
                                $"(field={_fieldCards.Count}, locked={_pickLockedCount}, " +
                                $"wanted={CardsGameApi.PickCardsWanted()}, popup={confirmStanding}). " +
                                "The laid card STAYS committed and stays in its recess; release it with the " +
                                "board's \"choose another card\" keycap (UNDO) or by grabbing it back — " +
                                "then a new pick is possible. A refused drop takes the same animated glide " +
                                "home every other refused drop takes; no card state was written.");
            _tray.NoteSlotActivity(); // the gesture still happened right next to CONFIRM
            _fan.Add(card);
            return;
        }

        // THE one log line per real pick drop (the test #14 contract).
        string where = target >= 0 && target < 2 ? "slot " + (target + 1) : "the locked stack beside slot 2";
        VRLog.Info("Cards", $"Drop ({hand.Side}): {probe.Describe()}, rule={rule} → " +
                            (accept
                                ? (wasOnField ? "stay in " + where + "." : "select into " + where + ".")
                                : (wasOnField ? "take back (unselect)." : "return to fan.")));

        // Accident window (test #19): the slots sit directly above the cluster's
        // Ready and beside the tray CONFIRM — every drop/take-back touching them
        // arms the confirm suppression, exactly like the play slots.
        if (accept || wasOnField)
            _tray.NoteSlotActivity();

        if (accept)
        {
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            // THE FREE SWAP THAT USED TO SIT HERE IS GONE (report item 11, 2026-09-07). A fan
            // candidate dropped while the game's confirm popup shows the completed selection can
            // no longer reach this branch at all — the refusal above returns first — so the
            // "displace the most recent placement, then reopen through the game's own cancel"
            // path has no caller left, and BeginPickSwapReopen is deleted rather than left dead.
            // The reason is in the refusal's own block: it committed the visual half of the swap
            // one frame before the model half, and Rebuild's field prune ate the incoming card in
            // the gap. The reopen seam that REMAINS is the grab-back one
            // (MaybeReopenPickSelection), which is driven by the player's own hand and displaces
            // nothing on its own authority.
            // Task #5 (double sound): a commit queues CardsHandUI.SelectCard, whose
            // AbilityCardUI.ToggleSelect plays the card's own serialized profile click
            // (mouseDownAudioItem, AbilityCardUI.cs:1184) one frame later — our thunk on
            // top made TWO sounds per placement. Play ours ONLY for the game-silent
            // re-drop of an already-placed card; the commit lets the game's click be
            // the one placement sound.
            // Re-seating a field card that a reopen deselected (grab-back → change of
            // mind → drop back into the slot) must ALSO commit, even after the reopen
            // queue already drained — its widget reads unselected then.
            bool willCommit = !wasOnField || _pickReopenBusy
                              || (card.GameCard != null && !card.GameCard.IsSelected);
            if (!willCommit)
                PlayCardSound(CardsConfig.CardPlaceSound.Value, card.transform);
            PlaceOnField(card);
            // Commit through the one seam. Also on a re-drop DURING a pick reopen
            // (task #11): the reopen's cancel deselected this card, so re-seating it
            // must re-select it — the queued select lands after the cancel resolves.
            if (willCommit)
                TryCommitPick(card, gameHand, wasOnField ? "re-drop" : "drop-slot");
        }
        else if (wasOnField)
        {
            // Task #5: the queued UnselectCard below plays the game's own profile click
            // (ToggleSelect deselect path) — no mod sound on top. During a pick reopen
            // the card is already deselected (the game plays nothing), so the soft undo
            // click keeps audible feedback for that case.
            if (_pickReopenBusy)
                PlayCardSound(CardsConfig.CardTakeBackSound.Value, card.transform);
            // EVENT-DISCARD BATCHING: taking back a LOCKED card unlocks it (the batch
            // shrinks; the step display recomputes from the new locked count).
            int takeBackIndex = _fieldCards.IndexOf(card);
            if (takeBackIndex >= 0 && takeBackIndex < _pickLockedCount)
                _pickLockedCount--;
            _fieldCards.Remove(card);
            _pickExitFlown.Remove(card); // a taken-back card may be laid (and flown) again
            RelayoutField();
            _fan.Add(card);
            AbilityCardUI widget = card.GameCard!;
            CAbilityCard ability = widget.AbilityCard;
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.UnselectCard(handRef, ability),
                () =>
                {
                    VRLog.Info("Cards", $"Pick take-back ({CardsGameApi.Mode(handRef)}): '{CardsGameApi.CardName(widget)}' " +
                                        "via drop-slot → CardsHandUI.UnselectCard.");
                    _dirty = true;
                });
        }
        else
        {
            _fan.Add(card); // released in the void: animated return
        }
    }

    /// <summary>
    /// THE pick commit — both input paths (field drop, poke fallback) land here and
    /// nowhere else. Skips are logged: an already-selected widget (the game's
    /// SelectCard would no-op anyway) and a commit still in flight (no
    /// double-commit). The outcome is verified from the widget state after the
    /// queued call resolved.
    /// </summary>
    private void TryCommitPick(VRCard card, CardsHandUI gameHand, string seam)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null)
            return;
        // During a pick reopen (task #11) the widget's IsSelected is stale — the queued
        // cancel is about to deselect everything — so the commit must be queued anyway
        // (CardsHandUI.SelectCard itself no-ops on a genuinely selected card).
        if (widget.IsSelected && !_pickReopenBusy)
        {
            VRLog.Info("Cards", $"Pick commit skipped (seam={seam}) — '{CardsGameApi.CardName(widget)}' " +
                                "is already selected (no double-commit).");
            return;
        }
        if (_pickCommitBusy)
        {
            VRLog.Info("Cards", $"Pick commit ignored (seam={seam}) — a commit is already in flight " +
                                "(no double-commit, test #19 guard pattern).");
            return;
        }
        _pickCommitBusy = true;
        CAbilityCard ability = widget.AbilityCard;
        CardsHandUI handRef = gameHand;
        CardActionQueue.Enqueue(
            () => CardsGameApi.SelectCard(handRef, ability),
            () =>
            {
                _pickCommitBusy = false;
                bool selected = widget != null && widget.IsSelected;
                VRLog.Info("Cards", $"Pick commit ({CardsGameApi.Mode(handRef)}): '{(widget != null ? CardsGameApi.CardName(widget) : "?")}' " +
                                    $"via {seam} → CardsHandUI.SelectCard {(selected ? "accepted" : "rejected")}.");
                if (!selected && _fieldCards.Remove(card))
                {
                    _pickExitFlown.Remove(card);
                    RelayoutField();
                    _fan.Add(card);
                }
                _dirty = true;
            });
    }

    private void PlaceOnField(VRCard card)
    {
        if (!_fieldCards.Contains(card))
            _fieldCards.Add(card);
        RelayoutField();
    }

    /// <summary>Dedup key for the &gt;2-pick overflow log (item 28); -1 = not logged.</summary>
    private int _loggedFieldOverflow = -1;

    /// <summary>
    /// EVENT-DISCARD BATCHING: the physical seat a field-list index maps to. Cards of
    /// the LIVE batch (index ≥ locked count) take the recesses 0/1; LOCKED cards
    /// (index &lt; locked count) stack on the beside-Slot2 overflow seats (2, 3, …) —
    /// visibly "already chosen", out of the way of the active batch. -1 = not on field.
    /// </summary>
    private int PickSeatOfIndex(int index)
    {
        if (index < 0)
            return -1;
        int locked = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
        return index < locked ? 2 + index : index - locked;
    }

    /// <summary>
    /// Home the pick candidates into the SLOT RECESSES (test #28): the live batch into
    /// the LEFT slot (Slot1) then the RIGHT slot (Slot2), every LOCKED batch card onto
    /// the beside-Slot2 stack (see <see cref="PickSeatOfIndex"/> — the same overflow
    /// seats the pre-batching flow used for a rare 3rd card; logged once, never
    /// silently capped). Held cards are never re-homed (the phantom-ACCEPT lesson,
    /// see PlayTray.PlaceCard).
    /// </summary>
    private void RelayoutField()
    {
        _pickLockedCount = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
        int n = _fieldCards.Count;
        if (n <= 2)
            _loggedFieldOverflow = -1; // back within the two slots — re-arm the overflow log
        for (int i = 0; i < n; i++)
        {
            VRCard card = _fieldCards[i];
            if (card == null || card.IsHeld)
                continue;
            // EVENT-DISCARD EXIT: a card whose exit flight has been launched owns its own
            // transform until it lands (and is PARKED afterwards). Re-homing it onto an overflow
            // seat here is exactly the reported defect — kartenabwurf2.jpg shows the two locked
            // cards of step 1/2 sitting in the board's woodwork right of Slot2, which is where
            // PlayTray.PlacePickCard's index>=2 fallback puts them (PlayTray.4.Slots.cs:1064-1069:
            // SlotHomeOffsetFor(1) + (index-1)*CardWidth*1.15 along the slot's local +X).
            if (_pickExitFlown.Contains(card))
                continue;
            int seat = PickSeatOfIndex(i);
            if (_tray.PlacePickCard(card, seat) < 0 && seat >= 2
                && i >= _pickLockedCount && seat != _loggedFieldOverflow)
            {
                // Only an UNLOCKED card on an overflow seat is unexpected (the locked
                // stack lives there by design) — keep the diagnostic for that case.
                _loggedFieldOverflow = seat;
                VRLog.Info("Cards", $"Pick: {n} cards laid — extra card #{seat + 1} placed BESIDE Slot2 " +
                                    "(both recesses full; graceful fallback, no cap).");
            }
        }
    }

    /// <summary>
    /// The recess the next FRESH pick candidate should land in (test #28): Slot1 while
    /// the live batch is empty, Slot2 once one is laid. -1 once the CURRENT BATCH is
    /// full — batch size is min(2, total wanted − locked), so a ONE-card burn stops
    /// telegraphing after the left slot fills, a two-card burn wants both, and an
    /// event-discard of N &gt; 2 wants exactly the live batch (the tray CONFIRM locks
    /// it and the count restarts for the next batch).
    /// </summary>
    private int PickTargetSlot()
    {
        int locked = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
        int batchWant = Mathf.Min(2, CardsGameApi.PickCardsWanted() - locked);
        int placed = _fieldCards.Count - locked;
        return placed < batchWant ? placed : -1;
    }

    /// <summary>
    /// EVENT-DISCARD BATCHING: lock the current full batch of picks so the recesses
    /// free up for the next batch (tray CONFIRM = "WEITER" while more cards remain).
    /// Pure VR bookkeeping — every locked card STAYS selected in the game (the game
    /// only counts selections toward <c>maxCardsSelected</c>; its own confirm popup
    /// opens when the TOTAL is reached, and commits everything at once). Fires only
    /// when MORE than a full batch is still outstanding — the final batch commits
    /// through the game's own DialogPopup instead. Returns true when a batch locked.
    /// </summary>
    private bool TryLockPickBatch(CardsHandUI hand)
    {
        if (CardsGameApi.IsPickConfirmDialogOpen(hand) || _pickReopenBusy)
            return false;
        int total = CardsGameApi.PickCardsWanted();
        int locked = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
        if (total - locked <= 2)
            return false; // final batch — the game's own confirm dialog owns the commit
        int placed = _fieldCards.Count - locked;
        if (placed < 2)
            return false; // batch not full yet — nothing to lock
        int firstNewlyLocked = locked;
        _pickLockedCount = _fieldCards.Count;
        // EVENT-DISCARD EXIT (user 2026-08-24): the page's cards LEAVE the board here instead of
        // stacking beside Slot2. Launched BEFORE RelayoutField so the flight is already claimed
        // when the relayout runs and the relayout skips them (see _pickExitFlown).
        FlyLockedPicksToPile(hand, firstNewlyLocked, _pickLockedCount);
        RelayoutField();
        _tray.NoteSlotActivity(); // the locked cards just moved next to CONFIRM — arm the accident guard
        int totalSteps = (total + 1) / 2;
        int step = Mathf.Clamp(_pickLockedCount / 2 + ((_pickLockedCount % 2) != 0 ? 1 : 0) + 1, 1, totalSteps);
        VRLog.Info("Cards", $"Pick batch LOCKED: {_pickLockedCount}/{total} card(s) chosen — recesses cleared for " +
                            $"step {step}/{totalSteps}. Locked cards stay game-selected (VR-only batching; the game's " +
                            "confirm dialog opens when the full count is selected).");
        _dirty = true;
        return true;
    }

    /// <summary>
    /// EVENT-DISCARD EXIT — the page's chosen cards fly into the discard stack the moment the page
    /// closes, instead of being stacked beside Slot2 where they clip into the board's woodwork.
    ///
    /// <para>USER REPORT 2026-08-24 (verbatim): "Wie man in kartenabwurf2.jpg sehen kann, gehen die
    /// zwei ersten Karten der ersten Seite komisch zur Seite und clippen dann im board — stattdessen
    /// will ich das ganz normal die 'verbrennen' Animation abgespielt wird und die Karten in den
    /// jeweiligen Pile gehen wie es bei allen anderen Verbrennungen auch der Fall ist. Das soll auf
    /// der ersten Seite mit den ersten beiden Karten und dann auf der nächsten Seite mit der 3
    /// Karte passieren." The photograph is the proof of the mechanism, not a symptom of it: the two
    /// cards in it sit exactly where <c>PlayTray.PlacePickCard</c>'s <c>index &gt;= 2</c> fallback
    /// puts them (PlayTray.4.Slots.cs:1064-1069 — Slot2's home offset plus
    /// <c>(index-1) * CardWidth * 1.15</c> along the slot's local +X, i.e. one and two card widths
    /// PAST the right recess, which is board frame and then thin air).</para>
    ///
    /// <para>THE FLIGHT IS THE EXISTING ONE, NOT A SECOND IMPLEMENTATION. Same
    /// <see cref="VRCard.FlyToPile"/>, same <see cref="FlyToPileSeconds"/>, same
    /// <see cref="BoardUp"/> arc and same <see cref="BoardArcMin"/> floor as the turn-clear flight
    /// (<c>TryStartFlyToPile</c>), the burn flight (<c>TryStartBurnFly</c>) and the short-rest
    /// redraw (<see cref="FlyShortRestCardToDiscard"/>), and the same
    /// <c>_flyingToPile</c> + <c>_factory.Park</c> ownership contract, so the park sweep and
    /// <c>PileArrivalsPending</c> already understand these cards without a single new case.</para>
    ///
    /// <para>IT RUNS AHEAD OF THE MODEL, DELIBERATELY, AND THAT IS THE ONE COST. A locked batch is
    /// pure VR bookkeeping — the game has NOT moved these cards into
    /// <c>CCharacterClass.DiscardedAbilityCards</c> yet (it commits the whole event at once through
    /// its own confirm dialog, see <see cref="TryLockPickBatch"/>), so <c>RoundCardExitOf</c> would
    /// still answer <c>Hand</c> here and cannot be the trigger. The trigger is therefore the mod's
    /// own page turn, and the consequence is stated out loud: for the seconds between the page turn
    /// and the final confirm the discard STACK LABEL still reads the model's number (0), while the
    /// cards are already visually in it. The label converges on its own the moment the game commits
    /// — <c>PileArrivalsPending</c> keeps no ledger (CardsDriver.4.Rebuild.cs:2000-2019).</para>
    ///
    /// <para>ONLY <c>DiscardCard</c>. A ≥3-card forced BURN would reach the same batching, but a
    /// burn's user-ruled order is "artwork on the lying card first, THEN the pile flight"
    /// (<c>TryTakeBurnFlightSlot</c>) and at page-turn time the game has not started that artwork,
    /// so flying here would clip an animation the user explicitly asked to watch. Those cards keep
    /// today's overflow seats and the refusal is LOGGED, so the case shows up in a hardware log
    /// instead of silently taking a new path.</para>
    ///
    /// <para>MULTIPLAYER: announced through the same <c>ReportCardFx</c> semantic anchor pair every
    /// other flight uses (2 bytes, board-relative, no card identity). Nothing new goes on the wire
    /// and no game state is written — the game's own selection, made by
    /// <see cref="TryCommitPick"/> through <c>CardsHandUI.SelectCard</c>, is untouched.</para>
    /// </summary>
    private void FlyLockedPicksToPile(CardsHandUI hand, int firstIndex, int endIndex)
    {
        CardHandMode mode = CardsGameApi.Mode(hand);
        if (mode != CardHandMode.DiscardCard)
        {
            VRLog.Info("Cards", $"Pick batch EXIT REFUSED (mode={mode}): the {endIndex - firstIndex} card(s) of " +
                                "this page keep the beside-Slot2 overflow seats. The page-turn exit flight is " +
                                "armed for DiscardCard ONLY — a burn's artwork plays on the LYING card and the " +
                                "game has not started it at page-turn time, so flying here would clip it.");
            return;
        }
        if (!_piles.TryGetPileWorld(PileKind.Discard, out Vector3 pilePos, out float slabWidth))
        {
            VRLog.Info("Cards", $"Pick batch EXIT REFUSED (mode={mode}): the discard stack is not built / not " +
                                "visible, so there is no destination to fly to. The card(s) keep the " +
                                "beside-Slot2 overflow seats — never a wrong-spot teleport.");
            return;
        }

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        int flown = 0, skipped = 0;
        for (int i = firstIndex; i < endIndex && i < _fieldCards.Count; i++)
        {
            VRCard card = _fieldCards[i];
            // Held / already flying / already flown / already parked: every one of these means
            // something else owns the card's transform right now. Leave it alone rather than
            // fighting the other owner (the "don't win a write war" rule).
            if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing
                || _pickExitFlown.Contains(card) || !card.gameObject.activeInHierarchy)
            {
                skipped++;
                continue;
            }
            _pickExitFlown.Add(card);
            _flyingToPile.Add(card);
            VRCard flying = card;
            Vector3 from = card.transform.position;
            float arcHeight = Mathf.Max(minArc, Vector3.Distance(from, pilePos) * VRCard.FlyArcHeightFraction);
            // MP parity: the RECESS the card is physically leaving → the discard stack, the same
            // anchor pair the turn-clear flight reports. The recess is the seat the card held in
            // the LIVE batch, i.e. its offset within the page (i - firstIndex ∈ {0,1}) — NOT
            // PickSeatOfIndex(i), which by the time this runs already answers with the
            // beside-Slot2 overflow seat the card is being spared from ever taking.
            int recess = i - firstIndex;
            ReportCardFx(SlotAnchor(recess), PileAnchor(PileKind.Discard));
            CardFlightLedger.Note("own", "Discard", "own-pick-page-turn",
                card.GameCard != null ? CardsGameApi.CardName(card.GameCard) : card.name);
            card.FlyToPile(pilePos, slabWidth, FlyToPileSeconds, arcUp, () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
            }, minArc);
            flown++;
            VRLog.Info("Cards", $"Pick batch EXIT: CARD FLIGHT '{(card.GameCard != null ? CardsGameApi.CardName(card.GameCard) : card.name)}' " +
                                $"— WHY: the VR page it was chosen on just closed (field index {i}, recess " +
                                $"{recess + 1} of the page), so it flies from {from} into the Discard stack " +
                                $"({FlyToPileSeconds:F2}s, arc {arcHeight:F3} m over the board, orientation " +
                                $"locked) instead of being re-homed onto overflow seat {PickSeatOfIndex(i)} " +
                                "beside Slot2 — which is where kartenabwurf2.jpg shows it clipping into the " +
                                "board. The card STAYS game-selected; " +
                                "the game commits the whole event through its own confirm dialog. VR " +
                                "presentation only — no game state is written here.");
        }
        if (skipped > 0)
            VRLog.Info("Cards", $"Pick batch EXIT: {flown} flew, {skipped} skipped (held, already animating, " +
                                "already flown, or not live) — a skipped card keeps its overflow seat and is " +
                                "re-offered by the next page turn / the final commit's park sweep.");
    }

    /// <summary>
    /// PICK RESTART — back to page 1, from the beginning, and hand the flown pages to the return
    /// flight. The counterpart of <see cref="TryLockPickBatch"/> / <see cref="FlyLockedPicksToPile"/>
    /// and the VR half of the game's own "Wähle eine andere Karte" cancel.
    ///
    /// <para>USER REPORT 2026-08-24: "Wird der gedrückt soll die Auswahl auf der ersten Seite
    /// nochmal komplett von anfang an beginnen." That is not a preference the mod may interpret
    /// loosely — it is what the GAME does: the cancel callback runs <c>DeselectAllCards()</c>
    /// (CardsHandUI.cs:2099-2115), which drops EVERY selection including the ones an earlier VR page
    /// locked. Leaving <see cref="_pickLockedCount"/> standing would have the banner claim "Schritt
    /// 2/2" over a selection the game has already emptied.</para>
    ///
    /// <para>IT DOES NOT CLEAR <see cref="_fieldCards"/> ITSELF, deliberately. That list is what the
    /// park sweep uses to recognise a card that just left a pick recess
    /// (<c>_lastFieldCards</c> → <c>TryStartFlyToPile</c>'s pick-field pre-filter), and the next
    /// Rebuild prunes it from the game's own <c>IsSelected</c> — the one authority — and calls
    /// <c>RelayoutField</c> itself. Clearing it here would be the mod asserting a card left the
    /// field before the model says so.</para>
    ///
    /// <para>Called ONLY from the completion callback of the queued cancel, i.e. after the cancel
    /// has actually landed. No game state is written; every value touched is mod-local
    /// presentation bookkeeping.</para>
    /// </summary>
    /// <returns>How many already-flown cards were queued for the return flight.</returns>
    private int ArmPickRestart()
    {
        _pickReturnFlight.Clear();
        foreach (VRCard flown in _pickExitFlown)
        {
            if (flown != null)
                _pickReturnFlight.Add(flown);
        }
        int queued = _pickReturnFlight.Count;
        // The batch bookkeeping is dropped WHOLESALE — that is what "from the beginning" means.
        // Dropping the flown claim with it is safe because no RelayoutField can run between here
        // and the Rebuild this same frame (CardActionQueue.Pump runs before it, and the prune in
        // the pick branch empties _fieldCards before the field is laid out again).
        _pickExitFlown.Clear();
        _pickLockedCount = 0;
        _loggedFieldOverflow = -1; // the overflow diagnostic re-arms with the fresh page
        return queued;
    }

    // -------------------------------------------------------------- short rest --

    /// <summary>
    /// Independent secrecy signal for record 46. Read the presented hand's native flow,
    /// including the confirmation before a sacrifice widget exists; never infer this from
    /// record 39's resolved recess. An unresolved seat must not uncover the discard fan.
    /// </summary>
    internal static bool ShortRestInProgress
    {
        get
        {
            CardsHandUI? hand = Instance != null ? Board.CharacterFocus.PresentedHand(Instance.CurrentHand()) : null;
            return hand != null && hand.PlayerActor != null
                   && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest
                   && (CardsGameApi.IsShortRestSelected(hand)
                       || CardsGameApi.IsShortRestChoosing(hand) || hand.IsImprovedLongResting);
        }
    }

    /// <summary>
    /// THE SEAM THE SACRIFICE-SEAT RECORD (39) IS SAMPLED FROM: which round recess the short-rest
    /// sacrifice is PHYSICALLY lying in right now, and the hand it belongs to, or a -1 recess when
    /// no short rest is mid-choice. Static for the same reason
    /// <c>Piles.PileViewer.CurrentCounts</c> is: the driver instance is a private of this class and
    /// the Net layer must not thread through it.
    ///
    /// <para>IT REPORTS WHAT IS ON THE BOARD, NOT WHAT THE GAME INTENDS. The recess comes from
    /// <c>PlayTray.SlotIndexOfCard</c> — the physical parent of the card this driver actually laid
    /// down — so the record can never claim a face for a recess the owner is not looking at. The
    /// window it is non-empty for is exactly the window <see cref="PresentShortRestCard"/> holds
    /// <see cref="_shortRestCard"/> for, which the game opens synchronously before the burn/redraw
    /// dialog and closes synchronously inside <c>FinalizeShortRest</c>.</para>
    ///
    /// <para>THE HAND RIDES ALONG because the receiver resolves the seat in a DISCARD LIST and has
    /// to resolve it in the RIGHT character's. The sampler compares it with the character the
    /// board presents and stays silent when they differ, rather than indexing a list it guessed at
    /// — the same stance <c>Net.LocalRigSampler.NameHeldCard</c> takes for an absent actor.</para>
    /// </summary>
    internal static (CardsHandUI? hand, int recess) SacrificeSeat =>
        Instance != null ? Instance.SacrificeSeatNow() : (null, -1);

    private (CardsHandUI? hand, int recess) SacrificeSeatNow()
    {
        if (_shortRestCard == null || _shortRestPresented == null)
            return (null, -1);
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return (null, -1);
        return (hand, _tray.SlotIndexOfCard(_shortRestCard));
    }

    /// <summary>
    /// THE SECOND SEAM RECORD 39 IS SAMPLED FROM (2026-09-06 report item 7, the half that is not the
    /// fan): the card the owner has LAID INTO round recess <paramref name="recess"/> as one step of
    /// a MODAL CARD PICK, and the hand it belongs to — a long rest's burn card, a
    /// <c>RecoverDiscardedCard</c> / <c>RecoverLostCard</c> pick. Null widget when that recess holds
    /// no pick card.
    ///
    /// <para>WHY IT IS NOT <see cref="SacrificeSeat"/> WITH A WIDER BODY. The sacrifice is presented
    /// BY this driver and tracked in one field; a pick card is put there by the PLAYER and lives in
    /// <see cref="_fieldCards"/>, whose index-to-recess mapping is
    /// <see cref="PickSeatOfIndex"/> (the live batch takes recesses 0/1, a LOCKED card goes to the
    /// beside-slot-2 overflow stack and is therefore NOT in a recess at all). Two different facts,
    /// two accessors, one record.</para>
    ///
    /// <para>IT REPORTS WHAT IS ON THE BOARD AND NOTHING ELSE, exactly as
    /// <see cref="SacrificeSeat"/> does. <see cref="_fieldCards"/> is filled ONLY from
    /// <see cref="HandlePickRelease"/>, which the release router reaches only under
    /// <see cref="IsPickMode"/> — <c>LoseCard</c> / <c>DiscardCard</c> / <c>RecoverDiscardedCard</c>
    /// / <c>RecoverLostCard</c> / <c>IncreaseCardLimit</c>. The ordinary two-card commit seats its
    /// cards through <c>PlayTray.PlaceCard</c> and never appears here, which is the first of the
    /// three places <c>NetProtocol.ExtIdSacrificeSeat</c>'s boundary paragraph is enforced in. A
    /// HELD card is excluded too: it is in a fist, not in the recess.</para>
    /// </summary>
    internal static (CardsHandUI? hand, AbilityCardUI? widget) PickFieldSeat(int recess) =>
        Instance != null ? Instance.PickFieldSeatNow(recess) : (null, null);

    private (CardsHandUI? hand, AbilityCardUI? widget) PickFieldSeatNow(int recess)
    {
        if (recess < 0 || _fieldCards.Count == 0)
            return (null, null);
        // ITEM 6b, AND THE ONE PLACE THAT DELIBERATELY KEEPS THE MODE TERM. This tuple becomes the
        // MIRRORED pick-field seat record, and its own contract two paragraphs up is "it reports
        // what is ON THE BOARD and nothing else". The board still physically holds the committed
        // card while its burn plays (see CardsDriver.Rebuild's pickModeSeats), so switching this to
        // the liveness term would empty every observer's recess ~1 s before the owner's — a 1:1
        // breach traded for nothing. The three tests below already require a real seat: a live
        // occupant, its physical slot index, and a non-held card.
        CardsHandUI? hand = CurrentHand();
        if (hand == null || !CardsGameApi.IsPickMode(CardsGameApi.Mode(hand)))
            return (null, null);
        for (int i = 0; i < _fieldCards.Count; i++)
        {
            VRCard card = _fieldCards[i];
            if (card == null || card.IsHeld || card.GameCard == null)
                continue;
            if (PickSeatOfIndex(i) != recess)
                continue;
            // …and it must PHYSICALLY be in that recess right now. PickSeatOfIndex says where the
            // layout WANTS it; a card mid-flight out of the field (_pickExitFlown) or one the tray
            // could not seat is not lying there, and a record that named it would put a face in a
            // recess the owner is looking at an empty one.
            return _tray.SlotIndexOfCard(card) == recess ? (hand, card.GameCard) : (null, null);
        }
        return (null, null);
    }

    /// <summary>
    /// Present the short-rested card in the LEFT slot recess (test #25, item 1d;
    /// test #28 seating). Adopts the sacrifice widget through the SAME
    /// <see cref="AdoptedCard"/> path as every other physical card and homes it into
    /// Slot1 exactly like a single-card pick candidate (<see cref="PlayTray.PlacePickCard"/>)
    /// — so it reads exactly like the avoid-damage sacrifice. DISPLAY-ONLY: forced non-grabbable /
    /// non-poke (the zone loop resolves the same, this makes the intent explicit and
    /// covers the frames between a poll-driven swap and the next Rebuild). The card's
    /// live face is re-claimed off the docked DialogPopup automatically by
    /// <c>CardFace.Maintain</c> — the popup's own buttons still
    /// commit the choice. Change-deduped Info line on present / redraw-swap.
    /// </summary>
    private void PresentShortRestCard(CardsHandUI hand, CAbilityCard lost)
    {
        AbilityCardUI? widget = CardsGameApi.ShortRestedCardWidget(hand);
        if (widget == null || widget.AbilityCard == null)
        {
            // The sacrifice's widget is not resolvable this frame (rare mid-swap) —
            // drop any stale display; the next poll re-presents once it exists.
            RemoveShortRestCard();
            return;
        }

        VRCard card = AdoptedCard(widget);
        bool changed = !ReferenceEquals(lost, _shortRestPresented);
        bool isNewCard = !ReferenceEquals(card, _shortRestCard);
        if (isNewCard)
        {
            if (_shortRestCard != null)
                // Issue 2 REDRAW: the previously-offered sacrifice flies BACK into the discard pile
                // (that is where it came from) and parks — instead of just vanishing in place.
                FlyShortRestCardToDiscard(_shortRestCard);
            _shortRestCard = card;
        }
        // Capture after retiring the old physical offer: re-adoption can replace its widget
        // while retaining the same model card. Acceptance may clear the native state later.
        Net.CardFlightVisibility.MarkShortRest(lost);

        card.Grabbable = false;      // display-only — the docked choice commits, not a drop
        card.PokeSelectEnabled = false;

        // Task #4b (collision safety): the sacrifice docks into the LEFT recess — if the
        // mod still shows a played card in EITHER slot it is stale by definition here
        // (PerformShortRest ran DeselectAllCards game-side, CardsHandUI.cs:771-area), so
        // re-home it to the fan BEFORE docking; the two must never overlap in a recess.
        // Normally SyncFromGameState already evicted the occupancy and the closed-fan
        // adopt in CardFan.SetCards re-parks the physical card; this covers any ordering
        // where the sync lags the present by a frame.
        for (int s = 0; s < 2; s++)
        {
            VRCard? stale = _tray.Occupant(s);
            if (stale == null || ReferenceEquals(stale, card))
                continue;
            _tray.RemoveCard(stale);
            _fan.Add(stale);
            VRLog.Info("Cards", $"Short rest: stale occupant re-homed from slot {s + 1} to the fan " +
                                "before docking the sacrifice (collision safety).");
        }

        card.gameObject.SetActive(true);
        _tray.PlacePickCard(card, 0); // LEFT slot recess (test #28) — display-only sacrifice; sets the home

        // BURN ANIM fallback seed: record the SEAT the sacrifice is being placed on as its
        // last-known world pose. The Rebuild zone loop cannot do it here — the fly-in below marks
        // the card flying, and the loop skips flying cards — and no further Rebuild need happen
        // before the player commits the burn, which is why the hardware log showed "NO last-known
        // VR position" and skipped the animation entirely. With the seat recorded, even the
        // degraded path (card already parked/recycled by the time the burn watcher looks) can fly
        // its transient slab from the left slot, i.e. from where the player actually saw the card.
        if (card.TryGetHomeWorldPose(out Vector3 seatPos, out Quaternion seatRot))
        {
            _lastCardWorldPos[widget] = seatPos;
            _lastCardWorldRot[widget] = seatRot;
        }

        // Issue 2: the offered sacrifice ORIGINATES in the discard pile (short rest loses a random
        // DISCARDED card), so on a fresh present / redraw-swap it flies OUT of the discard pile and
        // arcs over the board INTO the left slot — never popping up from below. A plain rebuild
        // (same card still offered) leaves the seated card where it sits. Falls back gracefully to
        // PlacePickCard's default seat when the discard pile is off / not built (no wrong-spot fly).
        if (isNewCard && !card.IsHeld
            && _piles.TryGetPileWorld(PileKind.Discard, out Vector3 srcPos, out float srcWidth))
        {
            card.FlyFromPile(srcPos, srcWidth, FlyToPileSeconds, BoardUp(), BoardArcMin());
            // MP parity (report 6): the short-rest sacrifice flying OUT of the discard pile into
            // the left slot is a card gliding back out of a pile — peers replay it in reverse.
            ReportCardFx(Net.CardFxAnchor.Discard, Net.CardFxAnchor.Slot0);
            VRLog.Info("Cards", $"Short rest: sacrifice '{CardsGameApi.CardName(widget)}' flies OUT of the discard " +
                                $"pile into the left slot ({FlyToPileSeconds:F2}s, arc over the board, orientation " +
                                "locked) — it originates there (issue 2; not a fly-from-below).");
        }

        if (changed)
        {
            bool swap = _shortRestPresented != null;
            LogShortRestSacrifice(hand, lost, widget, swap);
            _shortRestPresented = lost;
        }
    }

    /// <summary>
    /// THE ONE LINE THAT STATES BOTH PICTURES OF THE SHORT-REST SACRIFICE — grep
    /// <c>SHORT REST SACRIFICE</c>.
    ///
    /// <para><b>WHY IT NAMES THE PEER'S PICTURE TOO.</b> User report 2026-09-05, item 15, verbatim:
    /// "Bei einer kurzen Rast soll es sichtbar sein welche Karte dort liegt - ich sehe nur die
    /// Rückseite." Answering that from logs used to need TWO of them read side by side — this
    /// client's "presenting sacrificed card 'X'" and the watcher's "round-card faces=anon-back", one
    /// of which is a 2 kB board line. Both terms that decide the watcher's picture are facts THIS
    /// client can evaluate about ITSELF, so it states them rather than leaving them to be
    /// correlated:</para>
    /// <list type="number">
    /// <item>THE REVEAL GATE, AND IT IS NOT THE ONE THIS LINE USED TO NAME.
    /// <see cref="Net.RevealGate.PeersSeeOurCardFronts"/> is the rule for a card of the TWO-CARD
    /// COMMIT and it IS always false here — every short rest runs inside
    /// <c>SelectAbilityCardsOrLongRest</c> (<c>CardsHandUI.UpdateShortRest</c> only shows the
    /// button in that phase; the 2026-09-06 host census reads <c>PHASE=SelectAbilityCardsOrLongRest
    /// </c> for all thirteen ticks of the co-player's rest). But the sacrifice is not that
    /// population: it is
    /// <see cref="Net.RevealGate.PeerCardPopulation.SacrificedCard"/>, which the user carved out of
    /// the phase on 2026-09-05 and put BACK INTO it on 2026-09-07 (item 6: "Kurze Rast =
    /// Auswahlphase = verdeckt") — read that member's own doc for why the two rulings are separated
    /// in time by <c>FinalizeShortRest</c> rather than contradictory — and the watcher asks
    /// <c>RevealGate.IsPublicPopulation</c> over that member and nothing else
    /// (<c>RemoteControlBoard.TryResolveSacrifice</c>). Printing the commit's gate here made this
    /// line say "backs by rule" about a card the rule permits — the instrument was quoting a term
    /// its own subject does not use.</item>
    /// <item>THE IDENTITY, and the second half of the sentence that stood here was FALSE. The card
    /// is not in <c>CCharacterClass.RoundAbilityCards</c> — that much is true, and it is why the
    /// recess drew an anonymous back before ModBuild 462 — but "no wire record carries its key" has
    /// not been true since: extension record 39 (<c>NetProtocol.ExtIdSacrificeSeat</c>) names WHICH
    /// recess holds it and WHERE it sits in this character's DISCARD arc, which the card never
    /// leaves while it lies here. Whether that record actually WROTE anything is a fact about this
    /// client and is reported by its own sender line, grep token <c>SHORT REST SEAT</c> — read that
    /// line, not this one, to learn whether the identity travelled.</item>
    /// </list>
    ///
    /// <para>Change-gated by its caller (present / redraw-swap only), so a short rest costs one or
    /// two lines. PURE: it latches nothing, so retiring it can break nothing.</para>
    /// </summary>
    private static void LogShortRestSacrifice(CardsHandUI hand, CAbilityCard lost,
                                              AbilityCardUI widget, bool swap)
    {
        bool online = FFSNetwork.IsOnline;
        bool front = Net.RevealGate.CardFaces(Net.RevealGate.PeerCardPopulation.SacrificedCard,
            hand.PlayerActor, lost.CardInstanceID, out Net.RevealGate.FaceRule rule)
            != Net.RevealGate.CardFaceSource.None;
        VRLog.Note("Cards", $"SHORT REST SACRIFICE: {(swap ? "REDREW —" : "presenting")} "
            + $"'{CardsGameApi.CardName(widget)}' in the LEFT recess, FRONT up, display-only. "
            + $"peers draw: {(!online ? "n/a (offline)" : front ? "FRONT" : "BACK")} — "
            + Net.RevealGate.RuleText(rule)
            + ". Its accepted short-rest burn flight remains covered, including a phase edge. "
            + "Record 39 names a model seat independently of face permission.");
    }

    /// <summary>
    /// Issue 2 (short-rest REDRAW): fly the previously-offered sacrifice card BACK into the discard
    /// pile (its origin) with the same over-the-board arc as <see cref="TryStartFlyToPile"/>, then
    /// park it on arrival. Falls back to an instant park when the card is held / already parked or
    /// the discard pile is off / not built (never a wrong-spot teleport). Purely VR presentation —
    /// the game's own short-rest state is untouched.
    /// </summary>
    private void FlyShortRestCardToDiscard(VRCard card)
    {
        Net.CardFlightVisibility.Forget(card.GameCard?.AbilityCard);
        if (card.IsHeld || !card.gameObject.activeInHierarchy
            || !_piles.TryGetPileWorld(PileKind.Discard, out Vector3 pos, out float width))
        {
            _factory.Park(card);
            return;
        }
        float minArc = BoardArcMin();
        _flyingToPile.Add(card);
        VRCard flying = card;
        // MP parity (report 6): the redrawn sacrifice flying back into the discard pile.
        ReportCardFx(Net.CardFxAnchor.Slot0, Net.CardFxAnchor.Discard);
        CardFlightLedger.Note("own", "Discard", "own-short-rest-redraw",
            card.GameCard != null ? CardsGameApi.CardName(card.GameCard) : card.name);
        card.FlyToPile(pos, width, FlyToPileSeconds, BoardUp(), () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"Short rest REDRAW: '{flying.name}' reached the discard pile — parked.");
        }, minArc);
        VRLog.Info("Cards", $"Short rest REDRAW: '{card.name}' flies BACK into the discard pile " +
                            $"({FlyToPileSeconds:F2}s, arc over the board, orientation locked) before the new " +
                            "sacrifice flies out.");
    }

    /// <summary>
    /// Tear down the short-rest sacrifice display (choice resolved / ShortRestedCard
    /// went null / mode-hand-scenario change). Parks the card (its face stays adopted
    /// for the pile viewer to reuse — the game restores it on hand teardown); the zone
    /// loop re-parks it harmlessly thereafter. Change-deduped Info line.
    /// </summary>
    private void RemoveShortRestCard()
    {
        if (_shortRestCard == null && _shortRestPresented == null)
            return;
        if (_shortRestCard != null)
        {
            // BURN ANIM (user: "a card burned by a SHORT REST must slide into the burnt pile"):
            // when the player accepts the sacrifice the game moves it straight into the Lost pile,
            // and THIS method — running inside Rebuild, i.e. BEFORE TickBurnToPile in the same
            // Update — used to park it on the spot. The burn watcher then found only a parked card
            // with no recorded pose and logged "animation skipped" (hardware log 2026-07-26,
            // 'ABILITY_CARD_LeapingCleave'), so the sacrifice simply blinked out of the left slot.
            // Launch the flight here instead, while the card is still seated and live; the claim
            // inside TryStartBurnFly stops the watcher from starting a second one.
            //
            // ─── "ONLY A CARD THAT IS NOT FLYING AFTERWARDS IS PARKED" WAS THE BUG, AND IT WAS
            // ─── WRITTEN HERE AS AN ASSERTION (2026-09-07 report item 7) ────────────────────────
            // Verbatim: "Der Spieler berichtet, dass die Karte wegploppt, man das Verbrenne-Geräusch
            // hört (obwohl man die Karte nicht mehr sieht) und danach die Fluganimation in den
            // Stapel 'aus dem Nichts' kam." All three pictures are this one line.
            //
            // TryStartBurnFly has THREE outcomes, not two, and the discarded return value carried
            // the third. It returns true when the burn path OWNS the card — which is EITHER "the
            // flight launched" OR "the flight is HELD while the game's burn artwork still plays ON
            // the lying card", the user-ruled order (artwork first, THEN the pile flight). Its own
            // doc states the contract: "In the held case the caller must leave the card exactly
            // where it lies (no park, no vanish)." A HELD card is not flying, so `!IsFlying` was
            // true and this parked the very card the burn path had just taken ownership of.
            //
            // WHAT THAT PRODUCED, MEASURED, in the ModBuild 470 peer log, three consecutive lines:
            //   235407  BURN ANIM: holding 'ABILITY_CARD_GrabandGo' ON THE BOARD while its burn
            //           artwork plays (effect running)          <- the hold was taken
            //   235408  Short rest: sacrificed card removed from the board centre
            //                                                    <- and this parked it anyway
            //   235709  BURN HOLD: 'ABILITY_CARD_GrabandGo' waited 2,00s on the board (artwork
            //           finished) - flying to the Burnt pile now
            //   235711  BURN ANIM [pile-watch/slab]: transient card-back slab from (-13.25, 7.14,
            //           -14.03) -> Burnt pile
            // The card popped at 235408; the game's own PlaySound_CardUI_BurnedCard and its
            // 2 s BurnCardTimeline then played on a widget with no VR card in front of the player;
            // and 2.00 s later the release found no live card (`_factory.Find` null, hence the
            // `/slab` branch) and flew a transient slab out of the empty seat. "Aus dem Nichts" is
            // literally what the /slab suffix means.
            //
            // THE FIX IS TO USE THE RETURN VALUE. Held or flying, the burn path owns the card and
            // parks it itself: TickBurnToPile re-offers a held widget every tick (a held widget is
            // deliberately kept OUT of _knownBurntWidgets), and the release launches the same
            // FlyToPile arc from the card's true, still-live pose — so the sequence becomes the one
            // the user asked for: burn artwork to completion, THEN the flight, the card
            // disappearing as part of it. `IsFlying` STAYS as the belt for the OTHER flight that
            // can own this card here, the redraw's fly-back to the discard pile
            // (FlyShortRestCardToDiscard), which the burn path knows nothing about.
            //
            // THE FACE RULING IS SATISFIED BY CONSTRUCTION AND WAS NOT BEFORE: "Beim Verbrennen
            // EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar sein." The
            // slab this used to fall back to is a card BACK on both faces; the real VR card that
            // now flies carries the front it was already showing.
            CardsHandUI? provenanceHand = CurrentHand();
            if (provenanceHand != null && PileFateOf(provenanceHand, _shortRestCard) != PileKind.Burnt)
                Net.CardFlightVisibility.Forget(_shortRestCard.GameCard?.AbilityCard);
            bool burnPathOwnsIt = TryStartBurnFly(CurrentHand(), _shortRestCard, "short-rest sacrifice");
            if (!burnPathOwnsIt && !_shortRestCard.IsFlying)
                _factory.Park(_shortRestCard);
            _shortRestCard = null;
        }
        if (_shortRestPresented != null)
        {
            VRLog.Info("Cards", "Short rest: sacrificed card removed from the board centre " +
                                "(choice resolved / short rest ended).");
            _shortRestPresented = null;
        }
    }

    /// <summary>
    /// Redraw watchdog (test #25, item 1d): <c>PerformFinalShortRest</c> re-points
    /// ShortRestedCard at the alternate card WITHOUT a mode / selection change, so
    /// nothing else would mark the driver dirty. Poll the accessor (guarded exactly
    /// like Rebuild — off during pick modes) and flip dirty on any present / swap /
    /// remove edge; Rebuild is the sole executor. Allocation-free, no-op when steady.
    /// </summary>
    private void PollShortRest(CardsHandUI? hand)
    {
        // ITEM 6b: a pick that has ENDED must not keep the short-rest watchdog switched off — the
        // guard exists so the two flows never own the slots at once, and a dead one owns nothing.
        CAbilityCard? current = null;
        if (hand != null && !PickFlowLive(hand))
            current = CardsGameApi.ShortRestedCard(hand);
        if (!ReferenceEquals(current, _shortRestPresented))
            _dirty = true;
    }
}
