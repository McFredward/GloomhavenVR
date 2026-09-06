// THE ACTIVE-PILE SOURCE LIST — NetProtocol.HeldFaceListActive (6), on extension record 36.
//
// User, 2026-09-06, item 9, verbatim: "Die aktiven Karten sind immer sichtbar (was du schon gemacht
// hast) d.h. aber auch, dass wenn ein Spieler eine aktive Karte in die Hand nimmt, soll diese auch
// mit der Vorderseite AUCH in der Auswahlphase sichtbar sein. (Aktuell sieht man nur die Rückseite
// beim remote Spieler)."
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is three things:
//
//   1. THE VALUE HAS TO FIT AND HAVE TO BE ACCEPTED. The SOURCE-LIST field is three bits and 6 was
//      a reserved value; a build that adds it without moving HeldFaceListMax writes a code every
//      receiver — including its own — clamps back to zero, which looks exactly like "the sender
//      named no seat" and is the state report item 9 was ABOUT. Value and cap have to move
//      together and nothing at either end of the wire says so.
//
//   2. IT MUST NOT LEAK INTO THE TWO NARROWER VOCABULARIES THAT SHARE THIS CODEC. Record 39 (a
//      card lying in a ROUND RECESS) and record 43 (which pile a hand FAN is showing) both encode
//      with EncodeHeldFace and both accept a subset of the lists. An active card is never lying in
//      a recess and is never fanned out as a hand, so both must refuse id 6 — and refusing it is a
//      property of two predicates that live nowhere near this value's declaration.
//
//   3. THE SEAT MUST SURVIVE THE ROUND TRIP UNCHANGED. The index and the list share one byte; a
//      list id that overflowed its field would corrupt the seat rather than the list, and the seat
//      is what names the card.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class HeldFaceActiveVectors
{
    public static void Run(Harness t)
    {
        ValueAndCapMoveTogether(t);
        RoundTripsOnRecord36(t);
        RefusedByTheNarrowerVocabularies(t);
    }

    // ---------------------------------------------------------------------------------------
    //  36f. THE VALUE IS INSIDE THE FIELD AND INSIDE THE CAP.
    // ---------------------------------------------------------------------------------------
    private static void ValueAndCapMoveTogether(Harness t)
    {
        t.Case("36f. the ACTIVE source list fits the 3-bit field and is accepted by this build");
        t.Equal(6, (int)NetProtocol.HeldFaceListActive, "id 6 — one of the two reserved values");
        t.True(NetProtocol.HeldFaceListActive <= NetProtocol.HeldFaceListMax,
               "…and HeldFaceListMax moved with it, or every receiver clamps it back to zero and "
               + "the picture is the 'sender named no seat' back report item 9 was about");
        t.Equal(6, (int)NetProtocol.HeldFaceListMax, "7 is the only reserved value left");
    }

    // ---------------------------------------------------------------------------------------
    //  36g. A HELD ACTIVE CARD SURVIVES THE ROUND TRIP, LIST AND SEAT.
    // ---------------------------------------------------------------------------------------
    private static void RoundTripsOnRecord36(Harness t)
    {
        t.Case("36g. a held ACTIVE card round-trips with its list and its seat intact");
        for (int seat = 0; seat <= NetProtocol.HeldFaceIndexMax; seat++)
        {
            byte code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListActive, seat);
            t.Equal(NetProtocol.HeldFaceListActive, (int)NetProtocol.HeldFaceList(code),
                    $"seat {seat} keeps its list id");
            t.Equal(seat, NetProtocol.HeldFaceIndex(code), $"seat {seat} keeps its index");
            t.True(NetProtocol.HeldFaceNamesCard(code),
                   $"seat {seat} names a card, so the receiver resolves it instead of drawing a back");
        }

        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasHeldCardFace = true,
            HeldFaceCode = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListActive, 2),
            HeldFaceCount = 4,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "the packet reads back");
        t.True(got.HasHeldCardFace, "the record survives the serializer's own list clamp");
        t.Equal(NetProtocol.HeldFaceListActive, (int)NetProtocol.HeldFaceList(got.HeldFaceCode),
                "…naming the ACTIVE pile");
        t.Equal(2, NetProtocol.HeldFaceIndex(got.HeldFaceCode), "…at seat 2");
        t.Equal(4, (int)got.HeldFaceCount, "…of a list of 4, which is the length belt's input");
    }

    // ---------------------------------------------------------------------------------------
    //  36h. THE TWO NARROWER VOCABULARIES REFUSE IT.
    // ---------------------------------------------------------------------------------------
    private static void RefusedByTheNarrowerVocabularies(Harness t)
    {
        t.Case("36h. the ACTIVE list is refused by the recess and the fan-source vocabularies");
        t.True(!NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListActive),
               "an active card is never LYING IN A ROUND RECESS, so record 39 may not name it");
        t.True(!NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListActive),
               "and it is never the pile a hand FAN is re-Shown over, so record 43 may not either");

        // The recess vocabulary is exactly the two pile arcs, stated positively so a future list
        // cannot join it by accident.
        t.True(NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListDiscard),
               "the DISCARD arc may (a short rest's sacrifice, a long rest's burn card)");
        t.True(NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListBurnt),
               "the BURNT arc may (a recover-lost pick)");
        t.True(!NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListHand),
               "the HAND may NOT — that is the two-card commit, the one secret the selection phase "
               + "exists to keep, and refusing it here is what makes it unexpressible");
        t.True(!NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListItems),
               "an item is not an ability card and never lies in a round recess");
        t.True(!NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListMapLoadout),
               "there is no control board in the map room");
        t.True(!NetProtocol.RecessSeatListAllowed(NetProtocol.HeldFaceListNone),
               "and 'no list' names nothing at all");
    }
}
