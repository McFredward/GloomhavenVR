Shader "GloomhavenVR/FixtureDepth728"
{
    Properties { _NativeDepth ("Actual native depth", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_NativeDepth);
            float4 frag(v2f_img input) : SV_Target
            {
                float depth = SAMPLE_DEPTH_TEXTURE(_NativeDepth, input.uv);
                return float4(depth, depth, depth, 1);
            }
            ENDCG
        }
    }
}
