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
