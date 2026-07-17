using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase-3b orchestrator (one MonoBehaviour, created by <see cref="CardsModule"/>):
/// - pumps <see cref="CardActionQueue"/> (one blocking game call per frame max),
/// - enforces <see cref="HandSuppression"/>,
/// - reacts to the P2 bus/mode machine + Cards signals by rebuilding the card zones
///   (fan / tray / half layout) from authoritative game state,
/// - routes interactions (grab-drop, pokes, palm gate) into the verified game APIs.
///
/// Everything here runs on the main thread; handlers only set dirty flags or enqueue
/// — per-frame work happens in <see cref="Update"/> (P2 threading rules).
/// </summary>
internal sealed class CardsDriver : MonoBehaviour
{
    private readonly VRCardFactory _factory = new();
    private readonly CardFan _fan = new();
    private readonly PlayTray _tray = new();
    private readonly RestControls _rest = new();
    private readonly HalfSelection _half = new();
    private readonly PileViewer _piles = new();
    private readonly PileBrowser _browser = new();

    // Reused buffers (no per-frame allocations).
    private readonly List<AbilityCardUI> _widgetBuffer = new(24);
    private readonly List<AbilityCardUI> _pileWidgetBuffer = new(16);
    private readonly List<VRCard> _fanBuffer = new(24);
    private readonly List<VRCard> _halfBuffer = new(4);
    private readonly List<VRCard> _browseBuffer = new(16);
    private readonly List<VRCard> _fakeCards = new(12);

    private bool _dirty;
    private bool _fakeActive;
    private VRHand? _gateHand;
    private CardsHandUI? _boundHand;

    // ------------------------------------------------------------------ lifecycle --

    private void OnEnable()
    {
        VRModeStateMachine.ModeChanged += OnModeChanged;
        VREvents.CardSelectionChanged += OnCardSelectionChanged;
        VREvents.HandShown += OnHandShown;
        CardsSignals.HandDestroying += OnHandDestroying;
        CardsSignals.CardRecycling += OnCardRecycling;
        VRHands.HandsChanged += OnHandsChanged;

        _tray.SwapRequested += OnSwapRequested;
        _tray.ConfirmRequested += OnConfirmRequested;
        _tray.UndoRequested += OnUndoRequested;
        _rest.ShortRestRequested += OnShortRestRequested;
        _rest.LongRestRequested += OnLongRestRequested;
        _half.PlayRequested += OnPlayRequested;
        _piles.PokeToggled += OnPileTogglePoked;
        _piles.GrabOpened += OnPileGrabOpened;
        _piles.GrabReleased += OnPileGrabReleased;

        _dirty = true;
    }

    private void OnDisable()
    {
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VREvents.HandShown -= OnHandShown;
        CardsSignals.HandDestroying -= OnHandDestroying;
        CardsSignals.CardRecycling -= OnCardRecycling;
        VRHands.HandsChanged -= OnHandsChanged;
    }

    private void OnDestroy()
    {
        ClearLaserHover();
        ClearBoardHover();
        _liveGrabs.Clear();
        VRCard.InteractionBlockedHand = null;
        _fan.Destroy();
        _browser.Destroy();
        _half.Destroy();
        _rest.Destroy();
        _piles.Destroy(); // before the tray — the stacks live under its PileMount
        _tray.Destroy();
        _factory.Dispose(); // restores every adopted face
        CardActionQueue.Clear();
    }

    // ------------------------------------------------------------------ handlers --

    private void OnModeChanged(VRModeChange change)
    {
        _dirty = true;
        if (change.To == VRMode.CardSelection || change.To == VRMode.HalfSelection)
        {
            _tray.InvalidatePlacement();
            _half.InvalidatePlacement();
        }
        // A dialog owns the scene (test #21 C): an open pile browse would float
        // behind/through it — close, the stacks stay for re-opening afterwards.
        if (change.To == VRMode.ModalUI)
            CloseBrowser("modal dialog opened");
    }

    private void OnHandShown(HandShownEvent e) => _dirty = true;

    private void OnCardSelectionChanged(CardSelectionEvent e) => _dirty = true;

    private void OnHandsChanged() => _dirty = true;

    private void OnHandDestroying(CardsHandUI hand)
    {
        // Pool safety: give every adopted face back BEFORE the game recycles.
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
                continue;
            VRCard? card = _factory.Find(cards[i]);
            if (card != null)
                _half.DestroyZonesFor(card);
        }
        _factory.ReleaseHand(hand);
        _tray.ClearSlots();
        _fieldCards.Clear(); // the hand's VRCards just died — no dead refs on the field
        _shortRestCard = null; // ditto the sacrifice display (item 1d, reversibility)
        _shortRestPresented = null;
        if (_browseHand == hand)
            CloseBrowser("hand destroyed");
        if (_boundHand == hand)
            _boundHand = null;
        _dirty = true;
    }

    private void OnCardRecycling(AbilityCardUI widget)
    {
        VRCard? card = _factory.Find(widget);
        if (card != null)
        {
            if (ReferenceEquals(card, _laserHover))
                ClearLaserHover();
            if (ReferenceEquals(card, _browseHover))
                ClearBrowseHover();
            _half.DestroyZonesFor(card);
            _fan.Remove(card);
            _browser.Remove(card);
            if (_fieldCards.Remove(card))
                RelayoutField();
            if (ReferenceEquals(card, _shortRestCard)) // sacrifice widget recycled under us
            {
                _shortRestCard = null;
                _shortRestPresented = null;
            }
            _tray.RemoveCard(card);
            _liveGrabs.Remove(card); // recycled mid-grab: its release must not route a drop
        }
        _factory.ReleaseWidget(widget);
        _dirty = true;
    }

    // ------------------------------------------------------------------ update --

    private void Update()
    {
        CardActionQueue.Pump();
        HandSuppression.Tick();

        Transform? anchor = AnchorParent();
        if (anchor == null)
        {
            // Hands (and rig) are down — nothing physical can exist.
            if (_fan.IsOpen)
                _fan.Close();
            CloseBrowser("hands down");
            ClearLaserHover();
            ClearBoardHover();
            ClearBrowseHover();
            _tray.SetVisible(false);
            _half.SetVisible(false);
            return;
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild(anchor);
        }

        // Deferred initial placement (test #17): retries until the head has a
        // real tracked pose — the tray stays hidden meanwhile.
        _tray.TickPlacement();

        UpdatePalmGate();
        UpdateFanLaser();
        UpdateFanHoverSplit();
        UpdateBoardLaser();
        UpdateBrowseLaser();
        UpdateSlotHighlight();
        _fan.Tick();
        _half.Tick();
        _browser.Tick(); // held reading fan follows the grabbing hand (item 5)

        CardsHandUI? hand = CurrentHand();
        if (_tray.IsVisible)
        {
            _tray.TickStatus(_fakeActive ? null : hand);
            _rest.TickStatus(_fakeActive ? null : hand);
            _piles.TickStatus(_fakeActive ? null : hand);
        }

        PollShortRest(_fakeActive ? null : hand); // redraw-swaps ShortRestedCard with no mode change
        LogFanState(hand);
    }

    // ------------------------------------------------------------------ fan diagnostics --

    private (CardHandMode? mode, int widgets, int fanBuffer, bool gateEnabled, bool revealed,
        bool open, bool boundHand, VRMode vrMode)? _lastFanState;

    /// <summary>
    /// Test #16 diagnostic (change-deduped Info, [Cards] style): everything the fan's
    /// visibility depends on, in one line. The #16 hardware log proved the rebuild
    /// side healthy ("Rebuild: … fan=10" all session) while the user saw NO cards —
    /// the reveal gating (palm gate disabled by a stuck ModalUI) was only visible in
    /// Debug lines the LogOutput capture drops. With this line, any future "fan never
    /// showed" is attributable from LogOutput.log alone.
    /// </summary>
    private void LogFanState(CardsHandUI? hand)
    {
        CardHandMode? mode = hand != null ? CardsGameApi.Mode(hand) : null;
        PalmGate? gate = _gateHand != null ? _gateHand.PalmGate : null;
        bool gateEnabled = gate != null && gate.Enabled;
        bool revealed = gate != null && (CardsConfig.RevealAlways
            ? gate.Enabled
            : gate.Enabled && (gate.IsOpen || _laserHover != null));

        var state = (mode, widgets: _widgetBuffer.Count, fanBuffer: _fanBuffer.Count, gateEnabled,
            revealed, open: _fan.IsOpen, boundHand: _boundHand != null,
            vrMode: VRModeStateMachine.CurrentMode);
        if (_lastFanState.HasValue && _lastFanState.Value == state)
            return;
        _lastFanState = state;

        VRLog.Info("Cards", $"fan state: mode={(mode.HasValue ? mode.Value.ToString() : "none")}, " +
                            $"widgets={state.widgets}, fanBuffer={state.fanBuffer}, " +
                            $"gateEnabled={gateEnabled}, revealed={revealed}, open={_fan.IsOpen}, " +
                            $"boundHand={_boundHand != null} (vrMode={state.vrMode}).");
    }

    /// <summary>Cards live in the same scaled space as the hands (rig root in VR, sim camera in dev).</summary>
    private static Transform? AnchorParent()
    {
        VRHand? any = VRHands.Left != null ? VRHands.Left : VRHands.Right;
        if (any == null)
            return null;
        Transform handsRoot = any.transform.parent; // "GloomhavenVR.Hands"
        return handsRoot != null ? handsRoot : any.transform;
    }

    private CardsHandUI? CurrentHand()
    {
        if (!CardsGameApi.InScenario)
            return null;
        CardsHandUI? hand = CardsGameApi.ActiveHand();
        return hand != null && CardsGameApi.IsLocalHand(hand) ? hand : null;
    }

    private void UpdatePalmGate()
    {
        // Fan trigger: palm gate on the NON-dominant hand (Demeo: the off hand holds
        // the deck, the dominant hand interacts).
        VRHand? gateHand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (gateHand != _gateHand)
            _gateHand = gateHand;
        // P7 (test #10): the fan-owning hand is COMPLETELY excluded from card
        // hover/highlight/grab/poke — its palm sits inside the fan and its own
        // proximity hover made two cards flip-flop highlights forever. Only the
        // free (dominant) hand interacts with cards, by laser or proximity.
        VRCard.InteractionBlockedHand = _gateHand;
        if (_gateHand == null)
        {
            if (_fan.IsOpen)
                _fan.Close();
            ClearLaserHover();
            return;
        }

        // Live-tunable gate feel (P7): pure supination (roll-axis) measure on the raw
        // device pose — pitching/pointing the arm no longer factors in (test #10).
        PalmGate gate = _gateHand.PalmGate;
        gate.EnterThreshold = CardsConfig.SupinationThreshold.Value;
        gate.ExitThreshold = CardsConfig.SupinationExitThreshold;
        gate.RollAxisOnly = true;
        gate.UseDevicePalmNormal = !_gateHand.IsSimulated; // sim hands pose the rig directly
        // G5 (DEMEO-HANDS-CARDS §4): the reveal preset harmlessly overrides the
        // Enter/Exit thresholds above when [Cards] RevealPreset=demeo (tight cone);
        // and while the dominant hand holds something the gate stays put so a pluck
        // never re-triggers the fan mid-reach ([Cards] RevealIgnoreWhenGrabbing).
        gate.ApplyDemeoPreset(CardsConfig.RevealDemeo);
        gate.IgnoreWhenHandBusy = CardsConfig.RevealIgnoreWhenGrabbing.Value;

        bool allowFan = _fanBuffer.Count > 0 || _fan.Cards.Count > 0;
        // RevealMode=always: no gesture at all while a card phase is live (gate.Enabled
        // is the mode policy). Tilt mode additionally HOLDS the fan open while the
        // dominant laser is on it — plucking must never collapse the fan mid-reach.
        bool revealed = CardsConfig.RevealAlways
            ? gate.Enabled
            : gate.Enabled && (gate.IsOpen || _laserHover != null);
        bool shouldOpen = allowFan && revealed;
        if (shouldOpen && !_fan.IsOpen)
            _fan.Open(_gateHand);
        else if (!shouldOpen && _fan.IsOpen)
            _fan.Close();
    }

    // ------------------------------------------------------------------ fan laser --

    private VRCard? _laserHover;

    /// <summary>
    /// Demeo pluck (P6): the dominant hand's laser highlights fan cards (pop + one
    /// haptic tick per card change) and TriggerDown pulls the pointed card into the
    /// dominant hand (released on TriggerUp). Proximity grab keeps working unchanged.
    /// </summary>
    private void UpdateFanLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_fan.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null)
        {
            ClearLaserHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_fan.TryRaycast(pick.Origin, pick.Direction, _laserHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            ClearLaserHover();
            return;
        }

        if (!ReferenceEquals(card, _laserHover))
        {
            ClearLaserHover();
            _laserHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }

        // Clamp the visible beam to the card — also raises Ray.HasFreshUiHit, which
        // suppresses the board far-click for this trigger press.
        dom.Ray.UiHitOverride = point;

        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearLaserHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
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
        // path); proximity highlight only fills in when the laser hovers nothing.
        VRCard? hovered = _laserHover;
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

    // ------------------------------------------------------------------ board laser --

    private IPokeable? _boardHover;
    private VRHand? _boardHoverHand;
    private VRCard? _trayCardHover;

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
            || !dom.Ray.Enabled || dom.Grabber.Held != null || _laserHover != null)
        {
            ClearBoardHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        var ray = new Ray(pick.Origin, pick.Direction);
        float maxDist = 3f * dom.WorldScale;

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

        // Slotted cards: pluck them back with the laser, like fan cards.
        bool cardWins = _tray.TryRaycastCards(pick.Origin, pick.Direction,
            out VRCard? card, out Vector3 cardPoint, out float cardDist) && cardDist < bestDist;

        // The game's own UI (RayUgui) closer than everything → neither hovers.
        float nearest = cardWins ? cardDist : best != null ? bestDist : float.PositiveInfinity;
        if (float.IsPositiveInfinity(nearest) || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < nearest))
        {
            ClearBoardHover();
            return;
        }

        if (cardWins)
        {
            ClearBoardPokeHover();
            if (!ReferenceEquals(card, _trayCardHover))
            {
                ClearTrayCardHover();
                _trayCardHover = card;
                card!.SetLaserHover(true);
                dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on change
            }
            dom.Ray.UiHitOverride = cardPoint;
            if (dom.TriggerDown && card!.CanGrab)
            {
                VRCard grab = card;
                ClearTrayCardHover();
                VRLog.Info("Cards", "Board: slotted card laser-plucked.");
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
        if (dom.TriggerDown)
        {
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
                ForeignInteraction("board button");
                button.Press(dom, "laser");
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
        if (_boardHover == null)
            return;
        if (_boardHoverHand != null)
            _boardHover.OnPokeExit(_boardHoverHand);
        _boardHover = null;
        _boardHoverHand = null;
    }

    private void ClearTrayCardHover()
    {
        if (_trayCardHover == null)
            return;
        _trayCardHover.SetLaserHover(false);
        _trayCardHover = null;
    }

    // ------------------------------------------------------------------ browse laser --

    private VRCard? _browseHover;

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
        if (!_browser.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null
            || _laserHover != null || _trayCardHover != null || _boardHover != null)
        {
            ClearBrowseHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_browser.TryRaycast(pick.Origin, pick.Direction, _browseHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            ClearBrowseHover();
            return;
        }

        if (!ReferenceEquals(card, _browseHover))
        {
            ClearBrowseHover();
            _browseHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearBrowseHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
    }

    private void ClearBrowseHover()
    {
        if (_browseHover == null)
            return;
        _browseHover.SetLaserHover(false);
        _browseHover = null;
    }

    // ------------------------------------------------------------------ slot snap preview --

    private int _snapHighlightSlot = -1;
    private VRCard? _snapHighlightCard;
    private VRCard? _fieldHighlightCard; // pick-field counterpart of _snapHighlightCard (test #21 B)

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
        // Pick modes (test #21 B): the drop FIELD is the only snap target — same
        // telegraph contract as the slots (glow-at-release is the primary accept
        // rule, haptic tick on edge). Slot glow stays off while the field shows.
        if (_tray.PickFieldVisible)
        {
            VRCard? fieldHeld = HeldCard(out VRHand? fieldHolder);
            bool near = fieldHeld != null && fieldHolder != null
                && _tray.PickFieldNear(fieldHeld.transform.position,
                    fieldHolder.Rig.PalmCenter.position, out _, out _);
            _tray.SetPickFieldHighlight(near);
            VRCard? target = near ? fieldHeld : null;
            if (!ReferenceEquals(target, _fieldHighlightCard))
            {
                _fieldHighlightCard = target;
                if (target != null && fieldHolder != null)
                    fieldHolder.SendHaptic(HapticPreset.HoverTick); // debounced: only on edge
            }
            _tray.SetHighlightedSlot(-1);
            _snapHighlightSlot = -1;
            _snapHighlightCard = null;
            return;
        }
        if (_fieldHighlightCard != null)
        {
            _fieldHighlightCard = null;
            _tray.SetPickFieldHighlight(false);
        }

        int slot = -1;
        VRHand? holder = null;
        VRCard? held = null;
        if (_tray.IsVisible)
        {
            held = HeldCard(out holder);
            if (held != null && holder != null)
            {
                slot = _tray.SlotNear(held.transform.position, holder.Rig.PalmCenter.position);
                // Mirror the release-time targeting (item 27.1):
                // - a HELD TRAY card never glows its own origin slot — releasing there
                //   returns it to the fan, so only the OTHER slot (reorder/swap) glows;
                // - a FAN card hovering an OCCUPIED slot telegraphs a SWAP into that very
                //   slot (occupant → hand), so the glow stays put — no divert, both-
                //   occupied included.
                if (_tray.ContainsCard(held) && slot == _tray.SlotOf(held))
                    slot = -1;
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

    // ------------------------------------------------------------------ rebuild --

    private void Rebuild(Transform anchor)
    {
        CardsHandUI? hand = CurrentHand();

        if (hand == null)
        {
            RebuildFakeOrClear(anchor);
            return;
        }
        if (_fakeActive)
            ClearFakeCards();
        _boundHand = hand;

        _tray.EnsureBuilt(_factory, anchor);
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

        _fanBuffer.Clear();
        _halfBuffer.Clear();

        // Test #15: the tray is the central DASHBOARD — visible for the whole
        // scenario (initiative track, objectives, confirm/undo, settings), not only
        // during card selection. Cards remain grabbable only in CardsSelection.
        bool trayVisible = true;
        bool halfVisible = false;
        bool pokeSelect = false;
        bool grabbable = false;

        switch (mode)
        {
            case CardHandMode.CardsSelection:
                grabbable = true;
                for (int i = 0; i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    if (widget.CardType == CardPileType.Hand)
                        _fanBuffer.Add(AdoptedCard(widget));
                }
                _tray.SyncFromGameState(hand, _factory);
                // Tray occupants were created by the sync — hook + re-adopt them too.
                for (int slot = 0; slot < 2; slot++)
                {
                    VRCard? occupant = _tray.Occupant(slot);
                    if (occupant == null)
                        continue;
                    HookCard(occupant);
                    if (occupant.NeedsFace && occupant.GameCard != null)
                        occupant.AttachGameCard(occupant.GameCard);
                }
                break;

            case CardHandMode.LoseCard:
            case CardHandMode.DiscardCard:
            case CardHandMode.RecoverDiscardedCard:
            case CardHandMode.RecoverLostCard:
            case CardHandMode.IncreaseCardLimit:
                // Modal card picks (long-rest burn, avoid-damage, discards,
                // recovers): the 2D UI is click-to-select. VR (test #21 B): the
                // candidates are GRABBABLE — lay one onto the board's drop field to
                // select it — with poke-to-select kept as the fallback; both paths
                // commit through the same seam (TryCommitPick →
                // CardsHandUI.SelectCard).
                pokeSelect = true;
                grabbable = true;
                // Field occupants stay valid only while the game still reports them
                // selected (an undo / "choose other card" returns them to the fan).
                for (int i = _fieldCards.Count - 1; i >= 0; i--)
                {
                    VRCard occupant = _fieldCards[i];
                    if (occupant == null || occupant.GameCard == null || !occupant.GameCard.IsSelected)
                        _fieldCards.RemoveAt(i);
                }
                // Item 9: the SELECTABLE widgets become the fan. In CardsSelection the
                // fan is the real hand; in the burn-two-discarded flow the game marks
                // the DISCARD-pile widgets selectable (CardHandMode.LoseCard, pile
                // Discarded, count 2 — AbilityCardUI.SetMode), so the exact same fan
                // becomes the discard pile, picked exactly like hand cards through the
                // one authoritative TryCommitPick → SelectCard seam. Track the source
                // pile for the change-deduped Info line below.
                CardPileType pickSource = CardPileType.None;
                for (int i = 0; i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    if (!widget.IsSelectable)
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
                halfVisible = true;
                CollectRoundCards(hand, _halfBuffer);
                break;

            default:
                break;
        }

        if (mode != CardHandMode.CardsSelection)
            _tray.ClearSlots(); // stale occupancy must not pin cards outside CardsSelection

        // Drop field (test #21 B): exists ONLY during a pick mode (C); leaving the
        // mode clears its occupants — the zone loop below parks them.
        bool pick = IsPickMode(mode);

        // Short-rest sacrifice overlay (test #25, item 1d): while the game presents the
        // randomly lost card's burn/redraw choice (ShortRestedCard != null; the choice
        // itself is the docked DialogPopup), lay that card physically at the board
        // centre on the SAME centre field the avoid-damage burn uses — DISPLAY-ONLY.
        // Guarded off during the pick modes (which own the field) so the two flows can
        // never collide, and off when the tray is hidden. The short-rest random path
        // stays in CardHandMode.CardsSelection, so this simply overlays the unchanged
        // hand fan. Rebuild is the sole executor; PollShortRest keeps it live on redraw.
        CAbilityCard? shortRested = pick ? null : CardsGameApi.ShortRestedCard(hand);
        bool shortRest = shortRested != null && trayVisible;

        _tray.SetPickFieldVisible((pick || shortRest) && trayVisible);
        if (shortRest)
            PresentShortRestCard(hand, shortRested!);
        else
            RemoveShortRestCard();

        if (!pick)
        {
            _fieldCards.Clear();
            _loggedPickSource = null; // re-entering a pick mode logs its source afresh (item 9)
        }

        // Pile browse (test #21): refresh content or close — BEFORE the zone flags
        // below so freshly closed browse cards park in this same pass.
        UpdateBrowser(hand, mode);

        // Configure cards per zone; everything else parks invisibly.
        for (int i = 0; i < _factory.All.Count; i++)
        {
            VRCard card = _factory.All[i];
            if (card == null || card.IsHeld)
                continue;
            bool inFan = _fanBuffer.Contains(card);
            bool inHalf = _halfBuffer.Contains(card);
            bool inTray = _tray.SlotOf(card) >= 0;
            bool inBrowse = _browser.Contains(card);
            bool inField = _fieldCards.Contains(card);
            // Short-rest sacrifice card: its own zone. Never in any of the above, so
            // its Grabbable/PokeSelect resolve to false here (display-only) — we only
            // keep it OUT of the park sweep so PresentShortRestCard's centre home holds.
            bool inShortRest = ReferenceEquals(card, _shortRestCard);

            card.PokeSelectEnabled = inFan && pokeSelect;
            // Field occupants stay grabbable: plucking one back off the field and
            // releasing it elsewhere unselects through the game's own seam. Browse
            // cards are grabbable too (item 5) — but purely to pull one close and
            // read it; the release routes back to the arc, never to a game seam.
            card.Grabbable = (inFan && grabbable) || (inTray && grabbable) || inField || inBrowse;
            if (!inFan)
                card.ResetColliderRegion(); // fan strips only apply while fanned

            if (!inFan && !inHalf && !inTray && !inBrowse && !inField && !inShortRest)
                _factory.Park(card);
        }

        _fan.SetCards(_fanBuffer);
        _tray.SetVisible(trayVisible);
        _half.SetVisible(halfVisible);
        if (halfVisible)
            _half.SetCards(_halfBuffer);

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
        // Prefer the phase machine's own pair (CardsActionControlller.Init'ed them).
        CardsGameApi.GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second);
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            bool isActionCard =
                (first != null && widget.fullAbilityCard == first) ||
                (second != null && widget.fullAbilityCard == second) ||
                (first == null && second == null && CardsGameApi.IsInRound(hand, widget.AbilityCard));
            if (isActionCard)
            {
                VRCard card = AdoptedCard(widget);
                if (!into.Contains(card))
                    into.Add(card);
            }
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
        // Item 8: grabbing a hand/tray/field card while a browse is open is a foreign
        // interaction. Grabbing a BROWSE card is part of the browse (read close), so
        // it is exempt — only the arc's own cards may be plucked without dismissing.
        if (!_browser.Contains(card))
            ForeignInteraction("card grabbed");
        // Accident window (test #19): a pluck FROM a slot or the pick field means
        // the hand is working right next to CONFIRM — arm the suppression guard.
        if (_tray.SlotOf(card) >= 0 || _fieldCards.Contains(card))
            _tray.NoteSlotActivity();
        if (_fan.Contains(card))
            _fan.Remove(card);
        // Tray occupancy stays until the release decides select/unselect/swap.
    }

    private void OnCardReleased(VRCard card, VRHand hand, Vector3 velocity)
    {
        if (!_liveGrabs.Remove(card))
        {
            VRLog.Warn("Cards", $"Release without live grab ignored ({card.name}) — drop path is once-per-release.");
            return;
        }

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

        // (a) Return-to-hand: a placed card must NOT be re-pinned to its OWN origin
        // slot when released away. Neither a highlight latched there at grab/near-
        // release (test #15's glow-is-drop rule) nor the generous capture radius may
        // hold it — only the OTHER slot keeps a placed card in the tray (the reorder/
        // swap below). Everything else, origin included, falls through to the fan.
        int origin = _tray.SlotOf(card); // -1 for a fan card
        if (wasInTray)
        {
            if (highlightSlot == origin) highlightSlot = -1;
            if (slot == origin) slot = -1;
        }

        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot;

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
            hand.SendHaptic(HapticPreset.ClickPulse);
            // SelectCard is the spin-wait path — queued; outcome verified against
            // the authoritative round pile afterwards.
            _tray.PlaceCard(card, slot);
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
            VRCard displaced = _tray.Occupant(slot)!;
            CAbilityCard? displacedAbility = displaced.GameCard?.AbilityCard;
            _tray.RemoveCard(displaced);
            _fan.Add(displaced);
            _tray.PlaceCard(card, slot);
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
            // Tray → elsewhere: take the card back.
            _tray.RemoveCard(card);
            _fan.Add(card);
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.UnselectCard(handRef, ability),
                () => _dirty = true);
        }
        else
        {
            _fan.Add(card); // released in the void: animated return
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
    /// Release routing for the modal pick modes (test #21 B). Accept rules mirror
    /// the play slots (test #15): the glow that telegraphed the drop wins, the
    /// generous capture radius is the fallback. Accepting a fan card lays it onto
    /// the field and commits the selection; releasing a FIELD card anywhere else
    /// takes the pick back through the game's own UnselectCard seam.
    /// </summary>
    private void HandlePickRelease(VRCard card, VRHand hand, CardsHandUI gameHand)
    {
        bool highlight = ReferenceEquals(_fieldHighlightCard, card);
        bool near = _tray.PickFieldNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float dist, out float radius);
        bool wasOnField = _fieldCards.Contains(card);
        bool accept = highlight || near;
        string rule = highlight ? "highlight" : near ? "radius" : "none";

        // THE one log line per real pick drop (the test #14 contract).
        VRLog.Info("Cards", $"Drop ({hand.Side}): pick field {dist:F2} m, radius {radius:F2} m, rule={rule} → " +
                            (accept
                                ? (wasOnField ? "stay on field." : "select onto field.")
                                : (wasOnField ? "take back (unselect)." : "return to fan.")));

        // Accident window (test #19): the field sits directly above the cluster's
        // Ready and beside the tray CONFIRM — every drop/take-back touching it
        // arms the confirm suppression, exactly like the play slots.
        if (accept || wasOnField)
            _tray.NoteSlotActivity();

        if (accept)
        {
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            PlaceOnField(card);
            if (!wasOnField)
                TryCommitPick(card, gameHand, "drop-field");
        }
        else if (wasOnField)
        {
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
                                        "via drop-field → CardsHandUI.UnselectCard.");
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
        if (widget.IsSelected)
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

    /// <summary>
    /// Home the field occupants onto the drop field: one card centered, two (the
    /// burn-two-from-discard flows) side by side. Held cards are never re-homed
    /// (the phantom-ACCEPT lesson, see PlayTray.PlaceCard).
    /// </summary>
    private void RelayoutField()
    {
        Transform? field = _tray.PickFieldAnchor;
        if (field == null)
            return;
        int n = _fieldCards.Count;
        float w = CardsConfig.CardWidth.Value;
        for (int i = 0; i < n; i++)
        {
            VRCard card = _fieldCards[i];
            if (card == null || card.IsHeld)
                continue;
            card.gameObject.SetActive(true);
            float x = n > 1 ? (i - (n - 1) * 0.5f) * w * 0.55f : 0f;
            card.SetHome(field, new Vector3(x, 0f, -0.002f * (i + 1)), Quaternion.identity, 1f);
        }
    }

    // -------------------------------------------------------------- short rest --

    /// <summary>
    /// Present the short-rested card at the board centre (test #25, item 1d). Adopts
    /// the sacrifice widget through the SAME <see cref="AdoptedCard"/> path as every
    /// other physical card and homes it dead-centre on the pick field with the
    /// identical single-card centring <see cref="RelayoutField"/> uses — so it reads
    /// exactly like the avoid-damage sacrifice. DISPLAY-ONLY: forced non-grabbable /
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
        if (!ReferenceEquals(card, _shortRestCard))
        {
            if (_shortRestCard != null)
                _factory.Park(_shortRestCard); // redraw: park the previous sacrifice
            _shortRestCard = card;
        }

        card.Grabbable = false;      // display-only — the docked choice commits, not a drop
        card.PokeSelectEnabled = false;

        Transform? field = _tray.PickFieldAnchor;
        if (field != null)
        {
            card.gameObject.SetActive(true);
            card.SetHome(field, new Vector3(0f, 0f, -0.002f), Quaternion.identity, 1f);
        }

        if (changed)
        {
            bool swap = _shortRestPresented != null;
            VRLog.Info("Cards", $"Short rest: {(swap ? "REDREW —" : "presenting")} sacrificed card " +
                                $"'{CardsGameApi.CardName(widget)}' at the board centre " +
                                "(display-only; burn/redraw commits via the docked choice).");
            _shortRestPresented = lost;
        }
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

    /// <summary>
    /// Enforce "slot 0 = initiative": if the game's initiative card is not the slot-0
    /// occupant (e.g. cards were dropped right-to-left), issue the game's own swap.
    /// </summary>
    private void ReconcileInitiative(CardsHandUI hand)
    {
        if (hand == null || hand.PlayerActor == null)
            return;
        if (hand.PlayerActor.CharacterClass.RoundAbilityCards.Count != 2)
            return;
        VRCard? slot0 = _tray.Occupant(0);
        if (slot0 == null || slot0.GameCard == null)
            return;
        CAbilityCard? initiative = CardsGameApi.InitiativeCard(hand);
        if (initiative != null && initiative != slot0.GameCard.AbilityCard)
        {
            if (CardsGameApi.SwapInitiative(hand))
                VRLog.Debug("Cards", "Initiative reconciled to slot order.");
        }
    }

    private void OnSwapRequested()
    {
        ForeignInteraction("tray initiative swap");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(
            () => CardsGameApi.SwapInitiative(handRef),
            () =>
            {
                _tray.SyncFromGameState(handRef, _factory);
                _dirty = true;
            });
    }

    private void OnConfirmRequested()
    {
        // Confirm OR revoke (test #19), decided INSIDE the queued action so it
        // serializes behind pending card selects and reads the freshest state:
        // - active hand already confirmed → the game's own un-ready path
        //   (UIReadyToggle.ReadyUp(false) → GameActionType.UnreadyPlayer);
        // - online card selection, not yet readied → ready-up via the same toggle
        //   (the 2D UI shows the toggle INSTEAD of the ReadyButton there);
        // - everything else → the ReadyButton dispatch (Pass/StepComplete — no
        //   spin-wait, ScenarioRuleClient.Pass only messages the SRL).
        // Every outcome logs the RESOLVED game state.
        ForeignInteraction("tray CONFIRM");
        CardActionQueue.Enqueue(
            () =>
            {
                CardsHandUI? hand = CurrentHand();
                if (hand != null && CardsGameApi.IsConfirmed(hand))
                {
                    bool revoked = CardsGameApi.SetReady(false);
                    VRLog.Info("Cards", $"Board: CONFIRM → ready {(revoked ? "REVOKED" : "revoke rejected")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
                else if (CardsGameApi.ReadyToggleAvailable())
                {
                    bool readied = CardsGameApi.SetReady(true);
                    VRLog.Info("Cards", $"Board: CONFIRM → ready toggle {(readied ? "READIED" : "rejected")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
                else
                {
                    bool fired = CardsGameApi.ClickReady();
                    VRLog.Info("Cards", $"Board: CONFIRM → ReadyButton {(fired ? "clicked" : "rejected (not interactable)")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
            },
            () => _dirty = true);
    }

    private void OnUndoRequested()
    {
        ForeignInteraction("tray UNDO");
        CardActionQueue.Enqueue(
            () =>
            {
                bool fired = CardsGameApi.ClickUndo();
                VRLog.Info("Cards", $"Board: UNDO → UndoButton {(fired ? "clicked" : "rejected (not interactable)")}.");
            },
            () => _dirty = true);
    }

    private void OnShortRestRequested()
    {
        ForeignInteraction("short rest toggle");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleShortRest(handRef), () => _dirty = true);
    }

    private void OnLongRestRequested()
    {
        ForeignInteraction("long rest toggle");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleLongRest(handRef), () => _dirty = true);
    }

    private void OnPlayRequested(VRCard card, CBaseCard.ActionType type)
    {
        ForeignInteraction("action play");
        FullAbilityCard? full = card.FullCard;
        if (full == null)
            return;
        // No spin-wait in OnAbilityClick, but queue anyway: serializes with pending
        // selects and keeps game entries out of interaction callbacks.
        CardActionQueue.Enqueue(() => CardsGameApi.PlayHalf(full, type), () => _dirty = true);
    }

    // ------------------------------------------------------------------ pile browse --

    // Browse state (test #21): what was open when, so any mode/hand change closes
    // it deterministically (C: browse fans never survive a context switch).
    private bool _browseHeld;
    private CardsHandUI? _browseHand;
    private CardHandMode _browseMode;

    /// <summary>The modal pick modes (poke-select fan flows; drop-field flows since test #21).</summary>
    private static bool IsPickMode(CardHandMode mode) =>
        mode == CardHandMode.LoseCard
        || mode == CardHandMode.DiscardCard
        || mode == CardHandMode.RecoverDiscardedCard
        || mode == CardHandMode.RecoverLostCard
        || mode == CardHandMode.IncreaseCardLimit;

    private void OnPileTogglePoked(PileKind kind, VRHand hand)
    {
        if (_browser.IsOpen && _browser.Kind == kind)
        {
            CloseBrowser("poked again");
            return;
        }
        OpenBrowser(kind, held: false, hand);
    }

    private void OnPileGrabOpened(PileKind kind, VRHand hand) => OpenBrowser(kind, held: true, hand);

    private void OnPileGrabReleased(PileKind kind, VRHand hand)
    {
        if (_browseHeld)
            CloseBrowser("grip released");
    }

    private void OpenBrowser(PileKind kind, bool held, VRHand? hand)
    {
        CardsHandUI? gameHand = CurrentHand();
        Transform? anchor = AnchorParent();
        if (gameHand == null || anchor == null || !CardsConfig.PileViewer.Value)
            return;
        CardHandMode mode = CardsGameApi.Mode(gameHand);
        if (IsPickMode(mode) || VRModeStateMachine.CurrentMode == VRMode.ModalUI)
            return; // modal pick flows / dialogs own the scene — browsing is non-modal only
        _browseHeld = held;
        _browseHand = gameHand;
        _browseMode = mode;
        // Held grab (item 5): the arc becomes a reading fan pinned to the grabbing
        // hand — "the pile in my hand". Poke-toggle stays a fixed head-relative wall.
        _browser.Open(kind, anchor, held ? hand : null);
        VRLog.Info("Cards", $"Pile browse OPEN: {kind} ({(held ? "held in hand" : "toggled")}, mode={mode}).");
        _dirty = true; // content fills in Rebuild.UpdateBrowser
    }

    /// <summary>
    /// Close-on-foreign-interaction watchdog (test #22, item 8): while a pile browse
    /// is open, ANY interaction that is not part of the browse itself dismisses it —
    /// grabbing a hand/tray card, pressing a board button, a rest toggle, an action
    /// play. Every foreign-interaction seam funnels through this ONE close path
    /// (logged with its trigger) instead of scattering CloseBrowser calls across the
    /// handlers. Lifecycle closes (hands-down, hand destroyed, mode/dialog change,
    /// pile emptied) keep their own paths — those are reversibility guarantees (item
    /// D / test #21 C), not user interactions.
    /// </summary>
    private void ForeignInteraction(string source)
    {
        if (_browser.IsOpen)
            CloseBrowser($"foreign interaction: {source}");
    }

    private void CloseBrowser(string reason)
    {
        if (!_browser.IsOpen)
            return;
        VRLog.Info("Cards", $"Pile browse CLOSE ({reason}).");
        _browseHeld = false;
        _browseHand = null;
        ClearBrowseHover();
        _browser.Close();
        _dirty = true; // next rebuild parks the browsed cards
    }

    /// <summary>
    /// Rebuild-time browse refresh: close on any context change (mode/hand — C),
    /// otherwise mirror the authoritative pile into the arc. Content comes from the
    /// same widgets the 2D pile viewer re-parents (see CardsGameApi.GetPileWidgets),
    /// adopted read-only — Grabbable/PokeSelect stay off via the zone-flag loop.
    /// </summary>
    private void UpdateBrowser(CardsHandUI hand, CardHandMode mode)
    {
        if (!_browser.IsOpen)
            return;
        if (hand != _browseHand || mode != _browseMode)
        {
            CloseBrowser($"context change (mode={mode}, handSwitch={hand != _browseHand})");
            return;
        }

        bool burnt = _browser.Kind == PileKind.Burnt;
        CardsGameApi.GetPileWidgets(hand, burnt, _pileWidgetBuffer);
        _browseBuffer.Clear();
        for (int i = 0; i < _pileWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _pileWidgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            _browseBuffer.Add(AdoptedCard(widget));
        }
        if (_browseBuffer.Count == 0)
        {
            CloseBrowser("pile empty");
            return;
        }
        PileKind kind = burnt ? PileKind.Burnt : PileKind.Discard;
        _browser.SetCards(_browseBuffer, $"{PileViewer.Caption(kind)} ({_browseBuffer.Count})");
    }

    // ------------------------------------------------------------------ dev fake hand --

    private void RebuildFakeOrClear(Transform anchor)
    {
        // No active local hand: no piles to show or browse, no pick field (test #21
        // C — the stacks hide, an open browse closes, field occupants clear; all
        // return with the next active hand).
        _piles.SetVisible(false);
        CloseBrowser(CardsGameApi.InScenario ? "no active hand" : "scenario ended");
        _tray.SetPickFieldVisible(false);
        _fieldCards.Clear();
        RemoveShortRestCard(); // sacrifice display never survives losing the active hand (item 1d)

        bool wantFake = Plugin.DevMode.Value && CardsConfig.DevFakeHand.Value > 0 && !CardsGameApi.InScenario;
        if (!wantFake)
        {
            if (_fakeActive)
                ClearFakeCards();

            _fanBuffer.Clear();
            _fan.SetCards(_fanBuffer);
            _half.SetVisible(false);
            if (CardsGameApi.InScenario)
            {
                // Dashboard (test #15): a scenario without an ACTIVE local hand
                // (other players' turns, in-between phases) keeps the tray up —
                // initiative track/objectives/status stay readable; slots empty.
                _tray.EnsureBuilt(_factory, anchor);
                _rest.EnsureBuilt(_tray);
                _tray.ClearSlots();
                _tray.SetVisible(true);
            }
            else
            {
                _tray.SetVisible(false);
                if (_boundHand != null)
                {
                    _factory.Clear(); // scenario/hand gone: restore faces, drop cards
                    _boundHand = null;
                }
            }
            return;
        }

        if (!_fakeActive)
        {
            _fakeActive = true;
            int n = Mathf.Clamp(CardsConfig.DevFakeHand.Value, 1, 12);
            for (int i = 0; i < n; i++)
            {
                VRCard card = _factory.CreateBlank();
                card.BuildPlaceholderFace(i);
                HookCard(card);
                _fakeCards.Add(card);
            }
            VRLog.Info("Cards", $"Dev fake hand: {n} placeholder cards spawned.");
        }

        _tray.EnsureBuilt(_factory, anchor);
        _rest.EnsureBuilt(_tray);
        _tray.SetVisible(true);
        _half.SetVisible(false);

        _fanBuffer.Clear();
        for (int i = 0; i < _fakeCards.Count; i++)
        {
            VRCard card = _fakeCards[i];
            if (card == null || card.IsHeld || _tray.SlotOf(card) >= 0)
                continue;
            card.Grabbable = true;
            _fanBuffer.Add(card);
        }
        _fan.SetCards(_fanBuffer);
    }

    private void RouteFakeRelease(VRCard card, VRHand hand)
    {
        int highlightSlot = ReferenceEquals(_snapHighlightCard, card) ? _snapHighlightSlot : -1;
        int slot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float d1, out float d2, out float radius);
        if (slot >= 0 && _tray.Occupant(slot) != null && _tray.Occupant(slot) != card)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }
        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot; // test #15: what glows is what drops (see OnCardReleased)
        VRLog.Info("Cards", $"Drop ({hand.Side}, fake): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
                            $"rule={rule} → " + (slot >= 0 ? $"slot {slot + 1}." : "fan."));
        if (slot >= 0 || _tray.ContainsCard(card))
            _tray.NoteSlotActivity(); // accident window (test #19), fake-mode parity
        if (slot >= 0)
        {
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            _tray.PlaceCard(card, slot); // the Drop line above is the announcement
        }
        else
        {
            _tray.RemoveCard(card);
            _fan.Add(card);
        }
        _dirty = true;
    }

    private void ClearFakeCards()
    {
        _fakeActive = false;
        for (int i = 0; i < _fakeCards.Count; i++)
        {
            if (_fakeCards[i] != null)
                Destroy(_fakeCards[i].gameObject);
        }
        _fakeCards.Clear();
        _tray.ClearSlots();
    }
}
