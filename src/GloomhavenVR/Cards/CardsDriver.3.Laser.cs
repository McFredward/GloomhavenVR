using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 3 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: fan laser, fan hover split, hand-contact single winner (issue A/B), board laser,
// browse laser, active laser, modal input-block, slot snap preview.
//
// ORDER THAT ENTERS THIS FILE: UpdateHandContactArbitration must run AFTER the laser paths in
// TickInteractionsAndStatus (part 2). The fields each region needs are declared beside it here,
// not in part 1 — that was already true before the split and is why these cuts are contiguous.

internal sealed partial class CardsDriver
{
    // ------------------------------------------------------------------ fan laser --

    private VRCard? _laserHover;

    /// <summary>
    /// Fan-grab reliability (T2): unscaled-time deadline until which the LAST laser-hovered
    /// fan card still wins the trigger after the beam slips off it. The trigger PULL itself
    /// jerks the aim ray (hardware: grabs out of the open fan missed every 2nd-3rd try) —
    /// the exact frame of TriggerDown the ray often no longer touches the narrow card
    /// strip, the hover cleared, and the trigger fell through to nothing (the proximity
    /// fallback was ALSO deferred: our own fan clamp from the previous frame keeps
    /// <c>Ray.HasFreshUiHit</c> fresh, and ProximityGrabber yields on that flag). A short
    /// grace keeps the highlighted card the trigger's owner across the pull.
    /// </summary>
    private float _laserHoverGraceUntil;

    /// <summary>How long (s, unscaled) a slipped-off fan hover still owns the trigger.
    /// Long enough to bridge a trigger-pull jerk (a few frames), short enough that the
    /// beam clamp visibly releases as soon as the player genuinely points away.</summary>
    private const float FanHoverGraceSeconds = 0.15f;

    /// <summary>One-shot session log guard for the fan grab-rescue confirmation line.</summary>
    private static bool s_loggedFanRescue;

    // -------------------------------------------- fan hover ownership (laser vs hand) --

    /// <summary>Which of the two hover sources owns the fan's ONE highlight this frame.</summary>
    private enum FanHoverOwner
    {
        None,
        Laser,
        Hand,
    }

    /// <summary>
    /// USER ISSUE (pulling a card out of the fan): the laser could highlight one card while the
    /// physically reaching hand highlighted a DIFFERENT one — two lifted cards at once, and the
    /// trigger silently belonged to the laser's card, so the fan promised two things and honoured
    /// the one the player was not looking at. The two sources were independent by construction:
    /// <see cref="UpdateFanLaser"/> pops via <c>_laserPopped</c>, the hand pops via
    /// <c>_popped</c>/<c>_pokeHover</c>, and <see cref="VRCard"/>'s pop is an OR of the two —
    /// <see cref="UpdateHandContactArbitration"/> only ever arbitrated hand candidates AGAINST EACH
    /// OTHER (and deliberately exempted the laser card), never hand against laser.
    ///
    /// ARBITRATION RULE — sticky first-engaged ownership: the source that engaged FIRST keeps the
    /// fan highlight until IT disengages; the other source may not raise a second one meanwhile.
    /// Chosen over "laser always wins" because on marginal geometry (the hand reaching in is
    /// exactly when the same controller's ray also sweeps the fan) an unconditional priority makes
    /// the highlight jump between the two sources as the hand moves, and flapping is worse than
    /// either choice. Two refinements:
    ///   * both sources on the SAME card is not a conflict — the laser takes it so the beam clamps
    ///     visibly to the card the trigger will grab;
    ///   * a fresh engagement with BOTH sources live in the same frame goes to the LASER, matching
    ///     the split's existing laser-first precedence (see <see cref="UpdateFanHoverSplit"/>) and
    ///     the trigger arbitration in <c>ProximityGrabber</c> (<c>Ray.HasFreshUiHit</c>).
    /// Ownership is released the moment the owner has no candidate left, so the other source can
    /// take over in the SAME frame — there is never a frame with no highlight.
    ///
    /// The loser loses the trigger too, not just the pop: whichever source owns the highlight also
    /// owns the grab (the hand branch in <see cref="UpdateFanLaser"/> / the pop-suppression in
    /// <see cref="UpdateHandContactArbitration"/>). That is the same "what pops is what you grab"
    /// contract the hand-side single-winner arbitration already enforces — splitting them here
    /// would recreate exactly the lie the user reported.
    /// </summary>
    private FanHoverOwner _fanHoverOwner;

    /// <summary>Throttle clock (unscaled seconds) for the ownership-change log.</summary>
    private static float s_nextFanOwnerLogAt;

    /// <summary>
    /// Elect the owner of the fan's single highlight for this frame (see
    /// <see cref="_fanHoverOwner"/> for the rule and why it is sticky).
    /// <paramref name="rayCard"/> is the fan card the beam is on this frame (null = none, uGUI
    /// test already applied). Allocation-free.
    /// </summary>
    private FanHoverOwner ResolveFanHoverOwner(VRHand dom, VRCard? rayCard)
    {
        VRCard? handCard = HandOwnedFanCard(dom);
        bool laserLive = rayCard != null;
        bool handLive = handCard != null;

        FanHoverOwner owner;
        if (!laserLive && !handLive)
            owner = FanHoverOwner.None;
        else if (laserLive && handLive && ReferenceEquals(rayCard, handCard))
            owner = FanHoverOwner.Laser; // same card — not a conflict; prefer the honest beam clamp
        else if (_fanHoverOwner == FanHoverOwner.Laser && laserLive)
            owner = FanHoverOwner.Laser; // incumbent holds while its own source still has a card
        else if (_fanHoverOwner == FanHoverOwner.Hand && handLive)
            owner = FanHoverOwner.Hand;
        else
            owner = laserLive ? FanHoverOwner.Laser : FanHoverOwner.Hand; // fresh engage; laser takes ties

        if (owner != _fanHoverOwner)
        {
            _fanHoverOwner = owner;
            float now = Time.unscaledTime;
            if (now >= s_nextFanOwnerLogAt)
            {
                s_nextFanOwnerLogAt = now + 0.5f;
                VRLog.Debug("Cards", $"Fan highlight owner → {owner} (laser='{rayCard?.name ?? "none"}', " +
                                     $"hand='{handCard?.name ?? "none"}'). Sticky single-owner: the other " +
                                     "source raises no second highlight and does not own the trigger.");
            }
        }
        return owner;
    }

    /// <summary>
    /// The ONE fan card the free hand is physically in contact with, or null. Reads the
    /// hand-contact arbitration's winner (elected LAST tick — the arbitration deliberately runs
    /// after the laser paths, see <see cref="UpdateHandContactArbitration"/>; a one-frame-old
    /// winner is exactly right for a STICKY owner and keeps the frame order untouched) and falls
    /// back to the ProximityGrabber highlight, which is the source the T2 lift-priority rescue was
    /// written against. The winner is read rather than the highlight alone because while the laser
    /// owns the fan every fan card refuses the hand in <c>AllowsHand</c>, so
    /// <c>Grabber.Highlighted</c> is null then — the contact winner keeps being elected regardless,
    /// which is what makes the laser→hand handoff instant instead of costing a re-acquire frame.
    /// </summary>
    private VRCard? HandOwnedFanCard(VRHand dom)
    {
        // Unity's lifetime-aware != (a destroyed card is "null" without being a null reference).
        VRCard? winner = _handContactWinner;
        if (winner != null && !winner.IsHeld && winner.CanGrab && _fan.Contains(winner))
            return winner;
        if (dom.Grabber.Highlighted is VRCard prox && !prox.IsHeld && prox.CanGrab && _fan.Contains(prox))
            return prox;
        return null;
    }

    /// <summary>
    /// Demeo pluck (P6): the dominant hand's laser highlights fan cards (pop + one
    /// haptic tick per card change) and TriggerDown pulls the pointed card into the
    /// dominant hand (released on TriggerUp). Proximity grab keeps working unchanged.
    ///
    /// T2 (fan grab misses ~every 2nd-3rd try): when the ray does NOT land on a fan card
    /// this frame, two rescue paths mirror the tray's LIFT-PRIORITY accept (task #2)
    /// before the hover is dropped — the card the player was visibly promised wins the
    /// trigger instead of the pull falling through:
    /// 1. Proximity lift-priority: the dominant hand's proximity HIGHLIGHT is a fan card
    ///    (the popped card under the reaching hand — the affordance promise). Beam clamps
    ///    to it, TriggerDown grabs exactly it. Reaching into the fan previously lost the
    ///    trigger to Ray.HasFreshUiHit arbitration (see ProximityGrabber.Tick): the fan
    ///    clamp raises the flag every hovered frame, so the proximity path NEVER fired
    ///    while the laser was anywhere near the fan.
    /// 2. Hover grace: the last laser-hovered card still wins for a short window
    ///    (<see cref="FanHoverGraceSeconds"/>) after the beam slips off — the trigger
    ///    pull itself jerks the ray off the narrow card strip on the press frame.
    /// Both yield to a live game-UI hit (RayUgui) exactly like the tray pattern, so a
    /// UI click can never double-fire with a grab; the beam clamp keeps suppressing the
    /// board far-click (Cards ticks before Board). Single-winner by construction.
    ///
    /// SINGLE OWNER (user issue: two cards highlighted at once): the beam only acts when it OWNS
    /// the fan hover this frame — see <see cref="_fanHoverOwner"/> for the sticky first-engaged
    /// rule. While the reaching HAND owns it instead, the laser raises no pop, clamps no beam and
    /// hands the trigger to the hand's card (which is exactly the shape of T2 rescue path 1, so
    /// the two share one branch below).
    /// </summary>
    private void UpdateFanLaser()
    {
        VRHand? dom = VRHands.Primary;
        // Ray.ACTIVE, not Ray.Enabled, in this and every laser guard below (2026-08-08): Active is
        // the level-derived effective state — the mode mask AND a pose AND not holding AND not
        // stood down for a physical card contact (see UpdateLaserContactStandDown). The two terms
        // spelled out beside it were always implied by it; the card-contact term is the new one,
        // and reading it HERE is what makes "the hand is in a card" clear this path's hover
        // through the very same early-out a disabled ray already took.
        if (!_fan.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Active || dom.Grabber.Held != null)
        {
            _fanHoverOwner = FanHoverOwner.None;
            ClearLaserHover();
            return;
        }

        // Where the beam is on the fan RIGHT NOW (null = nowhere, or a closer live game-UI hit).
        PickPose pick = dom.Ray.Current;
        VRCard? card = null;
        Vector3 point = default;
        if (_fan.TryRaycast(pick.Origin, pick.Direction, _laserHover, out VRCard? beamCard,
                out Vector3 beamPoint, out float dist)
            && beamCard != null
            && !(dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            card = beamCard;
            point = beamPoint;
        }

        FanHoverOwner owner = ResolveFanHoverOwner(dom, card);
        if (owner != FanHoverOwner.Laser)
        {
            // The laser does NOT own the fan hover this frame — either the reaching hand does, or
            // neither source has a card. Two ways a card still owns the TRIGGER without the beam:
            // 1. hand ownership (which subsumes the T2 proximity lift-priority rescue), and
            // 2. the T2 pull-jerk grace on the card the beam just left.
            // Yields to a live game-UI hit like the tray lift-priority accept.
            if (owner == FanHoverOwner.Hand)
            {
                // Hand ownership is open-ended (it lasts as long as the hand stays in contact), so
                // the laser pop must go NOW rather than be left standing on the rescue winner the
                // way the 0.15 s grace does: a lingering _laserHover ALSO keeps gating the board /
                // browse / active lasers off ("a fan hover owns the frame"), and that gate must
                // never outlive the beam actually being on the fan — the same starvation
                // UpdateBoardLaser's lift-priority branch had to be narrowed for. No visual blink:
                // the winner is in contact range by construction and is the only fan card that
                // still AllowsHand, so ProximityGrabber's highlight carries the very same pop.
                ClearLaserHover();
            }
            if (!dom.RayUgui.HasHit)
            {
                VRCard? rescue = owner == FanHoverOwner.Hand
                    ? HandOwnedFanCard(dom) // 1. the popped card under the reaching hand
                    : null;
                if (rescue == null && _laserHover != null && !_laserHover.IsHeld && _fan.Contains(_laserHover)
                    && Time.unscaledTime <= _laserHoverGraceUntil)
                    rescue = _laserHover; // 2. trigger-pull jerk grace

                if (rescue != null)
                {
                    // A stale laser pop on a DIFFERENT card than the rescue winner drops now.
                    if (_laserHover != null && !ReferenceEquals(_laserHover, rescue))
                        ClearLaserHover();
                    // Own the trigger for the winner, but do NOT move the beam. The beam is by
                    // definition NOT on the winner here (either it missed the fan entirely, or it
                    // is on a different card that lost the ownership arbitration), so any point
                    // published here is a fiction — and the beam ends at that point's
                    // PROJECTION onto the aim ray, so publishing the card CENTRE parked the
                    // reticle on the plane through the centre perpendicular to the beam: the
                    // "invisible wall through the middle of the card" that stayed under the dot
                    // however far the beam was swept off the card. SuppressFarClick keeps the
                    // board far-click + proximity double-path suppression (HasFreshUiHit) that
                    // the clamp used to provide, without inventing a hit.
                    dom.Ray.SuppressFarClick();
                    if (dom.TriggerDown && rescue.CanGrab)
                    {
                        if (!s_loggedFanRescue)
                        {
                            s_loggedFanRescue = true;
                            VRLog.Info("Cards", "Fan grab RESCUE active (T2): the HIGHLIGHTED fan card " +
                                                "won a trigger the beam was not on (hand owns the fan " +
                                                "hover / ray missed the card strip / pull-jerk grace) — " +
                                                "what is lifted is always what gets grabbed.");
                        }
                        ClearLaserHover();
                        dom.Grabber.ForceGrab(rescue, releaseOnTriggerUp: true);
                    }
                    return;
                }
            }
            ClearLaserHover();
            return;
        }

        // The laser owns the fan hover, which is only ever elected while the beam IS on a card.
        VRCard beam = card!;
        if (!ReferenceEquals(beam, _laserHover))
        {
            ClearLaserHover();
            _laserHover = beam;
            beam.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }
        _laserHoverGraceUntil = Time.unscaledTime + FanHoverGraceSeconds; // refresh the pull-jerk grace

        // Clamp the visible beam to the card — also raises Ray.HasFreshUiHit, which
        // suppresses the board far-click for this trigger press.
        dom.Ray.UiHitOverride = point;

        if (dom.TriggerDown && beam.CanGrab)
        {
            ClearLaserHover();
            dom.Grabber.ForceGrab(beam, releaseOnTriggerUp: true);
        }
    }

    private void ClearLaserHover()
    {
        if (_laserHover == null)
            return;
        _laserHover.SetLaserHover(false);
        _laserHover = null;
    }

    // ------------------------------------------------------------------ fan hover split --

    private int _fanHoverIndex = -1;

    /// <summary>
    /// G6/G2 (DEMEO-HANDS-CARDS §5 Group D): drive the fan's whole-hand hover SPLIT from
    /// EITHER input source. The dominant-hand laser hover is primary (our controller path);
    /// only when no card is laser-hovered does the dominant hand's proximity highlight —
    /// its finger near a fan card, Demeo-style — split the fan instead. Purely the VISUAL
    /// split: pluck/select still route through <see cref="UpdateFanLaser"/> and the
    /// proximity grabber unchanged. The index is resolved against the fan's OWN card order
    /// (<see cref="CardFan.Cards"/> — the exact list <c>SetHovered</c> indexes into, so it
    /// cannot drift from the driver's <c>_fanBuffer</c> after a mid-frame grab/return) and
    /// pushed only on change. Allocation-free.
    /// </summary>
    private void UpdateFanHoverSplit()
    {
        if (!_fan.IsOpen)
        {
            if (_fanHoverIndex != -1)
            {
                _fanHoverIndex = -1;
                _fan.SetHovered(-1);
            }
            return;
        }

        // Precedence: laser wins whenever a fan card is laser-hovered (primary controller
        // path); the hand-contact arbitration winner fills in when the laser hovers nothing.
        // This ALSO reads the ownership arbitration for free and needs no branch of its own:
        // _laserHover is null on exactly the frames the reaching hand owns the fan hover (see
        // _fanHoverOwner / UpdateFanLaser), so the split always opens around the same single card
        // that is popped — the two can no longer disagree about which source is speaking
        // (issue A: the split always opens around the ONE lifted card — the proximity
        // highlight follows the same winner via AllowsHand, so it stays the fallback for
        // the first frame after a winner change).
        VRCard? hovered = _laserHover;
        if (hovered == null && _handContactWinner != null && _fan.Contains(_handContactWinner))
            hovered = _handContactWinner;
        if (hovered == null)
        {
            VRHand? dom = VRHands.Primary;
            if (dom != null && dom != _gateHand && dom.Grabber.Held == null
                && dom.Grabber.Highlighted is VRCard proximityCard && _fan.Contains(proximityCard))
                hovered = proximityCard;
        }

        int index = hovered != null ? FanIndexOf(hovered) : -1;
        if (index != _fanHoverIndex)
        {
            _fanHoverIndex = index;
            _fan.SetHovered(index);
        }
    }

    /// <summary>Index of <paramref name="card"/> in the fan's card order, or -1. Allocation-free.</summary>
    private int FanIndexOf(VRCard card)
    {
        IReadOnlyList<VRCard> cards = _fan.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
                return i;
        }
        return -1;
    }

    // ------------------------------------------- hand-contact single winner (issue A/B) --

    // The reach constants that used to live here (3.5 cm tip / 13 cm palm / 2 cm hysteresis,
    // scale-1 metres) moved to FanSweep — they were duplicated verbatim in FOUR places
    // (here, CardFan, PileBrowser, ItemsPile) and had already drifted apart in how they were
    // RANKED. FanSweep.ResolveReach now derives them from each target's own live world size, so
    // this fan (whose root hangs off the palm, i.e. relative size exactly 1) gets the identical
    // numbers back while the board-anchored fans finally scale with the board. See FanSweep.

    /// <summary>The single card the free (dominant) hand is currently "in contact with"
    /// (null = none). Elected over the open fan plus the dock/pick-field pool.</summary>
    private VRCard? _handContactWinner;

    /// <summary>
    /// The single dock/pick-field card the GATE (fan-owning, non-dominant) hand is currently in
    /// contact with (null = none). Exists since the 2026-08-04 both-hands ruling: the gate hand now
    /// hovers/grabs every card except the ability fan's own, so it needs the same single-winner
    /// contact election the dominant hand has — without one, two adjacent dock cards could both
    /// pop for that hand (the very issue A/B this arbitration was built for). Deliberately scored
    /// over the dock/pick-field pool ONLY: fan cards refuse the gate hand outright
    /// (VRCard.AllowsGateHand=false — the fan hangs off this very palm), and electing one would
    /// re-create the palm-in-fan flip-flop the veto was born from.
    /// </summary>
    private VRCard? _gateContactWinner;

    /// <summary>Cards currently pop-suppressed by the arbitration — cleared and re-filled every
    /// tick so a card leaving the fan/dock pools can never keep a stale suppression.</summary>
    private readonly List<VRCard> _contactSuppressed = new(24);

    /// <summary>Throttle clock (unscaled seconds) for the arbitration winner-change log.</summary>
    private float _nextContactLogAt;

    /// <summary>
    /// USER ISSUE A (fan sweep lifts several cards) + B (dock highlight fights): per-tick
    /// SINGLE-WINNER arbitration over every card the free (dominant) hand can touch — the
    /// open fan's cards plus the slot-docked/pick-field cards. Among all cards in contact
    /// range (index tip within <see cref="ContactTipReach"/> OR palm within
    /// <see cref="ContactPalmReach"/> of the card's grab collider), exactly ONE wins: the
    /// closest by hand distance, with a <see cref="ContactStickyMargin"/> hysteresis bonus
    /// for the incumbent so the lift never flutters at strip boundaries. Every other pool
    /// card is suppressed (<see cref="VRCard.SetHandPopSuppressed"/>): its hand-driven pop
    /// drops immediately AND it refuses the hand in <c>AllowsHand</c>, so the
    /// ProximityGrabber's highlight — and therefore the trigger grab — lands on the same
    /// single winner. The laser-hovered fan/tray card is never suppressed (the laser path
    /// already arbitrates itself and its pluck must keep working). Allocation-free.
    ///
    /// BOTH HANDS (user ruling 2026-08-04): the GATE hand runs its own parallel election over
    /// the dock/pick-field pool (never the fan — see <see cref="_gateContactWinner"/>), so its
    /// contact lift is single-winner too; dock losers refuse exactly the hands that elected
    /// someone else (<see cref="SuppressDockLoser"/>).
    /// </summary>
    private void UpdateHandContactArbitration()
    {
        VRHand? dom = VRHands.Primary;
        VRCard.HandArbitrationHand = dom;

        // ---- dominant-hand election: open fan + dock/pick-field (unchanged shape) ----
        FanSweepPick<VRCard> pick = FanSweepPick<VRCard>.Empty;
        float worldScale = 1f;
        if (dom != null && !ReferenceEquals(dom, _gateHand) && dom.HasPose
            && dom.Grabber.Held == null && !_modalInputBlocked)
        {
            Vector3 tip = dom.Rig.IndexTip.position;
            Vector3 palm = dom.Rig.PalmCenter.position;
            worldScale = dom.WorldScale;

            if (_fan.IsOpen)
            {
                // FAN SWEEP (user issue: hand exactly BETWEEN two cards): rank fan
                // candidates PRIMARILY by index-fingertip distance (tipFirst) so the card
                // nearest the pointing finger always wins. The palm reach still QUALIFIES a
                // card as a candidate, but mixing the wide, noisy palm metric into the
                // WINNER choice let two adjacent cards' near-equal palm distances flip the
                // lift back and forth — tip-first ranking resolves the midpoint case
                // deterministically. This is now literally the same code the pile fans run.
                IReadOnlyList<VRCard> fanCards = _fan.Cards;
                for (int i = 0; i < fanCards.Count; i++)
                    ScoreContact(fanCards[i], tip, palm, worldScale, tipFirst: true,
                        _handContactWinner, ref pick);
            }
            if (_tray.IsVisible)
            {
                // Dock / pick-field cards keep the min(tip,palm) reach metric — the complaint
                // is the fan sweep, and these sit far enough apart not to oscillate.
                ScoreContact(_tray.Occupant(0), tip, palm, worldScale, tipFirst: false,
                    _handContactWinner, ref pick);
                ScoreContact(_tray.Occupant(1), tip, palm, worldScale, tipFirst: false,
                    _handContactWinner, ref pick);
                for (int i = 0; i < _fieldCards.Count; i++)
                    ScoreContact(_fieldCards[i], tip, palm, worldScale, tipFirst: false,
                        _handContactWinner, ref pick);
            }
        }

        // ---- gate-hand election: dock/pick-field ONLY (both-hands ruling 2026-08-04) ----
        // Same candidacy/ranking/incumbent-stickiness as the dominant hand above (shared
        // FanSweep.Score), its own incumbent so the two elections can never steal each other's
        // hysteresis. Skipped while that hand holds something / under a modal, exactly like the
        // dominant branch. Fan cards are deliberately NOT scored — see _gateContactWinner.
        FanSweepPick<VRCard> gatePick = FanSweepPick<VRCard>.Empty;
        float gateScale = 1f;
        if (_gateHand != null && _gateHand.HasPose && _gateHand.Grabber.Held == null
            && !_modalInputBlocked && _tray.IsVisible)
        {
            Vector3 tip = _gateHand.Rig.IndexTip.position;
            Vector3 palm = _gateHand.Rig.PalmCenter.position;
            gateScale = _gateHand.WorldScale;
            ScoreContact(_tray.Occupant(0), tip, palm, gateScale, tipFirst: false,
                _gateContactWinner, ref gatePick);
            ScoreContact(_tray.Occupant(1), tip, palm, gateScale, tipFirst: false,
                _gateContactWinner, ref gatePick);
            for (int i = 0; i < _fieldCards.Count; i++)
                ScoreContact(_fieldCards[i], tip, palm, gateScale, tipFirst: false,
                    _gateContactWinner, ref gatePick);
        }

        VRCard? winner = pick.Winner;
        if (!ReferenceEquals(winner, _handContactWinner))
        {
            _handContactWinner = winner;
            // Throttled arbitration log: winner + its index-tip distance + runner-up, so the
            // next hardware log proves the tip-first resolution (between-two-cards no longer
            // flip-flops). Rate-limited so a rapid crossing cannot flood the log.
            float now = Time.unscaledTime;
            if (winner != null && now >= _nextContactLogAt)
            {
                _nextContactLogAt = now + 0.5f;
                FanSweep.LogWinner("Ability-fan", dom != null ? dom.Side.ToString() : "—", pick,
                    FanSweep.ResolveReach(worldScale, ((IFanSweepTarget)winner).SweepFaceWidthWorld));
            }
        }
        VRCard? gateWinner = gatePick.Winner;
        if (!ReferenceEquals(gateWinner, _gateContactWinner))
        {
            _gateContactWinner = gateWinner;
            float now = Time.unscaledTime;
            if (gateWinner != null && _gateHand != null && now >= _nextContactLogAt)
            {
                _nextContactLogAt = now + 0.5f;
                FanSweep.LogWinner("Dock/pick-field", _gateHand.Side.ToString(), gatePick,
                    FanSweep.ResolveReach(gateScale, ((IFanSweepTarget)gateWinner).SweepFaceWidthWorld));
            }
        }

        // Re-derive the suppression set from scratch every tick (stale-flag proof: a card
        // that left the pools mid-frame is cleared here or by its own OnDisable).
        for (int i = 0; i < _contactSuppressed.Count; i++)
        {
            if (_contactSuppressed[i] != null)
                _contactSuppressed[i].SetHandPopSuppressed(false);
        }
        _contactSuppressed.Clear();
        if (winner == null && gateWinner == null)
            return;
        if (_fan.IsOpen && winner != null)
        {
            // SINGLE FAN HIGHLIGHT (user issue: laser on one card, hand near another, both lifted).
            // While the LASER owns the fan hover (see _fanHoverOwner) the hand may not raise a
            // second one, so EVERY fan card except the beam's own is suppressed here — this tick's
            // contact winner included. Feeding _laserHover in as the fan-side winner reuses the
            // existing exemption rather than adding a second suppression path, and leaves the
            // "the laser-hovered card is never suppressed" invariant literally intact. The tray
            // pool keeps the real contact winner: this arbitration is about the FAN's highlight.
            VRCard fanWinner = _fanHoverOwner == FanHoverOwner.Laser && _laserHover != null
                ? _laserHover
                : winner;
            IReadOnlyList<VRCard> fanCards = _fan.Cards;
            for (int i = 0; i < fanCards.Count; i++)
                SuppressContactLoser(fanCards[i], fanWinner);
        }
        if (_tray.IsVisible)
        {
            SuppressDockLoser(_tray.Occupant(0), winner, gateWinner, dom);
            SuppressDockLoser(_tray.Occupant(1), winner, gateWinner, dom);
            for (int i = 0; i < _fieldCards.Count; i++)
                SuppressDockLoser(_fieldCards[i], winner, gateWinner, dom);
        }
    }

    /// <summary>
    /// Fold <paramref name="card"/> into the running <paramref name="pick"/> through the SHARED
    /// election (<see cref="FanSweep.Score{T}"/>) — the one the pile browse fan and the item fan
    /// now run too, so "make the pile fans behave like the hand cards" is a fact of the code
    /// rather than a promise. Candidacy and ranking are documented on <see cref="FanSweep"/>;
    /// <paramref name="tipFirst"/> selects fan (tip-only ranking) versus dock/pick-field
    /// (legacy min(tip,palm), those cards sit far enough apart not to oscillate).
    ///
    /// The reach is resolved PER CARD from that card's own live world width, so a hand-fan card
    /// (whose fan hangs off the palm) resolves to exactly the old constants while a card in a
    /// board-anchored arc scales with the board. Two divides per card per frame.
    /// </summary>
    private void ScoreContact(VRCard? card, Vector3 tip, Vector3 palm, float worldScale,
        bool tipFirst, VRCard? incumbent, ref FanSweepPick<VRCard> pick)
    {
        if (card == null)
            return;
        FanReach reach = FanSweep.ResolveReach(worldScale, ((IFanSweepTarget)card).SweepFaceWidthWorld);
        FanSweep.Score(card, tip, palm, reach, incumbent, tipFirst, ref pick);
    }

    /// <summary>Suppress a FAN card that lost the dominant hand's contact arbitration. The
    /// laser-hovered fan/tray card is exempt — laser hover/pluck must keep working unchanged.</summary>
    private void SuppressContactLoser(VRCard? card, VRCard winner)
    {
        if (card == null || card.IsHeld || ReferenceEquals(card, winner)
            || ReferenceEquals(card, _laserHover) || ReferenceEquals(card, _trayCardHover))
            return;
        card.SetHandPopSuppressed(true);
        _contactSuppressed.Add(card);
    }

    /// <summary>
    /// Suppress a DOCK/PICK-FIELD card that lost the per-hand contact elections (both-hands
    /// ruling 2026-08-04: this pool is swept by BOTH hands). A card that won EITHER election is
    /// never suppressed — pop suppression is card-wide, so suppressing one hand's winner for the
    /// other hand's sake would kill the lift the winning hand was promised. A loser refuses
    /// exactly the hands whose elections ran and elected someone else (the accumulating two-slot
    /// refusal in <see cref="VRCard.SetHandPopSuppressed(bool, VRHand?)"/>), so each hand's grab
    /// keeps following its own winner. Laser-hover exemptions as in
    /// <see cref="SuppressContactLoser"/>.
    /// </summary>
    private void SuppressDockLoser(VRCard? card, VRCard? domWinner, VRCard? gateWinner, VRHand? dom)
    {
        if (card == null || card.IsHeld
            || ReferenceEquals(card, domWinner) || ReferenceEquals(card, gateWinner)
            || ReferenceEquals(card, _laserHover) || ReferenceEquals(card, _trayCardHover))
            return;
        if (domWinner != null)
            card.SetHandPopSuppressed(true, dom);
        if (gateWinner != null)
            card.SetHandPopSuppressed(true, _gateHand);
        _contactSuppressed.Add(card);
    }

    // --------------------------------------------- laser stand-down on card contact --

    /// <summary>
    /// USER REPORT 2026-08-08: "Während die Hand physisch in einer Karte von einem Pile (zB
    /// Hand-Pile) steckt, deaktiviere den Laser — aktuell passiert es mir öfters dass ich eine
    /// Karte greifen will, aber versehentlich mit dem Laser dahinter irgendwas greife oder
    /// bediene." A hand reaching INTO a fan has its controller behind that fan, so its beam
    /// leaves through the card and lands on whatever is behind it — the control board, a hex, a
    /// figure, a floated panel — and the trigger pull that means "take THIS card" was being spent
    /// there. Every fix so far was a per-target yield (the hand-owns-the-trigger claims, the
    /// pop-suppression, the occluder distances); this is the general one: while the hand is in a
    /// card, that hand has no beam at all.
    ///
    /// <para>THE CANDIDATE SOURCE IS THE EXISTING CONTACT ELECTIONS, nothing new — the very winners
    /// that already decide which single card lifts under the hand, each with its own incumbent
    /// hysteresis inside <see cref="FanSweep.Score{T}"/>: <see cref="_handContactWinner"/> (the
    /// dominant hand over the ability fan + the board's slot/pick-field recesses),
    /// <see cref="_gateContactWinner"/> (the gate hand over the same recesses),
    /// <see cref="PileBrowser.HandOwnedCard"/>, <see cref="ItemsPile.HandOwnedChip"/>, and — the
    /// one pool with no election of its own — the active column via the hand's own proximity
    /// highlight (sticky by <c>ProximityGrabber.SwitchMarginMeters</c>). No second election and no
    /// second hysteresis is introduced anywhere: the elections stay the single stable answer to
    /// "which card is this hand's", and the geometry below only ever asks WHETHER that one card is
    /// being touched. The only time term is the release grace inside
    /// <see cref="RayInteractor.StandDownForCardContact"/>, which exists for the trigger-pull jerk,
    /// exactly like the fan occluder's hold.</para>
    ///
    /// <para>AN ELECTION IS NOT A TOUCH — ROUND 2 (user report 2026-08-08: "Sei strenger mit dem
    /// Deaktivieren des Lasers — das will ich eigentlich wirklich nur, wenn die Hand die Karte
    /// physisch berührt; aktuell ist es immer wenn auch eine Karte nur gehighlighted ist, das führt
    /// dazu dass der Laser auch nicht da ist obwohl die Hand weiter über der Karte ist"). The first
    /// round read "this hand elected a card" as "this hand is INSIDE a card". It is not: every one
    /// of those elections is proximity/REACH scored — <see cref="FanSweep.TipReachMeters"/> is
    /// 3,5 cm off the collider surface, <see cref="FanSweep.PalmReachMeters"/> and
    /// <c>ProximityGrabber.ReachMeters</c> are THIRTEEN centimetres — so a winner exists (and the
    /// card visibly lifts) from a good hand's breadth away. That is exactly right for "which card
    /// would this hand take" and exactly wrong for "the beam is buried in a card", so the laser
    /// vanished while the player was still hovering above the fan with nothing in the way of the
    /// beam. <see cref="TryHandContact(VRHand, VRCard, out ContactGeometry)"/> is the missing
    /// term: the elected card must additionally OVERLAP the hand — a hand point inside the card's
    /// face and within <see cref="ContactSlabHalfDepthMeters"/> of its plane. Two orders of
    /// magnitude tighter than the reach that nominated it, and measured, not assumed: the numbers
    /// go into the stand-down log line.</para>
    ///
    /// <para>PER HAND, and only for the fan the hand is actually IN: every source above is
    /// per-hand and reach-gated, so the other hand keeps its laser and a fan the hand is nowhere
    /// near suppresses nothing. Hands that hold something are skipped (a held hand has no beam
    /// anyway — <c>Ray.Active</c>) and elect no winner.</para>
    ///
    /// Runs FIRST in the laser block so the mod's own laser paths see the stand-down on the same
    /// frame they would otherwise hover with it; every one of them reads it through
    /// <c>dom.Ray.Active</c>. The winners it reads are one frame old by construction (the
    /// elections deliberately run after the laser paths — see
    /// <see cref="UpdateHandContactArbitration"/>), which is the same one-frame-old read
    /// <see cref="HandOwnedFanCard"/> already documents and is covered by the release grace.
    /// Allocation-free: the zone strings are literals and the card's name is read only on the
    /// stand-down edge.
    /// </summary>
    private void UpdateLaserContactStandDown()
    {
        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
                continue;
            Object? card = ContactedCard(hand, out string zone, out ContactGeometry geo);
            if (card != null)
                hand.Ray.StandDownForCardContact(zone, card, geo.ToLog());
        }
    }

    /// <summary>
    /// The ONE grabbable card/chip <paramref name="hand"/> is physically TOUCHING right now, or
    /// null. Each of the five pools contributes its own single-winner election as the CANDIDATE
    /// (see <see cref="UpdateLaserContactStandDown"/> for why the elections and not a second one of
    /// our own), and every candidate then has to pass the same geometric overlap test —
    /// <see cref="TryHandContact(VRHand, VRCard, out ContactGeometry)"/> — before it counts.
    ///
    /// <para>A candidate that fails the touch test does NOT end the search: the pools are different
    /// card sets and a hand elected in one can be buried in another (the ability fan hangs off the
    /// gate palm while the browse arc and the item fan float over the board). Falling through costs
    /// at most four more rect tests on a frame that is about to conclude "no contact" anyway.</para>
    ///
    /// <paramref name="zone"/> is a literal naming the pool and <paramref name="geo"/> carries the
    /// measurement, both for the stand-down log line. Allocation-free.
    /// </summary>
    private Object? ContactedCard(VRHand hand, out string zone, out ContactGeometry geo)
    {
        // 1. the dominant hand's election: the open ability fan PLUS the board's slot/pick-field
        //    recesses (the arbitration scores both pools into one winner, so ask the fan which
        //    of the two it is purely to name the zone).
        if (!ReferenceEquals(hand, _gateHand) && ReferenceEquals(hand, VRHands.Primary)
            && TryHandContact(hand, _handContactWinner, out geo))
        {
            zone = _fan.Contains(_handContactWinner!) ? "hand fan" : "board slot / pick field";
            return _handContactWinner;
        }
        // 2. the gate hand's parallel election over the same recesses (fan cards refuse that
        //    hand outright — the fan hangs off its own palm).
        if (ReferenceEquals(hand, _gateHand) && TryHandContact(hand, _gateContactWinner, out geo))
        {
            zone = "board slot / pick field";
            return _gateContactWinner;
        }
        // 3./4. the two board-anchored fans — both hands sweep these.
        VRCard? browse = _browser.HandOwnedCard(hand);
        if (TryHandContact(hand, browse, out geo))
        {
            zone = "pile browse arc";
            return browse;
        }
        ItemsPile.ItemChip? chip = _piles.HandOwnedItemChip(hand);
        if (TryHandContact(hand, chip, out geo))
        {
            zone = "item fan";
            return chip;
        }
        // 5. the active column is the one card pool with NO hand-sweep election of its own, so
        //    its contact signal is the hand's proximity grab candidate — which is exactly "the
        //    card this hand's trigger would take", sticky by the grabber's own switch margin.
        if (hand.Grabber.Highlighted is VRCard prox && prox != null && !prox.IsHeld
            && prox.CanGrab && _active.Contains(prox)
            && TryHandContact(hand, prox, out geo))
        {
            zone = "active column";
            return prox;
        }
        zone = "";
        geo = default;
        return null;
    }

    // ------------------------------------------- the CONTACT test (touch, not proximity) --

    /// <summary>
    /// Half-depth of the slab a hand point must be inside to count as TOUCHING a card, in REAL
    /// metres (multiplied by the hand's own world scale at the call site, like every other distance
    /// in this codebase, so it means the same thing at any rig zoom or board scale).
    ///
    /// <para>Derived, not guessed: the card's own grab box is 2 cm deep in card-local metres
    /// (<c>VRCard.Build</c>: <c>_box.size = new Vector3(w, h, 0.02f)</c>; the item chip's
    /// <c>ColliderDepth</c> is the same 2 cm), so ONE CENTIMETRE is the half-thickness the rest of
    /// the mod already treats as the card's body — a point inside it is inside the card as far as
    /// the grabber is concerned. The remaining 5 mm is tolerance for the two places this test
    /// cannot be exact: controller tracking noise, and the fact that the fingertip/palm anchors are
    /// rig points a few millimetres inside a hand that has no real skin. Against the reach that
    /// NOMINATED the card — 3,5 cm at the fingertip, 13 cm at the palm — this is 2 to 9 times
    /// tighter, which is the whole point of the round-2 report: highlighted is not touched.</para>
    /// </summary>
    private const float ContactSlabHalfDepthMeters = 0.015f;

    /// <summary>
    /// Lateral accept margin on the card's half-extents for the contact test — the same 10 % the
    /// mod's other card-rect tests have long carried (<see cref="LiftHitMargin"/>,
    /// <see cref="FanSweep.LaserAcceptMargin"/>), and card-RELATIVE for the same reason they are:
    /// expressed as a fraction of the live half-extents it is automatically correct on a 0,32×
    /// board and on a 0,80× one. It buys the edges of the card a few millimetres of slack so a
    /// finger touching the very rim of a card still reads as contact; it cannot widen the test into
    /// the neighbouring card's territory, because WHICH card is being tested was already decided by
    /// the election.
    /// </summary>
    private const float ContactRectMargin = 1.10f;

    /// <summary>
    /// What the contact test MEASURED, so the stand-down log line can prove its own threshold
    /// instead of asserting a card name (user report 2026-08-08 round 2). Distances are world
    /// units here and converted to real millimetres exactly once, in <see cref="ToLog"/>.
    /// Plain mutable struct, filled by value on the stack — nothing allocates.
    /// </summary>
    private struct ContactGeometry
    {
        /// <summary>Which hand point touched — a literal, never formatted per frame.</summary>
        internal string Probe;

        /// <summary>Signed distance from that point to the card PLANE (world units, along the card's
        /// +Z: positive is behind the face, negative in front of it).</summary>
        internal float Depth;

        /// <summary>Smaller of the two in-face edge clearances (world units, ≥ 0 on an accept).</summary>
        internal float Margin;

        /// <summary>The depth tolerance that accepted it (world units) — printed as the yardstick.</summary>
        internal float Limit;

        /// <summary>The hand's world scale, i.e. world units per real metre.</summary>
        internal float Scale;

        /// <summary>Real millimetres for the log (world distance ÷ the rig/board scale).</summary>
        internal RayInteractor.CardContact ToLog()
        {
            float mm = 1000f / Mathf.Max(Scale, 1e-4f);
            return new RayInteractor.CardContact(Probe ?? "no probe", Depth * mm, Margin * mm,
                Limit * mm);
        }
    }

    /// <summary>
    /// Is <paramref name="hand"/> physically overlapping <paramref name="card"/> — i.e. is one of
    /// its hand points inside the card's face AND within
    /// <see cref="ContactSlabHalfDepthMeters"/> of the card's plane?
    ///
    /// <para>TESTED WHERE THE CARD VISIBLY IS, and where it rests, accepting either — the lesson
    /// <see cref="TryHitLiftedCard"/> already had to learn for the beam. This test runs on exactly
    /// the card the hand has just elected, which is the card that is at that moment RAISING toward
    /// the player: the live rect is where the player sees it and reaches for it, the resting rect is
    /// where it was a moment ago, and during the raise the truth is somewhere between the two.
    /// Testing one pose alone would make the contact blink through the animation.</para>
    /// </summary>
    private static bool TryHandContact(VRHand hand, VRCard? card, out ContactGeometry geo)
    {
        geo = default;
        if (card == null)
            return false;
        if (card.TryGetLiveLaserRect(out Vector3 c, out Vector3 n, out Vector3 r, out Vector3 u,
                out float hw, out float hh)
            && TryHandContactRect(hand, c, n, r, u, hw, hh, ref geo))
            return true;
        return card.TryGetRestingLaserRect(out c, out n, out r, out u, out hw, out hh)
               && TryHandContactRect(hand, c, n, r, u, hw, hh, ref geo);
    }

    /// <summary>
    /// Item-chip overload of <see cref="TryHandContact(VRHand, VRCard, out ContactGeometry)"/>.
    /// A chip is not a <see cref="VRCard"/> and its face is the item card's own near-square
    /// rectangle, so its geometry comes from <see cref="ItemsPile.ItemChip.TryGetFaceRect"/> — the
    /// same two poses, the same test, the same tolerances.
    /// </summary>
    private static bool TryHandContact(VRHand hand, ItemsPile.ItemChip? chip, out ContactGeometry geo)
    {
        geo = default;
        if (chip == null)
            return false;
        if (chip.TryGetFaceRect(resting: false, out Vector3 c, out Vector3 n, out Vector3 r,
                out Vector3 u, out float hw, out float hh)
            && TryHandContactRect(hand, c, n, r, u, hw, hh, ref geo))
            return true;
        return chip.TryGetFaceRect(resting: true, out c, out n, out r, out u, out hw, out hh)
               && TryHandContactRect(hand, c, n, r, u, hw, hh, ref geo);
    }

    /// <summary>
    /// The touch test itself against ONE world-space card rectangle, for both hand points.
    ///
    /// <para>WHICH HAND POINTS, AND WHY BOTH. The INDEX TIP is the point that physically arrives
    /// first and the point the fan elections already rank by (<c>tipFirst</c> — "the card nearest
    /// the pointing finger wins"), so a card plucked out of a fan is touched by the tip; it is
    /// tested first for that reason. The PALM CENTRE is the point the GRAB itself is measured from
    /// (<c>ProximityGrabber</c> scores every candidate against <c>Rig.PalmCenter</c>), so it is what
    /// "the hand is on the card" means for a card taken with a whole-hand grip off the board dock or
    /// the active column, where the fingers curl PAST the small card and the tip can be beyond its
    /// far edge while the palm is flat on it. Requiring both would refuse every one of those grips;
    /// requiring only the tip would refuse them too. Either point inside the slab is a genuine
    /// physical overlap — and it is the palm that carries the controller, i.e. the beam origin the
    /// whole stand-down is about.</para>
    ///
    /// Fills <paramref name="geo"/> only on an accept. No allocations, no square roots.
    /// </summary>
    private static bool TryHandContactRect(VRHand hand, Vector3 center, Vector3 normal, Vector3 right,
        Vector3 up, float halfW, float halfH, ref ContactGeometry geo)
    {
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        float limit = ContactSlabHalfDepthMeters * scale;
        if (TryProbeRect(hand.Rig.IndexTip.position, center, normal, right, up, halfW, halfH, limit,
                out float depth, out float margin))
        {
            geo = new ContactGeometry
            {
                Probe = "index tip", Depth = depth, Margin = margin, Limit = limit, Scale = scale,
            };
            return true;
        }
        if (TryProbeRect(hand.Rig.PalmCenter.position, center, normal, right, up, halfW, halfH, limit,
                out depth, out margin))
        {
            geo = new ContactGeometry
            {
                Probe = "palm", Depth = depth, Margin = margin, Limit = limit, Scale = scale,
            };
            return true;
        }
        return false;
    }

    /// <summary>One point against one rect: project onto the card's plane, require the projection
    /// inside the face (plus <see cref="ContactRectMargin"/>) and the signed plane distance inside
    /// <paramref name="depthLimit"/>. <paramref name="depth"/> and <paramref name="margin"/> are the
    /// two numbers the log prints.</summary>
    private static bool TryProbeRect(Vector3 point, Vector3 center, Vector3 normal, Vector3 right,
        Vector3 up, float halfW, float halfH, float depthLimit, out float depth, out float margin)
    {
        Vector3 rel = point - center;
        depth = Vector3.Dot(rel, normal);
        margin = Mathf.Min(halfW * ContactRectMargin - Mathf.Abs(Vector3.Dot(rel, right)),
                           halfH * ContactRectMargin - Mathf.Abs(Vector3.Dot(rel, up)));
        return margin >= 0f && depth >= -depthLimit && depth <= depthLimit;
    }

    // ------------------------------------------------------------------ board laser --

    private IPokeable? _boardHover;
    private VRHand? _boardHoverHand;
    private VRCard? _trayCardHover;

    /// <summary>Distance along the aim ray to whatever the board laser hovered this frame
    /// (<see cref="_boardHover"/> or <see cref="_trayCardHover"/>), +inf while it hovers nothing.
    /// The board hover used to be a BLUNT VETO over the two board-anchored fans - "the board
    /// hovered something, therefore the browse arc gets no frame" - with no regard for which is in
    /// FRONT. Recording the distance turns that into the same nearest-wins arbitration the fans
    /// already run against game uGUI, so a card the beam is genuinely on can take the frame back
    /// from the board slab behind it.</summary>
    private float _boardHoverDist = float.PositiveInfinity;

    /// <summary>Frame on which the board laser actually SPENT a trigger pull (element press /
    /// slot-card pluck). A fan that takes the hover back afterwards must not spend the SAME pull a
    /// second time, so the pluck (never the hover) yields for that one frame.</summary>
    private int _boardConsumedTriggerFrame = -1;

    /// <summary>Tolerance on the fan-occlusion test, real metres at rig scale 1 - the same value
    /// and the same sense as <c>RayInteractor.FanOcclusionEpsilonMeters</c>, so the board laser
    /// draws the "behind an open fan" line in exactly the place the physics pick, RayUguiDriver
    /// and RayGrabDriver already draw it.</summary>
    private const float FanOcclusionSlackMeters = 0.005f;

    /// <summary>
    /// Grazing-angle accept margin for the lift-priority hit test (mirrors CardFan.TryRaycast's
    /// own margin). A card met at a shallow angle subtends almost nothing, so a few mm of
    /// controller jitter is the difference between "dead centre" and "off the edge"; widening
    /// the rect ~10 % cannot pick a different card here (the branch already knows WHICH card —
    /// the palm-highlighted one — and only asks WHETHER the beam is on it).
    /// </summary>
    private const float LiftHitMargin = 1.10f;

    /// <summary>
    /// Does the ray cross <paramref name="card"/>'s face, and where? Tests the card WHERE IT
    /// VISIBLY IS (live rect, pop included) and falls back to the resting rect, accepting
    /// either.
    ///
    /// The live rect is the important one and used to be missing. This branch fires because the
    /// PALM highlight lifted the card, so by the time it runs the card is visibly raised toward
    /// the player — testing only the RESTING rect (which exists to break the laser-driven pop
    /// feedback loop, a loop this branch does not have) asked whether the beam crosses a
    /// rectangle the card has already left. At a flat angle that gap projects far along the view
    /// direction, so the test failed while the reticle sat dead centre on the card: "the laser
    /// goes straight through the card". Accepting BOTH poses keeps the card hittable throughout
    /// the raise animation, when neither pose alone covers it.
    /// </summary>
    private static bool TryHitLiftedCard(VRCard card, Vector3 origin, Vector3 direction,
        out Vector3 point)
    {
        if (card.TryGetLiveLaserRect(out Vector3 c, out Vector3 n, out Vector3 r, out Vector3 u,
                out float hw, out float hh)
            && TryHitRect(c, n, r, u, hw, hh, origin, direction, out point))
            return true;
        if (card.TryGetRestingLaserRect(out c, out n, out r, out u, out hw, out hh)
            && TryHitRect(c, n, r, u, hw, hh, origin, direction, out point))
            return true;
        point = default;
        return false;
    }

    /// <summary>Ray/rect intersection in world meters (same math as CardFan.TryRaycast).</summary>
    private static bool TryHitRect(Vector3 center, Vector3 normal, Vector3 right, Vector3 up,
        float halfW, float halfH, Vector3 origin, Vector3 direction, out Vector3 point)
    {
        point = default;
        float denom = Vector3.Dot(direction, normal); // cards face the viewer with −Z
        if (denom < 1e-5f)
            return false;
        float dist = Vector3.Dot(center - origin, normal) / denom;
        if (dist <= 0f)
            return false;
        Vector3 hit = origin + direction * dist;
        Vector3 rel = hit - center;
        if (Mathf.Abs(Vector3.Dot(rel, right)) > halfW * LiftHitMargin
            || Mathf.Abs(Vector3.Dot(rel, up)) > halfH * LiftHitMargin)
            return false;
        point = hit;
        return true;
    }

    // Throttle for the lift-priority diagnostic below (shared; the branch runs per frame).
    private static float s_nextLiftPriorityLogAt;
    private static bool s_lastLiftOnCard;

    /// <summary>
    /// Diagnostic for the lift-priority branch — the path that produced the phantom
    /// "orthogonal wall through the middle of the card". Logs when the branch owns the frame
    /// and whether the beam is clamped to a REAL hit on the card or merely left alone, so a
    /// hardware log shows the branch engaging (it is silent otherwise: it only ever logged on
    /// an actual trigger-grab) and proves the beam is no longer parked on the card centre.
    /// Throttled, plus an immediate line whenever the on-card state flips.
    /// </summary>
    private static void LogLiftPriority(VRCard lifted, bool onCard)
    {
        float now = Time.unscaledTime;
        if (onCard == s_lastLiftOnCard && now < s_nextLiftPriorityLogAt)
            return;
        s_lastLiftOnCard = onCard;
        s_nextLiftPriorityLogAt = now + 2f;
        VRLog.Debug("Cards", $"Board laser: LIFT-PRIORITY owns the trigger for '{lifted.name}' " +
            $"(palm highlight on a docked card) — {(onCard ? "ray IS on the card: beam clamped to the real hit, frame pre-empted" : "FALLBACK: ray hit nothing at all, beam left free, far-click suppressed only")}. " +
            "Board elements the beam actually lands on are never starved (they are scanned first).");
    }

    /// <summary>
    /// P7 (test #10): laser support for every control-board element — the dominant
    /// hand's ray is tested geometrically against the tray's registered pokeables
    /// (Collider.Raycast works on triggers, no physics-layer coupling) and against
    /// the two slotted cards. Hover clamps the beam (UiHitOverride, which also
    /// suppresses the board far-click); TriggerDown pokes the element or plucks the
    /// card into the hand. Fan laser wins when both apply. No allocations.
    /// </summary>
    private void UpdateBoardLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_tray.IsVisible || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Active || dom.Grabber.Held != null || _laserHover != null)
        {
            ClearBoardHover();
            return;
        }

        // ---- THE OPEN FAN IS IN FRONT OF THE BOARD (root cause, hardware rounds 1-4) ----------
        //
        // ROOT CAUSE of "der Laser geht an manchen Kopfpositionen einfach durch die Karte" - and
        // the reason four rounds of tolerance work could not move it, because the tolerance was
        // never consulted on the failing frames AT ALL.
        //
        // The discard/burnt browse arc and the item fan float ABOVE the control board
        // (PileBrowser.BoardAnchorBase = board top + 0.26 m, 0.05 m proud), and the WHOLE board
        // mesh is a registered laser target (PlayTray.1.Core: every MeshCollider under the board
        // visual gets a BoardSurfaceTarget - the object the hardware log calls
        // "Board: laser click -> Board"), as are the pile stacks, CONFIRM/UNDO and the rest discs.
        // So a beam that passes through a browse card and CARRIES ON lands on the board behind it.
        // This method ran BEFORE UpdateBrowseLaser (CardsDriver.TickInteractionsAndStatus), had no
        // occlusion test of any kind, and therefore:
        //   1. hovered the board element BEHIND the card and
        //   2. clamped the beam onto it (UiHitOverride = bestPoint) - the beam is drawn straight
        //      THROUGH the card and ends on the board, which is literally the reported symptom, and
        //   3. left _boardHover non-null, whereupon UpdateBrowseLaser's guard returned IMMEDIATELY
        //      and SILENTLY: no pick, no hover, no log line. That silence is why the hardware log
        //      contains only ~14 browse-laser lines and why every one of them looks like an
        //      innocent near miss - the frames that actually failed never reached the diagnostic.
        // The head-position dependence follows directly: the arc BILLBOARDS TO THE HEAD, so the
        // head alone decides how the cards are tilted over the board and therefore whether the
        // continuation of a beam that grazes a card still lands on board geometry. Looking down
        // the beam continues onto the slab (arc dead); looking up from below it sails over the
        // board and hits nothing (arc alive) - exactly the earlier "von unten super, von oben kaum"
        // report, and exactly the "head position must have NO influence" requirement being violated.
        //
        // THE FIX is not a new rule: it is this method finally honouring the arbitration seam every
        // OTHER consumer of the beam already honours. RayInteractor.ComputeFanOccluder measures the
        // nearest hit across all three fans (hand fan, browse arc, item fan) every frame and
        // publishes it as Ray.FanOccluderDistance; the physics pick, RayUguiDriver and RayGrabDriver
        // all reject targets behind it. The board laser was the one hold-out. Anything farther than
        // the nearest fan card is BEHIND a card the player can see, so it may neither hover nor
        // clamp the beam. Deliberately the EXACT-rect occluder (allowNearMiss:false), so a card the
        // beam only near-misses still lets the board through - that residue is arbitrated by
        // distance in UpdateBrowseLaser / UpdateItemFanLaser instead.
        float fanOccluder = dom.Ray.FanOccluderDistance;
        float occluderSlack = fanOccluder + FanOcclusionSlackMeters * dom.WorldScale;

        // Task #2 (tray-card grab radius) — LIFT-PRIORITY accept. The proximity highlight
        // (ProximityGrabber, 0.13 m palm reach off the card collider) is exactly what
        // hover-LIFTS a slotted card, so the accept volume for the trigger-grab is the
        // hover-lift radius itself — the lift IS the affordance promise. Without this,
        // the grab often failed even though the card was visibly lifted: the trigger is
        // shared with the laser click, and the beam near-missing onto a board element
        // (CONFIRM/UNDO/rest/pile — grab attempts point INTO the board by nature) or
        // onto the OTHER slotted card swallowed the pull as an element press / wrong-
        // card pluck (ProximityGrabber defers on Ray.HasFreshUiHit). While the free
        // hand's highlight IS a tray-docked card: suppress the board hover/click for
        // the frame, clamp the beam onto the lifted card (telegraphs the winner), and
        // grab exactly that card on TriggerDown. Single-winner by construction —
        // Highlighted is unique per hand and only a SLOTTED card takes this path; fan
        // cards, figures and non-lifted cards are untouched. Yields to a live game-UI
        // hit (RayUgui) like every path below so a docked game-widget click can never
        // double-fire with a grab.
        // Task #2 follow-up: pick/field cards docked in a slot recess (LoseCard/recover
        // flows) take the same lift-priority accept as the two played-slot occupants —
        // they live in the same recesses, carry the same dock grab apron, and suffered
        // the same trigger fall-through to board actions.
        // ROOT CAUSE of the "invisible wall through the middle of the card" (hardware rounds
        // 5-7; the collider hunts were chasing the wrong object entirely). This branch used to
        // publish `Ray.UiHitOverride = lifted.transform.position` — the card's CENTRE. The beam
        // does not end AT an override point, it ends at that point's PROJECTION onto the aim ray
        // (RayInteractor.UpdateVisuals, deliberately, so the beam can never bend). Feeding it a
        // centre therefore parks the reticle on the plane through the card centre PERPENDICULAR
        // TO THE BEAM — a phantom wall standing across the ray at the card's midline, which the
        // beam "hits" no matter where it actually points, while this branch's `return` keeps the
        // card itself from ever registering a hover. Exactly the reported symptom, right down to
        // being worst at flat angles: pointing along the board brings the controller close enough
        // to a docked card for ProximityGrabber's palm highlight to engage, which is this
        // branch's trigger.
        //
        // The branch's real job is the trigger, not the beam: own the pull so it grabs the lifted
        // card instead of a near-missed board element. So clamp the beam ONLY when the ray truly
        // crosses the card (then the point is honest and telegraphs the winner).
        //
        // FOLLOW-UP (buttons died next to a lifted card): owning the frame whenever a docked card
        // is palm-highlighted — beam on the card or not — starves every board element. The tester
        // hit exactly that: after grazing a raised card, sweeping the SAME flat beam onto the
        // board buttons passed straight through them, because the palm was still inside the
        // card's grab volume and this branch kept returning before the element scan. A near-miss
        // deserves the card; a beam sitting squarely ON a button is a deliberate aim and must win.
        // So the lifted card only PRE-EMPTS the frame when the ray is genuinely on it; otherwise
        // the normal scan runs, and the lift-priority accept applies further down as a FALLBACK,
        // reached only when the ray found nothing else at all.
        VRCard? lifted = dom.Grabber.Highlighted is VRCard hl
                         && (_tray.ContainsCard(hl) || _fieldCards.Contains(hl))
                         && !dom.RayUgui.HasHit
            ? hl
            : null;
        if (lifted != null && TryHitLiftedCard(lifted, dom.Ray.Current.Origin,
                dom.Ray.Current.Direction, out Vector3 liftedHit)
            && Vector3.Dot(liftedHit - dom.Ray.Current.Origin, dom.Ray.Current.Direction) <= occluderSlack)
        {
            ClearBoardHover();
            dom.Ray.UiHitOverride = liftedHit; // honest hit — beam lands ON the card
            LogLiftPriority(lifted, onCard: true);
            if (dom.TriggerDown && lifted.CanGrab)
            {
                VRLog.Info("Cards", "Board: hover-LIFTED slot card trigger-grabbed " +
                                    "(lift-priority accept — beam near-miss suppressed).");
                dom.Grabber.ForceGrab(lifted, releaseOnTriggerUp: true);
            }
            return;
        }

        PickPose pick = dom.Ray.Current;
        var ray = new Ray(pick.Origin, pick.Direction);
        // Clamped by the fan occluder (see the block above): board geometry behind an open fan's
        // card is not reachable, so the scan simply never looks past it.
        float maxDist = Mathf.Min(3f * dom.WorldScale, occluderSlack);

        IPokeable? best = null;
        Vector3 bestPoint = default;
        float bestDist = maxDist;
        var targets = _tray.LaserTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            Collider col = targets[i].Collider;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                continue;
            if (col.Raycast(ray, out RaycastHit hit, bestDist))
            {
                best = targets[i].Target;
                bestPoint = hit.point;
                bestDist = hit.distance;
            }
        }

        // Slotted cards: pluck them back with the laser, like fan cards. Same fan occlusion as the
        // element scan above - a slot card seen THROUGH an open browse/item fan is not pickable.
        bool cardWins = _tray.TryRaycastCards(pick.Origin, pick.Direction,
            out VRCard? card, out Vector3 cardPoint, out float cardDist)
                        && cardDist < bestDist && cardDist <= occluderSlack;

        // The game's own UI (RayUgui) closer than everything → neither hovers.
        float nearest = cardWins ? cardDist : best != null ? bestDist : float.PositiveInfinity;
        if (float.IsPositiveInfinity(nearest) || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < nearest))
        {
            ClearBoardHover();

            // LIFT-PRIORITY FALLBACK (see the branch above): the ray found NOTHING — no board
            // element, no card, no game UI — while the palm holds a docked card lifted. This is
            // the case the accept was written for: a grab reach whose beam sails past the board
            // into empty space, where letting the trigger through would fire a board far-click
            // instead of the obviously-intended grab. Claim the trigger without touching the beam
            // (SuppressFarClick, never a fabricated hit point) and grab the lifted card. Because
            // this now sits AFTER the scan, a beam that is actually on a button reaches the button.
            if (lifted != null)
            {
                dom.Ray.SuppressFarClick();
                LogLiftPriority(lifted, onCard: false);
                if (dom.TriggerDown && lifted.CanGrab)
                {
                    // TAKE-BACK RESTORED (user report 2026-08-04: "die hingelegte Karte zum
                    // Verbrennen nicht mehr greifen"). The EVENT-DISCARD DEADLOCK FIX
                    // (b7bfb38, MP remote Player.log:11706ff) used to SUPPRESS this grab for a
                    // placed pick card while the pick confirm dialog was open, so a nothing-hit
                    // trigger pull could not cancel-loop the dialog. That guard over-reached:
                    // SuppressFarClick() above raises Ray.HasFreshUiHit, which ALSO makes
                    // ProximityGrabber's trigger path defer (ProximityGrabber.Tick,
                    // "!Ray.HasFreshUiHit"), and the promised "grip it to swap" alternative did
                    // not exist with [Cards] GrabButton=Trigger — so while the dialog was open
                    // there was NO working grab route at all unless the beam happened to lie
                    // exactly on the card rect (hardware log 2026-08-04: five "FALLBACK grab
                    // SUPPRESSED" lines, zero successful grabs). The deadlock the guard fixed
                    // is now solved STRUCTURALLY instead:
                    //   - the dock no longer shows the dialog's cancel option at all
                    //     (DecisionDockSurface suppresses GUI_CHOOSE_OTHER_CARD); grabbing
                    //     the card IS that action (MaybeReopenPickSelection), per user ruling
                    //     2026-08-04 — so a reach toward the dock only ever aims at COMMIT,
                    //   - the tray CONFIRM presses the dialog's commit option (b7bfb38's other
                    //     half, unchanged), so a commit affordance always exists and an
                    //     accidental grab is recoverable by simply re-placing the card.
                    // A palm-highlighted card visibly LIFTS — the lift is the grab promise this
                    // branch exists to honour; refusing the pull under a lifted card was the
                    // exact task-#2 bug all over again.
                    VRLog.Info("Cards", "Board: hover-LIFTED slot card trigger-grabbed " +
                                        "(lift-priority FALLBACK — ray hit nothing at all).");
                    dom.Grabber.ForceGrab(lifted, releaseOnTriggerUp: true);
                }
                return;
            }

            // Requirement 4 click-away for the ITEM fan lives in UpdateItemFanLaser now — the item
            // chips left this method's collider scan when their laser pick was unified with the
            // pile-browse fan (geometric + sticky; see ItemsPile.TryLaserRaycast for why the scan
            // skipped every second chip on right→left sweeps). A trigger that misses everything
            // here falls through to that path, which owns the item fan's hover, pluck AND dismiss.
            return;
        }

        if (cardWins)
        {
            ClearBoardPokeHover();
            // USER BUG B (rooted cards): a slot-docked card that is NOT grabbable in the
            // current phase (locked selection / committed round cards — CanGrab false)
            // must produce ZERO pop, ZERO scale change, ZERO haptic on laser hover: the
            // hover-pop is a GRAB affordance promise, and popping a card that refuses the
            // grab set up the pop↔drop oscillation (constant buzz + pulse). The beam
            // still clamps to the card so the trigger can never fall through to a board
            // click BEHIND it; action taps on a card face route through the registered
            // face canvas (RayUgui), which already outranks this path when closer.
            if (!card!.CanGrab)
            {
                ClearTrayCardHover();
                dom.Ray.UiHitOverride = cardPoint;
                return;
            }
            if (!ReferenceEquals(card, _trayCardHover))
            {
                ClearTrayCardHover();
                _trayCardHover = card;
                card.SetLaserHover(true);
                dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on change
            }
            dom.Ray.UiHitOverride = cardPoint;
            _boardHoverDist = cardDist; // nearest-wins arbitration with the board-anchored fans
            if (dom.TriggerDown && !HandFanOwnsTrigger(dom))
            {
                VRCard grab = card;
                ClearTrayCardHover();
                VRLog.Info("Cards", "Board: slotted card laser-plucked.");
                _boardConsumedTriggerFrame = Time.frameCount;
                dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
            }
            return;
        }

        ClearTrayCardHover();
        if (!ReferenceEquals(best, _boardHover))
        {
            ClearBoardPokeHover();
            _boardHover = best;
            _boardHoverHand = dom;
            best!.OnPokeEnter(dom); // elements do their own hover haptic/tint
        }
        dom.Ray.UiHitOverride = bestPoint;
        _boardHoverDist = bestDist; // nearest-wins arbitration with the board-anchored fans
        // HAND-FAN OWNERSHIP: a hand physically holding up a browse card / item chip already owns
        // this pull (see UpdateBoardFanHandTrigger — it ran first and normally grabbed, which this
        // path's Grabber.Held guard would have caught; this covers the frame where the grab itself
        // was refused). Hover, tint and beam clamp above stay live; only the PRESS yields.
        if (dom.TriggerDown && !HandFanOwnsTrigger(dom))
        {
            // COMMIT gate (laser ruling 2026-08): hover/tint/beam-clamp above stay live under
            // a blocking modal, but element presses COMMIT (tray buttons call game APIs
            // directly, bypassing the 2D overlay raycast blocker vanilla relies on — END
            // TURN / item use are non-undoable). Decision table:
            // WorldUI.ModalFallback.HardCommitLockActive.
            if (_modalInputBlocked)
            {
                // Throttled + culprit-naming (user report 2026-08-02): the host's log carried 20+
                // identical "SUPPRESSED — blocking modal open" lines that named neither the window
                // nor the fact that it never released. One line per second, and it says WHICH
                // window is holding the gate (DescribeBlockingWindows allocates — hence the gate).
                if (Time.unscaledTime >= _nextPressSuppressLogAt)
                {
                    _nextPressSuppressLogAt = Time.unscaledTime + 1f;
                    VRLog.Info("Cards", $"Board: laser press on '{(best as MonoBehaviour)?.name ?? best!.ToString()}' " +
                                        "SUPPRESSED — blocking modal open (commit gate; hover stays live). " +
                                        $"Blocking window(s): {WorldUI.ModalFallback.DescribeBlockingWindows()}.");
                }
                return;
            }
            // The pull is spent HERE - a fan that takes the hover back this frame (nearest-wins,
            // see _boardConsumedTriggerFrame) must not spend it a second time.
            _boardConsumedTriggerFrame = Time.frameCount;
            // Route through Press for buttons so the log carries source=laser and
            // rejected presses explain their gate (test #14); other pokeables (badge,
            // rest tokens) keep the plain OnPoke path.
            if (best is PlayTray.BoardButton button)
            {
                // Item 8: any board button press is a foreign interaction (the Cards
                // events cover CONFIRM/UNDO/rest via their handlers regardless of
                // input modality; this also catches board buttons with no Cards event,
                // e.g. settings/recenter, on the laser path). Pile stacks are NOT
                // BoardButtons — they route through OnPoke below and manage the browse.
                // Req #6 EXEMPTION: the item-use CONFIRM button is PART of the item interaction, so it
                // must NOT close the item fan (a foreign close would drop the pending chip before the
                // confirm callback runs). The finger-poke path never routes ForeignInteraction anyway.
                if (!_tray.IsItemUseConfirm(button))
                    ForeignInteraction("board button");
                button.Press(dom, "laser");
            }
            else if (best is PileViewer.PileStack pile)
            {
                // Pile stacks: dedicated laser path — OnPoke now carries the finger's
                // entry-only re-arm gate (double-trigger fix), which must never block a
                // deliberate second laser click while the beam rests on the stack.
                pile.LaserToggle(dom);
            }
            else
            {
                VRLog.Info("Cards", $"Board: laser click → {(best as MonoBehaviour)?.name ?? best!.ToString()}.");
                best!.OnPoke(dom);
            }
        }
    }

    private void ClearBoardHover()
    {
        ClearBoardPokeHover();
        ClearTrayCardHover();
    }

    private void ClearBoardPokeHover()
    {
        _boardHoverDist = float.PositiveInfinity;
        if (_boardHover == null)
            return;
        if (_boardHoverHand != null)
            _boardHover.OnPokeExit(_boardHoverHand);
        _boardHover = null;
        _boardHoverHand = null;
    }

    private void ClearTrayCardHover()
    {
        _boardHoverDist = float.PositiveInfinity;
        if (_trayCardHover == null)
            return;
        _trayCardHover.SetLaserHover(false);
        _trayCardHover = null;
    }

    /// <summary>
    /// Nearest-wins arbitration between a board-anchored FAN (browse arc / item fan) and whatever
    /// the board laser hovered earlier in the same frame.
    ///
    /// <para>The two fan laser paths used to open with <c>_boardHover != null</c> in their guard -
    /// a blunt veto that asked WHETHER the board hovered anything, never WHICH IS IN FRONT. Since
    /// both fans float above the board and the entire board slab is a laser target, the beam that
    /// lands on a fan card almost always continues onto board geometry, so the veto fired on
    /// exactly the frames the fan should have won, silently. See the root-cause block in
    /// <see cref="UpdateBoardLaser"/>.</para>
    ///
    /// Returns true when the fan hit at <paramref name="fanDistance"/> is genuinely nearer and the
    /// board hover has been handed back (hover, tint and beam clamp released); false when the board
    /// really is in front and owns the frame.
    /// </summary>
    private bool TryTakeFrameFromBoardHover(float fanDistance)
    {
        if (_boardHover == null && _trayCardHover == null)
            return true;
        if (_boardHoverDist <= fanDistance)
            return false;
        ClearBoardHover();
        return true;
    }

    /// <summary>True while the board laser has already spent THIS frame's trigger pull (see
    /// <see cref="_boardConsumedTriggerFrame"/>) - a fan that took the hover back may still hover,
    /// but must not also pluck with the same pull.</summary>
    private bool BoardAlreadySpentTrigger => Time.frameCount == _boardConsumedTriggerFrame;

    // -------------------------------------------- board-fan hand ownership (browse/items) --

    /// <summary>The hand whose trigger the browse arc / item fan claimed this frame, and the frame
    /// it was claimed on (see <see cref="UpdateBoardFanHandTrigger"/>).</summary>
    private VRHand? _handFanTriggerHand;
    private int _handFanTriggerFrame = -1;

    /// <summary>True while a board-anchored fan owns <paramref name="hand"/>'s trigger this frame —
    /// the board laser then hovers and clamps as usual but must not PRESS with that pull.</summary>
    private bool HandFanOwnsTrigger(VRHand hand) =>
        ReferenceEquals(hand, _handFanTriggerHand) && Time.frameCount == _handFanTriggerFrame;

    /// <summary>
    /// "What lights up is what you get", for the two BOARD-ANCHORED fans: while a hand is
    /// physically in contact with a card in the discard/burnt browse arc or a chip in the item fan,
    /// that hand's trigger belongs to THAT card — for BOTH hands.
    ///
    /// ROOT CAUSE (user report 2026-08-03: "physisch reagiert die Karte im Fächer zwar und wird
    /// gehighlighted — ich kann sie aber nicht mit Trigger aufnehmen"). Highlight and press were
    /// resolved by two different systems that could not agree:
    ///  • the HIGHLIGHT comes from the fan's own hand sweep (<see cref="PileBrowser.HandOwnedCard"/> /
    ///    <see cref="ItemsPile.HandOwnedChip"/>), which measures the fingertip against the card;
    ///  • the PRESS comes from <see cref="ProximityGrabber"/>, which explicitly DEFERS the trigger
    ///    whenever <c>Ray.HasFreshUiHit</c> is set — and both fans float ABOVE the control board, so
    ///    the beam behind a hand reaching into them lands on the board, where
    ///    <see cref="UpdateBoardLaser"/> clamps it and consumes the pull as a board click.
    /// The 2026-08-03 hardware log shows exactly that: three <c>Board: laser click → Board</c> lines
    /// interleaved with <c>Pile-browse hand sweep (Right): WINNER …</c>, and not one pluck. The
    /// press was never refused — it was spent somewhere else, silently.
    ///
    /// Runs BEFORE <see cref="UpdateBoardLaser"/>, so the grab happens first and the board laser's
    /// own <c>Grabber.Held != null</c> guard then skips the frame; <see cref="HandFanOwnsTrigger"/>
    /// additionally gates the board PRESS for the rare frame where the grab itself was refused.
    /// Yields to a live game-UI hit (RayUgui) exactly like the tray's lift-priority accept, so a UI
    /// click can never double-fire with a grab. Allocation-free; no-op when neither fan is open.
    /// </summary>
    private void UpdateBoardFanHandTrigger()
    {
        bool browseOpen = _browser.IsOpen;
        bool itemsOpen = _piles.ItemsBrowseOpen;
        if (!browseOpen && !itemsOpen)
            return;

        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
                continue;
            if (hand.RayUgui.HasHit)
                continue; // genuinely nearer game UI wins the trigger, as everywhere else

            VRCard? card = browseOpen ? _browser.HandOwnedCard(hand) : null;
            if (card != null)
            {
                ClaimHandFanTrigger(hand);
                if (hand.TriggerDown && card.CanGrab)
                {
                    VRLog.Info("Cards", $"Pile-browse: HAND owns the trigger — '{card.name}' is " +
                                        $"physically in contact with the {hand.Side} hand, so the pull " +
                                        "takes it instead of falling through to the board laser " +
                                        "(what is lifted is what gets grabbed).");
                    hand.Grabber.ForceGrab(card, releaseOnTriggerUp: true);
                }
                continue;
            }

            ItemsPile.ItemChip? chip = itemsOpen ? _piles.HandOwnedItemChip(hand) : null;
            if (chip == null)
                continue;
            ClaimHandFanTrigger(hand);
            // COMMIT gate (laser ruling 2026-08): a chip pluck is a commit (chip → use slot spends
            // the item), so it stays refused under a blocking modal — the browse arc's read-only
            // pluck does not.
            if (hand.TriggerDown && !_modalInputBlocked)
            {
                VRLog.Info("Cards", $"Item fan: HAND owns the trigger — '{chip.name}' is physically " +
                                    $"in contact with the {hand.Side} hand, so the pull takes it " +
                                    "instead of falling through to the board laser.");
                chip.OnPoke(hand); // pluck into the hand (ForceGrab, released on trigger-up)
            }
        }
    }

    /// <summary>Claim <paramref name="hand"/>'s trigger for a board-anchored fan this frame. The
    /// far-click suppression is only published for a hand that actually HAS a beam: raising
    /// <c>HasFreshUiHit</c> on the non-dominant hand would make its own ProximityGrabber defer the
    /// very trigger this method exists to deliver.</summary>
    private void ClaimHandFanTrigger(VRHand hand)
    {
        _handFanTriggerHand = hand;
        _handFanTriggerFrame = Time.frameCount;
        // ...and only for a hand whose beam is still LIVE. A hand that is in a card has already
        // stood its laser down (UpdateLaserContactStandDown — which is exactly the situation this
        // method fires in), so there is no far click left to suppress; re-raising HasFreshUiHit
        // would only re-arm the "the laser owns this pull" flag the stand-down just dropped, and
        // that flag makes this hand's OWN ProximityGrabber defer the very trigger being claimed.
        if (hand.Ray.Enabled && !hand.Ray.CardContactStandDown)
            hand.Ray.SuppressFarClick();
    }

    // ------------------------------------------------------------------ browse laser --

    private VRCard? _browseHover;

    /// <summary>Pull-jerk grace for the BROWSE arc (the exact T2 mechanism the ability fan
    /// (<see cref="_laserHoverGraceUntil"/>) and the item fan (<see cref="_itemChipGraceChip"/>)
    /// already carry): the card the beam last hovered plus the unscaled deadline until which it
    /// still owns a trigger whose ray slipped off the card on the press frame. Without it that pull
    /// landed in the miss branch below and CLOSED the pile via the click-away instead of taking the
    /// card the player was visibly promised.</summary>
    private VRCard? _browseGraceCard;
    private float _browseGraceUntil;

    /// <summary>
    /// Item 5 (laser-selectable pile browse): the dominant hand's ray highlights an
    /// open browse arc's cards and TriggerDown plucks the pointed card into the hand
    /// to read it close (released on TriggerUp → returns to the arc, no game state).
    /// Yields to the fan laser and the board laser — those are real interactions; the
    /// browse is a passive read layered on top. Mirrors <see cref="UpdateFanLaser"/>.
    /// </summary>
    private void UpdateBrowseLaser()
    {
        VRHand? dom = VRHands.Primary;
        // _boardHover / _trayCardHover are DELIBERATELY NOT in this guard any more - they were the
        // silent veto that killed the arc on every frame the beam also crossed the board slab
        // behind it (root cause, see UpdateBoardLaser). The board is now arbitrated BY DISTANCE
        // below, after the pick, so the log always says which one was in front.
        if (!_browser.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Active || dom.Grabber.Held != null
            || _laserHover != null)
        {
            if (_browser.IsOpen && dom != null)
                LogBrowseStarved(dom);
            ClearBrowseHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        // allowNearMiss: the interaction path gets the angular near-miss rescue (a browse card on
        // a shrunken board subtends less than the controller's aim jitter); the fan occluder does
        // not — see FanSweep.LaserMinHalfAngleDegrees.
        bool browseHit = _browser.TryRaycast(pick.Origin, pick.Direction, _browseHover,
            out VRCard? card, out Vector3 point, out float dist, allowNearMiss: true);
        bool browseUiInFront = browseHit && dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist;
        // The board laser ran earlier this frame. It only keeps the frame if what it hovered is
        // genuinely NEARER than this card; otherwise it hands the hover, tint and beam clamp back.
        bool boardInFront = browseHit && !browseUiInFront
                            && !TryTakeFrameFromBoardHover(dist);
        // After the steal above, "does the board STILL hold a hover" is the whole question: it is
        // false exactly when the arc won it back. A miss (or a nearer uGUI hit) never steals, so the
        // board keeps the frame there - preserving the old guard's contract that a board-hovering
        // trigger is a board press, never a click-away dismiss of the pile.
        bool boardOwnsFrame = _boardHover != null || _trayCardHover != null;
        if (!browseHit || card == null || browseUiInFront || boardInFront)
        {
            LogBrowseLaser(dom, browseUiInFront
                ? "not delivered — nearer game UI is in front of the arc"
                : boardOwnsFrame
                    ? "not delivered - a NEARER board element is in front of the arc"
                    : "not delivered - the ray is not on any browse card");
            ClearBrowseHover();
            if (boardOwnsFrame)
                return; // the board owns hover AND trigger this frame - no click-away, no grace
            // T2 pull-jerk grace, browse edition: the trigger pull jerks the aim ray off the card
            // on the very press frame, so this miss branch is exactly where a "take that card" pull
            // used to land — and it then CLOSED the pile via the click-away below instead of taking
            // the card the beam had been sitting on. For a short window after a genuine hover the
            // last-hovered card still owns the trigger: claim the frame (SuppressFarClick — never a
            // fabricated beam point, same reasoning as the ability fan's rescue) and pluck it.
            if (!dom.RayUgui.HasHit && _browseGraceCard != null && !_browseGraceCard.IsHeld
                && _browser.Contains(_browseGraceCard) && Time.unscaledTime <= _browseGraceUntil)
            {
                dom.Ray.SuppressFarClick();
                if (dom.TriggerDown && _browseGraceCard.CanGrab)
                {
                    VRCard rescue = _browseGraceCard;
                    _browseGraceCard = null;
                    LogBrowseLaser(dom, $"DELIVERED (pull-jerk grace) — the trigger came down with the " +
                                        $"beam just off '{rescue.name}'; taking it instead of dismissing the fan");
                    dom.Grabber.ForceGrab(rescue, releaseOnTriggerUp: true);
                }
                return;
            }
            // ISSUE #7 click-away dismiss: a TRIGGER press that is NOT on a browse card —
            // empty space, the game board/UI, or anything the ray misses here — closes the
            // pile, through the SAME ForeignInteraction path board/card/rest presses already
            // use. Reached only when nothing else is hovered (the guard above yields to a
            // hovered fan/board/tray target, whose own grab/press routes ForeignInteraction),
            // so a real interaction still closes it there; and a trigger ONTO a browse card
            // takes the pluck path below instead, so grabbing/inspecting a pile card is unaffected.
            // COMMIT gate (laser ruling 2026-08): a stray trigger under a blocking modal —
            // most likely aimed at the modal and near-missing — must not close the player's
            // open pile either (the browse laser path itself stays live for hover).
            if (dom.TriggerDown && !_modalInputBlocked)
                ForeignInteraction("click-away (trigger off the pile)");
            return;
        }

        // SINGLE-OWNER contract ("what pops is what you grab"): while this hand is physically IN
        // the arc, the HAND owns both the pop and the trigger (UpdateBoardFanHandTrigger already
        // claimed the pull). Its beam is somewhere else at that range — the arc floats above the
        // board — so the card the beam finds and the card the hand lifted are routinely different
        // ones, and honouring the beam here would re-open the very gap this round closes. Same
        // card = no conflict: the beam clamp is the honest one, so the laser keeps it. This is the
        // browse edition of the item fan's yield and of the ability fan's UpdateFanHoverSplit rule.
        VRCard? handOwned = _browser.HandOwnedCard(dom);
        if (handOwned != null && !ReferenceEquals(handOwned, card))
        {
            LogBrowseLaser(dom, $"not delivered — the HAND owns the arc ('{handOwned.name}' is " +
                                "physically in contact); the beam yields so what pops is what is taken");
            ClearBrowseHover();
            _browseGraceCard = null; // no laser promise while the hand owns the arc
            return;
        }

        if (!ReferenceEquals(card, _browseHover))
        {
            ClearBrowseHover();
            _browseHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }
        // Refresh the pull-jerk grace on every hovered frame (see _browseGraceCard).
        _browseGraceCard = card;
        _browseGraceUntil = Time.unscaledTime + FanHoverGraceSeconds;

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        if (dom.TriggerDown && card.CanGrab && !BoardAlreadySpentTrigger)
        {
            VRCard grab = card;
            LogBrowseLaser(dom, $"DELIVERED — trigger plucked '{grab.name}' out of the arc");
            ClearBrowseHover();
            _browseGraceCard = null; // the promise is honoured — no stale grace after the pluck
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
        else
        {
            LogBrowseLaser(dom, !dom.TriggerDown
                ? "no press this frame - hover only"
                : BoardAlreadySpentTrigger
                    ? "not delivered - the board laser already spent this pull (hover taken back, pluck yields one frame)"
                    : "not delivered - the hovered card refuses grabs (CanGrab=false)");
        }
    }

    /// <summary>Throttle clock + last verdict for the browse-fan laser diagnostic.</summary>
    private float _nextBrowseLaserLogAt;
    private string _lastBrowseLaserVerdict = string.Empty;

    // ---- browse-laser STATE diagnostic (the evidence the fifth round is built on) -------------
    //
    // WHY THE OLD ONE WAS NOT ENOUGH. It keyed on the press verdict STRING and then throttled to
    // one line per second, so a beam flickering on and off a card at 90 Hz produced ONE line per
    // second out of ninety decisions - and the frames that failed hardest produced NO line at all,
    // because the board-hover veto returned before the diagnostic (see UpdateBoardLaser's
    // root-cause block). Fourteen lines in a whole session cannot separate "the aim was off" from
    // "the pick never ran". This one keys on the whole DECISION STATE (verdict + card + hit/rescue
    // + rejecting branch) and prints on every CHANGE, so an oscillation prints every flip; it adds
    // a once-a-second heartbeat while the state holds, and a hard session cap so a long game can
    // never turn the log into a flood.

    private string _lastBrowseStateVerdict = string.Empty;
    private string _lastBrowseStateCard = string.Empty;
    private FanSweep.LaserReject _lastBrowseStateReject = FanSweep.LaserReject.None;
    private bool _lastBrowseStateHit;
    private bool _lastBrowseStateRescued;
    private int _browseStateLines;
    private string _lastBrowseStarveReason = string.Empty;

    /// <summary>Hard cap on browse-laser state lines per session - generous enough for several
    /// minutes of deliberate testing, small enough that a forgotten open pile cannot flood.</summary>
    private const int BrowseStateLineCap = 600;

    /// <summary>The browse-arc twin of <see cref="LogItemFanLaser"/>: hit/miss, what, dead-on or
    /// angular-rescued, the card's real size, the world scale, and the press verdict - plus, on
    /// every state CHANGE, the full geometric trace (see <see cref="LogBrowseTrace"/>).</summary>
    private void LogBrowseLaser(VRHand hand, string pressVerdict)
    {
        FanSweep.FanLaserPick pick = _browser.LastLaserPick;
        FanSweep.FanLaserTrace trace = _browser.LastLaserTrace;
        string cardName = pick.Name ?? "none";
        bool stateChanged =
            !string.Equals(pressVerdict, _lastBrowseStateVerdict, System.StringComparison.Ordinal)
            || !string.Equals(cardName, _lastBrowseStateCard, System.StringComparison.Ordinal)
            || pick.Hit != _lastBrowseStateHit
            || pick.Rescued != _lastBrowseStateRescued
            || trace.Reject != _lastBrowseStateReject;
        if (stateChanged)
        {
            _lastBrowseStateVerdict = pressVerdict;
            _lastBrowseStateCard = cardName;
            _lastBrowseStateHit = pick.Hit;
            _lastBrowseStateRescued = pick.Rescued;
            _lastBrowseStateReject = trace.Reject;
            _lastBrowseStarveReason = string.Empty; // a live verdict re-arms the starve line
            LogBrowseTrace(hand, pick, trace, pressVerdict);
        }

        // The original one-line-per-second summary stays exactly as it was (it is what every
        // previous hardware round is indexed against).
        if (string.Equals(pressVerdict, _lastBrowseLaserVerdict, System.StringComparison.Ordinal)
            && Time.unscaledTime < _nextBrowseLaserLogAt)
            return;
        if (!pick.Hit && pick.Distance <= 0f)
            return; // the beam is nowhere near the arc — not news
        _lastBrowseLaserVerdict = pressVerdict;
        _nextBrowseLaserLogAt = Time.unscaledTime + 1f;
        FanSweep.LogLaser("Pile-browse", hand.Side.ToString(), pick, hand.WorldScale, pressVerdict);
    }

    /// <summary>
    /// ONE line carrying the entire geometry of a browse-laser decision: the pick ray (which is the
    /// ray the beam is DRAWN along - RayInteractor.UpdateVisuals uses the same origin/direction in
    /// the same Tick, and Plugin.LaserFingerOrigin defaults OFF, so "the beam is not the ray" is
    /// excluded by construction), the HEAD position, both tested rects, and the crossing point in
    /// CARD-LOCAL coordinates normalised to the card's own half-extents.
    ///
    /// <para>HOW TO READ IT. <c>rest u/v</c> and <c>live u/v</c> are the crossing on the card face:
    /// |u| &lt;= 1 and |v| &lt;= 1 means the beam is ON that pose of the card (1.10 is the accepted
    /// margin), u = +1.3 means 30 % of a half-width past the right edge. If the misses cluster at a
    /// SPECIFIC u/v that moves with the head while the ray barely changes, the target is moving
    /// under the beam; if u/v scatters around the edge, it is aim. If <c>live</c> is on the face
    /// while <c>rest</c> is not (or vice versa) the two-pose union is doing its job and the refusal
    /// came from somewhere else - which the <c>reject</c> field then names.</para>
    /// </summary>
    private void LogBrowseTrace(VRHand hand, in FanSweep.FanLaserPick pick,
        in FanSweep.FanLaserTrace trace, string pressVerdict)
    {
        if (_browseStateLines >= BrowseStateLineCap)
            return;
        _browseStateLines++;
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        Vector3 o = trace.RayOrigin, d = trace.RayDirection, h = trace.HeadPosition;
        string rest = trace.RestValid
            ? $"rest c=({trace.RestCenter.x:F2},{trace.RestCenter.y:F2},{trace.RestCenter.z:F2}) " +
              $"n=({trace.RestNormal.x:F2},{trace.RestNormal.y:F2},{trace.RestNormal.z:F2}) " +
              $"half=({trace.RestHalfW:F3},{trace.RestHalfH:F3}) u/v=({trace.RestU:F2},{trace.RestV:F2}) " +
              $"at {trace.RestDistance / scale * 100f:F1} cm"
            : "rest = <none>";
        string live = trace.LiveValid
            ? $"live c=({trace.LiveCenter.x:F2},{trace.LiveCenter.y:F2},{trace.LiveCenter.z:F2}) " +
              $"n=({trace.LiveNormal.x:F2},{trace.LiveNormal.y:F2},{trace.LiveNormal.z:F2}) " +
              $"half=({trace.LiveHalfW:F3},{trace.LiveHalfH:F3}) u/v=({trace.LiveU:F2},{trace.LiveV:F2}) " +
              $"at {trace.LiveDistance / scale * 100f:F1} cm"
            : "live = <none>";
        VRLog.Info("Cards",
            $"Pile-browse laser STATE ({hand.Side}) #{_browseStateLines}: " +
            $"{(pick.Hit ? (pick.Rescued ? "RESCUED" : "HIT") : "MISS")} '{trace.CardName}' " +
            $"reject={trace.Reject}. ray o=({o.x:F2},{o.y:F2},{o.z:F2}) d=({d.x:F3},{d.y:F3},{d.z:F3}); " +
            $"head=({h.x:F2},{h.y:F2},{h.z:F2}); {rest}; {live}; " +
            $"overshoot {pick.Overshoot / scale * 100f:F2} cm vs pad {pick.Pad / scale * 100f:F2} cm. " +
            $"Press: {pressVerdict}.");
    }

    /// <summary>
    /// The line the browse laser NEVER had: why it did not run at all this frame. Every guard exit
    /// used to be a bare <c>return</c>, so a starved arc looked exactly like an idle one in the log
    /// - which is how a board element hovering THROUGH the arc stayed invisible across four
    /// hardware rounds. Change-keyed on the reason (never per-frame).
    /// </summary>
    private void LogBrowseStarved(VRHand dom)
    {
        string reason =
            dom == _gateHand ? "the dominant hand is the palm-gate hand"
            : !dom.HasPose ? "the dominant hand has no pose"
            : dom.Ray.CardContactStandDown ? "the dominant hand is physically in a card — its laser stands down"
            : !dom.Ray.Enabled ? "the dominant hand's ray is disabled by the mode mask"
            : dom.Grabber.Held != null ? "the dominant hand is holding something"
            : _laserHover != null ? "the ability hand fan owns the beam (_laserHover)"
            : "unknown";
        if (string.Equals(reason, _lastBrowseStarveReason, System.StringComparison.Ordinal))
            return;
        _lastBrowseStarveReason = reason;
        _lastBrowseStateVerdict = string.Empty; // the next live verdict is a state change again
        VRLog.Info("Cards", $"Pile-browse laser ({dom.Side}): NOT EVALUATED - {reason}. " +
                            "The arc is open; the beam simply never reached the pick this frame.");
    }

    private void ClearBrowseHover()
    {
        if (_browseHover == null)
            return;
        _browseHover.SetLaserHover(false);
        _browseHover = null;
    }

    // ------------------------------------------------------------------ item-fan laser --

    /// <summary>The item-fan chip the dominant hand's laser is currently over (null = none) and
    /// the hand that hovered it (for the OnPokeExit on clear).</summary>
    private ItemsPile.ItemChip? _itemChipHover;
    private VRHand? _itemChipHoverHand;

    /// <summary>Pull-jerk grace for the ITEM fan (user round 2 — the trigger pull that was meant
    /// to take a chip landed behind the fan): the chip the beam last hovered plus the unscaled
    /// deadline until which it still owns a trigger whose ray slipped off the chip strip. The
    /// exact T2 mechanism the ability fan already carries (<see cref="_laserHoverGraceUntil"/>);
    /// the item fan only had the per-frame sticky pick, so the pull-jerk frame plucked NOTHING
    /// and fell through to the click-away dismiss (fan closed) while RayUguiDriver — before this
    /// fix's occluder hold — pressed the panel behind. Survives <see cref="ClearItemFanHover"/>
    /// on purpose: the hover is gone the moment the beam slips, the promise is not.</summary>
    private ItemsPile.ItemChip? _itemChipGraceChip;
    private float _itemChipGraceUntil;

    /// <summary>Throttle (unscaled) for the "press SUPPRESSED by a blocking modal" diagnostic —
    /// shared by the board-element and item-chip press gates. A held trigger re-enters those
    /// branches every frame; one named line per second is enough to make the next hardware log
    /// decisive without drowning it (the 2026-08-02 host log had 20+ identical lines).</summary>
    private float _nextPressSuppressLogAt;

    /// <summary>
    /// Laser path for the OPEN item fan — the item twin of <see cref="UpdateBrowseLaser"/>: same
    /// priority slot (yields to the fan/board/tray/browse hovers above it), same geometric pick
    /// with sticky hysteresis, same click-away dismiss. The chips used to sit in
    /// <see cref="UpdateBoardLaser"/>'s generic collider scan instead — nearest LIVE
    /// Collider.Raycast, re-elected every frame with no hysteresis — and because the hover pop
    /// moves and enlarges that very collider, right→left laser sweeps skipped every second chip
    /// (the full mechanism, with the geometry numbers, lives on
    /// <see cref="ItemsPile.TryLaserRaycast"/>). Hover and pluck still route through the chip's
    /// own IPokeable seam (OnPokeEnter = pop + hover haptic, OnPoke = ForceGrab released on
    /// trigger-up), so the affordances are byte-identical to the old path — only the PICK moved
    /// to the mechanism every other fan already uses. No allocations.
    /// </summary>
    private void UpdateItemFanLaser()
    {
        VRHand? dom = VRHands.Primary;
        // _boardHover / _trayCardHover left the guard for the same reason they left
        // UpdateBrowseLaser's: the item fan also floats ABOVE the board, so "the board hovered
        // something" was never evidence that the board is in FRONT. Arbitrated by distance below.
        if (!_piles.ItemsBrowseOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Active || dom.Grabber.Held != null
            || _laserHover != null
            || _browseHover != null)
        {
            ClearItemFanHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        // allowNearMiss: the interaction path gets the angular near-miss rescue (a chip on a
        // shrunken board subtends less than the controller's own aim jitter — see
        // FanSweep.LaserMinHalfAngleDegrees). The fan OCCLUDER keeps the exact rect.
        bool rayHit = _piles.TryRaycastItemChips(pick.Origin, pick.Direction, _itemChipHover,
            out ItemsPile.ItemChip? chip, out Vector3 point, out float dist, allowNearMiss: true);
        bool uiInFront = rayHit && dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist;
        bool boardInFront = rayHit && !uiInFront && !TryTakeFrameFromBoardHover(dist);
        // See UpdateBrowseLaser: after the steal, a board hover that is STILL set owns the frame.
        bool boardOwnsFrame = _boardHover != null || _trayCardHover != null;
        if (!rayHit || chip == null || uiInFront || boardInFront)
        {
            LogItemFanLaser(dom, uiInFront
                ? "not delivered — nearer game UI is in front of the fan"
                : boardOwnsFrame
                    ? "not delivered - a NEARER board element is in front of the fan"
                    : "not delivered - the ray is not on any chip");
            ClearItemFanHover();
            if (boardOwnsFrame)
                return; // the board owns hover AND trigger this frame - no click-away, no grace
            // T2 pull-jerk grace, item-fan edition (user round 2): the trigger pull jerks the
            // aim ray off the narrow chip strip on the very press frame, so this miss branch is
            // exactly where a "take the card" pull used to land — and it then CLOSED the fan
            // via the click-away below instead of taking the promised chip. For a short window
            // after a genuine hover, the last-hovered chip still owns the trigger: claim the
            // frame (SuppressFarClick — no fabricated beam point, same reasoning as the ability
            // fan's rescue) and pluck exactly that chip. Yields to a live game-UI hit like
            // every rescue (with the occluder hold in RayInteractor, a panel BEHIND the fan can
            // never be that hit — only genuinely nearer UI is).
            if (!dom.RayUgui.HasHit && _itemChipGraceChip != null
                && Time.unscaledTime <= _itemChipGraceUntil
                && _itemChipGraceChip.Holder == null
                && _itemChipGraceChip.gameObject.activeInHierarchy)
            {
                dom.Ray.SuppressFarClick();
                // COMMIT gate (laser ruling 2026-08): chip plucks are commits (an item chip
                // dropped on the use slot spends the item — non-undoable); suppressed under a
                // blocking modal while the SuppressFarClick above still claims the frame.
                if (dom.TriggerDown && !_modalInputBlocked)
                {
                    ItemsPile.ItemChip rescue = _itemChipGraceChip;
                    _itemChipGraceChip = null;
                    VRLog.Info("Cards", "Item fan: pull-jerk grace pluck — the trigger came down with the " +
                                        $"beam just off the hovered chip; taking '{rescue.name}' instead of " +
                                        "dismissing the fan (what was lifted is what gets grabbed).");
                    rescue.OnPoke(dom); // pluck into the hand (ForceGrab, released on trigger-up)
                }
                return;
            }
            // Requirement 4 click-away dismiss (moved here from UpdateBoardLaser's empty-hit
            // branch when the chips left its scan): a trigger that misses every chip — and, by
            // the yield guard above, every fan/board/tray/browse target — closes the item fan
            // through the same ForeignInteraction seam as the discard/burnt browser's click-away.
            // COMMIT gate (laser ruling 2026-08): not under a blocking modal — a trigger meant
            // for the modal must not close the player's item fan.
            if (dom.TriggerDown && !_modalInputBlocked)
                ForeignInteraction("click-away (trigger off the item fan)");
            return;
        }

        // SINGLE-OWNER contract ("what pops is what you grab", user report 2026-08-02): while the
        // dominant hand is physically IN the fan, the hand owns both the pop and the trigger. Its
        // beam is on the fan too at that range, and a laser hover sets Ray.UiHitOverride, which
        // makes ProximityGrabber defer the trigger — so the popped chip (hand sweep) and the
        // taken chip (beam) could disagree. Yielding here is the item-fan edition of the ability
        // fan's UpdateFanHoverSplit ownership rule. Same chip = no conflict: the beam clamp is
        // the honest one, so the laser keeps it.
        ItemsPile.ItemChip? handOwned = _piles.HandOwnedItemChip(dom);
        if (handOwned != null && !ReferenceEquals(handOwned, chip))
        {
            LogItemFanLaser(dom, $"not delivered — the HAND owns the fan ('{handOwned.name}' is " +
                                 "physically in contact); the beam yields so what pops is what is taken");
            ClearItemFanHover();
            _itemChipGraceChip = null; // no laser promise while the hand owns the fan
            return;
        }

        if (!ReferenceEquals(chip, _itemChipHover))
        {
            ClearItemFanHover();
            _itemChipHover = chip;
            _itemChipHoverHand = dom;
            chip.OnPokeEnter(dom); // pop + hover haptic (debounced: only on chip change)
        }
        // Refresh the pull-jerk grace on every hovered frame (see _itemChipGraceChip): if the
        // trigger comes down within FanHoverGraceSeconds of the beam slipping off THIS chip,
        // the miss branch above plucks it instead of click-away-closing the fan.
        _itemChipGraceChip = chip;
        _itemChipGraceUntil = Time.unscaledTime + FanHoverGraceSeconds;

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        // COMMIT gate (laser ruling 2026-08): hover pop + beam clamp above stay live under a
        // blocking modal; the pluck itself is a commit (chip → use slot spends the item).
        // BoardAlreadySpentTrigger: the hover was taken back from the board this frame, but the
        // pull it already spent is not available twice.
        if (dom.TriggerDown && !BoardAlreadySpentTrigger)
        {
            if (_modalInputBlocked)
            {
                // The user's "the laser goes straight through the fan" case: the chip IS hovered
                // (it popped), the trigger DID come down, and only the commit gate refused it.
                // Without this line the refusal was completely silent — indistinguishable from a
                // missed pick. Throttled + names the window holding the gate.
                if (Time.unscaledTime >= _nextPressSuppressLogAt)
                {
                    _nextPressSuppressLogAt = Time.unscaledTime + 1f;
                    VRLog.Info("Cards", $"Item fan: laser pluck of '{chip.name}' SUPPRESSED — blocking modal open " +
                                        "(commit gate; the hover pop you see is live, the pluck is not). " +
                                        $"Blocking window(s): {WorldUI.ModalFallback.DescribeBlockingWindows()}.");
                    LogItemFanLaser(dom, "trigger DOWN but SUPPRESSED by the blocking-modal commit gate");
                }
                return;
            }
            ItemsPile.ItemChip pluck = chip;
            LogItemFanLaser(dom, $"DELIVERED — trigger plucked '{pluck.name}' into the hand");
            ClearItemFanHover();
            _itemChipGraceChip = null; // the promise is honoured — no stale grace after the pluck
            pluck.OnPoke(dom); // pluck into the hand (ForceGrab, released on trigger-up)
        }
        else
        {
            LogItemFanLaser(dom, "no press this frame — hover only");
        }
    }

    /// <summary>Throttle clock (unscaled) for the item-fan laser diagnostic below.</summary>
    private float _nextItemLaserLogAt;

    /// <summary>Last verdict logged, so a CHANGE of outcome is reported immediately.</summary>
    private string _lastItemLaserVerdict = string.Empty;

    /// <summary>
    /// The item-fan laser line the user asked for: whether the ray hit, WHAT it hit, whether the
    /// angular rescue was needed and by how much, the chip's real size and the world scale — plus
    /// why a press was or was not delivered. Together with the hand-sweep line this is the whole
    /// decision chain in two log lines, which is the point: "the log alone tells us which of the
    /// candidate causes it was". Rate-limited to one line per second, but a CHANGED verdict prints
    /// immediately (a state flip is exactly the moment worth having).
    /// </summary>
    private void LogItemFanLaser(VRHand hand, string pressVerdict)
    {
        if (string.Equals(pressVerdict, _lastItemLaserVerdict, System.StringComparison.Ordinal)
            && Time.unscaledTime < _nextItemLaserLogAt)
            return;
        FanSweep.FanLaserPick pick = _piles.LastItemLaserPick;
        // Nothing anywhere near the fan is not news — it is the resting state of the beam.
        if (!pick.Hit && pick.Distance <= 0f)
            return;
        _lastItemLaserVerdict = pressVerdict;
        _nextItemLaserLogAt = Time.unscaledTime + 1f;
        FanSweep.LogLaser("Item-fan", hand.Side.ToString(), pick, hand.WorldScale, pressVerdict);
    }

    /// <summary>Drop the item-fan laser hover (un-pop via OnPokeExit). Same shape as
    /// <see cref="ClearBrowseHover"/>; a chip destroyed under us (fan rebuild) compares
    /// Unity-null and is simply forgotten on the next hover change.</summary>
    private void ClearItemFanHover()
    {
        if (_itemChipHover == null)
            return;
        if (_itemChipHoverHand != null)
            _itemChipHover.OnPokeExit(_itemChipHoverHand);
        _itemChipHover = null;
        _itemChipHoverHand = null;
    }

    // ------------------------------------------------------------------ active laser --

    private VRCard? _activeHover;

    /// <summary>
    /// Feature 6 (laser-interactable active cards): the dominant hand's ray highlights the
    /// active grid's cards and TriggerDown plucks the pointed card into the hand to read it
    /// close (released on TriggerUp → returns to the grid, no game state — see
    /// <see cref="OnCardReleased"/>). Lowest priority of the laser paths: yields to the fan
    /// (<see cref="_laserHover"/>), the tray cards/board (<see cref="_trayCardHover"/>/
    /// <see cref="_boardHover"/>) and the pile browse (<see cref="_browseHover"/>) — the
    /// active grid is a passive read layered on top, like the browse arc. Mirrors
    /// <see cref="UpdateBrowseLaser"/>.
    /// </summary>
    private void UpdateActiveLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_active.IsShown || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Active || dom.Grabber.Held != null
            || _laserHover != null || _trayCardHover != null || _boardHover != null || _browseHover != null
            || _itemChipHover != null)
        {
            ClearActiveHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_active.TryRaycast(pick.Origin, pick.Direction, _activeHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            ClearActiveHover();
            return;
        }

        if (!ReferenceEquals(card, _activeHover))
        {
            ClearActiveHover();
            _activeHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearActiveHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
    }

    private void ClearActiveHover()
    {
        if (_activeHover == null)
            return;
        _activeHover.SetLaserHover(false);
        _activeHover = null;
    }

    // ------------------------------------------------------------------ modal input-block --

    /// <summary>
    /// Modal COMMIT-block (called ONLY from <see cref="TickInteractionsAndStatus"/>, which owns
    /// the predicate): force EVERY card non-poke/non-grab so nothing behind a blocking modal can
    /// be plucked or fingertip-selected. Laser ruling 2026-08: this is a COMMIT gate only — the
    /// laser paths keep running, so hover pops and beam clamps stay live on these cards; every
    /// pluck/rescue/select refuses itself at its existing CanGrab / PokeSelectEnabled check.
    ///
    /// The gate is <see cref="WorldUI.ModalFallback.BlockingWindowModalActive"/>, NOT
    /// <c>WindowModalActive</c>. That is not a detail — <c>WindowModalActive</c> ("ANY floated
    /// window") was the bug here twice over, and the caller records both rounds. Do not widen the
    /// predicate here or at the call site "to be safe"; the pause/ESC/Options family must impose
    /// ZERO card restrictions.
    ///
    /// A card already HELD when the modal opens is left alone (it stays held, like the
    /// dialog-open grab gate in <see cref="VRCard.CanGrab"/>). The normal per-card flags
    /// are restored by the next <see cref="Rebuild"/> once the modal closes.
    /// </summary>
    private void BlockCardInteractions()
    {
        IReadOnlyList<VRCard> all = _factory.All;
        for (int i = 0; i < all.Count; i++)
        {
            VRCard card = all[i];
            if (card == null || card.IsHeld)
                continue;
            card.PokeSelectEnabled = false;
            card.Grabbable = false;
        }
    }

    // ------------------------------------------------------------------ slot snap preview --

    private int _snapHighlightSlot = -1;
    private VRCard? _snapHighlightCard;
    private VRCard? _fieldHighlightCard; // pick counterpart of _snapHighlightCard (test #28)
    private int _fieldHighlightSlot = -1; // the slot _fieldHighlightCard is telegraphed into

    /// <summary>
    /// Test #13: while a card is HELD near the tray, glow the slot it would snap
    /// into on release (same accept/divert rules as OnCardReleased) and tick a
    /// haptic when the target slot changes — the drop is telegraphed, never a
    /// guess. Toggles/haptics only on change; no per-frame allocations.
    /// Test #15: the glowing slot is also THE authoritative drop target — the
    /// release path accepts it directly (see OnCardReleased), so what glows is
    /// what drops, even when the release gesture moves the hand out of radius.
    /// </summary>
    private void UpdateSlotHighlight()
    {
        // B/C: game-state overlay gate (results window / narrator dialog / scenario end —
        // see UpdateOverlayGate): no placement can commit, so no slot telegraph may glow
        // either — clear both the pick-mode and the snap highlight state and bail. The
        // read-only viewer-card suppression below stays untouched (it handles a
        // different case: browse/active cards that never slot).
        if (_overlayGateBlocked)
        {
            _tray.SetHighlightedSlot(-1);
            _fieldHighlightCard = null;
            _fieldHighlightSlot = -1;
            _snapHighlightSlot = -1;
            _snapHighlightCard = null;
            return;
        }
        // Pick modes (test #28): the candidate homes into the WANTED slot recess —
        // same telegraph contract as the play slots (glow-at-release is the primary
        // accept rule, haptic tick on edge), but the transient gold glow tracks the
        // wanted (next-empty) pick slot. Only a FRESH candidate telegraphs; a slotted
        // pick card being re-dropped does not (its release stays/unselects directly).
        CardsHandUI? pickHand = _fakeActive ? null : CurrentHand();
        bool pickMode = _tray.IsVisible && pickHand != null && IsPickMode(CardsGameApi.Mode(pickHand));
        if (pickMode)
        {
            VRCard? pickHeld = HeldCard(out VRHand? pickHolder);
            if (pickHeld != null && IsReadOnlyViewerCard(pickHeld))
            {
                pickHeld = null; // T2: browse/active viewer cards never slot — no telegraph
                pickHolder = null;
            }
            int want = PickTargetSlot();
            bool onField = pickHeld != null && _fieldCards.Contains(pickHeld);
            bool near = pickHeld != null && pickHolder != null && !onField && want >= 0
                && _tray.SlotNear(pickHeld.transform.position, pickHolder.Rig.PalmCenter.position) >= 0;
            int glowSlot = near ? want : -1;
            _tray.SetHighlightedSlot(glowSlot);
            VRCard? target = glowSlot >= 0 ? pickHeld : null;
            if (!ReferenceEquals(target, _fieldHighlightCard))
            {
                _fieldHighlightCard = target;
                _fieldHighlightSlot = glowSlot;
                if (target != null && pickHolder != null)
                    pickHolder.SendHaptic(HapticPreset.HoverTick); // debounced: only on edge
            }
            _snapHighlightSlot = -1;
            _snapHighlightCard = null;
            return;
        }
        if (_fieldHighlightCard != null)
        {
            _fieldHighlightCard = null;
            _fieldHighlightSlot = -1;
        }

        int slot = -1;
        VRHand? holder = null;
        VRCard? held = null;
        if (_tray.IsVisible)
        {
            held = HeldCard(out holder);
            // T2: a card plucked out of the pile BROWSE arc (or the active column) is a READ —
            // its release ALWAYS returns it to the viewer (see OnCardReleased's browse/active
            // early-outs), so glowing a tray slot for it telegraphs a drop that cannot happen.
            // No telegraph, no highlight-accept, no haptic for read-only viewer cards.
            if (held != null && IsReadOnlyViewerCard(held))
            {
                held = null;
                holder = null;
            }
            if (held != null && holder != null)
            {
                slot = _tray.SlotNear(held.transform.position, holder.Rig.PalmCenter.position);
                // Task #4b GATE ("what glows is what drops"): a fan card that the release
                // path would REFUSE (fewer than 2 playable cards — the player must rest,
                // see OnCardReleased) must not telegraph a snap either.
                if (slot >= 0 && !_tray.ContainsCard(held)
                    && pickHand != null && CardsGameApi.MustRestInsteadOfPlay(pickHand))
                    slot = -1;
                // Mirror the release-time targeting (item 27.1 + item 2):
                // - a HELD TRAY card NOW glows its own origin slot too — hovering the slot
                //   it came from telegraphs the RESTORE (releasing there re-seats it), so
                //   the origin, the other slot (reorder/swap) and the void are all valid;
                // - a FAN card hovering an OCCUPIED slot telegraphs a SWAP into that very
                //   slot (occupant → hand), so the glow stays put — no divert, both-
                //   occupied included.
                // What glows is what drops, origin included.
            }
        }
        _snapHighlightCard = slot >= 0 ? held : null;

        // PlayTray dedupes the visual toggle itself (safe across tray rebuilds);
        // the driver-side cache only edges the haptic.
        _tray.SetHighlightedSlot(slot);
        if (slot != _snapHighlightSlot)
        {
            _snapHighlightSlot = slot;
            if (slot >= 0 && holder != null)
                holder.SendHaptic(HapticPreset.HoverTick); // debounced: only on slot change
        }
    }

    /// <summary>
    /// T2: cards whose RELEASE always returns them where they came from, never into the
    /// select/slot seams — so no drop telegraph may ever glow for them ("what glows is what
    /// drops"). Three sources, one contract:
    /// <list type="bullet">
    /// <item>the discard/burnt BROWSE arc and the active-cards column (the original T2 case);</item>
    /// <item>INSPECTION grabs (2026-08-08): a hand card picked up purely to be read while the
    ///   placement is refused (<see cref="VRCard.InspectOnly"/>) — its release routes home before
    ///   any game seam, so a glowing slot would promise a play that cannot happen.</item>
    /// </list>
    /// </summary>
    private bool IsReadOnlyViewerCard(VRCard card) =>
        card.InspectOnly || _browser.Contains(card) || _active.Contains(card);

    private static VRCard? HeldCard(out VRHand? holder)
    {
        holder = null;
        VRHand? left = VRHands.Left;
        if (left != null && left.Grabber.Held is VRCard heldLeft)
        {
            holder = left;
            return heldLeft;
        }
        VRHand? right = VRHands.Right;
        if (right != null && right.Grabber.Held is VRCard heldRight)
        {
            holder = right;
            return heldRight;
        }
        return null;
    }
}
