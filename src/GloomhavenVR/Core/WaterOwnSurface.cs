using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MOD'S OWN WATER FILM — which bundled shader carries it, what look is derived from the
/// tileset's own authored values, and the render state that look needs. Kept in a file free of
/// everything but <see cref="Color"/> and <see cref="Mathf"/> so it can be driven from
/// <c>tests/GloomhavenVR.WireTests</c> without a headset and without a game.
///
/// <para>WHY ROUND FIVE STOPS TUNING THE GAME'S SHADER. ModBuild 161's <c>BAND READ-BACK</c> block
/// reads values back OFF THE LIVE MATERIAL INSTANCE after the writes — i.e. exactly what the shader
/// will sample — and it proves every write landed: <c>_Edge_Distance 0.2 -> 0</c>,
/// <c>_Edge_Colour_Distance 0.9 -> 0</c>, <c>_WaterBorderWidth 0.1 -> 0</c>, <c>_Edge_Colour</c> and
/// <c>_WaterBorderCol</c> both repainted in the body hue at alpha 0, <c>_EdgeColour_Toggle 1 -> 0</c>,
/// the instance's keyword list empty, <c>_Smoothness 0.754 -> 0.08</c>, every metal/reflection
/// scalar at 0, <c>_Color_Tint.a 0.737 -> 0.45</c>. The user's verdict was <i>"Keine Änderungen bei
/// der Wasser Problematik."</i> Every band width is zero, every band colour is at alpha zero, and
/// <c>.planning/debug/spiegeltiles.jpg</c> still shows a flat pale near-WHITE sheet — mottled, with
/// visible structure in it — lying LIGHTER than the stone around it, where the authored body is
/// <c>_Color_Tint</c> = RGBA(0.195, 0.311, 0.131) dark green.</para>
///
/// <para>AND THE OTHER HALF IS NOW MEASURED RATHER THAN ASSUMED. That reading left exactly two
/// possibilities — either the pale pixels come from something compiled INSIDE
/// <c>VFX/Water_Shd_Trans</c>, or we did not own the renderer he was pointing at — and
/// <c>[Water] DebugPaint</c> was built to settle the second. He has now run it, on hardware:
/// <i>"Die debug farbe funktioniert - alles färbt sich magenta wie gewollt."</i> The pale hexes
/// turn MAGENTA, which is the FILM's colour: they are this driver's tracked water film, they have
/// been all along, and "we are tuning the wrong objects" is dead. He adds <i>"Ich konnte aber mit
/// den anderen Einstellungen die kopf-gebundene Reflektion nicht deaktivieren, egal was ich
/// eingestellt hab."</i></para>
///
/// <para>OWNED RENDERER + EVERY REACHABLE PROPERTY NEUTRAL + THE DEFECT UNCHANGED. There is one
/// explanation left and it is no longer a hypothesis: the pale sheet AND the head-bound reflection
/// are produced by a TEXTURE or by a CONSTANT compiled into <c>VFX/Water_Shd_Trans</c>, and nothing
/// addressed by property name can reach either. So the film's whole material is replaced. That
/// deletes the game's shader from the surface, and everything inside it with it.</para>
///
/// <para>WHICH MAKES ONE REQUIREMENT HARD RATHER THAN PREFERRED: <b>the replacement shader must
/// sample no environment reflection at all.</b> A head-bound reflection is precisely the thing he
/// cannot switch off, so a replacement that could produce one would answer nothing. The chosen
/// shader is checked against that in its own source, below, and not against its description.</para>
///
/// <para>WHY <c>GloomhavenVR/Overlay</c> AND NOT <c>GloomhavenVR/EnvPuddle</c>. EnvPuddle is the
/// prettier water and it was the first candidate; it was read in full and rejected on evidence,
/// twice over. FIRST AND DISQUALIFYING: <b>it computes a view-dependent reflection.</b> Its second
/// pass builds the mirror images of the moon and the candle out of
/// <c>float3 R = reflect(-V, N)</c> with <c>N</c> perturbed by the ripple — that IS a head-bound
/// reflection, by construction, and it is the exact symptom this build exists to remove. No tuning
/// of its colours reaches that; the reflection vector is in the shader. SECOND, and enough on its
/// own: its whole shape is authored against ONE mesh. <c>PuddleMesh</c> writes <c>uv.y</c> as a
/// NORMALISED RADIUS (0 at the centre, 1 at an fbm-irregular rim) and every feature is cut out of
/// that coordinate — <c>PuddleShore</c>'s wet/deep ramps and, decisively, the meniscus
/// <c>GHVR_PUD_LIP_AT 0.930</c>, a hairline that is a <c>#define</c> and cannot be reached by any
/// property. On the game's <c>TERRAIN_Water_Plane</c>, whose UVs run 0..1 across a hex quad rather
/// than radially, that hairline is a bright STRIPE across every water hex and the wet/deep ramp is a
/// gradient band across it. Its ripples are centred on <c>_Center</c> in OBJECT space at
/// <c>_Radius</c> 0.6 m, and its reflections take <c>_MoonDir</c> / <c>_CandPos</c> in object space
/// from <c>EnvRoomBuilder</c>, which never runs in a game scenario. So EnvPuddle on that quad does
/// not degrade to "a plainer puddle"; it degrades to a striped, ring-marked surface that reads as a
/// bug — and the standing instruction is that a flat translucent film is acceptable where a surface
/// that looks like a bug is not. <c>GloomhavenVR/EnvGround</c> was rejected for a simpler reason:
/// it is OPAQUE, and a film the floor cannot read through is the opposite of what
/// <c>[Water] Opacity</c> exists for. <c>GloomhavenVR/HeadUnlit</c> is opaque too (no
/// <c>Blend</c> statement at all, <c>Queue=Geometry</c>), so its alpha is simply discarded.</para>
///
/// <para>WHAT <c>GloomhavenVR/Overlay</c> ACTUALLY IS, read off
/// <c>unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader</c> rather than assumed: one
/// pass, <c>Tags { "RenderType"="Transparent" "Queue"="Transparent" }</c>, fragment
/// <c>tex2D(_MainTex, uv) * _Color * i.color</c> — no lighting, no environment sample, no depth
/// read, no screen-space term of any kind — with <c>Cull</c>, <c>ZTest</c>, <c>ZWrite</c>,
/// <c>Blend</c> src and <c>Blend</c> dst all driven from properties. A sweep of that file for
/// <c>unity_SpecCube</c>, <c>reflect(</c>, <c>samplerCUBE</c>, <c>UNITY_SAMPLE_TEXCUBE</c>,
/// <c>worldRefl</c> and <c>Cubemap</c> returns ZERO matches, so the hard requirement above is met
/// by measurement: there is no view-dependent term in this shader and it cannot produce a
/// head-bound reflection. Every mechanism that could paint a pale sheet is absent for the same
/// reason — no shoreline term, no foam term, no depth read, and no texture unless we bind one, and
/// we deliberately bind none, because a texture is the surviving suspect.</para>
///
/// <para>WHAT IT COSTS, STATED PLAINLY RATHER THAN GLOSSED. This film does not animate: it has no
/// ripple, because Overlay declares no normal input for the authored <c>_Normal_Map</c> to go into.
/// It is a flat translucent sheet of the tileset's own green. That is a real loss against the
/// game's authored water and it is accepted knowingly — a still green pool is water, and the sheet
/// in the photograph is not.</para>
///
/// <para>THE INVARIANT, and the same one <see cref="WaterEdgeBand"/> holds for the same reason:
/// <b>the replacement look may never be brighter or more opaque than what the tileset
/// authored.</b> The hue is the authored <c>_Color_Tint</c> RGB verbatim — this module cannot
/// invent a colour the tileset never had — and the alpha is <c>min(authored, [Water] Opacity)</c>.
/// A violation arrives as "jetzt ist es noch heller" and nothing else in this repository could
/// notice; <c>WaterOwnSurfaceVectors</c> pins it.</para>
/// </summary>
internal static class WaterOwnSurface
{
    /// <summary>The bundled shader the water film is re-based onto. A key of
    /// <c>BundleShaders.Paths</c> already (nothing had to be added to that table), so the lookup
    /// goes through all three of <c>BundleShaders.Resolve</c>'s mechanisms — the SHADER.FIND trap
    /// has cost this project two builds and <c>BundledShaderVectors</c> fails the build gate for a
    /// bare <c>Shader.Find</c> on any <c>GloomhavenVR/*</c> name anywhere in <c>src/</c>.</summary>
    internal const string FilmShaderName = "GloomhavenVR/Overlay";

    // --- the properties Overlay DECLARES. Every one of these was read out of the .shader file;
    //     none is guessed, and the driver writes nothing that is not on this list.
    internal const string TintProperty = "_Color";
    internal const string MainTexProperty = "_MainTex";
    internal const string CullProperty = "_Cull";
    internal const string ZTestProperty = "_ZTest";
    internal const string ZWriteProperty = "_ZWrite";
    internal const string SrcBlendProperty = "_SrcBlend";
    internal const string DstBlendProperty = "_DstBlend";

    /// <summary>Two-sided. The WINDING lesson: four meshes have shipped in this project wound
    /// against the side they are seen from, and one of them was invisible for ten builds. A water
    /// quad seen from a VR table is seen from above and from the side, and a film that vanished
    /// from one seat would read as exactly the bug this round is trying to end.
    /// <c>UnityEngine.Rendering.CullMode.Off</c>.</summary>
    internal const float CullOff = 0f;

    /// <summary>The film must NOT write depth. It is a transparent sheet lying on a basin bed that
    /// has already been drawn; writing depth would let it occlude whatever sorts after it and
    /// would make the water a hole in the transparent queue.
    /// <c>UnityEngine.Rendering.CompareFunction.LessEqual</c> for the test, 0 for the write.</summary>
    internal const float ZWriteOff = 0f;

    /// <inheritdoc cref="ZWriteOff"/>
    internal const float ZTestLessEqual = 4f;

    /// <summary>STRAIGHT ALPHA BLENDING, and this pair is the sharpest thing in this file. Overlay
    /// exposes its blend factors as properties, so a plausible-looking edit could set them to
    /// One/One — and <c>Blend One One</c> is ADDITIVE: it would put the film's colour ON TOP of the
    /// stone and make the hexes LIGHTER than their surroundings, which is a pixel-for-pixel
    /// re-creation of the defect in <c>spiegeltiles.jpg</c>, produced this time by the mod's own
    /// shader. <c>UnityEngine.Rendering.BlendMode.SrcAlpha</c> /
    /// <c>OneMinusSrcAlpha</c>.</summary>
    internal const float SrcBlendSrcAlpha = 5f;

    /// <inheritdoc cref="SrcBlendSrcAlpha"/>
    internal const float DstBlendOneMinusSrcAlpha = 10f;

    /// <summary>
    /// The replacement film's colour, built from the tileset's OWN authored body tint.
    /// </summary>
    /// <param name="authored">The film material's authored <c>_Color_Tint</c> — read off the SHARED
    /// material, never off our instance, so a re-apply is exactly idempotent. Measured on hardware
    /// as RGBA(0.195, 0.311, 0.131, 0.737).</param>
    /// <param name="opacityCap"><c>[Water] Opacity</c>, the ceiling on how much floor the film may
    /// hide. Default 0.45.</param>
    /// <param name="value">The colour to write into <see cref="TintProperty"/>. Only meaningful
    /// when this returns true.</param>
    /// <param name="reason">Always set: what was derived, or why nothing could be. Goes verbatim
    /// into the hardware log — the OWN SURFACE block prints it, so a reader of the log can check
    /// the derivation against the authored numbers on the same line.</param>
    /// <returns>False only when the authored tint cannot be used at all, in which case the caller
    /// must leave the renderer on the game's own material rather than invent a colour.</returns>
    internal static bool TryBuildFilmColour(
        Color authored, float opacityCap, out Color value, out string reason)
    {
        value = authored;
        if (!Finite(authored.r) || !Finite(authored.g) || !Finite(authored.b)
            || !Finite(authored.a))
        {
            reason = "the authored _Color_Tint is not finite — refused, and the renderer keeps the "
                     + "game's own material rather than being repainted in a colour this module "
                     + "invented";
            return false;
        }
        if (!Finite(opacityCap))
        {
            reason = "[Water] Opacity is not finite — refused for the same reason";
            return false;
        }

        // NEVER RAISE. The alpha is the smaller of what the tileset authored and what the dial
        // allows; the hue is the tileset's verbatim, channel for channel, because a hue this module
        // chose for itself would be a look the tileset never had.
        float alpha = Mathf.Clamp01(Mathf.Min(authored.a, opacityCap));
        value = new Color(authored.r, authored.g, authored.b, alpha);
        reason = $"authored _Color_Tint RGBA({authored.r:0.###},{authored.g:0.###},"
                 + $"{authored.b:0.###},{authored.a:0.###}) -> film RGBA({value.r:0.###},"
                 + $"{value.g:0.###},{value.b:0.###},{value.a:0.###}) (hue verbatim, alpha "
                 + $"min(authored, [Water] Opacity {opacityCap:0.###}))";
        return true;
    }

    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
