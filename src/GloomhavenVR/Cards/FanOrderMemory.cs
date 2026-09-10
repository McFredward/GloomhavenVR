using System;
using System.Collections.Generic;

namespace GloomhavenVR.Cards;

/// <summary>Local presentation order keyed by persistent character name and class card ID.
/// No Unity references or game state are retained; widget/scene lifetimes cannot reorder a hand.</summary>
internal sealed class FanOrderMemory
{
    private readonly Dictionary<string, List<int>> _orders = new(StringComparer.Ordinal);

    internal List<int> ForCharacter(string character)
    {
        if (_orders.TryGetValue(character, out List<int>? order)) return order;
        // A process can visit many campaigns. Bound scalar presentation history without clearing
        // any currently presented character on an ordinary scenario transition.
        if (_orders.Count >= 64) _orders.Clear();
        order = new List<int>(24);
        _orders.Add(character, order);
        return order;
    }

    internal List<int> Apply<T>(string character, List<T> cards, List<T> scratch, Func<T, int> key)
    {
        List<int> order = ForCharacter(character);
        for (int i = 0; i < cards.Count; i++)
        {
            int id = key(cards[i]);
            if (id != int.MinValue && !order.Contains(id)) order.Add(id);
        }
        scratch.Clear(); scratch.AddRange(cards); cards.Clear();
        for (int k = 0; k < order.Count; k++)
            for (int i = 0; i < scratch.Count; i++)
                if (key(scratch[i]) == order[k]) { cards.Add(scratch[i]); break; }
        for (int i = 0; i < scratch.Count; i++)
            if (!cards.Contains(scratch[i])) cards.Add(scratch[i]);
        return order;
    }

    internal static void Insert(List<int> order, int id, int before, int after)
    {
        order.Remove(id);
        int at = before != int.MinValue ? order.IndexOf(before) : order.IndexOf(after);
        if (at < 0) at = order.Count;
        else if (before == int.MinValue) at++;
        order.Insert(at, id);
    }

    internal static bool CanReorder(bool controlled, bool normalHand, bool sameCharacter)
        => controlled && normalHand && sameCharacter;
}
