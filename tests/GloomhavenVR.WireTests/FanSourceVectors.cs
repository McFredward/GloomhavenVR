// WHICH PILE THE SENDER'S FAN IS DRAWN FROM — extension record 43, byte for byte.
//
// User, 2026-09-06, item 7, verbatim: "Bei einer langen Rast werden bei dem betroffenen Spieler die
// Handkarten zu den abgeworfenen Karten. Diese sollen auch mit der Vorderseite sichtbar sein für
// alle Spieler. Aktuell ist der Fächer als auch die Karte in der Hand des Spielers wieder nur die
// Rückseite obwohl es sich hier nicht um die Auswahlphase handelt."
//
// AND THE FIRST THING TO SAY ABOUT IT IS THAT THE PHASE WAS NOT THE BLOCKER. The host's own census
// stands at RevealGate.ShowRoundCardFronts(actor)=true for the whole of the co-player's long rest,
// with "hand fan[p2] 0 FRONT / 2 BACK — LENGTH BELT: 6 model card(s) vs 2 slab(s) on the wire"
// beside it. The gate was OPEN. What was shut was the ARITHMETIC: during a long rest's burn step
// the game re-Shows the owner's hand over the DISCARD pile (the co-player's own log line, "Pick fan
// source (LoseCard): discard pile"), so the arc the owner holds up is their discard pile — while
// every observer resolved it as the HAND and got a list of the wrong length. This record is the one
// fact neither client can compute: WHICH pile is being fanned.
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is four different things:
//
//   1. THE HAND MUST STAY UNSAYABLE. The hand is the DEFAULT — it is what every receiver already
//      resolves and what every sender predating this record means — so "my fan is my hand" and "no
//      record at all" have to be ONE state on the wire. Two spellings of one picture is how a
//      mirror latches: a fan that stopped being a pick would keep resolving against the discard
//      list, and every face in it would be a confidently wrong card.
//
//   2. AN UNKNOWN LIST MUST DEGRADE TO THE HAND, NOT TO A PILE. Three of the six ids in the shared
//      HeldFaceList vocabulary are lists a hand fan is never drawn from, and ids past the maximum
//      belong to a build that does not exist yet. Every one of them has to read as "no record",
//      because the hand is the picture this receiver already draws and a wrong PILE is a face from
//      one card printed on another.
//
//   3. THE TWO PILES MUST NOT SWAP. Discard and burnt are different lists of different lengths,
//      and nothing at either end can see a swap — the sender writes what it sampled, the receiver
//      walks what it was told.
//
//   4. THE RECORD BEHIND IT MUST SURVIVE. A 1-byte payload must leave the reader on the next
//      record's own offset.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanSourceVectors
{
    public static void Run(Harness t)
    {
        EachPileIsItsOwnList(t);
        TheHandIsUnsayable(t);
        UnknownListsAreTheHand(t);
        TailSurvives(t);
    }

    // ---------------------------------------------------------------------------------------
    //  43a. THE TWO PILES, ONE AT A TIME. Each is written alone and must come back naming exactly
    //       its own list and no other — the swap in note 3 above.
    // ---------------------------------------------------------------------------------------
    private static void EachPileIsItsOwnList(Harness t)
    {
        t.Case("43a. fan source — each pile arrives as exactly its own list, and only its own");

        (byte list, string name)[] cases =
        {
            (NetProtocol.HeldFaceListDiscard, "the DISCARD pile (a long rest's burn step)"),
            (NetProtocol.HeldFaceListBurnt, "the BURNT pile (a recover-lost)"),
        };

        foreach ((byte list, string name) in cases)
        {
            var ext = new byte[PresenceSerializer.MaxSize];
            int n = PresenceSerializer.Write(new PresenceState
            {
                HasFanSource = true,
                FanSourceList = list,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

            t.True(got.HasFanSource, $"{name} is delivered");
            t.Equal((int)list, (int)got.FanSourceList, $"{name} arrives as exactly its own list id");

            // …and the OTHER pile is not what arrived. Asserting only that the wanted list came
            // back passes just as happily when the reader hands out a constant.
            byte other = list == NetProtocol.HeldFaceListDiscard
                ? NetProtocol.HeldFaceListBurnt
                : NetProtocol.HeldFaceListDiscard;
            t.True(got.FanSourceList != other,
                   $"{name} is NOT the other pile — a swap here draws every face in the fan from "
                   + "the wrong list, and neither end can see it");
        }

        // The two ids this record may carry, and no third. A future pile has to be added to
        // IsFanSourcePile deliberately, not by an id happening to be in range.
        t.True(NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListDiscard),
               "the discard pile is a fannable pile");
        t.True(NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListBurnt),
               "the burnt pile is a fannable pile");
        t.True(!NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListHand),
               "the HAND is not — it is the default and is unsayable here (note 1)");
        t.True(!NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListItems),
               "an item list is not a fan a hand can hold");
        t.True(!NetProtocol.IsFanSourcePile(NetProtocol.HeldFaceListMapLoadout),
               "a map loadout is not a pick fan");
    }

    // ---------------------------------------------------------------------------------------
    //  43b. THE HAND COSTS NOTHING AND CANNOT BE SAID. Note 1: absence IS "the hand", so an
    //       explicit hand id must emit no record and decode identically to a packet without one.
    // ---------------------------------------------------------------------------------------
    private static void TheHandIsUnsayable(Harness t)
    {
        t.Case("43b. fan source — the HAND emits no record, and absence decodes to the same thing");

        var bare = new byte[PresenceSerializer.MaxSize];
        int baseline = PresenceSerializer.Write(new PresenceState(), bare);

        var handed = new byte[PresenceSerializer.MaxSize];
        int withHand = PresenceSerializer.Write(new PresenceState
        {
            HasFanSource = true,            // the flag is set and the list is the hand…
            FanSourceList = NetProtocol.HeldFaceListHand,
        }, handed);

        t.Equal(baseline, withHand,
                "an ordinary hand fan emits NO record — every packet of every player who is not "
                + "mid-pick is byte-identical to ModBuild 458's, which is nearly all of them");

        t.True(PresenceSerializer.TryRead(handed, withHand, out PresenceState got), "it reads back");
        t.True(!got.HasFanSource, "…and it decodes as 'no record', i.e. the hand");

        t.True(PresenceSerializer.TryRead(bare, baseline, out PresenceState none),
               "the record-free packet reads back too");
        t.Equal((int)none.FanSourceList, (int)got.FanSourceList,
                "absence and an explicit hand are the same picture — a mirror that told them apart "
                + "would keep resolving a discard arc after the pick that fanned it closed");
    }

    // ---------------------------------------------------------------------------------------
    //  43c. AN ID THIS BUILD CANNOT NAME IS THE HAND. Note 2 — the degradation that cannot put a
    //       face from one pile onto a card from another.
    // ---------------------------------------------------------------------------------------
    private static void UnknownListsAreTheHand(Harness t)
    {
        t.Case("43c. fan source — an item list, a map loadout and a future id all read as the hand");

        byte[] notPiles =
        {
            NetProtocol.HeldFaceListNone,
            NetProtocol.HeldFaceListHand,
            NetProtocol.HeldFaceListItems,
            NetProtocol.HeldFaceListMapLoadout,
            (byte)(NetProtocol.HeldFaceListMax + 1),   // a build that does not exist yet
            0x7F,
        };

        foreach (byte list in notPiles)
        {
            var ext = new byte[PresenceSerializer.MaxSize];
            int n = PresenceSerializer.Write(new PresenceState
            {
                HasFanSource = true,
                FanSourceList = list,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");
            t.True(!got.HasFanSource,
                   $"list id {list} is not a pile a fan can be, so it decodes to 'no record' — the "
                   + "HAND, which is the picture this receiver drew before the record existed");
        }

        // The same on the READ side against a hand-built packet: a sender from a later build really
        // can put an id on the wire that this one has no name for, and the writer above would
        // simply have omitted it. Only a packet nobody's writer produced tests the reader.
        foreach (byte list in new byte[] { NetProtocol.HeldFaceListItems, 0xE0 })
        {
            byte[] packet = Tail($"2B 01 {list:X2}");
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out PresenceState got),
                   $"a hand-built record naming list {list} still parses");
            t.True(!got.HasFanSource,
                   $"…and list {list} draws the HAND rather than a pile nobody named");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  43d. A 1-BYTE PAYLOAD MUST NOT SWALLOW ITS NEIGHBOUR.
    // ---------------------------------------------------------------------------------------
    private static void TailSurvives(Harness t)
    {
        t.Case("43d. fan source — the record beside it still arrives");

        var ext = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasFanSource = true,
            FanSourceList = NetProtocol.HeldFaceListDiscard,
            // The record this one is ABOUT: the card plucked out of that very fan. During a long
            // rest the two describe one arc, and before this build they disagreed about which list
            // it was — so a vector that carries both is the shape of the defect itself.
            HasHeldCardFace = true,
            HeldFaceCode = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListDiscard, 1),
            HeldFaceCount = 2,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState got), "it reads back");

        t.True(got.HasFanSource, "the fan-source record is delivered");
        t.Equal((int)NetProtocol.HeldFaceListDiscard, (int)got.FanSourceList,
                "…naming the discard pile");
        t.True(got.HasHeldCardFace, "and the held-card face beside it survived the 1-byte payload");
        t.Equal((int)NetProtocol.HeldFaceListDiscard,
                (int)NetProtocol.HeldFaceList(got.HeldFaceCode),
                "…on its own offset, seated in THE SAME list the fan says it is fanning — which is "
                + "the whole of report item 7: the fan and the card in the fist are one arc");
        t.Equal(1, NetProtocol.HeldFaceIndex(got.HeldFaceCode), "…at its own seat");
    }

    /// <summary>A minimal extras packet whose extension tail is exactly <paramref name="body"/>.
    /// Hand-built rather than produced by the writer, because a golden vector made by calling the
    /// code it tests proves nothing — and because the writer is specifically incapable of producing
    /// the packets 43c needs (it omits every non-pile id).</summary>
    private static byte[] Tail(string body, int records = 1) => Hex.Bytes(
        "31 52 56 47 03 01 80 00 80 00 " + records.ToString("X2") + "\n" + body);
}
