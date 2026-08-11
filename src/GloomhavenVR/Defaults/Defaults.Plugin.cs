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
using GloomhavenVR.Hands;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Plugin.cs -----------------------------------------------------------------
    internal const bool General_Enabled = true;                              // => [General] Enabled
    internal const VRLogLevel LogLevel = VRLogLevel.Trace;                   // => [General] LogLevel
    internal const string RuntimeOverride = "";                              // => [General] RuntimeOverride
    internal const string RuntimePriority = "auto";                          // => [Core] RuntimePriority
    internal const bool SkipRuntimeCandidates = false;                       // => [Core] SkipRuntimeCandidates
    internal const bool EnableGraphicsJobs = true;                           // => [Core] EnableGraphicsJobs
    internal const bool AutoRestartForGraphicsJobs = true;                   // => [Core] AutoRestartForGraphicsJobs
    internal const int InitDelayFrames = 0;                                  // => [Core] InitDelayFrames
    internal const bool MenuRig = true;                                      // => [Rig] MenuRig
    internal const bool SpawnInCircle = true;                                // => [Rig] SpawnInCircle
    internal const bool Experimental3DMap = false;                           // => [Rig] Experimental3DMap
    internal const float WorldTiltDegrees = 0f;                              // => [Rig] WorldTiltDegrees
    internal const float MaskedReaimHeadRate = 30f;                          // => [Rig] MaskedReaimHeadRate
    internal const float MaskedReaimGain = 0.15f;                            // => [Rig] MaskedReaimGain
    internal const float MaskedReaimDeadband = 5f;                           // => [Rig] MaskedReaimDeadband
    internal const bool DisablePostProcessing = true;                        // => [Compat] DisablePostProcessing
    internal const bool DisableVolumetricFog = true;                         // => [Compat] DisableVolumetricFog
    internal const string DisableComponents = "";                            // => [Compat] DisableComponents
    internal const bool WallFade = true;                                     // => [Compat] WallFade
    internal const bool TutorialVRAdapt = true;                              // => [Compat] TutorialVRAdapt
    internal const string PrimaryHand = "Right";                             // => [Hands] PrimaryHand
    internal const Hands.HandStyle Hands_HandStyle = Hands.HandStyle.Glove;  // => [Hands] HandStyle
    internal const bool LaserFingerOrigin = false;                           // => [Hands] LaserFingerOrigin
    internal const bool ScrollWithStickOnly = true;                          // => [Hands] ScrollWithStickOnly
    internal const float LaserFingerOffsetMeters = 0.02f;                    // => [Hands] LaserFingerOffsetMeters
    internal const string HandColor = "D9C9B5";                              // => [Hands] HandColor
    internal static readonly Color VoidColor = new Color(0f, 0f, 0f, 1f);    // => [Rig] VoidColor
    internal const bool ForwardRendering = true;                             // => [Rig] ForwardRendering
    internal const bool Dev_Enabled = false;                                 // => [Dev] Enabled
    internal const bool Overlay = true;                                      // => [Dev] Overlay
    internal const bool SimulateHands = false;                               // => [Dev] SimulateHands
    internal const float InputDeviceDumpInterval = 0f;                       // => [Dev] InputDeviceDumpInterval
    internal const float GloveScale = 1.12f;                                 // => [Hands] GloveScale
    internal const float PlateScale = 0.62f;                                 // => [Hands] PlateScale
    internal const float ArcaneScale = 0.62f;                                // => [Hands] ArcaneScale
}
