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
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            sampler2D _MainTex2; float4 _MainTex2_ST;
            sampler2D _BumpMap2;
            float _BumpScale, _PtHard;
            fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

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
                light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz)));
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
