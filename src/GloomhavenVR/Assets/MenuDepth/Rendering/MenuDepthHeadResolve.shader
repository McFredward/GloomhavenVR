Shader "GloomhavenVR/MenuDepthHeadResolve"
{
    Properties { _MainTex ("Supersampled color",2D)="black" {} }
    SubShader
    {
        Cull Off ZTest Always ZWrite On Blend Off ColorMask RGBA
        Pass
        {
            CGPROGRAM
            #pragma vertex resolveVertex
            #pragma fragment resolveHead
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            Texture2D _GvrResolveDepth;
            SamplerState Gvr_point_clamp_sampler;
            float4 _MainTex_TexelSize, _GvrResolveUV, _GvrResolveRect;
            float _GvrResolveStep;
            struct result { float4 color:SV_Target; float depth:SV_Depth; };
            v2f_img resolveVertex(appdata_img v)
            {
                // The viewport is the complete destination. Place this exact
                // pixel rectangle in canonical sampled-RT coordinates; this
                // avoids any API-specific nonzero viewport-origin conversion.
                float2 headUV=_GvrResolveRect.xy+v.texcoord*_GvrResolveRect.zw;
                v2f_img o;o.pos=float4(headUV*2-1,0,1);o.uv=v.texcoord;
                #if UNITY_UV_STARTS_AT_TOP
                    o.pos.y=-o.pos.y;
                #endif
                return o;
            }
            result resolveHead(v2f_img i)
            {
                float2 uv=i.uv*_GvrResolveUV.xy;
                result o;o.color=tex2D(_MainTex,uv);
                float2 step=abs(_MainTex_TexelSize.xy)*_GvrResolveStep;
                float a=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(-1,-1),0).r;
                float b=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(-1,1),0).r;
                float c=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(1,-1),0).r;
                float d=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(1,1),0).r;
                #if defined(UNITY_REVERSED_Z)
                    o.depth=max(max(a,b),max(c,d));
                #else
                    o.depth=min(min(a,b),min(c,d));
                #endif
                return o;
            }
            ENDCG
        }
    }
}
