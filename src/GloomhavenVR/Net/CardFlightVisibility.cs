using System.Runtime.CompilerServices;

namespace GloomhavenVR.Net;

/// <summary>Owner-local provenance, captured while the short-rest offer exists and consumed by
/// its actual burn launch. Weak model keys survive widget recycling without retaining a scenario.</summary>
internal static class CardFlightVisibility
{
    internal const byte CoveredBurnBit = NetProtocol.CardFxCoveredBurnBit;
    private static ConditionalWeakTable<object, object> s_offered = new();
    private static readonly object Marker = new();

    internal static void MarkShortRest(object? card)
    {
        if (card == null) return;
        s_offered.Remove(card);
        s_offered.Add(card, Marker);
    }

    internal static void Forget(object? card)
    {
        if (card != null) s_offered.Remove(card);
    }

    internal static byte ConsumeBurn(object? card)
    {
        if (card == null || !s_offered.Remove(card)) return 0;
        return CoveredBurnBit;
    }

    // Closing the visual context alone is not cancellation: a model loss can arrive afterward.
    // A returned/played/activated card or a later round, however, ends the old offer's lifetime.
    internal static bool KeepObservedCandidate(bool actorAvailable, bool returnedToResources,
        bool alreadyLost, bool laterRound) => actorAvailable && !returnedToResources
            && (alreadyLost || !laterRound);

    internal static bool Covered(byte flags) => (flags & CoveredBurnBit) != 0;
    internal static void Reset() => s_offered = new ConditionalWeakTable<object, object>();
}
