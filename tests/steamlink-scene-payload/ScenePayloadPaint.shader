Shader "ScenePayloadDraft/Paint"
{
    Properties { _Eye ("Owned eye label",Float)=0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        float _Eye;
        float4 scene(float2 uv)
        {
            float2 q=float2(uv.x,1-uv.y);
            // Explicit known contribution operations: transparent clear, two
            // straight-over layers, opaque board, then additive emission.
            float a1=q.x<0.45 ? 0.5 : 0;
            float a2=q.y>0.35 && q.y<0.75 ? 0.375 : 0;
            float3 c1=float3(0.6,0.2,0.1+_Eye*0.2);
            float3 c2=float3(0.1+_Eye*0.3,0.5,0.25);
            float3 e=c1*a1;
            float a=a1;
            e=c2*a2+e*(1-a2); a=a2+a*(1-a2);
            if(q.x>0.15 && q.x<0.35 && q.y>0.15 && q.y<0.3)
            {e=float3(0.2,0.3+_Eye*0.2,0.4);a=1;}
            // Strong emission at A=0 in top-right, and over glass in the middle.
            if(q.x>0.65 && q.x<0.9 && q.y>0.05 && q.y<0.65)
                e+=float3(0.25+_Eye*0.1,0.125,0.0625);
            // Asymmetric high frequency row detail exposes all mask row seams.
            float detail=fmod(floor(q.y*2048),7)<3 ? 0.03125 : 0;
            if(q.x>0.92) {a=detail;e=float3(detail*0.5,detail*0.25,detail*0.75);}
            return float4(e,a);
        }
        float4 contribution(v2f_img i):SV_Target
        {
            // Deliberately unrelated alpha: a tap of ordinary source.a is wrong.
            return float4(scene(i.uv).rgb,0.875);
        }
        float4 coverage(v2f_img i):SV_Target
        {
            // Only G is the declared coverage plane. Other channels are decoys.
            return float4(0.9375,scene(i.uv).a,0.0625,0.8125);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment contribution
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment coverage
            #pragma target 3.0
            ENDCG
        }
    }
}
