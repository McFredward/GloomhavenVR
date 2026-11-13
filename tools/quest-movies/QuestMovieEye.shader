Shader "Hidden/GloomhavenVR/QuestMovieEye"
{
    Properties
    {
        _MainTex ("Full SBS decoder", 2D) = "black" {}
        _Background ("Original background", 2D) = "black" {}
        _Eye ("Recorded eye", Float) = 0
        _FrameSize ("Fitted frame size", Vector) = (1,1,0,0)
        _Alpha ("Original movie alpha", Float) = 1
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
            sampler2D _MainTex, _Background;
            float4 _MainTex_TexelSize, _FrameSize;
            float _Eye, _Alpha;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = float4(input.position.xy * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    output.position.y = -output.position.y;
                #endif
                output.uv = input.uv;
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                float2 frame = (input.uv - 0.5) / _FrameSize.xy + 0.5;
                float inside = step(0.0, frame.x) * step(frame.x, 1.0)
                    * step(0.0, frame.y) * step(frame.y, 1.0);
                float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                float2 packed = float2(frame.x * 0.5 + _Eye * 0.5, frame.y);
                packed = clamp(packed, float2(_Eye * 0.5 + halfTexel.x, halfTexel.y),
                    float2((_Eye + 1.0) * 0.5 - halfTexel.x, 1.0 - halfTexel.y));
                float4 background = tex2D(_Background, input.uv);
                return lerp(background, float4(tex2D(_MainTex, packed).rgb, 1.0), saturate(_Alpha) * inside);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
