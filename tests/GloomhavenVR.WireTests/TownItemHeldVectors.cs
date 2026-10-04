using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class TownItemHeldVectors
{
    internal static void Run(Harness t)
    {
        t.Case("622: atomic public map item source has full seats and stable item provenance");
        var source = new TownItemHeldSource(TownItemHeldSource.Owned, 0x04030201, 0x1023, 44, 87);
        var state = new AvatarState { WorldScale = 1, HasHeldCard = true,
            HeldCardPose = new RigPose { Rotation = Quaternion.identity }, HasHeldCardFace = true,
            HeldFaceCode = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListItems, 44), HeldFaceCount = 87,
            HeldTownItem = source };
        var bytes = new byte[AvatarSerializer.MaxSize];
        int size = AvatarSerializer.Write(state, bytes);
        var tail = new byte[15]; Array.Copy(bytes, size - 15, tail, 0, 15);
        t.Wire(Hex.Bytes("65 0D 01 01 02 03 04 23 10 00 00 2C 00 57 00"), tail, 15, "101 owned golden, no five-bit wrap");
        t.True(AvatarSerializer.TryRead(bytes, size, out var decoded) && decoded.HeldTownItem.HasValue
            && decoded.HeldTownItem.Value.Same(source), "rig sampler source remains atomic with pose beyond seat30");
        t.True(AvatarSerializer.TryRead(bytes, size - 15, out decoded) && !decoded.HeldTownItem.HasValue,
            "legacy rig without101 keeps original36 unknown-seat semantics");
        for (int cut = size - 14; cut < size; cut++)
            t.True(!AvatarSerializer.TryRead(bytes, cut, out _), "truncated101 cannot preserve an old item source " + cut);
        Array.Copy(tail, 0, bytes, size, 15);
        t.True(!AvatarSerializer.TryRead(bytes, size + 15, out _), "duplicate101 rejected");
        var malformed = (byte[])tail.Clone(); malformed[11] = 87;
        t.True(!TownItemHeldSource.TryRead(malformed, 2, 13, out _), "seat equal to count rejected");
        malformed = (byte[])tail.Clone(); malformed[2] = 3;
        t.True(!TownItemHeldSource.TryRead(malformed, 2, 13, out _), "reserved source kind rejected");
        t.True(!new TownItemHeldSource(TownItemHeldSource.Owned, 0, 42, 0, 1).Validate(), "no assigned map peer cannot name another viewer inventory");
        t.True(!new TownItemHeldSource(TownItemHeldSource.Owned, 1, 0, 0, 1).Validate(), "absent native item identity refused");
        state.HeldTownItem = new TownItemHeldSource(TownItemHeldSource.Stock, 0, 42, 0, 0);
        state.HeldFaceCount = 0;
        size = AvatarSerializer.Write(state, bytes); Array.Copy(bytes, size - 15, tail, 0, 15);
        t.Wire(Hex.Bytes("65 0D 02 00 00 00 00 2A 00 00 00 00 00 00 00"), tail, 15, "public stock golden needs no character assignment");
        t.True(AvatarSerializer.TryRead(bytes, size, out decoded) && decoded.HeldTownItem.GetValueOrDefault().Kind == TownItemHeldSource.Stock,
            "stock source survives atomic rig decode");
        state.HasHeldCard = false; size = AvatarSerializer.Write(state, bytes);
        t.True(AvatarSerializer.TryRead(bytes, size, out decoded) && !decoded.HeldTownItem.HasValue, "release clears held item provenance");

        var extras = new PresenceState { HasSecondHeldCard = true,
            SecondHeldCardPose = new RigPose { Rotation = Quaternion.identity }, HasHeldCardFace = true,
            HeldFaceCode = 0x80, HeldFaceCount = 87, SecondHeldFaceCode = 0x9f, SecondHeldFaceCount = 87,
            HeldTownItem = source };
        bytes = new byte[PresenceSerializer.MaxSize]; size = PresenceSerializer.Write(extras, bytes);
        Array.Copy(bytes, size - 15, tail, 0, 15);
        t.Wire(Hex.Bytes("65 0D 01 01 02 03 04 23 10 00 00 2C 00 57 00"), tail, 15, "secondary extras uses the identical101 golden");
        t.True(PresenceSerializer.TryRead(bytes, size, out var received) && received.HeldTownItem.HasValue
            && received.HeldTownItem.Value.Same(source), "second held pose retains its own native item source");
        extras.HasSecondHeldCard = false; size = PresenceSerializer.Write(extras, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out received) && !received.HeldTownItem.HasValue, "secondary release cannot latch101");

        t.Case("622: owner fan deadzone uses finite float249, leaving count width and248 tombstone intact");
        t.Equal(4, NetProtocol.BoardTuneFieldWidth(249), "249 has its explicit full float width");
        t.Equal(0, NetProtocol.BoardTuneFieldWidth(248), "historical248 remains reserved");
        t.Equal(1, NetProtocol.BoardTuneFieldWidth(247), "legacy count247 retains byte width");
        var tune = new byte[8]; int offset = 1; tune[0] = 1;
        t.True(NetProtocol.WriteTuneFloatField(tune, ref offset, 249, .125f, .004f), "moved owner deadzone emits249");
        t.Wire(Hex.Bytes("01 F9 00 00 00 3E"), tune, offset, "float249 golden preserves metres without a Count narrowing");
        t.Equal(.125f, NetProtocol.BoardTuneFloat(tune, 0, offset, 249, .004f), "owner deadzone exact source float");
        foreach (float value in new[] { 0f, .0041f, 40f, -1f })
        {
            offset = 1; NetProtocol.WriteTuneFloatField(tune, ref offset, 249, value, .004f);
            t.Equal(value, NetProtocol.BoardTuneFloat(tune, 0, offset, 249, .004f), "unbounded legal config survives249 " + value);
        }
        offset = 1;
        t.True(!NetProtocol.WriteTuneFloatField(tune, ref offset, 249, float.NaN, .004f)
            && !NetProtocol.WriteTuneFloatField(tune, ref offset, 249, float.PositiveInfinity, .004f), "nonfinite249 cannot emit a broken follow target");
        t.Equal(.004f, NetProtocol.BoardTuneFloat(tune, 0, 5, 249, .004f), "truncated float uses authored fallback");

        t.Case("622: maximum legal snapshot budget retains source101 and float249 atomically");
        offset = 1; NetProtocol.WriteTuneFloatField(tune, ref offset, 249, .125f, .004f);
        var tunePage = new byte[255];
        var floatFields = new byte[5]; Array.Copy(tune, 1, floatFields, 0, 5);
        int tuneLength = BoardTunePages.WritePage(floatFields, 5, 0, BoardTunePages.Signature(floatFields, 0, 5), tunePage);
        extras.HasSecondHeldCard = true; extras.HasBoardTuning = true;
        extras.BoardTuningBytes = tunePage; extras.BoardTuningLength = tuneLength;
        size = PresenceSerializer.Write(extras, bytes);
        // Unknown additive records fill the complete legal7700 byte envelope; known source/tune
        // records are genuine writer output. This distinguishes transport capacity from arithmetic.
        const int maximum = 7700;
        var maximumBytes = new byte[maximum]; Array.Copy(bytes, maximumBytes, 11);
        int write = 11, remaining = maximum - size, paddingRecords = 0;
        while (remaining > 0)
        {
            int payload = Math.Min(255, remaining - 2);
            if (remaining - (payload + 2) == 1) payload--;
            maximumBytes[write++] = 251; maximumBytes[write++] = (byte)payload;
            write += payload; remaining -= payload + 2; paddingRecords++;
        }
        Array.Copy(bytes, 11, maximumBytes, write, size - 11);
        maximumBytes[10] = (byte)(bytes[10] + paddingRecords);
        t.True(PresenceSerializer.MaxSize - maximum >= 257 && ExtrasFragments.MaxSnapshotBytes >= maximum,
            "complete145-cloth +101 +249 maximum retains record-sized writer headroom");
        var assembly = new ExtrasFragments(); byte[]? complete = null;
        byte[][] fragments = ExtrasFragments.Encode(maximumBytes, maximum, 622);
        for (int page = fragments.Length - 1; page >= 0; page--)
        {
            complete = assembly.Accept(2, fragments[page], fragments[page].Length, 0);
            if (page > 0) t.True(complete == null, "no partial maximum snapshot clears the held source");
        }
        t.True(complete != null && PresenceSerializer.TryRead(complete, complete.Length, out received)
            && received.HeldTownItem.HasValue && received.HeldTownItem.Value.Same(source)
            && received.HasBoardTuning, "7700 bytes reorder/reassemble/decode without dropping101 or249");

        t.Case("622: record26 remains byte-identical for scenario recess and public map withdrawal");
        foreach (byte clip in new byte[] { 0, 31, 87, 254 })
        {
            var map = new PresenceState { HasItemUseClip = true, ItemUseClipIndex = clip };
            size = PresenceSerializer.Write(map, bytes);
            t.True(PresenceSerializer.TryRead(bytes, size, out received) && received.HasItemUseClip && received.ItemUseClipIndex == clip,
                "26 raw-seat range retains scenario grammar " + clip);
            t.True(bytes[size - 3] == NetProtocol.ExtIdItemUseClip && bytes[size - 2] == 1 && bytes[size - 1] == clip,
                "map context introduces no26 payload change " + clip);
        }
    }
}
