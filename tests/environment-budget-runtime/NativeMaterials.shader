// Generated native API surrogate, not game artwork. The actual production simple
// shader is copied from --source-root and imported beside this fixture at run time.
Shader "Amp_Basic_N_MRAO"
{
    Properties
    {
        _MainTex("Color",2D) = "white" {}
        _Tint("Tint",Color) = (1,1,1,0)
        _Diffuse_Boost("Boost",Float) = 1
        _Desaturation("Desaturation",Float) = 0
        _UVTiling("Tiling",Float) = 1
        _UV_Offset("Offset",Float) = 0
        _WorldSpace("World",Float) = 0
        _WorldSpace_tiling("World tiling",Float) = 1
        _WorldSpace_FallOff("World blend",Float) = 0.8
        _Difuse_Alpha_On("Alpha",Float) = 0
        _Cutoff("Cutoff",Float) = 0.5
        _IsDimmed("Dimmed",Float) = 0
        _DimmFactor("Dim factor",Float) = 0.3
        _AddVertexAnim("Animated",Float) = 0
        _UseEmissiveMap("Emissive map",Float) = 0
        _Diffuse_Emissive_On("Emissive",Float) = 0
        _WallFade_On("Wall fade",Float) = 0
        _FixtureProbeLighting("Fixture native SH and reflection oracle",Float) = 0
        _ToggleWallfade("Wall fade toggle",Float) = 0
        ToggleWallFade("Wall fade toggle 2",Integer) = 0
        _ToggleWallFadeLocal("Wall fade local",Float) = 0
        _TilesOcclusionMap("Native occlusion map boundary",2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ _WALLFADE_ON_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile __ UNITY_SPECCUBE_BLENDING
            #pragma multi_compile __ UNITY_SPECCUBE_BOX_PROJECTION
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _TilesOcclusionMap;
            float4 _MainTex_ST, _Tint;
            float _UVTiling, _UV_Offset;
            float _WallFade_On, _Cutoff;
            float _FixtureProbeLighting;
            int ToggleWallFade;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float objY:TEXCOORD2; float3 ambient:TEXCOORD3; float3 normal:TEXCOORD4; float2 lightmapUV:TEXCOORD5; };
            v2f vert(appdata v)
            { UNITY_SETUP_INSTANCE_ID(v); v2f o; o.pos=UnityObjectToClipPos(v.vertex); float2 uv=v.texcoord.xy*_UVTiling+_UV_Offset; o.uv=TRANSFORM_TEX(uv,_MainTex); o.screen=ComputeScreenPos(o.pos); o.objY=v.vertex.y; o.normal=UnityObjectToWorldNormal(v.normal); o.ambient=ShadeSH9(float4(o.normal,1)); o.lightmapUV=v.texcoord*unity_LightmapST.xy+unity_LightmapST.zw; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                // Explicit GL surrogate for the documented native LOW fragment branch.
                // Original Windows compiled shader blobs cannot execute on this GL host.
                // The fixture only models above-foundation, alpha-zero map samples:
                // m=1-occ.r, clip(m-cutoff), with the original runtime MPB and textures.
                if (_WallFade_On > 0.5 && ToggleWallFade > 0.5 && i.objY >= 0.4)
                    clip(1-tex2D(_TilesOcclusionMap,i.screen.xy/i.screen.w).r-_Cutoff);
                float3 lighting=1;
                if (_FixtureProbeLighting > 0.5)
                {
                    float3 diffuse=i.ambient;
                    #if defined(LIGHTMAP_ON)
                    diffuse=DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap,i.lightmapUV));
                    #endif
                    lighting=diffuse+DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0,i.normal),unity_SpecCube0_HDR);
                }
                return fixed4(tex2D(_MainTex,i.uv).rgb*_Tint.rgb*lighting,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
