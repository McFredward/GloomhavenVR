using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeDecisionHighlightVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native mandatory highlight: exact bounded original Image payload");
        var state = new NativeDecisionHighlightState { SampleTime = 1.5f, Flags = 63,
            ImageType = 1, FillMethod = 4, FillOrigin = 3, FillAmount = .75f,
            PixelsPerUnitMultiplier = 2f, ReferencePixelsPerUnit = 100f,
            SpriteName = new string('s', 64), TextureName = new string('t', 64) };
        for (int i = 0; i < state.Rect.Length; i++) state.Rect[i] = i + .25f;
        state.Rect[14] = state.Rect[15] = state.Rect[16] = 0f; state.Rect[17] = 1f;
        for (int i = 0; i < state.Colors.Length; i++) state.Colors[i] = i / 8f;
        byte[] buffer = new byte[NativeDecisionHighlightCodec.MaxSize + 2];
        buffer[0] = buffer[buffer.Length - 1] = 0xCC;
        int length = NativeDecisionHighlightCodec.Write(state, buffer, 1);
        t.Equal(254, length, "all original fields and maximum UTF8 names fit one TLV");
        t.Equal((byte)0xCC, buffer[0], "offset preserves preceding data");
        t.Equal((byte)0xCC, buffer[buffer.Length - 1], "exact payload preserves following data");
        t.Wire(Hex.Bytes("00 00 C0 3F 00 00 80 3E"), Slice(buffer, 1, 8), 8,
            "source timestamp and first original coordinate use frozen little endian floats");
        t.True(NativeDecisionHighlightCodec.TryRead(buffer, 1, length, out var decoded)
            && decoded!.SampleTime == 1.5f && NativeDecisionHighlightState.SamePicture(state, decoded),
            "every original geometry, renderer, Image and asset selector field round trips");
        for (int n = 0; n < length; n++)
            t.True(!NativeDecisionHighlightCodec.TryRead(buffer, 1, n, out _), "every torn payload is refused");
        t.True(!NativeDecisionHighlightCodec.TryRead(buffer, -1, length, out _), "negative offset rejected");
        t.True(!NativeDecisionHighlightCodec.TryRead(buffer, 1, int.MaxValue, out _), "overflow length rejected");
        var copy = state.Snapshot(); state.Rect[0] = 999;
        t.Equal(.25f, copy.Rect[0], "publication owns the geometry array");
        state = copy.Snapshot(); state.SampleTime = 99;
        t.True(NativeDecisionHighlightState.SamePicture(copy, state), "redundant packet timestamps cannot invent visual changes");
        state.SpriteName += "s";
        t.Equal(0, NativeDecisionHighlightCodec.Write(state, buffer), "oversized public sprite selector rejected without truncation");
        state = copy.Snapshot(); state.SpriteName = "é" + new string('s', 63);
        t.True(!state.Validate(), "name limit counts encoded bytes");
        state = copy.Snapshot(); state.SpriteName = "\uD800";
        t.True(!state.Validate(), "invalid UTF16 source is refused");
        state = copy.Snapshot(); state.Rect[0] = float.NaN;
        t.True(!state.Validate(), "nonfinite geometry is refused");
        state = copy.Snapshot(); state.Rect[17] = 0;
        t.True(!state.Validate(), "zero quaternion rejected before Unity interpolation");
        state = copy.Snapshot(); state.Rect[17] = 2;
        t.True(!state.Validate(), "oversized nonunit quaternion rejected");
        state = copy.Snapshot(); state.PixelsPerUnitMultiplier = 0;
        t.True(!state.Validate(), "degenerate border scale is refused");
        state = copy.Snapshot(); state.TextureName = string.Empty;
        t.True(!state.Validate(), "partial asset identity cannot choose an ambiguous sprite");
        byte[] malformed = Slice(buffer, 1, length);
        malformed[108] = 0x80;
        t.True(!NativeDecisionHighlightCodec.TryRead(malformed, 0, length, out _), "unknown render flag rejected");
        malformed = Slice(buffer, 1, length); Array.Clear(malformed, 72, 4);
        t.True(!NativeDecisionHighlightCodec.TryRead(malformed, 0, length, out _), "received zero quaternion rejected atomically");
        malformed = Slice(buffer, 1, length); malformed[74] = 0; malformed[75] = 0x40;
        t.True(!NativeDecisionHighlightCodec.TryRead(malformed, 0, length, out _), "received oversized quaternion rejected atomically");
        malformed = Slice(buffer, 1, length); malformed[125] = 0xFF;
        t.True(!NativeDecisionHighlightCodec.TryRead(malformed, 0, length, out _), "malformed UTF8 rejected atomically");
        malformed = Slice(buffer, 1, length); malformed[124] = 65;
        t.True(!NativeDecisionHighlightCodec.TryRead(malformed, 0, length, out _), "overlong received selector rejected");
    }

    private static byte[] Slice(byte[] source, int offset, int count)
    { var result = new byte[count]; Array.Copy(source, offset, result, 0, count); return result; }
}
