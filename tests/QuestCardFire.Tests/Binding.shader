Shader "QuestFixture/NativeFireBinding"
{
    Properties
    {
        _ParticleTexture ("Native particle", 2D) = "white" {}
        _FXAnim ("Native fire animation", Float) = 0
        _Glow ("Native glow", Float) = 1
        _TintColor ("Native tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 position:POSITION):SV_POSITION { return UnityObjectToClipPos(position); }
            fixed4 frag():SV_Target { return fixed4(1,1,1,1); }
            ENDCG
        }
    }
}
