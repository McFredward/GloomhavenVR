// GENERATED-AND-HAND-EDITABLE — the single home of every shipped config default.
//
// WHY THIS FILE EXISTS
// --------------------
// The mod binds ~550 config entries across 18 module config classes. Their default values
// were literals at the Bind call, which meant re-basing the shipped defaults onto a tuned
// setup was a hunt through the whole codebase. They all live here now, one per line, each
// tagged with the config identity it feeds:
//
//     internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward
//
// The `// => [Section] Key` annotation is the machine-readable part: scripts/rebase-defaults.py
// finds an entry by it, so a line may move but its annotation must stay exact.
//
// RULES
//   * one entry per line, initialiser a LITERAL (or a `new Vector3(...)`/`new Color(...)` of
//     literals) — never an expression that reads another entry;
//   * `const` wherever C# allows it, so the compiler inlines it and the compiled form is
//     identical to the old literal-at-the-bind;
//   * `static readonly` only for Vector2/Vector3/Color, which cannot be const;
//   * the *_ByBoard / *_ByStyle arrays exist so a per-variant bind inside a loop can index
//     them; they are assembled from the named entries above them and hold no literals.
//
// Editing: change the number, rebuild. Or drop a tuned cfg into .planning/debug/default/ and
// run `python3 scripts/rebase-defaults.py apply`.


using UnityEngine;
using GloomhavenVR.Core;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Core/MixedReality.cs ------------------------------------------------------
    internal const bool MixedReality_Enabled = false;                     // => [MixedReality] Enabled
    internal static readonly Color KeyColor = new Color(0f, 1f, 0f, 1f);  // => [MixedReality] KeyColor
    internal const bool HideSkyMeshes = true;                             // => [MixedReality] HideSkyMeshes

    // ---- Core/PerfConfig.cs --------------------------------------------------------
    internal const bool Perf_Enabled = true;                 // => [Perf] Enabled
    internal const float SummaryIntervalSeconds = 30f;       // => [Perf] SummaryIntervalSeconds
    internal const bool Attribution = true;                  // => [Perf] Attribution
    internal const int TopSteps = 6;                         // => [Perf] TopSteps
    internal const bool SpikeLines = true;                   // => [Perf] SpikeLines
    internal const float SpikeBudgetFactor = 2f;             // => [Perf] SpikeBudgetFactor
    internal const float SpikeMaxPerSecond = 2f;             // => [Perf] SpikeMaxPerSecond
    internal const bool Allocations = true;                  // => [Perf] Allocations
    internal const bool XrStats = true;                      // => [Perf] XrStats
    internal const bool FrameSplit = true;                   // => [Perf] FrameSplit
    internal const bool SceneCensus = true;                  // => [Perf] SceneCensus
    internal const bool SceneProfile = false;                // => [Perf] SceneProfile
    internal const bool CullSubmitSplit = false;             // => [Perf] CullSubmitSplit
    internal const bool CacheTickDelegates = true;           // => [Optimize] CacheTickDelegates
    internal const bool MapIconCache = true;                 // => [Optimize] MapIconCache
    internal const bool FigureScanCache = true;              // => [Optimize] FigureScanCache
    internal const bool LeanLogStrings = true;               // => [Optimize] LeanLogStrings
    internal const bool TooltipScanGate = true;              // => [Optimize] TooltipScanGate
    internal const float FanRelayoutMinInterval = 0f;        // => [Optimize] FanRelayoutMinInterval
    internal const float WallFadeEvalInterval = 0f;          // => [Optimize] WallFadeEvalInterval
    internal const bool QuietDiagnostics = false;            // => [Optimize] QuietDiagnostics
    internal const float RemoteContentInterval = 0f;         // => [Optimize] RemoteContentInterval
    internal const bool HeadDepthPrepass = false;            // => [Optimize] HeadDepthPrepass
    internal const string HeadCullingMaskDrop = "";          // => [Optimize] HeadCullingMaskDrop
    internal const bool HeadMaskFromScenarioCamera = false;  // => [Optimize] HeadMaskFromScenarioCamera

    // ---- Core/StaticBatchConfig.cs -------------------------------------------------
    internal const BatchMode Batching_Mode = BatchMode.On;  // => [Batching] Mode
    internal const string Roots = "Maps";                   // => [Batching] Roots
    internal const bool AutoDetectRoots = true;             // => [Batching] AutoDetectRoots
    internal const int MinRenderers = 8;                    // => [Batching] MinRenderers
    internal const int MaxVertices = 4_000_000;             // => [Batching] MaxVertices
    internal const float SettleSeconds = 4f;                // => [Batching] SettleSeconds
    internal const float RescanSeconds = 30f;               // => [Batching] RescanSeconds
    internal const int RescanGrowth = 8;                    // => [Batching] RescanGrowth
    internal const bool IncludeInactive = true;             // => [Batching] IncludeInactive
    internal const bool Watchdog = true;                    // => [Batching] Watchdog
    internal const bool WatchdogAutoRevert = true;          // => [Batching] WatchdogAutoRevert
    internal const bool WatchdogAutoExclude = true;         // => [Batching] WatchdogAutoExclude
    internal const string ExcludeLayers = "";               // => [Batching] ExcludeLayers
    internal const string ExcludeNames = "Glow";            // => [Batching] ExcludeNames
    internal const string ExcludeComponents = "";           // => [Batching] ExcludeComponents
    internal const bool FreeCombinedCpuCopy = true;         // => [Batching] FreeCombinedCpuCopy
    internal const bool VerboseLog = false;                 // => [Batching] VerboseLog

    // ---- Core/StereoModeConfig.cs --------------------------------------------------
    internal const string RenderMode = nameof(StereoModeConfig.Mode.MultiPass);  // => [Stereo] RenderMode

    // ---- Core/WallSegmentFade.cs ---------------------------------------------------
    internal const float OnFraction = 0.3f;                  // => [WallFade] OnFraction
    internal const float OffFraction = 0.3f;                 // => [WallFade] OffFraction
    internal const float ExitDwellMovedSeconds = 2.5f;       // => [WallFade] ExitDwellMovedSeconds
    internal const float ExitDwellStationarySeconds = 3.6f;  // => [WallFade] ExitDwellStationarySeconds
}
