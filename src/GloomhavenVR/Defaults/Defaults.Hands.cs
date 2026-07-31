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
    internal const float GlovePitch = -88f;                 // => [WristHud] GlovePitch
    internal const float PlatePitch = -102f;                // => [WristHud] PlatePitch
    internal const float ArcanePitch = -102f;               // => [WristHud] ArcanePitch
    internal const float GloveYaw = -180f;                  // => [WristHud] GloveYaw
    internal const float PlateYaw = -180f;                  // => [WristHud] PlateYaw
    internal const float ArcaneYaw = -180f;                 // => [WristHud] ArcaneYaw
    internal const float GloveRoll = -3f;                   // => [WristHud] GloveRoll
    internal const float PlateRoll = 0f;                    // => [WristHud] PlateRoll
    internal const float ArcaneRoll = 0f;                   // => [WristHud] ArcaneRoll
    internal const float GloveOffsetX = -0.003f;            // => [WristHud] GloveOffsetX
    internal const float PlateOffsetX = 0.0185f;            // => [WristHud] PlateOffsetX
    internal const float ArcaneOffsetX = 0.0185f;           // => [WristHud] ArcaneOffsetX
    internal const float GloveOffsetY = -0.053f;            // => [WristHud] GloveOffsetY
    internal const float PlateOffsetY = -0.147f;            // => [WristHud] PlateOffsetY
    internal const float ArcaneOffsetY = -0.147f;           // => [WristHud] ArcaneOffsetY
    internal const float GloveOffsetZ = -0.005f;            // => [WristHud] GloveOffsetZ
    internal const float PlateOffsetZ = 0.052f;             // => [WristHud] PlateOffsetZ
    internal const float ArcaneOffsetZ = 0.052f;            // => [WristHud] ArcaneOffsetZ
}
