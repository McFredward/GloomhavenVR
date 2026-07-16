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
            _tray.SetVisible(false);
            _half.SetVisible(false);
            return;
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild(anchor);
        }

        UpdatePalmGate();
        UpdateFanLaser();
        _fan.Tick();
        _half.Tick();

        CardsHandUI? hand = CurrentHand();
        if (_tray.IsVisible)
        {
            _tray.TickStatus(_fakeActive ? null : hand);
            _rest.TickStatus(_fakeActive ? null : hand);
        }
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
        if (_gateHand == null)
        {
            if (_fan.IsOpen)
                _fan.Close();
            ClearLaserHover();
            return;
        }

        // Live-tunable gate feel (P6): forgiving Demeo cone on the raw device pose —
        // the visual rig's grip-pitch offset demanded ~60° extra supination (test #8).
        PalmGate gate = _gateHand.PalmGate;
        gate.EnterThreshold = CardsConfig.TiltThreshold.Value;
        gate.ExitThreshold = CardsConfig.TiltExitThreshold;
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
        if (!_fan.TryRaycast(pick.Origin, pick.Direction, out VRCard? card, out Vector3 point, out float dist)
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

        bool trayVisible = false;
        bool halfVisible = false;
        bool pokeSelect = false;
        bool grabbable = false;

        switch (mode)
        {
            case CardHandMode.CardsSelection:
                trayVisible = true;
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

        if (!trayVisible)
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

    private void OnCardGrabbed(VRCard card, VRHand hand)
    {
        if (_fan.Contains(card))
            _fan.Remove(card);
        // Tray occupancy stays until the release decides select/unselect/swap.
    }

    private void OnCardReleased(VRCard card, VRHand hand, Vector3 velocity)
    {
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

        // The inspect pose floats the card toward the face — test the HAND's position
        // too, so "put my hand over the slot and let go" always drops (P6).
        int slot = _tray.SlotAt(card.transform.position);
        if (slot < 0)
            slot = _tray.SlotAt(hand.Rig.PalmCenter.position);
        bool wasInTray = _tray.ContainsCard(card);
        CAbilityCard ability = card.GameCard.AbilityCard;

        // Dropping onto an occupied slot diverts to the free one (or bounces).
        if (slot >= 0 && !wasInTray && _tray.Occupant(slot) != null)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }

        if (slot >= 0 && !wasInTray)
        {
            // Fan → tray: play the card. SelectCard is the spin-wait path — queued;
            // outcome verified against the authoritative round pile afterwards.
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
            //

            _fanBuffer.Clear();
            _fan.SetCards(_fanBuffer);
            _tray.SetVisible(false);
            _half.SetVisible(false);
            if (_boundHand != null)
            {
                _factory.Clear(); // scenario/hand gone: restore faces, drop cards
                _boundHand = null;
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
        int slot = _tray.SlotAt(card.transform.position);
        if (slot < 0)
            slot = _tray.SlotAt(hand.Rig.PalmCenter.position);
        if (slot >= 0 && _tray.Occupant(slot) != null && _tray.Occupant(slot) != card)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }
        if (slot >= 0)
            _tray.PlaceCard(card, slot);
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
