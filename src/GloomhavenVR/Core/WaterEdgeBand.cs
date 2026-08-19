using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE EDGE / FOAM / BORDER BAND — the arithmetic of collapsing it, kept in a file free of
/// everything but <see cref="Mathf"/> and <see cref="Color"/> so it can be driven property by
/// property in <c>tests/GloomhavenVR.WireTests</c>, without a headset and without a game.
///
/// <para>WHY THIS FILE EXISTS, AND WHAT THE PHOTOGRAPH PROVED. Through ModBuild 160 this module
/// tuned the BODY of the water film — <c>_Color_Tint</c>'s alpha, and the reflection scalars. The
/// hardware log says those writes landed (<c>_Smoothness 0.754 -> 0.08</c>, <c>_Color_Tint.a</c>
/// capped at 0.45, <c>_Edge_Colour</c> set to the body colour, <c>_EDGECOLOUR_TOGGLE_ON</c>
/// cleared) and the user's verdict was <i>"Keinen Unterschied bei der Reflektion."</i>
/// <c>.planning/debug/spiegeltiles.jpg</c> is what nobody had put beside the numbers: the affected
/// hexes render as a flat, pale, milky near-WHITE sheet, LIGHTER than the stone around them, with
/// the hex grid showing through. The authored body colour is <c>_Color_Tint</c> =
/// RGBA(0.195, 0.311, 0.131, 0.737) — dark green. A dark green body cannot produce a near-white
/// sheet. <b>So the visible pixels are not the body term at all; they are the edge/foam/border
/// term</b>, whose authored colour <c>_Edge_Colour</c> = RGBA(0.887, 0.887, 0.887, 0.867) is
/// near-white and whose second, separate mechanism <c>_WaterBorderCol</c> =
/// RGBA(0.670, 0.617, 0.528, 0.561) is a pale beige. That also explains, without any further
/// hypothesis, why capping the tint alpha changed nothing the user could see: he was never looking
/// at the tint.</para>
///
/// <para>WHY NUMBERS AND NOT A KEYWORD. ModBuild 160's answer to the foam was
/// <c>DisableKeyword("_EDGECOLOUR_TOGGLE_ON")</c> plus recolouring <c>_Edge_Colour</c>. The log can
/// only report that we CALLED those; it cannot report that the compiled variant branches the way
/// we assumed, and there is a whole second border mechanism — <c>_WaterBorderWidth</c> /
/// <c>_WaterBorderCol</c> — which carries no toggle keyword whatsoever and which nothing in this
/// module had ever touched. So the band is now collapsed NUMERICALLY as well: every width to zero,
/// every band colour to the body hue at alpha zero. A band of width zero covers no pixels at either
/// extreme of a pinned depth fade, which is the property that makes this attack independent of a
/// shader nobody here can open.</para>
///
/// <para>THE INVARIANT, and the only thing that makes an unattended write onto a compiled shader
/// defensible: <b>no collapse in this file may ever raise a value above what the tileset
/// authored.</b> A width already at or below zero is left exactly alone rather than written to
/// zero; a band colour already fully transparent is left alone. There is no exception — nothing
/// here can make a surface brighter than the tileset drew it. <c>WaterEdgeVectors</c> pins that,
/// because its violation would arrive as "jetzt ist es noch heller" and nothing in this repository
/// could otherwise have noticed.</para>
///
/// <para>THIS ONLY RUNS WHILE THE FILM DRAWS ON THE GAME'S SHADER, i.e. while
/// <c>WaterSettings.OwnSurface</c> is off or the mod's bundle has not yielded its water shader. The
/// shipped film carries a mod-owned material with no band of any kind on it.</para>
/// </summary>
internal static class WaterEdgeBand
{
    /// <summary>
    /// Every SCALAR that sets how far the shoreline band reaches, verbatim from the hardware
    /// census of <c>VFX/Water_Shd_Trans</c> (LogOutput.log:941): <c>_Edge_Distance</c> = 0.2,
    /// <c>_Edge_Colour_Distance</c> = 0.9, <c>_WaterBorderWidth</c> = 0.1.
    ///
    /// <para>The third name is the one that matters most, because it belongs to a SECOND border
    /// mechanism that has no toggle keyword at all — nothing this module has ever shipped could
    /// have switched it off, and ModBuild 160's keyword clear had no effect on it by
    /// construction.</para>
    ///
    /// <para>Extend this array if a tileset ships terrain water on another shader with another
    /// spelling; the driver walks it by name and states loudly in the log which of these the live
    /// shader does not declare, so a missing name shows up as a log line rather than as silence.</para>
    /// </summary>
    internal static readonly string[] BandWidthProperties =
        { "_Edge_Distance", "_Edge_Colour_Distance", "_WaterBorderWidth" };

    /// <summary>The two band COLOURS of the same shader. <c>_Edge_Colour</c> is near-white
    /// (0.887, 0.887, 0.887, 0.867) and <c>_WaterBorderCol</c> is a pale beige
    /// (0.670, 0.617, 0.528, 0.561) — between them they are the only pale things this material
    /// declares, and the photograph shows a pale sheet.</summary>
    internal static readonly string[] BandColourProperties =
        { "_Edge_Colour", "_WaterBorderCol" };

    /// <summary>The shoreline toggle, and the keyword an Amplify <c>[Toggle]</c> with no explicit
    /// keyword name generates from it. The census prints the attribute as a bare <c>[Toggle]</c>
    /// and the live material's keyword list as exactly <c>[_EDGECOLOUR_TOGGLE_ON]</c>, which is
    /// what confirms the derivation (uppercase property name + <c>_ON</c>).</summary>
    internal const string EdgeToggleProperty = "_EdgeColour_Toggle";

    /// <inheritdoc cref="EdgeToggleProperty"/>
    internal const string EdgeToggleKeyword = "_EDGECOLOUR_TOGGLE_ON";

    /// <summary>
    /// Collapse one band WIDTH to nothing.
    /// </summary>
    /// <param name="authored">The value on the shared material — always the source, never the
    /// value already written, so a re-assert can never compound and a restore is exact.</param>
    /// <param name="value">The value to write. Only meaningful when this returns true.</param>
    /// <param name="reason">Always set: either what was done, or why nothing was. Goes verbatim
    /// into the hardware log.</param>
    /// <returns>True when <paramref name="value"/> should be written.</returns>
    internal static bool TryCollapseWidth(float authored, out float value, out string reason)
    {
        value = authored;
        if (float.IsNaN(authored) || float.IsInfinity(authored))
        {
            reason = "authored value is not finite — refused rather than written";
            return false;
        }
        if (authored <= 0f)
        {
            // Writing 0 here would RAISE a negative width. There is nothing to collapse anyway.
            reason = $"already {authored:0.###}, at or below zero — left exactly as authored";
            return false;
        }
        value = 0f;
        reason = $"{authored:0.###} -> 0 (band collapsed)";
        return true;
    }

    /// <summary>
    /// Collapse one band COLOUR: keep the BODY's hue so that whichever extreme the unfed depth
    /// fade lands on there is nothing pale to draw with, and take the alpha to zero so the band
    /// contributes no coverage at all. Two independent neutralisations in one write, because
    /// either one alone could be the inert half — and the hue half is the one that survives a
    /// shader which ignores the band alpha.
    /// </summary>
    /// <param name="authored">The band colour on the shared material.</param>
    /// <param name="body">The film's own <c>_Color_Tint</c>, already capped. The band is painted
    /// in this so a band that still draws draws the water, not a shoreline.</param>
    internal static bool TryCollapseColour(
        Color authored, Color body, out Color value, out string reason)
    {
        value = authored;
        if (float.IsNaN(authored.a) || float.IsInfinity(authored.a))
        {
            reason = "authored alpha is not finite — refused rather than written";
            return false;
        }
        if (authored.a <= 0f)
        {
            reason = $"already alpha {authored.a:0.###}, at or below zero — left exactly as authored";
            return false;
        }
        value = new Color(body.r, body.g, body.b, 0f);
        reason = $"RGBA({authored.r:0.###},{authored.g:0.###},{authored.b:0.###},{authored.a:0.###})"
                 + $" -> body hue RGBA({body.r:0.###},{body.g:0.###},{body.b:0.###}) at alpha 0";
        return true;
    }
}
