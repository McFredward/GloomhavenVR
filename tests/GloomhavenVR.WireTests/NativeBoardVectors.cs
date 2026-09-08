using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeBoardVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native-board/independent-raw-golden");
        // Explicit wire bytes, independent of both the writer and the float serializer.
        byte[] raw = new byte[61];
        raw[2] = 0x80; raw[3] = 0x3f; // time 1
        raw[6] = 0x70; raw[7] = 0x41; // depth 15
        byte[] golden = Frame(raw, false);
        t.True(NativeBoardCodec.TryRead(golden, golden.Length, out NativeBoardState? clear)
            && clear != null && clear.SampleTime == 1f && clear.InitiativeDepthPixels == 15f
            && clear.Generation == 0 && clear.Elements.Length == 0 && clear.Frame.All(x => x == 0),
            "handwritten little-endian empty board reads exactly");
        var output = new byte[NativeBoardCodec.MaxSize];
        int n = NativeBoardCodec.Write(clear!, output);
        t.True(output.Take(6).SequenceEqual(new byte[] { 0x31, 0x52, 0x56, 0x47, 3, 10 }), "message header is frozen GVR1 version3 type10");
        t.True(output[12] == 1 && n < golden.Length, "repeated geometry chooses lossless compression");
        t.True(Unpack(output, n).SequenceEqual(raw), "writer produces the independently authored raw body");

        t.Case("native-board/full-output-and-copy");
        NativeElementState[] elements = Elements(false);
        float[] frame = { 901.25f, 240f, 1000f, 400f, -31f, 15f, 0f, 1f, 2f, 1f, 80f, 60f };
        var state = new NativeBoardState(17.125f, 27.75f, 31, elements, frame);
        NativeBoardState timeCopy = state.CopyWithTime(20f);
        frame[0] = 0; elements[0].Graphics[0].A = 999;
        t.True(timeCopy.SamePicture(state) && timeCopy.SampleTime == 20f && timeCopy.Frame[0] == 901.25f
            && timeCopy.Elements[0].Graphics[0].A != 999, "publication and idle predecessor deep-copy every rendered field");
        n = NativeBoardCodec.Write(state, output);
        t.True(NativeBoardCodec.TryRead(output, n, out NativeBoardState? received)
            && received!.SamePicture(state) && received.SampleTime == state.SampleTime,
            "six elements retain exact floats, FX, sibling and all animation values");
        var maximum = new NativeBoardState(19f, 40f, 32, Elements(true), frame);
        n = NativeBoardCodec.Write(maximum, output);
        t.True(n <= NativeBoardCodec.MaxSize && Unpack(output, n).Length == 10387,
            "maximum declared cardinalities fit the explicit raw and framed bounds");
        t.True(NativeBoardCodec.TryRead(output, n, out received) && received!.SamePicture(maximum),
            "maximum frame survives paging and compression without quantization");

        t.Case("native-board/atomic-refusal");
        for (int length = 0; length < golden.Length; length++)
            t.True(!NativeBoardCodec.TryRead(golden, length, out received) && received == null,
                "every truncated golden prefix refuses without a partial publication");
        foreach (int at in new[] { 0, 4, 5, 6, 7, 8, 10, 12, 13 })
        {
            byte[] bad = (byte[])golden.Clone(); bad[at] ^= 0x80;
            t.True(!NativeBoardCodec.TryRead(bad, bad.Length, out received) && received == null,
                "malformed header, page, encoding or raw bound refuses atomically");
        }
        byte[] tail = golden.Concat(new byte[] { 52, 5, 1, 0, 0, 0, 0 }).ToArray();
        t.True(!NativeBoardCodec.TryRead(tail, tail.Length, out received), "malformed tail cannot publish an otherwise complete frame");
        byte[] nan = (byte[])raw.Clone(); nan[2] = 0xc0; nan[3] = 0x7f;
        byte[] malformed = Frame(nan, false);
        t.True(!NativeBoardCodec.TryRead(malformed, malformed.Length, out received), "nonfinite sample refuses");
        byte[] bomb = Frame(new byte[11001], true, 61);
        t.True(!NativeBoardCodec.TryRead(bomb, bomb.Length, out received), "inflation beyond claimed length refuses after one extra byte");
        byte[] invalidDeflate = Frame(raw, true); invalidDeflate[15] ^= 0xff;
        t.True(!NativeBoardCodec.TryRead(invalidDeflate, invalidDeflate.Length, out received), "damaged compressed payload refuses");
        byte[] full = Frame(Unpack(output, n), false);
        byte[] wrongOffset = (byte[])full.Clone(); wrongOffset[256] = 0;
        t.True(!NativeBoardCodec.TryRead(wrongOffset, wrongOffset.Length, out received), "second page duplicate offset cannot publish");
        byte[] reordered = (byte[])full.Clone(); Buffer.BlockCopy(full, 252, reordered, 6, 246); Buffer.BlockCopy(full, 6, reordered, 252, 246);
        t.True(!NativeBoardCodec.TryRead(reordered, reordered.Length, out received), "reordered canonical pages refuse");
        foreach (var corruption in new[] { (At: 61, Value: (byte)64), (At: 62, Value: (byte)6),
            (At: 95, Value: (byte)8), (At: 242, Value: (byte)33), (At: 917, Value: (byte)255) })
        {
            byte[] invalid = Unpack(output, n); invalid[corruption.At] = corruption.Value;
            byte[] encoded = Frame(invalid, false);
            t.True(!NativeBoardCodec.TryRead(encoded, encoded.Length, out received) && received == null,
                "invalid native flags, sibling, FX bound or original animation kind refuses atomically");
        }
        byte[] duplicateSibling = Unpack(output, n); duplicateSibling[61 + 1721 + 1] = duplicateSibling[62];
        byte[] badSiblingFrame = Frame(duplicateSibling, false);
        t.True(!NativeBoardCodec.TryRead(badSiblingFrame, badSiblingFrame.Length, out received), "duplicate element sibling refuses the complete frame");
    }

    private static NativeElementState[] Elements(bool maximum)
    {
        var result = new NativeElementState[6];
        for (int e = 0; e < 6; e++)
        {
            var state = result[e] = new NativeElementState { Flags = (byte)(1 | ((e % 4) << 4)), Sibling = (byte)(5 - e),
                Graphics = Enumerable.Range(0, 7).Select(i => Graphic(i + e)).ToArray(),
                Effects = Enumerable.Range(0, maximum ? 32 : 2).Select(i => Graphic(i + e)).ToArray(),
                Animations = new UseBarAnimationValue[3][] };
            for (int i = 0; i < 8; i++) state.Rect[i] = i * 0.3125f - e;
            for (int a = 0; a < 3; a++) state.Animations[a] = Enumerable.Range(0, maximum ? 16 : 3)
                .Select(i => new UseBarAnimationValue { SettingIndex = (byte)i, Kind = UseBarAnimationKind.GraphicColor4,
                    Values = new[] { i * 0.625f, -a * 1.5f, e * 3.125f, i + e * 0.03125f } }).ToArray();
        }
        return result;
    }
    private static NativeElementGraphic Graphic(int value) => new()
    { Flags = (byte)(value % 8), R = value * 0.0625f, G = -value * 2.125f, B = 3f, A = value * 0.125f, Fx = value * 0.3125f };
    private static byte[] Frame(byte[] raw, bool compress, int? rawLength = null)
    {
        byte[] data = raw;
        if (compress)
        {
            using var memory = new MemoryStream();
            using (var stream = new DeflateStream(memory, CompressionLevel.Fastest, true)) stream.Write(raw);
            data = memory.ToArray();
        }
        using var payload = new MemoryStream(); using var p = new BinaryWriter(payload);
        p.Write((byte)(compress ? 1 : 0)); p.Write((ushort)(rawLength ?? raw.Length)); p.Write(data);
        byte[] packed = payload.ToArray();
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        w.Write(new byte[] { 0x31, 0x52, 0x56, 0x47, 3, 10 });
        for (int at = 0; at < packed.Length; at += 240)
        {
            int count = Math.Min(240, packed.Length - at);
            w.Write((byte)52); w.Write((byte)(count + 4)); w.Write((ushort)packed.Length); w.Write((ushort)at); w.Write(packed, at, count);
        }
        return output.ToArray();
    }
    private static byte[] Unpack(byte[] frame, int length)
    {
        using var payload = new MemoryStream();
        for (int at = 6; at < length; ) { int count = frame[at + 1] - 4; payload.Write(frame, at + 6, count); at += count + 6; }
        byte[] packed = payload.ToArray();
        if (packed[0] == 0) return packed.Skip(3).ToArray();
        using var input = new MemoryStream(packed, 3, packed.Length - 3); using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        using var raw = new MemoryStream(); inflate.CopyTo(raw); return raw.ToArray();
    }
}
