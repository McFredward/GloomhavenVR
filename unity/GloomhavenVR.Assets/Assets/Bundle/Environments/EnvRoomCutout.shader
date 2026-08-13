// GloomhavenVR — two-sided alpha-cutout variant of EnvRoom (grass, ferns,
// hanging moss). Same baked-light model as EnvRoom (see there); Cull Off with
// VFACE so both sides of a foliage card are lit as seen. Kept as its own
// shader (not a multi_compile) so materials stay dead simple and deterministic.
Shader "GloomhavenVR/EnvRoomCutout"
{
    Properties
    {
        _MainTex ("Albedo (A=opacity)", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
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
        _VCol ("Vertex color amount", Range(0,1)) = 0
        _Cutoff ("Cutout threshold", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            float _BumpScale, _VCol, _Cutoff;
            fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos;

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
                float3 opos : TEXCOORD1;
                float3 n    : TEXCOORD2;
                float3 t    : TEXCOORD3;
                float3 b    : TEXCOORD4;
                fixed4 vcol : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.opos = v.vertex.xyz;
                o.n = v.normal;
                o.t = v.tangent.xyz;
                o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
                o.vcol = v.color;
                return o;
            }

            float Flicker (float amt, float phase)
            {
                float t = _Time.y;
                float f = 0.42 * sin(t * 11.3 + phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
                return 1.0 + amt * 0.35 * f;
            }

            float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase)
            {
                float3 lv = lpos.xyz - opos;
                float d = max(length(lv), 1e-4);
                float x = saturate(1.0 - d * d * lpos.w * lpos.w);
                float ndl = saturate(dot(N, lv / d));
                return lcol.rgb * (x * x * ndl * Flicker(lcol.a, phase));
            }

            fixed4 frag (v2f i, fixed face : VFACE) : SV_Target
            {
                fixed4 alb = tex2D(_MainTex, i.uv) * _Tint;
                clip(alb.a - _Cutoff);

                float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.uv));
                n_ts.xy *= _BumpScale;
                float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);
                N *= face >= 0 ? 1.0 : -1.0;

                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5);
                light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz)));
                light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0);
                light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1);
                light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4);

                return fixed4(alb.rgb * light, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
