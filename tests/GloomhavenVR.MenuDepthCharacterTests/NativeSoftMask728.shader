// Exact semantic translation of the pinned original D3D11 vertex/fragment
// programs (920/572 bytes), not an execution claim for that Windows bytecode.
Shader "UI/Inverted soft alpha mask (compatible with canvas group)"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Mask ("Alpha mask (R)", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct input { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct output { float4 vertex : SV_POSITION; float4 color : COLOR; float2 mainUV : TEXCOORD0; float2 maskUV : TEXCOORD1; };
            sampler2D _MainTex, _Mask; float4 _MainTex_ST, _Mask_ST;
            output vert(input v)
            {
                output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color;
                o.mainUV=v.uv*_MainTex_ST.xy+_MainTex_ST.zw; o.maskUV=v.uv*_Mask_ST.xy+_Mask_ST.zw; return o;
            }
            float4 frag(output i) : SV_Target
            {
                float4 color=tex2D(_MainTex,i.mainUV)*i.color;
                color.a*=1-tex2D(_Mask,i.maskUV).a; return color;
            }
            ENDCG
        }
    }
}
