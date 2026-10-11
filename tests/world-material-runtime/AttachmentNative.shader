// A GL fragment-branch surrogate derived from the original native HIGH DXBC,
// not a replacement game shader. Lighting/art and the simplex noise function are
// outside this fixture: _NativeNoise supplies a valid noise value at this point.
// Both sides of its M>0 branch and exact native foundation/screen term execute.
Shader "Amp_Basic_WallFade"
{
    Properties
    {
        _MainTex("Original albedo",2D)="white"{}
        _ToggleWallfade("Authored native fade alias",Float)=0
        _WallFade_On("Authored native keyword gate",Float)=1
        _Tint("Tint",Color)=(.8,.4,.2,1)
        _Cutoff("Original authored mask clip",Float)=.5
        _EnableOcclusionMap("Native map scale",Float)=1
        ToggleWallFade("Native enable",Integer)=0
        _TilesOcclusionMap("Native tile coverage",2D)="black"{}
        _NativeNoise("Native simplex sample boundary",Float)=.03
        _NativeToggleVariant("Native N_MRAO output saturation",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Geometry" "RenderType"="Opaque"}
        Pass
        {
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _TilesOcclusionMap, _MainTex;
            float4 _Tint;
            float _Cutoff,_EnableOcclusionMap,_NativeNoise,_NativeToggleVariant;
            int ToggleWallFade;
            struct v2f {float4 pos:SV_POSITION;float4 screen:TEXCOORD0;float3 world:TEXCOORD1;};
            v2f vert(appdata_base v)
            {v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.screen=ComputeScreenPos(o.pos);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;}
            fixed4 frag(v2f i):SV_Target
            {
                float2 uv=i.screen.xy/(i.screen.w+1e-11);
                float4 occlusion=tex2D(_TilesOcclusionMap,uv);
                // Alpha-zero maps always take the native 1-r branch at a visible fragment.
                float M=(1-occlusion.r)*_EnableOcclusionMap;
                float distanceTerm=min(distance(i.world,_WorldSpaceCameraPos)*.02,1);
                float aspect=_ScreenParams.x/_ScreenParams.y;
                float radial=min(length(float2(aspect*uv.x*.5-aspect*.25,uv.y*.5-.25)),1);
                float foundation=min(max(1-i.world.y,0),5)/3;
                float t=min(3.333333*min(pow(distanceTerm+radial,8)+foundation,1),1);
                float S=t*t*(3-2*t);
                float maxMS=max(M,S);
                float A=maxMS+42*_NativeNoise*(1-maxMS);
                float B=M>0?1:S;
                float nativeClip=1+ToggleWallFade*(A*B-1);
                // N_MRAO DXBC uses mad_sat for the same value; native HIGH does not.
                if (_NativeToggleVariant>0) nativeClip=saturate(nativeClip);
                clip(nativeClip-_Cutoff);
                return _Tint * tex2D(_MainTex, float2(.5,.5));
            }
            ENDCG
        }
    }
    Fallback Off
}
