using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE SOLE-OCCLUDER RULE — "die Wand reagiert nicht direkt auf Verdeckung".
///
/// <para><b>THE REPORT.</b> User, 2026-09-07, about <c>hauswand2.mp4</c>, verbatim: <i>"Dein
/// Title 'Wall never fades' stimmt nicht, es faded - aber im gegensatz zu den anderen Wänden
/// erst wenn man super nah rangeht oder aus einem winkel in dem fast die ganze map verdeckt ist.
/// Das Problem das ich aber habe ist das teile der map sichtbar sind aber die 'Sackgasse' (auf
/// dem Video zu sehen) verdeckt bleibt. Die Wand reagiert also (anders als die anderen Wände)
/// nicht direkt auf verdeckung. Eventuell hat das mit der Position zu tun."</i></para>
///
/// <para><b>WHAT THE ROOM RULE MEASURES, AND WHY HIS SENTENCE IS THE DIAGNOSIS.</b>
/// <c>BlockedFraction</c> is <c>blocked hexes / ALL playable hexes of the room</c>, capped in
/// cells since ModBuild 468. A wall that seals a three-hex dead end completely scores 3/24 = 0.13
/// however completely it seals it; a wall lying across the room's long axis scores 0.87 while
/// hiding floor the player can walk around. Both numbers are about the ROOM. Neither is about
/// what THAT wall hides. "Eventuell hat das mit der Position zu tun" is exactly right and it is
/// not a defect in one wall — it is what the metric measures.</para>
///
/// <para><b>THE RULE.</b> Count the blocked cells of which this segment is the ONLY occluder in
/// the whole segment table — the hexes that would become visible if, and only if, THIS wall
/// faded. Fade at <see cref="WallFadeTuning.ExclusiveCellBar"/> of them, whatever the room's
/// size. That is his sentence turned into arithmetic: a wall whose hidden hexes are hidden by
/// somebody else as well changes nothing when it fades, and a wall that is the sole reason three
/// playable hexes cannot be seen is in the way.</para>
///
/// <para><b>WHY NOT THE SHADOW-FRACTION RULE THE ROUND WAS BRIEFED TO SHIP.</b> That design
/// replaced the denominator with the segment's own 2D shadow — the playable hexes lying behind
/// its footprint from the head — so a wall sealing a dead end would read ~1.00. It was measured
/// against the ModBuild 468 log before a line of it was written, and the log falsifies it. The
/// 468 <c>PER-WALL</c> rows carry per-cell attribution (<c>cells #4,#5,#6,#7,#8</c>) and the
/// <c>OCCLUDER VERDICTS</c> line above each of them carries the head; 24 hex positions fitted to
/// those 6 720 blocked/not-blocked decisions reproduce 97.2 % of them from a PURE 2D shadow
/// predicate. So in this scenario the 3D ray test IS the 2D shadow: the shadow set equals the
/// blocked set exactly on 98 of 137 wall-passes and differs by at most one hex on 114 of them.
/// The shadow FRACTION is therefore 1.00 for 72 of the 88 wall-passes that sit under today's
/// bar — including 'Wall 3' hiding two hexes and 'Wall 4' hiding one — and a
/// <c>frac ≥ 0.80</c> rule would have added 73 wall-passes across SIX walls, which is the
/// regression the user has forbidden twice. Every guard that trims it back down is a guard on the
/// ABSOLUTE CELL COUNT, which is what ModBuild 468's cap already is. The fraction term carries no
/// information in this scenario and the round would have shipped a second copy of the cap.</para>
///
/// <para><b>THE EVIDENCE FOR THIS RULE, FROM THE SAME LOG AND WITHOUT ANY RECONSTRUCTION.</b>
/// The exclusive count is computable directly from the 468 <c>PER-WALL</c> cell sets. Over its
/// 35 paired passes, of the wall-passes the 6-cell room rule does NOT already carry:</para>
/// <list type="bullet">
///   <item><c>blocked ≥ 3</c> would add 43 of them, spread over 'Wall 3' (14), 'Wall 7' (22),
///   'Wall 6' (3), 'Wall 8' (2) and 'Wall 5' (2) — a mass regression;</item>
///   <item><c>exclusive ≥ 3</c> adds 21, and every single one is 'Wall 7' — the timber-framed
///   house of <c>hauswand2.mp4</c>, at <c>xz[-7.6..-3.8][-0.5..1.7]</c>, whose exclusive set is
///   the SAME three cells <c>#5,#6,#7</c> on all 21 of them;</item>
///   <item><c>exclusive ≥ 4</c> adds nothing at all: every wall-pass with four exclusive cells
///   is already carried by the room rule.</item>
/// </list>
/// <para>No other segment in that log reaches three exclusive cells while under the bar (the
/// highest is two, 'Wall 6' twice). The bar therefore has a full cell of margin on the evidence
/// that exists, in both directions. The fitted positions of <c>#5,#6,#7</c> — (-4.5,2.3),
/// (-5.1,1.8), (-4.7,2.5) — put them in a three-hex pocket immediately behind 'Wall 7' and closed
/// on its west side by 'Wall 8' <c>xz[-7.8..-6.2][0.7..3.8]</c>, which is the cul-de-sac in the
/// video; and the video independently confirms the pocket is BEHIND the wall from his viewpoint
/// and about two to three hexes wide.</para>
///
/// <para><b>NO REGRESSION IS POSSIBLE, AND THAT IS A PROOF AND NOT A SAMPLE.</b> The verdict is
/// <c>roomRule || exclusiveRule</c>. With <see cref="WallFadeTuning.ExclusiveCellBar"/> at 0, or
/// on any pass where the exclusive rule reads false, every statement executed is the ModBuild 468
/// one, bit for bit. Where it reads true, <c>raw</c> is true where it might have been false.
/// <c>OcclusionFade.StepDwell</c> latches on a run of <c>raw == true</c> lasting the enter dwell,
/// and adding trues can only LENGTHEN such a run, never shorten one; and <c>seg.State</c> turning
/// true feeds the room Schmitt its LOW bar, which can only keep it true longer. So the number of
/// walls that stop fading is 0 by construction, not by replay — the same structural argument that
/// carried the 468 cell cap.</para>
///
/// <para><b>THE ONE-TICK LAG IS DELIBERATE.</b> Ownership is accumulated as each segment is
/// measured and read on the NEXT pass, because the segment being decided is somewhere in the
/// middle of the table and the segments after it have not been measured yet. A two-pass loop
/// would remove the lag and cost a second walk of the table; the lag it removes is ONE
/// EVALUATION — the map rotates only on passes that evaluate, so it is exactly one step of the
/// same EMA the room rule advances, and the enter dwell then wants several more of them before
/// anything latches. The instrument prints the lag's own size in frames (<c>xlag</c>) so this
/// sentence is falsifiable rather than merely asserted: at the ModBuild 468 cadence it should
/// read somewhere around nine frames, and a much larger number means the decision cadence, not
/// this rule, is what to look at.</para>
///
/// <para><b>WHY OWNERSHIP IS COUNTED OVER EVERY SEGMENT AND NOT OVER THE SOLID ONES.</b> Counting
/// only the segments that are currently solid would make each wall's verdict a function of its
/// neighbours' verdicts: two walls hiding the same hex would both read exclusive 0, and the
/// moment one faded the other's count would jump and drag it down too — a cascade, and a rule
/// whose own output is one of its inputs. The count here is a pure function of geometry and the
/// head, identical whatever any wall's fade state is.</para>
///
/// <para><b>THE KNOWN OVER-REPORT, NAMED RATHER THAN HIDDEN.</b> A segment held solid by a
/// fail-safe or a standing ruling — a doorway, a gate column, an engulfing wall, a segment with
/// no bounds or no room grid — never reaches <c>BlockedFraction</c>, so it contributes nothing to
/// the ownership map and a wall sharing its cells with only such a segment reads them as
/// exclusive. That over-reports: fading the wall would not in fact reveal a hex the archway is
/// still hiding. It is left as it is for two reasons. Measuring the held population would mean
/// running the ray loop for segments whose verdict is decreed, which is work spent to change
/// nothing on every board where the case does not arise; and the error is in the ADD direction
/// only, so it cannot un-fade anything. Its size in the ModBuild 468 log is zero: the exclusive
/// counts come out Wall-7-only whether the two door segments are in the union or out of it, and
/// the <c>excl</c> field this build ships prints the count per wall so the next log answers it
/// per scenario instead of by argument.</para>
///
/// <para><b>COST.</b> Two int arrays the size of the sample table (24 entries on the reported
/// board, 96 at the mod's whole budget), one array write per blocked cell on a path that already
/// appends that cell to a list, and one array read per blocked cell in the decision. No ray work
/// and no allocation after the first frame. The <c>TICK BUDGET</c> instrument's Decide phase is
/// what would show it.</para>
///
/// <para>MULTIPLAYER: local, presentation-only, no wire field. Every client decides its own
/// walls from its own head exactly as before, and a fade this rule causes is broadcast through
/// the ordinary wire record 17 path with no change — a FLAT (unmodded) player is unaffected
/// because nothing here touches game state.</para>
///
/// <para>WHY A SEPARATE FILE: so it cannot collide with concurrent work on the other parts. It
/// declares no static field, so <c>check-partial-order.py</c>'s hazard cannot arise here.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>Written by the pass in flight; read by the next one. Index is the GLOBAL
        /// sample index (<c>RoomSampleStart[room] + cell</c>), so segments in different rooms can
        /// never collide.</summary>
        private int[] _occCountNext = System.Array.Empty<int>();
        private Segment?[] _occOwnerNext = System.Array.Empty<Segment?>();
        /// <summary>The previous pass's finished ownership map — what the decision reads.</summary>
        private int[] _occCount = System.Array.Empty<int>();
        private Segment?[] _occOwner = System.Array.Empty<Segment?>();
        /// <summary>Sample-table size the two live arrays describe. A rescan that resizes the
        /// table invalidates them: a stale map indexed by a NEW cell numbering would attribute one
        /// wall's hexes to another, which is the worst thing this file could do quietly.</summary>
        private int _occSamples = -1;
        /// <summary>The committed table the map is indexed against. The sample COUNT alone is not
        /// enough: <c>_live</c> is one of two buffers that swap on every commit, and a rescan that
        /// renumbers the cells while keeping their count would leave a map that looks valid and
        /// credits the wrong wall. The table identity moves on every swap, so this catches it.</summary>
        private object? _occTable;
        /// <summary>Does <see cref="_occCount"/> describe a COMPLETE previous pass on the current
        /// table? False for exactly one pass after an invalidation.
        /// <para>It has to be a flag and not "the array is all zeros": a zeroed map makes every
        /// blocked cell read count 0, and the rule below treats count 0 as exclusive, so an
        /// invalidated map would tell every wall in the scenario it is the sole occluder of
        /// everything it hides — a scenario-wide spurious spike on every rescan. With the flag,
        /// an invalid map yields 0 exclusive cells and the rule simply does not fire that
        /// pass.</para></summary>
        private bool _occValid;
        private bool _occAccumulated;
        /// <summary>Frames between the pass that filled <see cref="_occCount"/> and the pass
        /// reading it — normally 1. Printed, not assumed.</summary>
        private int _occLagFrames;
        private int _occFilledFrame = -1;
        /// <summary>How many cells the pass in flight has claimed, for the instrument.</summary>
        private int _occClaims;

        /// <summary>
        /// Start a new ownership accumulation. Called once per decide pass, BEFORE any segment is
        /// measured, and it publishes the pass that just finished as the one the decisions read.
        ///
        /// <para><paramref name="evaluating"/> is the tick's own <c>evaluate</c> gate and it is
        /// load-bearing: a pass that does not evaluate calls <c>BlockedFraction</c> for nothing, so
        /// rotating on it would publish an EMPTY map — and an empty map says "nobody else hides
        /// this" about every cell, which silently degrades the rule into a bare blocked-cell bar.
        /// On such a pass the map and the buffer are both left exactly as they are.</para>
        /// </summary>
        private void BeginOcclusionOwnership(bool evaluating)
        {
            if (!evaluating)
                return;
            int n = _live.AllSamples.Count;
            if (n != _occSamples || !ReferenceEquals(_occTable, _live))
            {
                // A resized or renumbered sample table invalidates every index in the map. Drop it
                // rather than reindex it: one pass with no exclusive verdicts is a missed fade, a
                // mis-indexed map is a WRONG one. The arrays are reused when only the table
                // identity moved, so a commit every couple of seconds allocates nothing.
                if (n != _occSamples)
                {
                    _occCount = new int[n];
                    _occOwner = new Segment?[n];
                    _occCountNext = new int[n];
                    _occOwnerNext = new Segment?[n];
                    _occSamples = n;
                }
                _occTable = _live;
                _occValid = false;
                _occAccumulated = false;
                _occFilledFrame = -1;
                _occLagFrames = 0;
            }
            else
            {
                // Publish the finished pass, then clear the buffer it came from and fill it again.
                int[] c = _occCount; Segment?[] o = _occOwner;
                _occCount = _occCountNext; _occOwner = _occOwnerNext;
                _occCountNext = c; _occOwnerNext = o;
                _occValid = _occAccumulated;
                _occLagFrames = _occFilledFrame >= 0 ? Time.frameCount - _occFilledFrame : 0;
                _occFilledFrame = Time.frameCount;
            }
            _occAccumulated = true;
            System.Array.Clear(_occCountNext, 0, _occCountNext.Length);
            // Owners are cleared with the counts: a stale reference beside a zero count would let
            // a dead segment claim a cell it no longer measures.
            System.Array.Clear(_occOwnerNext, 0, _occOwnerNext.Length);
            _occClaims = 0;
        }

        /// <summary>
        /// Book this segment's freshly measured blocked cells into the pass in flight.
        ///
        /// <para>Called from <see cref="BlockedFraction"/>, which is the ONE place a segment's
        /// own-room cell attribution is produced — so every measured segment contributes exactly
        /// once, including the split-run members <c>EvaluateSplitRuns</c> measures, whose masonry
        /// occludes whether or not it gets to vote.</para>
        ///
        /// <para>THE ROOM IS <see cref="Segment.RoomIndex"/> AND NOT <c>LastDecidingRoom</c>. A
        /// seam wall's alt-room passes run with <c>_attributeCells</c> false, so
        /// <c>LastBlockedCells</c> always describes the OWN room even when the alt room won the
        /// MAX and set <c>LastDecidingRoom</c>. Mapping the cells through the deciding room would
        /// silently offset every one of them into another room's numbering.</para>
        /// </summary>
        private void AccumulateOcclusionOwnership(Segment seg)
        {
            int room = seg.RoomIndex;
            if (room < 0 || room >= _live.RoomSampleStart.Count || seg.LastBlockedCells.Count == 0)
                return;
            int start = _live.RoomSampleStart[room];
            List<int> cells = seg.LastBlockedCells;
            for (int i = 0; i < cells.Count; i++)
            {
                int g = start + cells[i];
                if (g < 0 || g >= _occCountNext.Length)
                    continue;
                int had = _occCountNext[g]++;
                // The owner is meaningful only while the count is 1; past that it is cleared, so
                // "count == 1 && owner == me" cannot be satisfied by a leftover.
                _occOwnerNext[g] = had == 0 ? seg : null;
                _occClaims++;
            }
        }

        /// <summary>
        /// How many of this segment's currently blocked cells NOTHING ELSE blocked on the previous
        /// pass — the hexes that become visible if and only if this wall fades.
        ///
        /// <para>Inside a VALID map a cell whose previous-pass count is 0 counts as exclusive:
        /// the map is one pass old, so a cell this segment has only just started blocking has no
        /// entry yet, and treating "unknown" as "shared" would make every newly hidden hex
        /// invisible to the rule for one pass — a silent stutter at exactly the moment the player
        /// turns. An INVALID map is a different thing entirely and returns 0: see
        /// <see cref="_occValid"/> for why the two cases must not share a convention.</para>
        /// </summary>
        private int ExclusiveBlockedCells(Segment seg)
        {
            int room = seg.RoomIndex;
            if (!_occValid || room < 0 || room >= _live.RoomSampleStart.Count
                || seg.LastBlockedCells.Count == 0)
            {
                return 0;
            }
            int start = _live.RoomSampleStart[room];
            List<int> cells = seg.LastBlockedCells;
            int sole = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int g = start + cells[i];
                if (g < 0 || g >= _occCount.Length)
                    continue;
                int c = _occCount[g];
                if (c == 0 || (c == 1 && ReferenceEquals(_occOwner[g], seg)))
                    sole++;
            }
            return sole;
        }

        /// <summary>
        /// The sole-occluder Schmitt trigger, in CELLS. Returns this segment's own latched verdict
        /// and writes <see cref="Segment.LastExclusive"/> for the instruments.
        ///
        /// <para>THE BAND EDGES SIT HALF A CELL BELOW THE INTEGER BARS ON PURPOSE. An EMA
        /// approaches a constant input from below and reaches it only through float rounding, so
        /// a bar written as <c>≥ 3</c> against a steady reading of exactly 3 takes an unbounded
        /// and unpredictable number of frames to cross — that is why 'Wall 7' sits at
        /// <c>ema0.24 blk6/24</c> for a whole pass beside a 6-cell bar in the 468 log. Enter at
        /// 2.5 and release at 1.5 makes a steady 3 cross promptly and a steady 2 never cross,
        /// with the full cell of Schmitt band the ModBuild 250 group churn showed is needed.</para>
        /// </summary>
        private bool ExclusiveVerdict(Segment seg, float fracStep)
        {
            int bar = WallFadeTuning.ExclusiveCellBar;
            int sole = ExclusiveBlockedCells(seg);
            seg.LastExclusive = sole;
            if (bar <= 0)
            {
                // Dial off: drop the EMA seed too, so switching it back on re-seeds from a live
                // reading instead of resuming a value taken minutes ago.
                seg.ExclusiveSmoothInit = false;
                seg.ExclusiveLatch = false;
                return false;
            }
            OcclusionFade.AdvanceCoverage(sole, fracStep, ref seg.ExclusiveSmooth,
                ref seg.ExclusiveSmoothInit);
            seg.ExclusiveLatch = OcclusionFade.Above(seg.ExclusiveSmooth, seg.ExclusiveLatch,
                bar - 0.5f, bar - 1.5f);
            return seg.ExclusiveLatch;
        }

        /// <summary>The clause the instruments print: which of the two rules is holding this wall
        /// faded. A literal, so naming it costs no allocation on the hot path.</summary>
        private static string CarriedBy(bool room, bool exclusive) =>
            room ? (exclusive ? "BOTH" : "ROOM")
                 : (exclusive ? "EXCLUSIVE" : "-");

        /// <summary>Ownership-map health for the instrument line: the lag in frames, how many
        /// cell claims the last finished pass booked, and the sample table it is indexed by.</summary>
        private string OcclusionOwnershipClause() =>
            $"xlag{_occLagFrames}f over {_occSamples} sample(s), {_occClaims} claim(s) this pass"
            + (_occValid
                ? string.Empty
                : " — MAP INVALID this pass (the sample table was just rebuilt or swapped), so "
                  + "every 'excl' below reads 0 BY DESIGN and not because nothing is exclusive");
    }
}
