// Town stations live on the mod layer in maps with no enabled scene lights (build-539
// hardware evidence). Use the same self-contained studio-light principle as BoardLit:
// retain albedo, normal and metallic detail without depending on a game's light cap,
// ambient probes or an observer's environment. One pass; no runtime Light components.
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
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _EMISSION
            #include "UnityCG.cginc"
            #include "UnityStandardUtils.cginc"
            sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
            float4 _MainTex_ST;
            fixed4 _Color, _EmissionColor;
            half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _TownVisibility;
            struct AppData
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Interpolated
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float3 tangent : TEXCOORD3;
                float3 bitangent : TEXCOORD4;
                float3 objectPosition : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Interpolated vert(AppData input)
            {
                Interpolated output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Interpolated, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.objectPosition = input.vertex.xyz;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.tangent = UnityObjectToWorldDir(input.tangent.xyz);
                output.bitangent = cross(output.normal, output.tangent) * input.tangent.w * unity_WorldTransformParams.w;
                return output;
            }
            fixed4 frag(Interpolated input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Position-stable dissolve: both eyes and peers observe the same intermediate
                // surface. The full-opacity path avoids noise work entirely.
                if (_TownVisibility < 0.99999h)
                {
                    float3 cell = floor(input.objectPosition * 128.0);
                    float noise = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                    clip(_TownVisibility - max(noise, 0.0001));
                }
                half3 normal = normalize(input.normal);
                #if defined(_NORMALMAP)
                    half3 tangentNormal = UnpackScaleNormal(tex2D(_BumpMap, input.uv), _BumpScale);
                    normal = normalize(input.tangent * tangentNormal.x + input.bitangent * tangentNormal.y + normal * tangentNormal.z);
                #endif
                normal *= facing >= 0 ? 1 : -1;
                half3 albedo = tex2D(_MainTex, input.uv).rgb * _Color.rgb;
                half metallic = _Metallic;
                half smoothness = _Glossiness;
                #if defined(_METALLICGLOSSMAP)
                    half4 masks = tex2D(_MetallicGlossMap, input.uv);
                    metallic = masks.r;
                    smoothness = masks.a * _GlossMapScale;
                #endif
                half3 key = normalize(half3(-0.45, 0.8, -0.5));
                half3 fill = normalize(half3(0.65, 0.35, 0.55));
                half keyDiffuse = saturate(dot(normal, key));
                half fillDiffuse = saturate(dot(normal, fill));
                half shade = 0.42h + 0.45h * keyDiffuse + 0.18h * fillDiffuse;
                half3 view = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfway = normalize(key + view);
                half gloss = pow(saturate(dot(normal, halfway)), exp2(3 + smoothness * 6));
                half3 specular = lerp(half3(0.04, 0.04, 0.04), albedo, metallic);
                half3 colour = albedo * shade + specular * gloss * keyDiffuse * (0.15h + smoothness * 0.55h);
                #if defined(_EMISSION)
                    colour += _EmissionColor.rgb;
                #endif
                return fixed4(colour, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
