Shader "Hidden/GloomhavenVR/ViewportWitness"
{
    Properties { _DepthOnly ("Depth witness", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull Off ZTest Always ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _GHVRActorForegroundDepth;
            float4 _GHVRActorForegroundSize;
            float _GHVRActorForegroundReady, _DepthOnly;
            struct Input { float4 vertex : POSITION; };
            struct Output { float4 position : SV_POSITION; };
            Output vert(Input input)
            {
                Output output;
                output.position = float4(input.vertex.xy, 0.5, 1);
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                if (_DepthOnly > 0.5) return fixed4(0,0,0,1);
                if (_GHVRActorForegroundReady < 0.5) return fixed4(0,0,1,1);
                float raw = SAMPLE_DEPTH_TEXTURE(_GHVRActorForegroundDepth,
                    input.position.xy * _GHVRActorForegroundSize.xy);
                #if defined(UNITY_REVERSED_Z)
                    bool covered = raw > 0.1;
                #else
                    bool covered = raw < 0.9;
                #endif
                return covered ? fixed4(0,1,0,1) : fixed4(1,0,0,1);
            }
            ENDCG
        }
    }
}
