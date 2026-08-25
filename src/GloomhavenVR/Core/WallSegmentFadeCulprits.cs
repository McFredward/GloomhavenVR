using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// WHICH RENDERERS MOVED THE SCENE SIGNATURE — the instrument the ModBuild 277 log asks for by
/// name, extracted from the driver so it can be driven without a headset.
///
/// <para><b>THE QUESTION.</b> PERF S5 skips a rescan commit when a 64-bit signature over every
/// input the commit reads has not moved. In the ModBuild 277 hardware log it skipped 80 of 113
/// judged cycles — and of the 33 that committed, <b>28 committed on one single term</b>:
/// <c>0 no table yet, 1 room reveal, 0 asked for, 0 dissolve material swap, 0 board moved,
/// 28 scene signature moved, 2 wall signature moved, 0 STALENESS CEILING, 0 segment AABB
/// drift</c>. That refusal's own text ends with the next round's question: <i>"IF THIS IS THE
/// COUNT THAT DOMINATES, the scene is churning under the snapshot and the next round's question
/// is WHICH renderers, not whether to skip."</i> It dominates. This file answers WHICH.</para>
///
/// <para><b>WHY THE ANSWER MATTERS RATHER THAN BEING TRIVIA.</b> Each of those 28 commits is a
/// measured ~85-134 ms atomic frame (ModBuild 277: 33 commits, mean worst-commit 85.6 ms, max
/// 134.0 ms). They are the user's remaining Ruckler in as many words: <i>"Die kurzen 'Hänger'
/// sind noch da - und es liegt definitiv an der Wandausblendung. Ich habe sie im Test testweise
/// deaktiviert und die Hänger waren weg."</i> If the renderers moving the hash are things that
/// cannot change a fade verdict — a torch flame toggling, a particle system, a figure's
/// animation swapping a renderer in and out — then the 90 ms is being paid for nothing and a
/// narrower signature would take nearly all 33 of those commits away. If instead they are wall
/// masonry appearing and disappearing, then the scene really is churning, the skip is already
/// correct, and the next round must attack the commit's cost rather than its frequency. Those
/// are opposite conclusions and nothing shipped before this build can tell them apart.</para>
///
/// <para><b>WHY IT IS A PURE FILE.</b> Two reasons, and the second is the load-bearing one.
/// First, this project's ledger is emphatic that <i>a new instrument's first output is a
/// hypothesis</i> — three separate rounds have been lost to an instrument that was believed on
/// sight. An instrument that can be run against a KNOWN input, in CI, is one that has been
/// falsified before it is trusted; see <c>WallSignatureCulpritVectors</c> in the wire suite,
/// which drives exactly the NULL case (nothing changed ⇒ the census must find nothing and SAY
/// SO) and a known-positive control. Second, this project's ledger is equally emphatic that
/// <i>a truncated list is not absence</i> and <i>a summary stat is not the field</i>: an
/// ellipsis-capped list of prefab names once produced the conclusion "X never appears" from a
/// list that had simply run out of room. So the report below carries group COUNTS and an
/// explicit elision count, never a bare top-N, and the formatting rule that guarantees it is
/// testable here rather than only observable in a log.</para>
///
/// <para><b>WHAT IT DOES NOT DO.</b> It never proposes narrowing the signature and it never
/// narrows one. It reports. The decision to hash fewer renderers is a correctness change to the
/// skip invariant — a wrong skip is a segment table that never gets rebuilt — and it is made
/// against this data by a human, in a later build, with the numbers in hand.</para>
/// </summary>
internal static class WallSegmentFadeCulprits
{
    // The seven verdict bits plus liveness, exactly as ClassifySlice folds them into the scene
    // half of the signature. Named here rather than inlined so the report can say WHICH bit
    // moved — "a renderer changed" and "a renderer stopped being wall-fade-capable" are
    // different findings and only the second one indicts the signature's width.
    internal const int BitMesh = 1;
    internal const int BitParticles = 2;
    internal const int BitMountable = 4;
    internal const int BitMod = 8;
    internal const int BitWallFade = 16;
    internal const int BitFoliage = 32;
    internal const int BitWater = 64;
    internal const int BitActive = 128;

    /// <summary>ModBuild 279 (Option A) — the ROUND-7 EXEMPTION verdict:
    /// <c>IsFigureOrActorRenderer &amp;&amp; !IsWallGeneratedDressing</c>. It is NOT one of the
    /// eight the shipped signature folds; it is carried by this census because the narrowing's
    /// falsifier has to be able to say "the renderers the narrowing stopped listening to were
    /// these, and they were figures" — or, much more usefully, that they were not.</summary>
    internal const int BitFigure = 256;

    /// <summary>How many bits the census carries. Named so the two report loops cannot drift
    /// from the array beside them, which is how a ninth bit would otherwise have been added and
    /// silently never printed.</summary>
    internal const int BitCount = 9;

    /// <summary>Human names for the nine bits, index = bit position.</summary>
    internal static readonly string[] BitNames =
    {
        "is a MeshRenderer", "is a particle system", "is mountable", "is a mod object",
        "carries a wall-fade shader", "carries a foliage shader", "is a water surface",
        "activeInHierarchy",
        "is a round-7 FIGURE the narrowing exempts",
    };

    /// <summary>
    /// ModBuild 279 (Option A) — THE NARROWED SIGNATURE'S BIT SELECTION, and the only copy of it.
    ///
    /// <para>The scene half of the skip signature folds identity plus these bits, once per
    /// renderer. The NARROWED half folds identity plus THIS function of them: for a renderer the
    /// round-7 ruling puts beyond every adoption lane's reach, the <c>activeInHierarchy</c> bit —
    /// which the game flips constantly and which the ModBuild-277 log's decoded refusals show as
    /// the sole mover in 5 of 17 sampled refusals — is replaced by a FIGURE bit.</para>
    ///
    /// <para><b>WHY IT LIVES IN THE UNITY-FREE FILE.</b> Because it is arithmetic that decides
    /// whether a 95 ms rebuild happens, and this project's ledger says a new instrument's first
    /// output is a hypothesis. Here it can be driven exhaustively over all 256 bit patterns on
    /// both arms, in CI, with no headset — see <c>WallSignatureCulpritVectors</c>, which pins the
    /// four properties the narrowing's safety argument actually rests on:</para>
    /// <list type="number">
    /// <item>a NON-figure is folded exactly as before, bit for bit, so the narrowing can only
    ///   ever affect the class it names;</item>
    /// <item>a FIGURE's value does not depend on <c>activeInHierarchy</c> — that is the whole
    ///   saving, and it is a property rather than an intention;</item>
    /// <item>a figure/non-figure CROSSING always changes the value, whatever the other bits are,
    ///   so a renderer reparented into or out of the round-7 class still commits;</item>
    /// <item>no figure's value can collide with any non-figure's, so the two classes cannot
    ///   cancel inside the commutative accumulators.</item>
    /// </list>
    /// </summary>
    internal static int NarrowedBits(int bits, bool figure) =>
        figure ? (bits & ~BitActive) | BitFigure : bits;

    /// <summary>One live snapshot entry, as the census sees it. Deliberately not a Unity type:
    /// the id is <c>GetInstanceID</c>, the name is the one <c>ClassifyMaterialsAndName</c>
    /// already allocated (so this instrument adds no interop string read), and the bits are the
    /// same eight the signature folds.</summary>
    internal readonly struct Entry
    {
        internal Entry(int id, string? name, int bits)
        {
            Id = id;
            Name = name;
            Bits = bits;
        }

        internal readonly int Id;
        internal readonly string? Name;
        internal readonly int Bits;
    }

    /// <summary>What the banked table holds per renderer. The name is a REFERENCE to the string
    /// the census already owns — banking it costs a dictionary slot, not an allocation — and it
    /// is the only way a renderer that has since been destroyed can still be named.</summary>
    internal readonly struct Banked
    {
        internal Banked(string? name, int bits)
        {
            Name = name;
            Bits = bits;
        }

        internal readonly string? Name;
        internal readonly int Bits;
    }

    /// <summary>A group of culprits sharing one name, and how many there were.</summary>
    internal readonly struct Group
    {
        internal Group(string name, int count)
        {
            Name = name;
            Count = count;
        }

        internal readonly string Name;
        internal readonly int Count;
    }

    /// <summary>
    /// The census result. Every field is a count taken over the WHOLE population — the group
    /// lists are a presentation cap and the elision counters say exactly what they left out, so
    /// no reader can mistake a truncated list for a complete one.
    /// </summary>
    internal sealed class Census
    {
        /// <summary>Renderers present live that the banked table has never seen.</summary>
        internal int Entered;
        /// <summary>Renderers the banked table holds that are no longer in the live census.</summary>
        internal int Left;
        /// <summary>Renderers present on both sides whose verdict bits differ.</summary>
        internal int Changed;
        /// <summary>Renderers present on both sides with identical bits.</summary>
        internal int Unchanged;

        /// <summary>How many CHANGED renderers moved each bit. Index = bit position, so
        /// <c>BitFlips[7]</c> is the <c>activeInHierarchy</c> count. A renderer that moved two
        /// bits is counted under both — this is a per-BIT tally, not a partition, and the sum
        /// is therefore ≥ <see cref="Changed"/> by construction.</summary>
        internal readonly int[] BitFlips = new int[BitCount];

        internal readonly List<Group> EnteredGroups = new();
        internal readonly List<Group> LeftGroups = new();
        internal readonly List<Group> ChangedGroups = new();

        /// <summary>Distinct NAMES in each class, and how many of those the group list omits.
        /// Both are printed: "top 8 of 41 groups, 312 renderer(s) not listed" is a complete
        /// statement; "top 8" alone is the defect this project has a ledger entry for.</summary>
        internal int EnteredGroupCount, LeftGroupCount, ChangedGroupCount;
        internal int EnteredElidedGroups, LeftElidedGroups, ChangedElidedGroups;
        internal int EnteredElidedRenderers, LeftElidedRenderers, ChangedElidedRenderers;

        /// <summary>True when the census found NOTHING — the null result the caller must print
        /// LOUDLY rather than suppress: the signature said the scene moved, so a census that
        /// finds no mover is evidence that the instrument or the banking is wrong, and that is
        /// a finding in its own right.</summary>
        internal bool FoundNothing => Entered == 0 && Left == 0 && Changed == 0;
    }

    /// <summary>Unity appends this to an instantiated prefab's name; two copies of one prefab
    /// must group together, so it is stripped for the grouping key only.</summary>
    private const string CloneSuffix = "(Clone)";

    /// <summary>
    /// The grouping key for a renderer name. Deliberately almost nothing: strip a trailing
    /// <c>(Clone)</c> and surrounding whitespace, and otherwise group by the EXACT name.
    ///
    /// <para>WHY NOT A CLEVERER NORMALISATION. Unity does not decorate instantiated
    /// GameObjects beyond "(Clone)" — scene copies of one prefab already share a name
    /// character for character ('Wall 2', 'polySurface2'), so stripping trailing digits would
    /// merge 'Wall 2' and 'Wall 3', which are different walls and the exact distinction this
    /// census exists to make. A normaliser that merges two real populations produces a
    /// confident wrong answer, which is worse than a longer list.</para>
    /// </summary>
    internal static string GroupKey(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "<unnamed>";
        string n = name!.Trim();
        if (n.EndsWith(CloneSuffix, StringComparison.Ordinal))
            n = n.Substring(0, n.Length - CloneSuffix.Length).TrimEnd();
        return n.Length == 0 ? "<unnamed>" : n;
    }

    /// <summary>
    /// Diff the live census against what the last commit banked.
    ///
    /// <para>ORDER-FREE, like the signature half it explains: the live side is a sequence and
    /// the banked side a dictionary, and every comparison is by instance ID. The
    /// <c>FindObjectsOfType</c> sweep does not specify an order and one cycle in three takes a
    /// fresh one — a census that depended on order would report a reordered array as a churning
    /// scene, which is precisely the mistake the signature's commutative fold exists to avoid.
    /// </para>
    ///
    /// <para>COST: one dictionary probe per live renderer, plus one pass over the banked table
    /// for the leavers. No allocation per renderer — the group tallies reuse the scratch
    /// dictionaries the caller owns.</para>
    /// </summary>
    /// <param name="banked">Instance ID → what the last commit saw. Never mutated.</param>
    /// <param name="live">This cycle's classified renderers, in any order.</param>
    /// <param name="topGroups">How many name groups each class lists. The rest are counted,
    /// never dropped silently.</param>
    internal static Census Diff(
        IReadOnlyDictionary<int, Banked> banked,
        IReadOnlyList<Entry> live,
        int topGroups)
    {
        var census = new Census();
        var enteredBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var leftBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var changedBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<int>();

        for (int i = 0; i < live.Count; i++)
        {
            Entry e = live[i];
            seen.Add(e.Id);
            if (!banked.TryGetValue(e.Id, out Banked was))
            {
                census.Entered++;
                Tally(enteredBy, GroupKey(e.Name));
                continue;
            }
            if (was.Bits == e.Bits)
            {
                census.Unchanged++;
                continue;
            }
            census.Changed++;
            Tally(changedBy, GroupKey(e.Name));
            int moved = was.Bits ^ e.Bits;
            for (int b = 0; b < BitCount; b++)
            {
                if ((moved & (1 << b)) != 0)
                    census.BitFlips[b]++;
            }
        }

        foreach (KeyValuePair<int, Banked> kv in banked)
        {
            if (seen.Contains(kv.Key))
                continue;
            census.Left++;
            Tally(leftBy, GroupKey(kv.Value.Name));
        }

        Rank(enteredBy, topGroups, census.EnteredGroups,
            out census.EnteredGroupCount, out census.EnteredElidedGroups,
            out census.EnteredElidedRenderers);
        Rank(leftBy, topGroups, census.LeftGroups,
            out census.LeftGroupCount, out census.LeftElidedGroups,
            out census.LeftElidedRenderers);
        Rank(changedBy, topGroups, census.ChangedGroups,
            out census.ChangedGroupCount, out census.ChangedElidedGroups,
            out census.ChangedElidedRenderers);
        return census;
    }

    private static void Tally(Dictionary<string, int> by, string key)
    {
        by.TryGetValue(key, out int n);
        by[key] = n + 1;
    }

    /// <summary>
    /// Sort the groups by count (descending, then by name so the line is stable between two
    /// runs of the same scene) and take the top N — reporting how many groups AND how many
    /// renderers were left out, which is the whole difference between this and a list ending
    /// in an ellipsis.
    /// </summary>
    private static void Rank(
        Dictionary<string, int> by, int topGroups, List<Group> into,
        out int groupCount, out int elidedGroups, out int elidedRenderers)
    {
        groupCount = by.Count;
        elidedGroups = 0;
        elidedRenderers = 0;
        if (by.Count == 0)
            return;
        var all = new List<Group>(by.Count);
        foreach (KeyValuePair<string, int> kv in by)
            all.Add(new Group(kv.Key, kv.Value));
        all.Sort(static (a, b) => a.Count != b.Count
            ? b.Count.CompareTo(a.Count)
            : string.CompareOrdinal(a.Name, b.Name));
        int take = topGroups < 0 ? 0 : Math.Min(topGroups, all.Count);
        for (int i = 0; i < take; i++)
            into.Add(all[i]);
        for (int i = take; i < all.Count; i++)
        {
            elidedGroups++;
            elidedRenderers += all[i].Count;
        }
    }

    /// <summary>
    /// The line. One string, self-describing, and it prints something meaningful in all three
    /// states an instrument can be in: it found movers, it found none (which indicts itself),
    /// or it has no banked table to compare against.
    /// </summary>
    internal static string Format(Census c, int liveCount, int bankedCount, bool tableBanked)
    {
        var sb = new System.Text.StringBuilder(768);
        sb.Append("SIGNATURE CULPRITS: ");
        if (!tableBanked)
        {
            sb.Append("no banked census to compare against — the last commit landed before this "
                    + "instrument was armed (a scene load, a config flip, or the very first "
                    + "commit of the session). The NEXT scene-signature refusal will name names.");
            return sb.ToString();
        }
        sb.Append(liveCount).Append(" renderer(s) classified this cycle against ")
          .Append(bankedCount).Append(" banked at the last commit — ")
          .Append(c.Entered).Append(" ENTERED, ").Append(c.Left).Append(" LEFT, ")
          .Append(c.Changed).Append(" CHANGED verdict bits, ")
          .Append(c.Unchanged).Append(" identical.");
        if (c.FoundNothing)
        {
            sb.Append(" NOTHING MOVED, AND THAT IS A FINDING ABOUT THIS INSTRUMENT, NOT ABOUT "
                    + "THE SCENE: the signature refused this cycle, so something in the fold "
                    + "must differ. A census that agrees with the banked table while the hash "
                    + "does not means the two are not built from the same population — the "
                    + "banking site, the bit set or the fold order is wrong. Do not narrow the "
                    + "signature on the strength of a run that prints this line.");
            return sb.ToString();
        }
        AppendClass(sb, "ENTERED", c.Entered, c.EnteredGroups, c.EnteredGroupCount,
            c.EnteredElidedGroups, c.EnteredElidedRenderers);
        AppendClass(sb, "LEFT", c.Left, c.LeftGroups, c.LeftGroupCount,
            c.LeftElidedGroups, c.LeftElidedRenderers);
        AppendClass(sb, "CHANGED", c.Changed, c.ChangedGroups, c.ChangedGroupCount,
            c.ChangedElidedGroups, c.ChangedElidedRenderers);
        if (c.Changed > 0)
        {
            sb.Append(" WHICH BIT MOVED (per-bit tally over the CHANGED population; a renderer "
                    + "that moved two bits counts under both, so these sum to at least the "
                    + "CHANGED count):");
            bool any = false;
            for (int b = 0; b < BitCount; b++)
            {
                if (c.BitFlips[b] == 0)
                    continue;
                sb.Append(any ? ", " : " ").Append(c.BitFlips[b]).Append(" x '")
                  .Append(BitNames[b]).Append('\'');
                any = true;
            }
            sb.Append('.');
        }
        sb.Append(" READ IT LIKE THIS: a population that cannot change a fade verdict — "
                + "particle systems, torch flames toggling activeInHierarchy, figures animating "
                + "renderers in and out — means these ~90ms commits are paid for nothing and "
                + "the signature should hash a narrower set. Wall masonry entering and leaving "
                + "means the scene genuinely churns, the skip is already right, and the cost "
                + "belongs to the commit rather than to its frequency.");
        return sb.ToString();
    }

    private static void AppendClass(
        System.Text.StringBuilder sb, string label, int total, List<Group> groups,
        int groupCount, int elidedGroups, int elidedRenderers)
    {
        if (total == 0)
            return;
        sb.Append(' ').Append(label).Append(" (").Append(total).Append(" renderer(s) in ")
          .Append(groupCount).Append(" name group(s)):");
        for (int i = 0; i < groups.Count; i++)
        {
            sb.Append(i == 0 ? " " : ", ").Append(groups[i].Count).Append(" x '")
              .Append(groups[i].Name).Append('\'');
        }
        if (elidedGroups > 0)
        {
            sb.Append(", and ").Append(elidedGroups).Append(" further group(s) covering ")
              .Append(elidedRenderers).Append(" renderer(s) NOT LISTED");
        }
        sb.Append('.');
    }
}
