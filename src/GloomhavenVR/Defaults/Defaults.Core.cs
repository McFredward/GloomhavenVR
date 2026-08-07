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
    internal const bool MixedReality_Enabled = false;                     // => [MixedReality] Enabled  (pinned: user ruling 2026-08-04 — MR see-through is the OWNER's personal setup, a fresh install must start in full VR; the tuned cfg's `true` briefly shipped in ModBuild 55 by accident and was reverted the same day)
    internal static readonly Color KeyColor = new Color(0f, 1f, 0f, 1f);  // => [MixedReality] KeyColor
    internal const bool HideSkyMeshes = true;                             // => [MixedReality] HideSkyMeshes
    internal const bool OpaquePreviewTiles = true;                        // => [MixedReality] OpaquePreviewTiles
    internal const float UnseenSkirtScale = 1.2f;                         // => [MixedReality] UnseenSkirtScale  (round 7: XZ widening of the GROOVE-FILL copy so neighboring fills overlap under the bevel channels; the primary underlay is exact 1:1)
    internal const float UnseenRimInset = 0.03f;                          // => [MixedReality] UnseenRimInset  (round 15: world-units the mod-BUILT rim-curtain prism stands inside each unseen piece's authored vertical side faces; 0.03 on a piece whose whole height is 0.3 wu — deep enough that a damaged/notched edge cannot expose it, shallow enough that the leak band at the outer silhouette stays sub-pixel)
    internal const float UnseenRimTopClearance = 0.025f;                   // => [MixedReality] UnseenRimTopClearance  (round 15: world-units the rim curtain's cap stays below each piece's mesh-top plane; > UnseenWaferDrop by design, and re-forced to waferDrop + 0.005 at build time, so the curtain always hides behind the XZ x1.2 wider wafer and no authored top face is ever painted over)
    internal const float UnseenWaferDrop = 0.02f;                         // => [MixedReality] UnseenWaferDrop  (round 12: FRESH KEY replacing UnseenFillDrop — the old key's persisted 0.35 deep-fill value survived round 11's default change and re-opened the seam canyon (ModBuild-69 log: 'wafer = mesh-top − 0.35 wu'); world-units the wafer sits below each piece's mesh-top plane, 0.02 hugs the top without z-fighting)

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

    // ---- Core/StereoModeConfig.cs --------------------------------------------------
    internal const string RenderMode = nameof(StereoModeConfig.Mode.MultiPass);  // => [Stereo] RenderMode

    // ---- Core/WallSegmentFade.cs ---------------------------------------------------
    internal const float OnFraction = 0.3f;                  // => [WallFade] OnFraction
    internal const float OffFraction = 0.3f;                 // => [WallFade] OffFraction
    internal const float ExitDwellMovedSeconds = 2.5f;       // => [WallFade] ExitDwellMovedSeconds
    internal const float ExitDwellStationarySeconds = 3.6f;  // => [WallFade] ExitDwellStationarySeconds
    internal const bool StackedShellFade = true;             // => [WallFade] StackedShellFade
    // Shipped ON so the next MP hardware test shows the peer-synced fades without cfg fiddling
    // (receiver-side gate; own fades are always broadcast — WallSegmentFade.Net.cs).
    internal const bool SyncPeerFades = true;                // => [WallFade] SyncPeerFades
}
