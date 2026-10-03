// A GL API/pixel fixture, not redistributed native game shader source. The actual shipped
// compiled shader's INSTANCING_ON variants are independently checked by the runner.
Shader "Amp_Basic_N_MRAO" {
 Properties { _Color("Color",Color)=(0.8,0.4,0.2,1) }
 SubShader { Tags { "RenderType"="Opaque" "Queue"="Geometry" } Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct input { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct output { float4 vertex:SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
 float4 _Color;
 output vert(input v) { UNITY_SETUP_INSTANCE_ID(v); output o; UNITY_TRANSFER_INSTANCE_ID(v,o); o.vertex=UnityObjectToClipPos(v.vertex); return o; }
 fixed4 frag(output i):SV_Target { UNITY_SETUP_INSTANCE_ID(i); return _Color; }
 ENDCG
 } }
}
