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
        }

        private const int CommitPhaseCount = (int)CommitPhase.BoardVolume + 1;

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

        private static string[] BuildCommitPhaseScopeNames()
        {
            var names = new string[CommitPhaseCount];
            for (int i = 0; i < CommitPhaseCount; i++)
                names[i] = "WallFade.Commit." + CommitPhaseNames[i];
            return names;
        }

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
            _cycleWorstCommitPhase >= 0 ? CommitPhaseNames[_cycleWorstCommitPhase] : "none yet";

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
                _index = index;
                _measure = PerfMonitor.Scope(CommitPhaseScopeNames[index]);
                _startMillis = (float)RescanClock.Elapsed.TotalMilliseconds;
            }

            public void Dispose()
            {
                float ms = (float)RescanClock.Elapsed.TotalMilliseconds - _startMillis;
                _measure.Dispose();
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
            /// <summary>A SUBSET of <see cref="Sweep"/>, not a sibling of it — the nearest-wall
            /// table walk alone, the term PERF S7 made cheap. It is a stage of its own so that
            /// SWEEP MINUS ELECTIONWALK is the per-candidate prologue, which is the number the
            /// 438 round could not see. It is deliberately NOT subtracted from Sweep: a stage
            /// whose total silently excluded its own sub-stage would make the eleven figures
            /// stop adding up to the phase, and this project has an entry for a census whose
            /// arithmetic nobody could reproduce.</summary>
            ElectionWalk,
        }

        private const int MountedStageCount = (int)MountedStage.ElectionWalk + 1;

        private static readonly string[] MountedStageNames =
        {
            "Ownership", "UnitHomes", "WallHomes", "Park", "Sticky", "ElectionIndex", "Sweep",
            "Riders", "Leavers", "Union", "Census", "ElectionWalk(subset of Sweep)",
        };

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

        /// <summary>Close one stage and open the next: adds the elapsed time to
        /// <paramref name="stage"/> and returns the new stamp. A chain of these costs one clock
        /// read per boundary and cannot double-count, because each call's return IS the next
        /// call's start.</summary>
        private float MountedMark(MountedStage stage, float since)
        {
            float now = MountedClockMillis();
            _mountedStageCycle[(int)stage] += now - since;
            return now;
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
            sb.Append(" MOUNTED STAGES (ModBuild 439 — worst single cycle / window total, ms; ")
              .Append("the 438 product hypothesis was falsified by the count above, so the phase ")
              .Append("is sliced before anything else is tried): ");
            for (int i = 0; i < MountedStageCount; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(MountedStageNames[i]).Append(' ')
                  .Append(_mountedStageWorst[i].ToString("F2")).Append('/')
                  .Append(_mountedStageTotal[i].ToString("F1"));
            }
            sb.Append(". ElectionWalk is a SUBSET of Sweep and is listed inside it, so ")
              .Append("SWEEP MINUS ELECTIONWALK IS THE PER-CANDIDATE PROLOGUE — the term the ")
              .Append("438 round could not see. FUNNEL: ").Append(_mountedInReach)
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

        /// <summary>This cycle's per-row contribution to the scene half, index for index with
        /// <c>_facts</c>. Written by <see cref="RecordSceneFactRow"/> from <c>ClassifySlice</c>,
        /// which visits every index exactly once per cycle in ascending order.</summary>
        private ulong[] _sigRow = Array.Empty<ulong>();

        /// <summary>Whether that row's term actually ENTERED the fold. False for the ModBuild
        /// 439 mod-owned exemption, whose term is computed (so this census can still report it)
        /// and deliberately not accumulated.</summary>
        private bool[] _sigRowFolded = Array.Empty<bool>();

        /// <summary>The same two arrays as the commit consumed them, plus each row's name — the
        /// only way to name a renderer that has since been DESTROYED, for the same reason
        /// <c>RendererFact.Name</c> exists.</summary>
        private ulong[] _sigBankRow = Array.Empty<ulong>();
        private bool[] _sigBankFolded = Array.Empty<bool>();
        private string?[] _sigBankName = Array.Empty<string?>();
        private int _sigBankCount;

        /// <summary>WHEN the snapshot the bank was taken over was swept. Row indices are only
        /// comparable while the snapshot ARRAY is the same one — a fresh
        /// FindObjectsOfType&lt;Renderer&gt; returns an array in unspecified order, so diffing
        /// row i against row i across a sweep would compare two different renderers and produce
        /// a confident wrong answer ("a ratio with two populations"). The timestamp is banked
        /// rather than the array itself: holding the array would keep a scene's worth of
        /// renderer wrappers alive across the gap, which is exactly what ClearSurveyState
        /// exists to avoid.</summary>
        private float _sigBankSweptAt = float.NegativeInfinity;

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

        /// <summary>Highest row count this cycle has touched, so the bank copies exactly what
        /// the fold wrote even if <c>_factCount</c> and the arrays disagree.</summary>
        private const int SigDeltaNamedRowCap = 12;

        /// <summary>Drop the per-cycle row state. Called from <c>ResetSceneFactSignature</c>, so
        /// the counters below are always this cycle's and never two cycles blended.</summary>
        private void ResetSceneFactRows()
        {
            _sceneFoldedRows = 0;
            _sceneExemptRows = 0;
        }

        /// <summary>Record one census row's contribution. Two array writes; no interop, no
        /// branch on configuration — an instrument that is only banked when it is switched on
        /// is the inverse of this project's "gated remedy never ran" entry, and this one is
        /// small enough that it never needs a gate.</summary>
        private void RecordSceneFactRow(int index, ulong contribution, bool folded)
        {
            if (_sigRow.Length <= index)
            {
                int want = Mathf.NextPowerOfTwo(index + 1);
                Array.Resize(ref _sigRow, want);
                Array.Resize(ref _sigRowFolded, want);
            }
            _sigRow[index] = contribution;
            _sigRowFolded[index] = folded;
            if (folded)
                _sceneFoldedRows++;
            else
                _sceneExemptRows++;
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
                    Array.Resize(ref _sigBankFolded, want);
                    Array.Resize(ref _sigBankName, want);
                }
                Array.Copy(_sigRow, _sigBankRow, n);
                Array.Copy(_sigRowFolded, _sigBankFolded, n);
                for (int i = 0; i < n; i++)
                    _sigBankName[i] = _facts[i].Name;
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

        /// <summary>The half that names renderers: the banked rows, diffed.</summary>
        private void AppendSigDeltaRows(System.Text.StringBuilder sb)
        {
            sb.Append(" ROW CENSUS: ");
            if (!_sigBankValid)
            {
                sb.Append("no commit has banked one yet, so there is no baseline — this is the ")
                  .Append("first refusal of the session and says so rather than printing an ")
                  .Append("empty diff that would read as 'nothing changed'.");
                return;
            }
            if (_sigBankSweptAt != _snapshotTakenAt || _sigBankCount != _factCount)
            {
                _sigDeltaUnexplained++;
                sb.Append("NOT COMPARABLE — the snapshot was re-swept since the table in force ")
                  .Append("was built (banked ").Append(_sigBankCount).Append(" row(s) taken at ")
                  .Append(_sigBankSweptAt.ToString("F1")).Append("s, live ").Append(_factCount)
                  .Append(" row(s) taken at ").Append(_snapshotTakenAt.ToString("F1"))
                  .Append("s). FindObjectsOfType does not specify its order, so row i is not the ")
                  .Append("same renderer on both sides and a diff here would be a confident ")
                  .Append("wrong answer. The ARITHMETIC above still stands: it is order-free.");
                return;
            }

            int changed = 0, changedFolded = 0, changedExempt = 0, named = 0;
            ulong sumCheck = 0, xorCheck = 0;
            var rows = new System.Text.StringBuilder(512);
            for (int i = 0; i < _sigBankCount; i++)
            {
                ulong before = _sigBankRow[i];
                ulong after = _sigRow[i];
                if (before == after && _sigBankFolded[i] == _sigRowFolded[i])
                    continue;
                changed++;
                bool folded = _sigBankFolded[i] || _sigRowFolded[i];
                if (folded)
                {
                    changedFolded++;
                    unchecked
                    {
                        sumCheck += (_sigRowFolded[i] ? after : 0UL)
                                  - (_sigBankFolded[i] ? before : 0UL);
                        xorCheck ^= (_sigRowFolded[i] ? after : 0UL)
                                  ^ (_sigBankFolded[i] ? before : 0UL);
                    }
                }
                else
                {
                    changedExempt++;
                }
                if (named >= SigDeltaNamedRowCap)
                    continue;
                named++;
                if (named > 1)
                    rows.Append("; ");
                AppendSigDeltaRow(rows, i, before, after, folded);
            }

            sb.Append(changed).Append(" of ").Append(_sigBankCount)
              .Append(" census row(s) moved since the table in force was built — ")
              .Append(changedFolded).Append(" of them FOLDED (these, and only these, are what ")
              .Append("refused the skip) and ").Append(changedExempt)
              .Append(" EXEMPT (ModBuild 439's mod-owned exemption: computed, reported, and ")
              .Append("deliberately not accumulated). This cycle folded ").Append(_sceneFoldedRows)
              .Append(" row(s) and exempted ").Append(_sceneExemptRows).Append(". ");
            if (named > 0)
            {
                sb.Append("ROWS (").Append(named).Append(" named, ").Append(changed - named)
                  .Append(" more counted above but not listed): ").Append(rows).Append(". ");
            }
            if (changedFolded == 0)
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
                sb.Append(ok
                    ? "CROSS-CHECK PASSES: the folded rows named above reproduce the sum AND xor "
                      + "deltas exactly, so this census explains the very number that refused."
                    : "CROSS-CHECK FAILS: the folded rows do NOT reproduce the deltas (rows give "
                      + sumCheck.ToString("X16") + "/" + xorCheck.ToString("X16")
                      + ", the fold moved by "
                      + (_sceneFactSigSum - _committedSceneSum).ToString("X16") + "/"
                      + (_sceneFactSigXor ^ _committedSceneXor).ToString("X16")
                      + ") — THIS INSTRUMENT IS LYING AND MUST BE FIXED BEFORE IT IS READ.");
            }
        }

        private void AppendSigDeltaRow(System.Text.StringBuilder sb, int index,
                                       ulong before, ulong after, bool folded)
        {
            string name = _facts[index].Name ?? _sigBankName[index] ?? "<unnamed>";
            sb.Append('\'').Append(name).Append('\'');
            if (after == WallSigDelta.DeadRow)
            {
                sb.Append(" WAS DESTROYED (its snapshot row went null)");
            }
            else if (before == WallSigDelta.DeadRow)
            {
                sb.Append(" refilled a row that had been a dead hole");
            }
            else if (WallSigDelta.TryDecodeRow(before, out int idB, out int bitsB)
                     && WallSigDelta.TryDecodeRow(after, out int idA, out int bitsA))
            {
                if (idB == idA)
                {
                    sb.Append(" #").Append(idA).Append(' ').Append(bitsB).Append("->").Append(bitsA)
                      .Append(" (").Append(WallSigDelta.Names(bitsB ^ bitsA)).Append(" moved)");
                }
                else
                {
                    sb.Append(" — row now holds a DIFFERENT renderer (#").Append(idB)
                      .Append(" -> #").Append(idA).Append(')');
                }
            }
            else
            {
                sb.Append(" — term did not invert (").Append(before.ToString("X16")).Append("->")
                  .Append(after.ToString("X16")).Append(')');
            }
            sb.Append(folded ? " [FOLDED]" : " [EXEMPT]");
        }

        /// <summary>The BUDGET line's own clause for this instrument, so the window totals are
        /// readable even in a capture whose default tier dropped the per-refusal line. Appended,
        /// never woven into an existing sentence.</summary>
        private void AppendSigDeltaClause(System.Text.StringBuilder sb)
        {
            sb.Append(" SIGNATURE DELTA (ModBuild 439): ").Append(_sigDeltaRefusals)
              .Append(" scene-signature refusal(s) this window, ").Append(_sigDeltaUnexplained)
              .Append(" of which the ROW CENSUS could not explain (re-swept snapshot, or no ")
              .Append("folded row moved). Each refusal is one ~155ms commit the player feels as ")
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
            sb.Append(CommitPhaseNames[index]).Append(' ')
              .Append(_phaseWorstMillis[index].ToString("F2")).Append("ms worst / ")
              .Append(_phaseTotalMillis[index].ToString("F1")).Append("ms total");
        }
    }
}
