Shader "Hidden/GloomhavenVR/QuestCameraVideo"
{
    Properties
    {
        _MainTex ("Native decoded movie", 2D) = "black" {}
        _UvTransform ("Native aspect mapping", Vector) = (1,1,0,0)
        _Alpha ("Native camera alpha", Float) = 1
        _Plane ("Native camera plane", Float) = 0
        _ZTest ("Native depth comparison", Float) = 8
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGBA
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _UvTransform;
            float _Alpha, _Plane;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                #if defined(UNITY_REVERSED_Z)
                    float depth = 1.0 - _Plane;
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, _Plane);
                #endif
                output.position = float4(input.position.xy, depth, 1.0);
                output.uv = input.uv * _UvTransform.xy + _UvTransform.zw;
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                float inside = step(0.0, input.uv.x) * step(input.uv.x, 1.0)
                             * step(0.0, input.uv.y) * step(input.uv.y, 1.0);
                return float4(tex2D(_MainTex, input.uv).rgb * inside, _Alpha);
            }
            ENDHLSL
        }
    }
}
