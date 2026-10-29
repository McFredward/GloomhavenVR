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
            Texture2D _GvrResolveColor;
            float4x4 _GvrHeadSampleInverse, _GvrHeadGlassInverse, _GvrHeadAnnotationInverse[8];
            float4 _GvrHeadGlassRect, _GvrHeadAnnotationRects[8];
            float3 _GvrHeadSampleEye, _GvrPlanePoint, _GvrPlaneNormal;
            float _GvrHeadClipActive, _GvrHeadAnnotationCount;
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
            bool rayInside(float4x4 inverseFrame, float4 rect, float3 eye, float3 position)
            {
                float3 origin=mul(inverseFrame,float4(eye,1)).xyz;
                float3 destination=mul(inverseFrame,float4(position,1)).xyz;
                float3 ray=destination-origin;
                if(abs(ray.z)<.00000001)return false;
                float along=-origin.z/ray.z;
                float2 plane=origin.xy+ray.xy*along;
                return along>=0&&all(plane>=rect.xy)&&all(plane<=rect.zw);
            }
            bool permitted(float2 uv,float raw,float alpha)
            {
                if(_GvrHeadClipActive<.5)return true;
                bool valid;
                #if defined(UNITY_REVERSED_Z)
                    valid=raw>.000001;
                #else
                    valid=raw<.999999;
                #endif
                float z=raw;
                #if !defined(UNITY_REVERSED_Z)
                    z=lerp(UNITY_NEAR_CLIP_VALUE,1,z);
                #endif
                float4 position=mul(_GvrHeadSampleInverse,float4(uv*2-1,z,1));position/=position.w;
                float side=dot(_GvrHeadSampleEye-_GvrPlanePoint,_GvrPlaneNormal)<0?-1:1;
                bool foreground=valid&&(abs(dot(_GvrHeadSampleEye-_GvrPlanePoint,_GvrPlaneNormal))<.00001
                    ||dot(position.xyz-_GvrPlanePoint,_GvrPlaneNormal)*side>.000001);
                if(foreground||rayInside(_GvrHeadGlassInverse,_GvrHeadGlassRect,_GvrHeadSampleEye,position.xyz))return true;
                if(alpha<=.001)return false;
                for(int a=0;a<8;a++)
                {
                    if(a>=_GvrHeadAnnotationCount)break;
                    if(rayInside(_GvrHeadAnnotationInverse[a],_GvrHeadAnnotationRects[a],_GvrHeadSampleEye,position.xyz))return true;
                }
                return false;
            }
            result quarter(float2 uv)
            {
                result o;
                o.color=_GvrResolveColor.SampleLevel(Gvr_point_clamp_sampler,uv,0);
                o.depth=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv,0).r;
                if(!permitted(uv,o.depth,o.color.a))
                {
                    o.color=0;
                    #if defined(UNITY_REVERSED_Z)
                        o.depth=0;
                    #else
                        o.depth=1;
                    #endif
                }
                return o;
            }
            result resolveHead(v2f_img i)
            {
                float2 uv=i.uv*_GvrResolveUV.xy;
                float2 step=abs(_MainTex_TexelSize.xy)*_GvrResolveStep;
                result a=quarter(uv+step*float2(-1,-1));
                result b=quarter(uv+step*float2(-1,1));
                result c=quarter(uv+step*float2(1,-1));
                result d=quarter(uv+step*float2(1,1));
                result o;o.color=(a.color+b.color+c.color+d.color)*.25;
                #if defined(UNITY_REVERSED_Z)
                    o.depth=max(max(a.depth,b.depth),max(c.depth,d.depth));
                #else
                    o.depth=min(min(a.depth,b.depth),min(c.depth,d.depth));
                #endif
                return o;
            }
            ENDCG
        }
    }
}
