// Initial Quest loading HUD only. Preserve the native Sprite colour/coverage
// convention, but make foreground composition independent of retained scene
// depth and of the menu capture which begins before its handoff is complete.
// Desktop and later native scenario spinners keep their existing program.
Shader "GloomhavenVR/QuestLoadingSpinner"
{
    Properties
    {
        _MainTex ("Native loading Sprite atlas", 2D) = "white" {}
        _Color ("Native tint and animated opacity", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct Input
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Output vert(Input input)
            {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Output, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv; // The owned mesh already carries the exact native atlas crop.
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                fixed4 colour = tex2D(_MainTex, input.uv) * input.color;
                colour.rgb *= colour.a;
                return colour;
            }
            ENDCG
        }
    }
    Fallback Off
}
