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

    /// <summary>Suppress the high-cadence per-subsystem diagnostic lines (wall fade, fan depth curve, uGUI clicks).</summary>
    internal static ConfigEntry<bool> QuietDiagnostics = null!;

    /// <summary>Seconds between remote-board content refreshes (0 = leave the subsystem's own cadence).</summary>
    internal static ConfigEntry<float> RemoteContentInterval = null!;

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

    /// <summary>[Optimize] QuietDiagnostics, defaulting to off (keep today's diagnostics) while unbound.</summary>
    internal static bool Quiet => QuietDiagnostics != null && QuietDiagnostics.Value;

    /// <summary>[Optimize] RemoteContentInterval, 0 = keep the subsystem's own cadence.</summary>
    internal static float RemoteContentSeconds =>
        RemoteContentInterval == null ? 0f : Mathf.Clamp(RemoteContentInterval.Value, 0f, 2f);

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
        Enabled = _file.Bind("Perf", "Enabled", true,
            "Master switch for the performance instrumentation. ON logs a periodic [Perf] FRAME / "
            + "STEPS summary plus a [Perf] SPIKE line for every frame that blows the display's frame "
            + "budget — the numbers that turn 'it judders when I turn my head fast' into an "
            + "attributable subsystem. OFF makes the whole layer a no-op (one bool test per frame, "
            + "no stopwatch reads, no dictionary lookups, no log lines).");
        SummaryIntervalSeconds = _file.Bind("Perf", "SummaryIntervalSeconds", 30f, new ConfigDescription(
            "Seconds between the periodic [Perf] FRAME and [Perf] STEPS summary lines. Lower = a "
            + "finer time resolution in the log at the cost of more lines; the per-frame sampling "
            + "cost does not change with this value.",
            new AcceptableValueRange<float>(5f, 600f)));
        Attribution = _file.Bind("Perf", "Attribution", true,
            "Measure each NAMED mod step (every TickGuard.Run step plus the explicitly scoped driver "
            + "bodies) with a stopwatch, so the [Perf] STEPS line can rank the mod's own subsystems by "
            + "cost and a SPIKE line can name who owned the spike. This is the single most valuable "
            + "piece of the instrumentation: it answers 'is the hitch OURS?'. Costs two "
            + "QueryPerformanceCounter reads and one dictionary lookup per step per frame (a few "
            + "microseconds in total). OFF still measures frame pacing, just without attribution.");
        TopSteps = _file.Bind("Perf", "TopSteps", 6, new ConfigDescription(
            "How many mod steps the periodic [Perf] STEPS line ranks (by total time spent in the "
            + "window). Ignored while Attribution is off.",
            new AcceptableValueRange<int>(1, 20)));
        SpikeLines = _file.Bind("Perf", "SpikeLines", true,
            "Emit a [Perf] SPIKE line whenever a single frame takes longer than SpikeBudgetFactor x "
            + "the display's frame budget, naming the worst mod steps IN THAT FRAME. This is the line "
            + "that catches the reported 'world judders on a fast head turn' — a judder is a handful "
            + "of late frames, which an average can never show.");
        SpikeBudgetFactor = _file.Bind("Perf", "SpikeBudgetFactor", 2f, new ConfigDescription(
            "A frame counts as a SPIKE when it takes longer than this multiple of the display's frame "
            + "budget (budget = 1 / actual refresh rate, read from the XR display — not a hardcoded "
            + "72/90 Hz). 2.0 = 'at least one whole displayed frame was missed'. Lower catches more, "
            + "at the price of more SPIKE lines.",
            new AcceptableValueRange<float>(1.2f, 10f)));
        SpikeMaxPerSecond = _file.Bind("Perf", "SpikeMaxPerSecond", 2f, new ConfigDescription(
            "Hard ceiling on SPIKE lines per second. A genuinely bad stretch would otherwise emit one "
            + "line per frame and the logging itself would become the performance problem; suppressed "
            + "spikes are still COUNTED and reported in the next FRAME summary, so nothing is lost. "
            + "There is deliberately no 'unlimited' setting.",
            new AcceptableValueRange<float>(0.1f, 20f)));
        Allocations = _file.Bind("Perf", "Allocations", true,
            "Sample GC collection counts and managed-heap growth once per frame and report them per "
            + "window. A gen0 collection landing inside a head turn is exactly the reported symptom, "
            + "so allocation pressure is first-class evidence here. Cost: one cheap heap-size read "
            + "per frame.");
        XrStats = _file.Bind("Perf", "XrStats", true,
            "Ask the XR display subsystem for its OWN truth — dropped frames, presented frames, GPU "
            + "time, motion-to-photon latency — which is the only way to see reprojection (the "
            + "compositor covering for us) rather than just our CPU timings. Counters the runtime "
            + "does not expose are reported as 'n/a' in the log, never as a fake zero.");

        // ---- [Optimize] ----------------------------------------------------------------------
        CacheTickDelegates = _file.Bind("Optimize", "CacheTickDelegates", true,
            "Cache the Action delegates handed to TickGuard.Run instead of re-creating them from an "
            + "instance method group every frame. Pure work removal — identical behaviour, just "
            + "without ~7 delegate allocations per frame feeding the gen0 collector that causes "
            + "head-turn hitches. OFF restores the old per-frame allocation (A/B only).");
        MapIconCache = _file.Bind("Optimize", "MapIconCache", true,
            "Cache the campaign-map icon scan. The map-icon draw runs from Camera.onPreCull — once "
            + "PER RENDERING CAMERA PER FRAME, and the stereo flat screen has two or three — and it "
            + "used to redo a full-scene FindObjectOfType, two allocating component walks, a "
            + "GetComponent per decal, a fresh MaterialPropertyBlock per decal and a long diagnostic "
            + "string per decal every single time. The decal SET only changes with the map state, so "
            + "it is scanned on an interval while every icon's pose is still read live each frame — "
            + "panning and zooming are pixel-identical. OFF restores the per-frame scan (A/B).");
        FigureScanCache = _file.Bind("Optimize", "FigureScanCache", true,
            "Skip per-frame component walks the figure-grab driver does not need: an already-adopted "
            + "figure is no longer re-resolved through GetComponentInChildren every frame, and the "
            + "ring suppressor takes its 'nothing is held' early-out BEFORE it allocates the "
            + "enumerators it would have iterated. Behaviour-identical work removal; OFF restores "
            + "the unconditional walks.");
        LeanLogStrings = _file.Bind("Optimize", "LeanLogStrings", true,
            "Do not BUILD diagnostic strings that the log then throws away. Several diagnostics are "
            + "throttled or change-gated inside the callee, so the interpolated message (plus the "
            + "UnityEngine.Object.name access, which allocates a fresh string every read) was paid "
            + "for on every frame while only one line in hundreds was printed. The gates now sit in "
            + "front of the string work instead of behind it. Identical log output either way.");
        TooltipScanGate = _file.Bind("Optimize", "TooltipScanGate", true,
            "Gate the world-tooltip subsystem's per-frame work on whether a tooltip is actually "
            + "shown: the full canvas subtree walk used to run before that check, and the fallback "
            + "search for the game's CanvasManager retried a full-scene FindObjectOfType every frame "
            + "for as long as it stayed unresolved. Identical tooltips, far fewer scans.");
        FanRelayoutMinInterval = _file.Bind("Optimize", "FanRelayoutMinInterval", 0f, new ConfigDescription(
            "Minimum seconds between two GAZE-driven re-layouts of the open card fan. 0 = re-lay out "
            + "on every frame the gaze gate trips, which is today's behaviour and what a fast head "
            + "turn does every single frame. A small value (0.02 = 50 Hz) removes the redundant "
            + "re-layouts a >72 Hz display asks for without any visible change, because each card's "
            + "own home-lerp smooths whatever the gate lets through. Only affects the GAZE gate — "
            + "card set changes, hovers, plucks and the fan-out reveal always re-lay out immediately.",
            new AcceptableValueRange<float>(0f, 0.2f)));
        WallFadeEvalInterval = _file.Bind("Optimize", "WallFadeEvalInterval", 0f, new ConfigDescription(
            "Minimum seconds between two wall see-through VISIBILITY evaluations (the per-segment "
            + "sample sweep). 0 = every frame, today's behaviour. The evaluation already feeds a "
            + "Schmitt trigger with second-scale dwell hysteresis, so sampling it at e.g. 0.05 "
            + "(20 Hz) cannot change which walls fade — it only stops re-deciding a decision that is "
            + "deliberately slow. Inert unless [Compat] WallFade is on.",
            new AcceptableValueRange<float>(0f, 0.25f)));
        QuietDiagnostics = _file.Bind("Optimize", "QuietDiagnostics", false,
            "Suppress the high-cadence per-subsystem DIAGNOSTIC log lines (the wall-fade 'diag:' "
            + "sweep, the card-fan 'Fan depth-curve:' sweep recorder, the per-click uGUI trace). "
            + "Default OFF because those lines are the evidence base for several open investigations "
            + "and they measured well under one line per second on hardware — switch it ON for a "
            + "clean performance capture where only the [Perf] lines matter.");
        RemoteContentInterval = _file.Bind("Optimize", "RemoteContentInterval", 0f, new ConfigDescription(
            "Override the refresh interval (seconds) of the REMOTE player board content scan — the "
            + "4 Hz walk that rebuilds other players' board contents in multiplayer. 0 = leave the "
            + "subsystem's own 0.25 s cadence. Raising it (e.g. 0.5) halves that walk's cost; it only "
            + "delays how fast a peer's board contents catch up, and it does nothing at all in single "
            + "player.",
            new AcceptableValueRange<float>(0f, 2f)));
    }
}
