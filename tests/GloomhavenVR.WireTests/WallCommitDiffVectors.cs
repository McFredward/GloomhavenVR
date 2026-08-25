using System;
using System.Diagnostics;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE EQUALITY GATE FOR THE SLICED WALL COMMIT, FALSIFIED BEFORE IT IS BELIEVED.
///
/// <para><b>WHY THIS IS ON THIS LIST, and it is the sharpest case yet.</b> PERF B slices the
/// ~95 ms atomic wall-table commit over ~63 frames so the user stops feeling it — his words,
/// 2026-08-25: <i>"Ich will aber eigentlich gar keine spürbaren Ruckler - nicht nur
/// seltenere."</i> The only thing standing between that and a wall left permanently
/// half-transparent is <see cref="WallCommitDiff"/>, whose verdict — "the sliced table and the
/// atomic table agree" — is the entire safety argument for touching a fade behaviour the user
/// has just called perfect.</para>
///
/// <para>A gate that cannot fail is not a gate. This project's ledger records sixteen builds
/// lost on one defect to instruments that were believed on sight, an instrument that
/// contradicted its own numbers, and one whose first output was a wrong cause stated with
/// confidence. So the cases below drive the gate on a <b>NULL input</b> (two identical tables:
/// it must find nothing, AND say that finding nothing is a statement about itself) and on a
/// <b>known positive of every difference class it claims to detect</b> — including one the
/// design note did not name.</para>
///
/// <para><b>THE FOUR SNAP CASES, one control each.</b> Case 1: a segment dropped mid-fade, whose
/// renderers nothing will ever take back. Case 2: a shared segment whose animation state is not
/// carried. Case 3: a shared segment whose renderer LIST changed, where the leaver freezes and
/// the joiner never fades. Case 4 — not in the design note: a renderer owned by BOTH tables
/// under DIFFERENT anchors, which is neither a leaver nor a joiner and which no per-segment
/// carry-forward can see.</para>
///
/// <para><b>AND THE ONE THAT IS NOT A SNAP CASE AT ALL:</b> the gate must walk all SEVEN
/// ownership classes. The design note proposes four. Body, Stacked and UnitDressing each carry
/// their own <c>Prev*</c> list and their own <c>*State</c> undo flag, so a gate blind to them
/// agrees with a torn table — and agreement from a blind instrument is exactly what would ship
/// build 2. The class-coverage case below plants a difference in each of the seven in turn and
/// requires the gate to name it.</para>
/// </summary>
internal static class WallCommitDiffVectors
{
    /// <summary>The table size the hardware logs actually report, and the cost case runs at the
    /// TOP of it: 99-127 segments (ModBuild 277-280 heartbeats) over ~1,888 claimed renderers,
    /// which the design note prices as "O(total owned renderers) ≈ 2,000-3,000". A cost measured
    /// on a toy table is a number that sounds like an answer; so is a cost measured on a table
    /// seven times too big, which is what the first draft of this case did (127 x 15 in EVERY
    /// one of the seven classes = 13,335 owned renderers, and a reassuringly large headroom
    /// figure derived from a population that does not exist).</summary>
    private const int LoggedSegments = 127;
    /// <summary>Owned renderers per segment, TOTAL across all seven classes — 127 x 20 = 2,540,
    /// the top of the logged range.</summary>
    private const int LoggedOwnedPerSegment = 20;
    /// <summary>A deliberately oversized second run, to show how the cost scales rather than
    /// asserting that one point on the curve is fine.</summary>
    private const int OversizeFactor = 4;

    internal static void Run(Harness t)
    {
        var d = new WallCommitDiff.Diff();
        var scratch = new WallCommitDiff.Scratch();

        // ---- NULL CONTROL 1: two identical tables ------------------------------------------
        // The single most important case here. If this ever reports a difference, every
        // non-zero count the gate prints on hardware is noise and build 2 must not ship.
        t.Case("walldiff/null-identical");
        WallCommitDiff.TableFacts a = BuildTable(3, seedFade: 0.5f);
        WallCommitDiff.TableFacts b = BuildTable(3, seedFade: 0.5f);
        WallCommitDiff.Compare(a, b, d, scratch, topGroups: 8);
        t.True(d.FoundNothing, "two identically-built tables must differ in nothing at all");
        t.Equal(0, d.OnlyInOld, "no segment is only in the old table");
        t.Equal(0, d.OnlyInNew, "no segment is only in the new table");
        t.Equal(0, d.OwnershipChanged, "no segment changed ownership");
        t.Equal(0, d.FieldsChanged, "no segment changed a compared field");
        t.Equal(3, d.Identical, "all three segments are identical");
        t.Equal(0, d.Leaving.Count, "nothing leaves");
        t.Equal(0, d.Joining.Count, "nothing joins");
        t.Equal(0, d.Moved.Count, "nothing changes owner");
        t.Equal(3, d.Carried.Count, "all three anchors carry animation state forward");
        t.Equal(0, d.StructuralFieldDiffs, "and no structural field moved");
        // THE PART THAT MAKES IT A NULL CONTROL RATHER THAN A PASS: the line must accuse
        // itself, and must print the denominators the reader needs to tell "they agree" from
        // "there was nothing to compare".
        string nullLine = WallCommitDiff.Format(d, "atomic vs sliced", 3, 3);
        t.True(nullLine.Contains("NO DIFFERENCE OF ANY KIND"),
            "the null result is stated, not suppressed");
        t.True(nullLine.Contains("DEPENDS ENTIRELY ON THE DENOMINATORS"),
            "…and it says so: a null reading is a statement about the instrument");
        t.True(nullLine.Contains("owned renderers on the two sides"),
            "…with both populations printed, so the ratio has a visible denominator");

        // ---- NULL CONTROL 2: nothing to compare --------------------------------------------
        // The state an instrument is in before it has been armed. It must NOT read as agreement
        // — a held instrument reading as agreement is how a shipped-and-lying diagnostic starts.
        t.Case("walldiff/null-empty");
        WallCommitDiff.TableFacts empty1 = BuildTable(0, 0f);
        WallCommitDiff.TableFacts empty2 = BuildTable(0, 0f);
        WallCommitDiff.Compare(empty1, empty2, d, scratch, topGroups: 8);
        string emptyLine = WallCommitDiff.Format(d, "atomic vs sliced", 0, 0);
        t.True(emptyLine.Contains("BOTH TABLES WERE EMPTY"),
            "an empty comparison names itself as empty");
        t.True(emptyLine.Contains("It is not agreement"),
            "…and refuses to be read as agreement");
        t.True(!emptyLine.Contains("NO DIFFERENCE OF ANY KIND"),
            "…and does not borrow the agreement wording");

        // ---- CASE 1: a segment dropped MID-FADE --------------------------------------------
        // The worst of the four. Nothing owns the renderer, nothing restores it, and the
        // MaterialPropertyBlock stays on it for the rest of the session.
        t.Case("walldiff/case1-dropped-mid-fade");
        WallCommitDiff.TableFacts old1 = BuildTable(3, seedFade: 0.5f);
        WallCommitDiff.TableFacts new1 = BuildTable(2, seedFade: 0.5f); // anchor 3 is gone
        WallCommitDiff.Compare(old1, new1, d, scratch, topGroups: 8);
        t.Equal(1, d.OnlyInOld, "the dropped segment is found");
        t.Equal(1, d.OnlyInOldMidFade, "…and it is reported as MID-FADE, which is the severity");
        t.Equal(0, d.OnlyInNew, "nothing appeared");
        // Its renderers must be on the restore list, in every class it owned them in.
        t.Equal(WallCommitDiff.KindCount, d.Leaving.Count,
            "every one of the dropped segment's owned renderers is a LEAVER to restore");
        t.True(AllFrom(d.Leaving, AnchorId(3)),
            "…and every leaver names the dropped anchor as the owner to restore from");
        t.Equal(0, d.Joining.Count, "nothing joins");
        t.Equal(0, d.Moved.Count, "nothing changes owner");
        // MID-FADE must not mean "Fade > 0" alone: a wall at rest still holding a torch hidden
        // is exactly as permanent a loss.
        t.Case("walldiff/case1-mid-fade-is-not-only-fade");
        WallCommitDiff.TableFacts oldTorch = BuildTable(1, seedFade: 0f);
        First(oldTorch).MountedState = 2;   // Fade 0, but a mounted prop is held hidden
        WallCommitDiff.TableFacts newTorch = BuildTable(0, 0f);
        WallCommitDiff.Compare(oldTorch, newTorch, d, scratch, topGroups: 8);
        t.Equal(1, d.OnlyInOldMidFade,
            "a segment at Fade 0 still holding a mounted prop counts as mid-fade");

        // ---- CASE 2: a shared segment whose animation state is not carried -----------------
        t.Case("walldiff/case2-animation-not-carried");
        WallCommitDiff.TableFacts old2 = BuildTable(2, seedFade: 0.7f);
        WallCommitDiff.TableFacts new2 = BuildTable(2, seedFade: 0f); // a FRESH build starts at rest
        WallCommitDiff.Compare(old2, new2, d, scratch, topGroups: 8);
        t.Equal(2, d.FieldsChanged, "both shared segments differ on a compared field");
        t.Equal(2, d.FieldDiffs[WallCommitDiff.FieldFade], "…and the field is Fade");
        t.Equal(0, d.OwnershipChanged, "ownership is untouched");
        t.Equal(2, d.Carried.Count, "both anchors are on the carry-forward list");
        // The split is the point: an animation difference is EXPECTED of a fresh build and must
        // not be reported in the same breath as a structural one.
        t.Equal(0, d.StructuralFieldDiffs,
            "a Fade difference is ANIMATION state, not a structural disagreement");
        string case2Line = WallCommitDiff.Format(d, "atomic vs sliced", 2, 2);
        t.True(case2Line.Contains("NO STRUCTURAL FIELD DIFFERED"),
            "…and the line says the structural half is clean, which is what build 2 needs");
        t.True(case2Line.Contains("ANIMATION FIELDS"),
            "…while still naming the animation differences it did find");

        // ---- CASE 3: a shared segment whose RENDERER LIST changed --------------------------
        // The subtle one. Old owns {A,B}, new owns {A,C}: A is fine, B freezes, C never fades.
        t.Case("walldiff/case3-renderer-list-changed");
        WallCommitDiff.TableFacts old3 = OneSegment(anchor: 1, kind: WallCommitDiff.KindRenderers,
            ids: new[] { 100, 200 });
        WallCommitDiff.TableFacts new3 = OneSegment(anchor: 1, kind: WallCommitDiff.KindRenderers,
            ids: new[] { 100, 300 });
        WallCommitDiff.Compare(old3, new3, d, scratch, topGroups: 8);
        t.Equal(0, d.OnlyInOld, "the segment itself survives");
        t.Equal(0, d.OnlyInNew, "…on both sides");
        t.Equal(1, d.OwnershipChanged, "but its ownership changed, and the gate says so");
        t.Equal(1, d.OwnershipDiffs[WallCommitDiff.KindRenderers],
            "…in the wall-renderer class");
        t.Equal(1, d.Leaving.Count, "exactly one renderer leaves");
        t.Equal(200, d.Leaving[0].RendererId, "…and it is B, the one that would freeze mid-fade");
        t.Equal(1, d.Joining.Count, "exactly one renderer joins");
        t.Equal(300, d.Joining[0].RendererId, "…and it is C, the one that would never fade");
        t.Equal(0, d.Moved.Count, "A stays put and is therefore on neither list");

        // ---- CASE 4: a renderer owned by BOTH tables under DIFFERENT anchors ---------------
        // NOT IN THE DESIGN NOTE. Neither a leaver nor a joiner: the renderer is owned
        // throughout, so a union-of-owned-renderers diff sees nothing, and a per-segment carry
        // sees nothing either — but the block state that says whether it carries an MPB lives
        // on the segment, and it just changed segment. This is the statue in skelet.jpg
        // changing wall, which is a photographed defect class in this subsystem.
        t.Case("walldiff/case4-renderer-changed-owner");
        WallCommitDiff.TableFacts old4 = TwoSegments(
            anchorA: 1, idsA: new[] { 100, 200 }, anchorB: 2, idsB: new[] { 300 });
        WallCommitDiff.TableFacts new4 = TwoSegments(
            anchorA: 1, idsA: new[] { 100 }, anchorB: 2, idsB: new[] { 200, 300 });
        WallCommitDiff.Compare(old4, new4, d, scratch, topGroups: 8);
        t.Equal(0, d.Leaving.Count, "nothing is unowned — which is exactly why this case hides");
        t.Equal(0, d.Joining.Count, "and nothing is newly owned");
        t.Equal(1, d.Moved.Count, "but one renderer changed OWNER, and only case 4 can see it");
        t.Equal(200, d.Moved[0].RendererId, "…the renderer that moved wall");
        t.Equal(AnchorId(1), d.Moved[0].FromAnchorId, "…from the first wall");
        t.Equal(AnchorId(2), d.Moved[0].ToAnchorId, "…to the second");
        t.Equal(2, d.OwnershipChanged, "both walls report a changed ownership set");
        string case4Line = WallCommitDiff.Format(d, "atomic vs sliced", 2, 2);
        t.True(case4Line.Contains("case 4"),
            "the line names case 4 explicitly — it is the one a reader would not look for");

        // ---- CLASS COVERAGE: all SEVEN ownership classes, not the four the note names -------
        // A gate blind to a class agrees with a torn table. This plants one difference in each
        // class in turn and requires the gate to name that class and no other.
        for (int k = 0; k < WallCommitDiff.KindCount; k++)
        {
            t.Case("walldiff/class-coverage/" + WallCommitDiff.KindNames[k]);
            WallCommitDiff.TableFacts oldK = OneSegment(1, k, new[] { 100 });
            WallCommitDiff.TableFacts newK = OneSegment(1, k, new[] { 101 });
            WallCommitDiff.Compare(oldK, newK, d, scratch, topGroups: 8);
            t.Equal(1, d.OwnershipDiffs[k],
                "a difference in '" + WallCommitDiff.KindNames[k] + "' is detected");
            t.Equal(1, d.OwnershipChanged, "…as exactly one changed segment");
            t.Equal(1, d.Leaving.Count, "…with one leaver");
            t.Equal(k, d.Leaving[0].Kind, "…carrying the class it left, so the restore is right");
            t.Equal(1, d.Joining.Count, "…and one joiner");
            t.Equal(k, d.Joining[0].Kind, "…in the same class");
        }

        // A renderer that moves BETWEEN classes must be a leaver in the old class and a joiner
        // in the new one — the seven undo logs are independent, and restoring a foliage piece
        // as if it were a sibling would leave a shader property behind.
        t.Case("walldiff/class-crossing");
        WallCommitDiff.TableFacts oldX = OneSegment(1, WallCommitDiff.KindFoliage, new[] { 100 });
        WallCommitDiff.TableFacts newX = OneSegment(1, WallCommitDiff.KindSiblings, new[] { 100 });
        WallCommitDiff.Compare(oldX, newX, d, scratch, topGroups: 8);
        t.Equal(1, d.Leaving.Count, "the piece leaves the foliage log");
        t.Equal(WallCommitDiff.KindFoliage, d.Leaving[0].Kind, "…named as foliage");
        t.Equal(1, d.Joining.Count, "and joins the sibling log");
        t.Equal(WallCommitDiff.KindSiblings, d.Joining[0].Kind, "…named as a sibling");
        t.Equal(0, d.Moved.Count, "a class crossing is not an owner change");

        // ---- STRUCTURAL DIFFERENCES: what actually indicts a sliced build ------------------
        t.Case("walldiff/structural-bounds");
        WallCommitDiff.TableFacts oldB = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        WallCommitDiff.TableFacts newB = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        First(newB).Bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));
        WallCommitDiff.Compare(oldB, newB, d, scratch, topGroups: 8);
        t.Equal(1, d.FieldDiffs[WallCommitDiff.FieldBounds], "a differing decision AABB is found");
        t.Equal(1, d.StructuralFieldDiffs, "…and it counts as STRUCTURAL, not animation");
        string structLine = WallCommitDiff.Format(d, "atomic vs sliced", 1, 1);
        t.True(structLine.Contains("STRUCTURAL FIELDS"), "the line names the structural half");
        t.True(!structLine.Contains("NO STRUCTURAL FIELD DIFFERED"),
            "…and must NOT also claim the structural half is clean");

        // Bounds are compared BITWISE, not with a tolerance: a sliced build that lands a centre
        // one ULP away has done something different, and a tolerance here would be a hidden
        // dial deciding how wrong the slice is allowed to be.
        t.Case("walldiff/structural-bounds-one-ulp");
        WallCommitDiff.TableFacts oldU = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        WallCommitDiff.TableFacts newU = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        First(oldU).Bounds = new Bounds(new Vector3(10f, 0f, 0f), Vector3.one);
        First(newU).Bounds = new Bounds(
            new Vector3(BitIncrement(10f), 0f, 0f), Vector3.one);
        WallCommitDiff.Compare(oldU, newU, d, scratch, topGroups: 8);
        t.Equal(1, d.FieldDiffs[WallCommitDiff.FieldBounds],
            "one ULP of difference is a difference — no tolerance is applied");

        // The gate-lift link is compared by the PARTNER'S ANCHOR, never by Segment reference:
        // two tables never share Segment instances, so a reference comparison would report
        // every single segment as different and the gate would be useless in exactly the
        // configuration it is built for.
        t.Case("walldiff/gate-lift-compared-by-anchor");
        WallCommitDiff.TableFacts oldG = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        WallCommitDiff.TableFacts newG = OneSegment(1, WallCommitDiff.KindRenderers, new[] { 100 });
        First(oldG).GateLiftAnchorId = AnchorId(9);
        First(newG).GateLiftAnchorId = AnchorId(9);
        WallCommitDiff.Compare(oldG, newG, d, scratch, topGroups: 8);
        t.Equal(0, d.FieldDiffs[WallCommitDiff.FieldGateLift],
            "two tables pointing at the same PARTNER agree, though the Segment objects differ");
        First(newG).GateLiftAnchorId = AnchorId(8);
        WallCommitDiff.Compare(oldG, newG, d, scratch, topGroups: 8);
        t.Equal(1, d.FieldDiffs[WallCommitDiff.FieldGateLift],
            "…and a genuinely re-pointed lift is caught");

        // ---- ORDER FREEDOM ------------------------------------------------------------------
        // A sliced build visits its phases on different frames and fills its lists in a
        // different sequence. A gate that called a reordering a difference would fire on every
        // cycle and mean nothing — the same mistake the skip signature's commutative fold
        // exists to avoid.
        t.Case("walldiff/order-free");
        WallCommitDiff.TableFacts oldO = OneSegment(1, WallCommitDiff.KindRenderers,
            new[] { 100, 200, 300 });
        WallCommitDiff.TableFacts newO = OneSegment(1, WallCommitDiff.KindRenderers,
            new[] { 300, 100, 200 });
        WallCommitDiff.Compare(oldO, newO, d, scratch, topGroups: 8);
        t.True(d.FoundNothing, "the same renderers in a different order are the same table");

        // ---- ELISION: a truncated list must never read as absence ---------------------------
        // This project has a ledger entry for exactly this: an ellipsis-capped list produced
        // the conclusion "X never appears" from a list that had simply run out of room.
        t.Case("walldiff/elision-counts");
        WallCommitDiff.TableFacts oldE = BuildTable(20, seedFade: 0.5f);
        WallCommitDiff.TableFacts newE = BuildTable(0, 0f); // every one of the 20 is dropped
        WallCommitDiff.Compare(oldE, newE, d, scratch, topGroups: 3);
        t.Equal(20, d.OnlyInOld, "all twenty are counted");
        t.Equal(20, d.OnlyInOldGroupCount, "…in twenty distinct name groups");
        t.Equal(3, d.OnlyInOldGroups.Count, "…of which three are listed");
        t.Equal(17, d.OnlyInOldElidedGroups, "…and seventeen groups are declared elided");
        t.Equal(17, d.OnlyInOldElidedSegments, "…holding seventeen segments, also declared");
        string elided = WallCommitDiff.Format(d, "atomic vs sliced", 20, 0);
        t.True(elided.Contains("NOT LISTED (a cap, not an absence)"),
            "the cap names itself as a cap");
        t.True(elided.Contains("20 segment(s) in 20 name group(s)"),
            "…and the WHOLE population is stated before the capped list");
        // With no cap in play the line must say so, rather than leaving the reader guessing.
        WallCommitDiff.Compare(oldE, newE, d, scratch, topGroups: 64);
        t.Equal(0, d.OnlyInOldElidedGroups, "a cap wide enough to hold everything elides nothing");
        t.True(WallCommitDiff.Format(d, "atomic vs sliced", 20, 0).Contains("every group listed"),
            "…and the line states that it listed everything");

        // ---- COST, ON A TABLE THE SIZE THE HARDWARE LOG REPORTS ------------------------------
        // MEASURE IT, DO NOT ASSUME IT. The design note estimates 0.05-0.2 ms; the number that
        // matters is the one this machine produces on a table of the logged size. The assertion
        // is a loose CEILING (a CI box under load is not a Quest 3) — the value is PRINTED, and
        // the printed value is what the report quotes.
        t.Case("walldiff/cost-at-logged-table-size");
        WallCommitDiff.TableFacts big1 = BuildBigTable(LoggedSegments, LoggedOwnedPerSegment);
        WallCommitDiff.TableFacts big2 = BuildBigTable(LoggedSegments, LoggedOwnedPerSegment);
        WallCommitDiff.Compare(big1, big2, d, scratch, topGroups: 8); // warm the pools
        t.True(d.FoundNothing, "the big synthetic pair is identical, so the timing is the null path");
        int owned = LoggedSegments * LoggedOwnedPerSegment;
        t.Equal(owned, d.OwnedInOld, "the synthetic table really is the logged size");
        double perCall = TimeGate(big1, big2, d, scratch);
        WallCommitDiff.TableFacts huge1 =
            BuildBigTable(LoggedSegments * OversizeFactor, LoggedOwnedPerSegment);
        WallCommitDiff.TableFacts huge2 =
            BuildBigTable(LoggedSegments * OversizeFactor, LoggedOwnedPerSegment);
        WallCommitDiff.Compare(huge1, huge2, d, scratch, topGroups: 8);
        double perCallBig = TimeGate(huge1, huge2, d, scratch);
        Console.WriteLine(
            $"      walldiff cost: {perCall:F3} ms per gate run over {LoggedSegments} segments / "
            + $"{owned} owned renderers (the top of the logged range), and {perCallBig:F3} ms at "
            + $"{OversizeFactor}x that. The design note estimated 0.05-0.2 ms. THIS IS A DESKTOP "
            + "CI BOX, NOT THE QUEST 3's CPU: read the ratio between the two runs, which is a "
            + "property of the algorithm, and treat the absolute figures as an order of "
            + "magnitude that no headset has confirmed.");
        t.True(perCall < 10.0,
            $"the gate must not be a stall in its own right (measured {perCall:F3} ms)");

        // And the same table with ONE planted difference, to prove the cost case is not timing
        // an early-out: a gate that is fast because it stops looking is not fast.
        t.Case("walldiff/cost-case-is-not-an-early-out");
        WallCommitDiff.TableFacts big3 = BuildBigTable(LoggedSegments, LoggedOwnedPerSegment);
        First(big3).RoomIndex = 999;
        WallCommitDiff.Compare(big1, big3, d, scratch, topGroups: 8);
        t.Equal(1, d.FieldDiffs[WallCommitDiff.FieldRoomIndex],
            "the same big table with one field moved is caught, so the walk is complete");
        t.Equal(LoggedSegments - 1, d.Identical, "…and every other segment still compares equal");
    }

    // ---- builders ---------------------------------------------------------------------------

    /// <summary>Instance ids are arbitrary but must never be 0 — this file's contract is that 0
    /// means "no object", and a builder that handed out 0 would silently test the wrong thing.</summary>
    private static int AnchorId(int n) => 1000 + n;

    private static WallCommitDiff.SegmentFacts First(WallCommitDiff.TableFacts t) =>
        (WallCommitDiff.SegmentFacts)t.Segments[0];

    private static bool AllFrom(
        System.Collections.Generic.List<WallCommitDiff.OwnedMove> moves, int anchorId)
    {
        for (int i = 0; i < moves.Count; i++)
        {
            if (moves[i].FromAnchorId != anchorId || moves[i].ToAnchorId != 0)
                return false;
        }
        return moves.Count > 0;
    }

    /// <summary>A table of <paramref name="count"/> segments, each owning one renderer in every
    /// one of the seven classes, each with a distinct anchor name so the grouping and elision
    /// arithmetic has something to group.</summary>
    private static WallCommitDiff.TableFacts BuildTable(int count, float seedFade)
    {
        var t = new WallCommitDiff.TableFacts();
        for (int i = 1; i <= count; i++)
        {
            WallCommitDiff.SegmentFacts s = t.Rent();
            s.AnchorId = AnchorId(i);
            s.Name = "Wall " + i;
            s.Fade = seedFade;
            s.HasBlock = seedFade > 0f;
            s.RoomIndex = i % 3;
            s.HasBounds = true;
            s.Bounds = new Bounds(new Vector3(i, 0f, 0f), Vector3.one);
            s.HeldCutoff = 0.5f;
            s.WireKey = (uint)(0xA000_0000u + i);
            for (int k = 0; k < WallCommitDiff.KindCount; k++)
                s.Owned[k].Add(10_000 * (k + 1) + i);
            s.Seal();
        }
        return t;
    }

    /// <summary>One segment owning <paramref name="ids"/> in exactly one class — the shape the
    /// case-3, case-4 and class-coverage controls need, with nothing else moving.</summary>
    private static WallCommitDiff.TableFacts OneSegment(int anchor, int kind, int[] ids)
    {
        var t = new WallCommitDiff.TableFacts();
        WallCommitDiff.SegmentFacts s = t.Rent();
        s.AnchorId = AnchorId(anchor);
        s.Name = "Wall " + anchor;
        s.RoomIndex = 0;
        s.HeldCutoff = 0.5f;
        foreach (int id in ids)
            s.Owned[kind].Add(id);
        s.Seal();
        return t;
    }

    private static WallCommitDiff.TableFacts TwoSegments(
        int anchorA, int[] idsA, int anchorB, int[] idsB)
    {
        var t = new WallCommitDiff.TableFacts();
        AddSeg(t, anchorA, idsA);
        AddSeg(t, anchorB, idsB);
        return t;
    }

    private static void AddSeg(WallCommitDiff.TableFacts t, int anchor, int[] ids)
    {
        WallCommitDiff.SegmentFacts s = t.Rent();
        s.AnchorId = AnchorId(anchor);
        s.Name = "Wall " + anchor;
        s.RoomIndex = 0;
        s.HeldCutoff = 0.5f;
        foreach (int id in ids)
            s.Owned[WallCommitDiff.KindRenderers].Add(id);
        s.Seal();
    }

    /// <summary>
    /// The logged table size. <paramref name="ownedPerSegment"/> is the TOTAL across all seven
    /// classes, spread round-robin over them — which is the shape the real table has (a wall
    /// owns a dozen renderers, a couple of foliage pieces and one or two mounted props, not
    /// fifteen of each). Getting that wrong is how the first draft of the cost case timed a
    /// population seven times larger than the one that exists and reported comfortable headroom
    /// from it.
    /// </summary>
    private static WallCommitDiff.TableFacts BuildBigTable(int segments, int ownedPerSegment)
    {
        var t = new WallCommitDiff.TableFacts();
        for (int i = 1; i <= segments; i++)
        {
            WallCommitDiff.SegmentFacts s = t.Rent();
            s.AnchorId = AnchorId(i);
            s.Name = "Wall " + (i % 12); // a realistic number of distinct names, not one each
            s.Fade = (i % 7) * 0.1f;
            s.HasBlock = (i % 3) == 0;
            s.RoomIndex = i % 9;
            s.HasBounds = true;
            s.Bounds = new Bounds(new Vector3(i, i * 0.5f, i * 0.25f), Vector3.one * 2f);
            s.HeldCutoff = 0.5f;
            s.WireKey = (uint)(0xB000_0000u + i);
            for (int j = 0; j < ownedPerSegment; j++)
            {
                int k = j % WallCommitDiff.KindCount;
                s.Owned[k].Add(1_000_000 * (k + 1) + i * 100 + j);
            }
            s.Seal();
        }
        return t;
    }

    /// <summary>Time one gate run, averaged. Separate from the assertions so the two table sizes
    /// are measured by identical code.</summary>
    private static double TimeGate(
        WallCommitDiff.TableFacts a, WallCommitDiff.TableFacts b,
        WallCommitDiff.Diff d, WallCommitDiff.Scratch scratch)
    {
        const int Reps = 200;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Reps; i++)
            WallCommitDiff.Compare(a, b, d, scratch, topGroups: 8);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / Reps;
    }

    /// <summary>The next representable float above <paramref name="x"/>. Written out because
    /// <c>MathF.BitIncrement</c> is not available on every target this suite has been built
    /// for, and because a hand-rolled "x + tiny" would not actually be one ULP.</summary>
    private static float BitIncrement(float x)
    {
        int bits = BitConverter.SingleToInt32Bits(x);
        return BitConverter.Int32BitsToSingle(x >= 0f ? bits + 1 : bits - 1);
    }
}
