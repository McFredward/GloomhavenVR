Shader "ScenePayloadDraft/Probe"
{
    Properties { _MainTex ("Owned RT",2D)="black"{} _PointUV ("Texel center",Vector)=(0,0,0,0) }
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
            float4 _PointUV;
            float4 frag(v2f_img i):SV_Target
            {
                return tex2Dlod(_MainTex,float4(_PointUV.xy,0,0));
            }
            ENDCG
        }
    }
}
