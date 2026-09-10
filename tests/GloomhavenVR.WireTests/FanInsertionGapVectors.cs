using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanInsertionGapVectors
{
    internal static void Run(Harness t)
    {
        t.Case("fan insertion: explicit active and cleared golden payloads");
        var buffer = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState { HandCardCount = 3, HasFanInsertionGap = true, FanInsertionGap = 3 };
        int length = PresenceSerializer.Write(in state, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 03 80 00 01 47 01 04"), buffer, length,
            "end gap3 is record71 code4 alongside its atomic count3");
        state.FanInsertionGap = -1;
        length = PresenceSerializer.Write(in state, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 03 80 00 01 47 01 00"), buffer, length,
            "closing marker is explicit record71 code0, not a stale omitted record");
        t.True(PresenceSerializer.TryRead(buffer, length, out PresenceState decoded)
            && decoded.HasFanInsertionGap && decoded.FanInsertionGap == -1, "explicit clear decodes");
        state.HasFanInsertionGap = false;
        int legacyLength = PresenceSerializer.Write(in state, buffer);
        t.True(PresenceSerializer.TryRead(buffer, legacyLength, out decoded)
            && !decoded.HasFanInsertionGap && decoded.FanInsertionGap == -1, "legacy absence defaults inactive");

        t.Case("fan insertion: every valid count and boundary survives the codec");
        state.HasFanInsertionGap = true;
        for (int count = 0; count <= 16; count++)
        {
            state.HandCardCount = (byte)count;
            for (int gap = -1; gap <= count; gap++)
            {
                state.FanInsertionGap = gap;
                length = PresenceSerializer.Write(in state, buffer);
                t.True(PresenceSerializer.TryRead(buffer, length, out decoded), "valid packet reads");
                t.Equal(gap, decoded.FanInsertionGap, "gap and count stay atomic at " + count + "/" + gap);
            }
        }
        state.HandCardCount = 3;
        foreach (int invalid in new[] { -99, -2, 4, 16, 17, int.MaxValue })
        {
            state.FanInsertionGap = invalid;
            length = PresenceSerializer.Write(in state, buffer);
            t.True(PresenceSerializer.TryRead(buffer, length, out decoded) && decoded.FanInsertionGap == -1,
                "invalid local gap clears rather than indexing another arc " + invalid);
        }

        t.Case("fan insertion: malformed and duplicate records reject, unknown records skip");
        foreach (string hex in new[] {
            "31 52 56 47 03 01 80 03 80 00 01 47",
            "31 52 56 47 03 01 80 03 80 00 01 47 01",
            "31 52 56 47 03 01 80 03 80 00 01 47 00",
            "31 52 56 47 03 01 80 03 80 00 01 47 02 01 01",
            "31 52 56 47 03 01 80 10 80 00 01 47 01 12",
            "31 52 56 47 03 01 80 03 80 00 01 47 01 05",
            "31 52 56 47 03 01 80 03 80 00 02 47 01 01 47 01 01" })
        {
            byte[] bad = Hex.Bytes(hex);
            t.True(!PresenceSerializer.TryRead(bad, bad.Length, out _), "invalid atomic insertion record rejects " + hex);
        }
        byte[] future = Hex.Bytes("31 52 56 47 03 01 80 03 80 00 03 FA 02 AA BB 47 01 04 01 01 32");
        t.True(PresenceSerializer.TryRead(future, future.Length, out decoded) && decoded.FanInsertionGap == 3
            && decoded.HasHandScale && decoded.HandScaleCode == 50,
            "unknown-before and known-after records preserve byte alignment");
        state.HasFanInsertionGap = false;
        state.HasFanArcOrder = true; state.FanArcOrderCount = 3; state.FanArcOrder = new[] { 2, 0, 1 };
        state.HasCharFocus = true; state.CharFocusActorId = 42;
        int before = PresenceSerializer.Write(in state, buffer);
        state.HasFanInsertionGap = true; state.FanInsertionGap = 2;
        int after = PresenceSerializer.Write(in state, buffer);
        t.Equal(before + 3, after, "actual serialized tail growth is exactly three bytes");
        t.True(PresenceSerializer.TryRead(buffer, after, out decoded) && decoded.HandCardCount == 3
            && decoded.FanArcOrderCount == 3 && decoded.FanArcOrder![0] == 2
            && decoded.CharFocusActorId == 42 && decoded.FanInsertionGap == 2,
            "focus/order/count/insertion arrive as one complete presence snapshot");

        t.Case("fan insertion: actual sampler scratch mutation pre-empts the content cadence");
        var edge = new FanPresentationSendState();
        int[] order = { 0, 1, 2 };
        t.True(edge.HasChanged(true, 3, order, -1), "new connection publishes first complete state");
        edge.MarkSent(true, 3, order, -1);
        t.True(!edge.HasChanged(true, 3, order, -1), "unchanged state does not resend per frame");
        order[0] = 2; order[2] = 0;
        t.True(edge.HasChanged(true, 3, order, -1), "same-count reorder is an immediate edge despite reused buffer");
        edge.MarkSent(true, 3, order, -1);
        foreach (int gap in new[] { 0, 1, 3, -1 })
        {
            t.True(edge.HasChanged(true, 3, order, gap), "open/move/close edge pre-empts " + gap);
            edge.MarkSent(true, 3, order, gap);
            t.True(!edge.HasChanged(true, 3, order, gap), "same gap does not repeatedly pre-empt " + gap);
        }
        t.True(edge.HasChanged(false, 0, order, -1), "removing explicit order is an edge too");
        edge.MarkSent(false, 0, order, -1);
        t.True(!edge.HasChanged(false, 99, order, -1), "inactive scratch count cannot make a resend storm");
        edge.Reset();
        t.True(edge.HasChanged(false, 0, order, -1), "reconnect publishes even an identical fan");
        edge.MarkSent(true, 3, order, 2);
        for (int i = 0; i < 100; i++) edge.HasChanged(true, 3, order, 2);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) edge.HasChanged(true, 3, order, 2);
        t.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - start, "unchanged edge comparisons allocate nothing");
    }
}
