namespace GloomhavenVR;

// -------------------------------------------------------------------------------------------------
//  SPATIAL VOICE CHAT — every shipped default, one annotated line each.
//
//  RULES (see Defaults.Core.cs for the full statement): one entry per line, initialiser a LITERAL,
//  `const` wherever C# allows it, and the trailing `// => [Section] Key` annotation is MACHINE
//  READABLE — scripts/check-remote-defaults.py and scripts/rebase-defaults.py both parse it.
//
//  A CLAMP FALLBACK IS NOT A DEFAULT. The numbers below are the shipped values; any Clamped(...)
//  or AcceptableValueRange elsewhere is a bound, not a default. This project has lost two rounds
//  to that confusion, twice.
//
//  DISTANCES ARE PERCEIVED METRES. Not world units. They are multiplied by rigScale (world units
//  per perceived metre, ~13 to ~26 in a scenario) in exactly one place, VoiceSpatial.ApplyScale.
//  See Core/EnvSound.cs:128-155 for the convention and what ignoring it costs.
// -------------------------------------------------------------------------------------------------

internal static partial class Defaults
{
    // ---- Voice/VoiceModule.cs ------------------------------------------------------------------
    internal const bool Voice_Enabled = true;              // => [Voice] Enabled
    internal const float VoiceSpatialBlend = 1f;           // => [Voice] SpatialBlend
    internal const float VoiceFullLevelMeters = 2f;        // => [Voice] FullLevelMeters
    internal const float VoiceSilenceMeters = 25f;         // => [Voice] SilenceMeters
    internal const float VoiceRolloffShape = 1.6f;         // => [Voice] RolloffShape
    internal const float VoiceSpread = 35f;                // => [Voice] Spread
    // ---- Voice/VoiceBadge.cs -------------------------------------------------------------------
    internal const bool VoiceSpeakingBadge = true;         // => [Voice] SpeakingBadge
    internal const float VoiceBadgeScale = 0.38f;          // => [Voice] BadgeScale
}
