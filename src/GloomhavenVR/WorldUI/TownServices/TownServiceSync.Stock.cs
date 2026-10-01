using System;
using System.Globalization;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class TownServiceSync
{
    private static readonly TownServiceSync Stock = new();

    /// <summary>A lifted stock sample belongs to its visitor, independently of the public
    /// cabinet author and of the NPC currently receiving the visitor's other hand.</summary>
    private static void TickStock(Transform sharedFrame)
    {
        using (TownServiceMirror.UseStockLane()) Stock.TickStockCore(sharedFrame);
    }

    internal static void ResetStock()
    {
        using (TownServiceMirror.UseStockLane()) Stock.ResetCore();
    }

    private void TickStockCore(Transform sharedFrame)
    {
        TownServiceCatalog? catalog = TownServicePublicMerchant.Catalog;
        Transform? station = TownServicePublicMerchant.StationRoot;
        uint session = TownServicePublicMerchant.Session;
        bool moving = false;
        if (catalog != null && station != null)
            foreach (TownServiceCatalog.Entry entry in catalog.Entries)
                if (entry.Current && entry.Sample.IsMoving) { moving = true; break; }
        if (!moving || catalog == null || station == null || session == 0)
        { ResetCore(); return; }
        _sharedFrame = sharedFrame;
        PrepareCore();
        if (!NativeTemplates.Ready || _generationExhausted) return;
        if (_session != session)
        {
            if (_generation == uint.MaxValue)
            {
                ResetCore(); _generationExhausted = true;
                Report("stock generation", new InvalidOperationException(
                    "Stock presentation generation exhausted; restart the mod to resume publishing."));
                return;
            }
            ResetCore(); _session = session; _service = 1; _nextId = 0; _generation++;
        }
        TownServiceMirror.BeginSession(1, _generation, sharedFrame, station,
            TownServicePublicMerchant.SessionAge);
        foreach (Published module in Modules.Values) module.Seen = false;
        foreach (SourceEntry source in Sources.Values) source.Seen = false;
        Visited.Clear(); Dynamic.Clear(); PriorityRoots.Clear();
        PublishStockEntries(catalog);
        Removed.Clear();
        foreach (var pair in Modules) if (!pair.Value.Seen) Removed.Add(pair.Key);
        foreach (string key in Removed)
        { TownServiceMirror.UnregisterModule(Modules[key].Id); Modules.Remove(key); }
        PruneSources();
    }

    private void PublishStockEntries(TownServiceCatalog catalog)
    {
        foreach (TownServiceCatalog.Entry entry in catalog.Entries)
        {
            if (!entry.Current || !entry.Sample.IsMoving) continue;
            PriorityRoots.Add(entry.MountRoot); PriorityRoots.Add(entry.CardRoot);
            if (entry.BodyRoot != null) PriorityRoots.Add(entry.BodyRoot);
            if (entry.RowContent != null) PriorityRoots.Add(entry.RowContent);
            // These are the actual lifted originals. The address distinguishes the
            // visitor's sample from an identical owned item and from the shared rack;
            // the original model-aware face remains a child of this one moving mount.
            Publish("merchant.heldstock", entry.MountRoot, prewarm: true);
            Publish("item." + entry.ItemId.ToString(CultureInfo.InvariantCulture), entry.CardRoot, prewarm: true);
            Publish("merchant.heldstock.body", entry.BodyRoot, prewarm: true);
            if (entry.RowContent != null)
                Publish("merchant.heldstock.row", entry.RowContent, entry.RowSource.transform,
                    entry.RowCloneOf, prewarm: true);
        }
    }
}
