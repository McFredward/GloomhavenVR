// WHICH ROUND-CARD HALF IS ALREADY SPENT — extension record 41, byte for byte.
//
// User, 2026-09-05, item 8, verbatim: "Die Kartenanimation wenn eine der beiden Karten schon
// benutzt wurde, wird nicht synchronisiert. Auch hier will ich 1:1 das selbe sehen. Also wenn die
// Karte grau wird weil sie schon benutzt wurde oder 'verbrennt'. So ist auch für die Mitspieler
// ersichtlich, welche der beiden Karten bereits benutzt/verbrannt wurde. Die 1:1 Regel verlangt es."
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is four different things:
//
//   1. THE FOUR BITS MUST NOT SWAP. The whole point of the record is telling the player WHICH of
//      their two cards, and WHICH half of it, is already gone. A recess-1-top bit that arrives as
//      recess-2-top is a confidently WRONG picture: the peer greys a card the owner can still
//      play, which is worse than the undimmed board this record replaces, because the player reads
//      it as information and plans a turn around it. Nothing at either end can see the swap — the
//      sender writes what it sampled, the receiver dims what it was told.
//
//   2. AN UNKNOWN BIT MUST DRAW NOTHING. Half the byte is unassigned. A future build putting a
//      fifth field in bits 4..7 must not reach this one as a dimmed half, so the mask is ANDed on
//      the way in as well as on the way out. UNDEFINED IS ALWAYS BRIGHT — the picture a peer
//      predating the record draws, and therefore the only degradation that cannot mislead.
//
//   3. ABSENCE HAS TO STAY FREE, and it has to stay UNAMBIGUOUS. The sender omits the record for
//      an empty mask; the reader must therefore decode an explicit zero to exactly the same thing,
//      or a peer would keep a stale dim across the round boundary that clears it. Two spellings of
//      "nothing is spent" that decode differently is how a mirror latches.
//
//   4. THE RECORD BEHIND IT MUST SURVIVE. A 1-byte payload must leave the reader on the next
//      record's own offset.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SpentHalfVectors
{
    public static void Run(Harness t)
    {
        EachBitIsItsOwnHalf(t);
        UnknownBitsAreBright(t);
        AbsentAndExplicitZeroAgree(t);
        TailSurvives(t);
    }

    // ---------------------------------------------------------------------------------------
    //  41a. THE FOUR BITS, ONE AT A TIME. Each is written alone and must come back naming exactly
    //       its own (recess, half) and no other — the swap in note 1 above.
    // ---------------------------------------------------------------------------------------
    private static void EachBitIsItsOwnHalf(Harness t)
    {
        t.Case("41a. spent half — each bit names its own recess and half, and only its own");

        (byte bit, int slot, bool top, string name)[] cases =
        {
            (NetProtocol.RoundHalfSpentSlot0Top,    0, true,  "recess 1 TOP"),
            (NetProtocol.RoundHalfSpentSlot0Bottom, 0, false, "recess 1 BOTTOM"),
            (NetProtocol.RoundHalfSpentSlot1Top,    1, true,  "recess 2 TOP"),
            (NetProtocol.RoundHalfSpentSlot1Bottom, 1, false, "recess 2 BOTTOM"),
        };

        foreach ((byte bit, int slot, bool top, string name) in cases)
        {
            var ext = new byte[PresenceSerializer.MaxSize];
            int n = PresenceSerializer.Write(new PresenceState
            {
                HasRoundHalfSpent = true,
                RoundHalfSpentMask = bit,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

            t.True(got.HasRoundHalfSpent, $"{name} is delivered");
            t.Equal((int)bit, (int)got.RoundHalfSpentMask, $"{name} arrives as exactly its own bit");

            // …and every OTHER half is bright. This is the half of the test that catches a swap:
            // asserting the wanted half is dim passes just as happily when all four are dim.
            for (int s = 0; s < 2; s++)
            {
                foreach (bool half in new[] { true, false })
                {
                    bool want = s == slot && half == top;
                    t.Equal(want, NetProtocol.RoundHalfIsSpent(got.RoundHalfSpentMask, s, half),
                            $"{name}: recess {s + 1} {(half ? "top" : "bottom")} is "
                            + (want ? "DIM" : "bright"));
                }
            }
        }

        // A slot this record does not describe can never alias onto one it does — the guard that
        // keeps a third recess from greying recess 1.
        t.Equal(0, (int)NetProtocol.RoundHalfSpentBit(2, top: true),
                "a third recess has no bit");
        t.True(!NetProtocol.RoundHalfIsSpent(0xFF, 2, top: true),
                "…and cannot be spent even against an all-ones mask");
    }

    // ---------------------------------------------------------------------------------------
    //  41b. THE SPARE BITS DRAW NOTHING. A sender from a later build sets bits 4..7; this build
    //       must strip them rather than dim a half it has no name for.
    // ---------------------------------------------------------------------------------------
    private static void UnknownBitsAreBright(Harness t)
    {
        t.Case("41b. spent half — bits this build does not define are stripped, not drawn");

        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasRoundHalfSpent = true,
            // recess 1 top, plus four bits from a build that does not exist yet
            RoundHalfSpentMask = (byte)(NetProtocol.RoundHalfSpentSlot0Top | 0xF0),
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

        t.True(got.HasRoundHalfSpent, "the record survives the unknown bits");
        t.Equal((int)NetProtocol.RoundHalfSpentSlot0Top, (int)got.RoundHalfSpentMask,
                "only the four defined bits arrive — the spare nibble is stripped on the way out "
                + "AND on the way in, so neither end can dim a half nobody named");

        // A mask of NOTHING BUT unknown bits is "nothing is spent" — not "a record arrived", which
        // would leave a receiver holding HasRoundHalfSpent with no half to draw.
        var ext2 = new byte[PresenceSerializer.MaxSize];
        int n2 = PresenceSerializer.Write(new PresenceState
        {
            HasRoundHalfSpent = true,
            RoundHalfSpentMask = 0xF0,
        }, ext2);
        t.True(PresenceSerializer.TryRead(ext2, n2, out PresenceState got2), "it reads back");
        t.True(!got2.HasRoundHalfSpent,
               "a mask of only unknown bits decodes to 'record absent', i.e. a bright board");
    }

    // ---------------------------------------------------------------------------------------
    //  41c. THE TWO SPELLINGS OF "NOTHING" MUST AGREE, and the empty one must cost no bytes.
    // ---------------------------------------------------------------------------------------
    private static void AbsentAndExplicitZeroAgree(Harness t)
    {
        t.Case("41c. spent half — absence and an explicit zero decode alike, and both are free");

        var bare = new byte[PresenceSerializer.MaxSize];
        int baseline = PresenceSerializer.Write(new PresenceState(), bare);

        var zeroed = new byte[PresenceSerializer.MaxSize];
        int withFlag = PresenceSerializer.Write(new PresenceState
        {
            HasRoundHalfSpent = true,   // the flag is set and the mask is empty…
            RoundHalfSpentMask = 0,
        }, zeroed);

        t.Equal(baseline, withFlag,
                "an empty mask emits NO record — a round before anything is played is byte-"
                + "identical to ModBuild 448's, so the record costs nothing on the packets that "
                + "are nearly all of them");

        t.True(PresenceSerializer.TryRead(zeroed, withFlag, out PresenceState got), "it reads back");
        t.True(!got.HasRoundHalfSpent, "…and it decodes as 'nothing is spent'");
        t.Equal(0, (int)got.RoundHalfSpentMask, "with an empty mask");

        // The same reading as a packet that never carried the record at all. These two MUST agree:
        // the sender uses omission to mean "nothing is spent", so a receiver that treated absence
        // as "no news" would keep the previous round's dimming on a fresh card.
        t.True(PresenceSerializer.TryRead(bare, baseline, out PresenceState none),
               "the record-free packet reads back too");
        t.Equal((int)none.RoundHalfSpentMask, (int)got.RoundHalfSpentMask,
                "absence and an explicit zero are the same picture — a mirror that told them apart "
                + "would latch a stale dim across the round boundary that clears it");
    }

    // ---------------------------------------------------------------------------------------
    //  41d. A 1-BYTE PAYLOAD MUST NOT SWALLOW ITS NEIGHBOUR.
    // ---------------------------------------------------------------------------------------
    private static void TailSurvives(Harness t)
    {
        t.Case("41d. spent half — the record behind it still arrives");

        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasRoundHalfSpent = true,
            RoundHalfSpentMask = NetProtocol.RoundHalfSpentSlot1Bottom,
            // A neighbour with a payload of its own, so a reader that mis-stepped by one byte
            // would corrupt something loud rather than something invisible.
            HasSacrificeSeat = true,
            SacrificeSeatCode0 = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListDiscard, 3),
            SacrificeSeatCount0 = 8,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

        t.True(got.HasRoundHalfSpent, "the spent-half record is delivered");
        t.Equal((int)NetProtocol.RoundHalfSpentSlot1Bottom, (int)got.RoundHalfSpentMask,
                "…naming recess 2's bottom half");
        t.True(got.HasSacrificeSeat, "and the record beside it survived the 1-byte payload");
        t.Equal(3, (int)NetProtocol.HeldFaceIndex(got.SacrificeSeatCode0),
                "…on its own offset, with its own seat intact");
    }
}
