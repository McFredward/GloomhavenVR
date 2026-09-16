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
        _burnLayoutNativeActive = false;
        if (!_fakeActive)
        {
            for (int i = 0; i < _factory.All.Count; i++)
            {
                VRCard card = _factory.All[i];
                if (card == null || card.IsFlying || card.IsVanishing || IsParked(card)) continue;
                AbilityCardUI? widget = card.GameCard;
                if (widget == null) continue;

                _burnLayoutNativeActive |= BurnArtwork.Playing(BurnArtwork.EffectsOf(card.FullCard))
                    || BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));

                // Model loss precedes the native timeline by several frames. Capture the hold
                // during that gap, while the outgoing slot/grid address is still authoritative.
                // Previously-burnt cards browsed from the lost pile never qualify as fresh.
                if (card.IsHeld || !IsFreshBurn(_boundHand, card)
                    || _burnHoldSince.ContainsKey(widget)) continue;
                if (_active.Contains(card))
                    _activeExitOrigins[widget] = CapturedActiveSource(widget, widget.PlayerActor);
                _burnHoldSince[widget] = new BurnHold(Time.unscaledTime, artworkSeen: false);
            }
        }

        // A quiet frame must still complete pending holds, even after the game switched its
        // presented actor. Do this before accepting a new layout so its flight uses the old seat.
        FlushBurnHolds("card layout awaits native burn completion");
        bool pending = _burnLayoutNativeActive || _burnHoldSince.Count != 0 || IncomingHandBurnActive();
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
        bool active = incoming.gameObject.activeInHierarchy && incoming.AnimatingLostCards && incoming.animatedLosingCard;
        if (incoming.cardsUI == null) return active;
        foreach (AbilityCardUI widget in incoming.cardsUI)
        {
            if (widget == null) continue;
            active |= BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));
        }
        return active;
    }

    private CardsHandUI? PresentedHandForCardLayout(CardsHandUI? gameHand) =>
        _burnLayoutPending ? _boundHand : Board.CharacterFocus.PresentedHand(gameHand);

    private bool DeferLayoutForBurn()
    {
        if (!_burnLayoutPending) return false;
        _dirty = true;
        return true;
    }
}
