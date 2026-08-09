using System;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

// ==================================================================================================
//  THE ZOOM AXIS — WHERE THE HEAD WAS STANDING WHEN THE FRAME COST WHAT IT COST.
//  (a partial of PerfFrameSplit: the per-frame arrays here are index-aligned with LogicMs/RenderMs
//   and share the same _count, which is the whole reason this lives inside that class and not
//   beside it. Two parallel populations with two different sample gates cannot be bucketed.)
//
//  WHY IT EXISTS. The 2026-08 analysis (.planning/perf-zoomed-out.md §1.1) had to PROVE that the
//  frame rate is view-dependent by reading the 10-second [Core] Heartbeat lines — which print the
//  head pose — against the 30-second [Perf] SPLIT windows, and matching them up by hand. That is a
//  correlation across two different cadences, made by eye, and it is the load-bearing claim of the
//  whole document: "the only variable left is where the head is". Nothing in the instrument
//  recorded the viewpoint, so the instrument could not make that claim itself.
//
//  It also had to concede (§1.5) that the ONE number that is genuinely a zoom axis — how many
//  renderers the head camera keeps after culling — was sampled once per 30 s window and therefore
//  showed no trend at all: 1925 renderers in an overview window against 2129 in a close-in one is
//  noise, not signal, because each figure is a single instant.
//
//  WHAT THIS ADDS, IN ONE SENTENCE: every frame now carries WHERE IT WAS SEEN FROM, so one window
//  can answer "how does frame time vary with viewing distance" on its own line, with no second log
//  and no correlation by hand.
//
//  THE THREE NUMBERS, AND WHY EACH ONE IS CHEAP ENOUGH TO BE ALWAYS-ON WITH THE PROFILER:
//
//  1. HEIGHT above the board plane and DISTANCE from the board centre. Both come from the game's
//     own orbit focus (CameraController.s_CameraController.FocusPoint — the point the rig itself is
//     built at, VRRigDriver.cs) and the head camera's world position. Cost: ONE interop call for
//     the head position, ONE for the singleton's alive-check, and a Vector3 subtract. The camera's
//     Transform is cached and only re-fetched when the rig hands us a different camera instance,
//     which is a reference compare, not a Unity null compare.
//
//  2. VISIBLE RENDERERS, per frame, WITHOUT a per-frame full-scene walk. A walk of ~3000 renderers
//     every frame would itself be the stutter — that is exactly why the census is once per window.
//     So the census's own walk (which already runs, once per window, when [Perf] SceneCensus is on)
//     is reused as a SEED: it records each renderer's visibility into a parallel bool array and the
//     total into a running counter. From then on each frame re-reads a SLICE of the roster
//     (RenderersPerFrame) round-robin and adjusts the running counter by the difference. The number
//     is therefore a live estimate that is at most one sweep stale — 3050 renderers at 64/frame is
//     48 frames, about half a second at 90 Hz — and it costs a fixed 64 iterations per frame no
//     matter how large the scene is. There is no ramp: the seed makes it correct from frame one of
//     the window.
//
//     WHOSE VISIBILITY IS IT? Renderer.isVisible means "rendered by ANY camera last frame". In a
//     SCENARIO that is the head camera alone: the game's own cameras are retargeted to a sink and
//     have their culling mask zeroed for the duration of their own render
//     (WorldUI/FlatScreen.3.Desktop.cs, OnScrubPreCull), so they contribute nothing. That is
//     precisely why this count is the zoom axis that matters — the head camera carries a blanket
//     mask and sees a far wider swathe of the board than the flat game ever rendered, and
//     everything it declares visible keeps its animators and particle systems simulating inside the
//     measured LOGIC span. Outside a scenario the number is "visible to any camera" and means less.
//
//  3. THE BUCKETING, which is the point. At the window close the frames are sorted by distance and
//     split into thirds, and each third reports its own p50 frametime, p50 logic and p50 render.
//     p50 and not the mean, for the reason the SPLIT line already documents at length: one stall
//     frame moves a mean and moves nothing else. Near-third against far-third, in one line, is the
//     measurement §1.1 of the analysis had to assemble by hand.
//
//  COST, STATED. Per frame, with the profiler ON: 2 interop calls for the pose, 2 × 64 = 128 for
//  the renderer slice (a Unity alive-check and an isVisible read each), 4 float array writes, ZERO
//  allocations. The whole sample is wrapped in PerfMonitor.Scope("Perf.ZoomSample") so it does not
//  have to be believed — it prices ITSELF on the [Perf] STEPS line, in the same units as every
//  other step, and if it ever ranks near the mod's real work that is a defect visible in the log.
//  With the profiler OFF the sample is not reached at all (RollFrame's recorded branch is not
//  entered), so the cost is the same static bool test the rest of this class already pays.
//
//  WHAT IT DOES NOT DO. It changes nothing the player sees: it reads two transforms and a
//  visibility flag and writes log text. No game object, no game state, no wire traffic.
// ==================================================================================================

internal static partial class PerfFrameSplit
{
    // ---- tuning ----------------------------------------------------------------------------------

    /// <summary>
    /// How many renderers the per-frame slice re-reads. A FIXED count, not a fraction of the scene:
    /// the whole design goal is that the per-frame cost does not grow with the scene, because the
    /// scenes this exists to measure are exactly the large ones. 64 sweeps a 3000-renderer room in
    /// ~0.5 s at 90 Hz, which is far finer than the 30 s window it feeds.
    /// </summary>
    private const int RenderersPerFrame = 64;

    /// <summary>
    /// How far the head must travel (world units) inside a window before "nearest third" and
    /// "farthest third" describe different views. Below this the thirds are three samples of ONE
    /// viewpoint and the line says so instead of reporting a spread that is really just noise.
    /// </summary>
    private const float MeaningfulSpanUnits = 1.5f;

    /// <summary>Frames a window needs before the thirds are worth printing at all.</summary>
    private const int MinZoomFrames = 30;

    /// <summary>
    /// Below this near→far frametime difference the line refuses to call the frame view-dependent.
    /// One millisecond is a deliberately blunt floor and it is the right kind of blunt: this app is
    /// compositor-quantised (the SPLIT line's own note), so frametime moves in whole 11.11 ms
    /// budgets and a sub-millisecond difference between two thirds is neither felt nor trustworthy.
    /// </summary>
    private const float ZoomNoiseFloorMs = 1f;

    // ---- per-frame samples (SAME index space as LogicMs/RenderMs — written under the same gate) ---

    /// <summary>Head height above the board plane, world units. NaN = no head/board this frame.</summary>
    private static readonly float[] ViewHeight = new float[Capacity];

    /// <summary>Head distance from the board centre, world units. NaN = no head/board this frame.</summary>
    private static readonly float[] ViewDist = new float[Capacity];

    /// <summary>The frame's wall time in ms, copied in so the buckets can report it without having
    /// to reach into PerfMonitor's ring — whose sample gate is NOT this one.</summary>
    private static readonly float[] ViewFrameMs = new float[Capacity];

    /// <summary>Live estimate of the renderers the head camera kept, or −1 when no roster exists.</summary>
    private static readonly float[] ViewVisible = new float[Capacity];

    private static readonly float[] ViewScratch = new float[Capacity];
    private static readonly float[] ViewKeys = new float[Capacity];
    private static readonly int[] ViewIndex = new int[Capacity];

    // ---- the sampled roster ----------------------------------------------------------------------

    /// <summary>The census's own FindObjectsOfType array, retained as the sampling roster. Renderers
    /// created since it was taken are invisible to the estimate until the next window refreshes
    /// it — the census total printed on the same line is what says whether the scene grew.</summary>
    private static Renderer[]? _roster;

    /// <summary>Last observed visibility per roster slot; the running total is maintained against it.</summary>
    private static bool[] _rosterSeen = Array.Empty<bool>();

    private static int _rosterCursor;
    private static int _rosterVisible;
    private static bool _rosterReady;

    // ---- head/board resolution -------------------------------------------------------------------

    private static Camera? _viewCam;
    private static Transform? _viewTf;

    // ---- window summary (computed once per window, on demand) --------------------------------------

    private static bool _viewPrepared;
    private static int _viewN;
    private static int _viewVisibleN;
    private static float _viewHeightP50, _viewHeightMin, _viewHeightMax;
    private static float _viewDistP50, _viewDistMin, _viewDistMax;
    private static float _viewVisibleP50;

    // ==============================================================================================
    //  Per-frame sampling
    // ==============================================================================================

    /// <summary>
    /// Record the viewpoint for the frame being closed out, into <paramref name="slot"/> — the same
    /// index <see cref="LogicMs"/> and <see cref="RenderMs"/> just received, which is what makes the
    /// bucketing below able to quote logic and render medians per distance third.
    ///
    /// <para>ONE FRAME OF OFFSET, NAMED. This runs at the TOP of frame N (PerfMonitor's host, order
    /// −30000) and is attributed to frame N−1, whose logic, render and present have all completed.
    /// The head pose it reads is therefore N's, not N−1's: at 90 Hz that is 11 ms of head motion,
    /// i.e. centimetres, against an axis measured in whole world units. Sampling it in the tail
    /// LateUpdate instead would cost a second interop call per frame to remove an error three orders
    /// of magnitude below the signal. Renderer visibility, by contrast, is read here BECAUSE this is
    /// the top of frame N: isVisible then reflects frame N−1's culling exactly.</para>
    /// </summary>
    private static void SampleView(int slot, float frameMs)
    {
        // Priced on the STEPS line under its own name, so the cost claim in this file's header is
        // checkable from any hardware log rather than taken on trust.
        using (PerfMonitor.Scope("Perf.ZoomSample"))
        {
            ViewFrameMs[slot] = frameMs;
            ViewHeight[slot] = float.NaN;
            ViewDist[slot] = float.NaN;
            ViewVisible[slot] = -1f;

            if (TryReadView(out Vector3 head, out Vector3 focus))
            {
                ViewHeight[slot] = head.y - focus.y;
                ViewDist[slot] = (head - focus).magnitude;
            }
            if (StepRoster())
                ViewVisible[slot] = _rosterVisible;
        }
    }

    /// <summary>
    /// Head world position and the board's plane/centre. The board frame is the game's own orbit
    /// focus: it is the point the VR rig is built at (VRRigDriver), the point every world panel is
    /// anchored to (WorldUI/PanelLayout.TryGetAnchor) and the plane the comfort clamp measures eye
    /// height above (Rig/Comfort). Using anything else here would measure a different board than the
    /// rest of the mod does. Two interop calls; the Transform is cached against camera IDENTITY (a
    /// reference compare, not a Unity null compare, which would itself be an interop call).
    /// </summary>
    private static bool TryReadView(out Vector3 head, out Vector3 focus)
    {
        head = default;
        focus = default;

        Camera? cam = Rig.VRRigDriver.HeadCamera;
        if (!ReferenceEquals(cam, _viewCam))
        {
            _viewCam = cam;
            _viewTf = cam != null ? cam.transform : null;   // interop, only when the rig changed
        }
        if (ReferenceEquals(_viewTf, null))
            return false;

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return false;                                   // no board frame: menu, or pre-scenario

        try
        {
            head = _viewTf!.position;                       // the one per-frame interop call
        }
        catch (Exception)
        {
            // The camera was destroyed between the identity check and the read. Drop the cache so
            // the next frame re-resolves (and then resolves to null) instead of throwing forever.
            _viewCam = null;
            _viewTf = null;
            return false;
        }
        focus = controller.FocusPoint;                      // managed field read on the singleton
        return true;
    }

    /// <summary>
    /// Re-read one slice of the roster and keep the running visible total honest. False when there
    /// is no roster — [Perf] SceneCensus is the switch that authorises the full walk this seeds
    /// from, so with the census off there is deliberately no estimate rather than a fabricated one.
    /// </summary>
    private static bool StepRoster()
    {
        Renderer[]? roster = _roster;
        if (roster == null || !_rosterReady)
            return false;
        // The census is what REFRESHES the roster once per window. Switched off mid-session it stops
        // refreshing, and a roster that is never refreshed drifts further from the scene every
        // window — so the estimate stops with it rather than quietly ageing.
        if (PerfConfig.SceneCensus != null && !PerfConfig.SceneCensus.Value)
        {
            DropRoster();
            return false;
        }
        int n = roster.Length;
        if (n == 0)
            return false;

        bool[] seen = _rosterSeen;
        if (seen.Length < n)
            return false;   // cannot happen (the seed sizes it) — but this runs every frame
        int slice = n < RenderersPerFrame ? n : RenderersPerFrame;
        int c = _rosterCursor;
        for (int k = 0; k < slice; k++)
        {
            if (c >= n)
                c = 0;
            Renderer r = roster[c];
            // A destroyed renderer reads as not-visible and is subtracted ONCE; the next window's
            // census walk drops it from the roster entirely. Degrading, never throwing.
            bool vis = r != null && r.isVisible;
            if (vis != seen[c])
            {
                seen[c] = vis;
                _rosterVisible += vis ? 1 : -1;
            }
            c++;
        }
        _rosterCursor = c >= n ? 0 : c;
        if (_rosterVisible < 0)
            _rosterVisible = 0;
        return true;
    }

    /// <summary>
    /// Adopt the census's walk as the sampling roster and seed the running total from it. Called
    /// from <see cref="AppendSceneCensus"/> with the array it already built and already walked, so
    /// this adds no walk of its own — it only remembers what that walk saw.
    /// </summary>
    private static void SeedRoster(Renderer[] all, int visible, bool[] seen)
    {
        _roster = all;
        _rosterSeen = seen;
        _rosterCursor = 0;
        _rosterVisible = visible;
        _rosterReady = true;
    }

    /// <summary>Grow the visibility shadow array to cover the roster (never shrinks; the slack costs
    /// one byte per renderer and saves an allocation on every window whose scene wobbles).</summary>
    private static bool[] RosterShadow(int n)
    {
        if (_rosterSeen.Length < n)
            _rosterSeen = new bool[n + 256];
        return _rosterSeen;
    }

    /// <summary>Drop the roster (the census switched off mid-session; it reseeds when it comes back).</summary>
    private static void DropRoster()
    {
        _roster = null;
        _rosterReady = false;
        _rosterCursor = 0;
        _rosterVisible = 0;
    }

    /// <summary>Full teardown: the roster AND the cached head camera (hot reload / rig destroyed).</summary>
    private static void ResetRoster()
    {
        DropRoster();
        _viewCam = null;
        _viewTf = null;
    }

    // ==============================================================================================
    //  Window summary
    // ==============================================================================================

    /// <summary>
    /// Sort the window's frames by viewing distance, once, and derive everything both output lines
    /// need from that single ordering. Allocation-free: the key array and the index array are
    /// preallocated and <see cref="Array.Sort(Array,Array,int,int)"/> permutes them in place.
    /// </summary>
    private static void PrepareView()
    {
        if (_viewPrepared)
            return;
        _viewPrepared = true;
        _viewN = 0;
        _viewVisibleN = 0;

        int v = 0;
        for (int i = 0; i < _count; i++)
        {
            float d = ViewDist[i];
            if (float.IsNaN(d))
                continue;   // no head or no board that frame — not a zero, an absence
            ViewKeys[v] = d;
            ViewIndex[v] = i;
            v++;
        }
        _viewN = v;
        if (v == 0)
            return;

        Array.Sort(ViewKeys, ViewIndex, 0, v);
        _viewDistMin = ViewKeys[0];
        _viewDistMax = ViewKeys[v - 1];
        _viewDistP50 = ViewKeys[(v - 1) / 2];

        _viewHeightP50 = Median(ViewHeight, 0, v, skipNegative: false, out _);
        MinMax(ViewHeight, 0, v, out _viewHeightMin, out _viewHeightMax);
        _viewVisibleP50 = Median(ViewVisible, 0, v, skipNegative: true, out _viewVisibleN);
    }

    /// <summary>
    /// Median of <paramref name="source"/> over the frames <see cref="ViewIndex"/> holds in
    /// [from, to). Nearest-rank, matching this class's other percentile: a value that actually
    /// occurred beats an average of two that did not.
    /// </summary>
    private static float Median(float[] source, int from, int to, bool skipNegative, out int n)
    {
        n = 0;
        for (int k = from; k < to; k++)
        {
            float value = source[ViewIndex[k]];
            if (skipNegative && value < 0f)
                continue;
            ViewScratch[n++] = value;
        }
        if (n == 0)
            return float.NaN;
        Array.Sort(ViewScratch, 0, n);
        return ViewScratch[(n - 1) / 2];
    }

    private static void MinMax(float[] source, int from, int to, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        for (int k = from; k < to; k++)
        {
            float value = source[ViewIndex[k]];
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }
        if (min > max)
        {
            min = 0f;
            max = 0f;
        }
    }

    // ==============================================================================================
    //  Output
    // ==============================================================================================

    /// <summary>
    /// The [Perf] FRAME line's viewpoint clause — WHERE the window was looked at from, as a p50 with
    /// its full range, in the same statistical treatment the frametime beside it already gets.
    ///
    /// <para>The range matters as much as the median: a window whose distance span is a few tenths
    /// is a window in which the player stood still, and its frametime describes ONE view. A window
    /// with a wide span is one the ZOOM clause on the SPLIT line can actually decompose.</para>
    /// </summary>
    internal static void AppendViewClause(StringBuilder sb)
    {
        if (PerfConfig.FrameSplit == null || !PerfConfig.FrameSplit.Value)
        {
            sb.Append(" | view n/a ([Perf] FrameSplit is off, and the zoom axis is sampled on that "
                      + "line's per-frame roll — switch it on to get a viewpoint back)");
            return;
        }
        PrepareView();
        if (_viewN == 0)
        {
            sb.Append(" | view n/a (no head camera, or no board focus point, in this window — "
                      + "outside a scenario there is no board plane to measure a viewpoint against)");
            return;
        }

        sb.Append(" | view height p50 ").Append(_viewHeightP50.ToString("F1"))
          .Append(" (").Append(_viewHeightMin.ToString("F1")).Append("..")
          .Append(_viewHeightMax.ToString("F1")).Append(") dist p50 ")
          .Append(_viewDistP50.ToString("F1"))
          .Append(" (").Append(_viewDistMin.ToString("F1")).Append("..")
          .Append(_viewDistMax.ToString("F1")).Append(") wu above/from the board plane, n=")
          .Append(_viewN);
        if (_viewVisibleN > 0)
        {
            sb.Append(" | visible p50 ").Append(_viewVisibleP50.ToString("F0"))
              .Append(" renderer(s) (n=").Append(_viewVisibleN).Append(')');
        }
        else
        {
            sb.Append(" | visible n/a ([Perf] SceneCensus is off, so there is no roster to sample)");
        }
        sb.Append(" — world UNITS, not metres: the diorama is scaled (the same numbers the [Core] "
                  + "Heartbeat's head pos prints). A NARROW dist range means the player stood still, "
                  + "so this window describes one view and the ZOOM clause below cannot decompose it");
        // The instrument's own price, measured, not asserted. It is deliberately NOT folded into the
        // 'mod ...ms/frame' figure above (that total is captured before this sample runs), so the
        // mod's share never silently includes the cost of watching it.
        if (PerfMonitor.TryGetStepAverageMs("Perf.ZoomSample", out double costMs))
        {
            sb.Append(". Sampling this axis cost ").Append(costMs.ToString("F3"))
              .Append("ms/frame in this window (its own measured cost, also on the STEPS line as "
                      + "Perf.ZoomSample; if it ever ranks near the mod's real work, the instrument "
                      + "has become the thing it measures)");
        }
    }

    /// <summary>
    /// The [Perf] SPLIT line's ZOOM clause — the answer to "how does frame time vary with viewing
    /// distance", from ONE window, on ONE line.
    ///
    /// <para>Frames are sorted by the head's distance from the board centre and cut into thirds, and
    /// each third reports its own p50 frametime, p50 logic span and p50 render-loop span. That last
    /// part is what makes it a diagnosis rather than an observation: if pulling back costs 7 ms and
    /// 6 of them land in LOGIC, the cost is view-scaled SIMULATION (animators, particle systems and
    /// isVisible-gated scripts that stop being culled as the head rises), and no amount of
    /// draw-call work will touch it. If it lands in RENDER, it is submission volume and the culling
    /// mask and camera count are the levers. Same window, same frames, no correlation by hand.</para>
    /// </summary>
    private static void AppendZoom(StringBuilder sb)
    {
        PrepareView();
        if (_viewN < MinZoomFrames)
        {
            sb.Append(" | ZOOM n/a (only ").Append(_viewN).Append(" frame(s) in this window carried a "
                      + "viewpoint — a distance sweep needs at least ").Append(MinZoomFrames).Append(')');
            return;
        }

        int v = _viewN;
        int a = v / 3;
        int b = 2 * v / 3;
        float nearMax = ViewKeys[a - 1];
        float farMin = ViewKeys[b];
        float span = _viewDistMax - _viewDistMin;

        float nearFrame = Median(ViewFrameMs, 0, a, false, out _);
        float midFrame = Median(ViewFrameMs, a, b, false, out _);
        float farFrame = Median(ViewFrameMs, b, v, false, out _);
        float nearLogic = Median(LogicMs, 0, a, false, out _);
        float midLogic = Median(LogicMs, a, b, false, out _);
        float farLogic = Median(LogicMs, b, v, false, out _);
        float nearRender = Median(RenderMs, 0, a, false, out _);
        float midRender = Median(RenderMs, a, b, false, out _);
        float farRender = Median(RenderMs, b, v, false, out _);
        float nearVis = Median(ViewVisible, 0, a, true, out int nearVisN);
        float midVis = Median(ViewVisible, a, b, true, out int midVisN);
        float farVis = Median(ViewVisible, b, v, true, out int farVisN);

        sb.Append(" | ZOOM — how frame time varies with VIEWING DISTANCE, from this window alone. "
                  + "Frames bucketed by the head's distance from the board centre (before this axis "
                  + "existed the same claim needed 10 s heartbeats matched against 30 s windows by "
                  + "hand): NEAR third (<=").Append(nearMax.ToString("F1")).Append("wu, n=").Append(a)
          .Append(") frametime p50 ").Append(nearFrame.ToString("F2"))
          .Append("ms — logic ").Append(nearLogic.ToString("F2"))
          .Append(", render ").Append(nearRender.ToString("F2"));
        AppendVisible(sb, nearVis, nearVisN);
        sb.Append("; MID third (n=").Append(b - a).Append(") frametime p50 ")
          .Append(midFrame.ToString("F2")).Append("ms — logic ").Append(midLogic.ToString("F2"))
          .Append(", render ").Append(midRender.ToString("F2"));
        AppendVisible(sb, midVis, midVisN);
        sb.Append("; FAR third (>=").Append(farMin.ToString("F1")).Append("wu, n=").Append(v - b)
          .Append(") frametime p50 ").Append(farFrame.ToString("F2"))
          .Append("ms — logic ").Append(farLogic.ToString("F2"))
          .Append(", render ").Append(farRender.ToString("F2"));
        AppendVisible(sb, farVis, farVisN);

        float dFrame = farFrame - nearFrame;
        float dLogic = farLogic - nearLogic;
        float dRender = farRender - nearRender;
        // MEDIAN-to-median separation, not the gap between the bucket edges: "how much further out
        // was the far third, typically" is the number that makes a ms/wu rate mean anything.
        float nearDistP50 = ViewKeys[(a - 1) / 2];
        float farDistP50 = ViewKeys[b + (v - b - 1) / 2];
        sb.Append(". NEAR→FAR (+").Append((farDistP50 - nearDistP50).ToString("F1"))
          .Append("wu further out): frametime ").Append(Signed(dFrame))
          .Append("ms, logic ").Append(Signed(dLogic))
          .Append("ms, render ").Append(Signed(dRender)).Append("ms");
        if (nearVisN > 0 && farVisN > 0)
            sb.Append(", visible ").Append(Signed(farVis - nearVis)).Append(" renderer(s)");

        sb.Append(". VERDICT: ")
          .Append(ZoomVerdict(span, farDistP50 - nearDistP50, dFrame, dLogic, dRender, nearFrame));
    }

    private static void AppendVisible(StringBuilder sb, float visible, int samples)
    {
        if (samples > 0)
            sb.Append(", visible ").Append(visible.ToString("F0"));
    }

    private static string Signed(float value) =>
        (value >= 0f ? "+" : string.Empty) + value.ToString("F2");

    /// <summary>
    /// Say, in words, what the thirds mean — including the two cases where the honest answer is
    /// "this window cannot tell you". A spread computed over a window in which the player never
    /// moved is three samples of one view, and reporting it as a distance effect is exactly the kind
    /// of false positive this line exists to remove.
    /// </summary>
    private static string ZoomVerdict(float span, float separation, float dFrame, float dLogic,
        float dRender, float nearFrame)
    {
        if (span < MeaningfulSpanUnits)
            return $"the head barely moved in this window (it spanned {span:F1}wu), so 'near' and "
                   + "'far' are the SAME VIEW — the three thirds above are three samples of one "
                   + "state, not a distance sweep, and any spread between them is noise. This line "
                   + "only means something across a window in which the player actually moved "
                   + "between an overview and a close-in view.";

        if (Mathf.Abs(dFrame) < ZoomNoiseFloorMs)
            return $"frame time does NOT track viewing distance here — {dFrame:+0.00;-0.00}ms across "
                   + $"a {span:F1}wu span, which is inside the noise floor ({ZoomNoiseFloorMs:F1}ms; "
                   + "a compositor-quantised frame steps in whole budgets, so anything smaller than "
                   + "this cannot be felt anyway). Whatever owns this frame (see the layer verdict "
                   + "below), it is not the zoom.";

        float share = Mathf.Abs(dFrame) < 0.001f ? 0f : 100f * dLogic / dFrame;
        float renderShare = Mathf.Abs(dFrame) < 0.001f ? 0f : 100f * dRender / dFrame;
        string direction = dFrame > 0f ? "PULLING BACK COSTS" : "PULLING BACK SAVES";
        string where;
        if (share >= 50f)
        {
            where = $"and {share:F0}% of it lands in the LOGIC span, not in submission. That is "
                    + "view-scaled SIMULATION — animators, particle systems and isVisible-gated "
                    + "scripts that stop being culled as the head rises — so the lever is WHAT THE "
                    + "HEAD CAMERA DECLARES VISIBLE (its culling mask, see the visible count above "
                    + "and the [Perf] SCENE line's per-layer census), not draw calls and not pixels.";
        }
        else if (renderShare >= 50f)
        {
            where = $"and {renderShare:F0}% of it lands in the RENDER LOOP. That is submission "
                    + "volume: more renderers survive culling from further out, and MultiPass pays "
                    + "for each one twice. Switch [Perf] CullSubmitSplit on to see which half of the "
                    + "render loop grew.";
        }
        else
        {
            where = $"but only {share:F0}% of it is logic and {renderShare:F0}% is the render loop — "
                    + "the rest is in BLOCKED, i.e. the GPU, the compositor, or a rate lock that "
                    + "quantises the far frames to a different multiple of the budget. Check the "
                    + "display Hz on the FRAME line before reading this as a CPU effect.";
        }
        return $"{direction} {Mathf.Abs(dFrame):F2}ms/frame over {separation:F1}wu of pull-back "
               + $"({100f * dFrame / Mathf.Max(0.01f, nearFrame):F0}% of the near-third frametime), "
               + where;
    }
}
