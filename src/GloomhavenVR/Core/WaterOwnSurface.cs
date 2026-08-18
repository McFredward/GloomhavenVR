using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE MOD'S OWN WATER FILM — which bundled shader carries it, what look is derived from the
/// tileset's own authored values, and the render state that look needs. Kept in a file free of
/// everything but <see cref="Color"/>, <see cref="Vector4"/> and <see cref="Mathf"/> so it can be
/// driven from <c>tests/GloomhavenVR.WireTests</c> without a headset and without a game.
///
/// <para>WHY THE FILM'S WHOLE MATERIAL IS REPLACED. ModBuild 161's <c>BAND READ-BACK</c> block
/// reads values back OFF THE LIVE MATERIAL INSTANCE after the writes — i.e. exactly what the shader
/// samples — and it proves every write landed: <c>_Edge_Distance 0.2 -> 0</c>,
/// <c>_Edge_Colour_Distance 0.9 -> 0</c>, <c>_WaterBorderWidth 0.1 -> 0</c>, both band colours
/// repainted in the body hue at alpha 0, <c>_EdgeColour_Toggle 1 -> 0</c>, the instance's keyword
/// list empty, <c>_Smoothness 0.754 -> 0.08</c>, every metal/reflection scalar at 0. The user's
/// verdict was <i>"Keine Änderungen bei der Wasser Problematik."</i> and
/// <c>[Water] DebugPaint</c> had already settled that these ARE our renderers
/// (<i>"Die debug farbe funktioniert - alles färbt sich magenta wie gewollt."</i>). Owned renderer
/// + every reachable property neutral + the defect unchanged leaves one explanation: the pale sheet
/// and the head-bound reflection are a TEXTURE or a CONSTANT compiled into
/// <c>VFX/Water_Shd_Trans</c>, and nothing addressed by property name reaches either. So the game's
/// shader is deleted from the surface, and everything inside it with it.</para>
///
/// <para>ModBuild 162 DID THAT, AND IT WORKED — AND THE CURE COST TOO MUCH LOOK. User, verbatim:
/// <i>"Beide Probleme gelöst, top! Allerdings: Das Wasser sieht jetzt sehr viel schlechter aus. Das
/// echte Wasser hatte ANimation und co. das will ich auch wieder. Ich will es so nah wie möglich an
/// dem 'echten' Wasser haben - aber eben so dass es in VR funktioniert."</i> ModBuild 162 put the
/// film on <c>GloomhavenVR/Overlay</c>, which is a flat unlit sheet, because Overlay was the only
/// bundled shader that provably sampled no environment. That was the right emergency move and it is
/// no longer the answer: <see cref="FilmShaderName"/> is now <c>GloomhavenVR/WaterVR</c>, a shader
/// authored into this mod's own bundle for this one job, and Overlay is what is left when the
/// bundle cannot yield it (<see cref="FallbackFilmShaderName"/>).</para>
///
/// <para>WHAT "THE REAL WATER" ACTUALLY IS, measured off the game's own material by the hardware
/// log's <c>WATER SURFACE</c> census of <c>TERRAIN_GEN_WaterPlane_Crypt_Mat</c>:
/// <c>_Normal_Map</c> = 'WaterBump' 512x512, <c>_NormalTilings</c> = (0.14, 6.00, -0.12, -0.20),
/// <c>_WaterUVAnimSpeedA</c> = (1.00, 1.00, 0.60, 0.00), <c>_WaterUVAnimSpeedB</c> =
/// (0.50, 1.00, 1.00, 0.00), <c>_WaterNoiseSpeed</c> = (1.00, 1.00, 1.00, 0.00),
/// <c>_Color_Tint</c> = RGBA(0.195, 0.311, 0.131, 0.737),
/// <c>_DetailOpacityBaseNormalStr</c> = (5.00, 5.00, 0.00, 0.00), <c>_Smoothness</c> = 0.754,
/// render queue 2900. <b>Two scrolling normal layers at different tilings and speeds are the whole
/// of its motion.</b> Every one of those values is READ OFF THE GAME MATERIAL at runtime and fed to
/// <c>GloomhavenVR/WaterVR</c>; the constants in this file are the measured fallbacks for a tileset
/// whose material does not declare one of them, never a look chosen here.</para>
///
/// <para>AND THE VERTEX WAVES ARE DELIBERATELY NOT REPRODUCED. The same census reads
/// <c>_addSphericalWaves = 0</c>, so the tileset's own displacement gate is OFF and
/// <c>_VertexOffsetWaves 0.05</c> never reaches geometry — displacing here would be a look the
/// tileset never had. Independently: Unity culls a renderer against its MESH's authored bounds, so
/// displaced geometry vanishes as you approach and, under MultiPass, vanishes in ONE EYE FIRST.
/// This project has already lost a build to exactly that. <c>WaterVR.shader</c> has no vertex
/// program that moves a vertex and no property that could switch one on.</para>
///
/// <para><b>THE HARD REQUIREMENT: NOTHING IN THE REPLACEMENT SHADER MAY DEPEND ON THE VIEW
/// DIRECTION.</b> The user's words for the defect were <i>"die kopf-gebundene Reflektion"</i> and
/// <i>"bewegen sich schnell mit den Kopfbewegungen mit"</i>, and he reports no property dial could
/// switch it off — so a replacement that can compute a view-dependent term answers nothing. That
/// rules out <c>unity_SpecCube0</c>, <c>reflect()</c>, cube samples, <c>worldRefl</c>, Fresnel and
/// even a Blinn-Phong specular, whose half-vector highlight slides across the surface with the head
/// exactly as the report describes. It is also the strongest available STEREO guarantee: under
/// MultiPass each eye renders its own pass, so any view-dependent term is a different image per
/// eye, and this project has already parked one feature permanently over that
/// (<c>.planning/wall-fade-stereo-rivalry.md</c>). A view-independent shader is per-eye identical
/// by construction. <c>WaterOwnSurfaceVectors</c> sweeps BOTH shaders' source for every spelling of
/// an environment sample and fails the build gate on a hit — the shader is defended by a test, not
/// by this paragraph.</para>
///
/// <para>SO WHERE DOES THE SPARKLE COME FROM? THE SCROLLING NORMALS. A fixed light direction dotted
/// against the animated normal gives highlights that are born on the crests, travel with them and
/// break up as the two layers slide past each other — moving light that is identical in both eyes
/// because the eye is not in the expression. The direction comes from the scene's own main
/// directional light when there is one and from <see cref="DefaultLightLocal"/> when there is not;
/// <see cref="TryBuildLightDirection"/> decides and says which, and the census prints it.</para>
///
/// <para>WHY <c>GloomhavenVR/EnvPuddle</c> IS STILL NOT THE ANSWER, since it is the prettier water
/// and keeps being the obvious candidate. Two independent disqualifications. FIRST: it computes
/// <c>float3 R = reflect(-V, N)</c> and builds the mirror images of the moon and the candle out of
/// it — a head-bound reflection by construction, i.e. the exact symptom this work exists to remove,
/// and unreachable by any property because the reflection vector is in the shader. SECOND: its
/// whole shape is authored against ONE mesh. <c>PuddleMesh</c> writes <c>uv.y</c> as a NORMALISED
/// RADIUS and every feature is cut out of that coordinate — the wet/deep ramps and, decisively, the
/// meniscus <c>GHVR_PUD_LIP_AT 0.930</c>, a hairline that is a <c>#define</c> no property can
/// reach. On the game's <c>TERRAIN_Water_Plane</c>, whose UVs run 0..1 across a hex quad rather
/// than radially, that hairline is a bright STRIPE across every water hex. <c>WaterVR</c> exists
/// precisely because neither of those could be tuned away.</para>
///
/// <para>THE INVARIANT, and the same one <see cref="WaterEdgeBand"/> holds for the same reason:
/// <b>the replacement look may never be brighter or more opaque than what the tileset
/// authored.</b> The hue is the authored <c>_Color_Tint</c> RGB verbatim — this module cannot
/// invent a colour the tileset never had — and the alpha is <c>min(authored, [Water] Opacity)</c>.
/// <c>WaterVR</c>'s own body term is a lerp SYMMETRIC about 1.0, so the wave shading has a mean of
/// exactly the authored tint rather than a pedestal above it; only the glint adds, and only over
/// the few percent of pixels that carry one. A violation arrives as "jetzt ist es noch heller" and
/// nothing else in this repository could notice; <c>WaterOwnSurfaceVectors</c> pins it.</para>
/// </summary>
internal static class WaterOwnSurface
{
    /// <summary>The bundled shader the water film is re-based onto: the mod's own animated,
    /// view-independent water. A key of <c>BundleShaders.Paths</c> — the lookup goes through all
    /// three of <c>BundleShaders.Resolve</c>'s mechanisms, and it MUST, because nothing in the
    /// mod's own bundle prefabs references this shader (it is put on the GAME's quads), so
    /// <c>Shader.Find</c> alone can never resolve it. The SHADER.FIND trap has cost this project
    /// two builds and <c>BundledShaderVectors</c> fails the build gate for a bare
    /// <c>Shader.Find</c> on any <c>GloomhavenVR/*</c> name anywhere in <c>src/</c>.</summary>
    internal const string FilmShaderName = "GloomhavenVR/WaterVR";

    /// <summary>What the film falls back to when the bundle does not yield
    /// <see cref="FilmShaderName"/>: ModBuild 162's flat translucent sheet on
    /// <c>GloomhavenVR/Overlay</c>. It is a WORSE look and the census says so in as many words
    /// rather than letting a still pool read as a shader that shipped and did nothing — but it is
    /// the same colour at the same queue with the same absence of any view-dependent term, so the
    /// defect stays fixed while the animation is missing.</summary>
    internal const string FallbackFilmShaderName = "GloomhavenVR/Overlay";

    // --- properties DECLARED BY BOTH shaders. Every one was read out of the two .shader files;
    //     none is guessed. Sharing the names is what lets one driver path write either shader —
    //     a fallback whose property names differed would be a fallback that silently wrote
    //     nothing, and its only symptom would be a pool at the shader's authored defaults.
    internal const string TintProperty = "_Color";
    internal const string MainTexProperty = "_MainTex";
    internal const string CullProperty = "_Cull";
    internal const string ZTestProperty = "_ZTest";
    internal const string ZWriteProperty = "_ZWrite";
    internal const string SrcBlendProperty = "_SrcBlend";
    internal const string DstBlendProperty = "_DstBlend";

    /// <summary>The names above, for the wire test that checks BOTH shaders declare every one of
    /// them.</summary>
    internal static readonly string[] CommonProperties =
    {
        TintProperty, MainTexProperty, CullProperty, ZTestProperty, ZWriteProperty,
        SrcBlendProperty, DstBlendProperty,
    };

    // --- properties only GloomhavenVR/WaterVR declares: the animation itself.
    internal const string NormalMapProperty = "_Normal_Map";
    internal const string NormalTilingsProperty = "_NormalTilings";
    internal const string ScrollAProperty = "_WaterUVAnimSpeedA";
    internal const string ScrollBProperty = "_WaterUVAnimSpeedB";
    internal const string NormalStrengthProperty = "_NormalStrength";
    internal const string ProcNormalProperty = "_ProcNormal";
    internal const string LightDirProperty = "_LightDir";
    internal const string ShimmerProperty = "_Shimmer";
    internal const string WaveShadeProperty = "_WaveShade";
    internal const string SmoothnessProperty = "_Smoothness";

    /// <inheritdoc cref="CommonProperties"/>
    internal static readonly string[] WaterProperties =
    {
        NormalMapProperty, NormalTilingsProperty, ScrollAProperty, ScrollBProperty,
        NormalStrengthProperty, ProcNormalProperty, LightDirProperty, ShimmerProperty,
        WaveShadeProperty, SmoothnessProperty,
    };

    // --- the names the GAME's material carries these values under. Three of them happen to be
    //     spelled the same on both sides (_Normal_Map, _NormalTilings, _Smoothness); the two
    //     scroll vectors are read from the game under the same names and written RESOLVED (see
    //     ScrollRate). Every name here was printed by the hardware census, so none is guessed.
    internal const string GameNoiseSpeedProperty = "_WaterNoiseSpeed";
    internal const string GameNormalStrengthProperty = "_DetailOpacityBaseNormalStr";
    internal const string GameTintProperty = "_Color_Tint";

    // --- the measured authored values, used ONLY as fallbacks for a material that does not
    //     declare one of them. Source: the WATER SURFACE census of
    //     TERRAIN_GEN_WaterPlane_Crypt_Mat on VFX/Water_Shd_Trans.
    internal static readonly Vector4 AuthoredTilings = new(0.14f, 6.00f, -0.12f, -0.20f);
    internal static readonly Vector4 AuthoredSpeedA = new(1.00f, 1.00f, 0.60f, 0f);
    internal static readonly Vector4 AuthoredSpeedB = new(0.50f, 1.00f, 1.00f, 0f);
    internal static readonly Vector4 AuthoredNoiseSpeed = new(1.00f, 1.00f, 1.00f, 0f);
    internal static readonly Vector4 AuthoredNormalStrength = new(5.00f, 5.00f, 0f, 0f);
    internal const float AuthoredSmoothness = 0.754f;

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

    /// <summary>STRAIGHT ALPHA BLENDING, and this pair is the sharpest thing in this file. Both
    /// shaders expose their blend factors as properties, so a plausible-looking edit could set them
    /// to One/One — and <c>Blend One One</c> is ADDITIVE: it would put the film's colour ON TOP of
    /// the stone and make the hexes LIGHTER than their surroundings, which is a pixel-for-pixel
    /// re-creation of the defect in <c>spiegeltiles.jpg</c>, produced this time by the mod's own
    /// shader. <c>UnityEngine.Rendering.BlendMode.SrcAlpha</c> /
    /// <c>OneMinusSrcAlpha</c>.</summary>
    internal const float SrcBlendSrcAlpha = 5f;

    /// <inheritdoc cref="SrcBlendSrcAlpha"/>
    internal const float DstBlendOneMinusSrcAlpha = 10f;

    /// <summary>
    /// Where the light comes from when the scene has no usable directional light, expressed in the
    /// SURFACE-LOCAL frame <c>WaterVR</c> shades in: x along U, y along V, z along the surface
    /// normal. Steeply overhead and leaning a little, which is what a crypt's own lighting does and
    /// what puts the glints on the far side of the crests rather than in a band across the middle.
    /// It is a CONSTANT and not a dial on purpose: a light direction the player can spin is a way
    /// to make the water look wrong, and nothing about it is a preference.
    /// </summary>
    /// <remarks>Normalised at construction rather than by hand: the shader normalises what it is
    /// given, so a non-unit constant would be harmless on screen and would still make every
    /// invariant written about this vector unverifiable.</remarks>
    internal static readonly Vector3 DefaultLightLocal =
        new Vector3(0.34f, 0.22f, 0.91f).normalized;

    /// <summary>A light lying in or under the water's own plane cannot glint on it: its
    /// <c>dot(N, L)</c> is at or below zero over the whole flat part of the surface, which would
    /// leave the shimmer alive only on the steepest slopes and read as a rim of sparks. Below this
    /// much local z the scene's light is refused and <see cref="DefaultLightLocal"/> is used
    /// instead, with the reason string saying so.</summary>
    internal const float MinLightElevation = 0.08f;

    /// <summary>The one measured sample of <c>_DetailOpacityBaseNormalStr</c>, used as the
    /// reference point that <see cref="NormalStrength"/> normalises against. See that method for
    /// why a number authored for a shader nobody can open cannot simply be passed through.</summary>
    internal const float ReferenceNormalStrength = 5f;

    /// <summary>What <see cref="ReferenceNormalStrength"/> maps to in <c>WaterVR</c>'s own units —
    /// the ripple strength the tileset's authored value means HERE.</summary>
    internal const float NominalRippleStrength = 1.2f;

    /// <summary>Ceiling on the ripple strength, and it is a comfort limit as much as a look one.
    /// Past about this the two blended normals tilt far enough that <c>dot(N, L)</c> swings from
    /// end to end within a pixel, the glints stop being highlights and become per-pixel noise, and
    /// per-pixel noise on a 90 Hz headset ALIASES into a crawling carpet — which is a discomfort
    /// report, not merely an ugly one.</summary>
    internal const float MaxRippleStrength = 4f;

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

    /// <summary>
    /// One normal layer's SCROLL RATE, in UV per second, from the game material's own authored
    /// speed vector.
    ///
    /// <para>THIS IS THE ONE GUESS IN THE WHOLE FEATURE AND IT IS MADE HERE ON PURPOSE — in a pure
    /// function, with a wire vector on it and a log line that prints what it resolved to and from
    /// what — rather than inside HLSL where nothing could check it. The census reads
    /// <c>_WaterUVAnimSpeedA = (1.00, 1.00, 0.60, 0.00)</c> and
    /// <c>_WaterUVAnimSpeedB = (0.50, 1.00, 1.00, 0.00)</c>. <c>.xy</c> is unambiguously the per-axis
    /// rate; <c>.z</c> is a per-layer multiplier in every Amplify water graph that spells a speed
    /// this way, but the game's shaders ship COMPILED inside <c>always_loaded_base*</c> and there is
    /// no install on the build machine to open them with, so it cannot be read, only inferred.
    /// <c>.w</c> is 0 on both layers and is not used.</para>
    ///
    /// <para>The inference is bounded rather than blind: taking <c>.z</c> as a multiplier changes
    /// layer A's rate by 40% and layer B's not at all, so it moves how FAST the water runs and
    /// never what it looks like. A <c>.z</c> of zero is refused (treated as 1) because a tileset
    /// that meant "no motion" would have authored <c>.xy</c> at zero, and a layer frozen by a
    /// component nobody can read would look exactly like this shader failing.</para>
    ///
    /// <para>And the absolute rate is the one thing that cannot be settled offline, which is why
    /// <c>[Water] RippleSpeed</c> exists at all — it is the dial that closes the gap if the water
    /// reads as a conveyor belt or as a photograph.</para>
    ///
    /// <para>A NOTE ON THE FREQUENCY-SCRUB CLASS, because this function looks like it. The rate
    /// returned here is multiplied by the shared clock inside the shader, and this project has a
    /// standing rule that a STRENGTH may never scale a frequency that is then multiplied by
    /// <c>_Time</c> — the signature of that bug is a term that is correct at t=0 and drifts
    /// further wrong the longer the scene runs, because changing the multiplier teleports the
    /// phase. Nothing here is that: every factor is a CONSTANT for the life of a scene (the
    /// tileset's authored numbers) or a value only a human moves (<c>[Water] RippleSpeed</c>).
    /// Turning the dial does jump the pattern once, which is what a phase change looks like and is
    /// the accepted cost of a tuning dial; nothing varies it per frame, so there is no drift to
    /// accumulate.</para>
    /// </summary>
    /// <param name="authored">The layer's authored <c>_WaterUVAnimSpeed*</c>.</param>
    /// <param name="noise">The material's <c>_WaterNoiseSpeed</c>, whose <c>.x</c> is the shared
    /// clock scale over both layers (authored 1.00, i.e. the identity on the report's tileset).</param>
    /// <param name="dial"><c>[Water] RippleSpeed</c>.</param>
    /// <returns><c>(rateU, rateV, 0, 0)</c> in UV per second, ready to be written straight into the
    /// shader's own <c>_WaterUVAnimSpeed*</c>. The shader multiplies it by the clock and adds it to
    /// the tiled UV and does nothing else, so this function is the ONLY place the interpretation
    /// lives.</returns>
    internal static Vector4 ScrollRate(Vector4 authored, Vector4 noise, float dial)
    {
        float layer = Positive(authored.z) ? authored.z : 1f;
        float clock = Positive(noise.x) ? noise.x : 1f;
        float d = Finite(dial) ? Mathf.Max(dial, 0f) : 1f;
        float k = layer * clock * d;
        float u = Finite(authored.x) ? authored.x * k : 0f;
        float v = Finite(authored.y) ? authored.y * k : 0f;
        return new Vector4(u, v, 0f, 0f);
    }

    /// <summary>
    /// The ripple strength <c>WaterVR</c> should tilt its blended normal by, from the game
    /// material's <c>_DetailOpacityBaseNormalStr</c>.
    ///
    /// <para>WHY THE AUTHORED NUMBER IS NOT PASSED THROUGH RAW. It is 5.0, and 5.0 fed to
    /// <c>normalize(float3(bump * s, 1))</c> tilts the surface by up to 79 degrees — that is not
    /// the game's water, it is a field of shards. The number was authored for an expression nobody
    /// here can read (the game's shaders ship compiled, and there is no install on the build machine
    /// to open them with), so it carries a RELATIVE meaning and not an absolute one:
    /// <see cref="ReferenceNormalStrength"/> is the one measured sample, it maps to
    /// <see cref="NominalRippleStrength"/> here, and a tileset that authors half of it gets half the
    /// ripple. That is the strongest form of "pass the tileset's own value through" that is
    /// available when the receiving expression is unknown.</para>
    ///
    /// <para>WHICH COMPONENT. The name decomposes as Detail / Opacity / BaseNormalStr — three names
    /// for four components — so which slot is the normal strength cannot be settled from the name
    /// alone. It does not have to be: the measured vector is (5.00, 5.00, 0.00, 0.00), so every
    /// reading that picks a NON-ZERO component gives 5.0, and a reading that picked z or w would
    /// give 0.0, i.e. no ripple at all — which the observed water plainly has. <c>.x</c> is taken,
    /// and any of the readings that could be right agree with it.</para>
    /// </summary>
    internal static float NormalStrength(Vector4 authored)
    {
        if (!Finite(authored.x) || authored.x <= 0f)
            return NominalRippleStrength;
        float scaled = authored.x / ReferenceNormalStrength * NominalRippleStrength;
        return Mathf.Clamp(scaled, 0f, MaxRippleStrength);
    }

    /// <summary>
    /// Where the glints are lit from, in the SURFACE-LOCAL frame the shader shades in.
    ///
    /// <para>THE FRAME. <c>WaterVR</c> shades a flat horizontal quad — the hardware FLOOR CENSUS
    /// reads <c>'TERRAIN_Water_Plane' y[0.0..0.0]</c> across its whole footprint — so its tangent
    /// frame is the world XZ plane with the surface normal along world +Y. The mapping is therefore
    /// the exact swizzle <c>(x, z, y)</c> and not an approximation. It is used INSTEAD of a
    /// world-space TBN because building one needs the MESH's tangents, and these quads are
    /// Apparance prefab instances whose importer settings this mod does not own; a missing tangent
    /// is a black surface, and a surface-local frame cannot fail that way.</para>
    ///
    /// <para>This is the ONLY direction anywhere in the shader, and it is a uniform: it does not
    /// depend on where the camera is, so every term built on it is identical in both eyes' MultiPass
    /// passes by construction.</para>
    /// </summary>
    /// <param name="worldToLight">Direction TOWARD the scene's main directional light, in world
    /// space (i.e. <c>-light.transform.forward</c>). Ignored when
    /// <paramref name="haveLight"/> is false.</param>
    /// <param name="haveLight">Whether the scene actually yielded a directional light.</param>
    /// <param name="local">The direction to write into <see cref="LightDirProperty"/>. Always set:
    /// on every refusal path it is <see cref="DefaultLightLocal"/>, because a water film with no
    /// light direction at all is a flat sheet again and that is the outcome this whole build
    /// exists to stop.</param>
    /// <param name="reason">Always set, and it names WHICH source won and why. Goes verbatim into
    /// the census.</param>
    /// <returns>True when the scene's own light was used, false when the constant was.</returns>
    internal static bool TryBuildLightDirection(
        Vector3 worldToLight, bool haveLight, out Vector4 local, out string reason)
    {
        local = new Vector4(
            DefaultLightLocal.x, DefaultLightLocal.y, DefaultLightLocal.z, 0f);

        if (!haveLight)
        {
            reason = "the scene has no directional light, so the glints are lit from the fixed "
                     + $"local direction ({DefaultLightLocal.x:0.##},{DefaultLightLocal.y:0.##},"
                     + $"{DefaultLightLocal.z:0.##}) — steeply overhead and leaning, which is what "
                     + "puts the highlights on the far side of the crests instead of in a band "
                     + "across the middle";
            return false;
        }
        if (!Finite(worldToLight.x) || !Finite(worldToLight.y) || !Finite(worldToLight.z)
            || worldToLight.sqrMagnitude < 1e-8f)
        {
            reason = "the scene's directional light gave a degenerate or non-finite direction "
                     + $"({worldToLight.x:0.###},{worldToLight.y:0.###},{worldToLight.z:0.###}) — "
                     + "refused, and the fixed local direction is used instead";
            return false;
        }

        Vector3 w = worldToLight.normalized;
        // THE SWIZZLE: world (x, y, z) -> local (along U, along V, along the surface normal), for
        // a horizontal quad whose normal is world +Y.
        var candidate = new Vector3(w.x, w.z, w.y);
        if (candidate.z < MinLightElevation)
        {
            reason = $"the scene's directional light points from world ({w.x:0.##},{w.y:0.##},"
                     + $"{w.z:0.##}), i.e. {candidate.z:0.###} above the water's own plane, which "
                     + $"is under the {MinLightElevation:0.##} floor — a light at or below the "
                     + "surface cannot glint on it and would leave sparks on the steepest slopes "
                     + "only, so the fixed local direction is used instead";
            return false;
        }

        local = new Vector4(candidate.x, candidate.y, candidate.z, 0f);
        reason = $"the scene's main directional light, world ({w.x:0.##},{w.y:0.##},{w.z:0.##}) "
                 + $"-> surface-local ({candidate.x:0.##},{candidate.y:0.##},{candidate.z:0.##}) "
                 + "(x along U, y along V, z along the surface normal)";
        return true;
    }

    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

    private static bool Positive(float f) => Finite(f) && f > 0f;
}
