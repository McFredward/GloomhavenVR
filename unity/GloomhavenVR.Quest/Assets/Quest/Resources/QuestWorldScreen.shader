Shader "Hidden/GloomhavenVR/QuestWorldScreen"
{
    Properties
    {
        _MainTex ("Owned left or mono capture", 2D) = "black" {}
        _RightTex ("Owned right capture", 2D) = "black" {}
        _StereoCapture ("Shared stereo routing active", Float) = 0
    }
    SubShader
    {
        Pass
        {
            // Preserve the original opaque world screen's depth/alpha policy.
            ZTest Always Cull Off ZWrite Off Blend One Zero
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"

            // These are owned ordinary capture textures, never XR backbuffers.
            // UNITY_DECLARE_SCREENSPACE_TEXTURE would incorrectly turn them into
            // sampler2DArray when the headset uses instancing or multiview.
            sampler2D _MainTex, _RightTex;
            float4 _MainTex_ST;
            float _StereoCapture;
            struct Input
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Output vert(Input input)
            {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Output, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.position);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            float4 frag(Output input) : SV_TARGET
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float eye = 0.0;
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    eye = unity_StereoEyeIndex * _StereoCapture;
                #endif
                return float4(lerp(tex2D(_MainTex, input.uv).rgb,
                    tex2D(_RightTex, input.uv).rgb, eye), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
