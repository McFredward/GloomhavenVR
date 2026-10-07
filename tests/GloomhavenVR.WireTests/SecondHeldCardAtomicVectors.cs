using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>Independent byte vectors for111, the compact atomic secondary held-card record.</summary>
internal static class SecondHeldCardAtomicVectors
{
    internal static void Run(Harness t)
    {
        var state = new AvatarState {
            HasHeldCard = true, HeldCardPose = new RigPose { Rotation = Quaternion.identity },
            HasSecondHeldCardState = true, HasSecondHeldCard = true,
            SecondHeldCardPose = new RigPose { Position = new Vector3(1, 2, 3), Rotation = Quaternion.identity },
            HasSecondHeldCardFace = true, SecondHeldFaceCode = 0x9f, SecondHeldFaceCount = 87,
            SecondHeldTownItem = new TownItemHeldSource(1, 0x04030201, 4131, 44, 87),
            HeldCardGripMask = 3
        };
        var buffer = new byte[AvatarSerializer.MaxSize];
        var expected = Hex.Bytes("6F 29 1B 03 00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F 00 00 00 00 9F 57 01 01 02 03 04 23 10 00 00 2C 00 57 00");
        int length = AvatarSerializer.Write(state, buffer);
        var actual = new byte[expected.Length]; Array.Copy(buffer, length - actual.Length, actual, 0, actual.Length);
        t.Case("638: secondary item pose/source/grip atomic compact rig golden");
        t.Wire(expected, actual, actual.Length, "111 complete owned item grammar immutable golden");
        t.True(AvatarSerializer.TryRead(buffer, length, out var parsed) && parsed.HasSecondHeldCardState
            && parsed.HasSecondHeldCard && parsed.HeldCardGripMask == 3 && parsed.SecondHeldCardPose.Position == new Vector3(1,2,3)
            && parsed.SecondHeldTownItem.HasValue && parsed.SecondHeldTownItem.Value.Same(state.SecondHeldTownItem.Value),
            "111 own source and second pose decoded together");
        int start = length - expected.Length;
        for (int cut = start + 1; cut < length; cut++)
            t.True(!AvatarSerializer.TryRead(buffer, cut, out _), "partial111 is never accepted " + cut);
        var malformed = (byte[])buffer.Clone(); malformed[start + 2] = 0x1f;
        t.True(!AvatarSerializer.TryRead(malformed, length, out _), "111 cannot claim both map and item address");
        malformed = (byte[])buffer.Clone(); malformed[start + 2] &= 0x0f;
        t.True(!AvatarSerializer.TryRead(malformed, length, out _), "111 two-card assignment must identify primary left");
        malformed = (byte[])buffer.Clone(); malformed[start + 3] = 4;
        t.True(!AvatarSerializer.TryRead(malformed, length, out _), "111 rejects unknown grip bits");
        malformed = (byte[])buffer.Clone(); malformed[start + 28] = 0x01;
        t.True(!AvatarSerializer.TryRead(malformed, length, out _), "111 item cannot name a private ability face");
        var duplicate = new byte[length + expected.Length]; Array.Copy(buffer, duplicate, length);
        Array.Copy(expected, 0, duplicate, length, expected.Length);
        t.True(!AvatarSerializer.TryRead(duplicate, duplicate.Length, out _), "111 duplicate authority rejected");
        malformed = (byte[])buffer.Clone(); malformed[start] = 240;
        t.True(AvatarSerializer.TryRead(malformed, length, out parsed) && parsed.HasHeldCard && !parsed.HasSecondHeldCardState,
            "opaque future records do not alter original rig prefix");

        state.HasHeldCardFace = true;
        length = AvatarSerializer.Write(state, buffer);
        int pairStart = length - expected.Length;
        const int prefix = 33;
        var reordered = new byte[length]; Array.Copy(buffer, reordered, prefix);
        Array.Copy(buffer, pairStart, reordered, prefix, expected.Length);
        Array.Copy(buffer, prefix, reordered, prefix + expected.Length, pairStart - prefix);
        reordered[prefix + expected.Length + 4] = 0x81;
        reordered[prefix + expected.Length + 5] = 2;
        t.True(AvatarSerializer.TryRead(reordered, length, out parsed) && parsed.SecondHeldFaceCode == 0x9f
            && parsed.SecondHeldFaceCount == 87 && parsed.SecondHeldTownItem.HasValue,
            "reordered legacy face TLV cannot overwrite atomic second source");
        state.HasHeldCardFace = false;

        state.SecondHeldTownItem = null; state.HasSecondHeldMapCard = true;
        state.SecondHeldMapKey = 0x88776655; state.SecondHeldMapPoolSeat = 4; state.SecondHeldMapPoolCount = 19;
        state.SecondHeldMapArcSeat = 2; state.SecondHeldFaceCode = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListMapLoadout, 4);
        length = AvatarSerializer.Write(state, buffer);
        t.True(AvatarSerializer.TryRead(buffer, length, out parsed) && parsed.HasSecondHeldMapCard
            && parsed.SecondHeldMapKey == state.SecondHeldMapKey && parsed.SecondHeldMapPoolSeat == 4
            && parsed.SecondHeldMapPoolCount == 19 && parsed.SecondHeldMapArcSeat == 2,
            "111 preserves full public map source for second hand");

        state.HasSecondHeldCard = false; state.HeldCardGripMask = 3;
        length = AvatarSerializer.Write(state, buffer); actual = new byte[4]; Array.Copy(buffer, length - 4, actual, 0, 4);
        t.Wire(Hex.Bytes("6F 02 00 01"), actual, 4, "111 explicit release still carries primary grip only");
        t.True(AvatarSerializer.TryRead(buffer, length, out parsed) && parsed.HasSecondHeldCardState
            && !parsed.HasSecondHeldCard && parsed.HeldCardGripMask == 1 && !parsed.SecondHeldTownItem.HasValue,
            "release is authoritative absence rather than an omitted stale record");
        state.PrimaryHeldCardLeft = true; length = AvatarSerializer.Write(state, buffer);
        Array.Copy(buffer, length - 4, actual, 0, 4);
        t.Wire(Hex.Bytes("6F 02 10 01"), actual, 4, "111 single-left hand assignment golden");
        t.True(AvatarSerializer.TryRead(buffer, length, out parsed) && parsed.PrimaryHeldCardLeft
            && !parsed.HasSecondHeldCard, "single-left assignment survives its own atomic rig edge");
        state.HasHeldCard = false; length = AvatarSerializer.Write(state, buffer);
        Array.Copy(buffer, length - 4, actual, 0, 4);
        t.Wire(Hex.Bytes("6F 02 00 00"), actual, 4, "111 both cards released immutable golden");
        state.HasSecondHeldCardState = false; length = AvatarSerializer.Write(state, buffer);
        t.True(AvatarSerializer.TryRead(buffer, length, out parsed) && !parsed.HasSecondHeldCardState,
            "legacy packets still select fallback secondary extras");

        state.HasHeldCard = state.HasSecondHeldCardState = state.HasSecondHeldCard = true;
        state.HasSecondHeldMapCard = false; state.HasSecondHeldCardFace = true;
        state.SecondHeldFaceCode = 0x9f; state.SecondHeldTownItem = new TownItemHeldSource(2, 0, 9, 0, 0);
        state.HasHeldCardFace = true; state.HeldFaceCode = 0x9f;
        state.HeldTownItem = new TownItemHeldSource(2, 0, 8, 0, 0);
        state.HeadValid = state.Left.Tracked = state.Right.Tracked = state.HasFingers = state.HasHeldFigure = true;
        state.HasBoardPose = state.HasBoard = state.WristBoard = true; state.BoardScale = 2;
        state.WorldScale = 2;
        length = AvatarSerializer.Write(state, buffer);
        t.True(length <= AvatarSerializer.MaxSize && length < 256 && AvatarSerializer.TryRead(buffer, length, out parsed)
            && parsed.HasSecondHeldCard && parsed.HeldTownItem.HasValue && parsed.SecondHeldTownItem.HasValue,
            "maximal legal two-card rig with board hands fingers figure stays compact unfragmented");
    }
}
