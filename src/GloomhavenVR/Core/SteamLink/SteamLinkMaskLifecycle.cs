namespace GloomhavenVR.Core.SteamLink;

internal enum SteamLinkMaskStep { Idle, Prepare, WaitForOptions, Ready, Continue, Stop }

/// <summary>Actual one-request lifecycle; menu waiting never spends the visible output deadline.</summary>
internal sealed class SteamLinkMaskLifecycle
{
    internal ProbePolicy Policy { get; } = new();
    internal bool HasRequest { get; private set; }
    internal bool Running => Policy.Phase == ProbePhase.Running;

    internal SteamLinkMaskStep Tick(bool requested, bool optionsVisible, bool supportedSession,
                                     bool sameCamera, double now)
    {
        if (!requested)
            return HasRequest ? SteamLinkMaskStep.Stop : SteamLinkMaskStep.Idle;
        if (!supportedSession || Policy.Phase == ProbePhase.Stopped)
            return SteamLinkMaskStep.Stop;
        if (!HasRequest)
        {
            HasRequest = true;
            return SteamLinkMaskStep.Prepare;
        }
        if (Running)
        {
            if (optionsVisible || !sameCamera || !Policy.IsRunning(now))
                return SteamLinkMaskStep.Stop;
            return SteamLinkMaskStep.Continue;
        }
        return optionsVisible ? SteamLinkMaskStep.WaitForOptions : SteamLinkMaskStep.Ready;
    }

    internal bool StopForOptionsOpening()
    {
        if (!Running) return false;
        Policy.Stop();
        return true;
    }

    internal void Stop() => Policy.Stop();
}
