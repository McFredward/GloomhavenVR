// The ten golden vectors specified in .planning/refactor/REVIEW-Net-Rig.md §W5.
//
// Every expected byte string below is derived by hand from
// `.planning/refactor/INVARIANTS-Net-Rig.md` Part I §3a-3d and §4a-4e — NOT by running the
// writer and pasting what it produced. A vector generated from the code it tests asserts only
// that the code is self-consistent, which is exactly the property a lockstep Write/TryRead
// refactor preserves while corrupting every peer.
//
// All values were chosen to be EXACTLY representable so a reader can verify the hex by hand:
//   positions use ±{0, 0.125, 0.25, 0.5, 1, 2, 3, 4, 8, 16} — all exact in float32;
//   rotations use axis-aligned unit quaternions, so ‖q‖ == 1f exactly, the normalisation is a
//   no-op, the clamp is a no-op, and round(±1 · 32767) is exactly ±32767 = 0x7FFF / 0x8001.
// Nothing here depends on a rounding tie-break.

using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class GoldenVectors
{
    // ---- shared pose literals ------------------------------------------------------------
    // pos = 3 × float32 LE (12 B), rot = 4 × int16 LE (8 B), order x, y, z, w. §3d.

    private const string PoseHead = @"
        0000803F 00000040 000040C0   // pos (1, 2, -3)
        0000 0000 0000 FF7F          // rot (0,0,0,1) identity -> w = +32767";

    private const string PoseLeft = @"
        0000003F 000080BE 00008040   // pos (0.5, -0.25, 4)
        FF7F 0000 0000 0000          // rot (1,0,0,0) -> x = +32767";

    private const string PoseRight = @"
        00000000 00008041 000000BE   // pos (0, 16, -0.125)
        0000 0180 0000 0000          // rot (0,-1,0,0) -> y = -32767 = 0x8001";

    private const string PoseFigure = @"
        00000041 00000000 0000803E   // pos (8, 0, 0.25)
        0000 0000 FF7F 0000          // rot (0,0,1,0) -> z = +32767";

    private const string PoseCard = @"
        000080BF 000000C0 0000003F   // pos (-1, -2, 0.5)
        0000 0000 0000 FF7F          // rot identity";

    private const string PoseBoard = @"
        00000040 00000000 000000BF   // pos (2, 0, -0.5)
        0000 0000 0000 FF7F          // rot identity";

    private static RigPose Pose(float x, float y, float z, float qx, float qy, float qz, float qw)
        => new RigPose { Position = new Vector3(x, y, z), Rotation = new Quaternion(qx, qy, qz, qw) };

    private static RigPose Head() => Pose(1f, 2f, -3f, 0f, 0f, 0f, 1f);
    private static RigPose Left() => Pose(0.5f, -0.25f, 4f, 1f, 0f, 0f, 0f);
    private static RigPose Right() => Pose(0f, 16f, -0.125f, 0f, -1f, 0f, 0f);
    private static RigPose Figure() => Pose(8f, 0f, 0.25f, 0f, 0f, 1f, 0f);
    private static RigPose Card() => Pose(-1f, -2f, 0.5f, 0f, 0f, 0f, 1f);
    private static RigPose Board() => Pose(2f, 0f, -0.5f, 0f, 0f, 0f, 1f);

    // ---- state builders ------------------------------------------------------------------

    /// <summary>Vector 2's state: head only. Everything else default.</summary>
    private static AvatarState RigHeadOnly() => new AvatarState
    {
        HeadValid = true, Head = Head(), MaskId = 0, WorldScale = 1f,
    };

    /// <summary>Vector 3's state: head + both hands + finger curls, right-dominant.</summary>
    private static AvatarState RigHandsAndFingers()
    {
        var s = new AvatarState
        {
            HeadValid = true, Head = Head(), HasFingers = true, MaskId = 2,
            WorldScale = 2f, DominantRight = true, HandStyle = 1,
        };
        s.Left.Tracked = true; s.Left.Pose = Left();
        s.Left.Curl0 = 0f; s.Left.Curl1 = 0.4f; s.Left.Curl2 = 0.6f;
        s.Left.Curl3 = 0.8f; s.Left.Curl4 = 1f;
        s.Right.Tracked = true; s.Right.Pose = Right();
        s.Right.Curl0 = 1f; s.Right.Curl1 = 0.8f; s.Right.Curl2 = 0.6f;
        s.Right.Curl3 = 0.4f; s.Right.Curl4 = 0f;
        return s;
    }

    /// <summary>Vector 4's state: head + held figure + held card.</summary>
    private static AvatarState RigHeldFigureAndCard() => new AvatarState
    {
        HeadValid = true, Head = Head(), MaskId = 1, WorldScale = 1f, HandStyle = 2,
        HasHeldFigure = true, HeldFigureActorId = 0x01020304, HeldFigurePose = Figure(),
        HasHeldCard = true, HeldCardPose = Card(),
    };

    /// <summary>Vector 6's state: board + every additive block, all sub-fields set.</summary>
    private static PresenceState ExtrasEverything() => new PresenceState
    {
        HasBoard = true, Board = Board(), BoardScale = 0.5f,
        HandCardCount = 7, DominantRight = true,
        GhostHand = true, GhostStrength = 0x80,
        HasItemFan = true, ItemCardCount = 3, ItemFanHeld = true, ItemFanLeftHand = true,
        HasCardFx = true, FxSeq = 0x2A, FxEndpoints = 0x51,
        HasPileBrowse = true, PileBrowseKind = NetProtocol.PileBrowseKindBurnt,
        PileBrowseCardCount = 9, PileBrowseHeld = true, PileBrowseLeftHand = false,
        HasMaskSize = true, MaskSizeCode = 125,
        BoardStyleCode = 2,
    };

    /// <summary>Vector 7's state: a non-default head-mask size and nothing else at all.</summary>
    private static PresenceState ExtrasMaskSizeOnly() => new PresenceState
    {
        HasMaskSize = true, MaskSizeCode = 200,
    };

    // ======================================================================================

    public static void Run(Harness t)
    {
        var rig = new byte[AvatarSerializer.MaxSize];
        var ext = new byte[PresenceSerializer.MaxSize];

        // -- 1. Header -------------------------------------------------------------------
        // §2: magic is written LITTLE-ENDIAN, so the constant reads "GVR1" and the BYTE
        // STREAM reads "1RVG". Do not "fix" one to match the other.
        t.Case("1. header");
        int n = AvatarSerializer.Write(RigHeadOnly(), rig);
        t.Wire(Hex.Bytes("31 52 56 47 03 00"), rig, 6, "rig header is magic 31 52 56 47, v3, type 0");
        int m = PresenceSerializer.Write(ExtrasMaskSizeOnly(), ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01"), ext, 6, "extras header is magic 31 52 56 47, v3, type 1");
        t.Equal(NetProtocol.MsgRig, (byte)NetPacket.PeekType(rig, n), "PeekType routes a rig packet");
        t.Equal(NetProtocol.MsgExtras, (byte)NetPacket.PeekType(ext, m), "PeekType routes an extras packet");
        t.Equal(-1, NetPacket.PeekType(Hex.Bytes("31 52 56 47 02 00"), 6), "PeekType rejects version 2");

        // -- 2. Rig, all flags clear except head -----------------------------------------
        // §3a: the fixed part is 12 bytes and the first pose starts at offset 12.
        t.Case("2. rig, head only");
        n = AvatarSerializer.Write(RigHeadOnly(), rig);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03               // version 3
            00               // type MsgRig
            41               // flags: bit0 FlagHeadValid + bit6 FlagHandStyle (always set)
            00               // maskId
            0000803F         // worldScale = 1.0
            " + PoseHead + @"
            00               // handStyle (trailing byte, always written)
            "), rig, n, "12-byte fixed part, then the head pose at offset 12");
        t.Equal(33, n, "head-only rig packet is 12 + 20 + 1 bytes");

        // -- 3. Rig, both hands + fingers -------------------------------------------------
        // §3c: curls sit IMMEDIATELY AFTER THEIR OWN HAND'S POSE, not batched at the end.
        // Batching them would keep Write/TryRead consistent and pass any round-trip test.
        t.Case("3. rig, both hands + fingers");
        n = AvatarSerializer.Write(RigHandsAndFingers(), rig);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 00            // version, type
            6F               // flags: head|left|right|fingers|dominantRight|handStyle
            02               // maskId = 2
            00000040         // worldScale = 2.0
            " + PoseHead + @"
            " + PoseLeft + @"
            00 66 99 CC FF   // LEFT curls, immediately after the LEFT pose
            " + PoseRight + @"
            FF CC 99 66 00   // RIGHT curls, immediately after the RIGHT pose
            01               // handStyle = 1 (Plate)
            "), rig, n, "each hand's curls follow that hand's own pose");
        t.Equal(83, n, "head + 2 hands with fingers + style = 12+20+25+25+1");

        // -- 4. Rig, held figure + held card ---------------------------------------------
        // §3c: the held-card pose comes AFTER the hand-style byte, so a pre-held-card reader
        // still finds the style byte at the offset it expects.
        t.Case("4. rig, held figure + held card");
        n = AvatarSerializer.Write(RigHeldFigureAndCard(), rig);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 00            // version, type
            D1               // flags: head|heldFigure|handStyle|heldCard
            01               // maskId = 1
            0000803F         // worldScale = 1.0
            " + PoseHead + @"
            04 03 02 01      // heldFigure actorId int32 LE = 0x01020304
            " + PoseFigure + @"
            02               // handStyle = 2 (Arcane) -- BEFORE the held-card pose
            " + PoseCard + @"
            "), rig, n, "held-card pose is written after the style byte, not before it");
        t.Equal(77, n, "head + held figure + style + held card = 12+20+24+1+20");

        // -- 5. FlagHandStyle ------------------------------------------------------------
        // §3b: bit 6 is set UNCONDITIONALLY on write; reading still honours the flag, so a
        // pre-style sender parses.
        t.Case("5. FlagHandStyle");
        foreach (var s in new[] { RigHeadOnly(), RigHandsAndFingers(), RigHeldFigureAndCard() })
        {
            AvatarSerializer.Write(s, rig);
            t.True((rig[6] & NetProtocol.FlagHandStyle) != 0,
                   "bit 6 is set on write regardless of state");
        }
        // A packet from a sender that predates the field: bit 6 clear, no trailing byte.
        byte[] preStyle = Hex.Bytes(@"
            31 52 56 47 03 00
            01               // flags: FlagHeadValid only -- no FlagHandStyle
            00 0000803F
            " + PoseHead);
        t.True(AvatarSerializer.TryRead(preStyle, preStyle.Length, out AvatarState old),
               "a pre-hand-style packet still parses");
        t.Equal(32, preStyle.Length, "and it is one byte shorter");
        t.Equal((byte)0, old.HandStyle, "with HandStyle defaulting to 0 (Glove)");
        t.True(old.HeadValid, "and the head pose still read");

        // -- 6. Extras, board + all four additive blocks ----------------------------------
        // §4b: handCardCount sits BEFORE every additive block, and the four blocks appear in
        // ASCENDING FLAG-BIT ORDER (ghost 0x04, item fan 0x08, card FX 0x20, block 0x80).
        t.Case("6. extras, board + all four additive blocks");
        m = PresenceSerializer.Write(ExtrasEverything(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type MsgExtras
            FF               // flags: all eight bits
            " + PoseBoard + @"
            0000003F         // boardScale = 0.5
            07               // handCardCount -- BEFORE every additive block
            80               // [bit2] ghostStrength
            03               // [bit3] itemCardCount
            2A 51            // [bit5] fxSeq, fxEndpoints
            55               // [bit7] byte A: kind 1 (Burnt) | held 0x04 | maskSize 0x10 | style 2 << 5
            09               // byte B: browse card count
            7D               // byte C: maskSizeCode = 125 (INSIDE the block)
            "), ext, m, "additive blocks in ascending flag-bit order, after handCardCount");
        t.Equal(39, m, "worst-case extras packet is 39 bytes (MaxSize 44 has headroom)");

        // -- 7. Extras, mask-size only ---------------------------------------------------
        // §4d: a size-only packet writes the block with the pile-browse sub-fields ZEROED and
        // byte B = 0. Every reader that ever shipped bit 7 gates the fan on count > 0, so the
        // block carries the size without inventing a fan on anybody's screen.
        t.Case("7. extras, mask size only");
        m = PresenceSerializer.Write(ExtrasMaskSizeOnly(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse only -- 'a BLOCK follows', not 'fan open'
            00               // handCardCount
            10               // byte A: PileBrowseMaskSizeBit only; kind/held/left/style all 0
            00               // byte B: count 0  <- this is what says 'no fan' to every reader
            C8               // byte C: maskSizeCode = 200 (2.00x)
            "), ext, m, "size-only block zeroes the pile-browse sub-fields and byte B");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sz), "and it parses");
        t.Equal((byte)0, sz.PileBrowseCardCount, "count 0 -> every reader renders no fan");
        t.True(sz.HasMaskSize, "while the mask size is delivered");
        t.Equal((byte)200, sz.MaskSizeCode, "with the right code");

        // -- 8. Non-default-only transmission --------------------------------------------
        // §4d: default board style + default mask size must emit bytes IDENTICAL to a packet
        // built without either feature. This is the whole backward-compatibility argument:
        // a default-configured player is byte-for-byte a pre-feature sender.
        t.Case("8. defaults are byte-identical to a pre-feature packet");
        var withDefaults = new PresenceState
        {
            HandCardCount = 4,
            BoardStyleCode = NetProtocol.BoardStyleDefaultCode,   // Oak
            HasMaskSize = false,                                  // default 1.00x
        };
        var withoutFeatures = new PresenceState { HandCardCount = 4 };
        var a = new byte[PresenceSerializer.MaxSize];
        var b = new byte[PresenceSerializer.MaxSize];
        int na = PresenceSerializer.Write(withDefaults, a);
        int nb = PresenceSerializer.Write(withoutFeatures, b);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            00               // flags: NO block bit -- defaults cost zero bits
            04               // handCardCount
            "), a, na, "a default-configured sender emits a pre-feature packet");
        t.Equal(na, nb, "both packets are the same length");
        t.True(SameBytes(a, b, na), "and byte-for-byte identical");
        t.Equal(8, na, "8 bytes: header 7 + handCardCount");

        // -- 9. Forward compatibility ----------------------------------------------------
        // §3e / §4e: readers validate ONLY their own flags' length. A future sender appending
        // fields must still parse here, with identical state.
        t.Case("9. forward compatibility (trailing bytes from a future sender)");
        foreach (var s in new[] { RigHeadOnly(), RigHandsAndFingers(), RigHeldFigureAndCard() })
        {
            int len = AvatarSerializer.Write(s, rig);
            t.True(AvatarSerializer.TryRead(rig, len, out AvatarState plain), "rig parses");
            byte[] padded = Pad(rig, len, 8);
            t.True(AvatarSerializer.TryRead(padded, padded.Length, out AvatarState fut),
                   "rig still parses with 8 junk bytes appended");
            t.True(SameRig(plain, fut), "and yields identical state");
        }
        foreach (var s in new[] { ExtrasEverything(), ExtrasMaskSizeOnly(), withDefaults })
        {
            int len = PresenceSerializer.Write(s, ext);
            t.True(PresenceSerializer.TryRead(ext, len, out PresenceState plain), "extras parses");
            byte[] padded = Pad(ext, len, 8);
            t.True(PresenceSerializer.TryRead(padded, padded.Length, out PresenceState fut),
                   "extras still parses with 8 junk bytes appended");
            t.True(SameExtras(plain, fut), "and yields identical state");
        }
        // The converse: a TRUNCATED packet must be rejected, not read past the end.
        t.True(!AvatarSerializer.TryRead(rig, 11, out _), "a rig packet shorter than 12 B is rejected");
        for (int len = AvatarSerializer.Write(RigHandsAndFingers(), rig) - 1; len >= 12; len -= 7)
            t.True(!AvatarSerializer.TryRead(rig, len, out _), $"truncation to {len} B is rejected");

        // -- 10. Byte A bit 7 stays reserved ----------------------------------------------
        // §4c: bit 7 of byte A is "reserved, must be written 0" — the last free extension slot
        // in the entire protocol. Sweep every combination the writer can reach.
        t.Case("10. byte A bit 7 is 0 in every packet the writer can produce");
        int swept = 0;
        for (int kind = 0; kind <= 3; kind++)
        for (int held = 0; held <= 1; held++)
        for (int left = 0; left <= 1; left++)
        for (int size = 0; size <= 1; size++)
        for (int style = 0; style <= 3; style++)
        {
            var s = new PresenceState
            {
                HasPileBrowse = true,
                PileBrowseKind = (byte)kind,
                PileBrowseHeld = held == 1,
                PileBrowseLeftHand = left == 1,
                PileBrowseCardCount = 5,
                HasMaskSize = size == 1,
                MaskSizeCode = 150,
                BoardStyleCode = (byte)style,
                HandCardCount = 1,
            };
            int len = PresenceSerializer.Write(s, ext);
            int aOff = 8;    // header 7 + handCardCount 1, no board/ghost/item/fx in this sweep
            t.True(len > aOff && (ext[aOff] & 0x80) == 0,
                   $"byte A bit 7 clear (kind={kind} held={held} left={left} size={size} style={style})");
            swept++;
        }
        t.Equal(128, swept, "swept every reachable byte-A combination");
    }

    // ---- helpers --------------------------------------------------------------------------

    private static byte[] Pad(byte[] src, int len, int extra)
    {
        var padded = new byte[len + extra];
        Array.Copy(src, padded, len);
        for (int i = 0; i < extra; i++)
            padded[len + i] = (byte)(0xA5 ^ i);   // recognisable junk a future sender might send
        return padded;
    }

    private static bool SameBytes(byte[] x, byte[] y, int len)
    {
        for (int i = 0; i < len; i++)
            if (x[i] != y[i]) return false;
        return true;
    }

    private static bool SameRig(in AvatarState x, in AvatarState y)
        => x.HeadValid == y.HeadValid && x.Head.Position == y.Head.Position
        && x.Left.Tracked == y.Left.Tracked && x.Right.Tracked == y.Right.Tracked
        && x.Left.Pose.Position == y.Left.Pose.Position
        && x.Right.Pose.Position == y.Right.Pose.Position
        && x.Left.Curl2 == y.Left.Curl2 && x.Right.Curl2 == y.Right.Curl2
        && x.HasFingers == y.HasFingers && x.MaskId == y.MaskId
        && x.HandStyle == y.HandStyle && x.WorldScale == y.WorldScale
        && x.DominantRight == y.DominantRight
        && x.HasHeldFigure == y.HasHeldFigure && x.HeldFigureActorId == y.HeldFigureActorId
        && x.HeldFigurePose.Position == y.HeldFigurePose.Position
        && x.HasHeldCard == y.HasHeldCard && x.HeldCardPose.Position == y.HeldCardPose.Position;

    private static bool SameExtras(in PresenceState x, in PresenceState y)
        => x.HasBoard == y.HasBoard && x.Board.Position == y.Board.Position
        && x.BoardScale == y.BoardScale && x.HandCardCount == y.HandCardCount
        && x.DominantRight == y.DominantRight
        && x.GhostHand == y.GhostHand && x.GhostStrength == y.GhostStrength
        && x.HasItemFan == y.HasItemFan && x.ItemCardCount == y.ItemCardCount
        && x.ItemFanHeld == y.ItemFanHeld && x.ItemFanLeftHand == y.ItemFanLeftHand
        && x.HasCardFx == y.HasCardFx && x.FxSeq == y.FxSeq && x.FxEndpoints == y.FxEndpoints
        && x.HasPileBrowse == y.HasPileBrowse && x.PileBrowseKind == y.PileBrowseKind
        && x.PileBrowseCardCount == y.PileBrowseCardCount
        && x.PileBrowseHeld == y.PileBrowseHeld && x.PileBrowseLeftHand == y.PileBrowseLeftHand
        && x.HasMaskSize == y.HasMaskSize && x.MaskSizeCode == y.MaskSizeCode
        && x.BoardStyleCode == y.BoardStyleCode;
}
