// Optional native-world material stages. See WORLD-MATERIAL-SHADERS.md for the
// addressed native DXBC contracts, supported routes and explicitly retained FX.
// Both stages retain native mesh geometry, albedo routes, fog and live wall masks.
Shader "GloomhavenVR/WorldSimpleMaterial"
{
    Properties
    {
        _MainTex ("Original albedo", 2D) = "white" {}
        [HideInInspector] _texcoord ("Original HIGH UV transform", 2D) = "white" {}
        _Tint ("Original AMP tint", Color) = (1,1,1,0)
        _Color ("Original Unity tint", Color) = (1,1,1,1)
        _Diffuse_Boost ("Original HIGH diffuse boost", Float) = 1
        _Desaturation ("Original desaturation", Float) = 0
        _Desaturation_On ("Original LOW desaturation switch", Float) = 0
        _UVTiling ("Original HIGH scalar tiling", Float) = 1
        _UV_Offset ("Original HIGH scalar offset", Float) = 0
        _WorldSpace ("Original projection switch", Float) = 0
        _WorldSpace_tiling ("Original HIGH world tiling", Float) = 1
        _WorldSpace_FallOff ("Original HIGH projection exponent", Float) = .8
        _Difuse_Alpha_On ("Original albedo alpha switch", Float) = 0
        _Cutout ("Original LOW albedo threshold", Float) = 1
        _Cutoff ("Original native mask threshold", Float) = .5
        _IsDimmed ("Original HIGH N_MRAO dimming", Float) = 0
        _DimmFactor ("Original HIGH N_MRAO dimmed grey", Float) = .3
        _GHVRWorldMaterialMode ("1 simple lit; 2 textured", Float) = 1
        _GHVRWorldNativeRoute ("Audited native shader route", Float) = 1
        _GHVRWorldNeverFade ("Authored never-fade floor", Float) = 0
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Lighting.cginc"
    sampler2D _MainTex, _TilesOcclusionMap;
    float4 _MainTex_ST, _texcoord_ST, _Tint, _Color;
    float _Diffuse_Boost, _Desaturation, _UVTiling, _UV_Offset;
    float _WorldSpace_tiling, _WorldSpace_FallOff, _Cutout, _Cutoff;
    float _IsDimmed, _DimmFactor, _EnableOcclusionMap;
    float _GHVRWorldMaterialMode, _GHVRWorldNativeRoute, _GHVRWorldNeverFade;
    // Native camera globals stay outside Properties: a shader material default
    // must not shadow the original per-camera map/enable/integer toggle globals.
    int ToggleWallFade;
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
    bool LowRoute() { return _GHVRWorldNativeRoute == 2. || _GHVRWorldNativeRoute == 4. || _GHVRWorldNativeRoute == 6.; }
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
        UNITY_FOG_COORDS(4)
        UNITY_VERTEX_OUTPUT_STEREO
    };
    v2f vert(appdata v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = UnityObjectToClipPos(v.vertex);
        // HIGH uses its hidden _texcoord_ST BEFORE scalar tiling/offset. LOW
        // and Unity families use _MainTex_ST. Native HIGH ignores that latter ST.
        float2 highUv = v.uv * _texcoord_ST.xy + _texcoord_ST.zw;
        o.uv = LowRoute() || _GHVRWorldNativeRoute >= 9.
            ? TRANSFORM_TEX(v.uv, _MainTex) : highUv * _UVTiling + _UV_Offset;
        o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.normal = UnityObjectToWorldNormal(v.normal);
        o.screen = ComputeScreenPos(o.pos);
        UNITY_TRANSFER_FOG(o,o.pos);
        return o;
    }
    float4 OriginalAlbedo(v2f i)
    {
        #if defined(_WORLDSPACE_ON)
        if (_GHVRWorldNativeRoute < 9.)
        {
            // AMP projects onto signed ZY, XZ, XY (the Z projection flips X).
            // The interpolated native vertex normal supplies the blend weights.
            float3 n = i.normal;
            float3 blend = LowRoute() ? abs(n) : pow(abs(n), _WorldSpace_FallOff);
            blend /= dot(blend, 1.) + .00001;
            float scale = LowRoute() ? _MainTex_ST.x : _WorldSpace_tiling;
            float3 p = i.world * scale;
            float3 direction = sign(n);
            return tex2D(_MainTex, float2(p.z * direction.x, p.y)) * blend.x
                + tex2D(_MainTex, float2(p.x * direction.y, p.z)) * blend.y
                + tex2D(_MainTex, float2(-p.x * direction.z, p.y)) * blend.z;
        }
        #endif
        return tex2D(_MainTex, i.uv);
    }
    float NativeWallAmount(v2f i)
    {
        bool low = false;
        bool high = false;
        #if defined(_WALLFADE_ON_ON)
        high = _GHVRWorldNativeRoute == 1.;
        #endif
        #if defined(_TOGGLEWALLFADE_ON)
        low = _GHVRWorldNativeRoute == 2.;
        #endif
        #if !defined(_TOGGLEWALLFADEOFF_ON)
        high = high || _GHVRWorldNativeRoute == 3.;
        low = low || _GHVRWorldNativeRoute == 4.;
        #endif
        if (_GHVRWorldNeverFade > .5 || (!high && !low) || ToggleWallFade == 0) return 1.;
        // LOW's foundation cutoff is WORLD height, not source vertex/object Y.
        if (low && i.world.y < .4) return 1.;
        float2 uv = i.screen.xy / (i.screen.w + 1e-11);
        float4 occlusion = tex2D(_TilesOcclusionMap, uv);
        // LOW N_MRAO remaps BOTH channels before comparing depth. LOW WallFade
        // uses the original sample; HIGH multiplies only the resulting mask.
        if (_GHVRWorldNativeRoute == 2.)
            occlusion = 1. + _EnableOcclusionMap * (occlusion - 1.);
        float depth = i.screen.z / i.screen.w;
        #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
        depth = depth * .5 + .5;
        #endif
        float m = occlusion.a >= depth ? 1. : 1. - occlusion.r;
        if (low)
        {
            float threshold = _GHVRWorldNativeRoute == 2. ? _Cutout : _Cutoff;
            clip(m - threshold);
            return 1.;
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
        return 1. + ToggleWallFade * (A * B - 1.);
    }
    void OriginalClip(float4 albedo, float wall)
    {
        if (_GHVRWorldNativeRoute == 1.)
        {
            float alpha = 1.;
            #if defined(_DIFUSE_ALPHA_ON_ON)
            alpha = albedo.a;
            #endif
            // HIGH N_MRAO clips the SATURATED PRODUCT, not two independent clips.
            clip(saturate(wall * alpha) - _Cutoff);
        }
        else if (_GHVRWorldNativeRoute == 3. || _GHVRWorldNativeRoute == 5.) clip(wall - _Cutoff);
        else if (_GHVRWorldNativeRoute == 2.)
        {
            #if defined(_DIFUSE_ALPHA_ON_ON)
            clip(albedo.a - _Cutout);
            #endif
        }
        else if (_GHVRWorldNativeRoute == 6.) clip(_Cutout - albedo.a);
        else if (_GHVRWorldNativeRoute == 9.)
        {
            #if defined(_ALPHATEST_ON)
            #if defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
            clip(_Color.a - _Cutoff);
            #else
            clip(albedo.a * _Color.a - _Cutoff);
            #endif
            #endif
        }
    }
    float3 OriginalColor(float4 albedo)
    {
        if (_GHVRWorldNativeRoute >= 9.) return albedo.rgb * _Color.rgb;
        float3 color = albedo.rgb * _Tint.rgb;
        if (LowRoute())
        {
            #if defined(_DESATURATION_ON)
            color = lerp(color, dot(color,float3(.299,.587,.114)), _Desaturation);
            #endif
        }
        else
        {
            color = lerp(color, dot(color,float3(.299,.587,.114)), _Desaturation) * _Diffuse_Boost;
            if (_GHVRWorldNativeRoute == 1.)
                color = lerp(color, dot(color,float3(.299,.587,.115)) * _DimmFactor, _IsDimmed);
        }
        return color;
    }
    float4 frag(v2f i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float4 albedo = OriginalAlbedo(i);
        OriginalClip(albedo, NativeWallAmount(i));
        float3 color = OriginalColor(albedo);
        if (_GHVRWorldMaterialMode < 1.5)
        {
            float3 n = normalize(i.normal);
            float3 direction = normalize(_WorldSpaceLightPos0.xyz - i.world * _WorldSpaceLightPos0.w);
            // Deliberate stage-1 compromise: SH ambient and one diffuse main light;
            // no normal/MRAO/detail, reflections, specular, parallax or ForwardAdd.
            color *= max(ShadeSH9(float4(n,1.)), 0.) + _LightColor0.rgb * saturate(dot(n,direction));
        }
        // Stage 2 retains original texture/tint/dim and fog without lighting work.
        float4 result = float4(color,1.);
        UNITY_APPLY_FOG(i.fogCoord,result);
        return result;
    }
    ENDCG
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile __ _WORLDSPACE_ON
            #pragma multi_compile __ _DIFUSE_ALPHA_ON_ON
            #pragma multi_compile __ _DESATURATION_ON
            #pragma multi_compile __ _WALLFADE_ON_ON
            #pragma multi_compile __ _TOGGLEWALLFADE_ON
            #pragma multi_compile __ _TOGGLEWALLFADEOFF_ON
            #pragma multi_compile __ _ALPHATEST_ON
            #pragma multi_compile __ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            ENDCG
        }
        // The geometry remains a depth-writing 3D surface. Shadow policy remains
        // independently selectable; original source shadow flags are never written.
        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma multi_compile __ _WORLDSPACE_ON
            #pragma multi_compile __ _DIFUSE_ALPHA_ON_ON
            #pragma multi_compile __ _ALPHATEST_ON
            #pragma multi_compile __ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            struct shadowOutput
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                float3 world : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            shadowOutput shadowVert(appdata v)
            {
                shadowOutput o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                float2 highUv = v.uv * _texcoord_ST.xy + _texcoord_ST.zw;
                o.uv = LowRoute() || _GHVRWorldNativeRoute >= 9.
                    ? TRANSFORM_TEX(v.uv, _MainTex) : highUv * _UVTiling + _UV_Offset;
                o.world = mul(unity_ObjectToWorld,v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 shadowFrag(shadowOutput i) : SV_Target
            {
                // AMP's original LOW casters contain no texture/clip at all, and
                // HIGH N_MRAO/WallFade use Diffuse's opaque fallback caster. Preserve
                // that native shadow silhouette, independently of main-view fades.
                v2f sample;
                UNITY_INITIALIZE_OUTPUT(v2f,sample);
                sample.uv = i.uv; sample.world = i.world; sample.normal = i.worldNormal;
                if (_GHVRWorldNativeRoute == 5.) clip(1. - _Cutoff);
                else if (_GHVRWorldNativeRoute == 9.) OriginalClip(OriginalAlbedo(sample),1.);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
