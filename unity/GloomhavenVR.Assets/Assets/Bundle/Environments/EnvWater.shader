// GloomhavenVR — still swamp water shader. Dark base with a slowly rippling moon
// glint (specular toward _GlintDir, normal perturbed by a scrolling tileable noise
// texture) and a faint horizon fresnel. Everything derives from world position and
// the real per-eye view vector, so it is stereo-correct like any physical specular;
// ripple animation is pure shader _Time — no scripts. Opaque.
Shader "GloomhavenVR/EnvWater"
{
    Properties
    {
        _Color ("Water color", Color) = (0.02,0.035,0.045,1)
        _HorizonCol ("Horizon tint", Color) = (0.05,0.09,0.13,1)
        _NoiseTex ("Ripple noise (tileable)", 2D) = "grey" {}
        _RippleScale ("Ripple scale (1/m)", Range(0.01,2)) = 0.18
        _RippleSpeed ("Ripple speed", Range(0,1)) = 0.045
        _RippleAmp ("Ripple amount", Range(0,1)) = 0.35
        _GlintDir ("Moon direction (world)", Vector) = (0.5,0.35,0.6,0)
        _GlintCol ("Moon glint color", Color) = (0.55,0.62,0.75,1)
        _GlintPower ("Glint tightness", Range(4,400)) = 90
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _HorizonCol, _GlintCol;
            sampler2D _NoiseTex;
            float _RippleScale, _RippleSpeed, _RippleAmp, _GlintPower;
            float4 _GlintDir;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.wp.xz * _RippleScale;
                float t = _Time.y * _RippleSpeed;
                float n1 = tex2D(_NoiseTex, uv + float2(t, t * 0.7)).r;
                float n2 = tex2D(_NoiseTex, uv * 1.7 - float2(t * 0.8, t * 0.5)).r;
                float2 slope = (float2(n1, n2) - 0.5) * _RippleAmp;
                float3 N = normalize(float3(slope.x, 1.0, slope.y));

                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 L = normalize(_GlintDir.xyz);
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _GlintPower);

                float fres = pow(1.0 - saturate(dot(V, float3(0,1,0))), 3.0);
                float3 col = lerp(_Color.rgb, _HorizonCol.rgb, fres)
                           + _GlintCol.rgb * spec;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
