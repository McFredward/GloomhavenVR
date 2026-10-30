// Independent physical-world reference: the back part of a real object is
// visible through the original rectangular glass aperture; its front part is
// unrestricted. No production capture/projection/shader code is used here.
Shader "GloomhavenVR.Tests/GlassAperture730"
{
    Properties { _MainTex ("Native surface", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4x4 _WorldToGlass;
            float4 _GlassRect;
            float3 _Eye;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 glass : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
                o.glass=mul(_WorldToGlass,mul(unity_ObjectToWorld,v.vertex)).xyz; return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float side=_Eye.z<0?-1:1;
                if(abs(_Eye.z)>.00001 && i.glass.z*side<=0)
                {
                    float3 ray=i.glass-_Eye;
                    float2 aperture=_Eye.xy-ray.xy*(_Eye.z/ray.z);
                    clip(min(min(aperture.x-_GlassRect.x,_GlassRect.z-aperture.x),
                        min(aperture.y-_GlassRect.y,_GlassRect.w-aperture.y)));
                }
                return tex2D(_MainTex,i.uv);
            }
            ENDCG
        }
    }
}
