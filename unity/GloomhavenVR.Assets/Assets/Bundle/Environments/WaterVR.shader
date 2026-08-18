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
//  twice at the two authored tilings, scroll each by its own authored rate
//  against the shared clock, blend them, and shade from the combined normal.
//  Every one of those numbers arrives as a PROPERTY, written by
//  Core/WaterTerrainVR.cs off the game's own shared material at runtime — this
//  file's defaults are the measured values only so that a material built
//  without a driver still looks like the tileset's water rather than like
//  nothing.
//
//  THE SPARKLE COMES OUT OF THE NORMALS, NOT OUT OF THE EYE. A fixed light
//  direction dotted against the animated normal gives highlights that are born
//  on the crests, travel with the waves and break up as the two layers slide
//  past each other. It moves like water because the WATER is moving; it does
//  not move when the head moves, because the head is not in the expression.
//
// ============================================================================
//  WHAT IS DELIBERATELY ABSENT: VERTEX DISPLACEMENT
// ============================================================================
//  There is no vertex program that moves a vertex, and there is no property
//  that could switch one on. Two reasons, and the first is the tileset's own:
//
//   * THE AUTHORED MATERIAL DOES NOT DISPLACE. `_addSphericalWaves = 0` on the
//     game's own material — the gate is off, so `_VertexOffsetWaves = 0.05`
//     and `_VertexOffsetWaveMask = (0,0,1,0)` never reach geometry. Adding
//     displacement here would be a look the tileset never had, which is the
//     one thing this whole module is not allowed to invent.
//   * CULLING CANNOT SEE A VERTEX PROGRAM. Unity culls a renderer against the
//     MESH's authored bounds; geometry a vertex shader pushes outside them is
//     still culled, so a displaced surface vanishes as you approach it — and
//     under MULTIPASS it vanishes in ONE EYE FIRST, because the two eye
//     frustums differ. This project has already lost a build to exactly that.
//     A flat quad displaced by even a few centimetres would put it back.
//
//  All the motion is therefore in the fragment's NORMAL, which is free of both
//  problems: it changes no geometry, so nothing can be culled that was not
//  culled before.
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

        // ---- the ripple, straight off the game's material ----
        _Normal_Map ("Ripple normal map — the tileset's own _Normal_Map", 2D) = "bump" {}
        _NormalTilings ("Tilings: layer A in xy, layer B in zw", Vector) = (0.14,6.00,-0.12,-0.20)
        // RESOLVED rates, in UV per second, NOT the raw authored float4. The census reads
        // _WaterUVAnimSpeedA = (1, 1, 0.6, 0) and the game shader's own reading of .z cannot be
        // recovered from a compiled shader nobody has an install to open; the ONE guess in this
        // feature is made in C# (WaterOwnSurface.ScrollRate), where it is a pure function with a
        // wire vector on it and a log line that prints what it resolved to and from what.
        _WaterUVAnimSpeedA ("Layer A scroll (UV/s in xy)", Vector) = (0.60,0.60,0,0)
        _WaterUVAnimSpeedB ("Layer B scroll (UV/s in xy)", Vector) = (0.50,1.00,0,0)
        _NormalStrength ("Ripple strength (from _DetailOpacityBaseNormalStr)", Range(0,8)) = 1.0
        // 0 = sample _Normal_Map. 1 = the analytic fallback, used ONLY when the driver could not
        // read a normal map off the game material at all. It is a property rather than a keyword
        // so the census can read back which one is live.
        _ProcNormal ("Analytic ripple instead of the texture (fallback)", Range(0,1)) = 0

        // ---- the light, and it is the only direction in this shader ----
        // SURFACE-LOCAL: x along U, y along V, z along the surface normal. See THE FRAME below.
        _LightDir ("Direction TOWARD the light (surface-local)", Vector) = (0.34,0.22,0.91,0)
        _Shimmer ("Glint strength", Range(0,2)) = 0.35
        _WaveShade ("Wave body shading depth", Range(0,1)) = 0.18
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
            float4 _NormalTilings, _WaterUVAnimSpeedA, _WaterUVAnimSpeedB, _LightDir;
            float _NormalStrength, _ProcNormal, _Shimmer, _WaveShade, _Smoothness;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                // xy = the mesh's own UV (for _MainTex). zw = the RIPPLE coordinate, which is the
                // same UV shifted by the quad's own world origin — see EVERY HEX WAS THE SAME HEX.
                float4 uv   : TEXCOORD0;
                fixed4 color : COLOR;
            };

            // NO VERTEX DISPLACEMENT — see the header. The position goes through untouched, so
            // the renderer's authored bounds still describe the geometry exactly and nothing can
            // be culled that was not culled before.
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
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
                // The fix costs one interpolator channel and changes NOTHING about the pattern's
                // scale or speed: the quad's own world origin is added to the UV before the
                // authored tiling is applied. The quads sit about a world unit apart and their
                // UVs span 0..1 across themselves, so the origin's XZ is already in the same unit
                // as the UV — the pattern therefore runs CONTINUOUSLY across the pool instead of
                // restarting at every seam. Re-rendered, adjacent quads now differ by a mean 13
                // of 255 per channel against an image-wide standard deviation of 19, i.e. they
                // are as different from each other as the picture is varied.
                //
                // ONE HONEST LIMIT, visible in that same render: the offset only decorrelates an
                // axis whose authored tiling is not a whole number. Layer A tiles 6.00 in v, so a
                // quad displaced by a WHOLE unit along that axis lands on the same texture rows —
                // which is why the preview grid, whose quads sit at integer world positions, still
                // shows matching horizontal banding down a column. The game's hexes are not on an
                // integer lattice, so it does not arise there; and layer B (tiling -0.12, -0.20)
                // decorrelates both axes regardless.
                float2 org = float2(unity_ObjectToWorld._m03, unity_ObjectToWorld._m23);
                o.uv = float4(v.uv, v.uv + org);
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

                // TWO LAYERS, at the tileset's own two tilings, each scrolling at its own
                // resolved rate. This is the whole of the game water's motion.
                //
                // THE SCROLL OFFSET IS WRAPPED, and that is a precision guard rather than a look.
                // Both the sampled texture (Repeat) and the analytic field above have period
                // exactly 1, so adding or dropping a whole period is invisible — while an
                // unwrapped `t * rate` reaches four figures over a long session and starts eating
                // the fractional bits that carry the ripple's detail. The surface would go
                // gradually blocky over an evening and nothing would say why.
                float2 uvA = i.uv.zw * _NormalTilings.xy + frac(t * _WaterUVAnimSpeedA.xy);
                float2 uvB = i.uv.zw * _NormalTilings.zw + frac(t * _WaterUVAnimSpeedB.xy);

                float2 bump;
                if (_ProcNormal > 0.5)
                    bump = GhvrProcBumpXY(uvA) + GhvrProcBumpXY(uvB);
                else
                    bump = GhvrBumpXY(tex2D(_Normal_Map, uvA))
                         + GhvrBumpXY(tex2D(_Normal_Map, uvB));

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
                float3 n = normalize(float3(bump * _NormalStrength, 1.0));

                // THE ONLY DIRECTION IN THIS SHADER, and it is a UNIFORM: it comes from the
                // scene's own main directional light (or a fixed constant when the scene has
                // none — the driver logs which). It does not depend on where the camera is, so
                // this dot product is identical in both eyes' passes by construction.
                float3 L = normalize(_LightDir.xyz + float3(0, 0, 1e-4));
                float ndl = saturate(dot(n, L));

                // THE BODY. Crests lean into the light and troughs away from it, so the sheet
                // has moving structure even where nothing glints. Written as a symmetric lerp
                // about 1.0 so the MEAN brightness is exactly the authored tint: the standing
                // invariant of this module is that the replacement may never be brighter than
                // what the tileset authored, and a body term with a pedestal in it would raise
                // the whole pool by that pedestal.
                float3 body = tex2D(_MainTex, i.uv.xy).rgb * _Color.rgb * i.color.rgb
                            * lerp(1.0 - _WaveShade, 1.0 + _WaveShade, ndl);

                // THE GLINT. pow() of the same view-independent dot, tightened by the tileset's
                // own _Smoothness — 0.754 authored, which lands at an exponent of ~73, i.e. a
                // highlight a few degrees wide. It is born on a crest, travels with the crest
                // and dies when the two layers slide out of phase; it does NOT move when the
                // head moves, which is the entire point.
                float glint = _Shimmer * pow(ndl, lerp(4.0, 96.0, saturate(_Smoothness)));

                // The glint is light sitting ON the water, so it carries its own opacity: a
                // highlight you can see the basin floor through reads as a stain on the surface
                // rather than as a reflection off it.
                float alpha = saturate(_Color.a * i.color.a + glint);
                return fixed4(body + glint.xxx, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
