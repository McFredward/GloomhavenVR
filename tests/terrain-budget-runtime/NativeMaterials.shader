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
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ _WALLFADE_ON_ON
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _TilesOcclusionMap;
            float4 _MainTex_ST, _Tint;
            float _UVTiling, _UV_Offset;
            float _WallFade_On, _Cutoff;
            int ToggleWallFade;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float objY:TEXCOORD2; };
            v2f vert(appdata_base v)
            { v2f o; o.pos=UnityObjectToClipPos(v.vertex); float2 uv=v.texcoord.xy*_UVTiling+_UV_Offset; o.uv=TRANSFORM_TEX(uv,_MainTex); o.screen=ComputeScreenPos(o.pos); o.objY=v.vertex.y; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                // Explicit GL surrogate for the documented native LOW fragment branch.
                // Original Windows compiled shader blobs cannot execute on this GL host.
                // The fixture only models above-foundation, alpha-zero map samples:
                // m=1-occ.r, clip(m-cutoff), with the original runtime MPB and textures.
                if (_WallFade_On > 0.5 && ToggleWallFade > 0.5 && i.objY >= 0.4)
                    clip(1-tex2D(_TilesOcclusionMap,i.screen.xy/i.screen.w).r-_Cutoff);
                return fixed4(tex2D(_MainTex,i.uv).rgb*_Tint.rgb,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
