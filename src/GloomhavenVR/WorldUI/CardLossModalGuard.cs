namespace GloomhavenVR.WorldUI;

/// <summary>Recognizes native card-loss locks that have no interactive flat window.</summary>
internal static class CardLossModalGuard
{
    internal static bool OwnsAllLocks(UIManager? manager, bool cardsActive)
    {
        if (!cardsActive || manager == null)
            return false;

        // AnimateCardsLost locks its hand before yielding or starting LostAnimation.
        // This includes foreign hands, whose inert VR replicas never call BeginBurn.
        var owners = manager.elementsLockUI;
        if (owners == null || owners.Count == 0)
            return false;

        foreach (var owner in owners)
        {
            if (owner == null)
                return false;
            var hand = owner.GetComponent<CardsHandUI>();
            if (hand == null || !hand.AnimatingLostCards)
                return false;
        }

        // CancelAnimateCardLost releases this lock but can leave AnimatingLostCards
        // true. Never use that flag alone, a timeout, or the currently focused hand.
        // Mixed locks are deliberately excluded so unrelated fallback UI stays usable.
        return true;
    }
}
