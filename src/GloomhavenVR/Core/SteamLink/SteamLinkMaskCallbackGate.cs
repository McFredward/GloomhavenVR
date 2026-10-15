namespace GloomhavenVR.Core.SteamLink;

/// <summary>Actual callback lease check; a retired component cannot stop a fresh settings request.</summary>
internal static class SteamLinkMaskCallbackGate
{
    internal static bool OwnsRequest(object? currentDriver, object driver) => ReferenceEquals(currentDriver, driver);

    internal static bool Admit(ProbePolicy current, ProbePolicy callback, bool sameComponent,
                                bool sameCamera, bool allowed, out bool stopCurrent)
    {
        stopCurrent = false;
        if (!ReferenceEquals(current, callback))
        {
            callback.Stop();
            return false;
        }
        if (!sameComponent || !sameCamera)
            return false;
        if (allowed && current.IsRunning)
            return true;
        current.Stop();
        stopCurrent = true;
        return false;
    }
}
