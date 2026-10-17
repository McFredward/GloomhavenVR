// Equal spatial resolution for scene RGB and independent scene coverage. No
// coverage box-downsample, spatial reconstruction kernel or opacity threshold.
Shader "GloomhavenVR/SteamLinkSceneFullAtlas"
{
    Properties { _MainTex ("Current resolved scene contribution and coverage",2D)="black"{} }
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

            float4 scene(float2 topDown)
            {
                float2 uv=float2(topDown.x,1-topDown.y);
                #if UNITY_UV_STARTS_AT_TOP
                if(_MainTex_TexelSize.y<0)uv.y=1-uv.y;
                #endif
                return _MainTex.SampleLevel(linear_clamp_sampler,uv,0);
            }
            float code(int i)
            {
                if(i==0)return 104;if(i==1)return 14;if(i==2)return 149;if(i==3)return 250;if(i==4)return 167;if(i==5)return 249;if(i==6)return 185;if(i==7)return 25;if(i==8)return 121;if(i==9)return 144;if(i==10)return 38;return 152;
            }
            float4 frag(v2f_img i):SV_Target
            {
                float2 p=float2(i.uv.x,1-i.uv.y)*3168;
                if(all(p>=560)&&all(p<2608))
                    return float4(scene((p-560)/2048).rgb,1);
                // Twelve independent grey format cells in the unused corner.
                float2 cell=floor((p-64)/36);
                float2 offset=p-64-cell*36;
                if(cell.x>=0&&cell.x<12&&cell.x==cell.y&&all(offset>=0)&&all(offset<32))
                {
                    float g=code((int)cell.x)/255;
                    return float4(g,g,g,1);
                }
                float2 global;
                bool data=true;
                if(p.x>=560&&p.x<2640&&p.y>=16&&p.y<560)
                    global=p-float2(576,32); // Rows 0..511, top.
                else if(p.x>=2608&&p.x<3152&&p.y>=560&&p.y<2640)
                    global=float2(2048-(p.y-576),512+(p.x-2624));
                else if(p.x>=528&&p.x<2608&&p.y>=2608&&p.y<3152)
                    global=p-float2(544,2624)+float2(0,1024);
                else if(p.x>=16&&p.x<560&&p.y>=528&&p.y<2608)
                    global=float2(2048-(p.y-544),1536+(p.x-32));
                else { global=0;data=false; }
                if(data)
                {
                    float2 q=clamp(global/2048,.5/2048,1-.5/2048);
                    float a=saturate(scene(q).a);
                    return float4(a,a,a,1);
                }
                float2 edge=clamp((p-560)/2048,.5/2048,1-.5/2048);
                return float4(scene(edge).rgb,1);
            }
            ENDCG
        }
    }
}
