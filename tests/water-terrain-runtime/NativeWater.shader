Shader "VFX/Water_Shd_Trans" {
    Properties {
        _Color_Tint("Water color",Color)=(.2,.4,.3,.7)
        _Smoothness("Smoothness",Float)=.7
        _Metallic("Metal",Float)=.5
        _Normal_Map("Normals",2D)="bump" {}
        _NormalTilings("Tilings",Vector)=(1,1,2,2)
        _Normal_Strength("Strength",Vector)=(1,1,1,1)
    }
    SubShader { Tags {"Queue"="Transparent"} Pass { CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 _Color_Tint;
        float4 vert(float4 vertex:POSITION):SV_POSITION {return UnityObjectToClipPos(vertex);}
        float4 frag():SV_Target {return _Color_Tint;}
        ENDCG } }
    Fallback Off
}
