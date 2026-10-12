Shader "GloomhavenVR/HealthbarDuplicateForward" {
 Properties { _Color ("Color",Color) = (0,0,0,1) }
 SubShader {
  Tags { "RenderType"="Opaque" }
  // Duplicate native-style names are deliberate: Add must not supply depth.
  Pass { Name "FORWARD" Tags { "LightMode"="ForwardAdd" } ZWrite Off
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
   fixed4 frag():SV_Target { return fixed4(0,0,0,1); }
   ENDCG
  }
  Pass { Name "FORWARD" Tags { "LightMode"="ForwardBase" } ZWrite On
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
   fixed4 frag():SV_Target { return fixed4(0,0,0,1); }
   ENDCG
  }
 }
}
