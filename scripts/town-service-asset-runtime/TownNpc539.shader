// Self-contained Standard metallic lighting; the shader is included in ghvr-town.bundle.
// Hunyuan's selected materials explicitly set glTF doubleSided=true. Cull Off and
// face-oriented tangent normals preserve those surfaces instead of introducing holes.
Shader "GloomhavenVR/TownNpc"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base colour", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Range(0,2)) = 1
        _MetallicGlossMap ("Metallic R / Smoothness A", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Mapped smoothness scale", Range(0,1)) = 1
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _TownVisibility ("Town visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Off
        CGPROGRAM
        // Unity's surface compiler generates instance-ID setup and stereo eye routing
        // for all passes. Do not opt out with noinstancing or replace generated vertices.
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP
        #pragma shader_feature_local _EMISSION
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
        fixed4 _Color, _EmissionColor;
        half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _TownVisibility;
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            fixed facing : VFACE;
        };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            // Object-space dissolve is shared by both eyes and peers, without time/head
            // dependence. The full-opacity path skips noise sampling entirely. addshadow
            // makes the shadow caster use the same surface clip during transitions.
            if (_TownVisibility < 0.99999h)
            {
                float3 objectPosition = mul(unity_WorldToObject, float4(input.worldPos, 1)).xyz;
                float3 cell = floor(objectPosition * 128.0);
                float noise = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                clip(_TownVisibility - max(noise, 0.0001));
            }
            fixed4 colour = tex2D(_MainTex, input.uv_MainTex) * _Color;
            output.Albedo = colour.rgb;
            output.Alpha = 1;
            output.Normal = half3(0, 0, 1);
            #if defined(_NORMALMAP)
                output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
            #endif
            output.Normal *= input.facing >= 0 ? 1 : -1;
            output.Metallic = _Metallic;
            output.Smoothness = _Glossiness;
            #if defined(_METALLICGLOSSMAP)
                half4 masks = tex2D(_MetallicGlossMap, input.uv_MainTex);
                output.Metallic = masks.r;
                output.Smoothness = masks.a * _GlossMapScale;
            #endif
            #if defined(_EMISSION)
                output.Emission = _EmissionColor.rgb;
            #endif
        }
        ENDCG
    }
    FallBack Off
}
