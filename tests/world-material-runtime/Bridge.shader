Shader "Fixture/WorldMaterialBridge"
{
    Properties { _MainTex("Texture",2D)="white" {} _Tint("Tint",Color)=(1,1,1,1) _GHVRWorldMaterialMode("Mode",Float)=0 _GHVRWorldNativeRoute("Route",Float)=0 }
    SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile __ _WALLFADE_ON_ON
        #pragma multi_compile __ _WORLDSPACE_ON
        #pragma multi_compile __ _DIFUSE_ALPHA_ON_ON
        #pragma multi_compile __ _DESATURATION_ON
        #pragma multi_compile __ _TOGGLEWALLFADE_ON
        #pragma multi_compile __ _TOGGLEWALLFADEOFF_ON
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _Tint,_MainTex_ST;
        struct v2f { float4 vertex:SV_POSITION;float2 uv:TEXCOORD0; };
        v2f vert(appdata_base v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.texcoord,_MainTex);return o;}
        float4 frag(v2f i):SV_Target{return tex2D(_MainTex,i.uv)*_Tint;}
        ENDCG } }
    Fallback Off
}
