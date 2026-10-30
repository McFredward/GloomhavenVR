// Same declared GL stand-in as NativeCard732; the native derived bytes are
// verified independently. These pixels prove dispatch/state/scoping, not DXBC.
Shader "GloomhavenVR/FixtureNativeCardDepth"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _MainColour ("Native property authority witness", Color) = (1,1,1,1)
        _Flow ("Native animation property witness", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Comparison", Float) = 8
        _Stencil ("Stencil", Float) = 0
        _StencilOp ("Operation", Float) = 0
        _StencilWriteMask ("WriteMask", Float) = 255
        _StencilReadMask ("ReadMask", Float) = 255
        _ColorMask ("ColorMask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite On ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 position:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainColour, _ClipRect;
            Output vert(Input i) { Output o; o.vertex=UnityObjectToClipPos(i.vertex); o.position=i.vertex.xy; o.color=i.color; o.uv=i.uv; return o; }
            float4 frag(Output i):SV_Target
            {
                float4 c=tex2D(_MainTex,i.uv)*i.color*_MainColour;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.position,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
