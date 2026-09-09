using System.Collections.Generic;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    /// <summary>Read-only snapshot of actual visible cards; no pool search or widget activation.</summary>
    internal static void CopyVisibleCards(List<VRCard> into)
    {
        into.Clear();
        if (Instance == null) return;
        foreach (VRCard card in Instance._factory.All)
            if (card != null && card.gameObject.activeInHierarchy && card.FullCard != null) into.Add(card);
    }
}
