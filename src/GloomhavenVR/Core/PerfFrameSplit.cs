using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WHERE THE FRAME ACTUALLY GOES — the measurement that decides the 2026-07 judder
/// investigation, added after the pixel-budget hypothesis was refuted on hardware.
///
/// <para>WHY THIS EXISTS. <see cref="PerfMonitor"/> measures the frame INTERVAL and the mod's own
/// share of it. Both were unambiguous: the mod costs ~2 % of the frame, and the frame is ~23 ms
/// against an 11.11 ms budget. What neither could answer is WHICH LAYER is spending the other
/// 98 %. The one counter that looked like an answer — the XR runtime's <c>gpu</c> figure — read
/// almost exactly the frame interval in every window, and a "GPU time" that equals total frame
/// time and does not move when the pixel-sample budget is cut 11× is not reporting GPU busy time;
/// it is reporting the interval or a wait. So a preset sweep on hardware moved it by 7 % and
/// taught us nothing. This class replaces that guess with a decomposition that CANNOT be fooled,
/// because it is built out of the mod's own clock reads at known points in Unity's frame.</para>
///
/// <para>THE DECOMPOSITION. Unity's main thread runs a frame as
/// <c>Update → LateUpdate → render loop (cull + submit, per camera) → present/wait → next
/// Update</c>. Three timestamps bracket that:</para>
/// <list type="number">
/// <item><b>logic span</b> — from <see cref="PerfMonitor"/>'s host <c>Update</c> (execution order
/// −30000, the first thing in the frame) to this class's tail <c>LateUpdate</c> (order +30000,
/// the last). Everything the game and the mod COMPUTE lives in here.</item>
/// <item><b>render-loop span</b> — from the first <see cref="Camera.onPreCull"/> to the last
/// <see cref="Camera.onPostRender"/> of the same frame. This is the main thread inside Unity's
/// rendering: culling plus draw-call submission (and, when the render thread's queue is full,
/// the main thread's own blocking inside those submits). It is also measured PER CAMERA, which
/// is the number that prices the scenario camera's third full-scene render into a sink texture
/// nothing reads — and each camera's figure is split again at <see cref="Camera.onPreRender"/>
/// into CULL (visibility determination; scales with how many renderers exist and pass the culling
/// mask) and SUBMIT (draw calls, including a forward camera's depth-texture prepass and every
/// shadow map). "The render loop owns the frame" is true of both halves and their levers are
/// different, so the split is measured rather than argued.</item>
/// <item><b>blocked</b> — the remainder of the frame interval. The main thread is neither
/// computing nor submitting: it is waiting for the GPU, for the compositor's next present slot,
/// or for the XR runtime. A frame that is nearly all "blocked" is NOT a CPU problem, and no
/// amount of draw-call or logic optimisation will move it.</item>
/// </list>
///
/// <para>READING IT. The verdict line states which of the three owns the frame, in words, so the
/// next hardware log answers the question by itself:</para>
/// <list type="bullet">
/// <item><c>logic ≈ frame</c> → the game's own <c>Update</c>/<c>LateUpdate</c> is the wall.</item>
/// <item><c>render ≈ frame</c> → draw-call submission is the wall. MultiPass doubling the head
/// camera's passes is then the direct cause, and the sink renders are pure waste on top.</item>
/// <item><c>blocked ≈ frame</c> → the CPU is idle and we are waiting on the GPU or the
/// compositor. Note that a runtime that has locked the app to half rate produces exactly this
/// signature WITHOUT the GPU being full, which is why <see cref="PerfMonitor"/> now shouts when
/// the display rate changes: below a rate lock every timing is quantised to the new interval and
/// comparisons across the boundary are meaningless.</item>
/// </list>
///
/// <para>FRAMETIMINGMANAGER. Unity's own <c>FrameTimingManager</c> is queried too, because when
/// it binds it gives a real GPU-timer number to cross-check the XR runtime's figure against. On
/// 2021.3 its <c>FrameTiming</c> struct carries only <c>cpuFrameTime</c> and <c>gpuFrameTime</c>
/// (the per-thread main/render split arrived in a later Unity), and the whole subsystem returns
/// NOTHING unless the player was built with frame-timing stats enabled — which is a build-time
/// decision we do not control. It is therefore a BONUS, never the primary: when it does not
/// bind, every one of its figures prints <c>n/a</c> and the spans above still answer the
/// question on their own. The call is latched behind its own try/catch so a struct-shape
/// mismatch on some other Unity version degrades to n/a instead of taking the instrumentation
/// down.</para>
///
/// <para>THE VIEWPOINT (see <c>PerfFrameSplit.Zoom.cs</c>). Every recorded frame also carries WHERE
/// IT WAS SEEN FROM: the head's height above the board plane, its distance from the board centre,
/// and a rolling estimate of how many renderers the head camera kept after culling. Because those
/// samples share this class's index space, the SPLIT line can cut the window's frames into distance
/// thirds and report each third's own p50 frametime, logic and render — which answers "how does
/// frame time vary with viewing distance" from ONE window, where it previously took a hand-made
/// correlation between 10 s heartbeats and 30 s windows. That file states the per-frame cost.</para>
///
/// <para>COST. Two <see cref="Stopwatch.GetTimestamp"/> reads per frame for the logic span, two
/// per camera render — three with <c>[Perf] CullSubmitSplit</c> on, which is what pays for the
/// cull/submit seam and is why it is a switch and defaults off (four cameras in a scenario), one
/// int-keyed dictionary lookup per camera
/// render, and no steady-state allocation at all — camera records are keyed by
/// <c>GetInstanceID()</c> precisely so the hot path never touches <c>Camera.name</c>, which
/// allocates a fresh string on every read. Single-digit microseconds. Off, it is one static bool
/// test per frame and the hooks are not even registered.</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. Reads clocks, writes log lines. It adds one component and
/// two static camera-callback subscriptions, both dropped in <see cref="Shutdown"/>. It never
/// touches a game object, game state or wire traffic.</para>
/// </summary>
internal static partial class PerfFrameSplit
{
    private const string Scope0 = "Perf";

    /// <summary>Ring capacity for the per-frame span samples — matches PerfMonitor's frame ring.</summary>
    private const int Capacity = 8192;

    // ---- per-camera records ------------------------------------------------------------------

    /// <summary>
    /// One camera's main-thread render cost. Keyed by instance id (never by name: reading
    /// <c>UnityEngine.Object.name</c> allocates a string on EVERY access, and this runs per camera
    /// per frame). The name is captured once, when the record is created.
    /// </summary>
    private sealed class CamRec
    {
        public CamRec(string name) => Name = name;

        public readonly string Name;

        /// <summary>Seconds this camera spent between onPreCull and onPostRender in the window.</summary>
        public double WindowSeconds;

        /// <summary>
        /// Of <see cref="WindowSeconds"/>, the part spent CULLING — onPreCull → onPreRender, which
        /// is exactly Unity's visibility determination for this camera/pass. Split out because the
        /// two halves have DIFFERENT levers: culling scales with the number of renderers that
        /// exist and pass the culling mask, submission with the number of draw calls the visible
        /// ones produce. "The render loop owns the frame" does not say which, and picking the
        /// wrong one is a wasted round on hardware.
        /// </summary>
        public double WindowCullSeconds;

        /// <summary>
        /// Of <see cref="WindowSeconds"/>, the part spent SUBMITTING — onPreRender → onPostRender.
        /// Includes the built-in forward path's depth-texture prepass and any shadow-map passes,
        /// because both happen inside this camera's render between those two callbacks.
        /// </summary>
        public double WindowSubmitSeconds;

        /// <summary>Render PASSES in the window (MultiPass makes the head camera two per frame).</summary>
        public int WindowPasses;

        /// <summary>Frames in the window in which this camera rendered at all.</summary>
        public int WindowFrames;

        /// <summary>Passes so far in the CURRENT frame (rolled into the window each frame).</summary>
        public int FramePasses;

        /// <summary>Seconds so far in the CURRENT frame.</summary>
        public double FrameSeconds;

        /// <summary>Culling seconds so far in the CURRENT frame.</summary>
        public double FrameCullSeconds;

        /// <summary>Submission seconds so far in the CURRENT frame.</summary>
        public double FrameSubmitSeconds;
    }

    private static readonly Dictionary<int, CamRec> Cameras = new(8);
    private static readonly List<CamRec> CameraOrder = new(8);
    private static readonly List<CamRec> Ranked = new(8);

    // ---- per-frame state ---------------------------------------------------------------------

    /// <summary>HOT-PATH GATE — a plain static field so the camera hooks cost one load+branch.</summary>
    private static bool _active;

    private static long _logicStart;      // set at the top of the frame (PerfMonitor's host)
    private static long _logicEnd;        // set by the tail component's LateUpdate
    private static bool _logicEndSeen;

    private static long _renderFirst;     // first onPreCull of the frame
    private static long _renderLast;      // last onPostRender of the frame
    private static bool _renderSeen;

    private static long _camOpen;         // open onPreCull timestamp
    private static int _camOpenId;
    private static long _camCullDone;     // that camera's onPreRender timestamp (0 = not seen)

    // ---- window accumulators -------------------------------------------------------------------

    private static readonly float[] LogicMs = new float[Capacity];
    private static readonly float[] RenderMs = new float[Capacity];
    private static readonly float[] SortScratch = new float[Capacity];
    private static int _count;

    private static double _logicSum, _renderSum;
    private static float _logicMax, _renderMax;
    private static int _passSum;          // total camera passes over the window

    /// <summary>
    /// A single frame longer than this counts as a STALL, not as a frame: a synchronous scene
    /// load, an asset-bundle decompress, or Apparance regenerating a room. Hardware 2026-07 caught
    /// two of them at 1067 ms and 974 ms INSIDE otherwise ordinary windows, and one of those
    /// single samples moved the window's logic mean from ~0.1 ms to 1.87 ms — which then read as
    /// "the mean is above the p95", a reading that looks like a broken percentile and is not one.
    /// Stalls are counted and named separately rather than silently averaged in.
    /// </summary>
    private const float StallMs = 100f;

    private static int _logicStalls, _renderStalls;

    // ---- Unity FrameTimingManager (bonus; n/a when the player disabled frame-timing stats) -----

    private static readonly FrameTiming[] Timings = new FrameTiming[1];
    private static double _ftCpuSum, _ftGpuSum;
    private static int _ftSamples;
    private static bool _ftFaulted;
    private static string _ftFault = string.Empty;

    private static PerfSplitTail? _tail;
    private static bool _hooked;

    /// <summary>Whether <see cref="OnPreRender"/> is currently subscribed (tracked apart from
    /// <see cref="_hooked"/> because [Perf] CullSubmitSplit switches it on its own).</summary>
    private static bool _splitHooked;

    // ==========================================================================================
    //  Lifecycle
    // ==========================================================================================

    /// <summary>
    /// Attach the tail component. Idempotent. The camera hooks are (un)registered lazily by
    /// <see cref="SetActive"/> so a disabled measurement costs nothing at all, not even a
    /// delegate invoke per camera.
    /// </summary>
    internal static void Install(GameObject root)
    {
        if (_tail == null)
            _tail = root.AddComponent<PerfSplitTail>();
    }

    /// <summary>Drop the component, the hooks and every record (hot-reload teardown; never throws).</summary>
    internal static void Shutdown()
    {
        SetActive(false);
        if (_tail != null)
        {
            UnityEngine.Object.Destroy(_tail);
            _tail = null;
        }
        Cameras.Clear();
        CameraOrder.Clear();
        Ranked.Clear();
        ResetRoster();
        ResetWindow();
        _ftFaulted = false;
        _ftFault = string.Empty;
    }

    /// <summary>
    /// Arm or disarm the measurement. Registering the camera callbacks only while armed is what
    /// keeps the OFF state free — an unregistered <see cref="Camera.onPreCull"/> is not a null
    /// check per camera, it is no call at all.
    ///
    /// <para>SUBSCRIPTION SYMMETRY. All three handlers are STATIC methods of this static class, so
    /// each <c>+=</c> adds the one and only delegate that method can produce and each <c>-=</c>
    /// removes it. The <c>_hooked</c> latch makes the pair idempotent, so no number of rig builds,
    /// scene loads or settings flips can grow the invocation list: it is either exactly these
    /// three entries or none. Nothing here is subscribed per camera.</para>
    /// </summary>
    private static void SetActive(bool on)
    {
        // The cull/submit seam is switchable on its own ([Perf] CullSubmitSplit), so its
        // subscription is tracked separately from the other two rather than assumed to follow
        // them — an asymmetric -= is how a hook leak starts.
        bool splitOn = on && PerfConfig.CullSubmitSplitOn;
        if (splitOn != _splitHooked)
        {
            if (splitOn)
                Camera.onPreRender += OnPreRender;
            else
                Camera.onPreRender -= OnPreRender;
            _splitHooked = splitOn;
        }
        if (on == _hooked)
        {
            _active = on;
            return;
        }
        if (on)
        {
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
        }
        else
        {
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
        }
        _hooked = on;
        _active = on;
    }

    // ==========================================================================================
    //  Per-frame sampling — driven by PerfMonitor's host, which owns the frame boundary
    // ==========================================================================================

    /// <summary>
    /// Close out the frame that just ended and open the next one. Called from
    /// <see cref="PerfMonitor"/>'s host <c>Update</c> (execution order −30000), so "now" is the
    /// first instant of frame N and every span recorded below belongs to frame N−1, whose logic,
    /// rendering and present have all completed. <paramref name="record"/> is false for the very
    /// first sampled frame (which carries the load hitch) and while the monitor is off.
    /// <paramref name="frameMs"/> is that frame's complete wall time, passed in rather than re-read
    /// so the zoom buckets quote the SAME frametime the FRAME line does, for the same frame.
    /// </summary>
    internal static void RollFrame(bool enabled, bool record, float frameMs)
    {
        SetActive(enabled);
        long now = Stopwatch.GetTimestamp();

        if (enabled && record && _logicStart != 0L)
        {
            double freq = Stopwatch.Frequency;

            // Logic span: host Update (first) → tail LateUpdate (last). A frame in which the tail
            // never ran (component disabled mid-frame, scene teardown) contributes nothing rather
            // than a bogus zero. Everything derived per frame — including the camera-pass total —
            // is gated on the SAME condition, so no reported average is ever a sum over one
            // population divided by the count of another.
            bool recorded = _logicEndSeen && _count < Capacity;
            if (recorded)
            {
                float logicMs = (float)((_logicEnd - _logicStart) / freq * 1000d);
                float renderMs = _renderSeen
                    ? (float)((_renderLast - _renderFirst) / freq * 1000d)
                    : 0f;
                LogicMs[_count] = logicMs;
                RenderMs[_count] = renderMs;
                // Same slot, same gate: the viewpoint is only ever written for a frame whose logic
                // and render spans were also written, which is what lets the ZOOM clause quote
                // logic/render medians per distance third without aligning two populations.
                SampleView(_count, frameMs);
                _count++;
                _logicSum += logicMs;
                _renderSum += renderMs;
                if (logicMs > _logicMax)
                    _logicMax = logicMs;
                if (renderMs > _renderMax)
                    _renderMax = renderMs;
                if (logicMs >= StallMs)
                    _logicStalls++;
                if (renderMs >= StallMs)
                    _renderStalls++;
            }

            for (int i = 0; i < CameraOrder.Count; i++)
            {
                CamRec c = CameraOrder[i];
                if (recorded && c.FramePasses > 0)
                {
                    c.WindowSeconds += c.FrameSeconds;
                    c.WindowCullSeconds += c.FrameCullSeconds;
                    c.WindowSubmitSeconds += c.FrameSubmitSeconds;
                    c.WindowPasses += c.FramePasses;
                    c.WindowFrames++;
                    _passSum += c.FramePasses;
                }
                c.FramePasses = 0;
                c.FrameSeconds = 0d;
                c.FrameCullSeconds = 0d;
                c.FrameSubmitSeconds = 0d;
            }

            SampleFrameTimings();
        }
        else
        {
            for (int i = 0; i < CameraOrder.Count; i++)
            {
                CameraOrder[i].FramePasses = 0;
                CameraOrder[i].FrameSeconds = 0d;
                CameraOrder[i].FrameCullSeconds = 0d;
                CameraOrder[i].FrameSubmitSeconds = 0d;
            }
        }

        _logicStart = now;
        _logicEnd = now;
        _logicEndSeen = false;
        _renderSeen = false;
        _camOpenId = 0;
        _camCullDone = 0L;
    }

    /// <summary>Tail hook: the last main-thread instant before Unity's render loop.</summary>
    private static void MarkLogicEnd()
    {
        if (!_active)
            return;
        _logicEnd = Stopwatch.GetTimestamp();
        _logicEndSeen = true;
    }

    private static void OnPreCull(Camera cam)
    {
        if (!_active || cam == null)
            return;
        long t = Stopwatch.GetTimestamp();
        // Only renders that happen AFTER the logic phase belong to Unity's render loop. A camera
        // driven manually from a LateUpdate (the mod's stereo compositor does exactly that) would
        // otherwise start the "render loop" span inside the logic span, double-counting the
        // overlap and understating the blocked time. Such renders are still attributed to their
        // camera below — they just do not move the span boundary.
        if (_logicEndSeen)
        {
            if (!_renderSeen)
            {
                _renderFirst = t;
                _renderSeen = true;
            }
            _renderLast = t;   // a render with no matching post-render still bounds the span
        }
        _camOpen = t;
        _camOpenId = cam.GetInstanceID();
        _camCullDone = 0L;
    }

    /// <summary>
    /// Unity fires this AFTER culling and BEFORE the camera's rendering, so it is the one seam
    /// that separates the render loop's two halves. Culling scales with how many renderers EXIST
    /// and pass the culling mask; submission scales with how many DRAW CALLS the survivors
    /// produce (and a forward camera's depth-texture prepass and every shadow map are on the
    /// submission side). The SPLIT line's "render loop owns the frame" verdict cannot distinguish
    /// them, and their levers are different, so the distinction is measured rather than guessed.
    /// </summary>
    private static void OnPreRender(Camera cam)
    {
        if (!_active || cam == null)
            return;
        if (cam.GetInstanceID() != _camOpenId)
            return; // out-of-order/nested render — leave the split unattributed rather than wrong
        _camCullDone = Stopwatch.GetTimestamp();
    }

    private static void OnPostRender(Camera cam)
    {
        if (!_active || cam == null)
            return;
        long t = Stopwatch.GetTimestamp();
        if (_renderSeen)
            _renderLast = t;
        int id = cam.GetInstanceID();
        if (id != _camOpenId)
            return; // a nested/foreign render closed out of order — do not attribute it
        _camOpenId = 0;

        if (!Cameras.TryGetValue(id, out CamRec rec))
        {
            // Cold path, once per camera per session: this is the ONLY place Camera.name is read.
            rec = new CamRec(cam.name);
            Cameras[id] = rec;
            CameraOrder.Add(rec);
        }
        double freq = Stopwatch.Frequency;
        rec.FrameSeconds += (t - _camOpen) / freq;
        // No onPreRender for this pass (a camera that culled but never rendered, or a hook order
        // we did not see) → the whole pass counts as submission rather than inventing a split.
        long cullDone = _camCullDone > 0L ? _camCullDone : _camOpen;
        rec.FrameCullSeconds += (cullDone - _camOpen) / freq;
        rec.FrameSubmitSeconds += (t - cullDone) / freq;
        rec.FramePasses++;
        _camCullDone = 0L;
    }

    /// <summary>
    /// Unity's own frame timings, when the player was built with them enabled. Latched: the first
    /// throw (a <c>FrameTiming</c> field this Unity does not have) disables the probe for the
    /// session and is reported once, rather than throwing every frame into the caller's guard.
    /// </summary>
    private static void SampleFrameTimings()
    {
        if (_ftFaulted)
            return;
        try
        {
            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, Timings);
            if (got == 0u)
                return; // stats disabled in this build — stays n/a, never a fake zero
            _ftCpuSum += Timings[0].cpuFrameTime;
            _ftGpuSum += Timings[0].gpuFrameTime;
            _ftSamples++;
        }
        catch (Exception e)
        {
            _ftFaulted = true;
            _ftFault = e.GetType().Name;
        }
    }

    // ==========================================================================================
    //  Window reporting
    // ==========================================================================================

    internal static void ResetWindow()
    {
        _count = 0;
        _viewPrepared = false;   // the distance ordering below belongs to the window that just closed
        _logicSum = 0d;
        _renderSum = 0d;
        _logicMax = 0f;
        _renderMax = 0f;
        _logicStalls = 0;
        _renderStalls = 0;
        _passSum = 0;
        _ftCpuSum = 0d;
        _ftGpuSum = 0d;
        _ftSamples = 0;
        for (int i = 0; i < CameraOrder.Count; i++)
        {
            CamRec c = CameraOrder[i];
            c.WindowSeconds = 0d;
            c.WindowCullSeconds = 0d;
            c.WindowSubmitSeconds = 0d;
            c.WindowPasses = 0;
            c.WindowFrames = 0;
        }
    }

    /// <summary>True once the window holds enough samples for the SPLIT line to mean anything.</summary>
    internal static bool HasWindow => _count > 1;

    /// <summary>
    /// Compose the <c>[Perf] SPLIT</c> line: the three spans, the per-camera render cost, and a
    /// verdict in words naming which layer owns the frame. <paramref name="frameMeanMs"/> is
    /// PerfMonitor's own mean frame interval for the same window, so the two lines are directly
    /// comparable.
    /// </summary>
    internal static void AppendSplit(System.Text.StringBuilder sb, float windowSeconds, float frameMeanMs)
    {
        float logicMean = (float)(_logicSum / _count);
        float renderMean = (float)(_renderSum / _count);
        // p50 is reported alongside the mean because ONE stall frame is enough to make the mean
        // meaningless: hardware 2026-07 produced "logic 1.87 mean, p95 0.24" from a single 1067 ms
        // sample in a 703-frame window. That is not a broken percentile — it is a mean that no
        // longer describes any frame. The median does, and printing both makes the difference
        // visible instead of leaving it to be misread as an instrumentation bug.
        float logicP50 = Percentile(LogicMs, 0.50f);
        float renderP50 = Percentile(RenderMs, 0.50f);
        float logicP95 = Percentile(LogicMs, 0.95f);
        float renderP95 = Percentile(RenderMs, 0.95f);
        // The render loop runs INSIDE neither span's overlap: logic ends before the first cull.
        float blockedMean = Mathf.Max(0f, frameMeanMs - logicMean - renderMean);

        sb.Append("SPLIT ").Append(windowSeconds.ToString("F1")).Append("s n=").Append(_count)
          .Append(" — where the ").Append(frameMeanMs.ToString("F2"))
          .Append("ms frame goes on the MAIN THREAD")
          .Append(" | logic (Update→LateUpdate) ").Append(logicMean.ToString("F2"))
          .Append(" p50 ").Append(logicP50.ToString("F2"))
          .Append(" p95 ").Append(logicP95.ToString("F2"))
          .Append(" max ").Append(_logicMax.ToString("F2")).Append("ms (")
          .Append(Share(logicMean, frameMeanMs)).Append(')')
          .Append(" | render loop (cull+submit) ").Append(renderMean.ToString("F2"))
          .Append(" p50 ").Append(renderP50.ToString("F2"))
          .Append(" p95 ").Append(renderP95.ToString("F2"))
          .Append(" max ").Append(_renderMax.ToString("F2")).Append("ms (")
          .Append(Share(renderMean, frameMeanMs)).Append(')')
          .Append(" | blocked (waiting on GPU/compositor) ").Append(blockedMean.ToString("F2"))
          .Append("ms (").Append(Share(blockedMean, frameMeanMs)).Append(')');

        AppendStalls(sb, logicMean, logicP50, renderMean, renderP50);

        sb.Append(" | camera passes/frame ")
          .Append((_passSum / (float)_count).ToString("F1"));
        AppendCameras(sb);
        AppendFrameTimings(sb);
        AppendZoom(sb);

        sb.Append(" | VERDICT: ").Append(Verdict(logicMean, renderMean, blockedMean, frameMeanMs));
    }

    /// <summary>
    /// Name the stall frames instead of letting them hide inside a mean. A window that contains a
    /// scene load, an asset-bundle decompress or a room regeneration is not a window about steady
    /// state, and the difference is invisible in the mean alone — it shows up as the mean sitting
    /// ABOVE the p95, which reads like a broken percentile and is not one.
    /// </summary>
    private static void AppendStalls(System.Text.StringBuilder sb,
        float logicMean, float logicP50, float renderMean, float renderP50)
    {
        int stalls = _logicStalls + _renderStalls;
        bool skewed = logicMean > logicP50 * 2f + 0.05f || renderMean > renderP50 * 2f + 0.05f;
        if (stalls == 0 && !skewed)
            return;

        sb.Append(" | STALLS: ").Append(_logicStalls).Append(" logic and ").Append(_renderStalls)
          .Append(" render frame(s) over ").Append(StallMs.ToString("F0")).Append("ms in this "
                  + "window — a synchronous scene load, an asset-bundle decompress or a room "
                  + "regeneration, NOT steady-state cost");
        if (skewed)
        {
            sb.Append(". THE MEANS ABOVE ARE SKEWED BY THEM: read p50, not the mean. A mean that "
                      + "sits above its own p95 is arithmetic, not a broken percentile — a single "
                      + "1 s sample in a 700-frame window adds 1.5 ms to the mean and nothing at "
                      + "all to the median");
        }
    }

    private static void AppendCameras(System.Text.StringBuilder sb)
    {
        Ranked.Clear();
        for (int i = 0; i < CameraOrder.Count; i++)
        {
            if (CameraOrder[i].WindowPasses > 0)
                Ranked.Add(CameraOrder[i]);
        }
        if (Ranked.Count == 0)
        {
            sb.Append(" | per camera: none rendered this window");
            return;
        }
        Ranked.Sort(CompareCameraDesc);
        // The split is reported only if it was actually measured for the whole window. Printing
        // "cull 0.00 + submit X" for a window in which the seam was off would read as a measured
        // zero, and a measured zero is the one thing this instrumentation must never invent.
        bool split = _splitHooked;
        sb.Append(split
            ? " | per camera (main-thread, avg per frame it rendered — 'cull' is onPreCull→"
              + "onPreRender, 'submit' is onPreRender→onPostRender incl. any depth prepass and "
              + "shadow maps):"
            : " | per camera (main-thread cull+submit, avg per frame it rendered; the cull/submit "
              + "seam is OFF — switch [Perf] CullSubmitSplit on to break these down):");
        for (int i = 0; i < Ranked.Count && i < 8; i++)
        {
            CamRec c = Ranked[i];
            float frames = Mathf.Max(1, c.WindowFrames);
            sb.Append(i == 0 ? " " : ", ").Append(c.Name).Append(' ')
              .Append((c.WindowSeconds * 1000d / frames).ToString("F2")).Append("ms");
            if (split)
            {
                sb.Append(" (cull ").Append((c.WindowCullSeconds * 1000d / frames).ToString("F2"))
                  .Append(" + submit ").Append((c.WindowSubmitSeconds * 1000d / frames).ToString("F2"))
                  .Append(')');
            }
            sb.Append(" x").Append((c.WindowPasses / frames).ToString("F1")).Append(" pass");
        }
    }

    private static void AppendFrameTimings(System.Text.StringBuilder sb)
    {
        sb.Append(" | Unity FrameTimingManager ");
        if (_ftFaulted)
        {
            sb.Append("n/a (probe threw ").Append(_ftFault).Append(" — this Unity's FrameTiming has a "
                      + "different shape; the spans above do not depend on it)");
            return;
        }
        if (_ftSamples == 0)
        {
            sb.Append("n/a (this player was built without frame-timing stats — GetLatestTimings "
                      + "returns no samples. NOT zero: the counter simply does not exist here)");
            return;
        }
        sb.Append("cpu ").Append((_ftCpuSum / _ftSamples).ToString("F2"))
          .Append("ms gpu ").Append((_ftGpuSum / _ftSamples).ToString("F2"))
          .Append("ms (n=").Append(_ftSamples).Append("; per-thread main/render split is n/a on this "
                  + "Unity — its FrameTiming carries only cpu/gpu totals)");
    }

    /// <summary>
    /// The whole point of the line: say, in words, which layer owns the frame — and say plainly
    /// when the answer is "none of the ones the mod can move".
    /// </summary>
    private static string Verdict(float logic, float render, float blocked, float frame)
    {
        if (frame <= 0.01f)
            return "no frame time to attribute";
        float l = logic / frame, r = render / frame, b = blocked / frame;
        if (l >= 0.5f)
            return $"MAIN-THREAD LOGIC owns the frame ({l * 100f:F0}%). The game's own Update/LateUpdate "
                   + "is the wall — draw calls and pixels are not. The mod's share of that is on the "
                   + "STEPS line above; if it is small, this is the game's own code and the mod cannot "
                   + "move it.";
        if (r >= 0.35f)
            return $"the RENDER LOOP owns the frame ({r * 100f:F0}%). The main thread is inside culling "
                   + "and draw-call submission, so the levers are the NUMBER of things submitted, not "
                   + "their pixel cost: MultiPass renders the head camera twice, and every extra camera "
                   + "listed above is a whole additional scene submission. WHICH HALF is the next question, "
                   + "and the two have different levers — cull scales with how many renderers exist and "
                   + "pass the culling mask (see the SCENE line's per-layer counts), submit with how many "
                   + "draw calls the visible ones produce (materials, shadow passes, the forward depth "
                   + "prepass; see the SCENE and GFX lines). "
                   + (PerfConfig.CullSubmitSplitOn
                       ? "The per-camera cull/submit figures above answer it."
                       : "Switch [Perf] CullSubmitSplit on to have the per-camera figures above answer it.");
        if (b >= 0.5f)
            return $"the main thread is BLOCKED for {b * 100f:F0}% of the frame — it is neither computing "
                   + "nor submitting, it is waiting. That is the GPU, the XR compositor, or a runtime "
                   + "rate lock (check the display Hz on the FRAME line: a halved rate produces exactly "
                   + "this signature with the GPU nowhere near full). CPU-side optimisation cannot move "
                   + "a frame that looks like this.";
        return $"no single layer dominates (logic {l * 100f:F0}%, render {r * 100f:F0}%, blocked "
               + $"{b * 100f:F0}%) — the frame is spread across all three.";
    }

    private static string Share(float part, float whole) =>
        whole <= 0.01f ? "n/a" : (100f * part / whole).ToString("F0") + "%";

    private static int CompareCameraDesc(CamRec a, CamRec b) => b.WindowSeconds.CompareTo(a.WindowSeconds);

    private static float Percentile(float[] source, float q)
    {
        Array.Copy(source, SortScratch, _count);
        Array.Sort(SortScratch, 0, _count);
        int idx = Mathf.Clamp(Mathf.CeilToInt(q * _count) - 1, 0, _count - 1);
        return SortScratch[idx];
    }

    /// <summary>
    /// HOW MUCH THERE IS TO DRAW. <c>UnityStats</c> (batches, draw calls, tris) is editor-only, so
    /// the closest runtime proxy is a census of the renderers that COULD be submitted: total,
    /// enabled, and how many the culling actually kept last frame. Together with the camera-pass
    /// count above it prices a scene submission — an extra camera costs roughly "visible × its own
    /// culling", which is exactly the question the sink-render experiment asks.
    ///
    /// <para>DELIBERATELY ONCE PER WINDOW, NEVER PER FRAME: <c>FindObjectsOfType</c> walks every
    /// loaded object and allocates the array, which is far too expensive for a frame budget and
    /// would make the instrumentation the stutter. At a 30 s cadence it is one hitch of a few
    /// milliseconds per window, and it is skipped entirely while the census is switched off.</para>
    ///
    /// <para>IT COUNTS uGUI GRAPHICS TOO, AND THAT HALF EXISTS BECAUSE ITS ABSENCE COST A SESSION
    /// (2026-08-09). <c>Graphic</c> does NOT derive from <see cref="Renderer"/> — a uGUI Image is a
    /// <c>CanvasRenderer</c> — so a census of Renderers is structurally BLIND to every UI object in
    /// the game. In the multiplayer capture that blindness was total: the renderer count sat at
    /// 1543-1558 for forty minutes while the frame rate slid from 78 fps to 8 fps on BOTH machines,
    /// which read as "the scene is not growing, so this is not a mod leak" — and it was a mod leak,
    /// of Image objects being parented onto a mirrored board's world-space canvas four times a
    /// second and never removed.</para>
    ///
    /// <para>WHY THAT LEAK HID FROM EVERY OTHER NUMBER HERE, WHICH IS THE POINT OF THIS COMMENT.
    /// Unity rebuilds canvases in <c>PostLateUpdate.PlayerUpdateCanvases</c> — AFTER the tail
    /// LateUpdate that closes the logic span, and BEFORE the camera callbacks that open the render
    /// span. So <c>Canvas.SendWillRenderCanvases</c> and <c>BuildBatch</c> land in NEITHER measured
    /// span: their whole cost falls into the "blocked (waiting on GPU/compositor)" remainder, whose
    /// name then actively misleads. That is exactly what the capture shows — logic flat, render
    /// loop flat at ~2.3 ms, camera passes flat at 4.0, renderers flat, and "blocked" climbing 5 ms
    /// → 83 ms. A growing graphic count is the ONE number that separates "the compositor is
    /// struggling" from "we are rebuilding an ever-larger canvas", and it is one line of census.</para>
    /// </summary>
    internal static void AppendSceneCensus(System.Text.StringBuilder sb)
    {
        Renderer[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        }
        catch (Exception e)
        {
            sb.Append(" | scene census n/a (").Append(e.GetType().Name).Append(')');
            return;
        }
        // The walk doubles as the ZOOM axis's SEED: every renderer's visibility is written into the
        // shadow array and the total into the running counter, so the per-frame slice sampler starts
        // correct instead of ramping up over its first sweep. Costs one bool store per renderer on a
        // walk that was already happening.
        bool[] shadow = RosterShadow(all.Length);
        int enabled = 0, visible = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            bool vis = false;
            if (r != null && r.enabled)
            {
                enabled++;
                vis = r.isVisible;
                if (vis)
                    visible++;
            }
            shadow[i] = vis;
        }
        SeedRoster(all, visible, shadow);
        sb.Append(" | scene census: ").Append(all.Length).Append(" renderer(s), ")
          .Append(enabled).Append(" enabled, ").Append(visible)
          .Append(" visible to at least one camera (ONE INSTANT, sampled once per window — the "
                  + "per-frame cost of this walk would itself be a stutter, which is why this "
                  + "number shows no trend across windows and the ZOOM clause above carries the "
                  + "per-frame estimate instead. This walk is also that estimate's seed)");
        AppendGraphicCensus(sb);
    }

    /// <summary>The uGUI half of <see cref="AppendSceneCensus"/> — see there for why it exists.
    /// Separate method so a throw inside it cannot cost the renderer census that already
    /// succeeded.</summary>
    private static void AppendGraphicCensus(System.Text.StringBuilder sb)
    {
        UnityEngine.UI.Graphic[] graphics;
        try
        {
            graphics = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Graphic>();
        }
        catch (Exception e)
        {
            sb.Append("; uGUI census n/a (").Append(e.GetType().Name).Append(')');
            return;
        }

        int enabled = 0, mod = 0;
        for (int i = 0; i < graphics.Length; i++)
        {
            UnityEngine.UI.Graphic g = graphics[i];
            if (g == null || !g.enabled)
                continue;
            enabled++;
            // MOD-OWNED means "under one of our own world-space hosts", which is where a mirrored
            // panel's clone lives. Splitting them out is what turns "UI is growing" into "OUR UI is
            // growing" without a second capture: the game's own HUD churns legitimately, ours must
            // not. Resolved by layer, which costs an int compare — no name string, no allocation.
            if (g.gameObject.layer == VRLayers.ModLayer)
                mod++;
        }

        sb.Append("; uGUI: ").Append(graphics.Length).Append(" graphic(s), ")
          .Append(enabled).Append(" enabled, ").Append(mod)
          .Append(" on the mod's own layer. A Graphic is NOT a Renderer, so the count "
                  + "above cannot see these — and canvas rebuild runs after LateUpdate and before "
                  + "the render loop, so its cost shows up as 'blocked', not as logic or submit. "
                  + "A mod count that CLIMBS window over window with a steady scenario is an object "
                  + "leak onto a world-space canvas, which is precisely the shape that collapsed "
                  + "both machines of the 2026-08-09 multiplayer session");
    }

    /// <summary>One clause for the startup CAPS line.</summary>
    internal static string Describe() =>
        "frameSplit=ok (own clocks: logic span, render-loop span, per-camera passes"
        + (PerfConfig.CullSubmitSplitOn ? " split into cull/submit" : "; cull/submit split OFF")
        + ") "
        + "zoomAxis=ok (head height/distance vs the board focus per frame; visible-renderer estimate "
        + (PerfConfig.SceneCensus == null || PerfConfig.SceneCensus.Value
            ? $"from a {RenderersPerFrame}-renderer slice per frame, seeded by the census walk"
            : "OFF — [Perf] SceneCensus is off, so there is no roster to sample")
        + ") "
        + "frameTimingManager=probed-per-frame";

    // ==========================================================================================
    //  Tail component
    // ==========================================================================================

    /// <summary>
    /// The LAST main-thread instant of the logic phase. <see cref="DefaultExecutionOrder"/>
    /// +30000 puts its <c>LateUpdate</c> after every other component's, which — paired with
    /// PerfMonitor's host at −30000 — brackets the frame's whole logic phase exactly. Fully
    /// guarded: instrumentation that throws would take the rest of the frame's LateUpdates with
    /// it, which is strictly worse than no instrumentation.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    private sealed class PerfSplitTail : MonoBehaviour
    {
        private bool _faulted;

        private void LateUpdate()
        {
            if (_faulted)
                return;
            try
            {
                MarkLogicEnd();
            }
            catch (Exception e)
            {
                _faulted = true;
                VRLog.Error(Scope0, $"Frame-split tail threw and DISABLED ITSELF: {e}");
            }
        }
    }
}
