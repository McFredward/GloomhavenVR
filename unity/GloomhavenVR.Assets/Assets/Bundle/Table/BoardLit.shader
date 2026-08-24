// Self-contained board shader for the control-board asset (PlayTray).
//
// WHY custom (not built-in Standard): a bundled material referencing the built-in
// Standard/UI shaders is the "pink-material trap" (TOOLCHAIN.md §4.1) — built-in
// shaders are not packed into AssetBundles, so at runtime in the game the material
// resolves to nothing and renders magenta. This shader compiles INTO
// gloomhavenvr.bundle, so it is always present.
//
// Lighting is BAKED (two fixed studio directions + an ambient floor), independent of
// the scene's own lights: the diorama the board sits in has no guaranteed lighting,
// and an ordinary lit shader would render the board black there (the same black-out
// the hands avoid). The AI-authored albedo already carries baked detail; this adds
// just enough normal-mapped shape so the brass/woodgrain reads, and never goes dark.
// One variant, opaque, no external dependency.
Shader "GloomhavenVR/BoardLit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Ambient ("Ambient floor", Range(0,1)) = 0.5
        _LightBoost ("Key light", Range(0,2)) = 0.85
        _NormalStrength ("Normal strength", Range(0,2)) = 1.0
        // ---- SPECULAR (ModBuild 248) -------------------------------------------------
        // R = metallic, G = roughness, packed by BuildHands from the two greyscale maps an
        // artist delivers. Default "black" = metallic 0 / roughness 0, and _SpecStrength 0
        // means the block below does not execute at all — so every material that does not
        // opt in renders BIT-IDENTICALLY to the build before this existed, not "looks the
        // same". Both control boards and two of the three hands are in that set.
        _MRSMap ("Metallic (R) / Roughness (G)", 2D) = "black" {}
        _SpecStrength ("Specular strength", Range(0,2)) = 0
        // Cull mode: Back (2) for the board (default), Off (0) for the AI-generated
        // hand glove — its mesh is fragmented (310 shells, non-manifold), so single-
        // sided culling turns missing/flipped faces into black voids. Rendering both
        // sides fills those gaps with the shell behind (see HandsBuilder DEFECT 2).
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1; // world normal
                float3 wt : TEXCOORD2; // world tangent
                float3 wb : TEXCOORD3; // world bitangent
                float3 wp : TEXCOORD4; // world position — ONLY the specular block reads it
            };

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            sampler2D _MRSMap;
            fixed4 _Color;
            float _Ambient, _LightBoost, _NormalStrength, _SpecStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                o.wn  = UnityObjectToWorldNormal(v.normal);
                o.wt  = UnityObjectToWorldDir(v.tangent.xyz);
                o.wb  = cross(o.wn, o.wt) * v.tangent.w * unity_WorldTransformParams.w;
                o.wp  = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 alb = tex2D(_MainTex, i.uv) * _Color;
                float3 nt = UnpackNormal(tex2D(_BumpMap, i.uv));
                nt.xy *= _NormalStrength;
                float3 N = normalize(i.wt * nt.x + i.wb * nt.y + i.wn * nt.z);
                // Two-sided lighting: when a back face is drawn (Cull Off on the hand),
                // flip the normal so the interior wall filling a hole is lit, not black.
                N *= sign(facing);

                // Baked studio rig (world space): a warm key from above-front and a
                // soft fill from the opposite side, plus a guaranteed ambient floor.
                float3 key  = normalize(float3(0.35, 0.85, -0.45));
                float3 fill = normalize(float3(-0.55, 0.35, 0.30));
                float lit = saturate(dot(N, key)) * _LightBoost
                          + saturate(dot(N, fill)) * 0.35;
                float shade = _Ambient + lit;
                float3 col = alb.rgb * shade;

                // ---- SPECULAR, OPT-IN (ModBuild 248) ---------------------------------
                // WHY THIS EXISTS. The plate gauntlet's second delivery is a full PBR set,
                // and its base colour is FLAT by design: the hammered-steel micro-detail
                // that the first delivery had baked into the albedo now lives in the normal
                // and roughness maps. Shipping only albedo+normal therefore made the
                // gauntlet look flatter and lighter than the asset it replaced — the
                // delivery got better and the picture got worse. This is the term that
                // reads the other half of it.
                //
                // ZERO STATE IS BIT-IDENTICAL, not "visually identical": with
                // _SpecStrength at its 0 default the branch does not execute, so nothing
                // here can perturb a material that has not opted in. That is the same rule
                // EnvElement.cginc holds the element art to, and it is what lets a shared
                // shader take a feature for ONE asset.
                //
                // IT IS BLINN-PHONG AGAINST THE SAME TWO BAKED DIRECTIONS the diffuse uses,
                // never a scene light — the whole reason this shader exists is that the
                // diorama's lighting is not guaranteed, and a specular that depended on it
                // would black out exactly where the diffuse was written not to.
                //
                // DIFFUSE IS NOT ENERGY-CONSERVED AWAY UNDER METAL, deliberately. A real
                // metal has no diffuse lobe, but this albedo was authored to be READ as the
                // surface colour by a shader with no specular at all, so subtracting it
                // would darken the gauntlet at the same moment the highlight arrives and
                // the net change would be a guess. The highlight is added on top and its
                // weight is one material float the next hardware round can turn.
                //
                // PER-EYE BY CONSTRUCTION, and that is correct rather than a hazard here:
                // a real highlight IS view-dependent, so the two eyes SHOULD disagree. The
                // thing that would bite is a mirror-sharp lobe on a high-frequency normal
                // map (stereo rivalry, this project's own recurring defect) — the delivered
                // roughness is 0.62 mean, which is a broad lobe, and the floor below keeps
                // it broad even where the map goes to zero.
                if (_SpecStrength > 0.0)
                {
                    float2 mr = tex2D(_MRSMap, i.uv).rg;
                    float rough = max(0.08, mr.g);            // floor: never mirror-sharp
                    float power = exp2((1.0 - rough) * 9.0 + 1.0);
                    // Metals tint their highlight with the base colour; dielectrics do not.
                    float3 f0 = lerp(float3(0.04, 0.04, 0.04), alb.rgb, mr.r);
                    float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                    float3 Hk = normalize(key + V);
                    float3 Hf = normalize(fill + V);
                    float3 spec = f0 * (pow(saturate(dot(N, Hk)), power) * saturate(dot(N, key)) * _LightBoost
                                      + pow(saturate(dot(N, Hf)), power) * saturate(dot(N, fill)) * 0.35);
                    col += spec * _SpecStrength;
                }
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
