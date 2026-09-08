// HELD PROPS — extension record 37, byte for byte.
//
// User, 2026-09-05, verbatim: "Das Aufnehmen der Props wird im Multiplayer nicht synchronisiert,
// sie sollen wie die Figuren vollständig synchronisiert werden mit allem drum und dran (mach da
// keinen Unterschied zwischen Figuren und Props!)."
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is three different things:
//
//   1. THE RECORD IS READ BY ITS LENGTH, not by a count field — 27 bytes means one hand, 54 means
//      both. That decision is invisible in the writer (it writes what it has) and invisible in the
//      reader (it reads what it is given); it only shows up as a WRONG POSE when the two disagree.
//      A one-slot record followed by another record is therefore the load-bearing case, and it is
//      driven here with a hand-built packet rather than through the writer, because a vector
//      produced by calling the code it tests proves nothing.
//
//   2. THE REJECTIONS ARE SILENT BY DESIGN. Prop id 0, a NaN position, two slots claiming one hand,
//      two slots claiming one prop — each of them decodes to "nothing held", which looks exactly
//      like a peer who is not carrying anything. There is no log to notice, so if a rejection
//      stopped working nobody would find out until a chest was standing in mid-air on somebody
//      else's board. And unlike a figure, a prop moved out of the world NEVER COMES BACK: the game
//      re-authors a figure's transform every frame and never a prop's.
//
//   3. ABSENCE HAS TO STAY FREE. The whole additive contract is "a player who is not carrying
//      anything emits the bytes the previous build emitted", and the cheapest way to break it is a
//      tail gate that opens on the flag instead of on the payload.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class HeldPropVectors
{
    /// <summary>A pose whose bytes are readable by eye: position (1, 2, 3) and identity
    /// rotation, i.e. 12 bytes of IEEE-754 LE followed by 00 00 00 00 00 00 FF 7F.</summary>
    private static RigPose Pose(float x, float y, float z) => new()
    {
        Position = new UnityEngine.Vector3(x, y, z),
        Rotation = UnityEngine.Quaternion.identity,
    };

    public static void Run(Harness t)
    {
        OneHand(t);
        BothHands(t);
        Absent(t);
        Rejections(t);
        LengthGate(t);
        Budget(t);
    }

    // ---------------------------------------------------------------------------------------
    //  37a. ONE HAND — the common case, and the one every idle-to-carrying transition passes
    //       through. 29 bytes on the tail: [37][27] plus one slot.
    // ---------------------------------------------------------------------------------------
    private static void OneHand(Harness t)
    {
        t.Case("37a. held prop, one hand — the 27-byte slot form");
        var ext = new byte[PresenceSerializer.MaxSize];
        int m = PresenceSerializer.Write(new PresenceState
        {
            HasHeldProp = true,
            HeldPropId = 0x11223344,
            HeldPropPose = Pose(1f, 2f, 3f),
            HeldPropLeftHand = true,
            HeldPropStretchCode = 2500,   // 2.5x the chest's own board size
        }, ext);

        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type MsgExtras
            80               // flags: FlagPileBrowse -- 'a BLOCK follows'
            00               // handCardCount
            80               // byte A: PileBrowseExtensionBit only (bit 7)
            00               // byte B: browse count 0 -> no fan
            01               // tail: 1 record
            25 1B            // record: id 37, len 27 (ONE slot)
            01               // hand: bit 0 set -> the LEFT hand carries it
            44 33 22 11      // propId 0x11223344, little endian
            00 00 80 3F      // pose.x = 1.0
            00 00 00 40      // pose.y = 2.0
            00 00 40 40      // pose.z = 3.0
            00 00 00 00      // quat x, y = 0
            00 00 FF 7F      // quat z = 0, w = 32767 (identity)
            C4 09            // held size 2500 milli = 2.5x board size, little endian
            "), ext, m, "the slot is [hand][u32 id][20 B pose][u16 size]");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), "and it parses");
        t.True(got.HasHeldProp, "the held prop is delivered");
        t.Equal(0x11223344, got.HeldPropId, "with its stable id intact");
        t.True(got.HeldPropLeftHand, "in the LEFT hand");
        t.Equal(2500, (int)got.HeldPropStretchCode, "at the size the holder measured");
        t.True(!got.HasSecondHeldProp, "and the other hand is empty");
        t.Equal(1f, got.HeldPropPose.Position.x, "pose x survives");
        t.Equal(2f, got.HeldPropPose.Position.y, "pose y survives");
        t.Equal(3f, got.HeldPropPose.Position.z, "pose z survives");
        t.Equal(2.5f, NetProtocol.DecodeHeldStretch(got.HeldPropStretchCode),
                "and the size decodes to the holder's own factor");
    }

    // ---------------------------------------------------------------------------------------
    //  37b. BOTH HANDS — the 54-byte form. The second slot is appended, not interleaved, and it
    //       carries its OWN hand bit: unlike the figure family, no slot has to describe another.
    // ---------------------------------------------------------------------------------------
    private static void BothHands(Harness t)
    {
        t.Case("37b. held props, both hands — the 54-byte two-slot form");
        var ext = new byte[PresenceSerializer.MaxSize];
        int m = PresenceSerializer.Write(new PresenceState
        {
            HasHeldProp = true,
            HeldPropId = 0x11223344,
            HeldPropPose = Pose(1f, 2f, 3f),
            HeldPropLeftHand = true,
            HeldPropStretchCode = 2500,
            HasSecondHeldProp = true,
            SecondHeldPropId = 0x55667788,
            SecondHeldPropPose = Pose(4f, 5f, 6f),
            SecondHeldPropLeftHand = false,
            SecondHeldPropStretchCode = (ushort)NetProtocol.HeldStretchCodeNeutral,
        }, ext);

        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type MsgExtras
            80 00 80 00      // flags, handCardCount, byte A, byte B
            01               // tail: 1 record
            25 36            // record: id 37, len 54 (TWO slots)
            01               // slot 1 hand: LEFT
            44 33 22 11      // slot 1 propId 0x11223344
            00 00 80 3F 00 00 00 40 00 00 40 40   // slot 1 position (1, 2, 3)
            00 00 00 00 00 00 FF 7F               // slot 1 rotation: identity
            C4 09            // slot 1 held size 2.5x
            00               // slot 2 hand: RIGHT (bit clear)
            88 77 66 55      // slot 2 propId 0x55667788
            00 00 80 40 00 00 A0 40 00 00 C0 40   // slot 2 position (4, 5, 6)
            00 00 00 00 00 00 FF 7F               // slot 2 rotation: identity
            E8 03            // slot 2 held size 1000 milli = board size
            "), ext, m, "both slots ride one record, appended in grab order");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), "and it parses");
        t.True(got.HasHeldProp && got.HasSecondHeldProp, "both props are delivered");
        t.Equal(0x55667788, got.SecondHeldPropId, "the second id is intact");
        t.True(!got.SecondHeldPropLeftHand, "and it is in the RIGHT hand");
        t.Equal(4f, got.SecondHeldPropPose.Position.x, "the second pose is the second pose");
        t.Equal(1f, NetProtocol.DecodeHeldStretch(got.SecondHeldPropStretchCode),
                "a neutral code is board size");
    }

    // ---------------------------------------------------------------------------------------
    //  37c. ABSENCE IS FREE. A player carrying nothing must emit the bytes the previous build
    //       emitted — so the tail gate has to test the PAYLOAD, not the flag.
    // ---------------------------------------------------------------------------------------
    private static void Absent(Harness t)
    {
        t.Case("37c. held prop, absence costs nothing");
        var ext = new byte[PresenceSerializer.MaxSize];

        byte[] idle = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type MsgExtras
            00               // flags: no block at all
            00               // handCardCount
            ");

        int m = PresenceSerializer.Write(new PresenceState(), ext);
        t.Wire(idle, ext, m, "an empty state writes no tail");

        // AND THE FLAG ALONE MUST NOT OPEN THE TAIL. This is the shape of the bug the gate exists
        // to prevent: a sender that sets HasHeldProp while nothing names a prop would otherwise
        // emit a five-byte tail on EVERY packet, for every player, forever.
        m = PresenceSerializer.Write(new PresenceState { HasHeldProp = true, HeldPropId = 0 }, ext);
        t.Wire(idle, ext, m, "a flag with no prop id writes no record and does not open the tail");

        // A SECOND SLOT CANNOT RIDE ALONE either — the slots are grab order, so slot 1 is occupied
        // whenever slot 2 is, and a state claiming otherwise is not a state a grab registry can
        // produce.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSecondHeldProp = true,
            SecondHeldPropId = 0x55667788,
            SecondHeldPropPose = Pose(4f, 5f, 6f),
        }, ext);
        t.Wire(idle, ext, m, "a second slot without a first writes nothing at all");
    }

    // ---------------------------------------------------------------------------------------
    //  37d. THE REJECTIONS. Each of these is a packet a stale or corrupted stream can produce and
    //       a grab registry cannot, and each of them must decode to a picture that is merely
    //       INCOMPLETE — never to a prop somewhere it has never been.
    // ---------------------------------------------------------------------------------------
    private static void Rejections(Harness t)
    {
        t.Case("37d. held prop, the reader refuses what no hand could produce");

        // A ZERO ID NAMES NOTHING. 0 is "none" everywhere on this wire, so a first slot carrying it
        // takes the whole record with it.
        byte[] zeroId = Tail(@"
            25 1B
            01
            00 00 00 00      // propId 0 -- 'none'
            00 00 80 3F 00 00 00 40 00 00 40 40
            00 00 00 00 00 00 FF 7F
            E8 03");
        t.True(PresenceSerializer.TryRead(zeroId, zeroId.Length, out PresenceState z),
               "a zero-id record still parses as a packet");
        t.True(!z.HasHeldProp, "…and delivers no held prop at all");

        // A NaN POSITION would move a real board prop out of the world — permanently, because
        // nothing in the game ever re-authors a prop's transform.
        byte[] nan = Tail(@"
            25 1B
            01
            44 33 22 11
            00 00 C0 7F      // position.x = NaN
            00 00 00 40 00 00 40 40
            00 00 00 00 00 00 FF 7F
            E8 03");
        t.True(PresenceSerializer.TryRead(nan, nan.Length, out PresenceState n),
               "a NaN-position record still parses as a packet");
        t.True(!n.HasHeldProp, "…and is dropped whole rather than flinging a prop out of the world");

        // TWO SLOTS, ONE HAND. A hand holds one object; equal hand bits mean a stale or duplicated
        // packet, and driving both would stack two chests in one palm.
        byte[] sameHand = Tail(@"
            25 36
            01 44 33 22 11
            00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F E8 03
            01 88 77 66 55   // slot 2 also claims the LEFT hand
            00 00 80 40 00 00 A0 40 00 00 C0 40 00 00 00 00 00 00 FF 7F E8 03");
        t.True(PresenceSerializer.TryRead(sameHand, sameHand.Length, out PresenceState h),
               "the contradictory pair still parses");
        t.True(h.HasHeldProp, "…the FIRST slot stands");
        t.True(!h.HasSecondHeldProp, "…and only the contradicting second slot is dropped");

        // TWO SLOTS, ONE PROP. The same object cannot ride both hands, and this is also the exact
        // shape of the transition where a sender re-packs its slots one packet after the receiver
        // last heard the old ordering.
        byte[] sameProp = Tail(@"
            25 36
            01 44 33 22 11
            00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F E8 03
            00 44 33 22 11   // slot 2 names the SAME prop
            00 00 80 40 00 00 A0 40 00 00 C0 40 00 00 00 00 00 00 FF 7F E8 03");
        t.True(PresenceSerializer.TryRead(sameProp, sameProp.Length, out PresenceState d),
               "the duplicated pair still parses");
        t.True(d.HasHeldProp && !d.HasSecondHeldProp, "…and the duplicate slot is dropped");

        // AN OUT-OF-ENVELOPE SIZE FAILS CLOSED TO BOARD SIZE — record 30's rule, per slot. A
        // garbage byte must render a chest at the size it stands on the board at, never at 65x and
        // never at nothing.
        byte[] badSize = Tail(@"
            25 36
            01 44 33 22 11
            00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F FF FF   // size 65535
            00 88 77 66 55
            00 00 80 40 00 00 A0 40 00 00 C0 40 00 00 00 00 00 00 FF 7F C4 09   // size 2500, sane
            ");
        t.True(PresenceSerializer.TryRead(badSize, badSize.Length, out PresenceState b),
               "the mixed-size record parses");
        t.Equal(NetProtocol.HeldStretchCodeNeutral, (int)b.HeldPropStretchCode,
                "the garbage size reads as board size");
        t.Equal(2500, (int)b.SecondHeldPropStretchCode,
                "and the legitimate slot beside it keeps its own");
    }

    // ---------------------------------------------------------------------------------------
    //  37e. THE LENGTH IS THE SLOT COUNT. A 27-byte record must deliver exactly one prop AND leave
    //       the reader positioned on the next record — the failure this guards is a reader that
    //       walks 54 bytes regardless and eats whatever follows as a pose.
    // ---------------------------------------------------------------------------------------
    private static void LengthGate(Harness t)
    {
        t.Case("37e. held prop, a one-slot record does not eat the record behind it");
        byte[] packet = Tail(@"
            25 1B            // id 37, len 27 -- ONE slot
            01 44 33 22 11
            00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F C4 09
            01 01 3E         // id 1 (hand scale), len 1, value 62 -> 0.62x
            ", records: 2);
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out PresenceState got),
               "the two-record packet parses");
        t.True(got.HasHeldProp, "the one held prop is delivered");
        t.True(!got.HasSecondHeldProp, "…and no second slot is invented from the next record");
        t.Equal(0x11223344, got.HeldPropId, "with the right id");
        t.True(got.HasHandScale && got.HandScaleCode == 62,
               "and the record BEHIND it is read intact, at its own offset");
    }

    // ---------------------------------------------------------------------------------------
    //  37f. THE BUFFER BUDGET. PresenceSerializer.MaxSize carries a stated rule — the documented
    //       worst case plus a margin of at least one record's worth — and record 37's 56 bytes are
    //       what forced the raise from 1800. A rule only a comment states is a rule that expires.
    // ---------------------------------------------------------------------------------------
    private static void Budget(Harness t)
    {
        t.Case("37f. held prop, the send buffer still satisfies its own margin rule");
        const int documentedWorstCase = 3449; // Records 46 and 47 included.
                                               // 1513 -> 1570 when this record and the shared-gaze
                                               // byte landed in one round; 1570 -> 1726 on
                                               // 2026-09-05 when an EXISTING term grew (the wall-
                                               // fade key cap, 24 -> 63 keys = +156 B); 1726 ->
                                               // 1732 in the same round when the SHORT-REST
                                               // SACRIFICE SEAT record (39) added its 6. THIS
                                               // LITERAL IS A CONSUMER OF THAT SUM: it must be
                                               // re-read from PresenceState's doc block every time
                                               // the sum moves, or the margin it claims to police
                                               // goes on passing against a number nobody documents
                                               // any more. It has already been stale once in this
                                               // one round, which is why record 39's lane updated
                                               // it in the same commit that moved the sum. 1732 ->
                                               // 1735 on 2026-09-06: the SPENT-HALF record (41),
                                               // 3 bytes flat ([id][len] + one mask byte); 1735 ->
                                               // 1738 in the same round: the FAN SOURCE PILE
                                               // record (43), 3 bytes flat as well ([id][len] +
                                               // one list-id byte). 1738 -> 1747 on 2026-09-06:
                                               // the FAN ARC ORDER record (44), 9 bytes at its
                                               // maximum ([id][len] + its 7-byte payload) and in
                                               // force only for a player who has dragged a card in
                                               // their own fan. 1747 -> 1798 on 2026-09-07: the
                                               // USE-BAR SLOT IDENTITY record (45), 51 bytes at
                                               // its maximum ([id][len] + an entry count and 16
                                               // three-byte entries) and in force only while a
                                               // decision prompt has the owner's active-bonus or
                                               // item bar up.
        const int largestSingleRecord = 257;   // board tuning: 2 TLV + one 255-byte page
        t.True(PresenceSerializer.MaxSize >= documentedWorstCase + largestSingleRecord,
               $"MaxSize {PresenceSerializer.MaxSize} leaves "
               + $"{PresenceSerializer.MaxSize - documentedWorstCase} bytes over the documented "
               + $"worst case {documentedWorstCase}, which is at least one record's worth "
               + $"({largestSingleRecord})");

        // And the record really is 56 bytes at its worst, which is the term the sum above added.
        t.Equal(56, 2 + 2 * NetProtocol.HeldPropSlotBytes,
                "record 37's worst case is [id][len] + two 27-byte slots");
    }

    /// <summary>A minimal extras packet whose extension tail is exactly <paramref name="body"/>.
    /// Hand-built rather than produced by the writer, because a golden vector made by calling the
    /// code it tests proves nothing — and because half of these are packets the writer is
    /// specifically incapable of producing.</summary>
    private static byte[] Tail(string body, int records = 1) => Hex.Bytes(
        "31 52 56 47 03 01 80 00 80 00 " + records.ToString("X2") + "\n" + body);
}
