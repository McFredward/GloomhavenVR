Shader "SceneOutputOwned/Probe"
{
    Properties { _MainTex("Original resolved or packed RT",2D)="black"{} _PointUV("Center",Vector)=(0,0,0,0) }
    SubShader
    {
        Cull Off ZTest Always ZWrite Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            Texture2D _MainTex;SamplerState linear_clamp_sampler;
            float4 _PointUV;
            float4 frag(v2f_img i):SV_Target{return _MainTex.SampleLevel(linear_clamp_sampler,_PointUV.xy,0);}
            ENDCG
        }
    }
}
