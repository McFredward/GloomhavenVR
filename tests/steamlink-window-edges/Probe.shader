Shader "SceneWindowEdge/Probe" {
 Properties { _MainTex("Source",2D)="black"{} }
 SubShader { Cull Off ZTest Always ZWrite Off Blend Off
 Pass { CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.5
 #include "UnityCG.cginc"
 Texture2D _MainTex; SamplerState linear_clamp_sampler;
 float4 frag(v2f_img i):SV_Target { float a=_MainTex.SampleLevel(linear_clamp_sampler,i.uv,0).a;return float4(a,a,a,1); }
 ENDCG }
 Pass { CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.5
 #include "UnityCG.cginc"
 Texture2D _MainTex; SamplerState linear_clamp_sampler;
 float4 frag(v2f_img i):SV_Target {
 float2 p=clamp(float2(i.uv.x,1-i.uv.y)*1024,.5,1023.5);
 float tile=min(3,floor(p.y/256));float2 origin=float2(fmod(tile,2)*1056+16,tile<2?48:2448);
 float2 uv=(origin+float2(p.x,p.y-tile*256))/float2(2112,2752);uv.y=1-uv.y;
 float a=_MainTex.SampleLevel(linear_clamp_sampler,uv,0).g;return float4(a,a,a,1); }
 ENDCG }
 Pass { CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.5
 #include "UnityCG.cginc"
 Texture2D _MainTex; SamplerState linear_clamp_sampler;
 float4 frag(v2f_img i):SV_Target { float2 p=float2(32+i.uv.x*2048,352+(1-i.uv.y)*2048);float2 uv=p/float2(2112,2752);uv.y=1-uv.y;return _MainTex.SampleLevel(linear_clamp_sampler,uv,0); }
 ENDCG }
 }
}
