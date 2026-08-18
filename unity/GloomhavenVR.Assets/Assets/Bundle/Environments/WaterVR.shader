// GloomhavenVR — THE WATER FILM, ANIMATED, WITH NO VIEW DIRECTION IN IT ANYWHERE.
//
// ============================================================================
//  WHY THIS FILE EXISTS
// ============================================================================
//  ModBuild 162 replaced the game's water material with a mod-owned one on
//  GloomhavenVR/Overlay, because Overlay was the only bundled shader that
//  provably sampled no environment. It worked — user, verbatim: "Beide
//  Probleme gelöst, top!" — and it cost the water everything that made it
//  water: "Allerdings: Das Wasser sieht jetzt sehr viel schlechter aus. Das
//  echte Wasser hatte ANimation und co. das will ich auch wieder. Ich will es
//  so nah wie möglich an dem 'echten' Wasser haben - aber eben so dass es in
//  VR funktioniert."
//
//  So this shader has exactly two requirements and they pull against each
//  other only if you get the sparkle from the eye:
//    1. It must MOVE, and move the way the tileset's own water moved.
//    2. NOTHING IN IT MAY DEPEND ON THE VIEW DIRECTION.
//
// ============================================================================
//  REQUIREMENT 2, AND WHY IT IS ABSOLUTE RATHER THAN CAUTIOUS
// ============================================================================
//  The defect five hardware rounds were spent on was, in the user's own words,
//  "die kopf-gebundene Reflektion" that "bewegen sich schnell mit den
//  Kopfbewegungen mit". There is no `unity_SpecCube0`, no `reflect()`, no
//  `texCUBE`/`samplerCUBE`/`UNITY_SAMPLE_TEXCUBE`, no `worldRefl`, no Fresnel
//  and NO VIEW VECTOR OF ANY KIND below. Not even a Blinn-Phong specular:
//  a half-vector highlight slides across the surface as the head moves, which
//  is the reported symptom re-created out of the mod's own shader.
//
//  AND IT IS NOT ONLY ABOUT THAT REPORT. Under MULTIPASS stereo each eye is a
//  separate render pass, so ANY view-dependent term produces a DIFFERENT image
//  per eye. This project has already parked one feature permanently over
//  exactly that (.planning/wall-fade-stereo-rivalry.md: the wall dissolve is
//  one-eyed on the game's masonry shader and could not be fixed without losing
//  the dissolve). A shader with no view vector in it is PER-EYE IDENTICAL BY
//  CONSTRUCTION — not "tuned until the rivalry stopped being reported", which
//  is the weaker guarantee that failed there.
//
//  tests/GloomhavenVR.WireTests/WaterOwnSurfaceVectors.cs sweeps this file's
//  SOURCE for every spelling of an environment sample and fails the build gate
//  on a hit. That lint is the regression guard: the shader is not defended by
//  this comment, it is defended by a test.
//
// ============================================================================
//  REQUIREMENT 1: WHERE THE MOTION COMES FROM
// ============================================================================
//  The target is measured, not imagined. The hardware log's WATER SURFACE
//  census read these off TERRAIN_GEN_WaterPlane_Crypt_Mat on the game's own
//  VFX/Water_Shd_Trans:
//
//      _Normal_Map                 'WaterBump' 512x512
//      _NormalTilings              (0.14, 6.00, -0.12, -0.20)
//      _WaterUVAnimSpeedA          (1.00, 1.00, 0.60, 0.00)
//      _WaterUVAnimSpeedB          (0.50, 1.00, 1.00, 0.00)
//      _WaterNoiseSpeed            (1.00, 1.00, 1.00, 0.00)
//      _Color_Tint                 RGBA(0.195, 0.311, 0.131, 0.737)
//      _DetailOpacityBaseNormalStr (5.00, 5.00, 0.00, 0.00)
//      _Smoothness                 0.754      renderQueue 2900
//      _addSphericalWaves 0   _AddOpaqueDetail 0   _VertexOffsetWaves 0.05
//
//  TWO SCROLLING NORMAL LAYERS AT DIFFERENT TILINGS AND SPEEDS ARE THE WHOLE
//  OF ITS MOTION. That is what this shader reproduces: sample _Normal_Map
//  twice at two tilings, drift each by its own rate against the shared clock,
//  blend them, and shade from the combined normal. Every one of those numbers
//  arrives as a PROPERTY, derived by Core/WaterOwnSurface.cs from the game's
//  own shared material and written by Core/WaterTerrainVR.cs at runtime — this
//  file's defaults are the RESOLVED forms of the measured values, so that a
//  material built without a driver looks like the shipped water rather than
//  like nothing.
//
//  THE SPARKLE COMES OUT OF THE NORMALS, NOT OUT OF THE EYE. A fixed light
//  direction dotted against the animated normal gives highlights that are born
//  on the crests, travel with the waves and break up as the two layers slide
//  past each other. It moves like water because the WATER is moving; it does
//  not move when the head moves, because the head is not in the expression.
//
// ============================================================================
//  MODBUILD 163 SHIPPED THE MEASURED NUMBERS AND THEY LOOKED LIKE THIS
// ============================================================================
//  User, verbatim, on hardware: "Statt langsam, seichte Wellen sehe ich extrem
//  schnelle (und viele) hektische weiße Streifen die auf der FLACHEN Oberfläche
//  vorbeisausen. Das hat mit immersiven 3D Wellen nichts zu tun. Außerdem soll
//  es eher dezent und ruhig sein und nicht so austicken wie aktuell."
//
//  The shader was live (FLOOR CENSUS: 'TERRAIN_Water_Plane' sh='GloomhavenVR/
//  WaterVR' q2900), so that is a LOOK, and it had four compounding causes. All
//  four are fixed below and each fix is commented where it lives:
//
//   1. THE SCROLL WAS IN TEXTURE SPACE, so its speed was multiplied by the
//      tiling. Layer A tiles 0.14 in U — one texture repeat per ~7 quad widths
//      — and scrolled 0.6 texture-repeats/s, i.e. 0.6/0.14 = 4.3 WORLD UNITS
//      per second sideways; layer B ran 5.0 world units/s along V. Across a
//      1 m hex that is the "vorbeisausen". The drift is now applied to the
//      WORLD coordinate and tiled afterwards, so a rate is world units/s and
//      no longer multiplied by a frequency. See THE DRIFT below.
//   2. 43:1 ANISOTROPY WAS THE STREAK. Layer A's authored (0.14, 6.00) stretches
//      the normal map 43x along one axis, which IS a long thin band; moving
//      fast, it is a white streak. The tilings arriving here are now pulled
//      toward each layer's own geometric mean (WaterOwnSurface.TameTilings,
//      43:1 -> ~3:1 at the shipped tame factor), which keeps the flow direction
//      and loses the razor bands.
//   3. THE TWO LAYERS WERE ADDED WITH EQUAL WEIGHT, so the fine streaky layer
//      competed with the coarse one instead of decorating it. They now arrive
//      with amplitude weights inversely proportional to their own frequency
//      (_LayerWeights), so the LARGE slow swell carries the shape and the fine
//      layer is low-amplitude detail on top of it.
//   4. THE HIGHLIGHT WAS A SPARK AND IT REACHED ALPHA. pow(ndl, lerp(4,96,s))
//      at the live _Smoothness 0.219 is an exponent of 24 — a lobe a few
//      degrees wide — and the glint was added into the ALPHA, so a highlight
//      went bright AND opaque at once: a white streak, by construction. The
//      lobe is now broad and dim, it is measured against the UNDISTURBED
//      sheet's own response so still water glints exactly zero, and it does not
//      touch the alpha at all.
//
//  AND THE OTHER HALF OF THE REPORT — "FLACHE Oberfläche", no sense of 3D — is
//  answered by the SWELL in the next section, because it could not be answered
//  here: the three shape fixes above make the shading broad and low-frequency,
//  which is what gentle relief looks like, but a flat sheet lit as though it had
//  waves is still a flat sheet. The follow-up ruling said so in as many words:
//  "Nicht nur 'calm' sondern auch wirklich 3D wellen einbauen."
//
// ============================================================================
//  WHAT IS DELIBERATELY ABSENT: VERTEX DISPLACEMENT
// ============================================================================
// ============================================================================
//  THE SWELL: REAL VERTICAL DISPLACEMENT, AND HOW THE CULLING TRAP IS PAID FOR
// ============================================================================
//  Through ModBuild 163 this shader deliberately displaced nothing and put all
//  the motion in the fragment's normal. That answered the culling hazard by
//  avoiding it, and the hardware verdict on the result was: "Aktuell waren es
//  nur weiße streifen auf einer flachen Oberfläche" and "Nicht nur 'calm'
//  sondern auch wirklich 3D wellen einbauen". Shading alone cannot make a
//  surface have relief — a flat sheet lit as if it had waves is exactly what he
//  is looking at — so the geometry now moves, and the hazard is PAID rather
//  than avoided:
//
//   * CULLING CANNOT SEE A VERTEX PROGRAM. Unity culls a renderer against its
//     MESH's bounds; geometry a vertex program pushes outside them is culled
//     anyway, so a displaced surface vanishes as you approach it and, under
//     MULTIPASS, vanishes in ONE EYE FIRST because the two eye frustums differ.
//     This project has already lost a build to exactly that.
//     WHY A PAD IS EXACT HERE. That earlier loss needed an arc SWEEP because
//     the displacement was a rotation: the swept volume of a rotating limb is
//     not its rest bounds plus a constant. This displacement is PURELY VERTICAL
//     (world +/-Y) and BOUNDED by _SwellAmp, which the driver writes and the
//     dial's own Range caps — so the swept volume is exactly the rest bounds
//     Minkowski-summed with a segment of length 2*_SwellAmp along world Y. The
//     driver pads the mesh bounds it hands us by that amplitude on every local
//     axis (Core/WaterSwellMesh.cs), which CONTAINS that segment whatever the
//     quad's orientation. It is the true swept volume, not a guess-pad.
//   * A FOUR-VERTEX QUAD CANNOT MAKE A WAVE. The film the game places is a flat
//     plane with a handful of vertices, so the driver SUBDIVIDES the mesh it
//     found — midpoint refinement, which is exact for any mesh (every new
//     vertex lies on an existing edge), so the pool's footprint and UVs are
//     unchanged and only the sampling gets finer. The vertex counts are in the
//     census's FILM MESH block; the original mesh is restored on release
//     exactly as the material instances are.
//   * IT IS STILL VIEW-INDEPENDENT. The displacement is a function of WORLD
//     POSITION AND TIME ONLY, so both eyes displace identically and the
//     MultiPass guarantee below is untouched.
//   * AND THE FRAGMENT SHADES THE SAME FUNCTION. The normal comes from the
//     ANALYTIC gradient of the very wave that moved the vertex, evaluated at
//     the same world XZ (the displacement is vertical, so XZ interpolates
//     exactly). Relief lit as though it were flat is the defect, not the fix.
//
//  WHAT ABOUT THE TILESET'S OWN `_addSphericalWaves = 0`? It is still 0 and we
//  still do not read it: the game's own displacement gate stays off because the
//  swell here is not the tileset's wave, it is the VR-side relief the user
//  asked for by name, bounded by a dial ([Water] SwellHeight) and by the 9 cm
//  gap between the film at y=0.0 and the basin bed at y[-0.34..-0.09] that a
//  trough must never punch through.
// ============================================================================
Shader "GloomhavenVR/WaterVR"
{
    Properties
    {
        // ---- the body, shared with GloomhavenVR/Overlay so ONE driver path writes both ----
        // The names are Overlay's (_MainTex/_Color/_Cull/_ZTest/_ZWrite/_SrcBlend/_DstBlend)
        // deliberately: WaterTerrainVR falls back to Overlay when this shader cannot be
        // resolved out of the bundle, and a fallback whose property names differ is a fallback
        // that silently writes nothing. WaterOwnSurface.CommonProperties is the list, and the
        // wire test checks BOTH shaders declare every name on it.
        _MainTex ("Body texture (unused by the driver; 'white' keeps _Color alone)", 2D) = "white" {}
        _Color ("Body tint — the tileset's own _Color_Tint", Color) = (0.195,0.311,0.131,0.737)

        // ---- the ripple, derived from the game's material ----
        _Normal_Map ("Ripple normal map — the tileset's own _Normal_Map", 2D) = "bump" {}
        // RESOLVED tilings, in REPEATS PER WORLD UNIT: layer A in xy, layer B in zw. NOT the raw
        // authored (0.14, 6.00, -0.12, -0.20) — that is 43:1 anisotropy, i.e. a razor band 7 quad
        // widths long and 17 cm wide, which is the "weiße Streifen" of the ModBuild 163 report.
        // WaterOwnSurface.TameTilings pulls each layer toward its OWN geometric mean (so the mean
        // feature size is preserved exactly and only the aspect ratio moves) and then applies
        // [Water] WaveScale. The default below is that resolution of the measured values:
        // A (0.14, 6.00) -> (0.52, 1.61), i.e. 1.92 m x 0.62 m per repeat; B (-0.12, -0.20) ->
        // (-0.14, -0.17), i.e. the 6-7 m swell that carries the shape.
        _NormalTilings ("Resolved tilings: layer A xy, layer B zw (repeats/world unit)", Vector)
            = (0.52,1.61,-0.14,-0.17)
        // AMPLITUDE WEIGHTS, x for layer A and y for layer B, summing to 1. The two layers used to
        // be added at equal weight, which let the fine streaky layer compete with the coarse one;
        // they are now weighted inversely to their own frequency (WaterOwnSurface.LayerWeights), so
        // the coarse swell carries the shape and the fine layer is detail on top of it.
        _LayerWeights ("Layer amplitude weights (A in x, B in y)", Vector) = (0.145,0.855,0,0)
        // RESOLVED drift rates, in WORLD UNITS PER SECOND, NOT the raw authored float4 and no
        // longer in texture space. The census reads _WaterUVAnimSpeedA = (1, 1, 0.6, 0) and the
        // game shader's own reading of .z cannot be recovered from a compiled shader nobody has an
        // install to open; the ONE guess in this feature is made in C#
        // (WaterOwnSurface.ScrollRate), where it is a pure function with a wire vector on it and a
        // log line that prints what it resolved to and from what. The defaults are those rates at
        // the shipped [Water] RippleSpeed.
        _WaterUVAnimSpeedA ("Layer A drift (WORLD UNITS/s in xy)", Vector) = (0.30,0.30,0,0)
        _WaterUVAnimSpeedB ("Layer B drift (WORLD UNITS/s in xy)", Vector) = (0.25,0.50,0,0)
        _NormalStrength ("Ripple strength (from _DetailOpacityBaseNormalStr)", Range(0,8)) = 0.5
        // 0 = sample _Normal_Map. 1 = the analytic fallback, used ONLY when the driver could not
        // read a normal map off the game material at all. It is a property rather than a keyword
        // so the census can read back which one is live.
        _ProcNormal ("Analytic ripple instead of the texture (fallback)", Range(0,1)) = 0

        // ---- THE SWELL: the geometry that moves, in WORLD units ----
        // _SwellAmp is a PEAK displacement, so the surface travels +/- this much about its
        // authored plane. The driver derives it from each film quad's OWN width ([Water]
        // SwellHeight is a fraction of that width), so a diorama at a different scale gets the
        // same-looking wave — 4.5 cm on a 1 m hex at the shipped dial; and the Range caps it well
        // inside the 9 cm the census measured between the film (y 0.0) and the basin bed below it
        // (y[-0.34..-0.09]), so a trough can never punch through the bed. 4.5 cm over the shipped
        // 1.1 m wavelength is a crest slope of 14 degrees, i.e. a SHALLOW wave (1:24) and not a
        // chop. 0 = a flat film and the ORIGINAL mesh, unsubdivided.
        _SwellAmp ("Swell amplitude (world units, peak)", Range(0,0.06)) = 0.045
        _SwellWave ("Swell wavelength (world units)", Float) = 1.1
        _SwellSpeed ("Swell phase speed (world units/s)", Float) = 0.10

        // ---- the light, and it is the only direction in this shader ----
        // SURFACE-LOCAL: x along U, y along V, z along the surface normal. See THE FRAME below.
        _LightDir ("Direction TOWARD the light (surface-local)", Vector) = (0.34,0.22,0.91,0)
        _Shimmer ("Glint strength", Range(0,2)) = 0.10
        _WaveShade ("Wave body shading depth", Range(0,1)) = 0.35
        _Smoothness ("Glint tightness — the tileset's own _Smoothness", Range(0,1)) = 0.754

        // ---- render state, as properties, exactly as Overlay exposes them ----
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4  // LEqual
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0           // Off
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5  // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10 // OneMinusSrcAlpha
    }

    SubShader
    {
        // 2900 is the authored render queue of TERRAIN_GEN_WaterPlane_Crypt_Mat, so the tag's
        // default already puts a driverless material where the water drew. The driver does not
        // rely on it: it writes Material.renderQueue from the game material's own queue, which
        // overrides the tag, so a tileset that authors a different queue keeps it.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True" }

        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // No _MainTex_ST: the driver never sets a tiling or offset on the body texture (it
            // binds none at all), and an unused _ST is a knob that reads as a setting.
            sampler2D _MainTex;
            sampler2D _Normal_Map;
            fixed4 _Color;
            float4 _NormalTilings, _LayerWeights, _WaterUVAnimSpeedA, _WaterUVAnimSpeedB, _LightDir;
            float _NormalStrength, _ProcNormal, _Shimmer, _WaveShade, _Smoothness;
            float _SwellAmp, _SwellWave, _SwellSpeed;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                // xy = the mesh's own UV (for _MainTex). zw = the WORLD XZ of this vertex, which
                // is both the ripple coordinate and the swell's phase coordinate — see EVERY HEX
                // WAS THE SAME HEX.
                float4 uv   : TEXCOORD0;
                fixed4 color : COLOR;
            };

            // ============================================================== THE SWELL ==
            // Two travelling wave trains summed into one height field over WORLD XZ. It is the
            // whole of the geometry's motion and the whole of the surface's large-scale shading,
            // and it is a function of POSITION AND TIME ONLY — no camera, no eye, no view vector,
            // so both MultiPass eyes displace and shade it identically by construction.
            //
            // Returns (h, dh/dx, dh/dz) together, because the fragment needs the DERIVATIVE of the
            // very function the vertex program displaced by: a surface lit as though it were flat
            // is the reported defect ("weiße streifen auf einer FLACHEN Oberfläche"), and the only
            // way to be sure the light agrees with the relief is to differentiate the same
            // expression rather than to author a second one that resembles it.
            //
            // The two directions are incommensurate on purpose (the crests never line up into a
            // corrugation) and the shorter train travels slower, which is the deep-water
            // dispersion c = sqrt(g L / 2pi) reduced to its shape: without it the two components
            // lock into one rigid pattern that slides across the pool instead of rolling.
            //
            // THE SECOND TRAIN IS THE SMALLER ONE, 0.35 of the first, and that ratio is a look
            // decision made against the renders: at 0.55 the two trains are comparable and the
            // surface reads as a lumpy field, which is not what "Wellen" means. At 0.35 the first
            // train's crests are legible as CRESTS rolling across the pool and the second one only
            // stops them from being a corrugation.
            static const float2 GHVR_SWELL_D1 = float2(0.943858, 0.330350);   // normalize(1, 0.35)
            static const float2 GHVR_SWELL_D2 = float2(-0.371391, 0.928477);  // normalize(-0.4, 1)
            #define GHVR_SWELL_RATIO2 0.73   // the second train's wavelength, as a fraction
            #define GHVR_SWELL_AMP2   0.35   // ...and its amplitude

            float3 GhvrSwell (float2 p, float t)
            {
                float k1 = 6.2831853 / max(_SwellWave, 0.05);
                float k2 = k1 / GHVR_SWELL_RATIO2;
                float w1 = k1 * _SwellSpeed;
                float w2 = k2 * _SwellSpeed * sqrt(GHVR_SWELL_RATIO2);
                float ph1 = dot(GHVR_SWELL_D1, p) * k1 - w1 * t;
                float ph2 = dot(GHVR_SWELL_D2, p) * k2 - w2 * t;
                // Normalised so the two trains together peak at exactly _SwellAmp: the amplitude
                // property has to MEAN the peak displacement, because the bounds pad the driver
                // writes is computed from it and a surface that travelled further than its own
                // stated amplitude would be culled at the crests.
                float norm = _SwellAmp / (1.0 + GHVR_SWELL_AMP2);
                float h = norm * (sin(ph1) + GHVR_SWELL_AMP2 * sin(ph2));
                float2 g = norm * (cos(ph1) * k1 * GHVR_SWELL_D1
                                 + GHVR_SWELL_AMP2 * cos(ph2) * k2 * GHVR_SWELL_D2);
                return float3(h, g);
            }

            // THE VERTEX PROGRAM MOVES THE VERTEX, and only along world Y. See the header for why
            // that is the one displacement whose culling can be paid for exactly: the swept volume
            // is the rest bounds plus a segment of length 2*_SwellAmp along Y, and the driver pads
            // the mesh bounds by that amplitude on every local axis before handing it over.
            v2f vert (appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float t = _Time.y + _GhvrTimeOfs;
                wp.y += GhvrSwell(wp.xz, t).x;
                o.pos = UnityWorldToClipPos(wp);
                // ===================================== EVERY HEX WAS THE SAME HEX ==
                // The game places its water as SEPARATE quads — 17 of them in the report's room,
                // each a TERRAIN_Water_Plane instance with its own transform — and each one's UV
                // runs 0..1 across itself. A ripple keyed on that UV alone therefore draws the
                // IDENTICAL pattern on every hex in the pool. The first offscreen render of a 5x5
                // grid (Assets/Editor/PreviewWaterVR.cs) showed exactly that: twenty-five copies
                // of one tile with the seams between them plain to read, which is a surface that
                // looks like a bug — and the standing instruction is that a plainer surface is
                // acceptable where one that looks like a bug is not.
                //
                // The ripple and the swell are therefore both keyed on the vertex's WORLD XZ,
                // which is exact rather than approximate: every tiling below is expressed in
                // repeats per WORLD UNIT, so the pattern is one continuous field the quads are cut
                // out of, and no seam, no per-quad repetition and no integer-lattice banding can
                // exist by construction. (Through ModBuild 163 this was the mesh UV shifted by the
                // quad's own world origin, which only decorrelated axes whose tiling was not a
                // whole number — layer A tiled 6.00 in v and banded down a column of the preview
                // grid. That limit is gone with the coordinate it came from.)
                //
                // The displacement above is VERTICAL, so XZ is untouched by it and the fragment
                // can re-evaluate the swell at exactly the position the vertex was moved from.
                o.uv = float4(v.uv, wp.xz);
                o.color = v.color;
                return o;
            }

            // ================================================== UNPACKING THE BUMP ==
            // The RG-or-AG form, written out rather than taken from UnpackNormal, because the
            // texture is the GAME's and its import settings are not ours to know. DXT5nm stores
            // x in .a with .r pinned at 1; a plain RGB normal map stores x in .r with .a at 1.
            // `r * a` is therefore x under BOTH conventions, and a shader that guessed wrong
            // would produce a ripple that only tilts along one axis — which reads as corduroy,
            // not as water, and would be blamed on the maths rather than on an importer.
            float2 GhvrBumpXY (float4 packed)
            {
                return float2(packed.r * packed.a, packed.g) * 2.0 - 1.0;
            }

            // ================================================== THE FALLBACK RIPPLE ==
            // Used ONLY when the driver could not read _Normal_Map off the game material
            // (_ProcNormal = 1); the census says so in as many words when it happens, because a
            // still sheet and a sheet rippling off the wrong source look identical in a report.
            //
            // The analytic gradient of two crossing wave trains whose crests do not line up:
            //   h = sin(k u + 0.8 sin(k v)) + 0.6 sin(k (2v - u) - 1.1)
            // The cross-modulation in the first term is what stops a pair of plane waves reading
            // as a corrugated sheet — the same reason EnvPuddle's ice relief crosses three
            // incommensurate plane waves rather than using one radial family.
            //
            // EVERY COEFFICIENT INSIDE A sin() IS AN INTEGER MULTIPLE OF k, so this field has
            // period exactly 1 in both axes — which is what makes the frac() on the scroll offset
            // below safe here as well as on the texture path. A non-integer coefficient (1.37 was
            // the first draft's) looks identical in a still frame and puts a visible jump in the
            // surface every time the clock term wraps.
            float2 GhvrProcBumpXY (float2 p)
            {
                const float k = 6.2831853;
                float a = k * p.x + 0.8 * sin(k * p.y);
                float b = k * (2.0 * p.y - p.x) - 1.1;
                float2 g;
                g.x = k * cos(a) - 0.6 * k * cos(b);
                g.y = 0.8 * k * cos(k * p.y) * cos(a) + 1.2 * k * cos(b);
                return g * 0.05;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ONE CLOCK. _Time.y is the shared frame clock and _GhvrTimeOfs is the
                // preview-only offset the batch renderer steps (EnvRoom.shader), which is what
                // lets an offscreen render show this surface at two different instants without a
                // headset.
                float t = _Time.y + _GhvrTimeOfs;

                // TWO RIPPLE LAYERS, at the two resolved tilings, each DRIFTING at its own
                // resolved rate. They are the second-order detail that rides on the swell now,
                // not the whole of the surface.
                //
                // THE DRIFT IS IN WORLD SPACE AND THE TILING IS APPLIED AFTER IT. Through ModBuild
                // 163 this read `uv * tiling + frac(t * rate)`, i.e. the rate was in TEXTURE
                // repeats per second, so the speed on screen was rate/tiling: layer A's 0.6 over a
                // tiling of 0.14 was 4.3 WORLD UNITS per second and layer B's was 5.0, which
                // across a 1 m hex is the "hektische weiße Streifen die vorbeisausen" of the
                // report. `(world + t*rate) * tiling` makes a rate mean world units per second and
                // decouples it from the frequency entirely.
                //
                // THE OFFSET IS STILL WRAPPED, and that is a precision guard rather than a look:
                // it is `frac(t * rate * tiling)`, which is the same expression modulo one whole
                // texture repeat. Both the sampled texture (Repeat) and the analytic field above
                // have period exactly 1, so dropping whole periods is invisible — while an
                // unwrapped product reaches four figures over a long session and starts eating the
                // fractional bits that carry the ripple's detail. The surface would go gradually
                // blocky over an evening and nothing would say why.
                float2 tilA = _NormalTilings.xy, tilB = _NormalTilings.zw;
                float2 uvA = i.uv.zw * tilA + frac(t * _WaterUVAnimSpeedA.xy * tilA);
                float2 uvB = i.uv.zw * tilB + frac(t * _WaterUVAnimSpeedB.xy * tilB);

                // WEIGHTED, NOT SUMMED. Equal weights let the fine layer compete with the coarse
                // one; the weights the driver resolves are inversely proportional to each layer's
                // own frequency and sum to 1, so the coarse layer carries the shape and the fine
                // one decorates it. See WaterOwnSurface.LayerWeights.
                float2 wgt = _LayerWeights.xy;
                float2 bump;
                if (_ProcNormal > 0.5)
                    bump = wgt.x * GhvrProcBumpXY(uvA) + wgt.y * GhvrProcBumpXY(uvB);
                else
                    bump = wgt.x * GhvrBumpXY(tex2D(_Normal_Map, uvA))
                         + wgt.y * GhvrBumpXY(tex2D(_Normal_Map, uvB));

                // THE FRAME. The blended tangent-space normal is used directly as a
                // SURFACE-LOCAL normal: x along U, y along V, z along the surface normal. That
                // is exact here rather than approximate — the game's water film is a flat
                // horizontal quad (hardware FLOOR CENSUS: 'TERRAIN_Water_Plane' y[0.0..0.0]
                // across its whole footprint) — and it is chosen over a world-space TBN because
                // building one needs the MESH's tangents, and an Apparance-instanced quad whose
                // importer settings we do not own may not carry any. A missing tangent is a
                // black surface; a surface-local frame cannot fail that way.
                //
                // The blend is the UDN form (add the xy, keep z at 1, normalise): it is the one
                // that keeps two normal layers from cancelling where their slopes oppose, which
                // an average does, and cancellation is what would flatten the sparkle exactly
                // where the two layers cross — i.e. where water sparkles most.
                //
                // AND THE SWELL'S OWN SLOPE IS THE FIRST TERM. A height field h(x,z) has normal
                // (-dh/dx, -dh/dz, 1) in exactly this frame, so the analytic gradient of the wave
                // that displaced the vertex enters the same expression the ripple does — the light
                // and the geometry cannot disagree, because they are one function.
                float3 sw = GhvrSwell(i.uv.zw, t);
                float3 n = normalize(float3(-sw.yz + bump * _NormalStrength, 1.0));

                // THE ONLY DIRECTION IN THIS SHADER, and it is a UNIFORM: it comes from the
                // scene's own main directional light (or a fixed constant when the scene has
                // none — the driver logs which). It does not depend on where the camera is, so
                // this dot product is identical in both eyes' passes by construction.
                float3 L = normalize(_LightDir.xyz + float3(0, 0, 1e-4));
                float ndl = saturate(dot(n, L));

                // THE UNDISTURBED SHEET IS THE ZERO POINT, and this is what keeps a calm surface
                // calm. A flat film has n = (0,0,1), so its response is exactly L.z — 0.91 for the
                // shipped light direction. Through ModBuild 163 the body was
                // lerp(1-_WaveShade, 1+_WaveShade, ndl), which at ndl = 0.91 is 1.148: the pool
                // sat 15% ABOVE the authored tint before any wave had moved, and the standing
                // invariant of this module is that the replacement may never be brighter than what
                // the tileset authored. Referencing both the shading and the glint to ndl0 makes
                // still water EXACTLY the authored tint and lets only the waves move it.
                float ndl0 = saturate(L.z);

                // THE BODY. Crests lean into the light and troughs away from it, so the sheet has
                // moving structure even where nothing glints. The soft knee (x/(1+|x|)) keeps the
                // response inside (-1,1) without a clamp: a clamp would flatten the deepest
                // troughs into plateaus of one flat colour with a visible edge, which reads as a
                // stain rather than as water.
                float rel = (ndl - ndl0) / max(1.0 - ndl0, 0.05);
                rel = rel / (1.0 + abs(rel));
                float3 body = tex2D(_MainTex, i.uv.xy).rgb * _Color.rgb * i.color.rgb
                            * (1.0 + _WaveShade * rel);

                // THE GLINT, BROAD AND DIM. pow() of the same view-independent dot, widened by the
                // tileset's own _Smoothness. THE EXPONENT RANGE IS THE FIX: it was
                // lerp(4, 96, _Smoothness), which at the 0.219 the driver caps to on hardware is
                // 24 — a lobe a few degrees wide, i.e. a spark. lerp(1, 8, ...) is 2.7 there, a
                // wide sheen that cannot resolve into a point. Subtracting the flat sheet's own
                // response makes still water glint EXACTLY zero, so the broad lobe cannot become a
                // pedestal over the whole pool; only a crest tilted toward the light adds light.
                float e = lerp(1.0, 8.0, saturate(_Smoothness));
                float glint = _Shimmer * max(pow(ndl, e) - pow(ndl0, e), 0.0);

                // THE GLINT DOES NOT TOUCH THE ALPHA. It used to be added into it, so a highlight
                // went bright AND opaque in the same pixel — which is a white streak by
                // construction, and is half of what the report was looking at. The film's opacity
                // is now the tileset's authored alpha and nothing else.
                float alpha = saturate(_Color.a * i.color.a);
                return fixed4(body + glint.xxx, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
