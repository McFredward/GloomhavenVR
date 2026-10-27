// Native UI colour follows Unity's MIT-licensed built-in UI shader.
// Copyright (c) 2016 Unity Technologies. See Unity-LICENSE.txt.
Shader "GloomhavenVR/MenuDepthCharacter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Native colour", 2D) = "white" {}
        _NativeDepth ("Native surface depth", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        [HideInInspector] _GvrCharacterActive ("Spatial capture", Float) = 0
        [HideInInspector] _GvrCharacterDepthWrite ("Spatial depth write", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off
        ZWrite [_GvrCharacterDepthWrite]
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityUI.cginc"
        struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
        struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 mask : TEXCOORD1; };
        struct output { fixed4 color : SV_Target; float depth : SV_Depth; };
        sampler2D _MainTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_NativeDepth);
        fixed4 _Color, _TextureSampleAdd;
        float4 _ClipRect, _NativeViewport, _NativePlane, _ImageUV, _CaptureOldUV, _CaptureRect, _CaptureUVScaleOffset;
        float _UIMaskSoftnessX, _UIMaskSoftnessY, _GvrCharacterActive, _FrontSide;
        float4x4 _NativeInverseVP, _NativeToGraphic, _GraphicLocalToWorld;
        v2f vert(appdata v)
        {
            v2f o;
            float2 oldUV = v.uv;
            float4 worldPosition = mul(unity_ObjectToWorld, v.vertex);
            if (_GvrCharacterActive > 0.5)
            {
                float2 location = (v.uv - _ImageUV.xy) / (_ImageUV.zw - _ImageUV.xy);
                v.vertex = float4(lerp(_CaptureRect.xy, _CaptureRect.zw, location), 0, 1);
                oldUV = lerp(_CaptureOldUV.xy, _CaptureOldUV.zw, location);
                worldPosition = mul(_GraphicLocalToWorld, v.vertex);
                // uGUI may batch the mesh into Canvas-local coordinates. Mask
                // input follows that actual draw space, not the Graphic's TRS.
                v.vertex = mul(unity_WorldToObject, worldPosition);
            }
            o.vertex = mul(UNITY_MATRIX_VP, worldPosition);
            float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
            float4 rect = clamp(_ClipRect, -2e10, 2e10);
            o.mask = float4(v.vertex.xy * 2 - rect.xy - rect.zw,
                0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
            o.color = v.color * _Color; o.uv = oldUV; return o;
        }
        output sample(v2f i, bool frontPass)
        {
            output result; result.depth = i.vertex.z;
            float2 uv = i.uv;
            if (_GvrCharacterActive > 0.5) uv = uv * _CaptureUVScaleOffset.xy + _CaptureUVScaleOffset.zw;
            float nativeDepth = SAMPLE_DEPTH_TEXTURE(_NativeDepth, uv);
            bool valid = false;
            #if defined(UNITY_REVERSED_Z)
                valid = nativeDepth > 0.000001;
            #else
                valid = nativeDepth < 0.999999;
            #endif
            bool foreground = false;
            if (_GvrCharacterActive > 0.5 && valid)
            {
                float z = nativeDepth;
                #if !defined(UNITY_REVERSED_Z)
                    z = lerp(UNITY_NEAR_CLIP_VALUE, 1, z);
                #endif
                float2 clipUV = (uv - _NativeViewport.xy) / _NativeViewport.zw * 2 - 1;
                float4 nativePosition = mul(_NativeInverseVP, float4(clipUV, z, 1)); nativePosition /= nativePosition.w;
                foreground = dot(_NativePlane, nativePosition) * _FrontSide > 0;
                float4 physical = mul(_NativeToGraphic, nativePosition);
                float4 projected = mul(UNITY_MATRIX_VP, mul(_GraphicLocalToWorld, physical));
                result.depth = projected.z / projected.w;
                #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                    result.depth = result.depth * 0.5 + 0.5;
                #endif
            }
            else if (_GvrCharacterActive > 0.5)
            {
                // The original colour, including depthless native FX, remains visible.
                // No fabricated geometry depth is assigned to a post-effect-only pixel.
                #if defined(UNITY_REVERSED_Z)
                    result.depth = 0;
                #else
                    result.depth = 1;
                #endif
            }
            if (frontPass != foreground) discard;
            if (!frontPass)
            {
                clip(min(min(i.uv.x - _ImageUV.x, _ImageUV.z - i.uv.x), min(i.uv.y - _ImageUV.y, _ImageUV.w - i.uv.y)));
            }
            i.color.a = round(i.color.a * 255.0) / 255.0;
            fixed4 color = (tex2D(_MainTex, uv) + _TextureSampleAdd) * i.color;
            #ifdef UNITY_UI_CLIP_RECT
            if (!frontPass)
            {
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= mask.x * mask.y;
            }
            #endif
            clip(color.a - 0.001);
            color.rgb *= color.a; result.color = color; return result;
        }
        output behind(v2f i) { return sample(i, false); }
        output front(v2f i) { return sample(i, true); }
        ENDCG
        Pass
        {
            Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment behind
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            ENDCG
        }
        Pass
        {
            // The front surface is outside the physical mask plane, and therefore
            // must not be rejected by that plane's old two-dimensional aperture.
            Stencil { Comp Always Pass Keep }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment front
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            ENDCG
        }
    }
}
