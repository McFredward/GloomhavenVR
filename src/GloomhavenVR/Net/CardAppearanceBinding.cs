namespace GloomhavenVR.Net;

/// <summary>One received address can never migrate to another model or resurrect after invalidation.</summary>
internal sealed class CardAppearanceBinding<T> where T : class
{
    private T? _model;
    internal CardAppearanceBinding(T? model) => _model = model;
    internal bool Matches(T requested, T? currentAddress)
    {
        if (!ReferenceEquals(_model, currentAddress)) _model = null;
        return _model != null && ReferenceEquals(_model, requested);
    }
}

/// <summary>Immutable class-pool positions, with a distinct high-bit namespace for supply cards.</summary>
internal static class CardAppearancePool
{
    internal const ushort SupplyBit = 0x8000;
    internal static bool TryLocate<T>(System.Collections.Generic.IReadOnlyList<T>? ordinary,
        System.Collections.Generic.IReadOnlyList<T>? supply, T target, out ushort seat, out ushort count) where T : class
    {
        if (Locate(ordinary, target, out seat, out count)) return true;
        if (!Locate(supply, target, out seat, out count)) return false;
        seat |= SupplyBit;
        return true;
    }
    private static bool Locate<T>(System.Collections.Generic.IReadOnlyList<T>? cards, T target,
        out ushort seat, out ushort count) where T : class
    {
        seat = count = 0;
        if (cards == null || cards.Count == 0 || cards.Count > SupplyBit) return false;
        for (int i = 0; i < cards.Count; i++)
            if (ReferenceEquals(cards[i], target)) { seat = (ushort)i; count = (ushort)cards.Count; return true; }
        return false;
    }
    internal static T? Resolve<T>(System.Collections.Generic.IReadOnlyList<T>? ordinary,
        System.Collections.Generic.IReadOnlyList<T>? supply, ushort seat, ushort count) where T : class
    {
        var cards = (seat & SupplyBit) != 0 ? supply : ordinary;
        int position = seat & ~SupplyBit;
        return count > 0 && count <= SupplyBit && cards != null && cards.Count == count && position < count
            ? cards[position] : null;
    }
}
