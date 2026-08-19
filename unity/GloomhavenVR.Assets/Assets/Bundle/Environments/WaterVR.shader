// GloomhavenVR — THE WATER FILM, ANIMATED, WITH NO VIEW DIRECTION IN IT ANYWHERE.
//
// The whole program lives in WaterVR.cginc and is included TWICE below. Read that file for the
// maths, the motion model and the view-independence guarantee; this one is the property table, the
// render state, and the two SubShaders.
//
// ============================================================================
//  WHY THERE ARE TWO SUBSHADERS
// ============================================================================
//  ModBuild 164 shipped a displacement shader and NO GEOMETRY FOR IT TO MOVE. The hardware census
//  read the film's mesh back as:
//
//      FILM MESH: 'TERRAIN_Water_Plane' 33 verts / 0 tris,
//                 local bounds centre (0,0.02,0) size (1.73,0,1.998)
//
//  33 vertices spread over a 1.73 x 2.0 m hex is a sample every ~0.4 m, and the swell it was asked
//  to carry had a 1.1 m wavelength — under three samples per wave, which is barely above Nyquist
//  and produces a coarse, lattice-locked figure rather than a crest. (The `0 tris` on the same line
//  is the census reading `Mesh.triangles`, which comes back empty on a mesh imported without
//  Read/Write — the game's are. That is also why the CPU subdivision path this build removes could
//  never have worked on these meshes: it needs the index buffer, and there is not one to read.)
//
//  So the geometry now comes from the GPU, which needs no CPU access to anything:
//
//    * SubShader 1 (LOD 300) runs a HULL and DOMAIN stage and subdivides each authored triangle
//      _TessFactor ways along every edge before displacing it. At the shipped factor of 4 a 0.47 m
//      edge becomes 0.12 m — nineteen samples across the longest swell component and five across
//      the shortest — for sixteen times the triangles of the authored mesh and not one byte of new
//      vertex data.
//    * SubShader 2 (LOD 100) is the same water with the tessellation stages removed, for a device
//      that cannot run them. It still displaces; it just samples the wave at whatever density the
//      game's own mesh has, which is the ModBuild 164 look. The driver logs which one the device
//      resolved to (SystemInfo.graphicsShaderLevel), because "the water looks flat again" must be
//      answerable from the log rather than from a photograph.
//
//  The offscreen harness forces each in turn through Shader.maximumLOD, so the contact sheet
//  contains a flat-versus-tessellated A/B that PROVES the tessellator added geometry. That is the
//  instrument ModBuild 164 did not have, and its absence is exactly why a mesh swap that never
//  happened stayed invisible for a whole round.
//
// ============================================================================
//  THE TESSELLATION FACTOR IS FIXED, AND THAT IS A STEREO REQUIREMENT
// ============================================================================
//  The textbook tessellation shader scales its factor with the distance to the camera. That is a
//  view dependency in the GEOMETRY: under MULTIPASS the two eyes are separate passes ~6.4 cm apart,
//  so they would subdivide the same patch to different densities and sample different crest
//  heights — stereo rivalry along every silhouette, which is the class of defect this whole module
//  exists to remove. _TessFactor is a uniform and the patch-constant function reads nothing else.
//
// ============================================================================
//  WHERE THE LOOK COMES FROM
// ============================================================================
//  Every number below is either measured off the game's own material by the hardware census, or
//  derived from a measured one by a pure function in Core/WaterOwnSurface.cs that the wire tests
//  pin. The census read these off TERRAIN_GEN_WaterPlane_Crypt_Mat on VFX/Water_Shd_Trans:
//
//      _Normal_Map 'WaterBump' 512x512      _NormalTilings (0.14, 6.00, -0.12, -0.20)
//      _WaterUVAnimSpeedA (1, 1, 0.6, 0)    _WaterUVAnimSpeedB (0.5, 1, 1, 0)
//      _WaterNoiseSpeed (1, 1, 1, 0)        _Color_Tint RGBA(0.195, 0.311, 0.131, 0.737)
//      _DetailOpacityBaseNormalStr (5,5,0,0)  _Smoothness 0.754   renderQueue 2900
//
//  This file's defaults are the RESOLVED forms of those values at the shipped dials, so a material
//  built without a driver looks like the shipped water rather than like nothing.
Shader "GloomhavenVR/WaterVR"
{
    Properties
    {
        // ---- the body, shared with GloomhavenVR/Overlay so ONE driver path writes both ----
        // The names are Overlay's (_MainTex/_Color/_Cull/_ZTest/_ZWrite/_SrcBlend/_DstBlend)
        // deliberately: WaterTerrainVR falls back to Overlay when this shader cannot be resolved out
        // of the bundle, and a fallback whose property names differ is a fallback that silently
        // writes nothing. WaterOwnSurface.CommonProperties is the list, and the wire test checks
        // BOTH shaders declare every name on it.
        _MainTex ("Body texture (unused by the driver; 'white' keeps _Color alone)", 2D) = "white" {}
        _Color ("Body tint — the tileset's own _Color_Tint", Color) = (0.195,0.311,0.131,0.737)

        // ---- the ripple, derived from the game's material ----
        _Normal_Map ("Ripple normal map — the tileset's own _Normal_Map", 2D) = "bump" {}
        // RESOLVED tilings, in REPEATS PER WORLD UNIT: layer A in xy, layer B in zw. NOT the raw
        // authored (0.14, 6.00, -0.12, -0.20) — that is 43:1 anisotropy, i.e. a razor band 7 quad
        // widths long and 17 cm wide, which is the "weiße Streifen" of the ModBuild 163 report.
        // WaterOwnSurface.TameTilings pulls each layer toward its OWN geometric mean (so the mean
        // feature size is preserved exactly and only the aspect ratio moves) and then applies
        // [Water] WaveScale. A (0.14, 6.00) -> (0.52, 1.61), i.e. 1.92 m x 0.62 m per repeat;
        // B (-0.12, -0.20) -> (-0.14, -0.17), i.e. the 6-7 m swell that carries the shape.
        _NormalTilings ("Resolved tilings: layer A xy, layer B zw (repeats/world unit)", Vector)
            = (0.52,1.61,-0.14,-0.17)
        // AMPLITUDE WEIGHTS, x for layer A and y for layer B, summing to 1 — inversely proportional
        // to each layer's own frequency (WaterOwnSurface.LayerWeights), so the coarse layer carries
        // the shape and the fine one is detail on top of it.
        _LayerWeights ("Layer amplitude weights (A in x, B in y)", Vector) = (0.145,0.855,0,0)
        // RESOLVED drift rates in WORLD UNITS PER SECOND — and since ModBuild 165 that is a PEAK
        // SWAY SPEED rather than a translation rate, because standing water has no net drift. See
        // GhvrRippleOffset. These defaults are the resolution of the measured values at the shipped
        // [Water] RippleSpeed 0.12: A 0.6 x 0.12 = 0.072, B (0.5, 1.0) x 0.12 = (0.06, 0.12).
        _WaterUVAnimSpeedA ("Layer A rate (peak WORLD UNITS/s in xy)", Vector) = (0.072,0.072,0,0)
        _WaterUVAnimSpeedB ("Layer B rate (peak WORLD UNITS/s in xy)", Vector) = (0.060,0.120,0,0)
        // x = layer A's sway period in SECONDS, y = layer B's, z = the share of the rate that is
        // still a genuine one-way drift. 13 and 17 are deliberately not a ratio of small whole
        // numbers: two layers that reversed together would read as the whole pool twitching.
        _RippleSway ("Ripple sway: A period s (x), B period s (y), drift share (z)", Vector)
            = (13,17,0.25,0)
        _NormalStrength ("Ripple strength (from _DetailOpacityBaseNormalStr)", Range(0,8)) = 0.5
        // 0 = sample _Normal_Map. 1 = the analytic fallback, used ONLY when the driver could not
        // read a normal map off the game material at all. It is a property rather than a keyword so
        // the census can read back which one is live.
        _ProcNormal ("Analytic ripple instead of the texture (fallback)", Range(0,1)) = 0

        // ---- THE SWELL: the geometry that moves, in WORLD units ----
        // _SwellAmp is a PEAK displacement, so the surface travels +/- this much about its authored
        // plane. The driver derives it from each film quad's OWN width ([Water] SwellHeight is a
        // fraction of that width), so a diorama at a different scale gets the same-looking wave —
        // 3.6 cm on the report's 2.6 m-wide film at the shipped dial. The Range caps it well inside
        // the 9 cm the census measured between the film (y 0.0) and the basin bed below it
        // (y[-0.34..-0.09]), so a trough can never punch through the bed, and the SAME number is
        // what the driver pads Renderer.localBounds by. 0 = a flat film.
        _SwellAmp ("Swell amplitude (world units, peak)", Range(0,0.06)) = 0.036
        // The LONGEST component's wavelength; the other three are irrational fractions of it
        // (1/phi, sqrt(2)-1, 2-sqrt(3)). 2.4 m is longer than either tile-lattice vector
        // (1.73 x 1.998 m), so the primary swell spans more than one hex and cannot be read as a
        // per-tile figure however the tiles are laid out.
        _SwellWave ("Swell wavelength of the longest component (world units)", Float) = 2.4
        // THE ONLY NET TRANSLATION LEFT IN THE GEOMETRY, in world units per second. Standing water
        // has none; a trace keeps the standing components' nodes from being nailed to fixed world
        // positions. 2 cm/s is 1.2 m a minute across a 5 m pool.
        _SwellSpeed ("Swell trace drift (world units/s)", Float) = 0.02
        // How long the LONGEST component takes to rise and fall once, in seconds. The shorter
        // components scale as sqrt(their wavelength ratio), so at 9 s they run 9.0 / 7.1 / 5.8 /
        // 4.7 s. This is the number "es fließt noch viel zu schnell" is about and the census prints
        // all four.
        _SwellPeriod ("Swell period of the longest component (seconds)", Float) = 9
        // How far the QUIET parts of the pool drop below full amplitude, 0 = the same everywhere and
        // 1 = dead still in the troughs of the modulation. This is the term that answers "es soll
        // sich nicht auf jeden tile exakt gleichen was passiert" — see GhvrSwellCalm.
        _SwellCalm ("Large-scale calm depth (0 = uniform, 1 = some of the pool still)", Range(0,1))
            = 0.75

        // ---- the geometry the swell is sampled on ----
        // Edges per authored edge. FIXED, never distance-scaled: see the header. 4 turns the
        // film's ~0.47 m authored edge into 0.12 m for SIXTEEN times the triangles — ~510 per film
        // and ~8700 for the report's pool of 17, which is one small prop's worth. The Range's
        // ceiling is WaterOwnSurface.MaxTessellationFactor and the driver clamps to the same number.
        _TessFactor ("Tessellation factor (fixed; NEVER distance-scaled)", Range(1,8)) = 4

        // ---- the light, and it is the only direction in this shader ----
        // SURFACE-LOCAL: x along U, y along V, z along the surface normal.
        _LightDir ("Direction TOWARD the light (surface-local)", Vector) = (0.34,0.22,0.91,0)
        _Shimmer ("Glint strength", Range(0,2)) = 0.05
        // HOW DEEPLY THE WAVES SHADE THE BODY. Nothing writes this at runtime, so the number here
        // IS the shipped one. It stays at ModBuild 164's 0.35 deliberately: the amplitude below
        // already dropped by 40% and the speed by five, and muting the light as well would be
        // calming the same surface three times over — the ruling is that the water be quiet, not
        // that its relief be hidden.
        _WaveShade ("Wave body shading depth", Range(0,1)) = 0.35
        _Smoothness ("Glint tightness — the tileset's own _Smoothness", Range(0,1)) = 0.754

        // ---- render state, as properties, exactly as Overlay exposes them ----
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4  // LEqual
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0           // Off
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5  // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10 // OneMinusSrcAlpha
    }

    // ========================================================================================
    //  SUBSHADER 1 — TESSELLATED. The shipped path on any d3d11 GPU.
    // ========================================================================================
    SubShader
    {
        // 2900 is the authored render queue of TERRAIN_GEN_WaterPlane_Crypt_Mat, so the tag's
        // default already puts a driverless material where the water drew. The driver does not rely
        // on it: it writes Material.renderQueue from the game material's own queue, which overrides
        // the tag, so a tileset that authors a different queue keeps it.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True" }
        LOD 300

        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 4.6
            #pragma vertex GhvrTessVert
            #pragma hull GhvrHull
            #pragma domain GhvrDomain
            #pragma fragment GhvrWaterFrag
            #include "WaterVR.cginc"

            // The control point handed between the three stages. It carries the OBJECT-space
            // position rather than the world one, because the domain stage interpolates it and then
            // runs the ordinary vertex program on the result — so there is exactly one copy of the
            // displacement and the LOD 100 SubShader below runs the same function on the same input.
            struct GhvrPatch
            {
                float4 vertex : INTERNALTESSPOS;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct GhvrPatchConst
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            GhvrPatch GhvrTessVert (appdata v)
            {
                GhvrPatch o;
                o.vertex = v.vertex;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // THE FACTOR READS NOTHING BUT A UNIFORM. No camera position, no patch centre distance,
            // no screen-space edge length — every one of those is a view dependency in the geometry
            // and therefore a different mesh in each MultiPass eye. Clamped here as well as in the
            // driver because a material authored by hand is a second way in.
            GhvrPatchConst GhvrPatchConstant (InputPatch<GhvrPatch, 3> patch)
            {
                GhvrPatchConst o;
                float f = clamp(_TessFactor, 1.0, 8.0);
                o.edge[0] = f;
                o.edge[1] = f;
                o.edge[2] = f;
                o.inside = f;
                return o;
            }

            [UNITY_domain("tri")]
            [UNITY_partitioning("integer")]
            [UNITY_outputtopology("triangle_cw")]
            [UNITY_patchconstantfunc("GhvrPatchConstant")]
            [UNITY_outputcontrolpoints(3)]
            GhvrPatch GhvrHull (InputPatch<GhvrPatch, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            // INTEGER partitioning, not fractional: the factor is a constant, so there is nothing
            // for fractional partitioning to smooth between — and its extra sliver triangles would
            // be geometry that exists only to be degenerate on a headset that is already GPU-bound.
            [UNITY_domain("tri")]
            v2f GhvrDomain (GhvrPatchConst tess, const OutputPatch<GhvrPatch, 3> patch,
                            float3 bary : SV_DomainLocation)
            {
                appdata v;
                v.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y
                         + patch[2].vertex * bary.z;
                v.uv = patch[0].uv * bary.x + patch[1].uv * bary.y + patch[2].uv * bary.z;
                v.color = patch[0].color * bary.x + patch[1].color * bary.y
                        + patch[2].color * bary.z;
                return GhvrWaterVert(v);
            }
            ENDCG
        }
    }

    // ========================================================================================
    //  SUBSHADER 2 — NO TESSELLATION. Only reached on a device without shader model 4.6.
    // ========================================================================================
    //  It is NOT a "flat water" fallback: it runs the identical displacement and the identical
    //  shading, and the only thing it lacks is the extra sampling density. On the game's 33-vertex
    //  hex that is the ModBuild 164 look — relief that is present in the maths and barely legible on
    //  screen — so the driver says in the log which SubShader the device resolved to, and a report
    //  of "the water is flat again" is answerable from that line instead of from a photograph.
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex GhvrWaterVert
            #pragma fragment GhvrWaterFrag
            #include "WaterVR.cginc"
            ENDCG
        }
    }
    Fallback Off
}
