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

            Array.Clear(_phaseWorstMillis, 0, CommitPhaseCount);
            Array.Clear(_phaseTotalMillis, 0, CommitPhaseCount);
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
