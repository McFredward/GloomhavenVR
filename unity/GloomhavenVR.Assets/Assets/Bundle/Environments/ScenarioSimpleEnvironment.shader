// Optional static-environment compromise. Original wall dissolve, foliage, water,
// actors and UI never use this shader. Keeps original albedo UV/tint and world-space
// projection; removes normal/MRAO/detail texture work. Build with Unity 2021.3.5f1.
Shader "GloomhavenVR/ScenarioSimpleEnvironment"
{
    Properties
    {
        _MainTex ("Original color texture", 2D) = "white" {}
        _Tint ("Original tint", Color) = (1,1,1,0)
        _Diffuse_Boost ("Original diffuse boost", Float) = 1
        _Desaturation ("Original desaturation", Range(0,1)) = 0
        _UVTiling ("Original UV tiling", Float) = 1
        _UV_Offset ("Original UV offset", Float) = 0
        _WorldSpace ("Original world projection", Float) = 0
        _WorldSpace_tiling ("Original world tiling", Float) = 1
        _WorldSpace_FallOff ("Original projection blend", Float) = 0.8
        _Difuse_Alpha_On ("Original color cutout", Float) = 0
        _Cutoff ("Original cutout threshold", Float) = 0.5
        _IsDimmed ("Original dimming", Float) = 0
        _DimmFactor ("Original dim amount", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Tint;
            float _Diffuse_Boost, _Desaturation, _UVTiling, _UV_Offset;
            float _WorldSpace, _WorldSpace_tiling, _WorldSpace_FallOff;
            float _Difuse_Alpha_On, _Cutoff, _IsDimmed, _DimmFactor;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float2 uv = v.uv * _UVTiling + _UV_Offset;
                o.uv = TRANSFORM_TEX(uv, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normal);
                fixed4 tex;
                if (_WorldSpace > 0.5)
                {
                    float3 blend = pow(abs(n), max(1.0, _WorldSpace_FallOff * 8.0));
                    blend /= max(dot(blend,1.0),0.0001);
                    float3 p = i.world * _WorldSpace_tiling;
                    tex = tex2D(_MainTex,p.yz) * blend.x
                        + tex2D(_MainTex,p.xz) * blend.y
                        + tex2D(_MainTex,p.xy) * blend.z;
                }
                else tex = tex2D(_MainTex,i.uv);
                if (_Difuse_Alpha_On > 0.5) clip(tex.a - _Cutoff);
                fixed3 color = tex.rgb * _Tint.rgb * _Diffuse_Boost;
                color = lerp(color,dot(color,fixed3(0.299,0.587,0.114)),_Desaturation);
                float3 light = max(ShadeSH9(float4(n,1.0)),0.08)
                    + _LightColor0.rgb * saturate(dot(n,(_WorldSpaceLightPos0.xyz * rsqrt(max(dot(_WorldSpaceLightPos0.xyz, _WorldSpaceLightPos0.xyz), 0.0001)))));
                color *= light * lerp(1.0,_DimmFactor,saturate(_IsDimmed));
                fixed4 result = fixed4(color,1.0);
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback Off
}
