Shader "NativePass658" {
 Properties { _MainTex("Original sprite",2D)="white" {} }
 SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha One
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
 struct output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
 output vert(appdata v) { output o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o; }
 fixed4 frag(output i):SV_Target { float edge=min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));clip(.03-edge);return i.color; }
 ENDCG } }
}