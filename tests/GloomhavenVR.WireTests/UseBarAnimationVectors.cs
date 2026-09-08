using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class UseBarAnimationVectors
{
    internal static void Run(Harness t)
    {
        t.Case("49: actual native animation values have an independent golden message");
        var state = new UseBarAnimationState
        {
            Slot = 2, ActorId = -2, SlotIdentity = 0x1234,
            Entries = new[]
            {
                new UseBarAnimationValue { SettingIndex = 4, Kind = UseBarAnimationKind.Scale3, Values = new[] { 1f, .5f, -2f } },
                new UseBarAnimationValue { SettingIndex = 9, Kind = UseBarAnimationKind.CanvasGroupAlpha1, Values = new[] { .25f } },
            },
        };
        var sample = new UseBarAnimationSnapshot(1.5f, new[] { state });
        var buffer = new byte[UseBarAnimationCodec.MaxSize];
        int length = UseBarAnimationCodec.Write(sample, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 03 31 23 02 FE FF FF FF 34 12 00 00 C0 3F 01 02 00 02 04 00 00 00 80 3F 00 00 00 3F 00 00 00 C0 09 05 00 00 80 3E"),
            buffer, length, "record 49 preserves fractional scale and alpha with owner identities and sample time");
        t.True(UseBarAnimationCodec.TryRead(buffer, length, out UseBarAnimationSnapshot? decoded)
            && decoded!.SampleTime == 1.5f && UseBarAnimationState.Equivalent(sample.States, decoded.States), "golden animation frame parses");
        state.Entries[0].Values[0] = 9;
        t.Equal(1f, sample.States[0].Entries[0].Values[0], "published frame owns its value arrays");
        state.Entries[0].Values[0] = 1;
        byte[] golden = Copy(buffer, length);

        int[] components = { 3, 3, 3, 2, 2, 1, 1, 1, 4, 4, 1, 1, 2, 1 };
        for (int kind = 0; kind < components.Length; kind++)
        {
            t.Equal(components[kind], UseBarAnimationValue.Components((UseBarAnimationKind)kind), "native property kind has its pinned float arity");
            state.Entries = new[] { new UseBarAnimationValue { SettingIndex = 255, Kind = (UseBarAnimationKind)kind, Values = new float[components[kind]] } };
            for (int c = 0; c < components[kind]; c++) state.Entries[0].Values[c] = -0.125f * (c + 1);
            sample = new UseBarAnimationSnapshot(2, new[] { state });
            length = UseBarAnimationCodec.Write(sample, buffer);
            t.True(UseBarAnimationCodec.TryRead(buffer, length, out decoded)
                && UseBarAnimationState.Equivalent(sample.States, decoded!.States), "each native kind round trips original setting ordinal 255");
        }
        t.Equal(0, UseBarAnimationValue.Components((UseBarAnimationKind)14), "future native kind is not guessed");
        t.Equal(0, UseBarAnimationValue.Components((UseBarAnimationKind)255), "unknown native kind is not guessed");

        var all = new UseBarAnimationState[8];
        for (int slot = 0; slot < all.Length; slot++)
        {
            all[slot] = new UseBarAnimationState { Slot = (byte)slot, ActorId = int.MinValue, SlotIdentity = ushort.MaxValue,
                Entries = new UseBarAnimationValue[16] };
            for (int entry = 0; entry < 16; entry++)
                all[slot].Entries[entry] = new UseBarAnimationValue { SettingIndex = (byte)entry, Kind = UseBarAnimationKind.GraphicColor4,
                    Values = new[] { float.MaxValue, -float.MaxValue, .125f, .875f } };
        }
        sample = new UseBarAnimationSnapshot(3, all);
        length = UseBarAnimationCodec.Write(sample, buffer);
        t.Equal(2582, length, "eight slots with sixteen four-component settings fit the dedicated frame bound");
        t.Equal(length, UseBarAnimationCodec.MaxEncodedBytes, "documented whole-frame maximum equals actual bytes");
        t.True(length < UseBarAnimationCodec.MaxSize, "native animation frame remains below 3072-byte transport bound");
        for (int n = 0; n < length; n++)
            t.True(!UseBarAnimationCodec.TryRead(buffer, n, out _), "every torn prefix, including an entire missing final slot, is refused");
        t.True(UseBarAnimationCodec.TryRead(buffer, length, out decoded)
            && UseBarAnimationState.Equivalent(sample.States, decoded!.States), "the complete maximum frame publishes atomically");
        for (int at = 6; at < length; at += 2 + buffer[at + 1])
            t.True(buffer[at + 1] <= 255, "every animation record fits one TLV");

        byte[] maximum = Copy(buffer, length);
        var reversed = Copy(maximum, length);
        for (int record = 0; record < 16; record++)
            Buffer.BlockCopy(maximum, 6 + (15 - record) * 161, reversed, 6 + record * 161, 161);
        t.True(UseBarAnimationCodec.TryRead(reversed, reversed.Length, out decoded)
            && UseBarAnimationState.Equivalent(sample.States, decoded!.States), "reordered chunks and slots still assemble the same complete frame");
        var duplicate = Append(maximum, maximum, 6, 161);
        t.True(UseBarAnimationCodec.TryRead(duplicate, duplicate.Length, out _), "identical repeated chunks are inert");
        duplicate[duplicate.Length - 1] ^= 1;
        t.True(!UseBarAnimationCodec.TryRead(duplicate, duplicate.Length, out _), "a conflicting duplicate rejects the complete frame");
        var duplicateOrdinal = Copy(maximum, maximum.Length);
        duplicateOrdinal[6 + 161 + 2 + 15] = 0;
        t.True(!UseBarAnimationCodec.TryRead(duplicateOrdinal, duplicateOrdinal.Length, out _), "duplicate setting ordinals across chunks cannot alias a native target");
        var mismatchedActor = Copy(maximum, maximum.Length);
        mismatchedActor[6 + 161 + 3] ^= 1;
        t.True(!UseBarAnimationCodec.TryRead(mismatchedActor, mismatchedActor.Length, out _), "chunks from different slot owners cannot splice");
        var mismatchedTime = Copy(maximum, maximum.Length);
        mismatchedTime[6 + 161 + 12] ^= 1;
        t.True(!UseBarAnimationCodec.TryRead(mismatchedTime, mismatchedTime.Length, out _), "chunks from different source frames cannot splice");
        var mismatchedCount = Copy(maximum, maximum.Length);
        mismatchedCount[6 + 161 + 13] = 7;
        t.True(!UseBarAnimationCodec.TryRead(mismatchedCount, mismatchedCount.Length, out _), "inconsistent snapshot slot counts reject the frame");

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var value = Copy(golden, golden.Length);
            int at = 25;
            AvatarSerializer.WriteF32(value, ref at, bad);
            t.True(!UseBarAnimationCodec.TryRead(value, value.Length, out _), "non-finite native properties cannot enter rendering");
            value = Copy(golden, golden.Length); at = 15;
            AvatarSerializer.WriteF32(value, ref at, bad);
            t.True(!UseBarAnimationCodec.TryRead(value, value.Length, out _), "non-finite source clock cannot enter interpolation");
        }
        RefuseByte(t, golden, 4, 2, "wrong version is refused");
        RefuseByte(t, golden, 5, NetProtocol.MsgExtras, "presence cannot be parsed as an animation frame");
        RefuseByte(t, golden, 8, 8, "ninth slot is refused");
        RefuseByte(t, golden, 19, 9, "ninth snapshot slot count is refused");
        RefuseByte(t, golden, 20, 17, "seventeenth setting is refused");
        RefuseByte(t, golden, 21, 1, "noncanonical chunk start is refused");
        RefuseByte(t, golden, 22, 1, "incorrect chunk entry count is refused");
        RefuseByte(t, golden, 24, 255, "unknown native kind invalidates the frame");
        var zeroActor = Copy(golden, golden.Length); Array.Clear(zeroActor, 9, 4);
        t.True(!UseBarAnimationCodec.TryRead(zeroActor, zeroActor.Length, out _), "unknown actor cannot move a bonus");
        var zeroIdentity = Copy(golden, golden.Length); Array.Clear(zeroIdentity, 13, 2);
        t.True(!UseBarAnimationCodec.TryRead(zeroIdentity, zeroIdentity.Length, out _), "unknown bonus identity cannot move a replacement slot");
        var negativeTime = Copy(golden, golden.Length); int timeAt = 15;
        AvatarSerializer.WriteF32(negativeTime, ref timeAt, -1);
        t.True(!UseBarAnimationCodec.TryRead(negativeTime, negativeTime.Length, out _), "negative owner clock is refused");
        byte[] withUnknown = Append(golden, new byte[] { 200, 2, 0xAB, 0xCD }, 0, 4);
        t.True(UseBarAnimationCodec.TryRead(withUnknown, withUnknown.Length, out _), "future TLVs are skipped by their declared length");
        t.True(!UseBarAnimationCodec.TryRead(withUnknown, withUnknown.Length - 1, out _), "a truncated future TLV cannot be accepted as complete");

        sample = new UseBarAnimationSnapshot(1.5f, Array.Empty<UseBarAnimationState>());
        length = UseBarAnimationCodec.Write(sample, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 03 31 0F FF 00 00 00 00 00 00 00 00 C0 3F 00 00 00 00"),
            buffer, length, "empty owner frame explicitly withdraws all animation targets with its clock");
        t.True(UseBarAnimationCodec.TryRead(buffer, length, out decoded) && decoded!.States.Length == 0,
            "empty animation frame clears previous interpolation state");
        byte[] mixed = Append(golden, buffer, 6, length - 6);
        t.True(!UseBarAnimationCodec.TryRead(mixed, mixed.Length, out _), "empty and populated frames cannot be mixed");
        t.True(!UseBarAnimationCodec.TryRead(buffer, 6, out _), "a header alone is not a withdrawal");
        t.True(!UseBarAnimationCodec.TryRead(buffer, buffer.Length + 1, out _), "buffer extent is checked before any read");
        t.True(!UseBarAnimationCodec.TryRead(buffer, -1, out _), "negative lengths are refused");
    }

    private static void RefuseByte(Harness t, byte[] source, int at, byte value, string reason)
    {
        byte[] copy = Copy(source, source.Length); copy[at] = value;
        t.True(!UseBarAnimationCodec.TryRead(copy, copy.Length, out _), reason);
    }
    private static byte[] Copy(byte[] source, int length)
    {
        var result = new byte[length]; Buffer.BlockCopy(source, 0, result, 0, length); return result;
    }
    private static byte[] Append(byte[] source, byte[] tail, int at, int count)
    {
        var result = new byte[source.Length + count];
        Buffer.BlockCopy(source, 0, result, 0, source.Length);
        Buffer.BlockCopy(tail, at, result, source.Length, count); return result;
    }
}
