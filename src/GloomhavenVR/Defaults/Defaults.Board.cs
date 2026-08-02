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
using GloomhavenVR.Board;
using GloomhavenVR.Board.FigureGrab;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Board/BoardConfig.cs ------------------------------------------------------
    internal const bool ForceFarMode = false;        // => [Board] ForceFarMode  (pinned: the tuned cfg's `true` predates the GRIP gate — the fingertip used to steal the pick from the laser on any accidental brush, so far-only was the only safe shipped setting. Since TouchTilesWithFingertip the near pick exists ONLY while the grip is held, which is what makes the feature safe to ship on; keeping the far-only override at `true` would ship it dead.)
    internal const bool TouchTilesWithFingertip = true;  // => [Board] TouchTilesWithFingertip
    internal const float TouchRange = 0.10f;         // => [Board] TouchRange
    internal const bool SnapToHexCenter = false;     // => [Board] SnapToHexCenter
    internal const bool HoverHaptics = true;         // => [Board] HoverHaptics
    internal const float AoeFlickThreshold = 0.6f;   // => [Board] AoeFlickThreshold
    internal const float AoeRepeatInterval = 0.35f;  // => [Board] AoeRepeatInterval

    // ---- Board/FigureGrab/FigureGrabConfig.cs --------------------------------------
    internal const bool GrabFigures = true;                 // => [FigureGrab] GrabFigures
    internal const float HeldScale = 1.5f;                  // => [FigureGrab] HeldScale  (legacy: read once as the seed for its successor)
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
    internal const bool KillBorderLine = false;      // => [HexHighlight] KillBorderLine
    internal const bool KillFill = false;            // => [HexHighlight] KillFill
    internal const bool LogMaterialDump = true;      // => [HexHighlight] LogMaterialDump

    // ---- Board/SelectionReadyHighlighter.cs ----------------------------------------
    internal const bool SelectionReady_Enabled = true;  // => [SelectionReady] Enabled
}
