using System;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Config surface for the performance instrumentation (<see cref="PerfMonitor"/>) and for every
/// individually switchable OPTIMIZATION the perf pass introduced.
///
/// <para>WHY ITS OWN MODULE CONFIG FILE (<c>dev.gloomhavenvr.perf.cfg</c>): performance is a
/// cross-cutting concern that touches Rig, Cards, WorldUI, Net and Core alike, so it belongs to
/// none of their module configs — and keeping it in one file means a hardware tester can mail
/// exactly one cfg back with the log. Follows the canonical <see cref="ModuleConfig"/> pattern
/// (bind-once guard, BepInEx auto-saves on every write, so panel edits persist for free).</para>
///
/// <para>TWO SECTIONS, TWO CONTRACTS:</para>
/// <list type="bullet">
/// <item><b>[Perf]</b> — MEASUREMENT. Every entry here only decides what gets logged and how
/// often; none of them changes a single pixel. The master switch defaults ON because the whole
/// point of the pass is that the NEXT hardware log already carries the numbers; the measurement
/// is built to cost a couple of microseconds per frame (see PerfMonitor's cost note) and
/// <see cref="Enabled"/> = false makes it a hard no-op down to a single bool test per frame.</item>
/// <item><b>[Optimize]</b> — BEHAVIOUR. Each entry switches exactly ONE optimization, and each
/// one's DEFAULT is chosen so that nothing the player can see changes silently: pure
/// work-removal (idempotence gates, cached delegates, change-gating) defaults ON because it is
/// invisible by construction, while anything that trades quality or freshness for frames
/// defaults to TODAY'S behaviour and has to be switched on deliberately.</item>
/// </list>
///
/// <para>NOT IN THE VR SETTINGS ANY MORE (2026-07, user: the performance pane was "super
/// verwirrend für den User"): every [Perf] entry and every pure work-removal [Optimize] entry is
/// CONFIG-FILE ONLY now. A settings row asks the player to make a decision, and these offer no
/// decision to make — the measurement changes nothing visible, and the work-removal switches are
/// invisible by construction (that is exactly why they default ON). They are A/B harnesses for
/// this debug phase, so they live here, described here, and nowhere else. The three interval
/// levers below (<see cref="FanRelayoutMinInterval"/>, <see cref="WallFadeEvalInterval"/>,
/// <see cref="RemoteContentInterval"/>) DO trade freshness for work, so they kept a UI row — but
/// in the Debug pane (<c>SettingsPanel.BuildTimingCategory</c>), not the user-facing category,
/// because the measurement says the CPU is ~2.5% of frame time and pointing a stuttering player
/// at a CPU lever would aim them at the wrong problem. The user-facing performance rows are the
/// GPU quality trade only (<c>SettingsPanel.BuildPerformanceCategory</c> → Rig.RenderQuality).</para>
/// </summary>
internal static class PerfConfig
{
    private static ConfigFile? _file;

    /// <summary>True once <see cref="Bind"/> has run (entries are safe to read).</summary>
    internal static bool IsBound => _file != null;

    // ---- [Perf] measurement ----------------------------------------------------------------

    /// <summary>Master switch for the whole instrumentation layer.</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>Seconds between the periodic FRAME/STEPS summary lines.</summary>
    internal static ConfigEntry<float> SummaryIntervalSeconds = null!;

    /// <summary>Per-subsystem step attribution (the TickGuard / PerfMonitor.Scope stopwatch seam).</summary>
    internal static ConfigEntry<bool> Attribution = null!;

    /// <summary>How many mod steps the periodic STEPS line ranks.</summary>
    internal static ConfigEntry<int> TopSteps = null!;

    /// <summary>Emit a SPIKE line whenever a frame blows the budget.</summary>
    internal static ConfigEntry<bool> SpikeLines = null!;

    /// <summary>A frame counts as a SPIKE above this multiple of the display's frame budget.</summary>
    internal static ConfigEntry<float> SpikeBudgetFactor = null!;

    /// <summary>Hard ceiling on SPIKE lines per second (a bad patch must not flood the log).</summary>
    internal static ConfigEntry<float> SpikeMaxPerSecond = null!;

    /// <summary>Sample GC collection counts + managed-heap growth per window.</summary>
    internal static ConfigEntry<bool> Allocations = null!;

    /// <summary>Query the XR display's own counters (dropped/presented frames, GPU time, motion-to-photon).</summary>
    internal static ConfigEntry<bool> XrStats = null!;

    /// <summary>Decompose the frame into main-thread logic / render-loop / blocked (the SPLIT line).</summary>
    internal static ConfigEntry<bool> FrameSplit = null!;

    /// <summary>Count the scene's renderers once per window and append it to the SPLIT line.</summary>
    internal static ConfigEntry<bool> SceneCensus = null!;

    /// <summary>Break the renderer census down by root/layer/type/material and dump the render state.</summary>
    internal static ConfigEntry<bool> SceneProfile = null!;

    /// <summary>Split each camera's render-loop figure into culling and submission halves.</summary>
    internal static ConfigEntry<bool> CullSubmitSplit = null!;

    // ---- [Optimize] behaviour ---------------------------------------------------------------

    /// <summary>Cache the per-frame TickGuard delegates instead of re-allocating them every frame.</summary>
    internal static ConfigEntry<bool> CacheTickDelegates = null!;

    /// <summary>Cache the campaign-map icon scan (choreographer, decals, renderers, property blocks).</summary>
    internal static ConfigEntry<bool> MapIconCache = null!;

    /// <summary>Skip the per-frame component walks for figures that are already adopted / nothing held.</summary>
    internal static ConfigEntry<bool> FigureScanCache = null!;

    /// <summary>Do not build diagnostic strings for log lines that are throttled or change-gated away.</summary>
    internal static ConfigEntry<bool> LeanLogStrings = null!;

    /// <summary>Gate the per-frame tooltip canvas walk behind "is a tooltip actually shown".</summary>
    internal static ConfigEntry<bool> TooltipScanGate = null!;

    /// <summary>Minimum seconds between two gaze-driven card-fan re-layouts (0 = every frame, today's behaviour).</summary>
    internal static ConfigEntry<float> FanRelayoutMinInterval = null!;

    /// <summary>Seconds between wall see-through visibility evaluations (0 = every frame, today's behaviour).</summary>
    internal static ConfigEntry<float> WallFadeEvalInterval = null!;

    /// <summary>Seconds between initiative-row depth normalisations (0 = every frame, today's behaviour).</summary>
    internal static ConfigEntry<float> InitiativeDepthEvalInterval = null!;

    /// <summary>Suppress the high-cadence per-subsystem diagnostic lines (wall fade, fan depth curve, uGUI clicks).</summary>
    internal static ConfigEntry<bool> QuietDiagnostics = null!;

    /// <summary>Seconds between remote-board content refreshes (0 = leave the subsystem's own cadence).</summary>
    internal static ConfigEntry<float> RemoteContentInterval = null!;

    /// <summary>Keep the head camera's forward depth-texture prepass (a full extra scene submission per eye).</summary>
    internal static ConfigEntry<bool> HeadDepthPrepass = null!;

    /// <summary>Layer names/indices removed from the head camera's culling mask (comma-separated; empty = none).</summary>
    internal static ConfigEntry<string> HeadCullingMaskDrop = null!;

    /// <summary>Seed the scenario head mask from the game's ScenarioCamera instead of the blanket anchor mask.</summary>
    internal static ConfigEntry<bool> HeadMaskFromScenarioCamera = null!;

    // ---- safe accessors ---------------------------------------------------------------------
    // Optimization sites live in per-frame code that can run BEFORE (or entirely without) a
    // successful Bind — a module whose Init threw, a hot-reload mid-frame, the flat-screen path.
    // Each accessor therefore answers with the entry's DEFAULT while unbound, so an unbound config
    // can never silently change behaviour in either direction.

    /// <summary>[Optimize] CacheTickDelegates, defaulting to on while unbound.</summary>
    internal static bool CacheDelegates => CacheTickDelegates == null || CacheTickDelegates.Value;

    /// <summary>[Optimize] MapIconCache, defaulting to on while unbound.</summary>
    internal static bool MapIconCacheOn => MapIconCache == null || MapIconCache.Value;

    /// <summary>[Optimize] FigureScanCache, defaulting to on while unbound.</summary>
    internal static bool FigureScanCacheOn => FigureScanCache == null || FigureScanCache.Value;

    /// <summary>[Optimize] LeanLogStrings, defaulting to on while unbound.</summary>
    internal static bool LeanStrings => LeanLogStrings == null || LeanLogStrings.Value;

    /// <summary>[Optimize] TooltipScanGate, defaulting to on while unbound.</summary>
    internal static bool TooltipGateOn => TooltipScanGate == null || TooltipScanGate.Value;

    /// <summary>[Optimize] FanRelayoutMinInterval, defaulting to 0 (every frame) while unbound.</summary>
    internal static float FanRelayoutInterval =>
        FanRelayoutMinInterval == null ? 0f : Mathf.Clamp(FanRelayoutMinInterval.Value, 0f, 0.2f);

    /// <summary>[Optimize] WallFadeEvalInterval, defaulting to 0 (every frame) while unbound.</summary>
    internal static float WallFadeInterval =>
        WallFadeEvalInterval == null ? 0f : Mathf.Clamp(WallFadeEvalInterval.Value, 0f, 0.25f);

    /// <summary>[Optimize] InitiativeDepthEvalInterval, defaulting to 0 (every frame) while unbound.</summary>
    internal static float InitiativeDepthInterval =>
        InitiativeDepthEvalInterval == null ? 0f : Mathf.Clamp(InitiativeDepthEvalInterval.Value, 0f, 0.25f);

    /// <summary>[Optimize] QuietDiagnostics, defaulting to off (keep today's diagnostics) while unbound.</summary>
    internal static bool Quiet => QuietDiagnostics != null && QuietDiagnostics.Value;

    /// <summary>[Optimize] RemoteContentInterval, 0 = keep the subsystem's own cadence.</summary>
    internal static float RemoteContentSeconds =>
        RemoteContentInterval == null ? 0f : Mathf.Clamp(RemoteContentInterval.Value, 0f, 2f);

    /// <summary>[Perf] SceneProfile, defaulting to off while unbound (it is off when bound too).</summary>
    internal static bool SceneProfileOn => SceneProfile != null && SceneProfile.Value;

    /// <summary>[Perf] CullSubmitSplit, defaulting to off while unbound.</summary>
    internal static bool CullSubmitSplitOn => CullSubmitSplit != null && CullSubmitSplit.Value;

    /// <summary>[Optimize] HeadDepthPrepass, defaulting to on (today's behaviour) while unbound.</summary>
    internal static bool DepthPrepassOn => HeadDepthPrepass == null || HeadDepthPrepass.Value;

    /// <summary>[Optimize] HeadMaskFromScenarioCamera, defaulting to off (today's behaviour) while unbound.</summary>
    internal static bool HeadMaskFromScenarioCam =>
        HeadMaskFromScenarioCamera != null && HeadMaskFromScenarioCamera.Value;

    // ---- [Optimize] HeadCullingMaskDrop: parsed once per distinct string, not per frame --------
    // The entry is human-written text ("Water, 14, TransparentFX") and it is read from the rig's
    // per-frame mask re-assert, so parsing it there would allocate and split a string every frame
    // on every rig. Cache on the raw text: BepInEx hands back the same string instance until the
    // value actually changes, and a value change is exactly when the mask must be recomputed.

    private static string _dropSource = string.Empty;
    private static int _dropMask;

    /// <summary>
    /// Culling-mask bits the head camera must NOT render, resolved from
    /// <see cref="HeadCullingMaskDrop"/>. 0 = drop nothing, which is the default and means the
    /// mask policy is exactly what it always was.
    ///
    /// <para>WHY A LIST AND NOT A FIXED SET: which layers the head camera can afford to lose is a
    /// MEASUREMENT, not a fact we can look up — it depends on the scenario, on what the game's
    /// anchor camera happened to have in its mask, and on what the player can see. The
    /// <c>[Perf] SCENE</c> line prints every layer by NAME with its renderer count and whether the
    /// head camera currently renders it, so this entry is the other half of that instrument: read
    /// the names off the log, drop one, and compare the render-loop split. Nothing is guessed here.</para>
    /// </summary>
    internal static int HeadMaskDropMask
    {
        get
        {
            string source = HeadCullingMaskDrop?.Value ?? string.Empty;
            // Ordinal VALUE compare, not ReferenceEquals: a config backend that ever handed back
            // a fresh string instance for the same text would otherwise re-parse — and re-LOG —
            // every single frame. One short-string compare per frame is not worth that risk.
            if (!string.Equals(source, _dropSource, StringComparison.Ordinal))
            {
                _dropSource = source;
                _dropMask = ParseLayerMask(source);
            }
            return _dropMask;
        }
    }

    /// <summary>
    /// Parse "Water, 14, TransparentFX" into a culling-mask. Unknown names are reported once (on
    /// the parse, not per frame) and ignored — a typo must never silently blank the head camera.
    /// </summary>
    private static int ParseLayerMask(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;
        int mask = 0;
        string[] parts = source.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i].Trim();
            if (token.Length == 0)
                continue;
            int layer;
            if (int.TryParse(token, out layer))
            {
                if (layer < 0 || layer > 31)
                {
                    VRLog.Warn("Perf", $"[Optimize] HeadCullingMaskDrop: '{token}' is not a layer "
                                       + "index 0-31 — ignored.");
                    continue;
                }
            }
            else
            {
                layer = LayerMask.NameToLayer(token);
                if (layer < 0)
                {
                    VRLog.Warn("Perf", $"[Optimize] HeadCullingMaskDrop: no layer named '{token}' — "
                                       + "ignored. The [Perf] SCENE line lists every layer by name.");
                    continue;
                }
            }
            mask |= 1 << layer;
        }
        if (mask != 0)
            VRLog.Info("Perf", $"[Optimize] HeadCullingMaskDrop resolved '{source}' → mask 0x{mask:X8}. "
                               + "The head camera stops culling AND submitting those layers, once per "
                               + "eye pass. Clear the entry to restore the normal mask policy.");
        return mask;
    }

    /// <summary>
    /// Bind-once against the perf module's own config file. Lazy: called from
    /// <see cref="PerfMonitor"/>'s host, from the settings-panel accessors and from every
    /// optimization site before it reads an entry, so no module-init ordering is assumed.
    /// </summary>
    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("perf");

        // ---- [Perf] -------------------------------------------------------------------------
        Enabled = _file.Bind("Perf", "Enabled", Defaults.Perf_Enabled,
            "Master switch for the performance instrumentation. ON logs a periodic [Perf] FRAME / "
            + "STEPS summary plus a [Perf] SPIKE line for every frame that blows the display's frame "
            + "budget — the numbers that turn 'it judders when I turn my head fast' into an "
            + "attributable subsystem. OFF makes the whole layer a no-op (one bool test per frame, "
            + "no stopwatch reads, no dictionary lookups, no log lines).");
        SummaryIntervalSeconds = _file.Bind("Perf", "SummaryIntervalSeconds", Defaults.SummaryIntervalSeconds, new ConfigDescription(
            "Seconds between the periodic [Perf] FRAME and [Perf] STEPS summary lines. Lower = a "
            + "finer time resolution in the log at the cost of more lines; the per-frame sampling "
            + "cost does not change with this value.",
            new AcceptableValueRange<float>(5f, 600f)));
        Attribution = _file.Bind("Perf", "Attribution", Defaults.Attribution,
            "Measure each NAMED mod step (every TickGuard.Run step plus the explicitly scoped driver "
            + "bodies) with a stopwatch, so the [Perf] STEPS line can rank the mod's own subsystems by "
            + "cost and a SPIKE line can name who owned the spike. This is the single most valuable "
            + "piece of the instrumentation: it answers 'is the hitch OURS?'. Costs two "
            + "QueryPerformanceCounter reads and one dictionary lookup per step per frame (a few "
            + "microseconds in total). OFF still measures frame pacing, just without attribution.");
        TopSteps = _file.Bind("Perf", "TopSteps", Defaults.TopSteps, new ConfigDescription(
            "How many mod steps the periodic [Perf] STEPS line ranks (by total time spent in the "
            + "window). Ignored while Attribution is off.",
            new AcceptableValueRange<int>(1, 20)));
        SpikeLines = _file.Bind("Perf", "SpikeLines", Defaults.SpikeLines,
            "Emit a [Perf] SPIKE line whenever a single frame takes longer than SpikeBudgetFactor x "
            + "the display's frame budget, naming the worst mod steps IN THAT FRAME. This is the line "
            + "that catches the reported 'world judders on a fast head turn' — a judder is a handful "
            + "of late frames, which an average can never show.");
        SpikeBudgetFactor = _file.Bind("Perf", "SpikeBudgetFactor", Defaults.SpikeBudgetFactor, new ConfigDescription(
            "A frame counts as a SPIKE when it takes longer than this multiple of the display's frame "
            + "budget (budget = 1 / actual refresh rate, read from the XR display — not a hardcoded "
            + "72/90 Hz). 2.0 = 'at least one whole displayed frame was missed'. Lower catches more, "
            + "at the price of more SPIKE lines.",
            new AcceptableValueRange<float>(1.2f, 10f)));
        SpikeMaxPerSecond = _file.Bind("Perf", "SpikeMaxPerSecond", Defaults.SpikeMaxPerSecond, new ConfigDescription(
            "Hard ceiling on SPIKE lines per second. A genuinely bad stretch would otherwise emit one "
            + "line per frame and the logging itself would become the performance problem; suppressed "
            + "spikes are still COUNTED and reported in the next FRAME summary, so nothing is lost. "
            + "There is deliberately no 'unlimited' setting.",
            new AcceptableValueRange<float>(0.1f, 20f)));
        Allocations = _file.Bind("Perf", "Allocations", Defaults.Allocations,
            "Sample GC collection counts and managed-heap growth once per frame and report them per "
            + "window. A gen0 collection landing inside a head turn is exactly the reported symptom, "
            + "so allocation pressure is first-class evidence here. Cost: one cheap heap-size read "
            + "per frame.");
        XrStats = _file.Bind("Perf", "XrStats", Defaults.XrStats,
            "Ask the XR display subsystem for its OWN truth — dropped frames, presented frames, GPU "
            + "time, motion-to-photon latency — which is the only way to see reprojection (the "
            + "compositor covering for us) rather than just our CPU timings. Counters the runtime "
            + "does not expose are reported as 'n/a' in the log, never as a fake zero.");
        FrameSplit = _file.Bind("Perf", "FrameSplit", Defaults.FrameSplit,
            "Decompose every frame into MAIN-THREAD LOGIC (Update->LateUpdate), RENDER LOOP "
            + "(culling + draw-call submission, also broken down per camera and per pass) and "
            + "BLOCKED (waiting on the GPU / the XR compositor) — the [Perf] SPLIT line. This is the "
            + "measurement that says which LAYER owns the frame, which neither the frame interval "
            + "nor the runtime's own GPU counter can answer: on 2026-07 hardware that counter read "
            + "the frame interval in every window and did not move when the pixel-sample budget was "
            + "cut 11x, because the runtime had locked the app to 45 Hz throughout. The spans here "
            + "are built from the mod's own clock reads at known points in Unity's frame, so they "
            + "bind on every platform with no player-setting prerequisite. Cost: two timer reads per "
            + "frame plus two per camera render, and no allocation. ALSO CARRIES THE ZOOM AXIS: the "
            + "same per-frame roll records where the head was standing (height above the board "
            + "plane, distance from the board centre) and how many renderers the head camera kept, "
            + "which is what lets the SPLIT line's ZOOM clause bucket the window's frames into "
            + "near/middle/far thirds and print each third's own p50 frametime, logic and render. "
            + "Without that, every claim about zoom has to be made by correlating the 10 s "
            + "heartbeat's head pose against these 30 s windows by hand.");
        SceneCensus = _file.Bind("Perf", "SceneCensus", Defaults.SceneCensus,
            "Append a renderer census (total / enabled / visible to at least one camera) to the "
            + "[Perf] SPLIT line. UnityStats' batch and draw-call counters are editor-only, so this "
            + "is the closest runtime proxy for 'how much is there to submit', and together with the "
            + "camera-pass count it prices one extra full-scene render. Deliberately sampled ONCE "
            + "PER WINDOW: the object walk behind it allocates and would be a stutter of its own at "
            + "frame rate. Switch OFF for a capture where even a per-window hitch matters. That "
            + "once-per-window number is a single INSTANT and shows no trend, which is why the same "
            + "walk now also SEEDS the per-frame visible-renderer estimate on the ZOOM clause: from "
            + "the seed, each frame re-reads a fixed 64-renderer slice of the roster round-robin and "
            + "adjusts a running total, so the count is live and at most half a second stale without "
            + "any per-frame full-scene walk. Switching this entry off takes that estimate with it.");

        // DEFAULT OFF, and off for a reason that is not caution: an earlier version of the walk
        // behind this entry hung the game outright at the first window close (2026-07), which
        // lands in the intro — before the pane that owns this switch exists. The defect is fixed
        // and the walk additionally refuses to run in the pre-menu scenes, but a heavyweight
        // object walk is not something that should be on by default in a build a player runs.
        SceneProfile = _file.Bind("Perf", "SceneProfile", Defaults.SceneProfile,
            "OFF by default. Append two more lines to each summary: [Perf] SCENE — the renderer population broken "
            + "down by scene-root/child group, layer (with names), renderer type, shadow-casting "
            + "mode, MaterialPropertyBlock count and DISTINCT MATERIAL COUNT — and [Perf] GFX — the "
            + "render state that multiplies submission volume (quality level, shadow settings, "
            + "pixel lights, the light census, and the head camera's path/depth-texture/culling "
            + "mask). The bare census on the SPLIT line says 1683 renderers; it cannot say WHAT "
            + "they are, and a count cannot choose a lever. The distinct-material number is the "
            + "decisive one: the built-in pipeline can only merge renderers that share a material "
            + "instance, so one material per renderer means there is no batch for anything — mod "
            + "or game — to break. Sampled ONCE PER WINDOW like the census: the walk allocates and "
            + "would be a stutter of its own at frame rate. Never runs in the pre-menu scenes "
            + "(Bootstrap/Intro): there are five renderers there, and a fault in a walk that runs "
            + "before the settings pane exists cannot be switched off from inside the headset.");

        CullSubmitSplit = _file.Bind("Perf", "CullSubmitSplit", Defaults.CullSubmitSplit,
            "OFF by default. Split each camera's figure on the [Perf] SPLIT line into CULL "
            + "(onPreCull→onPreRender: Unity's visibility determination, which scales with how "
            + "many renderers exist and pass the culling mask) and SUBMIT (onPreRender→"
            + "onPostRender: the draw calls, including a forward camera's depth-texture prepass "
            + "and every shadow map). 'The render loop owns the frame' does not say which half, "
            + "and the two have different levers, so this is the entry that decides it instead of "
            + "arguing it. Costs one extra Stopwatch read and one Camera.onPreRender subscription "
            + "per camera render; OFF unsubscribes the callback entirely rather than null-checking "
            + "inside it, and the SPLIT line then prints the combined per-camera figure as before.");

        // ---- [Optimize] ----------------------------------------------------------------------
        CacheTickDelegates = _file.Bind("Optimize", "CacheTickDelegates", Defaults.CacheTickDelegates,
            "Cache the Action delegates handed to TickGuard.Run instead of re-creating them from an "
            + "instance method group every frame. Pure work removal — identical behaviour, just "
            + "without ~7 delegate allocations per frame feeding the gen0 collector that causes "
            + "head-turn hitches. OFF restores the old per-frame allocation (A/B only).");
        MapIconCache = _file.Bind("Optimize", "MapIconCache", Defaults.MapIconCache,
            "Cache the campaign-map icon scan. The map-icon draw runs from Camera.onPreCull — once "
            + "PER RENDERING CAMERA PER FRAME, and the stereo flat screen has two or three — and it "
            + "used to redo a full-scene FindObjectOfType, two allocating component walks, a "
            + "GetComponent per decal, a fresh MaterialPropertyBlock per decal and a long diagnostic "
            + "string per decal every single time. The decal SET only changes with the map state, so "
            + "it is scanned on an interval while every icon's pose is still read live each frame — "
            + "panning and zooming are pixel-identical. OFF restores the per-frame scan (A/B).");
        FigureScanCache = _file.Bind("Optimize", "FigureScanCache", Defaults.FigureScanCache,
            "Skip per-frame component walks the figure-grab driver does not need: an already-adopted "
            + "figure is no longer re-resolved through GetComponentInChildren every frame, and the "
            + "ring suppressor takes its 'nothing is held' early-out BEFORE it allocates the "
            + "enumerators it would have iterated. Behaviour-identical work removal; OFF restores "
            + "the unconditional walks.");
        LeanLogStrings = _file.Bind("Optimize", "LeanLogStrings", Defaults.LeanLogStrings,
            "Do not BUILD diagnostic strings that the log then throws away. Several diagnostics are "
            + "throttled or change-gated inside the callee, so the interpolated message (plus the "
            + "UnityEngine.Object.name access, which allocates a fresh string every read) was paid "
            + "for on every frame while only one line in hundreds was printed. The gates now sit in "
            + "front of the string work instead of behind it. Identical log output either way.");
        TooltipScanGate = _file.Bind("Optimize", "TooltipScanGate", Defaults.TooltipScanGate,
            "Gate the world-tooltip subsystem's per-frame work on whether a tooltip is actually "
            + "shown: the full canvas subtree walk used to run before that check, and the fallback "
            + "search for the game's CanvasManager retried a full-scene FindObjectOfType every frame "
            + "for as long as it stayed unresolved. Identical tooltips, far fewer scans.");
        FanRelayoutMinInterval = _file.Bind("Optimize", "FanRelayoutMinInterval", Defaults.FanRelayoutMinInterval, new ConfigDescription(
            "Minimum seconds between two GAZE-driven re-layouts of the open card fan. 0 = re-lay out "
            + "on every frame the gaze gate trips, which is today's behaviour and what a fast head "
            + "turn does every single frame. A small value (0.02 = 50 Hz) removes the redundant "
            + "re-layouts a >72 Hz display asks for without any visible change, because each card's "
            + "own home-lerp smooths whatever the gate lets through. Only affects the GAZE gate — "
            + "card set changes, hovers, plucks and the fan-out reveal always re-lay out immediately.",
            new AcceptableValueRange<float>(0f, 0.2f)));
        WallFadeEvalInterval = _file.Bind("Optimize", "WallFadeEvalInterval", Defaults.WallFadeEvalInterval, new ConfigDescription(
            "Minimum seconds between two wall see-through VISIBILITY evaluations (the per-segment "
            + "sample sweep). 0 = every frame, today's behaviour. The evaluation already feeds a "
            + "Schmitt trigger with second-scale dwell hysteresis, so sampling it at e.g. 0.05 "
            + "(20 Hz) cannot change which walls fade — it only stops re-deciding a decision that is "
            + "deliberately slow. Inert unless [Compat] WallFade is on.",
            new AcceptableValueRange<float>(0f, 0.25f)));
        InitiativeDepthEvalInterval = _file.Bind("Optimize", "InitiativeDepthEvalInterval", Defaults.InitiativeDepthEvalInterval, new ConfigDescription(
            "Minimum seconds between two DEPTH NORMALISATIONS of the docked initiative row — the "
            + "pass that walks every active portrait's subtree and clamps the row's authored "
            + "front-to-back spread to [WorldUI] InitiativeDepthMaxSpreadPx. 0 = every frame, "
            + "today's behaviour, and the measured cost of that walk rises with the number of "
            + "figures in the round. The pass is IDEMPOTENT and re-derives every target from the "
            + "recorded authored z, so running it at e.g. 0.05 (20 Hz) cannot change where a "
            + "portrait ends up — it only delays by at most that interval when a NEWLY pooled "
            + "portrait is first flattened.",
            new AcceptableValueRange<float>(0f, 0.25f)));
        QuietDiagnostics = _file.Bind("Optimize", "QuietDiagnostics", Defaults.QuietDiagnostics,
            "Suppress the high-cadence per-subsystem DIAGNOSTIC log lines (the wall-fade 'diag:' "
            + "sweep, the card-fan 'Fan depth-curve:' sweep recorder, the per-click uGUI trace). "
            + "Default OFF because those lines are the evidence base for several open investigations "
            + "and they measured well under one line per second on hardware — switch it ON for a "
            + "clean performance capture where only the [Perf] lines matter.");
        RemoteContentInterval = _file.Bind("Optimize", "RemoteContentInterval", Defaults.RemoteContentInterval, new ConfigDescription(
            "Override the refresh interval (seconds) of the REMOTE player board content scan — the "
            + "4 Hz walk that rebuilds other players' board contents in multiplayer. 0 = leave the "
            + "subsystem's own 0.25 s cadence. Raising it (e.g. 0.5) halves that walk's cost; it only "
            + "delays how fast a peer's board contents catch up, and it does nothing at all in single "
            + "player.",
            new AcceptableValueRange<float>(0f, 2f)));
        HeadDepthPrepass = _file.Bind("Optimize", "HeadDepthPrepass", Defaults.HeadDepthPrepass,
            "Keep the head camera's DepthTextureMode.Depth. ON is today's behaviour and it is NOT "
            + "free: on the built-in FORWARD path (which the mod's head camera uses) Unity builds "
            + "_CameraDepthTexture by rendering the whole opaque scene a SECOND time through each "
            + "shader's shadow-caster pass — a full extra scene submission PER EYE PASS, i.e. four "
            + "full submissions per frame under MultiPass instead of two. That is the largest "
            + "single piece of submission volume the MOD itself adds, and the 2026-07 measurement "
            + "says submission volume is the wall. OFF halves it. What OFF costs: the game's VFX "
            + "shaders (torch flames, glow billboards, DFade clouds) soft-fade against that depth "
            + "texture, and without it the fade fails OPEN — glow renders straight through thin "
            + "walls again, which is the exact bug this mode was added to fix. So this is a real "
            + "trade, not free work removal, and it defaults to today's behaviour. Flip it from "
            + "the Debug settings pane so the [Perf] measurement window closes on the boundary and "
            + "the two SPLIT lines are comparable.");
        HeadCullingMaskDrop = _file.Bind("Optimize", "HeadCullingMaskDrop", Defaults.HeadCullingMaskDrop,
            "Layers the head camera must NOT render, as a comma-separated list of layer NAMES or "
            + "indices (e.g. 'Water, 14'). Empty = drop nothing, which is today's behaviour and the "
            + "normal mask policy (anchor camera's mask | the mod layer). Every layer removed here "
            + "is a slice of the scene that stops being culled AND submitted, once per eye pass — "
            + "but which layers are safe is a MEASUREMENT, not something that can be looked up, "
            + "because it depends on the scenario and on what the game's anchor camera happened to "
            + "carry. The [Perf] SCENE line prints every layer by name with its renderer count and "
            + "whether the head camera currently renders it: read the names off the log, drop one, "
            + "compare the render-loop split. Unknown names are reported and ignored, never "
            + "silently applied, so a typo cannot blank the view. Re-asserted every frame, so "
            + "clearing the entry restores the normal mask immediately.");
        HeadMaskFromScenarioCamera = _file.Bind("Optimize", "HeadMaskFromScenarioCamera", Defaults.HeadMaskFromScenarioCamera,
            "In a SCENARIO, seed the head camera's culling mask from the game's own ScenarioCamera "
            + "instead of from the anchor camera. Why this exists: the scenario anchor resolves to "
            + "'Main Camera', whose mask is 0xFFFFFFFF — ALL 32 layers — while the camera the flat "
            + "game actually renders the dungeon with carries 0x700FFF17 and deliberately excludes "
            + "thirteen of them. The head camera therefore culls and submits a surplus the game "
            + "never draws, twice per frame under MultiPass. OFF (default) keeps the blanket mask, "
            + "which was the safe original choice for a good reason: the head camera legitimately "
            + "renders things the ScenarioCamera never did — the mod's hands, cards, control board, "
            + "remote avatars and the converted world-space UI. The mod layer and the UI layer are "
            + "therefore ADDED BACK unconditionally and can never be dropped by this switch; "
            + "anything else that turns out to be needed will simply go invisible, which is why "
            + "this defaults off and why the log names every layer it drops, with its renderer "
            + "count, at the moment it drops it. Read the [Perf] SCENE line's per-layer breakdown "
            + "first: a layer with no renderers on it costs nothing to keep and gains nothing to "
            + "drop. Applies live; switching it back off restores the blanket mask immediately.");
    }
}
