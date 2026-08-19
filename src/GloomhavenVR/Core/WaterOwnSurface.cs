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
/// a diagnostic paint (since retired) had already settled that these ARE our renderers
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
/// of its motion.</b> Its LOOK values are READ OFF THE GAME MATERIAL at runtime and fed to
/// <c>GloomhavenVR/WaterVR</c>; the constants in this file are the measured fallbacks for a tileset
/// whose material does not declare one of them, never a look chosen here. Its three SPEED values
/// are, since ModBuild 166, deliberately not read at all — see
/// <see cref="ForbiddenTranslationProperties"/>, and the ruling below.</para>
///
/// <para>AND SINCE MODBUILD 164 THE GEOMETRY MOVES. Reproducing the tileset exactly was tried and
/// the hardware verdict was <i>"Statt langsam, seichte Wellen sehe ich extrem schnelle (und viele)
/// hektische weiße Streifen die auf der FLACHEN Oberfläche vorbeisausen"</i>, then
/// <i>"Nicht nur 'calm' sondern auch wirklich 3D wellen einbauen"</i>. Three causes of the streaks
/// are answered by <see cref="TameTilings"/>, <see cref="LayerWeights"/> and the shader's own
/// glint; the FLATNESS could not be, because no shading term can give a sheet relief. So
/// <c>WaterVR</c> displaces its vertices — vertically, by <see cref="SwellAmplitude"/>, as a
/// function of world position and time only.</para>
///
/// <para><b>AND SINCE MODBUILD 165 THE GEOMETRY IT MOVES COMES FROM THE GPU.</b> ModBuild 164 sent
/// that displacement to a mesh that had nothing to displace: the census read the film as
/// <c>'TERRAIN_Water_Plane' 33 verts / 0 tris</c> over a 1.73 x 2.0 m hex — a sample every 0.4 m
/// for a 1.1 m wave — and the <c>0 tris</c> is <c>Mesh.triangles</c> coming back empty on a mesh
/// imported without Read/Write, which is also why a CPU subdivision of it could never have worked.
/// The film is now tessellated in the shader's own HULL and DOMAIN stages, which need no access to
/// the index buffer at all, at a FIXED factor — a distance-scaled one would subdivide differently
/// in each MultiPass eye. What remains on this side is the culling hazard that has cost this
/// project a build before: Unity culls against the renderer's bounds and a domain program is as
/// invisible to it as a vertex program, so the driver pads <c>Renderer.localBounds</c> by exactly
/// <see cref="MaxSwellAmplitude"/> on every axis. A pad is EXACT here where the earlier case needed
/// an arc sweep, because this displacement is a bounded translation along one axis rather than a
/// rotation. The tileset's own <c>_addSphericalWaves = 0</c> is still not read and still not
/// reproduced: this swell is the VR-side relief the user asked for by name, with its own dial and
/// its own ceiling.</para>
///
/// <para><b>AND SINCE MODBUILD 166 NOTHING ON THE SURFACE TRANSLATES AT ALL. THAT IS A USER RULING,
/// NOT A TUNING CHOICE.</b> ModBuild 164 was <i>"Es fließt noch viel zu schnell! Das ist kein Fluss
/// sondern soll eher eine Pfütze stehendes Wasser simulieren"</i>. ModBuild 165 answered it by
/// making most of the motion a SWAY that reverses and nets to zero, keeping a residual drift, and
/// the verdict on THAT was <i>"Immer noch viel zu hektisch und es fließt jetzt einmal in die eine
/// Richtung, stoppt kurz und fließt dann wieder in die andere. Erscheint nicht mehr immersiv. Ich
/// will außerdem so gut wie KEIN fließen, es ist kein Fluss sondern eine Pfütze. Die animationen
/// sollen sehr dezent und random sein!"</i></para>
///
/// <para>The sway was the integrator's own design instruction and it was wrong twice over: netting
/// to zero over a cycle is not the same as not moving, and a pattern that reverses reads as MORE
/// artificial than a steady drift, because nothing on real water does it. So the translation is not
/// tuned down — it is DELETED. There is no <c>_SwellSpeed</c>, no <c>_WaterUVAnimSpeed*</c> and no
/// <c>ScrollRate</c> anywhere in this feature (<see cref="ForbiddenTranslationProperties"/>), which
/// means there is no dial to raise and no zero for a later edit to make non-zero. What moves
/// instead: the swell is six STANDING components rising and falling in place at six incommensurate
/// periods (<see cref="SwellRatios"/>, <see cref="ResolvedSwellPeriod"/>) whose spatial phase
/// contains no clock; the ripple is sampled at fixed frames of the world plane and CROSSFADED
/// (<see cref="RippleFadeRatios"/>); and a large-scale bloom of three long standing modulations
/// (<see cref="SwellBloomRatios"/>, <see cref="SwellCalmDepth"/>) makes a different part of the
/// pool the lively one every few minutes without any travelling front. <see cref="SwellHeightAt"/>
/// is the C# mirror the wire test cross-correlates to prove the field's net translation is exactly
/// zero — the claim is measured, not asserted.</para>
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
/// <para>SO WHERE DOES THE SPARKLE COME FROM, NOW THAT NOTHING SCROLLS? THE SWELL'S OWN SLOPE. A
/// fixed light direction dotted against the analytic gradient of the standing wave gives highlights
/// that are born where a crest rises and fade where it falls — light that changes IN PLACE rather
/// than sliding, which is what a puddle under a lamp does, and which is identical in both eyes
/// because the eye is not in the expression. The ripple's crossfade breaks them up further. The direction comes from the scene's own main
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
    //
    //     AND THREE OF THEM ARE GONE SINCE MODBUILD 166, WHICH IS THE POINT OF THIS ROUND.
    //     `_WaterUVAnimSpeedA`, `_WaterUVAnimSpeedB` and `_SwellSpeed` were the three names a SPEED
    //     could be written under, and the ruling on ModBuild 165 was that the pattern must not
    //     travel at all — see <see cref="RippleFadeRatios"/>. They are DELETED rather than written
    //     as zero: a zeroed dial is one edit away from being raised, and a property that no longer
    //     exists is not.
    internal const string NormalMapProperty = "_Normal_Map";
    internal const string NormalTilingsProperty = "_NormalTilings";
    internal const string LayerWeightsProperty = "_LayerWeights";
    internal const string NormalStrengthProperty = "_NormalStrength";
    internal const string ProcNormalProperty = "_ProcNormal";
    internal const string LightDirProperty = "_LightDir";
    internal const string ShimmerProperty = "_Shimmer";
    internal const string WaveShadeProperty = "_WaveShade";
    internal const string SmoothnessProperty = "_Smoothness";
    internal const string SwellAmpProperty = "_SwellAmp";
    internal const string SwellWaveProperty = "_SwellWave";
    internal const string SwellPeriodProperty = "_SwellPeriod";
    internal const string SwellCalmProperty = "_SwellCalm";
    internal const string RippleFadeProperty = "_RippleFade";
    internal const string TessFactorProperty = "_TessFactor";

    /// <inheritdoc cref="CommonProperties"/>
    internal static readonly string[] WaterProperties =
    {
        NormalMapProperty, NormalTilingsProperty, LayerWeightsProperty,
        NormalStrengthProperty, ProcNormalProperty, LightDirProperty,
        ShimmerProperty, WaveShadeProperty, SmoothnessProperty,
        SwellAmpProperty, SwellWaveProperty, SwellPeriodProperty,
        SwellCalmProperty, RippleFadeProperty, TessFactorProperty,
    };

    /// <summary>Property names that MUST NOT appear in the film shader at all, with the reason.
    /// Every one of them is a SPEED — a term that moves the pattern bodily across the pool — and
    /// the standing user ruling is that the water has none. The wire test sweeps the shader source
    /// for each, so re-declaring one fails the build gate rather than a seventh photograph.</summary>
    internal static readonly string[] ForbiddenTranslationProperties =
    {
        "_WaterUVAnimSpeedA", "_WaterUVAnimSpeedB", "_SwellSpeed", "_RippleSway",
        "_WaterNoiseSpeed",
    };

    // --- the names the GAME's material carries these values under. Three of them happen to be
    //     spelled the same on both sides (_Normal_Map, _NormalTilings, _Smoothness). Every name
    //     here was printed by the hardware census, so none is guessed.
    //
    //     `_WaterUVAnimSpeedA/B` AND `_WaterNoiseSpeed` ARE NO LONGER READ, and this is the one
    //     place in the file where a value the tileset authored is deliberately dropped. They are
    //     SCROLL RATES, in texture repeats or world units per second, and there is no longer a
    //     coordinate in the shader that scrolls. The only way to keep using them would be to
    //     re-purpose a rate as a crossfade rate, which is a translation dial under a new name and
    //     exactly the sort of edit that would bring the flow back. So the tileset's motion is not
    //     reproduced; the tileset's LOOK — its normal map, its tilings, its tint, its smoothness —
    //     still is.
    internal const string GameNormalStrengthProperty = "_DetailOpacityBaseNormalStr";
    internal const string GameTintProperty = "_Color_Tint";

    // --- the measured authored values, used ONLY as fallbacks for a material that does not
    //     declare one of them. Source: the WATER SURFACE census of
    //     TERRAIN_GEN_WaterPlane_Crypt_Mat on VFX/Water_Shd_Trans.
    internal static readonly Vector4 AuthoredTilings = new(0.14f, 6.00f, -0.12f, -0.20f);
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
    /// the ripple strength the tileset's authored value means HERE.
    ///
    /// <para>IT WAS 1.2 AND THAT WAS THE WHOLE SURFACE. With the two ripple layers summed at full
    /// weight, 1.2 tilts the blended normal by up to about 26 degrees — four times the ~6 degrees
    /// the swell's own crests reach at the shipped amplitude and wavelength — so the normal map
    /// was not detail ON the waves, it WAS the waves, which is one half of "nur weiße streifen auf
    /// einer flachen Oberfläche". The texture ripple is now second-order by construction: the
    /// layers are weighted to sum to 1 rather than to 2, and this reference is 0.5.</para></summary>
    internal const float NominalRippleStrength = 0.5f;

    /// <summary>Ceiling on the ripple strength, and it is a comfort limit as much as a look one.
    /// Past about this the two blended normals tilt far enough that <c>dot(N, L)</c> swings from
    /// end to end within a pixel, the glints stop being highlights and become per-pixel noise, and
    /// per-pixel noise on a 90 Hz headset ALIASES into a crawling carpet — which is a discomfort
    /// report, not merely an ugly one. Lowered with <see cref="NominalRippleStrength"/> for the
    /// same reason: a tileset authoring three times the reference must still not out-shout the
    /// swell.</summary>
    internal const float MaxRippleStrength = 1.5f;

    /// <summary>
    /// How far each ripple layer's tiling is pulled toward its OWN geometric mean, 0 = the
    /// authored anisotropy verbatim and 1 = perfectly isotropic.
    ///
    /// <para>THIS IS THE STREAK. The measured <c>_NormalTilings</c> layer A is (0.14, 6.00): one
    /// texture repeat every 7.1 world units across and every 0.17 world units along, i.e. the
    /// normal map stretched 43:1 into long thin bands. A band is what a streak IS, and set moving
    /// it is the report's "hektische weiße Streifen" exactly. The tame is a power blend of the
    /// ratio, so 43:1 lands at 43^(1-0.70) = 3.1:1 — the flow direction survives, the razor does
    /// not.</para>
    ///
    /// <para>It is a constant rather than a dial because it is not a preference: an anisotropy the
    /// user can restore is a way to put a shipped defect back. <see cref="TameTilings"/> is the
    /// pure function, and the census prints the resolved wavelengths in metres so "too big" or
    /// "too small" is a number in the next log rather than an argument.</para>
    /// </summary>
    internal const float AnisoTame = 0.70f;

    /// <summary>
    /// The LONGEST swell component's wavelength in world units at <c>[Water] WaveScale</c> 1. The
    /// other three are irrational fractions of it (see <see cref="SwellRatios"/>).
    ///
    /// <para>RE-BASED FROM 1.1 m, AND THE OLD VALUE IS HALF OF WHY EVERY TILE LOOKED THE SAME. The
    /// game lays these films on a lattice of <see cref="TileLatticeX"/> x <see cref="TileLatticeZ"/>
    /// metres (hardware census: <c>TERRAIN_Water_Plane</c> local bounds size (1.73, 0, 1.998)). A
    /// single 1.1 m train is 1.573 lattice steps across and 1.816 along, i.e. within 6% of
    /// repeating on the lattice in one axis and 18% in the other — over the two or three tiles the
    /// eye takes in at once, that IS a repeat. 2.4 m is longer than either lattice vector, so the
    /// primary component spans more than one hex and cannot be read as a per-tile figure at all,
    /// and <see cref="LatticeMismatch"/> measures what the six of them together leave.</para>
    /// </summary>
    internal const float SwellWavelength = 2.4f;

    /// <summary>
    /// How long the LONGEST swell component takes to rise and fall once, in seconds, at
    /// <c>[Water] RippleSpeed</c> 1. The shorter components scale as the square root of their
    /// wavelength ratio — deep-water dispersion reduced to its shape.
    ///
    /// <para>1.1 s IS THE PHYSICAL ANSWER FOR THIS WAVE, which is why the dial and not the constant
    /// carries the "nur minimal Bewegungen" ruling. A 2.4 m deep-water wave has a period of
    /// sqrt(2 pi L / g) = 1.24 s; at the dial's own 1.0 this shader is therefore real water, and the
    /// shipped <c>[Water] RippleSpeed</c> of 0.00875 stretches it to 126 s, which is a puddle. That
    /// split means the dial is a statement anyone can check ("a sixtieth of real water's rate")
    /// rather than a number chosen against a photograph, and turning it up gives something
    /// recognisable rather than something arbitrary. THE DIAL HAS BEEN RE-BASED TWICE, 0.12 ->
    /// 0.035 -> 0.0175 -> 0.00875; this constant has not moved, because it is a measurement of water rather
    /// than a preference.</para>
    /// </summary>
    internal const float SwellPeriod = 1.1f;

    /// <summary>How far the quiet parts of the pool drop below full amplitude — 0 = the same
    /// everywhere, 1 = dead still wherever the three very long standing modulations cancel. This is
    /// the term that answers <i>"Die animationen sollen sehr dezent und random sein"</i> most
    /// directly, and it does it as a property of the continuous world-space field rather than per
    /// quad: a per-quad seed would put a step in the height at every tile seam.</summary>
    internal const float SwellCalmDepth = 0.75f;

    /// <summary>The six swell components' wavelengths, as fractions of
    /// <see cref="SwellWavelength"/>. Irrational and mutually incommensurate on purpose —
    /// 1, 1/phi, sqrt(2)-1, 2-sqrt(3), sqrt(3)-1, 2sqrt(2)-2 — so the sum has no finite period in
    /// any direction and the field genuinely never repeats rather than repeating on a cycle longer
    /// than the pool.
    ///
    /// <para>SIX SINCE MODBUILD 166, AND THE REASON IS TEMPORAL. Each component's bob period is the
    /// square root of its wavelength ratio (deep-water dispersion), so six wavelengths are six
    /// mutually incommensurate RATES; with four the sum still had a legible envelope — a few
    /// seconds of activity, then a lull — and <i>"die animationen sollen sehr dezent und random
    /// sein"</i> rules a rhythm out. Six also divides the same peak amplitude over more crests, so
    /// each one is individually gentler. The two added ones were picked by an exhaustive sweep against
    /// <see cref="LatticeMismatch"/> — every candidate ratio of that irrational family against every
    /// whole-degree direction at least 13 degrees from all the others, keeping only pairs that leave
    /// the mismatch at or above 0.145 cycles at <c>[Water] WaveScale</c> 1, a median of at least
    /// 0.055 over the whole dial, and no round dial setting scoring better than the shipped one. The
    /// winner leaves the mismatch at exactly the 0.156 the four-component table scored, i.e. neither
    /// new component is ever the worst one.</para>
    ///
    /// <para>MIRRORED IN THE SHADER (WaterVR.cginc): the wire test reads both and fails on
    /// drift.</para></summary>
    internal static readonly float[] SwellRatios =
        { 1f, 0.618034f, 0.414214f, 0.267949f, 0.732051f, 0.828427f };

    /// <summary>The six swell components' directions, in degrees. None is axis-aligned, no two are
    /// 90 degrees apart, no two are within 13 degrees of each other, and none belongs to the hex
    /// lattice's own 60-degree family — so no pair can conspire into a corrugation and none runs
    /// along a row of tiles. MIRRORED IN THE SHADER as unit vectors (WaterVR.cginc); the wire test
    /// reads both and fails on drift.</summary>
    internal static readonly float[] SwellDirections = { 17f, 103f, 61f, 148f, 47f, 164f };

    /// <summary>The six swell components' relative amplitudes: the wavelength ratio raised to 1.25,
    /// normalised by <see cref="SwellAmplitudeSum"/> so the six together peak at exactly the
    /// amplitude <see cref="SwellAmplitude"/> returns. The exponent is above 1, so the SHORT
    /// components are gentler in slope as well as in height (slope goes as ratio^0.25) and the
    /// longest wave carries the shape rather than competing with the detail.
    /// MIRRORED IN THE SHADER (WaterVR.cginc).</summary>
    internal static readonly float[] SwellAmplitudes =
        { 1f, 0.547981f, 0.332300f, 0.192781f, 0.677137f, 0.790347f };

    /// <inheritdoc cref="SwellAmplitudes"/>
    internal const float SwellAmplitudeSum = 3.540547f;

    /// <summary>
    /// The three ripple CROSSFADE periods, as multiples of the swell's own resolved period — so
    /// <c>[Water] RippleSpeed</c> 0 freezes the ripple as well as the geometry, and there is one
    /// clock over the whole surface rather than a rhythm running under a motionless swell.
    ///
    /// <para>THIS IS WHAT REPLACED THE SCROLL, AND IT IS A USER RULING RATHER THAN A TUNING CHOICE.
    /// Up to ModBuild 165 each normal layer was OFFSET by a rate times the clock — a one-way drift
    /// plus a sway that reversed — and the verdict was <i>"es fließt jetzt einmal in die eine
    /// Richtung, stoppt kurz und fließt dann wieder in die andere. Erscheint nicht mehr immersiv.
    /// Ich will außerdem so gut wie KEIN fließen, es ist kein Fluss sondern eine Pfütze."</i> The
    /// sway was the integrator's own design instruction and it was wrong: netting to zero over a
    /// cycle is not the same as not moving, and a reversing pattern reads as MORE artificial than a
    /// steady drift, because nothing on water does it.</para>
    ///
    /// <para>So the ripple's coordinate is now a FIXED function of world position, sampled in three
    /// fixed frames of the plane, and only the WEIGHTS of the three move. Features fade in and out
    /// where they are. THE THREE RATIOS ARE THE FIRST THREE POWERS OF phi — 1.618, 2.618, 4.236 —
    /// so every pairwise ratio is itself a power of the most irrational number there is, which is
    /// the strongest available statement that the three cycles never come back into step. (The
    /// first draft used 1.618 / 2.414 / 3.732 and the wire test caught it: 2.414/1.618 is 1.492,
    /// within half a percent of three halves, i.e. a pair that beats every third cycle.) At the
    /// shipped dial they resolve to 102, 165 and 266 seconds — long enough that the mix is "barely
    /// moving" by construction and not by taste. The census prints all three.</para>
    /// </summary>
    internal static readonly float[] RippleFadeRatios = { 1.618034f, 2.618034f, 4.236068f };

    /// <summary>The three BLOOM periods — the large-scale envelope that decides which part of the
    /// pool is lively — again as multiples of the swell's own resolved period, so one dial moves
    /// everything. They resolve to 195, 295 and 459 seconds at the shipped dial: a disturbance that
    /// swells and decays in one place over a couple of minutes and then in another, which is what
    /// "random" looks like on standing water. MIRRORED IN THE SHADER (GhvrSwellBloom).</summary>
    internal static readonly float[] SwellBloomRatios = { 3.1f, 4.7f, 7.3f };

    /// <summary>The measured tile lattice the films are laid out on, in world units: the hardware
    /// census reads <c>TERRAIN_Water_Plane</c>'s local bounds as size (1.73, 0, 1.998).
    /// <see cref="LatticeMismatch"/> is what keeps the swell from repeating on it.</summary>
    internal const float TileLatticeX = 1.73f;

    /// <inheritdoc cref="TileLatticeX"/>
    internal const float TileLatticeZ = 1.998f;

    /// <summary>
    /// How many ways the GPU splits each authored edge of the film's mesh. FIXED, never scaled by
    /// the distance to the camera.
    ///
    /// <para>THE DISTANCE RAMP IS DISQUALIFIED HERE, and it is worth writing down because it is
    /// what every tessellation tutorial does. Under MULTIPASS the two eyes are separate passes
    /// about 6.4 cm apart, so a factor computed from the camera would subdivide the same patch to
    /// different densities per eye and sample different crest heights — stereo rivalry along every
    /// silhouette, which is the class of defect this module exists to remove. A constant costs more
    /// triangles far away and is the only version that is safe.</para>
    ///
    /// <para>THE COST IS STATED, NOT ASSUMED. The census reads the film at 33 vertices over a
    /// 1.73 x 2.0 m hex — roughly 32 triangles and an edge near 0.47 m — and a factor of 4 makes
    /// that 0.12 m for SIXTEEN times the triangles: one film becomes ~510 and the report's pool of
    /// 17 becomes ~8700, which is one small prop's worth for the whole pool, on a PC GPU rendering
    /// two eyes at 90 Hz. No new vertex data is uploaded for any of it — the tessellator reads the
    /// authored mesh and the driver uploads nothing.</para>
    ///
    /// <para>AND THE HONEST CAVEAT RUNS THE OTHER WAY. The offscreen sheet was diffed with the
    /// tessellated and untessellated SubShaders forced in turn at the shipped amplitude, and they
    /// differ on 0.31% of the pixels of the waterline frame and 0.02% of the grazing one. That is
    /// because the fragment takes its normal from the ANALYTIC gradient rather than from the mesh,
    /// so extra vertices buy the SILHOUETTE and the true height against the bed — nothing else. At
    /// a 1.3 cm amplitude that is a thin band at the water's edge. It is still worth having: the
    /// edge is exactly where "flache Oberfläche" was judged, and the amplitude is a dial the user
    /// can raise. But nobody should expect this number to transform a photograph.</para>
    /// </summary>
    internal const float TessellationFactor = 4f;

    /// <summary>Ceiling on <see cref="TessellationFactor"/>, mirrored by the shader's own Range and
    /// by a clamp inside its patch-constant function. 8 ways per edge is 64 triangles per authored
    /// one, which on this film is already past the point where more of them change the
    /// silhouette.</summary>
    internal const float MaxTessellationFactor = 8f;

    /// <summary>
    /// Ceiling on the swell's peak amplitude in world units, and it is a COLLISION limit rather
    /// than a taste one. The hardware FLOOR CENSUS reads the film at <c>y[0.0..0.0]</c> with
    /// <c>TERRAIN_Crypt_Water_02_Base</c> directly beneath it at <c>y[-0.34..-0.09]</c> — a 9 cm
    /// gap. The wave is symmetric about the authored plane, so a trough reaches minus this much;
    /// 6 cm leaves 3 cm of clearance at the worst dial setting, and the shipped default now uses
    /// barely a fifth of it (1.3 cm on the report's 2.6 m film) because the ruling on ModBuild 165's
    /// 3.6 cm was still "viel zu hektisch". A film that dipped through its own basin bed would z-fight with the stone,
    /// which reads as the pool tearing open.
    /// </summary>
    internal const float MaxSwellAmplitude = 0.06f;

    /// <summary>The measured gap between the film and the basin bed under it, which
    /// <see cref="MaxSwellAmplitude"/> is derived from and which the wire test pins them
    /// against.</summary>
    internal const float FilmToBedGap = 0.09f;

    /// <summary>The edge length, in world units, the tessellation is aiming at. The census reads
    /// the film at 33 vertices over a 1.73 x 2.0 m hex; a planar patch with that many vertices
    /// carries roughly 32 triangles, i.e. an authored edge near 0.47 m, and
    /// <see cref="TessellationFactor"/> 4 brings that to 0.12 m — five samples across even the
    /// SHORTEST of the four swell components and nineteen across the longest. It is a stated TARGET
    /// rather than an input: the factor is fixed for the stereo reason above, so nothing computes a
    /// factor from this number, and the census prints both so the claim can be checked against the
    /// mesh the game actually supplied.</summary>
    internal const float TargetEdgeWU = 0.12f;

    /// <summary>
    /// How far the swell's six components are from repeating on the film lattice, in CYCLES, worst
    /// case over both lattice vectors and all six components. Bigger is better; 0 would mean the
    /// field is identical on every tile.
    ///
    /// <para>WHAT IT COMPUTES. Two neighbouring films differ by a lattice vector v, so component i
    /// picks up a phase of (d_i . v) / L_i cycles between them. If that is a whole number the
    /// component looks the same on both tiles. The whole FIELD repeats only if EVERY component
    /// does, so the distance of the WORST component from a whole number is what says a per-tile
    /// pattern cannot form — and it is the worst component that has to be far, not the average.</para>
    ///
    /// <para>WHY IT IS A FUNCTION AND NOT A COMMENT. ModBuild 164's single 1.1 m train scored 0.06
    /// against this lattice and the surface was reported as identical tile for tile. The number is
    /// therefore a property of the shipped look, it moves whenever the wavelength or a direction is
    /// retuned, and <c>WaterOwnSurfaceVectors</c> fails the build gate if it drops below a tenth of
    /// a cycle.</para>
    /// </summary>
    /// <param name="wavelength">The longest component's wavelength in world units, i.e. what the
    /// shader receives in <see cref="SwellWaveProperty"/>.</param>
    internal static float LatticeMismatch(float wavelength)
    {
        if (!Finite(wavelength) || wavelength <= 0f)
            return 0f;

        float worst = 1f;
        var lattice = new[] { new Vector2(TileLatticeX, 0f), new Vector2(0f, TileLatticeZ) };
        foreach (Vector2 v in lattice)
        {
            for (int i = 0; i < SwellRatios.Length; i++)
            {
                float rad = SwellDirections[i] * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                float len = wavelength * SwellRatios[i];
                if (len <= 1e-4f)
                    continue;
                float cycles = Vector2.Dot(dir, v) / len;
                // Distance to the NEAREST whole number of cycles, in [0, 0.5].
                float frac = Mathf.Abs(cycles - Mathf.Round(cycles));
                worst = Mathf.Min(worst, frac);
            }
        }
        return worst;
    }

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
    /// The swell's BOB PERIOD in seconds — how long the longest component takes to rise and fall
    /// once — and, through <see cref="RippleFadeRatios"/> and <see cref="SwellBloomRatios"/>, the
    /// clock every other cycle on the surface is a multiple of.
    ///
    /// <para>THERE IS NO `ScrollRate` ANY MORE, AND THIS FUNCTION IS WHAT STANDS WHERE IT STOOD.
    /// Up to ModBuild 165 the tileset's authored <c>_WaterUVAnimSpeedA/B</c> were resolved here
    /// into world units per second and handed to the shader, which offset the ripple's sampling
    /// coordinate by rate times the clock. That is a translation, the user has now rejected it
    /// three times (<i>"es ist kein Fluss sondern eine Pfütze"</i>), and the function is deleted
    /// with the properties it fed. Nothing in the surface has a rate any more; everything has a
    /// PERIOD, and a period cannot move a pattern.</para>
    ///
    /// <para>WHY THE DIAL DIVIDES. <c>[Water] RippleSpeed</c> is the one number a human moves, and
    /// "faster water" has to mean "shorter cycles" now that it cannot mean "quicker current". At
    /// the dial's own 1.0 the 2.4 m swell bobs in 1.1 s, which is what a real deep-water wave that
    /// long does (sqrt(2 pi L / g) = 1.24 s); the shipped 0.00875 stretches that to 126 s, i.e. about
    /// a sixtieth of real water. That split is what makes the default a statement anyone can check
    /// rather than a number chosen against a photograph.</para>
    ///
    /// <para>AND HALVING THE DIAL HALVES THE WHOLE SURFACE'S RATE, which is the property this
    /// function exists for. <see cref="RippleFadeRatios"/> and <see cref="SwellBloomRatios"/> are
    /// multiples of what this returns and the shader's bloom divides the same
    /// <c>_SwellPeriod</c>, so there is exactly one place a tempo ruling has to land. ModBuild 167
    /// re-based the dial 0.035 -> 0.0175 (ModBuild 167) -> 0.00875 (ModBuild 168), and every period on the surface doubled each time, together —
    /// "Mach die animation halb so schnell" answered without touching the relation between the
    /// swell, the crossfade and the bloom.</para>
    ///
    /// <para>AND THE PERIOD SCALES AS sqrt(WAVELENGTH), not linearly — deep-water dispersion, the
    /// same rule the six components are spaced by. A longer swell is a slower one, which is what
    /// stops <c>[Water] WaveScale</c> from turning a lazy roll into a fast one.</para>
    ///
    /// <para>A NOTE ON THE FREQUENCY-SCRUB CLASS, because this function looks like it. The value
    /// returned here becomes a divisor of the shared clock inside the shader, and this project has
    /// a standing rule that a STRENGTH may never scale a frequency that is then multiplied by
    /// <c>_Time</c> — the signature of that bug is a term that is correct at t=0 and drifts further
    /// wrong the longer the scene runs, because changing the multiplier teleports the phase.
    /// Nothing here is that: both factors are constants for the life of a scene, or values only a
    /// human moves. Turning a dial does jump the pattern once, which is what a phase change looks
    /// like and is the accepted cost of a tuning dial; nothing varies it per frame, so there is no
    /// drift to accumulate.</para>
    /// </summary>
    /// <param name="waveScale"><c>[Water] WaveScale</c>.</param>
    /// <param name="dial"><c>[Water] RippleSpeed</c>. Clamped to <see cref="MinSpeedDial"/> rather
    /// than special-cased, so 0 holds the surface still with its relief intact instead of
    /// flattening it.</param>
    internal static float ResolvedSwellPeriod(float waveScale, float dial)
    {
        float s = Positive(waveScale) ? waveScale : 1f;
        float d = Finite(dial) ? Mathf.Max(dial, 0f) : 1f;
        return SwellPeriod * Mathf.Sqrt(Mathf.Max(s, 0.01f)) / Mathf.Max(d, MinSpeedDial);
    }

    /// <summary>The smallest rate this dial resolves at, so that <c>[Water] RippleSpeed</c> 0 means
    /// STILL and not merely "slower". It has to be far below the shipped default rather than a
    /// round number near it: the floor used to be 0.01 against a shipped 0.12, which made 0 a
    /// twelvefold slowdown, and two re-bases of the default have since walked the shipped value
    /// down past it. At 1e-4 the swell's period at 0 is about three hours, which is a surface that
    /// does not change while anyone is looking at it — and the wire test pins the RATIO to the
    /// shipped period rather than the constant, so a third re-base cannot quietly undo this
    /// again.</summary>
    internal const float MinSpeedDial = 1e-4f;

    /// <summary>The three ripple crossfade periods in seconds, from the resolved swell period.
    /// Written straight into <see cref="RippleFadeProperty"/>; the census prints all three, so
    /// "too fast" is a number in the next log.</summary>
    internal static Vector4 ResolvedFadePeriods(float swellPeriod)
    {
        float p = Finite(swellPeriod) ? Mathf.Max(swellPeriod, 0.5f) : SwellPeriod;
        return new Vector4(p * RippleFadeRatios[0], p * RippleFadeRatios[1],
                           p * RippleFadeRatios[2], 0f);
    }

    /// <summary>The six components' authored SPATIAL phases, mirrored from WaterVR.cginc call by
    /// call — arbitrary irrational-looking constants whose only job is that the six do not start
    /// life aligned. Internal because the wire test's translation measurement needs them: an
    /// untranslated field's projection lands on exactly these angles, and the difference from them
    /// IS the distance the pattern has moved. <c>SwellTableMatchesTheShader</c> pins them against
    /// the shader's own call list, so there is one table and not two.</summary>
    internal static readonly float[] SwellSpatialPhases =
        { 0.000f, 2.399f, 4.113f, 1.071f, 5.602f, 3.246f };

    /// <summary>The six components' authored TEMPORAL phases. <inheritdoc
    /// cref="SwellSpatialPhases"/></summary>
    internal static readonly float[] SwellTemporalPhases =
        { 0.000f, 1.777f, 3.412f, 5.108f, 2.483f, 0.914f };

    /// <summary>The bloom's three modulation directions in degrees, wavelength multiples of the
    /// swell's own, and spatial phases — mirrored from <c>GhvrSwellBloom</c> and internal for the
    /// same reason as <see cref="SwellSpatialPhases"/>.</summary>
    internal static readonly float[] BloomDirections = { 37f, 126f, 72f };

    /// <inheritdoc cref="BloomDirections"/>
    internal static readonly float[] BloomWavelengthScales = { 4.1f, 6.7f, 9.3f };

    /// <inheritdoc cref="BloomDirections"/>
    internal static readonly float[] BloomSpatialPhases = { 0f, 1.3f, 3.7f };
    private static readonly float[] BloomTemporalPhases = { 0f, 2.1f, 4.3f };

    /// <summary>
    /// THE SWELL'S HEIGHT FIELD, IN C#, EVALUATED EXACTLY AS THE SHADER EVALUATES IT — the same six
    /// standing components, the same three-component bloom, the same normalisation.
    ///
    /// <para>WHY A SECOND COPY EXISTS AT ALL, when this file's whole discipline is one expression
    /// per fact. Because the ruling of ModBuild 166 is a claim about the field's BEHAVIOUR OVER
    /// TIME — "it must not translate, at all, in any direction, ever" — and no source lint can
    /// prove that. A regex can see that the string <c>drift</c> is gone; it cannot see that a
    /// plausible-looking new term moves the pattern 3 cm a minute. So
    /// <c>WaterOwnSurfaceVectors.PatternNeverTranslates</c> samples this function on a grid at two
    /// instants and cross-correlates the two — the same measurement the offscreen harness makes on
    /// the rendered pixels — and fails the build unless the best match is at exactly zero offset.
    /// The test also runs a deliberately drifted copy of the same field and requires the
    /// measurement to FIND that one, so a check that could never fail is not mistaken for a pass.
    /// <c>SwellTableMatchesTheShader</c> is what keeps this copy and the HLSL one the same field.
    /// </para>
    /// </summary>
    /// <param name="x">World X.</param>
    /// <param name="z">World Z.</param>
    /// <param name="t">Seconds on the shared clock.</param>
    /// <param name="wavelength">The longest component's wavelength, i.e.
    /// <see cref="SwellWaveProperty"/>.</param>
    /// <param name="period">The longest component's bob period, i.e.
    /// <see cref="SwellPeriodProperty"/>.</param>
    /// <param name="amp">Peak displacement, i.e. <see cref="SwellAmpProperty"/>.</param>
    /// <param name="calmDepth"><see cref="SwellCalmProperty"/>.</param>
    /// <remarks>The phase tables below are mirrored from the shader call by call. They are
    /// arbitrary irrational-looking constants whose only job is that the six components do not
    /// start life aligned; nothing reads them but this mirror and the shader.</remarks>
    internal static float SwellHeightAt(
        float x, float z, float t, float wavelength, float period, float amp, float calmDepth)
    {
        const float Tau = 6.2831853f;
        float L = Mathf.Max(wavelength, 0.15f);
        float T = Mathf.Max(period, 0.5f);

        float s = 0f;
        for (int i = 0; i < SwellRatios.Length; i++)
        {
            float rad = SwellDirections[i] * Mathf.Deg2Rad;
            float dx = Mathf.Cos(rad), dy = Mathf.Sin(rad);
            float k = Tau / Mathf.Max(L * SwellRatios[i], 0.05f);
            float w = Tau / Mathf.Max(T * Mathf.Sqrt(SwellRatios[i]), 0.25f);
            // THE SPATIAL PHASE HAS NO `t` IN IT. That single fact is what the wire test measures,
            // and it is written here in the same shape the shader writes it so a reader can see
            // that the two agree.
            float sp = k * (dx * x + dy * z) + SwellSpatialPhases[i];
            s += SwellAmplitudes[i] / SwellAmplitudeSum
                 * Mathf.Sin(sp) * Mathf.Cos(w * t + SwellTemporalPhases[i]);
        }
        return amp * SwellBloomAt(x, z, t, L, T, calmDepth) * s;
    }

    /// <summary>The bloom envelope, mirrored from <c>GhvrSwellBloom</c>: three long crossing
    /// STANDING modulations, so the lively part of the pool changes place by fading rather than by
    /// travelling. Bounded in [1 - calmDepth, 1], never above 1, because the peak amplitude is what
    /// the renderer's bounds were padded for.</summary>
    internal static float SwellBloomAt(
        float x, float z, float t, float wavelength, float period, float calmDepth)
    {
        const float Tau = 6.2831853f;
        float T = Mathf.Max(period, 0.5f);
        float m = 0f;
        for (int i = 0; i < 3; i++)
        {
            float rad = BloomDirections[i] * Mathf.Deg2Rad;
            float k = Tau / Mathf.Max(wavelength * BloomWavelengthScales[i], 0.2f);
            float q = k * (Mathf.Cos(rad) * x + Mathf.Sin(rad) * z) + BloomSpatialPhases[i];
            float b = Mathf.Cos(Tau * t / (T * SwellBloomRatios[i]) + BloomTemporalPhases[i]);
            m += Mathf.Sin(q) * b;
        }
        m /= 3f;
        float calm = Mathf.Clamp01(calmDepth);
        return 1f - calm * (0.5f - 0.5f * m);
    }

    /// <summary>
    /// The tilings <c>WaterVR</c> actually samples at, in REPEATS PER WORLD UNIT, from the game
    /// material's authored <c>_NormalTilings</c>.
    ///
    /// <para>TWO THINGS HAPPEN HERE AND BOTH ARE THE REPORT. First the ANISOTROPY is tamed: each
    /// layer's two components are pulled toward that layer's own geometric mean by
    /// <see cref="AnisoTame"/>, which is a power blend of the ratio — so the mean feature size is
    /// preserved EXACTLY (the product of the two components is unchanged), only the aspect ratio
    /// moves, and the flow direction is kept. The measured layer A (0.14, 6.00) is 43:1, i.e. a
    /// band 7.1 m long and 17 cm wide, and 43:1 tamed lands at 3.1:1 — (0.52, 1.61), or 1.92 m by
    /// 0.62 m per repeat. Then <c>[Water] WaveScale</c> divides both components, because a bigger
    /// wave is FEWER repeats per metre.</para>
    ///
    /// <para>A layer with a zero or non-finite component has no geometric mean to pull toward — a
    /// tiling of 0 means "constant along this axis", which is a shape and not an accident — so it
    /// is passed through with the scale applied and nothing else. That is the honest reading, and
    /// the census says DEFAULTED or READ for the source vector either way.</para>
    /// </summary>
    /// <param name="authored">The material's <c>_NormalTilings</c>: layer A in xy, layer B in
    /// zw.</param>
    /// <param name="waveScale"><c>[Water] WaveScale</c> — how big the waves are, as a multiple of
    /// the shipped size. Non-finite or non-positive is treated as 1.</param>
    internal static Vector4 TameTilings(Vector4 authored, float waveScale)
    {
        float s = Positive(waveScale) ? waveScale : 1f;
        Vector2 a = TameLayer(new Vector2(authored.x, authored.y), s);
        Vector2 b = TameLayer(new Vector2(authored.z, authored.w), s);
        return new Vector4(a.x, a.y, b.x, b.y);
    }

    private static Vector2 TameLayer(Vector2 t, float waveScale)
    {
        float ax = Mathf.Abs(t.x), ay = Mathf.Abs(t.y);
        if (!Positive(ax) || !Positive(ay))
            return t / waveScale;

        float gm = Mathf.Sqrt(ax * ay);
        float p = 1f - Mathf.Clamp01(AnisoTame);
        float x = gm * Mathf.Pow(ax / gm, p) * Mathf.Sign(t.x);
        float y = gm * Mathf.Pow(ay / gm, p) * Mathf.Sign(t.y);
        return new Vector2(x, y) / waveScale;
    }

    /// <summary>
    /// How much of the blended ripple normal each layer contributes, given the RESOLVED tilings.
    ///
    /// <para>THE TWO LAYERS USED TO BE ADDED AT EQUAL WEIGHT, and that is the third of the four
    /// causes in the ModBuild 163 report: the fine, fast, streaky layer competed with the coarse
    /// one instead of decorating it. Water does not work that way — a short wave rides on a long
    /// one at a fraction of its height — so the weights are inversely proportional to each layer's
    /// own frequency (the geometric mean of its tiling, in repeats per world unit) and sum to 1.
    /// On the measured tilings that is A 0.145 / B 0.855: the 6-7 m swell carries the shape and
    /// the 0.6-1.9 m layer is detail on top of it.</para>
    ///
    /// <para>Summing to 1 rather than to 2 is deliberate as well: the old expression added two
    /// full-amplitude layers, so the same <c>_NormalStrength</c> produced twice the tilt it reads
    /// as. See <see cref="NominalRippleStrength"/>.</para>
    /// </summary>
    /// <param name="resolved">The output of <see cref="TameTilings"/>.</param>
    /// <returns><c>(weightA, weightB, 0, 0)</c>, ready for <see cref="LayerWeightsProperty"/>. A
    /// degenerate tiling on either layer falls back to an even split, which is the previous
    /// behaviour and cannot make the surface disappear.</returns>
    internal static Vector4 LayerWeights(Vector4 resolved)
    {
        float fa = Mathf.Sqrt(Mathf.Abs(resolved.x * resolved.y));
        float fb = Mathf.Sqrt(Mathf.Abs(resolved.z * resolved.w));
        if (!Positive(fa) || !Positive(fb))
            return new Vector4(0.5f, 0.5f, 0f, 0f);
        float sum = fa + fb;
        // 1/f normalised: the reciprocal weights (1/fa, 1/fb) scaled to sum to 1 are (fb, fa)/sum.
        return new Vector4(fb / sum, fa / sum, 0f, 0f);
    }

    /// <summary>
    /// The swell's peak vertical displacement in world units, from the film quad's OWN width.
    ///
    /// <para>WHY IT IS A FRACTION OF THE QUAD RATHER THAN A CONSTANT. The mod's environments are
    /// dioramas at several scales and the same pool can arrive an order of magnitude smaller; a
    /// world constant would be an invisible ripple in one room and a churning sea in another.
    /// The report's film measures 2.6 m across its renderer bounds and the shipped dial is 0.5%, so
    /// it gets a 1.3 cm peak — 2.6 cm between trough and crest. Summed over the six components that
    /// is a peak crest slope of 2.9 degrees, against ModBuild 165's 8.6 and ModBuild 164's 19:
    /// "viel zu hektisch" answered as an angle rather than as a preference, and still relief that is
    /// legible at the waterline where the verdict on flatness was formed. RE-BASED FROM 0.014 — the
    /// ruling asked for the amplitudes to come down by a large factor, not a nudge, and a third is
    /// what that means here. It is the amplitude every number in the shader's header is quoted
    /// against.</para>
    ///
    /// <para>AND IT IS CAPPED TWICE. <see cref="MaxSwellAmplitude"/> is the collision limit
    /// against the basin bed 9 cm below the film; the quad width is what makes the dial mean the
    /// same thing at every scale. A non-finite width or dial gives 0 — a flat film, which is the
    /// previous shipped behaviour and can never be a surface tearing through its own bed.</para>
    /// </summary>
    /// <param name="quadWidthWU">The film quad's largest horizontal extent in world units, off its
    /// renderer bounds.</param>
    /// <param name="dial"><c>[Water] SwellHeight</c>, as a fraction of that width.</param>
    internal static float SwellAmplitude(float quadWidthWU, float dial)
    {
        if (!Finite(quadWidthWU) || !Finite(dial) || quadWidthWU <= 0f || dial <= 0f)
            return 0f;
        return Mathf.Clamp(quadWidthWU * dial, 0f, MaxSwellAmplitude);
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
