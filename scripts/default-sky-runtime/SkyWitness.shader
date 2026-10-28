// Explicit GL geometry/depth/UV witness. Original Windows programs remain in
// the shipped native bank; this is never production art or a D3D11 pixel claim.
Shader "AMP_SkyShader" {
 Properties { _Map00("Original cloud texture",2D)="white"{} _Clock("Witness clock",Float)=7 }
 SubShader { Tags {"Queue"="Geometry"} Pass { Cull Back ZWrite On ZTest LEqual
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _Map00;float _Clock;
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
 v2f vert(appdata_base v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord.xy;return o;}
 fixed4 frag(v2f i):SV_Target {fixed3 sky=tex2D(_Map00,i.uv+float2(_Clock*.003,0)).rgb;return fixed4(sky*.7+float3(.07,.11,.16),1);}
 ENDCG
 } }
}
