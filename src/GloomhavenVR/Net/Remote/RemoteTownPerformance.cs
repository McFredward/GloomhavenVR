namespace GloomhavenVR.Net;

/// <summary>Atomic face/body acceptance. A malformed, retired or reordered half can never
/// move the other half into a different author epoch or reconciliation interval.</summary>
internal static class RemoteTownPerformance
{
    internal static bool ObserveLegacyFace(int player, in TownFaceState face)
    {
        if (RemoteTownActivities.KnownPair(player)) return false;
        RemoteTownFaces.Observe(player, in face); return true;
    }
    internal static bool Observe(int player, in TownActivityState activity, in TownFaceState face, bool presence)
    {
        if (!TownActivityCodec.Matches(in activity, in face)
            || !RemoteTownActivities.CanObserve(player, in activity, presence)
            || !RemoteTownFaces.CanObserve(player, in face, presence)) return false;
        if (presence)
        { RemoteTownFaces.ObservePresence(player, in face); RemoteTownActivities.ObservePresence(player, in activity); }
        else
        { RemoteTownFaces.Observe(player, in face); RemoteTownActivities.Observe(player, in activity); }
        return true;
    }
}
