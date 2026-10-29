// UI presentation follows Unity's MIT-licensed built-in UI shader.
// Copyright (c) 2016 Unity Technologies. See Unity-LICENSE.txt.
Shader "GloomhavenVR/MenuDepthDisplay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Left eye", 2D) = "white" {}
        _RightTex ("Right eye", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        [HideInInspector] _GvrDisplayActive ("Expanded transparent carrier", Float) = 0
        [HideInInspector] _GvrCaptureDepthActive ("Physical foreground depth", Float) = 0
        _GvrDepthLeft ("Original left surface depth", 2D) = "white" {}
        _GvrDepthRight ("Original right surface depth", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        // This carrier also renders through native MeshRenderers, outside uGUI.
        ZTest LEqual
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityUI.cginc"
        struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
        struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 mask : TEXCOORD1; float3 world : TEXCOORD2; };
        struct output { fixed4 color : SV_Target; float depth : SV_Depth; };
        sampler2D _MainTex, _RightTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_GvrDepthLeft);
        UNITY_DECLARE_DEPTH_TEXTURE(_GvrDepthRight);
        fixed4 _Color, _TextureSampleAdd;
        float4 _ClipRect, _GvrDisplayRect, _GvrOriginalGlassUV, _GvrGlassDepth;
        float4x4 _GvrDisplayLocalToWorld;
        float3 _GvrPlanePoint, _GvrPlaneNormal;
        float _GvrDisplayActive, _GvrCaptureDepthActive, _GvrHeadRaster;
        float4x4 _GvrHeadInverseLeft, _GvrHeadInverseRight;
        float4x4 _GvrHeadGlassInverse, _GvrHeadAnnotationInverse[8];
        float4 _GvrHeadGlassRect, _GvrHeadAnnotationRects[8];
        float _GvrHeadClipActive, _GvrHeadAnnotationCount;
        float3 _GvrHeadEyeLeft, _GvrHeadEyeRight;
        float _UIMaskSoftnessX, _UIMaskSoftnessY, _GvrMenuDepthEye;
        v2f vert(appdata v)
        {
            v2f o;
            float4 world = mul(unity_ObjectToWorld, v.vertex);
            if (_GvrDisplayActive > .5)
            {
                world = mul(_GvrDisplayLocalToWorld, float4(lerp(_GvrDisplayRect.xy, _GvrDisplayRect.zw, v.uv), 0, 1));
                v.vertex = mul(unity_WorldToObject, world);
            }
            o.vertex = mul(UNITY_MATRIX_VP, world); o.world = world.xyz;
            float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
            float4 rect = clamp(_ClipRect, -2e10, 2e10);
            o.mask = float4(v.vertex.xy * 2 - rect.xy - rect.zw,
                0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
            o.color = v.color * _Color; o.uv = v.uv;
            if (_GvrHeadRaster > .5)
            {
                o.vertex = float4(v.uv * 2 - 1, 0, 1);
                o.vertex.y *= _ProjectionParams.x;
            }
            return o;
        }
        bool rayInside(float4x4 inverseFrame, float4 rect, float3 eye, float3 position)
        {
            float3 origin = mul(inverseFrame, float4(eye, 1)).xyz;
            float3 destination = mul(inverseFrame, float4(position, 1)).xyz;
            float3 ray = destination - origin;
            if (abs(ray.z) < .00000001) return false;
            float along = -origin.z / ray.z;
            float2 plane = origin.xy + ray.xy * along;
            return along >= 0 && all(plane >= rect.xy) && all(plane <= rect.zw);
        }
        output sample(v2f i, bool depthOnly)
        {
            bool right = _GvrMenuDepthEye > .5;
            bool foreground = false;
            output result; result.depth = i.vertex.z;
            if (_GvrHeadRaster > .5)
            {
                float raw = right ? SAMPLE_DEPTH_TEXTURE(_GvrDepthRight, i.uv) : SAMPLE_DEPTH_TEXTURE(_GvrDepthLeft, i.uv);
                bool valid;
                #if defined(UNITY_REVERSED_Z)
                    valid = raw > 0.000001;
                #else
                    valid = raw < 0.999999;
                #endif
                float z = raw;
                #if !defined(UNITY_REVERSED_Z)
                    z = lerp(UNITY_NEAR_CLIP_VALUE, 1, z);
                #endif
                float4x4 inverseVP = right ? _GvrHeadInverseRight : _GvrHeadInverseLeft;
                float3 eye = right ? _GvrHeadEyeRight : _GvrHeadEyeLeft;
                float4 position = mul(inverseVP, float4(i.uv * 2 - 1, z, 1)); position /= position.w;
                float side = dot(eye - _GvrPlanePoint, _GvrPlaneNormal) < 0 ? -1 : 1;
                foreground = valid && (abs(dot(eye-_GvrPlanePoint,_GvrPlaneNormal)) < .00001 || dot(position.xyz - _GvrPlanePoint, _GvrPlaneNormal) * side > .000001);
                if (_GvrHeadClipActive > .5 && !foreground
                    && !rayInside(_GvrHeadGlassInverse, _GvrHeadGlassRect, eye, position.xyz))
                {
                    fixed alpha = right ? tex2D(_RightTex, i.uv).a : tex2D(_MainTex, i.uv).a;
                    if (alpha * i.color.a <= .001) discard;
                    bool annotation = false;
                    for (int a = 0; a < 8; a++)
                    {
                        if (a >= _GvrHeadAnnotationCount) break;
                        annotation = annotation || rayInside(_GvrHeadAnnotationInverse[a], _GvrHeadAnnotationRects[a], eye, position.xyz);
                    }
                    if (!annotation) discard;
                }
                if (foreground) result.depth = raw;
                else
                {
                    float3 ray = position.xyz - eye;
                    float denominator = dot(ray, _GvrPlaneNormal);
                    float along = dot(_GvrPlanePoint - eye, _GvrPlaneNormal) / denominator;
                    float4 glass = mul(UNITY_MATRIX_VP, float4(eye + ray * along, 1));
                    result.depth = glass.z / glass.w;
                    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                        result.depth = result.depth * .5 + .5;
                    #endif
                }
            }
            else if (_GvrCaptureDepthActive > .5)
            {
                float raw = right ? SAMPLE_DEPTH_TEXTURE(_GvrDepthRight, i.uv) : SAMPLE_DEPTH_TEXTURE(_GvrDepthLeft, i.uv);
                float glass = right ? _GvrGlassDepth.y : _GvrGlassDepth.x;
                float scale = right ? _GvrGlassDepth.w : _GvrGlassDepth.z;
                #if defined(UNITY_REVERSED_Z)
                    foreground = raw > glass + 0.000001 && raw > 0.000001;
                #else
                    foreground = raw < glass - 0.000001 && raw < 0.999999;
                #endif
                if (foreground)
                {
                    float3 eye = _WorldSpaceCameraPos.xyz;
                    float3 ray = i.world - eye;
                    float distance = dot(ray, _GvrPlaneNormal);
                    float along = (dot(_GvrPlanePoint - eye, _GvrPlaneNormal) + (raw - glass) * scale) / distance;
                    float4 physical = mul(UNITY_MATRIX_VP, float4(eye + ray * along, 1));
                    result.depth = physical.z / physical.w;
                    #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                        result.depth = result.depth * .5 + .5;
                    #endif
                }
                bool outside = any(i.uv < _GvrOriginalGlassUV.xy) || any(i.uv > _GvrOriginalGlassUV.zw);
                if (outside && !foreground) discard;
            }
            if (depthOnly && !foreground) discard;
            i.color.a = round(i.color.a * 255.0) / 255.0;
            fixed4 sampled = right ? tex2D(_RightTex, i.uv) : tex2D(_MainTex, i.uv);
            fixed4 color = (sampled + _TextureSampleAdd) * i.color;
            #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= mask.x * mask.y;
            #endif
            #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
            #endif
            if (depthOnly) clip(color.a - .001);
            color.rgb *= color.a; result.color = color; return result;
        }
        output color(v2f i) { return sample(i, false); }
        output depth(v2f i) { return sample(i, true); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment color
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            ENDCG
        }
        Pass
        {
            // Only actual foreground geometry contributes head depth. Native
            // flat/translucent UI retains its original colour-only contract.
            ColorMask 0
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            ENDCG
        }
        Pass
        {
            // One fixed2x head-grid draw resolves to the existing1x head pair.
            // Colour integrates four source samples. Geometry keeps the nearest
            // covered sample, independently of colour/clear alpha interpolation.
            Cull Off ZTest Always ZWrite On Blend Off ColorMask RGBA
            CGPROGRAM
            #pragma vertex resolveVertex
            #pragma fragment resolveHead
            #pragma target 3.5
            Texture2D _GvrResolveDepth;
            SamplerState Gvr_point_clamp_sampler;
            float4 _MainTex_TexelSize, _GvrResolveUV;
            float _GvrResolveStep;
            v2f_img resolveVertex(appdata_img v)
            {
                v2f_img o;o.pos=float4(v.texcoord*2-1,0,1);o.uv=v.texcoord;
                #if UNITY_UV_STARTS_AT_TOP
                    o.pos.y=-o.pos.y;
                #endif
                return o;
            }
            output resolveHead(v2f_img i)
            {
                float2 uv=i.uv*_GvrResolveUV.xy;
                output result;result.color=tex2D(_MainTex,uv);
                float2 step=abs(_MainTex_TexelSize.xy)*_GvrResolveStep;
                float a=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(-1,-1),0).r;
                float b=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(-1,1),0).r;
                float c=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(1,-1),0).r;
                float d=_GvrResolveDepth.SampleLevel(Gvr_point_clamp_sampler,uv+step*float2(1,1),0).r;
                #if defined(UNITY_REVERSED_Z)
                    result.depth=max(max(a,b),max(c,d));
                #else
                    result.depth=min(min(a,b),min(c,d));
                #endif
                return result;
            }
            ENDCG
        }
    }
}
