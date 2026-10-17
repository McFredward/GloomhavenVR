Shader "LoadingSourceFixture/Opaque"
{
    Properties { _Color("Contribution",Color)=(0.3,0.4,0.8,0) }
    SubShader
    {
        Tags { "Queue"="Geometry" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 vert(float4 position:POSITION):SV_POSITION{return UnityObjectToClipPos(position);}
            float4 frag():SV_Target{return _Color;}
            ENDCG
        }
    }
}
