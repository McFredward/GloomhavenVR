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
/// <para>VERIFIED IN THE DECOMPILED GAME, not inferred.
/// <c>UIScenarioMultiplayerController.RefreshDamagePhase</c> (<c>:212-249</c>) branches on the
/// CARD OWNER — <c>m_ActorToShowCardsFor ?? m_ActorBeingAttacked</c> (<c>:216-218</c>) — not on the
/// attacked actor, and the test it applies depends on that actor's type: a <c>CPlayerActor</c> uses
/// its own <c>IsUnderMyControl</c> (<c>:238</c>), a <c>CHeroSummonActor</c> uses
/// <c>Summoner.IsUnderMyControl</c> (<c>:233</c>), and a <c>CEnemyActor</c> uses
/// <c>FFSNetwork.IsHost</c> (<c>:229</c>). The wording "the attacked actor's IsUnderMyControl" stood
/// in four places in this tree until ModBuild 480 and was wrong in all four; the CONCLUSION below
/// is unaffected, because whichever arm decides it, exactly one client takes <c>Show</c> and every
/// other client takes <c>ShowOtherPlayer</c> (<c>:242</c>, its only call site in the tree).</para>
///
/// <para>And the premise is STRONGER than "raises nothing".
/// <c>TakeDamagePanel.ShowOtherPlayer</c> (<c>TakeDamagePanel.cs:1102-1134</c>, whole body read)
/// calls <c>ResetToggles()</c> at <c>:1122</c>, and <c>ResetToggles</c>
/// (<c>TakeDamagePanel.cs:428-440</c>) contains <c>Singleton&lt;UIUseItemsBar&gt;.Instance.Hide()</c>
/// at <c>:434</c> and <c>Singleton&lt;UIActiveBonusBar&gt;.Instance.Hide()</c> at <c>:435</c>; it
/// then ends on <c>myWindow.Hide(instant: true)</c> at <c>:1133</c>. So the watcher's two bars are
/// not merely un-raised, they are actively CLEARED. Only the controlling client's
/// <c>TakeDamagePanel.Show</c> reaches <c>UIUseItemsBar.ShowItems</c> (<c>:249</c>) and
/// <c>UIActiveBonusBar.ShowReduceDamageActiveBonuses</c> (<c>:265</c>, <c>:269</c>). On the
/// WATCHER'S machine bars 0 and 3 are therefore never populated for that actor,
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
    // ---- the one value the record reserves ----------------------------------------------------

    /// <summary>The id that means "this slot has no transmissible identity". Never produced by
    /// <see cref="Fold"/>, which remaps a zero fold to 1, so absence and a real id can never be
    /// confused and the record needs no separate presence bit per slot.</summary>
    internal const ushort NoIdentity = 0;

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
