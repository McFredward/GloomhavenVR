using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Which way should <c>_InvertDepthFade</c> point? A DIAL, never a silent choice.
///
/// <para>The water film authors <c>_InvertDepthFade = 0</c> and the VR head camera writes no
/// <c>_CameraDepthTexture</c> at all (hardware census: <c>depthTextureMode=None</c>). So the
/// shader's depth-fade input is a CONSTANT across the whole quad, and this property does not
/// grade anything — it only selects WHICH of the two extremes the entire surface sits at. One of
/// those extremes paints the quad as shoreline; the other paints it as open water. Nobody in this
/// project has read this shader's source (it ships compiled inside <c>always_loaded_base*</c> and
/// there is no game install on the build machine), so WHICH extreme is which cannot be derived —
/// it can only be observed.
///
/// <para>That is exactly why collapsing the band widths to zero is the primary attack and this is
/// the secondary one: a band of width zero draws nothing at EITHER extreme, so it does not depend
/// on knowing the sign. This dial exists for the case where the whiteness turns out to come from a
/// depth-fade term that is not one of the named band properties, and it is a dial rather than a
/// default because <see cref="Inverted"/> is the one write in this whole module that RAISES a value
/// above what the tileset authored.</para>
/// </summary>
internal enum WaterDepthFadeMode
{
    /// <summary>Write back exactly what the tileset authored. The default, and the only setting
    /// under which this module makes no claim about the sign at all.</summary>
    Authored = 0,

    /// <summary>Force <c>_InvertDepthFade = 0</c>.</summary>
    NotInverted = 1,

    /// <summary>Force <c>_InvertDepthFade = 1</c>. This is a RAISE above the authored 0 and is
    /// therefore only ever reachable by the user moving the dial.</summary>
    Inverted = 2,
}

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
/// zero; a band colour already fully transparent is left alone. The single exception is
/// <see cref="WaterDepthFadeMode.Inverted"/>, which raises <c>_InvertDepthFade</c> from 0 to 1 and
/// is therefore unreachable except by the user moving a dial in the headset. <c>WaterEdgeVectors</c>
/// pins all of that, because its violation would arrive as "jetzt ist es noch heller" and nothing
/// in this repository could otherwise have noticed.</para>
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

    /// <summary>The depth-fade sign. See <see cref="WaterDepthFadeMode"/> for why it is a dial.</summary>
    internal const string InvertDepthFadeProperty = "_InvertDepthFade";

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

    /// <summary>
    /// Which value should <c>_InvertDepthFade</c> carry? See <see cref="WaterDepthFadeMode"/>.
    ///
    /// <para><see cref="WaterDepthFadeMode.Authored"/> WRITES the authored value rather than
    /// skipping the property, so that winding the dial back restores the surface exactly instead
    /// of leaving whatever the last setting wrote standing on our instance.</para>
    /// </summary>
    internal static bool TryResolveDepthFade(
        WaterDepthFadeMode mode, float authored, out float value, out string reason)
    {
        value = authored;
        if (float.IsNaN(authored) || float.IsInfinity(authored))
        {
            reason = "authored value is not finite — refused rather than written";
            return false;
        }
        switch (mode)
        {
            case WaterDepthFadeMode.NotInverted:
                value = 0f;
                reason = authored <= 0f
                    ? $"forced to 0 ([Water] DepthFade=NotInverted; authored {authored:0.###}, so "
                      + "this changes nothing)"
                    : $"{authored:0.###} -> 0 ([Water] DepthFade=NotInverted)";
                return true;
            case WaterDepthFadeMode.Inverted:
                value = 1f;
                reason = $"authored {authored:0.###} -> 1 ([Water] DepthFade=Inverted). THIS IS THE "
                         + "ONE WRITE IN THIS MODULE THAT RAISES A VALUE ABOVE WHAT THE TILESET "
                         + "AUTHORED, which is why it is reachable only by the user moving the "
                         + "dial and is never a default: with no _CameraDepthTexture the fade input "
                         + "is constant across the whole quad, so this flips which extreme the "
                         + "WHOLE surface sits at rather than grading anything";
                return true;
            default:
                reason = $"left at the authored {authored:0.###} ([Water] DepthFade=Authored — this "
                         + "module makes no claim about which extreme is which, because the shader "
                         + "ships compiled and nobody here has read it)";
                return true;
        }
    }
}
