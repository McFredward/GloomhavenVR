Shader "CoverageBank/Original"
{
    Properties { _Color("Color", Color)=(.2,.4,.6,.5) _Clip("Clip", Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Stencil { Ref 0 Comp Always Pass Keep }
        Pass
        {
            Name "OWNED_OVER"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color; float _Clip;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Interpolated { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            Interpolated vert(Input input) { Interpolated output; output.position=input.vertex; output.uv=input.uv; return output; }
            float4 frag(Interpolated input):SV_Target { if (_Clip>.5) clip(input.uv.x-.5); return _Color; }
            ENDCG
        }
    }
    Fallback Off
}
