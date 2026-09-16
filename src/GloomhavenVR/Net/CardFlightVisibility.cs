using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;

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
    private readonly struct OwnerRelease
    {
        internal OwnerRelease(int actor, byte endpoints, byte flags, CardFlightSource? source, double received, float completionTime, object? originalCard)
        { Actor = actor; Endpoints = endpoints; Flags = flags; Source = source; Received = received; CompletionTime = completionTime; OriginalCard = originalCard; }
        internal readonly int Actor;
        internal readonly byte Endpoints, Flags;
        internal readonly CardFlightSource? Source;
        internal readonly double Received;
        internal readonly float CompletionTime;
        internal readonly object? OriginalCard;
    }
    private static readonly List<OwnerRelease> OwnerReleases = new(8);
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    // A local read-only character view sees the same native burn as that character's owner.
    // Its inactive proxy timeline cannot authorize an earlier flight. The integrator calls
    // this only after validating the canonical sender and deduplicating its semantic event.
    internal static void ObserveOwnerRelease(int actorId, byte endpoints, byte flags, CardFlightSource? source, float completionTime = -1f, object? originalCard = null)
    {
        if (actorId == 0 || NetCardFx.To(endpoints) != CardFxAnchor.Burnt
            || (source.HasValue && source.Value.ActorId != actorId)) return;
        PruneOwnerReleases();
        if (OwnerReleases.Count >= CardBurnCompletionHistory.CountMax) return;
        OwnerReleases.Add(new OwnerRelease(actorId, endpoints, flags, source, Now, completionTime, originalCard));
    }

    internal static bool TryConsumeOwnerRelease(int actorId, CardFxAnchor origin,
        CardFlightSource? source, out byte flags, object? originalCard = null)
    {
        flags = 0;
        PruneOwnerReleases();
        int recess = origin == CardFxAnchor.Slot0 ? 0 : origin == CardFxAnchor.Slot1 ? 1 : -1;
        for (int i = 0; i < OwnerReleases.Count; i++)
        {
            OwnerRelease release = OwnerReleases[i];
            if (originalCard != null && release.OriginalCard != null)
            {
                if (release.Actor != actorId || !ReferenceEquals(release.OriginalCard, originalCard)) continue;
            }
            else if (!BurnReleasePolicy.Matches(actorId, origin == CardFxAnchor.Active, recess, source,
                release.Actor, NetCardFx.From(release.Endpoints), release.Source)) continue;
            flags = release.Flags;
            OwnerReleases.RemoveAt(i);
            return true;
        }
        return false;
    }

    internal static bool TryPeekOwnerRelease(int actorId, CardFxAnchor origin,
        CardFlightSource? source, out byte flags, out float completionTime, object? originalCard = null)
    {
        flags = 0; completionTime = -1f;
        PruneOwnerReleases();
        int recess = origin == CardFxAnchor.Slot0 ? 0 : origin == CardFxAnchor.Slot1 ? 1 : -1;
        for (int i = 0; i < OwnerReleases.Count; i++)
        {
            OwnerRelease release = OwnerReleases[i];
            if (originalCard != null && release.OriginalCard != null)
            {
                if (release.Actor != actorId || !ReferenceEquals(release.OriginalCard, originalCard)) continue;
            }
            else if (!BurnReleasePolicy.Matches(actorId, origin == CardFxAnchor.Active, recess, source,
                release.Actor, NetCardFx.From(release.Endpoints), release.Source)) continue;
            // A visible presentation awaiting its causal frame renews its bounded receipt.
            // Unclaimed old slot receipts still expire, so a later card cannot inherit them.
            OwnerReleases[i] = new OwnerRelease(release.Actor, release.Endpoints, release.Flags,
                release.Source, Now, release.CompletionTime, release.OriginalCard);
            flags = release.Flags; completionTime = release.CompletionTime; return true;
        }
        return false;
    }

    private static void PruneOwnerReleases()
    {
        double now = Now;
        for (int i = OwnerReleases.Count - 1; i >= 0; i--)
            if (OwnerReleases[i].CompletionTime < 0f && now - OwnerReleases[i].Received > 8d) OwnerReleases.RemoveAt(i);
    }

    internal static void Reset()
    {
        s_offered = new ConditionalWeakTable<object, object>();
        OwnerReleases.Clear();
    }
}
