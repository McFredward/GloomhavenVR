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
using GloomhavenVR.Hands;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Hands/HandsConfig.cs ------------------------------------------------------
    internal const float GlovePinkyCounterAbduction = 14f;  // => [Hands] GlovePinkyCounterAbduction
    internal const bool GhostHandOnFan = true;              // => [Hands] GhostHandOnFan
    internal const bool GhostHandOnHeldCard = true;         // => [Hands] GhostHandOnHeldCard
    internal const float GhostHandStrength = 0.55f;         // => [Hands] GhostHandStrength
    internal const bool TestFist = false;                   // => [Hands] TestFist
    internal const float CurlProximal = 75f;                // => [Hands] CurlProximal
    internal const float CurlMiddle = 95f;                  // => [Hands] CurlMiddle
    internal const float CurlTip = 65f;                     // => [Hands] CurlTip
    internal const float CurlInputFullAt = 0.85f;           // => [Hands] CurlInputFullAt
    internal const float GloveGripPitchDegrees = -39f;      // => [Hands] GloveGripPitchDegrees
    internal const float PlateGripPitchDegrees = -26f;      // => [Hands] PlateGripPitchDegrees
    internal const float ArcaneGripPitchDegrees = -26f;     // => [Hands] ArcaneGripPitchDegrees
    internal const float GloveLateralOffset = -0.01f;       // => [Hands] GloveLateralOffset
    internal const float PlateLateralOffset = 0f;           // => [Hands] PlateLateralOffset
    internal const float ArcaneLateralOffset = 0f;          // => [Hands] ArcaneLateralOffset
    internal const float GloveVerticalOffset = 0.061f;      // => [Hands] GloveVerticalOffset
    internal const float PlateVerticalOffset = -0.009f;     // => [Hands] PlateVerticalOffset
    internal const float ArcaneVerticalOffset = -0.009f;    // => [Hands] ArcaneVerticalOffset
    internal const float GloveForwardOffset = -0.054f;      // => [Hands] GloveForwardOffset
    internal const float PlateForwardOffset = -0.009f;      // => [Hands] PlateForwardOffset
    internal const float ArcaneForwardOffset = -0.009f;     // => [Hands] ArcaneForwardOffset
    internal const float GloveGripRollDegrees = -109f;      // => [Hands] GloveGripRollDegrees
    internal const float PlateGripRollDegrees = -109f;      // => [Hands] PlateGripRollDegrees
    internal const float ArcaneGripRollDegrees = -109f;     // => [Hands] ArcaneGripRollDegrees
    internal const float GloveGripYawDegrees = -35f;        // => [Hands] GloveGripYawDegrees
    internal const float PlateGripYawDegrees = -35f;        // => [Hands] PlateGripYawDegrees
    internal const float ArcaneGripYawDegrees = -35f;       // => [Hands] ArcaneGripYawDegrees
    internal const float GloveSpreadOffset = 0.07f;         // => [Hands] GloveSpreadOffset
    internal const float PlateSpreadOffset = 0.05f;         // => [Hands] PlateSpreadOffset
    internal const float ArcaneSpreadOffset = 0.05f;        // => [Hands] ArcaneSpreadOffset
    // ---- the wrist HUD's PALM pose (2026-08-09 turn-around) -------------------------------
    // The plate now hangs on the palm side and the base rotation IS the wanted orientation
    // (WristHud.PalmFlat = the wrist anchor's own frame, derived in WristHud.Build), so every
    // per-style TRIM ships 0 and the user has nothing to dial to get the requested look. The
    // OFFSETS are in that same wrist frame: Side = across the hand, Finger = toward the
    // fingertips, Lift = out of the palm. 2 cm back down the forearm and 2 cm clear of the palm
    // puts a 9.6 x 7.8 cm plate over the inner wrist, where a watch is worn, without
    // intersecting any of the three hand meshes. Predecessors (GlovePitch/GloveOffsetX/...
    // against the old back-of-hand base) are deliberately gone rather than renamed in place:
    // those numbers were the correction for a base that no longer exists, and carrying them
    // over would cancel the turn-around. The offsets are also no longer named for an axis
    // LETTER — that is what made the X row unsteppable; see HandsConfig's bind block.
    internal const float GlovePalmPitch = 0f;               // => [WristHud] GlovePalmPitch
    internal const float PlatePalmPitch = 0f;               // => [WristHud] PlatePalmPitch
    internal const float ArcanePalmPitch = 0f;              // => [WristHud] ArcanePalmPitch
    internal const float GlovePalmYaw = 0f;                 // => [WristHud] GlovePalmYaw
    internal const float PlatePalmYaw = 0f;                 // => [WristHud] PlatePalmYaw
    internal const float ArcanePalmYaw = 0f;                // => [WristHud] ArcanePalmYaw
    internal const float GlovePalmRoll = 0f;                // => [WristHud] GlovePalmRoll
    internal const float PlatePalmRoll = 0f;                // => [WristHud] PlatePalmRoll
    internal const float ArcanePalmRoll = 0f;               // => [WristHud] ArcanePalmRoll
    internal const float GlovePalmSideOffset = 0f;          // => [WristHud] GlovePalmSideOffset
    internal const float PlatePalmSideOffset = 0f;          // => [WristHud] PlatePalmSideOffset
    internal const float ArcanePalmSideOffset = 0f;         // => [WristHud] ArcanePalmSideOffset
    internal const float GlovePalmFingerOffset = -0.02f;    // => [WristHud] GlovePalmFingerOffset
    internal const float PlatePalmFingerOffset = -0.02f;    // => [WristHud] PlatePalmFingerOffset
    internal const float ArcanePalmFingerOffset = -0.02f;   // => [WristHud] ArcanePalmFingerOffset
    internal const float GlovePalmLiftOffset = 0.02f;       // => [WristHud] GlovePalmLiftOffset
    internal const float PlatePalmLiftOffset = 0.02f;       // => [WristHud] PlatePalmLiftOffset
    internal const float ArcanePalmLiftOffset = 0.02f;      // => [WristHud] ArcanePalmLiftOffset
}
