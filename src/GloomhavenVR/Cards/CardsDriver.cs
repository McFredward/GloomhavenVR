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

    // Reused buffers (no per-frame allocations).
    private readonly List<AbilityCardUI> _widgetBuffer = new(24);
    private readonly List<VRCard> _fanBuffer = new(24);
    private readonly List<VRCard> _halfBuffer = new(4);
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
        _half.Destroy();
        _rest.Destroy();
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
            _half.DestroyZonesFor(card);
            _fan.Remove(card);
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
            ClearLaserHover();
            ClearBoardHover();
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
        UpdateBoardLaser();
        UpdateSlotHighlight();
        _fan.Tick();
        _half.Tick();

        CardsHandUI? hand = CurrentHand();
        if (_tray.IsVisible)
        {
            _tray.TickStatus(_fakeActive ? null : hand);
            _rest.TickStatus(_fakeActive ? null : hand);
        }

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
                button.Press(dom, "laser");
            }
            else
            {
                VRLog.Info("Cards", $"Board: laser click → {(best as MonoBehaviour)?.name ?? best!.GetType().Name}.");
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

    // ------------------------------------------------------------------ slot snap preview --

    private int _snapHighlightSlot = -1;
    private VRCard? _snapHighlightCard;

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
        int slot = -1;
        VRHand? holder = null;
        VRCard? held = null;
        if (_tray.IsVisible)
        {
            held = HeldCard(out holder);
            if (held != null && holder != null)
            {
                slot = _tray.SlotNear(held.transform.position, holder.Rig.PalmCenter.position);
                // Mirror the release-time divert: occupied target diverts a non-tray
                // card to the free slot (tray→tray stays put — that is a swap).
                if (slot >= 0 && !_tray.ContainsCard(held) && _tray.Occupant(slot) != null)
                {
                    int other = 1 - slot;
                    slot = _tray.Occupant(other) == null ? other : -1;
                }
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
                // Modal card picks (long-rest burn, avoid-damage, recovers): the 2D
                // UI is click-to-select — VR is poke-to-select on the fan.
                pokeSelect = true;
                for (int i = 0; i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    if (widget.IsSelectable)
                        _fanBuffer.Add(AdoptedCard(widget));
                }
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

        // Configure cards per zone; everything else parks invisibly.
        for (int i = 0; i < _factory.All.Count; i++)
        {
            VRCard card = _factory.All[i];
            if (card == null || card.IsHeld)
                continue;
            bool inFan = _fanBuffer.Contains(card);
            bool inHalf = _halfBuffer.Contains(card);
            bool inTray = _tray.SlotOf(card) >= 0;

            card.PokeSelectEnabled = inFan && pokeSelect;
            card.Grabbable = (inFan && grabbable) || (inTray && grabbable);
            if (!inFan)
                card.ResetColliderRegion(); // fan strips only apply while fanned

            if (!inFan && !inHalf && !inTray)
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

        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null || card.GameCard == null)
        {
            _fan.Add(card);
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

        // Dropping onto an occupied slot diverts to the free one (or bounces).
        // (The highlight already mirrors this divert while telegraphing.)
        if (slot >= 0 && !wasInTray && _tray.Occupant(slot) != null)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }

        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot;

        // THE one log line per real drop (test #14; #15 adds the accepting rule).
        VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
                            $"rule={rule} → " +
                            (slot < 0
                                ? (wasInTray ? "take back to fan." : "return to fan.")
                                : (wasInTray ? $"reorder to slot {slot + 1}." : $"play into slot {slot + 1}.")));

        if (slot >= 0 && !wasInTray)
        {
            // Fan → tray: play the card. The snap itself is PlaceCard's SetHome —
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
        CAbilityCard ability = card.GameCard.AbilityCard;
        CardsHandUI handRef = gameHand;
        // Modal pick (LoseCard/Recover…): select → the game runs its confirm dialog.
        CardActionQueue.Enqueue(
            () => CardsGameApi.SelectCard(handRef, ability),
            () => _dirty = true);
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
        // ClickReady runs the ReadyButton dispatch (Pass/StepComplete — no spin-wait,
        // ScenarioRuleClient.Pass only messages the SRL), but queue it anyway so it
        // serializes behind pending card selects.
        CardActionQueue.Enqueue(
            () =>
            {
                bool fired = CardsGameApi.ClickReady();
                VRLog.Info("Cards", $"Board: CONFIRM → ReadyButton {(fired ? "clicked" : "rejected (not interactable)")}.");
            },
            () => _dirty = true);
    }

    private void OnUndoRequested()
    {
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
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleShortRest(handRef), () => _dirty = true);
    }

    private void OnLongRestRequested()
    {
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleLongRest(handRef), () => _dirty = true);
    }

    private void OnPlayRequested(VRCard card, CBaseCard.ActionType type)
    {
        FullAbilityCard? full = card.FullCard;
        if (full == null)
            return;
        // No spin-wait in OnAbilityClick, but queue anyway: serializes with pending
        // selects and keeps game entries out of interaction callbacks.
        CardActionQueue.Enqueue(() => CardsGameApi.PlayHalf(full, type), () => _dirty = true);
    }

    // ------------------------------------------------------------------ dev fake hand --

    private void RebuildFakeOrClear(Transform anchor)
    {
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
