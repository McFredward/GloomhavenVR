using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static partial class MirrorProgram
{
    private static void StockPublicationRouting()
    {
        var catalog = new TownServiceCatalog();
        TownServiceCatalog.Entry Entry(string name, int id, bool moving)
        {
            Transform mount = Go(name + " original moving mount").transform;
            Transform face = Rect(name + " original item widget", mount, Vector2.zero, new Vector2(80, 120));
            Transform body = Go(name + " original item backing").transform; body.SetParent(mount, false);
            Transform row = Rect(name + " original price", mount, Vector2.zero, new Vector2(80, 20));
            var entry = new TownServiceCatalog.Entry { ItemId = id, CardRoot = face, BodyRoot = body,
                RowContent = row, RowSource = Go(name + " native row source").transform };
            entry.Sample.IsMoving = moving; entry.Sample.IsPhysical = true;
            catalog.Entries.Add(entry); return entry;
        }
        var held = Entry("held stock", 71, true); held.Sample.IsHeld = true; held.Exposed = false;
        var returning = Entry("returning original stock", 72, true);
        var parked = Entry("parked stock palm", 73, true);
        var stationary = Entry("stationary rack card", 74, false);
        var retired = Entry("retired native borrower", 75, true); retired.Current = false;
        TownServiceSync.PublishStockFixture(catalog);
        // The canonical avatar remains the only visible held author (receiver masking is
        // exercised through the real mirror pipeline in CardReturns626). Prepare the same
        // original parts here before release so a short return never starts with cold artwork.
        Check(TownServiceSync.Calls.Count == 12, "independent stock publisher contains only current moving original samples");
        foreach (var entry in new[] { held, returning, parked })
        {
            Check(TownServiceSync.Calls.FindAll(call => call.Key == "merchant.heldstock" && call.Source == entry.MountRoot).Count == 1,
                "held stock uses its explicit visitor-owned original mount");
            Check(TownServiceSync.Calls.FindAll(call => call.Key == "item." + entry.ItemId && call.Source == entry.CardRoot).Count == 1,
                "held stock retains the original model-aware native item front");
            Check(TownServiceSync.Calls.FindAll(call => call.Key == "merchant.heldstock.body" && call.Source == entry.BodyRoot).Count == 1,
                "held stock retains the original physical backing");
            Check(TownServiceSync.Calls.FindAll(call => call.Key == "merchant.heldstock.row" && call.Source == entry.RowContent
                && call.Provenance == entry.RowSource && call.CloneOf!(entry.RowSource) == entry.RowContent).Count == 1,
                "held stock price retains its original native row provenance");
            Check(TownServiceSync.StockFixturePriority(entry.MountRoot) && TownServiceSync.StockFixturePriority(entry.CardRoot)
                && TownServiceSync.StockFixturePriority(entry.BodyRoot!) && TownServiceSync.StockFixturePriority(entry.RowContent!),
                "every moving original stock part keeps its animation publication priority");
        }
        foreach (var entry in new[] { stationary, retired })
            Check(!TownServiceSync.Calls.Exists(call => call.Source == entry.MountRoot || call.Source == entry.CardRoot
                || call.Source == entry.BodyRoot || call.Source == entry.RowContent)
                && !TownServiceSync.StockFixturePriority(entry.MountRoot) && !TownServiceSync.StockFixturePriority(entry.CardRoot)
                && !TownServiceSync.StockFixturePriority(entry.BodyRoot!) && !TownServiceSync.StockFixturePriority(entry.RowContent!),
                "stock visitor lane never duplicates stationary or retired cabinet cards");
        foreach (var entry in catalog.Entries) { entry.Sample.IsMoving = false; entry.Sample.IsHeld = false; }
        returning.Sample.ReturnNumbers = new float[38]; returning.Sample.ReturnStarted = Time.unscaledTime;
        TownServiceSync.PublishStockFixture(catalog);
        Check(TownServiceSync.Calls.Count == 4 && TownServiceSync.Calls.TrueForAll(call =>
            call.Source == returning.MountRoot || call.Source == returning.CardRoot
            || call.Source == returning.BodyRoot || call.Source == returning.RowContent),
            "completed original stock return retains only its exact endpoint through the bounded observer grace");
        returning.Sample.ReturnNumbers = null;
        TownServiceSync.PublishStockFixture(catalog);
        Check(TownServiceSync.Calls.Count == 0, "completed stock returns leave no visitor stock publication");
    }
}

namespace GloomhavenVR.WorldUI
{
    internal sealed partial class TownServiceSync
    {
        internal static void PublishStockFixture(TownServiceCatalog catalog)
        { Calls.Clear(); Private.PriorityRoots.Clear(); Private.PublishStockEntries(catalog); }
        internal static bool StockFixturePriority(Transform source) => Private.PriorityRoots.Contains(source);
    }
}
