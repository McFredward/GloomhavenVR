Shader "CoverageRuntimeFixture/Surface"
{
    Properties
    {
        _Color ("Color", Color) = (1,.375,.625,.17)
        _Cutout ("Cutout", Float) = 0
        _AlphaToMask ("Alpha to coverage", Float) = 0
        _ZWrite ("Depth writes", Float) = 1
        _Mask ("Color channels", Float) = 15
        _StencilRef ("Stencil ref", Float) = 0
        _StencilComp ("Stencil comparison", Float) = 8
        _StencilPass ("Stencil pass", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" }
        Cull Off ZTest LEqual ZWrite [_ZWrite] ColorMask [_Mask]
        AlphaToMask [_AlphaToMask]
        Stencil { Ref [_StencilRef] Comp [_StencilComp] Pass [_StencilPass] }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Cutout;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
            float4 frag(Output i):SV_Target
            {
                if(_Cutout > .5)clip(frac(i.uv.x*4)+frac(i.uv.y*4)-.85);
                return _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
