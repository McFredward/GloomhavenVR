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
            if (!State.TryGetValue(name, out Entry e))
            {
                e = new Entry();
                State[name] = e;
            }
            e.Count++;
            float now = Time.unscaledTime;
            if (!e.Opened)
            {
                e.Opened = true;
                e.LastLog = now;
                VRLog.Error(scope ?? DeriveScope(name),
                    $"Tick '{name}' threw and was ISOLATED — the rest of the frame's " +
                    "ticks still run, so the pause-menu tap / input pipeline can't be " +
                    $"starved by one subsystem. {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            else if (now - e.LastLog >= 10f)
            {
                VRLog.Error(scope ?? DeriveScope(name),
                    $"Tick '{name}' is still throwing ({e.Count} time(s) so far) — latest " +
                    $"{ex.GetType().Name}: {ex.Message}. Fix the subsystem; ticks stay isolated.");
                e.LastLog = now;
            }
        }
        finally
        {
            PerfMonitor.EndStep(name, perf);
        }
    }

    /// <summary>Module tag from a fully-qualified step name ("Board.Targeting" → "Board").</summary>
    private static string DeriveScope(string name)
    {
        int dot = name.IndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }
}
