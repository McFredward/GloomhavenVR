// SHORT-REST SACRIFICE SEAT — extension record 39, byte for byte.
//
// User, 2026-09-05, item 15, verbatim: "Bei einer kurzen Rast soll es sichtbar sein welche Karte
// dort liegt - ich sehe nur die Rückseite." And the standing rule he restated over it: "Gewährleiste
// dass außerhalb der Auswahlphase NIEMALS Rückseiten auf Vorderseiten angezeigt werden sondern immer
// die echte Vorderseite. IMMER OHNE AUSNAHME."
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is four different things:
//
//   1. RECESS 2 CANNOT RIDE ALONE. The record is read by its LENGTH, so a 2-byte record ALWAYS
//      describes recess 1. A sender with the sacrifice in recess 2 that wrote only its own entry
//      would have every receiver draw that card's front in the WRONG RECESS — a face lying in a
//      place its owner has nothing in. The writer's SacrificeSeatPayload forces the long form for
//      exactly this case, and nothing about that decision is visible at either end: the writer
//      writes what it has, the reader reads what it is given.
//
//   2. A LIST THIS RECORD MAY NOT CARRY IS ALWAYS A BACK. The recess vocabulary is exactly the two
//      PILE arcs (NetProtocol.RecessSeatListAllowed) and it is NARROWER than record 36's whole
//      list space: an undefined id (7) and a DEFINED but forbidden one (the HAND, the ACTIVE pile)
//      must both decode to "no sacrifice here" rather than to a resolved seat. That is the one
//      failure the record must not have — a confidently WRONG face in a peer's recess is worse
//      than the anonymous back the record replaces, because the player cannot read it as a failure
//      and would act on it — AND it is the anti-cheat boundary: refusing the HAND at the DECODE is
//      what makes the two-card commit unexpressible in this format rather than merely unwritten by
//      this build's sampler.
//
//   3. ABSENCE HAS TO STAY FREE. A short rest lasts a handful of seconds; every other moment of
//      every session must emit the bytes ModBuild 447 emitted. The cheapest way to break that is a
//      tail gate that opens on the FLAG instead of on the payload.
//
//   4. THE RECORD BEHIND IT MUST SURVIVE. A 2-byte record must leave the reader on the next
//      record's own offset rather than swallowing 4.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SacrificeSeatVectors
{
    public static void Run(Harness t)
    {
        Recess1(t);
        Recess2ForcesLongForm(t);
        Absent(t);
        UndefinedListIsABack(t);
        LengthGate(t);
    }

    // ---------------------------------------------------------------------------------------
    //  39a. THE COMMON CASE — the sacrifice in recess 1, seat 3 of a discard arc of 8. Two bytes
    //       of payload on the tail: [39][2] plus one recess.
    // ---------------------------------------------------------------------------------------
    private static void Recess1(Harness t)
    {
        t.Case("39a. sacrifice seat, recess 1 — the 2-byte form");
        var ext = new byte[PresenceSerializer.MaxSize];
        int m = PresenceSerializer.Write(new PresenceState
        {
            HasSacrificeSeat = true,
            // list 2 (DISCARD) << 5 | seat 3  ==  0x43
            SacrificeSeatCode0 = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListDiscard, 3),
            SacrificeSeatCount0 = 8,
        }, ext);

        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type MsgExtras
            80               // flags: FlagPileBrowse -- 'a BLOCK follows'
            00               // handCardCount
            80               // byte A: PileBrowseExtensionBit only (bit 7)
            00               // byte B: browse count 0 -> no fan
            01               // tail: 1 record
            27 02            // record: id 39, len 2 (ONE recess)
            43               // code: list 2 (DISCARD) in bits 5..7, seat 3 in bits 0..4
            08               // the discard arc was 8 long when the sender seated it
            "), ext, m, "recess 1 alone costs [39][2] plus one 2-byte entry");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), "it reads back");
        t.True(got.HasSacrificeSeat, "the sacrifice seat is delivered");
        t.Equal(NetProtocol.HeldFaceListDiscard, (int)NetProtocol.HeldFaceList(got.SacrificeSeatCode0),
                "…naming the DISCARD list");
        t.Equal(3, (int)NetProtocol.HeldFaceIndex(got.SacrificeSeatCode0), "…at seat 3");
        t.Equal(8, (int)got.SacrificeSeatCount0, "…with the length belt beside it");
        t.Equal(0, (int)got.SacrificeSeatCode1, "and recess 2 names nothing");
    }

    // ---------------------------------------------------------------------------------------
    //  39b. THE TRAP. The sacrifice in recess 2 and NOTHING in recess 1 — the writer must still
    //       emit the 4-byte form, because a 2-byte record is by definition recess 1's. Writing
    //       recess 2's entry alone would draw the card face-up in the wrong recess on every peer.
    // ---------------------------------------------------------------------------------------
    private static void Recess2ForcesLongForm(Harness t)
    {
        t.Case("39b. sacrifice seat, recess 2 forces the 4-byte form");
        var ext = new byte[PresenceSerializer.MaxSize];
        int m = PresenceSerializer.Write(new PresenceState
        {
            HasSacrificeSeat = true,
            SacrificeSeatCode1 = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListDiscard, 5),
            SacrificeSeatCount1 = 9,
        }, ext);

        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00 80 00
            01               // tail: 1 record
            27 04            // record: id 39, len 4 -- the LONG form, though recess 1 is empty
            00 00            // recess 1: names nothing
            45 09            // recess 2: list 2, seat 5, arc length 9
            "), ext, m, "a lone recess-2 entry is padded to the long form, never sent as 2 bytes");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), "it reads back");
        t.Equal(0, (int)got.SacrificeSeatCode0, "recess 1 still names nothing after the round trip");
        t.Equal(5, (int)NetProtocol.HeldFaceIndex(got.SacrificeSeatCode1),
                "and recess 2 keeps ITS seat rather than being re-seated onto recess 1");
    }

    // ---------------------------------------------------------------------------------------
    //  39c. ABSENCE IS FREE. No sacrifice ⇒ no record, and — because this is the only record in
    //       the packet — no extension tail at all. This is every packet of every session outside
    //       the seconds of an actual short rest.
    // ---------------------------------------------------------------------------------------
    private static void Absent(Harness t)
    {
        t.Case("39c. no sacrifice — the record and the whole tail stay off the wire");
        var ext = new byte[PresenceSerializer.MaxSize];

        // The flag set but NEITHER recess naming anything: the payload is 0, so the tail gate must
        // refuse to open on the flag alone. This is the shape a sampler bug takes.
        int m = PresenceSerializer.Write(new PresenceState { HasSacrificeSeat = true }, ext);
        int bare = PresenceSerializer.Write(new PresenceState(), ext.Length > 0 ? new byte[PresenceSerializer.MaxSize] : ext);
        t.Equal(bare, m, "a flag with no payload emits exactly the bytes an empty state does");

        int m2 = PresenceSerializer.Write(new PresenceState
        {
            HasSacrificeSeat = true,
            SacrificeSeatCode0 = 0,
            SacrificeSeatCount0 = 8,   // a length with no code is still nothing
        }, ext);
        t.Equal(bare, m2, "…and a length byte without a code does not open it either");
    }

    // ---------------------------------------------------------------------------------------
    //  39d. A LIST THIS RECORD MAY NOT CARRY IS ALWAYS A BACK. The recess vocabulary is exactly
    //       the two PILE arcs (NetProtocol.RecessSeatListAllowed) and it is NARROWER than record
    //       36's: list 7 is undefined everywhere, and list 1 (the HAND) is defined but forbidden
    //       HERE, because a card of the two-card commit is a hand card and is the one secret
    //       SelectAbilityCardsOrLongRest exists to keep. Both must read as "no sacrifice", never
    //       as a seat — and the hand case is the one that makes the secret unexpressible in the
    //       format rather than merely unwritten by this build's sampler.
    // ---------------------------------------------------------------------------------------
    private static void UndefinedListIsABack(Harness t)
    {
        t.Case("39d. sacrifice seat, a list this record may not carry decodes to a back");
        byte[] packet = Tail(@"
            27 04
            E3 08            // list 7 (undefined anywhere) << 5 | seat 3
            E5 09            // list 7 again, other recess     | seat 5
            ");
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out PresenceState got),
               "the record still parses");
        t.True(!got.HasSacrificeSeat,
               "…and neither recess names a card, so both draw the anonymous back");

        // THE ANTI-CHEAT CASE. A sender naming the HAND for a recess is naming a card of the
        // two-card commit; the DECODE refuses it, so no consumer can be handed one to resolve.
        byte[] hand = Tail(@"
            27 04
            23 08            // list 1 (HAND — defined by record 36, FORBIDDEN here) << 5 | seat 3
            C5 09            // list 6 (ACTIVE — likewise defined, likewise not a recess list)
            ");
        t.True(PresenceSerializer.TryRead(hand, hand.Length, out PresenceState h),
               "the record still parses");
        t.True(!h.HasSacrificeSeat,
               "…and a HAND or ACTIVE seat is refused at the decode, so the recess draws its back");

        // A list this build DOES define, at the reserved 'I cannot seat it' index, is also a back —
        // and it is a DISTINCT state from 'no list', which is what lets a log tell them apart.
        byte[] unseated = Tail("27 02 5F 08");   // list 2, index 31 = HeldFaceIndexUnknown
        t.True(PresenceSerializer.TryRead(unseated, unseated.Length, out PresenceState u),
               "an unseated code parses");
        t.True(u.HasSacrificeSeat, "the record is present (the list IS known)");
        t.True(!NetProtocol.HeldFaceNamesCard(u.SacrificeSeatCode0),
               "…but it names no card, so the consumer draws the back");
    }

    // ---------------------------------------------------------------------------------------
    //  39e. THE LENGTH IS THE RECESS COUNT. A 2-byte record must deliver exactly recess 1 AND
    //       leave the reader on the next record — the failure this guards is a reader that walks
    //       4 bytes regardless and eats whatever follows as recess 2's entry.
    // ---------------------------------------------------------------------------------------
    private static void LengthGate(Harness t)
    {
        t.Case("39e. sacrifice seat, a one-recess record does not eat the record behind it");
        byte[] packet = Tail(@"
            27 02            // id 39, len 2 -- ONE recess
            43 08
            01 01 3E         // id 1 (hand scale), len 1, value 62 -> 0.62x
            ", records: 2);
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out PresenceState got),
               "the two-record packet parses");
        t.True(got.HasSacrificeSeat, "recess 1 is delivered");
        t.Equal(0, (int)got.SacrificeSeatCode1,
                "…and no recess-2 entry is invented from the next record's bytes");
        t.True(got.HasHandScale && got.HandScaleCode == 62,
               "and the record BEHIND it is read intact, at its own offset");
    }

    /// <summary>A minimal extras packet whose extension tail is exactly <paramref name="body"/>.
    /// Hand-built rather than produced by the writer, because a golden vector made by calling the
    /// code it tests proves nothing — and because the malformed ones are packets the writer is
    /// specifically incapable of producing.</summary>
    private static byte[] Tail(string body, int records = 1) => Hex.Bytes(
        "31 52 56 47 03 01 80 00 80 00 " + records.ToString("X2") + "\n" + body);
}
