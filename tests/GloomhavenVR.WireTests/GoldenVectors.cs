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

    /// <summary>A non-default HAND SCALE and nothing else — the first extension-tail record.</summary>
    private static PresenceState ExtrasHandScaleOnly() => new PresenceState
    {
        HasHandScale = true, HandScaleCode = 62,
    };

    /// <summary>A SECOND held figure (record 8) and nothing else: the mini in the sender's LEFT
    /// hand while the rig packet's figure rides the RIGHT one. Reuses the rig packet's figure pose
    /// literal so the two held-figure encodings are visibly the same 20 bytes.</summary>
    private static PresenceState ExtrasSecondFigure() => new PresenceState
    {
        HasSecondFigure = true, SecondFigureActorId = 0x01020304, SecondFigurePose = Figure(),
        SecondFigureLeftHand = true, PrimaryFigureLeftHand = false,
    };

    /// <summary>A SECOND held card (record 10) and nothing else: the card in the sender's OTHER
    /// hand while BOTH hands hold one. Reuses the rig packet's held-card pose literal so the two
    /// held-card encodings are visibly the same 20 bytes — which IS the whole record: pose only,
    /// no hand byte (the receiver renders the slab at the absolute pose, never parented to a
    /// hand) and no identity, ever.</summary>
    private static PresenceState ExtrasSecondHeldCard() => new PresenceState
    {
        HasSecondHeldCard = true, SecondHeldCardPose = Card(),
    };

    /// <summary>SLOT-CARD SIZE (record 11) and nothing else: the sender's board renders its slot
    /// frame overlays at 90.0 mm and a parked card at 119.7 mm (the shipped-default effective
    /// card width, 0.0635 × 1.3 × 1.45). Clean tenth-mm codes so the golden hex is
    /// hand-verifiable: 900 = 0x0384, 1197 = 0x04AD.</summary>
    private static PresenceState ExtrasSlotCardSize() => new PresenceState
    {
        HasSlotCardSize = true, SlotFrameWidthCode = 900, SlotCardWidthCode = 1197,
    };

    /// <summary>Board-UI + fan-anchor records (the 1:1 parity round's two additions), with a board
    /// so the round-trip loop also exercises them next to the pose. Position components are exact
    /// float32 values so the golden hex is hand-verifiable.</summary>
    private static PresenceState ExtrasBoardUiAndFanAnchor() => new PresenceState
    {
        HasBoard = true, Board = Board(), BoardScale = 1f, HandCardCount = 2,
        HasBoardUi = true, BoardButtonsMask = 0xB5, BoardOverlayMask = 0x02,
        HasFanAnchor = true, FanAnchorLocal = new Vector3(0.5f, 0.25f, -0.125f),
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
        t.Equal(39, m, "worst-case extras packet is 39 bytes without a tail (MaxSize 112 has headroom)");

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

        // -- 7b. Extension tail ----------------------------------------------------------
        // Byte A bit 7 no longer means "reserved" but "a TLV tail follows". The tail is what
        // makes the protocol extensible again: [count] then [id][len][payload] per record.
        t.Case("7b. extras, extension tail (hand scale)");
        m = PresenceSerializer.Write(ExtrasHandScaleOnly(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse -- 'a BLOCK follows'
            00               // handCardCount
            80               // byte A: PileBrowseExtensionBit only (bit 7)
            00               // byte B: count 0 -> no fan
            01               // tail: 1 record
            01 01 3E         // record: id 1 (hand scale), len 1, value 62 (0.62x)
            "), ext, m, "the tail is [count][id][len][payload]");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hs), "and it parses");
        t.True(hs.HasHandScale, "the hand scale is delivered");
        t.Equal((byte)62, hs.HandScaleCode, "with the right code");
        t.Equal(0.62f, NetProtocol.DecodeHandScale(hs.HandScaleCode), "decoding to 0.62x");

        // AN UNKNOWN RECORD MUST BE SKIPPED BY ITS OWN LENGTH — the whole point of TLV. This is
        // a hand-built packet from a hypothetical NEWER sender: an unknown id 99 with a 4-byte
        // payload, followed by the hand scale this build does know.
        byte[] future = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80               // byte A: extension tail
            00               // byte B
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4, payload this build has never heard of
            01 01 3E         // id 1, len 1, hand scale 0.62x
            ");
        t.True(PresenceSerializer.TryRead(future, future.Length, out PresenceState fwd),
               "a packet from a newer sender still parses");
        t.True(fwd.HasHandScale, "and the record we DO know is read past the one we do not");
        t.Equal((byte)62, fwd.HandScaleCode, "with its value intact");

        // -- 7c. Ghost sides ----------------------------------------------------------------
        // The ghost flag never carried a SIDE — receivers inferred "the non-dominant hand",
        // which the fan made true. A held card can ghost either hand or both, so the exact
        // sides ride the tail as a bitmask, next to the legacy flag + strength they refine.
        t.Case("7c. extras, ghost sides mask (held card in the dominant hand)");
        m = PresenceSerializer.Write(new PresenceState
        {
            GhostHand = true, GhostStrength = 140,
            HasGhostSides = true, GhostSidesMask = NetProtocol.GhostSideRightBit,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            84               // flags: FlagPileBrowse | FlagExtrasGhostHand
            00               // handCardCount
            8C               // ghost strength 140 (legacy block, unchanged position)
            80               // byte A: PileBrowseExtensionBit only
            00               // byte B: count 0 -> no fan
            01               // tail: 1 record
            02 01 02         // record: id 2 (ghost sides), len 1, mask = right
            "), ext, m, "the sides record rides the tail; every legacy byte stays put");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState gs), "and it parses");
        t.True(gs.GhostHand, "the legacy flag still says 'a ghost is active'");
        t.Equal((byte)140, gs.GhostStrength, "with the strength where it always was");
        t.True(gs.HasGhostSides, "the sides record is delivered");
        t.Equal(NetProtocol.GhostSideRightBit, gs.GhostSidesMask,
                "and it can say what the old flag could not: the DOMINANT hand");

        // A TRUNCATED tail must not destroy what was parsed before it.
        byte[] cut = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 01");
        t.True(PresenceSerializer.TryRead(cut, cut.Length, out PresenceState trunc),
               "a truncated tail still parses the packet");
        t.True(trunc.HasMaskSize, "the mask size ahead of the tail survives");
        t.Equal((byte)200, trunc.MaskSizeCode, "with its value");
        t.True(!trunc.HasHandScale, "and the incomplete record is simply not delivered");

        // -- 7d. Mod-version record (the version handshake) --------------------------------
        // Record id 3: [u16 ModBuild LE][UTF8 display bytes]. Unlike every record before it,
        // it is written on EVERY extras packet — its ABSENCE is the signal (a modded peer
        // without it predates the handshake and reads as ModBuild 0 = mismatch), so there is
        // no default whose omission could stand in for it.
        t.Case("7d. extras, mod-version record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasModVersion = true, ModBuild = 1, ModVersionText = "0.1.0",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse -- 'a BLOCK follows'
            00               // handCardCount
            80               // byte A: PileBrowseExtensionBit only
            00               // byte B: count 0 -> no fan
            01               // tail: 1 record
            03 07            // record: id 3 (mod version), len 7 = 2 (build) + 5 (text)
            01 00            // ModBuild = 1, uint16 LE
            30 2E 31 2E 30   // '0.1.0' UTF8
            "), ext, m, "the version record is [id 3][len][u16 build LE][UTF8 display]");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState mv), "and it parses");
        t.True(mv.HasModVersion, "the version is delivered");
        t.Equal((ushort)1, mv.ModBuild, "with the right build number");
        t.Equal("0.1.0", mv.ModVersionText ?? "", "and the right display string");
        t.True(!mv.HasPileBrowse || mv.PileBrowseCardCount == 0,
               "and a version-only packet invents no browse fan");

        // OLD-READER-SKIPS-UNKNOWN-TLV, from the version record's perspective: to a reader
        // built BEFORE id 3 this record is exactly the unknown-id case — the same skip-by-length
        // path the id-99 vector proves — so a pre-handshake peer parses this packet unchanged.
        // Here, the converse direction: a NEWER sender's unknown record ahead of the version
        // record must not eat it.
        byte[] mixed = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80               // byte A: extension tail
            00               // byte B
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- a field this build has never heard of
            03 03 05 00 58   // id 3, len 3: build 5 + 'X'
            ");
        t.True(PresenceSerializer.TryRead(mixed, mixed.Length, out PresenceState mixedState),
               "a packet with an unknown record ahead of the version record parses");
        t.True(mixedState.HasModVersion, "the version record behind it is still read");
        t.Equal((ushort)5, mixedState.ModBuild, "with its build intact");
        t.Equal("X", mixedState.ModVersionText ?? "", "and its display string intact");

        // A modded-but-pre-handshake sender: extras WITHOUT the record. HasModVersion must
        // read false — the receiver treats that as ModBuild 0, the defined mismatch value.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 4 }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noVer), "no-record packet parses");
        t.True(!noVer.HasModVersion, "record absent -> HasModVersion false (reads as ModBuild 0)");

        // Defensive display cap: a runaway string is truncated to ModVersionTextMaxBytes on
        // write; the record length says so and the reader gets exactly the capped text.
        string runaway = "0.1.0-with-a-runaway-suffix-far-beyond-the-cap";
        m = PresenceSerializer.Write(new PresenceState
        {
            HasModVersion = true, ModBuild = 700, ModVersionText = runaway,
        }, ext);
        t.Equal(11 + 2 + 2 + NetProtocol.ModVersionTextMaxBytes, m,
                "capped record: 11-byte shell + id + len + 2 build + 20 text bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState capped), "and it parses");
        t.Equal((ushort)700, capped.ModBuild, "the (>255) build survives the u16 round-trip");
        t.Equal(runaway.Substring(0, NetProtocol.ModVersionTextMaxBytes),
                capped.ModVersionText ?? "", "the display string is capped, not corrupted");

        // A TRUNCATED version record (claims 7 payload bytes, delivers 1): the tail is
        // abandoned mid-record, everything parsed before it survives, nothing throws.
        byte[] cutVer = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 03 07 01");
        t.True(PresenceSerializer.TryRead(cutVer, cutVer.Length, out PresenceState cutState),
               "a truncated version record still parses the packet");
        t.True(!cutState.HasModVersion, "and the incomplete record is simply not delivered");

        // -- 7e. Board-UI record (buttons + wanted glow) -----------------------------------
        // Record id 4: [buttons][overlays]. Written on every packet with a live tray, so
        // "record present, bits clear" (no dynamic controls shown) is distinguishable from
        // "sender predates the field" (legacy always-drawn furniture on the receiver).
        t.Case("7e. extras, board-UI + fan-anchor records");
        m = PresenceSerializer.Write(ExtrasBoardUiAndFanAnchor(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            81               // flags: FlagHasBoard | FlagPileBrowse ('a BLOCK follows')
            " + PoseBoard + @"
            0000803F         // boardScale = 1.0
            02               // handCardCount
            80               // byte A: PileBrowseExtensionBit only
            00               // byte B: count 0 -> no fan
            02               // tail: 2 records
            04 03 B5 02 00   // record: id 4 (board UI), len 3, buttons 0xB5, overlays 0x02,
                             //   cap states 0x00 (nothing accented, every gated cap DIMMED)
            05 0C            // record: id 5 (fan anchor), len 12
            0000003F         // x = 0.5
            0000803E         // y = 0.25
            000000BE         // z = -0.125
            "), ext, m, "board-UI and fan-anchor records ride the tail as [id][len][payload]");
        t.Equal(54, m, "header 7 + board 24 + count 1 + block 2 + tail 1 + 5 + 14 = 54 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState bu), "and it parses");
        t.True(bu.HasBoardUi, "the board-UI record is delivered");
        t.Equal((byte)0xB5, bu.BoardButtonsMask, "with the buttons mask intact");
        t.Equal((byte)0x02, bu.BoardOverlayMask, "and the wanted-glow mask intact");
        t.True(bu.HasFanAnchor, "the fan anchor is delivered");
        t.True(bu.FanAnchorLocal == new Vector3(0.5f, 0.25f, -0.125f),
               "with the exact board-local position");

        // Overlay hygiene: only the DEFINED overlay bits are wire state — bits 0..1 (wanted-slot
        // glow), bit 2 (FOLLOW/PIN), bits 3..4 (card-slot occupancy), bit 5 (that nibble's validity)
        // and, since the mirrored-cap round, bits 6..7 (the snap-glow HOVER field). The writer masks
        // anything undefined so a future use cannot be pre-claimed by garbage, and the reader masks
        // again (never trust the wire). This expectation moved 0x06 → 0x3E → 0xFE as the occupancy
        // nibble and then the snap field widened BoardUiOverlayMask — which is exactly the assertion
        // that would catch a widening done on only one side.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x01, BoardOverlayMask = 0xFE,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ov), "overlay-mask packet parses");
        t.Equal((byte)0xFE, ov.BoardOverlayMask,
                "every defined overlay bit survives (0xFE -> 0xFE: the byte is FULL now)");
        t.Equal((byte)0x00, (byte)(0xFF & ~NetProtocol.BoardUiOverlayMask),
                "and NO reserved overlay bit is left — the next overlay field needs a new byte, " +
                "not a free bit in this one");
        // The cap-state byte gets the same hygiene — and as of the item-pile cue bit it is FULL too.
        // This expectation moved 0x7F -> 0xFF when BoardUiCapItemPileUsableBit (bit 7) was claimed,
        // which is exactly the assertion that would have caught the mask being widened on only one
        // side of the codec.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00, BoardCapStateMask = 0xFF,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cs), "cap-state packet parses");
        t.True(cs.HasBoardCapStates, "and the 3-byte record delivers its cap-state byte");
        t.Equal((byte)0xFF, cs.BoardCapStateMask,
                "every defined cap-state bit survives (0xFF -> 0xFF: bit 7 is the item-pile cue now)");
        t.Equal((byte)0x00, (byte)(0xFF & ~NetProtocol.BoardUiCapStateDefinedMask),
                "and NO reserved bit is left in byte 2 either — record 4 is FULL in all three " +
                "bytes, so the next board-UI flag needs a FOURTH byte, not a free bit");

        // -- 7f. FOLLOW/PIN (board-UI byte 1 bit 2) ----------------------------------------
        // The cross-version contract in both directions, byte-exact.
        //   * A PINNED sender emits bit 2, and only bit 2 — no length change, no new record.
        //   * A pre-bit sender's byte 1 has it CLEAR, which must decode as FOLLOW (the
        //     un-accented default look every earlier build already drew), never as garbage.
        t.Case("7f. extras, FOLLOW/PIN state");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardOverlayMask = NetProtocol.BoardUiPinnedBit,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            04 03 00 04 00   // record: id 4 (board UI), len 3, buttons 0x00,
                             //   overlays 0x04 = PINNED, cap states 0x00
            "), ext, m, "the pinned bit rides byte 1 of the EXISTING board-UI record — zero new bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState pin), "and it parses");
        t.Equal((byte)NetProtocol.BoardUiPinnedBit, pin.BoardOverlayMask, "PINNED survives the round trip");
        t.Equal((byte)0x00, (byte)(pin.BoardOverlayMask & NetProtocol.BoardUiWantedMask),
                "and it does not bleed into the wanted-slot glow mask");

        // A pre-bit sender: byte 1 = only a wanted-glow bit. Bit 2 clear == FOLLOW.
        byte[] preBit = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 04 02 B5 01");
        t.True(PresenceSerializer.TryRead(preBit, preBit.Length, out PresenceState preState),
               "a pre-pinned-bit board-UI record still parses");
        t.Equal((byte)0x01, preState.BoardOverlayMask, "its overlay byte survives unchanged");
        t.True((preState.BoardOverlayMask & NetProtocol.BoardUiPinnedBit) == 0,
               "and reads as FOLLOW — the look those builds were already drawn in");

        // -- 7f2. CARD-SLOT OCCUPANCY (board-UI byte 1 bits 3..4 + validity bit 5) ---------
        // The user requirement: "wo aktuell eine Karte liegt und wo nicht auf dem controllboard
        // soll vollstaendig synchronisiert werden" — a peer must see a card BACK lying in exactly
        // the recesses the owner has filled, and an empty recess where they have none. Two bits of
        // POSITION (never an identity) plus a validity bit, all inside the EXISTING two-byte
        // record: no new record, no length change, wire version still 3.
        t.Case("7f2. extras, card-slot occupancy");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardOverlayMask = (byte)(NetProtocol.BoardUiSlot0Bit | NetProtocol.BoardUiSlotsValidBit),
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            04 03 00 28 00   // record: id 4 (board UI), len 3, buttons 0x00,
                             //   overlays 0x28 = slot0 occupied (0x08) | occupancy valid (0x20),
                             //   cap states 0x00
            "), ext, m, "the occupancy nibble rides byte 1 of the EXISTING board-UI record");
        t.Equal(16, m, "and costs ZERO extra bytes of its own — the same 16 as the FOLLOW/PIN " +
                       "vector above, both of which grew by the ONE cap-state byte");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sl), "and it parses");
        t.Equal((byte)0x28, sl.BoardOverlayMask, "the occupancy + validity bits survive the round trip");
        t.Equal(1, (sl.BoardOverlayMask & NetProtocol.BoardUiSlotMask) >> NetProtocol.BoardUiSlotShift,
                "slot 0 occupied, slot 1 empty");
        t.Equal((byte)0x00, (byte)(sl.BoardOverlayMask & NetProtocol.BoardUiWantedMask),
                "and it does not bleed into the wanted-slot glow mask");
        t.True((sl.BoardOverlayMask & NetProtocol.BoardUiPinnedBit) == 0,
               "nor into the FOLLOW/PIN bit");

        // BOTH recesses full, on top of a wanted glow and a PINNED board — every defined overlay
        // bit at once, byte-exact, so a re-assignment of any of them shows up here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0xB5,
            BoardOverlayMask = (byte)(0x02 | NetProtocol.BoardUiPinnedBit
                                      | NetProtocol.BoardUiSlot0Bit | NetProtocol.BoardUiSlot1Bit
                                      | NetProtocol.BoardUiSlotsValidBit),
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            04 03 B5 3E 00   // id 4, len 3, buttons 0xB5, overlays 0x3E =
                             //   wanted bit1 (0x02) | PINNED (0x04) | both slots (0x18) | valid (0x20),
                             //   cap states 0x00
            "), ext, m, "wanted glow, FOLLOW/PIN and both occupancy bits coexist in one byte");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState both), "and it parses");
        t.Equal(3, (both.BoardOverlayMask & NetProtocol.BoardUiSlotMask) >> NetProtocol.BoardUiSlotShift,
                "both recesses read as occupied");
        t.Equal((byte)0x02, (byte)(both.BoardOverlayMask & NetProtocol.BoardUiWantedMask),
                "the wanted-glow mask is untouched by the nibble above it");
        t.True((both.BoardOverlayMask & NetProtocol.BoardUiPinnedBit) != 0,
               "and so is the PINNED bit");

        // EMPTY-BUT-KNOWN. This is the half of the requirement a plain occupancy mask cannot
        // express: "both recesses are empty" is real state and must NOT decode the same as "this
        // sender has no idea". The validity bit is what separates them, and it is why the nibble
        // could not copy the FOLLOW/PIN trick of letting 0 mean the old look.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardOverlayMask = NetProtocol.BoardUiSlotsValidBit,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState empty), "empty-but-known parses");
        t.True((empty.BoardOverlayMask & NetProtocol.BoardUiSlotsValidBit) != 0,
               "an owner with two EMPTY recesses still asserts the validity bit");
        t.Equal(0, (empty.BoardOverlayMask & NetProtocol.BoardUiSlotMask) >> NetProtocol.BoardUiSlotShift,
                "with an all-clear occupancy nibble");

        // A PRE-OCCUPANCY sender (wanted glow only, as build 18 wrote it): the nibble AND the
        // validity bit are clear, which must read as "unknown" and never as "both empty" — the
        // receiver keeps rendering that peer's slots from the replicated model alone.
        byte[] preSlots = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 04 02 B5 01");
        t.True(PresenceSerializer.TryRead(preSlots, preSlots.Length, out PresenceState preSlotState),
               "a pre-occupancy board-UI record still parses");
        t.Equal((byte)0x01, preSlotState.BoardOverlayMask, "its overlay byte survives unchanged");
        t.True((preSlotState.BoardOverlayMask & NetProtocol.BoardUiSlotsValidBit) == 0,
               "and reads as UNKNOWN occupancy, not as 'both recesses empty'");

        // -- 7f3. SNAP-GLOW HOVER TELEGRAPH (board-UI byte 1 bits 6..7) -------------------
        // The gold "the held card lands HERE on release" rim, which the previous revision claimed
        // "cannot be" reproduced and instead faked from the occupancy edge (i.e. AFTER the drop).
        // A recess index in the two bits that byte was still reserving: ZERO extra bytes.
        t.Case("7f3. extras, snap-glow hover telegraph");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardOverlayMask = (byte)(NetProtocol.EncodeSnapSlot(1) << NetProtocol.BoardUiSnapShift),
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            04 03 00 80 00   // record: id 4, len 3, buttons 0x00,
                             //   overlays 0x80 = snap field value 2 (= slot 1) at bits 6..7,
                             //   cap states 0x00
            "), ext, m, "the snap-glow recess rides bits 6..7 of the EXISTING overlay byte");
        t.Equal(16, m, "and costs ZERO extra bytes — same 16 as the occupancy vector above");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sn), "and it parses");
        t.Equal(1, NetProtocol.DecodeSnapSlot(
                    (sn.BoardOverlayMask & NetProtocol.BoardUiSnapMask) >> NetProtocol.BoardUiSnapShift),
                "the hovered recess survives the round trip");
        t.Equal((byte)0x00, (byte)(sn.BoardOverlayMask & NetProtocol.BoardUiWantedMask),
                "and does not bleed into the wanted-slot glow mask");
        t.Equal(0, (sn.BoardOverlayMask & NetProtocol.BoardUiSlotMask) >> NetProtocol.BoardUiSlotShift,
                "nor into the occupancy nibble");

        // The ENCODING contract, both directions, including the two values that must degrade.
        t.Equal((byte)0, NetProtocol.EncodeSnapSlot(-1), "no hover encodes as the 0 sentinel");
        t.Equal((byte)1, NetProtocol.EncodeSnapSlot(0), "slot 0 encodes as 1 — the ids are 1-based");
        t.Equal((byte)2, NetProtocol.EncodeSnapSlot(1), "slot 1 encodes as 2");
        t.Equal((byte)0, NetProtocol.EncodeSnapSlot(2),
                "a recess this two-slot board does not have degrades to 'no hover', never to a " +
                "glow on the wrong recess");
        t.Equal(-1, NetProtocol.DecodeSnapSlot(0), "the 0 sentinel decodes to 'none'");
        t.Equal(-1, NetProtocol.DecodeSnapSlot(3),
                "and so does the invalid value 3 — never trust the wire");

        // A PRE-SNAP sender (build <= 88): bits 6..7 clear. 0 has to mean "no rim", because that is
        // what those builds' receivers rendered — the same "0 is the old look" rule the PINNED bit
        // follows, and the reason the sentinel is 0 rather than a cap id.
        byte[] preSnap = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 04 02 B5 01");
        t.True(PresenceSerializer.TryRead(preSnap, preSnap.Length, out PresenceState preSnapState),
               "a pre-snap board-UI record still parses");
        t.Equal(-1, NetProtocol.DecodeSnapSlot(
                    (preSnapState.BoardOverlayMask & NetProtocol.BoardUiSnapMask)
                    >> NetProtocol.BoardUiSnapShift),
                "and reads as NO hover — the receiver falls back to its legacy occupancy-edge flash");

        // -- 7f4. CAP STATES (board-UI BYTE 2, length-gated) ------------------------------
        // The seven bits that end "every mirrored cap looks enabled and un-accented". Its validity
        // flag is the record's own LENGTH, which is why the byte needed no bit of its own.
        t.Case("7f4. extras, cap states");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true,
            BoardButtonsMask = (byte)(NetProtocol.BoardUiConfirmBit
                                      | NetProtocol.BoardUiShortRestBit
                                      | NetProtocol.BoardUiSkipBit),
            BoardCapStateMask = (byte)(NetProtocol.BoardUiCapConfirmReadyBit
                                       | NetProtocol.BoardUiCapShortRestAccentBit
                                       | NetProtocol.BoardUiCapSkipEnabledBit),
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            04 03 51 00 4A   // id 4, len 3, buttons 0x51 = confirm|shortRest|skip,
                             //   overlays 0x00, cap states 0x4A = confirm CONFIRMED (0x02)
                             //   | short rest ACCENT (0x08) | skip ENABLED (0x40).
                             //   Note the short rest is ACCENTED but NOT enabled: the owner
                             //   selected a rest that is no longer available, which is a real
                             //   board state and a THIRD distinct look.
            "), ext, m, "the cap-state byte is the board-UI record's third byte, 1 B for 7 states");
        t.Equal(16, m, "ONE byte more than the two-byte form — a new record would have cost three");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState st), "and it parses");
        t.True(st.HasBoardCapStates, "the cap-state byte is delivered");
        t.True((st.BoardCapStateMask & NetProtocol.BoardUiCapConfirmReadyBit) != 0,
               "the CONFIRM cap reads as confirmed (worn brass on the mirror)");
        t.True((st.BoardCapStateMask & NetProtocol.BoardUiCapConfirmAccentBit) == 0,
               "and not as merely accented — confirmed beats accent, as StateColor resolves them");
        t.True((st.BoardCapStateMask & NetProtocol.BoardUiCapShortRestAccentBit) != 0
               && (st.BoardCapStateMask & NetProtocol.BoardUiCapShortRestEnabledBit) == 0,
               "the short-rest disc reads SELECTED but UNAVAILABLE — the dark-wood look, which is " +
               "neither of the two the mirror could show before");
        t.True((st.BoardCapStateMask & NetProtocol.BoardUiCapSkipEnabledBit) != 0,
               "and the skip cap reads pressable");
        t.Equal((byte)0x00, st.BoardOverlayMask, "with nothing bled into the overlay byte");

        // THE LENGTH IS THE VALIDITY FLAG. A hand-built TWO-byte record — exactly what every build
        // up to 88 wrote — must deliver its buttons and overlays and NO cap states, so the receiver
        // keeps every mirrored cap at the colour it was built with instead of reading zeros as
        // "everything disabled and un-accented".
        byte[] preCaps = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 04 02 51 01");
        t.True(PresenceSerializer.TryRead(preCaps, preCaps.Length, out PresenceState preCapState),
               "a two-byte board-UI record still parses");
        t.True(preCapState.HasBoardUi && preCapState.BoardButtonsMask == 0x51,
               "its buttons mask is delivered unchanged");
        t.True(!preCapState.HasBoardCapStates,
               "but NO cap states are claimed — the length says the byte is absent, and the " +
               "receiver therefore leaves the caps at their built colour rather than painting " +
               "every gated cap DIMMED from a byte that was never sent");
        t.Equal((byte)0x00, preCapState.BoardCapStateMask,
                "and the mask stays clear, so a consumer that forgets the flag still gets zeros " +
                "rather than garbage");

        // -- 7f5. THE ITEM-PILE USABLE CUE (board-UI BYTE 2, BIT 7) ------------------------
        // The last free bit anywhere in record 4, spent on the closed items pile's "an item can be
        // played right now" heartbeat cue. Before it, a peer's mirrored items stack was three inert
        // slabs and a number while the owner's puffed embers and threw rings — a straight breach of
        // the standing 1:1 ruling on board ANIMATIONS, and one that cost nothing to fix because this
        // byte already rides every packet that carries a board pose.
        t.Case("7f5. extras, item-pile usable cue");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true,
            // The whole item flow in ONE packet, which is the reason the bit lives in this record at
            // all: the recess (byte 0 bit 2), the USE cap (byte 0 bit 3) and the pile cue (byte 2
            // bit 7) are one picture, and a peer that got them in different frames would paint half
            // of it against the other half's state.
            BoardButtonsMask = (byte)(NetProtocol.BoardUiItemRecessBit
                                      | NetProtocol.BoardUiItemUseCapBit),
            BoardCapStateMask = NetProtocol.BoardUiCapItemPileUsableBit,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            04 03 0C 00 80   // id 4, len 3, buttons 0x0C = item recess | item USE cap,
                             //   overlays 0x00, cap states 0x80 = the item-pile usable cue.
                             //   ZERO extra bytes over the previous build: the cue is a bit in a
                             //   byte that was already being written.
            "), ext, m, "the item-pile cue is bit 7 of the board-UI record's existing third byte");
        t.Equal(16, m, "and the packet is the same 16 bytes a cue-less one costs — a new record " +
                       "would have been three more");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cue), "and it parses");
        t.True(cue.HasBoardCapStates, "the cap-state byte is delivered");
        t.True((cue.BoardCapStateMask & NetProtocol.BoardUiCapItemPileUsableBit) != 0,
               "the peer's mirrored items stack beats: embers puff and rings fly on the shared " +
               "item heartbeat, driven on the receiver's own clock from this bit's edges");
        t.Equal((byte)0x00,
                (byte)(cue.BoardCapStateMask & ~NetProtocol.BoardUiCapItemPileUsableBit),
                "and NOTHING bled into the seven CAP bits below it — a lit pile must not accent a " +
                "cap or enable a rest disc");
        t.True((cue.BoardButtonsMask & NetProtocol.BoardUiItemRecessBit) != 0
               && (cue.BoardButtonsMask & NetProtocol.BoardUiItemUseCapBit) != 0,
               "with the recess and the USE cap arriving in the SAME packet, which is why the bit " +
               "rides record 4 rather than one of its own");

        // A PRE-CUE sender (every build up to this one): the byte is present — it has carried the
        // cap states since ModBuild 88 — but bit 7 is clear, and clear has to mean "no cue", because
        // that is exactly what those builds' receivers drew (nothing at all on the mirrored stack).
        // The same "0 is the old look" rule the PINNED and SNAP fields follow.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardCapStateMask = NetProtocol.BoardUiCapSkipEnabledBit, // 0x40 — bit 7 clear
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState preCue),
               "a cap-state byte without bit 7 still parses");
        t.True(preCue.HasBoardCapStates
               && (preCue.BoardCapStateMask & NetProtocol.BoardUiCapItemPileUsableBit) == 0,
               "and reads as NO cue — the mirrored stack stays dark, which is what every build " +
               "before this one rendered");

        // AN OLD READER, the symmetric half. Builds that shipped before this bit mask byte 2 with
        // their own narrower 0x7F, so a packet from a player whose pile IS lit must decode on their
        // machine as the seven cap states they know and nothing else — dropped, never mis-rendered
        // as an eighth cap state.
        const byte LegacyCapStateMask = 0x7F; // BoardUiCapStateDefinedMask before the cue bit
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x00,
            BoardCapStateMask = (byte)(NetProtocol.BoardUiCapItemPileUsableBit
                                       | NetProtocol.BoardUiCapConfirmAccentBit),
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState oldCue), "old-reader packet parses");
        t.Equal((byte)NetProtocol.BoardUiCapConfirmAccentBit,
                (byte)(oldCue.BoardCapStateMask & LegacyCapStateMask),
                "an old reader sees ONLY its own seven bits (here: the accented CONFIRM cap) and " +
                "drops the cue bit — additive, never corrupting");

        // ---- BACKWARD COMPATIBILITY, the explicit assertion ------------------------------
        // An OLD reader (build <= 18) masks byte 1 with its OWN narrower overlay mask, 0x07. Feed
        // it a packet from a build that fills every new bit and it must still (a) parse the packet,
        // (b) step over the record by its own length byte and land on whatever follows, and (c) see
        // EXACTLY its own three bits — the occupancy nibble and its validity bit are invisible to
        // it, not corrupting.
        const byte LegacyOverlayMask = 0x07; // BoardUiOverlayMask as builds <= 18 defined it
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x42,
            BoardOverlayMask = (byte)(0x03 | NetProtocol.BoardUiPinnedBit
                                      | NetProtocol.BoardUiSlot0Bit | NetProtocol.BoardUiSlot1Bit
                                      | NetProtocol.BoardUiSlotsValidBit),
            HasCardHighlight = true, HandHighlightIndex = 5,
            FanHighlightIndex = NetProtocol.CardHighlightNone,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            02               // tail: 2 records
            04 03 42 3F 00   // id 4, len 3, buttons 0x42, overlays 0x3F, cap states 0x00
            06 02 05 FF      // id 6 card highlight — the record an OLD reader must still reach
            "), ext, m, "a fully-populated overlay byte still leaves the tail walk byte-identical");
        t.Equal((byte)3, ext[12], "the board-UI record's LENGTH byte is 3 now that the cap-state " +
                                  "byte rides it — and an old reader's 'i += len' therefore still " +
                                  "skips exactly the right distance, which is the whole point of " +
                                  "TLV: the extra byte is INVISIBLE to it, never a shifted tail walk");
        t.Equal((byte)0x3F, ext[14], "and the new bits really are on the wire in byte 1");
        t.Equal((byte)0x07, (byte)(ext[14] & LegacyOverlayMask),
                "an OLD reader masking byte 1 with its own 0x07 sees exactly its own three bits " +
                "(wanted 0..1 + PINNED) — the occupancy nibble and its validity bit are invisible " +
                "to it, never mis-read as one of them");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState compat),
               "and the packet carrying the new bits parses end to end");
        t.Equal((byte)5, compat.HandHighlightIndex,
                "including the record BEHIND the board-UI one — proof the tail walk is unshifted, " +
                "which is what an old peer's parser depends on");

        // -- 7g. CARD HIGHLIGHT (extension record 6) ---------------------------------------
        // Two fan-local INDICES, never a card identity. Written only while something is
        // highlighted, so an idle packet stays byte-identical to the previous build's.
        t.Case("7g. extras, card-highlight record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCardHighlight = true, HandHighlightIndex = 3,
            FanHighlightIndex = NetProtocol.CardHighlightNone,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            06 02 03 FF      // record: id 6, len 2, hand index 3, board fan index 255 (none)
            "), ext, m, "the card-highlight record is [id][len][hand index][board-fan index]");
        t.Equal(15, m, "header 7 + count 1 + block 2 + tail 1 + 4 = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hl), "and it parses");
        t.True(hl.HasCardHighlight, "the highlight record is delivered");
        t.Equal((byte)3, hl.HandHighlightIndex, "with the hand-fan index intact");
        t.Equal(NetProtocol.CardHighlightNone, hl.FanHighlightIndex, "and 'none' for the board fan");

        // The record is ORDERED behind the fan anchor, so a packet carrying both is byte-exact
        // in the documented id order (4, 5, 6) — a reorder in Write would show up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x01, BoardOverlayMask = 0x00,
            HasFanAnchor = true, FanAnchorLocal = new Vector3(0.5f, 0.25f, -0.125f),
            HasCardHighlight = true, HandHighlightIndex = NetProtocol.CardHighlightNone,
            FanHighlightIndex = 7,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            03               // tail: 3 records, in id order
            04 03 01 00 00   // id 4 board UI (3-byte form)
            05 0C 0000003F 0000803E 000000BE   // id 5 fan anchor
            06 02 FF 07      // id 6 card highlight
            "), ext, m, "records ride the tail in id order: board UI, fan anchor, card highlight");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState all), "and all three parse");
        t.Equal((byte)7, all.FanHighlightIndex, "the board-fan highlight index survives");

        // -- 7h. PICK BANNER (extension record 7) ------------------------------------------
        // The owner's placard line, UTF8, capped and truncated on a CHARACTER boundary. Written
        // only while a placard is up; an actor and a count, never a card identity.
        t.Case("7h. extras, pick-banner record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPickBanner = true, PickBannerText = "AB",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            07 02 41 42      // record: id 7, len 2, UTF8 'A' 'B'
            "), ext, m, "the pick-banner record is [id][len][UTF8 bytes]");
        t.Equal(15, m, "header 7 + count 1 + block 2 + tail 1 + 4 = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState pb), "and it parses");
        t.True(pb.HasPickBanner, "the pick-banner record is delivered");
        t.Equal("AB", pb.PickBannerText ?? string.Empty, "with the line intact");

        // Ordered LAST, behind the card highlight — a reorder in Write shows up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCardHighlight = true, HandHighlightIndex = 1,
            FanHighlightIndex = NetProtocol.CardHighlightNone,
            HasPickBanner = true, PickBannerText = "A",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            02               // tail: 2 records, in id order
            06 02 01 FF      // id 6 card highlight
            07 01 41         // id 7 pick banner, UTF8 'A'
            "), ext, m, "the pick banner rides the tail after the card highlight (id order 6, 7)");

        // A multi-byte glyph survives the round trip intact (the German placard is full of them).
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPickBanner = true, PickBannerText = "Wähle",
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState uml), "a UTF8 line parses");
        t.Equal("Wähle", uml.PickBannerText ?? string.Empty, "and the umlaut survives byte-exact");

        // An empty line emits NO record — "placard down" and "sender predates the record" must
        // render identically, and an idle packet stays byte-identical to the previous build's.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPickBanner = true, PickBannerText = string.Empty, HandCardCount = 5,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty pick banner writes no record at all");

        // The cap truncates on a CHARACTER boundary: a run of 2-byte glyphs longer than the cap
        // must never be cut mid-sequence (that would decode as a replacement char on the peer).
        string longLine = new string('ä', NetProtocol.PickBannerTextMaxBytes);
        byte[] cappedBanner = PresenceSerializer.EncodePickBannerText(longLine);
        t.True(cappedBanner.Length <= NetProtocol.PickBannerTextMaxBytes,
               "the encoded line honours the cap");
        t.Equal(new string('ä', NetProtocol.PickBannerTextMaxBytes / 2),
                System.Text.Encoding.UTF8.GetString(cappedBanner),
                "and it truncated on a character boundary, not mid-glyph");

        // An idle player emits NO record at all — the whole point of gating it on "something is
        // really highlighted" (a peer that predates the record renders the same flat fans).
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "nothing highlighted -> no block, no tail, no record: byte-identical to build 20");

        // Encoder clamp: a negative or out-of-range index can never single out the wrong card.
        t.Equal(NetProtocol.CardHighlightNone, NetProtocol.EncodeHighlightIndex(-1),
                "index -1 (nothing hovered) encodes as 'none'");
        t.Equal(NetProtocol.CardHighlightNone, NetProtocol.EncodeHighlightIndex(255),
                "an index at the sentinel encodes as 'none' rather than colliding with it");
        t.Equal((byte)254, NetProtocol.EncodeHighlightIndex(254), "the last real index survives");

        // A truncated highlight record abandons the tail without delivering half a value.
        byte[] cutHl = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 06 02 03");
        t.True(PresenceSerializer.TryRead(cutHl, cutHl.Length, out PresenceState cutHlState),
               "a truncated highlight record still parses the packet");
        t.True(!cutHlState.HasCardHighlight, "and the incomplete record is simply not delivered");

        // A NaN fan-anchor component from a hostile/corrupt sender must not place a fan at NaN:
        // the record is dropped (reads as absent = the authored default spot), the packet parses.
        byte[] nanAnchor = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            01               // 1 record
            05 0C            // id 5, len 12
            0000C07F         // x = NaN
            0000803E 000000BE
            ");
        t.True(PresenceSerializer.TryRead(nanAnchor, nanAnchor.Length, out PresenceState nan),
               "a NaN fan anchor still parses the packet");
        t.True(!nan.HasFanAnchor, "and the poisoned record is simply not delivered");

        // Unknown-record skip in FRONT of the new ids (the old-reader contract, seen from the
        // other side): a record this build does not know is stepped over by its own length and
        // the fan anchor behind it is still read. To a BUILD-1 reader ids 4 and 5 are exactly
        // this unknown-id case, so it parses these packets unchanged and simply keeps its
        // legacy furniture/fan placement.
        byte[] futureAnchor = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            05 0C 0000003F 0000803E 000000BE
            ");
        t.True(PresenceSerializer.TryRead(futureAnchor, futureAnchor.Length, out PresenceState fa),
               "a packet with an unknown record ahead of the fan anchor parses");
        t.True(fa.HasFanAnchor, "and the fan anchor behind it is still read");
        t.True(fa.FanAnchorLocal == new Vector3(0.5f, 0.25f, -0.125f), "with its value intact");

        // A TRUNCATED fan-anchor record (claims 12 payload bytes, delivers 4): the tail is
        // abandoned mid-record, everything parsed before it survives, nothing throws.
        byte[] cutAnchor = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 05 0C 00 00 00 3F");
        t.True(PresenceSerializer.TryRead(cutAnchor, cutAnchor.Length, out PresenceState cutFa),
               "a truncated fan-anchor record still parses the packet");
        t.True(!cutFa.HasFanAnchor, "and the incomplete record is simply not delivered");

        // -- 7i. SECOND HELD FIGURE (extension record 8) -----------------------------------
        // A VR player can hold one board figure PER HAND. The rig packet carries the first one and
        // its flag byte is full, so the second rides the extras tail: [hand flags][int32 actorId
        // LE][pose 20] = 25 bytes. Written only while a second mini is really held.
        t.Case("7i. extras, second held figure record");
        m = PresenceSerializer.Write(ExtrasSecondFigure(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            08 19            // record: id 8 (second held figure), len 25
            01               // hands: bit0 second figure LEFT, bit1 clear -> first figure RIGHT
            04 03 02 01      // actorId int32 LE = 0x01020304
            " + PoseFigure + @"
            "), ext, m, "the second-figure record is [id 8][len 25][hands][actorId][pose]");
        t.Equal(38, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 25 = 38 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sf), "and it parses");
        t.True(sf.HasSecondFigure, "the second figure is delivered");
        t.Equal(0x01020304, sf.SecondFigureActorId, "with the stable actor id intact");
        t.True(sf.SecondFigurePose.Position == new Vector3(8f, 0f, 0.25f),
               "and the exact world-frame position");
        t.True(sf.SecondFigureLeftHand, "the second mini is in the LEFT hand");
        t.True(!sf.PrimaryFigureLeftHand, "and the first one in the RIGHT — the pairing the rig packet cannot state");

        // ABSENT WHEN NOT HELD. One figure (or none) must emit the exact bytes previous builds
        // emitted: no record, no tail, no block. This is the whole backward-compatibility argument
        // for the SENDER side — a player who is not holding two minis is byte-for-byte a
        // pre-record-8 sender.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "no second figure -> no record, no tail, no block: byte-identical to build 36");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noSf), "and it parses");
        t.True(!noSf.HasSecondFigure, "with HasSecondFigure false (peers release only that slot)");

        // Ordered LAST, behind the pick banner — a reorder in Write shows up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCardHighlight = true, HandHighlightIndex = 1,
            FanHighlightIndex = NetProtocol.CardHighlightNone,
            HasPickBanner = true, PickBannerText = "A",
            HasSecondFigure = true, SecondFigureActorId = 0x01020304, SecondFigurePose = Figure(),
            SecondFigureLeftHand = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            03               // tail: 3 records, in id order
            06 02 01 FF      // id 6 card highlight
            07 01 41         // id 7 pick banner, UTF8 'A'
            08 19 01 04 03 02 01
            " + PoseFigure + @"
            "), ext, m, "the second figure rides the tail after the pick banner (id order 6, 7, 8)");

        // BACKWARD COMPATIBILITY, the direction that actually ships: a peer built BEFORE record 8
        // receives this packet. To that reader id 8 is exactly the unknown-id case, so it steps over
        // the record by its own length and everything it DOES know is untouched. Asserted with a
        // hand-built packet in which a 25-byte unknown record sits where record 8 sits, followed by
        // a record every build since the handshake knows.
        byte[] oldReader = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            02               // 2 records
            63 19            // id 99, len 25 -- record 8 as a PRE-RECORD-8 READER sees it
            01 04 03 02 01
            " + PoseFigure + @"
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReader, oldReader.Length, out PresenceState oldRd),
               "a 25-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldRd.HasSecondFigure, "the unknown record delivers nothing (as on a pre-record-8 peer)");
        t.True(oldRd.HasModVersion, "and the record behind it is read past it");
        t.Equal((ushort)1, oldRd.ModBuild, "with its value intact");

        // THE HAND BITS ARE LOAD-BEARING: a record claiming BOTH figures are in the same hand is a
        // contradiction no local grab can produce (a hand holds one object) and a stale/corrupt
        // packet can. It is rejected outright rather than stacking two minis in one palm.
        byte[] sameHand = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            08 19
            03               // hands: bit0 AND bit1 -> both figures claim the LEFT hand
            04 03 02 01
            " + PoseFigure);
        t.True(PresenceSerializer.TryRead(sameHand, sameHand.Length, out PresenceState clash),
               "a contradictory hand pair still parses the packet");
        t.True(!clash.HasSecondFigure, "and the record is dropped — never two minis in one palm");

        // Actor id 0 means "no figure" everywhere in this system, so it can never identify one.
        byte[] zeroId = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            08 19 01 00 00 00 00
            " + PoseFigure);
        t.True(PresenceSerializer.TryRead(zeroId, zeroId.Length, out PresenceState zeroSf),
               "an actor id of 0 still parses the packet");
        t.True(!zeroSf.HasSecondFigure, "and the record is dropped");

        // A NaN position from a hostile/corrupt sender must not fling a real board figure out of
        // the world — the same guard the fan anchor carries, for the same reason.
        byte[] nanFigure = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            08 19 01 04 03 02 01
            0000C07F 00000000 0000803E   // pos x = NaN
            0000 0000 FF7F 0000
            ");
        t.True(PresenceSerializer.TryRead(nanFigure, nanFigure.Length, out PresenceState nanSf),
               "a NaN second-figure position still parses the packet");
        t.True(!nanSf.HasSecondFigure, "and the poisoned record is simply not delivered");

        // A TRUNCATED record (claims 25 payload bytes, delivers 5): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutFigure = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 08 19 01 04 03 02 01");
        t.True(PresenceSerializer.TryRead(cutFigure, cutFigure.Length, out PresenceState cutSf),
               "a truncated second-figure record still parses the packet");
        t.True(cutSf.HasMaskSize, "the mask size ahead of the tail survives");
        t.True(!cutSf.HasSecondFigure, "and the incomplete record is simply not delivered");

        // The RIG packet is untouched by all of this: the first held figure still rides its own
        // 15 Hz block at the same offsets, which is why a pre-record-8 peer keeps seeing that one.
        n = AvatarSerializer.Write(RigHeldFigureAndCard(), rig);
        t.Equal(77, n, "the rig packet's held-figure block is byte-unchanged by the second slot");
        t.True(AvatarSerializer.TryRead(rig, n, out AvatarState rigHeld), "and it parses");
        t.True(rigHeld.HasHeldFigure, "with the FIRST held figure still delivered by the rig packet");
        t.Equal(0x01020304, rigHeld.HeldFigureActorId, "and its actor id intact");

        // -- 7i2. HELD-FIGURE STRETCH (extension record 30) --------------------------------
        // The manual two-hand resize factor of the sender's held figure(s): [u16 primary LE]
        // [u16 secondary LE], milli-factors, 1000 = 1.0× = neutral. Written only while a factor
        // is non-neutral; absence means both are 1.0, and a garbage code decodes to neutral —
        // never to a clamped extreme.
        t.Case("7i2. extras, held-figure stretch record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHeldStretch = true,
            HeldStretchPrimaryCode = 1500,  // 1.5×
            HeldStretchSecondaryCode = 1000, // neutral — that slot is unstretched (or empty)
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            1E 04            // record: id 30 (held-figure stretch), len 4
            DC 05            // primary milli-factor 1500 = 1.5x, u16 LE
            E8 03            // secondary milli-factor 1000 = neutral, u16 LE
            "), ext, m, "the stretch record is [id 30][len 4][u16 primary][u16 secondary]");
        t.Equal(17, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 4 = 17 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hs1), "and it parses");
        t.True(hs1.HasHeldStretch, "the stretch record is delivered");
        t.Equal(1500, (int)hs1.HeldStretchPrimaryCode, "with the primary factor intact");
        t.Equal(1000, (int)hs1.HeldStretchSecondaryCode, "and the neutral secondary intact");

        // Both slots stretched (the second mini was grabbed after a first was resized, then
        // resized itself): shrink and grow travel together in one 4-byte record.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHeldStretch = true,
            HeldStretchPrimaryCode = 500,   // 0.5×
            HeldStretchSecondaryCode = 3000, // 3.0×
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            1E 04
            F4 01            // primary 500 = 0.5x
            B8 0B            // secondary 3000 = 3.0x
            "), ext, m, "both slots ride the same record, primary first");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hs2), "and it parses");
        t.Equal(500, (int)hs2.HeldStretchPrimaryCode, "the shrunk primary is delivered");
        t.Equal(3000, (int)hs2.HeldStretchSecondaryCode, "and the grown secondary with it");

        // ABSENT WHEN NEUTRAL — the whole backward-compatibility argument for the sender side: a
        // player who never stretched (or whose gesture came back to exactly 1.0) is byte-for-byte
        // a pre-record-30 sender, tail and all.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHeldStretch = true,
            HeldStretchPrimaryCode = (ushort)NetProtocol.HeldStretchCodeNeutral,
            HeldStretchSecondaryCode = (ushort)NetProtocol.HeldStretchCodeNeutral,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "both factors neutral -> no record, no tail, no block: byte-identical to build 113");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noHs), "and it parses");
        t.True(!noHs.HasHeldStretch, "with HasHeldStretch false (peers reset to neutral)");

        // THE QUANTIZER'S CLAMPS AND THE DECODER'S FAIL-CLOSED ENVELOPE, pinned as numbers: the
        // encoder clamps a real factor into [0.10×, 8.0×] (a legitimate sender can never emit a
        // rejectable code — the config dials' bind ranges sit INSIDE this envelope), the decoder
        // treats anything outside it (including 0) as neutral, never as a clamped extreme.
        t.Equal(1500, (int)NetProtocol.EncodeHeldStretch(1.5f), "1.5x encodes to 1500");
        t.Equal(NetProtocol.HeldStretchCodeMin, (int)NetProtocol.EncodeHeldStretch(0.01f),
                "an impossible tiny factor clamps UP to the envelope floor on the SENDER");
        t.Equal(NetProtocol.HeldStretchCodeMax, (int)NetProtocol.EncodeHeldStretch(64f),
                "an impossible huge factor clamps DOWN to the envelope ceiling on the SENDER");
        t.Equal(NetProtocol.HeldStretchCodeNeutral, (int)NetProtocol.EncodeHeldStretch(float.NaN),
                "NaN encodes to neutral — never trust a float either");
        t.True(NetProtocol.DecodeHeldStretch(1500) == 1.5f, "1500 decodes to exactly 1.5x");
        t.True(NetProtocol.DecodeHeldStretch(0) == 1f,
               "code 0 decodes to NEUTRAL on the RECEIVER — a zeroed record must not make a "
               + "figure invisible");
        t.True(NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMin - 1) == 1f
               && NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMax + 1) == 1f,
               "either side of the envelope decodes to neutral, never to a clamped extreme");

        // -- 7i3. SHARED ENVIRONMENT CLOCK (extension record 31) ---------------------------
        // USER REQUEST, verbatim: "Mond und Lichtstrahlen sollen im Multiplayer (falls beide
        // Spieler die selbe Umgebung ausgewählt haben) auch synchronisiert werden. Das gilt
        // generell für alle Effekt zB auch die Maus. Ich will das alle Spieler sie gleichzeitig
        // sehen (wenn die spieler es an haben)." — [style][u32 clockMillis LE]: the sender's
        // environment as a COMPARISON KEY plus the clock every _Time-driven effect of it runs on
        // (the rat = "die Maus", the drip and its puddle rings, the candle flicker, the canopy
        // sway, the shafts' shimmer). Written only while such an environment really stands.
        t.Case("7i3. extras, shared environment clock record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasEnvClock = true,
            EnvClockStyle = 1,          // SkyStyle.Cellar
            EnvClockMillis = 1234567u,  // 1234.567 s of shared clock
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            1F 05            // record: id 31 (shared environment clock), len 5
            01               // style 1 = Cellar
            87 D6 12 00      // clock 1234567 ms, u32 LE
            "), ext, m, "the clock record is [id 31][len 5][style][u32 millis LE]");
        t.Equal(18, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 5 = 18 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState env1), "and it parses");
        t.True(env1.HasEnvClock, "the clock record is delivered");
        t.Equal(1, (int)env1.EnvClockStyle, "with the style key intact");
        t.Equal(1234567L, (long)env1.EnvClockMillis, "and the millisecond reading intact");

        // The swamp, with every bit of the u32 set: the reading is an unsigned wall clock, so the
        // top byte must survive the round trip unsigned (a signed read would deliver -1 and the
        // follower would jump backwards by 49.7 days' worth of offset).
        m = PresenceSerializer.Write(new PresenceState
        {
            HasEnvClock = true,
            EnvClockStyle = 2,               // SkyStyle.SwampNight
            EnvClockMillis = 0xFFFFFFFFu,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            1F 05
            02               // style 2 = SwampNight
            FF FF FF FF      // clock 4294967295 ms — the u32 ceiling
            "), ext, m, "the swamp's clock rides the same 5-byte record");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState env2), "and it parses");
        t.Equal(2, (int)env2.EnvClockStyle, "the swamp style key is delivered");
        t.Equal(4294967295L, (long)env2.EnvClockMillis, "and the full u32 survives UNSIGNED");

        // ABSENT WHEN THERE IS NO ENVIRONMENT — the backward-compatibility argument for the sender
        // side: the game's own sky, OffBlack and MR all report style 0, and a 0 writes no record,
        // no tail and no block. Such a packet is byte-for-byte a pre-record-31 sender's.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasEnvClock = true,
            EnvClockStyle = 0,
            EnvClockMillis = 999u,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "style 0 -> no record, no tail, no block: byte-identical to build 137");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noEnv), "and it parses");
        t.True(!noEnv.HasEnvClock, "with HasEnvClock false (nothing to synchronise)");

        // FAIL-CLOSED TO ABSENCE on the receiver. A style code this build cannot name (here 3 —
        // OffBlack, which by design never writes; equally a future style from a newer peer) is
        // dropped WHOLE. "The same environment" — the user's own condition — must never be decided
        // by a code we do not understand, and the fallback is exactly the pre-record behaviour:
        // this client keeps its own clock. The record is still stepped over by its length, so the
        // records after it in the same tail arrive untouched.
        byte[] unknownEnv = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02         // two records in the tail
            1F 05 03 87 D6 12 00   // id 31, len 5, style 3 = unknown to this build
            1E 04 DC 05 E8 03      // id 30, len 4: a held stretch BEHIND the dropped record
            ");
        t.True(PresenceSerializer.TryRead(unknownEnv, unknownEnv.Length, out PresenceState badEnv),
               "a packet carrying an unknown environment style still parses");
        t.True(!badEnv.HasEnvClock,
               "the unknown style is dropped whole — the receiver keeps its own clock");
        t.True(badEnv.HasHeldStretch && badEnv.HeldStretchPrimaryCode == 1500,
               "and the record BEHIND it in the same tail is unharmed (skipped by length)");

        // READER SANITIZATION IS PER SLOT: a poisoned secondary must not cost the primary its
        // real factor. Hand-built packet: primary 0 (garbage), secondary 1500 (real).
        byte[] dirtyHs = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            1E 04
            00 00            // primary code 0 -> sanitized to neutral
            DC 05            // secondary 1500 -> kept
            ");
        t.True(PresenceSerializer.TryRead(dirtyHs, dirtyHs.Length, out PresenceState dirty),
               "a half-garbage stretch record still parses the packet");
        t.True(dirty.HasHeldStretch, "and is delivered");
        t.Equal(1000, (int)dirty.HeldStretchPrimaryCode, "with the garbage slot read as neutral");
        t.Equal(1500, (int)dirty.HeldStretchSecondaryCode, "and the sane slot intact");

        // BACKWARD COMPATIBILITY, the direction that actually ships: a peer built BEFORE record
        // 30 sees it as an unknown 4-byte record, steps over it by its length, and reads what it
        // does know behind it. (Same assertion shape as record 8's: unknown id 99 stands in for
        // how the old build's reader classifies id 30.)
        byte[] oldHsReader = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DC 05 E8 03            // id 99, len 4 -- record 30 as a PRE-RECORD-30 READER sees it
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldHsReader, oldHsReader.Length, out PresenceState oldHs),
               "a 4-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldHs.HasHeldStretch, "the unknown record delivers nothing (as on a pre-record-30 peer)");
        t.True(oldHs.HasModVersion, "and the record behind it is read past it");

        // ID ORDER: the stretch rides the tail LAST, behind the second-figure record it modifies.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondFigure = true, SecondFigureActorId = 0x01020304, SecondFigurePose = Figure(),
            SecondFigureLeftHand = true,
            HasHeldStretch = true, HeldStretchPrimaryCode = 2000, HeldStretchSecondaryCode = 1000,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            02               // tail: 2 records, in id order
            08 19 01 04 03 02 01
            " + PoseFigure + @"
            1E 04 D0 07 E8 03            // id 30 behind id 8; primary 2000 = 2.0x
            "), ext, m, "the stretch record rides the tail after the second figure (id order 8, 30)");

        // A TRUNCATED record (claims 4 payload bytes, delivers 2): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutHs = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 1E 04 DC 05");
        t.True(PresenceSerializer.TryRead(cutHs, cutHs.Length, out PresenceState cutStretch),
               "a truncated stretch record still parses the packet");
        t.True(cutStretch.HasMaskSize, "the mask size ahead of the tail survives");
        t.True(!cutStretch.HasHeldStretch, "and the incomplete record is simply not delivered");

        // -- 7j. BOARD TOOLTIP (extension record 9) ----------------------------------------
        // The tooltip parked in the sender's board tooltip area, UTF8, capped and truncated on
        // a CHARACTER boundary. Written only while a board-owned tooltip is shown AND the
        // sender-side identity gate passed (only content already public to peers); an idle
        // packet stays byte-identical to the previous build's.
        t.Case("7j. extras, board-tooltip record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTooltip = true, BoardTooltipText = "AB",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            09 02 41 42      // record: id 9, len 2, UTF8 'A' 'B'
            "), ext, m, "the board-tooltip record is [id][len][UTF8 bytes]");
        t.Equal(15, m, "header 7 + count 1 + block 2 + tail 1 + 4 = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState bt), "and it parses");
        t.True(bt.HasBoardTooltip, "the board-tooltip record is delivered");
        t.Equal("AB", bt.BoardTooltipText ?? string.Empty, "with the text intact");

        // Ordered LAST, behind the pick banner — a reorder in Write shows up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCardHighlight = true, HandHighlightIndex = 1,
            FanHighlightIndex = NetProtocol.CardHighlightNone,
            HasPickBanner = true, PickBannerText = "A",
            HasBoardTooltip = true, BoardTooltipText = "B",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            03               // tail: 3 records, in id order
            06 02 01 FF      // id 6 card highlight
            07 01 41         // id 7 pick banner, UTF8 'A'
            09 01 42         // id 9 board tooltip, UTF8 'B'
            "), ext, m, "the board tooltip rides the tail after the pick banner (id order 6, 7, 9)");

        // ... and behind the SECOND FIGURE too — id 9 is the new last record of the tail.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondFigure = true, SecondFigureActorId = 0x01020304, SecondFigurePose = Figure(),
            SecondFigureLeftHand = true,
            HasBoardTooltip = true, BoardTooltipText = "A",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            02               // tail: 2 records, in id order
            08 19 01 04 03 02 01
            " + PoseFigure + @"
            09 01 41         // id 9 board tooltip, UTF8 'A'
            "), ext, m, "the board tooltip rides the tail after the second figure (id order 8, 9)");

        // A multi-byte glyph survives the round trip intact (a German tooltip is full of them).
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTooltip = true, BoardTooltipText = "Rüstung",
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState btUml), "a UTF8 tooltip parses");
        t.Equal("Rüstung", btUml.BoardTooltipText ?? string.Empty, "and the umlaut survives byte-exact");

        // ABSENT WHEN NONE. An empty text emits NO record — "no tooltip", "identity gate
        // suppressed" and "sender predates the record" must all render identically, and an idle
        // packet stays byte-identical to the previous build's.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTooltip = true, BoardTooltipText = string.Empty, HandCardCount = 5,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty board tooltip writes no record at all");
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "no tooltip -> no block, no tail, no record: byte-identical to the previous build");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noBt), "and it parses");
        t.True(!noBt.HasBoardTooltip, "with HasBoardTooltip false (peers hide the panel)");

        // The cap truncates on a CHARACTER boundary: a run of 2-byte glyphs longer than the cap
        // must never be cut mid-sequence (that would decode as a replacement char on the peer).
        string longTip = new string('ä', NetProtocol.TooltipTextMaxBytes);
        byte[] cappedTip = PresenceSerializer.EncodeBoardTooltipText(longTip);
        t.True(cappedTip.Length <= NetProtocol.TooltipTextMaxBytes,
               "the encoded tooltip honours the cap");
        t.Equal(new string('ä', NetProtocol.TooltipTextMaxBytes / 2),
                System.Text.Encoding.UTF8.GetString(cappedTip),
                "and it truncated on a character boundary, not mid-glyph");
        t.True(NetProtocol.TooltipTextMaxBytes > NetProtocol.PickBannerTextMaxBytes,
               "the tooltip cap is wider than the pick line's (tooltips are longer)");
        t.True(NetProtocol.TooltipTextMaxBytes <= 255,
               "and still fits a single-byte TLV length");

        // OLD-READER SKIP-BY-LENGTH, both directions. (a) A pre-record-9 peer sees id 9 as the
        // unknown-id case: simulated with an unknown id carrying record 9's exact payload shape,
        // followed by a record every build since the handshake knows — read straight past it.
        byte[] oldReaderTip = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            02               // 2 records
            63 02 41 42      // id 99, len 2 -- record 9 as a PRE-RECORD-9 READER sees it
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReaderTip, oldReaderTip.Length, out PresenceState oldTip),
               "a 2-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldTip.HasBoardTooltip, "the unknown record delivers nothing (as on a pre-record-9 peer)");
        t.True(oldTip.HasModVersion, "and the record behind it is read past it");
        // (b) THIS reader steps over a future unknown record in FRONT of id 9 by its length and
        // still reads the tooltip behind it.
        byte[] futureTip = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            09 02 41 42      // id 9 board tooltip 'AB'
            ");
        t.True(PresenceSerializer.TryRead(futureTip, futureTip.Length, out PresenceState futTip),
               "a packet with an unknown record ahead of the board tooltip parses");
        t.True(futTip.HasBoardTooltip, "and the tooltip behind it is still read");
        t.Equal("AB", futTip.BoardTooltipText ?? string.Empty, "with its text intact");

        // A TRUNCATED tooltip record (claims 5 payload bytes, delivers 2): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutTip = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 09 05 41 42");
        t.True(PresenceSerializer.TryRead(cutTip, cutTip.Length, out PresenceState cutBt),
               "a truncated board-tooltip record still parses the packet");
        t.True(!cutBt.HasBoardTooltip, "and the incomplete record is simply not delivered");

        // -- 7k. SECOND HELD CARD (extension record 10) ------------------------------------
        // Since the both-hands ruling either hand can physically hold a card, but the rig
        // packet's FlagHeldCard block carries exactly ONE pose and its flag byte is full — so
        // build 49 deterministically sent the LEFT hand's card and the right one was invisible
        // to peers. That compromise is rejected (user ruling: "Wenn Karten in beiden Haenden
        // sind, soll das auch synchronisiert werden!"): the rig slot is byte-unchanged and the
        // OTHER hand's card rides the extras tail as [pose 20] — no hand byte (the receiver
        // renders the slab at the absolute transmitted pose, never parented to a hand) and no
        // identity, ever. Written only while TWO cards are held, one per hand.
        t.Case("7k. extras, second held-card record");
        m = PresenceSerializer.Write(ExtrasSecondHeldCard(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0A 14            // record: id 10 (second held card), len 20
            " + PoseCard + @"
            "), ext, m, "the second-held-card record is [id 10][len 20][pose] — pose only");
        t.Equal(33, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 20 = 33 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sc), "and it parses");
        t.True(sc.HasSecondHeldCard, "the second held card is delivered");
        t.True(sc.SecondHeldCardPose.Position == new Vector3(-1f, -2f, 0.5f),
               "with the exact shared-frame position — the same 20-byte encoding the rig " +
               "packet's held card uses");

        // ABSENT WHEN ONE OR ZERO CARDS ARE HELD. A one-card hold (either hand — the rig slot
        // carries it) and an idle player must emit the exact bytes build 49 emitted: no record,
        // no tail, no block. This is the whole backward-compatibility argument for the SENDER
        // side — a player not holding two cards is byte-for-byte a build-49 sender.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "at most one held card -> no record, no tail, no block: byte-identical to build 49");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noSc), "and it parses");
        t.True(!noSc.HasSecondHeldCard, "with HasSecondHeldCard false (peers drop only that slab)");

        // Ordered LAST, behind the second figure AND the board tooltip — record 10 is the new
        // last record of the tail (id order ... 8, 9, 10); a reorder in Write shows up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondFigure = true, SecondFigureActorId = 0x01020304, SecondFigurePose = Figure(),
            SecondFigureLeftHand = true,
            HasBoardTooltip = true, BoardTooltipText = "A",
            HasSecondHeldCard = true, SecondHeldCardPose = Card(),
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            03               // tail: 3 records, in id order
            08 19 01 04 03 02 01
            " + PoseFigure + @"
            09 01 41         // id 9 board tooltip, UTF8 'A'
            0A 14            // id 10 second held card, len 20
            " + PoseCard + @"
            "), ext, m, "the second held card rides the tail after records 8 and 9 (id order 8, 9, 10)");

        // BACKWARD COMPATIBILITY, the direction that actually ships: a peer built BEFORE
        // record 10 receives this packet. To that reader id 10 is exactly the unknown-id case,
        // so it steps over the record by its own length and everything it DOES know is
        // untouched. Asserted with a hand-built packet in which a 20-byte unknown record sits
        // where record 10 sits, followed by a record every build since the handshake knows.
        byte[] oldReaderCard = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            02               // 2 records
            63 14            // id 99, len 20 -- record 10 as a PRE-RECORD-10 READER sees it
            " + PoseCard + @"
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReaderCard, oldReaderCard.Length, out PresenceState oldSc),
               "a 20-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldSc.HasSecondHeldCard, "the unknown record delivers nothing (as on a pre-record-10 peer)");
        t.True(oldSc.HasModVersion, "and the record behind it is read past it");
        t.Equal((ushort)1, oldSc.ModBuild, "with its value intact");

        // ... and THIS reader steps over a future unknown record in FRONT of id 10 by its
        // length and still reads the second held card behind it.
        byte[] futureCard = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0A 14
            " + PoseCard + @"
            ");
        t.True(PresenceSerializer.TryRead(futureCard, futureCard.Length, out PresenceState futSc),
               "a packet with an unknown record ahead of the second held card parses");
        t.True(futSc.HasSecondHeldCard, "and the second held card behind it is still read");
        t.True(futSc.SecondHeldCardPose.Position == new Vector3(-1f, -2f, 0.5f),
               "with its pose intact");

        // A NaN position from a hostile/corrupt sender must not fling a slab out of the world —
        // the same guard the fan anchor and the second figure carry, for the same reason.
        byte[] nanCard = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0A 14
            0000C07F 000000C0 0000003F   // pos x = NaN
            0000 0000 0000 FF7F
            ");
        t.True(PresenceSerializer.TryRead(nanCard, nanCard.Length, out PresenceState nanSc),
               "a NaN second-held-card position still parses the packet");
        t.True(!nanSc.HasSecondHeldCard, "and the poisoned record is simply not delivered");

        // A TRUNCATED record (claims 20 payload bytes, delivers 4): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutCard = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 0A 14 00 00 80 BF");
        t.True(PresenceSerializer.TryRead(cutCard, cutCard.Length, out PresenceState cutSc),
               "a truncated second-held-card record still parses the packet");
        t.True(cutSc.HasMaskSize, "the mask size ahead of the tail survives");
        t.True(!cutSc.HasSecondHeldCard, "and the incomplete record is simply not delivered");

        // -- 7l. SLOT-CARD SIZE (extension record 11) --------------------------------------
        // The remote board hardcoded 0.0635 × 1.3 = 82.55 mm for a parked card and dropped the
        // owner's CardWidth config and [Cards] SlotOverlayScale_{board} (default 1.45!) entirely, so even two
        // default-configured clients disagreed by 31 % — the user's "Kartengröße nicht 1:1"
        // report. The record carries BOTH live widths (frame metric + card) in board-local
        // tenth-mm; absent = the legacy constant, which is exactly what pre-record peers render.
        t.Case("7l. extras, slot-card size record");
        m = PresenceSerializer.Write(ExtrasSlotCardSize(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0B 04            // record: id 11 (slot-card size), len 4
            84 03            // u16 LE 900  -> slot FRAME width 90.0 mm
            AD 04            // u16 LE 1197 -> slot CARD width 119.7 mm
            "), ext, m, "the slot-card size record is [id 11][len 4][u16 frame][u16 card], tenth-mm LE");
        t.Equal(17, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 4 = 17 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState scs), "and it parses");
        t.True(scs.HasSlotCardSize, "the slot-card size is delivered");
        t.Equal((ushort)900, scs.SlotFrameWidthCode, "with the frame width code intact");
        t.Equal((ushort)1197, scs.SlotCardWidthCode, "and the card width code intact");
        t.True(Mathf.Approximately(NetProtocol.DecodeSlotWidth(1197), 0.1197f),
               "decoding 1197 tenth-mm yields 119.7 mm — the shipped-default effective card width");

        // ABSENT AT THE LEGACY SIZES. A sender whose effective widths equal the pre-record
        // constant (0.0635 × 1.3) writes no record — HasSlotCardSize stays false at the driver —
        // so its packet is byte-identical to the previous build's. Serializer level: no flag, no
        // record, no tail.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 3 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 03"), ext, m,
               "legacy-sized sender -> no record, no tail, no block: byte-identical to the previous build");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noScs), "and it parses");
        t.True(!noScs.HasSlotCardSize, "with HasSlotCardSize false (peers keep the legacy 82.55 mm)");

        // Ordered LAST, behind the second held card — record 11 is the new last record of the
        // tail (id order ... 9, 10, 11); a reorder in Write shows up right here.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondHeldCard = true, SecondHeldCardPose = Card(),
            HasSlotCardSize = true, SlotFrameWidthCode = 900, SlotCardWidthCode = 1197,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            02               // tail: 2 records, in id order
            0A 14            // id 10 second held card, len 20
            " + PoseCard + @"
            0B 04 84 03 AD 04 // id 11 slot-card size
            "), ext, m, "the slot-card size rides the tail after record 10 (id order 10, 11)");

        // BACKWARD COMPATIBILITY, the direction that actually ships: a peer built BEFORE
        // record 11 receives this packet, sees an unknown 4-byte record where record 11 sits,
        // steps over it by its length and reads everything it DOES know — so it renders the
        // legacy card size (today's look) and nothing else changes.
        byte[] oldReaderSize = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            02               // 2 records
            63 04 84 03 AD 04            // id 99, len 4 -- record 11 as a PRE-RECORD-11 READER sees it
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReaderSize, oldReaderSize.Length, out PresenceState oldScs),
               "a 4-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldScs.HasSlotCardSize, "the unknown record delivers nothing (as on a pre-record-11 peer)");
        t.True(oldScs.HasModVersion, "and the record behind it is read past it");

        // ... and THIS reader steps over a future unknown record in FRONT of id 11 by its
        // length and still reads the slot-card size behind it.
        byte[] futureSize = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0B 04 84 03 AD 04
            ");
        t.True(PresenceSerializer.TryRead(futureSize, futureSize.Length, out PresenceState futScs),
               "a packet with an unknown record ahead of the slot-card size parses");
        t.True(futScs.HasSlotCardSize, "and the slot-card size behind it is still read");
        t.Equal((ushort)1197, futScs.SlotCardWidthCode, "with its value intact");

        // GARBAGE CODES (below the 5 mm floor) must not collapse a peer's cards to a sliver:
        // the pair is dropped WHOLE (a half-valid pair could split card and frame across two
        // builds' sizing), degrading to the legacy width.
        byte[] zeroSize = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0B 04 00 00 AD 04            // frame code 0 (< SlotWidthMinCode)
            ");
        t.True(PresenceSerializer.TryRead(zeroSize, zeroSize.Length, out PresenceState zeroScs),
               "a garbage slot-width code still parses the packet");
        t.True(!zeroScs.HasSlotCardSize, "and the poisoned pair is simply not delivered");

        // A TRUNCATED record (claims 4 payload bytes, delivers 2): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutSize = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 0B 04 84 03");
        t.True(PresenceSerializer.TryRead(cutSize, cutSize.Length, out PresenceState cutScs),
               "a truncated slot-card size record still parses the packet");
        t.True(cutScs.HasMaskSize, "the mask size ahead of the tail survives");
        t.True(!cutScs.HasSlotCardSize, "and the incomplete record is simply not delivered");

        // The RIG packet is untouched by all of this: the FIRST held card still rides its own
        // FlagHeldCard block at the same offsets — the sampler's left-first preference is
        // unchanged — which is why a pre-record-10 peer keeps seeing that one card exactly as
        // build 49 showed it.
        n = AvatarSerializer.Write(RigHeldFigureAndCard(), rig);
        t.Equal(77, n, "the rig packet's held-card block is byte-unchanged by the second slot");
        t.True(AvatarSerializer.TryRead(rig, n, out AvatarState rigCard), "and it parses");
        t.True(rigCard.HasHeldCard, "with the FIRST held card still delivered by the rig packet");
        t.True(rigCard.HeldCardPose.Position == new Vector3(-1f, -2f, 0.5f),
               "at the same pose bytes record 10 reuses");

        // -- 7l. HALF HOVER + SELECTION (extension record 14) ------------------------------
        // [byte0 hover][byte1 selection]: byte 0 is the transient pointer hover — board slot
        // (bits 0..1, sentinel 3 = no hover) + top-half bit; byte 1 the persistent CLICK state
        // — one 2-bit none/top/bottom field per slot (the game's steady half highlight after a
        // click, cleared by undo). Slot POSITIONS and halves, never a card identity. Written
        // while a half is hovered OR selected. (The record never shipped as 1 byte — the
        // 2-byte layout is its first wire form; ModBuild gates every peer to the same build.)
        t.Case("7l. extras, half hover + selection record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 1, HalfHoverTop = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0E 02 05 00      // id 14, len 2: hover slot 1 | top bit (0x04); no selection
            "), ext, m, "the record is [id 14][len 2][hover][selection] — positions only");
        t.Equal(15, m, "header 7 + count 1 + block 2 + tail 1 + 4 = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hh), "and it parses");
        t.True(hh.HasHalfHover && hh.HalfHoverActive, "the half hover is delivered");
        t.Equal(1, hh.HalfHoverSlot, "with the slot index intact");
        t.True(hh.HalfHoverTop, "and the TOP half named");
        t.Equal(NetProtocol.HalfSelectNone, hh.HalfSelect0, "slot 1 unselected");
        t.Equal(NetProtocol.HalfSelectNone, hh.HalfSelect1, "slot 2 unselected");

        // Hover on the bottom half of slot 0 is the all-zero hover byte — still delivered.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hhB), "slot0/bottom parses");
        t.True(hhB.HasHalfHover && hhB.HalfHoverActive && hhB.HalfHoverSlot == 0 && !hhB.HalfHoverTop,
               "slot 0 / bottom hover round-trips (byte 0 = 0x00 is a live hover, not the sentinel)");

        // SELECTION-ONLY (the follow-up defect: the CLICKED half, no pointer on the card):
        // byte 0 carries the no-hover sentinel, byte 1 both clicked halves.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true,
            HalfSelect0 = NetProtocol.HalfSelectTop,
            HalfSelect1 = NetProtocol.HalfSelectBottom,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            0E 02 03 09      // id 14: hover sentinel (slot field 3); sel slot0 TOP | slot1 BOTTOM<<2
            "), ext, m, "a selection-only record carries the no-hover sentinel in byte 0");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState selOnly), "and it parses");
        t.True(selOnly.HasHalfHover && !selOnly.HalfHoverActive,
               "delivered WITHOUT a hover — the sentinel reads as 'selection only'");
        t.Equal(NetProtocol.HalfSelectTop, selOnly.HalfSelect0, "slot 1's TOP half is clicked");
        t.Equal(NetProtocol.HalfSelectBottom, selOnly.HalfSelect1, "slot 2's BOTTOM half is clicked");

        // HOVER AND SELECTION TOGETHER (pointing at slot 0's bottom while slot 1's top is
        // committed) — both channels ride the same 2 bytes.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 0, HalfHoverTop = false,
            HalfSelect1 = NetProtocol.HalfSelectTop,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            0E 02 00 04      // id 14: hover slot 0 bottom; selection slot1 TOP (1<<2)
            "), ext, m, "hover and selection ride the record together");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hovSel), "and it parses");
        t.True(hovSel.HalfHoverActive && hovSel.HalfHoverSlot == 0 && !hovSel.HalfHoverTop,
               "with the hover intact");
        t.Equal(NetProtocol.HalfSelectTop, hovSel.HalfSelect1, "and the click intact");

        // No hover and no selection ⇒ no record, no tail, no block: byte-identical to the
        // previous build (the writer refuses an all-empty record).
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "nothing lit, nothing clicked -> no record: byte-identical to a pre-record-14 sender");

        // INVALID HOVER SLOT (2 — a recess the two-slot board does not have): the hover is
        // rejected; a valid selection in the same record still lands.
        byte[] badSlot = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 06 01"); // hover slot 2|top; sel0 TOP
        t.True(PresenceSerializer.TryRead(badSlot, badSlot.Length, out PresenceState hhBad),
               "a record naming hover slot 2 still parses the packet");
        t.True(!hhBad.HalfHoverActive, "the impossible hover is simply not delivered");
        t.True(hhBad.HasHalfHover && hhBad.HalfSelect0 == NetProtocol.HalfSelectTop,
               "while the valid selection beside it survives");

        // INVALID SELECTION VALUE 3 in a field: reads as none (never trust the wire); with the
        // hover also absent the whole record degrades to 'absent'.
        byte[] badSel = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 03 0F"); // sentinel hover; sel0=3, sel1=3
        t.True(PresenceSerializer.TryRead(badSel, badSel.Length, out PresenceState selBad),
               "a record whose selection fields are both the invalid 3 still parses");
        t.True(!selBad.HasHalfHover, "and delivers nothing — value 3 is none, none+none = absent");

        // A TRUNCATED record (claims 2 payload bytes, delivers 1): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutHalf = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 05");
        t.True(PresenceSerializer.TryRead(cutHalf, cutHalf.Length, out PresenceState cutHh),
               "a truncated half record still parses the packet");
        t.True(!cutHh.HasHalfHover, "and the incomplete record is simply not delivered");

        // An OLD reader steps over id 14 by its length (asserted as the unknown-id case) and a
        // NEW reader steps over an unknown record ahead of it.
        byte[] futureHalf = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0E 02 04 00      // id 14: hover slot 0 top, no selection
            ");
        t.True(PresenceSerializer.TryRead(futureHalf, futureHalf.Length, out PresenceState futHh),
               "a packet with an unknown record ahead of the half record parses");
        t.True(futHh.HasHalfHover && futHh.HalfHoverActive && futHh.HalfHoverTop,
               "and the half hover behind it is read");

        // -- 7l2. CAP PRESS (record 14 byte 0 bits 3..7) -----------------------------------
        // The ONE keycap animation that is not a function of already-synced state: the press dip.
        // Five bits of a byte that record 14 was already reserving — zero extra bytes — carrying
        // WHICH cap plus a 2-bit sequence, because the field is a LATCH that rides several packets
        // and a receiver must animate it CHANGING rather than being set.
        t.Case("7l2. extras, keycap press edge");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCapPress = true, CapPressCap = NetProtocol.CapPressLongRest, CapPressSeq = 2,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            0E 02            // record: id 14, len 2
            AB               // byte0 = 0xAB: hover slot field 3 (= NO hover), top bit clear,
                             //   cap id 5 (LONG rest) at bits 3..5, sequence 2 at bits 6..7
            00               // byte1: no half selected
            "), ext, m, "a press rides record 14 ALONE — no hover, no selection, no extra byte");
        t.Equal(15, m, "the same 15 bytes a hover-only record 14 costs — the press is free");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cp), "and it parses");
        t.True(cp.HasCapPress, "the press is delivered");
        t.Equal(NetProtocol.CapPressLongRest, cp.CapPressCap, "with the cap id intact");
        t.Equal((byte)2, cp.CapPressSeq, "and the sequence intact");
        t.True(!cp.HasHalfHover,
               "and it does NOT invent a half hover: a press-only record decodes to a press only");

        // A press RIDING ALONGSIDE a live hover and selection — the three fields share byte 0/1 and
        // must not bleed into each other. This is the packet a player produces by pressing CONFIRM
        // while their beam still rests on a card half.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 1, HalfHoverTop = true,
            HalfSelect0 = NetProtocol.HalfSelectBottom,
            HasCapPress = true, CapPressCap = NetProtocol.CapPressConfirm, CapPressSeq = 1,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0E 02            // id 14, len 2
            4D               // byte0 = 0x4D: slot 1 (0x01) | TOP half (0x04) | cap id 1 (CONFIRM)
                             //   at bits 3..5 (0x08) | sequence 1 at bits 6..7 (0x40)
            02               // byte1: slot 0 selected BOTTOM
            "), ext, m, "hover, selection and press share record 14 without touching each other");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState mix), "and it parses");
        t.True(mix.HalfHoverActive && mix.HalfHoverSlot == 1 && mix.HalfHoverTop,
               "the hover survives beside the press");
        t.Equal(NetProtocol.HalfSelectBottom, mix.HalfSelect0, "so does the selection");
        t.True(mix.HasCapPress && mix.CapPressCap == NetProtocol.CapPressConfirm
               && mix.CapPressSeq == 1, "and the press is read out of the same byte");

        // THE SENTINEL IS ZERO, and this is the assertion that pins why. A PRE-PRESS record 14 —
        // one every build up to 88 wrote, hover only, bits 3..7 clear — must deliver NO press. Had
        // the cap ids started at 0, those clear bits would have read as "the CONFIRM cap was
        // pressed" and every old peer's board would have twitched on its first hover.
        byte[] prePress = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 04 00");
        t.True(PresenceSerializer.TryRead(prePress, prePress.Length, out PresenceState pp),
               "a pre-press record 14 still parses");
        t.True(pp.HasHalfHover && pp.HalfHoverActive && pp.HalfHoverSlot == 0 && pp.HalfHoverTop,
               "its hover is delivered unchanged");
        t.True(!pp.HasCapPress,
               "and NO press is claimed from its cleared reserved bits — the receiver animates " +
               "nothing, which is exactly what those builds' boards did");
        t.Equal((byte)NetProtocol.CapPressNone, (byte)0,
                "because the 'no press' sentinel IS zero, and the seven cap ids run 1..7");

        // Every defined cap id survives its own round trip — a renumbering, or a mask that clips
        // the top id, fails here rather than silently animating the wrong cap on a peer's board.
        for (byte capId = NetProtocol.CapPressConfirm; capId <= NetProtocol.CapPressMaxId; capId++)
        {
            m = PresenceSerializer.Write(new PresenceState
            {
                HasCapPress = true, CapPressCap = capId, CapPressSeq = 3,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, m, out PresenceState one)
                   && one.HasCapPress && one.CapPressCap == capId && one.CapPressSeq == 3,
                   $"cap id {capId} survives the round trip with its sequence");
        }
        t.Equal((byte)7, NetProtocol.CapPressMaxId,
                "seven cap ids in three bits with zero reserved is an EXACT fit — an eighth cap " +
                "needs a new field, not a renumbering of these");
        t.Equal((byte)0xF8, NetProtocol.HalfHoverDefinedMask & 0xF8,
                "and byte 0 is now FULL: slot, top-half, cap id and sequence leave no reserved bit");

        // -- 7m. PILE COUNTS (extension record 15) -----------------------------------------
        // The numbers the sender's own three stack labels display ([discard][burnt][items]).
        // On the wire because the receiver's model read is NOT timely (session logs: the
        // discarding player's board read 2 while every observer's model still derived 0 for
        // the rest of the turn); public info, no identity — three integers.
        t.Case("7m. extras, pile-counts record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPileCounts = true, PileDiscardCount = 2, PileBurntCount = 1, PileItemsCount = 7,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            0F 03 02 01 07   // id 15 (pile counts), len 3: discard 2, burnt 1, items 7
            "), ext, m, "the pile-counts record is [id 15][len 3][discard][burnt][items]");
        t.Equal(16, m, "header 7 + count 1 + block 2 + tail 1 + 5 = 16 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState pc), "and it parses");
        t.True(pc.HasPileCounts, "the counts are delivered");
        t.Equal(2, pc.PileDiscardCount, "discard intact");
        t.Equal(1, pc.PileBurntCount, "burnt intact");
        t.Equal(7, pc.PileItemsCount, "items intact");

        // ALL-ZERO COUNTS still ship (the board-UI presence contract): "present, all empty"
        // must be distinguishable from "sender predates the field".
        m = PresenceSerializer.Write(new PresenceState { HasPileCounts = true }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState pcZero), "all-zero counts parse");
        t.True(pcZero.HasPileCounts, "and are DELIVERED as authoritative zeros, not as absence");

        // No stacks displayed ⇒ no record: byte-identical to a pre-record-15 sender, whose
        // receivers keep the legacy model-read counts.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 3 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 03"), ext, m,
               "no displayed stacks -> no record: byte-identical to a pre-record-15 sender");

        // A TRUNCATED record (claims 3 payload bytes, delivers 1): the tail is abandoned
        // mid-record, everything parsed before it survives, nothing throws.
        byte[] cutCounts = Hex.Bytes("31 52 56 47 03 01 80 00 90 00 C8 01 0F 03 02");
        t.True(PresenceSerializer.TryRead(cutCounts, cutCounts.Length, out PresenceState cutPc),
               "a truncated pile-counts record still parses the packet");
        t.True(!cutPc.HasPileCounts, "and the incomplete record is simply not delivered");
        t.True(cutPc.HasMaskSize, "while the mask size ahead of the tail survives");

        // -- 7n. TRACK HOVER (extension record 16) -----------------------------------------
        // The initiative-track entry the sender hovers, by stable CActor.ID (the track's
        // display order is per-client during the online selection phase, so an index would
        // lift the wrong portrait); flags bit 0 = the entry's info popup is open.
        t.Case("7n. extras, track-hover record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackHover = true, TrackHoverActorId = 0x01020304, TrackHoverPopup = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            10 05            // id 16 (track hover), len 5
            01               // flags: popup open
            04 03 02 01      // actor id LE
            "), ext, m, "the track-hover record is [id 16][len 5][flags][actorId LE]");
        t.Equal(18, m, "header 7 + count 1 + block 2 + tail 1 + 7 = 18 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState th), "and it parses");
        t.True(th.HasTrackHover, "the track hover is delivered");
        t.Equal(0x01020304, th.TrackHoverActorId, "with the stable actor id intact");
        t.True(th.TrackHoverPopup, "and the popup-open flag set");

        // No hover ⇒ no record: byte-identical to a pre-record-16 sender.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 2 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 02"), ext, m,
               "no hover -> no record: byte-identical to a pre-record-16 sender");

        // ACTOR ID 0 is "none" everywhere in this system and can never name an entry — the
        // WRITER refuses to emit it and the READER refuses to deliver it (both directions,
        // because neither side may trust the other).
        m = PresenceSerializer.Write(new PresenceState { HasTrackHover = true }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "a zero actor id is never written — the packet stays the idle packet");
        byte[] zeroActor = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 10 05 01 00 00 00 00");
        t.True(PresenceSerializer.TryRead(zeroActor, zeroActor.Length, out PresenceState thZero),
               "a hand-built zero-actor record still parses the packet");
        t.True(!thZero.HasTrackHover, "and is simply not delivered");

        // -- 7o. WALL FADES (extension record 17) ------------------------------------------
        // The sender's currently-faded wall set by cross-machine stable key (FNV-1a over
        // anchor name + CMap room label + quantized anchor XZ — see the record doc). Sorted,
        // capped at 24; the receiver's [WallFade] SyncPeerFades decides application.
        t.Case("7o. extras, wall-fades record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasWallFades = true, WallFadesCount = 2,
            WallFadesKeys = new uint[] { 0x01020304u, 0xA1B2C3D4u },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            11 09            // id 17 (wall fades), len 1+4n = 9
            02               // count 2
            04 03 02 01      // key 0 LE
            D4 C3 B2 A1      // key 1 LE
            "), ext, m, "the wall-fades record is [id 17][len 1+4n][count][n x u32 key LE]");
        t.Equal(22, m, "header 7 + count 1 + block 2 + tail 1 + 11 = 22 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState wf), "and it parses");
        t.True(wf.HasWallFades, "the wall-fade set is delivered");
        t.Equal(2, wf.WallFadesCount, "with both keys");
        t.True(wf.WallFadesKeys != null && wf.WallFadesKeys.Length == 2
               && wf.WallFadesKeys[0] == 0x01020304u && wf.WallFadesKeys[1] == 0xA1B2C3D4u,
               "and the key values intact");

        // An EMPTY set writes no record: byte-identical to a pre-record-17 sender.
        m = PresenceSerializer.Write(new PresenceState { HasWallFades = true }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "an empty wall-fade set is never written — the packet stays the idle packet");

        // NEVER TRUST THE WIRE: a count byte claiming more keys than the record LENGTH holds
        // is clamped to what actually fits — here count says 5 but len 5 fits exactly one key.
        byte[] overCount = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 11 05 05 AA BB CC DD");
        t.True(PresenceSerializer.TryRead(overCount, overCount.Length, out PresenceState wfOver),
               "a hand-built over-count record still parses the packet");
        t.True(wfOver.HasWallFades && wfOver.WallFadesCount == 1
               && wfOver.WallFadesKeys != null && wfOver.WallFadesKeys[0] == 0xDDCCBBAAu,
               "and delivers exactly the keys the length can hold");

        // A zero-count record degrades to "record absent" — no peer wall fades.
        byte[] zeroWalls = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 11 01 00");
        t.True(PresenceSerializer.TryRead(zeroWalls, zeroWalls.Length, out PresenceState wfZero),
               "a hand-built zero-count record still parses the packet");
        t.True(!wfZero.HasWallFades, "and is simply not delivered");

        // The WRITER cap: more keys than WallFadesMaxKeys are truncated to the cap (the
        // sender logs the truncation; the first 24 sorted keys still ride).
        var manyKeys = new uint[30];
        for (int k = 0; k < manyKeys.Length; k++)
            manyKeys[k] = (uint)(k + 1);
        m = PresenceSerializer.Write(new PresenceState
        {
            HasWallFades = true, WallFadesCount = 30, WallFadesKeys = manyKeys,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState wfCap), "a capped set parses");
        t.Equal(NetProtocol.WallFadesMaxKeys, wfCap.WallFadesCount,
                "and delivers exactly the 24-key cap");

        // ID ORDER of the new tail region: records 10, 14, 15, 16 in that order — a reorder in
        // Write shows up right here, and this is also the OLD-READER vector (to a pre-batch
        // peer, ids 14/15/16 are unknown records it steps over by length).
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondHeldCard = true, SecondHeldCardPose = Card(),
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 0, HalfHoverTop = true,
            HasPileCounts = true, PileDiscardCount = 1,
            HasTrackHover = true, TrackHoverActorId = 0x11,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            04               // tail: 4 records, in id order
            0A 14            // id 10 second held card
            " + PoseCard + @"
            0E 02 04 00      // id 14 half hover: slot 0, top; no selection
            0F 03 01 00 00   // id 15 pile counts: 1/0/0
            10 05 00 11 00 00 00 // id 16 track hover: no popup, actor 0x11
            "), ext, m, "the batch's records ride the tail after record 10, in id order 14, 15, 16");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState combo), "and the combo parses");
        t.True(combo.HasSecondHeldCard && combo.HasHalfHover && combo.HasPileCounts
               && combo.HasTrackHover, "with all four records delivered");

        // -- 7o. DECISION LINES (extension record 12) --------------------------------------
        // The board-UI record's decision bit only says THAT a prompt is docked; peers drew a
        // generic empty drawer. The docked widgets are LOCAL UI on the deciding player's client,
        // so their BUTTON LABELS ride the tail as one '\n'-joined UTF8 blob — pressable-widget
        // labels only, never a dialog's description text (which can name a card).
        t.Case("7o. extras, decision-lines record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = "Ja\nNein",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0C 07            // record: id 12 (decision lines), len 7
            4A 61 0A 4E 65 69 6E   // UTF8 'Ja\nNein' -- one label per line
            "), ext, m, "the decision-lines record is [id 12][len][UTF8 '\\n'-joined labels]");
        t.Equal(20, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 7 = 20 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState dl), "and it parses");
        t.True(dl.HasDecisionLines, "the decision lines are delivered");
        t.Equal("Ja\nNein", dl.DecisionLinesText ?? string.Empty, "byte-exact, lines intact");

        // ABSENT WHEN NO ROW IS DOCKED: empty text emits no record, no tail, no block — an idle
        // packet stays byte-identical to the previous build's.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = string.Empty, HandCardCount = 5,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty decision-lines text writes no record at all");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noDl), "and it parses");
        t.True(!noDl.HasDecisionLines, "with HasDecisionLines false (peers show the plain drawer)");

        // BYTE CAP, character boundary: a run of 2-byte glyphs longer than the cap must never be
        // cut mid-sequence.
        string longLines = new string('ö', NetProtocol.DecisionLinesMaxBytes);
        byte[] cappedLines = PresenceSerializer.EncodeDecisionLines(longLines);
        t.True(cappedLines.Length <= NetProtocol.DecisionLinesMaxBytes,
               "the encoded decision lines honour the cap");
        t.Equal(new string('ö', NetProtocol.DecisionLinesMaxBytes / 2),
                System.Text.Encoding.UTF8.GetString(cappedLines),
                "and truncated on a character boundary, not mid-glyph");
        t.True(NetProtocol.DecisionLinesMaxBytes <= 255, "the cap fits a single-byte TLV length");

        // OLD-READER SKIP-BY-LENGTH + future-record-in-front, the standard TLV pair.
        byte[] oldReaderDl = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00            // byte A extension tail, byte B count 0
            02               // 2 records
            63 07 4A 61 0A 4E 65 69 6E   // id 99, len 7 -- record 12 as a PRE-RECORD-12 READER sees it
            03 07 01 00 30 2E 31 2E 30   // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReaderDl, oldReaderDl.Length, out PresenceState oldDl),
               "a 7-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldDl.HasDecisionLines, "the unknown record delivers nothing (pre-record-12 peer)");
        t.True(oldDl.HasModVersion, "and the record behind it is read past it");
        byte[] futureDl = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0C 02 4A 61      // id 12 decision lines 'Ja'
            ");
        t.True(PresenceSerializer.TryRead(futureDl, futureDl.Length, out PresenceState futDl),
               "a packet with an unknown record ahead of the decision lines parses");
        t.True(futDl.HasDecisionLines, "and the decision lines behind it are still read");
        t.Equal("Ja", futDl.DecisionLinesText ?? string.Empty, "with their text intact");

        // TRUNCATED record (claims 5 payload bytes, delivers 2): tail abandoned, nothing throws.
        byte[] cutDl = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0C 05 4A 61");
        t.True(PresenceSerializer.TryRead(cutDl, cutDl.Length, out PresenceState cutDlS),
               "a truncated decision-lines record still parses the packet");
        t.True(!cutDlS.HasDecisionLines, "and the incomplete record is simply not delivered");

        // -- 7o2. DECISION STATE (extension record 23) --------------------------------------
        // Record 12 says WHAT the owner's decision options read; this one says everything about
        // them that is not a word — which prompt is docked, which prompt-TEXT variant the owner is
        // reading, and per option offered / dimmed / chosen. The text itself is NOT here on
        // purpose: the receiver composes it from its own localization, because the game's
        // mandatory-use branch embeds ACTIVE-BONUS CARD NAMES in the sentence and no card identity
        // may ever ride this wire.
        t.Case("7o2. extras, decision-state record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = "Ja",
            HasDecisionState = true,
            DecisionPromptKind = NetProtocol.DecisionKindTakeDamage,
            DecisionTextVariant = NetProtocol.DecisionTextDealDamage,
            DecisionOptionCount = 3,
            DecisionOptionFlags = new byte[]
            {
                NetProtocol.DecisionOptionOfferedBit,                                  // pressable
                NetProtocol.DecisionOptionDimmedBit,                                   // greyed + dim
                (byte)(NetProtocol.DecisionOptionOfferedBit | NetProtocol.DecisionOptionChosenBit),
            },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            02               // tail: 2 records
            0C 02 4A 61      // id 12 decision lines: 'Ja'
            18 05            // id 24 (decision state), len 5
            09               // flags: kind 1 (take damage) | variant 1 (deal damage) << 3
            03               // 3 options
            01 02 05         // #0 offered, #1 dimmed, #2 offered+chosen
            "), ext, m, "the decision-state record is [id 24][len][flags][n][n option bytes], "
                        + "written AFTER record 12 in id order");
        t.Equal(22, m, "header 7 + count 1 + block 2 + tail 1 + (2+2) + (2+5) = 22 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ds), "and it parses");
        t.True(ds.HasDecisionState, "the decision state is delivered");
        t.Equal(NetProtocol.DecisionKindTakeDamage, ds.DecisionPromptKind, "prompt kind intact");
        t.Equal(NetProtocol.DecisionTextDealDamage, ds.DecisionTextVariant, "text variant intact");
        t.Equal(3, ds.DecisionOptionCount, "all three option states are delivered");
        t.Equal(NetProtocol.DecisionOptionOfferedBit, ds.DecisionOptionFlags![0],
                "option 0 is OFFERED — the receiver draws a live plate");
        t.Equal(NetProtocol.DecisionOptionDimmedBit, ds.DecisionOptionFlags[1],
                "option 1 is greyed AND dimmed — two separate axes, not collapsed into one");
        t.True((ds.DecisionOptionFlags[2] & NetProtocol.DecisionOptionChosenBit) != 0,
               "option 2 carries the CHOSEN bit (the accent frame on the peer's plate)");

        // KIND AND VARIANT SHARE ONE BYTE and must not bleed into each other: the highest defined
        // variant with the highest defined kind still decodes as the pair that was written.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionState = true,
            DecisionPromptKind = NetProtocol.DecisionKindDialogPopup,       // 3
            DecisionTextVariant = NetProtocol.DecisionTextMandatoryUse,     // 5
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            18 02            // id 24, len 2
            2B               // flags: kind 3 | variant 5 << 3 = 0x03 | 0x28
            00               // no options
            "), ext, m, "kind and variant pack into one byte without spilling into each other");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState kv), "and it parses");
        t.Equal(NetProtocol.DecisionKindDialogPopup, kv.DecisionPromptKind, "kind 3 survives");
        t.Equal(NetProtocol.DecisionTextMandatoryUse, kv.DecisionTextVariant, "variant 5 survives");
        t.Equal(0, kv.DecisionOptionCount, "and an option-less record is legal (no plates to state)");

        // NOTHING TO STATE ⇒ NO RECORD, NO TAIL, NO BLOCK: an idle packet stays byte-identical to
        // the previous build's — the same contract every "only when non-default" record honours.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionState = true, HandCardCount = 5,
            DecisionPromptKind = NetProtocol.DecisionKindNone,
            DecisionTextVariant = NetProtocol.DecisionTextNone,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty decision state writes no record at all");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noDs), "and it parses");
        t.True(!noDs.HasDecisionState, "with HasDecisionState false (peers keep the plain plates)");

        // MASKED ON WRITE AND ON READ: a sender that sets bits this build does not define must not
        // light a meaning here, and must not corrupt the fields beside them.
        byte[] wildDs = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            18 03            // id 24, len 3
            C9               // flags: reserved bits 6+7 set, on top of kind 1 | variant 1
            01               // 1 option
            F9               // option: reserved bits 3..7 set, on top of OFFERED
            ");
        t.True(PresenceSerializer.TryRead(wildDs, wildDs.Length, out PresenceState wds),
               "a record with undefined bits still parses");
        t.Equal(NetProtocol.DecisionKindTakeDamage, wds.DecisionPromptKind,
                "the kind is read through the mask, unaffected by the reserved bits");
        t.Equal(NetProtocol.DecisionTextDealDamage, wds.DecisionTextVariant,
                "and so is the variant");
        t.Equal(NetProtocol.DecisionOptionOfferedBit, wds.DecisionOptionFlags![0],
                "every undefined option bit is masked away");

        // A LYING COUNT can neither overrun the record nor bleed into the next one: n is re-clamped
        // against the record's OWN length, and the record behind it still reads.
        byte[] lyingDs = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02
            18 04 09 FF 01 02          // id 24, len 4: claims 255 options, carries 2
            03 07 01 00 30 2E 31 2E 30 // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(lyingDs, lyingDs.Length, out PresenceState liar),
               "a decision-state record claiming more options than it carries still parses");
        t.Equal(2, liar.DecisionOptionCount, "the count is clamped to what the record really holds");
        t.True(liar.HasModVersion, "and the record behind it is read past it, undamaged");

        // OVER-CAP: a sender offering more options than the cap is clamped on read, so a peer can
        // never be made to allocate or draw past the record's own bound.
        byte[] overCap = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            18 0D 09 0C 01 01 01 01 01 01 01 01 01 01 01 01
            ");
        t.True(PresenceSerializer.TryRead(overCap, overCap.Length, out PresenceState dsCapped),
               "an over-cap decision-state record parses");
        t.Equal(NetProtocol.DecisionStateMaxOptions, dsCapped.DecisionOptionCount,
                "with the option count clamped to DecisionStateMaxOptions");

        // OLD-STYLE PACKET (no record 23 at all — a build-86 sender): the decision LINES still
        // arrive, and the receiver derives "no states, no prompt line", which is exactly the look
        // every build before this one drew.
        byte[] oldStyleDs = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0C 07 4A 61 0A 4E 65 69 6E   // id 12 only: 'Ja\nNein'
            ");
        t.True(PresenceSerializer.TryRead(oldStyleDs, oldStyleDs.Length, out PresenceState preDs),
               "a pre-record-23 packet parses");
        t.True(preDs.HasDecisionLines, "its decision lines are delivered");
        t.True(!preDs.HasDecisionState, "no decision state is invented");
        t.Equal(NetProtocol.DecisionKindNone, preDs.DecisionPromptKind,
                "the derived prompt kind is None — the receiver draws no prompt line");
        t.True(preDs.DecisionOptionFlags == null,
               "and no option states — every mirrored plate keeps the plain look");

        // TRUNCATED record (claims 5 payload bytes, delivers 1): tail abandoned, nothing thrown.
        byte[] cutDs = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 18 05 09");
        t.True(PresenceSerializer.TryRead(cutDs, cutDs.Length, out PresenceState cutDsS),
               "a truncated decision-state record still parses the packet");
        t.True(!cutDsS.HasDecisionState, "and the incomplete record is simply not delivered");

        // -- 7o2b. DECISION WIDGETS (extension record 29) -------------------------------------
        // Record 12 says what the owner's options READ and record 24 what STATE they are in; this
        // one says WHICH GAME WIDGET each option IS, so a peer can clone the real button out of its
        // own copy of TakeDamagePanel (the game raises that Singleton on every client) instead of
        // drawing a mod-built lookalike from our text. Plus the two numbers that widget paints on
        // itself and a peer's stale copy cannot know: the live damage amount and whether it is
        // lethal / shielded.
        //
        // A ROLE IS A WIDGET NAME, NEVER A CARD. The whole payload is three small enumerations and
        // a damage number — there is nothing in it that could name what would burn.
        t.Case("7o2b. extras, decision-widgets record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = "Ja",
            HasDecisionWidgets = true,
            DecisionWidgetFlags = (byte)(NetProtocol.DecisionWidgetLethalBit
                                         | NetProtocol.DecisionWidgetDamageValidBit),
            DecisionDamageAmount = 4,
            DecisionRoleCount = 3,
            DecisionRoles = new byte[]
            {
                NetProtocol.DecisionRoleBurnAvailable,   // 1
                NetProtocol.DecisionRoleTakeDamage,      // 3
                NetProtocol.DecisionRoleBurnDiscarded,   // 2
            },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            02               // tail: 2 records
            0C 02 4A 61      // id 12 decision lines: 'Ja'
            1D 06            // id 29 (decision widgets), len 6
            09               // flags: lethal (0x01) | damage-valid (0x08)
            04               // damage amount 4
            03               // 3 roles
            01 03 02         // burn-available, take-damage, burn-discarded
            "), ext, m, "the decision-widgets record is [id 29][len][flags][damage][n][n roles], "
                        + "written LAST, behind every existing record, in id order");
        t.Equal(23, m, "header 7 + count 1 + block 2 + tail 1 + (2+2) + (2+6) = 23 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState dw), "and it parses");
        t.True(dw.HasDecisionWidgets, "the widget record is delivered");
        t.Equal(4, dw.DecisionDamageAmount, "the damage number the owner's button displays");
        t.True((dw.DecisionWidgetFlags & NetProtocol.DecisionWidgetLethalBit) != 0,
               "the FATAL-damage icon flag survives (the receiver swaps its own icon, not a bitmap)");
        t.True((dw.DecisionWidgetFlags & NetProtocol.DecisionWidgetDamageValidBit) != 0,
               "…and the damage-valid flag, without which the receiver leaves its own number alone");
        t.True((dw.DecisionWidgetFlags & NetProtocol.DecisionWidgetShieldedBit) == 0,
               "the shield flag was not set and is not invented");
        t.Equal(3, dw.DecisionRoleCount, "all three roles are delivered");
        t.Equal(NetProtocol.DecisionRoleBurnAvailable, dw.DecisionRoles![0],
                "option 0 is the burn-one-available-card toggle");
        t.Equal(NetProtocol.DecisionRoleTakeDamage, dw.DecisionRoles[1],
                "option 1 is the take-damage button — index-aligned with records 12 and 24");
        t.Equal(NetProtocol.DecisionRoleBurnDiscarded, dw.DecisionRoles[2],
                "option 2 is the burn-two-discarded toggle");

        // NOTHING TO NAME ⇒ NO RECORD, NO TAIL, NO BLOCK: an idle packet stays byte-identical to
        // the previous build's — the same contract every "only when non-default" record honours.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionWidgets = true, HandCardCount = 5,
            DecisionWidgetFlags = 0, DecisionRoleCount = 0,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty decision-widgets record writes no record at all");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noDw), "and it parses");
        t.True(!noDw.HasDecisionWidgets,
               "with HasDecisionWidgets false (peers keep the mod-drawn plates)");

        // MASKED ON WRITE AND ON READ, and ROLES CLAMPED: a sender that sets bits this build does
        // not define must not light a meaning here, and a role code this build cannot resolve must
        // degrade to 'unknown' (the mod-drawn plate) rather than to the WRONG widget.
        byte[] wildDw = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            1D 05            // id 29, len 5
            F9               // flags: undefined bits 4..7 set, on top of lethal|damage-valid
            07               // damage 7
            02               // 2 roles
            03 7F            // take-damage, then a role code from a later build
            ");
        t.True(PresenceSerializer.TryRead(wildDw, wildDw.Length, out PresenceState wdw),
               "a record with undefined bits and an unknown role still parses");
        t.Equal((byte)(NetProtocol.DecisionWidgetLethalBit | NetProtocol.DecisionWidgetDamageValidBit),
                wdw.DecisionWidgetFlags, "every undefined flag bit is masked away");
        t.Equal(NetProtocol.DecisionRoleTakeDamage, wdw.DecisionRoles![0], "the known role survives");
        t.Equal(NetProtocol.DecisionRoleUnknown, wdw.DecisionRoles[1],
                "and the unknown one becomes 'unknown' — a peer draws its plate, never a guessed widget");

        // A LYING COUNT can neither overrun the record nor bleed into the next one: n is re-clamped
        // against the record's OWN length, and the record behind it still reads.
        byte[] lyingDw = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02
            1D 05 08 04 FF 01 03       // id 29, len 5: claims 255 roles, carries 2
            03 07 01 00 30 2E 31 2E 30 // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(lyingDw, lyingDw.Length, out PresenceState liarDw),
               "a decision-widgets record claiming more roles than it carries still parses");
        t.Equal(2, liarDw.DecisionRoleCount, "the count is clamped to what the record really holds");
        t.True(liarDw.HasModVersion, "and the record behind it is read past it, undamaged");

        // OVER-CAP: a sender offering more roles than the cap is clamped on read, so a peer can
        // never be made to allocate or resolve past the record's own bound.
        byte[] overCapDw = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            1D 0F 08 04 0C 01 01 01 01 01 01 01 01 01 01 01 01
            ");
        t.True(PresenceSerializer.TryRead(overCapDw, overCapDw.Length, out PresenceState dwCapped),
               "an over-cap decision-widgets record parses");
        t.Equal(NetProtocol.DecisionStateMaxOptions, dwCapped.DecisionRoleCount,
                "with the role count clamped to DecisionStateMaxOptions (records 24 and 29 share it)");

        // OLD-STYLE PACKET (records 12 + 24, no record 29 — a ModBuild-104 sender): the wordings and
        // the states still arrive, and the receiver derives "no roles", which makes it fall back to
        // the mod-drawn plates — exactly the look every build before this one drew.
        byte[] oldStyleDw = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02
            0C 07 4A 61 0A 4E 65 69 6E   // id 12: 'Ja\nNein'
            18 04 09 02 01 01            // id 24: kind 1 | variant 1, 2 options, both offered
            ");
        t.True(PresenceSerializer.TryRead(oldStyleDw, oldStyleDw.Length, out PresenceState preDw),
               "a pre-record-29 packet parses");
        t.True(preDw.HasDecisionLines && preDw.HasDecisionState,
               "its decision lines and states are delivered");
        t.True(!preDw.HasDecisionWidgets, "no widget record is invented");
        t.True(preDw.DecisionRoles == null,
               "and no roles — the receiver keeps the mod-drawn plates rather than cloning a widget "
               + "the sender never named");

        // TRUNCATED record (claims 6 payload bytes, delivers 2): tail abandoned, nothing thrown.
        byte[] cutDw = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1D 06 09 04");
        t.True(PresenceSerializer.TryRead(cutDw, cutDw.Length, out PresenceState cutDwS),
               "a truncated decision-widgets record still parses the packet");
        t.True(!cutDwS.HasDecisionWidgets, "and the incomplete record is simply not delivered");

        // ALL THREE DECISION RECORDS TOGETHER, in the byte order the writer emits them — the shape a
        // peer really receives while somebody is answering a damage prompt. The three are written on
        // ONE gate and sampled in ONE walk, so option i means the same widget in all three.
        var threeRoles = new byte[]
        {
            NetProtocol.DecisionRoleBurnAvailable,
            NetProtocol.DecisionRoleTakeDamage,
            NetProtocol.DecisionRoleBurnDiscarded,
        };
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = "Ja",
            HasDecisionState = true,
            DecisionPromptKind = NetProtocol.DecisionKindTakeDamage,
            DecisionTextVariant = NetProtocol.DecisionTextDealDamage,
            DecisionOptionCount = 3,
            DecisionOptionFlags = new byte[]
            {
                NetProtocol.DecisionOptionOfferedBit,
                NetProtocol.DecisionOptionOfferedBit,
                NetProtocol.DecisionOptionDimmedBit,
            },
            HasDecisionWidgets = true,
            DecisionWidgetFlags = NetProtocol.DecisionWidgetDamageValidBit,
            DecisionDamageAmount = 4,
            DecisionRoleCount = 3,
            DecisionRoles = threeRoles,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 03
            0C 02 4A 61            // id 12: 'Ja'
            18 05 09 03 01 01 02   // id 24: kind|variant, 3 options
            1D 06 08 04 03 01 03 02 // id 29: damage-valid, 4, 3 roles
            "), ext, m, "records 12, 24 and 29 ride together, in the writer's own emission order");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState all3), "and the trio parses");
        t.Equal(all3.DecisionOptionCount, all3.DecisionRoleCount,
                "the states and the roles describe the same number of options — index alignment is "
                + "structural, one sampler walk fills both");

        // -- 7o3. USE BARS (extension record 25) ---------------------------------------------
        // The SECOND drawer below the decision row: which of the four use-slot bars the owner has
        // docked AND visible, how many slots each shows, whether an element/option sub-picker
        // stands open in it, and per slot offered / dimmed / chosen. Variable-length blocks — one
        // per SET mask bit, in BIT order — which is what makes the reader's own bounds discipline
        // the thing under test.
        //
        // WHAT IS DELIBERATELY ABSENT: any slot LABEL. The game's use slots have none
        // (UIUseSlot<T> carries a button and two highlight objects; every concrete slot decorates
        // itself with a sprite off the item/bonus/ability ART), so a "label" here could only have
        // been card identity — which never rides this wire in any form. The receiver captions each
        // bar from the BAR BIT in its own language, the record-24 text-variant solution.
        t.Case("7o3. extras, use-bars record");
        var useBarFlags = new byte[NetProtocol.UseBarsCount];
        var useBarCounts = new byte[NetProtocol.UseBarsCount];
        var useBarSlots = new byte[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];
        useBarCounts[0] = 2;                                     // active-bonus bar: two slots
        useBarSlots[0] = NetProtocol.UseSlotOfferedBit;           //   #0 clickable
        useBarSlots[1] = (byte)(NetProtocol.UseSlotOfferedBit | NetProtocol.UseSlotChosenBit);
        useBarCounts[3] = 1;                                     // items bar: one slot…
        useBarFlags[3] = NetProtocol.UseBarElementPickerBit;      //   …with its element picker open
        useBarSlots[3 * NetProtocol.UseBarsMaxSlots] = NetProtocol.UseSlotDimmedBit;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasUseBars = true,
            UseBarsMask = (byte)(NetProtocol.UseBarActiveBonusBit | NetProtocol.UseBarItemsBit),
            UseBarFlags = useBarFlags,
            UseBarSlotCounts = useBarCounts,
            UseBarSlotStates = useBarSlots,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            19 08            // id 25 (use bars), len 8
            09               // bar mask: activeBonus (bit0) + items (bit3)
            00 02 01 05      // bar 0: no picker, 2 slots -- #0 offered, #1 offered+chosen
            01 01 02         // bar 3: element picker OPEN, 1 slot -- #0 dimmed (greyed)
            "), ext, m, "the use-bars record is [id 25][len][barMask] then, per SET bar bit in BIT "
                        + "order, [barFlags][n][n slot bytes] — written AFTER record 24 in id order");
        t.Equal(21, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 8) = 21 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ub), "and it parses");
        t.True(ub.HasUseBars, "the use-bar drawer is delivered");
        t.Equal((byte)(NetProtocol.UseBarActiveBonusBit | NetProtocol.UseBarItemsBit), ub.UseBarsMask,
                "with both bar bits intact — and the two bars the owner did NOT have up stay clear");
        t.Equal(2, ub.UseBarSlotCounts![0], "the active-bonus bar reports its two slots");
        t.Equal(0, ub.UseBarSlotCounts[1], "the abilities bar, absent from the mask, reports none");
        t.Equal(1, ub.UseBarSlotCounts[3], "and the items bar its one");
        t.Equal(NetProtocol.UseSlotOfferedBit, ub.UseBarSlotStates![0],
                "slot 0 of the first bar is OFFERED — the receiver draws a live tile");
        t.True((ub.UseBarSlotStates[1] & NetProtocol.UseSlotChosenBit) != 0,
               "slot 1 carries CHOSEN (the accent frame on the peer's tile)");
        t.Equal(NetProtocol.UseSlotDimmedBit,
                ub.UseBarSlotStates[3 * NetProtocol.UseBarsMaxSlots],
                "and the items slot is dimmed and not offered — two separate axes, as on the "
                + "owner's own bar");
        t.Equal(NetProtocol.UseBarElementPickerBit, ub.UseBarFlags![3],
                "the items bar reports its OPEN element sub-picker");
        t.Equal(0, ub.UseBarFlags[0], "and the active-bonus bar reports none");

        // MASKED ON WRITE AND ON READ, on all three byte kinds: a sender that sets bits this build
        // does not define must not light a fifth bar, a third picker or a fourth slot axis here.
        byte[] wildUb = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            19 05            // id 25, len 5
            F1               // bar mask: reserved bits 4..7 set, on top of bar 0
            FE               // bar flags: every reserved bit set, on top of OPTION picker
            02               // 2 slots
            F9 FA            // slot bytes: reserved bits 3..7 set, on top of OFFERED / DIMMED
            ");
        t.True(PresenceSerializer.TryRead(wildUb, wildUb.Length, out PresenceState wub),
               "a use-bars record with undefined bits still parses");
        t.Equal(NetProtocol.UseBarActiveBonusBit, wub.UseBarsMask,
                "the bar mask is read through the mask — no fifth bar can be invented");
        t.Equal(NetProtocol.UseBarOptionPickerBit, wub.UseBarFlags![0],
                "the bar flags likewise");
        t.Equal(NetProtocol.UseSlotOfferedBit, wub.UseBarSlotStates![0],
                "and every undefined slot bit is masked away");
        t.Equal(NetProtocol.UseSlotDimmedBit, wub.UseBarSlotStates[1], "on every slot, not just the first");

        // A LYING COUNT can neither overrun the record nor bleed into the next one: n is re-clamped
        // against what is LEFT INSIDE the record, and the record behind it still reads.
        byte[] lyingUb = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02
            19 05 01 00 FF 01 02       // id 25, len 5: bar 0 claims 255 slots, carries 2
            03 07 01 00 30 2E 31 2E 30 // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(lyingUb, lyingUb.Length, out PresenceState ubLiar),
               "a use-bars record claiming more slots than it carries still parses");
        t.Equal(2, ubLiar.UseBarSlotCounts![0], "the count is clamped to what the record really holds");
        t.True(ubLiar.HasModVersion, "and the record behind it is read past it, undamaged");

        // OVER-CAP: a sender offering more slots than the per-bar cap is clamped on read, so a peer
        // can never be made to allocate or draw past the record's own bound.
        byte[] ubOverCap = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            19 0F 01 00 0C 01 01 01 01 01 01 01 01 01 01 01 01
            ");
        t.True(PresenceSerializer.TryRead(ubOverCap, ubOverCap.Length, out PresenceState ubCapped),
               "an over-cap use-bars record parses");
        t.Equal(NetProtocol.UseBarsMaxSlots, ubCapped.UseBarSlotCounts![0],
                "with the slot count clamped to UseBarsMaxSlots");

        // A TRUNCATED BLOCK ends the walk WITHOUT losing the bars already read: the mask delivered
        // is the mask actually parsed, so a peer never draws a row it has no bytes for.
        byte[] ubCutBlock = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            19 04            // id 25, len 4
            09               // mask claims activeBonus AND items
            00 01 01         // bar 0 complete: no picker, 1 slot, offered -- then the record ENDS
            ");
        t.True(PresenceSerializer.TryRead(ubCutBlock, ubCutBlock.Length, out PresenceState ubCut),
               "a use-bars record whose last block is missing still parses");
        t.Equal(NetProtocol.UseBarActiveBonusBit, ubCut.UseBarsMask,
                "and delivers only the bar it really carried — the truncated items bar is dropped");
        t.Equal(1, ubCut.UseBarSlotCounts![0], "the complete bar keeps its slot");

        // NOTHING UP ⇒ NO RECORD, NO TAIL, NO BLOCK: the drawer is empty when the last bar releases
        // AND when every bar is render-hidden because the owner is looking at another character
        // (the surface drops a hidden bar from the mask before it ever gets here). An idle packet
        // stays byte-identical to the previous build's.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasUseBars = true, HandCardCount = 5,
            UseBarsMask = 0,
            UseBarFlags = useBarFlags, UseBarSlotCounts = useBarCounts, UseBarSlotStates = useBarSlots,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty use-bar mask writes no record at all");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noUb), "and it parses");
        t.True(!noUb.HasUseBars, "with HasUseBars false (the peer's bar drawer empties)");

        // OLD-STYLE PACKET (no record 25 at all — a build-88 sender): everything that existed
        // before still arrives, and the receiver derives "no bar drawer", which is exactly what
        // every build before this one drew below the decision row: nothing.
        byte[] oldStyleUb = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 02
            0C 02 4A 61      // id 12 decision lines: 'Ja'
            18 03 09 01 01   // id 24 decision state: take-damage, one OFFERED option
            ");
        t.True(PresenceSerializer.TryRead(oldStyleUb, oldStyleUb.Length, out PresenceState preUb),
               "a pre-record-25 packet parses");
        t.True(preUb.HasDecisionLines && preUb.HasDecisionState,
               "its decision records are delivered");
        t.True(!preUb.HasUseBars, "no use-bar drawer is invented");
        t.Equal(0, preUb.UseBarsMask, "the derived bar mask is 0 — the peer draws no second drawer");
        t.True(preUb.UseBarSlotStates == null,
               "and no slot states — never a guess at somebody else's live choice");

        // TRUNCATED record (claims 8 payload bytes, delivers 2): tail abandoned, nothing throws.
        byte[] cutUb = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 19 08 09 00");
        t.True(PresenceSerializer.TryRead(cutUb, cutUb.Length, out PresenceState cutUbS),
               "a truncated use-bars record still parses the packet");
        t.True(!cutUbS.HasUseBars, "and the incomplete record is simply not delivered");

        // ID ORDER: record 25 rides LAST, behind 12 and 24 — the whole decision display in one
        // packet, in id order, exactly as the owner's board shows it (row, states, bar drawer).
        useBarCounts[1] = 0;
        useBarCounts[3] = 0;
        useBarFlags[3] = 0;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasDecisionLines = true, DecisionLinesText = "Ja",
            HasDecisionState = true,
            DecisionPromptKind = NetProtocol.DecisionKindTakeDamage,
            DecisionTextVariant = NetProtocol.DecisionTextDealDamage,
            DecisionOptionCount = 1,
            DecisionOptionFlags = new[] { NetProtocol.DecisionOptionOfferedBit },
            HasUseBars = true,
            UseBarsMask = NetProtocol.UseBarActiveBonusBit,
            UseBarFlags = useBarFlags, UseBarSlotCounts = useBarCounts, UseBarSlotStates = useBarSlots,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            03                   // tail: 3 records, in id order
            0C 02 4A 61          // id 12 decision lines: 'Ja'
            18 03 09 01 01       // id 24 decision state
            19 05 01 00 02 01 05 // id 25 use bars: bar 0, no picker, 2 slots
            "), ext, m, "record 25 rides the tail behind records 12 and 24 (id order 12, 24, 25)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ubCombo),
               "and the whole decision display parses");
        t.True(ubCombo.HasDecisionLines && ubCombo.HasDecisionState && ubCombo.HasUseBars,
               "with all three decision-display records delivered");

        // -- 7o4. ITEM-USE CLIP (extension record 26) ---------------------------------------
        // WHICH position of the sender's open item fan lies clipped in their item-USE recess. The
        // gap it closes: the wire carried only HOW MANY item cards are up and that the recess is
        // VISIBLE, so a card the owner had laid INTO the recess was still drawn out in the arc on
        // every other machine while their mirrored recess stood empty — against the 2026-08-08
        // ruling that every Anzeige of the control board is synchronised. One INDEX byte into the
        // very fan the count already describes; the receiver replays the 0.28 s settle from the
        // edge this byte appears on, so the animation costs nothing beyond it.
        t.Case("7o4. extras, item-use clip record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasItemFan = true, ItemCardCount = 5,
            HasItemUseClip = true, ItemUseClipIndex = 2,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            88               // flags: FlagItemFan (bit3) + FlagPileBrowse ('a BLOCK follows', bit7)
            00               // handCardCount
            05               // itemCardCount — the fan the index below addresses
            80 00            // byte A: extension tail; byte B: browse count 0 -> no browse fan
            01               // tail: 1 record
            1A 01            // id 26 (item-use clip), len 1
            02               // fan position 2 lies in the owner's item-use recess
            "), ext, m, "the item-use clip record is [id 26][len 1][fan index] — an arc POSITION, "
                        + "never an item identity");
        t.Equal(15, m, "header 7 + hand count 1 + item count 1 + block 2 + tail 1 + (2 + 1) = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState clip), "and it parses");
        t.True(clip.HasItemFan, "the item fan is delivered");
        t.Equal(5, clip.ItemCardCount, "with its count");
        t.True(clip.HasItemUseClip, "and the clip is delivered with it");
        t.Equal(2, clip.ItemUseClipIndex, "naming fan position 2 — the slab the peer lays in the recess");

        // AN EMPTY RECESS WRITES NO RECORD AT ALL, which is the whole economics of this feature:
        // the fan is open for minutes and a card lies in the recess for seconds, so nearly every
        // packet stays byte-identical to the previous build's. Absence IS "nothing is clipped";
        // no sentinel index exists and none is needed.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasItemFan = true, ItemCardCount = 5, HandCardCount = 3,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 08 03 05"), ext, m,
               "an open item fan with an EMPTY recess writes no record, no tail and no block");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noClip), "and it parses");
        t.True(noClip.HasItemFan && !noClip.HasItemUseClip,
               "with the fan up and HasItemUseClip false — the peer draws every chip in the arc");
        t.Equal(-1, noClip.HasItemUseClip ? noClip.ItemUseClipIndex : -1,
                "i.e. exactly what a build predating record 26 renders");

        // INDEX 0 IS A REAL VALUE, not "none": the leftmost chip is the one most likely to be
        // placed first, and a writer that treated 0 as absent would leave that case unsynced.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasItemFan = true, ItemCardCount = 1, HasItemUseClip = true, ItemUseClipIndex = 0,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 88 00 01 80 00 01 1A 01 00"), ext, m,
               "fan position 0 is written like any other index");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState clip0), "and it parses");
        t.True(clip0.HasItemUseClip, "with the record delivered");
        t.Equal(0, clip0.ItemUseClipIndex, "naming the FIRST fan position");

        // NOT RANGE-CHECKED ON THE WIRE, deliberately: an index past the count a packet happens to
        // carry is legitimate for one frame either side of a fan resize, so the clamp lives in the
        // RENDERER against its own live slab count — record 6's rule, applied to the same shape of
        // field. The reader must therefore deliver the byte untouched rather than second-guess it.
        byte[] wideClip = Hex.Bytes("31 52 56 47 03 01 88 00 02 80 00 01 1A 01 FF");
        t.True(PresenceSerializer.TryRead(wideClip, wideClip.Length, out PresenceState clipWide),
               "an out-of-range clip index still parses");
        t.True(clipWide.HasItemUseClip, "the record is delivered");
        t.Equal(255, clipWide.ItemUseClipIndex,
                "with the byte intact — the renderer, not the reader, holds the bound");

        // TRUNCATED record (claims 1 payload byte, delivers 0): the tail is abandoned, nothing
        // throws, and no clip is invented.
        byte[] cutClip = Hex.Bytes("31 52 56 47 03 01 88 00 02 80 00 01 1A 01");
        t.True(PresenceSerializer.TryRead(cutClip, cutClip.Length, out PresenceState clipCut),
               "a truncated item-use clip record still parses the packet");
        t.True(!clipCut.HasItemUseClip, "and the incomplete record is simply not delivered");

        // ID ORDER: 26 rides BETWEEN 25 and 27, and an older reader steps over it by its own length
        // and still finds record 27 where it expects it — the whole point of the TLV tail.
        useBarCounts[0] = 1;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasItemFan = true, ItemCardCount = 3,
            HasUseBars = true, UseBarsMask = NetProtocol.UseBarActiveBonusBit,
            UseBarFlags = useBarFlags, UseBarSlotCounts = useBarCounts, UseBarSlotStates = useBarSlots,
            HasItemUseClip = true, ItemUseClipIndex = 1,
            HasTrackOrder = true, TrackOrderCount = 1, TrackOrderOwnedMask = 0x01,
            TrackOrderIds = new[] { 0x11 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            88 00 03             // flags: item fan + block; hand 0; item count 3
            80 00
            03                   // tail: 3 records, in id order
            19 04 01 00 01 01    // id 25 use bars: bar 0, no picker, 1 slot, offered
            1A 01 01             // id 26 item-use clip: fan position 1
            1B 06 01 01 11 00 00 00 // id 27 track order: one owned entry
            "), ext, m, "record 26 rides the tail between records 25 and 27 (id order 25, 26, 27)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState clipCombo), "and the combo parses");
        t.True(clipCombo.HasUseBars && clipCombo.HasItemUseClip && clipCombo.HasTrackOrder,
               "with all three records delivered");
        t.Equal(1, clipCombo.ItemUseClipIndex, "the clip index is its own");
        t.Equal(0x11, clipCombo.TrackOrderIds![0],
                "and the record BEHIND it is read past it, undamaged — an older peer skipping 26 "
                + "by its length lands exactly here");
        useBarCounts[0] = 0;

        // -- 7p. CAP LABELS (extension record 13) ------------------------------------------
        // What the sender's CONFIRM cap and docked SKIP button ACTUALLY read — the fix for
        // "mein Mitspieler las 'Fortfahren', ich sehe 'Bestätigen'": the receiver's neutral
        // GUI_CONFIRM re-localization is only the fallback now; the record carries the sender's
        // own displayed string, verbatim, in their language.
        t.Case("7p. extras, cap-labels record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasConfirmCapLabel = true, ConfirmCapLabel = "OK",
            HasSkipCapLabel = true, SkipCapLabel = "Skip",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            0D 09            // record: id 13 (cap labels), len 9
            03               // mask: bit0 confirm + bit1 skip
            02 4F 4B         // confirm: len 2, UTF8 'OK'
            04 53 6B 69 70   // skip: len 4, UTF8 'Skip'
            "), ext, m, "the cap-labels record is [id 13][len][mask][len+UTF8 per set bit, bit order]");
        t.Equal(22, m, "header 7 + count 1 + block 2 + tail 1 + 2 + 9 = 22 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cl), "and it parses");
        t.True(cl.HasConfirmCapLabel && cl.HasSkipCapLabel, "both labels are delivered");
        t.Equal("OK", cl.ConfirmCapLabel ?? string.Empty, "confirm label byte-exact");
        t.Equal("Skip", cl.SkipCapLabel ?? string.Empty, "skip label byte-exact");

        // ONE label alone: the mask says which block follows — a skip-only record must not be
        // misread as a confirm label.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSkipCapLabel = true, SkipCapLabel = "Zug",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0D 05            // id 13, len 5
            02               // mask: bit1 skip only
            03 5A 75 67      // skip: len 3, UTF8 'Zug'
            "), ext, m, "a skip-only record carries mask bit1 and one block");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState so), "and it parses");
        t.True(!so.HasConfirmCapLabel, "no confirm label is invented");
        t.Equal("Zug", so.SkipCapLabel ?? string.Empty, "and the skip label lands in the skip slot");

        // ABSENT WHEN NOTHING IS SHOWN: no labels -> no record, no tail — byte-identical idle.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasConfirmCapLabel = true, ConfirmCapLabel = string.Empty, HandCardCount = 5,
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "an empty confirm label writes no record at all");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState noCl), "and it parses");
        t.True(!noCl.HasConfirmCapLabel && !noCl.HasSkipCapLabel,
               "with both label flags false (peers keep the neutral fallback wording)");

        // BYTE CAP per label, character boundary.
        string longLabel = new string('ü', NetProtocol.CapLabelMaxBytes);
        byte[] cappedLabel = PresenceSerializer.EncodeConfirmCapLabel(longLabel);
        t.True(cappedLabel.Length <= NetProtocol.CapLabelMaxBytes,
               "the encoded confirm label honours the cap");
        t.Equal(new string('ü', NetProtocol.CapLabelMaxBytes / 2),
                System.Text.Encoding.UTF8.GetString(cappedLabel),
                "and truncated on a character boundary, not mid-glyph");
        t.True(1 + 2 * (1 + NetProtocol.CapLabelMaxBytes) <= 255,
               "mask + two capped labels fit a single-byte TLV length");

        // OLD-READER SKIP-BY-LENGTH + future-record-in-front.
        byte[] oldReaderCl = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02               // 2 records
            63 05 03 01 41 01 42          // id 99, len 5 -- record 13 as a pre-record-13 reader sees it
            03 07 01 00 30 2E 31 2E 30    // id 3, mod version: build 1, '0.1.0'
            ");
        t.True(PresenceSerializer.TryRead(oldReaderCl, oldReaderCl.Length, out PresenceState oldCl),
               "a 5-byte record this build does not know is skipped, and the packet parses");
        t.True(!oldCl.HasConfirmCapLabel && !oldCl.HasSkipCapLabel,
               "the unknown record delivers nothing (pre-record-13 peer)");
        t.True(oldCl.HasModVersion, "and the record behind it is read past it");
        byte[] futureCl = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0D 04 01 02 4F 4B// id 13 cap labels, confirm 'OK'
            ");
        t.True(PresenceSerializer.TryRead(futureCl, futureCl.Length, out PresenceState futCl),
               "a packet with an unknown record ahead of the cap labels parses");
        t.Equal("OK", futCl.ConfirmCapLabel ?? string.Empty, "and the confirm label is still read");

        // MALFORMED interior: a label length that claims to run past the record's own end is
        // dropped WITHOUT bleeding into the next record — the sub-reads are bounded by the
        // record length, and the outer walk still advances by that length.
        byte[] overrunCl = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02
            0D 03 01 10 41   // id 13: confirm claims 16 bytes, record only holds 1
            03 07 01 00 30 2E 31 2E 30    // id 3, mod version behind it
            ");
        t.True(PresenceSerializer.TryRead(overrunCl, overrunCl.Length, out PresenceState ovCl),
               "a cap-labels record whose inner length overruns still parses the packet");
        t.True(!ovCl.HasConfirmCapLabel, "the overrunning label is simply not delivered");
        t.True(ovCl.HasModVersion, "and the record BEHIND the malformed one is read intact");

        // TRUNCATED record (claims 9 payload bytes, delivers 3): tail abandoned, nothing throws.
        byte[] cutCl = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0D 09 03 02 4F");
        t.True(PresenceSerializer.TryRead(cutCl, cutCl.Length, out PresenceState cutClS),
               "a truncated cap-labels record still parses the packet");
        t.True(!cutClS.HasConfirmCapLabel, "and the incomplete record is simply not delivered");

        // ID ORDER: records 12 and 13 ride LAST, behind every record that already existed.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTooltip = true, BoardTooltipText = "A",
            HasDecisionLines = true, DecisionLinesText = "Ja",
            HasConfirmCapLabel = true, ConfirmCapLabel = "OK",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            03               // tail: 3 records, in id order
            09 01 41         // id 9 board tooltip 'A'
            0C 02 4A 61      // id 12 decision lines 'Ja'
            0D 04 01 02 4F 4B// id 13 cap labels, confirm 'OK'
            "), ext, m, "records 12 and 13 ride the tail behind record 9 (id order 9, 12, 13)");

        // -- 7p2. THE TWO CAP LABELS THAT NEVER TRAVELLED (record 13 mask bits 2 + 3) -------
        // UNDO carries the pick flow's dialog-CANCEL wording and the item-USE cap carries an
        // item-SURRENDER demand's wording; peers used to letter both from their OWN localization
        // ("Rückgängig" / "USE"), so a cancel read as an undo and a surrender read as a use.
        // Additive: the new blocks are the HIGH mask bits and ride LAST, behind the two that
        // already shipped.
        t.Case("7p2. extras, undo + item-use cap labels");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasConfirmCapLabel = true, ConfirmCapLabel = "OK",
            HasSkipCapLabel = true, SkipCapLabel = "Skip",
            HasUndoCapLabel = true, UndoCapLabel = "Zu",
            HasItemUseCapLabel = true, ItemUseCapLabel = "Ab",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            0D 0F            // record: id 13 (cap labels), len 15
            0F               // mask: bit0 confirm | bit1 skip | bit2 UNDO | bit3 ITEM-USE
            02 4F 4B         // confirm: len 2, UTF8 'OK'
            04 53 6B 69 70   // skip:    len 4, UTF8 'Skip'
            02 5A 75         // undo:    len 2, UTF8 'Zu'
            02 41 62         // itemUse: len 2, UTF8 'Ab'
            "), ext, m, "the two new labels ride behind the two that already shipped, in mask-bit order");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cl4), "and it parses");
        t.Equal("OK", cl4.ConfirmCapLabel ?? string.Empty, "confirm label byte-exact");
        t.Equal("Skip", cl4.SkipCapLabel ?? string.Empty, "skip label byte-exact");
        t.Equal("Zu", cl4.UndoCapLabel ?? string.Empty, "undo label byte-exact");
        t.Equal("Ab", cl4.ItemUseCapLabel ?? string.Empty, "item-use label byte-exact");

        // ONE of the new labels alone — the mask, not the position, is what names a block, so an
        // item-use-only record must not be misread as a confirm/skip/undo label.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasItemUseCapLabel = true, ItemUseCapLabel = "Ab",
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00 01
            0D 04            // id 13, len 4
            08               // mask: bit3 item-use ONLY
            02 41 62         // itemUse: len 2, UTF8 'Ab'
            "), ext, m, "an item-use-only record carries mask bit3 and exactly one block");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState uo), "and it parses");
        t.True(!uo.HasConfirmCapLabel && !uo.HasSkipCapLabel && !uo.HasUndoCapLabel,
               "no other label is invented from a block that is not there");
        t.Equal("Ab", uo.ItemUseCapLabel ?? string.Empty, "and it lands in the item-use slot");

        // AN OLD-STYLE RECORD (mask 0x03, the only two bits that ever shipped) must still deliver
        // its two labels and claim NEITHER new one — which is what a receiver derives when its peer
        // predates the bits: the neutral GUI_UNDO / GUI_USE fallback on those two caps.
        byte[] oldMask = Hex.Bytes(
            "31 52 56 47 03 01 80 00 80 00 01 0D 09 03 02 4F 4B 04 53 6B 69 70");
        t.True(PresenceSerializer.TryRead(oldMask, oldMask.Length, out PresenceState om),
               "a two-label cap-labels record still parses");
        t.Equal("OK", om.ConfirmCapLabel ?? string.Empty, "its confirm label is delivered");
        t.Equal("Skip", om.SkipCapLabel ?? string.Empty, "its skip label is delivered");
        t.True(!om.HasUndoCapLabel && !om.HasItemUseCapLabel,
               "and NEITHER new label is claimed — the receiver letters those two caps with its " +
               "own neutral wording, exactly as every build before this one did");

        // The mask's OWN hygiene: a future sender's bit 4 must not survive into a decode, or
        // widening the mask later would re-read old packets as having opted into a new block.
        t.Equal((byte)0x0F, NetProtocol.CapLabelDefinedMask,
                "four label slots are defined; bits 4..7 are still reserved");
        t.True(1 + NetProtocol.CapLabelSlotCount * (1 + NetProtocol.CapLabelMaxBytes) <= 255,
               "and four maximum-length labels plus the mask still fit one TLV record");

        // -- 7q. CHARACTER FOCUS (extension record 22) --------------------------------------
        // Which character the sender is LOOKING at (free character focus), by the stable
        // ActorGuid hash, plus the facts only they can know: whether the character THE GAME IS
        // WAITING ON is theirs (flags bit 0) and, when that is somebody OTHER than the character
        // they are looking at, which one it is (flags bit 1 + a trailing int32). Ids 18..21 are
        // deliberately skipped — reserved for records developed in parallel, and a shipped id can
        // never be renumbered.
        //
        // THE MARK A RECEIVER DRAWS IS A PURE FUNCTION OF THIS RECORD, which is the whole point of
        // the 2026-08-08 change: bit0 clear = no mark, bit0 set with attention == focus = GREEN,
        // bit0 set with attention != focus = RED. The vectors below cover all three, in both the
        // 5-byte and the 9-byte form, plus the old-style packet that carries neither.
        t.Case("7q. extras, character-focus record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCharFocus = true, CharFocusActorId = 0x0A0B0C0D,
            CharFocusOwnsAttention = true, CharFocusAttentionActorId = 0x0A0B0C0D,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only (bit 7)
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            16 05            // id 22 (character focus), len 5
            01               // flags: bit0 the sender owns the character being waited on;
                             //        bit1 CLEAR — that character IS the focus id below
            0D 0C 0B 0A      // focus actor id LE
            "), ext, m, "GREEN: attention == focus writes the 5-byte form, bit1 clear");
        t.Equal(18, m, "header 7 + count 1 + block 2 + tail 1 + 7 = 18 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cf), "and it parses");
        t.True(cf.HasCharFocus, "the focus is delivered");
        t.Equal(0x0A0B0C0D, cf.CharFocusActorId, "with the stable actor id intact");
        t.True(cf.CharFocusOwnsAttention, "and the owns-the-attention-actor flag set");
        t.Equal(0x0A0B0C0D, cf.CharFocusAttentionActorId,
                "and the attention actor read back as the focus id (no tail was needed)");
        t.Equal(FocusMark.Green, MarkOf(cf), "so the receiver derives GREEN");

        // RED: the sender owns the character the game is waiting on but is LOOKING at another one.
        // This is the case the wire could not express before, and the case the whole change is
        // for: during a pending DECISION there is no actor at turn on ANY machine, so a receiver
        // re-deriving the mark from its own turn read produced NO mark at all.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCharFocus = true, CharFocusActorId = 0x0A0B0C0D,
            CharFocusOwnsAttention = true, CharFocusAttentionActorId = 0x00BBAA99,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            16 09            // id 22, len 9 — the flag-guarded tail rides along
            03               // flags: bit0 owns the attention actor + bit1 its id follows
            0D 0C 0B 0A      // focus actor id LE (the character being LOOKED at)
            99 AA BB 00      // attention actor id LE (the character being WAITED ON)
            "), ext, m, "RED: attention != focus writes the 9-byte form with bit1 set");
        t.Equal(22, m, "the tail costs exactly four more bytes than the green form");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cfRed), "and it parses");
        t.Equal(0x0A0B0C0D, cfRed.CharFocusActorId, "the focus id is the character being looked at");
        t.True(cfRed.CharFocusOwnsAttention, "the owns-the-attention-actor flag survives");
        t.Equal(0x00BBAA99, cfRed.CharFocusAttentionActorId,
                "and the trailing attention id is delivered, not confused with the focus id");
        t.Equal(FocusMark.Red, MarkOf(cfRed), "so the receiver derives RED");

        // The flag is genuinely independent of the id (a player focusing a character while
        // somebody ELSE is being waited on — the common case for a spectating focus). With bit 0
        // clear the attention id is NOT sent: the sender's attention actor can then only be the
        // replicated turn actor, which every client reads identically for itself.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasCharFocus = true, CharFocusActorId = 0x00000011,
            CharFocusOwnsAttention = false, CharFocusAttentionActorId = 0x00BBAA99,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            16 05
            00               // flags: the sender does NOT own the character being waited on
            11 00 00 00
            "), ext, m, "no owned attention actor writes flags 0 and NO tail, even when the "
                        + "sender's state names one");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cfNone), "and it parses");
        t.True(!cfNone.CharFocusOwnsAttention, "with the attention flag clear");
        t.Equal(0, cfNone.CharFocusAttentionActorId,
                "and no attention actor at all — the receiver substitutes its own turn read");
        t.Equal(FocusMark.None, MarkOf(cfNone), "so the receiver derives NO mark");

        // IDLE IDENTITY: no focus -> no record, and the packet is byte-identical to what a
        // sender predating record 22 produces for the same state.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 2 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 02"), ext, m,
               "no focus -> no record: byte-identical to a pre-record-22 sender");

        // SENTINEL CLAMP, both ends: actor id 0 is "none" everywhere in this system. The writer
        // never emits it (the tail does not even open), and a hand-built record carrying it is
        // not delivered.
        m = PresenceSerializer.Write(new PresenceState { HasCharFocus = true }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "a zero focus actor id is never written — the packet stays the idle packet");
        byte[] zeroFocus = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 05 01 00 00 00 00");
        t.True(PresenceSerializer.TryRead(zeroFocus, zeroFocus.Length, out PresenceState cfZero),
               "a hand-built zero-id focus record still parses the packet");
        t.True(!cfZero.HasCharFocus, "and is simply not delivered");

        // OLD-STYLE PACKET (the 5-byte record every build before 2026-08-08 wrote, with bit 1
        // undefined and therefore clear): still parses, and still means what it always meant —
        // the sender owns the character being waited on and it IS the one they are looking at.
        // The green cue is byte-for-byte the pre-change behaviour; nothing regressed for the case
        // the old wire could express.
        byte[] oldStyle = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 05 01 22 00 00 00");
        t.True(PresenceSerializer.TryRead(oldStyle, oldStyle.Length, out PresenceState cfOld),
               "an old-style 5-byte record 22 still parses");
        t.Equal(0x22, cfOld.CharFocusActorId, "with its focus id intact");
        t.True(cfOld.CharFocusOwnsAttention, "bit0 still means 'the game is waiting on mine'");
        t.Equal(0x22, cfOld.CharFocusAttentionActorId,
                "and the attention actor falls back to the focus id, no tail required");
        t.Equal(FocusMark.Green, MarkOf(cfOld), "so an old-style packet still derives GREEN");

        // FLAG CLAMP: a future sender's undefined flag bits are masked off on read, so they can
        // never light a meaning this build does not define. FE = 1111_1110: bit0 clear, bit1 set,
        // and six undefined bits — the DEFINED bit 1 must survive the mask while the rest die.
        // With bit0 clear the tail is not read at all (the flags do not demand it), so the record
        // still means "no owned attention actor".
        byte[] wildFlags = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 05 FE 11 00 00 00");
        t.True(PresenceSerializer.TryRead(wildFlags, wildFlags.Length, out PresenceState cfWild),
               "undefined focus flag bits still parse");
        t.True(cfWild.HasCharFocus && cfWild.CharFocusActorId == 0x11, "the id survives");
        t.True(!cfWild.CharFocusOwnsAttention, "and every undefined bit is masked away");
        t.Equal(0, cfWild.CharFocusAttentionActorId,
                "bit1 alone never names an attention actor — bit0 is what claims one");
        t.Equal(FocusMark.None, MarkOf(cfWild), "so the mark stays None");

        // TRUNCATED record (claims 5 payload bytes, delivers 2): tail abandoned, nothing throws.
        byte[] cutFocus = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 05 01 11");
        t.True(PresenceSerializer.TryRead(cutFocus, cutFocus.Length, out PresenceState cutFocusS),
               "a truncated focus record still parses the packet");
        t.True(!cutFocusS.HasCharFocus, "and the incomplete record is simply not delivered");

        // TRUNCATED ATTENTION TAIL: bit 1 promises nine payload bytes, the length byte says five.
        // "Validate only what MY flags demand" cuts both ways — the reader honours the LENGTH, so
        // the record degrades to its 5-byte meaning (attention == focus, GREEN) instead of reading
        // four bytes that are not there.
        byte[] shortTail = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 05 03 22 00 00 00");
        t.True(PresenceSerializer.TryRead(shortTail, shortTail.Length, out PresenceState cfShort),
               "a bit1 record whose length byte excludes the tail still parses");
        t.Equal(0x22, cfShort.CharFocusAttentionActorId,
                "and degrades to 'the attention actor IS the focus', never to a torn read");
        t.Equal(FocusMark.Green, MarkOf(cfShort), "so it derives GREEN rather than a phantom RED");

        // A ZERO in the tail is the same sentinel it is everywhere else: it can never name a
        // character, so the record falls back to the 5-byte meaning instead of claiming nobody.
        byte[] zeroTail = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 16 09 03 22 00 00 00 00 00 00 00");
        t.True(PresenceSerializer.TryRead(zeroTail, zeroTail.Length, out PresenceState cfZeroTail),
               "a zero attention id in the tail still parses");
        t.Equal(0x22, cfZeroTail.CharFocusAttentionActorId, "and is clamped to the focus id");
        t.Equal(FocusMark.Green, MarkOf(cfZeroTail), "so it derives GREEN, not a mark on nobody");

        // ID ORDER: record 22 rides LAST, behind every record that already existed — and to a
        // peer predating it, id 22 is an unknown record it steps over by length.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPileCounts = true, PileDiscardCount = 1,
            HasTrackHover = true, TrackHoverActorId = 0x11,
            HasCharFocus = true, CharFocusActorId = 0x22,
            CharFocusOwnsAttention = true, CharFocusAttentionActorId = 0x22,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            03                   // tail: 3 records, in id order
            0F 03 01 00 00       // id 15 pile counts: 1/0/0
            10 05 00 11 00 00 00 // id 16 track hover: no popup, actor 0x11
            16 05 01 22 00 00 00 // id 22 character focus: owns the attention actor, actor 0x22
            "), ext, m, "record 22 rides the tail behind records 15 and 16 (id order 15, 16, 22)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cfCombo), "and the combo parses");
        t.True(cfCombo.HasPileCounts && cfCombo.HasTrackHover && cfCombo.HasCharFocus,
               "with all three records delivered");
        t.Equal(0x22, cfCombo.CharFocusActorId, "and the focus id is not confused with the hover id");

        // The SAME neighbourhood with record 22 in its 9-byte form: the trailing attention id must
        // not bleed into the next record and the tail count must still be right. This is the
        // vector that would catch a length byte left at 5 while nine bytes are written.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackHover = true, TrackHoverActorId = 0x11,
            HasCharFocus = true, CharFocusActorId = 0x22,
            CharFocusOwnsAttention = true, CharFocusAttentionActorId = 0x33,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            02                            // tail: 2 records, in id order
            10 05 00 11 00 00 00          // id 16 track hover: no popup, actor 0x11
            16 09 03 22 00 00 00 33 00 00 00 // id 22: bit0+bit1, focus 0x22, attention 0x33
            "), ext, m, "the 9-byte record 22 keeps its place and its length byte in a full tail");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cfTailCombo),
               "and the 9-byte combo parses");
        t.Equal(0x11, cfTailCombo.TrackHoverActorId, "the hover id ahead of it is untouched");
        t.Equal(0x22, cfTailCombo.CharFocusActorId, "the focus id is intact");
        t.Equal(0x33, cfTailCombo.CharFocusAttentionActorId, "and the attention id is intact");
        t.Equal(FocusMark.Red, MarkOf(cfTailCombo), "so the receiver derives RED");

        // -- 7q2. A FOCUSED CHARACTER'S PILE, END TO END --------------------------------------
        // THE EXACT PACKET the "piles open for any character, in any phase, and mirrored 1:1"
        // feature emits (user ruling 2026-08-08, hardware ModBuild 89): the sender has a TEAMMATE
        // focused, is browsing that teammate's DISCARD pile, and their stack labels show that
        // teammate's numbers. Three already-shipped pieces have to agree inside ONE packet for a
        // peer to draw it — and the whole feature adds NO new record, which is the claim this
        // vector exists to pin down:
        //   * the trailing BLOCK's pile-browse sub-fields say WHICH pile and HOW MANY slabs,
        //   * record 15 carries the counts the owner's own three labels are DISPLAYING, and
        //   * record 22 names WHICH CHARACTER the owner is looking at.
        // The receiver joins them: Net.RemoteBoardFocus.DisplayedActor resolves record 22 against
        // its own replicated scenario and RemotePileFronts reads THAT character's
        // CCharacterClass.DiscardedAbilityCards for the faces. NO CARD IDENTITY RIDES ANY OF IT —
        // the bytes below are a pile kind, four counts and one actor hash, and that is the entire
        // payload of "der Peer sieht welches Pile für welchen Character offen ist".
        t.Case("7q2. extras, a focused character's open pile (browse block + counts + focus)");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPileBrowse = true, PileBrowseKind = NetProtocol.PileBrowseKindDiscard,
            PileBrowseCardCount = 4,
            HasPileCounts = true, PileDiscardCount = 4, PileBurntCount = 1, PileItemsCount = 6,
            HasCharFocus = true, CharFocusActorId = 0x00000022,
            CharFocusOwnsAttention = false,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: FlagPileBrowse -- 'a BLOCK follows'
            00               // handCardCount: the hand fan is not up, only the pile arc
            80               // byte A: kind bits 0..1 = 0 (DISCARD), held/left clear
                             //         (board-anchored), bit7 extension tail
            04               // byte B: 4 slabs in the arc
            02               // tail: 2 records, in id order
            0F 03 04 01 06   // id 15 pile counts: discard 4, burnt 1, items 6 -- the FOCUSED
                             //                    character's numbers, not the owner's own
            16 05 00 22 00 00 00 // id 22: focus actor 0x22, bit0 clear (the game is waiting on
                             //          somebody else -- browsing a teammate mid-enemy-turn)
            "), ext, m, "one packet says WHICH pile, HOW MANY cards, the three counts and WHICH "
                        + "character — and adds no record of its own");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState focusPile), "and it parses");
        t.True(focusPile.HasPileBrowse, "the browse block is delivered");
        t.Equal(NetProtocol.PileBrowseKindDiscard, focusPile.PileBrowseKind, "as the DISCARD pile");
        t.Equal((byte)4, focusPile.PileBrowseCardCount, "with four slabs");
        t.True(!focusPile.PileBrowseHeld, "board-anchored, never hand-held (PileStack.CanGrab)");
        t.True(focusPile.HasPileCounts, "the counts ride the same packet");
        t.Equal(4, focusPile.PileDiscardCount, "discard matches the open arc");
        t.Equal(1, focusPile.PileBurntCount, "burnt intact");
        t.Equal(6, focusPile.PileItemsCount, "items intact");
        t.Equal(0x22, focusPile.CharFocusActorId,
                "and record 22 names the character all of it is ABOUT — the ONLY selector a peer "
                + "needs to resolve the pile's cards from its own replicated model");

        // THE BURNT PILE of the same focused character: only the kind bits move. Two bits are the
        // whole difference between "Abgeworfen" and "Verbrannt" on every peer's screen, which is
        // why the enum order is guarded (PileKindWireOrderGuard) rather than trusted.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPileBrowse = true, PileBrowseKind = NetProtocol.PileBrowseKindBurnt,
            PileBrowseCardCount = 1,
            HasCharFocus = true, CharFocusActorId = 0x00000022,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            81               // byte A: kind bits 0..1 = 1 (BURNT) + bit7 extension tail
            01               // byte B: one slab
            01
            16 05 00 22 00 00 00
            "), ext, m, "the burnt pile of the same focused character differs by the kind bits alone");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState focusBurnt), "and it parses");
        t.Equal(NetProtocol.PileBrowseKindBurnt, focusBurnt.PileBrowseKind, "as the BURNT pile");
        t.Equal(0x22, focusBurnt.CharFocusActorId, "for the same focused character");

        // A CLOSED fan while the focus is still held: byte B goes to 0 and every reader that ever
        // shipped bit 7 gates the fan on count > 0, so the peer's arc collapses while their
        // mirrored board keeps showing the focused character's counts. This is the pair the
        // "the fan re-targets instead of closing" behaviour rides on — a character switch changes
        // record 22 and the counts, NOT the presence of the browse block.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasPileCounts = true, PileDiscardCount = 4, PileBurntCount = 1, PileItemsCount = 6,
            HasCharFocus = true, CharFocusActorId = 0x00000033,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState switched), "a switch packet parses");
        t.Equal((byte)0, switched.PileBrowseCardCount, "no arc -> count 0, which is 'no fan' to every reader");
        t.Equal(0x33, switched.CharFocusActorId, "while the focus id names the NEW character");
        t.Equal(4, switched.PileDiscardCount, "and the counts are the ones that board is displaying");


        // -- 7r. INITIATIVE-TRACK SELECTION FRAME (extension record 23) ----------------------
        // Which entries the sender's OWN track is framing with vanilla's selectionObject, read off
        // the live widget rather than re-derived. A LIST because an extra-turn actor can leave a
        // second frame standing (InitiativeTrack.cs:340), and PLAYERS/ENEMIES/OBJECTS alike —
        // which is what retires the "record 22 cannot name a monster" limitation.
        t.Case("7r. extras, initiative-track selection record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackSelection = true, TrackSelectionCount = 1,
            TrackSelectionIds = new[] { 0x0A0B0C0D, 0, 0, 0 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only (bit 7)
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            17 05            // id 23 (track selection), len 1 + 4*1
            01               // count
            0D 0C 0B 0A      // framed actor id LE
            "), ext, m, "the track-selection record is [id 23][len 1+4n][count][n × actorId LE]");
        t.Equal(18, m, "header 7 + count 1 + block 2 + tail 1 + 7 = 18 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ts), "and it parses");
        t.True(ts.HasTrackSelection, "the selection is delivered");
        t.Equal(1, ts.TrackSelectionCount, "with one framed entry");
        t.Equal(0x0A0B0C0D, ts.TrackSelectionIds![0], "and the stable actor id intact");

        // TWO frames at once — the extra-turn state vanilla can genuinely produce.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackSelection = true, TrackSelectionCount = 2,
            TrackSelectionIds = new[] { 0x11, 0x22, 0, 0 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            17 09            // id 23, len 1 + 4*2
            02
            11 00 00 00
            22 00 00 00
            "), ext, m, "two standing frames ride one record, in sample order");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ts2), "and the pair parses");
        t.Equal(2, ts2.TrackSelectionCount, "with both entries delivered");
        t.Equal(0x22, ts2.TrackSelectionIds![1], "and the second id in the second slot");

        // COUNT CLAMP on write: a caller claiming more ids than the cap (or than its own buffer)
        // can never make the record longer than TrackSelectionMaxIds allows.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackSelection = true, TrackSelectionCount = 99,
            TrackSelectionIds = new[] { 1, 2, 3, 4, 5, 6 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            17 11            // id 23, len 1 + 4*4 — clamped to the 4-id cap
            04
            01 00 00 00
            02 00 00 00
            03 00 00 00
            04 00 00 00
            "), ext, m, "an over-claimed count is clamped to TrackSelectionMaxIds on write");

        // IDLE IDENTITY: no frame -> no record, and the packet is byte-identical to what a sender
        // predating record 23 produces for the same state.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 2 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 02"), ext, m,
               "no selection frame -> no record: byte-identical to a pre-record-23 sender");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackSelection = true, TrackSelectionCount = 0,
            TrackSelectionIds = new[] { 0x11 },
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "an EMPTY selection does not even open the tail");

        // SENTINEL CLAMP on read: actor id 0 is "none" everywhere in this system, so a hand-built
        // record carrying one drops it — and an all-zero record is not delivered at all.
        byte[] zeroSel = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 17 05 01 00 00 00 00");
        t.True(PresenceSerializer.TryRead(zeroSel, zeroSel.Length, out PresenceState tsZero),
               "a hand-built zero-id selection record still parses the packet");
        t.True(!tsZero.HasTrackSelection, "and is simply not delivered");

        // LENGTH CLAMP on read: a count that claims more ids than the record actually carries is
        // re-clamped against the LENGTH (never trust the wire) — the surviving ids still land.
        byte[] overSel = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 17 05 04 11 00 00 00");
        t.True(PresenceSerializer.TryRead(overSel, overSel.Length, out PresenceState tsOver),
               "an over-claimed count still parses");
        t.True(tsOver.HasTrackSelection, "the record is delivered");
        t.Equal(1, tsOver.TrackSelectionCount, "clamped to what the record length can hold");
        t.Equal(0x11, tsOver.TrackSelectionIds![0], "with the one real id intact");

        // TRUNCATED record (claims 9 payload bytes, delivers 2): tail abandoned, nothing throws.
        byte[] cutSel = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 17 09 02 11");
        t.True(PresenceSerializer.TryRead(cutSel, cutSel.Length, out PresenceState cutSelS),
               "a truncated selection record still parses the packet");
        t.True(!cutSelS.HasTrackSelection, "and the incomplete record is simply not delivered");

        // ID ORDER: record 23 rides LAST, behind record 22 — and to a peer predating it, id 23 is
        // an unknown record it steps over by length. The three track records must not be confused
        // with one another: hover (16), focus (22) and selection (23) each carry their own id.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackHover = true, TrackHoverActorId = 0x11,
            HasCharFocus = true, CharFocusActorId = 0x22, CharFocusOwnsAttention = true,
            HasTrackSelection = true, TrackSelectionCount = 1, TrackSelectionIds = new[] { 0x33 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            03                   // tail: 3 records, in id order
            10 05 00 11 00 00 00 // id 16 track hover: no popup, actor 0x11
            16 05 01 22 00 00 00 // id 22 character focus: owns turn, actor 0x22
            17 05 01 33 00 00 00 // id 23 track selection: one framed entry, actor 0x33
            "), ext, m, "record 23 rides the tail behind records 16 and 22 (id order 16, 22, 23)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState tsCombo), "and the combo parses");
        t.True(tsCombo.HasTrackHover && tsCombo.HasCharFocus && tsCombo.HasTrackSelection,
               "with all three track records delivered");
        t.Equal(0x11, tsCombo.TrackHoverActorId, "the hover id is its own");
        t.Equal(0x22, tsCombo.CharFocusActorId, "the focus id is its own");
        t.Equal(0x33, tsCombo.TrackSelectionIds![0],
                "and the selection id is confused with neither — the three are different facts");


        // -- 7s. INITIATIVE-TRACK PLAYER ORDER (extension record 27) -------------------------
        // Vanilla's InitiativeTrackActorBehaviour.CompareTo (:160-171) sorts two PLAYER entries by
        // IsUnderMyControl while online AND in the card-selection phase — the foreign one first —
        // so during that phase MY index 3 really is YOUR index 5, and the mirrored track (a clone
        // of the OBSERVER's widget) showed the observer's arrangement on every peer's board. This
        // record carries the sender's own on-screen order of the PLAYER entries plus a mask of
        // which of them they control; the mask also pays for the mirrored "still has to choose"
        // ring, whose other half (has this character committed) is derived on the receiver from
        // the replicated model and never sent.
        t.Case("7s. extras, initiative-track player order record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackOrder = true, TrackOrderCount = 2, TrackOrderOwnedMask = 0x02,
            TrackOrderIds = new[] { 0x0A0B0C0D, 0x11, 0, 0, 0, 0 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80               // flags: block only (bit 7)
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            1B 0A            // id 27 (track order), len 2 + 4*2
            02               // count
            02               // owned mask: ids[1] is the sender's own character
            0D 0C 0B 0A      // ids[0] — a player they do NOT control, so vanilla sorts it FIRST
            11 00 00 00      // ids[1] — theirs, last in the player block
            "), ext, m, "the track-order record is [id 27][len 2+4n][count][ownedMask][n × actorId LE]");
        t.Equal(23, m, "header 7 + count 1 + block 2 + tail 1 + 12 = 23 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState to), "and it parses");
        t.True(to.HasTrackOrder, "the order is delivered");
        t.Equal(2, to.TrackOrderCount, "with both player entries");
        t.Equal(0x0A0B0C0D, to.TrackOrderIds![0], "the foreign entry first, exactly as vanilla sorts it");
        t.Equal(0x11, to.TrackOrderIds![1], "and the sender's own character last");
        t.Equal(0x02, to.TrackOrderOwnedMask, "with the owned mask index-aligned to the ids");

        // MASKED READ: the owned mask is masked to the bits TrackOrderMaxIds can define, on write
        // AND on read, so a newer sender's wider cap can never light ownership on an id this build
        // never received. 0xFF on the wire, 0x3F after the mask, and only bits < count mean
        // anything at all.
        byte[] wideMask = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1B 06 01 FF 11 00 00 00");
        t.True(PresenceSerializer.TryRead(wideMask, wideMask.Length, out PresenceState toMask),
               "a hand-built record with every mask bit set still parses");
        t.Equal(NetProtocol.TrackOrderOwnedDefinedMask & 0x01, toMask.TrackOrderOwnedMask & 0x01,
                "the one id it carries reads as owned");
        t.Equal(0, toMask.TrackOrderOwnedMask & ~NetProtocol.TrackOrderOwnedDefinedMask,
                "and no bit outside the defined mask survives the read");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackOrder = true, TrackOrderCount = 1, TrackOrderOwnedMask = 0xFF,
            TrackOrderIds = new[] { 0x11 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            1B 06            // id 27, len 2 + 4*1
            01
            3F               // owned mask, masked to TrackOrderOwnedDefinedMask on WRITE too
            11 00 00 00
            "), ext, m, "the owned mask is masked on write as well — the board-UI overlay discipline");

        // COUNT CLAMP on write: a caller claiming more ids than the cap (or than its own buffer)
        // can never make the record longer than TrackOrderMaxIds allows.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackOrder = true, TrackOrderCount = 99, TrackOrderOwnedMask = 0x01,
            TrackOrderIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            01
            1B 1A            // id 27, len 2 + 4*6 — clamped to the 6-id cap
            06
            01
            01 00 00 00
            02 00 00 00
            03 00 00 00
            04 00 00 00
            05 00 00 00
            06 00 00 00
            "), ext, m, "an over-claimed count is clamped to TrackOrderMaxIds on write");
        t.Equal(39, m, "and the record's worst case really is 2 + 26 = 28 bytes on the tail");

        // LENGTH CLAMP on read: a count that claims more ids than the record actually carries is
        // re-clamped against the LENGTH (never trust the wire) — the surviving id still lands.
        byte[] overOrd = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1B 06 04 01 11 00 00 00");
        t.True(PresenceSerializer.TryRead(overOrd, overOrd.Length, out PresenceState toOver),
               "an over-claimed count still parses");
        t.True(toOver.HasTrackOrder, "the record is delivered");
        t.Equal(1, toOver.TrackOrderCount, "clamped to what the record length can hold");
        t.Equal(0x11, toOver.TrackOrderIds![0], "with the one real id intact");

        // SENTINEL CLAMP on read, and the reason it is not a plain drop: actor id 0 is "none"
        // everywhere in this system, and the owned mask is INDEX-ALIGNED with the ids — so a
        // dropped id has to take its bit with it or ownership shifts onto the wrong character.
        // Here ids[0] is the sentinel and the mask says "ids[1] is mine"; after compaction the
        // surviving id is at index 0 and the mask must read 0x01, not 0x02.
        byte[] holeOrd = Hex.Bytes(
            "31 52 56 47 03 01 80 00 80 00 01 1B 0A 02 02 00 00 00 00 11 00 00 00");
        t.True(PresenceSerializer.TryRead(holeOrd, holeOrd.Length, out PresenceState toHole),
               "a record with a sentinel id still parses");
        t.True(toHole.HasTrackOrder, "and is delivered with the survivor");
        t.Equal(1, toHole.TrackOrderCount, "the sentinel is compacted out");
        t.Equal(0x11, toHole.TrackOrderIds![0], "leaving the real id at index 0");
        t.Equal(0x01, toHole.TrackOrderOwnedMask,
                "and the owned mask is REBUILT over the survivors — a pass-through would have " +
                "moved ownership onto the wrong character");
        byte[] zeroOrd = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1B 06 01 01 00 00 00 00");
        t.True(PresenceSerializer.TryRead(zeroOrd, zeroOrd.Length, out PresenceState toZero),
               "an all-sentinel record still parses the packet");
        t.True(!toZero.HasTrackOrder, "and is simply not delivered");

        // TRUNCATED record (claims 10 payload bytes, delivers 3): tail abandoned, nothing throws.
        byte[] cutOrd = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1B 0A 02 02 11");
        t.True(PresenceSerializer.TryRead(cutOrd, cutOrd.Length, out PresenceState cutOrdS),
               "a truncated order record still parses the packet");
        t.True(!cutOrdS.HasTrackOrder, "and the incomplete record is simply not delivered");

        // AN OLD-STYLE PACKET — no record 27 at all, which is ALSO what every in-window sender
        // produces outside the online card-selection phase. WHAT THE RECEIVER DERIVES FROM IT is
        // the point of this vector: count 0 and mask 0, i.e. NO order override (the mirrored
        // arrangement stands, and outside that phase it is already the owner's, because every
        // client's CompareTo then reduces to the same GetOrderPriority/SubInitiative comparison
        // over the same replicated model) and NO mirrored selection-phase ring (the cue does not
        // exist outside that phase either). The absence IS the release — nothing is ever latched.
        byte[] oldStyleNoOrder = Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            02
            10 05 00 11 00 00 00 // id 16 track hover
            17 05 01 33 00 00 00 // id 23 track selection
            ");
        t.True(PresenceSerializer.TryRead(oldStyleNoOrder, oldStyleNoOrder.Length, out PresenceState toOld),
               "a build-86 packet with records 16 and 23 but no 27 parses unchanged");
        t.True(toOld.HasTrackHover && toOld.HasTrackSelection,
               "its own records are delivered exactly as before");
        t.True(!toOld.HasTrackOrder, "record 27 is absent");
        t.Equal(0, toOld.TrackOrderCount, "so the receiver derives NO order override…");
        t.Equal(0, toOld.TrackOrderOwnedMask, "…and NO owned set, hence no mirrored selection ring");

        // IDLE IDENTITY: outside the card-selection phase the sampler returns 0, so no record —
        // and the packet is byte-identical to what a sender predating record 27 produces.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 2 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 02"), ext, m,
               "no track order -> no record: byte-identical to a pre-record-27 sender");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackOrder = true, TrackOrderCount = 0, TrackOrderOwnedMask = 0x01,
            TrackOrderIds = new[] { 0x11 },
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), ext, m,
               "an EMPTY order does not even open the tail");

        // ID ORDER: record 27 rides LAST, behind 16, 22, 23 and 24 — the tail is written in
        // ascending id order and a peer predating any of them steps over it by length. All four
        // track-widget records must stay distinct facts.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasTrackHover = true, TrackHoverActorId = 0x11,
            HasCharFocus = true, CharFocusActorId = 0x22, CharFocusOwnsAttention = true,
            HasTrackSelection = true, TrackSelectionCount = 1, TrackSelectionIds = new[] { 0x33 },
            HasTrackOrder = true, TrackOrderCount = 1, TrackOrderOwnedMask = 0x01,
            TrackOrderIds = new[] { 0x44 },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01
            80 00
            80 00
            04                      // tail: 4 records, in id order
            10 05 00 11 00 00 00    // id 16 track hover
            16 05 01 22 00 00 00    // id 22 character focus
            17 05 01 33 00 00 00    // id 23 track selection
            1B 06 01 01 44 00 00 00 // id 27 track order: one player entry, and it is theirs
            "), ext, m, "record 27 rides the tail behind 16, 22 and 23 (id order 16, 22, 23, 27)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState toCombo), "and the combo parses");
        t.True(toCombo.HasTrackHover && toCombo.HasCharFocus && toCombo.HasTrackSelection
               && toCombo.HasTrackOrder, "with all four track records delivered");
        t.Equal(0x11, toCombo.TrackHoverActorId, "the hover id is its own");
        t.Equal(0x33, toCombo.TrackSelectionIds![0], "the selection id is its own");
        t.Equal(0x44, toCombo.TrackOrderIds![0],
                "and the order id is confused with none of them — four different facts");


        // -- 7s. EMPTY-FAN PLACARD (record 14, byte 1, bit 4) ------------------------------
        // The one control-board-adjacent display that had no mirror: the hand-anchored "Keine
        // Handkarten" plate. It rides record 14's reserved nibble as ONE bit, and — this is the
        // point of these vectors — it OPENS the record on its own, because the placard can be up
        // while nothing at all is hovered or selected. It could never be inferred: 0 hand cards
        // with the fan closed is also every idle player (the trap in INVARIANTS-Net-Rig.md).
        t.Case("7s. extras, empty-fan placard bit (record 14 byte 1 bit 4)");
        m = PresenceSerializer.Write(new PresenceState { EmptyFanHint = true }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0E 02 03 10      // id 14, len 2: hover SENTINEL (no hover), byte1 = bit4 only
            "), ext, m, "the placard alone opens record 14 with the no-hover sentinel in byte 0");
        t.Equal(15, m, "header 7 + count 1 + block 2 + tail 1 + 4 = 15 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hint), "and it parses");
        t.True(hint.EmptyFanHint, "the placard bit is delivered");
        t.True(!hint.HasHalfHover,
               "WITHOUT claiming a half hover — a placard-only record must not light a hover " +
               "state nobody is in, which is why bit 4 is read outside the hover/selection drop");
        t.Equal(NetProtocol.HalfSelectNone, hint.HalfSelect0, "and no selection is invented");

        // The placard rides ALONGSIDE a live hover + selection in the same two bytes, at no cost.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 1, HalfHoverTop = true,
            HalfSelect0 = NetProtocol.HalfSelectBottom,
            EmptyFanHint = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            0E 02 05 12      // id 14: hover slot1|top; byte1 = sel0 BOTTOM (2) | placard (0x10)
            "), ext, m, "the placard bit costs ZERO bytes when the record is already riding");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hintBoth), "and it parses");
        t.True(hintBoth.EmptyFanHint && hintBoth.HasHalfHover && hintBoth.HalfHoverActive,
               "with the placard and the hover both delivered");
        t.Equal(1, hintBoth.HalfHoverSlot, "the hover slot is untouched by the new bit");
        t.Equal(NetProtocol.HalfSelectBottom, hintBoth.HalfSelect0,
                "and so is the selection field beside it");

        // NO placard, no hover, no selection -> no record at all: byte-identical to the build
        // before the bit existed.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 3 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 03"), ext, m,
               "no placard and nothing lit -> no record: byte-identical to a pre-bit sender");

        // An OLD-STYLE packet — a sender that predates the bit writes byte 1's bits 4..7 as 0.
        // The receiver must DERIVE 'no placard' from that, never a stale one.
        byte[] preBitPacket = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 04 00");
        t.True(PresenceSerializer.TryRead(preBitPacket, preBitPacket.Length, out PresenceState old14),
               "a pre-bit record 14 still parses");
        t.True(old14.HasHalfHover && old14.HalfHoverActive && old14.HalfHoverTop,
               "with its hover intact");
        t.True(!old14.EmptyFanHint,
               "and NO placard derived — absence means 'down', which is what those builds render");

        // RESERVED bits 5..7 set by a future sender: masked off, and they must not be mistaken
        // for the placard or bleed into the selection fields.
        byte[] futBits = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 03 F0");
        t.True(PresenceSerializer.TryRead(futBits, futBits.Length, out PresenceState fut14),
               "a record with reserved bits 5..7 set parses");
        t.True(fut14.EmptyFanHint, "bit 4 is still read");
        t.True(!fut14.HasHalfHover, "and bits 5..7 light nothing at all");

        // -- 7t. BOARD TUNING (extension record 28) ----------------------------------------
        // The owner's OWN dial positions, SPARSE: a field only where the value differs from the
        // shipped default for their synced style, and NO RECORD AT ALL when nothing differs.
        // That last case is the important one — it is the whole economic argument for the record.
        t.Case("7t. extras, board-tuning record");

        // THE DEFAULT CASE FIRST: an untuned player produces a zero-length payload, which must
        // not even open the extension tail. Byte-for-byte a pre-record sender.
        m = PresenceSerializer.Write(new PresenceState
        {
            HandCardCount = 4,
            HasBoardTuning = true,          // the sampler ran…
            BoardTuningBytes = System.Array.Empty<byte>(),
            BoardTuningLength = 0,          // …and found every dial at its default
        }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 04"), ext, m,
               "EVERY DIAL AT THE DEFAULT -> no record, no tail, no block: byte-identical to a " +
               "pre-record-28 sender (the whole reason this record is affordable)");
        t.Equal(8, m, "and the packet is the bare 8-byte extras packet");

        // ONE MOVED DOCK, AS A PAGE. Since 2026-08-09 the record's payload is one PAGE of the
        // sender's tuning: [pageIndex][pageCount][sig lo][sig hi][idLo][idHi][n][n × field]. With
        // one dial there is exactly one page, so it claims the WHOLE id space (idLo 0, idHi 255) and
        // is a complete snapshot on its own.
        //   (0.012, -0.034, 0.005) m -> 120, -340, 50 tenth-mm -> 78 00 / AC FE / 32 00.
        byte[] oneField = { NetProtocol.TuneObjectivesOffset, 0x78, 0x00, 0xAC, 0xFE, 0x32, 0x00 };
        ushort oneSig = BoardTunePages.Signature(oneField, 0, oneField.Length);
        var onePage = new byte[255];
        int onePageLen = BoardTunePages.WritePage(oneField, oneField.Length, 0, oneSig, onePage);
        t.Equal(14, onePageLen, "one dial is a 14-byte page: 7 header + 1 id + 6 value");
        t.Equal(1, onePage[NetProtocol.BoardTunePageCountAt], "and it is the only page");
        t.Equal(0, onePage[NetProtocol.BoardTunePageIdLoAt], "page 0 always claims from id 0…");
        t.Equal(255, onePage[NetProtocol.BoardTunePageIdHiAt], "…and the LAST page always to 255");
        t.Equal(1, onePage[NetProtocol.BoardTunePageFieldCountAt], "carrying one field");

        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTuning = true, BoardTuningBytes = onePage, BoardTuningLength = onePageLen,
        }, ext);
        t.Wire(Hex.Bytes($@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            1C 0E            // id 28 (0x1C), len 14
            00 01            //   page 0 of 1
            {oneSig & 0xFF:X2} {oneSig >> 8:X2}   //   generation signature (FNV-1a of the field run)
            00 FF            //   this page is a COMPLETE statement about ids 0..255
            01               //   1 field
            01 78 00 AC FE 32 00   //   id 1 (objectives offset): 120, -340, 50 tenth-mm
            "), ext, m, "one moved dock rides one page: TLV header 2 + page header 7 + id 1 + value 6");
        t.Equal(27, m, "header 7 + count 1 + block 2 + tail 1 + 16 = 27 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState tune), "and it parses");
        t.True(tune.HasBoardTuning, "the tuning page is delivered");
        t.Equal(onePageLen, tune.BoardTuningLength, "with its payload length intact");

        // THE PAGE IS NOT WHAT A CONSUMER READS — the ASSEMBLER's output is. One page of one is a
        // complete generation, so a single Accept publishes.
        var solo = new BoardTunePageAssembler();
        t.True(solo.Accept(tune.BoardTuningBytes, 0, tune.BoardTuningLength),
               "a single-page generation converges on its first packet");
        t.Equal(1, solo.PagesSeen, "one page held…");
        t.Equal(1, solo.PageCount, "…of one");
        t.Equal(8, solo.AssembledLength, "assembled as [n][id][6-byte value] = 8 bytes");
        t.Equal(1, solo.Assembled[0], "with the field count re-stated at the front");
        Vector3 objOff = NetProtocol.BoardTuneVector(
            solo.Assembled, 0, solo.AssembledLength,
            NetProtocol.TuneObjectivesOffset, new Vector3(9f, 9f, 9f));
        t.Equal(0.012f, objOff.x, "x decodes at 0.1 mm resolution");
        t.Equal(-0.034f, objOff.y, "y decodes signed");
        t.Equal(0.005f, objOff.z, "z decodes");
        t.True(!solo.Accept(tune.BoardTuningBytes, 0, tune.BoardTuningLength),
               "and re-cycling the same page publishes NOTHING — a stable sender must not make its " +
               "peers tear down and rebuild a control board five times a second");

        // A FIELD THAT IS NOT IN THE RECORD reads as the caller's own shipped default — which is
        // what makes a SPARSE record correct: absence means "the value you already have".
        Vector3 absent = NetProtocol.BoardTuneVector(
            solo.Assembled, 0, solo.AssembledLength,
            NetProtocol.TunePileOffset, new Vector3(1f, 2f, 3f));
        t.True(absent == new Vector3(1f, 2f, 3f),
               "an absent field yields the receiver's own default, never zero");

        // ALL FOUR VALUE WIDTHS in one record, in ascending id order (the layout contract).
        //   id  15 vec3   asset offset (0, -0.11, 0.08) -> 0, -1100, 800
        //   id  70 length card width 0.0700 m           -> 700
        //   id 128 factor objectives scale 1.250        -> 1250
        //   id 192 angle  asset pitch 57.00 deg         -> 5700
        //   id 224 count  fan max hand for curve 8      -> 8
        byte[] mixedFields =
        {
            NetProtocol.TuneAssetOffset,       0x00, 0x00, 0xB4, 0xFB, 0x20, 0x03,
            NetProtocol.TuneCardWidth,         0xBC, 0x02,
            NetProtocol.TuneObjectivesScale,   0xE2, 0x04,
            NetProtocol.TuneAssetPitch,        0x44, 0x16,
            NetProtocol.TuneFanMaxHandForCurve, 0x08,
        };
        ushort mixSig = BoardTunePages.Signature(mixedFields, 0, mixedFields.Length);
        var mixPage = new byte[255];
        int mixLen = BoardTunePages.WritePage(mixedFields, mixedFields.Length, 0, mixSig, mixPage);
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTuning = true, BoardTuningBytes = mixPage, BoardTuningLength = mixLen,
        }, ext);
        t.Wire(Hex.Bytes($@"
            31 52 56 47 03 01 80 00
            80 00
            01
            1C 19            // id 28, len 25
            00 01            //   page 0 of 1
            {mixSig & 0xFF:X2} {mixSig >> 8:X2}   //   generation
            00 FF 05         //   complete for ids 0..255, 5 fields
            0F 00 00 B4 FB 20 03   //   id 15 vec3  : asset offset (0, -0.11, 0.08)
            46 BC 02               //   id 70 length: card width 70.0 mm
            80 E2 04               //   id 128 factor: objectives scale 1.250
            C0 44 16               //   id 192 angle : asset pitch 57.00 deg
            E0 08                  //   id 224 count : fan max hand for curve = 8
            "), ext, m, "all four value widths ride one page, ids ascending");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState mixTune), "and it parses");
        var mixAsm = new BoardTunePageAssembler();
        t.True(mixAsm.Accept(mixTune.BoardTuningBytes, 0, mixTune.BoardTuningLength),
               "and assembles");
        byte[] mixedTune = mixAsm.Assembled;
        int mixedTuneLen = mixAsm.AssembledLength;
        t.Equal(mixedFields.Length + 1, mixedTuneLen,
                "the assembled payload is the field run behind ONE count byte — the exact shape " +
                "RemoteBoardTuning has always read, which is why no consumer of a dial changed");
        Vector3 asset = NetProtocol.BoardTuneVector(mixedTune, 0, mixedTuneLen,
                                                    NetProtocol.TuneAssetOffset, Vector3.zero);
        t.Equal(-0.11f, asset.y, "the bronze board's shipped mesh offset survives the round trip");
        t.Equal(0.08f, asset.z, "on both moved axes");
        t.Equal(0.07f, NetProtocol.BoardTuneLength(mixedTune, 0, mixedTuneLen,
                                                   NetProtocol.TuneCardWidth, 0f),
                "a LENGTH field decodes in tenth-millimetres");
        t.Equal(1.25f, NetProtocol.BoardTuneFactor(mixedTune, 0, mixedTuneLen,
                                                   NetProtocol.TuneObjectivesScale, 0f),
                "a FACTOR field decodes in thousandths");
        t.Equal(57f, NetProtocol.BoardTuneAngle(mixedTune, 0, mixedTuneLen,
                                                NetProtocol.TuneAssetPitch, 0f),
                "an ANGLE field decodes in hundredth-degrees");
        t.Equal(8, NetProtocol.BoardTuneCount(mixedTune, 0, mixedTuneLen,
                                              NetProtocol.TuneFanMaxHandForCurve, 0),
                "a COUNT field is one plain byte");
        t.Equal(2f, NetProtocol.BoardTuneFactor(mixedTune, 0, mixedTuneLen,
                                                NetProtocol.TuneFanCurvePower, 2f),
                "and every field NOT in the record still reads as the receiver's own default");

        // MASKED READ: a field is found by WALKING the sparse list at the widths the ids declare.
        // Here a 6-byte vec3 sits before a 2-byte factor whose VALUE BYTES (0x80 0xE2) would look
        // like an id-128 field if the walk ever mis-stepped — the reason the widths are fixed by
        // id RANGE and not guessed.
        t.Equal(1.25f, NetProtocol.BoardTuneFactor(mixedTune, 0, mixedTuneLen,
                                                   NetProtocol.TuneObjectivesScale, 0f),
                "the walk steps over a 6-byte vec3 to reach the factor behind it");
        t.Equal(-1f, NetProtocol.BoardTuneFactor(mixedTune, 0, mixedTuneLen,
                                                 NetProtocol.TuneElementsScale, -1f),
                "and a factor id that is NOT present is not matched by another field's value byte");

        // A RESERVED id (no defined width) ENDS the walk: fields before it survive, nothing past
        // it is guessed at. Cannot happen between same-build peers; it must not corrupt if it does.
        byte[] reserved =
        {
            3,
            NetProtocol.TuneCardWidth, 0xBC, 0x02,
            0xFF, 0x11, 0x22,                        // id 255: RESERVED, width unknown
            NetProtocol.TuneObjectivesScale, 0xE2, 0x04,
        };
        t.Equal(0.07f, NetProtocol.BoardTuneLength(reserved, 0, reserved.Length,
                                                   NetProtocol.TuneCardWidth, 0f),
                "a field BEFORE an unknown-width id is delivered");
        t.Equal(9f, NetProtocol.BoardTuneFactor(reserved, 0, reserved.Length,
                                                NetProtocol.TuneObjectivesScale, 9f),
                "and everything behind it degrades to the default rather than being mis-parsed");

        // TRUNCATION: the record claims 14 payload bytes and delivers 4. The tail is abandoned
        // mid-record, the packet still parses, and nothing is delivered from the torn record.
        byte[] cutTune = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1C 0E 00 01 78 00");
        t.True(PresenceSerializer.TryRead(cutTune, cutTune.Length, out PresenceState cutT),
               "a truncated tuning record still parses the packet");
        t.True(!cutT.HasBoardTuning, "and the incomplete record is simply not delivered");

        // A RECORD SHORTER THAN ONE PAGE HEADER is not a page at all and is refused by LENGTH,
        // before a byte of it is interpreted — the floor moved from 1 to 7 when the record was
        // paged, and it is the reason BoardTuneMinRecordBytes and BoardTuneAssembledMinBytes are
        // deliberately two different constants (a wire page vs. an assembled result).
        byte[] stubPage = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1C 06 00 01 11 22 00 FF");
        t.True(PresenceSerializer.TryRead(stubPage, stubPage.Length, out PresenceState stubT),
               "a 6-byte record-28 payload parses as a packet");
        t.True(!stubT.HasBoardTuning, "…but is not delivered: one byte short of a page header");

        // A FIELD truncated INSIDE an otherwise well-formed record: the walk stops at the short
        // field, the fields before it stand. (The record's own TLV length bounds every read.)
        byte[] shortField = { 2, NetProtocol.TuneCardWidth, 0xBC, 0x02, NetProtocol.TunePileOffset, 0x01 };
        t.Equal(0.07f, NetProtocol.BoardTuneLength(shortField, 0, shortField.Length,
                                                   NetProtocol.TuneCardWidth, 0f),
                "the complete field before a truncated one is still read");
        t.True(NetProtocol.BoardTuneVector(shortField, 0, shortField.Length,
                                           NetProtocol.TunePileOffset, Vector3.one) == Vector3.one,
               "and the truncated field yields the default, never a torn vector");

        // A ZERO-FIELD PAGE IS LEGAL AND MUST BE DELIVERED — the exact reversal the paging brought,
        // and the one place where reading the old test would now be actively misleading. Before
        // paging, "zero fields" meant an empty RECORD, indistinguishable from an untuned sender, and
        // it was dropped. A zero-field PAGE says something else entirely: "nothing is tuned in MY id
        // range". Without it a generation could never converge for a player who tuned only offsets,
        // because the page covering the factors would have nothing to say and would vanish. The
        // untuned case is still expressed the way it always was: by omitting the record.
        byte[] emptyRec = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 1C 07 01 02 34 12 40 FF 00");
        t.True(PresenceSerializer.TryRead(emptyRec, emptyRec.Length, out PresenceState emptyT),
               "a zero-FIELD page parses");
        t.True(emptyT.HasBoardTuning,
               "…and is DELIVERED: 'nothing is tuned in ids 64..255' is a statement, not a silence");
        t.Equal(7, emptyT.BoardTuningLength, "a bare page header, no fields behind it");

        // THE ONE-BYTE TLV CEILING, STILL PINNED — but it is now a PAGE's ceiling, not a player's.
        // The extension tail writes a record's length in a SINGLE byte and the writer refuses more,
        // so 255 must still go out and 256 must still be refused. What CHANGED on 2026-08-09 is what
        // that boundary means: before paging it was the total capacity of a player's tuning, the
        // record stood at exactly 255, and the next dial was silently undeliverable — for the player
        // who had moved every dial, i.e. the least likely person to be testing. Now it bounds one
        // page and another page follows, so this test guards the FRAMING and no longer guards a
        // capacity anybody can reach. The convergence tests below are what guard the capacity.
        var maxTune = new byte[255];
        maxTune[NetProtocol.BoardTunePageIndexAt] = 0;
        maxTune[NetProtocol.BoardTunePageCountAt] = 1;
        maxTune[NetProtocol.BoardTunePageIdHiAt] = 255;
        maxTune[NetProtocol.BoardTunePageFieldCountAt] = 1;
        maxTune[NetProtocol.BoardTunePageHeaderBytes] = NetProtocol.TuneCardWidth;
        maxTune[NetProtocol.BoardTunePageHeaderBytes + 1] = 0xBC;
        maxTune[NetProtocol.BoardTunePageHeaderBytes + 2] = 0x02;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTuning = true, BoardTuningBytes = maxTune, BoardTuningLength = maxTune.Length,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState maxT),
               "a 255-byte page — a page's own worst case — is written and parses");
        t.Equal(255, maxT.BoardTuningLength, "at its full length");
        var overTune = new byte[256];
        overTune[NetProtocol.BoardTunePageCountAt] = 1;
        overTune[NetProtocol.BoardTunePageIdHiAt] = 255;
        m = PresenceSerializer.Write(new PresenceState
        {
            HandCardCount = 4,
            HasBoardTuning = true, BoardTuningBytes = overTune, BoardTuningLength = overTune.Length,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState overT), "a 256-byte payload parses…");
        t.True(!overT.HasBoardTuning,
               "…as a packet WITHOUT the record: one byte over the TLV ceiling drops that PAGE. " +
               "Unreachable by construction now (header 7 + BoardTunePageMaxFieldBytes 248 = 255), " +
               "and the guard stays precisely because of that — a paging bug must cost one page, " +
               "not a length byte that wrapped and tore every record behind it in the tail");

        // The two swap dials that ride the one-byte COUNT width to stay under that ceiling: whole
        // degrees and whole percent, decoded in their own units by RemoteBoardTuning.
        byte[] swapCounts =
        {
            2,
            NetProtocol.TuneFanSwapSpin, 58,
            NetProtocol.TuneFanSwapOverlapPercent, 66,
        };
        t.Equal(58, NetProtocol.BoardTuneCount(swapCounts, 0, swapCounts.Length,
                                               NetProtocol.TuneFanSwapSpin, 0),
                "the swap's counter-roll rides the COUNT width as whole degrees");
        t.Equal(66, NetProtocol.BoardTuneCount(swapCounts, 0, swapCounts.Length,
                                               NetProtocol.TuneFanSwapOverlapPercent, 0),
                "and its overlap as whole percent (0.66 -> 66), un-quantized on the receiver");

        // QUANTIZATION CLAMPS (never trust a config, either): values past each encoding's range
        // saturate instead of wrapping into a wrong sign.
        t.Equal(short.MaxValue, NetProtocol.EncodeTuneLength(99f), "a 99 m length clamps");
        t.Equal(short.MinValue, NetProtocol.EncodeTuneLength(-99f), "and so does a negative one");
        t.Equal((short)0, NetProtocol.EncodeTuneLength(float.NaN), "NaN encodes as 0, never garbage");
        t.Equal(short.MaxValue, NetProtocol.EncodeTuneFactor(999f), "a runaway factor clamps");
        t.Equal(short.MaxValue, NetProtocol.EncodeTuneAngle(9999f), "and a runaway angle clamps");
        t.Equal(0.0001f, NetProtocol.DecodeTuneLength(1), "1 tenth-mm is the length step");
        t.Equal(0.001f, NetProtocol.DecodeTuneFactor(1), "0.001 is the factor step");
        t.Equal(0.01f, NetProtocol.DecodeTuneAngle(1), "0.01 deg is the angle step");

        // ID ORDER ON THE WIRE: record 28 is appended LAST, behind record 24, exactly like every
        // record before it — an older reader steps over it by its length.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 0, HalfHoverTop = true,
            HasBoardTuning = true, BoardTuningBytes = onePage, BoardTuningLength = onePageLen,
        }, ext);
        t.Wire(Hex.Bytes($@"
            31 52 56 47 03 01 80 00
            80 00
            02
            0E 02 04 00      // id 14 first
            1C 0E 00 01 {oneSig & 0xFF:X2} {oneSig >> 8:X2} 00 FF 01 01 78 00 AC FE 32 00  // id 28 behind it
            "), ext, m, "record 28 rides the tail LAST, in id order behind record 14");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ordered), "and the combo parses");
        t.True(ordered.HasHalfHover && ordered.HasBoardTuning,
               "with both records delivered");

        // An UNKNOWN record ahead of it (a future sender's field) is stepped over by length. THIS is
        // the compatibility property the paging deliberately did NOT break: a reader that does not
        // know id 28 still skips it correctly. What paging DID break is a reader that knows the OLD
        // id-28 LAYOUT — it would read the page header as a field count and an id. That is
        // acceptable for one reason, recorded here as well as at the format: the ModBuild handshake
        // is BLOCKING (VersionGuard), so peers on different builds never exchange an interpreted
        // packet and an old reader can never meet a new page.
        byte[] futureTune = Hex.Bytes($@"
            31 52 56 47 03 01 80 00
            80 00
            02
            63 03 DE AD BE   // id 99, len 3 -- unknown to this build
            1C 0E 00 01 {oneSig & 0xFF:X2} {oneSig >> 8:X2} 00 FF 01 01 78 00 AC FE 32 00
            ");
        t.True(PresenceSerializer.TryRead(futureTune, futureTune.Length, out PresenceState futT),
               "a packet with an unknown record ahead of the tuning record parses");
        t.True(futT.HasBoardTuning, "and the tuning behind it is read");
        var futAsm = new BoardTunePageAssembler();
        t.True(futAsm.Accept(futT.BoardTuningBytes, 0, futT.BoardTuningLength), "and assembles");
        t.Equal(0.012f, NetProtocol.BoardTuneVector(futAsm.Assembled, 0, futAsm.AssembledLength,
                                                    NetProtocol.TuneObjectivesOffset, Vector3.zero).x,
                "with its field intact");

        // -- 7u. BOARD TUNING: THE PAGING ITSELF ------------------------------------------------
        // The ruling this whole mechanism serves, verbatim (user, 2026-08-09):
        //   "Ändert ein Spieler also die Positionen für sich selber, so sollen alle anderen diese
        //    Position bei seinem board auch sehen."
        // A guarantee cannot have a capacity ceiling. Record 28 had one — 255 payload bytes, the
        // extension tail's one-byte length — and it stood at EXACTLY 255, so the next feature's
        // dials could not ride at all and the failure was invisible from inside a headset. These
        // vectors pin the replacement: page framing, tiling, convergence, generations, and the
        // boundary the old scheme used to drop at.
        t.Case("7u. extras, board tuning is PAGED (no capacity ceiling)");

        // A FIELD LIST BIGGER THAN THE OLD CEILING. 40 vec3 fields = 280 bytes — 25 past the 255 the
        // old scheme could express at all, and a size at which the old sampler refused the WHOLE
        // record and every peer drew the shipped defaults. It must now split and converge.
        var bigFields = new byte[40 * 7];
        for (int f = 0; f < 40; f++)
        {
            bigFields[f * 7] = (byte)(f + 1);              // ids 1..40, ascending (the layout contract)
            bigFields[f * 7 + 1] = (byte)(f + 1);          // x = (f+1) tenth-mm, distinct per field
            bigFields[f * 7 + 3] = 0x10;                   // y = 4096 tenth-mm
            bigFields[f * 7 + 5] = 0x20;                   // z = 8192 tenth-mm
        }
        t.Equal(280, bigFields.Length, "40 vec3 dials are 280 field bytes — past the OLD 255 ceiling");
        ushort bigSig = BoardTunePages.Signature(bigFields, 0, bigFields.Length);
        int bigPages = BoardTunePages.PageCount(bigFields, bigFields.Length);
        t.Equal(2, bigPages, "and they split into two pages, neither of which the tail can refuse");

        var page = new byte[255];
        var asm = new BoardTunePageAssembler();

        int p0 = BoardTunePages.WritePage(bigFields, bigFields.Length, 0, bigSig, page);
        t.Equal(252, p0,
                "page 0 fills to 7 header + 35 × 7 = 252 — three bytes short of the tail's 255 " +
                "ceiling, because the split is greedy at FIELD boundaries and a 36th 7-byte vec3 " +
                "would not fit the 248-byte budget. Never truncated mid-field, which is what lets " +
                "the receiver validate a page whole");
        t.Equal(0, page[NetProtocol.BoardTunePageIdLoAt], "page 0 claims from id 0");
        t.Equal(35, page[NetProtocol.BoardTunePageIdHiAt],
                "…to the id of its LAST field, so page 1 can claim from 36 with no gap");
        t.Equal(35, page[NetProtocol.BoardTunePageFieldCountAt], "carrying 35 of the 40 fields");
        t.True(!asm.Accept(page, 0, p0),
               "one page of two publishes NOTHING — a half tuning is never drawn");
        t.Equal(0, asm.AssembledLength, "and there is nothing to draw from yet: shipped defaults");
        t.Equal(1, asm.PagesSeen, "though the page IS held");

        int p1 = BoardTunePages.WritePage(bigFields, bigFields.Length, 1, bigSig, page);
        t.Equal(42, p1, "page 1 is 7 header + 5 × 7 = 42 bytes");
        t.Equal(36, page[NetProtocol.BoardTunePageIdLoAt], "claiming from one past page 0's last id");
        t.Equal(255, page[NetProtocol.BoardTunePageIdHiAt],
                "…to 255, so the two ranges TILE the whole id space and the pair is a snapshot");
        t.True(asm.Accept(page, 0, p1), "and the second page COMPLETES the generation");
        t.Equal(2, asm.PagesSeen, "both pages held");
        t.Equal(281, asm.AssembledLength, "assembled: 1 count byte + all 280 field bytes");
        t.Equal(40, asm.Assembled[0], "all 40 dials, none dropped — which the old scheme could not do");

        // EVERY field survives the round trip, first and last included. The last one is the point:
        // it lived on page 1, i.e. past the byte where the old record stopped existing.
        t.Equal(0.0001f, NetProtocol.BoardTuneVector(asm.Assembled, 0, asm.AssembledLength,
                                                     1, Vector3.zero).x,
                "the first dial (page 0) decodes");
        t.Equal(0.0040f, NetProtocol.BoardTuneVector(asm.Assembled, 0, asm.AssembledLength,
                                                     40, Vector3.zero).x,
                "and so does the FORTIETH — the dial the 255-byte ceiling used to make undeliverable");

        // CONVERGENCE FROM A COLD JOIN, IN ANY ORDER. A peer joining mid-cycle meets page 1 first;
        // the sender keeps cycling, so page 0 follows and the generation completes. This is the
        // whole convergence guarantee: at most pageCount packets, whenever you arrive.
        var late = new BoardTunePageAssembler();
        int q1 = BoardTunePages.WritePage(bigFields, bigFields.Length, 1, bigSig, page);
        t.True(!late.Accept(page, 0, q1), "a peer joining mid-cycle meets page 1 and draws nothing yet");
        int q0 = BoardTunePages.WritePage(bigFields, bigFields.Length, 0, bigSig, page);
        t.True(late.Accept(page, 0, q0),
               "and converges on the very next pass of the cycle — no handshake, no request");
        t.Equal(281, late.AssembledLength, "to the identical complete payload");
        t.Equal(40, late.Assembled[0], "with every dial present regardless of arrival order");

        // A LOST PAGE IS REPAIRED BY THE CYCLE, not by a retransmit protocol: the sender re-sends
        // every page forever, so a drop costs one lap and nothing else.
        var lossy = new BoardTunePageAssembler();
        int r1 = BoardTunePages.WritePage(bigFields, bigFields.Length, 1, bigSig, page);
        t.True(!lossy.Accept(page, 0, r1), "page 1 arrives");
        t.True(!lossy.Accept(page, 0, r1), "page 1 arrives AGAIN (page 0 was dropped in flight)");
        t.Equal(1, lossy.PagesSeen, "a repeat is not progress — the page count is honest");
        int r0 = BoardTunePages.WritePage(bigFields, bigFields.Length, 0, bigSig, page);
        t.True(lossy.Accept(page, 0, r0), "and the next lap delivers page 0 and completes it");

        // A NEW GENERATION MID-CYCLE: the owner drags a dial while their pages are in flight. The
        // two generations must never be blended, and the peer's board must not flicker back to the
        // shipped defaults while the new one arrives — so the PREVIOUS complete assembly stands
        // until the new one is whole.
        var changed = new byte[bigFields.Length];
        System.Array.Copy(bigFields, changed, bigFields.Length);
        changed[1] = 99;                                    // one dial moved, on page 0
        ushort newSig = BoardTunePages.Signature(changed, 0, changed.Length);
        t.True(newSig != bigSig, "a moved dial is a new generation (the digest differs)");

        int n0 = BoardTunePages.WritePage(changed, changed.Length, 0, newSig, page);
        t.True(!asm.Accept(page, 0, n0),
               "page 0 of the NEW generation publishes nothing — half of it is still the old one");
        t.Equal(281, asm.AssembledLength, "and the OLD complete assembly still stands…");
        t.Equal(0.0001f, NetProtocol.BoardTuneVector(asm.Assembled, 0, asm.AssembledLength,
                                                     1, Vector3.zero).x,
                "…so the peer's board keeps the last coherent picture instead of flickering to defaults");
        int n1 = BoardTunePages.WritePage(changed, changed.Length, 1, newSig, page);
        t.True(asm.Accept(page, 0, n1), "the second page completes the new generation…");
        t.Equal(0.0099f, NetProtocol.BoardTuneVector(asm.Assembled, 0, asm.AssembledLength,
                                                     1, Vector3.zero).x,
                "…and the moved dial lands, in one step, never half-applied");
        t.Equal(newSig, asm.Generation, "the assembler now tracks the new generation");

        // A DIAL RETURNING TO ITS DEFAULT is expressed by ABSENCE, the record's oldest rule, and
        // paging must not have broken it: the page re-states its whole range every time, so a field
        // that stops being sampled simply stops appearing and the reader falls back.
        var fewer = new byte[bigFields.Length - 7];
        System.Array.Copy(bigFields, 0, fewer, 0, fewer.Length);   // drop the 40th dial
        ushort fewerSig = BoardTunePages.Signature(fewer, 0, fewer.Length);
        int fp = BoardTunePages.PageCount(fewer, fewer.Length);
        for (int k = 0; k < fp; k++)
        {
            int len = BoardTunePages.WritePage(fewer, fewer.Length, k, fewerSig, page);
            asm.Accept(page, 0, len);
        }
        t.Equal(39, asm.Assembled[0], "the un-tuned dial is gone from the assembly…");
        t.Equal(7.5f, NetProtocol.BoardTuneVector(asm.Assembled, 0, asm.AssembledLength,
                                                  40, new Vector3(7.5f, 0f, 0f)).x,
                "…and reads as the receiver's own shipped default again, never as a stale value");

        // THE SENDER'S CYCLE. Update() is the change detector as well as the splitter, and a change
        // restarts at page 0 — which is what makes the convergence bound measurable from the DRAG
        // rather than from wherever the cursor happened to be.
        var sender = new BoardTunePageSender();
        t.True(sender.Update(bigFields, bigFields.Length), "a first sample is a change");
        t.True(!sender.Update(bigFields, bigFields.Length),
               "re-sampling an unchanged config is NOT — otherwise every packet would log and " +
               "restart the cycle, and the board would rebuild at 5 Hz on both peers");
        t.Equal(2, sender.PageCount, "it splits into the same two pages");
        t.Equal(40, sender.FieldCount, "over 40 dials");
        t.True(!sender.Overflowed, "with nothing refused");

        int s0 = sender.NextPage(page);
        t.Equal(0, page[NetProtocol.BoardTunePageIndexAt], "the cycle starts at page 0");
        int s1 = sender.NextPage(page);
        t.Equal(1, page[NetProtocol.BoardTunePageIndexAt], "then page 1");
        int s2 = sender.NextPage(page);
        t.Equal(0, page[NetProtocol.BoardTunePageIndexAt],
                "then WRAPS to 0 — forever, which is what repairs a lost page and converges a " +
                "peer who joined at any moment");
        t.Equal(s0, s2, "and the wrapped page is byte-identical in length to the first");
        t.True(s1 < s0, "page 1 is the short one (5 fields against 35)");

        sender.NextPage(page);                                   // mid-cycle: cursor is at page 1
        t.True(sender.Update(changed, changed.Length), "a moved dial is a change…");
        sender.NextPage(page);
        t.Equal(0, page[NetProtocol.BoardTunePageIndexAt],
                "…and RESTARTS the cycle at page 0, so the bound is measured from the drag");

        // AN UNTUNED PLAYER SENDS NOTHING AT ALL. This is the record's whole economic argument and
        // paging must not have cost it: no fields ⇒ no pages ⇒ no record ⇒ the extension tail does
        // not even open, and the packet is byte-identical to a pre-record sender's (pinned above).
        var quiet = new BoardTunePageSender();
        t.True(!quiet.Update(System.Array.Empty<byte>(), 0), "an untuned sample is not a change…");
        t.Equal(0, quiet.PageCount, "…there are no pages…");
        t.Equal(0, quiet.NextPage(page), "…and no page is written");

        // STRUCTURAL REFUSAL, LOUDLY. Every bound the assembler enforces is derived from the field-id
        // space, so a same-build peer cannot trip one; the point is that if one is ever tripped, the
        // board is drawn from the last COMPLETE tuning and the receiver KNOWS, rather than being
        // drawn from a half-parsed one and not knowing.
        var strict = new BoardTunePageAssembler();
        byte[] badIndex = { 3, 2, 0x11, 0x22, 0x00, 0xFF, 0x00 };      // page 3 of 2
        t.True(!strict.Accept(badIndex, 0, badIndex.Length), "a page index past the page count…");
        t.True(strict.Refused, "…is refused, and says so");
        byte[] tooManyPages = { 0, 200, 0x11, 0x22, 0x00, 0xFF, 0x00 };
        t.True(!strict.Accept(tooManyPages, 0, tooManyPages.Length),
               "a generation claiming 200 pages is refused — the receiver's accumulator is bounded " +
               "by the ID SPACE, never sized from a number the wire supplied");
        byte[] unknownWidth = { 0, 1, 0x11, 0x22, 0x00, 0xFF, 1, 0xFF, 0x00, 0x00 };
        t.True(!strict.Accept(unknownWidth, 0, unknownWidth.Length),
               "a field whose RESERVED id has no declared width is refused whole, never guessed at");
        byte[] tornField = { 0, 1, 0x11, 0x22, 0x00, 0xFF, 1, NetProtocol.TunePileOffset, 0x01 };
        t.True(!strict.Accept(tornField, 0, tornField.Length),
               "and so is a field cut short of the width its id declares");
        byte[] miscount = { 0, 1, 0x11, 0x22, 0x00, 0xFF, 9, NetProtocol.TuneCardWidth, 0xBC, 0x02 };
        t.True(!strict.Accept(miscount, 0, miscount.Length),
               "a page whose declared field count disagrees with its own bytes is refused — the two " +
               "must never be allowed to differ, because the assembled count byte is built from one " +
               "and read by the other");
        t.Equal(0, strict.AssembledLength, "nothing was ever published from any of them");

        // THE ID SPACE IS THE ONLY BOUND LEFT, and it is a BUILD-TIME one: 247 usable ids at their
        // own widths is 921 bytes, which is what sizes the sender's buffer and the receiver's
        // accumulator. Pinned here so that widening a range is a deliberate act with a failing test
        // attached, not something a dial discovers at runtime on somebody's Quest.
        t.Equal(247, NetProtocol.BoardTuneMaxFields,
                "247 usable field ids: 47 vec + 16 colour + 64 length + 64 factor + 32 angle + "
                + "24 count — the COLOUR range was carved out of the vec range's unused tail, so "
                + "the id COUNT is exactly what it was before it existed");
        t.Equal(921, NetProtocol.BoardTuneMaxFieldBytes,
                "= 921 bytes at their widths — it was 969, and it went DOWN when thirteen dials "
                + "were added: sixteen ids moved from 7 bytes each to 4");
        t.True(NetProtocol.BoardTuneMaxFields < 255,
               "and under 255, which is the PROOF that the assembled payload's one-byte field count " +
               "can never overflow: each id may appear at most once");
        t.Equal(248, NetProtocol.BoardTunePageMaxFieldBytes,
                "a page carries 248 field bytes (255 tail ceiling - 7 header)");
        t.True(NetProtocol.BoardTuneMaxFieldBytes
               <= NetProtocol.BoardTuneMaxPages * (NetProtocol.BoardTunePageMaxFieldBytes - 6),
               "and BoardTuneMaxPages is DERIVED from those two, with the 6-byte worst-case split " +
               "waste folded in — so the whole id space always fits inside the page cap");

        // -- 7v. BOARD TUNING: the ITEM CUE and the ITEM BERTH (ids 80 / 161..169) --------------
        // The ten dials the old 255-byte ceiling had locked out COMPLETELY: the item-cue / item-berth
        // re-art shipped them as frozen constants in RemoteControlBoard and RemoteBoardFurniture
        // because record 28 could not carry an eleventh field, and the paging round RESERVED these
        // exact ids for them so the merge would be a sampler line rather than an id negotiation.
        // scripts/check-wire-coverage.py failed on all ten until they were claimed; these vectors are
        // what stops them from being un-claimed by a refactor that "tidies" the sampler.
        t.Case("7v. extras, item-cue + item-berth tuning dials");

        // EVERY ONE OF THE TEN AT ITS SHIPPED VALUE, in ascending id order (the layout contract).
        //   id  80 length ItemBerthRingThickness 0.0042 m -> 42 tenth-mm
        //   id 161 factor ItemCueBeatSeconds     1.25 s   -> 1250   (SECONDS on the factor WIDTH)
        //   id 162 factor ItemCueRingReach       2.3      -> 2300
        //   id 163 factor ItemCueRingAlpha       0.95     -> 950
        //   id 164 factor ItemCueEmberRate       22 /s    -> 2200   (TENTHS — see below)
        //   id 165 factor ItemCueEmberSize       2.1      -> 2100
        //   id 166 factor ItemBerthGlow          0.34     -> 340
        //   id 167 factor ItemBerthPingSeconds   1.5 s    -> 1500
        //   id 168 factor ItemBerthPingReach     1.5      -> 1500
        //   id 169 factor ItemBerthRevealSeconds 0.26 s   -> 260
        byte[] cueFields =
        {
            NetProtocol.TuneItemBerthRingThickness, 0x2A, 0x00,
            NetProtocol.TuneItemCueBeatSeconds,     0xE2, 0x04,
            NetProtocol.TuneItemCueRingReach,       0xFC, 0x08,
            NetProtocol.TuneItemCueRingAlpha,       0xB6, 0x03,
            NetProtocol.TuneItemCueEmberRate,       0x98, 0x08,
            NetProtocol.TuneItemCueEmberSize,       0x34, 0x08,
            NetProtocol.TuneItemBerthGlow,          0x54, 0x01,
            NetProtocol.TuneItemBerthPingSeconds,   0xDC, 0x05,
            NetProtocol.TuneItemBerthPingReach,     0xDC, 0x05,
            NetProtocol.TuneItemBerthRevealSeconds, 0x04, 0x01,
        };
        t.Equal(30, cueFields.Length, "ten dials are 30 field bytes: one 3-byte length + nine 3-byte factors");
        ushort cueSig = BoardTunePages.Signature(cueFields, 0, cueFields.Length);
        var cuePage = new byte[255];
        int cueLen = BoardTunePages.WritePage(cueFields, cueFields.Length, 0, cueSig, cuePage);
        t.Equal(37, cueLen, "one page: 7 header + 30 field bytes");

        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTuning = true, BoardTuningBytes = cuePage, BoardTuningLength = cueLen,
        }, ext);
        t.Wire(Hex.Bytes($@"
            31 52 56 47 03 01 80 00
            80 00
            01
            1C 25            // id 28, len 37
            00 01            //   page 0 of 1
            {cueSig & 0xFF:X2} {cueSig >> 8:X2}   //   generation
            00 FF 0A         //   complete for ids 0..255, 10 fields
            50 2A 00         //   id  80 length: berth outline 4.2 mm
            A1 E2 04         //   id 161 factor: cue beat 1.250 s
            A2 FC 08         //   id 162 factor: ring reach 2.300x
            A3 B6 03         //   id 163 factor: ring alpha 0.950
            A4 98 08         //   id 164 factor: ember rate 2.200 -> 22.0 embers/s (TENTHS)
            A5 34 08         //   id 165 factor: ember size 2.100x
            A6 54 01         //   id 166 factor: berth glow 0.340
            A7 DC 05         //   id 167 factor: berth ping 1.500 s
            A8 DC 05         //   id 168 factor: ping reach 1.500x
            A9 04 01         //   id 169 factor: berth reveal 0.260 s
            "), ext, m, "the ten dials ride ONE page, ids ascending — 80 in the LENGTH range because " +
                        "it is a metre scalar, 161..169 in the FACTOR range because they are 2-byte " +
                        "values; the range fixes the WIDTH, never the unit");
        t.Equal(50, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 37) = 50 bytes");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState cueT), "and it parses");
        var cueAsm = new BoardTunePageAssembler();
        t.True(cueAsm.Accept(cueT.BoardTuningBytes, 0, cueT.BoardTuningLength), "and assembles");
        byte[] cueTune = cueAsm.Assembled;
        int cueTuneLen = cueAsm.AssembledLength;
        t.Equal(31, cueTuneLen, "assembled: 1 count byte + 30 field bytes");
        t.Equal(10, cueTune[0], "all ten dials present");

        t.Equal(0.0042f, NetProtocol.BoardTuneLength(cueTune, 0, cueTuneLen,
                                                     NetProtocol.TuneItemBerthRingThickness, 0f),
                "the berth's outline thickness decodes in tenth-millimetres, like every metre scalar");
        t.Equal(1.25f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                   NetProtocol.TuneItemCueBeatSeconds, 0f),
                "the cue's heartbeat period decodes to the millisecond");
        t.Equal(2.3f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                  NetProtocol.TuneItemCueRingReach, 0f),
                "ring reach");
        t.Equal(0.95f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                   NetProtocol.TuneItemCueRingAlpha, 0f),
                "ring alpha");
        t.Equal(2.1f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                  NetProtocol.TuneItemCueEmberSize, 0f),
                "ember size");
        t.Equal(0.34f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                   NetProtocol.TuneItemBerthGlow, 0f),
                "berth glow");
        t.Equal(1.5f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                  NetProtocol.TuneItemBerthPingSeconds, 0f),
                "the berth's inward ping period");
        t.Equal(1.5f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                  NetProtocol.TuneItemBerthPingReach, 0f),
                "and its reach");
        t.Equal(0.26f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                   NetProtocol.TuneItemBerthRevealSeconds, 0f),
                "the berth's grow-in / collapse-out time");

        // THE EMBER RATE'S SCALE, pinned because nothing else can catch it. [Cards] ItemCueEmberRate
        // accepts 0..60 embers/s and the FACTOR width is an i16 in thousandths, i.e. it saturates at
        // 32.767 — so sampled RAW, a player who cranked their embers past 32 would be silently
        // CLAMPED on every peer's screen. That is the exact class of invisible divergence this whole
        // record exists to end, so the dial is carried in TENTHS of its own unit. A sender and a
        // receiver that disagreed about the scale would disagree by a factor of ten with nothing
        // failing, which is why the constant is named once in NetProtocol and asserted here.
        t.Equal((short)32767, NetProtocol.EncodeTuneFactor(60f),
                "60 embers/s SATURATES the raw factor width — the bug this scale exists to avoid");
        t.Equal((short)6000, NetProtocol.EncodeTuneFactor(60f * NetProtocol.TuneItemCueEmberRateScale),
                "…and codes to 6000 at tenths, comfortably inside i16, resolution 0.01 embers/s");
        t.Equal(0.1f, NetProtocol.TuneItemCueEmberRateScale, "the scale is a tenth, stated once");
        t.Equal(22f, NetProtocol.BoardTuneFactor(cueTune, 0, cueTuneLen,
                                                 NetProtocol.TuneItemCueEmberRate, 0f)
                     / NetProtocol.TuneItemCueEmberRateScale,
                "and the wire's 2.200 un-scales to 22 embers/s, which is what RemoteBoardTuning hands " +
                "the mirrored emitter — no renderer ever learns what container it crossed in");

        // ABSENCE STILL MEANS "THE VALUE YOU ALREADY HAVE", for the scaled dial as much as any other:
        // a peer who never touched their embers sends no field, and the receiver's fallback must
        // resolve to the shipped rate and NOT to a tenth of it. (RemoteBoardTuning scales its own
        // fallback before the lookup for exactly this reason.)
        t.Equal(22f, NetProtocol.BoardTuneFactor(System.Array.Empty<byte>(), 0, 0,
                                                 NetProtocol.TuneItemCueEmberRate,
                                                 22f * NetProtocol.TuneItemCueEmberRateScale)
                     / NetProtocol.TuneItemCueEmberRateScale,
                "an ABSENT ember rate resolves to the shipped rate exactly, never to a tenth of it");

        // -- 7v. THE KEYCAP GEOMETRY FAMILY (record 28 ids 81..98 + 228) ------------------------
        // Every 3D keycap standing on a control board — the turn-flow SKIP cap, the Confirm/Undo
        // pads, the gear/Fixiert plates and the rest discs — was mirrored on a peer's board from a
        // FROZEN constant in RemoteBoardFurniture, and scripts/check-wire-coverage.py carried the
        // whole set as ONE PENDING debt reading "the renderer is owned by a parallel round — wire
        // it when that lands". It landed. The user report that cashed it in is about the SKIP cap
        // (hardware ModBuild 96: "vermisse ich die Einstellungen im Debug Menu für genau diese
        // 'Überspringen'-Tasten (offsets, Form, Größe, etc..)"), and under the 1:1 ruling a dial the
        // player can find is a dial every peer must see them turn.
        t.Case("7v. extras, board-tuning: the keycap geometry family");

        // THE ID RANGES ARE THE CONTRACT, and getting one wrong is the single unrecoverable mistake
        // here: an id, once shipped, can never be renumbered. Assert the WIDTHS the ranges imply
        // rather than the numbers alone, because that is what a receiver actually walks.
        t.Equal(2, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneRoundOffsetX),
                "the [RoundButtons] offsets are LENGTHS (2 B, tenth-mm), not a vec3 — they are three "
                + "separate config entries and the coverage guard resolves one key per field id");
        t.Equal(2, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneRestCapTravel),
                "…and so is the last of them, id 98, still inside the length range");
        t.Equal(1, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneRoundCapShape),
                "the turn-flow cap's SHAPE is a one-byte COUNT-range id: the range fixes the WIDTH, "
                + "never the unit");
        t.Equal(81, NetProtocol.TuneRoundOffsetX, "the family starts one past the item berth's id 80");
        t.Equal(98, NetProtocol.TuneRestCapTravel, "and ends at 98, leaving 99..127 free");
        t.Equal(228, NetProtocol.TuneRoundCapShape, "the shape takes the first free count id");

        // A SKIP CAP THE OWNER HAS RESHAPED, end to end. Negative offsets matter here and nowhere
        // else in this record's length family: the shipped [RoundButtons] OffsetX is -0.045, so a
        // sign bug would put every peer's skip cap 90 mm from where its owner sees it.
        //   id 81 offsetX -0.060 m -> -600 tenth-mm -> A8 FD
        //   id 84 capSize  0.050 m ->  500          -> F4 01
        //   id 88 travel   0.012 m ->  120          -> 78 00
        //   id 228 shape   Square (1)               -> 01
        byte[] capFields =
        {
            NetProtocol.TuneRoundOffsetX,  0xA8, 0xFD,
            NetProtocol.TuneRoundCapSize,  0xF4, 0x01,
            NetProtocol.TuneRoundCapTravel, 0x78, 0x00,
            NetProtocol.TuneRoundCapShape, 0x01,
        };
        ushort capSig = BoardTunePages.Signature(capFields, 0, capFields.Length);
        var capPage = new byte[255];
        int capLen = BoardTunePages.WritePage(capFields, capFields.Length, 0, capSig, capPage);
        var capAsm = new BoardTunePageAssembler();
        t.True(capAsm.Accept(capPage, 0, capLen), "the reshaped skip cap converges on one page");
        byte[] capTune = capAsm.Assembled;
        int capTuneLen = capAsm.AssembledLength;
        t.Equal(-0.06f, NetProtocol.BoardTuneLength(capTune, 0, capTuneLen,
                                                    NetProtocol.TuneRoundOffsetX, 0f),
                "the skip cap's sideways seat decodes SIGNED — its shipped default is itself "
                + "negative (-0.045), so an unsigned read would be wrong for every untuned player too");
        t.Equal(0.05f, NetProtocol.BoardTuneLength(capTune, 0, capTuneLen,
                                                   NetProtocol.TuneRoundCapSize, 0f),
                "its cap radius");
        t.Equal(0.012f, NetProtocol.BoardTuneLength(capTune, 0, capTuneLen,
                                                    NetProtocol.TuneRoundCapTravel, 0f),
                "and its press travel — the depth a peer's mirrored cap dips on the synced press edge");
        t.Equal(1, NetProtocol.BoardTuneCount(capTune, 0, capTuneLen,
                                              NetProtocol.TuneRoundCapShape, 0),
                "the shape arrives as its enum's integer value (1 = Square)");

        // ABSENCE STILL MEANS "THE VALUE YOU ALREADY HAVE" for the dials this owner did NOT move —
        // which is the whole reason nineteen new fields cost an untuned player nothing.
        t.Equal(0.035f, NetProtocol.BoardTuneLength(capTune, 0, capTuneLen,
                                                    NetProtocol.TuneRoundCapHeight, 0.035f),
                "an absent cap height yields the receiver's own shipped default, never zero — a zero "
                + "would build a degenerate keycap mesh on every peer's board");
        t.Equal(0, NetProtocol.BoardTuneCount(capTune, 0, capTuneLen,
                                              NetProtocol.TuneRoundCapShape + 1, 0),
                "and an id this generation does not carry reads as the fallback, not as a neighbour");

        // THE SKIP CAP'S OTHER SEAT — [Cards] ClusterOffset_{board}, id 16 (ModBuild 97).
        //
        // The cap is placed by TWO offsets that add in the same tray-root-local frame: the shared
        // [RoundButtons] group offset asserted above (ids 81..83) and this PER-BOARD one. Only the
        // first was ever wired, because the second moved nothing on either end — the rigid-dock lag
        // fix had reparented the local cluster off ButtonClusterMount and dropped the mount's
        // translation, so the dial the user was turning wrote to a transform with no children
        // ("Die Offsets bei den Überspringen-Tasten haben keinen Einfluss"). It rode
        // check-wire-coverage.py as a NO-OP exemption, which is how a bug survives as documentation.
        // With ButtonCluster.AttachDocked and RemoteBoardFurniture both consuming it, it is an
        // ordinary vec3 field — and it takes the FIRST FREE VEC ID, which is the part that can
        // never be taken back.
        t.Equal(16, NetProtocol.TuneClusterOffset,
                "the cluster seat takes id 16, one past the board mesh's own offset at 15");
        t.Equal(6, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneClusterOffset),
                "and it is a VEC3 (6 B, three signed tenth-mm i16s) — one field for a Vector3 dial, "
                + "unlike the [RoundButtons] triple, which is three separate config entries");
        t.True(NetProtocol.TuneClusterOffset <= NetProtocol.TuneVecIdMax,
                "…inside the vec range, whose tail was trimmed to 47 and must not be overrun");

        //   id 16 vec3 cluster offset (-0.012, 0.030, -0.004) -> -120, 300, -40
        byte[] seatFields =
        {
            NetProtocol.TuneClusterOffset, 0x88, 0xFF, 0x2C, 0x01, 0xD8, 0xFF,
        };
        ushort seatSig = BoardTunePages.Signature(seatFields, 0, seatFields.Length);
        var seatPage = new byte[255];
        int seatLen = BoardTunePages.WritePage(seatFields, seatFields.Length, 0, seatSig, seatPage);
        var seatAsm = new BoardTunePageAssembler();
        t.True(seatAsm.Accept(seatPage, 0, seatLen), "the moved cluster seat converges on one page");
        Vector3 seat = NetProtocol.BoardTuneVector(seatAsm.Assembled, 0, seatAsm.AssembledLength,
                                                   NetProtocol.TuneClusterOffset, Vector3.zero);
        t.Equal(-0.012f, seat.x, "the peer sees the owner's cluster nudged LEFT — signed, like every "
                                + "other seat: the user's own probe in the ModBuild-97 log was -0.01");
        t.Equal(0.03f, seat.y, "up-board on y");
        t.Equal(-0.004f, seat.z, "and toward the board face on z — the raw frame the local mount uses");
        t.True(NetProtocol.BoardTuneVector(seatAsm.Assembled, 0, seatAsm.AssembledLength,
                                           NetProtocol.TuneClusterOffset, Vector3.zero)
               != NetProtocol.BoardTuneVector(capTune, 0, capTuneLen,
                                              NetProtocol.TuneClusterOffset, Vector3.zero),
               "and a record that does NOT carry it leaves the receiver on its own shipped seat — "
               + "all three boards ship (0,0,0), so an untuned peer is drawn exactly as before");

        // THE CONVERGENCE BOUND IS UNCHANGED BY THESE NINETEEN, and that is a claim about page
        // ARITHMETIC rather than about bytes, so it is checked rather than asserted in a comment.
        // This is the sampler's complete field run at its worst case — every dial moved — in the
        // id-width census Sample() produces: 15 vec3 + 35 length + 41 factor + 9 angle + 5 count.
        var census = new byte[418];
        int cAt = 0;
        void Field(byte id, int width)
        {
            census[cAt] = id;
            cAt += 1 + width;
        }
        for (byte id = 1; id <= 16; id++) Field(id, 6);        // vec3   (ids 1..16, 16 = cluster seat)
        for (byte id = 48; id <= 53; id++) Field(id, 3);       // colour (ids 48..53, all new)
        for (byte id = 64; id <= 100; id++) Field(id, 2);      // length (ids 64..100, 99..100 new)
        for (byte id = 128; id <= 169; id++) Field(id, 2);     // factor (170 is new; 156 unsampled)
        for (byte id = 192; id <= 200; id++) Field(id, 2);     // angle
        for (byte id = 224; id <= 232; id++) Field(id, 1);     // count  (228..232 are the shapes/bools)
        t.Equal(418, cAt,
                "the sampler's worst case is 418 field bytes over 119 dials — 411 over 118 before " +
                "the cluster seat rejoined it, and it was 370 over 105 " +
                "before the colours and shapes, 314 over 86 before the keycap geometry family, 284 " +
                "over 76 before the item-cue ten, and under the OLD scheme none of it could have " +
                "grown at all: the whole payload had to fit 255");
        t.Equal(2, BoardTunePages.PageCount(census, cAt),
                "and it STILL splits into two pages, so the convergence guarantee written down in " +
                "BoardTunePages — complete state by T + pageCount x 200 ms — is unchanged at <=400 ms");
        int cp0 = BoardTunePages.WritePage(census, cAt, 0, 1, cuePage);
        t.Equal(254, cp0,
                "page 0 fills to 7 header + 247 field bytes (was 246 through three earlier " +
                "censuses): the sixteenth vec3 shifts the run by 7, and 247 is where the next " +
                "field stops fitting the 248-byte budget — the packing rule is unchanged, only " +
                "WHICH dials spill onto page 1");
        int cp1 = BoardTunePages.WritePage(census, cAt, 1, 1, cuePage);
        t.Equal(178, cp1,
                "…and page 1 now holds 171 of its own 248 field bytes (was 165): ~25 more 3-byte " +
                "dials before the bound would go to <=600 ms");

        // AND THE SIX COLOURS COST 24 BYTES, WHICH IS THE WHOLE ARGUMENT FOR THE NEW WIDTH — pinned
        // as arithmetic rather than left in a comment, because the alternative it beat (three FACTOR
        // fields per colour) is the one somebody refactoring "for consistency" would reach for.
        t.Equal(24, 6 * (1 + NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneLabelColor)),
                "six colours as 3-byte COLOUR fields: 24 bytes and 6 of the 247 field ids");
        t.Equal(54, 18 * (1 + NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneObjectivesScale)),
                "…the same six as eighteen FACTOR channels would have been 54 bytes AND 18 ids — "
                + "and the ID SPACE is the only bound this record still has, so that is the axis "
                + "the choice was made on, not the bytes");

        // -- 7w. BOARD TUNING: the COLOUR range, and the shapes that finally got a receiver ------
        // The user overruled an audit note that had called the 21 [ButtonColors] entries "a look
        // decision, not a wire gap": "Bitte implementier auch die Farben und Formen der Knöpfe, dass
        // sie über die Leitung gehen - so dass das remote Board 1:1 das anzeigt was der Spieler
        // sieht" (2026-08-09). Carrying eighteen colour channels needed a container, and the one
        // chosen is a NEW WIDTH — 3 bytes, one uint8 per channel — carved out of the vec range's
        // unused tail. A new width is the kind of change that is unrecoverable if it ships wrong
        // (an id, once out, can never be renumbered), so the range boundaries are asserted here
        // rather than trusted to a comment.
        t.Case("7w. extras, board-tuning: the COLOUR width and the cap shapes");

        t.Equal(47, NetProtocol.TuneVecIdMax,
                "the vec range gave up its top sixteen ids — it used 15 of 63 in the record's whole "
                + "life, and a 7-byte field is the most expensive thing this record can carry");
        t.Equal(48, NetProtocol.TuneColorIdMin, "…which the COLOUR range takes over at 48");
        t.Equal(63, NetProtocol.TuneColorIdMax, "…through 63, one short of the LENGTH range");
        t.Equal(6, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneVecIdMax),
                "id 47 is still a 6-byte vec3");
        t.Equal(3, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneColorIdMin),
                "and id 48 is a 3-byte colour — the boundary is exactly where the constants say");
        t.Equal(3, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneColorIdMax),
                "…as is 63");
        t.Equal(2, NetProtocol.BoardTuneFieldWidth(NetProtocol.TuneLengthIdMin),
                "…and 64 is a LENGTH again, so no id changed width that was already shipped");
        t.Equal(48, NetProtocol.TuneLabelColor, "the label fill takes the first colour id");
        t.Equal(53, NetProtocol.TuneRestCapTint, "and the rest tint the sixth, leaving 54..63 free");

        // THE QUANTIZATION IS THE ARGUMENT FOR THE WIDTH, so it is checked as one. 8 bits per
        // channel is not an approximation of the shipped label colour: it IS the swatch the default
        // was authored as, and the round trip reproduces #FBF3E0 byte for byte.
        t.Equal(251, NetProtocol.EncodeTuneColorChannel(0.984f), "0.984 -> 0xFB");
        t.Equal(243, NetProtocol.EncodeTuneColorChannel(0.953f), "0.953 -> 0xF3");
        t.Equal(224, NetProtocol.EncodeTuneColorChannel(0.878f), "0.878 -> 0xE0 — #FBF3E0, the "
                + "authored parchment, is what the wire carries and not an approximation of it");
        t.Equal(128, NetProtocol.EncodeTuneColorChannel(0.5f),
                "the shipped 0.5 cap tint rounds to 128 (Mathf.RoundToInt is banker's: 127.5 -> 128)");
        t.Equal(0, NetProtocol.EncodeTuneColorChannel(-1f), "an out-of-range channel SATURATES…");
        t.Equal(255, NetProtocol.EncodeTuneColorChannel(2f), "…at both ends, never wraps to a "
                + "different hue on somebody else's board");
        t.Equal(0, NetProtocol.EncodeTuneColorChannel(float.NaN), "and NaN encodes as 0, never garbage");
        t.Equal(1f, NetProtocol.DecodeTuneColorChannel(255), "255 decodes to exactly 1.0…");
        t.Equal(0.2f, NetProtocol.DecodeTuneColorChannel(51), "…and 51 to exactly 0.2");

        // AND THE PROPERTY THE WHOLE RECORD RESTS ON, for the new width as for every old one:
        // "differs from the shipped default" is decided on the CODE, so a config file's text
        // round-trip cannot make an untuned player emit a colour field forever.
        var untunedLabel = new Color(0.984f, 0.953f, 0.878f, 1f);
        var roundTripped = new Color(0.9840001f, 0.95299995f, 0.8780002f, 1f);
        var colBuf = new byte[64];
        int colAt = 0;
        t.True(!NetProtocol.WriteTuneColorField(colBuf, ref colAt, NetProtocol.TuneLabelColor,
                                                roundTripped, untunedLabel),
               "a colour that survived a text round-trip in its last float bits writes NO field — "
               + "the same picture is the same bytes, and an untuned player stays silent");
        t.Equal(0, colAt, "…and consumes nothing");
        t.True(!NetProtocol.WriteTuneColorField(colBuf, ref colAt, NetProtocol.TuneBoardCapTint,
                                                new Color(0.5f, 0.5f, 0.5f, 1f),
                                                new Color(0.5f, 0.5f, 0.5f, 1f)),
               "and the SHIPPED 0.5 cap tint writes nothing — worth its own line because that value "
               + "is where the sampler's silence was nearly lost: ButtonTuning's pre-Bind fallback "
               + "for the four tints used to be WHITE while the shipped entry is 0.5, so an untuned "
               + "player would have broadcast a colour field during scene load");
        t.Equal(0, colAt, "…still nothing consumed");
        t.True(!NetProtocol.WriteTuneColorField(colBuf, ref colAt, NetProtocol.TuneLabelColor,
                                                new Color(0.984f, 0.953f, 0.878f, 0.25f),
                                                untunedLabel),
               "…and a DIFFERENT ALPHA is not a difference: the range carries no alpha, so comparing "
               + "one would emit a field whose bytes could not express what changed");
        t.True(NetProtocol.WriteTuneColorField(colBuf, ref colAt, NetProtocol.TuneLabelColor,
                                               new Color(1f, 0.2f, 0.2f, 1f), untunedLabel),
               "a colour the owner actually moved DOES write");
        t.Equal(4, colAt, "one colour is 4 bytes on the wire: id + R + G + B, no alpha");

        // A PLAYER WHO RE-COLOURED AND RE-SHAPED THEIR BOARD, end to end. Six fields across four
        // widths, ascending: two colours, one length that could not have ridden a build ago, one
        // bool and the two shapes whose mirrors had no branch to draw them with.
        //   id  48 colour  label fill      (1, 0.2, 0.2)  -> FF 33 33
        //   id  50 colour  BoardCapTint    (1, 1, 1)      -> FF FF FF  (the 0.5 halving turned off)
        //   id  99 length  RestCapWidth    0.080 m        -> 800 -> 20 03
        //   id 229 count   LabelOutline    off            -> 00
        //   id 231 count   RestCapShape    Square (1)     -> 01
        //   id 232 count   GenericCapShape Round  (0)     -> 00
        byte[] skinFields =
        {
            NetProtocol.TuneLabelColor,      0xFF, 0x33, 0x33,
            NetProtocol.TuneBoardCapTint,    0xFF, 0xFF, 0xFF,
            NetProtocol.TuneRestCapWidth,    0x20, 0x03,
            NetProtocol.TuneLabelOutlineOn,  0x00,
            NetProtocol.TuneRestCapShape,    0x01,
            NetProtocol.TuneGenericCapShape, 0x00,
        };
        t.Equal(17, skinFields.Length,
                "six dials, 17 field bytes: two 4-byte colours + one 3-byte length + three 2-byte counts");
        ushort skinSig = BoardTunePages.Signature(skinFields, 0, skinFields.Length);
        var skinPage = new byte[255];
        int skinLen = BoardTunePages.WritePage(skinFields, skinFields.Length, 0, skinSig, skinPage);
        t.Equal(24, skinLen, "one page: 7 header + 17 field bytes");

        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardTuning = true, BoardTuningBytes = skinPage, BoardTuningLength = skinLen,
        }, ext);
        t.Wire(Hex.Bytes($@"
            31 52 56 47 03 01 80 00
            80 00
            01
            1C 18            // id 28, len 24
            00 01            //   page 0 of 1
            {skinSig & 0xFF:X2} {skinSig >> 8:X2}   //   generation
            00 FF 06         //   complete for ids 0..255, 6 fields
            30 FF 33 33      //   id  48 colour: label fill (1.00, 0.20, 0.20)
            32 FF FF FF      //   id  50 colour: Confirm/Undo face tint = identity white
            63 20 03         //   id  99 length: square rest cap 80 mm wide
            E5 00            //   id 229 count : label keyline OFF
            E7 01            //   id 231 count : rest caps SQUARE
            E8 00            //   id 232 count : Confirm/Undo ROUND
            "), ext, m, "the colours ride BEFORE the lengths because their id range does — ascending "
                        + "id order is the record's layout contract and the reason a page can claim "
                        + "a RANGE it is complete for");
        t.Equal(37, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 24) = 37 bytes");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState skinT), "and it parses");
        var skinAsm = new BoardTunePageAssembler();
        t.True(skinAsm.Accept(skinT.BoardTuningBytes, 0, skinT.BoardTuningLength), "and assembles");
        byte[] skinTune = skinAsm.Assembled;
        int skinTuneLen = skinAsm.AssembledLength;
        t.Equal(18, skinTuneLen, "assembled: 1 count byte + 17 field bytes");
        t.Equal(6, skinTune[0], "all six dials present");

        Color fill = NetProtocol.BoardTuneColor(skinTune, 0, skinTuneLen,
                                                NetProtocol.TuneLabelColor, Color.black);
        t.Equal(1f, fill.r, "the label fill's red decodes exactly");
        t.Equal(0.2f, fill.g, "…its green…");
        t.Equal(0.2f, fill.b, "…and its blue");
        t.Equal(1f, NetProtocol.BoardTuneColor(skinTune, 0, skinTuneLen,
                                               NetProtocol.TuneBoardCapTint, Color.black).r,
                "and the cap tint arrives at identity white — the mirrored Confirm cap must then be "
                + "drawn at the FULL palette colour, which is the one case the old renderer got "
                + "right by accident while getting the shipped 0.5 wrong for everybody");

        // ALPHA COMES FROM THE FALLBACK, NEVER THE WIRE — the range carries no alpha byte, and a
        // decoder that invented one would make every mirrored label transparent the day somebody
        // passed a non-opaque fallback.
        t.Equal(0.25f, NetProtocol.BoardTuneColor(skinTune, 0, skinTuneLen,
                                                  NetProtocol.TuneLabelColor,
                                                  new Color(0f, 0f, 0f, 0.25f)).a,
                "a present colour field keeps the CALLER's alpha");
        Color absentOutline = NetProtocol.BoardTuneColor(skinTune, 0, skinTuneLen,
                                                        NetProtocol.TuneLabelOutlineColor,
                                                        new Color(0.5f, 0.5f, 0.5f, 1f));
        t.Equal(0.5f, absentOutline.r,
                "and an ABSENT colour is the receiver's own shipped value, unchanged in every "
                + "channel — which is what lets a player who moved ONE colour cost five fields "
                + "nothing");

        t.Equal(0, NetProtocol.BoardTuneCount(skinTune, 0, skinTuneLen,
                                              NetProtocol.TuneLabelOutlineOn, 1),
                "the keyline switch arrives as 0 = off (a bool in the one-byte count width)");
        t.Equal(1, NetProtocol.BoardTuneCount(skinTune, 0, skinTuneLen,
                                              NetProtocol.TuneRestCapShape, 0),
                "the rest caps are SQUARE — a shape that no mirror could draw one build ago, which "
                + "is exactly why the dial was a stated PENDING debt and not a sampler line");
        t.Equal(0, NetProtocol.BoardTuneCount(skinTune, 0, skinTuneLen,
                                              NetProtocol.TuneGenericCapShape, 1),
                "and the Confirm/Undo column ROUND");
        t.Equal(0.08f, NetProtocol.BoardTuneLength(skinTune, 0, skinTuneLen,
                                                   NetProtocol.TuneRestCapWidth, 0f),
                "[RestButtons] Width rides now that a square rest cap exists to consume it");
        t.Equal(0.105f, NetProtocol.BoardTuneLength(skinTune, 0, skinTuneLen,
                                                    NetProtocol.TuneRestCapHeight, 0.105f),
                "…and its Height falls back to the receiver's shipped side, never to zero");

        // A COLOUR ID THIS BUILD KNOWS BUT THE SENDER DID NOT USE, next to one it never will: the
        // walk has to step over a 3-byte field by THREE, and getting that wrong would mis-parse
        // every field after the first colour rather than merely lose one.
        t.Equal(1, NetProtocol.BoardTuneCount(skinTune, 0, skinTuneLen,
                                              NetProtocol.TuneRestCapShape, 0),
                "the LAST-but-one field still resolves after two colour fields were stepped over — "
                + "the proof that the new width is walked at 3 and not at 6 or 2");

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
        foreach (var s in new[] { ExtrasEverything(), ExtrasMaskSizeOnly(), withDefaults,
                                  ExtrasBoardUiAndFanAnchor(), ExtrasSecondFigure(),
                                  ExtrasSecondHeldCard(), ExtrasSlotCardSize(),
                                  new PresenceState { HasBoardTooltip = true, BoardTooltipText = "Tip" },
                                  new PresenceState { HasDecisionLines = true, DecisionLinesText = "Ja\nNein" },
                                  new PresenceState
                                  {
                                      HasDecisionLines = true, DecisionLinesText = "Ja\nNein",
                                      HasDecisionState = true,
                                      DecisionPromptKind = NetProtocol.DecisionKindTakeDamage,
                                      DecisionTextVariant = NetProtocol.DecisionTextMandatoryUse,
                                      DecisionOptionCount = 2,
                                      DecisionOptionFlags = new byte[] { 1, 6 },
                                  },
                                  new PresenceState
                                  {
                                      HasConfirmCapLabel = true, ConfirmCapLabel = "OK",
                                      HasSkipCapLabel = true, SkipCapLabel = "Skip",
                                  } })
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
        && x.BoardStyleCode == y.BoardStyleCode
        && x.HasModVersion == y.HasModVersion && x.ModBuild == y.ModBuild
        && x.ModVersionText == y.ModVersionText
        && x.HasBoardUi == y.HasBoardUi && x.BoardButtonsMask == y.BoardButtonsMask
        && x.BoardOverlayMask == y.BoardOverlayMask
        && x.HasBoardCapStates == y.HasBoardCapStates
        && x.BoardCapStateMask == y.BoardCapStateMask
        && x.HasFanAnchor == y.HasFanAnchor && x.FanAnchorLocal == y.FanAnchorLocal
        && x.HasSecondFigure == y.HasSecondFigure
        && x.SecondFigureActorId == y.SecondFigureActorId
        && x.SecondFigurePose.Position == y.SecondFigurePose.Position
        && x.SecondFigureLeftHand == y.SecondFigureLeftHand
        && x.PrimaryFigureLeftHand == y.PrimaryFigureLeftHand
        && x.HasBoardTooltip == y.HasBoardTooltip
        && x.BoardTooltipText == y.BoardTooltipText
        && x.HasSecondHeldCard == y.HasSecondHeldCard
        && x.SecondHeldCardPose.Position == y.SecondHeldCardPose.Position
        && x.HasSlotCardSize == y.HasSlotCardSize
        && x.SlotFrameWidthCode == y.SlotFrameWidthCode
        && x.SlotCardWidthCode == y.SlotCardWidthCode
        && x.HasDecisionLines == y.HasDecisionLines
        && x.DecisionLinesText == y.DecisionLinesText
        && x.HasDecisionState == y.HasDecisionState
        && x.DecisionPromptKind == y.DecisionPromptKind
        && x.DecisionTextVariant == y.DecisionTextVariant
        && x.DecisionOptionCount == y.DecisionOptionCount
        && x.HasConfirmCapLabel == y.HasConfirmCapLabel
        && x.ConfirmCapLabel == y.ConfirmCapLabel
        && x.HasSkipCapLabel == y.HasSkipCapLabel
        && x.SkipCapLabel == y.SkipCapLabel
        && x.HasUndoCapLabel == y.HasUndoCapLabel
        && x.UndoCapLabel == y.UndoCapLabel
        && x.HasItemUseCapLabel == y.HasItemUseCapLabel
        && x.ItemUseCapLabel == y.ItemUseCapLabel
        && x.HasCapPress == y.HasCapPress
        && x.CapPressCap == y.CapPressCap && x.CapPressSeq == y.CapPressSeq
        && x.HasCharFocus == y.HasCharFocus
        && x.CharFocusActorId == y.CharFocusActorId
        && x.CharFocusOwnsAttention == y.CharFocusOwnsAttention
        && x.CharFocusAttentionActorId == y.CharFocusAttentionActorId
        && x.HasTrackSelection == y.HasTrackSelection
        && x.TrackSelectionCount == y.TrackSelectionCount
        && x.HasUseBars == y.HasUseBars
        && x.UseBarsMask == y.UseBarsMask
        && x.HasTrackOrder == y.HasTrackOrder
        && x.TrackOrderCount == y.TrackOrderCount
        && x.TrackOrderOwnedMask == y.TrackOrderOwnedMask;

    /// <summary>The three states of the attention cue, mirrored from <c>Board.FocusTurnMark</c> —
    /// that enum lives in the plugin assembly (UnityEngine types all the way down) and cannot be
    /// referenced here, so the DERIVATION is restated instead. Named strings rather than an enum so
    /// a failure prints the mark that was expected, not an ordinal.</summary>
    private static class FocusMark
    {
        internal const string None = "FocusTurnMark.None";
        internal const string Green = "FocusTurnMark.AtTurnCorrect";
        internal const string Red = "FocusTurnMark.AtTurnWrong";
    }

    /// <summary>
    /// THE RECEIVER'S DERIVATION, in the one line <c>Board.CharacterFocus.MarkForPeer</c> runs —
    /// restated here so the golden vectors assert the MARK a peer actually wears and not merely the
    /// bytes that carry it. It reads nothing but the decoded record: that independence from local
    /// turn state is precisely the property under test (before 2026-08-08 the mark was derived by
    /// comparing the focus id against the receiver's own <c>Choreographer.CurrentPlayerActor</c>,
    /// which is null on every client during a pending decision).
    /// </summary>
    private static string MarkOf(in PresenceState s)
    {
        if (!s.HasCharFocus || !s.CharFocusOwnsAttention || s.CharFocusAttentionActorId == 0)
            return FocusMark.None;
        return s.CharFocusAttentionActorId == s.CharFocusActorId ? FocusMark.Green : FocusMark.Red;
    }
}
