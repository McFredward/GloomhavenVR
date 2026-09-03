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
    internal const bool GrabProps = true;                   // => [FigureGrab] GrabProps
    internal const bool HighlightWhileWalkIn = false;       // => [FigureGrab] HighlightWhileWalkIn
    internal const bool ClothFollowsFreeHand = true;        // => [FigureGrab] ClothFollowsFreeHand
    internal const float ClothHandReachMillimeters = 250f;  // => [FigureGrab] ClothHandReachMillimeters
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

    // ---- Board/FigureGrab/PropHeldPose.cs — the MAP-ITEM held pose ------------------
    // Eight keys mirroring the figures' eight EFFECTIVE held-pose dials one for one, so a chest
    // can be placed in the hand independently of a miniature (user, ModBuild 350: "ich will genau
    // das selbe nun auch für Map-Items ... separat einstellen können"). Every value here IS the
    // figure value this mod ships, taken from the entries above and from the shipped seed chain
    // they feed ({Style}HeldRotPitch <- {Style}HeldTiltDegrees <- HeldTiltDegrees = 17, etc.), so
    // the first build with these keys holds a prop exactly where ModBuild 349 held it.
    internal const float PropHeldOffsetSide = 0.051f;       // => [FigureGrab] PropHeldOffsetSide
    internal const float PropHeldOffsetUp = -0.013f;        // => [FigureGrab] PropHeldOffsetUp
    internal const float PropHeldOffsetForward = 0.05f;     // => [FigureGrab] PropHeldOffsetForward
    internal const float PropHeldRotPitch = 18f;            // => [FigureGrab] PropHeldRotPitch
    internal const float PropHeldRotYaw = -133f;            // => [FigureGrab] PropHeldRotYaw
    internal const float PropHeldRotRoll = 0f;              // => [FigureGrab] PropHeldRotRoll
    internal const bool PropHeldUpright = true;             // => [FigureGrab] PropHeldUpright
    internal const bool PropHeldUprightAtGrab = true;       // => [FigureGrab] PropHeldUprightAtGrab

    // ---- Board/HexHighlightFix.cs --------------------------------------------------
    // [HexHighlight] SwapStableShader / StableZTest / StableDepthBias / KillBorderFlame /
    // KillCrosshair had their lines here. All five were UNBOUND by the 2026-08-22 settings audit
    // ("Etwas was das spiel kaputt macht wenn man es umstellt ist nicht optional") — every one of
    // them decided how the TARGETING HIGHLIGHT draws, and StableZTest = 0 (CompareFunction.Never)
    // meant it did not draw at all. The values now live as constants in Board/HexHighlightFix,
    // beside the code that applies them.
    // [HexHighlight] KillBorderLine / KillFill are GONE (user ruling 2026-08-13): switching
    // them ON erased the hex outline and fill — the readout that says which field you are
    // acting on. See Board/HexHighlightFix.cs.
    internal const bool LogMaterialDump = true;      // => [HexHighlight] LogMaterialDump

    // ---- Board/SelectionReadyHighlighter.cs ----------------------------------------
    internal const bool SelectionReady_Enabled = true;  // => [SelectionReady] Enabled
}
