using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    /// <summary>Seek the validated original rack to its separately delivered control
    /// clock. The input packet never invents original member IDs or substitutes local
    /// widgets: a new epoch first needs its corresponding validated native bank header.</summary>
    internal static void ApplyPublicControlClock(int author, uint session, TownRackState control)
    {
        int peer = -author;
        if (author <= 0 || author != PublicAuthor || !Sessions.TryGetValue(peer, out TownServiceSessionInfo? current)
            || current.Session != session || !RemoteRacks.TryGetValue(peer, out var racks)) return;
        foreach (RackPlayback clock in racks.Values)
        {
            TownRackState native = clock.State;
            if (native == null || native.Turn != control.Turn || native.From != control.From || native.To != control.To) continue;
            // Preserve original geometry, content membership, stock layout and template
            // dependencies. Only the actual original numeric drawer animation is sought.
            float elapsed = Mathf.Max(native.Elapsed, control.Elapsed);
            native.Elapsed = elapsed;
            native.Page = TownRackState.Progress(elapsed) < .5f ? native.From : native.To;
            native.LeadAngle = control.LeadAngle; native.ScrollDirection = control.ScrollDirection;
            native.PageCount = control.PageCount;
            clock.Elapsed = Mathf.Max(clock.Elapsed, elapsed);
            clock.ReceivedTime = Time.unscaledTime;
        }
    }
}
