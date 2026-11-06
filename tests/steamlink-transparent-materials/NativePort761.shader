// TEST ONLY: source RGBA/texture/clipping port with the ORIGINAL serialized
// fixed-function factors supplied by the native asset auditor. Production uses
// the exact original D3D11 programs; this fixture does not execute those programs.
Shader "NATIVE_NAME761"
{
    Properties
    {
        _MainTex("Texture",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
        _StencilComp("Stencil comparison",Float)=8
        _Stencil("Stencil reference",Float)=0
        _StencilOp("Stencil operation",Float)=0
        _StencilWriteMask("Stencil write mask",Float)=255
        _StencilReadMask("Stencil read mask",Float)=255
        _ColorMask("Channels",Float)=15
        _ZWrite("Depth write",Float)=0
        _ZTest("Depth test",Float)=4
        _GreyOut("Authored effect state",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off ZWrite [_ZWrite] ZTest [_ZTest]
        Blend NATIVE_RGB761, NATIVE_ALPHA761
        ColorMask [_ColorMask]
        Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; float4 _Color; float4 _ClipRect;
            struct Vertex {float4 p:POSITION;float2 uv:TEXCOORD0;float4 c:COLOR;};
            struct Fragment {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 c:COLOR;float2 local:TEXCOORD1;};
            Fragment vert(Vertex v) {Fragment o;o.p=UnityObjectToClipPos(v.p);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.c=v.c*_Color;o.local=v.p.xy;return o;}
            float4 frag(Fragment i):SV_Target
            {
                float4 c=tex2D(_MainTex,i.uv)*i.c;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
