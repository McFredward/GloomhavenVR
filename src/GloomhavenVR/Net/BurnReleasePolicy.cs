namespace GloomhavenVR.Net;

/// <summary>Lost-event fallback for a remote burn. A card whose owner still reports it seated
/// cannot fly just because this client's inactive UI lacks the owner's native coroutine.</summary>
internal static class BurnReleasePolicy
{
    internal static bool RetireWithoutFlight(bool observedOwnerProgress, bool ownerRunning, bool hasOwnerRelease, bool nativePlaying)
        => observedOwnerProgress && !ownerRunning && !hasOwnerRelease && !nativePlaying;

    internal static bool MayFallback(bool occupancyKnown, int recess, int occupiedMask,
                                     float elapsed, float maximumNativeHold)
    {
        bool seated = occupancyKnown && recess >= 0 && (occupiedMask & (1 << recess)) != 0;
        return !seated && elapsed >= maximumNativeHold;
    }
    // A source belongs to one actor and one origin population. A focus change must never
    // retarget a pending burn or let a Board/Slot event release an active-cell claim.
    internal static bool Matches(int actor, bool active, int recess, CardFlightSource? claimSource,
        int eventActor, CardFxAnchor origin, CardFlightSource? eventSource)
    {
        if (actor == 0 || actor != eventActor || active != (origin == CardFxAnchor.Active)) return false;
        int eventRecess = origin == CardFxAnchor.Slot0 ? 0 : origin == CardFxAnchor.Slot1 ? 1 : -1;
        if (eventRecess >= 0 && recess != eventRecess) return false;
        if (!eventSource.HasValue || eventSource.Value.Count == 0) return true;
        return claimSource.HasValue && claimSource.Value.ActorId == eventSource.Value.ActorId
            && claimSource.Value.Seat == eventSource.Value.Seat
            && claimSource.Value.Count == eventSource.Value.Count;
    }

    internal static bool OwnsBoard(int actor, int displayedActor) => actor != 0 && actor == displayedActor;
}
