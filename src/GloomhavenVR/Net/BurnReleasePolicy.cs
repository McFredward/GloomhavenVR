namespace GloomhavenVR.Net;

/// <summary>Lost-event fallback for a remote burn. A card whose owner still reports it seated
/// cannot fly just because this client's inactive UI lacks the owner's native coroutine.</summary>
internal static class BurnReleasePolicy
{
    internal static bool MayFallback(bool occupancyKnown, int recess, int occupiedMask,
                                     float elapsed, float maximumNativeHold)
    {
        bool seated = occupancyKnown && recess >= 0 && (occupiedMask & (1 << recess)) != 0;
        return !seated && elapsed >= maximumNativeHold;
    }
}
