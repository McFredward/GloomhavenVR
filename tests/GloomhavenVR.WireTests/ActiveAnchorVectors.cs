// THE ACTIVE-CARD FLIGHT ANCHOR — the card-FX endpoint byte, nibble by nibble.
//
// User, 2026-09-06, item 8b, verbatim: "Es kann so zB vorkommen, dass man eine Karte aktiviert und
// dann in der zweiten Aktion direkt nutzt. D.h. wenn eine Karte aktiviert wurde soll sie unmittelbar
// mit einer Animation wie bei den Fächern zum 'Aktiv' Bereich gehen und dort umfassend
// synchronisiert sichtbar sein für alle (auch auf dem remote board)."
//
// The mirrored flight reuses the existing machine (NetCardFx -> RemoteCardFx); all it needed was a
// name for the active matrix. That name is a new VALUE in an existing byte, NOT a new record field,
// so no width, no worst case and no PresenceSerializer.MaxSize moves. What it DOES move is the
// forward-compatibility boundary, and that is what these vectors pin.
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is four different things:
//
//   1. THE NIBBLE MUST NOT OVERFLOW. Both endpoints share ONE byte — low nibble FROM, high nibble
//      TO. Every anchor id therefore has to fit in 4 bits, and the day somebody adds a sixteenth
//      one the FROM endpoint starts eating the TO endpoint's bits and every flight in the game
//      goes to the wrong place at once. Nothing at either end can see that: the sender packs what
//      it was handed and the receiver unpacks what arrived. This test fails on the ninth-plus
//      anchor only if it overflows, which is the exact frame in which it becomes wrong.
//
//   2. THE TWO ENDPOINTS MUST NOT SWAP. "From the slot TO the active matrix" and "from the active
//      matrix TO the slot" are opposite pictures, and the second one is a card LEAVING a state it
//      is actually entering. A packing that transposes the nibbles passes any test that only
//      round-trips a symmetric pair, so every case here is deliberately asymmetric.
//
//   3. AN OLDER RECEIVER MUST DEGRADE, NEVER MISPLACE. NetCardFx.Clamp is the whole
//      flat/older-build contract: an id this build has no name for resolves to the board CENTRE, a
//      real place on the sender's own board, rather than to an unresolvable anchor or the world
//      origin. Raising Clamp's bound is the one line that has to move in lockstep with the enum,
//      and forgetting it silently turns the new anchor into Board on the CURRENT build — a flight
//      that looks nearly right and lands 0.5 m away, which is the kind of defect this project
//      loses rounds to.
//
//   4. THE ENDPOINTS BYTE MUST SURVIVE THE RECORD. The pair rides the presence extension as one
//      byte beside a sequence number; a reader that mis-sizes it corrupts every record behind it.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class ActiveAnchorVectors
{
    public static void Run(Harness t)
    {
        EveryAnchorFitsItsNibble(t);
        EndpointsDoNotSwap(t);
        UnknownIdsDegradeToBoard(t);
        TheByteSurvivesTheRecord(t);
    }

    /// <summary>Every anchor this build defines, so a new one added without a vector still meets
    /// the nibble and clamp assertions below.</summary>
    private static readonly CardFxAnchor[] All =
    {
        CardFxAnchor.HandFan, CardFxAnchor.Slot0, CardFxAnchor.Slot1, CardFxAnchor.Discard,
        CardFxAnchor.Burnt, CardFxAnchor.Items, CardFxAnchor.Board, CardFxAnchor.Active,
    };

    // ---------------------------------------------------------------------------------------
    //  8b-i. FOUR BITS EACH, AND NO TWO THE SAME. Note 1 above.
    // ---------------------------------------------------------------------------------------
    private static void EveryAnchorFitsItsNibble(Harness t)
    {
        t.Case("8b-i. card-FX anchors — every id fits its nibble and no two collide");

        for (int i = 0; i < All.Length; i++)
        {
            t.True((byte)All[i] <= 0x0F,
                   $"{All[i]} (id {(byte)All[i]}) fits the 4 bits it shares a byte with");
            for (int j = i + 1; j < All.Length; j++)
                t.True((byte)All[i] != (byte)All[j],
                       $"{All[i]} and {All[j]} are different ids — two anchors on one id is one "
                       + "flight destination silently standing in for another");
        }

        t.Equal(7, (byte)CardFxAnchor.Active,
                "the ACTIVE matrix is id 7 — the value an older receiver's Clamp bound (<= Board, "
                + "id 6) rejects, which is exactly why it degrades to Board there and must NOT "
                + "degrade here");
    }

    // ---------------------------------------------------------------------------------------
    //  8b-ii. FROM IS NOT TO. Note 2 above — the real activation flight is asymmetric.
    // ---------------------------------------------------------------------------------------
    private static void EndpointsDoNotSwap(Harness t)
    {
        t.Case("8b-ii. card-FX anchors — the two endpoints keep their own nibbles");

        // The flight the user asked for: a card leaves the round-card recess it was played from and
        // arrives in the active matrix. Never the reverse.
        (CardFxAnchor from, CardFxAnchor to, string what)[] cases =
        {
            (CardFxAnchor.Slot0, CardFxAnchor.Active, "recess 1 -> active matrix"),
            (CardFxAnchor.Slot1, CardFxAnchor.Active, "recess 2 -> active matrix"),
            (CardFxAnchor.Board, CardFxAnchor.Active, "board centre -> active matrix (slot unknown)"),
            (CardFxAnchor.Active, CardFxAnchor.Discard, "active matrix -> discard (the reverse)"),
        };

        foreach ((CardFxAnchor from, CardFxAnchor to, string what) in cases)
        {
            NetCardFx.Reset();
            NetCardFx.Report(from, to);
            t.True(NetCardFx.TryDequeue(out byte endpoints, out byte seq), $"{what}: it queues");
            t.True(seq != 0, $"{what}: a real event stamps a fresh sequence number");
            t.Equal((int)from, (int)NetCardFx.From(endpoints), $"{what}: FROM survives");
            t.Equal((int)to, (int)NetCardFx.To(endpoints), $"{what}: TO survives");
            t.True(NetCardFx.From(endpoints) != NetCardFx.To(endpoints),
                   $"{what}: the two endpoints are still different — a transposed pack would make "
                   + "an asymmetric flight read as its own mirror image");
        }

        NetCardFx.Reset();
    }

    // ---------------------------------------------------------------------------------------
    //  8b-iii. THE OLDER-BUILD CONTRACT. Note 3 above.
    // ---------------------------------------------------------------------------------------
    private static void UnknownIdsDegradeToBoard(Harness t)
    {
        t.Case("8b-iii. card-FX anchors — an id this build cannot name lands on the board centre");

        // Every id above the last one we define. A future build using any of them must reach this
        // one as the board centre — a real place on the sender's own board — and never as an
        // unresolvable anchor.
        for (byte id = (byte)((byte)CardFxAnchor.Active + 1); id <= 0x0F; id++)
        {
            byte packed = (byte)(id | (id << 4));
            t.Equal((int)CardFxAnchor.Board, (int)NetCardFx.From(packed),
                    $"an unknown FROM id {id} degrades to the board centre");
            t.Equal((int)CardFxAnchor.Board, (int)NetCardFx.To(packed),
                    $"an unknown TO id {id} degrades to the board centre");
        }

        // …and the one this change added must NOT be among them. This is the assertion that fails
        // if the enum gains a value and Clamp's bound does not follow it: the flight would still
        // "work", quietly, to the wrong point.
        byte active = (byte)((byte)CardFxAnchor.Active | ((byte)CardFxAnchor.Active << 4));
        t.Equal((int)CardFxAnchor.Active, (int)NetCardFx.From(active),
                "the ACTIVE anchor is NOT clamped away on a build that defines it — Clamp's bound "
                + "moved with the enum");
        t.Equal((int)CardFxAnchor.Active, (int)NetCardFx.To(active),
                "…in the high nibble too");
    }

    // ---------------------------------------------------------------------------------------
    //  8b-iv. THE RECORD BEHIND IT. Note 4 above.
    // ---------------------------------------------------------------------------------------
    private static void TheByteSurvivesTheRecord(Harness t)
    {
        t.Case("8b-iv. card-FX anchors — an ACTIVE pair rides the presence record intact");

        NetCardFx.Reset();
        NetCardFx.Report(CardFxAnchor.Slot1, CardFxAnchor.Active);
        t.True(NetCardFx.TryDequeue(out byte endpoints, out byte seq), "the event dequeues");

        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasCardFx = true,
            FxSeq = seq,
            FxEndpoints = endpoints,
            // A record BEHIND it, so a mis-sized read of the FX record is caught by the next
            // record's contents rather than by silence.
            HasRoundHalfSpent = true,
            RoundHalfSpentMask = NetProtocol.RoundHalfSpentSlot1Bottom,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

        t.True(got.HasCardFx, "the card-FX record is delivered");
        t.Equal((int)seq, (int)got.FxSeq, "the sequence number survives");
        t.Equal((int)endpoints, (int)got.FxEndpoints, "the endpoint byte survives");
        t.Equal((int)CardFxAnchor.Slot1, (int)NetCardFx.From(got.FxEndpoints),
                "…and still decodes FROM recess 2");
        t.Equal((int)CardFxAnchor.Active, (int)NetCardFx.To(got.FxEndpoints),
                "…TO the active matrix, which is the flight user item 8b asked for");

        t.True(got.HasRoundHalfSpent, "the record behind it survived");
        t.Equal((int)NetProtocol.RoundHalfSpentSlot1Bottom, (int)got.RoundHalfSpentMask,
                "…with its own contents, so the FX record was stepped over by its real width");

        NetCardFx.Reset();
    }
}
