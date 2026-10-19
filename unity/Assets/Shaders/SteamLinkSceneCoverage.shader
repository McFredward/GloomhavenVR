// Same CameraTarget/depth/MSAA only; no copied depth or second scene render.
Shader "Hidden/GloomhavenVR/SteamLinkSceneCoverage"
{
    SubShader
    {
        Cull Off ZWrite Off ColorMask A Blend Off AlphaToMask Off
        Stencil { WriteMask 0 Comp Always Pass Keep Fail Keep ZFail Keep }
        CGINCLUDE
        #pragma target 3.5
        #include "UnityCG.cginc"
        float4 vert(uint id : SV_VertexID) : SV_POSITION
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            #if defined(UNITY_REVERSED_Z)
            const float farDepth = 0;
            #else
            const float farDepth = 1;
            #endif
            return float4(uv * 2 - 1, farDepth, 1);
        }
        float4 reset() : SV_Target { return 0; }
        float4 stamp() : SV_Target { return float4(0, 0, 0, 1); }
        float4 backdropVert(float4 vertex : POSITION) : SV_POSITION
        {
            return UnityObjectToClipPos(vertex);
        }
        ENDCG
        Pass
        {
            Name "RESET"
            ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment reset
            ENDCG
        }
        Pass
        {
            Name "STAMP"
            // Unity reverses ShaderLab's logical comparison for reversed Z.
            // Only raw far-plane vertex Z differs; never flip Greater again.
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment stamp
            ENDCG
        }
        Pass
        {
            Name "BACKDROP"
            // Same exact owned depthless quad, after RESET/STAMP. Keep native RGB,
            // depth, stencil, MSAA sample coverage and the earlier hand draw order.
            ZTest Always
            CGPROGRAM
            #pragma vertex backdropVert
            #pragma fragment stamp
            ENDCG
        }
    }
    Fallback Off
}
