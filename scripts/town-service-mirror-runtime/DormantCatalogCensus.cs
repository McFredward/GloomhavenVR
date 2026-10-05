using System;
using System.Collections;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static partial class MirrorProgram
{
    private static void DormantCatalogCensus()
    {
        TownServiceMirror.Shutdown(); TownServiceSync.ResetNetwork();
        TownServiceSync.UseProductionPublish = true; NetPlayerActors.Peer = 1;
        Transform shared = Go("prepared dormant census frame").transform;
        var catalog = new TownServiceCatalog { OriginalBankPrepared = true, OriginalBankRevision = 1 };
        try
        {
            for (int slot = 0; slot < 2; slot++)
            {
                Transform mount = Go("prepared dormant census mount " + slot, shared).transform;
                var entry = new TownServiceCatalog.Entry { Exposed = slot == 0, ItemId = 980 + slot,
                    PhysicalMount = mount, CardRoot = Go("prepared dormant census item " + slot, mount).transform,
                    FaceRoot = Go("prepared dormant census face " + slot, mount).transform,
                    BodyRoot = Go("prepared dormant census body " + slot, mount).transform,
                    RowContent = Go("prepared dormant census row " + slot, mount).transform,
                    RowSource = Go("native dormant census row " + slot, shared).transform };
                catalog.Entries.Add(entry); catalog.PreparedOriginalEntries.Add(entry); TownServiceCatalog.CardMounts.Add(mount, entry);
            }
            TownServiceSync.TickPublic(shared, shared, catalog, 998, 0f);
            TownServiceSync.TickPublic(shared, shared, catalog, 998, 0f);
            object publisher = typeof(TownServiceSync).GetField("Public", PrivateStatic)!.GetValue(null)!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var sources = (IDictionary)publisher.GetType().GetField("Sources", flags)!.GetValue(publisher)!;
            MethodInfo prune = publisher.GetType().GetMethod("PruneSources", flags)!;
            int reads = TownServiceCatalog.FixtureOwnershipChecks;
            for (int tick = 0; tick < 200; tick++) prune.Invoke(publisher, null);
            Check(TownServiceCatalog.FixtureOwnershipChecks == reads,
                "prepared dormant originals perform zero repeated native ownership walks between censuses");
            var dormant = catalog.Entries[1]; int sourceCount = TownServiceSync.PublicSourceCount, moduleCount = TownServiceSync.PublicModuleCount;
            // This source is still alive. Expire the real cache deadline, then remove the
            // actual native ownership map; no fake ownership/resolution callback is used.
            TownServiceCatalog.CardMounts.Remove(dormant.MountRoot);
            foreach (Transform part in new[] { dormant.MountRoot, dormant.CardRoot, dormant.FaceRoot!, dormant.BodyRoot!, dormant.RowContent! })
                sources[part]!.GetType().GetField("OwnershipCheckAfter", flags)!.SetValue(sources[part], Time.unscaledTime - 1f);
            prune.Invoke(publisher, null);
            Check(TownServiceSync.PublicSourceCount == sourceCount - 5 && TownServiceSync.PublicModuleCount == moduleCount - 5,
                "expired dormant ownership census retires a live original whose real catalog owner was removed");
            var visible = catalog.Entries[0]; visible.Exposed = false;
            TownServiceSync.TickPublic(shared, shared, catalog, 998, 0f);
            sourceCount = TownServiceSync.PublicSourceCount; moduleCount = TownServiceSync.PublicModuleCount;
            // Destruction bypasses the future census deadline for every native part.
            TownServiceCatalog.CardMounts.Remove(visible.MountRoot);
            UnityEngine.Object.DestroyImmediate(visible.MountRoot.gameObject); prune.Invoke(publisher, null);
            Check(TownServiceSync.PublicSourceCount == sourceCount - 5 && TownServiceSync.PublicModuleCount == moduleCount - 5,
                "destroyed prepared original sources and registered bank modules retire immediately before census expiry");
        }
        finally
        {
            TownServiceSync.UseProductionPublish = false; TownServiceSync.ResetNetwork(); TownServiceCatalog.CardMounts.Clear();
        }
    }
}
