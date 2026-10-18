Shader "RobustCoverageOwned/Paint"
{
    Properties { _Eye("Independent eye label",Float)=0 }
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
            float _Eye;
            float4 frag(v2f_img i):SV_Target
            {
                float2 q=float2(i.uv.x,1-i.uv.y);
                float a=1;
                if(all(q>=.125)&&all(q<.375))a=.25;
                else if(all(q>=.375)&&all(q<.625))a=.5;
                else if(all(q>=.625)&&all(q<.875))a=.75;
                else if(q.x>=.625&&q.x<.875&&q.y>=.125&&q.y<.375)
                    return float4(.2+_Eye*.1,.3,.4,0);
                else return float4((128+_Eye*127)/255,(128+_Eye*127)/255,(128+_Eye*127)/255,1);
                return float4(float3(.2+_Eye*.2,.4,.6)*a,a);
            }
            ENDCG
        }
    }
}
