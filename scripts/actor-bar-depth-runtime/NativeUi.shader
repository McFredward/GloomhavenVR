Shader "Fixture/ActorBarNativeUI" {
 Properties { _MainTex("Atlas",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _ClipRect("Clip",Vector)=(-2,-1,2,1) _StencilComp("Stencil comparison",Float)=8 _Stencil("Stencil reference",Float)=0 _StencilOp("Stencil operation",Float)=0 _StencilWriteMask("Stencil write",Float)=255 _StencilReadMask("Stencil read",Float)=255 _ColorMask("Color mask",Float)=15 _ZTestMode("Z test",Float)=4 _GradientScale("Gradient scale",Float)=5 _TextureWidth("Texture width",Float)=256 _TextureHeight("Texture height",Float)=256 _FaceDilate("Face dilate",Float)=0 _OutlineWidth("Outline width",Float)=0 _OutlineSoftness("Outline softness",Float)=0 _ScaleRatioA("Scale ratio",Float)=1 }
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"} Pass {
 Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
 Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct app {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;}; struct v2f {float4 pos:SV_POSITION;float4 color:COLOR;}; float4 _Color;
 v2f vert(app v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.color=v.color*_Color;return o;}
 fixed4 frag(v2f i):SV_Target {return i.color;}
 ENDCG
 }} Fallback Off }
