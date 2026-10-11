using System;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownOriginalReceipt639Vectors
{
    internal static void Run(Harness t)
    {
        var entries = new[] { new TownServiceOriginalReceiptEntry(0x1234, 0x0102030405060708UL) };
        byte[] packet = TownServiceOriginalReceiptCodec.Write(0x01020304, 3, 0x0a0b0c0d, 0, entries);
        // Independent bytes: record112 is additive. Never infer its grammar by
        // writing and reading the same implementation in lockstep.
        t.Wire(Hex.Bytes("31 52 56 47 03 1C 70 16 01 04 03 02 01 03 0D 0C 0B 0A 00 01 34 12 08 07 06 05 04 03 02 01"),
            packet, packet.Length, "112 exact original receipt immutable golden");
        t.True(TownServiceOriginalReceiptCodec.TryRead(packet, packet.Length, out var decoded)
            && decoded!.OriginPeer == 0x01020304 && decoded.Service == 3 && decoded.Session == 0x0a0b0c0d
            && decoded.Lane == 0 && decoded.Entries.Length == 1
            && decoded.Entries[0].Module == 0x1234 && decoded.Entries[0].Sequence == 0x0102030405060708UL,
            "112 retains exact baseline metadata, not a rendered or purchased flag");
        for (int cut = 0; cut < packet.Length; cut++)
            t.True(!TownServiceOriginalReceiptCodec.TryRead(packet, cut, out _), "112 rejects truncated original receipt " + cut);
        foreach (int at in new[] { 4, 5, 6, 7, 8, 13, 18, 19 })
        {
            byte[] malformed = (byte[])packet.Clone(); malformed[at] = 255;
            t.True(!TownServiceOriginalReceiptCodec.TryRead(malformed, malformed.Length, out _), "112 rejects invalid header at " + at);
        }
        var extended = new byte[packet.Length + 4]; Array.Copy(packet, extended, packet.Length);
        extended[packet.Length] = 238; extended[packet.Length + 1] = 2;
        extended[packet.Length + 2] = 17; extended[packet.Length + 3] = 42;
        t.True(TownServiceOriginalReceiptCodec.TryRead(extended, extended.Length, out _), "112 skips additive unknown records");
        var duplicate = new byte[packet.Length * 2 - 6]; Array.Copy(packet, duplicate, packet.Length);
        Array.Copy(packet, 6, duplicate, packet.Length, packet.Length - 6);
        t.True(!TownServiceOriginalReceiptCodec.TryRead(duplicate, duplicate.Length, out _), "112 rejects duplicate known authority");
        var maximum = new TownServiceOriginalReceiptEntry[20];
        for (ushort i = 0; i < maximum.Length; i++) maximum[i] = new TownServiceOriginalReceiptEntry(i, 100UL + i);
        byte[] max = TownServiceOriginalReceiptCodec.Write(2, 3, 1, 0, maximum);
        t.True(max.Length == 220 && TownServiceOriginalReceiptCodec.TryRead(max, max.Length, out var full)
            && full!.Entries.Length == 20, "112 exact bounded twenty-module batch");
        t.True(PresentationBatch.HasTownOriginalPage(packet, packet.Length), "112 receipt retains ReliableOrdered original dependency transport");
        var mixed = PresentationBatch.Write(new System.Collections.Generic.List<byte[]> {
            Hex.Bytes("31 52 56 47 03 01"), packet });
        t.True(PresentationBatch.TryRead(mixed, mixed.Length, out var pages) && pages!.Length == 2
            && PresentationBatch.HasTownOriginalPage(mixed, mixed.Length), "112 receipt shares one existing native event cap");
        var scheduler = new ExtrasSendScheduler(0, 3, 4);
        scheduler.Enqueue(packet, packet.Length);
        byte[]? sent = scheduler.NextBatch(0);
        t.True(sent != null && TownServiceOriginalReceiptCodec.TryRead(sent, sent.Length, out _), "112 drains through actual scheduler");
        scheduler.Enqueue(packet, packet.Length);
        t.True(scheduler.NextBatch(.001) == null, "112 cannot bypass fifty-millisecond send clock");
        scheduler.Clear();
        t.True(scheduler.NextBatch(1) == null, "112 network reset discards old receipts");
    }
}
