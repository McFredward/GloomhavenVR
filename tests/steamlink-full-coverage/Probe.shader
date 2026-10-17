Shader "FullCoverageOwned/Probe" {
 Properties {_MainTex("Current atlas",2D)="black"{} _Legacy("Legacy",Float)=0}
 SubShader {Cull Off ZTest Always ZWrite Off Blend Off
 Pass {CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.5
 #include "UnityCG.cginc"
 Texture2D _MainTex;SamplerState linear_clamp_sampler;float _Legacy;float4 _Origin0,_Origin1,_Origin2,_Origin3;
 float4 frag(v2f_img i):SV_Target{
 float2 q=float2(i.uv.x,1-i.uv.y);float2 color,mask,canvas;
 if(_Legacy>.5){canvas=float2(2112,2752);color=float2(32,352)+q*2048;float2 p=clamp(q*1024,.5,1023.5);float tile=min(3,floor(p.y/256));mask=float2(fmod(tile,2)*1056+16,tile<2?48:2448)+float2(p.x,p.y-tile*256);}
 else{canvas=3168; color=560+q*2048;float2 p=clamp(q*2048,.5,2047.5);float tile=min(3,floor(p.y/512));float2 local=float2(p.x,p.y-tile*512);if(tile==0)mask=_Origin0.xy+local;else if(tile==1)mask=_Origin1.xy+float2(local.y,2048-local.x);else if(tile==2)mask=_Origin2.xy+local;else mask=_Origin3.xy+float2(local.y,2048-local.x);}
 float2 cu=color/canvas,mu=mask/canvas;cu.y=1-cu.y;mu.y=1-mu.y;
 return float4(_MainTex.SampleLevel(linear_clamp_sampler,cu,0).rgb,_MainTex.SampleLevel(linear_clamp_sampler,mu,0).g);
 }
 ENDCG}
 }
}
