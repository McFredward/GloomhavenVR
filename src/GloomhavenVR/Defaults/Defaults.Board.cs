// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Board;
using GloomhavenVR.Board.FigureGrab;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Board/BoardConfig.cs ------------------------------------------------------
    internal const bool TouchTilesWithFingertip = true;  // => [Board] TouchTilesWithFingertip
    internal const float TouchRange = 0.10f;         // => [Board] TouchRange
    internal const bool SnapToHexCenter = false;     // => [Board] SnapToHexCenter
    internal const bool HoverHaptics = true;         // => [Board] HoverHaptics
    internal const float AoeFlickThreshold = 0.6f;   // => [Board] AoeFlickThreshold
    internal const float AoeRepeatInterval = 0.35f;  // => [Board] AoeRepeatInterval
    internal const bool AutoFocusOnTurn = true;      // => [Board] AutoFocusOnTurn

    // ---- Board/FigureGrab/FigureGrabConfig.cs --------------------------------------
    internal const bool GrabFigures = true;                 // => [FigureGrab] GrabFigures
    internal const float PickRadiusMillimeters = 40f;       // => [FigureGrab] PickRadiusMillimeters
    internal const float StretchReachMillimeters = 80f;     // => [FigureGrab] StretchReachMillimeters
    internal const float StretchScaleMin = 0.5f;            // => [FigureGrab] StretchScaleMin
    internal const float StretchScaleMax = 3f;              // => [FigureGrab] StretchScaleMax
    internal const bool StretchLimits = true;               // => [FigureGrab] StretchLimits
    internal const bool HeldFigureInfo = true;              // => [FigureGrab] HeldFigureInfo
    internal const float HeldOffsetForward = 0.05f;         // => [FigureGrab] HeldOffsetForward  (legacy: read once as the seed for its successor)
    internal const float HeldOffsetUp = 0.01f;              // => [FigureGrab] HeldOffsetUp  (legacy: read once as the seed for its successor)
    internal const float HeldOffsetSide = 0.03f;            // => [FigureGrab] HeldOffsetSide  (legacy: read once as the seed for its successor)
    internal const bool HeldUprightAtGrab = true;           // => [FigureGrab] HeldUprightAtGrab
    internal const bool HeldUpright = true;                 // => [FigureGrab] HeldUpright
    internal const float FigureGrab_HeldTiltDegrees = 17f;  // => [FigureGrab] HeldTiltDegrees  (legacy: read once as the seed for its successor)
    internal const float HeldFaceYawDegrees = -133f;        // => [FigureGrab] HeldFaceYawDegrees  (legacy: read once as the seed for its successor)
    internal const float GloveHeldRollDegrees = 0f;         // => [FigureGrab] GloveHeldRollDegrees  (legacy: read once as the seed for its successor)
    internal const float PlateHeldRollDegrees = 0f;         // => [FigureGrab] PlateHeldRollDegrees  (legacy: read once as the seed for its successor)
    internal const float ArcaneHeldRollDegrees = 0f;        // => [FigureGrab] ArcaneHeldRollDegrees  (legacy: read once as the seed for its successor)
    internal static readonly float[] HeldRollDegrees_ByStyle = { GloveHeldRollDegrees, PlateHeldRollDegrees, ArcaneHeldRollDegrees };

    // ---- Board/HexHighlightFix.cs --------------------------------------------------
    internal const bool SwapStableShader = true;     // => [HexHighlight] SwapStableShader
    internal const int StableZTest = 4;              // => [HexHighlight] StableZTest
    internal const float StableDepthBias = 0.0002f;  // => [HexHighlight] StableDepthBias
    internal const bool KillBorderFlame = true;      // => [HexHighlight] KillBorderFlame
    internal const bool KillCrosshair = true;        // => [HexHighlight] KillCrosshair
    // [HexHighlight] KillBorderLine / KillFill are GONE (user ruling 2026-08-13): switching
    // them ON erased the hex outline and fill — the readout that says which field you are
    // acting on. See Board/HexHighlightFix.cs.
    internal const bool LogMaterialDump = true;      // => [HexHighlight] LogMaterialDump

    // ---- Board/SelectionReadyHighlighter.cs ----------------------------------------
    internal const bool SelectionReady_Enabled = true;  // => [SelectionReady] Enabled
}
