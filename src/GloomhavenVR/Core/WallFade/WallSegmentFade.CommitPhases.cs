using System;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PERF S3 (2026-08-23) — THE COMMIT, PRICED PHASE BY PHASE.
///
/// <para>WHAT THE PREVIOUS ROUND LEFT. ModBuild 227 (PERF S2) split the 118 ms
/// <c>WallFade.Rescan</c> frame into three measurable stages and fixed two of them: the scene
/// sweep is now taken rarely and costs 4.19 ms when it is (<c>WallFade.Sweep</c>), and the
/// per-renderer classification is spread over 12–18 frames at 1.5 ms each
/// (<c>WallFade.Classify</c>). The ModBuild 228 hardware log shows both holding. What it also
/// shows is that the third stage did not move: <c>WallFade.Rescan</c> — which since PERF S2
/// covers the COMMIT alone (<c>WallSegmentFade.cs</c>, <c>StepRescanCycle</c>) — reads
/// "83.375ms avg, worst 95.47ms, 41.7ms/s, frames 15" over a 30 s window, i.e. one 82–97 ms
/// frame every two seconds. At 90 Hz that is eight dropped frames, and it is the whole
/// remaining stall.</para>
///
/// <para>WHY THIS FILE EXISTS AND NOT A FIX. <c>RescanCore</c> runs twenty-three distinct
/// phases and the log priced exactly one number for all of them together. This project's
/// standing rule is MEASURE THE OUTCOME, NOT THE PATH, and it has twice shipped an
/// optimisation for a phase it had never actually priced (see the memory entries "Measure the
/// picture, not the state" and "One step too early"). So the commit is instrumented BEFORE it
/// is sliced: every phase gets its own <see cref="PerfMonitor"/> scope
/// (<c>WallFade.Commit.*</c>, which the <c>[Perf] STEPS</c> / <c>STEPS TAIL</c> ranking picks
/// up on its own), and the class's own BUDGET line names the three most expensive phases with
/// their numbers — so the attribution survives even with <c>[Perf] Enabled = false</c>, which
/// is how most capture sessions run.</para>
///
/// <para>WHY BOTH INSTRUMENTS. <c>PerfMonitor</c> answers "how much of the session did this
/// cost" (window totals, ranked against every other mod step). The BUDGET clause answers "what
/// was the WORST SINGLE CYCLE made of" — which is the question a 90 Hz stall actually asks,
/// because a phase that costs 40 ms once every two seconds and a phase that costs 2 ms forty
/// times look identical in a window total and are completely different defects. The project
/// has been burned by exactly that conflation ("A WORST field is the tail").</para>
///
/// <para>COST. Two <see cref="System.Diagnostics.Stopwatch"/> timestamp reads and two array
/// writes per phase — twenty-three phases, so ~50 timestamp reads per commit, well under 5 µs
/// against a commit that currently costs 85 ms. No allocation: the scope is a readonly struct
/// used through <c>using</c> (so the compiler calls <c>Dispose</c> directly, no box), the
/// accumulators are arrays allocated once, and the breakdown clause is built into the same
/// <see cref="System.Text.StringBuilder"/> the budget line already uses.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver : MonoBehaviour
    {
        /// <summary>
        /// The phases of <c>RescanCore</c>, in execution order. The order is load-bearing in
        /// the commit itself (every one of those orderings is a user ruling or a defect fix —
        /// see the comments at each call site); here it only fixes the array indices, so a
        /// phase added to the commit must be added to BOTH this enum and
        /// <see cref="CommitPhaseNames"/> at the matching position.
        /// </summary>
        private enum CommitPhase
        {
            Figures = 0,
            TileAnchors,
            RoomRegistry,
            DeadSegments,
            WallCache,
            Doors,
            GateSeed,
            Adopt,
            Water,
            Samples,
            Rooms,
            Ground,
            Engulf,
            Ground2,
            Stacked,
            FreeStanding,
            PropUnits,
            Siblings,
            Mounted,
            WireKeys,
            GateLift,
            GateBounds,
            StandCensus,
            UnitCensus,
            BoardVolume,

            /// <summary>SENTINEL, ALWAYS LAST — the array length, so appending a phase cannot
            /// forget to widen the arrays that index by this enum. Its sibling in
            /// WallSegmentFade.CommitPhases.cs did not have one, the length was written by hand as
            /// "the member that was last when I wrote this line", a later build appended past it,
            /// and ModBuild 441 shipped an IndexOutOfRangeException that aborted the rescan and
            /// stopped every wall in the game from fading. Correct here today; a sentinel is what
            /// keeps it correct tomorrow.</summary>
            Count,
        }

        private const int CommitPhaseCount = (int)CommitPhase.Count;

        /// <summary>Short names for the BUDGET clause. Kept in enum order (see
        /// <see cref="CommitPhase"/>) — index IS the enum value.</summary>
        private static readonly string[] CommitPhaseNames =
        {
            "Figures", "TileAnchors", "RoomRegistry", "DeadSegments", "WallCache", "Doors",
            "GateSeed", "Adopt", "Water", "Samples", "Rooms", "Ground", "Engulf", "Ground2",
            "Stacked", "FreeStanding", "PropUnits", "Siblings", "Mounted", "WireKeys", "GateLift",
            "GateBounds",
            "StandCensus", "UnitCensus", "BoardVolume",
        };

        /// <summary>Full <see cref="PerfMonitor"/> step names, built once at type load so the
        /// hot path never concatenates a string. These are what <c>[Perf] STEPS</c> and
        /// <c>[Perf] STEPS TAIL</c> rank and what the integrator greps: the nesting under the
        /// existing (load-bearing, see <c>StepRescanCycle</c>) <c>WallFade.Rescan</c> scope is
        /// expressed in the NAME, because PerfMonitor's ranking is flat.</summary>
        private static readonly string[] CommitPhaseScopeNames = BuildCommitPhaseScopeNames();

        /// <summary>ModBuild 443: index-guarded, and deliberately. This runs in a STATIC
        /// INITIALISER, so a name list one entry short would throw inside the type constructor
        /// and take the whole class down with a TypeInitializationException that names no phase
        /// — the ModBuild 442 failure mode with a worse stack. A placeholder here turns that
        /// into a wrong label, and <see cref="CheckCommitPhaseNames"/> then says so out loud on
        /// the first BUDGET line.</summary>
        private static string[] BuildCommitPhaseScopeNames()
        {
            var names = new string[CommitPhaseCount];
            for (int i = 0; i < CommitPhaseCount; i++)
            {
                names[i] = "WallFade.Commit."
                         + (i < CommitPhaseNames.Length ? CommitPhaseNames[i] : "Phase" + i);
            }
            return names;
        }

        private static bool _commitPhaseNamesChecked;

        /// <summary>The sibling of the MOUNTED STAGES name check, for the same reason: this list
        /// and the enum beside it are two hand-ordered lists of one thing, and ModBuild 441
        /// shipped the other one merely REORDERED — correct numbers under wrong names, with
        /// nothing to notice. Checked once, at a tier the default log prints.</summary>
        private static void CheckCommitPhaseNames()
        {
            if (_commitPhaseNamesChecked)
                return;
            _commitPhaseNamesChecked = true;
            if (CommitPhaseNames.Length == CommitPhaseCount)
                return;
            // HW-VERIFY
            VRLog.Alert(Name, "COMMIT PHASES name list is " + CommitPhaseNames.Length
                              + " entry(s) against " + CommitPhaseCount + " phase(s) — the "
                              + "breakdown and every 'WallFade.Commit.*' step name are "
                              + "MISLABELLED from the first mismatch on. Add the name beside the "
                              + "enum member, in enum order.");
        }

        /// <summary>One phase's label, never an out-of-range index — see
        /// <see cref="CheckCommitPhaseNames"/>, which is what says the label is wrong.</summary>
        private static string CommitPhaseLabel(int index) =>
            index >= 0 && index < CommitPhaseNames.Length ? CommitPhaseNames[index]
                                                          : "Phase" + index;

        /// <summary>Worst SINGLE-CYCLE cost of each phase since the last BUDGET line (ms).</summary>
        private readonly float[] _phaseWorstMillis = new float[CommitPhaseCount];

        /// <summary>Total cost of each phase since the last BUDGET line (ms), across all
        /// cycles in the window — the number that says whether a phase is expensive once or
        /// cheap-but-constant.</summary>
        private readonly float[] _phaseTotalMillis = new float[CommitPhaseCount];

        /// <summary>Per-cycle accumulator: a phase may be entered more than once in one commit
        /// (<see cref="CommitPhase.Ground"/> / <see cref="CommitPhase.Ground2"/> are separate
        /// entries on purpose, but a future phase might not be), so the WORST-cycle figure is
        /// taken from this and not from a single scope's duration.</summary>
        private readonly float[] _phaseCycleMillis = new float[CommitPhaseCount];

        /// <summary>Clear the per-cycle accumulators at the top of one commit. Called by
        /// <c>RescanCore</c> before its first phase.</summary>
        private void BeginCommitPhases()
        {
            Array.Clear(_phaseCycleMillis, 0, CommitPhaseCount);
        }

        /// <summary>Fold one finished commit's per-cycle costs into the window accumulators.
        /// Called by <c>RescanCore</c> after its last phase — in a <c>finally</c>, so a phase
        /// that throws still leaves the window arithmetic consistent.</summary>
        private void EndCommitPhases()
        {
            // Close the per-node fact window with the commit itself: those memos are only ever
            // valid for one synchronous pass, and the WALL-PATH AUDIT reaches the same walk
            // from the heartbeat afterwards. See _nodeRendererCount in
            // WallSegmentFade.PropUnit.cs.
            EndNodeFactMemos();
            // PERF S4 — WHICH PHASE OWNED THE WORST COMMIT FRAME. The three-phase breakdown
            // below answers "what is the commit made of over a window"; this answers "what was
            // the single frame the player felt", which is a different question whenever one
            // cycle is unlike its neighbours (the ModBuild 271 log has WallCache worst in four
            // windows and PropUnits worst in a fifth). Taken over THIS cycle's totals, and kept
            // only if this cycle is the window's most expensive commit — so the phase named is
            // always the one inside the frame whose number the line prints.
            float cycleTotal = 0f;
            int worstPhase = -1;
            float worstPhaseMs = 0f;
            for (int i = 0; i < CommitPhaseCount; i++)
            {
                float ms = _phaseCycleMillis[i];
                _phaseTotalMillis[i] += ms;
                if (ms > _phaseWorstMillis[i])
                    _phaseWorstMillis[i] = ms;
                cycleTotal += ms;
                if (ms > worstPhaseMs)
                {
                    worstPhaseMs = ms;
                    worstPhase = i;
                }
            }
            if (cycleTotal >= _windowWorstCommitTotalMillis)
            {
                _windowWorstCommitTotalMillis = cycleTotal;
                _cycleWorstCommitPhase = worstPhase;
                _cycleWorstCommitPhaseMillis = worstPhaseMs;
            }
        }

        /// <summary>The largest per-cycle phase total seen since the last budget line — the
        /// selector for <see cref="_cycleWorstCommitPhase"/>, and nothing else. Reset with every
        /// other counter that line owns.</summary>
        private float _windowWorstCommitTotalMillis;

        /// <summary>Name of the phase that owned the worst commit frame this window, or a plain
        /// statement that no commit completed. Never a constant: a change-gated reason string
        /// prints once and then reads as a dead instrument.</summary>
        private string WorstCommitPhaseName =>
            _cycleWorstCommitPhase >= 0 ? CommitPhaseLabel(_cycleWorstCommitPhase) : "none yet";

        /// <summary>Open a measured phase. Use as <c>using (Phase(CommitPhase.X))</c> — the
        /// struct is disposed by the compiler without boxing.</summary>
        private CommitPhaseScope Phase(CommitPhase phase) => new(this, (int)phase);

        /// <summary>
        /// One phase's measurement: a <see cref="PerfMonitor"/> scope (for the session-wide
        /// ranking, active only when <c>[Perf] Attribution</c> is on) PLUS an unconditional
        /// stopwatch read into the driver's own accumulators (for the BUDGET clause, which
        /// must print whatever the perf monitor's configuration is — the class's one
        /// attributable line does not get to depend on a toggle).
        /// </summary>
        private readonly struct CommitPhaseScope : IDisposable
        {
            private readonly FadeDriver _driver;
            private readonly int _index;
            private readonly float _startMillis;
            private readonly PerfMonitor.Measure _measure;

            internal CommitPhaseScope(FadeDriver driver, int index)
            {
                _driver = driver;
                // ModBuild 443 — range-guarded for the reason MountedMark is: a throw in here
                // aborts RescanCore and stops every wall in the game from fading. An index
                // outside the arrays costs this phase its measurement and nothing else.
                bool ok = (uint)index < (uint)CommitPhaseCount;
                if (!ok)
                    NoteStageIndexOutOfRange("COMMIT PHASES", index, CommitPhaseCount);
                _index = ok ? index : -1;
                _measure = PerfMonitor.Scope(ok ? CommitPhaseScopeNames[index]
                                                : "WallFade.Commit.<out-of-range>");
                _startMillis = (float)RescanClock.Elapsed.TotalMilliseconds;
            }

            public void Dispose()
            {
                float ms = (float)RescanClock.Elapsed.TotalMilliseconds - _startMillis;
                _measure.Dispose();
                if (_index >= 0)
                    _driver._phaseCycleMillis[_index] += ms;
            }
        }

        /// <summary>
        /// Append the commit's phase breakdown to the BUDGET line: the three most expensive
        /// phases by WORST SINGLE CYCLE (the stall question), each with its window total, plus
        /// the total of everything else so the reader can see whether the top three ARE the
        /// commit or merely its loudest third. Resets the window accumulators — the budget line
        /// owns them, exactly like every other counter it prints.
        /// </summary>
        private void AppendCommitPhaseBreakdown(System.Text.StringBuilder sb)
        {
            int a = -1, b = -1, c = -1;
            float total = 0f;
            for (int i = 0; i < CommitPhaseCount; i++)
            {
                total += _phaseTotalMillis[i];
                float w = _phaseWorstMillis[i];
                if (a < 0 || w > _phaseWorstMillis[a]) { c = b; b = a; a = i; }
                else if (b < 0 || w > _phaseWorstMillis[b]) { c = b; b = i; }
                else if (c < 0 || w > _phaseWorstMillis[c]) { c = i; }
            }

            CheckCommitPhaseNames();
            sb.Append(" COMMIT PHASES (worst single cycle, then window total): ");
            float named = 0f;
            AppendPhase(sb, a, ref named, first: true);
            AppendPhase(sb, b, ref named, first: false);
            AppendPhase(sb, c, ref named, first: false);
            sb.Append(" — the other ").Append(CommitPhaseCount - 3).Append(" phase(s) cost ")
              .Append((total - named).ToString("F1"))
              .Append("ms in the window between them. Every phase also reports as ")
              .Append("'WallFade.Commit.<name>' in [Perf] STEPS / STEPS TAIL.");

            AppendElectionProduct(sb);
            AppendMountedStageBreakdown(sb);
            AppendSigDeltaClause(sb);

            Array.Clear(_phaseWorstMillis, 0, CommitPhaseCount);
            Array.Clear(_phaseTotalMillis, 0, CommitPhaseCount);
        }

        /// <summary>
        /// PERF S7 — THE PRODUCT THE Mounted PHASE IS MADE OF, NAMED AS A PRODUCT.
        ///
        /// <para>WHY THIS FIELD AND NOT ANOTHER MILLISECOND. The ModBuild 437 log priced the
        /// commit phase by phase and Mounted came out worst (82.64 ms / 90.52 ms on the two
        /// expensive commits, under 8 ms on every cheap one) — but a phase total cannot say
        /// WHICH of the two populations inside it moved. Mounted walks candidates, and for each
        /// candidate it walks the segment table; between the cheap and the expensive commits the
        /// candidate count roughly tripled while the segment table went from ~10 rows to ~826
        /// (10 walls and no split runs, versus 34 walls and 792 split-run pieces on the PER-WALL
        /// line, after the "open all doors" cheat at log line 9153). Those two hypotheses —
        /// "more candidates" and "more segments per candidate" — predict a 3x and a ~270x term
        /// respectively and the phase total is compatible with both. This clause prints the
        /// product itself, so the next log settles it without another round.</para>
        ///
        /// <para>HOW TO READ IT AGAINST THE FIX. PERF S7 did not reduce the PAIR COUNT at all —
        /// the loop still visits every row — it made each pair cheap (no UnityEngine.Object null
        /// compare, no RoomDecisionValid, no Bounds property recomputation, no dictionary
        /// enumerator). So PAIRS/cycle should read the SAME as it would have before, and
        /// Mounted's worst-single-cycle number should fall while it does. If PAIRS/cycle is
        /// small (tens of thousands) and Mounted is still ~90 ms, the product is NOT the cost
        /// and the next lane must look at the per-candidate prologue instead — that is the
        /// falsifier, and it is the reason the raw count is printed beside the millisecond.</para>
        /// </summary>
        private void AppendElectionProduct(System.Text.StringBuilder sb)
        {
            int cycles = _cycleCount > 0 ? _cycleCount : 1;
            sb.Append(" MOUNTED ELECTION (PERF S7 — the product, not a millisecond): ")
              .Append(_electWindow[ElectWindowCandidates])
              .Append(" candidate(s) reached the nearest-wall election in this window and walked ")
              .Append(_electWindow[ElectWindowPairs])
              .Append(" (candidate, segment) pair(s) between them, over a segment table that ")
              .Append("held ").Append(_electRowsLast).Append(" row(s) at the last cycle — that row ")
              .Append("count is one per wall AND one per split-run piece, so it is the term the ")
              .Append("PER-WALL line's SPLIT RUN(s) drive. Per rescan cycle in this window ")
              .Append("(committing or skipped alike) that is ~")
              .Append((_electWindow[ElectWindowPairs] / cycles).ToString())
              .Append(" pair(s). PERF S7 made each pair cheap and did NOT reduce their number, ")
              .Append("so this count staying put while Commit.Mounted falls is the fix working; ")
              .Append("this count being SMALL while Mounted stays ~90ms falsifies the product as ")
              .Append("the cause and points at the per-candidate prologue instead.");

            // ModBuild 440 — THE SAME PRODUCT IN THE TWO LANES THE 439 SLICE ACTUALLY INDICTED.
            // Riders 40-54 ms and Union 19-20 ms against 12 ms for the whole sweep, and both of
            // them walked _live.Segments.Values ONCE PER CANDIDATE with a UnityEngine.Object null
            // compare on the row — the exact shape PERF S7 removed from the election in 438, in
            // two lanes written after it. These counts are printed for the same reason that one
            // is: they do NOT fall when the walk is flattened, so a count that stays put while
            // the millisecond falls is the fix working, and a count that is small while the
            // millisecond stays is this hypothesis falsified in its own words.
            sb.Append(" HANGING/UNION PRODUCTS (ModBuild 440): the hanging-plant lane walked ")
              .Append(_electWindow[HangingWindowPairs]).Append(" (candidate, segment) pair(s) ")
              .Append("over ").Append(_electWindow[HangingWindowCandidates])
              .Append(" candidate(s), and the union-overlap lane ")
              .Append(_electWindow[UnionWindowPairs]).Append(" pair(s) over ")
              .Append(_electWindow[UnionWindowCandidates])
              .Append(" dressing renderer(s), over the same ").Append(_electRowsLast)
              .Append("-row table. Read them against MOUNTED STAGES' HangingPlants and Union ")
              .Append("figures below.");

            Array.Clear(_electWindow, 0, _electWindow.Length);
            _electRowsLast = 0;
        }

        // =================================================================================
        // ModBuild 439 — INSIDE Commit.Mounted, BECAUSE THE PRODUCT HYPOTHESIS WAS FALSIFIED.
        // =================================================================================
        //
        // WHAT PERF S7 PREDICTED AND WHAT THE HARDWARE RETURNED. ModBuild 438 flattened the
        // nearest-wall election into a sequential array on the theory that Mounted's cost is a
        // PRODUCT — candidates x segment-table rows — and predicted 90.52 ms -> ~20 ms. The
        // ModBuild 438 log's committing windows read Mounted 78.13 / 88.69 / 87.13 / 87.75 /
        // 89.50 ms. It did not move.
        //
        // AND THE FALSIFIER THAT SHIPPED WITH IT ANSWERED, in AppendElectionProduct's own words:
        // one window reports 51 candidates walking 33150 pairs for 88.50 ms. That is 2.7 µs PER
        // PAIR against a post-change estimate of 6-8 ns — five hundred times over. The product
        // was never the cost. 88.5 ms over 51 candidates is ~1.7 ms EACH, spent somewhere that
        // is flat in the pair count, and no shipped instrument can say where: Commit.Mounted is
        // ONE scope around a method that runs eleven distinct passes over three different
        // populations.
        //
        // SO THE PHASE IS SLICED BEFORE IT IS OPTIMISED — the same rule PERF S3 applied to the
        // commit itself one level up, and for the same reason this project keeps re-learning
        // ("One step too early": a flawless measurement of the WRONG STAGE looks exactly like
        // proof). Eleven stamps and one accumulator per stage, plus the ELECTION WALK timed
        // separately INSIDE the sweep so that "the per-candidate prologue" and "the table walk"
        // are two numbers and not one hypothesis. Cost: two clock reads per stage per commit
        // and two per candidate that reaches the election — ~120 reads against an 88 ms phase.
        //
        // WHAT THE NEXT LOG WILL SETTLE. If SWEEP minus ELECTION carries the mass, the cost is
        // the per-candidate prologue and the fix is in the prologue's engine calls. If the
        // stages BEFORE the sweep carry it (Ownership, UnitHomes, Sticky), the cost is
        // proportional to the SEGMENT TABLE and the candidates were never the population that
        // mattered. If it is the CENSUS stage, we have been paying 88 ms to build strings.

        private enum MountedStage
        {
            Ownership = 0,
            UnitHomes,
            WallHomes,
            Park,
            Sticky,
            ElectionIndex,
            Sweep,
            Riders,
            Leavers,
            Union,
            Census,
            /// <summary>ModBuild 440 — split out of the 439 <c>Riders</c> stage, which lumped
            /// <c>CollectFreeStandingRiders</c> and <c>CollectHangingPlants</c> into one number
            /// and so could not say which of the two carried the 40-54 ms that stage reported.
            /// Two lanes, two populations, two figures.</summary>
            FreeRiders,
            HangingPlants,
            /// <summary>A SUBSET of <see cref="Sweep"/>, not a sibling of it — the nearest-wall
            /// table walk alone, the term PERF S7 made cheap. It is a stage of its own so that
            /// SWEEP MINUS ELECTIONWALK is the per-candidate prologue, which is the number the
            /// 438 round could not see. It is deliberately NOT subtracted from Sweep: a stage
            /// whose total silently excluded its own sub-stage would make the eleven figures
            /// stop adding up to the phase, and this project has an entry for a census whose
            /// arithmetic nobody could reproduce.</summary>
            ElectionWalk,

            /// <summary>SENTINEL, ALWAYS LAST — the array length, so adding a stage cannot forget
            /// to widen the three arrays that index by this enum.
            ///
            /// <para><b>THIS EXISTS BECAUSE ITS ABSENCE SHIPPED A CRASH.</b> ModBuild 441 wrote the
            /// length by hand as <c>(int)MountedStage.HangingPlants + 1</c>, naming the member that
            /// was last WHEN THAT LINE WAS WRITTEN. ModBuild 440 had already appended
            /// <c>ElectionWalk</c> after it, so the count was 13 while the enum's largest value was
            /// 13 — one past the end. <c>MountedMark(ElectionWalk, …)</c> threw
            /// <c>IndexOutOfRangeException</c> inside <c>CollectWallMountedProps</c>, the throw
            /// aborted <c>RescanCore</c>, the segment table was therefore never rebuilt, and NO WALL
            /// FADED AT ALL while the retry burned the frame. A hand-kept length beside a list that
            /// grows is the same defect one level up, and this file now takes the length from the
            /// list itself.</para></summary>
            Count,
        }

        private const int MountedStageCount = (int)MountedStage.Count;

        /// <summary>One label per stage, IN ENUM ORDER — index i is <c>(MountedStage)i</c>.
        ///
        /// <para>ModBuild 441 shipped this list in a different order than the enum: it ended
        /// "Census", "ElectionWalk", "FreeRiders", "HangingPlants" while the enum reads Census=10,
        /// FreeRiders=11, HangingPlants=12, ElectionWalk=13. Three of the fourteen figures were
        /// therefore printed under each other's names — a silent wrong answer from an instrument,
        /// which is worse than a missing one, and it rode in on the same commit as the crash above
        /// because both come from maintaining two hand-ordered lists of one thing. The static
        /// check below is what makes them one thing.</para></summary>
        private static readonly string[] MountedStageNames =
        {
            "Ownership", "UnitHomes", "WallHomes", "Park", "Sticky", "ElectionIndex", "Sweep",
            "Riders(RETIRED-always 0)", "Leavers", "Union", "Census",
            "FreeRiders", "HangingPlants", "ElectionWalk(subset of Sweep)",
        };

        /// <summary>Fails loudly at the first tick rather than mislabelling every figure for a
        /// build: a name list one entry short prints an out-of-range index, one entry long prints a
        /// label nothing produces, and either way the reader trusts it. Checked once.</summary>
        private static bool _mountedStageNamesChecked;

        private readonly float[] _mountedStageCycle = new float[MountedStageCount];
        private readonly float[] _mountedStageWorst = new float[MountedStageCount];
        private readonly float[] _mountedStageTotal = new float[MountedStageCount];

        /// <summary>How many of the ~9000 census rows survive each prefilter of the sweep. The
        /// per-candidate cost is a QUOTIENT and this is its denominator: 88 ms over 51
        /// candidates and 88 ms over 3000 are completely different defects, and the shipped
        /// instrument counts only the 51 that reached the election.</summary>
        private int _mountedInReach;
        private int _mountedPastStructural;

        private static float MountedClockMillis() => (float)RescanClock.Elapsed.TotalMilliseconds;

        /// <summary>Open one commit's stage accounting. Called at the top of
        /// <c>CollectWallMountedProps</c>; the return value seeds the stamp chain.</summary>
        private float BeginMountedStages()
        {
            Array.Clear(_mountedStageCycle, 0, MountedStageCount);
            _mountedInReach = 0;
            _mountedPastStructural = 0;
            return MountedClockMillis();
        }

        /// <summary>
        /// Close one stage and open the next: adds the elapsed time to
        /// <paramref name="stage"/> and returns the new stamp. A chain of these costs one clock
        /// read per boundary and cannot double-count, because each call's return IS the next
        /// call's start.
        ///
        /// <para><b>ModBuild 443 — RANGE-GUARDED, AND THIS IS THE LESSON OF 442 RATHER THAN A
        /// STYLE PREFERENCE.</b> The sentinel now makes the array the right LENGTH, which is the
        /// cause 442 fixed. This is about the CONSEQUENCE: the throw happened inside
        /// <c>CollectWallMountedProps</c>, it aborted <c>RescanCore</c>, the segment table was
        /// never rebuilt, and NO WALL IN THE GAME FADED. An instrument may cost a microsecond and
        /// it may print a wrong number, but it must never be able to stop the subsystem it is
        /// measuring. Out of range now loses one stage's milliseconds and says so once.</para>
        /// </summary>
        private float MountedMark(MountedStage stage, float since)
        {
            float now = MountedClockMillis();
            int i = (int)stage;
            if ((uint)i < (uint)MountedStageCount)
                _mountedStageCycle[i] += now - since;
            else
                NoteStageIndexOutOfRange("MOUNTED STAGES", i, MountedStageCount);
            return now;
        }

        private static bool _stageIndexAlerted;

        /// <summary>Say once, at a tier the default log prints, that a phase index was outside
        /// its array — the shape that cost ModBuild 441 an entire build's worth of wall
        /// fading.</summary>
        private static void NoteStageIndexOutOfRange(string which, int index, int count)
        {
            if (_stageIndexAlerted)
                return;
            _stageIndexAlerted = true;
            // HW-VERIFY
            VRLog.Alert(Name, which + " was handed index " + index + " against " + count
                              + " slot(s). The measurement for that stage is LOST and the "
                              + "breakdown below undercounts, but the rescan is not aborted — "
                              + "which is the whole point of the guard (ModBuild 441 threw here "
                              + "and no wall in the game faded). Add the stage to the enum, the "
                              + "name list and nothing else: the arrays size themselves.");
        }

        /// <summary>Fold this commit's stages into the window accumulators. In a
        /// <c>finally</c> at the call site, for the reason <see cref="EndCommitPhases"/> is.
        /// </summary>
        private void EndMountedStages()
        {
            for (int i = 0; i < MountedStageCount; i++)
            {
                float ms = _mountedStageCycle[i];
                _mountedStageTotal[i] += ms;
                if (ms > _mountedStageWorst[i])
                    _mountedStageWorst[i] = ms;
            }
        }

        /// <summary>The stage breakdown, appended to the BUDGET line beside the election
        /// product it is here to explain. Every stage is printed, including the zeroes: a clause
        /// that hides its empty rows is one a reader cannot tell from a stage that never ran.
        /// </summary>
        private void AppendMountedStageBreakdown(System.Text.StringBuilder sb)
        {
            // THE TWO LISTS ARE ONE THING OR THIS LINE LIES. A names array shorter than the enum
            // throws while building a log line; longer, and it prints a label no stage produces.
            // ModBuild 441 shipped it merely REORDERED, which is the quiet case: fourteen correct
            // numbers under three wrong names, and nothing to notice. Checked once, at Alert so it
            // reaches a default-tier log, and the line still prints — a mislabelled breakdown is
            // worth more than no breakdown, provided the reader is told.
            if (!_mountedStageNamesChecked)
            {
                _mountedStageNamesChecked = true;
                if (MountedStageNames.Length != MountedStageCount)
                    // HW-VERIFY
                    VRLog.Alert(Name, "MOUNTED STAGES name list is " + MountedStageNames.Length +
                                      " entry(s) against " + MountedStageCount + " stage(s) — the "
                                      + "breakdown below is MISLABELLED from the first mismatch on. "
                                      + "Add the name beside the enum member, in enum order.");
            }

            sb.Append(" MOUNTED STAGES (ModBuild 439 — worst single cycle / window total, ms; ")
              .Append("the 438 product hypothesis was falsified by the count above, so the phase ")
              .Append("is sliced before anything else is tried): ");
            for (int i = 0; i < MountedStageCount; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(i < MountedStageNames.Length ? MountedStageNames[i] : "stage" + i)
                  .Append(' ')
                  .Append(_mountedStageWorst[i].ToString("F2")).Append('/')
                  .Append(_mountedStageTotal[i].ToString("F1"));
            }
            sb.Append(". ElectionWalk is a SUBSET of Sweep and is listed inside it, so ")
              .Append("SWEEP MINUS ELECTIONWALK IS THE PER-CANDIDATE PROLOGUE — the term the ")
              .Append("438 round could not see. The 439 'Riders' stage is RETIRED and reads 0: ")
              .Append("it lumped two lanes over two populations into one figure and could not ")
              .Append("say which of them carried its 40-54 ms; FreeRiders and HangingPlants are ")
              .Append("those two lanes, measured apart. FUNNEL: ").Append(_mountedInReach)
              .Append(" census row(s) survived the union-reach prefilter and ")
              .Append(_mountedPastStructural)
              .Append(" reached the geometric tests, against the candidate count in the ELECTION ")
              .Append("clause above — the per-candidate cost is a quotient and this is its ")
              .Append("denominator.");

            Array.Clear(_mountedStageWorst, 0, MountedStageCount);
            Array.Clear(_mountedStageTotal, 0, MountedStageCount);
        }

        // =================================================================================
        // ModBuild 439 — WHICH RENDERER MOVES THE SCENE SIGNATURE, AND HOW IT IS AFFORDED.
        // =================================================================================
        //
        // THE USER'S OWN SEPARATION OF THE TWO STALLS (2026-09-05): "Es ist okay dass es mal
        // hängt wenn eine neue Tür geöffnet wird, weil dann verständlicherweise Dinge
        // nachgeladen werden. Aber diese Hänger lange danach die einfach 'random' auftreten
        // sind störend." A reveal commit is accepted. A commit long afterwards is the defect,
        // and in his ModBuild 438 log every single one of those is the SCENE SIGNATURE term:
        // past log line 11147 the WHY A CYCLE COMMITTED counters read 0/0/0/0/0, 2 scene
        // signature moved, 0 wall, 0 ceiling, 0 drift, in window after window.
        //
        // WHAT COULD NOT SAY WHICH. The refusal prints two 64-bit pairs and a renderer count
        // and stops there; SIGNATURE CULPRITS is the instrument for the rest and it is OFF by a
        // deliberate ModBuild 284 decision, so it printed ZERO times in that log (every one of
        // the 107 "CULPRITS" matches is the BUDGET line quoting the census's own name inside
        // its prose — the trap this project files under "a token quoted in its own
        // explanation"). Turning it back on costs ~9000 GetInstanceID() interop calls and ~9000
        // dictionary inserts ON THE COMMIT FRAME, which is the frame this whole exercise exists
        // to shrink.
        //
        // WHAT IS DONE INSTEAD, AND WHY IT IS CHEAPER THAN THE CENSUS IT DOES NOT REPLACE.
        // TWO instruments, and the second exists to check the first:
        //
        //  (1) THE ARITHMETIC. WallSigDelta runs the fold BACKWARDS. It needs no per-renderer
        //      state at all — only the four numbers the refusal already prints — because
        //      FnvPrime is odd and therefore invertible mod 2^64. Run against the ModBuild 438
        //      log BEFORE this shipped, six of its twenty-five distinct refusals decode to a
        //      single event and every one of them names instance id -14798 with bits 141 and
        //      13, i.e. ONE MOD-OWNED MeshRenderer switching itself on and off. See that file.
        //
        //  (2) THE ROW BANK. One ulong and one bool per census row, banked at the commit by
        //      Array.Copy and diffed by a ulong compare — no interop, no dictionary, no
        //      allocation after the first cycle, and the NAME comes from RendererFact.Name,
        //      which ClassifyMaterialsAndName has already allocated. This is what turns "one
        //      renderer, id -14798" into a name a reader can act on, and what reports the
        //      COMPOUND deltas the arithmetic ladder honestly refuses.
        //
        // WHY BOTH, RATHER THAN THE CHEAPER ONE. They are independent derivations of the same
        // fact, so the line can CROSS-CHECK itself: the banked rows that changed must account
        // for the sum and xor deltas EXACTLY. A new instrument's first output is a hypothesis
        // (this project has lost three rounds to one that was believed on sight); this one
        // arrives with its own falsifier attached and says CONSISTENT or INCONSISTENT in the
        // same sentence as its answer.

        // ---- ModBuild 440: THE ROWS ARE KEYED BY INSTANCE ID, NOT BY INDEX ------------------
        //
        // WHY THE 439 VERSION COULD NOT RUN IN THE CASE IT WAS BUILT FOR. It keyed the banked
        // rows by their INDEX in the snapshot array, and guarded that with a refusal whenever the
        // array had been re-swept - correct, because FindObjectsOfType does not specify its
        // order. But all three SIGNATURE DELTA lines in the ModBuild 439 log read
        //   ARITHMETIC: COMPOUND - more than one row moved
        //   ROW CENSUS: NOT COMPARABLE - the snapshot was re-swept (banked 8989 row(s) taken
        //               at 54.8s, live 9001 row(s) taken at 170.4s)
        // and that pairing is not bad luck, it is structural: a SWEEP is what admits several new
        // rows at once, so a compound delta and a re-sweep arrive TOGETHER. The instrument built
        // for the compound case was excluded from it by construction.
        //
        // THE FIX IS IN THE DIAGNOSIS. The renderer's INSTANCE ID is already in the fold - it is
        // the outer term of every row hash and it is how instance -14798 was decoded in 439 - and
        // it is stable across sweeps by construction. Keying on it makes the diff ORDER-FREE: a
        // set difference names which ids ENTERED and which LEFT even when the population size
        // changed, and it degrades gracefully, naming some rows when it cannot name all.
        //
        // THE CROSS-CHECK GETS STRONGER, WHICH IS THE POINT. With index keys the check said "the
        // rows I diffed reproduce the deltas"; with id keys the diff is over two SETS, so
        // reproducing both the sum AND the xor is a proof that the named set IS the symmetric
        // difference - the sum alone can be defeated by a compensating pair and the xor alone by
        // a repeated one, and neither failure survives both. The honest refusals are kept: no
        // banked census yet, and an id-less hole, both still say so rather than guess.
        //
        // COST, stated rather than assumed. Four arrays over ~9,000 rows on each side: the term
        // (ulong, 72 KB), the instance id (int, 36 KB), the folded flag (bool, 9 KB) and, banked
        // only, the name (a REFERENCE the census already owns, 72 KB on 64-bit). That is ~350 KB
        // of long-lived arrays, allocated once and grown by doubling, against a subsystem that
        // already holds a 9,000-entry Renderer snapshot and a 9,000-entry RendererFact table.
        // The per-cycle work is one extra int store per row; the diff builds one Dictionary of
        // ~9,000 int keys, and it runs ONLY on a refusal that also passes the BUDGET throttle,
        // i.e. at most once every five seconds, on a frame that is committing anyway. It reports
        // its own cost under 'WallFade.SigDelta'.

        /// <summary>This cycle's per-row contribution to the scene half, index for index with
        /// <c>_facts</c>. Written by <see cref="RecordSceneFactRow"/> from <c>ClassifySlice</c>,
        /// which visits every index exactly once per cycle in ascending order.</summary>
        private ulong[] _sigRow = Array.Empty<ulong>();

        /// <summary>The renderer's instance id for that row - the KEY the diff runs on, and the
        /// one fact about a row that survives a re-sweep. Taken from the same
        /// <c>GetInstanceID()</c> call the fold already makes, so it costs no interop.
        ///
        /// <para>A row whose renderer DIED keeps the id it had: the snapshot array is not
        /// rebuilt between sweeps, so index i is the same object, and carrying the id forward is
        /// what lets the census say WHICH renderer was destroyed instead of only that one was.
        /// The array is cleared when the snapshot is re-swept, so a row that died before it was
        /// ever classified reads 0 = unknown rather than inheriting a stranger's id.</para>
        /// </summary>
        private int[] _sigRowId = Array.Empty<int>();

        // ---- ModBuild 443: ONE FLAGS BYTE PER ROW, AND ITS CLEARED STATE IS THE SAFE ONE ----
        //
        // TWO FACTS ABOUT A ROW BESIDES ITS TERM, AND THEY GO IN ONE ARRAY. ModBuild 442 was a
        // hand-kept length beside a growing list, and the general shape of that defect is TWO
        // THINGS THAT MUST AGREE, MAINTAINED SEPARATELY. Three parallel bool arrays over 9,000
        // rows would be the same shape, so the flags live in one byte written at exactly one
        // site (RecordSceneFactRow).
        //
        // AND THE POLARITY IS CHOSEN, NOT INHERITED. Both bits are stored as the EXCEPTIONAL
        // answer, so a cleared array — which is what a fresh sweep leaves behind, and what a row
        // that died before it was ever classified reads — means "folded, and not a figure": the
        // conservative answer in both cases. A bit stored the other way round would make an
        // uninitialised row silently exempt itself from the signature, which is a wrong SKIP,
        // and a wrong skip is a segment table that never gets rebuilt.

        /// <summary>The row was EXEMPT from the fold (ModBuild 439's mod-owned exemption). Its
        /// term is still computed and recorded so the census can report it; it is simply not
        /// accumulated. Cleared = folded.</summary>
        private const byte SigRowExempt = 1;

        /// <summary>The row's renderer was <c>RendererFact.Figure</c> — a renderer the round-7
        /// ruling puts beyond every adoption lane's reach. Recorded for ONE question, asked by
        /// the coordinator against the ModBuild 442 log and answered in AppendSigDeltaRows:
        /// whether the renderers that move this signature are figures at all. Cleared = not a
        /// figure, which understates rather than overstates.</summary>
        private const byte SigRowFigure = 2;

        /// <summary>Per-row flags, index for index with <see cref="_sigRow"/>. See
        /// <see cref="SigRowExempt"/> / <see cref="SigRowFigure"/>.</summary>
        private byte[] _sigRowFlags = Array.Empty<byte>();

        /// <summary>The same three arrays as the commit consumed them, plus each row's name -
        /// the only way to name a renderer that has since been DESTROYED, for the same reason
        /// <c>RendererFact.Name</c> exists.</summary>
        private ulong[] _sigBankRow = Array.Empty<ulong>();
        private int[] _sigBankId = Array.Empty<int>();
        private byte[] _sigBankFlags = Array.Empty<byte>();
        private string?[] _sigBankName = Array.Empty<string?>();
        private int _sigBankCount;

        /// <summary>WHEN the snapshot the bank was taken over was swept. Since ModBuild 440 this
        /// is REPORTED and no longer GATES: the diff is keyed on instance ids, so a re-sweep
        /// changes the row ORDER and the row COUNT and changes nothing about the answer. It is
        /// still printed because "the bank is two minutes and three sweeps old" is a fact a
        /// reader needs when judging a large ENTERED count.</summary>
        private float _sigBankSweptAt = float.NegativeInfinity;

        /// <summary>The snapshot generation this cycle's row ids belong to, so
        /// <see cref="ResetSceneFactRows"/> can drop stale ids exactly when the array underneath
        /// them was replaced.</summary>
        private float _sigRowsSweptAt = float.NegativeInfinity;

        /// <summary>Banked rows whose renderer had already died with no id ever recorded - they
        /// fold the per-hole term and cannot be keyed, so they are counted and their arithmetic
        /// is carried explicitly instead of being silently dropped from the cross-check.</summary>
        private int _sigBankUnkeyedHoles;

        /// <summary>The id -&gt; banked row index map the diff runs on. Cleared and refilled,
        /// never rebuilt, so it allocates once and then reuses its capacity.</summary>
        private readonly System.Collections.Generic.Dictionary<int, int> _sigDiffMap = new(16384);

        private bool _sigBankValid;

        /// <summary>Throttle. The line is a <see cref="VRLog.Note"/> — it prints at the DEFAULT
        /// tier on purpose, because the answer a hardware round needs must survive the ModBuild
        /// 331 quiet-log mapping — so it is held to the BUDGET line's own cadence and never
        /// printed per refusal.</summary>
        private float _nextSigDeltaLogTime;

        /// <summary>How many rows this cycle's fold actually accumulated, and how many were
        /// exempted. <see cref="_sceneFoldedRows"/> replaces <c>_factCount</c> in the wall
        /// half's cross-check (see <c>FinishSurveySignature</c>): a census total that still
        /// counted the exempt rows would re-admit their churn through the other signature.
        /// </summary>
        private int _sceneFoldedRows;
        private int _sceneExemptRows;

        /// <summary>Window counters for the BUDGET line: how many scene-signature refusals this
        /// window, and how many of those the row census found NOTHING FOLDED to explain — the
        /// number that would indict the exemption or the instrument.</summary>
        private int _sigDeltaRefusals;
        private int _sigDeltaUnexplained;

        /// <summary>How many movers the line names, split by class. Only a FOLDED mover can have
        /// refused the skip, so it gets the bulk of the budget; the two exempt slots exist so a
        /// non-zero exempt count is never unfalsifiable from the log alone. Everything past a cap
        /// is counted and the elision is stated.</summary>
        private const int SigDeltaNamedFoldedCap = 10;
        private const int SigDeltaNamedExemptCap = 2;

        /// <summary>Drop the per-cycle row state. Called from <c>ResetSceneFactSignature</c>, so
        /// the counters below are always this cycle's and never two cycles blended.</summary>
        private void ResetSceneFactRows()
        {
            _sceneFoldedRows = 0;
            _sceneExemptRows = 0;
            // A FRESH SWEEP REPLACES THE ARRAY, so row i now holds a different renderer. The
            // carry-forward that lets a dead row keep its id (see _sigRowId) must not reach
            // across that boundary, or a renderer that died before its first classify would be
            // reported under the id of whatever stood at that index in the previous snapshot -
            // a name, confidently wrong, which is the one output this instrument may not have.
            if (_sigRowsSweptAt != _snapshotTakenAt)
            {
                _sigRowsSweptAt = _snapshotTakenAt;
                Array.Clear(_sigRowId, 0, _sigRowId.Length);
                // ModBuild 443: and the flags, for the same reason and with the same direction of
                // failure. A carried EXEMPT bit from the previous snapshot's occupant of row i
                // would exempt a GAME renderer from the signature — a wrong skip, which is a
                // segment table that never gets rebuilt. Cleared means folded.
                Array.Clear(_sigRowFlags, 0, _sigRowFlags.Length);
            }
        }

        /// <summary>Record one census row's contribution. Two array writes; no interop, no
        /// branch on configuration — an instrument that is only banked when it is switched on
        /// is the inverse of this project's "gated remedy never ran" entry, and this one is
        /// small enough that it never needs a gate.</summary>
        /// <summary>
        /// Was the renderer that last occupied this census row EXEMPT from the fold?
        ///
        /// <para><b>ModBuild 443 — THE DEATH CASE, WHICH THE 439 EXEMPTION DID NOT COVER.</b>
        /// <c>f.Mod</c> is <c>r.gameObject.layer == VRLayers.ModLayer</c>: a property of a LIVE
        /// object. A row whose renderer has been DESTROYED is a hole, so at the moment the
        /// exemption would be evaluated there is nothing left to ask — and until this build the
        /// hole folded the per-hole term unconditionally. A mod-owned renderer that DIED
        /// therefore moved the signature even though the identical renderer would have been
        /// exempt while alive.</para>
        ///
        /// <para><b>THE ModBuild 442 LOG NAMES THE COST.</b> Its row census reads: four
        /// <c>'VROverlay'</c> rows LEFT as dead holes, all four FOLDED, on the same line where
        /// <c>'GloomhavenVR.Laser_Right'</c> LEFT and was correctly EXEMPT. VROverlay is OURS —
        /// <c>Board/FigureGrab/FigureHighlight.cs</c> and <c>FigureOverlay.cs</c> create it for
        /// the figure hover glow, which ModBuild 439 extended to props, so more of them are made
        /// and destroyed than before. Four of the ten folded rows in that cycle were the mod's
        /// own overlays dying.</para>
        ///
        /// <para><b>WHY EXEMPTING THE DEATH IS SAFER THAN EXEMPTING THE LIFE, not merely as
        /// safe.</b> The 439 argument was that every consumer of the fact table rejects
        /// <c>f.Mod</c> before reading anything else. A DEAD row is rejected one term EARLIER
        /// still, by <c>f.R == null</c>, in every one of those consumers without exception. So a
        /// mod-owned renderer's death cannot change the commit's output by an even shorter
        /// argument than its life could not.</para>
        ///
        /// <para><b>AND IT IS NOT WIDENED TO THE ONE MOD ROW THAT IS NOT EXEMPT.</b> The 439
        /// exemption is a conjunction: a mod-owned renderer that would enter
        /// <c>_factWallFade</c> (<c>f.Mesh != null &amp;&amp; f.WallFadeShader</c>) keeps its
        /// full term, because that index really is read without a mod filter. Such a row was
        /// recorded FOLDED while alive, so this returns false for it and its death still folds —
        /// which is correct, because a dead row leaves <c>_factWallFade</c> and that changes
        /// <c>AdoptShaderMatchedWalls</c>'s input.</para>
        ///
        /// <para>Out of range, or a row that has never been recorded, returns FALSE = fold. That
        /// is the conservative direction and it is the same one the cleared flags array gives.
        /// </para>
        /// </summary>
        private bool SceneRowWasExemptWhenAlive(int index) =>
            index >= 0 && index < _sigRowFlags.Length
            && (_sigRowFlags[index] & SigRowExempt) != 0;

        /// <param name="instanceId">The renderer's instance id, or 0 for a snapshot HOLE - a
        /// row whose renderer has been destroyed has no id to ask for, so the row keeps the one
        /// it was last recorded with (see <see cref="_sigRowId"/>).</param>
        /// <param name="exempt">The row was NOT accumulated into the fold. Stored as the
        /// exceptional answer on purpose — see the flags block above.</param>
        /// <param name="figure">The row's renderer was <c>RendererFact.Figure</c>.</param>
        private void RecordSceneFactRow(int index, int instanceId, ulong contribution,
                                        bool exempt, bool figure)
        {
            if (_sigRow.Length <= index)
            {
                int want = Mathf.NextPowerOfTwo(index + 1);
                Array.Resize(ref _sigRow, want);
                Array.Resize(ref _sigRowId, want);
                Array.Resize(ref _sigRowFlags, want);
            }
            if (instanceId != 0)
                _sigRowId[index] = instanceId;
            _sigRow[index] = contribution;
            _sigRowFlags[index] = (byte)((exempt ? SigRowExempt : 0)
                                       | (figure ? SigRowFigure : 0));
            if (exempt)
                _sceneExemptRows++;
            else
                _sceneFoldedRows++;
        }

        /// <summary>Bank the rows the committed table was built from, at the same instant and
        /// from the same arrays as <c>AdoptCommittedSignature</c> banks the hashes — for the
        /// reason that method already gives about the census: a baseline taken at any other
        /// moment compares two different populations.</summary>
        private void BankSceneSignatureRows()
        {
            using (PerfMonitor.Scope("WallFade.SigBank"))
            {
                int n = _factCount;
                if (n > _sigRow.Length)
                    n = _sigRow.Length;
                if (_sigBankRow.Length < n)
                {
                    int want = Mathf.NextPowerOfTwo(Mathf.Max(n, 256));
                    Array.Resize(ref _sigBankRow, want);
                    Array.Resize(ref _sigBankId, want);
                    Array.Resize(ref _sigBankFlags, want);
                    Array.Resize(ref _sigBankName, want);
                }
                Array.Copy(_sigRow, _sigBankRow, n);
                Array.Copy(_sigRowId, _sigBankId, n);
                Array.Copy(_sigRowFlags, _sigBankFlags, n);
                _sigBankUnkeyedHoles = 0;
                for (int i = 0; i < n; i++)
                {
                    _sigBankName[i] = _facts[i].Name;
                    if (_sigBankId[i] == 0)
                        _sigBankUnkeyedHoles++;
                }
                _sigBankCount = n;
                _sigBankSweptAt = _snapshotTakenAt;
                _sigBankValid = true;
            }
        }

        /// <summary>
        /// Name what moved the scene half — the line the ModBuild 438 round could not print.
        ///
        /// <para>THROTTLED, PRICED AND SELF-CHECKING. It runs at the BUDGET cadence on a frame
        /// that was going to commit anyway, it reports its own cost under
        /// <c>WallFade.SigDelta</c>, and it ends by stating whether the rows it named account
        /// for the arithmetic delta EXACTLY. A census that cannot reproduce the number it is
        /// explaining is an instrument bug, and it says so in its own output rather than
        /// leaving a reader to notice.</para>
        /// </summary>
        private void NoteSceneSignatureDelta(float now)
        {
            _sigDeltaRefusals++;
            if (now < _nextSigDeltaLogTime)
                return;
            _nextSigDeltaLogTime = now + BudgetLogIntervalSeconds;
            using (PerfMonitor.Scope("WallFade.SigDelta"))
            {
                var sb = new System.Text.StringBuilder(1024);
                sb.Append("SIGNATURE DELTA (ModBuild 439 — WHICH renderer moved the scene half, ")
                  .Append("decoded instead of censused): ");
                AppendSigDeltaArithmetic(sb);
                AppendSigDeltaRows(sb);
                // HW-VERIFY
                VRLog.Note(Name, sb.ToString());
            }
        }

        /// <summary>The half that needs no state at all: run the fold backwards over the four
        /// numbers the refusal already prints.</summary>
        private void AppendSigDeltaArithmetic(System.Text.StringBuilder sb)
        {
            WallSigDelta.Delta d = WallSigDelta.Classify(
                _committedSceneSum, _committedSceneXor, _sceneFactSigSum, _sceneFactSigXor);
            sb.Append("ARITHMETIC (no per-renderer state — FnvPrime is odd, so the fold is ")
              .Append("invertible mod 2^64): ");
            switch (d.Kind)
            {
                case WallSigDelta.DeltaKind.Nothing:
                    sb.Append("BOTH ACCUMULATORS STOOD STILL, which this term cannot refuse on ")
                      .Append("— if this ever prints, the refusal and the fold disagree.");
                    break;
                case WallSigDelta.DeltaKind.BitsOnly:
                    sb.Append("VERDICT BITS ONLY. The sum delta is exactly ").Append(d.Quotient)
                      .Append(" x FnvPrime, and only a row whose IDENTITY did not change can ")
                      .Append("contribute a small multiple of the prime — so NOTHING was born, ")
                      .Append("died or left the snapshot this cycle. |")
                      .Append(d.Quotient).Append("| == 128 is one renderer's activeInHierarchy ")
                      .Append("and nothing else.");
                    break;
                case WallSigDelta.DeltaKind.Entered:
                case WallSigDelta.DeltaKind.Left:
                case WallSigDelta.DeltaKind.Died:
                    sb.Append(d.Kind == WallSigDelta.DeltaKind.Entered
                                ? "EXACTLY ONE ROW ENTERED the snapshot"
                                : d.Kind == WallSigDelta.DeltaKind.Left
                                    ? "EXACTLY ONE ROW LEFT the snapshot"
                                    : "EXACTLY ONE RENDERER WAS DESTROYED under the reused snapshot");
                    sb.Append(" (term ").Append(d.Row.ToString("X16")).Append("): ");
                    if (WallSigDelta.TryDecodeRow(d.Row, out int id, out int bits))
                    {
                        sb.Append("instance id ").Append(id).Append(", verdict bits ").Append(bits)
                          .Append(" = ").Append(WallSigDelta.Names(bits)).Append('.');
                        if ((bits & WallSigDelta.BitMod) != 0)
                        {
                            sb.Append(" THIS IS A MOD-OWNED RENDERER: every collection pass ")
                              .Append("rejects it on f.Mod before it reads anything else, so it ")
                              .Append("cannot change the commit's output and must not be moving ")
                              .Append("this signature.");
                        }
                    }
                    else
                    {
                        sb.Append("the term did not invert to a unique (id, bits) pair, which ")
                          .Append("means the fold and this decoder disagree — believe neither.");
                    }
                    break;
                default:
                    sb.Append("COMPOUND — more than one row moved, so the ladder refuses to name ")
                      .Append("one (sum delta / FnvPrime = ").Append(d.Quotient)
                      .Append(", too large to be verdict bits). The row census below is the ")
                      .Append("instrument for this case.");
                    break;
            }
        }

        /// <summary>
        /// The half that names renderers: the two row sets, diffed BY INSTANCE ID.
        ///
        /// <para>ORDER-FREE BY CONSTRUCTION (ModBuild 440). The banked ids go into a map, the
        /// live rows are walked against it, and whatever is left in the map LEFT the snapshot.
        /// Nothing here reads a row index as an identity, so a fresh
        /// <c>FindObjectsOfType&lt;Renderer&gt;</c> between the commit and this refusal — the
        /// case that silenced the 439 version in all three of its outputs — changes only the
        /// order and the count.</para>
        ///
        /// <para>WHAT IT STILL REFUSES TO ANSWER. A row whose renderer died before it was ever
        /// classified has no id (see <see cref="_sigRowId"/>); such rows are COUNTED on both
        /// sides, their per-hole terms are carried into the cross-check explicitly, and they are
        /// never matched to each other or named. That is the only place this census can be out
        /// of information, and it says so rather than pairing two unknowns.</para>
        /// </summary>
        private void AppendSigDeltaRows(System.Text.StringBuilder sb)
        {
            sb.Append(" ROW CENSUS (ModBuild 440 — keyed by INSTANCE ID, so a re-swept snapshot "
                    + "no longer silences it): ");
            if (!_sigBankValid)
            {
                sb.Append("no commit has banked one yet, so there is no baseline — this is the ")
                  .Append("first refusal of the session and says so rather than printing an ")
                  .Append("empty diff that would read as 'nothing changed'.");
                return;
            }

            _sigDiffMap.Clear();
            for (int i = 0; i < _sigBankCount; i++)
            {
                int id = _sigBankId[i];
                if (id != 0)
                    _sigDiffMap[id] = i;
            }

            int entered = 0, left = 0, changedRows = 0;
            int foldedMovers = 0, exemptMovers = 0, liveUnkeyedHoles = 0;
            // TWO CAPS, NOT ONE. Naming the first twelve movers in ROW ORDER is naming an
            // arbitrary twelve: only the FOLDED ones can have refused the skip, and an exempt
            // mover that arrived at a low index would push the answer off the end of the list.
            // The exempt slots are kept and small on purpose — a non-zero exempt count with no
            // name would leave the ModBuild 439 exemption unfalsifiable from the log alone.
            int namedFolded = 0, namedExempt = 0;
            // ModBuild 443 — THE FIGURE QUESTION, ANSWERED AS A COUNT RATHER THAN ARGUED. See
            // the clause this feeds for what a zero here does and does not prove. Accumulated as
            // the BIT (SigRowFigure == 2), so it is divided by that bit on the way out rather
            // than branched on the way in.
            int figureMovers = 0;
            ulong sumCheck = 0, xorCheck = 0;
            var rows = new System.Text.StringBuilder(768);

            for (int i = 0; i < _factCount && i < _sigRow.Length; i++)
            {
                int id = _sigRowId[i];
                ulong after = _sigRow[i];
                byte afterFlags = _sigRowFlags[i];
                bool afterFolded = (afterFlags & SigRowExempt) == 0;
                if (id == 0)
                {
                    liveUnkeyedHoles++;
                    continue; // an unkeyable hole: counted, carried arithmetically, never named
                }
                if (_sigDiffMap.TryGetValue(id, out int bi))
                {
                    _sigDiffMap.Remove(id); // matched — whatever survives the walk LEFT
                    ulong before = _sigBankRow[bi];
                    byte beforeFlags = _sigBankFlags[bi];
                    bool beforeFolded = (beforeFlags & SigRowExempt) == 0;
                    if (before == after && beforeFolded == afterFolded)
                        continue;
                    changedRows++;
                    bool changedFolded = beforeFolded || afterFolded;
                    figureMovers += (beforeFlags | afterFlags) & SigRowFigure;
                    Account(ref sumCheck, ref xorCheck, before, beforeFolded, after, afterFolded,
                            ref foldedMovers, ref exemptMovers);
                    if (TakeNameSlot(changedFolded, ref namedFolded, ref namedExempt, rows))
                    {
                        AppendSigDeltaChangedRow(rows, i, bi, before, after, changedFolded,
                                                 (byte)(beforeFlags | afterFlags));
                    }
                    continue;
                }
                entered++;
                figureMovers += afterFlags & SigRowFigure;
                Account(ref sumCheck, ref xorCheck, 0UL, false, after, afterFolded,
                        ref foldedMovers, ref exemptMovers);
                if (TakeNameSlot(afterFolded, ref namedFolded, ref namedExempt, rows))
                {
                    AppendSigDeltaEndpointRow(rows, "ENTERED", _facts[i].Name, id, after,
                                              afterFolded, afterFlags);
                }
            }

            foreach (System.Collections.Generic.KeyValuePair<int, int> kv in _sigDiffMap)
            {
                int bi = kv.Value;
                left++;
                ulong before = _sigBankRow[bi];
                byte beforeFlags = _sigBankFlags[bi];
                bool beforeFolded = (beforeFlags & SigRowExempt) == 0;
                figureMovers += beforeFlags & SigRowFigure;
                Account(ref sumCheck, ref xorCheck, before, beforeFolded, 0UL, false,
                        ref foldedMovers, ref exemptMovers);
                if (TakeNameSlot(beforeFolded, ref namedFolded, ref namedExempt, rows))
                {
                    AppendSigDeltaEndpointRow(rows, "LEFT", _sigBankName[bi], kv.Key, before,
                                              beforeFolded, beforeFlags);
                }
            }

            // The unkeyable holes on both sides fold the per-hole term, which is a constant: k of
            // them contribute k x DeadRow to the sum, and DeadRow to the xor iff k is odd. Both
            // are carried here so the cross-check below stays an equality and not an
            // approximation — a census whose arithmetic "nearly" reproduces the fold is one this
            // project has no use for.
            WallSigDelta.HoleCorrection(liveUnkeyedHoles, _sigBankUnkeyedHoles,
                                        out ulong holeSum, out ulong holeXor);
            unchecked
            {
                sumCheck += holeSum;
                xorCheck ^= holeXor;
            }

            int movers = entered + left + changedRows;
            int named = namedFolded + namedExempt;
            sb.Append(movers).Append(" row(s) moved between the ").Append(_sigBankCount)
              .Append(" banked (swept at ").Append(_sigBankSweptAt.ToString("F1"))
              .Append("s) and the ").Append(_factCount).Append(" live (swept at ")
              .Append(_snapshotTakenAt.ToString("F1")).Append("s) — ").Append(entered)
              .Append(" ENTERED, ").Append(left).Append(" LEFT, ").Append(changedRows)
              .Append(" CHANGED IN PLACE. ").Append(foldedMovers)
              .Append(" of them are FOLDED (these, and only these, are what refused the skip) ")
              .Append("and ").Append(exemptMovers)
              .Append(" are EXEMPT (ModBuild 439's mod-owned exemption: computed, reported, and ")
              .Append("deliberately not accumulated). This cycle folded ").Append(_sceneFoldedRows)
              .Append(" row(s) and exempted ").Append(_sceneExemptRows).Append("; ")
              .Append(liveUnkeyedHoles).Append(" live and ").Append(_sigBankUnkeyedHoles)
              .Append(" banked row(s) are holes with no recorded id and are counted, never ")
              .Append("named. ");
            // ModBuild 443 — THE FIGURE QUESTION, AND WHAT ITS TWO ANSWERS MEAN. The coordinator
            // read the ModBuild 442 log's FIGURE EXEMPTION clause — "0 cycle(s) this window moved
            // the FULL scene signature but NOT the narrowed one", in all 35 windows — and asked
            // whether the monster's renderers are simply not f.Figure. THE COUNTER IS NOT
            // MEASURING THAT, and the source settles it without a hardware round: the narrowed
            // half is FoldSig(ident, NarrowedBits(bits, figure)) — it replaces the
            // activeInHierarchy BIT and still folds IDENTITY. So a row that ENTERS or LEAVES the
            // snapshot moves the narrowed half too, whatever its figure verdict is, and the
            // narrowing is structurally incapable of removing a SPAWN or a DESPAWN. It can only
            // ever remove an activeInHierarchy flip of a renderer that is present on both sides.
            // The 442 log's movers are 6 ENTERED and 5 LEFT with 0 CHANGED IN PLACE, so a zero
            // yield there is the counter being right about a class of event outside its reach —
            // not evidence about figures. THIS number is the evidence about figures: it is the
            // f.Figure verdict of the movers themselves, recorded per row.
            sb.Append("FIGURE VERDICT OF THE MOVERS (ModBuild 443): ")
              .Append(figureMovers / SigRowFigure).Append(" of the ").Append(movers)
              .Append(" were RendererFact.Figure. Read it against the FIGURE EXEMPTION clause on ")
              .Append("this line and NOT as a substitute for it: that clause counts cycles the ")
              .Append("narrowing would have skipped, and the narrowing still folds IDENTITY, so ")
              .Append("it can never remove a spawn or a despawn — only an activeInHierarchy flip ")
              .Append("of a renderer present on both sides. A high count here with ENTERED/LEFT ")
              .Append("movers means the figure narrowing cannot help with this session's churn; ")
              .Append("a ZERO count means the round-7 figure test does not see these renderers ")
              .Append("at all, which is a finding about the test and not about the scene. ");
            if (named > 0)
            {
                sb.Append("ROWS (").Append(named).Append(" named, ").Append(movers - named)
                  .Append(" more counted above but not listed): ").Append(rows).Append(". ");
            }
            if (foldedMovers == 0)
            {
                _sigDeltaUnexplained++;
                sb.Append("NO FOLDED ROW MOVED AT ALL, which cannot refuse this term — either ")
                  .Append("the row recorder is out of step with the fold or the refusal came ")
                  .Append("from somewhere else. Believe the arithmetic clause, not this one. ");
            }
            unchecked
            {
                bool ok = sumCheck == _sceneFactSigSum - _committedSceneSum
                       && xorCheck == (_sceneFactSigXor ^ _committedSceneXor);
                if (!ok)
                    _sigDeltaUnexplained++;
                sb.Append(ok
                    ? "CROSS-CHECK PASSES: the folded rows above reproduce the sum AND the xor "
                      + "delta exactly. Over an ID-KEYED set diff that is a proof and not a "
                      + "plausible story — the sum alone can be defeated by a compensating pair "
                      + "and the xor alone by a repeated one, so an exact match on both says the "
                      + "named set IS the symmetric difference the signature saw."
                    : "CROSS-CHECK FAILS: the folded rows do NOT reproduce the deltas (rows give "
                      + sumCheck.ToString("X16") + "/" + xorCheck.ToString("X16")
                      + ", the fold moved by "
                      + (_sceneFactSigSum - _committedSceneSum).ToString("X16") + "/"
                      + (_sceneFactSigXor ^ _committedSceneXor).ToString("X16")
                      + ") — THIS INSTRUMENT IS LYING AND MUST BE FIXED BEFORE IT IS READ.");
            }
            _sigDiffMap.Clear(); // never hold a scene's worth of keys to the next cycle
        }

        /// <summary>One row's contribution to the cross-check and to the folded/exempt tally.
        /// A term that was not folded contributes NOTHING to the arithmetic, which is exactly
        /// what the exemption means, and the row is tallied as exempt instead.</summary>
        private static void Account(ref ulong sumCheck, ref ulong xorCheck,
                                    ulong before, bool beforeFolded,
                                    ulong after, bool afterFolded,
                                    ref int foldedMovers, ref int exemptMovers)
        {
            if (beforeFolded || afterFolded)
            {
                foldedMovers++;
                unchecked
                {
                    ulong a = afterFolded ? after : 0UL;
                    ulong b = beforeFolded ? before : 0UL;
                    sumCheck += a - b;
                    xorCheck ^= a ^ b;
                }
            }
            else
            {
                exemptMovers++;
            }
        }

        /// <summary>Claim one of the two naming budgets and write the separator if it does.
        /// Returns false when this mover's class is full — the caller then counts it and says so
        /// in the elision figure, because a truncated list that does not admit it is how "X never
        /// appears" once became a whole wrong conclusion.</summary>
        private static bool TakeNameSlot(bool folded, ref int namedFolded, ref int namedExempt,
                                         System.Text.StringBuilder rows)
        {
            if (folded)
            {
                if (namedFolded >= SigDeltaNamedFoldedCap)
                    return false;
                namedFolded++;
            }
            else
            {
                if (namedExempt >= SigDeltaNamedExemptCap)
                    return false;
                namedExempt++;
            }
            if (namedFolded + namedExempt > 1)
                rows.Append("; ");
            return true;
        }

        /// <summary>A renderer present on BOTH sides whose term moved — the case the whole
        /// exercise is about, because it is what an <c>activeInHierarchy</c> flip looks like.
        /// </summary>
        private void AppendSigDeltaChangedRow(System.Text.StringBuilder sb, int liveIndex,
                                              int bankIndex, ulong before, ulong after,
                                              bool folded, byte flags)
        {
            string name = _facts[liveIndex].Name ?? _sigBankName[bankIndex] ?? "<unnamed>";
            sb.Append('\'').Append(name).Append("' #").Append(_sigRowId[liveIndex]);
            if (after == WallSigDelta.DeadRow)
            {
                sb.Append(" WAS DESTROYED (its snapshot row went null)");
            }
            else if (before == WallSigDelta.DeadRow)
            {
                sb.Append(" refilled a row that had been a dead hole");
            }
            else
            {
                int bitDelta = WallSigDelta.BitDeltaOf(before, after);
                if (bitDelta >= 0
                    && WallSigDelta.TryDecodeRow(before, out _, out int bitsB)
                    && WallSigDelta.TryDecodeRow(after, out _, out int bitsA))
                {
                    sb.Append(' ').Append(bitsB).Append("->").Append(bitsA).Append(" (")
                      .Append(WallSigDelta.Names(bitDelta)).Append(" moved)");
                }
                else
                {
                    sb.Append(" term moved but did not invert (").Append(before.ToString("X16"))
                      .Append("->").Append(after.ToString("X16"))
                      .Append(") — the fold and this decoder disagree, believe neither");
                }
            }
            sb.Append(folded ? " [FOLDED]" : " [EXEMPT]");
            if ((flags & SigRowFigure) != 0)
                sb.Append("[FIGURE]");
        }

        /// <summary>A renderer on ONE side only: it entered the snapshot or it left it. The bits
        /// come out of the term itself, so the line says what KIND of renderer arrived even when
        /// the name is missing.</summary>
        private static void AppendSigDeltaEndpointRow(System.Text.StringBuilder sb, string what,
                                                      string? name, int id, ulong term,
                                                      bool folded, byte flags)
        {
            sb.Append('\'').Append(name ?? "<unnamed>").Append("' #").Append(id).Append(' ')
              .Append(what);
            if (term == WallSigDelta.DeadRow)
                sb.Append(" (as a dead hole)");
            else if (WallSigDelta.TryDecodeRow(term, out _, out int bits))
                sb.Append(" with bits ").Append(bits).Append(" = ").Append(WallSigDelta.Names(bits));
            sb.Append(folded ? " [FOLDED]" : " [EXEMPT]");
            if ((flags & SigRowFigure) != 0)
                sb.Append("[FIGURE]");
        }

        /// <summary>The BUDGET line's own clause for this instrument, so the window totals are
        /// readable even in a capture whose default tier dropped the per-refusal line. Appended,
        /// never woven into an existing sentence.</summary>
        private void AppendSigDeltaClause(System.Text.StringBuilder sb)
        {
            sb.Append(" SIGNATURE DELTA (ModBuild 439, id-keyed since 440): ")
              .Append(_sigDeltaRefusals)
              .Append(" scene-signature refusal(s) this window, ").Append(_sigDeltaUnexplained)
              .Append(" of which the ROW CENSUS could not explain (no folded row moved, or its ")
              .Append("cross-check failed — a re-swept snapshot is no longer one of the ways, ")
              .Append("which is the whole of ModBuild 440). Each refusal is one ~155ms commit the player feels as ")
              .Append("a hitch with nothing on screen to explain it; the 'SIGNATURE DELTA' line ")
              .Append("names the renderer behind it and prints at the DEFAULT log tier. This ")
              .Append("cycle folded ").Append(_sceneFoldedRows).Append(" census row(s) and ")
              .Append("exempted ").Append(_sceneExemptRows)
              .Append(" mod-owned one(s) (ModBuild 439 — see WallSigDelta).");
            _sigDeltaRefusals = 0;
            _sigDeltaUnexplained = 0;
        }

        private void AppendPhase(System.Text.StringBuilder sb, int index, ref float namedTotal,
                                 bool first)
        {
            if (index < 0)
                return;
            if (!first)
                sb.Append(", ");
            namedTotal += _phaseTotalMillis[index];
            sb.Append(CommitPhaseLabel(index)).Append(' ')
              .Append(_phaseWorstMillis[index].ToString("F2")).Append("ms worst / ")
              .Append(_phaseTotalMillis[index].ToString("F1")).Append("ms total");
        }
    }
}
