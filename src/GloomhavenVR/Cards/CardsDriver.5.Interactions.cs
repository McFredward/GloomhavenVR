using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
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

    private void OnCardGrabbed(VRCard card, VRHand hand)
    {
        _liveGrabs.Add(card);
        // More-card-sounds: soft pick tick on every card grab (fan pluck, slot pluck, pile/
        // active read-grab — proximity and laser alike). Edge-triggered by nature: Grabbed
        // fires exactly once per grab session. The game plays nothing of its own here (a
        // physical VR grab has no game call), so no stacking.
        PlayCardSound(CardsConfig.CardGrabSound.Value, card.transform);
        // Item 8: grabbing a hand/tray/field card while a browse is open is a foreign
        // interaction. Grabbing a BROWSE card is part of the browse (read close), so
        // it is exempt — only the arc's own cards may be plucked without dismissing.
        if (!_browser.Contains(card))
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
        MaybeReopenPickSelection(card);
        if (_fan.Contains(card))
        {
            _fanOriginCards.Add(card); // reorder: eligible for a fan-gap commit on release
            _fan.Remove(card);
        }
        // Tray occupancy stays until the release decides select/unselect/swap.
    }

    private void OnCardReleased(VRCard card, VRHand hand, Vector3 velocity)
    {
        if (!_liveGrabs.Remove(card))
        {
            VRLog.Warn("Cards", $"Release without live grab ignored ({card.name}) — drop path is once-per-release.");
            return;
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

        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null || card.GameCard == null)
        {
            _fan.Add(card);
            return;
        }

        // Pick modes (test #21 B): the drop field is the only target — the slot
        // logic below is CardsSelection-only.
        if (IsPickMode(CardsGameApi.Mode(gameHand)))
        {
            HandlePickRelease(card, hand, gameHand);
            return;
        }

        // Test #15 accept rules, in priority order:
        // 1. HIGHLIGHT: the slot that was GLOWING for this card at release wins —
        //    hardware logs showed the release gesture consistently moving the hand
        //    just out of radius (3.2–6 m vs 2.74 m at diorama scale ~23) while the
        //    glow HAD triggered; what glows is what drops, guaranteed.
        // 2. RADIUS fallback: generous dual-sample capture (test #13) — card center
        //    AND holding-hand palm both count, whichever is nearest.
        int highlightSlot = ReferenceEquals(_snapHighlightCard, card) ? _snapHighlightSlot : -1;
        int slot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float d1, out float d2, out float radius);
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
        VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
                            $"rule={rule} → {outcome}");

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
        if (!IsPickMode(CardsGameApi.Mode(hand)))
            return;
        if (!CardsGameApi.IsPickConfirmDialogOpen(hand))
            return;
        // Grab-time reopen applies to PLACED field cards only (take it back / re-seat
        // it): the choice must reopen the instant the placement is physically undone.
        // Grabbing a FAN card leaves the popup alone — a swap only commits on the
        // release INTO a slot (BeginPickSwapReopen from HandlePickRelease), so a
        // change-of-mind void release changes nothing. Browse/active cards are
        // read-only and never reach this.
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
    /// Task #11 (free swap, fan → slot while the popup is open): a fan candidate was
    /// dropped into a slot while the confirm popup still showed the full selection.
    /// Reopen through the game's cancel seam first, keeping all placements EXCEPT the
    /// most recent one (its slot goes to the incoming card — it returns to the fan);
    /// the caller then queues the incoming card's select, which re-completes the
    /// selection and re-opens the popup with the swapped set. Runs before the commit
    /// so the game never sees a select while the popup locks the cards.
    /// </summary>
    private void BeginPickSwapReopen(CardsHandUI hand, VRCard incoming)
    {
        if (_pickReopenBusy || !CardsGameApi.IsPickConfirmDialogOpen(hand))
            return;

        _reopenKeep.Clear();
        for (int i = 0; i < _fieldCards.Count - 1; i++) // all but the most recent placement
        {
            VRCard placed = _fieldCards[i];
            if (placed == null || ReferenceEquals(placed, incoming) || placed.GameCard == null)
                continue;
            _reopenKeep.Add(placed.GameCard.AbilityCard);
        }
        // Physically free the displaced placement now — the queued cancel deselects it
        // and it is not re-selected, so it returns to the fan instead of waiting for
        // the reconcile prune.
        if (_fieldCards.Count > 0)
        {
            VRCard displaced = _fieldCards[_fieldCards.Count - 1];
            if (displaced != null && !ReferenceEquals(displaced, incoming))
            {
                _fieldCards.RemoveAt(_fieldCards.Count - 1);
                RelayoutField();
                _fan.Add(displaced);
            }
        }
        EnqueuePickReopen(hand, "fan card swap-in");
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
        CardActionQueue.Enqueue(() => CardsGameApi.CancelPickConfirmDialog());
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
        int nearSlot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float d1, out float d2, out float radius);
        bool highlight = ReferenceEquals(_fieldHighlightCard, card) && _fieldHighlightSlot >= 0;
        bool accept = highlight || nearSlot >= 0;
        string rule = highlight ? "highlight" : nearSlot >= 0 ? "radius" : "none";
        // Landing slot: a fresh candidate goes to the wanted (next empty) slot; a
        // re-dropped pick card keeps its own seat (recess for the live batch, the
        // beside-Slot2 stack for a locked one — see PickSeatOfIndex).
        int target = wasOnField ? PickSeatOfIndex(_fieldCards.IndexOf(card)) : PickTargetSlot();

        // EVENT-DISCARD BATCHING: a FRESH candidate dropped while the current batch is
        // already full (both recesses placed, total requirement not yet reached, no
        // confirm popup to swap under) is REFUSED back to the fan — the player must
        // press the tray CONFIRM ("WEITER") to lock the batch first. Without this the
        // third card silently piled beside Slot2 and the "batches of two" structure
        // (and its step display) meant nothing. The dialog-open case stays a SWAP
        // (BeginPickSwapReopen below), and a reopen in flight is left alone. Checked
        // BEFORE the drop log so the one line per drop tells the true outcome.
        if (accept && !wasOnField && target < 0
            && !_pickReopenBusy && !CardsGameApi.IsPickConfirmDialogOpen(gameHand))
        {
            VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
                                $"rule={rule} → REFUSED: current batch of " +
                                $"{Mathf.Min(2, CardsGameApi.PickCardsWanted() - _pickLockedCount)} is full — " +
                                "press the board CONFIRM to lock it in before choosing more. Card returns to the fan.");
            _tray.NoteSlotActivity(); // the gesture still happened right next to CONFIRM
            _fan.Add(card);
            return;
        }

        // THE one log line per real pick drop (the test #14 contract).
        string where = target >= 0 && target < 2 ? "slot " + (target + 1) : "the locked stack beside slot 2";
        VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, rule={rule} → " +
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
            // Task #11 (free swap): a fan candidate dropped into a slot while the
            // confirm popup shows the completed selection — reopen through the game's
            // own "choose another card" first (displaces the most recent placement),
            // THEN queue this card's select; the popup reopens with the swapped set.
            if (!wasOnField)
                BeginPickSwapReopen(gameHand, card);
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
        _pickLockedCount = _fieldCards.Count;
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

    // -------------------------------------------------------------- short rest --

    /// <summary>
    /// Present the short-rested card in the LEFT slot recess (test #25, item 1d;
    /// test #28 seating). Adopts the sacrifice widget through the SAME
    /// <see cref="AdoptedCard"/> path as every other physical card and homes it into
    /// Slot1 exactly like a single-card pick candidate (<see cref="PlayTray.PlacePickCard"/>)
    /// — so it reads exactly like the avoid-damage sacrifice. DISPLAY-ONLY: forced non-grabbable /
    /// non-poke (the zone loop resolves the same, this makes the intent explicit and
    /// covers the frames between a poll-driven swap and the next Rebuild). The card's
    /// live face is re-claimed off the docked DialogPopup automatically by
    /// <c>CardFace.Maintain</c> (CardFace.cs:210) — the popup's own buttons still
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
            Net.NetCardFx.Report(Net.CardFxAnchor.Discard, Net.CardFxAnchor.Slot0);
            VRLog.Info("Cards", $"Short rest: sacrifice '{CardsGameApi.CardName(widget)}' flies OUT of the discard " +
                                $"pile into the left slot ({FlyToPileSeconds:F2}s, arc over the board, orientation " +
                                "locked) — it originates there (issue 2; not a fly-from-below).");
        }

        if (changed)
        {
            bool swap = _shortRestPresented != null;
            VRLog.Info("Cards", $"Short rest: {(swap ? "REDREW —" : "presenting")} sacrificed card " +
                                $"'{CardsGameApi.CardName(widget)}' in the left slot " +
                                "(display-only; burn/redraw commits via the docked choice).");
            _shortRestPresented = lost;
        }
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
        Net.NetCardFx.Report(Net.CardFxAnchor.Slot0, Net.CardFxAnchor.Discard);
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
            // inside TryStartBurnFly stops the watcher from starting a second one. Only a card that
            // is NOT flying afterwards is parked — a running flight (this one, or the redraw's
            // fly-back to the discard pile) parks itself in its completion callback.
            TryStartBurnFly(CurrentHand(), _shortRestCard, "short-rest sacrifice");
            if (!_shortRestCard.IsFlying)
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
        CAbilityCard? current = null;
        if (hand != null && !IsPickMode(CardsGameApi.Mode(hand)))
            current = CardsGameApi.ShortRestedCard(hand);
        if (!ReferenceEquals(current, _shortRestPresented))
            _dirty = true;
    }
}
