using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GloomhavenVR.WireTests;

/// <summary>
/// PERF S6 (2026-09-05) — "WHICH SEGMENTS HOLD THIS RENDERER?", THE OLD SHAPE AND THE NEW ONE,
/// DRIVEN AGAINST EACH OTHER.
///
/// <para>WHAT IT IS FOR. <c>ResolvePropUnit</c>'s PASS 2 answered that question by enumerating
/// the WHOLE segment table per member and running <c>List.IndexOf</c> on each one's renderer
/// list. ModBuild 435's board makes that table 826 entries (34 independently-deciding walls plus
/// 792 split-run pieces, [WallSegmentFade] PER-WALL line) against the few dozen the pass was
/// written for, and <c>WallFade.Commit.PropUnits</c> reads 52.6 ms. The replacement is a holder
/// index built by the walk PHASE 1a already takes.</para>
///
/// <para>WHAT THIS FILE MEASURES, STATED SO IT IS NOT OVER-READ. The shipped method lives on a
/// <c>MonoBehaviour</c> and takes Unity <c>Renderer</c>s, so it cannot run here. What runs here
/// is the two ACCESS SHAPES over the same containers (<c>Dictionary</c> values enumeration,
/// <c>List.IndexOf</c>, and the head/next chain) at the logged populations. That prices the
/// algorithm, which is the half a desktop box CAN price faithfully; the Unity interop beside it
/// on hardware is priced by the new [WallSegmentFade] TICK BUDGET line's holder clause, which
/// prints the same two visit counts from the player's own board. READ THE RATIO.</para>
///
/// <para>AND THE EQUIVALENCE IS ASSERTED BEFORE THE COST IS TAKEN, because a faster answer to a
/// different question is worth nothing. The two shapes are driven over a membership that
/// contains both awkward cases — a renderer held by TWO segments, and a renderer listed TWICE in
/// one segment's list — and the whole removal SEQUENCE (segment and order, not just the set) is
/// compared element for element.</para>
/// </summary>
internal static class WallPropUnitHolderVectors
{
    /// <summary>The ModBuild 435 board: 34 independently-deciding walls + 792 split-run pieces.
    /// The PER-WALL line prints both halves; this is their sum, and it is what
    /// <c>_live.Segments.Count</c> is on the scenario the user reported.</summary>
    private const int LoggedSegments = 826;

    /// <summary>Fade-capable renderers per wall segment — the [WallSegmentFade] heartbeat's
    /// "~24 each" figure. A split-run piece holds ONE renderer by construction (that is what the
    /// per-renderer split is), which is the second population below.</summary>
    private const int RenderersPerWall = 24;

    private const int WallSegments = 34;

    /// <summary>Multi-part props the ModBuild 435 PROP UNIT line reports resolved in one commit
    /// ("92 multi-part prop(s) ... now have ONE owner each").</summary>
    private const int LoggedUnits = 92;

    /// <summary>Members per unit — the census rows run 12-22 against a cap of 24; 12 is the low
    /// end, so the cost below is the CONSERVATIVE reading of the old shape.</summary>
    private const int MembersPerUnit = 12;

    private sealed class Seg
    {
        internal Seg(int id, bool perRendererSplit)
        {
            Id = id;
            PerRendererSplit = perRendererSplit;
        }

        internal readonly int Id;
        internal readonly bool PerRendererSplit;
        internal readonly List<object> Renderers = new();
    }

    /// <summary>One removal the pass would perform, in the order it would perform it.</summary>
    private readonly struct Removal : IEquatable<Removal>
    {
        internal Removal(int member, int seg) { Member = member; Seg = seg; }
        internal readonly int Member;
        internal readonly int Seg;
        public bool Equals(Removal other) => Member == other.Member && Seg == other.Seg;
        public override string ToString() => $"m{Member}@s{Seg}";
    }

    /// <summary>The table under test: segments in a Dictionary (the shipped container, and its
    /// enumeration order is the order both shapes must agree on) plus the holder index.</summary>
    private sealed class Table
    {
        internal readonly Dictionary<int, Seg> Segments = new();
        internal readonly Dictionary<object, int> HolderHead = new();
        internal readonly Dictionary<object, int> HolderTail = new();
        internal readonly List<Seg> HolderSeg = new();
        internal readonly List<int> HolderNext = new();

        /// <summary>PHASE 1a, verbatim in shape: the same single walk that fills the claim set
        /// also appends each (renderer, segment) to the holder chain, suppressing an immediate
        /// repeat of the segment it just added.</summary>
        internal void BuildHolders()
        {
            HolderHead.Clear();
            HolderTail.Clear();
            HolderSeg.Clear();
            HolderNext.Clear();
            foreach (Seg seg in Segments.Values)
            {
                foreach (object r in seg.Renderers)
                    AddHolder(r, seg);
            }
        }

        private void AddHolder(object r, Seg seg)
        {
            if (HolderTail.TryGetValue(r, out int tail))
            {
                if (ReferenceEquals(HolderSeg[tail], seg))
                    return;
                int node = HolderSeg.Count;
                HolderSeg.Add(seg);
                HolderNext.Add(-1);
                HolderNext[tail] = node;
                HolderTail[r] = node;
                return;
            }
            int head = HolderSeg.Count;
            HolderSeg.Add(seg);
            HolderNext.Add(-1);
            HolderHead[r] = head;
            HolderTail[r] = head;
        }
    }

    /// <summary>THE OLD SHAPE: every member against the whole table.</summary>
    private static int OldShape(Table t, List<object> members, Seg owner, List<Removal>? log)
    {
        int visits = 0;
        foreach (object m in members)
        {
            foreach (Seg seg in t.Segments.Values)
            {
                visits++;
                if (ReferenceEquals(seg, owner) || seg.PerRendererSplit)
                    continue;
                int at = seg.Renderers.IndexOf(m);
                if (at < 0)
                    continue;
                seg.Renderers.RemoveAt(at);
                log?.Add(new Removal(members.IndexOf(m), seg.Id));
            }
        }
        return visits;
    }

    /// <summary>THE NEW SHAPE: every member against its own holder chain.</summary>
    private static int NewShape(Table t, List<object> members, Seg owner, List<Removal>? log)
    {
        int visits = 0;
        foreach (object m in members)
        {
            for (int node = t.HolderHead.TryGetValue(m, out int h) ? h : -1;
                 node >= 0;
                 node = t.HolderNext[node])
            {
                visits++;
                Seg seg = t.HolderSeg[node];
                if (ReferenceEquals(seg, owner) || seg.PerRendererSplit)
                    continue;
                int at = seg.Renderers.IndexOf(m);
                if (at < 0)
                    continue;
                seg.Renderers.RemoveAt(at);
                log?.Add(new Removal(members.IndexOf(m), seg.Id));
            }
        }
        return visits;
    }

    internal static void Run(Harness t)
    {
        // ---- EQUIVALENCE, INCLUDING THE TWO AWKWARD CASES --------------------------------
        // The membership below is built so that the answer is NOT trivially the same:
        //   * member 0 is held by two different segments (PHASE 1's own comment says a renderer
        //     "that somehow sits in TWO segments' lists" is a real case);
        //   * member 1 is listed TWICE inside one segment (the multiplicity the chain has to
        //     suppress, or the new shape would remove it twice where the old removed it once);
        //   * member 2 is held only by the OWNER (both shapes must remove nothing);
        //   * member 3 is held by a per-renderer split piece (both shapes must skip it);
        //   * member 4 is held by nobody.
        t.Case("propunit-holders/equivalence");
        var oldRemovals = new List<Removal>();
        var newRemovals = new List<Removal>();
        int oldVisits = RunAwkward(oldShape: true, oldRemovals);
        int newVisits = RunAwkward(oldShape: false, newRemovals);
        t.Equal(oldRemovals.Count, newRemovals.Count,
            "the two shapes perform the same NUMBER of removals");
        bool sameSequence = oldRemovals.Count == newRemovals.Count;
        for (int i = 0; sameSequence && i < oldRemovals.Count; i++)
            sameSequence = oldRemovals[i].Equals(newRemovals[i]);
        t.True(sameSequence,
            "…the same removals, from the same segments, in the same order: "
            + string.Join(",", oldRemovals) + " vs " + string.Join(",", newRemovals));
        t.True(oldRemovals.Count > 0, "the fixture actually exercises a removal");
        t.True(newVisits < oldVisits,
            $"…and the new shape visited fewer segments ({newVisits} vs {oldVisits})");

        // ---- COST, AT THE LOGGED POPULATION ------------------------------------------------
        // MEASURE IT, DO NOT ASSUME IT. Both shapes run over the SAME table, rebuilt between
        // runs so neither is handed a list the other already emptied. The assertion is a loose
        // property of the algorithm (the new shape must not be slower); the numbers are PRINTED,
        // and the printed RATIO is what the report quotes.
        t.Case("propunit-holders/cost-at-logged-table-size");
        Table big = BuildLoggedTable(out List<List<object>> units, out Seg owner);
        int totalOldVisits = 0, totalNewVisits = 0;
        double oldMs = TimeShape(oldShape: true, ref totalOldVisits, out int oldReps);
        double newMs = TimeShape(oldShape: false, ref totalNewVisits, out int newReps);
        t.Equal(oldReps, newReps, "both shapes were timed over the same number of commits");
        t.True(totalOldVisits > totalNewVisits,
            "the whole-table walk really does visit more segments than the holder chains");
        t.True(newMs <= oldMs,
            $"the holder index is not slower ({newMs:F3} ms vs {oldMs:F3} ms per commit)");
        Console.WriteLine(
            $"      propunit holder cost: {oldMs:F3} ms per commit walking the whole table vs "
            + $"{newMs:F3} ms walking the holder chains — {(newMs > 0 ? oldMs / newMs : 0):F1}x, "
            + $"over {LoggedSegments} segment(s) ({WallSegments} wall(s) x {RenderersPerWall} "
            + $"renderer(s) + {LoggedSegments - WallSegments} split-run piece(s) x 1) and "
            + $"{LoggedUnits} unit(s) x {MembersPerUnit} member(s) = {totalOldVisits} segment "
            + $"visit(s) reduced to {totalNewVisits} chain step(s). THIS IS A DESKTOP CI BOX AND "
            + "ONLY THE ACCESS SHAPE, not the shipped method (which needs Unity): read the RATIO "
            + "between the two runs, which is a property of the algorithm, and treat the absolute "
            + "figures as an order of magnitude that no headset has confirmed. The hardware "
            + "counterpart is the two visit counts on the [WallSegmentFade] TICK BUDGET line.");
        // Silence the unused-out warnings without weakening the fixture: both are the table the
        // timing loop rebuilds, and they are named so the fixture reads.
        t.True(big.Segments.Count == LoggedSegments, "the synthetic table is the logged size");
        t.True(units.Count == LoggedUnits && owner.Renderers.Count >= 0,
            "…and carries the logged number of units");
    }

    /// <summary>The five-member fixture described at the call site. Returns segment visits.</summary>
    private static int RunAwkward(bool oldShape, List<Removal> log)
    {
        var t = new Table();
        var owner = new Seg(0, perRendererSplit: false);
        var a = new Seg(1, perRendererSplit: false);
        var b = new Seg(2, perRendererSplit: false);
        var split = new Seg(3, perRendererSplit: true);
        var empty = new Seg(4, perRendererSplit: false);
        t.Segments[0] = owner;
        t.Segments[1] = a;
        t.Segments[2] = b;
        t.Segments[3] = split;
        t.Segments[4] = empty;

        var members = new List<object>();
        for (int i = 0; i < 5; i++)
            members.Add(new object());

        a.Renderers.Add(members[0]);          // held by two segments…
        b.Renderers.Add(members[0]);          // …a and b
        a.Renderers.Add(members[1]);          // listed twice in ONE segment
        a.Renderers.Add(members[1]);
        owner.Renderers.Add(members[2]);      // the owner's own — never removed
        split.Renderers.Add(members[3]);      // a per-renderer split piece — never removed
        // members[4] is held by nobody.

        t.BuildHolders();
        return oldShape ? OldShape(t, members, owner, log) : NewShape(t, members, owner, log);
    }

    private static Table BuildLoggedTable(out List<List<object>> units, out Seg owner)
    {
        var t = new Table();
        var all = new List<object>(LoggedSegments * 4);
        for (int i = 0; i < LoggedSegments; i++)
        {
            bool wall = i < WallSegments;
            var seg = new Seg(i, perRendererSplit: !wall);
            int n = wall ? RenderersPerWall : 1;
            for (int k = 0; k < n; k++)
            {
                var r = new object();
                seg.Renderers.Add(r);
                all.Add(r);
            }
            t.Segments[i] = seg;
        }
        t.BuildHolders();
        owner = t.Segments[0];

        // The units the commit actually resolves. Members are drawn from the live membership —
        // that is what makes the old shape's walk find anything at all, and it is the case the
        // holder chain has to reproduce exactly.
        units = new List<List<object>>(LoggedUnits);
        int cursor = 0;
        for (int u = 0; u < LoggedUnits; u++)
        {
            var members = new List<object>(MembersPerUnit);
            for (int m = 0; m < MembersPerUnit; m++)
            {
                members.Add(all[cursor % all.Count]);
                cursor += 7; // a stride, so a unit's members are spread across the table
            }
            units.Add(members);
        }
        return t;
    }

    /// <summary>Time one shape over a whole commit's worth of units, rebuilding the table for
    /// every repetition so the two shapes see identical input.</summary>
    private static double TimeShape(bool oldShape, ref int visits, out int reps)
    {
        const int Reps = 5;
        reps = Reps;
        // One untimed pass to warm the JIT and the collections.
        {
            Table warm = BuildLoggedTable(out List<List<object>> warmUnits, out Seg warmOwner);
            foreach (List<object> u in warmUnits)
            {
                if (oldShape)
                    OldShape(warm, u, warmOwner, null);
                else
                    NewShape(warm, u, warmOwner, null);
            }
        }
        double total = 0;
        int seen = 0;
        for (int rep = 0; rep < Reps; rep++)
        {
            Table table = BuildLoggedTable(out List<List<object>> us, out Seg own);
            var sw = Stopwatch.StartNew();
            foreach (List<object> u in us)
            {
                seen += oldShape
                    ? OldShape(table, u, own, null)
                    : NewShape(table, u, own, null);
            }
            sw.Stop();
            total += sw.Elapsed.TotalMilliseconds;
        }
        visits = seen / Reps;
        return total / Reps;
    }
}
