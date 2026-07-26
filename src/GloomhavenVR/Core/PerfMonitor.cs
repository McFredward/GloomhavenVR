using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// Frame-pacing instrumentation for the hardware logs (2026-07 performance pass).
///
/// <para>WHY PACING AND NOT FPS: the reported symptom is "the world judders / lags behind when I
/// move my head fast". In VR that is almost never an average-throughput problem — the compositor
/// happily holds 72/90 Hz on average while a handful of LATE frames get reprojected, and a
/// reprojected frame is exactly what reads as the world dragging behind the head. An average FPS
/// number cannot see that. So the primary measurement here is the DISTRIBUTION of per-frame wall
/// times against the display's ACTUAL frame budget (read from the XR display, never a hardcoded
/// 72/90), plus a line for every individual frame that blew it.</para>
///
/// <para>WHAT IT MEASURES</para>
/// <list type="number">
/// <item><b>Frame pacing</b> — every frame's unscaled wall delta into a ring buffer; per window:
/// mean, p95, p99, max, count over budget, count over the spike threshold.</item>
/// <item><b>Attribution</b> — every NAMED mod step (all <see cref="TickGuard.Run"/> steps plus the
/// driver bodies explicitly wrapped in <see cref="Scope"/>) is stopwatch-timed and aggregated per
/// window AND per frame, so the STEPS line ranks the mod's own subsystems and a SPIKE line can say
/// which of them owned that frame. This is the piece that answers "is the hitch ours?" — and when
/// the mod total is a rounding error next to the frame time, the line says so in as many words,
/// which is just as valuable an answer.</item>
/// <item><b>Allocation pressure</b> — GC collection counts per generation and managed-heap growth
/// per window. A gen0 collection inside a head turn IS the reported symptom.</item>
/// <item><b>XR-side truth</b> — the display subsystem's own dropped/presented frame counters, GPU
/// time and motion-to-photon latency, resolved by reflection (their exact shape differs between
/// Unity XR versions). Anything the runtime does not expose is printed as <c>n/a</c>, never as a
/// fake zero — a zero here would be a lie that costs a whole test round to discover.</item>
/// </list>
///
/// <para>GREP HANDLES (all under the <c>[Perf]</c> scope):</para>
/// <list type="bullet">
/// <item><c>[Perf] FRAME</c> — the periodic pacing/GC/XR summary.</item>
/// <item><c>[Perf] STEPS</c> — the periodic per-subsystem cost ranking.</item>
/// <item><c>[Perf] SPIKE</c> — one line per over-budget frame (rate-limited, suppressed ones counted).</item>
/// <item><c>[Perf] CAPS</c> — one startup line stating which counters resolved and which did not.</item>
/// </list>
///
/// <para>COST AND SELF-DISCIPLINE. The monitor must never become the stutter it is hunting:</para>
/// <list type="bullet">
/// <item>Off (<c>[Perf] Enabled = false</c>) it is one static bool test per frame and per step —
/// <see cref="StepsActive"/> is a plain static FIELD, not a property, so the JIT folds the check
/// into a single load+branch on the hot path.</item>
/// <item>On, a frame costs: one <see cref="Stopwatch.GetTimestamp"/> pair per named step (~20 ns
/// each on Windows), one dictionary lookup per step, one heap-size read, and a walk of the (few
/// dozen) step records. Low single-digit microseconds against an 11 ms budget.</item>
/// <item>ZERO steady-state allocation: the step records, the frame ring, the sort scratch and the
/// line builder are all allocated once and reused. The only garbage is the finished log string
/// itself, once per window (and once per emitted spike).</item>
/// </list>
///
/// <para>FRAME BOUNDARY / ORDERING. The host component carries
/// <see cref="DefaultExecutionOrder"/> −30000 so its <c>Update</c> runs BEFORE every mod driver.
/// That makes the boundary exact: when the host wakes in frame N, <see cref="Time.unscaledDeltaTime"/>
/// is frame N−1's complete wall duration and the per-step accumulators still hold frame N−1's
/// complete work (its Update AND LateUpdate AND render callbacks have all run). So the host closes
/// out N−1 — spike check first, then roll into the window — and only then resets for frame N. No
/// half-frames, no off-by-one attribution.</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. The monitor reads clocks and counters and writes log lines.
/// It touches no game object, no game state and no wire traffic, so it is trivially
/// multiplayer-safe and trivially reversible (<see cref="Shutdown"/> destroys the host and drops
/// the records).</para>
/// </summary>
internal static class PerfMonitor
{
    private const string Scope0 = "Perf";

    /// <summary>Ring capacity for per-frame samples (~90 s at 90 Hz — well past any sane window).</summary>
    private const int FrameCapacity = 8192;

    /// <summary>
    /// HOT-PATH GATE. A plain static field (never a property) so the per-step check the whole mod
    /// pays for is a single load+branch. Written once per frame by the host from
    /// <c>[Perf] Enabled &amp;&amp; [Perf] Attribution</c>; read by <see cref="BeginStep"/>,
    /// <see cref="TickGuard.Run"/> and every <see cref="Scope"/>.
    /// </summary>
    internal static bool StepsActive;

    // ---- per-step attribution records --------------------------------------------------------

    /// <summary>
    /// One named mod step's accumulators. A CLASS (not a struct in a dictionary) so the hot path
    /// mutates the record in place after a single lookup, with no copy-back and no boxing.
    /// </summary>
    private sealed class Step
    {
        public Step(string name) => Name = name;

        public readonly string Name;

        /// <summary>Seconds accumulated in the CURRENT frame (reset at every frame roll).</summary>
        public double FrameSeconds;

        /// <summary>Calls in the current frame (a per-instance step like VRCard runs many times).</summary>
        public int FrameCalls;

        /// <summary>Seconds accumulated over the current summary window.</summary>
        public double WindowSeconds;

        /// <summary>Worst SINGLE-FRAME total this step reached in the window.</summary>
        public double WindowWorstFrameSeconds;

        /// <summary>Frames in the window in which this step ran at all.</summary>
        public int WindowFrames;

        /// <summary>Calls in the window.</summary>
        public int WindowCalls;

        /// <summary>Snapshot of <see cref="FrameSeconds"/> taken when a SPIKE line is composed.</summary>
        public double SpikeSeconds;
    }

    private static readonly Dictionary<string, Step> Steps = new(64);

    /// <summary>Parallel ORDERED list of the same records: the per-frame roll walks this instead of
    /// the dictionary, which keeps the roll allocation-free and cache-friendly.</summary>
    private static readonly List<Step> StepOrder = new(64);

    /// <summary>Scratch used to rank steps for the STEPS / SPIKE lines (reused, never re-allocated).</summary>
    private static readonly List<Step> Ranked = new(64);

    /// <summary>Nesting depth, so a step inside a step is attributed to both but counted ONCE in the
    /// mod total (otherwise nested guards would double-count and the mod share would exceed 100 %).</summary>
    private static int _depth;

    // ---- frame pacing -------------------------------------------------------------------------

    private static readonly float[] FrameMs = new float[FrameCapacity];
    private static readonly float[] SortScratch = new float[FrameCapacity];
    private static int _frameCount;   // samples written into the ring this window (capped at capacity)
    private static int _frameWrite;   // ring write cursor

    private static double _windowModSeconds;      // depth-0 mod time summed over the window
    private static double _windowModWorstSeconds; // worst single frame's depth-0 mod time
    private static int _overBudget;               // frames slower than one budget
    private static int _spikeFrames;              // frames past the spike threshold
    private static int _spikesSuppressed;         // spike lines dropped by the rate limit

    private static float _windowStart;
    private static float _lastSpikeLogTime;

    // ---- allocation pressure -------------------------------------------------------------------

    private static long _lastHeapBytes;
    private static long _windowAllocBytes;
    private static int _gc0, _gc1, _gc2;          // window-start collection counts

    // ---- display budget -------------------------------------------------------------------------

    private static float _refreshHz;
    private static string _refreshSource = "unknown";
    private static float _budgetSeconds = 1f / 90f;
    private static float _budgetResolvedAt = float.NegativeInfinity;

    /// <summary>False until one full frame has been sampled — the very first sampled frame carries
    /// the load/init hitch and would poison every percentile of the first window.</summary>
    private static bool _firstSampleDone;

    // ---- output ---------------------------------------------------------------------------------

    private static readonly StringBuilder Sb = new(512);
    private static PerfHost? _host;
    private static bool _capsLogged;

    // ==========================================================================================
    //  Step attribution API
    // ==========================================================================================

    /// <summary>
    /// Open a measured step. Returns 0 when measurement is off, which is the caller's cue to skip
    /// <see cref="EndStep"/> entirely — so an inactive monitor costs one branch, not a timer read.
    /// ALWAYS pair with <see cref="EndStep"/> in a <c>finally</c> (or use <see cref="Scope"/>):
    /// a throw that skipped the close would leak <see cref="_depth"/> for the rest of the frame.
    /// </summary>
    internal static long BeginStep()
    {
        if (!StepsActive)
            return 0L;
        _depth++;
        return Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Close a measured step opened with <see cref="BeginStep"/> and fold its duration into
    /// <paramref name="name"/>'s per-frame and per-window accumulators. Only DEPTH-0 steps feed the
    /// mod-total (nested steps are still attributed individually — they just must not be counted
    /// twice in the "how much of the frame was ours" number).
    /// </summary>
    internal static void EndStep(string name, long begin)
    {
        if (begin == 0L)
            return;
        long end = Stopwatch.GetTimestamp();
        double seconds = (end - begin) / (double)Stopwatch.Frequency;
        if (_depth > 0)
            _depth--;
        if (_depth == 0)
            _frameModSeconds += seconds;

        if (!Steps.TryGetValue(name, out Step step))
        {
            // Cold path only: a step name is a compile-time literal, so this runs at most once per
            // named step for the whole session.
            step = new Step(name);
            Steps[name] = step;
            StepOrder.Add(step);
        }
        step.FrameSeconds += seconds;
        step.FrameCalls++;
    }

    /// <summary>Depth-0 mod time accumulated in the CURRENT frame (rolled by the host).</summary>
    private static double _frameModSeconds;

    /// <summary>
    /// <c>using</c>-friendly measured scope for driver bodies that are NOT routed through
    /// <see cref="TickGuard"/> (the raw <c>Update()</c> bodies: the rig, the cards driver, the net
    /// avatar driver, the wall fade, the per-card and per-hand components). A STRUCT, so an
    /// inactive monitor allocates nothing and an active one allocates nothing either; and unlike
    /// TickGuard it does NOT swallow exceptions, so wrapping an existing body in it cannot change
    /// that body's error semantics.
    /// </summary>
    internal readonly struct Measure : IDisposable
    {
        private readonly string _name;
        private readonly long _begin;

        internal Measure(string name)
        {
            _name = name;
            _begin = BeginStep();
        }

        public void Dispose() => EndStep(_name, _begin);
    }

    /// <summary>Open a measured <see cref="Measure"/> scope for <paramref name="name"/>.</summary>
    internal static Measure Scope(string name) => new(name);

    // ==========================================================================================
    //  Lifecycle
    // ==========================================================================================

    /// <summary>
    /// Attach the sampling host to the mod's own Core root. Idempotent; safe to call before VR is
    /// up (the monitor is useful on the flat screen too, and the refresh-rate probe simply retries
    /// until an XR display exists).
    /// </summary>
    internal static void Install(GameObject root)
    {
        PerfConfig.Bind();
        if (_host != null)
            return;
        _host = root.AddComponent<PerfHost>();
        ResetWindow(Time.unscaledTime);
    }

    /// <summary>Drop the host and every record (hot-reload teardown; never throws).</summary>
    internal static void Shutdown()
    {
        StepsActive = false;
        if (_host != null)
        {
            UnityEngine.Object.Destroy(_host);
            _host = null;
        }
        Steps.Clear();
        StepOrder.Clear();
        Ranked.Clear();
        _depth = 0;
        _frameModSeconds = 0d;
        _capsLogged = false;
        _firstSampleDone = false;
        _budgetResolvedAt = float.NegativeInfinity;
        _refreshHz = 0f;
    }

    // ==========================================================================================
    //  Per-frame sampling (driven by PerfHost.Update, execution order -30000)
    // ==========================================================================================

    private static void Sample()
    {
        PerfConfig.Bind();
        bool on = PerfConfig.Enabled.Value;
        // Written for NEXT frame's steps; the value that was live during frame N-1 is the one whose
        // measurements we are closing out right now, which is exactly what we want.
        StepsActive = on && PerfConfig.Attribution.Value;
        if (!on)
        {
            // Keep the accumulators clean so switching the monitor back on mid-session does not
            // report a window that spans the off period.
            if (_frameCount != 0 || _frameModSeconds != 0d)
                ResetWindow(Time.unscaledTime);
            _frameModSeconds = 0d;
            _depth = 0;
            return;
        }

        if (!_capsLogged)
        {
            _capsLogged = true;
            XrProbe.Resolve();
            LogCapabilities();
        }

        float now = Time.unscaledTime;
        RefreshBudget();

        // --- close out frame N-1 -------------------------------------------------------------
        float dt = Time.unscaledDeltaTime;
        double modSeconds = _frameModSeconds;

        // The very first sampled frame carries the whole load/init hitch and would poison every
        // percentile for the first window; skip it rather than explain it in every log.
        if (!_firstSampleDone)
        {
            _firstSampleDone = true;
            _frameModSeconds = 0d;
            _depth = 0;
            return;
        }

        FrameMs[_frameWrite] = dt * 1000f;
        _frameWrite = (_frameWrite + 1) % FrameCapacity;
        if (_frameCount < FrameCapacity)
            _frameCount++;

        if (dt > _budgetSeconds)
            _overBudget++;
        _windowModSeconds += modSeconds;
        if (modSeconds > _windowModWorstSeconds)
            _windowModWorstSeconds = modSeconds;

        float spikeThreshold = _budgetSeconds * Mathf.Max(1.2f, PerfConfig.SpikeBudgetFactor.Value);
        bool spike = dt > spikeThreshold;
        if (spike)
        {
            _spikeFrames++;
            if (PerfConfig.SpikeLines.Value)
            {
                // Floored, never zero: a rate of 0 would mean "no limit", and an unlimited spike
                // line is one log write PER FRAME during exactly the stretch we are diagnosing —
                // the instrumentation would become the stall. Suppressed spikes are still counted.
                float perSecond = Mathf.Clamp(PerfConfig.SpikeMaxPerSecond.Value, 0.1f, 20f);
                if (now - _lastSpikeLogTime >= 1f / perSecond)
                {
                    _lastSpikeLogTime = now;
                    LogSpike(dt, modSeconds, spikeThreshold);
                }
                else
                {
                    _spikesSuppressed++;
                }
            }
        }

        SampleAllocations();

        // --- roll: frame N-1's step totals become window totals, accumulators clear for N -----
        for (int i = 0; i < StepOrder.Count; i++)
        {
            Step s = StepOrder[i];
            if (s.FrameSeconds > 0d)
            {
                s.WindowSeconds += s.FrameSeconds;
                s.WindowFrames++;
                s.WindowCalls += s.FrameCalls;
                if (s.FrameSeconds > s.WindowWorstFrameSeconds)
                    s.WindowWorstFrameSeconds = s.FrameSeconds;
            }
            s.FrameSeconds = 0d;
            s.FrameCalls = 0;
        }
        _frameModSeconds = 0d;
        // A step body that threw between Begin and End would otherwise leave the depth counter
        // stuck above 0 and silently stop the mod-total from ever accumulating again.
        _depth = 0;

        // --- periodic summary -----------------------------------------------------------------
        float interval = Mathf.Clamp(PerfConfig.SummaryIntervalSeconds.Value, 5f, 600f);
        if (now - _windowStart >= interval)
        {
            LogSummary(now - _windowStart);
            ResetWindow(now);
        }
    }

    private static void SampleAllocations()
    {
        if (!PerfConfig.Allocations.Value)
            return;
        // GetTotalMemory(false) does NOT collect — on Mono it reads the allocator's running total,
        // which is cheap enough for a per-frame sample. A NEGATIVE delta means a collection landed
        // between the two reads, so only positive deltas are credited as allocation; the collection
        // itself is visible in the gen counters below.
        long heap = GC.GetTotalMemory(false);
        long delta = heap - _lastHeapBytes;
        if (delta > 0)
            _windowAllocBytes += delta;
        _lastHeapBytes = heap;
    }

    private static void ResetWindow(float now)
    {
        _windowStart = now;
        _frameCount = 0;
        _frameWrite = 0;
        _windowModSeconds = 0d;
        _windowModWorstSeconds = 0d;
        _overBudget = 0;
        _spikeFrames = 0;
        _spikesSuppressed = 0;
        _windowAllocBytes = 0L;
        _lastHeapBytes = GC.GetTotalMemory(false);
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        XrProbe.Mark();
        for (int i = 0; i < StepOrder.Count; i++)
        {
            Step s = StepOrder[i];
            s.WindowSeconds = 0d;
            s.WindowWorstFrameSeconds = 0d;
            s.WindowFrames = 0;
            s.WindowCalls = 0;
        }
    }

    // ==========================================================================================
    //  Display budget
    // ==========================================================================================

    /// <summary>
    /// Resolve the frame BUDGET from the actual display refresh rate rather than a hardcoded
    /// 72/90 Hz — the same mod runs on a 72 Hz Quest link, a 90 Hz VDXR session and a 120 Hz
    /// desktop, and a budget that does not follow the display makes every over-budget count
    /// meaningless. Sources are tried in order of authority and the winner is named in the log so
    /// a wrong budget can never be mistaken for a real regression. Re-resolved once per window
    /// (Quest changes refresh rate at runtime), never per frame.
    /// </summary>
    private static void RefreshBudget()
    {
        float now = Time.unscaledTime;
        if (_refreshHz > 0f && now - _budgetResolvedAt < 10f)
            return; // already resolved recently
        _budgetResolvedAt = now;

        float hz = XrProbe.TryGetRefreshRate(out string source) ? XrProbe.LastRefreshHz : 0f;
        if (hz <= 0f)
        {
            // Last resort: the desktop's own mode. Wrong for an HMD, so it is labelled as such.
            hz = Screen.currentResolution.refreshRate;
            source = "Screen.currentResolution (NO XR display — desktop rate)";
        }
        if (hz <= 0f)
        {
            hz = 90f;
            source = "fallback default (nothing reported a rate)";
        }
        _refreshHz = hz;
        _refreshSource = source;
        _budgetSeconds = 1f / hz;
    }

    // ==========================================================================================
    //  Log lines
    // ==========================================================================================

    private static void LogCapabilities()
    {
        StringBuilder sb = Sb;
        sb.Length = 0;
        sb.Append("CAPS: instrumentation up. ")
          .Append("attribution=").Append(PerfConfig.Attribution.Value ? "on" : "off")
          .Append(" alloc=").Append(PerfConfig.Allocations.Value ? "on" : "off")
          .Append(" xr=").Append(PerfConfig.XrStats.Value ? "on" : "off")
          .Append(" | timer=Stopwatch(").Append(Stopwatch.IsHighResolution ? "high-res " : "LOW-RES ")
          .Append(Stopwatch.Frequency).Append("Hz)")
          .Append(" | counters: ").Append(XrProbe.Describe())
          .Append(". Counters marked n/a are NOT exposed by this runtime — they are reported as n/a, "
                  + "never as zero.");
        VRLog.Info(Scope0, sb.ToString());
    }

    private static void LogSummary(float windowSeconds)
    {
        if (_frameCount <= 1)
            return;

        Array.Copy(FrameMs, SortScratch, _frameCount);
        Array.Sort(SortScratch, 0, _frameCount);

        double sum = 0d;
        for (int i = 0; i < _frameCount; i++)
            sum += SortScratch[i];
        float mean = (float)(sum / _frameCount);
        float p50 = Percentile(0.50f);
        float p95 = Percentile(0.95f);
        float p99 = Percentile(0.99f);
        float max = SortScratch[_frameCount - 1];
        float budgetMs = _budgetSeconds * 1000f;

        StringBuilder sb = Sb;
        sb.Length = 0;
        sb.Append("FRAME ").Append(windowSeconds.ToString("F1")).Append("s n=").Append(_frameCount)
          .Append(" | display ").Append(_refreshHz.ToString("F1")).Append("Hz budget ")
          .Append(budgetMs.ToString("F2")).Append("ms (").Append(_refreshSource).Append(')')
          .Append(" | frametime mean ").Append(mean.ToString("F2"))
          .Append(" p50 ").Append(p50.ToString("F2"))
          .Append(" p95 ").Append(p95.ToString("F2"))
          .Append(" p99 ").Append(p99.ToString("F2"))
          .Append(" max ").Append(max.ToString("F2")).Append("ms")
          .Append(" | over-budget ").Append(_overBudget).Append('/').Append(_frameCount)
          .Append(" (").Append((100f * _overBudget / _frameCount).ToString("F1")).Append("%)")
          .Append(" | spikes ").Append(_spikeFrames);
        if (_spikesSuppressed > 0)
            sb.Append(" (").Append(_spikesSuppressed).Append(" lines rate-limited)");

        // The mod's own share — the number that decides whether to keep optimizing the mod at all.
        double modMeanMs = _windowModSeconds * 1000d / _frameCount;
        sb.Append(" | mod ").Append(modMeanMs.ToString("F2")).Append("ms/frame avg, worst ")
          .Append((_windowModWorstSeconds * 1000d).ToString("F2")).Append("ms (")
          .Append((100d * modMeanMs / Mathf.Max(0.001f, mean)).ToString("F1")).Append("% of frame time)");
        if (!PerfConfig.Attribution.Value)
            sb.Append(" [attribution OFF — mod share not measured]");

        if (PerfConfig.Allocations.Value)
        {
            sb.Append(" | gc gen0+").Append(GC.CollectionCount(0) - _gc0)
              .Append(" gen1+").Append(GC.CollectionCount(1) - _gc1)
              .Append(" gen2+").Append(GC.CollectionCount(2) - _gc2)
              .Append(" alloc+").Append((_windowAllocBytes / 1048576d).ToString("F1")).Append("MB (")
              .Append((_windowAllocBytes / 1048576d / Mathf.Max(0.001f, windowSeconds)).ToString("F2"))
              .Append("MB/s)");
        }

        if (PerfConfig.XrStats.Value)
        {
            sb.Append(" | xr ");
            XrProbe.AppendWindowStats(sb);
        }

        VRLog.Info(Scope0, sb.ToString());
        LogSteps(windowSeconds);
    }

    private static void LogSteps(float windowSeconds)
    {
        if (!PerfConfig.Attribution.Value || StepOrder.Count == 0)
            return;

        Ranked.Clear();
        for (int i = 0; i < StepOrder.Count; i++)
        {
            if (StepOrder[i].WindowSeconds > 0d)
                Ranked.Add(StepOrder[i]);
        }
        if (Ranked.Count == 0)
            return;
        Ranked.Sort(CompareWindowDesc);

        int top = Mathf.Clamp(PerfConfig.TopSteps.Value, 1, Mathf.Min(20, Ranked.Count));
        StringBuilder sb = Sb;
        sb.Length = 0;
        sb.Append("STEPS ").Append(windowSeconds.ToString("F1")).Append("s — the mod's own named steps, "
                  + "ranked by total time in the window (avg = per frame the step ran):");
        for (int i = 0; i < top; i++)
        {
            Step s = Ranked[i];
            double avgMs = s.WindowSeconds * 1000d / Mathf.Max(1, s.WindowFrames);
            sb.Append(i == 0 ? " " : " | ")
              .Append(s.Name).Append(' ')
              .Append(avgMs.ToString("F3")).Append("ms avg, worst ")
              .Append((s.WindowWorstFrameSeconds * 1000d).ToString("F2")).Append("ms, ")
              .Append((s.WindowSeconds * 1000d / Mathf.Max(0.001f, windowSeconds)).ToString("F1"))
              .Append("ms/s, frames ").Append(s.WindowFrames);
            if (s.WindowCalls != s.WindowFrames)
                sb.Append(", calls ").Append(s.WindowCalls);
        }
        sb.Append(" | (").Append(Ranked.Count).Append(" named steps measured)");
        VRLog.Info(Scope0, sb.ToString());
    }

    private static int CompareWindowDesc(Step a, Step b) => b.WindowSeconds.CompareTo(a.WindowSeconds);

    /// <summary>
    /// One line per over-budget frame — the judder itself, not its average. Names the worst mod
    /// steps IN THAT FRAME, and states plainly when the mod is not the culprit: a spike with a
    /// near-zero mod total is a game/GPU/compositor spike and no amount of mod optimization will
    /// move it, which is exactly the conclusion worth reading off a log rather than guessing at.
    /// </summary>
    private static void LogSpike(float dtSeconds, double modSeconds, float thresholdSeconds)
    {
        StringBuilder sb = Sb;
        sb.Length = 0;
        sb.Append("SPIKE frame ").Append(Time.frameCount).Append(": ")
          .Append((dtSeconds * 1000f).ToString("F2")).Append("ms = ")
          .Append((dtSeconds / _budgetSeconds).ToString("F1")).Append("x the ")
          .Append((_budgetSeconds * 1000f).ToString("F2")).Append("ms budget (threshold ")
          .Append((thresholdSeconds * 1000f).ToString("F2")).Append("ms)");

        if (PerfConfig.Attribution.Value)
        {
            sb.Append(" | mod ").Append((modSeconds * 1000d).ToString("F2")).Append("ms of it");
            Ranked.Clear();
            for (int i = 0; i < StepOrder.Count; i++)
            {
                Step s = StepOrder[i];
                if (s.FrameSeconds <= 0d)
                    continue;
                s.SpikeSeconds = s.FrameSeconds;
                Ranked.Add(s);
            }
            if (Ranked.Count > 0)
            {
                Ranked.Sort(CompareSpikeDesc);
                int top = Mathf.Min(4, Ranked.Count);
                sb.Append(" | worst steps:");
                for (int i = 0; i < top; i++)
                {
                    sb.Append(i == 0 ? " " : ", ").Append(Ranked[i].Name).Append(' ')
                      .Append((Ranked[i].SpikeSeconds * 1000d).ToString("F2")).Append("ms");
                }
            }
            // 25 % is a deliberately generous bar: below it the mod cannot be the story even if
            // every one of its steps were free.
            if (modSeconds < dtSeconds * 0.25f)
                sb.Append(" | VERDICT: NOT the mod — the mod accounts for under a quarter of this "
                          + "frame, so the stall is game/GPU/compositor side");
        }
        else
        {
            sb.Append(" | (attribution off — no step breakdown)");
        }

        if (PerfConfig.Allocations.Value)
            sb.Append(" | heap ").Append((GC.GetTotalMemory(false) / 1048576d).ToString("F1")).Append("MB");

        VRLog.Info(Scope0, sb.ToString());
    }

    private static int CompareSpikeDesc(Step a, Step b) => b.SpikeSeconds.CompareTo(a.SpikeSeconds);

    /// <summary>Nearest-rank percentile over the sorted scratch (no interpolation — a frame time
    /// that actually occurred is more useful evidence than an average of two that did not).</summary>
    private static float Percentile(float q)
    {
        int idx = Mathf.Clamp(Mathf.CeilToInt(q * _frameCount) - 1, 0, _frameCount - 1);
        return SortScratch[idx];
    }

    // ==========================================================================================
    //  Host component
    // ==========================================================================================

    /// <summary>
    /// The sampling pump. <see cref="DefaultExecutionOrder"/> −30000 puts its <c>Update</c> ahead
    /// of every mod driver, which is what makes the frame boundary exact (see the class doc). The
    /// body is fully guarded: instrumentation that can throw would be worse than no
    /// instrumentation, since it would take the rest of the frame's Updates with it.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    private sealed class PerfHost : MonoBehaviour
    {
        private bool _faulted;

        private void Update()
        {
            if (_faulted)
                return;
            try
            {
                Sample();
            }
            catch (Exception e)
            {
                _faulted = true;
                StepsActive = false;
                VRLog.Error(Scope0, "Instrumentation threw and DISABLED ITSELF (measuring must never "
                                    + $"break the frame): {e}");
            }
        }
    }

    // ==========================================================================================
    //  XR counter probe
    // ==========================================================================================

    /// <summary>
    /// Reflection-resolved access to the XR display's own counters.
    ///
    /// <para>WHY REFLECTION AND NOT DIRECT CALLS: the shape of <c>UnityEngine.XR.XRStats</c> moved
    /// between Unity XR versions — it has been a static class with parameterless <c>out</c> methods
    /// AND an extension class over <see cref="XRDisplaySubsystem"/>, living in either
    /// UnityEngine.XRModule or UnityEngine.VRModule depending on the version. Binding to one shape
    /// at compile time would make the mod fail to load on the other. Resolving by name once at
    /// startup binds whatever this install actually has, and reports the rest as n/a.</para>
    ///
    /// <para>NOT A PER-CALL REFLECTION COST: each resolved method is turned into a typed OPEN
    /// delegate via <see cref="Delegate.CreateDelegate(Type, MethodInfo)"/>, so the call site is a
    /// normal delegate invoke, not <c>MethodInfo.Invoke</c> (which would box the <c>out</c>
    /// argument on every call — allocation inside the very instrumentation hunting allocations).</para>
    /// </summary>
    private static class XrProbe
    {
        private delegate bool DisplayIntStat(XRDisplaySubsystem display, out int value);
        private delegate bool DisplayFloatStat(XRDisplaySubsystem display, out float value);
        private delegate bool FloatStat(out float value);

        private static readonly List<XRDisplaySubsystem> Displays = new(2);

        private static DisplayIntStat? _dropped;
        private static DisplayIntStat? _presented;
        private static DisplayFloatStat? _gpuTime;
        private static DisplayFloatStat? _motionToPhoton;
        private static DisplayFloatStat? _displayRefresh;
        private static FloatStat? _gpuTimeGlobal;
        private static PropertyInfo? _deviceRefreshRate;
        private static bool _resolved;

        private static int _markDropped = -1;
        private static int _markPresented = -1;

        /// <summary>The refresh rate the last successful probe reported (Hz).</summary>
        internal static float LastRefreshHz;

        internal static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            Type? stats = FindType("UnityEngine.XR.XRStats");
            if (stats != null)
            {
                _dropped = Bind<DisplayIntStat>(stats, "TryGetDroppedFrameCount");
                _presented = Bind<DisplayIntStat>(stats, "TryGetFramePresentCount");
                _gpuTime = Bind<DisplayFloatStat>(stats, "TryGetGPUTimeLastFrame");
                _motionToPhoton = Bind<DisplayFloatStat>(stats, "TryGetMotionToPhoton");
                if (_gpuTime == null)
                    _gpuTimeGlobal = Bind<FloatStat>(stats, "TryGetGPUTimeLastFrame");
            }

            _displayRefresh = Bind<DisplayFloatStat>(typeof(XRDisplaySubsystem), "TryGetDisplayRefreshRate");

            Type? device = FindType("UnityEngine.XR.XRDevice");
            if (device != null)
                _deviceRefreshRate = device.GetProperty("refreshRate", BindingFlags.Public | BindingFlags.Static);
        }

        private static Type? FindType(string fullName)
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? t = asm.GetType(fullName, throwOnError: false);
                    if (t != null)
                        return t;
                }
            }
            catch (Exception)
            {
                // A dynamic assembly that refuses reflection must not take the probe down.
            }
            return null;
        }

        private static T? Bind<T>(Type owner, string method) where T : Delegate
        {
            try
            {
                MethodInfo[] candidates = owner.GetMethods(BindingFlags.Public | BindingFlags.Static
                                                           | BindingFlags.Instance);
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].Name != method)
                        continue;
                    if (Delegate.CreateDelegate(typeof(T), candidates[i], throwOnBindFailure: false)
                        is T bound)
                        return bound;
                }
            }
            catch (Exception)
            {
                // Signature mismatch on this Unity version — the counter is simply reported n/a.
            }
            return null;
        }

        private static XRDisplaySubsystem? Display()
        {
            Displays.Clear();
            SubsystemManager.GetInstances(Displays);
            for (int i = 0; i < Displays.Count; i++)
            {
                if (Displays[i] != null && Displays[i].running)
                    return Displays[i];
            }
            return Displays.Count > 0 ? Displays[0] : null;
        }

        /// <summary>Refresh rate from the most authoritative source that answers; false = none did.</summary>
        internal static bool TryGetRefreshRate(out string source)
        {
            Resolve();
            XRDisplaySubsystem? d = Display();
            if (d != null && _displayRefresh != null && _displayRefresh(d, out float hz) && hz > 1f)
            {
                LastRefreshHz = hz;
                source = "XRDisplaySubsystem.TryGetDisplayRefreshRate";
                return true;
            }
            if (_deviceRefreshRate != null)
            {
                try
                {
                    if (_deviceRefreshRate.GetValue(null) is float dev && dev > 1f)
                    {
                        LastRefreshHz = dev;
                        source = "XRDevice.refreshRate";
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Property throws before a session exists — fall through to the next source.
                }
            }
            source = string.Empty;
            return false;
        }

        /// <summary>Snapshot the cumulative counters at a window boundary so the next summary can
        /// report DELTAS (the raw totals are meaningless; the per-window change is the signal).</summary>
        internal static void Mark()
        {
            if (!PerfConfig.IsBound || !PerfConfig.XrStats.Value)
                return;
            Resolve();
            XRDisplaySubsystem? d = Display();
            _markDropped = d != null && _dropped != null && _dropped(d, out int dr) ? dr : -1;
            _markPresented = d != null && _presented != null && _presented(d, out int pr) ? pr : -1;
        }

        internal static void AppendWindowStats(StringBuilder sb)
        {
            Resolve();
            XRDisplaySubsystem? d = Display();
            if (d == null)
            {
                sb.Append("no XR display subsystem (flat screen / VR not running)");
                return;
            }

            sb.Append("dropped ");
            if (_dropped != null && _dropped(d, out int dr) && _markDropped >= 0)
                sb.Append('+').Append(dr - _markDropped);
            else
                sb.Append("n/a");

            sb.Append(" presented ");
            if (_presented != null && _presented(d, out int pr) && _markPresented >= 0)
                sb.Append('+').Append(pr - _markPresented);
            else
                sb.Append("n/a");

            sb.Append(" gpu ");
            if (_gpuTime != null && _gpuTime(d, out float gpu))
                sb.Append(gpu.ToString("F2")).Append("ms");
            else if (_gpuTimeGlobal != null && _gpuTimeGlobal(out float gpu2))
                sb.Append(gpu2.ToString("F2")).Append("ms");
            else
                sb.Append("n/a");

            sb.Append(" motion2photon ");
            if (_motionToPhoton != null && _motionToPhoton(d, out float m2p))
                sb.Append((m2p * 1000f).ToString("F1")).Append("ms");
            else
                sb.Append("n/a");
        }

        /// <summary>Which counters actually bound on this install (the CAPS line).</summary>
        internal static string Describe()
        {
            Resolve();
            return $"dropped={(_dropped != null ? "ok" : "n/a")} "
                   + $"presented={(_presented != null ? "ok" : "n/a")} "
                   + $"gpuTime={(_gpuTime != null || _gpuTimeGlobal != null ? "ok" : "n/a")} "
                   + $"motionToPhoton={(_motionToPhoton != null ? "ok" : "n/a")} "
                   + $"refreshRate={(_displayRefresh != null ? "display" : _deviceRefreshRate != null ? "device" : "n/a")}";
        }
    }
}
