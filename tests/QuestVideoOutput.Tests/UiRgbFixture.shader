Shader "Hidden/GloomhavenVR/FixtureUiRgb"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha ColorMask RGB
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 position : POSITION; float4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; float4 color : COLOR; };
            Output vert(Input input)
            { Output output; output.position=UnityObjectToClipPos(input.position); output.color=input.color; return output; }
            float4 frag(Output input) : SV_TARGET { return input.color; }
            ENDHLSL
        }
    }
}
