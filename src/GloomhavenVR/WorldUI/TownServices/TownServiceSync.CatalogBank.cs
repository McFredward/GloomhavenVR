using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class TownServiceSync
{
    private TownServiceCatalog? _bankCatalog;
    private uint _bankRevision;
    private int _bankPublished;
    private readonly List<Transform?> _bankRoots = new();

    internal static bool HasPreparedPublicCatalog => Public._bankCatalog != null
        && Public._bankCatalog.OriginalBankPrepared
        && Public._bankPublished == Public._bankCatalog.PreparedOriginalEntries.Count
        && TownServiceMirror.HasPreparedLocalCatalogBank;

    private void ResetCatalogBank()
    { _bankCatalog = null; _bankRevision = 0; _bankPublished = 0; _bankRoots.Clear(); }

    private void PublishOriginalCatalogBank(TownServiceCatalog catalog)
    {
        if (!ReferenceEquals(_bankCatalog, catalog) || _bankRevision != catalog.OriginalBankRevision)
        { _bankCatalog = catalog; _bankRevision = catalog.OriginalBankRevision; _bankPublished = 0; }
        IReadOnlyList<TownServiceCatalog.Entry> prepared = catalog.PreparedOriginalEntries;
        // Preparation has already consumed the original asynchronous native widgets. This
        // bounded queue freezes their canonical partitions once, including dormant pages.
        for (int budget = 0; budget < 8 && _bankPublished < prepared.Count; budget++)
        {
            TownServiceCatalog.Entry entry = prepared[_bankPublished++];
            if (!entry.Current || entry.Sample.IsMoving) continue;
            Publish("merchant.cardmount", entry.MountRoot, prewarm: true);
            Publish("merchant.cardface", entry.FaceRoot, prewarm: true);
            Publish("item." + entry.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.CardRoot, prewarm: true);
            Publish("merchant.cardbody", entry.BodyRoot, prewarm: true);
            if (entry.RowContent != null)
                Publish("merchant.row", entry.RowContent, entry.RowSource.transform, entry.RowCloneOf, prewarm: true);
            _bankRoots.Clear(); _bankRoots.Add(entry.MountRoot); _bankRoots.Add(entry.FaceRoot);
            _bankRoots.Add(entry.CardRoot); _bankRoots.Add(entry.BodyRoot); _bankRoots.Add(entry.RowContent);
            foreach (Transform? root in _bankRoots)
                if (root != null && Sources.TryGetValue(root, out SourceEntry? source))
                    foreach (Published part in source.Parts)
                    {
                        part.CatalogResident = true;
                        TownServiceMirror.SetCatalogSource(part.Id, _bankRevision, !entry.Warm);
                        foreach (TownServiceMerchantDrawer rack in catalog.Drawers)
                            if (Sources.TryGetValue(rack.HousingRoot, out SourceEntry? housing) && housing.Parts.Count == 1)
                                TownServiceMirror.SetRackMember(part.Id, housing.Parts[0].Id, (ushort)entry.Page,
                                    rack.TurnEpoch, false, entry.PageGate);
                    }
        }
    }
}
