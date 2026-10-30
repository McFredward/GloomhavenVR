// GL mechanism witness, not extracted native Windows fragment programs.
// Native UI_Blur declares GrabPass + One/Zero, LEqual, ZWriteOn, CullBack.
Shader "Custom/SimpleGrabPassBlur" {
 Properties { _Size("Size",Float)=2 }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Opaque" }
  GrabPass { "_ConfirmationGrab738" }
  Pass {
   ZWrite On ZTest LEqual Cull Back Blend One Zero
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _ConfirmationGrab738;
   float4 _ConfirmationGrab738_TexelSize;
   float _Size;
   struct appdata {float4 vertex:POSITION;};
   struct v2f {float4 vertex:SV_POSITION;float4 grab:TEXCOORD0;};
   v2f vert(appdata v) {v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.grab=ComputeGrabScreenPos(o.vertex);return o;}
   fixed4 frag(v2f i):SV_Target {
    float2 uv=i.grab.xy/i.grab.w;float2 step=_ConfirmationGrab738_TexelSize.xy*_Size;
    return (tex2D(_ConfirmationGrab738,uv+step)+tex2D(_ConfirmationGrab738,uv-step)
     +tex2D(_ConfirmationGrab738,uv+float2(step.x,-step.y))+tex2D(_ConfirmationGrab738,uv+float2(-step.x,step.y)))*.25;
   }
   ENDCG
  }
 }
}
