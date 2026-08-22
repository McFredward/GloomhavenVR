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
    // [RenderQuality] ViewportScaleFallback and RebuildRigOnMsaaChange had their lines here. Both
    // were UNBOUND by the 2026-08-22 settings audit — the fallback is now the constant
    // RenderQuality.ViewportScaleFallback, the rebuild path is deleted. See RenderQuality for why
    // neither was ever a choice a player could hold.
    // -1 STAYS THE SHIPPED VALUE, re-examined 2026-08-23 and deliberately not changed. Its own
    // description names the reason the round wanted it moved (a forward-path per-pixel light past
    // the first re-submits every renderer it touches) and RenderQuality.ApplyPixelLights names the
    // reason it is not moved: the one time it was measured it was worth ~1 %, back when main-thread
    // submission WAS the wall, and threaded submission has since taken that wall out. What is left
    // is a visible change to the dungeon's lighting — flatter point-light falloff on walls and
    // floors — which a default may not make silently. It is offered instead: the "Leistung" and
    // "Schwache Hardware" presets set it, by name, in a dropdown the player can pick back.
    internal const int PixelLightCount = -1;             // => [RenderQuality] PixelLightCount
    // 0 = the "Qualität" preset, which is exactly what the three rows above spell at their own
    // defaults (MSAA 8x, eye 1.00x, lights untouched) — so a fresh install reads back as a named
    // preset rather than as "Eigene". This value is a MIRROR of those three, never a master; see
    // RenderQuality.QualityPreset. Any other starting number would be a lie the first tick corrects.
    internal const int QualityPreset = 0;                // => [RenderQuality] QualityPreset
}
