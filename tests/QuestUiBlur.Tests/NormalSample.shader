Shader "Hidden/QuestBlurNormalSampleProbe"
{
    Properties { _MainTex ("Raw imported normal", 2D) = "white" { } }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend One Zero
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img input) : SV_Target { return tex2D(_MainTex, input.uv); }
            ENDCG
        }
    }
}
