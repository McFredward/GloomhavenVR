using System;
using System.Diagnostics;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PERF S6 (2026-09-05) — THE PER-FRAME TICK, PRICED PHASE BY PHASE.
///
/// <para>WHAT THE PREVIOUS ROUNDS LEFT, AND WHY IT IS THE WRONG HALF. PERF S3/S4/S5 instrumented
/// the COMMIT to twenty-five named phases and then spent three builds shortening it. Read the
/// ModBuild 435 log's arithmetic on the scenario the user actually complained about — 8996 scene
/// renderers, 1176 fade-capable, 34 independently-deciding walls plus 792 split-run pieces:</para>
/// <code>
///   WallFade.Late     4.676ms avg, worst 203.47ms, 184.0ms/s, frames 1181
///   WallFade.Rescan 198.855ms avg, worst 199.76ms,  13.3ms/s, frames 2
///   WallFade.Classify 1.562ms avg,                   7.0ms/s, frames 135
///   WallFade.Prepare  1.473ms avg,                   3.1ms/s, frames 63
///   WallFade.FastReclaim 0.313ms avg,                1.2ms/s, frames 113
/// </code>
/// <para>Every one of those four is NESTED inside <c>WallFade.Late</c>. Subtract them and 159 of
/// the subsystem's 184 ms per second — <b>86 %</b> — is a residue that no instrument in this
/// subsystem has ever named: about 4.3 ms on EVERY frame. The commit the last three rounds
/// optimised is the other 13 ms/s. Twenty-five phases sit on the small half and one opaque scope
/// on the large one, which is this project's recorded "the blind spot is the lead" shape.</para>
///
/// <para>AND IT IS SUPERLINEAR IN THE BOARD. The same log's earlier window — 4677 scene
/// renderers, 399 fade-capable — reads <c>WallFade.Late 0.514ms avg, 38.5ms/s, frames 2246</c>,
/// i.e. ~0.24 ms/frame of residue. The fade-capable population grew 2.95x and the per-frame cost
/// grew about 18x. "Mit allen Türen geöffnet" is exactly the state that maximises both the number
/// of segments and the number of them that are fading at once, so a per-frame pass that walks the
/// whole segment table per frame is precisely the shape that would do this — but that is a
/// hypothesis, and this file exists so the next log ANSWERS it rather than agreeing with it.</para>
///
/// <para>WHY AN INSTRUMENT AND NOT A FIX. Same reason <c>CommitPhase</c> was built before
/// the commit was touched, and the reasoning is in WallSegmentFade.CommitPhases.cs: this project
/// has twice shipped an optimisation for a stage it had never priced, and twice shipped a perf
/// verdict stating a mechanism its own instrument could not observe. Three picture-neutral fixes
/// DO ship in this build (the prop-unit holder index, the floor memo probe order, the mounted
/// emitter hoist) and each carries its own argument at its own site; none of them was chosen from
/// this line, because this line has never run. What ships here is the measurement that decides
/// round two.</para>
///
/// <para>THE TWO TIERS, AND WHY THE SPLIT IS NOT A COMPROMISE. The top-level phases are timed on
/// EVERY frame: there are sixteen of them, so that is thirty-two <see cref="Stopwatch"/>
/// timestamp reads per frame, tens of nanoseconds each, against a 4.3 ms subject. The APPLIER
/// phases are per SEGMENT — 826 of them on this board, six phases each — and timing those every
/// frame would be ~10 000 timestamp reads, a quarter of a millisecond, i.e. 6 % of the very
/// number being measured. So they are timed on one frame in
/// <c>TickApplierSampleEvery</c>, the sample count is printed beside them, and the
/// applier figures are stated as per-SAMPLED-frame. The steady path is by definition steady, so
/// a one-in-eight sample estimates it; a rescan-commit frame is not steady, which is why the
/// commit has its own instrument and is reported separately by
/// <c>TickPhase.Pipeline</c>.</para>
///
/// <para>THE POPULATIONS ARE UNCONDITIONAL AND ARE THE POINT. A phase's milliseconds without the
/// count it walked cannot distinguish "expensive per item" from "too many items", and this
/// subsystem's whole question is which. Every phase therefore carries the population it walked,
/// counted with an integer add on every frame whether or not the frame is timed — so the ratio
/// ms/item is available even where the timing is sampled.</para>
///
/// <para>ITS OWN COST IS MEASURED, NOT ASSERTED. <c>_tickInstrumentTicks</c> accumulates
/// the fold at the end of each frame and the line prints it. If the instrument ever ranks near
/// the work it measures, it has become the thing it measures — the failure this repo has already
/// paid for once with a probe that blitted for 44 200 ticks after answering.</para>
///
/// <para>MULTIPLAYER: presentation only. Nothing here reads or writes the wire, no game state is
/// written, and every field below is an accumulator read by exactly one logger.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver : MonoBehaviour
    {
        /// <summary>
        /// The measured phases of one <c>Tick</c>. <see cref="TickPhase.Total"/> is the whole
        /// tick, so "unattributed" is a subtraction the reader can perform on the line itself
        /// rather than a claim the line makes.
        ///
        /// <para>The APPLIER block (<see cref="TickPhase.ApplyFoliage"/> through
        /// <see cref="TickPhase.ApplyWall"/>) is nested inside <see cref="TickPhase.Decide"/>,
        /// and <see cref="TickPhase.Pipeline"/> is where the already-instrumented rescan stages
        /// live — so those two are the ones a reader must not add to the others.</para>
        /// </summary>
        private enum TickPhase
        {
            Total = 0,
            /// <summary>The rescan pipeline's own slice of this frame: sweep, census, survey,
            /// prepare, or the atomic commit. Already broken out by <c>WallFade.Sweep</c> /
            /// <c>Classify</c> / <c>Survey</c> / <c>Prepare</c> / <c>Rescan</c> and, inside the
            /// last of those, by the twenty-five <c>WallFade.Commit.*</c> phases.</summary>
            Pipeline,
            /// <summary>Head pose, re-evaluation arming, inside-the-board and walk-in state.</summary>
            Perspective,
            /// <summary>The per-segment floor-sample visibility sweep against the head — the
            /// step the 2026-07 pass named as the prime head-motion-correlated suspect and
            /// never priced on its own.</summary>
            Visibility,
            /// <summary>One verdict per split RUN, on the union of its pieces' blocked cells.</summary>
            SplitRuns,
            /// <summary>The whole per-segment decision + apply loop. CONTAINS the six applier
            /// phases below.</summary>
            Decide,
            ApplyFoliage,
            ApplySiblings,
            ApplyMounted,
            ApplyStacked,
            ApplyBody,
            /// <summary>The wall's own channel: the dissolve census, the property block, and the
            /// per-renderer <c>SetPropertyBlock</c> loop that is write primitive 1 of 4.</summary>
            ApplyWall,
            /// <summary>Shared corner pieces — min-fade of the adjacent walls, per frame.</summary>
            Corners,
            /// <summary>The fade-write census (WallSegmentFade.FadeCensus.cs).</summary>
            FadeCensus,
            /// <summary>FLOOR NEVER FADES, the ModBuild 430/431 audit half.</summary>
            FloorAudit,
            /// <summary>The 0.25 s regenerated-shell reclaim sweep.</summary>
            FastReclaim,
            /// <summary>The sliced wall-path audit.</summary>
            PathAudit,
        }

        private const int TickPhaseCount = (int)TickPhase.PathAudit + 1;

        /// <summary>First applier phase — the sampled tier starts here.</summary>
        private const int TickApplierFirst = (int)TickPhase.ApplyFoliage;

        /// <summary>Short names for the TICK BUDGET line. Index IS the enum value.</summary>
        private static readonly string[] TickPhaseNames =
        {
            "Total", "Pipeline", "Perspective", "Visibility", "SplitRuns", "Decide",
            "ApplyFoliage", "ApplySiblings", "ApplyMounted", "ApplyStacked", "ApplyBody",
            "ApplyWall", "Corners", "FadeCensus", "FloorAudit", "FastReclaim", "PathAudit",
        };

        /// <summary>What each phase's population COUNTS, so the line never prints a bare number
        /// whose unit the reader has to infer. Index IS the enum value.</summary>
        private static readonly string[] TickPhaseUnits =
        {
            "frame", "frame", "frame", "segment", "run", "segment",
            "foliage renderer", "sibling renderer", "mounted piece", "stacked piece",
            "body mesh", "wall renderer", "corner piece", "frame", "frame", "frame", "frame",
        };

        /// <summary><see cref="PerfMonitor"/> step names for the TOP-LEVEL phases, built once at
        /// type load so the hot path never concatenates. The applier tier deliberately gets NO
        /// PerfMonitor scope: it is entered once per segment, so a dictionary probe per entry
        /// would be ~5 000 probes a frame on this board.</summary>
        private static readonly string[] TickPhaseScopeNames = BuildTickPhaseScopeNames();

        private static string[] BuildTickPhaseScopeNames()
        {
            var names = new string[TickPhaseCount];
            for (int i = 0; i < TickPhaseCount; i++)
                names[i] = "WallFade.Tick." + TickPhaseNames[i];
            return names;
        }

        /// <summary>One frame in this many carries the per-segment applier timing. A CONSTANT
        /// and not a config dial on purpose: it is the instrument's own sampling rate, it is
        /// printed on the line it governs, and a dial the player can move is a dial that can
        /// make two logs incomparable without saying so.</summary>
        private const int TickApplierSampleEvery = 8;

        /// <summary>Stopwatch ticks spent in each phase THIS frame; folded into the window
        /// accumulators by <see cref="EndTickFrame"/> and cleared there.</summary>
        private readonly long[] _tickPhaseFrameTicks = new long[TickPhaseCount];

        /// <summary>Stopwatch ticks spent in each phase since the last TICK BUDGET line.</summary>
        private readonly long[] _tickPhaseWindowTicks = new long[TickPhaseCount];

        /// <summary>Worst SINGLE FRAME each phase cost since the last line (ms) — the stall
        /// question, which a window total cannot answer (a phase costing 40 ms once and one
        /// costing 2 ms twenty times are the same total and different defects).</summary>
        private readonly float[] _tickPhaseWorstMillis = new float[TickPhaseCount];

        /// <summary>How many items each phase walked since the last line — see the class header
        /// on why this is unconditional.</summary>
        private readonly long[] _tickPhaseWalked = new long[TickPhaseCount];

        /// <summary>Frames in which each phase was TIMED. For the top-level tier this equals
        /// <see cref="_tickFrames"/>; for the applier tier it is the sample count, and the line
        /// prints it so a per-frame figure is never read off a sampled one by accident.</summary>
        private readonly long[] _tickPhaseTimedFrames = new long[TickPhaseCount];

        /// <summary>THE UNCONDITIONAL LIVENESS FIELD: ticks that reached the fold since the last
        /// line, whatever the picture was doing. Zero here — and only that — means the driver
        /// did not run. A phase reading 0 ms beside a non-zero frame count means that phase did
        /// no work this window, which is a different statement and not a failure.</summary>
        private long _tickFrames;

        /// <summary>Frames on which the applier tier was sampled.</summary>
        private long _tickSampledFrames;

        /// <summary>The instrument's own measured cost since the last line (stopwatch ticks in
        /// <see cref="EndTickFrame"/>). Printed; see the class header.</summary>
        private long _tickInstrumentTicks;

        /// <summary>Countdown to the next sampled frame.</summary>
        private int _tickSampleCountdown;

        /// <summary>True while this frame carries the applier tier.</summary>
        private bool _tickSampling;

        private float _nextTickBudgetLog;

        /// <summary>Cadence of the TICK BUDGET line. The same five seconds the rescan BUDGET
        /// line uses, so the two are read side by side in one log.</summary>
        private const float TickBudgetLogIntervalSeconds = 5f;

        /// <summary>Open a TOP-LEVEL measured phase. Use as <c>using (Phase(TickPhase.X))</c>.
        /// Overloads <c>Phase(CommitPhase)</c> — different tier, different scope type, one
        /// name.</summary>
        private TickPhaseScope Phase(TickPhase phase) => new(this, (int)phase);

        /// <summary>Open an APPLIER phase: a real scope on a sampled frame, a
        /// <c>default</c> no-op struct on every other one (one branch, no timestamp read).</summary>
        private TickPhaseScope ApplyPhase(TickPhase phase) =>
            _tickSampling ? new TickPhaseScope(this, (int)phase) : default;

        /// <summary>Book <paramref name="items"/> against <paramref name="phase"/>'s population.
        /// Unconditional and one add — see the class header.</summary>
        private void NoteTickWalk(TickPhase phase, int items) =>
            _tickPhaseWalked[(int)phase] += items;

        /// <summary>
        /// One phase's measurement: raw stopwatch ticks into the driver's own accumulators
        /// (so the line prints whatever <c>[Perf]</c>'s configuration is — the class's one
        /// attributable line does not get to depend on a toggle), PLUS a
        /// <see cref="PerfMonitor"/> scope for the top-level tier only.
        /// </summary>
        private readonly struct TickPhaseScope : IDisposable
        {
            private readonly FadeDriver? _driver;
            private readonly int _index;
            private readonly long _start;
            private readonly PerfMonitor.Measure _measure;

            internal TickPhaseScope(FadeDriver driver, int index)
            {
                _driver = driver;
                _index = index;
                // Total is deliberately NOT given a PerfMonitor scope: it is the same span
                // WallFade.Late already ranks, and a second name for one span would put the
                // subsystem's own cost on the STEPS line twice.
                _measure = index > 0 && index < TickApplierFirst
                    ? PerfMonitor.Scope(TickPhaseScopeNames[index])
                    : default;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_driver == null)
                    return; // the no-op scope an unsampled applier frame gets
                long ticks = Stopwatch.GetTimestamp() - _start;
                _measure.Dispose();
                _driver._tickPhaseFrameTicks[_index] += ticks;
            }
        }

        /// <summary>Open the frame: decide whether it carries the applier tier. Called from
        /// <c>LateUpdate</c> BEFORE the tick, so a tick that returns early still leaves the
        /// sampling state consistent.</summary>
        private void BeginTickFrame()
        {
            if (--_tickSampleCountdown > 0)
            {
                _tickSampling = false;
                return;
            }
            _tickSampleCountdown = TickApplierSampleEvery;
            _tickSampling = true;
        }

        /// <summary>Fold this frame's phase costs into the window accumulators and emit the
        /// line on its cadence. Called from <c>LateUpdate</c> after the tick, inside the same
        /// <c>WallFade.Late</c> scope so the instrument's cost lands on the step it
        /// measures.</summary>
        private void EndTickFrame(float now)
        {
            long foldStart = Stopwatch.GetTimestamp();
            _tickFrames++;
            if (_tickSampling)
                _tickSampledFrames++;
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < TickPhaseCount; i++)
            {
                long ticks = _tickPhaseFrameTicks[i];
                _tickPhaseFrameTicks[i] = 0L;
                if (ticks == 0L)
                    continue;
                _tickPhaseWindowTicks[i] += ticks;
                _tickPhaseTimedFrames[i]++;
                float ms = (float)(ticks * msPerTick);
                if (ms > _tickPhaseWorstMillis[i])
                    _tickPhaseWorstMillis[i] = ms;
            }
            _tickInstrumentTicks += Stopwatch.GetTimestamp() - foldStart;

            // THE CADENCE AND THE RESET LIVE HERE, NOT IN THE LOGGER, and that is a rule this
            // repo enforces with a checker: a diagnostic that owns load-bearing state cannot be
            // gated off or retired without changing behaviour, which is the shape that once
            // nearly latched this very subsystem's fade off forever. LogTickBudget below only
            // reads and prints; every window accumulator is opened and closed by the mechanism.
            if (now < _nextTickBudgetLog)
                return;
            _nextTickBudgetLog = now + TickBudgetLogIntervalSeconds;
            LogTickBudget();
            Array.Clear(_tickPhaseWindowTicks, 0, TickPhaseCount);
            Array.Clear(_tickPhaseWorstMillis, 0, TickPhaseCount);
            Array.Clear(_tickPhaseWalked, 0, TickPhaseCount);
            Array.Clear(_tickPhaseTimedFrames, 0, TickPhaseCount);
            _tickFrames = 0;
            _tickSampledFrames = 0;
            _tickInstrumentTicks = 0;
        }

        /// <summary>
        /// THE ATTRIBUTABLE LINE FOR THE PER-FRAME HALF, and the counterpart of the rescan
        /// BUDGET line rather than a clause inside it: that line is emitted only when a rescan
        /// cycle ENDS, so a subsystem that is ticking and never committing prints nothing at all
        /// through it — which is the exact state this round found itself measuring.
        /// </summary>
        private void LogTickBudget()
        {
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            var sb = new System.Text.StringBuilder(1400);
            float totalMs = (float)(_tickPhaseWindowTicks[(int)TickPhase.Total] * msPerTick);
            float perFrame = _tickFrames > 0 ? totalMs / _tickFrames : 0f;
            sb.Append("TICK BUDGET: ").Append(_tickFrames)
              .Append(" tick(s) since the last line — THE LIVENESS FIELD, and it moves whatever ")
              .Append("the picture is doing; only 0 here means the driver did not run. ")
              .Append("WallFade.Late cost ").Append(totalMs.ToString("F1"))
              .Append("ms in the window, ").Append(perFrame.ToString("F3"))
              .Append("ms/frame, worst single frame ")
              .Append(_tickPhaseWorstMillis[(int)TickPhase.Total].ToString("F2")).Append("ms. ")
              .Append("PHASES (ms/frame over the frames the phase ran, then worst single frame, ")
              .Append("then the population it walked per frame it ran): ");

            for (int i = 1; i < TickPhaseCount; i++)
                AppendTickPhase(sb, i, msPerTick, first: i == 1);

            // The subtraction the reader would otherwise have to do by hand, and it is the whole
            // point of carrying Total: a residue near the total means the phases below name
            // almost nothing, which is a fault in THIS instrument and not in the subsystem.
            float named = 0f;
            for (int i = 1; i < TickPhaseCount; i++)
            {
                if (i == (int)TickPhase.Decide || i >= TickApplierFirst)
                    continue; // Decide contains the appliers; counting both double-counts
                named += (float)(_tickPhaseWindowTicks[i] * msPerTick);
            }
            named += (float)(_tickPhaseWindowTicks[(int)TickPhase.Decide] * msPerTick);
            sb.Append(" — UNATTRIBUTED ").Append((totalMs - named).ToString("F1"))
              .Append("ms of ").Append(totalMs.ToString("F1"))
              .Append("ms, i.e. the tick statements no phase above brackets (Decide is counted ")
              .Append("once, as the parent of the six Apply* phases, which are its parts and ")
              .Append("must not be added to it).");

            sb.Append(" SAMPLING: the six Apply* phases are timed on one frame in ")
              .Append(TickApplierSampleEvery).Append(" (").Append(_tickSampledFrames)
              .Append(" sampled of ").Append(_tickFrames)
              .Append(" this window) because they are entered ONCE PER SEGMENT and this board ")
              .Append("carries hundreds; their populations are counted on EVERY frame, so a ")
              .Append("ms-per-item ratio is available even where the ms is sampled. Every other ")
              .Append("phase is timed on every frame.");

            sb.Append(" PROP-UNIT HOLDER INDEX (PERF S6): the last commit's PASS 2 would have ")
              .Append("made ").Append(_propUnitHolderVisitsOld)
              .Append(" segment visit(s) under the old whole-table walk and made ")
              .Append(_propUnitHolderVisitsWalked)
              .Append(" holder-chain step(s) instead — the same removals, from the same segments, ")
              .Append("in the same order. 0 and 0 together mean no unit was torn since the last ")
              .Append("commit, which is the steady state and not a dead counter.");

            sb.Append(" THIS INSTRUMENT COST ")
              .Append(((float)(_tickInstrumentTicks * msPerTick)).ToString("F2"))
              .Append("ms in the window (the per-frame fold, measured — not the scopes ")
              .Append("themselves, which are two timestamp reads each). If it ever ranks near ")
              .Append("the phases below it, it has become the thing it measures.");

            // HW-VERIFY
            VRLog.Note(Name, sb.ToString());
        }

        private void AppendTickPhase(System.Text.StringBuilder sb, int i, double msPerTick,
                                     bool first)
        {
            if (!first)
                sb.Append(", ");
            long frames = _tickPhaseTimedFrames[i];
            float ms = (float)(_tickPhaseWindowTicks[i] * msPerTick);
            float per = frames > 0 ? ms / frames : 0f;
            // THE TWO DIVISORS ARE DIFFERENT AND BOTH ARE NAMED. The milliseconds are divided
            // by the frames the phase was TIMED on (which for the applier tier is the SAMPLED
            // frames); the population is booked on every tick, so it is divided by the tick
            // count. Reading the second off the first would inflate every sampled phase's
            // population eightfold.
            long walkFrames = Math.Max(_tickFrames, 1L);
            sb.Append(TickPhaseNames[i]).Append(' ').Append(per.ToString("F3"))
              .Append("ms, worst ").Append(_tickPhaseWorstMillis[i].ToString("F2"))
              .Append("ms, ").Append(frames).Append(" timed frame(s)");
            // A phase with no population of its own says nothing rather than printing a zero
            // that would read as "it walked nothing" — a different statement, and a false one.
            if (!string.Equals(TickPhaseUnits[i], "frame", StringComparison.Ordinal))
            {
                sb.Append(", ")
                  .Append((_tickPhaseWalked[i] / (double)walkFrames).ToString("F1")).Append(' ')
                  .Append(TickPhaseUnits[i]).Append("(s)/tick");
            }
        }
    }
}
