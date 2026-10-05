using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Current owner headers and exact native property patches against genuine captured originals.</summary>
internal static class TownCatalogClock
{
    internal static TownServiceFrame Create(TownServiceFrame root, IReadOnlyDictionary<ushort, TownServiceFrame> originals,
        IReadOnlyDictionary<ushort, ulong>? originalKeys = null)
    {
        TownCatalogBank bank = root.CatalogBank!;
        TownServiceFrame reference = TownServiceDelta.Retain(root);
        var headers = new TownServiceFrame[bank.Updates.Length]; var bases = new ulong[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            TownServiceFrame current = bank.Updates[i];
            headers[i] = TownServiceDelta.Retain(current); headers[i].Nodes = Array.Empty<TownServiceNode>();
            if (originals.TryGetValue(current.Module, out var previous) && previous.Session == current.Session && previous.Service == current.Service
                && previous.Sequence < current.Sequence)
            {
                // A retained basis is immutable. The transport computes its real
                // canonical key once when storing it, rather than reserializing
                // every original property for each subsequent page/header clock.
                ulong previousKey = originalKeys != null && originalKeys.TryGetValue(current.Module, out ulong cached) && cached != 0
                    ? cached : TownCatalogBank.ContentKey(previous);
                if (previousKey == bank.Members[i].ContentKey) continue;
                var reauthorized = TownServiceDelta.Retain(previous); reauthorized.PublicClaim = current.PublicClaim;
                if (TownServiceDelta.Compatible(reauthorized, current))
                { headers[i] = TownServiceDelta.Create(reauthorized, current); bases[i] = previousKey; }
            }
        }
        reference.CatalogBank = new TownCatalogBank { Prepared = true, Members = bank.Members, Headers = headers, HeaderBaseKeys = bases };
        return reference;
    }
}
