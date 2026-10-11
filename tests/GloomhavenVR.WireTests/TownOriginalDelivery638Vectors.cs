using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

/// <summary>Independent byte fixtures for the native reliable-delivery selector.
/// Routing classification does not unpack images or change packet/event budgets.</summary>
internal static class TownOriginalDelivery638Vectors
{
    internal static void Run(Harness t)
    {
        t.Case("Town originals alone select native reliable delivery");
        byte[] town = Hex.Bytes("31 52 56 47 03 14 30 0D 29 00 02 00 00 00 00 00 08 00 00 00 31");
        byte[] compressed = Hex.Bytes("31 52 56 47 03 10 40 10 29 00 02 00 00 00 00 00 13 08 00 08 00 00 00 31");
        byte[] raw = Hex.Bytes("31 52 56 47 03 13");
        foreach (byte[] page in new[] { town, compressed, raw })
            t.True(PresentationBatch.HasTownOriginalPage(page, page.Length), "original envelope routes reliably without decoding its artwork");
        byte[] presence = Hex.Bytes("31 52 56 47 03 01");
        byte[] rig = Hex.Bytes("31 52 56 47 03 00");
        byte[] numeric = Hex.Bytes("31 52 56 47 03 1A");
        foreach (byte[] page in new[] { presence, rig, numeric })
            t.True(!PresentationBatch.HasTownOriginalPage(page, page.Length), "independent live motion remains unreliable");
        byte[] ordinaryCompressed = (byte[])compressed.Clone(); ordinaryCompressed[16] = NetProtocol.MsgExtras;
        t.True(!PresentationBatch.HasTownOriginalPage(ordinaryCompressed, ordinaryCompressed.Length), "ordinary compressed presence remains unreliable");
        byte[] batch = PresentationBatch.Write(new List<byte[]> { presence, compressed });
        t.True(PresentationBatch.HasTownOriginalPage(batch, batch.Length), "mixed batch retains its exact original dependency");
        byte[] ordinaryBatch = PresentationBatch.Write(new List<byte[]> { presence, presence });
        t.True(!PresentationBatch.HasTownOriginalPage(ordinaryBatch, ordinaryBatch.Length), "ordinary batch retains unreliable delivery");
        t.Equal(864, PresentationBatch.MaxSize, "native side-action budget is unchanged");
        t.True(!PresentationBatch.HasTownOriginalPage(batch, batch.Length - 1), "truncated child cannot select reliable delivery");
        byte[] trailing = new byte[batch.Length + 1]; Array.Copy(batch, trailing, batch.Length);
        t.True(!PresentationBatch.HasTownOriginalPage(trailing, trailing.Length), "unframed trailing bytes reject a batch");
        byte[] invalid = (byte[])compressed.Clone(); invalid[7] = 15;
        t.True(!PresentationBatch.HasTownOriginalPage(invalid, invalid.Length), "empty compression record is not an original fragment");
        invalid = (byte[])town.Clone(); invalid[4]++;
        t.True(!PresentationBatch.HasTownOriginalPage(invalid, invalid.Length), "foreign protocol does not select native reliable delivery");
        t.True(!PresentationBatch.HasTownOriginalPage(town, town.Length + 1), "caller length is bounded by its actual buffer");
        t.True(!PresentationBatch.HasTownOriginalPage(town, 5), "short header is rejected");
        byte[] overBudget = new byte[865]; Array.Copy(town, overBudget, town.Length);
        t.True(!PresentationBatch.HasTownOriginalPage(overBudget, overBudget.Length), "event cap cannot be widened through classification");
        // Private/public/stock markers occupy the sequence namespace; all three
        // must use the same original-dependency delivery, without touching bytes.
        foreach (int lane in new[] { 0, 1, 2 })
        {
            byte[] page = (byte[])town.Clone(); page[10] = lane == 1 ? (byte)1 : (byte)0;
            page[15] = lane == 2 ? (byte)128 : (byte)0;
            t.True(PresentationBatch.HasTownOriginalPage(page, page.Length), "private/public/stock original marker preserves reliable route");
        }
    }
}
