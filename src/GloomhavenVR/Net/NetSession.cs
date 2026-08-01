namespace GloomhavenVR.Net;

/// <summary>
/// Session-scoped switches of the VR net layer. Deliberately a tiny static: the driver, the
/// version guard and the badges all read the same flag, and a single bool at driver level is
/// what makes flat-net mode trivially reversible on session end (no transport teardown, no
/// Harmony churn — the hook stays installed and inert).
/// </summary>
internal static class NetSession
{
    /// <summary>
    /// FLAT-NET MODE ("Als Flat-Spieler joinen"): the user accepted a mod-version mismatch by
    /// switching ALL mod network functionality off for the rest of the session — no sending
    /// (<c>NetAvatarDriver.TickSend</c>/<c>TickExtrasSend</c> gate), no processing of received
    /// mod packets (<c>NetAvatarDriver.OnPacketReceived</c> gate), remote avatars/boards torn
    /// down. The player KEEPS playing in VR locally; the game's own vanilla multiplayer
    /// (assignment, actions, pings) is untouched — our packets simply stop, which to every
    /// peer looks exactly like a flat player (that is the point).
    ///
    /// Set only by <see cref="VersionGuard"/> on the user's explicit choice; cleared by
    /// <see cref="Reset"/> when the session ends (transport offline), so the next session
    /// starts with full sync again.
    /// </summary>
    internal static bool FlatNetMode;

    /// <summary>Back to full sync (called when the multiplayer session ends).</summary>
    internal static void Reset() => FlatNetMode = false;
}
