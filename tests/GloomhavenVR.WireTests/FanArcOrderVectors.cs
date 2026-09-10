// THE OWNER'S OWN LEFT-TO-RIGHT FAN ORDER — extension record 44, byte for byte.
//
// User, 2026-09-06, item 2, verbatim: "Im Test war der remote Fächer anders als der Fächer die er
// gesehen hat, heißt: Ich habe ganz rechts eine andere Karte gesehen als der Spieler selber. Das
// darf niemals passieren. Auch nach umsortieren etc. müssen die Karten exakt an den selben Stellen
// remote zu sehen sein wie lokal beim Spieler." And his ruling on the fix: "Es ist sehr wichtig,
// dass im Fächer immer die richtigen Karten am richtigen Platz liegen. D.h. jegliche
// Umsortierungen die ein Spieler tätigt MÜSSEN zwingend auch so von allen anderen Spielern gesehen
// werden. Das ist NICHT optional."
//
// WHY THE FIELD EXISTS AT ALL, since the standing preference is against adding one: the owner's arc
// order is CardsDriver._fanOrder — the player's own drag-reorder through the insertion gap, plus
// CardFan._authoredOrder's return-home insertion. It is session-local and it is an arbitrary human
// choice, so no rule on another machine can reproduce it. Every observer therefore rebuilt the hand
// from CardsHandUI.cardsUI in the game's order, and the two provably diverge: in the 2026-09-06
// session 38 of the co-player's 54 non-empty 'Fan order [scenario hand]' readings say NOT SORTED,
// and 21 of the host's 33 do.
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is five different things:
//
//   1. THE PACKING MUST ROUND-TRIP EXACTLY. Two seats share a byte (even entries in the LOW nibble,
//      odd in the high one), so an off-by-one in the nibble split does not corrupt a value — it
//      SWAPS TWO CARDS, which is the very defect this record exists to fix and would look like the
//      feature working badly rather than like a codec bug.
//
//   2. AN ODD COUNT MUST NOT LOSE ITS LAST SEAT. ceil(n/2) bytes carry an unused high nibble for an
//      odd n, and a reader that derives its count from the byte length instead of the count byte
//      would silently invent a thirteenth seat or drop the last real one.
//
//   3. IT MUST FAIL CLOSED, AND ONLY ON A REAL FAULT. ValidateFanArcOrder is the whole safety
//      contract: a repeated index, or an index out of range of the receiver's own list, must be
//      REFUSED — because an order that is merely plausible would drop one card and duplicate
//      another, drawing a confident wrong face at every seat after it. That is strictly worse than
//      the divergence the record fixes. Equally, a VALID order must be accepted, or the feature is
//      dead and the log would say "refused" forever.
//
//      SINCE ModBuild 463 A SHORTER ORDER IS VALID — an INJECTION, not a permutation — and that is
//      the load-bearing change of 2026-09-06 report item 4. `count < listLength` is the owner
//      saying "my arc holds these members of your derived list and not the rest", which is exactly
//      the state of a card in their fist or lying in one of their round recesses. The old test
//      refused it, so the record went silent in the one state it was needed, the receiver's length
//      belt had no statement of membership left, and the whole fan drew BACKS (host census, eleven
//      consecutive ticks at "8 model card(s) vs 7 slab(s) ... remainder 0"). What must still be
//      refused is the dangerous shape, and it is a DIFFERENT shape: a repeat, or an index past the
//      end. `count > listLength` — more arc seats than the receiver holds cards — stays refused
//      too, because no injection into a shorter list exists.
//
//   4. IDENTITY MUST STAY UNSAYABLE. The sender omits the record when its arc already equals the
//      derived order, so "my order is the derived one" and "no record at all" have to be ONE state.
//      This is the same argument record 43 makes for the hand, and it is what keeps an ordinary
//      player's packet byte-identical to ModBuild 461's.
//
//   5. THE RECORD BEHIND IT MUST SURVIVE. A variable-length payload must leave the reader on the
//      next record's own offset, for every length it can take.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanArcOrderVectors
{
    public static void Run(Harness t)
    {
        PackingRoundTrips(t);
        OddCountsKeepTheirLastSeat(t);
        ValidationFailsClosed(t);
        AbsenceIsTheDerivedOrder(t);
        TailSurvives(t);
        Budget(t);
    }

    /// <summary>Write one order and read it back, as the serializer really does it.</summary>
    private static bool RoundTrip(int[] order, out PresenceState got)
    {
        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasFanArcOrder = true,
            FanArcOrderCount = order.Length,
            FanArcOrder = order,
        }, ext);
        return PresenceSerializer.TryRead(ext, n, out got) && got.HasFanArcOrder;
    }

    // ---------------------------------------------------------------------------------------
    //  44a. THE PACKING. Note 1: two seats to a byte, and a nibble swap is a CARD swap.
    // ---------------------------------------------------------------------------------------
    private static void PackingRoundTrips(Harness t)
    {
        t.Case("44a. fan arc order — every seat survives the nibble packing, in its own place");

        // A deliberately non-identity permutation of the full 12-seat clamp: reversed, so that a
        // reader which returned the identity, or which mirrored the nibble split, cannot pass.
        var order = new int[NetProtocol.FanArcOrderMaxSeats];
        for (int k = 0; k < order.Length; k++)
            order[k] = order.Length - 1 - k;

        t.True(RoundTrip(order, out PresenceState got), "a full 12-seat order reads back");
        t.Equal(order.Length, got.FanArcOrderCount, "all 12 seats are delivered");
        for (int k = 0; k < order.Length; k++)
        {
            t.Equal(order[k], got.FanArcOrder![k],
                    $"seat {k} arrives as {order[k]} — an even/odd nibble mix-up here does not "
                    + "corrupt a value, it SWAPS TWO CARDS");
        }

        // …and the nibble accessors are each other's inverse in isolation, which is what the
        // writer and the reader both lean on.
        var packed = new byte[NetProtocol.FanArcOrderMaxRecordBytes];
        for (int k = 0; k < NetProtocol.FanArcOrderMaxSeats; k++)
            NetProtocol.SetFanArcOrderSeat(packed, 0, k, (k * 5) & 0x0F);
        for (int k = 0; k < NetProtocol.FanArcOrderMaxSeats; k++)
        {
            t.Equal((k * 5) & 0x0F, NetProtocol.FanArcOrderSeat(packed, 0, k),
                    $"SetFanArcOrderSeat/FanArcOrderSeat invert each other at seat {k}");
        }

        // A 12-seat hand is 6 nibble bytes plus the count byte, and the record's own declared
        // maximum has to agree — a field id with no width kills the whole record.
        t.Equal(7, NetProtocol.FanArcOrderMaxRecordBytes,
                "the declared payload maximum is count + ceil(12/2) = 7 bytes");
        t.Equal(1, NetProtocol.FanArcOrderMinRecordBytes,
                "the declared payload minimum is the count byte alone");
    }

    // ---------------------------------------------------------------------------------------
    //  44b. ODD COUNTS. Note 2: the last nibble of an odd payload is padding, not a seat.
    // ---------------------------------------------------------------------------------------
    private static void OddCountsKeepTheirLastSeat(Harness t)
    {
        t.Case("44b. fan arc order — an odd count keeps its last seat and invents no extra one");

        for (int n = 1; n <= NetProtocol.FanArcOrderMaxSeats; n++)
        {
            var order = new int[n];
            for (int k = 0; k < n; k++)
                order[k] = n - 1 - k;              // reversed again: never the identity
            t.True(RoundTrip(order, out PresenceState got), $"a {n}-seat order reads back");
            t.Equal(n, got.FanArcOrderCount,
                    $"a {n}-seat order arrives as exactly {n} seats — the count byte rules, not "
                    + "the byte length, which for an odd n has a spare nibble");
            for (int k = 0; k < n; k++)
                t.Equal(order[k], got.FanArcOrder![k], $"n={n}: seat {k} survives");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  44c. THE SAFETY CONTRACT. Note 3, and it is the reason this record is safe to point at a
    //       face list at all: a wrong permutation shifts every face after the first mistake.
    // ---------------------------------------------------------------------------------------
    private static void ValidationFailsClosed(Harness t)
    {
        t.Case("44c. fan arc order — only DISTINCT in-range seats are accepted, and they ARE");

        // The good case first. A validator that refuses everything is trivially "safe" and
        // completely useless, and its log would read 'refused' forever.
        var good = new[] { 3, 0, 2, 1 };
        t.True(NetProtocol.ValidateFanArcOrder(good, 4, 4),
               "an exact permutation of 4 seats is ACCEPTED — without this the feature is dead and "
               + "the arc silently keeps the game's order");
        var identity = new[] { 0, 1, 2, 3 };
        t.True(NetProtocol.ValidateFanArcOrder(identity, 4, 4),
               "so is the identity, which a receiver may legitimately be handed");

        // …and every way the wire can be wrong.
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 0, 1, 1, 3 }, 4, 4),
               "a REPEATED index is refused — this is the dangerous one: right length, every index "
               + "in range, and it drops one card while duplicating another");
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 0, 1, 2, 4 }, 4, 4),
               "an index PAST the receiver's list is refused");
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 0, 1, 2, -1 }, 4, 4),
               "a negative index is refused");
        // ─── THE INJECTION, WHICH IS 2026-09-06 REPORT ITEM 4's WHOLE FIX ──────────────────────
        // A SHORTER order is not a disagreement, it is a statement: the arc holds these members of
        // the receiver's derived list and not the rest. It is the ordinary state of a card in the
        // owner's fist or lying in one of their round recesses, and refusing it is what left the
        // receiver with nothing but a length to reason from — after which its belt drew the whole
        // fan as BACKS for as long as the pick stood.
        t.True(NetProtocol.ValidateFanArcOrder(good, 4, 5),
               "FOUR arc seats naming distinct members of a FIVE-card derived list are ACCEPTED — "
               + "the owner is holding one of those five up, or has laid it in a recess, and this "
               + "is the record saying WHICH four the arc carries");
        t.True(NetProtocol.ValidateFanArcOrder(new[] { 4, 0 }, 2, 5),
               "and the surviving members need not be a prefix — the dropped card can be anywhere");
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 0, 5 }, 2, 5),
               "an index past the derived list is still refused inside an injection");
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 2, 2 }, 2, 5),
               "so is a repeat — that is the shape that draws a confident wrong face, and it is the "
               + "one this validator has always existed for");
        t.True(!NetProtocol.ValidateFanArcOrder(good, 4, 3),
               "MORE arc seats than the receiver holds cards is refused: no injection into a "
               + "shorter list exists, and this is the model lagging its own fan");
        t.True(!NetProtocol.ValidateFanArcOrder(good, 5, 5),
               "a count past the array is refused rather than read off the end");
        t.True(!NetProtocol.ValidateFanArcOrder(null!, 4, 4), "no order at all is refused");
        t.True(!NetProtocol.ValidateFanArcOrder(good, 0, 0), "an empty order is refused");
        var tooMany = new int[NetProtocol.FanArcOrderMaxSeats + 4];
        for (int k = 0; k < tooMany.Length; k++)
            tooMany[k] = k;
        t.True(!NetProtocol.ValidateFanArcOrder(tooMany, tooMany.Length, tooMany.Length),
               "a hand past the 12-seat clamp is refused — a 4-bit index cannot name seat 12");
        t.True(!NetProtocol.ValidateFanArcOrder(new[] { 0, 1 }, 2, NetProtocol.FanArcOrderMaxSeats + 1),
               "and so is a SHORT order into a list past the clamp — an injection whose codomain a "
               + "4-bit index cannot span is no safer than a permutation of one");
    }

    // ---------------------------------------------------------------------------------------
    //  44d. IDENTITY IS UNSAYABLE — WHEN THE ARC HOLDS THE WHOLE LIST. Note 4: absence IS "the
    //       derived order", so an ordinary player's packet must be byte-identical to the previous
    //       build's. It is NOT unsayable when the arc is short: there the entries can still read
    //       0,1,2,… and the record is the only thing saying that the members past the count are
    //       absent, so LocalRigSampler.SampleFanArcOrder writes it anyway. That asymmetry is a
    //       property of the SENDER and is asserted at its own site; what this case pins is the
    //       serializer half — a flag with nothing to say still costs nothing.
    // ---------------------------------------------------------------------------------------
    private static void AbsenceIsTheDerivedOrder(Harness t)
    {
        t.Case("44d. fan arc order — no record means the derived order, and costs nothing");

        var bare = new byte[PresenceSerializer.MaxSize];
        int baseline = PresenceSerializer.Write(new PresenceState(), bare);

        var flagged = new byte[PresenceSerializer.MaxSize];
        int withFlag = PresenceSerializer.Write(new PresenceState
        {
            HasFanArcOrder = true,      // flag set…
            FanArcOrderCount = 0,       // …but nothing to say
            FanArcOrder = null,
        }, flagged);

        t.Equal(baseline, withFlag,
                "a set flag with no order emits NO record — an un-dragged player's packet stays "
                + "byte-identical to ModBuild 461's, which is what makes this field free in the "
                + "overwhelmingly common case");
        for (int k = 0; k < baseline; k++)
            t.Equal(bare[k], flagged[k], $"byte {k} is unchanged");

        t.True(PresenceSerializer.TryRead(bare, baseline, out PresenceState got), "the bare packet reads");
        t.True(!got.HasFanArcOrder,
               "absence decodes to 'no order stated', which the receiver reads as 'keep the order "
               + "you derived' — the picture every build before this one drew");
        t.Equal(0, got.FanArcOrderCount, "and names no seats");
    }

    // ---------------------------------------------------------------------------------------
    //  44e. THE TAIL. Note 5: a variable-length record must leave the reader on the next
    //       record's own offset, at EVERY length it can take.
    // ---------------------------------------------------------------------------------------
    private static void TailSurvives(Harness t)
    {
        t.Case("44e. fan arc order — the record behind it survives every payload length");

        for (int n = 1; n <= NetProtocol.FanArcOrderMaxSeats; n++)
        {
            var order = new int[n];
            for (int k = 0; k < n; k++)
                order[k] = n - 1 - k;

            var ext = new byte[PresenceSerializer.MaxSize];
            int len = PresenceSerializer.Write(new PresenceState
            {
                HasFanArcOrder = true,
                FanArcOrderCount = n,
                FanArcOrder = order,
                // A record written AFTER this one, so a length error here eats it.
                HasFanSource = true,
                FanSourceList = NetProtocol.HeldFaceListBurnt,
            }, ext);

            t.True(PresenceSerializer.TryRead(ext, len, out PresenceState got), $"n={n} reads back");
            t.Equal(n, got.FanArcOrderCount, $"n={n}: the order arrives");
            t.True(got.HasFanSource,
                   $"n={n}: the record BEHIND it still arrives — a payload length that is one out "
                   + "here silently swallows every later record");
            t.Equal((int)NetProtocol.HeldFaceListBurnt, (int)got.FanSourceList,
                    $"n={n}: and arrives intact, not shifted");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  44f. THE BUFFER BUDGET. PresenceSerializer.MaxSize carries a stated rule — the documented
    //       worst case plus a margin of at least one record's worth. This record moved the sum.
    // ---------------------------------------------------------------------------------------
    private static void Budget(Harness t)
    {
        t.Case("44f. fan arc order, the send buffer still satisfies its own margin rule");

        // 1738 -> 1747 on 2026-09-06: [id][len] + FanArcOrderMaxRecordBytes 7. THIS LITERAL IS A
        // CONSUMER OF THE SUM IN PresenceSerializer.MaxSize's doc block and must be re-read from it
        // every time the sum moves — it has been stale once already in this very round.
        // 1747 -> 1798 on 2026-09-07: the USE-BAR SLOT IDENTITY record (45), 51 bytes at its
        // maximum ([id][len] + its 49-byte payload: an entry count and 16 three-byte entries).
        const int documentedWorstCase = 3840; // MB497: prior3837 plus three-byte insertion record71.
        const int largestSingleRecord = 257;   // board tuning: 2 TLV + one 255-byte page
        t.True(PresenceSerializer.MaxSize >= documentedWorstCase + largestSingleRecord,
               $"MaxSize {PresenceSerializer.MaxSize} leaves "
               + $"{PresenceSerializer.MaxSize - documentedWorstCase} bytes over the documented "
               + $"worst case {documentedWorstCase}, which is at least one record's worth "
               + $"({largestSingleRecord})");

        t.Equal(9, 2 + NetProtocol.FanArcOrderMaxRecordBytes,
                "record 44's worst case is [id][len] + its 7-byte payload — the term the sum added");
    }
}
