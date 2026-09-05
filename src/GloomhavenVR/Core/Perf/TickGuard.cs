using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Shared per-frame tick isolation + attribution helper (promoted from the private
/// nested guard in <c>WorldUIModule</c> so every module driver can reuse it).
///
/// <para>
/// The problem it solves: a module driver whose <c>Update()</c> runs a sequence of
/// <c>.Tick()</c> calls with no try/catch will, on a single throwing sub-tick, abort
/// the REST of that frame's ticks AND log an anonymous stackless NullReferenceException
/// every frame — a per-frame flood that names no subsystem and can starve the input
/// pipeline (the pause-menu reopen bug). Route each sub-tick through
/// <see cref="Run(string, Action, string)"/> and instead: the throw is isolated (the
/// remaining ticks still run), the FIRST throw for a given step is logged at Error WITH
/// its full stack tagged by step name, and repeats for that step are throttled to ~1 per
/// 10 s so an every-frame throw cannot itself flood the log. It NEVER rethrows.
/// </para>
///
/// <para>
/// This is a robustness + attribution layer, NOT a fix for the underlying null: the
/// point is that the next run's log shows <c>[Error] ... Tick '&lt;name&gt;' threw
/// &lt;exception + stack&gt;</c> naming the exact subsystem so it can be fixed at source.
/// </para>
///
/// <para>No per-frame allocation: the only hot-path work is a <see cref="Dictionary{TKey,TValue}"/>
/// lookup on the (cold) throw path. String interpolation and the scope substring are
/// computed only when a line is actually emitted (first throw + every 10 s), never per
/// frame during a throw storm. Callers must pass CACHED delegates (not per-frame method
/// groups from instance methods) to keep the delegate allocation out of the hot path.</para>
///
/// <para>SECOND JOB — MEASUREMENT (2026-07 performance pass). Because nearly every heavy mod
/// subsystem already routes its per-frame work through here under a stable NAME, this is also the
/// natural attribution seam: <see cref="Run(string, Action, string)"/> hands the step to
/// <see cref="PerfMonitor"/>, which stopwatch-times it and can therefore rank the mod's own steps
/// by cost in the periodic <c>[Perf] STEPS</c> line and name the owner of an individual
/// over-budget frame in a <c>[Perf] SPIKE</c> line. The measurement is gated on a single static
/// bool (<see cref="PerfMonitor.StepsActive"/>): with the monitor off the added cost of this
/// change is one predictable branch per step, and NOTHING about the isolation contract above
/// changes — the timer is closed in a <c>finally</c>, so a throwing step is still isolated,
/// still logged with its stack, and still measured.</para>
/// </summary>
internal static class TickGuard
{
    private sealed class Entry
    {
        public long Count;
        public float LastLog;
        public bool Opened;
    }

    private static readonly Dictionary<string, Entry> State = new();

    /// <summary>Seconds between repeat lines for one key. The house cadence — every guard in the
    /// mod that reports a throw storm uses this one, so two <c>is still throwing</c> lines from two
    /// subsystems are counted over the same window and are therefore comparable.</summary>
    private const float RepeatSeconds = 10f;

    /// <summary>Cap on distinct keys in <see cref="State"/>. <see cref="Run"/> feeds it a fixed set
    /// of step names, but <see cref="NoteThrow"/> lets a caller key on something derived from the
    /// exception (see <see cref="SubStepOf"/>), and an unbounded key set inside a DIAGNOSTIC is a
    /// leak wearing an instrument's clothes. Past the cap everything lands in one bucket, which is
    /// exactly what this class did before the cap existed.</summary>
    private const int MaxKeys = 64;

    /// <summary>The bucket used once <see cref="MaxKeys"/> is reached, named so a reader can see in
    /// the line itself that attribution has degraded rather than wonder why two subsystems suddenly
    /// share a count.</summary>
    private const string OverflowKey = "<key cap reached>";

    /// <summary>
    /// Run one per-frame sub-tick, ISOLATING any exception it throws so the remaining
    /// ticks in the frame still run (a single misbehaving subsystem must never starve the
    /// input pipeline — the reopen guarantee). The first throw per <paramref name="name"/>
    /// is logged at Error WITH its stack (so an anonymous per-frame NullReferenceException
    /// flood — previously untraced in Player.log — is finally attributable to a subsystem);
    /// repeats are summarized once per 10 s. Never rethrows.
    ///
    /// <paramref name="scope"/> is the VRLog module tag; when null it is derived from the
    /// module prefix of <paramref name="name"/> (the text before the first '.'), so a
    /// fully-qualified step name like <c>"Board.Targeting"</c> logs under <c>[Board]</c>.
    /// Callers whose step names embed dots mid-name (e.g. <c>"ActorBars.Late"</c>) should
    /// pass an explicit scope.
    /// </summary>
    public static void Run(string name, Action fn, string? scope = null)
    {
        // Perf attribution: 0 when the monitor is off, in which case the finally below is a single
        // compare-and-return. Opened OUTSIDE the try so a throw inside the body cannot skip it.
        long perf = PerfMonitor.BeginStep();
        try
        {
            fn();
        }
        catch (Exception ex)
        {
            NoteThrow(scope ?? DeriveScope(name), name, ex,
                      $"Tick '{name}'",
                      "the rest of the frame's ticks still run, so the pause-menu tap / input "
                      + "pipeline can't be starved by one subsystem.",
                      "Fix the subsystem; ticks stay isolated.");
        }
        finally
        {
            PerfMonitor.EndStep(name, perf);
        }
    }

    /// <summary>
    /// THE THROW-STORM REPORT, AND THE ONLY COPY OF IT. Records one throw against
    /// <paramref name="key"/> and emits at most two kinds of line for it: the FIRST one with the
    /// full stack, then a repeat every <see cref="RepeatSeconds"/> saying how many times it has
    /// happened since. Never rethrows, and allocates nothing on a frame that does not log.
    ///
    /// <para><b>WHY THIS IS A SHARED METHOD AND NOT A PATTERN TO COPY.</b> The 2026-09-05
    /// redundancy survey (R30) found this bookkeeping hand-rolled in three more places, and the
    /// copies had drifted in ways that each broke the project's own throw-storm triage procedure,
    /// which is "grep <c>is still throwing</c> and read N":</para>
    /// <list type="bullet">
    ///   <item><c>Board/FocusDriver.Carrier</c> reported the FIRST throw and then suppressed for
    ///   ever (<c>_reported.Add(name)</c>), so a focus-cue carrier throwing every frame emitted no
    ///   repeat line at all and was invisible to that grep.</item>
    ///   <item><c>Cards/Driver/CardsDriver.NoteTickThrow</c> kept its counter on the DRIVER
    ///   INSTANCE while this one is static, so two lines with identical wording carried N values
    ///   that could not be compared.</item>
    ///   <item>That same Cards guard keyed the WHOLE tick path, where this one keys per named
    ///   sub-step — so two throwing subsystems inside Cards read as one storm.</item>
    /// </list>
    ///
    /// <para><b>THE WORDING IS ASSEMBLED FROM THE CALLER'S OWN SENTENCES, deliberately.</b> Each of
    /// those sites explains a DIFFERENT consequence of the isolation ("the rest of the frame's ticks
    /// still run" against "the other carriers still render this frame"), and that sentence is the
    /// part a reader actually needs. What has to be identical is the SHAPE — the
    /// <c>threw and was ISOLATED</c> opener and the <c>is still throwing (N time(s) so far)</c>
    /// repeat — because those two are what the triage procedure greps for. So the caller owns the
    /// prose and this method owns the shape, and no existing line changed its wording.</para>
    /// </summary>
    /// <param name="scope">VRLog module tag.</param>
    /// <param name="key">What the count is kept against — a step name, a carrier name, or a
    /// sub-step derived from the exception (<see cref="SubStepOf"/>).</param>
    /// <param name="ex">The throw being reported.</param>
    /// <param name="subject">How the line names the thing that threw, e.g. <c>Tick 'Board.Rings'</c>.
    /// Interpolated directly in front of <c>threw and was ISOLATED</c>.</param>
    /// <param name="isolatedBecause">The caller's own sentence saying what still runs. Ends with a
    /// full stop; the exception detail follows it.</param>
    /// <param name="repeatAdvice">The caller's own closing sentence on the repeat line.</param>
    internal static void NoteThrow(string scope, string key, Exception ex, string subject,
                                   string isolatedBecause, string repeatAdvice)
    {
        if (!State.TryGetValue(key, out Entry e))
        {
            if (State.Count >= MaxKeys)
                key = OverflowKey;
            if (!State.TryGetValue(key, out e))
            {
                e = new Entry();
                State[key] = e;
            }
        }

        e.Count++;
        float now = Time.unscaledTime;
        if (!e.Opened)
        {
            e.Opened = true;
            e.LastLog = now;
            VRLog.Error(scope,
                $"{subject} threw and was ISOLATED — {isolatedBecause} " +
                $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        else if (now - e.LastLog >= RepeatSeconds)
        {
            VRLog.Error(scope,
                $"{subject} is still throwing ({e.Count} time(s) so far) — latest " +
                $"{ex.GetType().Name}: {ex.Message}. {repeatAdvice}");
            e.LastLog = now;
        }
    }

    /// <summary>
    /// The method an exception was thrown from, qualified by <paramref name="fallback"/>, for a
    /// caller that isolates a WHOLE path in one try/catch and therefore has no step name to key on.
    /// Null-safe, and cheap enough for the cold path (one reflection property read on a frame that
    /// has already thrown).
    ///
    /// <para>It is not exactly the sub-step: a throw three helpers deep names the helper rather than
    /// the tick. That is enough for the job — the defect this fixes is that two DIFFERENT throwing
    /// subsystems shared one counter and read as one storm, and two different methods give two
    /// different keys whatever depth they sit at. <paramref name="fallback"/> alone is used when the
    /// runtime carries no target site.</para>
    /// </summary>
    internal static string SubStepOf(Exception ex, string fallback)
    {
        string? name = ex.TargetSite?.Name;
        return string.IsNullOrEmpty(name) ? fallback : fallback + "/" + name;
    }

    /// <summary>Module tag from a fully-qualified step name ("Board.Targeting" → "Board").</summary>
    private static string DeriveScope(string name)
    {
        int dot = name.IndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }
}
