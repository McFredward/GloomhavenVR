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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
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
            };

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            fixed4 _Color;
            float _Ambient, _LightBoost, _NormalStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                o.wn  = UnityObjectToWorldNormal(v.normal);
                o.wt  = UnityObjectToWorldDir(v.tangent.xyz);
                o.wb  = cross(o.wn, o.wt) * v.tangent.w * unity_WorldTransformParams.w;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 alb = tex2D(_MainTex, i.uv) * _Color;
                float3 nt = UnpackNormal(tex2D(_BumpMap, i.uv));
                nt.xy *= _NormalStrength;
                float3 N = normalize(i.wt * nt.x + i.wb * nt.y + i.wn * nt.z);

                // Baked studio rig (world space): a warm key from above-front and a
                // soft fill from the opposite side, plus a guaranteed ambient floor.
                float3 key  = normalize(float3(0.35, 0.85, -0.45));
                float3 fill = normalize(float3(-0.55, 0.35, 0.30));
                float lit = saturate(dot(N, key)) * _LightBoost
                          + saturate(dot(N, fill)) * 0.35;
                float shade = _Ambient + lit;
                return fixed4(alb.rgb * shade, 1.0);
            }
            ENDCG
        }
    }
}
