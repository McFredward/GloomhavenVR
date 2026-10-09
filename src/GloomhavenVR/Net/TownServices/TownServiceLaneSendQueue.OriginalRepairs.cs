using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Only an accepted request for the owner's exact retained source marks
/// a known repair. The pure transport queue never infers an acknowledgment or
/// failed rendering from ordinary traffic, silence or a template address.</summary>
internal static class TownRequestedOriginalRepair
{
    internal static Func<TownServiceFrame, bool>? Source;
}

internal sealed partial class TownServiceLaneSendQueue
{
    private readonly Dictionary<ushort, TownServiceFrame> _requestedOriginals = new();

    private byte[]? TakeRequestedOriginal(double now)
    {
        if (_requestedOriginals.Count == 0) return null;
        TownServiceFrame? census = _manifestFrame ?? _sentManifest;
        foreach (ushort id in _order)
        {
            if (!_requestedOriginals.TryGetValue(id, out TownServiceFrame? original)) continue;
            if (!_queues.TryGetValue(id, out ExtrasSendQueue? queue)
                || !_latestOriginals.TryGetValue(id, out TownServiceFrame? current)
                || !ReferenceEquals(current, original)
                || census != null && (census.Service != original.Service || census.Session != original.Session
                    || !census.Visible || Array.BinarySearch(census.Modules, id) < 0))
            { _requestedOriginals.Remove(id); continue; }
            // The module's old fragmented delta may still name this baseline.
            // Finish those pages without deleting them; the already pinned exact
            // full source follows before its newest dependent artwork revisions.
            byte[]? page = queue.Next(now);
            if (page == null) continue;
            bool repaired = !queue.HasInFlight && ReferenceEquals(queue.CompletedIdentity, original);
            Completed(queue);
            if (repaired) _requestedOriginals.Remove(id);
            return page;
        }
        return null;
    }
}
