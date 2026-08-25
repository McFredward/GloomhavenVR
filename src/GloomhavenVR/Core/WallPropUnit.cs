using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// WHICH WALL OWNS A PROP THAT SEVERAL WALLS CLAIM — the whole arithmetic of the prop-unit
/// cohesion rule, and nothing else. Free of Unity on purpose (see the bottom of this header).
///
/// <para>THE REPORT. 2026-08-19, hardware, ModBuild 166, verbatim: <i>"Der Kopf des Skeletts wird
/// immer noch ausgeblendet (selber Effekt wie in dem Screenshot zuvor)."</i> The screenshot is
/// <c>.planning/debug/skelet.jpg</c>: a skeleton against a low wall with no head. It is the crypt
/// ossuary's <c>CR_OS_Skeleton_Statue</c>, and it is not one mesh — it is a skull, a body and a
/// broken half, three separate renderers.</para>
///
/// <para>WHAT THE LOG SAYS, verbatim from <c>.planning/debug/LogOutput.log</c>, the mounted pass's
/// NEAR-MISS census (which prints, per renderer, the ONE segment that holds it — the ownership
/// table is a dictionary, so a second line for the same name is a second renderer):</para>
/// <code>
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 2' (that wall's fade 1.00)
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 3' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 1.00)
/// 'CR_OS_Skeleton_Statue_Body'[mesh]   anchor 3.5 gap 0.00: already the wall renderer of 'Wall 1' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Body'[mesh]   anchor 3.5 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Broken'[mesh] anchor 3.6 gap 0.00: already the wall renderer of 'Wall 1' (that wall's fade 0.00)
/// </code>
/// <para>The anchors (5.1 for every skull, 3.5 for every body, 3.6 for every broken half) are
/// stable across the whole session, so these are instances of ONE asset, mounted 3.5 wu above a
/// floor plane the session logs at <c>sampY[0.05..0.05]</c> — i.e. genuinely built INTO the wall,
/// not standing on the ground. The parts land on DIFFERENT wall units, and those units make
/// independent fade decisions. The skull's wall reaches 1.00 while the body's sits at 0.00, and
/// the statue is decapitated. That is the photograph.</para>
///
/// <para>WHY THE ModBuild-157 RULE COULD NOT HELP. <c>WallSegmentFade.Standing.cs</c> protects a
/// figure/actor prop that STANDS ON THE FLOOR. Two of its three terms fail here and both failures
/// are correct: this instance carries no <c>Animator</c>/<c>ActorBehaviour</c> ancestry at all (it
/// is absent from every <c>FIGURE-GUARD</c> line in this session's log), and its foot is 3.5 wu
/// above the floor, not inside the ground band. These renderers really ARE wall geometry, and
/// exempting them would leave a solid skull hanging inside a dissolved wall — a worse artefact
/// than the current one, and explicitly refused. The unit must fade; it must fade TOGETHER.</para>
///
/// <para>NOT ONE PROP — A CLASS. <c>'SB_AncCaverns_DemonHead'</c> stands in the same census lines
/// against the same walls, and so do <c>'SB_AC_Arch_Top'</c>/<c>'SB_AC_Arch_Pillars'</c> (one
/// archway, two meshes) and <c>'CR_ST_WallShelf_Stone_Bone'</c> with its <c>_Bone</c> and
/// <c>_Skull</c> pieces. Any multi-part architectural feature whose parts land on different wall
/// units can be torn apart exactly the same way.</para>
///
/// <para>THE RULE. A prop's renderers are grouped into a UNIT, the unit gets exactly ONE owning
/// wall segment, and every renderer of the unit rides that one segment's fade. Grouping is
/// hierarchical and is decided by <c>WallSegmentFade.PropUnit.cs</c> (a name stem like
/// <c>CR_OS_Skeleton_Statue_*</c> is NOT used as evidence — see that file). OWNERSHIP is the
/// arithmetic below, and it is here rather than there because it is the part that can be wrong in
/// a way nothing in a headset can show: an owner that flips between two walls from one rescan to
/// the next produces a prop that pops in and out while neither wall changes state, which reads as
/// a flickering statue rather than as a bug in a tie-break.</para>
///
/// <para>FOUR RULES, IN ORDER, and each one exists to answer a question the one before it cannot:</para>
/// <list type="number">
/// <item>STICKY — an owner that is currently faded or fading keeps the unit. Changing owner
///   mid-fade would hand the prop to a solid wall and pop it back into a hole in the masonry.
///   This is the same "ownership sticky while faded" the mounted-dressing pass already prints in
///   its census header; there is one such concept in this module, not two.</item>
/// <item>MAJORITY — the wall holding most of the unit's renderers wins. It is the wall the asset
///   was built into: for the statue above, the unit that holds body + broken half beats the one
///   that holds only the skull.</item>
/// <item>NEAREST — a tie on count goes to the wall whose AABB centre is closest to the unit's
///   centroid in XZ. Ties are real (a two-piece archway split down the middle), and "closest"
///   is the only geometric answer that does not depend on iteration order.</item>
/// <item>KEY ORDER — a tie on both goes to the ordinal-lowest claim key, which is built from the
///   anchor NAME and its quantised XZ position (the <c>WallSegmentFade.ComputeWireKeys</c>
///   recipe). Deterministic across rescans AND across machines, so two peers never disagree
///   about which wall a shared statue belongs to. Dictionary iteration order is not.</item>
/// </list>
///
/// <para>FREE OF UNITY, and for the reason the wire-test project's header sets out: the failure
/// this file can ship is not an exception. It is a prop that changes owner every two seconds, and
/// that is observed only by eye, from inside a headset, one photograph per round — the same class
/// of unobservable that cost four builds on the haunt-figure darkening. Every input here is a
/// count, a distance and a string, so the real case from the log above can be driven through it on
/// the build machine (<c>tests/GloomhavenVR.WireTests/WallPropUnitVectors.cs</c>).</para>
///
/// <para>MULTIPLAYER: this decides nothing that goes on the wire. Every peer runs the identical
/// rules against the identical scene and reaches the identical owner — which is exactly why rule 4
/// is a stable key and not a dictionary order.</para>
///
/// <para>STEREO: no per-eye term enters here, and none can — there is no camera, no screen and no
/// eye in this file. The parked rivalry (<c>.planning/wall-fade-stereo-rivalry.md</c>) is about the
/// masonry shader's screen-radial vignette discarding one wall differently in each eye; this file
/// only changes WHICH wall a renderer belongs to, and both eyes get that same answer because it is
/// decided once per rescan on the CPU.</para>
/// </summary>
internal static class WallPropUnit
{
    /// <summary>Returned by <see cref="ChooseOwner"/> when the unit has no claimant at all.</summary>
    internal const int NoOwner = -1;

    /// <summary>Two centroid gaps closer together than this are a TIE, not a winner. A wall's
    /// AABB centre is a coarse thing (a wall run is tens of wu long), so a millimetre of
    /// difference between two of them is noise that would flip the owner between rescans as the
    /// segment's renderer list changes by one piece — precisely the pop rule 1 exists to
    /// prevent. 0.05 wu ≈ 3 real mm at this project's world scale.</summary>
    internal const float CentroidTieWU = 0.05f;

    /// <summary>One wall segment's claim on one prop unit.</summary>
    internal readonly struct Claim
    {
        /// <summary>Deterministic cross-machine identity of the claiming segment: its anchor's
        /// name and quantised XZ, the same recipe <c>ComputeWireKeys</c> hashes. The tie-break of
        /// last resort compares these ORDINALLY, so it must not contain anything process-local.</summary>
        internal readonly string Key;

        /// <summary>How many of the unit's renderers this segment currently holds.</summary>
        internal readonly int RendererCount;

        /// <summary>XZ distance from this segment's AABB centre to the unit's centroid (wu).</summary>
        internal readonly float CentroidGapXZ;

        /// <summary>True when this segment is faded or mid-fade right now — the condition under
        /// which rule 1 refuses to move the unit.</summary>
        internal readonly bool Faded;

        internal Claim(string key, int rendererCount, float centroidGapXZ, bool faded)
        {
            Key = key;
            RendererCount = rendererCount;
            CentroidGapXZ = centroidGapXZ;
            Faded = faded;
        }
    }

    /// <summary>
    /// Pick the ONE segment that owns this unit. Returns an index into <paramref name="claims"/>,
    /// or <see cref="NoOwner"/> when the list is empty, and reports WHICH of the four rules
    /// decided it in <paramref name="rule"/> — the census prints that verbatim, because "Wall 6
    /// won" without "and by what" is exactly the line ModBuild 164 shipped that could only ever
    /// print its own initialiser.
    /// </summary>
    /// <param name="claims">Every segment holding at least one of the unit's renderers.</param>
    /// <param name="stickyOwnerKey">The key this unit's owner had at the END of the previous
    /// rescan, or null/empty when the unit is new. Only honoured while that owner is still a
    /// claimant AND still faded (rule 1).</param>
    /// <param name="rule">Human-readable reason the winner won.</param>
    /// <param name="describe">PERF E (ModBuild 279) — build <paramref name="rule"/> at all.
    ///
    /// <para>The ONLY consumer of that string is <c>NotePropUnitCensus</c>, whose very first
    /// statement is <c>if (_propUnitCensus.Count &gt;= PropUnitCensusCap) return;</c>. Past that
    /// cap the sentence was formatted — three of its five forms are interpolations, and on
    /// net472 each is a <c>string.Format(string, object[])</c> with an array and a box per value
    /// — and then dropped. This method runs once per prop unit per commit and the ModBuild-277
    /// scenario carries 3,105 unit roots.</para>
    ///
    /// <para>IT DECIDES NOTHING. The returned index is computed by the identical four rules in
    /// the identical order on both paths; only the sentence differs, and when it is not built
    /// the caller has already established that nothing will read it. Default true, so every
    /// existing caller — including all fourteen wire vectors, which assert on the sentence — is
    /// unchanged.</para></param>
    internal static int ChooseOwner(IReadOnlyList<Claim> claims, string? stickyOwnerKey,
                                    out string rule, bool describe = true)
    {
        if (claims == null || claims.Count == 0)
        {
            rule = "no claimant";
            return NoOwner;
        }

        // RULE 1 — STICKY WHILE FADED. Deliberately first: a unit whose owner is mid-dissolve must
        // not be handed to a solid wall, whatever the counts say. The prop would snap back to
        // opaque inside a hole in the masonry, which is the same blink the mounted pass's sticky
        // ownership was introduced to stop.
        if (!string.IsNullOrEmpty(stickyOwnerKey))
        {
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].Faded && string.Equals(claims[i].Key, stickyOwnerKey,
                                                     System.StringComparison.Ordinal))
                {
                    rule = "sticky (owner mid-fade)";
                    return i;
                }
            }
        }

        // RULE 2 — MAJORITY. The wall that holds most of the unit is the wall the asset was built
        // into. Ties are collected rather than resolved here so rule 3 sees all of them.
        int bestCount = -1;
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].RendererCount > bestCount)
                bestCount = claims[i].RendererCount;
        }
        int tiedOnCount = 0;
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].RendererCount == bestCount)
                tiedOnCount++;
        }
        if (tiedOnCount == 1)
        {
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].RendererCount == bestCount)
                {
                    rule = describe ? $"majority {bestCount}/{TotalHeld(claims)}" : NoDescription;
                    return i;
                }
            }
        }

        // RULE 3 — NEAREST CENTROID, among the count-tied only. Compared with a tolerance
        // (see CentroidTieWU): a hair's difference between two wall-run centres is noise.
        float bestGap = float.PositiveInfinity;
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].RendererCount == bestCount && claims[i].CentroidGapXZ < bestGap)
                bestGap = claims[i].CentroidGapXZ;
        }
        int tiedOnGap = 0;
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].RendererCount == bestCount
                && claims[i].CentroidGapXZ <= bestGap + CentroidTieWU)
                tiedOnGap++;
        }
        if (tiedOnGap == 1)
        {
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].RendererCount == bestCount
                    && claims[i].CentroidGapXZ <= bestGap + CentroidTieWU)
                {
                    rule = describe
                        ? $"nearest centroid {bestGap:0.00} wu (tied {bestCount}/"
                          + $"{TotalHeld(claims)})"
                        : NoDescription;
                    return i;
                }
            }
        }

        // RULE 4 — ORDINAL-LOWEST KEY. The only rule that is guaranteed to terminate, and the
        // only one that is identical on every machine in the session.
        int winner = NoOwner;
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].RendererCount != bestCount
                || claims[i].CentroidGapXZ > bestGap + CentroidTieWU)
            {
                continue;
            }
            if (winner == NoOwner
                || string.CompareOrdinal(claims[i].Key, claims[winner].Key) < 0)
            {
                winner = i;
            }
        }
        if (winner == NoOwner)
            winner = 0; // unreachable by construction (the claim that SET bestGap always passes);
                        // pinned anyway, because "unreachable" is what an index crash is made of
        rule = describe
            ? $"key order '{claims[winner].Key}' (tied {bestCount}/{TotalHeld(claims)} at "
              + $"{bestGap:0.00} wu)"
            : NoDescription;
        return winner;
    }

    /// <summary>What <see cref="ChooseOwner"/> reports when its caller has said it will not print
    /// the sentence (PERF E). It can never reach a log — the census that would print it returns
    /// on its cap before reading — and it says so rather than being empty, so a copy of it
    /// appearing in a hardware log is immediately legible as an instrument bug rather than as a
    /// rule nobody named.</summary>
    internal const string NoDescription =
        "<no rule sentence was built: the prop-unit census was already at its cap>";

    /// <summary>How many of the unit's renderers are held by SOME claimant — the denominator the
    /// census prints next to the winning count, so "majority 2/3" reads as "two of the three
    /// pieces that had an owner", not as a fraction of an unstated whole.</summary>
    internal static int TotalHeld(IReadOnlyList<Claim> claims)
    {
        int n = 0;
        for (int i = 0; i < claims.Count; i++)
            n += claims[i].RendererCount;
        return n;
    }
}
