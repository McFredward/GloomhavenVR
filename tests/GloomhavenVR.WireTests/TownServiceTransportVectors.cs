using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownServiceTransportVectors
{
    internal static void Run(Harness t)
    {
        t.Case("Town service original widgets use the real wire header and loss-safe module lanes");
        var frame = Frame(62000, 1);
        byte[] raw = TownServiceCodec.Write(frame);
        t.Wire(Hex.Bytes("31 52 56 47 03 13"), raw, 6, "independent existing GVR1 little-endian header");
        t.Equal(19, NetPacket.PeekType(raw, raw.Length), "production router recognizes town snapshots");
        foreach (bool compressed in new[] { false, true })
        {
            byte[][] pages = ExtrasFragments.Encode(raw, raw.Length, 65536UL + frame.Module,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
            var receiver = new TownServiceFragments();
            byte[]? result = null;
            for (int i = pages.Length - 1; i >= 0; i--)
            {
                t.True(pages[i].Length <= ExtrasFragments.MaxDatagramBytes, "page stays inside global datagram cap");
                t.Equal(62000, TownServiceFragments.Stream(pages[i], pages[i].Length), "every page retains high pool module ID");
                byte[]? next = receiver.Accept(2, pages[i], pages[i].Length, .1);
                if (i > 0) t.True(next == null, "reordered incomplete module stays atomic");
                result = next ?? result;
            }
            t.True(result != null, "actual transport publishes complete module");
            if (result != null) t.Wire(raw, result, result.Length, "all original widget outputs preserved by fragmentation");
            foreach (byte[] page in pages) t.True(receiver.Accept(2, page, page.Length, .2) == null, "completed module cannot replay");
        }
        var queue = new TownServiceSendQueue(65536);
        var frames = new[] { Frame(62000, 1), Frame(4, 1), Frame(65000, 1) };
        foreach (TownServiceFrame row in frames) { byte[] bytes = TownServiceCodec.Write(row); queue.Enqueue(bytes, bytes.Length, row); }
        var multiplexed = new TownServiceFragments();
        var arrived = new HashSet<ushort>();
        for (int i = 0; i < 100; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? complete = multiplexed.Accept(2, page, page.Length, i * .05);
            if (complete != null && TownServiceCodec.TryRead(complete, complete.Length, out TownServiceFrame? decoded)) arrived.Add(decoded!.Module);
        }
        t.Equal(3, arrived.Count, "interleaved pool modules each complete without coalescing different rows");
        queue.Clear();
        frame.Sequence = 5; raw = TownServiceCodec.Write(frame); queue.Enqueue(raw, raw.Length, frame);
        bool reopened = false;
        for (int i = 100; i < 140; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? complete = multiplexed.Accept(2, page, page.Length, i * .05);
            reopened |= complete != null;
        }
        t.True(reopened, "closing and reopening preserves fragment sequence monotonicity");
        ColdService(t, 8, false); ColdService(t, 24, false); ColdService(t, 24, true); ColdService(t, 64, true);
    }
    private static void ColdService(Harness t, int rows, bool contention)
    {
        t.Case("town cold original widget catalog rows=" + rows + " saturatedOtherLanes=" + contention);
        var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var receiver = new TownServiceFragments();
        var expected = new HashSet<ushort>(); int rawTotal = 0, wireTotal = 0, deliveredBytes = 0;
        for (ushort module = 1; module <= rows + 3; module++)
        {
            TownServiceFrame sample = Frame(module, 1, module <= 3 ? 128 : 16);
            byte[] bytes = TownServiceCodec.Write(sample); rawTotal += bytes.Length;
            foreach (byte[] page in ExtrasFragments.Encode(bytes, bytes.Length, 65536UL + module,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true)) wireTotal += page.Length;
            expected.Add(module); sender.Enqueue(bytes, bytes.Length, identity: sample);
        }
        var backgrounds = new List<byte[]>();
        if (contention)
            foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgNativeBoard,
                NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
            {
                var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
                bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = kind;
                backgrounds.Add(bytes);
            }
        double last = -1, finished = -1; int initialCount = expected.Count;
        for (int frame = 0; frame < 90 * 40; frame++)
        {
            double now = frame / 90d;
            if (frame % 9 == 0) foreach (byte[] bytes in backgrounds) sender.Enqueue(bytes, bytes.Length);
            byte[]? packet = sender.NextBatch(now); if (packet == null) continue;
            t.True(last < 0 || now - last >= .05 - 1e-6, "cold catalog preserves global no-burst cadence"); last = now;
            t.True(packet.Length <= ExtrasFragments.MaxDatagramBytes, "cold catalog preserves shared datagram budget");
            byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out byte[][]? batch) ? batch! : new[] { packet };
            foreach (byte[] page in pages)
            {
                if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                deliveredBytes += page.Length;
                byte[]? complete = receiver.Accept(2, page, page.Length, now);
                if (complete != null && TownServiceCodec.TryRead(complete, complete.Length, out TownServiceFrame? sample)) expected.Remove(sample!.Module);
            }
            if (expected.Count == 0) { finished = now; break; }
        }
        t.Equal(0, expected.Count, "all synthetic native-like modules finish atomically before assembler expiry");
        Console.WriteLine("TOWN_COLD rows=" + rows + " modules=" + initialCount + " contention=" + contention
            + " raw=" + rawTotal + " compressedPages=" + wireTotal + " delivered=" + deliveredBytes + " completeSeconds=" + finished.ToString("F3"));
    }
    private static TownServiceFrame Frame(ushort module, ulong sequence, int count = 64)
    {
        var frame = new TownServiceFrame { Service = 1, Session = 99, Module = module, Template = 3,
            Sequence = sequence, Structure = 123, Visible = true, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, Nodes = new TownServiceNode[count] };
        for (int i = 0; i < frame.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = (uint)(i + 1) };
            node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = new[] { i * .13f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
            node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
            if (i % 4 == 3)
            {
                node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new float[40],
                    Text = new[] { "Original native item " + i + " — Äöü shield", "originalFont", "originalSprites" } });
                var material = new TownServiceValue { Numbers = new float[154], Text = new string[62] };
                material.Text[0] = "shader|native"; material.Text[1] = "GLOW";
                for (int p = 0; p < 30; p++) { material.Text[2 + p * 2] = "_Property" + p; material.Text[3 + p * 2] = string.Empty; }
                node.Values.Add(TownServiceProperty.TextMaterial, material);
            }
            frame.Nodes[i] = node;
        }
        return frame;
    }
}
