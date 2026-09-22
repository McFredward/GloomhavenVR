using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class MapButtonTooltipVectors
{
    internal static void Run(Harness t)
    {
        t.Case("82 map button tooltips: atomic native output, explicit clear, bounded transport");
        var bytes = new byte[MapButtonTooltipCodec.MaxSize];
        int size = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(1, null), bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 17 52 06 00 01 00 00 80 3F"), bytes, size,
            "independent golden clear reserves NPC message and TLV IDs");
        t.True(MapButtonTooltipCodec.TryRead(bytes, size, out var clear) && clear!.Payload == null
            && clear.SampleTime == 1, "clear preserves source time");
        size = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(2, new byte[] { 1, 254, 17 }), bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 17 52 09 00 01 00 00 00 40 01 FE 11"), bytes, size,
            "independent golden visible picture");
        for (int cut = 0; cut < size; cut++)
            t.True(!MapButtonTooltipCodec.TryRead(bytes, cut, out _), "truncated native picture rejected " + cut);
        var unknown = new byte[size + 3]; Array.Copy(bytes, unknown, size);
        unknown[size] = 250; unknown[size + 1] = 1; unknown[size + 2] = 42;
        t.True(MapButtonTooltipCodec.TryRead(unknown, unknown.Length, out _), "future TLV skipped");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            byte[] broken = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(invalid), 0, broken, 10, 4);
            t.True(!MapButtonTooltipCodec.TryRead(broken, size, out _), "invalid source time rejected");
        }
        var payload = new byte[MapButtonTooltipCodec.MaxPayload];
        new Random(546).NextBytes(payload);
        size = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(3, payload), bytes);
        t.Equal(MapButtonTooltipCodec.MaxSize, size, "maximum native picture fits declared exact bound");
        t.True(MapButtonTooltipCodec.TryRead(bytes, size, out var maximum), "maximum picture decodes");
        t.Wire(payload, maximum!.Payload!, maximum.Payload!.Length, "all native output bytes survive");
        t.True(!MapButtonTooltipCodec.TryRead(bytes, size - 1, out _), "partial last page rejected");
        byte[] bad = (byte[])bytes.Clone(); bad[8] = 1;
        t.True(!MapButtonTooltipCodec.TryRead(bad, size, out _), "reordered pages rejected");
        bad = (byte[])bytes.Clone(); bad[9] = 1;
        t.True(!MapButtonTooltipCodec.TryRead(bad, size, out _), "inconsistent page count rejected");
        var scheduler = new ExtrasSendScheduler(100, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        scheduler.Enqueue(bytes, size, identity: new MapButtonTooltipSnapshot(3, payload));
        int hiddenSize = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(4, null), bytes);
        scheduler.Enqueue(bytes, hiddenSize, identity: new MapButtonTooltipSnapshot(4, null));
        var assembler = new ExtrasFragments(NetProtocol.MsgMapButtonTooltip, NetProtocol.MsgMapButtonTooltipFragments,
            MapButtonTooltipCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime);
        int complete = 0;
        for (int i = 0; i < 200 && complete < 2; i++)
        {
            double now = i * .051;
            byte[]? page = scheduler.Next(now);
            if (page == null) continue;
            t.True(page.Length <= ExtrasFragments.MaxDatagramBytes, "shared event cap retained");
            byte[]? packet = assembler.Accept(2, page, page.Length, now);
            if (packet == null) continue;
            t.True(MapButtonTooltipCodec.TryRead(packet, packet.Length, out var received), "scheduled picture is complete");
            t.True(complete == 0 ? received!.Payload != null : received!.Payload == null,
                "visible boundary arrives before disappearance");
            complete++;
        }
        t.Equal(2, complete, "maximum incompressible picture and clear both finish");
        scheduler.Clear();
        t.True(scheduler.Next(100) == null, "session reset clears tooltip queue");

        var first = new MapButtonTooltipSnapshot(5, new byte[] { 1, 2, 0 });
        var animated = new MapButtonTooltipSnapshot(6, new byte[] { 1, 2, 1 });
        var other = new MapButtonTooltipSnapshot(7, new byte[] { 1, 255, 0 });
        t.True(MapButtonTooltipSnapshot.SameIdentity(first, animated), "animation stays in the same cap episode");
        t.True(!MapButtonTooltipSnapshot.SameIdentity(first, other), "city hover is a distinct identity boundary");
        var pending = new System.Collections.Generic.List<MapButtonTooltipSnapshot>();
        foreach (var sample in new[] { first, animated, other, new MapButtonTooltipSnapshot(8, null),
                     new MapButtonTooltipSnapshot(9, new byte[] { 1, 2, 2 }) })
            PresentationPending.Append(pending, sample, MapButtonTooltipSnapshot.SameIdentity);
        t.True(pending.Count <= 4 && pending[0] == first && pending[pending.Count - 2].Payload == null
            && pending[pending.Count - 1].SampleTime == 9, "burst retains opening and latest clear/reopen boundary");

        // Exercise the actual batching entry point with both highly compressible native
        // output and a small clear. Native pictures remain atomic after decompression.
        payload = new byte[4000]; payload[0] = 1; payload[1] = 255;
        size = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(10, payload), bytes);
        scheduler.Enqueue(bytes, size, identity: new MapButtonTooltipSnapshot(10, payload));
        byte[]? batchFirst = scheduler.NextBatch(101);
        t.True(batchFirst != null && NetPacket.PeekType(batchFirst, batchFirst.Length) == NetProtocol.MsgPresentationCompression,
            "compressible native tooltip uses existing lossless envelope");
        t.Equal((int)NetProtocol.MsgMapButtonTooltip, ExtrasFragments.CompressedPayloadType(batchFirst!, batchFirst!.Length),
            "compressed dispatcher recognizes tooltip payload type");
        byte[]? expanded = assembler.Accept(2, batchFirst!, batchFirst!.Length, 101);
        t.True(expanded != null && MapButtonTooltipCodec.TryRead(expanded, expanded.Length, out _),
            "compressed tooltip reassembles into validated picture");
        size = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(11, null), bytes);
        scheduler.Enqueue(bytes, size, identity: new MapButtonTooltipSnapshot(11, null));
        byte[]? small = scheduler.NextBatch(102);
        t.True(small != null && PresentationBatch.ChildType(NetPacket.PeekType(small, small.Length)),
            "tooltip clear is an allowed batch child");
        byte[] batch = PresentationBatch.Write(new System.Collections.Generic.List<byte[]> { batchFirst!, small! });
        t.True(PresentationBatch.TryRead(batch, batch.Length, out var children) && children!.Length == 2,
            "mixed compressed/clear batch accepted without nested routing");
        byte[]? finalClear = assembler.Accept(2, small!, small!.Length, 102);
        t.True(finalClear != null && MapButtonTooltipCodec.TryRead(finalClear, finalClear.Length, out var closed)
            && closed!.Payload == null, "clear after compressed picture removes it");
    }
}
