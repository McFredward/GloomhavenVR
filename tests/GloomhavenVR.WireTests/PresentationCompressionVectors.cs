using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class PresentationCompressionVectors
{
    internal static void Run(Harness t)
    {
        t.Case("489: lossless compression is a separate bounded atomic presentation envelope");
        byte[] raw = Snapshot(512);
        // Independent RFC1951 raw-deflate fixture, followed by the original packet's CRC32.
        byte[] golden = Hex.Bytes("31 52 56 47 03 10 40 20 08 07 06 05 04 03 02 01 01 00 02 11 00 00 00 33 0C 0A 73 67 66 64 18 05 23 14 00 00 EF 18 42 E7");
        t.Equal(1, ExtrasFragments.CompressedPayloadType(golden, golden.Length), "new message16/TLV64 names original extras payload");
        byte[]? decoded = new ExtrasFragments().Accept(2, golden, golden.Length, 0);
        t.True(decoded != null, "independent compressed golden decodes");
        if (decoded != null) t.Wire(raw, decoded, decoded.Length, "independent compressed bytes restore original packet exactly");
        byte[][] pages = ExtrasFragments.Encode(raw, raw.Length, 10, compress: true);
        t.Equal(16, NetPacket.PeekType(pages[0], pages[0].Length), "compressible input uses additive envelope");
        byte[] oldPage = ExtrasFragments.Encode(raw, raw.Length, 10)[0];
        t.Equal(2, NetPacket.PeekType(oldPage, oldPage.Length), "legacy encoding remains opt-in compatible");
        t.True(PresentationCompression.TryCompress(Snapshot(511), 511) == null, "small snapshot stays unchanged");
        var random = new Random(489);
        byte[] noise = Snapshot(4096); random.NextBytes(noise); Header(noise, NetProtocol.MsgExtras);
        t.True(PresentationCompression.TryCompress(noise, noise.Length) == null, "incompressible saturation uses original envelope");

        for (int cut = 0; cut < golden.Length; cut++)
            t.True(new ExtrasFragments().Accept(2, golden, cut, 0) == null, "truncated compressed datagram inert " + cut);
        byte[] bad = (byte[])golden.Clone(); bad[bad.Length - 1] ^= 1;
        t.True(new ExtrasFragments().Accept(2, bad, bad.Length, 0) == null, "checksum corruption never publishes");
        bad = (byte[])golden.Clone(); bad[16] = NetProtocol.MsgNativeBoard;
        t.True(new ExtrasFragments().Accept(2, bad, bad.Length, 0) == null, "foreign payload cannot enter extras assembler");
        bad = (byte[])golden.Clone(); bad[17] = 0xff; bad[18] = 0xff;
        t.True(new ExtrasFragments().Accept(2, bad, bad.Length, 0) == null, "declared expansion over stream limit refused");
        byte[]? packed = PresentationCompression.TryCompress(Snapshot(4096), 4096);
        t.True(packed != null && PresentationCompression.Expand(packed, 512, 4096, NetProtocol.MsgExtras) == null,
            "expansion bomb stops at advertised length plus one byte");
        if (packed != null)
        {
            t.True(PresentationCompression.Expand(packed, 4096, 2048, NetProtocol.MsgExtras) == null, "caller hard cap enforced before allocation");
            for (int cut = 0; cut < packed.Length; cut++)
                t.True(PresentationCompression.Expand(packed.Take(cut).ToArray(), 4096, 4096, NetProtocol.MsgExtras) == null,
                    "incomplete deflate/checksum body inert " + cut);
        }
        byte[] mixed = Snapshot(4096);
        var seed = new byte[1800]; random.NextBytes(seed);
        for (int i = 6; i < mixed.Length; i++) mixed[i] = seed[(i - 6) % seed.Length];
        pages = ExtrasFragments.Encode(mixed, mixed.Length, 25, compress: true);
        t.True(pages.Length >= 2 && NetPacket.PeekType(pages[0], pages[0].Length) == 16, "fixture covers multiple compressed pages");
        var receiver = new ExtrasFragments();
        for (int i = pages.Length - 1; i >= 0; i--)
        {
            t.True(pages[i].Length <= ExtrasFragments.MaxDatagramBytes, "compressed page fits unchanged budget");
            decoded = receiver.Accept(2, pages[i], pages[i].Length, 0);
            if (i > 0) t.True(decoded == null, "reordered prefix cannot publish partial state");
            else if (decoded != null) t.Wire(mixed, decoded, decoded.Length, "reordered compressed state is exact");
            else t.True(false, "complete compressed generation must decode");
            t.True(receiver.Accept(2, pages[i], pages[i].Length, 0) == null, "completed generation does not replay");
        }
        var mixedReceiver = new ExtrasFragments();
        t.True(mixedReceiver.Accept(2, pages[0], pages[0].Length, 0) == null, "first compressed page waits");
        byte[][] legacy = ExtrasFragments.Encode(mixed, mixed.Length, 25);
        foreach (byte[] page in legacy) t.True(mixedReceiver.Accept(2, page, page.Length, 0) == null, "same generation cannot switch encoding");
        foreach (byte[] page in pages) t.True(mixedReceiver.Accept(2, page, page.Length, 0) == null, "invalid mixed generation remains retired");
        raw = Snapshot(2048); Header(raw, NetProtocol.MsgNativeUseBar);
        pages = ExtrasFragments.Encode(raw, raw.Length, 40, NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments, compress: true);
        t.Equal(8, ExtrasFragments.NativeSlotStream(pages[0], pages[0].Length), "compressed native slot keeps low five sequence bits");
        MeasureAppearance(t);
    }

    private static void MeasureAppearance(Harness t)
    {
        foreach (var fixture in new[] { (Count: 8, Noise: false), (Count: 32, Noise: false), (Count: 32, Noise: true) })
        {
            int count = fixture.Count;
            var entropy = new Random(1489);
            var states = new CardAppearanceState[count];
            for (int c = 0; c < count; c++)
            {
                var nodes = new CardAppearanceNode[CardAppearanceNode.RoleCount];
                for (byte r = 0; r < nodes.Length; r++)
                {
                    var node = new CardAppearanceNode { Role = r, Flags = (byte)(r == 11 ? 11 : 3),
                        Mask = CardAppearanceNode.AllowedMask(r), Binding = r >= 12 ? (uint)(r + 1) : 0 };
                    for (int f = 0; f < node.Values.Length; f++)
                        node.Values[f] = r >= 12 ? (f == 0 ? 1f : 0f) : fixture.Noise
                            ? (float)entropy.NextDouble() : (f < 8 ? 1f : (c + f) * 0.03125f);
                    nodes[r] = node;
                }
                states[c] = new CardAppearanceState { ActorId = c + 1, FaceCode = 0x20, ListCount = 1, Nodes = nodes };
            }
            var buffer = new byte[CardAppearanceCodec.MaxSize];
            int length = CardAppearanceCodec.Write(new CardAppearanceSnapshot(9, states), buffer);
            byte[][] pages = ExtrasFragments.Encode(buffer, length, 1, CardAppearanceCodec.Message,
                CardAppearanceCodec.FragmentMessage, CardAppearanceCodec.MaxSize, compress: true);
            var receiver = new ExtrasFragments(CardAppearanceCodec.Message, CardAppearanceCodec.FragmentMessage, CardAppearanceCodec.MaxSize);
            byte[]? decoded = null;
            foreach (byte[] page in pages) decoded = receiver.Accept(2, page, page.Length, 0) ?? decoded;
            t.True(decoded != null && CardAppearanceCodec.TryRead(decoded, decoded.Length, out _), "complete native appearance survives compression " + count);
            if (decoded != null) t.Wire(buffer.Take(length).ToArray(), decoded, decoded.Length, "every native material float preserved " + count);
            Console.WriteLine("COMPRESSION appearance cards=" + count + " entropy=" + fixture.Noise + " raw=" + length + " wire=" + pages.Sum(p => p.Length)
                + " pages=" + pages.Length + " ratio=" + ((double)pages.Sum(p => p.Length) / length).ToString("F3"));
        }
    }

    private static byte[] Snapshot(int size) { var result = new byte[size]; Header(result, NetProtocol.MsgExtras); return result; }
    private static void Header(byte[] bytes, byte type)
    { bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = type; }
}
