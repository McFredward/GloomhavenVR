namespace GloomhavenVR.Net;

/// <summary>
/// WIRE RECORD 45 — WHICH bonus or item a peer's use-bar slot is showing, as one 16-bit id.
/// The record's SHAPE and its FOLD, with no game types in sight; the two directions that read and
/// write it live in <see cref="UseBarSlotSymbol"/>.
///
/// ─── WHY THE FILE IS SPLIT IN TWO ──────────────────────────────────────────────────────────────
/// This half is compiled into <c>tests/GloomhavenVR.WireTests</c>, which references
/// <c>UnityEngine.CoreModule</c> and NOTHING else — no <c>ScenarioRuleLibrary</c>, no
/// <c>Assembly-CSharp</c>. <c>PresenceState</c> is in that compilation and needs this record's
/// constants and its addressing codec, so anything that names a <c>CActiveBonus</c>, a
/// <c>CItem</c> or a <c>Sprite</c> has to be on the other side of the line. The split is therefore
/// the test project's reference list talking, not taste — and it is a useful line anyway: the
/// numbers below are the CONTRACT, and the file next door is the policy that fills them in.
///
/// ─── WHY THE RECORD EXISTS AT ALL ──────────────────────────────────────────────────────────────
/// <see cref="RemoteUseBarSymbols"/> resolves the same symbols with ZERO wire bytes, off this
/// client's own copy of the owner's bar, and for the bars the game raises on every client that is
/// still the right answer and still runs first. This record exists because for ONE prompt that
/// premise is false by construction, and the user reported exactly that prompt:
///
/// <para>User report 2026-09-07 item 8, verbatim: <i>"Die entsprechenden Symbole sehe ich auch
/// nicht. … Es ist von äußerster Wichtigkeit dass hier die 1:1 Regel eingehalten wird und jeder
/// Spieler genau das selbe sieht wie der lokale Spieler bei sich bei diesen
/// Entscheidungssymbolen."</i></para>
///
/// <para>VERIFIED IN THE DECOMPILED GAME, not inferred: <c>UIScenarioMultiplayerController</c>
/// lines 238-246 branch on the attacked actor's <c>IsUnderMyControl</c>. A client that does NOT
/// control that actor is sent to <c>TakeDamagePanel.ShowOtherPlayer</c>
/// (<c>TakeDamagePanel.cs:1102-1133</c>), whose whole body raises NEITHER
/// <c>UIUseItemsBar.ShowItems</c> NOR <c>UIActiveBonusBar.ShowReduceDamageActiveBonuses</c> and
/// ends on <c>myWindow.Hide(instant: true)</c>. Only the controlling client's
/// <c>TakeDamagePanel.Show</c> (<c>TakeDamagePanel.cs:215-270</c>) reaches those two calls. So on
/// the WATCHER'S machine bars 0 and 3 are never populated for that actor,
/// <c>RemoteUseBarSymbols.BarBelongsTo</c> is false by construction, and no local resolve can ever
/// succeed. The identity has to come from the owner.</para>
///
/// ─── NO ART RIDES THE WIRE ─────────────────────────────────────────────────────────────────────
/// What travels is a 16-bit NUMBER. The receiver looks that number up in ITS OWN replicated model
/// and only then asks the game for the sprite, exactly as <see cref="RemoteItemCardSource"/> does
/// for a peer's item faces: structure travels, art is resolved locally. Nothing here can name a
/// card to a client that could not already enumerate it.
///
/// ─── THE PAYLOAD ───────────────────────────────────────────────────────────────────────────────
/// <code>
/// [entries]                                     how many slots this record names, &lt;= 16
/// entries × [bar:3 | slot:5][idLo][idHi]        which slot, and its 16-bit id
/// </code>
/// Interleaved as whole 3-byte entries rather than as an address array followed by an id array.
/// That is a deliberate departure from the shape first sketched for this record, and the reason is
/// truncation: with the two arrays split, a record cut short mid-ids leaves entries that have an
/// address and no id, and the reader has to carry a second length to know which. Interleaved, a
/// truncated record simply carries fewer whole entries and the bound is one division.
///
/// ─── WHY A HASH IS SAFE HERE, AND AN INDEX WOULD NOT BE ────────────────────────────────────────
/// A deterministic INDEX into <c>CharacterClassManager.FindAllActiveBonuses(actor)</c> would be one
/// byte and would be wrong SILENTLY: if the two clients' lists differ by a single entry for one
/// beat, index 2 resolves to a DIFFERENT bonus and the peer is shown somebody else's decision with
/// no way to notice. A folded id is self-checking instead — the receiver recomputes the same fold
/// over its own candidates and requires EXACTLY ONE match. A stale list yields zero matches, a
/// collision yields two, and both REFUSE. The failure direction is the one the standing gate
/// demands: an honest blank, never a plausible lie.
///
/// <para><b>WHY 16 BITS AND NOT 8.</b> Because a collision costs the feature, the width is chosen
/// against the candidate-set size rather than against the byte budget. A character's active-bonus
/// set plus their inventory runs to a few tens of entries; over 20 candidates an 8-bit fold
/// collides with probability ≈ 1 − e^(−20·19/(2·256)) ≈ 52 %, so half of all prompts would refuse
/// and the record would look broken. At 16 bits the same figure is ≈ 0.3 %.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR IDENTITY — 16-bit id, sparse, default-off. The ART stays
/// DELIBERATELY-NOT on the wire and is resolved locally. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal static class UseBarSlotIdentity
{
    // ─── WIRE CONSTANTS PENDING RELOCATION INTO NetProtocol.cs ────────────────────────────────
    //
    // NetProtocol.cs is the integrator's file and a lane may not edit it, but this lane's sender,
    // receiver and wire tests all have to COMPILE against the record's constants before the gate
    // suite can be run at all. So they are declared here, and the lane's report hands the
    // integrator their exact text to MOVE into NetProtocol.cs beside ExtIdFanArcOrder. Nothing
    // reads a second copy of any of these numbers, so the move is a cut, a paste and a
    // `UseBarSlotIdentity.` → `NetProtocol.` rename at the call sites.

    /// <summary>Extension record id: WHICH bonus or item each visible use-bar slot is showing.
    /// Allocated by the integrator for ModBuild 479.</summary>
    internal const byte ExtIdUseBarSlotIdentity = 45;

    /// <summary>Smallest payload the record can have: the entry-count byte alone. A record shorter
    /// than this is malformed and is stepped over by the tail loop's own length skip.</summary>
    internal const int UseBarSlotIdentityMinRecordBytes = 1;

    /// <summary>Bytes per carried entry: one addressing byte then the id, little-endian.</summary>
    internal const int UseBarSlotIdentityEntryBytes = 3;

    /// <summary>
    /// Most entries the record can carry: <c>2 × NetProtocol.UseBarsMaxSlots</c> = 16 — the two
    /// bars that carry an identity (active bonus and items), at their existing per-bar slot cap.
    /// Clamped on BOTH ends, so a lying count can neither allocate nor overrun.
    ///
    /// <para>NOT 32, AND THE REASON IS THE BUDGET RULE RATHER THAN TIDINESS. The addressing byte
    /// has three bits of bar and can name all four, and 32 entries is what "one per addressable
    /// slot" would give; but 32 entries is a 97-byte payload, which takes the documented worst case
    /// to 1846 and leaves a 254-byte margin under <c>PresenceSerializer.MaxSize</c> 2100 — one byte
    /// INSIDE the standing rule that the margin stay at least as large as the biggest single record
    /// (257, board tuning). 16 is also the honest bound: bars 1 and 2 produce no id at all today
    /// (the abilities bar resolves locally, an augment slot has no icon), so the sender cannot
    /// currently emit a seventeenth entry. A future build that gives those bars identities must
    /// raise this cap AND <c>MaxSize</c> in the same commit, per that rule.</para>
    /// </summary>
    internal const int UseBarSlotIdentityMaxEntries = 2 * 8;

    /// <summary>Largest payload the record can occupy: the count byte plus every entry
    /// (1 + 16 × 3 = 49). With the 2-byte TLV header that is 51 bytes on the wire.</summary>
    internal const int UseBarSlotIdentityMaxRecordBytes =
        1 + (UseBarSlotIdentityMaxEntries * UseBarSlotIdentityEntryBytes);

    /// <summary>
    /// First ModBuild that can SEND record 45. A peer below this — a ModBuild-478 co-player, a FLAT
    /// player, an unmodded client — sends no record 45 at all, and its mirrored bars must keep
    /// drawing exactly the plates they drew before this build. The receiver never REQUIRES the
    /// record; this constant exists so the log can tell "that peer CANNOT name its slots" apart
    /// from "that peer named none", which is the distinction the ARC ORDER NOT APPLIED line was
    /// corrected to make on 2026-09-07 after the first grep of a host log accused a peer four
    /// builds PAST the record of predating it.
    /// </summary>
    internal const ushort UseBarSlotIdentityMinPeerBuild = 479;

    /// <summary>The id that means "this slot has no transmissible identity". Never produced by
    /// <see cref="Fold"/>, which remaps a zero fold to 1, so absence and a real id can never be
    /// confused and the record needs no separate presence bit per slot.</summary>
    internal const ushort NoIdentity = 0;

    // ---- the addressing codec ----------------------------------------------------------------

    /// <summary>Pack a (bar, slot) pair into the record's addressing byte: bar in the HIGH 3 bits,
    /// slot in the LOW 5. One expression, called by the writer and by the reader, so the two cannot
    /// disagree about which end the bar lives on — a swap there would move every symbol onto the
    /// wrong bar while still parsing perfectly.</summary>
    internal static byte UseBarSlotAddr(int bar, int slot) =>
        (byte)(((bar & 0x07) << 5) | (slot & 0x1F));

    /// <summary>The bar index inside an addressing byte (high 3 bits).</summary>
    internal static int UseBarSlotAddrBar(byte addr) => (addr >> 5) & 0x07;

    /// <summary>The slot index inside an addressing byte (low 5 bits).</summary>
    internal static int UseBarSlotAddrSlot(byte addr) => addr & 0x1F;

    // ---- the fold (ONE implementation, called by BOTH directions) ---------------------------
    //
    // Sender and receiver do not merely "use the same rule" by agreement — both call these exact
    // methods through UseBarSlotSymbol, so the two sides cannot drift apart in a future edit
    // without the compiler moving both.

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

    /// <summary>The FNV-1a starting value, for a caller that folds several fields in turn.</summary>
    internal static uint FoldStart => FnvOffset;

    private static uint Step(uint h, byte b) => (h ^ b) * FnvPrime;

    /// <summary>Fold a 32-bit value in, little-endian.</summary>
    internal static uint MixInt(uint h, int v)
    {
        h = Step(h, (byte)v);
        h = Step(h, (byte)(v >> 8));
        h = Step(h, (byte)(v >> 16));
        return Step(h, (byte)(v >> 24));
    }

    /// <summary>Fold an unsigned 32-bit value in, by the same bytes as <see cref="MixInt"/>.
    /// </summary>
    internal static uint MixUInt(uint h, uint v) => MixInt(h, unchecked((int)v));

    /// <summary>Fold a string in by its UTF-16 code units — no <c>Encoding</c> call and no
    /// substring, so the sampler stays allocation-free on its cadence. A null string folds a
    /// distinct sentinel byte rather than nothing, so "no name" and "empty name" differ.</summary>
    internal static uint MixString(uint h, string? s)
    {
        if (s == null)
            return Step(h, 0xFF);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            h = Step(h, (byte)c);
            h = Step(h, (byte)(c >> 8));
        }
        return h;
    }

    /// <summary>Fold 32 bits to 16, remapping 0 to 1 so <see cref="NoIdentity"/> stays reserved.
    /// </summary>
    internal static ushort Fold(uint h)
    {
        var v = (ushort)((h ^ (h >> 16)) & 0xFFFF);
        return v == 0 ? (ushort)1 : v;
    }
}
