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
        bool eligible = held != null && (_fanOriginCards.Contains(held) || _tray.ContainsCard(held));
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
                    _factory.Park(card);
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
        CardsHandUI? hand = CurrentHand();

        if (hand == null)
        {
            _hasSwitchPose = false; // no board to re-pose without a hand
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
                if (selecting)
                {
                    for (int i = 0; i < _widgetBuffer.Count; i++)
                    {
                        AbilityCardUI widget = _widgetBuffer[i];
                        if (widget.AbilityCard == null || widget.IsLongRest)
                            continue;
                        if (widget.CardType == CardPileType.Hand)
                            _fanBuffer.Add(AdoptedCard(widget));
                    }
                }
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
                bool actionTurn = CardsGameApi.IsActionTurn(hand);
                if (actionTurn)
                {
                    halfVisible = true;
                    CollectRoundCards(hand, _halfBuffer);
                }
                LogActionTurnLock(hand, actionTurn);
                break;

            default:
                break;
        }

        if (mode != CardHandMode.CardsSelection)
            _tray.ClearSlots(); // stale occupancy must not pin cards outside CardsSelection

        // Drop field (test #21 B): exists ONLY during a pick mode (C); leaving the
        // mode clears its occupants — the zone loop below parks them.
        bool pick = IsPickMode(mode);

        // Short-rest sacrifice overlay (test #25, item 1d; test #28 seating): while the
        // game presents the randomly lost card's burn/redraw choice (ShortRestedCard !=
        // null; the choice itself is the docked DialogPopup), lay that card physically
        // in the LEFT slot recess (Slot1) — the same left-slot home the avoid-damage
        // burn uses — DISPLAY-ONLY. Guarded off during the pick modes (which own the
        // slots) so the two flows can never collide, and off when the tray is hidden.
        // The short-rest random path stays in CardHandMode.CardsSelection, so this
        // simply overlays the unchanged hand fan. Rebuild is the sole executor;
        // PollShortRest keeps it live on redraw.
        CAbilityCard? shortRested = pick ? null : CardsGameApi.ShortRestedCard(hand);
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
            card.Grabbable = (inFan && grabbable) || (inTray && grabbable) || inField || inBrowse || inActive;
            if (!inFan)
                card.ResetColliderRegion(); // fan strips only apply while fanned
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
                    // burn fly owns this card — never also vanish it (no double animation).
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
        _fan.SetCards(_fanBuffer);
        _tray.SetVisible(trayVisible);
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

    private VRCard AdoptedCard(AbilityCardUI widget)
    {
        VRCard card = _factory.GetOrCreate(widget);
        if (card.NeedsFace)
            card.AttachGameCard(widget); // re-adopt after a dialog yielded the face
        HookCard(card);
        return card;
    }

    private void CollectRoundCards(CardsHandUI hand, List<VRCard> into)
    {
        // The AUTHORITATIVE per-character selector is the acting hand's own round pile
        // (IsInRound → CharacterClass.RoundAbilityCards) — always exactly the two played
        // cards of THIS actor. The phase machine's pair (CardsActionControlller.topCard/
        // bottomCard) is a static singleton that can lag a same-mode turn hand-off, so it is
        // only ADDED (covers the extra-turn pile, where cards are not in RoundAbilityCards) —
        // never used ALONE, so a stale pair can no longer make us dock the previous
        // character's cards (the second-character deadlock).
        CardsGameApi.GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second);
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
            }
        }
    }

    // ---------------------------------------------------------------- fly-to-pile (issue 5) --

    /// <summary>
    /// Issue 5 (user): animate a just-cleared PLAYED round card flying into its destination pile
    /// instead of instantly vanishing. Launched from the Rebuild park sweep for a card that was
    /// docked as a round card in the PREVIOUS rebuild (<see cref="_lastHalfCards"/>) and is now
    /// leaving every zone — i.e. the board is clearing at the end of this character's own action
    /// turn (the <c>IsActionTurn</c> gate flipped false). The fate is read from the game's own
    /// model piles (<see cref="PileFateOf"/>): a burned/lost card flies to the BURNT stack, every
    /// other (incl. unclear) card to the DISCARD stack. Returns true only when the fly was actually
    /// launched — the caller then skips the instant park; the fly's completion callback parks the
    /// card on arrival. Returns false (→ instant hide fallback) when the card isn't a cleared round
    /// card, is already flying, isn't visible, or the target pile is off / not built.
    /// </summary>
    private bool TryStartFlyToPile(CardsHandUI hand, VRCard card)
    {
        if (card.GameCard == null || card.IsFlying)
            return false;
        if (!_lastHalfCards.Contains(card))
            return false; // only the round cards docked last rebuild — never a fan/browse/etc. card
        if (!card.gameObject.activeInHierarchy)
            return false; // already parked/pooled — nothing to animate from

        PileKind fate = PileFateOf(hand, card);
        if (!_piles.TryGetPileWorld(fate, out Vector3 worldPos, out float slabWidth))
            return false; // pile offscreen / not built → fall back to the instant hide

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, worldPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        VRCard flying = card;
        PileKind dest = fate;
        // MP parity (report 6): peers replay this exact flight (slot → discard/burnt stack) against
        // THEIR copy of this player's board pose — 2 bytes, no per-frame transforms.
        Net.NetCardFx.Report(SlotAnchor(_tray.SlotOf(card)), PileAnchor(fate));
        card.FlyToPile(worldPos, slabWidth, FlyToPileSeconds, arcUp, () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"Fly-to-pile: '{flying.name}' reached the {dest} pile — parked.");
        }, minArc);
        // A played card whose fate is BURNT (a lost action) is a burn like any other — tag it with
        // the same BURN ANIM token the dedicated burn paths use so ONE grep proves every burn case.
        string tag = fate == PileKind.Burnt ? "BURN ANIM [turn-clear]" : "Fly-to-pile [turn-clear]";
        VRLog.Info("Cards", $"{tag}: '{card.name}' → {fate} pile ({FlyToPileSeconds:F2}s, " +
                            $"arc {arcHeight:F3} m over the board, orientation locked) — played round card cleared " +
                            "from the board (VR presentation only; game pile state untouched).");
        return true;
    }

    /// <summary>
    /// Issue 5: the destination pile for a cleared played card, from the game's AUTHORITATIVE
    /// model piles. A card sitting in the character's LOST or PERMANENTLY-LOST list is a burned
    /// card (BURNT stack); anything else — a normal discard, or a card whose fate is not yet
    /// resolved in the model (still in the round pile) — defaults to the DISCARD stack, the fate a
    /// non-lost ability card always ends at (task: "cards whose fate is unclear default to
    /// discard"). Read-only: no game state is touched.
    /// </summary>
    private static PileKind PileFateOf(CardsHandUI hand, VRCard card)
    {
        AbilityCardUI? widget = card.GameCard;
        CAbilityCard? ac = widget != null ? widget.AbilityCard : null;
        // The card's OWN owner (AbilityCardUI.PlayerActor) is authoritative — in a two-character
        // sequential turn a card cleared during the OTHER character's turn belongs to a different
        // actor than the currently-presented hand, so its lost/discard fate must be read from its
        // own character's piles. Fall back to the presented hand if the widget has no owner.
        CPlayerActor? actor = widget != null ? widget.PlayerActor : null;
        if (actor == null && hand != null)
            actor = hand.PlayerActor;
        if (ac != null && actor != null)
        {
            CCharacterClass klass = actor.CharacterClass;
            if (klass.LostAbilityCards.Contains(ac) || klass.PermanentlyLostAbilityCards.Contains(ac))
                return PileKind.Burnt;
        }
        return PileKind.Discard;
    }

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
    /// Returns true only when the flight was actually launched; the caller then skips its park (the
    /// completion callback parks the card on arrival). Declines — leaving the caller's park and
    /// <see cref="TickBurnToPile"/>'s transient-slab fallback to take over — when the card is held,
    /// already animating, no longer live (a pooled/parked or inactive card cannot animate: its
    /// Update does not run), not a fresh burn, or the burnt pile is off / not built.
    ///
    /// Claiming: the widget is added to <see cref="_knownBurntWidgets"/> immediately, so the burn
    /// watcher running LATER in this same frame treats it as already-known and cannot start a second
    /// animation for it.
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
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        Vector3 from = card.transform.position;
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(from, burntPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        _knownBurntWidgets.Add(widget); // claim before the watcher's diff sees it (no double animation)
        _lastCardWorldPos.Remove(widget); // consumed — the real card is flying, no fallback slab wanted
        _lastCardWorldRot.Remove(widget);
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
            _burnWatchHand = null;
            return;
        }
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntWidgetBuffer);

        // Re-baseline on a hand change (or first sight): record the current burnt set WITHOUT
        // animating — only cards that cross into it from here on are freshly burned.
        if (!ReferenceEquals(hand, _burnWatchHand))
        {
            _burnWatchHand = hand;
            _knownBurntWidgets.Clear();
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
        _knownBurntWidgets.Clear();
        for (int i = 0; i < _burntWidgetBuffer.Count; i++)
            if (_burntWidgetBuffer[i] != null)
                _knownBurntWidgets.Add(_burntWidgetBuffer[i]);
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
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out Vector3 burntPos, out float slabWidth))
            return; // burnt pile off / not built — no destination to fly to

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        VRCard? card = _factory.Find(widget);
        bool ownedElsewhere = card != null
            && (card.IsHeld || card.IsFlying || _flyingToPile.Contains(card) || _lastHalfCards.Contains(card));

        if (!ownedElsewhere && card != null && card.GameCard != null && card.gameObject.activeInHierarchy)
        {
            // Ideal: the real VR card is still live at its true board position — fly IT (face and
            // all), from where it actually sits, orientation held for the whole flight (FlyToPile).
            float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, burntPos) * VRCard.FlyArcHeightFraction);
            _flyingToPile.Add(card);
            VRCard flying = card;
            // MP parity (report 6): a damage-burn is the most dramatic card animation in the game —
            // peers replay it as a card arcing off this player's board into their burnt stack.
            Net.NetCardFx.Report(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
            card.FlyToPile(burntPos, slabWidth, FlyToPileSeconds, arcUp, () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
                VRLog.Info("Cards", $"BURN ANIM: '{flying.name}' reached the Burnt pile — parked.");
            }, minArc);
            _knownBurntWidgets.Add(widget); // claim (same contract as TryStartBurnFly) — animate once
            _lastCardWorldPos.Remove(widget); // consumed
            _lastCardWorldRot.Remove(widget);
            VRLog.Info("Cards", $"BURN ANIM [pile-watch]: '{CardsGameApi.CardName(widget)}' burned — real VR card " +
                                $"flies from {card.transform.position} → Burnt pile ({FlyToPileSeconds:F2}s, arc " +
                                $"{arcHeight:F3} m over the board). VR presentation only; game pile state untouched.");
            return;
        }

        if (ownedElsewhere)
            return; // the turn-clear sweep (or a live grab) is already animating this exact card

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
        Net.NetCardFx.Report(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
        VRLog.Info("Cards", $"BURN ANIM [slab]: '{CardsGameApi.CardName(widget)}' burned — transient card-back slab " +
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
