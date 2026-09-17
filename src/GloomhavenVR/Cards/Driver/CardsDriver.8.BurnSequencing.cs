using UnityEngine;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    private bool _burnLayoutPending;
    private bool _burnLayoutNativeActive;

    internal static bool BurnLayoutPending => Instance != null && Instance._burnLayoutPending;

    /// <summary>
    /// MB513: the outgoing card's flight hold protected only that card. Rebuild had already
    /// compacted the round dock, active grid and rest fan before its park sweep discovered the
    /// burn, so a replacement could slide through the still-burning original. Discover those
    /// burns before any layout or character exchange, and retain the complete presentation
    /// until the native artwork and existing loss/flight hold finish. The native hand, rules
    /// callbacks, mandatory windows and status pumps continue normally; only card presentation
    /// and the stale cards' input wait. No native animation is shortened by a timeout.
    /// </summary>
    private void RefreshBurnLayoutBarrier()
    {
        // A read-only adopted hand may have no locally running native iterator. The owner's
        // explicit progress also protects it when that record arrives just after focus admission.
        _burnLayoutNativeActive = Net.CardAppearanceMirror.OwnerBurnInProgress(_boundHand?.PlayerActor);
        if (!_fakeActive)
        {
            for (int i = 0; i < _factory.All.Count; i++)
            {
                VRCard card = _factory.All[i];
                if (card == null || card.IsFlying || card.IsVanishing || IsParked(card)) continue;
                AbilityCardUI? widget = card.GameCard;
                if (widget == null) continue;
                ObserveForeignBurnProgress(widget);

                _burnLayoutNativeActive |= BurnArtwork.Playing(BurnArtwork.EffectsOf(card.FullCard))
                    || BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));

                // Model loss precedes the native timeline by several frames. Capture the hold
                // during that gap, while the outgoing slot/grid address is still authoritative.
                // Previously-burnt cards browsed from the lost pile never qualify as fresh.
                if (card.IsHeld || !IsFreshBurn(_boundHand, card)
                    || HasBurnHold(widget)) continue;
                if (_active.Contains(card))
                    _activeExitOrigins[widget] = CapturedActiveSource(widget, widget.PlayerActor);
                _burnHoldSince[widget] = new BurnHold(Time.unscaledTime, artworkSeen: false);
            }
        }

        // A quiet frame must still complete pending holds, even after the game switched its
        // presented actor. Do this before accepting a new layout so its flight uses the old seat.
        FlushBurnHolds("card layout awaits native burn completion");
        // Observe an incoming native sequence even while the outgoing hand still owns a hold;
        // this also publishes the owner's explicit progress for remote admission.
        bool incomingActive = IncomingHandBurnActive();
        bool pending = _burnLayoutNativeActive || _burnHoldSince.Count != 0 || incomingActive;
        if (_burnLayoutPending != pending) _dirty = true;
        _burnLayoutPending = pending;
        if (pending)
        {
            BlockCardInteractions();
            _half.SetReadOnly(true);
        }
    }

    /// <summary>
    /// An incoming character may already be burning before any of its widgets were adopted by
    /// the VR factory. Inspect the original hand before ResolveHand commits that focus and before
    /// a swap parks the old cards. Lost-pile membership alone is historical, so it never starts a
    /// wait or invents a new flight. A completed/cancelled native sequence admits the queued view
    /// on the next frame without a deadline or a gameplay callback.
    /// </summary>
    private bool IncomingHandBurnActive()
    {
        CardsHandUI? incoming = Board.CharacterFocus.PresentedHand(CurrentHand());
        if (incoming == null || ReferenceEquals(incoming, _boundHand)) return false;
        // This is the same native lifetime pair used by BurnArtwork.LosingCards. Inactive hands
        // can retain cancelled bookkeeping; neither flag alone is proof of a running sequence.
        bool active = Net.CardAppearanceMirror.OwnerBurnInProgress(incoming.PlayerActor)
            || incoming.gameObject.activeInHierarchy && incoming.AnimatingLostCards && incoming.animatedLosingCard;
        if (incoming.cardsUI == null) return active;
        foreach (AbilityCardUI widget in incoming.cardsUI)
        {
            if (widget == null) continue;
            Net.CardAppearanceSampler.ObserveBurnProgress(widget);
            active |= BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));
        }
        return active;
    }

    private CardsHandUI? PresentedHandForCardLayout(CardsHandUI? gameHand) =>
        _burnLayoutPending ? _boundHand : Board.CharacterFocus.PresentedHand(gameHand);

    /// <summary>Only the controlled original's real pending flight path may bridge native
    /// completion to ReportCardFx. Historical lost-pile browsing and foreign progress never
    /// authorize this retention, so the sender cannot wait on its own replicated progress.</summary>
    internal static bool ExpectsBurnFlight(ScenarioRuleLibrary.CAbilityCard original)
    {
        CardsDriver? driver = Instance;
        if (driver == null) return false;
        foreach (AbilityCardUI widget in driver._burnHoldSince.Keys)
            if (widget != null && ReferenceEquals(widget.AbilityCard, original)
                && CardsGameApi.ControlsActor(widget.PlayerActor)) return true;
        foreach (VRCard card in driver._factory.All)
        {
            if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing || driver.IsParked(card)) continue;
            AbilityCardUI? widget = card.GameCard;
            if (widget != null && ReferenceEquals(widget.AbilityCard, original)
                && CardsGameApi.ControlsActor(widget.PlayerActor) && driver.IsFreshBurn(driver._boundHand, card)) return true;
        }
        return false;
    }

    private bool DeferLayoutForBurn()
    {
        if (!_burnLayoutPending) return false;
        _dirty = true;
        return true;
    }
}
