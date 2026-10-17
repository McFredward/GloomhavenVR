Shader "SceneOutputOwned/Paint"
{
    Properties { _Eye("Owned label",Float)=0 _Phase("Current paint",Float)=0 }
    SubShader
    {
        Cull Off ZTest Always ZWrite Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float _Eye,_Phase;
            float4 frag(v2f_img i):SV_Target
            {
                float2 q=float2(i.uv.x,1-i.uv.y);
                float a1=q.x<.45?.5:0,a2=q.y>.35&&q.y<.75?.375:0;
                float3 c1=float3(.6,.2,.1+_Eye*.2),c2=float3(.1+_Eye*.3,.5,.25);
                float3 e=c2*a2+c1*a1*(1-a2);float a=a2+a1*(1-a2);
                if(q.x>.65&&q.x<.9&&q.y>.05&&q.y<.65)e+=float3(.25+_Eye*.1,.125,.0625);
                if(q.x>.92){a=fmod(floor(q.y*2048),7)<3?.03125:0;e=float3(a*.5,a*.25,a*.75);}
                e.r+=_Phase*.125;
                return float4(e,a);
            }
            ENDCG
        }
    }
}
