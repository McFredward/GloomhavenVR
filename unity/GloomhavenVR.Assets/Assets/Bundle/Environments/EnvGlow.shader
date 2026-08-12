// GloomhavenVR — soft light-glow shader for small emissive spheres around flames and
// window light. A real world-space sphere whose brightness falls off toward its
// silhouette (dot(N,V) falloff): reads as a volumetric halo from EVERY direction and
// in stereo, with no billboarding and nothing attached to the camera. Additive,
// no depth write.
Shader "GloomhavenVR/EnvGlow"
{
    Properties
    {
        _Tint ("Glow color (alpha = strength)", Color) = (1,0.6,0.2,0.5)
        _Falloff ("Edge falloff", Range(0.5,8)) = 2.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Falloff;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float core = pow(saturate(dot(normalize(i.wn), V)), _Falloff);
                return fixed4(_Tint.rgb, core * _Tint.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
