Shader "Fixture/UnavailableWorld"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return vertex; }
            float4 frag() : SV_Target { return float4(1, 0, 1, 1); }
            ENDCG
        }
    }
    Fallback Off
}
