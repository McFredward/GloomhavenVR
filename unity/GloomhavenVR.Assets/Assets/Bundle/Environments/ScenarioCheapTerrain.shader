// Optional 3D scenery lighting compromise. Original textured geometry, MPB inputs,
// bilinear wall map, native simplex/foundation/vignette and cutoff remain live.
// Normal/MRAO/detail/reflection texture work is omitted. No UI, actor or torch route.
Shader "GloomhavenVR/ScenarioCheapTerrain"
{
    Properties
    {
        _MainTex ("Original color texture", 2D) = "white" {}
        _Tint ("Original tint", Color) = (1,1,1,0)
        _Diffuse_Boost ("Original diffuse boost", Float) = 1
        _Desaturation ("Original desaturation", Range(0,1)) = 0
        _UVTiling ("Original UV tiling", Float) = 1
        _UV_Offset ("Original UV offset", Float) = 0
        _WorldSpace ("Original world projection", Float) = 0
        _WorldSpace_tiling ("Original world tiling", Float) = 1
        _WorldSpace_FallOff ("Original projection blend", Float) = .8
        _Difuse_Alpha_On ("Original color cutout", Float) = 0
        _Cutoff ("Original mask clip", Float) = .5
        _IsDimmed ("Original dimming", Float) = 0
        _DimmFactor ("Original dim amount", Float) = .3
        _GHVRTerrainNativeRoute ("Original clip route", Float) = 0
        _GHVRTerrainNeverFade ("Never-fade floor", Float) = 0
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Lighting.cginc"
    sampler2D _MainTex, _TilesOcclusionMap;
    float4 _MainTex_ST, _Tint;
    float _Diffuse_Boost, _Desaturation, _UVTiling, _UV_Offset;
    float _WorldSpace, _WorldSpace_tiling, _WorldSpace_FallOff;
    float _Difuse_Alpha_On, _Cutoff, _IsDimmed, _DimmFactor;
    float _EnableOcclusionMap, _GHVRTerrainNativeRoute, _GHVRTerrainNeverFade;
    int ToggleWallFade;
    // Native camera globals intentionally are NOT Properties. A material default
    // must never shadow the game's global map/enable when no renderer MPB exists.
    float3 Mod289(float3 x) { return x - floor(x * .003460208) * 289.; }
    float4 Mod289(float4 x) { return x - floor(x * .003460208) * 289.; }
    float4 Permute(float4 x) { return Mod289((x * 34. + 1.) * x); }
    float4 NativeInvSqrt(float4 r) { return 1.792843 - .8537347 * r; }
    // Direct reconstruction of native HIGH/N_MRAO DXBC simplex branch. Constants,
    // corner order, world scales and time drift match the frame619 original blobs.
    float NativeSimplex(float3 v)
    {
        float3 i = floor(v + dot(v, .3333333));
        float3 x0 = v - i + dot(i, .1666667);
        float3 g = step(x0.yzx, x0.xyz);
        float3 l = 1. - g;
        float3 i1 = min(g.xyz, l.zxy);
        float3 i2 = max(g.xyz, l.zxy);
        float3 x1 = x0 - i1 + .1666667;
        float3 x2 = x0 - i2 + .3333333;
        float3 x3 = x0 - .5;
        i = Mod289(i);
        float4 p = Permute(Permute(Permute(i.z + float4(0., i1.z, i2.z, 1.))
            + i.y + float4(0., i1.y, i2.y, 1.)) + i.x + float4(0., i1.x, i2.x, 1.));
        float4 j = p - 49. * floor(p * .02040816);
        float4 x_ = floor(j * .1428571);
        float4 y_ = j - 7. * x_;
        float4 x = (x_ * 2. + .5) * .1428571 - 1.;
        float4 y = (y_ * 2. + .5) * .1428571 - 1.;
        float4 h = 1. - abs(x) - abs(y);
        float4 b0 = float4(x.xy, y.xy);
        float4 b1 = float4(x.zw, y.zw);
        float4 s0 = floor(b0) * 2. + 1.;
        float4 s1 = floor(b1) * 2. + 1.;
        float4 sh = -step(h, 0.);
        float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
        float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
        float3 p0 = float3(a0.xy, h.x);
        float3 p1 = float3(a0.zw, h.y);
        float3 p2 = float3(a1.xy, h.z);
        float3 p3 = float3(a1.zw, h.w);
        float4 norm = NativeInvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
        p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
        float4 m = max(.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.);
        m *= m;
        // Native A adds 42 * this value. Returning the unscaled dot preserves it.
        return dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
    }
    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 world : TEXCOORD1;
        float3 normal : TEXCOORD2;
        float4 screen : TEXCOORD3;
        float objectY : TEXCOORD4;
        UNITY_FOG_COORDS(5)
        UNITY_VERTEX_OUTPUT_STEREO
    };
    v2f vert(appdata v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = UnityObjectToClipPos(v.vertex);
        float2 uv = v.uv * _UVTiling + _UV_Offset;
        o.uv = TRANSFORM_TEX(uv, _MainTex);
        o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.normal = UnityObjectToWorldNormal(v.normal);
        o.screen = ComputeScreenPos(o.pos);
        o.objectY = v.vertex.y;
        UNITY_TRANSFER_FOG(o,o.pos);
        return o;
    }
    void NativeClip(v2f i)
    {
        if (_GHVRTerrainNeverFade > .5 || _GHVRTerrainNativeRoute < .5) return;
        // A solid wall has no map contribution. The HIGH/N_MRAO equation below
        // reduces exactly to clip(1 - cutoff) when its integer toggle is zero;
        // LOW performs no clip at all. Avoid its texture read, pow and simplex
        // work in this common case. Keep authored cutoffs above one effective,
        // and leave every active native dissolve/foundation route unchanged.
        if (ToggleWallFade == 0)
        {
            if (_GHVRTerrainNativeRoute >= 1.5) clip(1. - _Cutoff);
            return;
        }
        float2 uv = i.screen.xy / (i.screen.w + 1e-11);
        float4 occlusion = tex2D(_TilesOcclusionMap, uv);
        // Original map alpha holds native projected depth, not an opacity mask.
        float depth = i.screen.z / i.screen.w;
        #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
        // GL clip Z is -1..1; native map depth and the original D3D equation are 0..1.
        depth = depth * .5 + .5;
        #endif
        float m = occlusion.a >= depth ? 1. : 1. - occlusion.r;
        if (_GHVRTerrainNativeRoute < 1.5)
        {
            if (ToggleWallFade != 0 && i.objectY >= .4) clip(m - _Cutoff);
            return;
        }
        float M = m * _EnableOcclusionMap;
        float distanceTerm = min(distance(i.world, _WorldSpaceCameraPos) * .02, 1.);
        float aspect = _ScreenParams.x / _ScreenParams.y;
        float radial = min(length(float2(aspect * uv.x * .5 - aspect * .25, uv.y * .5 - .25)), 1.);
        float foundation = min(max(1. - i.world.y, 0.), 5.) / 3.;
        float t = min(3.333333 * min(pow(distanceTerm + radial, 8.) + foundation, 1.), 1.);
        float S = t * t * (3. - 2. * t);
        float n = NativeSimplex((i.world + _Time.y * float3(.02, -.04, .006)) * float3(6., 7., 10.));
        float maxMS = max(M,S);
        float A = maxMS + 42. * n * (1. - maxMS);
        float B = M > 0. ? 1. : S;
        float value = 1. + ToggleWallFade * (A * B - 1.);
        if (_GHVRTerrainNativeRoute > 2.5) value = saturate(value);
        clip(value - _Cutoff);
    }
    fixed4 OriginalAlbedo(v2f i)
    {
        float3 n = normalize(i.normal);
        if (_WorldSpace > .5)
        {
            float3 blend = pow(abs(n), max(1., _WorldSpace_FallOff * 8.));
            blend /= max(dot(blend,1.), .0001);
            float3 p = i.world * _WorldSpace_tiling;
            return tex2D(_MainTex,p.yz) * blend.x + tex2D(_MainTex,p.xz) * blend.y + tex2D(_MainTex,p.xy) * blend.z;
        }
        return tex2D(_MainTex,i.uv);
    }
    ENDCG
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                NativeClip(i);
                fixed4 tex = OriginalAlbedo(i);
                if (_Difuse_Alpha_On > .5) clip(tex.a - _Cutoff);
                fixed3 color = tex.rgb * _Tint.rgb * _Diffuse_Boost;
                color = lerp(color,dot(color,fixed3(.299,.587,.114)),_Desaturation);
                float3 n = normalize(i.normal);
                float3 light = max(ShadeSH9(float4(n,1.)), .08)
                    + _LightColor0.rgb * saturate(dot(n,_WorldSpaceLightPos0.xyz
                        * rsqrt(max(dot(_WorldSpaceLightPos0.xyz,_WorldSpaceLightPos0.xyz), .0001))));
                color *= light * lerp(1., _DimmFactor, saturate(_IsDimmed));
                fixed4 result = fixed4(color,1.);
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
        // Native shadow casting stays available; optional native wall clipping remains
        // a main-view channel, matching the unchanged original floor/visibility owner.
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback Off
}
