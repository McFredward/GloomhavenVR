// GloomhavenVR — still swamp-water shader (moonlit night ponds).
//
// Dark reflective standing water: two counter-scrolling tilings of a small
// procedural ripple-normal texture perturb the surface normal (_Time-driven,
// no scripts), which feeds
//   - a fixed sky-reflection tint (hemisphere: brighter as the perturbed
//     normal tips toward the viewer's horizon),
//   - a Blinn glint from the MOON direction (matches the baked _MoonDir of
//     the star dome) — view-dependent, i.e. correct per eye in stereo.
// Alpha-blended with vertex-color alpha fading the rim into the mud so pond
// edges never show a hard polygon line.
Shader "GloomhavenVR/EnvWater"
{
    Properties
    {
        _RippleTex ("Ripple normal (RG packed)", 2D) = "bump" {}
        _DeepCol ("Deep water color", Color) = (0.012,0.018,0.022,1)
        _SkyCol ("Sky reflection tint", Color) = (0.05,0.07,0.10,1)
        _MoonDir ("Moon direction (world, toward moon)", Vector) = (0.6,0.37,0.71,0)
        _MoonCol ("Moon glint color", Color) = (0.9,0.92,0.85,1)
        _GlintPow ("Glint tightness", Range(8,400)) = 120
        _RippleScale ("Ripple strength", Range(0,1)) = 0.35
        _Tiling ("Ripple tiling (m)", Float) = 1.6
        _Speed ("Ripple scroll speed", Float) = 0.014
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _RippleTex;
            fixed4 _DeepCol, _SkyCol, _MoonCol;
            float4 _MoonDir;
            float _GlintPow, _RippleScale, _Tiling, _Speed;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                float3 wp   : TEXCOORD0;
                float2 luv  : TEXCOORD1; // object-space XZ => stable tiling
                fixed4 vcol : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.luv = v.vertex.xz / max(_Tiling, 1e-3);
                o.vcol = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float3 n1 = UnpackNormal(tex2D(_RippleTex, i.luv + float2(t, t * 0.7)));
                float3 n2 = UnpackNormal(tex2D(_RippleTex, i.luv * 1.7 - float2(t * 1.3, t * 0.4)));
                // ripple normals live in the water plane: x/z from the maps, y up
                float3 N = normalize(float3((n1.xy + n2.xy) * _RippleScale, 1).xzy);

                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 L = normalize(_MoonDir.xyz);

                // sky reflection: stronger at grazing angles (cheap fresnel-ish)
                float fres = pow(1.0 - saturate(dot(N, V)), 2.0);
                float3 col = _DeepCol.rgb + _SkyCol.rgb * (0.25 + 0.75 * fres);

                // moon glint (Blinn) — sparkles as ripples pass through alignment
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _GlintPow);
                col += _MoonCol.rgb * spec;

                return fixed4(col, i.vcol.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
