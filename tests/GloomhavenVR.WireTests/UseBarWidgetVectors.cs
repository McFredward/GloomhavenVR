using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class UseBarWidgetVectors
{
    public static void Run(Harness t)
    {
        t.Equal((byte)255, new UseBarWidgetState().SlotAlpha, "native slot defaults to full opacity");
        t.Case("47: original bonus subwidgets preserve ordinals, numbers and native states");
        var sample = new UseBarWidgetState
        {
            Slot = 2, Flags = 3, SlotAlpha = 127, ConsumeIcons = new byte[] { 1, 7 },
            InlineSlots = new byte[] { 4, 9 }, InlineOptions = new byte[] { 3, 255 },
            InlineNumbers = new short[] { 0, -123 },
            ElementStates = new byte[] { 0x81, 0xA0, 0, 0x84, 0x82, 0x98 },
            OptionStates = new byte[] { 0x81, 0x86 },
        };
        var buffer = new byte[PresenceSerializer.MaxSize];
        int count = PresenceSerializer.Write(new PresenceState { UseBarWidgetStates = new[] { sample } }, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 2F 16 02 03 7F 02 01 07 02 04 03 09 FF 85 FF 81 A0 00 84 82 98 02 81 86"),
               buffer, count, "golden record 47 encodes original subwidget slot 9 with signed numeric value");
        t.True(PresenceSerializer.TryRead(buffer, count, out PresenceState read), "complete snapshot parses");
        t.True(UseBarWidgetState.Equivalent(new[] { sample }, read.UseBarWidgetStates), "subwidgets round trip exactly");
        DecodedAlphaVectors(t, sample, buffer);
        count = PresenceSerializer.Write(default, buffer);
        t.True(PresenceSerializer.TryRead(buffer, count, out read) && read.UseBarWidgetStates == null,
               "omission clears the previous full snapshot");

        var maximum = new UseBarWidgetState
        {
            ConsumeIcons = new byte[32], InlineSlots = new byte[32], InlineOptions = new byte[32],
            InlineNumbers = new short[32], OptionStates = new byte[32],
        };
        for (int i = 0; i < 32; i++)
        {
            maximum.ConsumeIcons[i] = 7;
            maximum.InlineSlots[i] = (byte)i;
            maximum.InlineOptions[i] = 255;
            maximum.InlineNumbers[i] = (short)(short.MinValue + i);
            maximum.OptionStates[i] = 0xBF;
        }
        t.Equal(204, UseBarWidgetCodec.PayloadBytes(maximum), "full slot fits one 255-byte TLV");
        int at = 0;
        UseBarWidgetCodec.Write(maximum, buffer, ref at);
        t.Equal(204, at, "actual written maximum matches budget");
        for (int truncated = 0; truncated < at; truncated++)
            t.True(!UseBarWidgetCodec.TryRead(buffer, 0, truncated, out _),
                   "truncation is refused within this record and cannot borrow another TLV");
        t.True(UseBarWidgetCodec.TryRead(buffer, 0, at, out UseBarWidgetState decoded)
               && UseBarWidgetState.Equivalent(new[] { maximum }, new[] { decoded }),
               "maximum descriptor round trips without truncation");
        buffer[3] = 33;
        t.True(!UseBarWidgetCodec.TryRead(buffer, 0, at, out _), "over-limit count refused");

        var all = new UseBarWidgetState[8];
        for (int i = 0; i < all.Length; i++)
        {
            maximum.Slot = (byte)i;
            at = 0;
            UseBarWidgetCodec.Write(maximum, buffer, ref at);
            UseBarWidgetCodec.TryRead(buffer, 0, at, out all[i]);
        }
        count = PresenceSerializer.Write(new PresenceState { UseBarWidgetStates = all }, buffer);
        t.Equal(11 + 8 * 206, count, "eight whole descriptors retain every native subwidget");
        t.True(PresenceSerializer.TryRead(buffer, count, out read)
               && UseBarWidgetState.Equivalent(all, read.UseBarWidgetStates), "whole bar round trips");
        t.True(PresenceSerializer.MaxSize >= 4044 + 257, "full extras budget including video72 leaves a complete record margin");
        UseBarWidgetState[]? duplicate = new[] { sample };
        var replacement = new UseBarWidgetState { Slot = sample.Slot };
        UseBarWidgetCodec.Store(ref duplicate, replacement);
        t.True(duplicate != null && duplicate.Length == 1 && ReferenceEquals(duplicate[0], replacement),
               "duplicate slot last wins without duplicating a native widget");
    }

    private static void DecodedAlphaVectors(Harness t, UseBarWidgetState sample, byte[] buffer)
    {
        for (int alpha = 0; alpha <= byte.MaxValue; alpha++)
        {
            sample.SlotAlpha = (byte)alpha;
            int at = 0;
            UseBarWidgetCodec.Write(sample, buffer, ref at);
            t.True(UseBarWidgetCodec.TryRead(buffer, 0, at, out UseBarWidgetState decoded)
                   && decoded.SlotAlpha == alpha, "native slot animation alpha survives every wire value");
        }
    }
}
