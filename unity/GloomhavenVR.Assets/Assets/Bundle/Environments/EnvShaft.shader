// GloomhavenVR — moonlight shafts breaking through the forest canopy.
//
// The one thing that sells a night forest is the LIGHT, so the beams are real
// world-space geometry: each shaft is a pair of crossed tapered blades built by
// BuildEnvironmentRooms.cs, running from a gap in the canopy down into the
// clearing along the moon bearing. Crossed blades (never a camera-facing card —
// the permanent VR constraint) keep the shaft readable from every yaw and are
// identical in both eyes.
//
// UV convention from the builder: u = across the blade (0..1), v = along the
// beam (0 at the canopy gap, 1 where it dies on the forest floor). Vertex alpha
// scales the whole shaft so the builder can dim distant ones.
//
// VERTEX COLOUR RGB IS DATA, NOT A TINT (ModBuild 137). r = fade-in length,
// g = fade-out length, both in v units. The builder derives them from METRES
// and divides by each shaft's own length, so shafts of different lengths share
// one material and still fade over the same distance. This mattered the moment
// the shafts were run up THROUGH the canopy tear (so that the light is seen
// entering where the moon is seen): a fade of a fixed fifth of the length would
// have put the fade-out exactly across the opening and hidden the one thing the
// change exists to show. The channel used to be white and multiplied into the
// tint, so nothing else had to change.
//
// Additive, no depth write, Transparent queue: trunks and canopy occlude the
// beams correctly (ZTest LEqual against the opaque pass), and additive blending
// is order-independent so overlapping shafts never sort wrong.
Shader "GloomhavenVR/EnvShaft"
{
    Properties
    {
        _Tint ("Beam color (a = strength)", Color) = (0.62,0.72,1.0,0.5)
        _Softness ("Cross-section softness", Range(0.5,8)) = 3.0
        _Shimmer ("Shimmer amount", Range(0,1)) = 0.25
        _ShimmerSpeed ("Shimmer speed", Range(0,2)) = 0.22
        // CANOPY SHADOW — baked by BuildEnvironmentRooms.cs (CanopyShadowBake).
        // The defaults below are "no shadow map": a white map decodes to depth
        // 1.0, which is the bake's own "nothing here" sentinel, and _CsFlt.w = 0
        // takes the whole term out. A material that never met the bake is
        // therefore bit-for-bit what it was before this shader grew the feature.
        _CsMap ("Canopy shadow depth (R:G = 16-bit)", 2D) = "white" {}
        _CsOrg ("Light-plane origin (OBJECT space, w = 1/depth span)", Vector) = (0,0,0,0)
        _CsU ("Light-plane axis U (w = 1/extent)", Vector) = (1,0,0,0)
        _CsV ("Light-plane axis V (w = 1/extent)", Vector) = (0,1,0,0)
        _CsDir ("Light travel direction (w = -near depth)", Vector) = (0,-1,0,0)
        _CsFlt ("Penumbra u, penumbra v, depth bias, 1 - minimum visibility", Vector) = (0,0,0,0)
        _CsThrow ("Max throw, 1/release (encoded depth), bite lo, 1/bite span", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0        // seven dependent texture reads in the frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Softness, _Shimmer, _ShimmerSpeed;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // ------------------------------------------------------ CANOPY SHADOW
            // An orthographic depth map of the trees along the moon bearing, baked
            // by BuildEnvironmentRooms.cs (CanopyShadowBake — read the block above
            // that class for WHY it is a depth and not a mask; the short version
            // is that a shaft's top and the boughs around the canopy tear it comes
            // through project to the same texels, and only a depth can tell them
            // apart). R:G is a 16-bit linear depth measured DOWN-LIGHT from a
            // plane just in front of the tallest tree; 1.0 (white) means "no
            // occluder", which is why real depths only ever reach 0.98.
            //
            // Everything is in OBJECT space. The room root carries the runtime's
            // placement yaw and scale, so object space is the only frame the bake
            // survives in — the same reason the baked light rig writes _DirDir and
            // _L0Pos in object space rather than world.
            //
            // These twenty lines are DUPLICATED in EnvGround.shader rather than
            // shared through a .cginc: the two want different tap counts (a blade
            // of lit mist needs a softer edge than a floor does), and a new
            // include is one more asset GUID to keep alive through the bundle for
            // twenty lines. The ENCODING has exactly one source — the builder —
            // and both copies quote it.
            sampler2D _CsMap;
            float4 _CsOrg, _CsU, _CsV, _CsDir, _CsFlt, _CsThrow;

            float3 CsCoord (float3 op)
            {
                float3 r = op - _CsOrg.xyz;
                return float3(dot(r, _CsU.xyz) * _CsU.w + 0.5,
                              dot(r, _CsV.xyz) * _CsV.w + 0.5,
                              (dot(r, _CsDir.xyz) + _CsDir.w) * _CsOrg.w);
            }

            // Each 16-bit layer arrives as two bytes over 255, so a stored depth
            // is (hi*255*256 + lo*255) / 65535. R:G is the NEAREST occluder in
            // the texel and B:A the DEEPEST — one value cannot serve both
            // receivers, see the two-layer note in BuildEnvironmentRooms.cs.
            //
            // MAXIMUM THROW. d is how far DOWN-LIGHT of a stored occluder this
            // fragment lies; d <= 0 means the occluder is behind me and I am lit.
            // A physically exact test would stop there, and in this room it would
            // answer "shadowed" everywhere: the moon sits at 40 deg, so the ray
            // from the clearing floor to the moon spends the next 20-30 m inside
            // the wood, and a wood at night genuinely has no moonlight on its
            // floor. The clearing, the canopy tear and the three shafts are an
            // AUTHORED FICTION and it is the fiction the user approved. So an
            // occluder only casts for _CsThrow.x of depth and then releases
            // smoothly over 1/_CsThrow.y: the trunk a beam passes through shadows
            // it, the roof 25 m up-light does not. The builder derives both
            // numbers — see the MAXIMUM THROW block in BuildEnvironmentRooms.cs.
            float CsThrow (float d)
            {
                return step(0.0, d) * saturate((_CsThrow.x - d) * _CsThrow.y);
            }

            // A blade takes whichever layer casts: R:G is the canopy hanging over
            // the beam, B:A is the trunk the beam runs THROUGH (which is deeper
            // than the roof and would otherwise never be seen). Whichever gives
            // the stronger shadow wins.
            float CsTap (float2 uv, float z)
            {
                float4 e = tex2D(_CsMap, uv);
                float n = (e.r * 65280.0 + e.g * 255.0) * (1.0 / 65535.0);
                float f = (e.b * 65280.0 + e.a * 255.0) * (1.0 / 65535.0);
                // f == 0 is the far layer's "nothing here", and it MUST be gated:
                // z - 0 is a small depth for anything high in the room, so an
                // ungated sentinel would shadow every shaft top.
                return 1.0 - max(CsThrow(z - n), step(0.00002, f) * CsThrow(z - f));
            }

            float CsVisible (float3 sc)
            {
                float z = sc.z - _CsFlt.z;
                float2 f = _CsFlt.xy;
                // PERCENTAGE-CLOSER filtering: compare first, average after. The
                // map is point-sampled on purpose — bilinear interpolation of a
                // DEPTH blends a trunk against the open sky beside it and invents
                // an occluder halfway between the two. Seven taps on a small disc
                // (0.22 m radius in the wood, 4-5 texels) give the soft penumbra a moonbeam
                // in mist actually has; a single tap is a stencil and reads as a
                // bug. World-space and identical in both eyes, so it fuses.
                float v = CsTap(sc.xy, z)
                        + CsTap(sc.xy + float2( 0.866,  0.500) * f, z)
                        + CsTap(sc.xy + float2( 0.000,  1.000) * f, z)
                        + CsTap(sc.xy + float2(-0.866,  0.500) * f, z)
                        + CsTap(sc.xy + float2(-0.866, -0.500) * f, z)
                        + CsTap(sc.xy + float2( 0.000, -1.000) * f, z)
                        + CsTap(sc.xy + float2( 0.866, -0.500) * f, z);
                v *= (1.0 / 7.0);
                // SHAFT BITE (ModBuild 140). The tap average alone is what made
                // the shafts read as SMOOTH on hardware even though the build log
                // said their lower runs were 42% occluded. A fir crown is an
                // alpha-tested sieve — 23.6% of the atlas is over the cutoff — so
                // a beam crossing the crown mass collects a MOTTLE of 0.2-0.5
                // coverage over metres of its length, and a linear average of
                // that is a uniform dimming: exactly "the beam got a bit fainter",
                // never "a bough crosses the beam".
                //
                // So the coverage is remapped before it is used: below _CsThrow.z
                // it is speckle and counts for nothing, above _CsThrow.z + span it
                // is a solid occluder and counts for everything. That is also the
                // physics of light through mist — extinction is exponential in the
                // needle mass crossed, not linear in a sub-texel coverage average
                // — and it is what turns the wash back into dappled light with
                // dark bars. The PENUMBRA is unaffected: the taps still average
                // first, so a shadow EDGE still crosses the ramp smoothly over the
                // tap disc; what the ramp removes is the flat middle.
                float sh = saturate(((1.0 - v) - _CsThrow.z) * _CsThrow.w);
                // Off the edge of the baked map, and anywhere in front of its near
                // plane, everything is lit. CLAMP addressing would otherwise drag
                // the border texels right across the room, and a hard cut-off
                // would draw a line on the floor — so it fades out over ~1/40th
                // of the map's own width, which is comfortably wider than the tap
                // disc, so no tap ever reaches past the border while it counts.
                float2 q = abs(sc.xy - 0.5);
                float edge = saturate((0.5 - max(q.x, q.y)) * 40.0)
                           * step(0.0, sc.z) * step(sc.z, 1.0);
                // _CsFlt.w is 1 - MINIMUM VISIBILITY, and those two really are one
                // number seen from opposite ends: a fragment the map calls fully
                // occluded keeps 1 - _CsFlt.w of its light and no less. The
                // builder now names it from the end that matters (Look.MinVis),
                // because the floor is the safety rail that lets the throw and the
                // bite be aggressive — a shaft may be cut to a hard dark band and
                // must still ARRIVE at the pool it lands in. The first pass had no
                // such rail and extinguished all three beams.
                return 1.0 - _CsFlt.w * sh * edge;
            }

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 col : COLOR; float3 wn : TEXCOORD1; float3 wp : TEXCOORD2; float3 cs : TEXCOORD3; };

            #define SKY_PERIOD 2880.0

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.col = v.color;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                // affine in object space, so interpolating it is exact
                o.cs = CsCoord(v.vertex.xyz);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD);
                // across the blade: soft gaussian core, zero at both rims
                float x = (i.uv.x - 0.5) * 2.0;
                float across = exp(-x * x * _Softness);
                // along the beam: fades in at the canopy gap, dies out before it
                // reaches the ground (a shaft has no visible end, only a pool).
                // Both lengths come from the vertex colour, in v units.
                float v = i.uv.y;
                float along = smoothstep(0.0, i.col.r, v)
                            * smoothstep(1.0, 1.0 - i.col.g, v);
                // slow drifting density — motes and mist crossing the beam
                float sh = 1.0 + _Shimmer * (sin(v * 7.3 + t * _ShimmerSpeed * 6.1)
                                           * sin(v * 2.7 - t * _ShimmerSpeed * 3.3 + x * 1.9));
                // A blade seen face-on is a slab of lit air; seen edge-on it is
                // nothing. Without this a wide blade turns into a visible PANE OF
                // GLASS across the view. The two crossed blades are 90 deg apart,
                // so their sum stays roughly constant as you turn — which is what
                // a real shaft does. View-dependent but smooth and symmetric, so
                // it fuses in stereo (unlike any screen-space pattern).
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float facing = abs(dot(normalize(i.wn), V));
                // USER FINDING, ModBuild 137 (hardware): "Die Lichstrahlen ...
                // clippen durch die Bäume, ich würde hier gerne das die Bäume
                // entsprechende Schatten werfen." A plain MULTIPLY into the same
                // product every other term goes into: the pass stays additive
                // and order-independent, overlapping shafts still cannot sort
                // wrong, and no existing term is touched. The blade simply stops
                // being lit air on the stretches where a trunk or a bough stands
                // between it and the moon.
                float a = across * along * sh * facing * _Tint.a * i.col.a * CsVisible(i.cs);
                return fixed4(_Tint.rgb * a, 1.0);   // col.rgb is data, see header
            }
            ENDCG
        }
    }
    Fallback Off
}
