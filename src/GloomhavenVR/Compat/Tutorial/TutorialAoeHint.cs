using System;
using GloomhavenVR.Board;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;

namespace GloomhavenVR.Compat;

/// <summary>Replace the native Tinkerer rotation hint before generic flat-controls matching.
/// Verified in resources.assets LanguageSource terms: desktop 5B_08 teaches R; the controller
/// variant uses UI_RETRY, whose gamepad glyph is blank in VR. Match message keys, not resolved
/// text, so missing glyphs and either language still teach the actual physical binding.</summary>
internal static class TutorialAoeHint
{
    internal static bool TryOverride(string? key, string? controllerKey, out string text)
    {
        text = string.Empty;
        if (!Matches(key) && !Matches(controllerKey))
            return false;
        TutorialAoeHintRefresh.EnsureBound();
        text = Loc.Mod(!AoeControl.UsesStick ? "tut_vr_aoe_buttons"
            : AoeControl.ResolveRotationSide() == HandSide.Left
            ? "tut_vr_aoe_left" : "tut_vr_aoe_right");
        return true;
    }

    private static bool Matches(string? key)
    {
        if (key == null)
            return false;
        const string category = "Consoles/";
        if (key.StartsWith(category, StringComparison.OrdinalIgnoreCase))
            key = key.Substring(category.Length);
        return string.Equals(key, "SCENARIO_PUZZLE_5B_08", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "SCENARIO_PUZZLE_5B_ROTATE_MESSAGE", StringComparison.OrdinalIgnoreCase);
    }
}
