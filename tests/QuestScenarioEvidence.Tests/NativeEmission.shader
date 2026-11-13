// Controlled binding boundary, not an oracle for original native Amp math.
// The retained native floor fixture separately exercises its actual shader.
Shader "Amp_Basic" {
 Properties { _MainTex ("Source", 2D) = "white" {} _Tint ("Native tint", Color) = (1,1,1,0) _EmissionMap ("Native emission", Float) = 0 }
 SubShader { Pass {
  Cull Off
  CGPROGRAM
  #pragma vertex vert
  #pragma fragment frag
  #include "UnityCG.cginc"
  sampler2D _MainTex; float4 _Tint; float _EmissionMap;
  struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
  struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
  v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
  float4 frag(v2f i) : SV_Target { return float4(tex2D(_MainTex, i.uv).rgb * _Tint.rgb * _EmissionMap, 1); }
  ENDCG
 } }
}
