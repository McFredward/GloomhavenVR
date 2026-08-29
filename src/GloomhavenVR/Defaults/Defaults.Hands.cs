// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Hands;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Hands/HandsConfig.cs ------------------------------------------------------
    internal const float GlovePinkyCounterAbduction = 14f;  // => [Hands] GlovePinkyCounterAbduction
    internal const bool GhostHandOnFan = true;              // => [Hands] GhostHandOnFan
    internal const bool GhostHandOnHeldCard = true;         // => [Hands] GhostHandOnHeldCard
    internal const bool HandsDisturbScenery = true;         // => [Hands] HandsDisturbScenery
    internal const bool HandsDisturbVfx = true;             // => [Hands] HandsDisturbVfx
    internal const float GhostHandStrength = 0.55f;         // => [Hands] GhostHandStrength
    internal const bool TestFist = false;                   // => [Hands] TestFist
    internal const float CurlProximal = 75f;                // => [Hands] CurlProximal
    internal const float CurlMiddle = 95f;                  // => [Hands] CurlMiddle
    internal const float CurlTip = 65f;                     // => [Hands] CurlTip
    internal const float CurlInputFullAt = 0.85f;           // => [Hands] CurlInputFullAt
    internal const float GloveGripPitchDegrees = -51f;      // => [Hands] GloveGripPitchDegrees
    internal const float PlateGripPitchDegrees = -45f;      // => [Hands] PlateGripPitchDegrees
    internal const float ArcaneGripPitchDegrees = -45f;     // => [Hands] ArcaneGripPitchDegrees
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
    // The plate hangs on the palm side; the base rotation is a half turn about the finger axis so
    // its READABLE face (a uGUI canvas reads from -Z) points out of the palm — see
    // WristHud.PalmFlat and the measured frame in WristHud.Build. The values below are the
    // hardware-tuned pose from the 2026-08-09 test, re-based mechanically from the dropped cfg.
    // They were captured on a build that ALREADY had the palm flip, so the trims are stated in the
    // post-flip plate frame (WristHud.ApplyPose composes base × trim) — do not re-express them.
    // The OFFSETS are in the wrist frame: Side = across the hand, Finger = toward the
    // fingertips, Lift = out of the palm. The gauntlet wears it close (5 cm down the arm, 2 cm
    // clear); plate and arcane push it 13 cm down the forearm and 7.5 cm out, where those bulkier
    // meshes leave room. Predecessors (GlovePitch/GloveOffsetX/...
    // against the old back-of-hand base) are deliberately gone rather than renamed in place:
    // those numbers were the correction for a base that no longer exists, and carrying them
    // over would cancel the turn-around. The offsets are also no longer named for an axis
    // LETTER — that is what made the X row unsteppable; see HandsConfig's bind block.
    internal const float GlovePalmPitch = 0f;               // => [WristHud] GlovePalmPitch
    internal const float PlatePalmPitch = 32f;              // => [WristHud] PlatePalmPitch
    internal const float ArcanePalmPitch = 32f;             // => [WristHud] ArcanePalmPitch
    internal const float GlovePalmYaw = 0f;                 // => [WristHud] GlovePalmYaw
    internal const float PlatePalmYaw = 1f;                 // => [WristHud] PlatePalmYaw
    internal const float ArcanePalmYaw = 1f;                // => [WristHud] ArcanePalmYaw
    internal const float GlovePalmRoll = 4f;                // => [WristHud] GlovePalmRoll
    internal const float PlatePalmRoll = -4f;               // => [WristHud] PlatePalmRoll
    internal const float ArcanePalmRoll = -4f;              // => [WristHud] ArcanePalmRoll
    internal const float GlovePalmSideOffset = 0f;          // => [WristHud] GlovePalmSideOffset
    internal const float PlatePalmSideOffset = 0.02f;       // => [WristHud] PlatePalmSideOffset
    internal const float ArcanePalmSideOffset = 0.02f;      // => [WristHud] ArcanePalmSideOffset
    internal const float GlovePalmFingerOffset = -0.05f;    // => [WristHud] GlovePalmFingerOffset
    internal const float PlatePalmFingerOffset = -0.13f;    // => [WristHud] PlatePalmFingerOffset
    internal const float ArcanePalmFingerOffset = -0.13f;   // => [WristHud] ArcanePalmFingerOffset
    internal const float GlovePalmLiftOffset = 0.02f;       // => [WristHud] GlovePalmLiftOffset
    internal const float PlatePalmLiftOffset = 0.075f;      // => [WristHud] PlatePalmLiftOffset
    internal const float ArcanePalmLiftOffset = 0.075f;     // => [WristHud] ArcanePalmLiftOffset
}
