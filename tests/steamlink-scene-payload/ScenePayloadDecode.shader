Shader "ScenePayloadDraft/Decode"
{
    Properties { _MainTex ("Draft atlas", 2D) = "black" {} }
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
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 q = clamp(float2(i.uv.x,1-i.uv.y),0.5/2048,1-0.5/2048);
                float2 color = (float2(32,352)+q*2048)/float2(2112,2752);
                float3 e = tex2Dlod(_MainTex,float4(color.x,1-color.y,0,0)).rgb;
                float2 p = q*1024-0.5;
                float t = min(floor(max(p.y,0)/256),3);
                float2 origin = float2(fmod(t,2)*1056+16,t < 2 ? 48 : 2448);
                float2 aUV = (origin+float2(p.x,p.y-t*256)+0.5)/float2(2112,2752);
                float a = tex2Dlod(_MainTex,float4(aUV.x,1-aUV.y,0,0)).g;
                return float4(e,a);
            }
            ENDCG
        }
    }
}
