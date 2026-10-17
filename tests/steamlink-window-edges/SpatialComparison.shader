Shader "SceneWindowEdge/SpatialComparison"
{
    Properties { _MainTex ("Current resolved scene contribution and coverage",2D)="black"{} _Kernel("Spatial kernel",Float)=1 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            Texture2D _MainTex;
            SamplerState linear_clamp_sampler;
            float4 _MainTex_TexelSize;
            float _Kernel;

            float4 scene(float2 topDown)
            {
                float2 uv=float2(topDown.x,1-topDown.y);
                #if UNITY_UV_STARTS_AT_TOP
                if(_MainTex_TexelSize.y<0)uv.y=1-uv.y;
                #endif
                return _MainTex.SampleLevel(linear_clamp_sampler,uv,0);
            }
            float coverageCell(float2 cell)
            {
                cell=clamp(cell,0,1023);
                float2 first=cell*2+0.5;
                // Four fixed 2048-scene-grid samples from the SAME source alpha.
                return saturate((scene(first/2048).a+scene((first+float2(1,0))/2048).a
                    +scene((first+float2(0,1))/2048).a+scene((first+1)/2048).a)*0.25);
            }
            float coverage(float2 global)
            {
                // A non-atlas-sized destination samples fractional virtual mask
                // cells. Reconstruct the pre-downsampled field, rather than
                // shifting its four-sample kernel continuously across the source.
                float2 p=global-0.5,lo=floor(p),f=frac(p);
                float a=coverageCell(lo),b=coverageCell(lo+float2(1,0));
                float c=coverageCell(lo+float2(0,1)),d=coverageCell(lo+1);
                if(_Kernel>2.5) {
                    float aL=coverageCell(lo+float2(-1,0)),cL=coverageCell(lo+float2(-1,1));
                    float aB=coverageCell(lo+float2(0,-1)),bB=coverageCell(lo+float2(1,-1));
                    float bR=coverageCell(lo+float2(2,0)),dR=coverageCell(lo+float2(2,1));
                    float cT=coverageCell(lo+float2(0,2)),dT=coverageCell(lo+float2(1,2));
                    float k=_Kernel-2;
                    float4 original=float4(a,b,c,d);
                    float4 mean=float4(aL+b+aB+c,a+bR+bB+d,cL+d+a+cT,c+dR+b+dT)*.25;
                    float4 low=min(original,min(min(float4(aL,a,cL,c),float4(b,bR,d,dR)),min(float4(aB,bB,a,b),float4(c,d,cT,dT))));
                    float4 high=max(original,max(max(float4(aL,a,cL,c),float4(b,bR,d,dR)),max(float4(aB,bB,a,b),float4(c,d,cT,dT))));
                    float4 sharpened=clamp(original+k*(original-mean),low,high);
                    return lerp(lerp(sharpened.x,sharpened.y,f.x),lerp(sharpened.z,sharpened.w,f.x),f.y);
                }
                bool binary=(a==0||a==1)&&(b==0||b==1)&&(c==0||c==1)&&(d==0||d==1);
                f=(_Kernel<1.5||binary)?step(.5,f):f*f*(3-2*f);
                return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);
            }
            float code(int i)
            {
                if(i==0)return 111;if(i==1)return 208;if(i==2)return 128;if(i==3)return 104;
                if(i==4)return 227;if(i==5)return 251;if(i==6)return 255;if(i==7)return 193;
                if(i==8)return 102;if(i==9)return 168;if(i==10)return 15;return 90;
            }
            float4 frag(v2f_img i):SV_Target
            {
                float2 p=float2(i.uv.x,1-i.uv.y)*float2(2112,2752);
                if(p.x>=32&&p.x<2080&&p.y>=352&&p.y<2400)
                    return float4(scene((p-float2(32,352))/2048).rgb,1);
                if(p.y>=2400&&p.y<2432&&p.x>=64&&p.x<64+12*160)
                {
                    float x=p.x-64;int cell=(int)floor(x/160);
                    if(x-cell*160<32){float g=code(cell)/255;return float4(g,g,g,1);}
                }
                float y=p.y<320?32:2432;
                if(p.y>=y&&p.y<y+288)
                {
                    float side=p.x<1056?0:1,t=(y==32?0:2)+side;
                    float2 global=float2(p.x-side*1056-16,p.y-y-16+t*256);
                    float a=coverage(global);return float4(a,a,a,1);
                }
                // No key-color opacity and no multiplication of RGB by A.
                float2 edge=clamp((p-float2(32,352))/2048,0.5/2048,1-0.5/2048);
                return float4(scene(edge).rgb,1);
            }
            ENDCG
        }
    }
}
