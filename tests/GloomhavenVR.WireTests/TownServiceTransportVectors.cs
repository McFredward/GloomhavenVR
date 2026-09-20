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
    }
    private static TownServiceFrame Frame(ushort module, ulong sequence)
    {
        var frame = new TownServiceFrame { Service = 1, Session = 99, Module = module, Template = 3,
            Sequence = sequence, Structure = 123, Visible = true, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, Nodes = new TownServiceNode[64] };
        for (int i = 0; i < frame.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = (uint)(i + 1) };
            node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = new[] { i * .13f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
            node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
            frame.Nodes[i] = node;
        }
        return frame;
    }
}
