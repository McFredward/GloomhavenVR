Shader "Hidden/GloomhavenVR/QuestTextureCopy"
{
    Properties
    {
        _MainTex ("Ordinary source texture", 2D) = "black" {}
        _UvScaleOffset ("Source crop", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off Blend One Zero
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _UvScaleOffset;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                // Graphics.Blit supplies an ordinary 0..1 fullscreen quad.
                // Do not borrow XR camera/view matrices for this offscreen copy.
                output.position = float4(input.position.xy * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    output.position.y = -output.position.y;
                #endif
                output.uv = input.uv;
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                return tex2D(_MainTex, input.uv * _UvScaleOffset.xy + _UvScaleOffset.zw);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
