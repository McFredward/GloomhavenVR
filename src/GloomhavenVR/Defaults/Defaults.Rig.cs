// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Rig;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Rig/ComfortSettings.cs ----------------------------------------------------
    internal const bool WorldGrabEnabled = true;                    // => [Comfort] WorldGrabEnabled
    internal const bool FreeMovement = true;                        // => [Comfort] FreeMovement
    internal const bool VerticalDrag = false;                       // => [Comfort] VerticalDrag
    internal const bool RotateEnabled = true;                       // => [Comfort] RotateEnabled
    internal const bool ScaleEnabled = true;                        // => [Comfort] ScaleEnabled
    internal const float ScaleMin = 0.5f;                           // => [Comfort] ScaleMin
    internal const float ScaleMax = 4f;                             // => [Comfort] ScaleMax
    internal const TurnMode Comfort_TurnMode = TurnMode.Smooth;     // => [Comfort] TurnMode
    internal const float SnapTurnDegrees = 45f;                     // => [Comfort] SnapTurnDegrees
    internal const float SmoothTurnSpeed = 90f;                     // => [Comfort] SmoothTurnSpeed
    internal const TurnHandChoice TurnHand = TurnHandChoice.Right;  // => [Comfort] TurnHand
    internal const bool FlightEnabled = true;                       // => [Comfort] FlightEnabled
    internal const FlightDirectionSource FlightDirection = FlightDirectionSource.Hand;  // => [Comfort] FlightDirection
    internal const float FlightMaxSpeed = 1.43362f;                 // => [Comfort] FlightMaxSpeed
    internal const TurnHandChoice FlightHand = TurnHandChoice.Right;  // => [Comfort] FlightHand
    internal const bool TurnStickVertical = false;                  // => [Comfort] TurnStickVertical  (pinned: the user asked for it as an OPTION, and its off state is the pre-feature behaviour)
    // [Comfort] TableHeightOffset is GONE (user ruling 2026-08: free locomotion replaced it).
    // No line here on purpose — a tuned cfg that still carries the key is reported as UNMAPPED
    // by scripts/rebase-defaults.py, which is exactly right for a retired key.
    internal const float RecenterHoldSeconds = 1.0f;                // => [Comfort] RecenterHoldSeconds
    internal const float SavedScaleMultiplier = 1.6499f;            // => [Comfort] SavedScaleMultiplier
    internal const bool DebugGizmos = false;                        // => [Comfort] DebugGizmos
    internal const bool KeepPlaceOnReorigin = true;                 // => [Comfort] KeepPlaceOnReorigin
    internal const bool TableScaleDefault25Applied = false;         // => [Comfort] TableScaleDefault25Applied  (pinned: one-shot migration marker — a fresh install must start false)

    // ---- Rig/RenderQuality.cs ------------------------------------------------------
    internal const int MsaaLevel = 8;                    // => [RenderQuality] MsaaLevel
    internal const bool ForceAnisotropic = true;         // => [RenderQuality] ForceAnisotropic
    internal const float EyeResolutionScale = 1.0f;      // => [RenderQuality] EyeResolutionScale
    internal const bool ViewportScaleFallback = true;    // => [RenderQuality] ViewportScaleFallback
    internal const bool RebuildRigOnMsaaChange = false;  // => [RenderQuality] RebuildRigOnMsaaChange
    internal const int PixelLightCount = -1;             // => [RenderQuality] PixelLightCount
}
