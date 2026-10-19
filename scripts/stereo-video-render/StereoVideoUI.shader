// UI/Default presentation contract with one SBS eye selection before texture sampling.
// Mask softness and premultiplied alpha follow Unity's MIT-licensed built-in UI shader.
// Copyright (c) 2016 Unity Technologies. MIT license: see Unity-LICENSE.txt.
Shader "GloomhavenVR/StereoVideoUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Movie", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 mask : TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            // Set by the actual camera's MultiPass callback. It is intentionally not a
            // material property: stencil-derived UI material copies must observe it too.
            float _GvrStereoVideoEye;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - rect.xy - rect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float eye = _GvrStereoVideoEye > 0.5 ? 1.0 : 0.0;
                float2 uv = float2(i.uv.x * 0.5 + eye * 0.5, i.uv.y);
                // Bilinear filtering must never read the other eye across the packed seam.
                float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                uv = clamp(uv, float2(eye * 0.5 + halfTexel.x, halfTexel.y),
                    float2((eye + 1.0) * 0.5 - halfTexel.x, 1.0 - halfTexel.y));
                i.color.a = round(i.color.a * 255.0) / 255.0;
                fixed4 color = (tex2D(_MainTex, uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
