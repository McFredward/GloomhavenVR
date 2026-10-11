// Explicit fresh-state fgFx execution port: native RestoreCard sets _FXAnim=0.
// The game's compiled effect shader is not executed by this fixture.
Shader "GloomhavenVR/FixtureNativeZeroFx661" {
 Properties { _FXAnim("FX animation",Float)=0 _MainTex("Native texture",2D)="white"{} }
 SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; };struct v2f { float4 vertex:SV_POSITION; };
   v2f vert(appdata v) { v2f o;o.vertex=UnityObjectToClipPos(v.vertex);return o; }
   fixed4 frag(v2f i):SV_Target { return fixed4(0,0,0,0); }
  ENDCG }
 }
}
