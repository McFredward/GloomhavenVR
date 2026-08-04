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
            04 02 B5 02      // record: id 4 (board UI), len 2, buttons 0xB5, overlays 0x02
            05 0C            // record: id 5 (fan anchor), len 12
            0000003F         // x = 0.5
            0000803E         // y = 0.25
            000000BE         // z = -0.125
            "), ext, m, "board-UI and fan-anchor records ride the tail as [id][len][payload]");
        t.Equal(53, m, "header 7 + board 24 + count 1 + block 2 + tail 1 + 4 + 14 = 53 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState bu), "and it parses");
        t.True(bu.HasBoardUi, "the board-UI record is delivered");
        t.Equal((byte)0xB5, bu.BoardButtonsMask, "with the buttons mask intact");
        t.Equal((byte)0x02, bu.BoardOverlayMask, "and the wanted-glow mask intact");
        t.True(bu.HasFanAnchor, "the fan anchor is delivered");
        t.True(bu.FanAnchorLocal == new Vector3(0.5f, 0.25f, -0.125f),
               "with the exact board-local position");

        // Overlay hygiene: only the DEFINED overlay bits are wire state — bits 0..1 (wanted-slot
        // glow), bit 2 (FOLLOW/PIN), bits 3..4 (card-slot occupancy) and bit 5 (that nibble's
        // validity). The writer masks the still-reserved bits 6..7 so a future use of them cannot be
        // pre-claimed by garbage, and the reader masks again (never trust the wire). This expectation
        // moved from 0x06 to 0x3E when the occupancy nibble widened BoardUiOverlayMask from 0x07 to
        // 0x3F — which is exactly the assertion that would catch a widening done on only one side.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasBoardUi = true, BoardButtonsMask = 0x01, BoardOverlayMask = 0xFE,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ov), "overlay-mask packet parses");
        t.Equal((byte)0x3E, ov.BoardOverlayMask,
                "undefined overlay bits are masked off, the defined ones survive (0xFE -> 0x3E)");
        t.Equal((byte)0xC0, (byte)(0xFE & ~NetProtocol.BoardUiOverlayMask),
                "bits 6..7 are the only reserved overlay bits left");

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
            04 02 00 04      // record: id 4 (board UI), len 2, buttons 0x00, overlays 0x04 = PINNED
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
            04 02 00 28      // record: id 4 (board UI), len 2, buttons 0x00,
                             //   overlays 0x28 = slot0 occupied (0x08) | occupancy valid (0x20)
            "), ext, m, "the occupancy nibble rides byte 1 of the EXISTING board-UI record");
        t.Equal(15, m, "and costs ZERO extra bytes — same 15 as the FOLLOW/PIN vector above");
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
            04 02 B5 3E      // id 4, len 2, buttons 0xB5, overlays 0x3E =
                             //   wanted bit1 (0x02) | PINNED (0x04) | both slots (0x18) | valid (0x20)
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
            04 02 42 3F      // id 4, len 2, buttons 0x42, overlays 0x3F = every defined overlay bit
            06 02 05 FF      // id 6 card highlight — the record an OLD reader must still reach
            "), ext, m, "a fully-populated overlay byte still leaves the tail walk byte-identical");
        t.Equal((byte)2, ext[12], "the board-UI record's LENGTH byte is still 2 — an old reader's " +
                                  "'i += len' skips exactly as far as it always did");
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
            04 02 01 00      // id 4 board UI
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
        // owner's CardWidth config and [Cards] SlotCardFill (default 1.45!) entirely, so even two
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

        // -- 7l. HALF HOVER (extension record 14) ------------------------------------------
        // The action half the sender's pointer is on in their action-selection layout: one
        // masked byte — board slot (bits 0..1) + top-half bit. A slot POSITION and a half,
        // never a card identity. Written only while a half really is lit.
        t.Case("7l. extras, half-hover record");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverSlot = 1, HalfHoverTop = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            0E 01 05         // id 14 (half hover), len 1, slot 1 | top bit (0x04)
            "), ext, m, "the half-hover record is [id 14][len 1][slot|top] — a position only");
        t.Equal(14, m, "header 7 + count 1 + block 2 + tail 1 + 3 = 14 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hh), "and it parses");
        t.True(hh.HasHalfHover, "the half hover is delivered");
        t.Equal(1, hh.HalfHoverSlot, "with the slot index intact");
        t.True(hh.HalfHoverTop, "and the TOP half named");

        // Bottom half of slot 0 is the all-zero byte — still a valid, delivered record.
        m = PresenceSerializer.Write(new PresenceState { HasHalfHover = true }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState hhB), "slot0/bottom parses");
        t.True(hhB.HasHalfHover && hhB.HalfHoverSlot == 0 && !hhB.HalfHoverTop,
               "slot 0 / bottom half round-trips (the record's presence, not its value, is the flag)");

        // No hover ⇒ no record, no tail, no block: byte-identical to the previous build.
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "no half lit -> no record: byte-identical to a pre-record-14 sender");

        // INVALID SLOT (a slot the two-recess board does not have): rejected, degrades to
        // "record absent" — a glow on the wrong recess is worse than no glow.
        byte[] badSlot = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 01 06"); // slot 2 | top
        t.True(PresenceSerializer.TryRead(badSlot, badSlot.Length, out PresenceState hhBad),
               "a half-hover record naming slot 2 still parses the packet");
        t.True(!hhBad.HasHalfHover, "and the impossible slot is simply not delivered");

        // An OLD reader steps over id 14 by its length (asserted as the unknown-id case) and a
        // NEW reader steps over an unknown record ahead of it.
        byte[] futureHalf = Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02
            63 04 DE AD BE EF// id 99, len 4 -- unknown
            0E 01 04         // id 14, slot 0, top
            ");
        t.True(PresenceSerializer.TryRead(futureHalf, futureHalf.Length, out PresenceState futHh),
               "a packet with an unknown record ahead of the half hover parses");
        t.True(futHh.HasHalfHover && futHh.HalfHoverTop, "and the half hover behind it is read");

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

        // ID ORDER of the new tail region: records 10, 14, 15, 16 in that order — a reorder in
        // Write shows up right here, and this is also the OLD-READER vector (to a pre-batch
        // peer, ids 14/15/16 are unknown records it steps over by length).
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondHeldCard = true, SecondHeldCardPose = Card(),
            HasHalfHover = true, HalfHoverSlot = 0, HalfHoverTop = true,
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
            0E 01 04         // id 14 half hover: slot 0, top
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
        && x.HasConfirmCapLabel == y.HasConfirmCapLabel
        && x.ConfirmCapLabel == y.ConfirmCapLabel
        && x.HasSkipCapLabel == y.HasSkipCapLabel
        && x.SkipCapLabel == y.SkipCapLabel;
}
