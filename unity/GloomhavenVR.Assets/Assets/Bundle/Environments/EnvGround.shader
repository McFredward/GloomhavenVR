// GloomhavenVR — swamp-ground shader: EnvRoom's baked-light model plus a
// two-texture-set blend driven by vertex color:
//   vcol.a   = blend factor between set 1 (mud) and set 2 (leaf litter),
//              painted by the heightfield generator (wet hollows = mud),
//   vcol.rgb = large-scale tint — carries the radial fade into darkness that
//              hides the 30 m ground rim (permanent rule: the edge of the
//              ground must vanish in night + fog, never show a horizon line).
// Same object-space light rig as EnvRoom (see there for the model).
Shader "GloomhavenVR/EnvGround"
{
    Properties
    {
        _MainTex ("Albedo A (mud)", 2D) = "white" {}
        _BumpMap ("Normal A", 2D) = "bump" {}
        _MainTex2 ("Albedo B (leaves)", 2D) = "white" {}
        _BumpMap2 ("Normal B", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        _AmbUp ("Hemisphere ambient - sky", Color) = (0.05,0.06,0.08,1)
        _AmbDown ("Hemisphere ambient - ground", Color) = (0.015,0.015,0.015,1)
        _DirDir ("Directional dir (OBJECT space, toward light)", Vector) = (0,1,0,0)
        _DirCol ("Directional color", Color) = (0,0,0,1)
        _L0Pos ("Light0 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L0Col ("Light0 color (a=flicker)", Color) = (0,0,0,0)
        _L1Pos ("Light1 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L1Col ("Light1 color (a=flicker)", Color) = (0,0,0,0)
        _L2Pos ("Light2 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L2Col ("Light2 color (a=flicker)", Color) = (0,0,0,0)
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        // How much of the directional (moon) term the GROUND takes. 1 = as every
        // other surface, which is what it was until ModBuild 136.
        // USER FINDING, ModBuild 135 (hardware): "Pass nochmal die
        // Lichtverhältnisse im Wald auf dem Boden an - der erscheint viel zu
        // hell bei den Lichtverältnissen. Er soll eher leicht angestrahlt werden
        // von Mond." The forest floor is very nearly horizontal, so N.L against
        // a moon at 40 deg altitude is ~0.64 EVERYWHERE — the one surface in the
        // room that is uniformly and fully lit, which is exactly why it read as
        // a lit floor instead of a floor a little moonlight falls on. This is
        // the knob for that, and it is on the GROUND only: turning the moon
        // itself down would flatten the trunk rim, which is the contrast recipe
        // ModBuild 134 spent a round building.
        _DirScale ("Directional (moon) response", Range(0,2)) = 1
        // CANOPY SHADOW — baked by BuildEnvironmentRooms.cs (CanopyShadowBake).
        // It multiplies the DIRECTIONAL term and nothing else, so it can only
        // ever subtract moonlight: the hemisphere ambient and all three point
        // lights are separate addends and are not in its reach. That matters
        // here more than anywhere, because the levels this shader carries were
        // hand-tuned to a user ruling (see _DirScale) and the landing pool the
        // board is read by is a POINT light. Nothing in this room gets brighter.
        //
        // Defaults are "no shadow map": a white map decodes to depth 1.0, which
        // is the bake's own "nothing here" sentinel, and _CsFlt.w = 0 takes the
        // whole term out.
        _CsMap ("Canopy shadow depth (R:G = 16-bit)", 2D) = "white" {}
        _CsOrg ("Light-plane origin (OBJECT space, w = 1/depth span)", Vector) = (0,0,0,0)
        _CsU ("Light-plane axis U (w = 1/extent)", Vector) = (1,0,0,0)
        _CsV ("Light-plane axis V (w = 1/extent)", Vector) = (0,1,0,0)
        _CsDir ("Light travel direction (w = -near depth)", Vector) = (0,-1,0,0)
        _CsFlt ("Penumbra u, penumbra v, depth bias, strength", Vector) = (0,0,0,0)
        _CsThrow ("Max throw, 1/release (encoded depth units)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0        // four albedo/normal reads plus six shadow taps
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            sampler2D _MainTex2; float4 _MainTex2_ST;
            sampler2D _BumpMap2;
            float _BumpScale, _PtHard, _DirScale;
            fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // ------------------------------------------------------ CANOPY SHADOW
            // An orthographic depth map of the trees along the moon bearing, baked
            // by BuildEnvironmentRooms.cs (CanopyShadowBake — read the block above
            // that class for WHY it stores a depth and not an occlusion mask).
            // R:G is a 16-bit linear depth measured DOWN-LIGHT from a plane just
            // in front of the tallest tree; 1.0 (white) means "no occluder", which
            // is why real depths only ever reach 0.98.
            //
            // Everything is in OBJECT space — the frame the ground mesh is built
            // in and the only one the runtime's placement yaw and scale cannot
            // move under it, exactly like _DirDir and _L0Pos above.
            //
            // Duplicated from EnvShaft.shader on purpose (see the note there):
            // the floor takes SIX taps where a blade of mist takes seven, because
            // the floor covers far more of the view and its shadow edge is
            // already softened by the normal map and the vertex fade.
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

            // The floor reads the FAR layer alone, and for a floor that is exact
            // rather than an approximation: the floor lies below everything in
            // its texel, so the deepest occluder is always the nearest one
            // up-light of it. Reading R:G here would store the canopy 20-30 m
            // overhead, put it past the throw, and report the floor lit while the
            // trunk 4 m away casts nothing — which is what the first bake did.
            float CsTap (float2 uv, float z)
            {
                float2 e = tex2D(_CsMap, uv).ba;
                float f = (e.x * 65280.0 + e.y * 255.0) * (1.0 / 65535.0);
                // 0 is "nothing here"; see the note in EnvShaft.shader
                return 1.0 - step(0.00002, f) * CsThrow(z - f);
            }

            float CsVisible (float3 sc)
            {
                float z = sc.z - _CsFlt.z;
                float2 f = _CsFlt.xy;
                // PERCENTAGE-CLOSER filtering: compare first, average after. The
                // map is point-sampled on purpose — bilinear interpolation of a
                // DEPTH blends a trunk against the open sky beside it and invents
                // an occluder halfway between the two. Six taps on a small disc
                // (0.18 m radius in the wood, 3-4 texels) give a trunk's shadow a
                // soft rim instead of the map's own grid; a single tap is a stencil.
                float v = CsTap(sc.xy, z)
                        + CsTap(sc.xy + float2( 0.951,  0.309) * f, z)
                        + CsTap(sc.xy + float2( 0.000,  1.000) * f, z)
                        + CsTap(sc.xy + float2(-0.951,  0.309) * f, z)
                        + CsTap(sc.xy + float2(-0.588, -0.809) * f, z)
                        + CsTap(sc.xy + float2( 0.588, -0.809) * f, z);
                v *= (1.0 / 6.0);
                // Off the edge of the baked map, and anywhere in front of its near
                // plane, everything is lit. CLAMP addressing would otherwise drag
                // the border texels right across the room, and a hard cut-off
                // would draw a line on the floor — so it fades out over ~1/40th
                // of the map's own width, which is comfortably wider than the tap
                // disc, so no tap ever reaches past the border while it counts.
                float2 q = abs(sc.xy - 0.5);
                float edge = saturate((0.5 - max(q.x, q.y)) * 40.0)
                           * step(0.0, sc.z) * step(sc.z, 1.0);
                return 1.0 - _CsFlt.w * (1.0 - v) * edge;
            }

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                fixed4 color   : COLOR;
            };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                float2 uv2  : TEXCOORD1;
                float3 opos : TEXCOORD2;
                float3 n    : TEXCOORD3;
                float3 t    : TEXCOORD4;
                float3 b    : TEXCOORD5;
                fixed4 vcol : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv, _MainTex2);
                o.opos = v.vertex.xyz;
                o.n = v.normal;
                o.t = v.tangent.xyz;
                o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
                o.vcol = v.color;
                return o;
            }

            float Flicker (float amt, float phase, float rate)
            {
                float t = (_Time.y + _GhvrTimeOfs) * rate;
                float f = 0.42 * sin(t * 11.3 + phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
                f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
                return 1.0 + amt * 0.35 * f;
            }

            float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate)
            {
                float3 lv = lpos.xyz - opos;
                float d2 = max(dot(lv, lv), 1e-8);
                float d = sqrt(d2);
                float q = d2 * lpos.w * lpos.w;
                float x = saturate(1.0 - q);
                float atten = x * x / (1.0 + _PtHard * q);
                float ndl = saturate(dot(N, lv / d));
                return lcol.rgb * (atten * ndl * Flicker(lcol.a, phase, rate));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float blend = i.vcol.a;
                fixed4 alb = lerp(tex2D(_MainTex, i.uv), tex2D(_MainTex2, i.uv2), blend) * _Tint;
                float3 n_ts = lerp(UnpackNormal(tex2D(_BumpMap, i.uv)),
                                   UnpackNormal(tex2D(_BumpMap2, i.uv2)), blend);
                n_ts.xy *= _BumpScale;
                float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);

                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5);
                // USER FINDING, ModBuild 137 (hardware): "... ich würde hier
                // gerne das die Bäume entsprechende Schatten werfen." Half of
                // "the trees cast shadows" is the trunk shadows lying across the
                // clearing floor, and this is it: a pure multiply on the MOON
                // term. It can only subtract — see the _CsMap block in the
                // Properties for why nothing here can get brighter.
                light += _DirCol.rgb * (_DirScale * saturate(dot(N, normalize(_DirDir.xyz)))
                                        * CsVisible(CsCoord(i.opos)));
                light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00);
                light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83);
                light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19);

                float3 col = alb.rgb * light * i.vcol.rgb;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
