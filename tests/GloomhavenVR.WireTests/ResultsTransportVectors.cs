using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class ResultsTransportVectors
{
    internal static void Run(Harness t)
    {
        t.Case("761: results125 has an independent bounded transport and unchanged presence");
        t.True(NetProtocol.MsgScenarioResults == 29 && NetProtocol.MsgScenarioResultsFragments == 30
            && NetProtocol.ExtIdScenarioResults == 125 && ScenarioResultsCodec.MessageType == 29
            && ScenarioResultsCodec.FragmentType == 30 && ScenarioResultsCodec.RecordId == 125,
            "literal independent result message and record identities match the actual dispatcher");
        var state = new ScenarioResultsState { Key = 41, Revision = 3, Author = 1, Visible = false };
        byte[] ordinary = ScenarioResultsCodec.Write(state);
        // Additive readers must skip unknown metadata. This also makes a valid,
        // compressible Results packet independently exercise the OUTER unchanged64
        // compression/fragment path instead of testing only the inner125 codec.
        using var padded = new MemoryStream(); padded.Write(ordinary, 0, ordinary.Length);
        var padding = new byte[257]; padding[0] = 250; padding[1] = 255;
        for (int i = 2; i < padding.Length; i++) padding[i] = 0x5a;
        for (int i = 0; i < 20; i++) padded.Write(padding, 0, padding.Length);
        byte[] packet = padded.ToArray();
        t.True(ScenarioResultsCodec.TryRead(packet, packet.Length, out var decoded)
            && decoded!.Key == 41 && !decoded.Visible, "actual125 reader skips the unrelated padded future TLVs");
        byte[][] compressed = ExtrasFragments.Encode(packet, packet.Length, 10,
            ScenarioResultsCodec.MessageType, ScenarioResultsCodec.FragmentType,
            ScenarioResultsCodec.MaxBytes, compress: true);
        var assembler = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes,
            ExtrasFragments.PresentationAssemblyLifetime);
        byte[]? complete = null;
        for (int i = compressed.Length - 1; i >= 0; i--)
        {
            byte[] page = compressed[i];
            t.True(page.Length <= ExtrasFragments.MaxDatagramBytes
                && NetPacket.PeekType(page, page.Length) == NetProtocol.MsgPresentationCompression
                && ExtrasFragments.CompressedPayloadType(page, page.Length) == 29,
                "outer64 permits the actual Results stream without altering its metadata grammar");
            byte[]? next = assembler.Accept(1, page, page.Length, 1);
            if (i != 0) t.True(next == null, "no partial Results125 picture is published");
            else complete = next;
        }
        t.True(complete != null, "all reversed compressed pages complete one atomic Results125 picture");
        t.Wire(packet, complete!, complete!.Length, "outer compression preserves every125 and future-TLV byte");
        t.True(ScenarioResultsCodec.TryRead(complete, complete.Length, out decoded) && decoded!.Revision == 3,
            "the production125 parser accepts the actual fully reassembled outer stream");
        t.True(assembler.Accept(1, compressed[0], compressed[0].Length, 2) == null,
            "already completed sequence cannot replay its public picture");

        byte[] presence = new byte[PresenceSerializer.MaxSize];
        int presenceLength = PresenceSerializer.Write(default, presence);
        Array.Resize(ref presence, presenceLength);
        var scheduler = new ExtrasSendScheduler(10, NetProtocol.MsgUseBarAnimation,
            NetProtocol.MsgUseBarAnimationFragments);
        scheduler.Enqueue(packet, packet.Length); scheduler.Enqueue(presence, presence.Length);
        var receive = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes,
            ExtrasFragments.PresentationAssemblyLifetime);
        var presenceReceive = new ExtrasFragments();
        bool sawResults = false, sawPresence = false;
        for (int step = 0; step < 200 && !(sawResults && sawPresence); step++)
        {
            byte[]? batch = scheduler.NextBatch(step * .051);
            if (batch == null) continue;
            t.True(batch.Length <= ExtrasFragments.MaxDatagramBytes, "Results never increases the existing event cap");
            byte[][] pages;
            if (NetPacket.PeekType(batch, batch.Length) == NetProtocol.MsgPresentationBatch)
            {
                t.True(PresentationBatch.TryRead(batch, batch.Length, out byte[][]? unpacked),
                    "production batch reader admits the Results envelope child");
                pages = unpacked!;
            }
            else pages = new[] { batch };
            foreach (byte[] page in pages)
            {
                int type = NetPacket.PeekType(page, page.Length);
                if (type == 1) sawPresence = PresenceSerializer.TryRead(page, page.Length, out _);
                else if (type == 2 || type == 16 && ExtrasFragments.CompressedPayloadType(page, page.Length) == 1)
                {
                    byte[]? restored = presenceReceive.Accept(1, page, page.Length, step * .051);
                    if (restored != null) sawPresence = PresenceSerializer.TryRead(restored, restored.Length, out _);
                }
                else if (type == 16 || type == 30)
                {
                    byte[]? restored = receive.Accept(1, page, page.Length, step * .051);
                    if (restored != null) sawResults = ScenarioResultsCodec.TryRead(restored, restored.Length, out decoded);
                }
            }
        }
        t.True(sawResults && sawPresence,
            "one actual scheduler serves Results plus original presence through bounded batch and receive paths");
        byte[][] uncompressed = ExtrasFragments.Encode(packet, packet.Length, 11, 29, 30,
            ScenarioResultsCodec.MaxBytes, compress: false);
        var plain = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes,
            ExtrasFragments.PresentationAssemblyLifetime);
        for (int i = 0; i < uncompressed.Length; i++)
        {
            t.True(PresentationBatch.ChildType(NetPacket.PeekType(uncompressed[i], uncompressed[i].Length)),
                "uncompressed30 is admitted too, not only compressed16");
            complete = plain.Accept(1, uncompressed[i], uncompressed[i].Length, 3);
            t.True((complete != null) == (i == uncompressed.Length - 1), "plain125 assembly remains atomic");
        }
        t.Wire(packet, complete!, complete!.Length, "uncompressed independent stream retains exact original125 bytes");
        Dependencies(t);
    }
    private static ScenarioResultsState Picture(ulong revision, float x = 0, uint key = 71)
    {
        var node = new GloomhavenVR.Net.TownServices.TownServiceNode { Binding = 1 };
        node.Values.Add(GloomhavenVR.Net.TownServices.TownServiceProperty.Transform,
            new GloomhavenVR.Net.TownServices.TownServiceValue { Numbers = new[] { x, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
        return new ScenarioResultsState { Key = key, Author = 1, Revision = revision, Visible = true,
            Parts = new[] { new ScenarioResultsPart { Id = 1, Structure = 33, Address = "native.root", Nodes = new[] { node } } } };
    }
    private static byte[] Large(ScenarioResultsState state)
    {
        using var bytes = new MemoryStream(); byte[] body = ScenarioResultsCodec.Write(state); bytes.Write(body, 0, body.Length);
        var random = new Random(761); var tail = new byte[257]; tail[0] = 250; tail[1] = 255;
        for (int row = 0; row < 20; row++)
        { for (int i = 2; i < tail.Length; i++) tail[i] = (byte)random.Next(256); bytes.Write(tail, 0, tail.Length); }
        return bytes.ToArray();
    }
    private static void Dependencies(Harness t)
    {
        t.Case("761: pending cumulative changes cannot drop their reliable full dependency");
        ScenarioResultsState basis = Picture(1);
        ScenarioResultsState second = ScenarioResultsDelta.Create(basis, Picture(2, 2))!;
        ScenarioResultsState third = ScenarioResultsDelta.Create(basis, Picture(3, 3))!;
        var queue = new ExtrasSendScheduler(100, 3, 4);
        byte[] full = Large(basis); queue.Enqueue(full, full.Length, identity: basis);
        byte[] firstPage = queue.NextBatch(0)!;
        var receive = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
        t.True(receive.Accept(1, firstPage, firstPage.Length, 0) == null,
            "first reliable baseline page cannot expose an incomplete picture");
        foreach (var state in new[] { second, third })
        { byte[] encoded = ScenarioResultsCodec.Write(state); queue.Enqueue(encoded, encoded.Length, identity: state); }
        ScenarioResultsState? completeBasis = null, expanded = null; bool sawSecond = false;
        for (int step = 1; step < 100; step++)
        {
            byte[]? page = queue.NextBatch(step * .051); if (page == null) continue;
            t.True(PresentationBatch.HasResultsPage(page, page.Length) && !PresentationBatch.HasTownOriginalPage(page, page.Length),
                "exact Results pages request reliable delivery without masquerading as town originals");
            byte[]? restored = receive.Accept(1, page, page.Length, step * .051);
            if (restored == null) continue;
            t.True(ScenarioResultsCodec.TryRead(restored, restored.Length, out ScenarioResultsState? snapshot),
                "actual queued Results packet survives complete receive parsing");
            if (snapshot!.BaseRevision == 0) completeBasis = snapshot;
            else
            { sawSecond |= snapshot.Revision == 2; expanded = ScenarioResultsDelta.Expand(completeBasis, snapshot); }
        }
        t.True(completeBasis?.Revision == 1 && !sawSecond && expanded?.Revision == 3
            && expanded.Parts[0].Nodes[0].Values[GloomhavenVR.Net.TownServices.TownServiceProperty.Transform].Numbers[0] == 3,
            "coalesced latest change retains and expands its exact complete basis after dropping intermediate motion");

        t.Case("761: a new complete original supersedes obsolete unfinished output");
        queue = new ExtrasSendScheduler(200, 3, 4); queue.Enqueue(full, full.Length, identity: basis);
        firstPage = queue.NextBatch(0)!; receive = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
        receive.Accept(1, firstPage, firstPage.Length, 0);
        byte[] oldDelta = ScenarioResultsCodec.Write(third); queue.Enqueue(oldDelta, oldDelta.Length, identity: third);
        var fresh = Picture(4, 4, 72); byte[] newFull = ScenarioResultsCodec.Write(fresh);
        queue.Enqueue(newFull, newFull.Length, identity: fresh);
        bool freshSeen = false, obsoleteSeen = false;
        for (int step = 1; step < 80; step++)
        {
            byte[]? page = queue.NextBatch(step * .051); if (page == null) continue;
            byte[]? restored = receive.Accept(1, page, page.Length, step * .051); if (restored == null) continue;
            t.True(ScenarioResultsCodec.TryRead(restored, restored.Length, out ScenarioResultsState? snapshot),
                "superseding original is still a valid complete125");
            freshSeen |= snapshot!.Key == 72 && snapshot.Revision == 4;
            obsoleteSeen |= snapshot.Key == 71;
        }
        t.True(freshSeen && !obsoleteSeen,
            "new complete state cancels old incomplete pages and every dependent waiting old delta");

        t.Case("761: same-opening full repair for a late participant keeps a bounded opening share");
        var late = Picture(5, 5, 72); byte[] lateFull = Large(late);
        queue.Enqueue(lateFull, lateFull.Length, identity: late);
        receive = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
        bool lateSeen = false;
        for (int step = 80; step < 106; step++)
        {
            // Keep another public lane busy to expose regular-turn starvation.
            byte[] presence = new byte[PresenceSerializer.MaxSize];
            int count = PresenceSerializer.Write(default, presence);
            queue.Enqueue(presence, count);
            byte[]? page = queue.NextBatch(step * .051); if (page == null) continue;
            byte[][] children = NetPacket.PeekType(page, page.Length) == NetProtocol.MsgPresentationBatch
                && PresentationBatch.TryRead(page, page.Length, out var unpacked) ? unpacked! : new[] { page };
            foreach (byte[] child in children)
            {
                if (!PresentationBatch.HasResultsPage(child, child.Length)) continue;
                byte[]? restored = receive.Accept(1, child, child.Length, step * .051);
                lateSeen |= restored != null && ScenarioResultsCodec.TryRead(restored, restored.Length, out var snapshot)
                    && snapshot!.Revision == 5;
            }
        }
        t.True(lateSeen, "late participant receives the same-opening complete original within the bounded reservation, alongside active presence");
    }

}
