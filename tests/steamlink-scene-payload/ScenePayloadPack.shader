Shader "ScenePayloadDraft/Pack"
{
    Properties { _MainTex ("Contribution", 2D) = "black" {} _CoverageTex ("Coverage G", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _CoverageTex;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = float2(i.uv.x, 1-i.uv.y) * float2(2112,2752);
                if (p.x >= 32 && p.x < 2080 && p.y >= 352 && p.y < 2400)
                {
                    float2 q = (p-float2(32,352))/2048;
                    float3 e = tex2Dlod(_MainTex,float4(q.x,1-q.y,0,0)).rgb;
                    return float4(e,1); // Never multiply contribution by coverage or source alpha.
                }
                float tileY = p.y < 320 ? 32 : 2432;
                if (p.y >= tileY && p.y < tileY+288)
                {
                    float side = p.x < 1056 ? 0 : 1;
                    float t = (tileY == 32 ? 0 : 2) + side;
                    float2 global = float2(p.x-side*1056-16, p.y-tileY-16+t*256);
                    float2 q = clamp(global/1024,0.5/1024,1-0.5/1024);
                    float a = tex2Dlod(_CoverageTex,float4(q.x,1-q.y,0,0)).g;
                    return float4(a,a,a,1);
                }
                // Explicitly NOT the current native presenter signature. No live
                // decoder can mistake this private draft for the synthetic v2.
                if (p.y >= 2720 && p.x < 12*64)
                {
                    float v = fmod(p.x,64)<32 ? (floor(p.x/64)+1)/16 : 0;
                    return float4(v,v,v,1);
                }
                // Continue scene edges in the adjacent gap. This is not geometry.
                float2 edge = clamp((p-float2(32,352))/2048,0.5/2048,1-0.5/2048);
                return float4(tex2Dlod(_MainTex,float4(edge.x,1-edge.y,0,0)).rgb,1);
            }
            ENDCG
        }
    }
}
