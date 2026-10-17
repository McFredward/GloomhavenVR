Shader "SceneOutputOwned/Edge"
{
    SubShader
    {
        Cull Off ZTest Always ZWrite Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex:POSITION):SV_POSITION{return UnityObjectToClipPos(vertex);}
            float4 frag():SV_Target{return float4(.95,.05,.7,1);}
            ENDCG
        }
    }
}
