// WHICH BONUS OR ITEM A PEER'S USE-BAR SLOT IS SHOWING — extension record 45, byte for byte.
//
// User, 2026-09-07, item 8, verbatim: "Die entsprechenden Symbole sehe ich auch nicht. … Es ist von
// äußerster Wichtigkeit dass hier die 1:1 Regel eingehalten wird und jeder Spieler genau das selbe
// sieht wie der lokale Spieler bei sich bei diesen Entscheidungssymbolen." He called item 8 the most
// important point of that round.
//
// WHY THE FIELD EXISTS AT ALL, since the standing preference is hard against adding one: because for
// this prompt the zero-wire local resolve CANNOT work, and that is a fact about the game rather than
// about the mod. UIScenarioMultiplayerController.RefreshDamagePhase (:212-249) branches on the CARD
// OWNER — m_ActorToShowCardsFor ?? m_ActorBeingAttacked (:216-218) — not on the attacked actor,
// testing CPlayerActor.IsUnderMyControl (:238), CHeroSummonActor.Summoner.IsUnderMyControl (:233) or
// FFSNetwork.IsHost (:229) by that actor's type, and sends every non-controlling client to
// TakeDamagePanel.ShowOtherPlayer (:242). ShowOtherPlayer HIDES both bars rather than merely not
// raising them: ResetToggles() at TakeDamagePanel.cs:1122 calls UIUseItemsBar.Hide() (:434) and
// UIActiveBonusBar.Hide() (:435), each of which Clear()s its slots back to the pool, and it ends on
// myWindow.Hide(instant: true) at :1133. Only TakeDamagePanel.Show reaches those two calls. So on the
// watcher's machine those two bars are never populated for that actor, RemoteUseBarSymbols' gate 2
// is false BY CONSTRUCTION, and no better local rule can exist. Both logs agree: the owner printed
// "USE BARS: 'UseBarActiveBonus' docked/VISIBLE" and the watcher's 55,616-line log has not one
// "[WorldUI] USE BARS" line for that bar, only "DOCK MIRROR: no mirrored use-bar symbol resolved".
//
// WHAT ONLY A TEST CAN CATCH HERE, and it is six different things:
//
//   1. THE ADDRESSING BYTE MUST NOT SWAP ITS ENDS. bar:3 in the HIGH bits, slot:5 in the LOW ones.
//      A writer and reader that swapped them together would still round-trip perfectly and would
//      put every symbol on the WRONG BAR — which looks like the feature working badly rather than
//      like a codec bug, exactly the failure mode record 44's nibble packing has.
//
//   2. AN ID MUST LAND ON THE SLOT IT NAMES. The ids are addressed, not positional, so an
//      off-by-one in the stride would paint a neighbour's decision onto a live tile. That is the
//      one outcome this whole area exists to prevent ("a symbol from somebody else's decision would
//      be worse than none"), and it is worse than the anonymous plate it replaces.
//
//   3. IT MUST FAIL CLOSED ON A HOSTILE RECORD. A lying entry count, a bar index this build has no
//      seat for (4..7 are addressable and undefined), a slot past UseBarsMaxSlots, a truncated
//      tail: every one of these must drop the entry rather than clamp it onto a neighbour. Clamping
//      is what would move a symbol; dropping only costs the tile its picture.
//
//   4. NoIdentity MUST STAY RESERVED. 0 is the sender's own "this slot has none". The fold must
//      never produce it, and a record that carries it must not store it — otherwise "the owner
//      named nothing here" and "the owner named something" become the same state.
//
//   5. ABSENCE MUST BE UNSAYABLE. A drawer whose slots carry no identity — the abilities/augment
//      case, and EVERY drawer on every build before 479 — must emit not one byte, so a ModBuild-478
//      co-player's packets and ours stay byte-identical. This is the same argument records 43 and
//      44 make, and it is what makes the record safe to ship mid-campaign.
//
//   6. THE RECORD BEHIND IT MUST SURVIVE. A variable-length payload must leave the reader on the
//      next record's own offset, for every length it can take.

using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class UseBarSlotIdentityVectors
{
    public static void Run(Harness t)
    {
        AddressingKeepsItsEnds(t);
        ExactBytes(t);
        EveryCarriedSlotRoundTrips(t);
        AbsenceIsUnsayable(t);
        HostileRecordsFailClosed(t);
        TheFoldReservesNoIdentity(t);
        TailSurvives(t);
        Budget(t);
    }

    /// <summary>A state whose bars 0 and 3 are up with the given slot counts, carrying
    /// <paramref name="ids"/>. Mirrors what NetAvatarDriver hands the writer.</summary>
    private static PresenceState Bars(int bar0Slots, int bar3Slots, ushort[] ids)
    {
        var counts = new byte[NetProtocol.UseBarsCount];
        counts[0] = (byte)bar0Slots;
        counts[3] = (byte)bar3Slots;
        byte mask = 0;
        if (bar0Slots > 0) mask |= NetProtocol.UseBarActiveBonusBit;
        if (bar3Slots > 0) mask |= NetProtocol.UseBarItemsBit;
        return new PresenceState
        {
            HasUseBars = true,
            UseBarsMask = mask,
            UseBarFlags = new byte[NetProtocol.UseBarsCount],
            UseBarSlotCounts = counts,
            UseBarSlotStates =
                new byte[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots],
            HasUseBarSlotIds = true,
            UseBarSlotIds = ids,
        };
    }

    private static ushort[] EmptyIds() =>
        new ushort[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];

    /// <summary>A minimal extras packet whose extension tail is exactly <paramref name="body"/>.
    /// Hand-built rather than produced by the writer, because a golden vector made by calling the
    /// code it tests proves nothing — and because the writer is specifically incapable of producing
    /// the malformed records case 45e needs.</summary>
    private static byte[] Tail(string body, int records = 1) => Hex.Bytes(
        "31 52 56 47 03 01 80 00 80 00 " + records.ToString("X2") + "\n" + body);

    // ---------------------------------------------------------------------------------------
    //  45a. THE ADDRESSING BYTE. Note 1: bar HIGH, slot LOW — a swap is a wrong-bar symbol.
    // ---------------------------------------------------------------------------------------
    private static void AddressingKeepsItsEnds(Harness t)
    {
        t.Case("45a. use-bar slot identity — the addressing byte keeps bar in the HIGH bits");

        // Every value the byte can hold, both ways. 8 x 32 = 256 pairs, and the sweep is the point:
        // a codec tested on one or two hand-picked pairs passes just as happily when the two fields
        // are swapped, because bar 0 slot 0 is 0x00 either way.
        for (int bar = 0; bar < 8; bar++)
        {
            for (int slot = 0; slot < 32; slot++)
            {
                byte addr = NetProtocol.UseBarSlotAddr(bar, slot);
                t.Equal(bar, NetProtocol.UseBarSlotAddrBar(addr),
                        $"addr 0x{addr:X2} decodes back to bar {bar}");
                t.Equal(slot, NetProtocol.UseBarSlotAddrSlot(addr),
                        $"addr 0x{addr:X2} decodes back to slot {slot}");
            }
        }

        // …and the ends are named explicitly, so a future "tidy the shifts" pass that swapped them
        // consistently in both directions still fails HERE even though the sweep above would pass.
        t.Equal(0x20, (int)NetProtocol.UseBarSlotAddr(1, 0),
                "bar 1 slot 0 is 0x20 — the bar lives in the HIGH three bits, not the low five");
        t.Equal(0x01, (int)NetProtocol.UseBarSlotAddr(0, 1),
                "bar 0 slot 1 is 0x01 — the slot lives in the LOW five bits");
        t.Equal(0x60, (int)NetProtocol.UseBarSlotAddr(3, 0),
                "the items bar (3), slot 0, is 0x60 — the byte the golden vector below spells out");
    }

    // ---------------------------------------------------------------------------------------
    //  45b. THE BYTES THEMSELVES.
    // ---------------------------------------------------------------------------------------
    private static void ExactBytes(Harness t)
    {
        t.Case("45b. use-bar slot identity — the exact bytes, appended behind record 25");

        var ext = new byte[PresenceSerializer.MaxSize];
        ushort[] ids = EmptyIds();
        ids[0] = 0xBEEF;                                   // bar 0, slot 0
        ids[1] = UseBarSlotIdentity.NoIdentity;            // bar 0, slot 1 — the owner named none
        ids[3 * NetProtocol.UseBarsMaxSlots] = 0x1234;     // bar 3, slot 0
        int m = PresenceSerializer.Write(Bars(2, 1, ids), ext);

        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            02               // tail: 2 records
            19 08            // id 25 (use bars), len 8
            09               // bar mask: activeBonus (bit0) + items (bit3)
            00 02 00 00      // bar 0: no picker, 2 slots, both plain
            00 01 00         // bar 3: no picker, 1 slot, plain
            2D 07            // id 45 (use-bar slot identity), len 7
            02               // 2 entries -- slot 1 of bar 0 named nothing and is ABSENT, not zero
            00 EF BE         // bar 0 slot 0 -> id 0xBEEF, little-endian
            60 34 12         // bar 3 slot 0 -> id 0x1234
            "), ext, m,
            "record 45 is [id 45][len][entries] then one [bar:3|slot:5][idLo][idHi] per NAMED slot, "
            + "appended LAST — a slot the owner could not identify costs no bytes at all");

        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), "and it parses");
        t.True(got.HasUseBarSlotIds, "…as a record that is present");
        t.Equal(0xBEEF, (int)got.UseBarSlotIds![0], "bar 0 slot 0 came back with its own id");
        t.Equal(0, (int)got.UseBarSlotIds[1],
                "bar 0 slot 1 stayed NoIdentity — an unnamed slot must not inherit a neighbour's");
        t.Equal(0x1234, (int)got.UseBarSlotIds[3 * NetProtocol.UseBarsMaxSlots],
                "bar 3 slot 0 landed on the ITEMS bar, not on bar 1 — the high/low bits held");
    }

    // ---------------------------------------------------------------------------------------
    //  45c. EVERY CARRIED SLOT. Note 2: an id must land on the slot it names, and nowhere else.
    // ---------------------------------------------------------------------------------------
    private static void EveryCarriedSlotRoundTrips(Harness t)
    {
        t.Case("45c. use-bar slot identity — every addressable slot of both carried bars, alone");

        var ext = new byte[PresenceSerializer.MaxSize];
        foreach (int bar in new[] { 0, 3 })
        {
            for (int slot = 0; slot < NetProtocol.UseBarsMaxSlots; slot++)
            {
                int k = (bar * NetProtocol.UseBarsMaxSlots) + slot;
                ushort id = (ushort)(0x0101 + (k * 7));    // distinct, and never 0
                ushort[] ids = EmptyIds();
                ids[k] = id;
                int bar0 = bar == 0 ? NetProtocol.UseBarsMaxSlots : 0;
                int bar3 = bar == 3 ? NetProtocol.UseBarsMaxSlots : 0;
                int m = PresenceSerializer.Write(Bars(bar0, bar3, ids), ext);

                t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got),
                       $"bar {bar} slot {slot}: the packet parses");
                t.Equal(id, got.UseBarSlotIds![k],
                        $"bar {bar} slot {slot}: the id came back on its OWN subscript");

                // …and on NO other. This is the assertion that catches a stride bug, which a
                // single-slot round-trip on slot 0 cannot: 0 is the same index under every stride.
                int strays = 0;
                for (int j = 0; j < got.UseBarSlotIds.Length; j++)
                {
                    if (j != k && got.UseBarSlotIds[j] != UseBarSlotIdentity.NoIdentity)
                        strays++;
                }
                t.Equal(0, strays,
                        $"bar {bar} slot {slot}: no OTHER slot picked up an id — a stride or "
                        + "addressing slip would paint a neighbour's decision onto a live tile");
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  45d. ABSENCE. Note 5: a drawer with no identifiable slot is byte-identical to ModBuild 478.
    // ---------------------------------------------------------------------------------------
    private static void AbsenceIsUnsayable(Harness t)
    {
        t.Case("45d. use-bar slot identity — a drawer that can name nothing emits nothing");

        var without = new byte[PresenceSerializer.MaxSize];
        var withFlag = new byte[PresenceSerializer.MaxSize];

        // The SAME bars, once with the record switched off and once with it on but every id
        // NoIdentity — which is precisely what an abilities/augment drawer produces, and what every
        // drawer produced before ModBuild 479.
        PresenceState bare = Bars(2, 1, EmptyIds());
        bare.HasUseBarSlotIds = false;
        bare.UseBarSlotIds = null;
        int a = PresenceSerializer.Write(bare, without);
        int b = PresenceSerializer.Write(Bars(2, 1, EmptyIds()), withFlag);

        t.Equal(a, b, "a drawer whose every slot is NoIdentity writes the same NUMBER of bytes as "
                      + "one that does not carry the record at all");
        bool identical = true;
        for (int i = 0; i < a; i++)
        {
            if (without[i] != withFlag[i]) { identical = false; break; }
        }
        t.True(identical,
               "…and the same BYTES: a ModBuild-478 co-player and a 479 one with nothing to name "
               + "put the identical packet on the wire, which is what makes this record safe to "
               + "ship into a campaign already in progress");

        t.True(PresenceSerializer.TryRead(without, a, out PresenceState got),
               "the record-free packet parses");
        t.True(!got.HasUseBarSlotIds,
               "…and decodes to 'no identity carried', which is the state a peer predating record "
               + "45 leaves — the receiver then falls back to the local resolve");
    }

    // ---------------------------------------------------------------------------------------
    //  45e. HOSTILE RECORDS. Note 3: drop, never clamp. Note 4: 0 stays reserved.
    // ---------------------------------------------------------------------------------------
    private static void HostileRecordsFailClosed(Harness t)
    {
        t.Case("45e. use-bar slot identity — a lying record drops entries, it never moves one");

        // A count that claims more entries than the record's own length can hold. The reader must
        // take the entries that FIT and stop, not read into the next record.
        byte[] lying = Tail("2D 04 FF 00 EF BE");
        t.True(PresenceSerializer.TryRead(lying, lying.Length, out PresenceState got1),
               "a record claiming 255 entries in a 4-byte payload still parses");
        t.True(got1.HasUseBarSlotIds, "…and keeps the one entry that actually fits");
        t.Equal(0xBEEF, (int)got1.UseBarSlotIds![0], "…which is the one the bytes really carry");

        // Bars 4..7 are addressable by the byte and undefined by this build. Every one must DROP.
        for (int bar = NetProtocol.UseBarsCount; bar < 8; bar++)
        {
            byte addr = NetProtocol.UseBarSlotAddr(bar, 0);
            byte[] p = Tail($"2D 04 01 {addr:X2} EF BE");
            t.True(PresenceSerializer.TryRead(p, p.Length, out PresenceState g),
                   $"a record naming undefined bar {bar} still parses");
            t.True(!g.HasUseBarSlotIds,
                   $"…and bar {bar} is DROPPED, not clamped onto bar {bar & 3} — clamping would "
                   + "move a symbol onto a bar its owner never named");
        }

        // Slots 8..31 likewise: addressable, past UseBarsMaxSlots, must drop.
        for (int slot = NetProtocol.UseBarsMaxSlots; slot < 32; slot++)
        {
            byte addr = NetProtocol.UseBarSlotAddr(0, slot);
            byte[] p = Tail($"2D 04 01 {addr:X2} EF BE");
            t.True(PresenceSerializer.TryRead(p, p.Length, out PresenceState g),
                   $"a record naming out-of-range slot {slot} still parses");
            t.True(!g.HasUseBarSlotIds,
                   $"…and slot {slot} is DROPPED rather than wrapped onto slot {slot & 7}");
        }

        // An explicit zero id is the sender's own "no identity" and must not be stored, or the
        // receiver could not tell a named slot from an unnamed one.
        byte[] zero = Tail("2D 04 01 00 00 00");
        t.True(PresenceSerializer.TryRead(zero, zero.Length, out PresenceState g0),
               "a record carrying an explicit id 0 still parses");
        t.True(!g0.HasUseBarSlotIds,
               "…and id 0 is dropped: NoIdentity is reserved, so 'named nothing' cannot be "
               + "confused with 'named something'");

        // A payload of just the count byte — the record's documented minimum — carries no entry.
        byte[] countOnly = Tail("2D 01 05");
        t.True(PresenceSerializer.TryRead(countOnly, countOnly.Length, out PresenceState g1),
               "a count-only record parses");
        t.True(!g1.HasUseBarSlotIds, "…and yields no identity at all");

        // A truncated entry (2 of its 3 bytes) is not half-read.
        byte[] cut = Tail("2D 03 01 00 EF");
        t.True(PresenceSerializer.TryRead(cut, cut.Length, out PresenceState g2),
               "a record whose single entry is one byte short parses");
        t.True(!g2.HasUseBarSlotIds,
               "…and the partial entry is dropped whole — never completed from the next record's "
               + "first byte");
    }

    // ---------------------------------------------------------------------------------------
    //  45f. THE FOLD. Note 4: NoIdentity is reserved, and the fold is a function.
    // ---------------------------------------------------------------------------------------
    private static void TheFoldReservesNoIdentity(Harness t)
    {
        t.Case("45f. use-bar slot identity — the fold never produces the reserved id, and is stable");

        // The reservation is what lets absence and presence share one array. If the fold could
        // return 0 for some real bonus, that bonus's slot would read as 'the owner named nothing'
        // and would silently lose its symbol forever — a bug that would only ever show up on one
        // particular card.
        int zeros = 0;
        for (int i = 0; i < 20000; i++)
        {
            ushort folded = UseBarSlotIdentity.Fold(
                UseBarSlotIdentity.MixInt(UseBarSlotIdentity.FoldStart, i));
            if (folded == UseBarSlotIdentity.NoIdentity)
                zeros++;
        }
        t.Equal(0, zeros, "20,000 folded ints never produce NoIdentity");

        // …including the input that folds to a raw zero before the remap, which is the only case
        // that can exercise it. Found by search rather than asserted blind.
        bool sawRemap = false;
        for (int i = 0; i < 400000 && !sawRemap; i++)
        {
            uint h = UseBarSlotIdentity.MixInt(UseBarSlotIdentity.FoldStart, i);
            if ((ushort)((h ^ (h >> 16)) & 0xFFFF) == 0)
            {
                sawRemap = true;
                t.Equal(1, (int)UseBarSlotIdentity.Fold(h),
                        $"the input that folds to a raw 0 (i={i}) is remapped to 1, not left at 0");
            }
        }
        t.True(sawRemap, "the remap branch is reachable and was exercised, not merely declared");

        // A fold is a function: the sender and the receiver compute it on different machines from
        // the same fields and must agree, so equal inputs must give equal outputs every time.
        for (int i = 0; i < 500; i++)
        {
            string name = "ABILITY_CARD_WardingStrength_" + i;
            ushort a = UseBarSlotIdentity.Fold(
                UseBarSlotIdentity.MixInt(
                    UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, name), i));
            ushort b = UseBarSlotIdentity.Fold(
                UseBarSlotIdentity.MixInt(
                    UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, name), i));
            t.Equal(a, b, $"the (name, cardId) fold is deterministic for '{name}'");
            t.True(a != UseBarSlotIdentity.NoIdentity,
                   $"…and never the reserved id for '{name}'");
        }

        // The two halves must both matter: a fold that ignored the card id would collide across
        // every copy of one ability, and one that ignored the name would collide across every
        // ability on one card. Both are real shapes in this game.
        ushort n1 = UseBarSlotIdentity.Fold(UseBarSlotIdentity.MixInt(
            UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, "ABILITY_A"), 7));
        ushort n2 = UseBarSlotIdentity.Fold(UseBarSlotIdentity.MixInt(
            UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, "ABILITY_B"), 7));
        ushort n3 = UseBarSlotIdentity.Fold(UseBarSlotIdentity.MixInt(
            UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, "ABILITY_A"), 8));
        t.True(n1 != n2, "two abilities on the same card fold apart");
        t.True(n1 != n3, "the same ability on two cards folds apart");

        // A null name is not an empty name — the sender refuses a nameless ability outright, and
        // this is the property that keeps the two from sharing an id if that ever changes.
        t.True(UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, null)
               != UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, string.Empty),
               "a null name and an empty name fold differently");
    }

    // ---------------------------------------------------------------------------------------
    //  45g. THE TAIL. Note 6: a variable-length record must not move the one behind it.
    // ---------------------------------------------------------------------------------------
    private static void TailSurvives(Harness t)
    {
        t.Case("45g. use-bar slot identity — the record behind it still parses, at every length");

        // Record 45 of every legal entry count, followed by record 3 (mod version, 'GVR'), which
        // the reader must still find on its own offset.
        for (int entries = 0; entries <= NetProtocol.UseBarSlotIdentityMaxEntries; entries++)
        {
            var body = new System.Text.StringBuilder();
            int payload = 1 + (entries * NetProtocol.UseBarSlotIdentityEntryBytes);
            body.Append($"2D {payload:X2} {entries:X2} ");
            for (int e = 0; e < entries; e++)
            {
                int bar = (e & 1) == 0 ? 0 : 3;
                int slot = e >> 1;
                byte addr = NetProtocol.UseBarSlotAddr(bar, slot);
                body.Append($"{addr:X2} {(0x21 + e):X2} 0{(e % 8) + 1} ");
            }
            // record 3: ExtIdModVersion — [u16 build LE][UTF8 text], build 471, text "GVR"
            body.Append("\n03 05 D7 01 47 56 52");

            byte[] packet = Tail(body.ToString(), records: 2);
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out PresenceState got),
                   $"{entries} entries: the packet parses");
            t.Equal(471, (int)got.ModBuild,
                    $"{entries} entries: the record BEHIND record 45 was found on its own offset — "
                    + "a length slip here would swallow it or start mid-record");
            t.Equal("GVR", got.ModVersionText,
                    $"{entries} entries: …and its payload was read whole, not from one byte in");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  45h. THE BUFFER BUDGET. PresenceSerializer.MaxSize carries a stated rule — the documented
    //       worst case plus a margin of at least one record's worth. This record moved the sum.
    // ---------------------------------------------------------------------------------------
    private static void Budget(Harness t)
    {
        t.Case("45h. use-bar slot identity, the send buffer still satisfies its own margin rule");

        // 1747 -> 1798 on 2026-09-07: [id][len] + UseBarSlotIdentityMaxRecordBytes 49. THIS LITERAL
        // IS A CONSUMER OF THE SUM IN PresenceSerializer.MaxSize's doc block and must be re-read
        // from it every time the sum moves.
        const int documentedWorstCase = 7082; // Prior6865 + shared motion77(4) + resident79(117) + face80(96).
        const int largestSingleRecord = 257;   // board tuning: 2 TLV + one 255-byte page
        t.True(PresenceSerializer.MaxSize >= documentedWorstCase + largestSingleRecord,
               $"MaxSize {PresenceSerializer.MaxSize} leaves "
               + $"{PresenceSerializer.MaxSize - documentedWorstCase} bytes over the documented "
               + $"worst case {documentedWorstCase}, which is at least one record's worth "
               + $"({largestSingleRecord})");

        t.Equal(51, 2 + NetProtocol.UseBarSlotIdentityMaxRecordBytes,
                "record 45's worst case is [id][len] + its 49-byte payload — the term the sum added");

        // AND WHY THE CAP IS 16 AND NOT 32. The addressing byte can name four bars, so "one entry
        // per addressable slot" would be 32 — a 97-byte payload, worst case 1846, margin 254. That
        // is ONE BYTE inside the rule above, and this assertion is here so that a future widening
        // trips the gate instead of the rule quietly expiring.
        const int ifItNamedEveryBar = 1 + (32 * NetProtocol.UseBarSlotIdentityEntryBytes);
        t.True(PresenceSerializer.MaxSize
               < (documentedWorstCase - NetProtocol.UseBarSlotIdentityMaxRecordBytes
                  + ifItNamedEveryBar) + 2 + largestSingleRecord,
               "a 32-entry version of this record would BREAK the margin rule at today's MaxSize — "
               + "which is why the cap is 16, and why widening it means raising MaxSize in the "
               + "same commit");

        t.Equal(16, NetProtocol.UseBarSlotIdentityMaxEntries,
                "the cap is the two carried bars at UseBarsMaxSlots each");
    }
}
