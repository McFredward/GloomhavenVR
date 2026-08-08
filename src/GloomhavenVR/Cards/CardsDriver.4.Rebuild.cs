using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 4 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: hand fan reorder, rebuild, fly-to-pile (issue 5), MP card-FX anchors, the nested
// BurnSlab, HookCard.
//
// ORDER THAT STAYS IN THIS FILE, deliberately: Rebuild runs before TickBurnToPile. Both are
// here so the pair cannot drift across a file boundary.

internal sealed partial class CardsDriver
{
    // ------------------------------------------------------------- hand fan reorder --

    // Persisted VR fan order (session-only), keyed by AbilityCardUI.CardInstanceID. Applied to
    // _fanBuffer every Rebuild (before _fan.SetCards) so it OVERRIDES the game's own SortCards
    // re-sort — a pure VR-presentation reorder with zero gameplay effect. Pruned to the present
    // hand each Rebuild so stale ids (id reuse across scenarios) never accumulate.
    private readonly List<int> _fanOrder = new(24);
    private readonly List<VRCard> _fanReorderScratch = new(24);

    // Cards plucked OUT of the fan and still held — eligible for a reorder commit on release.
    private readonly HashSet<VRCard> _fanOriginCards = new();

    // Live insertion telegraph while a fan-originating card is held (mirrors _snapHighlightSlot):
    // the open gap (0..n, -1 = none) and the card it belongs to, so "what glows is what drops."
    private int _insertGap = -1;
    private VRCard? _insertHighlightCard;

    // Long-rest empty-board dedupe: one Info line per long-rest turn, not one per rebuild
    // (see CollectRoundCards).
    private bool _loggedLongRestEmptyBoard;

    /// <summary>Stable CardInstanceID key for a fan card (int.MinValue = no game card).</summary>
    private static int FanId(VRCard? card) =>
        card != null && card.GameCard != null ? card.GameCard.CardInstanceID : int.MinValue;

    /// <summary>
    /// Stage A: stable-reorder <see cref="_fanBuffer"/> to match the persisted <see cref="_fanOrder"/>
    /// just before <c>_fan.SetCards</c>. Any card whose id is not yet tracked keeps its game-relative
    /// order and is registered (appended); tracked ids no longer in the hand are pruned. This is the
    /// single seam that overrides the game's re-sort. Allocation-free steady state (reused scratch).
    /// </summary>
    private void ReorderFanBuffer()
    {
        int n = _fanBuffer.Count;
        if (n == 0)
            return;

        // Register newcomers (append, preserving current game-relative order among unknowns).
        for (int i = 0; i < n; i++)
        {
            int id = FanId(_fanBuffer[i]);
            if (id != int.MinValue && !_fanOrder.Contains(id))
                _fanOrder.Add(id);
        }
        // Prune tracked ids absent from the current hand (id reuse / hand change hygiene).
        for (int k = _fanOrder.Count - 1; k >= 0; k--)
        {
            int id = _fanOrder[k];
            bool present = false;
            for (int i = 0; i < n; i++)
            {
                if (FanId(_fanBuffer[i]) == id) { present = true; break; }
            }
            if (!present)
                _fanOrder.RemoveAt(k);
        }

        // Emit in _fanOrder order (stable), then any leftover (null-id) card in original order.
        _fanReorderScratch.Clear();
        _fanReorderScratch.AddRange(_fanBuffer);
        _fanBuffer.Clear();
        for (int k = 0; k < _fanOrder.Count; k++)
        {
            int id = _fanOrder[k];
            for (int i = 0; i < _fanReorderScratch.Count; i++)
            {
                VRCard c = _fanReorderScratch[i];
                if (c != null && FanId(c) == id) { _fanBuffer.Add(c); break; }
            }
        }
        for (int i = 0; i < _fanReorderScratch.Count; i++)
        {
            VRCard c = _fanReorderScratch[i];
            if (c != null && !_fanBuffer.Contains(c))
                _fanBuffer.Add(c);
        }
    }

    /// <summary>
    /// Stage D: while a fan-originating OR tray-originating card is held over the OPEN fan and NOT
    /// over a board slot, resolve the nearest inter-card gap and telegraph it (open the gap + gold
    /// overlay + a debounced haptic on edge), caching it for the release commit. Board-slot
    /// telegraph wins so slot-play and fan-reorder never both glow. Clears whenever nothing
    /// eligible is held. Tray-origin (T1): a card lifted OFF a play slot inserts at the gap too —
    /// the release runs the normal take-back (UnselectCard) and then splices the card's id into
    /// the persisted order at the gap (see the tray take-back branch of OnCardReleased).
    /// </summary>
    private void UpdateFanInsertion()
    {
        // Only the REAL hand fan reorders (CardsSelection). Pick-mode "fans" (discard/burnt piles)
        // reuse the same _fan but must not telegraph a reorder gap — their releases route through
        // HandlePickRelease, never the commit path.
        CardsHandUI? reorderHand = _fakeActive ? null : CurrentHand();
        if (!_fan.IsOpen || reorderHand == null || CardsGameApi.Mode(reorderHand) != CardHandMode.CardsSelection)
        {
            ClearFanInsertion();
            return;
        }
        VRCard? held = HeldCard(out VRHand? holder);
        // Eligible: plucked out of the fan (reorder) OR lifted off a tray slot (T1 — take-back
        // straight into a chosen fan position). Board slot telegraph (UpdateSlotHighlight ran
        // first) wins outright.
        // INSPECTION grabs never telegraph a gap (2026-08-08). A locked CardsSelection hand keeps
        // mode == CardsSelection, so an inspect-only card WOULD reach the gap logic here — but its
        // release routes home before the reorder-commit branch is even considered, so the gold gap
        // would promise a re-seat that cannot happen. Same "what glows is what drops" contract the
        // slot telegraph honours through IsReadOnlyViewerCard.
        bool eligible = held != null && !held.InspectOnly
            && (_fanOriginCards.Contains(held) || _tray.ContainsCard(held));
        if (held == null || holder == null || !eligible || _snapHighlightSlot >= 0)
        {
            ClearFanInsertion();
            return;
        }

        int gap = _fan.NearestGap(held.transform.position);
        _insertHighlightCard = gap >= 0 ? held : null;
        if (gap != _insertGap)
        {
            _insertGap = gap;
            _fan.SetInsertionGap(gap);
            if (gap >= 0)
                holder.SendHaptic(HapticPreset.HoverTick); // debounced: only on gap change
        }
    }

    /// <summary>Drop any live insertion telegraph (fan closed / nothing eligible held / committed).</summary>
    private void ClearFanInsertion()
    {
        if (_insertGap != -1)
        {
            _insertGap = -1;
            _fan.SetInsertionGap(-1);
        }
        _insertHighlightCard = null;
    }

    /// <summary>
    /// Stage E commit: insert the held card's id into <see cref="_fanOrder"/> at the visual gap
    /// (translated to the persisted order, preserving slotted-card ids), then re-add it to the fan
    /// and rebuild so <see cref="ReorderFanBuffer"/> reproduces the new order everywhere. Session-only.
    /// </summary>
    private void CommitFanInsertion(VRCard card, int gap)
    {
        int id = FanId(card);
        if (id == int.MinValue)
        {
            _fan.Add(card);
            return;
        }
        IReadOnlyList<VRCard> fan = _fan.Cards; // current fan order (the held card is already out)
        _fanOrder.Remove(id);
        int insertAt = _fanOrder.Count;
        if (fan.Count == 0)
        {
            insertAt = _fanOrder.Count;
        }
        else if (gap < fan.Count)
        {
            int idx = _fanOrder.IndexOf(FanId(fan[gap]));      // before the card now to its right
            insertAt = idx >= 0 ? idx : _fanOrder.Count;
        }
        else
        {
            int idx = _fanOrder.IndexOf(FanId(fan[fan.Count - 1])); // after the last fan card
            insertAt = idx >= 0 ? idx + 1 : _fanOrder.Count;
        }
        _fanOrder.Insert(insertAt, id);
        _fan.Add(card);
        _dirty = true;
    }

    // ------------------------------------------------------------------ rebuild --

    /// <summary>
    /// Live board switch executor: re-park every card currently seated ON the tray back
    /// to the factory pool, THEN tear the tray down so the next <see cref="Rebuild"/>
    /// loads the newly selected prefab. Slot occupants / pick-field / short-rest cards
    /// are parented under the tray root, so <see cref="PlayTray.Destroy"/>'s
    /// DestroyImmediate would otherwise destroy those factory-owned VRCards and orphan
    /// their adopted game faces — re-parenting them to the pool first keeps them (and
    /// their faces) alive; the forced rebuild re-seats them from authoritative state.
    /// </summary>
    private void RebuildBoard()
    {
        // The poke-toggle pile browse fan is parented under the board root (so it inherits the
        // board's live scale/pose) — a board switch DestroyImmediates that root. Close the browse
        // FIRST (it does not touch the cards' parents), so the park loop below still catches the
        // adopted browse cards as children of trayRoot and re-parks them out before the teardown.
        CloseBrowser("board rebuilt");
        Transform? trayRoot = _tray.Root;
        if (trayRoot != null)
        {
            for (int i = 0; i < _factory.All.Count; i++)
            {
                VRCard card = _factory.All[i];
                if (card != null && !card.IsHeld && card.transform.IsChildOf(trayRoot))
                {
                    // Board rebuilt MID-FLIGHT / mid-burn-hold: Park cancels a running FlyToPile
                    // (VRCard.Park → CancelFly), so its completion callback never fires — drop the
                    // driver-side flight membership and any pending burn-artwork hold here, or the
                    // stale entries would block ("ownedElsewhere") or later re-launch ("hold
                    // release") an animation for a card the pool already owns.
                    _flyingToPile.Remove(card);
                    if (card.GameCard != null)
                        ClearBurnHold(card.GameCard);
                    _factory.Park(card);
                }
            }
        }
        // PART D: capture the outgoing board's world pose BEFORE Destroy so the new board keeps
        // the EXACT same location (Rebuild re-applies it after EnsureBuilt instead of PlaceAtHead).
        _hasSwitchPose = _tray.TryCapturePose(out _switchPos, out _switchRot, out _switchScale);
        _tray.Destroy();
        _dockAnimSuppressed = true; // issue 2: the rebuilt board re-populates its cards silently (no storm)
        _dirty = true;
    }

    private void Rebuild(Transform anchor)
    {
        // FREE CHARACTER FOCUS: the game's own presented hand goes in, and the hand the player
        // asked to LOOK at comes out. With no focus this is the identity function, so every path
        // below is byte-for-byte the pre-feature one. With a focus it is an OVERRIDE, and
        // CharacterFocus.ReadOnlyView is latched for the whole rebuild — see the read-only
        // handling below and at the per-card zone stamp, which together make "you cannot act on a
        // character you are only looking at" a property of the objects rather than a convention.
        CardsHandUI? hand = Board.CharacterFocus.ResolveHand(CurrentHand());

        // Give a previously focused character its faces back BEFORE this rebuild adopts anything —
        // and say so in the log. Runs first because it destroys VR cards, which must not happen
        // once this pass has started filling its buffers.
        ReleaseStaleFocusHand(hand);

        if (hand == null)
        {
            _hasSwitchPose = false; // no board to re-pose without a hand
            // A focus view that ends with no hand at all (scenario teardown, hand mid-rebuild)
            // must not leave the fan latched in a restricted mode: the next interactive fan would
            // be inert.
            _fan.SetMode(CardFan.FanMode.Interactive);
            RebuildFakeOrClear(anchor);
            return;
        }
        if (_fakeActive)
            ClearFakeCards();
        _boundHand = hand;

        bool hadTrayRoot = _tray.Root != null;
        _tray.EnsureBuilt(_factory, anchor);
        // PART D: on a board SWITCH, re-apply the captured pose (the new board spawns in the exact
        // same place) instead of PlaceAtHead. A genuine first build has no captured pose and places
        // at the head as usual.
        if (_hasSwitchPose)
        {
            _hasSwitchPose = false;
            _expectedPoseChange = "rebuild-restored (board switch)"; // sanctioned (issue C watchdog)
            _tray.RestorePose(_switchPos, _switchRot, _switchScale);
        }
        else if (!hadTrayRoot && TryRestoreCarriedPose())
        {
            // ISSUE C: the tray root was re-created OUTSIDE the board-switch path (e.g. torn
            // down externally) — carry the previous pose over instead of re-placing at the
            // head. The factory re-creates the object; the pose survives.
            VRLog.Info("Cards", "Tray rebuilt — previous board pose carried over (no re-place at head).");
        }
        _rest.EnsureBuilt(_tray);
        _half.EnsureBuilt(anchor);
        _half.DockTo(_tray); // action selection lives on the control board (test #19)

        // Pile viewer (test #21): stacks exist whenever an active local hand does.
        if (CardsConfig.PileViewer.Value)
        {
            _piles.EnsureBuilt(_tray);
            _piles.SetVisible(true);
        }
        else
        {
            _piles.SetVisible(false);
            CloseBrowser("[Cards] PileViewer off");
        }

        CardHandMode mode = CardsGameApi.Mode(hand);
        CardsGameApi.GetCards(hand, _widgetBuffer);

        // Issue 5: snapshot the PREVIOUS rebuild's docked round cards BEFORE clearing, so the park
        // sweep below can recognise a just-cleared played card and fly it into its pile.
        _lastHalfCards.Clear();
        _lastHalfCards.UnionWith(_halfBuffer);

        // Issue 1: snapshot the PREVIOUS rebuild's slot occupants (the vanish set) and every card
        // that was visible in a zone (the appear-suppression set) BEFORE SyncFromGameState re-seats
        // for this (possibly new) character. Read the tray occupants LIVE here — they are still the
        // outgoing character's cards until the sync below evicts them.
        _lastTrayCards.Clear();
        _lastVisibleCards.Clear();
        _lastVisibleCards.UnionWith(_fanBuffer);
        _lastVisibleCards.UnionWith(_halfBuffer);
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _tray.Occupant(s);
            if (occ != null)
            {
                _lastTrayCards.Add(occ);
                _lastVisibleCards.Add(occ);
            }
        }

        _fanBuffer.Clear();
        _halfBuffer.Clear();

        // Test #15: the tray is the central DASHBOARD — visible for the whole
        // scenario (initiative track, objectives, confirm/undo, settings), not only
        // during card selection. Cards remain grabbable only in CardsSelection.
        bool trayVisible = true;
        bool halfVisible = false;
        bool grabbable = false;

        // READ-ONLY FOCUS VIEW (feature "free character focus"). The player is looking at a
        // character the game is not presenting to them — a teammate, or one of their own that is
        // not the one acting. What they asked to see is exactly three things, and this branch
        // builds all three and nothing else:
        //   (c) that character's HAND cards in the fan — CardPileType.Hand off the character's own
        //       widget list. NOTE the vanilla branches below would show nothing here: the hand fan
        //       is gated on CardsGameApi.IsSelectionPhase, which is false during a turn. Hand
        //       cards are not secret in Gloomhaven (CCharacterClass.HandAbilityCards is
        //       host-replicated with no visibility gate, CCharacterClass.cs:91) — only the two
        //       CHOSEN round cards are, and only during SelectAbilityCardsOrLongRest, which
        //       CharacterFocus.Open refuses outright and RevealGate independently re-refuses.
        //   (a) the cards that character CHOSE this round, lying in the board's two card SLOTS —
        //       for as long as the game's replicated model still names them, NOT only during that
        //       character's own turn (Board.CharacterFocus.RoundCardDock; user 2026-08-08 "ich will
        //       AUCH, dass dort dann immer die jeweiligen ausgewählten Karten liegen"). The turn key
        //       was the bug: a teammate is focused precisely BECAUSE they are not acting.
        //   (b) their piles — the discard/burnt/items stacks and the browse arc follow `hand`
        //       automatically, further down; nothing extra is needed for them here.
        // grabbable stays FALSE, so the per-card stamp near the end of this method builds every
        // one of these cards non-grabbable and non-pokeable, and CardFan refuses the laser. The
        // slot cards get the SAME treatment through _half.SetReadOnly further down: no poke zones
        // are armed and the card canvas is never registered with UguiPokeSurfaces, so the dock is a
        // picture there too.
        bool readOnly = Board.CharacterFocus.ReadOnlyView;
        // INSPECTION ENTITLEMENT (user ruling 2026-08-08 — taking a card out of the hand to LOOK at
        // it must never be blocked). Latched ONCE for the whole rebuild, exactly like `readOnly`,
        // so the zone stamp and the fan mode below can never disagree about it. The rule and its
        // source evidence live in Board.CharacterFocus.HandInspectable: every character the local
        // client controls (which offline is every merc), never a foreign one.
        bool handInspectable = Board.CharacterFocus.HandInspectable(hand);
        if (readOnly)
        {
            // SLOT CARDS FIRST, so the fan can exclude them (see below).
            bool dockOpen = Board.CharacterFocus.RoundCardDock(hand, out string dockSource);
            if (dockOpen)
                CollectRoundCards(hand, _halfBuffer);
            // An EMPTY dock hides the layout outright rather than showing an armed-but-empty one —
            // "nothing chosen / nothing left to show" must look like an empty board, not a broken
            // one. The reason lands in the log line below.
            halfVisible = _halfBuffer.Count > 0;
            LogFocusSlotCards(hand, dockOpen, dockSource);

            FillHandFan();
            _tray.ClearSlots(); // no slot OCCUPANCY belongs to a character we are only watching —
                                // the dock parents the cards to the slot transforms without ever
                                // claiming a recess, so nothing becomes laser-pluckable
                                // (PlayTray.TryRaycastCards scans _occupants only).
        }
        else
        switch (mode)
        {
            case CardHandMode.CardsSelection:
                // Item B (mode-desync lock): CardsHandUI.currentMode STAYS CardsSelection
                // after the player confirms — it is only re-driven by the next
                // CardsHandManager.Show(...), which never runs during the enemy turn — so
                // the raw mode is a stale trap. The hardware repro sat in this stale
                // CardsSelection fan all through the enemy turn with the played cards still
                // reclaimable and the fan bound. Gate the whole INTERACTIVE selection on the
                // game's own phase (CardsGameApi.IsSelectionPhase == the exact
                // SelectAbilityCardsOrLongRest gate the game uses, CardsHandUI.cs:1516/1564):
                // - selecting → the grabbable hand fan + free slot placement, as before;
                // - locked (confirmed / enemy turn / any non-selection phase) → NO hand fan,
                //   nothing grabbable (grabbable stays false), and the two played cards stay
                //   DOCKED read-only in their slots. The fan unbinds (empty buffer) and no
                //   card is reclaimable until the next real card-selection phase.
                bool selecting = CardsGameApi.IsSelectionPhase(hand);
                grabbable = selecting;
                // THE HAND IS ALWAYS SHOWN — the LOCK only removes the AFFORDANCE (user ruling
                // 2026-08-08, see FillHandFan). The fan used to be filled only while `selecting`,
                // which is precisely the "keine Handkarten" bug: after the player confirms, the
                // hand's currentMode STAYS CardsSelection (the stale-mode trap documented above)
                // while IsSelectionPhase goes false, so the character the GAME presents — the one
                // that was selected when the phase began — got an EMPTY fan and the empty-hand
                // placard, while every FOCUSED character kept its cards (the read-only branch above
                // never had the phase gate). Same fill for both now; `grabbable` alone still carries
                // the lock, so nothing is reclaimable outside the real selection phase.
                FillHandFan();
                _tray.SyncFromGameState(hand, _factory);
                // Tray occupants were created by the sync — hook + re-adopt them too (both
                // while selecting AND locked, so the docked played cards keep their face).
                for (int slot = 0; slot < 2; slot++)
                {
                    VRCard? occupant = _tray.Occupant(slot);
                    if (occupant == null)
                        continue;
                    HookCard(occupant);
                    if (occupant.NeedsFace && occupant.GameCard != null)
                        occupant.AttachGameCard(occupant.GameCard);
                }
                LogSelectionLock(hand, selecting);
                break;

            case CardHandMode.LoseCard:
            case CardHandMode.DiscardCard:
            case CardHandMode.RecoverDiscardedCard:
            case CardHandMode.RecoverLostCard:
            case CardHandMode.IncreaseCardLimit:
                // Modal card picks (long-rest burn, avoid-damage, discards,
                // recovers): the 2D UI is click-to-select. VR (item 10): the
                // candidates are GRABBABLE ONLY — the card must be PLACED into a
                // control-board slot to select it. Poke-to-select is deliberately
                // NOT armed here: merely touching a hand card must never commit it
                // (a fingertip within 8 mm used to fire SelectCard with no board
                // placement). The only commit path is the deliberate slot drop
                // (HandlePickRelease → TryCommitPick → CardsHandUI.SelectCard).
                grabbable = true;
                // Field occupants stay valid only while the game still reports them
                // selected (an undo / "choose other card" returns them to the fan).
                // Task #11 free swap: while a pick-reopen (cancel → re-select) is in
                // flight the game momentarily reports EVERYTHING deselected — do not
                // prune on that transient or the still-placed card would snap to the
                // fan mid-swap; the reopen's completion re-runs this with final state.
                for (int i = _fieldCards.Count - 1; i >= 0; i--)
                {
                    VRCard occupant = _fieldCards[i];
                    if (occupant == null || occupant.GameCard == null
                        || (!_pickReopenBusy && !occupant.GameCard.IsSelected))
                    {
                        // Event-discard batching: a pruned LOCKED card shrinks the
                        // locked prefix (the step display recomputes from it).
                        if (i < _pickLockedCount)
                            _pickLockedCount--;
                        _fieldCards.RemoveAt(i);
                    }
                }
                // Item 9: the SELECTABLE widgets become the fan. In CardsSelection the
                // fan is the real hand; in the burn-two-discarded flow the game marks
                // the DISCARD-pile widgets selectable (CardHandMode.LoseCard, pile
                // Discarded, count 2 — AbilityCardUI.SetMode), so the exact same fan
                // becomes the discard pile, picked exactly like hand cards through the
                // one authoritative TryCommitPick → SelectCard seam. Track the source
                // pile for the change-deduped Info line below.
                CardPileType pickSource = CardPileType.None;
                // Task #11 (b): while the game's "Karten verbrennen / Wähle eine andere
                // Karte" confirm popup is open it flips EVERY widget unselectable
                // (OnCardSelected LoseCard branch, CardsHandUI.cs:2046-2052) — which
                // used to empty the fan the moment the required cards were placed. The
                // eligible cards must stay browsable/swappable through the whole flow,
                // so while that popup is open the fan keeps the widgets of the pick
                // source pile (the game's own selectableCardTypes) that are not
                // currently selected. Grabbing one triggers the reopen seam
                // (OnCardGrabbed → game's own "choose another card"), which restores
                // real selectability before any commit can run.
                bool confirmOpen = CardsGameApi.IsPickConfirmDialogOpen(hand);
                for (int i = 0; i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    bool eligible = widget.IsSelectable
                        || (confirmOpen && !widget.IsSelected && CardsGameApi.IsPickEligible(hand, widget));
                    if (!eligible)
                        continue;
                    if (pickSource == CardPileType.None)
                        pickSource = widget.CardType;
                    VRCard card = AdoptedCard(widget);
                    if (!_fieldCards.Contains(card))
                        _fanBuffer.Add(card);
                }
                LogPickSource(mode, pickSource);
                RelayoutField();
                break;

            case CardHandMode.ActionSelection:
                // Task #5 (clear the control board after the own turn): CardsHandUI.currentMode
                // STAYS ActionSelection after the player's own turn ends (it is only re-driven
                // by the next CardsHandManager.Show, which never runs during an enemy turn), so
                // this case is still hit while an ENEMY is up — and the two played cards would
                // otherwise stay docked on the board. Dock them ONLY while it is genuinely THIS
                // character's own action turn (CardsGameApi.IsActionTurn == Choreographer.
                // CurrentActor is this hand's locally-controlled player). The moment an enemy (or
                // any other actor) becomes current, halfVisible stays false and _halfBuffer stays
                // empty, so the zone loop below PARKS the played cards — the board is cleared.
                // When the character's own turn comes round again the cards re-dock. This is
                // pure VR presentation: the game's own 2D round-card state is untouched, and the
                // two-character sequential turns each show their own actor's cards (CurrentActor
                // is that actor during its turn). Long rest is unaffected: the long-rester's own
                // turn keeps CurrentActor == its player (IsActionTurn true) with an empty round
                // pile, exactly as before.
                // ONE SEAM for "may the board's card slots show this hand's chosen cards": with no
                // focus override CharacterFocus.RoundCardDock IS CardsGameApi.IsActionTurn, so this
                // branch is unchanged; routing it through the same method keeps the vanilla answer
                // and the focus answer from ever drifting apart in two places.
                bool actionTurn = Board.CharacterFocus.RoundCardDock(hand, out _);
                if (actionTurn)
                {
                    halfVisible = true;
                    CollectRoundCards(hand, _halfBuffer);
                }
                // The remaining HAND is shown here too (user ruling 2026-08-08: the cards must be
                // visible "egal in welcher Phase"). AFTER the round-card collection above, so a card
                // that is lying in a board slot is never also a fan card. `grabbable` stays false:
                // during an action turn the hand is a picture, exactly as in a focus view.
                FillHandFan();
                LogActionTurnLock(hand, actionTurn);
                break;

            default:
                // The remaining modes the game can park a hand in — CardHandMode.DeckSelection and
                // CardHandMode.Preview (CardHandMode.cs) — are NON-PICK modes, so the fan is the
                // hand fan and the hand is shown, read-only (grabbable is false here). This branch
                // is what makes the rule ABSOLUTE rather than "in the phases we happened to list":
                // a hand parked in any mode the mod does not name still shows its cards, so the
                // empty-hand placard can never be the answer to "the mod has no case for this".
                FillHandFan();
                break;
        }

        if (mode != CardHandMode.CardsSelection)
            _tray.ClearSlots(); // stale occupancy must not pin cards outside CardsSelection

        // Drop field (test #21 B): exists ONLY during a pick mode (C); leaving the
        // mode clears its occupants — the zone loop below parks them.
        // READ-ONLY FOCUS: a pick flow belongs to the character the GAME presents, never to one we
        // are merely watching — the watched character's stale CardsHandUI.currentMode could still
        // read LoseCard from its own last decision. Forcing `pick` false keeps the drop field, the
        // pick-confirm mirror and the short-rest overlay out of a focus view entirely.
        bool pick = !readOnly && IsPickMode(mode);

        // Short-rest sacrifice overlay (test #25, item 1d; test #28 seating): while the
        // game presents the randomly lost card's burn/redraw choice (ShortRestedCard !=
        // null; the choice itself is the docked DialogPopup), lay that card physically
        // in the LEFT slot recess (Slot1) — the same left-slot home the avoid-damage
        // burn uses — DISPLAY-ONLY. Guarded off during the pick modes (which own the
        // slots) so the two flows can never collide, and off when the tray is hidden.
        // The short-rest random path stays in CardHandMode.CardsSelection, so this
        // simply overlays the unchanged hand fan. Rebuild is the sole executor;
        // PollShortRest keeps it live on redraw.
        CAbilityCard? shortRested = pick || readOnly ? null : CardsGameApi.ShortRestedCard(hand);
        bool shortRest = shortRested != null && trayVisible;

        // Slots stay physically visible (fixed asset, test #28) — no field to toggle;
        // the driver only marks pick flows so CONFIRM mirrors the mode's confirm.
        _tray.SetPickActive(pick && trayVisible);
        if (shortRest)
            PresentShortRestCard(hand, shortRested!);
        else
            RemoveShortRestCard();

        if (!pick)
        {
            _fieldCards.Clear();
            _pickLockedCount = 0;     // event-discard batching never survives the mode
            _loggedPickSource = null; // re-entering a pick mode logs its source afresh (item 9)
        }

        // ACTIVE CARDS area (feature 6): refresh the permanently-shown active-card column
        // — BEFORE the zone flags below so its cards are marked in-zone and kept out of the
        // park sweep (like the browse arc), and their active-half highlight is set here.
        //
        // ORDER (fan-close sweep fix): this runs BEFORE UpdateBrowser on purpose. The browse
        // arc must only ever borrow card visuals the board is NOT already showing
        // (BoardOwnsCardVisual), and the ACTIVE column is one of those board zones — so its
        // membership has to be current for THIS rebuild before the browser filters against it.
        // Every other board zone (_halfBuffer, tray slots, _fieldCards, _shortRestCard) is
        // already resolved above.
        UpdateActive(hand);

        // Pile browse (test #21): refresh content or close — BEFORE the zone flags
        // below so freshly closed browse cards park in this same pass.
        UpdateBrowser(hand, mode);

        // Configure cards per zone; everything else parks invisibly.
        for (int i = 0; i < _factory.All.Count; i++)
        {
            VRCard card = _factory.All[i];
            if (card == null || card.IsHeld)
                continue;
            // Issue 5: a card mid-flight into a pile owns its own transform until it arrives —
            // never re-zone, re-home or re-park it (that would teleport it out of the animation).
            // Issue 2: a card mid-disappear likewise owns its transform until it parks itself — the
            // park sweep must not re-park it (double-hide) while it shrinks out.
            if (card.IsFlying || card.IsVanishing)
                continue;
            // Issue 1: remember every visible card's true world pose so a later damage-burn can fly
            // its slab from where the card ACTUALLY was, never from a teleported pile position.
            // BURN ANIM: the predicate is "NOT parked" rather than "active in hierarchy" on purpose.
            // A card in a CLOSED hand fan is inactive (the fan root is disabled) yet its transform
            // still carries its true world pose at the player's hand — that is exactly where a card
            // burned straight out of the hand must fly FROM. Only a pooled/parked card has a
            // meaningless pose (Park re-homes it to the pool root at the origin), and that is the
            // one case we must not record.
            if (card.GameCard != null && !IsParked(card))
            {
                _lastCardWorldPos[card.GameCard] = card.transform.position;
                _lastCardWorldRot[card.GameCard] = card.transform.rotation;
            }
            bool inFan = _fanBuffer.Contains(card);
            bool inHalf = _halfBuffer.Contains(card);
            bool inTray = _tray.SlotOf(card) >= 0;
            bool inBrowse = _browser.Contains(card);
            bool inField = _fieldCards.Contains(card);
            bool inActive = _active.Contains(card); // feature 6: shown in the active-cards column
            // Short-rest sacrifice card: its own zone. Never in any of the above, so
            // its Grabbable/PokeSelect resolve to false here (display-only) — we only
            // keep it OUT of the park sweep so PresentShortRestCard's centre home holds.
            bool inShortRest = ReferenceEquals(card, _shortRestCard);

            // PILE-ORIGIN MARKER RETIRE (discard-in-hand-fan bug 2026-08-04): a non-held,
            // non-flying card the browse arc no longer lists has been re-homed by THIS rebuild
            // from game truth (hand fan, pick fan - where the whole fan legitimately IS the
            // discard/burnt set -, tray, half, field, active, short rest) or is about to park.
            // Either way its browse loan is over, so the marker must not outlive it: a stale
            // marker would hijack the card's next release into the pile routing. Held cards were
            // skipped above - their marker survives the hold, which is the entire point (the
            // browser's own list does NOT survive a mid-hold close; see VRCard.PileOrigin).
            if (!inBrowse)
                card.PileOrigin = null;

            // Task #2 follow-up: the slot-dock grab apron (under/around-grab accept,
            // VRCard.SetDockGrabPad) is TRUE exactly for slot-docked cards — tray
            // occupants and pick/field cards in the recesses — and FALSE everywhere
            // else. Central re-assert every Rebuild (idempotent) so no dock/undock
            // path can leave a stale apron on a fan/browse/parked card.
            card.SetDockGrabPad(inTray || inField);

            // Item 10: poke-select is never armed on hand cards — touching a card
            // must not auto-select it; a card is committed only by placing it into a
            // board slot. Kept as an explicit reset so a previously pokeable card is
            // disarmed on rebuild.
            card.PokeSelectEnabled = false;
            // Field occupants stay grabbable: plucking one back off the field and
            // releasing it elsewhere unselects through the game's own seam. Browse
            // cards are grabbable too (item 5) — but purely to pull one close and
            // read it; the release routes back to the arc, never to a game seam.
            // Active cards (feature 6) are grabbable for the same read-only reason:
            // pluck one to read it, release returns it to the column, never a game seam.
            // READ-ONLY FOCUS (structural, not a convention): while the mod presents a character
            // the player may not drive, NOTHING it built is grabbable. Together with the
            // unconditional PokeSelectEnabled = false above and CardFan's Picture-mode raycast
            // veto, there is no interactor left that can even FIND one of these cards — so there is
            // no path from a VR input to a game call for a character we are only looking at. This
            // is the single funnel every card in every zone passes through on every rebuild, so it
            // cannot be bypassed by a card that changed zone between frames.
            //
            // COMMIT vs INSPECT (user ruling 2026-08-08: "Ich möchte das man jederzeit auch eine
            // Karte aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die Karte
            // nirgendwo ablegen kann. Das soll also niemals blockiert sein"). `commitGrab` is the
            // OLD verdict, byte for byte — "this card may be picked up AND placed", which is why
            // every phase that refused the PLACEMENT also refused the LOOK. `inspectGrab` is the
            // new, additive one: a HAND-FAN card of a hand this client is entitled to handle
            // (Board.CharacterFocus.HandInspectable) may ALWAYS be picked up, in every phase and
            // every mode, with `InspectOnly` stamped so the release routes it straight back home
            // without touching a game seam. Nothing else in the pipeline is widened: the tray, the
            // pick field, the browse arc, the active column and the round-card dock all keep the
            // exact verdicts they had.
            //
            // THE PILE ARC AND THE ACTIVE COLUMN JOIN THE INSPECT SIDE (user ruling 2026-08-08:
            // "Die Verbrannt-Piles und Abgeworfen-Piles sollen jederzeit … öffenbar sein und die
            // Karten sollen auch nehmbar sein um sie anzugucken, das darf nicht blockieren"). The
            // `!readOnly` in `commitGrab` used to make every card of a browse arc / active column
            // inert the moment the player was LOOKING at a character rather than driving it — which
            // is exactly when a pile gets opened, so "pick a discarded card up and read it" was
            // dead in the only view it matters in. These cards were never committable in the first
            // place (OnCardReleased's browse / active branches return them home BEFORE any game
            // hand is resolved, and the PileOrigin fallback catches one still held when the arc
            // closes), so this widens the GRAB and nothing else — the same split the hand fan got.
            bool commitGrab = !readOnly
                              && ((inFan && grabbable) || (inTray && grabbable)
                                  || inField || inBrowse || inActive);
            bool inspectGrab = (inFan || inBrowse || inActive) && !commitGrab && handInspectable;
            card.Grabbable = commitGrab || inspectGrab;
            card.InspectOnly = inspectGrab;
            // BOTH HANDS ON EVERY CARD (user ruling 2026-08-04: "Alle Karten sollen allgemein auch
            // mit der nicht-dominanten Hand aufgenommen werden koennen ... Das soll fuer alle
            // Karten gelten - ausser den Faecherkarten selber"). This is the GENERAL rule that
            // replaces the per-zone whittling of the last two builds (browse arc exempted
            // 2026-08-03, pick-field cards 2026-08-04): the fan-owning-hand veto
            // (VRCard.InteractionBlockedHand) exists for ONE geometric reason — the ability fan
            // hangs off the gate hand's own palm, so that hand's hover sits permanently inside the
            // fan — and therefore applies to the fan's OWN cards and nothing else. Every other
            // zone (browse arc, pick field, tray slots, active column, half selection) is
            // hover/highlight/grabbable with BOTH hands. Re-asserted every rebuild, so a recycled
            // widget can never carry a stale verdict into a new role; the two between-rebuild
            // seams (fan pluck -> true, fan re-entry -> false) are stamped at CardsDriver.
            // OnCardGrabbed and CardFan.Add/SetCards respectively (see VRCard.AllowsGateHand).
            card.AllowsGateHand = !inFan;
            // Fan strips only apply while the card is in a fan that TILES its colliders. That is
            // now the browse arc as well as the hand fan (PileBrowser.Relayout strips exactly like
            // CardFan.Relayout — the sweep-skips-cards fix), so a browse card must keep its strip
            // through a rebuild; every other pool still gets the full collider back here.
            if (!inFan && !inBrowse)
                card.ResetColliderRegion();
            if (!inActive)
                ClearActiveHighlight(card); // clear any stale active-region highlight on reused cards

            if (!inFan && !inHalf && !inTray && !inBrowse && !inField && !inShortRest && !inActive)
            {
                // Issue 5: a just-cleared PLAYED round card flies into its destination pile
                // (burned → burnt, discarded → discard) instead of vanishing; everything else
                // parks instantly as before. TryStartFlyToPile returns true only when it launched
                // the animation — the fly's completion callback parks the card on arrival.
                if (TryStartFlyToPile(hand, card))
                {
                    // fly-to-pile owns this card — never also vanish it (no double animation).
                }
                // BURN ANIM (user: "a burned card must SLIDE into the burnt pile instead of just
                // disappearing"): a card that leaves every zone in the SAME frame the game moved it
                // into the character's LOST pile was just BURNED — the long-rest chosen burn and the
                // take-damage "burn a card" pick both land here, because their pick card sits in a
                // board slot / on the field until the commit and is then simply un-zoned. Rebuild
                // runs BEFORE TickBurnToPile in the same Update, so without this the card was
                // already parked (pose gone) by the time the burn watcher noticed it — which is
                // exactly the "card just vanishes" the user reported. Flying it HERE, while the real
                // VR card is still live at its true position, reuses the very same FlyToPile arc as
                // the discard flow (and carries the game's own burn FX, which plays on the adopted
                // face). The claim in TryStartBurnFly keeps TickBurnToPile from animating it twice.
                else if (TryStartBurnFly(hand, card,
                             _lastTrayCards.Contains(card) ? "play slot"
                             : _lastVisibleCards.Contains(card) ? "hand fan"
                             : "board field / pick slot"))
                {
                    // burn fly owns this card — flying, OR lying in place while its burn artwork
                    // completes (the bounded artwork hold). Either way: never park/vanish it here,
                    // that would be exactly the reported "card vanishes after the burn animation".
                }
                // Issue 2/1: a docked card that CLEARS on a character/turn switch — a played ACTION
                // card (in last rebuild's half set) OR a SELECTED card lying in a board SLOT (in last
                // rebuild's tray set) — but is NOT flying to a pile used to POP away. Now it shrinks +
                // fades out IN PLACE, then parks. Everything else (fan close, browse/active, pool
                // return) keeps its own instant park / own animation. Suppressed on the first rebuild
                // after a board build so the scenario-load population never storms.
                else if (!_dockAnimSuppressed && (_lastHalfCards.Contains(card) || _lastTrayCards.Contains(card))
                         && card.gameObject.activeInHierarchy)
                {
                    VRCard vanishing = card;
                    bool wasSlot = _lastTrayCards.Contains(card);
                    card.Vanish(() => _factory.Park(vanishing));
                    VRLog.Info("Cards", $"Card disappear: '{card.name}' ({(wasSlot ? "selected slot" : "docked action")} " +
                                        $"card cleared on a character/turn switch) — scale-down + fade " +
                                        $"({VRCard.DockVanishSeconds:F2}s) in place, then parked (not flying to a pile).");
                }
                else
                {
                    _factory.Park(card);
                }
            }
        }

        // Hand reorder (Stage A): apply the persisted VR order to the real hand fan only —
        // overrides the game's SortCards. Pick-mode "fans" (discard/burnt piles) are transient
        // and keep game order.
        if (mode == CardHandMode.CardsSelection)
            ReorderFanBuffer();
        // FAN MODE: told BEFORE its content, so the very first frame of a restricted view already
        // carries the right affordance (a fan that learned its mode one frame late would offer the
        // wrong one for exactly that frame).
        //
        // THREE STATES, and the middle one is the 2026-08-08 ruling ("Ich möchte das man jederzeit
        // auch eine Karte aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die
        // Karte nirgendwo ablegen kann"):
        //  • INTERACTIVE — `grabbable` and not a focus override: the real card-selection window and
        //    the modal pick flows. Grab, laser-pluck, reorder, DROP into a slot. Unchanged.
        //  • INSPECT — the placement is refused (selection locked / confirmed, an action turn, any
        //    other phase, or a focus view of one of our OWN characters) but the hand is one this
        //    client is entitled to handle. The cards are fully grabbable and laser-pluckable and
        //    the release returns them HOME; no game seam is reachable (VRCard.InspectOnly).
        //    THIS IS WHAT USED TO BE A PICTURE, and that was the reported bug.
        //  • PICTURE — a FOREIGN character's hand in a focus view. Inert end to end, exactly as
        //    before; see Board.CharacterFocus.HandInspectable for why that one stays refused.
        CardFan.FanMode fanMode =
            !readOnly && grabbable ? CardFan.FanMode.Interactive
            : handInspectable ? CardFan.FanMode.Inspect
            : CardFan.FanMode.Picture;
        LogFanMode(hand, fanMode, readOnly, grabbable, mode);
        _fan.SetMode(fanMode);
        _fan.SetCards(_fanBuffer);
        _tray.SetVisible(trayVisible);
        // READ-ONLY FOCUS, slot cards: told BEFORE the content for the same reason as the fan. The
        // round-card dock is the ONE zone that arms real input on its cards — fingertip HalfZone
        // volumes plus the card's own uGUI canvas registered with UguiPokeSurfaces (which is what
        // makes the laser able to click a card half at all). A read-only dock arms neither, so a
        // watched character's played cards are a picture: no poke, no laser, no PlayHalf. Showing
        // the cards did not open an input path.
        _half.SetReadOnly(readOnly);
        _half.SetVisible(halfVisible);
        if (halfVisible)
            _half.SetCards(_halfBuffer);

        // Issue 2 APPEAR: a docked action card that just (re-)appeared for the new/active character
        // — now in the half set but NOT in it last rebuild — scales + fades in instead of popping
        // from nothing. Runs AFTER _half.SetCards so each card's home pose is already asserted (the
        // appear grows toward it). Suppressed on the first rebuild after a board build (no storm).
        // A card flying to a pile is never docked here, so the two never collide (no double anim).
        if (halfVisible && !_dockAnimSuppressed)
        {
            for (int i = 0; i < _halfBuffer.Count; i++)
            {
                VRCard card = _halfBuffer[i];
                if (card == null || card.IsHeld || card.IsFlying || _lastHalfCards.Contains(card))
                    continue;
                card.PlayAppear();
                VRLog.Info("Cards", $"Card appear: '{card.name}' (docked action card shown for the new/active " +
                                    $"character) — scale-in + fade ({VRCard.DockAppearSeconds:F2}s) instead of a pop.");
            }
        }

        // Issue 1 APPEAR (selected slot cards): a card that lands in a board SLOT because we switched
        // TO a character who ALREADY had cards lying there — now a slot occupant, but NOT visible in
        // any zone last rebuild (it was parked while the other character was active) — scales + fades
        // IN, in place at its slot home (PlaceCard already asserted it), instead of flying up from
        // below. A card that WAS visible last rebuild (the player just dropped it in from the fan)
        // stays out of this so its release glide is preserved. Suppressed on the first board rebuild.
        if (!_dockAnimSuppressed)
        {
            for (int s = 0; s < 2; s++)
            {
                VRCard? card = _tray.Occupant(s);
                if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing
                    || _lastVisibleCards.Contains(card))
                    continue;
                card.PlayAppear();
                VRLog.Info("Cards", $"Card appear: '{card.name}' (selected card shown in slot {s + 1} for a " +
                                    $"character who already had cards lying there) — scale-in + fade " +
                                    $"({VRCard.DockAppearSeconds:F2}s) in place instead of a fly-from-below.");
            }
        }

        // Issue 2 "no storm" guard: the first Rebuild after a board build / teardown populated the
        // board silently; every later change (the real character/turn switches) now animates.
        _dockAnimSuppressed = false;

        VRLog.Debug("Cards", $"Rebuild: mode={mode} fan={_fanBuffer.Count} tray={trayVisible} half={_halfBuffer.Count}.");
    }

    /// <summary>
    /// FOREIGN-FACE ADOPTION LEDGER for the character-focus feature, and the hand-back that goes
    /// with it.
    ///
    /// <para>WHY THIS EXISTS. A read-only focus view renders ANOTHER character's real
    /// <c>AbilityCardUI</c> widgets: <c>AdoptedCard</c> → <c>VRCard.AttachGameCard</c> →
    /// <c>CardFace.Adopt</c> REPARENTS the live <c>fullAbilityCard</c> into a VR card and records
    /// its original parent/sibling/anchors/pose for a full restore. That is the same path the local
    /// fan has always used across a multi-merc hand switch, and the clone-based readers
    /// (<c>RemoteAbilityCardSource</c> / <c>RemoteCardArt</c>, which <c>Instantiate</c> and then
    /// explicitly reset the clone's rotation and scale) are provably unaffected by it. But it is
    /// still the part of this feature with the least hardware evidence, so:</para>
    /// <list type="bullet">
    /// <item>the faces are handed back the moment the focus LEAVES a character —
    ///   <c>VRCardFactory.ReleaseHand</c> restores every one of that hand's widgets — rather than
    ///   left adopted until the scenario ends;</item>
    /// <item>and each switch logs WHAT was adopted and WHETHER the restore landed: the character,
    ///   how many widgets the mod still held, and how many faces are still parented under a VR
    ///   card afterwards. A healthy switch reads "restored N/N, 0 still held". Anything else names
    ///   the character and the count, which is what a log has to do for a screenshot report.</item>
    /// </list>
    /// </summary>
    private void ReleaseStaleFocusHand(CardsHandUI? nowHand)
    {
        CardsHandUI? nowFocusHand = Board.CharacterFocus.ReadOnlyView ? nowHand : null;
        if (ReferenceEquals(_focusAdoptedHand, nowFocusHand))
            return;

        CardsHandUI? was = _focusAdoptedHand;
        _focusAdoptedHand = nowFocusHand;

        if (was != null)
        {
            string wasName = Board.CharacterFocus.Describe(was.PlayerActor);
            int held = CountFocusAdopted(was);
            _factory.ReleaseHand(was);
            int stillHeld = CountFocusAdopted(was);
            int stillParented = CountFacesUnderVrCards(was);
            VRLog.Info("Cards", $"[Focus] RESTORED '{wasName}': handed {held} adopted card face(s) back " +
                                $"to the game (VR cards still held afterwards: {stillHeld}; faces still " +
                                $"parented under a VR card: {stillParented} — both MUST be 0). " +
                                "CardFace.Restore replays the recorded parent, sibling index, anchors, " +
                                "pose and active flag, so the character's 2D hand is the object it was " +
                                "before we borrowed it.");
        }

        if (nowFocusHand != null)
        {
            VRLog.Info("Cards", $"[Focus] ADOPTING '{Board.CharacterFocus.Describe(nowFocusHand.PlayerActor)}': " +
                                $"{CountHandWidgets(nowFocusHand)} live card widget(s) available on this " +
                                "client (the game builds a populated CardsHandUI per player actor on every " +
                                "client — Choreographer.cs:925/1112). Their faces are BORROWED, never " +
                                "copied and never mutated; the matching [Focus] RESTORED line reports the " +
                                "hand-back.");
        }
    }

    /// <summary>How many of <paramref name="hand"/>'s widgets the mod currently holds a VR card
    /// for. 0 after a clean release.</summary>
    private int CountFocusAdopted(CardsHandUI hand)
    {
        int n = 0;
        try
        {
            List<AbilityCardUI> cards = hand.cardsUI;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && _factory.Find(cards[i]) != null)
                    n++;
            }
        }
        catch { /* half-torn hand — the count is diagnostic, never a gate */ }
        return n;
    }

    /// <summary>How many of <paramref name="hand"/>'s faces are still parented under a mod VR card
    /// — the direct evidence that a restore did NOT land. 0 after a clean release.</summary>
    private static int CountFacesUnderVrCards(CardsHandUI hand)
    {
        int n = 0;
        try
        {
            List<AbilityCardUI> cards = hand.cardsUI;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCardUI c = cards[i];
                if (c == null || c.fullAbilityCard == null)
                    continue;
                if (c.fullAbilityCard.GetComponentInParent<VRCard>() != null)
                    n++;
            }
        }
        catch { /* diagnostic only */ }
        return n;
    }

    /// <summary>Live widget count of a hand (diagnostic; 0 while the hand is mid-build).</summary>
    private static int CountHandWidgets(CardsHandUI hand)
    {
        try { return hand.cardsUI != null ? hand.cardsUI.Count : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// THE ONE HAND-FAN FILL — "a character's hand cards are shown, in every phase, for every
    /// character" (user ruling 2026-08-08: "'Keine Handkarten' soll wirklich nur dann kommen, wenn
    /// der Character auch wirklich keine Handkarten hat, egal in welcher Phase — ansonsten sollen
    /// die Handkarten angezeigt werden").
    ///
    /// <para>WHY IT IS ONE METHOD. The mod used to have TWO hand fills with different rules: the
    /// read-only FOCUS branch, which took every <c>CardPileType.Hand</c> widget unconditionally,
    /// and the vanilla <c>CardsSelection</c> branch, which took them only while
    /// <c>CardsGameApi.IsSelectionPhase</c> was true. That asymmetry IS the reported bug: after
    /// "Fortfahren", the character the GAME still presents runs the vanilla branch with
    /// <c>selecting == false</c> and got an empty fan (hardware log: <c>fan state:
    /// mode=CardsSelection, widgets=23, fanBuffer=0</c>), while every character the player FOCUSED
    /// ran the focus branch and kept its cards. One fill, one rule, no way for the two to drift.</para>
    ///
    /// <para>WHAT IS AND IS NOT FILTERED. Long-rest placeholders are not cards. Non-hand piles are
    /// not the hand (a chosen ROUND card has already flipped to <c>CardPileType.Round</c> on every
    /// client — AbilityCardUI.cs:1098/1186 via ProxySelectCard → ToggleSelect, CardsHandUI.cs:2714).
    /// And a card has exactly ONE VR visual, so anything the ROUND-CARD DOCK already took this
    /// rebuild is skipped — otherwise the fan and the dock would fight over its home every rebuild
    /// (both call <c>VRCard.SetHome</c>). Callers therefore fill <c>_halfBuffer</c> FIRST.</para>
    ///
    /// <para>NO INTERACTION IS IMPLIED. This method decides VISIBILITY only. Whether the cards may
    /// be touched is decided elsewhere and unchanged: <c>grabbable</c> (per-card stamp) plus
    /// <c>CardFan.SetReadOnly</c> (the laser/remove veto). Showing a hand never opened an input
    /// path — the same separation the focus view already relies on.</para>
    /// </summary>
    private void FillHandFan()
    {
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            if (widget == null || widget.AbilityCard == null || widget.IsLongRest)
                continue;
            if (widget.CardType != CardPileType.Hand)
                continue;
            VRCard? already = _factory.Find(widget);
            if (already != null && _halfBuffer.Contains(already))
                continue;
            _fanBuffer.Add(AdoptedCard(widget));
        }
    }

    private VRCard AdoptedCard(AbilityCardUI widget)
    {
        VRCard card = _factory.GetOrCreate(widget);
        if (card.NeedsFace)
            card.AttachGameCard(widget); // re-adopt after a dialog yielded the face
        HookCard(card);
        return card;
    }

    // ------------------------------------------------- focus slot-card diagnostics --
    //
    // ONE LINE PER FOCUS SWITCH that makes an empty slot self-explaining (user requirement
    // 2026-08-08). Change-deduped on (character, resolved count, reason) so a per-frame rebuild is
    // silent while a genuine switch — or a slot set that changes under a live focus, e.g. the
    // watched character's turn resolving its cards into the piles — is always logged.

    private int _loggedSlotFocusId;
    private int _loggedSlotCount = -1;
    private string? _loggedSlotReason;

    /// <summary>
    /// State the slot-card outcome for the focused character: how many cards were resolved, FROM
    /// WHICH source — and when zero, which of the four possible reasons it was. The four are
    /// deliberately distinguishable, because they call for different answers: a shut RevealGate is
    /// the anti-cheat rule working, a long rest is correct-and-permanent for the round, an empty
    /// model list is "not chosen yet or already played out", and resolved &lt; model is a transient
    /// widget gap that the next rebuild retries.
    /// </summary>
    private void LogFocusSlotCards(CardsHandUI hand, bool dockOpen, string dockSource)
    {
        int resolved = _halfBuffer.Count;
        int inModel = Board.CharacterFocus.ModelRoundCardCount(hand);
        string name = Board.CharacterFocus.Describe(hand.PlayerActor);
        int id = Net.NetFigures.StableActorId(hand.PlayerActor);

        string detail;
        if (!dockOpen)
            detail = $"0 resolved — {dockSource}. Nothing is drawn until the reveal; this is the " +
                     "anti-cheat gate holding, not a failure.";
        else if (resolved > 0)
            detail = $"{resolved} resolved from {dockSource}, docked in the board's card slot(s) " +
                     "READ-ONLY (no poke zones armed, canvas not registered with the laser, " +
                     "Grabbable=false). No card identity was added to our wire — this is the same " +
                     "host-replicated list the remote control board already renders a peer's " +
                     "played cards from.";
        else if (CardsGameApi.IsLongResting(hand) || CardsGameApi.HasLongRested(hand))
            detail = "0 resolved — the character is LONG RESTING this round, so it plays no cards " +
                     "at all. The empty slots are correct for the whole round.";
        else if (inModel == 0)
            detail = "0 resolved — the model exposes no chosen cards for this character right now " +
                     "(CCharacterClass.RoundAbilityCards is empty): either the round's cards are " +
                     "not committed yet, or this character's turn is already over and the game " +
                     "moved them to the discard/burnt pile (GameState.cs:2286).";
        else
            detail = $"0 resolved although the model names {inModel} chosen card(s) — no live " +
                     "AbilityCardUI widget for them exists on this client yet (hand mid-(re)build). " +
                     "Transient: the next rebuild retries, and the focus survives it.";

        if (id == _loggedSlotFocusId && resolved == _loggedSlotCount && detail == _loggedSlotReason)
            return;
        _loggedSlotFocusId = id;
        _loggedSlotCount = resolved;
        _loggedSlotReason = detail;
        VRLog.Info("Cards", $"[Focus] slot cards for '{name}': {detail}");
    }

    private void CollectRoundCards(CardsHandUI hand, List<VRCard> into)
    {
        // LONG REST (user report 2026-08-04: "die zwei Karten von der vorherigen Runde [sind]
        // erschienen und haben dann nochmal die Animation abgespielt ... das Board [soll] an den
        // Kartenstellen leer bleiben"). ROOT CAUSE: on a long-rest confirm the actor's own turn
        // starts (IsActionTurn true, mode still ActionSelection) with ZERO cards played this
        // round — but the phase machine's card pair (CardsActionControlller.topCard/bottomCard)
        // is only ever re-written by Init(...) when cards ARE played
        // (CardsActionControlller.cs:91-111), which never runs for a long rest. The singleton
        // therefore still holds the PREVIOUS round's FullAbilityCards, and the supplement below
        // resurrected exactly that stale pair onto the board (hardware log: turn-clear flights
        // at 13824/13825, then confirm at 14501 → "Card appear" ×2 at 14534/14535 → the pair
        // flies to the piles AGAIN at 14721). Gate on the game's OWN long-rest state: the
        // rules model plays no round cards while CharacterClass.LongRest is set (set on
        // selection, CardsHandUI.cs:1950; cleared when the rest resolves, GameState.cs:2569)
        // and HasLongRested covers the tail of the same turn after that clear (set in
        // GameState.PlayerLongRested, reset at next round start, CCharacterClass.cs:1183) —
        // so the board's round slots stay EMPTY for the whole long-rest turn, keyed on real
        // state, not a heuristic. RoundAbilityCards is empty then anyway (IsInRound already
        // false), so this only suppresses the stale static pair.
        if (CardsGameApi.IsLongResting(hand) || CardsGameApi.HasLongRested(hand))
        {
            if (!_loggedLongRestEmptyBoard)
            {
                _loggedLongRestEmptyBoard = true;
                VRLog.Info("Cards", "Round-card dock EMPTY (long rest): the actor is long-resting, so no round " +
                                    "cards exist this round — the phase machine's stale topCard/bottomCard pair " +
                                    "(previous round) is ignored, nothing docks, nothing replays a pile flight.");
            }
            return;
        }
        _loggedLongRestEmptyBoard = false;
        // The AUTHORITATIVE per-character selector is the acting hand's own round pile
        // (IsInRound → CharacterClass.RoundAbilityCards) — always exactly the two played
        // cards of THIS actor. The phase machine's pair (CardsActionControlller.topCard/
        // bottomCard) is a static singleton that can lag a same-mode turn hand-off, so it is
        // only ADDED (covers the extra-turn pile, where cards are not in RoundAbilityCards) —
        // never used ALONE, so a stale pair can no longer make us dock the previous
        // character's cards (the second-character deadlock).
        //
        // FOCUS ADDENDUM (2026-08-08): the supplement is additionally scoped to the hand whose turn
        // it actually is. The phase machine is a STATIC singleton holding the ACTING character's
        // pair, and the round-card dock now also fills for a focused character that is NOT acting —
        // so an unscoped supplement could dock the acting character's cards onto a watched
        // character's board. The widget loop below only walks THIS hand's own widgets, so a foreign
        // FullAbilityCard could never match anyway; making the scope explicit means that safety is a
        // stated rule instead of a coincidence of which buffer we happen to be iterating.
        FullAbilityCard? first = null;
        FullAbilityCard? second = null;
        if (ReferenceEquals(Board.CharacterFocus.TurnActor, hand.PlayerActor))
            CardsGameApi.GetActionCards(out first, out second);
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            bool isActionCard =
                CardsGameApi.IsInRound(hand, widget.AbilityCard) ||
                (first != null && widget.fullAbilityCard == first) ||
                (second != null && widget.fullAbilityCard == second);
            if (isActionCard)
            {
                VRCard card = AdoptedCard(widget);
                if (!into.Contains(card))
                    into.Add(card);
                // The card is on the board again, so its previous "left the dock without flying"
                // verdict is spent: the NEXT time it leaves, that is a new event and may log again
                // (one refusal line per focus switch — see LogFlightRefused). This is also what
                // bounds the table: only cards that were docked can ever be in it.
                _loggedFlightRefusal.Remove(widget);
            }
        }
    }

    // ---------------------------------------------------------------- fly-to-pile (issue 5) --

    /// <summary>
    /// Every destination a DOCKED ROUND CARD can have, read off the owner's authoritative
    /// <c>CCharacterClass</c> lists. The game's own end-of-turn drain is
    /// <c>CCharacterClass.DiscardRoundAbilityCards</c> (CCharacterClass.cs:505, called from
    /// GameState.cs:2286/2271), which routes every round / extra-turn card through
    /// <c>MoveAbilityCardToPile</c> (CCharacterClass.cs:418) into EXACTLY one of the lists below —
    /// and <c>MoveAbilityCard</c> (CCharacterClass.cs:273) removes it from the source list first,
    /// so the lists are mutually exclusive and this classification is total.
    /// </summary>
    private enum RoundCardExit
    {
        /// <summary>No CAbilityCard / no owning actor — the model cannot be asked. Never fly.</summary>
        NoModel,
        /// <summary>Still in <c>RoundAbilityCards</c> / <c>ExtraTurnCards</c>: the card did NOT move.
        /// The dock's CONTENT changed (a character focus switch) — that is not a card going anywhere.</summary>
        StillRound,
        /// <summary><c>DiscardedAbilityCards</c> → the DISCARD stack.</summary>
        Discarded,
        /// <summary><c>LostAbilityCards</c> (a burn) → the BURNT stack.</summary>
        Lost,
        /// <summary><c>PermanentlyLostAbilityCards</c> → the BURNT stack (same stack as Lost; the 2D
        /// hand shows both under one "burnt" header, CardsHandUI.cs:1333/1340).</summary>
        PermanentlyLost,
        /// <summary><c>ActivatedCards</c> — a persistent/round-long card went to the ACTIVE COLUMN,
        /// not to a pile at all. No flight: the active-cards viewer picks it up.</summary>
        Activated,
        /// <summary><c>HandAbilityCards</c> — the selection was undone
        /// (<c>ClearRoundAbilityCards</c>, CCharacterClass.cs:522): back in the fan, no flight.</summary>
        Hand,
        /// <summary>In none of the lists — a SUPPLY card is consumed and removed from the model
        /// outright (CCharacterClass.cs:420-432), or the actor/widget went away. No flight.</summary>
        OffModel,
    }

    /// <summary>
    /// Issue 5 (user): animate a just-cleared PLAYED round card flying into its destination pile
    /// instead of instantly vanishing.
    ///
    /// <para>THE TRIGGER IS THE MODEL, NOT THE DOCK (user report 2026-08-08, bug 1). This used to
    /// fire on "the card was docked last rebuild and is in no zone now", which is a statement about
    /// the DOCK's contents — and a character focus switch empties the dock without moving a single
    /// card, so merely LOOKING at another character replayed both round cards into the discard pile
    /// (hardware log LogOutput.log:3300/3344/3384/3767/3803/3894/3930/4549, eight identical
    /// turn-clear flights of the same two cards in one session, no turn ever ended between them).
    /// The dock membership is still the pre-filter — only a card that was actually lying in the
    /// board's round slots may fly — but the DECISION is now
    /// <see cref="RoundCardExitOf"/>: the card flies only when it genuinely LEFT
    /// <c>RoundAbilityCards</c> FOR a pile, and it flies to the pile it actually entered.</para>
    ///
    /// <para>Returns true only when the fly was actually launched — the caller then skips the
    /// instant park; the fly's completion callback parks the card on arrival. Returns false (→ the
    /// caller's shrink-and-fade / instant hide fallback) when the card isn't a cleared round card,
    /// is already flying, isn't visible, did not move in the model, moved somewhere that is not a
    /// pile, or the target pile is off / not built.</para>
    /// </summary>
    private bool TryStartFlyToPile(CardsHandUI hand, VRCard card)
    {
        if (card.GameCard == null || card.IsFlying)
            return false;
        if (!_lastHalfCards.Contains(card))
            return false; // only the round cards docked last rebuild — never a fan/browse/etc. card
        if (!card.gameObject.activeInHierarchy)
            return false; // already parked/pooled — nothing to animate from

        RoundCardExit exit = RoundCardExitOf(hand, card, out CPlayerActor? owner);
        PileKind fate;
        switch (exit)
        {
            case RoundCardExit.Discarded:
                fate = PileKind.Discard;
                break;
            case RoundCardExit.Lost:
            case RoundCardExit.PermanentlyLost:
                fate = PileKind.Burnt;
                break;
            default:
                // The dock changed but the CARD did not go to a pile. Say so once, so a regression
                // (a spurious flight, or a missing one) is visible in the next hardware log.
                LogFlightRefused(card, exit, owner);
                return false;
        }
        // The card really moved — a later dock change for it is a different event and may log again.
        _loggedFlightRefusal.Remove(card.GameCard!);

        if (!_piles.TryGetPileWorld(fate, out Vector3 worldPos, out float slabWidth))
            return false; // pile offscreen / not built → fall back to the instant hide

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, worldPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        // A burnt-fate round card may have a pending artwork HOLD from the pile watcher (the burn
        // artwork played on the docked card mid-turn). This flight consumes the burn — drop the
        // hold so its later release can never fly a second slab for the same card.
        if (card.GameCard != null)
            ClearBurnHold(card.GameCard);
        VRCard flying = card;
        PileKind dest = fate;
        // MP parity (report 6): peers replay this exact flight (slot → discard/burnt stack) against
        // THEIR copy of this player's board pose — 2 bytes, no per-frame transforms. Because the
        // decision above is now the MODEL's, the peer's mirrored flight inherits both halves of the
        // fix: it fires only for a real move, and its destination anchor is the real destination.
        ReportCardFx(SlotAnchor(_tray.SlotOf(card)), PileAnchor(fate));
        card.FlyToPile(worldPos, slabWidth, FlyToPileSeconds, arcUp, () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"Fly-to-pile: '{flying.name}' reached the {dest} pile — parked.");
        }, minArc);
        // A played card whose fate is BURNT (a lost action) is a burn like any other — tag it with
        // the same BURN ANIM token the dedicated burn paths use so ONE grep proves every burn case.
        string tag = fate == PileKind.Burnt ? "BURN ANIM [turn-clear]" : "Fly-to-pile [turn-clear]";
        VRLog.Info("Cards", $"{tag}: CARD FLIGHT '{CardsGameApi.CardName(card.GameCard!)}' " +
                            $"(owner '{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') — WHY: it LEFT " +
                            $"CCharacterClass.RoundAbilityCards/ExtraTurnCards and ENTERED " +
                            $"CCharacterClass.{ModelListName(exit)}, so it flies to " +
                            $"the {fate} stack ({FlyToPileSeconds:F2}s, arc {arcHeight:F3} m over the board, " +
                            "orientation locked). Trigger and destination are both the authoritative model — a " +
                            "dock/focus change alone can never produce this line. VR presentation only; the game's " +
                            "own pile state is untouched.");
        return true;
    }

    /// <summary>The <c>CCharacterClass</c> list name behind an exit, for the flight/refusal log.</summary>
    private static string ModelListName(RoundCardExit exit) => exit switch
    {
        RoundCardExit.StillRound => "RoundAbilityCards/ExtraTurnCards",
        RoundCardExit.Discarded => "DiscardedAbilityCards",
        RoundCardExit.Lost => "LostAbilityCards",
        RoundCardExit.PermanentlyLost => "PermanentlyLostAbilityCards",
        RoundCardExit.Activated => "ActivatedCards",
        RoundCardExit.Hand => "HandAbilityCards",
        RoundCardExit.OffModel => "no CCharacterClass list (supply card consumed / actor gone)",
        _ => "no readable model",
    };

    /// <summary>
    /// REFUSAL LINE (user report 2026-08-08, bug 1): a docked round card left every zone but the
    /// model says it did not go to a pile — so there is NO flight, and that silence must be
    /// provable in the log rather than merely observed. One line per widget per verdict change
    /// (<see cref="_loggedFlightRefusal"/>), i.e. one per focus switch, never per frame: this whole
    /// path only runs from Rebuild, and only for the ≤2 cards the dock held last rebuild.
    /// </summary>
    private void LogFlightRefused(VRCard card, RoundCardExit exit, CPlayerActor? owner)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null)
            return;
        if (_loggedFlightRefusal.TryGetValue(widget, out RoundCardExit previous) && previous == exit)
            return;
        _loggedFlightRefusal[widget] = exit;

        string why = exit switch
        {
            RoundCardExit.StillRound =>
                "the card is STILL in the owner's RoundAbilityCards/ExtraTurnCards — nothing moved, the " +
                "round-card DOCK just changed its contents (a character focus switch does exactly that). " +
                "A flight here would be the reported bug.",
            RoundCardExit.Activated =>
                "the card was ACTIVATED (CCharacterClass.ActivatedCards) — a persistent/round-long card goes " +
                "to the ACTIVE COLUMN, not to a pile, so no pile flight exists to play.",
            RoundCardExit.Hand =>
                "the card went back to HandAbilityCards (the selection was undone) — it belongs in the hand " +
                "fan again, not in a pile.",
            RoundCardExit.OffModel =>
                "the card is in NO CCharacterClass list any more (a SUPPLY card is removed from the model when " +
                "used, CCharacterClass.cs:420-432) — there is no pile it entered.",
            _ =>
                "the model could not be read for this card (no CAbilityCard or no owning actor) — refusing " +
                "rather than guessing a pile.",
        };
        VRLog.Info("Cards", $"Fly-to-pile REFUSED: '{CardsGameApi.CardName(widget)}' (owner " +
                            $"'{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') left the round-card dock " +
                            $"but NOT for a pile — model says {ModelListName(exit)}. {why} The card is parked/faded " +
                            "in place instead, and nothing is announced to peers.");
    }

    /// <summary>
    /// Issue 5 / bug 1+2 (user 2026-08-08): where a docked round card stands in the game's
    /// AUTHORITATIVE model RIGHT NOW — the single source for both "may it fly at all" and "to which
    /// stack". Read-only: no game state is touched.
    ///
    /// <para>The card's OWN owner (<c>AbilityCardUI.PlayerActor</c>) is authoritative — in a
    /// two-character sequential turn a card cleared during the OTHER character's turn belongs to a
    /// different actor than the currently-presented hand, so its fate must be read from its own
    /// character's lists. Falls back to the presented hand if the widget has no owner.</para>
    /// </summary>
    private static RoundCardExit RoundCardExitOf(CardsHandUI? hand, VRCard card, out CPlayerActor? owner)
    {
        AbilityCardUI? widget = card.GameCard;
        CAbilityCard? ac = widget != null ? widget.AbilityCard : null;
        owner = widget != null ? widget.PlayerActor : null;
        if (owner == null && hand != null)
            owner = hand.PlayerActor;
        if (ac == null || owner == null)
            return RoundCardExit.NoModel;

        CCharacterClass klass = owner.CharacterClass;
        // ORDER: the "did it move at all" question first — everything below is a destination, and a
        // card that is still on the board has none.
        if (klass.RoundAbilityCards.Contains(ac) || klass.ExtraTurnCards.Contains(ac))
            return RoundCardExit.StillRound;
        if (klass.DiscardedAbilityCards.Contains(ac))
            return RoundCardExit.Discarded;
        if (klass.LostAbilityCards.Contains(ac))
            return RoundCardExit.Lost;
        if (klass.PermanentlyLostAbilityCards.Contains(ac))
            return RoundCardExit.PermanentlyLost;
        // ActivatedCards is the RAW List<CBaseCard> field (CCharacterClass.cs:97). The public
        // ActivatedAbilityCards property is a LINQ projection that allocates a new list on every
        // read — never call it from a rebuild path.
        if (klass.ActivatedCards.Contains(ac))
            return RoundCardExit.Activated;
        if (klass.HandAbilityCards.Contains(ac))
            return RoundCardExit.Hand;
        return RoundCardExit.OffModel;
    }

    /// <summary>
    /// Issue 5: the destination pile of a card that IS in a pile — a card sitting in the owner's
    /// LOST or PERMANENTLY-LOST list is a burned card (BURNT stack); everything else answers
    /// DISCARD. Kept as the burn watcher's question ("is this widget burnt in its owner's piles?",
    /// <see cref="IsFreshBurn"/>); the fly-to-pile TRIGGER does NOT use it, because "not burnt"
    /// must not be read as "therefore discarded" — see <see cref="RoundCardExitOf"/>.
    /// </summary>
    private static PileKind PileFateOf(CardsHandUI hand, VRCard card) =>
        RoundCardExitOf(hand, card, out _) switch
        {
            RoundCardExit.Lost or RoundCardExit.PermanentlyLost => PileKind.Burnt,
            _ => PileKind.Discard,
        };

    /// <summary>
    /// BURN ANIM: is this VR card PARKED in the factory pool (invisible, pose meaningless)?
    /// <see cref="VRCardFactory.Park"/> re-homes a card onto the pool root at the local origin, so a
    /// parked card's <c>transform.position</c> is the pool root's, NOT where the card last was. Every
    /// other parent — the hand fan (even a CLOSED, i.e. inactive, one), the tray, the half dock, the
    /// browse arc — keeps the card's true world pose, which is what a burn animation must fly from.
    /// </summary>
    private bool IsParked(VRCard card) => card.transform.parent == _factory.PoolRoot;

    /// <summary>
    /// BURN ANIM: has <paramref name="card"/> JUST been burned — i.e. is its ability card in the
    /// owner's LOST / PERMANENTLY-LOST list (<see cref="PileFateOf"/>, the game's own authoritative
    /// piles) while the burn watcher's previous-tick baseline (<see cref="_knownBurntWidgets"/>) did
    /// NOT yet contain its widget? Read-only on game state.
    ///
    /// The baseline is only trustworthy once <see cref="TickBurnToPile"/> has seeded it for THIS
    /// hand, so the check demands <c>hand == _burnWatchHand</c>: on a hand change (character tab
    /// switch, scenario load) the baseline is re-seeded silently there, and until that happened a
    /// hand's long-burned cards must never animate retroactively — the same "no storm" discipline
    /// the dock appear/disappear animations use.
    /// </summary>
    private bool IsFreshBurn(CardsHandUI? hand, VRCard card)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null || hand == null || !ReferenceEquals(hand, _burnWatchHand))
            return false;
        // OWNERSHIP GATE (user report 2026-08-07: "die verbrannte Karte taucht ploetzlich wieder
        // auf dem Board an derselben Stelle auf, obwohl der Charakter gewechselt wurde").
        //
        // ROOT CAUSE this line fixes. The baseline (_knownBurntWidgets) is re-seeded from the
        // PRESENTED hand's burnt pile on every hand change, and PileFateOf deliberately reads the
        // card's OWN owner. Without an ownership test the two disagree the instant characters
        // switch: character A's long-burned card is (a) not in B's freshly seeded baseline and
        // (b) still "lost" in A's own piles — so it re-qualified as a FRESH burn for hand B. The
        // park sweep then re-took ownership of it (TryStartBurnFly → artwork hold → "returns
        // true"), which by contract means "do not park, leave it lying exactly where it is", and
        // TickBurnToPile's hold hygiene dropped the hold again the same frame (the widget is not
        // in B's burnt buffer) — so the next Rebuild started a brand-new hold. That loop is the
        // reported reappearance, and the peer hardware log shows it verbatim: 17 consecutive
        // "BURN ANIM: holding 'ABILITY_CARD_GravelVortex' ON THE BOARD" lines (remote/LogOutput.log
        // 17442-18340) with no release, straddling a Cryonaris→Lastglowworm switch; Simulacrum the
        // same, and it even became grabbable in the hand fan again (16166 ff.).
        //
        // A burn belongs to the character that burned it. If the presented hand is not that
        // character's, this path has nothing to say about the card — it parks like any other
        // off-board card, and the OWNER's own pending flight was already flushed by
        // FlushBurnHolds when the hand changed.
        CPlayerActor? owner = widget.PlayerActor;
        if (owner != null && hand.PlayerActor != null && !ReferenceEquals(owner, hand.PlayerActor))
            return false;
        if (_knownBurntWidgets.Contains(widget))
            return false; // already in the burnt pile before this tick — not a fresh burn
        return PileFateOf(hand, card) == PileKind.Burnt;
    }

    /// <summary>
    /// BURN ANIM (user request): fly a card the game JUST burned into the BURNT stack with the very
    /// same over-the-board arc the discard flow uses (<see cref="VRCard.FlyToPile"/>) — short rest
    /// (random sacrifice), long rest (chosen burn) and damage-induced loss all funnel through here
    /// via their own call sites, so a burned card slides into the pile instead of blinking out.
    /// Purely VR presentation: the game already moved the card into its Lost pile by the time we see
    /// it, nothing here touches game data, and the card's own burn FX (the game's dissolve + the
    /// card-bounded CardSmoke, see <see cref="BurnCardFx"/>) keeps playing on the adopted face while
    /// it flies — no second effect is invented.
    ///
    /// Returns true when the burn path OWNS the card this frame — either the flight launched (the
    /// completion callback parks the card on arrival) or the flight is HELD while the game's burn
    /// artwork still plays ON the lying card (<see cref="TryTakeBurnFlightSlot"/>, the same gate
    /// the pile watcher uses — the user-ruled order: artwork first, THEN the pile flight). In the
    /// held case the caller must leave the card exactly where it lies (no park, no vanish); the
    /// per-tick watcher (<see cref="TickBurnToPile"/>) re-offers the widget every tick — it stays
    /// un-claimed while held — and launches the very same flight the moment the artwork completes
    /// or the deadline passes, from the card's true position, which this ownership preserved.
    /// Returns false — leaving the caller's park and <see cref="TickBurnToPile"/>'s transient-slab
    /// fallback to take over — when the card is held by a hand, already animating, no longer live
    /// (a pooled/parked or inactive card cannot animate: its Update does not run), not a fresh
    /// burn, or the burnt pile is off / not built.
    ///
    /// Claiming: on LAUNCH the widget is added to <see cref="_knownBurntWidgets"/> immediately, so
    /// the burn watcher running LATER in this same frame treats it as already-known and cannot
    /// start a second animation for it. A HELD widget is deliberately NOT claimed.
    /// </summary>
    private bool TryStartBurnFly(CardsHandUI? hand, VRCard card, string origin)
    {
        if (card.IsHeld || card.IsFlying || card.IsVanishing)
            return false;
        if (!card.gameObject.activeInHierarchy)
            return false; // inactive/parked: no Update ticks, so it could never animate
        if (!IsFreshBurn(hand, card))
            return false;
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out Vector3 burntPos, out float slabWidth))
            return false; // burnt pile off / not built — no destination to fly to

        AbilityCardUI widget = card.GameCard!;
        // ORDER OF THE BURN (same ruling as TryAnimateBurn): the park sweep can see the fresh burn
        // BEFORE the pile watcher does (Rebuild runs first in the frame, and the game commits the
        // pile move frames ahead of starting the card's burn timeline). Flying here immediately
        // would clip the artwork the user explicitly asked to watch on the lying card — so this
        // path waits behind the exact same bounded gate. While the gate holds, the card is OWNED:
        // it stays lying at its true pose (the sweep must not park it), and the watcher's per-tick
        // re-offer performs the launch when the artwork ends.
        if (!TryTakeBurnFlightSlot(widget, card))
            return true; // held: owned by the burn path — caller keeps hands off the card
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        Vector3 from = card.transform.position;
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(from, burntPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        _knownBurntWidgets.Add(widget); // claim before the watcher's diff sees it (no double animation)
        _lastCardWorldPos.Remove(widget); // consumed — the real card is flying, no fallback slab wanted
        _lastCardWorldRot.Remove(widget);
        // MP parity (report 6): this launch site was the ONE burn flight that never reported —
        // every other pile flight (turn-clear, pile-watch, fallback slab) announces itself, so a
        // peer watching this player's board saw those but missed a burn that fired from the park
        // sweep. Same 2-byte semantic endpoint pair as the others; peers replay a card-back slab
        // arcing off this player's board into their burnt stack.
        ReportCardFx(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
        VRCard flying = card;
        card.FlyToPile(burntPos, slabWidth, FlyToPileSeconds, arcUp, () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"BURN ANIM: '{flying.name}' reached the Burnt pile — parked.");
        }, minArc);
        VRLog.Info("Cards", $"BURN ANIM [{origin}]: '{CardsGameApi.CardName(widget)}' burned — real VR card flies " +
                            $"from {from} → Burnt pile ({FlyToPileSeconds:F2}s, arc {arcHeight:F3} m over the " +
                            "board, orientation locked). VR presentation only; the game's own pile state is " +
                            "untouched.");
        return true;
    }

    /// <summary>
    /// Issue A/B: the board's UP axis in world space — the direction a fly-to-pile bows so it arcs
    /// OVER the (possibly tilted) control board. Falls back to world-up before the tray exists.
    /// </summary>
    private Vector3 BoardUp() => _tray.Root != null ? _tray.Root.up : Vector3.up;

    // ---------------------------------------------------------------- MP card-FX anchors --
    //
    // Report 6 ("ALLE Kartenanimationen der Mitspieler sollen im Multiplayer sichtbar sein"): every
    // card animation the local VR launches is announced to peers as a SEMANTIC endpoint pair
    // (Net.NetCardFx), which they replay against their own copy of this player's board/hand pose.
    // These two helpers translate the driver's local notions — a slot index, a PileKind — into the
    // wire anchors. A card whose slot is unknown (-1: never docked, or already evicted) degrades to
    // the generic Board anchor, which flies from the board centre rather than nowhere.

    /// <summary>
    /// Announce a local card animation to peers — EXCEPT while the board is showing a character
    /// under a read-only focus.
    ///
    /// <para>WHY THE EXCEPTION (character focus, slot cards 2026-08-08). Every anchor on this wire
    /// is a POSITION on the SENDER'S OWN board ("slot 0 → discard stack"), which peers replay
    /// against their copy of that board. Under a read-only focus the local board is showing SOMEBODY
    /// ELSE'S cards, so the flights it plays belong to the watched character's turn, not to ours —
    /// forwarding them would make a peer's mirror of OUR board animate cards that were never on it.
    /// The dock now fills for a focused character that is not even at turn, so those clears happen
    /// far more often than before; suppressing here keeps the whole feature a LOCAL view change, and
    /// the owning client still reports its own flights on its own board exactly as before.</para>
    /// </summary>
    private static void ReportCardFx(Net.CardFxAnchor from, Net.CardFxAnchor to)
    {
        if (Board.CharacterFocus.ReadOnlyView)
            return;
        Net.NetCardFx.Report(from, to);
    }

    /// <summary>Wire anchor for a board slot index (-1 → the generic board anchor).</summary>
    private static Net.CardFxAnchor SlotAnchor(int slot) => slot switch
    {
        0 => Net.CardFxAnchor.Slot0,
        1 => Net.CardFxAnchor.Slot1,
        _ => Net.CardFxAnchor.Board,
    };

    /// <summary>Wire anchor for a destination pile stack.</summary>
    private static Net.CardFxAnchor PileAnchor(PileKind kind) =>
        kind == PileKind.Burnt ? Net.CardFxAnchor.Burnt : Net.CardFxAnchor.Discard;

    /// <summary>
    /// Issue 3 (user): the absolute MINIMUM arc peak (world meters) a fly-to/from-pile must reach so
    /// the card visibly clears the control board's top edge even on a SHORT hop — a flat skim was
    /// unreadable. Scaled by the board's live diorama scale (so it tracks board size/config) at
    /// ~1.5 card-heights of lift. The fly takes the max of this and its distance-proportional arc.
    /// </summary>
    private float BoardArcMin()
    {
        float boardScale = _tray.Root != null ? _tray.Root.lossyScale.x : 1f;
        return boardScale * CardsConfig.CardHeight * 1.5f;
    }

    /// <summary>
    /// Issue B (user, "when I burned a card due to damage I didn't perceive the animation"):
    /// watch the acting hand's BURNT pile membership each tick and animate any card that newly
    /// entered it — the take-damage burn ("burn available / discarded card") and any other
    /// lose-to-burnt path — flying into the burnt stack with the same over-the-board arc as the
    /// turn-clear sweep. The turn-clear round-card fly (<see cref="TryStartFlyToPile"/>) already
    /// owns its cards, so this skips anything it is handling (held / flying / in
    /// <see cref="_lastHalfCards"/>) — no double animation. A hand change re-seeds the baseline
    /// silently so a hand's already-burnt cards never animate retroactively.
    /// </summary>
    private void TickBurnToPile(CardsHandUI? hand)
    {
        if (hand == null)
        {
            // Same stranding risk as the hand CHANGE below: the hold owns the card, so losing the
            // presented hand while one is pending would leave it lying (or let the park sweep
            // swallow it silently). Land it instead.
            FlushBurnHolds("the presented hand went away (no local hand to watch)");
            _burnWatchHand = null;
            return;
        }
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntWidgetBuffer);

        // Re-baseline on a hand change (or first sight): record the current burnt set WITHOUT
        // animating — only cards that cross into it from here on are freshly burned.
        if (!ReferenceEquals(hand, _burnWatchHand))
        {
            // FLUSH, DO NOT DROP (user report 2026-08-07, the "burned card lies on the board
            // forever" half of the reappearance bug). This used to `_burnHoldSince.Clear()`, which
            // silently threw away a burn flight that was mid-artwork-hold. The card was NOT parked
            // (the hold's contract is "the burn path owns it, leave it lying"), and with its hold
            // gone nothing ever launched it — so the previous character's burned card simply stayed
            // on the board. Switching character is exactly when this happens: the take-damage panel
            // closing hands the presented hand straight back to whoever's turn it is, typically a
            // DIFFERENT character, one to two frames after the burn commits.
            //
            // A pending hold is a burn the player already watched; it must land. FlushBurnHolds
            // launches each one immediately (deadline semantics — the artwork has had its moment)
            // so the card flies into the burnt stack and parks, exactly as if the hold had timed
            // out with the same hand still presented. Peers see it too: the flush goes through the
            // same launch sites, so the same Board→Burnt NetCardFx event rides the wire.
            FlushBurnHolds("the presented hand changed to " +
                           $"'{(hand.PlayerActor != null ? CardsGameApi.ActorLabel(hand.PlayerActor) : "?")}'");
            _burnWatchHand = hand;
            _knownBurntWidgets.Clear();
            _burnHoldLogged.Clear();
            for (int i = 0; i < _burntWidgetBuffer.Count; i++)
                if (_burntWidgetBuffer[i] != null)
                    _knownBurntWidgets.Add(_burntWidgetBuffer[i]);
            return;
        }

        for (int i = 0; i < _burntWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _burntWidgetBuffer[i];
            if (widget == null || _knownBurntWidgets.Contains(widget))
                continue;
            TryAnimateBurn(widget); // newly entered the burnt pile this tick
        }

        // The rebuilt baseline both records the new arrivals (so they animate exactly once) and
        // drops any that left (a recovered lost card), so a re-burn later animates again.
        //
        // ROOT CAUSE of the "burn artwork plays, then the card just VANISHES" report
        // (user 2026-08-04; hardware log: "BURN ANIM: holding ... ON THE BOARD" at 11379/13331/
        // 17950/19039 with NOT ONE "waited ... flying to the Burnt pile now" release line in the
        // whole session): this very re-baseline used to add EVERY burnt widget — including one
        // whose flight TryTakeBurnFlightSlot had just DECLINED to keep the artwork playing on the
        // lying card. The next tick's loop above then skipped it as already-known, so
        // TryAnimateBurn was never re-offered, the hold never released, and the card was left for
        // a later Rebuild park sweep to swallow (IsFreshBurn false → the Vanish/Park fallback) —
        // the reported disappearance. A widget whose flight is still HELD must therefore stay OUT
        // of the baseline: it remains "fresh", the watch re-offers it every tick, and when the
        // artwork completes (or the deadline passes) the release actually launches the same
        // FlyToPile arc the discard flow uses.
        _knownBurntWidgets.Clear();
        for (int i = 0; i < _burntWidgetBuffer.Count; i++)
        {
            AbilityCardUI w = _burntWidgetBuffer[i];
            if (w != null && !_burnHoldSince.ContainsKey(w))
                _knownBurntWidgets.Add(w);
        }

        // Hold hygiene: a held widget that LEFT the burnt pile again (a recovered lost card —
        // possible before its flight ever released) has nothing left to fly; drop its hold so
        // neither dictionary accumulates dead widgets across a scenario.
        if (_burnHoldSince.Count > 0)
        {
            _burnHoldPruneScratch.Clear();
            foreach (AbilityCardUI held in _burnHoldSince.Keys)
                if (!_burntWidgetBuffer.Contains(held))
                    _burnHoldPruneScratch.Add(held);
            for (int i = 0; i < _burnHoldPruneScratch.Count; i++)
                ClearBurnHold(_burnHoldPruneScratch[i]);
        }
    }

    /// <summary>Seconds the flight waits for the game's burn artwork to START before giving up on
    /// it (the pile count commits several frames earlier — see TryAnimateBurn).</summary>
    private const float BurnEffectStartGraceSeconds = 0.5f;

    /// <summary>Hard ceiling on the whole wait: however long the artwork runs, a burned card is on
    /// its way to the pile after this. A stranded card on the board is worse than a clipped
    /// animation.</summary>
    private const float BurnEffectMaxHoldSeconds = 3f;

    /// <summary>Burned widgets whose flight is being held back, and when the hold started.</summary>
    private readonly Dictionary<AbilityCardUI, float> _burnHoldSince = new();

    /// <summary>Widgets whose hold has been logged once (one line per burn, not per frame).</summary>
    private readonly HashSet<AbilityCardUI> _burnHoldLogged = new();

    /// <summary>Reused scratch for pruning stale hold entries (allocation-free steady state).</summary>
    private readonly List<AbilityCardUI> _burnHoldPruneScratch = new(4);

    /// <summary>
    /// Drop all artwork-hold state for <paramref name="widget"/>. Called when a flight for the
    /// widget actually launches on ANOTHER path (the turn-clear sweep consumes the burn), when the
    /// widget leaves the burnt pile again (recovered), and when a board switch parks its card —
    /// so a consumed hold can never release a SECOND animation later (the release gate itself
    /// clears these two on a normal in-path release).
    /// </summary>
    private void ClearBurnHold(AbilityCardUI widget)
    {
        _burnHoldSince.Remove(widget);
        _burnHoldLogged.Remove(widget);
    }

    /// <summary>
    /// LAND every burn flight that is still sitting in the artwork hold, right now, and clear the
    /// hold table. The hold's whole contract is "the burn path OWNS this card — the caller must
    /// leave it lying exactly where it is"; so anything that invalidates the hold WITHOUT landing
    /// the flight strands a burned card on the board forever (the reported reappearance). The one
    /// event that used to do that is a presented-hand change: <see cref="TickBurnToPile"/> simply
    /// cleared the table, the widget was no longer offered (it is not in the NEW hand's burnt
    /// pile), and nothing ever launched it. Now the switch flushes instead — each pending burn
    /// flies to the burnt stack immediately, which is what the artwork deadline would have done a
    /// moment later anyway.
    ///
    /// Called with the OLD baseline still in place, so the launch sites see the same world they
    /// were held in. Safe to call with an empty table (no-op, no log).
    /// </summary>
    private void FlushBurnHolds(string reason)
    {
        if (_burnHoldSince.Count == 0)
            return;
        _burnHoldPruneScratch.Clear();
        foreach (AbilityCardUI held in _burnHoldSince.Keys)
            if (held != null)
                _burnHoldPruneScratch.Add(held);
        _burnHoldSince.Clear();
        _burnHoldLogged.Clear();
        for (int i = 0; i < _burnHoldPruneScratch.Count; i++)
        {
            AbilityCardUI widget = _burnHoldPruneScratch[i];
            VRLog.Info("Cards", $"BURN ANIM: FLUSHING the held flight of '{CardsGameApi.CardName(widget)}' — " +
                                $"{reason}. A burned card must never be left lying on the board when the " +
                                "character it belongs to is no longer the presented one.");
            _knownBurntWidgets.Add(widget); // claim first: never two flights for one burn
            LaunchBurnFlight(widget, "hand-switch flush");
        }
        _burnHoldPruneScratch.Clear();
    }

    /// <summary>
    /// May the burned <paramref name="widget"/> fly THIS tick? False = keep it lying on the board
    /// and re-offer it next tick (the caller must not claim it). See the order-of-the-burn note in
    /// <see cref="TryAnimateBurn"/>.
    /// </summary>
    private bool TryTakeBurnFlightSlot(AbilityCardUI widget, VRCard? card)
    {
        float now = Time.unscaledTime;
        if (!_burnHoldSince.TryGetValue(widget, out float since))
        {
            since = now;
            _burnHoldSince[widget] = since;
        }
        float held = now - since;

        bool effectActive = BurnArtworkActive(card);
        bool release = held >= BurnEffectMaxHoldSeconds                     // deadline: always go
                       || (effectActive == false && held >= BurnEffectStartGraceSeconds); // never started / already done

        if (effectActive && held < BurnEffectMaxHoldSeconds)
            release = false; // still burning ON the card — that is the whole point of the wait

        if (!release)
        {
            if (_burnHoldLogged.Add(widget))
                VRLog.Info("Cards", $"BURN ANIM: holding '{CardsGameApi.CardName(widget)}' ON THE BOARD " +
                                    $"while its burn artwork plays (effect {(effectActive ? "running" : "not started yet")}); " +
                                    $"it flies to the Burnt pile afterwards, at the latest after " +
                                    $"{BurnEffectMaxHoldSeconds:F1}s.");
            return false;
        }

        _burnHoldSince.Remove(widget);
        _burnHoldLogged.Remove(widget);
        if (held > 0.01f)
            VRLog.Info("Cards", $"BURN ANIM: '{CardsGameApi.CardName(widget)}' waited {held:F2}s on the board " +
                                $"({(effectActive ? "artwork still running — DEADLINE reached" : "artwork finished")}) " +
                                "— flying to the Burnt pile now.");
        return true;
    }

    /// <summary>True while the game plays its own burn/lost timeline on this card's widget — the
    /// same three tasks <see cref="BurnCardFx"/> keys its on-card diagnostic on.</summary>
    private static bool BurnArtworkActive(VRCard? card)
    {
        CardEffects? fx = card?.FullCard != null ? card.FullCard.cardEffects : null;
        if (fx == null)
            return false;
        try
        {
            return fx.HasEffect(CardEffects.FXTask.BurnCard)
                   || fx.HasEffect(CardEffects.FXTask.LostMode);
        }
        catch
        {
            return false; // a game-side shape change must never strand the card on the board
        }
    }

    /// <summary>
    /// Fly one freshly-burned card into the burnt pile. Prefers the card's LIVE VR representation
    /// (the fan card selected in the LoseCard step) so the very card the player burned flies; if
    /// no usable VR card exists at that instant (already parked/recycled, or a discard-pile source
    /// with no live fan card), a transient card-back slab flies from the discard pile to the burnt
    /// pile so the user still SEES the card go. Skips cards the turn-clear sweep already owns.
    /// </summary>
    private void TryAnimateBurn(AbilityCardUI widget)
    {
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out _, out _))
            return; // burnt pile off / not built — no destination to fly to

        VRCard? card = _factory.Find(widget);

        // ORDER OF THE BURN (user ruling 2026-08-03: "Ich möchte, dass die Karte erst liegen
        // bleibt, man auf der Karte selber die Verbrannt-Animation abwartet und DANN in das
        // jeweilige Pile geht").
        //
        // This watch fires off the PILE COUNT, which the game commits the instant the burn is
        // decided — several frames BEFORE it starts playing the card's own burn artwork. Flying
        // immediately produced exactly the reported sequence: the card left for the pile, the
        // game then ran its burn timeline on the card it still owns (which the dock re-claims, so
        // it "pops back on the board"), and that leftover only went away when the next cards were
        // dealt. The hardware log shows the two in the wrong order plainly — "BURN ANIM
        // [pile-watch] … flies from …" at line 2283, "Burn/ghost effect playing ON the dock card"
        // only at 2427.
        //
        // So the flight WAITS: first for the effect to start (short grace — the game needs a few
        // frames), then for it to finish. Both waits are bounded, and the card is not claimed
        // while waiting, so the watch simply re-offers it next tick. If the effect never appears
        // the deadline lets the flight go anyway — a burned card must never be stranded on the
        // board just because its artwork did not play.
        //
        // OWNERSHIP FIRST, gate second: a card the turn-clear sweep will fly (still docked as a
        // round card, _lastHalfCards), one already mid-flight, or one in the player's hand must
        // not enter the artwork hold at all — its owner animates (or holds) it, and a hold taken
        // here would either release into a duplicate flight or cycle hold/release log lines every
        // grace period until the owner finally moved it. The watch simply keeps re-offering; the
        // ownership resolves itself (turn-clear launches and consumes any stale hold via
        // ClearBurnHold, a grabbed card returns to a zone on release).
        bool ownedElsewhere = card != null
            && (card.IsHeld || card.IsFlying || _flyingToPile.Contains(card) || _lastHalfCards.Contains(card));
        if (ownedElsewhere)
            return;
        if (!TryTakeBurnFlightSlot(widget, card))
            return;
        LaunchBurnFlight(widget, "pile-watch");
    }

    /// <summary>
    /// Launch the actual burn flight for <paramref name="widget"/> — the REAL VR card when one is
    /// still live at its true pose, else a transient card-back slab from the card's last-known
    /// pose, else nothing (never a teleport). Split out of <see cref="TryAnimateBurn"/> so
    /// <see cref="FlushBurnHolds"/> can land a pending flight WITHOUT re-running the artwork hold
    /// gate — a hold that survived until the character switched has had its moment on the board and
    /// must not be dropped (see the flush's own doc). <paramref name="origin"/> only names the
    /// caller in the log. Reports the same Board→Burnt <see cref="Net.NetCardFx"/> event on both
    /// branches, so peers replay every burn regardless of which branch ran.
    /// </summary>
    private void LaunchBurnFlight(AbilityCardUI widget, string origin)
    {
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out Vector3 burntPos, out float slabWidth))
            return; // burnt pile off / not built — no destination to fly to

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        VRCard? card = _factory.Find(widget);

        if (card != null && card.GameCard != null && card.gameObject.activeInHierarchy
            && !card.IsHeld && !card.IsFlying && !_flyingToPile.Contains(card))
        {
            // Ideal: the real VR card is still live at its true board position — fly IT (face and
            // all), from where it actually sits, orientation held for the whole flight (FlyToPile).
            float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, burntPos) * VRCard.FlyArcHeightFraction);
            _flyingToPile.Add(card);
            VRCard flying = card;
            // MP parity (report 6): a damage-burn is the most dramatic card animation in the game —
            // peers replay it as a card arcing off this player's board into their burnt stack.
            ReportCardFx(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
            card.FlyToPile(burntPos, slabWidth, FlyToPileSeconds, arcUp, () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
                VRLog.Info("Cards", $"BURN ANIM: '{flying.name}' reached the Burnt pile — parked.");
            }, minArc);
            _knownBurntWidgets.Add(widget); // claim (same contract as TryStartBurnFly) — animate once
            _lastCardWorldPos.Remove(widget); // consumed
            _lastCardWorldRot.Remove(widget);
            VRLog.Info("Cards", $"BURN ANIM [{origin}]: '{CardsGameApi.CardName(widget)}' burned — real VR card " +
                                $"flies from {card.transform.position} → Burnt pile ({FlyToPileSeconds:F2}s, arc " +
                                $"{arcHeight:F3} m over the board). VR presentation only; game pile state untouched.");
            return;
        }

        // Issue 1: the live VR card is already parked (position lost) or recycled, so fly a transient
        // card-back slab from the burned card's LAST-KNOWN world pose — NEVER from the discard pile
        // (that teleport to a different place first was the user's "glitched over the pile then
        // appeared somewhere else" bug). Keep that recorded rotation constant for the whole flight so
        // the card stays equally oriented. With no recorded pose there is genuinely nowhere to fly
        // from, so the animation is SKIPPED (no teleporting slab).
        bool hasFrom = _lastCardWorldPos.TryGetValue(widget, out Vector3 fromPos);
        Quaternion fromRot = _lastCardWorldRot.TryGetValue(widget, out Quaternion r) ? r : Quaternion.identity;
        _lastCardWorldPos.Remove(widget); // consumed either way
        _lastCardWorldRot.Remove(widget);
        if (!hasFrom)
        {
            VRLog.Warn("Cards", $"BURN ANIM [none]: '{CardsGameApi.CardName(widget)}' → Burnt pile: NO last-known VR " +
                                "position for the burned widget — animation skipped (never teleport a slab to a " +
                                "different place; the game pile state is unchanged). If this line appears for a " +
                                "short/long rest or damage burn, the earlier live-card paths all declined — check " +
                                "which zone the card was in when it burned.");
            return;
        }
        Transform? anchor = AnchorParent();
        if (anchor == null)
            return;
        float slabArc = Mathf.Max(minArc, Vector3.Distance(fromPos, burntPos) * VRCard.FlyArcHeightFraction);
        BurnSlab.Launch(anchor, fromPos, fromRot, burntPos, slabWidth, FlyToPileSeconds, arcUp, minArc);
        // MP parity (report 6): the fallback slab is the same event on the wire — the peer plays a
        // back slab either way (they never see faces), so both burn branches read identically.
        ReportCardFx(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
        VRLog.Info("Cards", $"BURN ANIM [{origin}/slab]: '{CardsGameApi.CardName(widget)}' burned — transient card-back slab " +
                            $"from {fromPos} (the burned card's true last position) → Burnt pile " +
                            $"({FlyToPileSeconds:F2}s, arc {slabArc:F3} m over the board), orientation held — no " +
                            "live VR card left for the burned widget.");
    }

    /// <summary>
    /// Transient card-back slab for the burn fallback (issue B): a pooled-free, self-destructing
    /// mini card that flies from a source into the burnt pile with the SAME over-the-board arc as
    /// <see cref="VRCard.FlyToPile"/>, then removes itself. Used only when the burned card has no
    /// live VR representation to fly. Parented under the cards anchor so it shares the diorama
    /// scale; works in world space on unscaled time (card phases pause timeScale).
    /// </summary>
    private sealed class BurnSlab : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private Vector3 _up;
        private float _height;
        private float _elapsed;
        private float _duration;
        private Vector3 _fromScale;
        private Vector3 _toScale;

        internal static void Launch(Transform anchor, Vector3 fromWorld, Quaternion fixedRot, Vector3 toWorld,
            float targetWorldWidth, float duration, Vector3 worldUp, float minArcHeight = 0f)
        {
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject("BurnSlab");
            go.transform.SetParent(anchor, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CardMesh.Get(w, h);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { CardMesh.CreateEdgeMaterial(), CardMesh.CreateBackMaterial() };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Core.VRLayers.Apply(go);

            float parentLossy = anchor.lossyScale.x;
            float startLocal = 1f; // full card size at the source
            float endLocal = (parentLossy > 1e-5f && w > 1e-5f)
                ? targetWorldWidth / (parentLossy * w)
                : 0.5f;

            var slab = go.AddComponent<BurnSlab>();
            slab._from = fromWorld;
            slab._to = toWorld;
            slab._up = worldUp.sqrMagnitude > 1e-6f ? worldUp.normalized : Vector3.up;
            slab._height = Mathf.Max(minArcHeight, Vector3.Distance(fromWorld, toWorld) * VRCard.FlyArcHeightFraction);
            slab._duration = Mathf.Max(0.05f, duration);
            slab._fromScale = Vector3.one * startLocal;
            slab._toScale = Vector3.one * Mathf.Max(1e-4f, endLocal);

            go.transform.position = fromWorld;
            go.transform.localScale = slab._fromScale;
            // Issue 1: hold the burned card's LAST-KNOWN orientation for the whole flight — the card
            // must stay equally oriented, never snap to a billboard or the pile's orientation.
            go.transform.rotation = fixedRot;
        }

        private void Update()
        {
            _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap, like the fan anim
            float ft = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            float e = 1f - (1f - ft) * (1f - ft); // ease-out on the base slide
            transform.position = Vector3.Lerp(_from, _to, e) + VRCard.FlyArcOffset(ft, _up, _height);
            transform.localScale = Vector3.Lerp(_fromScale, _toScale, e);
            if (ft >= 1f)
                Destroy(gameObject);
        }
    }

    private readonly HashSet<VRCard> _hooked = new();

    private void HookCard(VRCard card)
    {
        if (!_hooked.Add(card))
            return;
        card.Released += OnCardReleased;
        card.Grabbed += OnCardGrabbed;
        card.Poked += OnCardPoked;
    }
}
