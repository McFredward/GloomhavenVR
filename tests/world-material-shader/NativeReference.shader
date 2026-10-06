// Independent GL reconstruction of the addressed native main-view parameter
// branch, not original Windows bytecode or full PBR/lighting. Native HIGH noise is
// a parameterized sample here; the separate source-bound noise probe checks DXBC
// float32 samples. See native-contract.json for exact original hashes/offsets.
Shader "Fixture/NativeWorldParameters"
{
    Properties
    {
        _MainTex("Albedo",2D)="white"{}
        _texcoord("HIGH UV",2D)="white"{}
        _Tint("AMP",Color)=(1,1,1,0)
        _Color("Unity",Color)=(1,1,1,1)
        _Diffuse_Boost("Boost",Float)=1
        _Desaturation("Desat",Float)=0
        _UVTiling("Tiling",Float)=1
        _UV_Offset("Offset",Float)=0
        _WorldSpace_tiling("World tile",Float)=1
        _WorldSpace_FallOff("World exponent",Float)=.8
        _Cutout("LOW alpha",Float)=1
        _Cutoff("Clip",Float)=.5
        _IsDimmed("Dim",Float)=0
        _DimmFactor("Dim grey",Float)=.3
        _GHVRWorldNativeRoute("Route",Float)=1
        _GHVRWorldNeverFade("Floor",Float)=0
        _NativeNoise("Native simplex boundary",Float)=.003
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex referenceVertex
            #pragma fragment referenceFragment
            #pragma multi_compile_fog
            #pragma multi_compile __ _WORLDSPACE_ON
            #pragma multi_compile __ _DIFUSE_ALPHA_ON_ON
            #pragma multi_compile __ _DESATURATION_ON
            #pragma multi_compile __ _WALLFADE_ON_ON
            #pragma multi_compile __ _TOGGLEWALLFADE_ON
            #pragma multi_compile __ _TOGGLEWALLFADEOFF_ON
            #pragma multi_compile __ _ALPHATEST_ON
            #pragma multi_compile __ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #include "UnityCG.cginc"
            sampler2D _MainTex, _TilesOcclusionMap;
            float4 _Tint,_Color,_MainTex_ST,_texcoord_ST;
            float _UVTiling,_UV_Offset,_WorldSpace_tiling,_WorldSpace_FallOff;
            float _Desaturation,_Diffuse_Boost,_IsDimmed,_DimmFactor,_Cutoff,_Cutout;
            float _GHVRWorldNativeRoute,_GHVRWorldNeverFade,_NativeNoise,_EnableOcclusionMap;
            int ToggleWallFade;
            struct Input {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};
            struct Pixel {float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float4 screen:TEXCOORD3;UNITY_FOG_COORDS(4)};
            Pixel referenceVertex(Input v)
            {
                Pixel p;
                p.position=UnityObjectToClipPos(v.vertex);
                p.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                p.normal=UnityObjectToWorldNormal(v.normal);
                bool high=_GHVRWorldNativeRoute==1||_GHVRWorldNativeRoute==3||_GHVRWorldNativeRoute==5;
                p.uv=high?(v.uv*_texcoord_ST.xy+_texcoord_ST.zw)*_UVTiling+_UV_Offset:v.uv*_MainTex_ST.xy+_MainTex_ST.zw;
                p.screen=ComputeScreenPos(p.position);
                UNITY_TRANSFER_FOG(p,p.position);
                return p;
            }
            float4 referenceFragment(Pixel p):SV_Target
            {
                int route=(int)_GHVRWorldNativeRoute;
                bool low=route==2||route==4||route==6;
                float4 tex=tex2D(_MainTex,p.uv);
                #ifdef _WORLDSPACE_ON
                if(route<9)
                {
                    float3 weight=low?abs(p.normal):pow(abs(p.normal),_WorldSpace_FallOff);
                    float total=weight.x+weight.y+weight.z+.00001;
                    float scale=low?_MainTex_ST.x:_WorldSpace_tiling;
                    float3 location=p.world*scale;
                    float4 x=tex2D(_MainTex,float2(sign(p.normal.x)*location.z,location.y));
                    float4 y=tex2D(_MainTex,float2(sign(p.normal.y)*location.x,location.z));
                    float4 z=tex2D(_MainTex,float2(-sign(p.normal.z)*location.x,location.y));
                    tex=(x*weight.x+y*weight.y+z*weight.z)/total;
                }
                #endif
                bool lowFade=false,highFade=false;
                #ifdef _TOGGLEWALLFADE_ON
                lowFade=route==2;
                #endif
                #ifdef _WALLFADE_ON_ON
                highFade=route==1;
                #endif
                #ifndef _TOGGLEWALLFADEOFF_ON
                lowFade=lowFade||route==4;highFade=highFade||route==3;
                #endif
                float fade=1;
                if(_GHVRWorldNeverFade<.5&&ToggleWallFade!=0&&(highFade||(lowFade&&p.world.y>=.4)))
                {
                    float2 screen=p.screen.xy/(p.screen.w+1e-11);
                    float4 map=tex2D(_TilesOcclusionMap,screen);
                    if(route==2)map=map*_EnableOcclusionMap+(1-_EnableOcclusionMap);
                    float nativeDepth=.5+.5*p.screen.z/p.screen.w;
                    float mask=map.a>=nativeDepth?1:1-map.r;
                    if(lowFade)clip(mask-(route==2?_Cutout:_Cutoff));
                    else
                    {
                        float dist=min(length(p.world-_WorldSpaceCameraPos)*.02,1);
                        float aspect=_ScreenParams.x/_ScreenParams.y;
                        float radius=min(length((screen-.5)*float2(aspect,.5)*float2(.5,1)),1);
                        float baseHeight=clamp(1-p.world.y,0,5)/3;
                        float t=min(min(pow(dist+radius,8)+baseHeight,1)*3.333333,1);
                        float screenMask=t*t*(3-2*t);
                        float M=mask*_EnableOcclusionMap;
                        float n=_NativeNoise*42;
                        float A=max(M,screenMask)*(1-n)+n;
                        float B=M>0?1:screenMask;
                        fade=1+ToggleWallFade*(A*B-1);
                    }
                }
                if(route==1)
                {
                    float alpha=1;
                    #ifdef _DIFUSE_ALPHA_ON_ON
                    alpha=tex.a;
                    #endif
                    clip(saturate(alpha*fade)-_Cutoff);
                }
                if(route==3||route==5)clip(fade-_Cutoff);
                #ifdef _DIFUSE_ALPHA_ON_ON
                if(route==2)clip(tex.a-_Cutout);
                #endif
                if(route==6)clip(_Cutout-tex.a);
                #ifdef _ALPHATEST_ON
                if(route==9)
                {
                    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
                    clip(_Color.a-_Cutoff);
                    #else
                    clip(tex.a*_Color.a-_Cutoff);
                    #endif
                }
                #endif
                float3 tinted=tex.rgb*(route>=9?_Color.rgb:_Tint.rgb);
                if(route<9)
                {
                    float grey=dot(tinted,float3(.299,.587,.114));
                    if(!low)tinted=(tinted+(grey-tinted)*_Desaturation)*_Diffuse_Boost;
                    #ifdef _DESATURATION_ON
                    if(low)tinted=tinted+(grey-tinted)*_Desaturation;
                    #endif
                    if(route==1)
                    {
                        float dimGrey=dot(tinted,float3(.299,.587,.115))*_DimmFactor;
                        tinted=tinted+(dimGrey-tinted)*_IsDimmed;
                    }
                }
                float4 output=float4(tinted,1);
                UNITY_APPLY_FOG(p.fogCoord,output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
