// A declared GPU shader boundary. The original native Highlight and mask write
// the real _RectFormat property; tests verify its exact values independently.
Shader "GVR/RectFormat666" {
Properties { _RectFormat ("Rectangle",Vector)=(0,0,0,0) _Color ("Color",Color)=(1,1,1,1) }
SubShader { Tags { "Queue"="Transparent" } Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
Pass { CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct appdata { float4 vertex:POSITION;float4 color:COLOR; };
struct v2f { float4 vertex:SV_POSITION;float4 color:COLOR; };
float4 _RectFormat;
v2f vert(appdata v) { v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;return o; }
fixed4 frag(v2f i):SV_Target { return _RectFormat.x>0&&_RectFormat.y>0?i.color:fixed4(0,0,0,0); }
ENDCG } }
}
