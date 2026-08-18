using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE TWO ROUND-CARD FACTS THE 2026-08-15 HARDWARE SESSION PROVED WERE MISSING:
/// record 14's STANDARD-ACTION qualifier byte (item 6) and record 18's SLOT ORDER (item 8).
///
/// <para><b>WHY THEY ARE ON THIS HARNESS.</b> Both defects are invisible from inside one headset by
/// construction. Item 6's picture is correct on the owner's screen and wrong on everyone else's;
/// item 8's is correct on both screens SOMETIMES — the two derivations coincide whenever the printed
/// initiative sort happens to put the initiative card first, which is why the report reads "war in
/// EINEM Test verdreht". Neither can be reproduced by a developer alone, and neither leaves a
/// failing frame anywhere.</para>
///
/// <para><b>BYTE-EXACT FIRST, PROPERTY SECOND.</b> The golden vectors here follow this project's
/// rule (see the .csproj header): a refactor that moves a block in <c>Write</c> AND in
/// <c>TryRead</c> together keeps every round-trip passing while corrupting every peer, so the
/// expected BYTES are what is asserted. The permutation property below is added ON TOP because item
/// 8 asked for exactly that guarantee — that the ORDER survives encode→decode for every arrangement
/// — and because its whole failure mode was an order that was never encoded at all.</para>
///
/// <para><b>THE ADDITIVE-LENGTH CONTRACT is the sharpest thing here.</b> Record 14 grew from 2 bytes
/// to 3, and the ONE property that makes that safe is that a sender with nothing to qualify still
/// emits exactly the 2-byte record every earlier build emitted. That is asserted as bytes, in both
/// directions, because it is the property a future "always write the whole record, it's simpler"
/// tidy-up would silently destroy.</para>
/// </summary>
internal static class BoardSlotVectors
{
    internal static void Run(Harness t)
    {
        var ext = new byte[512];

        // -- A. RECORD 14 BYTE 2 — the STANDARD-ACTION qualifier (item 6) -------------------
        // "Wenn jemand die standart Aktion ausgewählt hat oder drüber hovered wird trotzdem der
        // große untere bzw obere Bereich der Karte bei den remote boards angezeigt/gehighlighted."
        // The chip and the half are two rectangles on one card; the wire could only name the half.
        t.Case("A1. record 14 stays TWO bytes when nothing is a standard action");
        int m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 1, HalfHoverTop = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            0E 02 05 00      // id 14, LEN 2: hover slot 1 | top bit; no selection, NO byte 2
            "), ext, m,
            "a hover on the big half is byte-identical to every build before the qualifier existed");

        t.Case("A2. the qualifier byte appears — and ONLY then");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 1, HalfHoverTop = true,
            HalfHoverDefault = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            0E 03 05 00 01   // id 14, LEN 3: same hover, + byte 2 bit 0 = it is the CHIP
            "), ext, m, "the beam on slot 2's TOP standard-action chip costs exactly one more byte");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState chip), "and it parses");
        t.True(chip.HalfHoverActive && chip.HalfHoverSlot == 1 && chip.HalfHoverTop,
               "the hover slot and half are untouched by the new byte");
        t.True(chip.HalfHoverDefault, "and the region is the standard-action field");

        t.Case("A3. both selections qualified — the logged repro (owner clicked the MOVE chip)");
        // remote2/LogOutput.log:47996 'uGUI click: Default action button' -> :48001 "slot 1 =
        // BOTTOM". Before byte 2 that packet was indistinguishable from a click on the whole
        // bottom half, which is exactly what LogOutput.log:50531 drew on the observer.
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true,
            HalfSelect0 = NetProtocol.HalfSelectBottom, HalfSelect0Default = true,
            HalfSelect1 = NetProtocol.HalfSelectTop, HalfSelect1Default = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            0E 03 03 06 06   // id 14, len 3: no-hover sentinel; sel0 BOTTOM|sel1 TOP<<2; bits 1|2
            "), ext, m, "a selection-only record carries its qualifier in the same appended byte");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState sels), "and it parses");
        t.True(sels.HalfSelect0 == NetProtocol.HalfSelectBottom && sels.HalfSelect0Default,
               "slot 1's committed region is its BOTTOM standard-action field");
        t.True(sels.HalfSelect1 == NetProtocol.HalfSelectTop && sels.HalfSelect1Default,
               "slot 2's committed region is its TOP standard-action field");
        t.True(!sels.HalfHoverDefault, "and no hover was qualified, because there is no hover");

        t.Case("A4. an OLD 2-byte record decodes to 'the big half', not to nothing");
        // The compatibility direction that matters: a peer that predates the byte must keep
        // producing the picture it always produced, never a blank one.
        byte[] old14 = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 02 05 01");
        t.True(PresenceSerializer.TryRead(old14, old14.Length, out PresenceState legacy),
               "a 2-byte record still parses");
        t.True(legacy.HasHalfHover && legacy.HalfHoverActive && legacy.HalfHoverTop,
               "the hover it does carry survives");
        t.True(!legacy.HalfHoverDefault && !legacy.HalfSelect0Default && !legacy.HalfSelect1Default,
               "and every region defaults to the BIG half — the only thing those builds could mean");

        t.Case("A5. the qualifier cannot invent a state it does not qualify");
        // Bits set for a hover that is not there and for a slot with no selection. The reader
        // gates each bit on the state it belongs to, so a corrupt or over-eager byte lights
        // nothing rather than a highlight on an untouched card.
        byte[] loud = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 03 03 01 07");
        t.True(PresenceSerializer.TryRead(loud, loud.Length, out PresenceState gated),
               "a record whose byte 2 claims more than bytes 0/1 support still parses");
        t.True(!gated.HalfHoverDefault, "the hover qualifier is dropped — the sentinel says no hover");
        t.True(gated.HalfSelect0 == NetProtocol.HalfSelectTop && gated.HalfSelect0Default,
               "slot 1 really is selected, so its qualifier stands");
        t.True(!gated.HalfSelect1Default, "slot 2 has no selection, so its qualifier is dropped");

        t.Case("A6. reserved bits 3..7 of byte 2 light nothing");
        byte[] future = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 0E 03 05 00 F8");
        t.True(PresenceSerializer.TryRead(future, future.Length, out PresenceState fut),
               "a future sender's reserved bits still parse");
        t.True(fut.HalfHoverActive && !fut.HalfHoverDefault,
               "and mean nothing here — the reader masks with HalfDefaultByteDefinedMask");
        t.Equal((byte)0x07, NetProtocol.HalfDefaultByteDefinedMask,
                "the defined mask is hover + one bit per board slot");

        // -- B. RECORD 18 — the ROUND-CARD SLOT ORDER (item 8) ------------------------------
        // "Die Position der Karten (linke Karte/rechte Karte) war in einem Test verdreht … Die
        // Reihenfolge MUSS zwingend identisch sein wie es der jenige Spieler auch sieht."
        t.Case("B1. the unswapped order is STATED, not assumed");
        // It would have been cheaper to write the record only when swapped. That is exactly the
        // trap the pile-counts record documents: "stated and equal to the default" has to be
        // distinguishable from "nobody said", because only the first lets a receiver stop guessing.
        m = PresenceSerializer.Write(new PresenceState { HasSlotOrder = true }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            12 01 01         // id 18, len 1: VALID, not swapped
            "), ext, m, "the record is three bytes and says 'left recess = the initiative card'");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ord0), "and it parses");
        t.True(ord0.HasSlotOrder && !ord0.SlotOrderSwapped, "as a stated, unswapped order");

        t.Case("B2. the swapped order");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSlotOrder = true, SlotOrderSwapped = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            01
            12 01 03         // id 18, len 1: VALID | SWAPPED
            "), ext, m, "one bit says the LEFT recess holds the non-initiative card");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState ord1), "and it parses");
        t.True(ord1.HasSlotOrder && ord1.SlotOrderSwapped, "as a stated, swapped order");

        t.Case("B3. no order ⇒ no record ⇒ byte-identical to a pre-record-18 sender");
        m = PresenceSerializer.Write(new PresenceState { HandCardCount = 5 }, ext);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 05"), ext, m,
               "a sender that cannot answer says nothing at all, so a peer keeps its own derivation");

        t.Case("B4. ENCODE(order) → DECODE(order) is the identity for EVERY permutation");
        // The property item 8 asked for, stated as a property: whatever arrangement the owner
        // physically has, the receiver reads back THAT arrangement — never one it re-derived.
        // Two round cards is two permutations, and both are exercised as whole packets rather
        // than as a bit test, so a writer that drops the record fails here too.
        for (int perm = 0; perm < 2; perm++)
        {
            bool swapped = perm == 1;
            int n = PresenceSerializer.Write(new PresenceState
            {
                HasSlotOrder = true, SlotOrderSwapped = swapped,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, n, out PresenceState back),
                   $"permutation {perm} parses");
            t.True(back.HasSlotOrder, $"permutation {perm} is STATED on the far side");
            t.Equal(swapped, back.SlotOrderSwapped,
                    $"permutation {perm} arrives as the arrangement it was sent as");
        }

        t.Case("B5. a byte without the VALID bit states nothing");
        // Never trust the wire: an order asserted by a corrupt tail would seat a peer's cards the
        // wrong way round with total confidence, which is strictly worse than the old guess.
        byte[] invalid = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 12 01 02");
        t.True(PresenceSerializer.TryRead(invalid, invalid.Length, out PresenceState noVal),
               "a swapped-but-not-valid byte still parses");
        t.True(!noVal.HasSlotOrder,
               "and leaves the receiver on its own derivation rather than on a second guess");

        t.Case("B6. reserved bits 2..7 light nothing, and a truncated record delivers nothing");
        byte[] loudOrder = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 12 01 FF");
        t.True(PresenceSerializer.TryRead(loudOrder, loudOrder.Length, out PresenceState masked),
               "a future sender's reserved bits still parse");
        t.True(masked.HasSlotOrder && masked.SlotOrderSwapped,
               "the two defined bits are read and the rest are masked away");
        byte[] cut = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 12 01");
        t.True(PresenceSerializer.TryRead(cut, cut.Length, out PresenceState cutOrder),
               "a record that claims a payload byte it does not deliver still parses the packet");
        t.True(!cutOrder.HasSlotOrder, "and the incomplete record is simply not delivered");

        t.Case("B7. both new facts ride one packet, in id order, without disturbing each other");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasHalfHover = true, HalfHoverActive = true, HalfHoverSlot = 0, HalfHoverTop = false,
            HalfHoverDefault = true,
            HalfSelect1 = NetProtocol.HalfSelectTop,
            HasSlotOrder = true, SlotOrderSwapped = true,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47 03 01 80 00
            80 00
            02
            0E 03 00 04 01   // id 14, len 3: hover slot 0 BOTTOM chip; sel slot 1 TOP; qualifier
            12 01 03         // id 18, len 1: VALID | SWAPPED
            "), ext, m, "record 14 (grown) then record 18 (new), appended in ascending id order");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState both), "and both parse");
        t.True(both.HalfHoverActive && !both.HalfHoverTop && both.HalfHoverDefault,
               "the hover names slot 1's BOTTOM standard-action field");
        t.True(both.HalfSelect1 == NetProtocol.HalfSelectTop && !both.HalfSelect1Default,
               "the committed region is slot 2's TOP HALF — the qualifier did not leak across");
        t.True(both.HasSlotOrder && both.SlotOrderSwapped,
               "and the order is the owner's, stated in the same packet");
    }
}
