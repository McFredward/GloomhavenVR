Shader "FullCoverageOwned/Surface" {
 Properties {_Color("RGBA",Color)=(.2,.4,.6,1) _Mode("Alpha mode",Float)=0 _Additive("Additive",Float)=0}
 SubShader {Tags {"Queue"="Transparent"} Cull Off ZWrite Off ZTest LEqual
 Blend One OneMinusSrcAlpha,One OneMinusSrcAlpha
 Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;float _Mode,_Additive;
 struct V{float4 vertex:POSITION;float2 uv:TEXCOORD0;};struct F{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
 F vert(V v){F o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
 float4 frag(F i):SV_Target{float a=_Color.a;if(_Mode>.5&&_Mode<1.5)a*=i.uv.x;if(_Mode>1.5)a*=.1+.35*i.uv.x+.55*i.uv.y;return float4(_Color.rgb*(_Additive>.5?1:a),a);}
 ENDCG}
 }
}
