// UI presentation follows Unity's MIT-licensed built-in UI shader.
// Copyright (c) 2016 Unity Technologies. See Unity-LICENSE.txt.
Shader "GloomhavenVR/MenuDepthGraphic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Mask ("Native Dissolve Mask (R)", 2D) = "white" {}
        _Cutoff ("Native Dissolve Cutoff", Range(0,1)) = 0.5
        _EdgeWeight ("Native Dissolve Edge Weight", Range(0,5)) = 1
        [HideInInspector] _GvrMenuDepthDissolve ("Native Dissolve Enabled", Float) = 0
        [HideInInspector] _GvrMenuDepthSrcBlend ("Native Source Blend", Float) = 1
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
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        // Heightfield triangles resolve their own visibility. Empty sprite pixels
        // never author depth, preserving following native text and mask behavior.
        ZWrite On
        ZTest [unity_GUIZTestMode]
        // Original UI/Default uses One; UI/Dissolve mask uses SrcAlpha for
        // BOTH RGB and alpha. The source-aware material owner selects 1 or 5.
        Blend [_GvrMenuDepthSrcBlend] OneMinusSrcAlpha
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
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 mask : TEXCOORD1; float2 dissolveUV : TEXCOORD2; };
            sampler2D _MainTex, _Mask;
            float4 _MainTex_ST, _Mask_ST;
            fixed4 _Color, _TextureSampleAdd;
            float _Cutoff, _EdgeWeight, _GvrMenuDepthDissolve;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            float4 _GvrMenuDepthWorldEye;
            float _GvrMenuDepthProjectMask;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 maskPosition = v.vertex.xy;
                float maskW = o.vertex.w;
                if (_GvrMenuDepthProjectMask > 0.5)
                {
                    // RectMask2D's aperture is on the original UI plane. A deeper
                    // surface must be clipped where its viewing ray crosses that
                    // plane, rather than by its displaced model-space XY alone.
                    float3 eye = mul(unity_WorldToObject, float4(_GvrMenuDepthWorldEye.xyz, 1)).xyz;
                    float denominator = v.vertex.z - eye.z;
                    if (abs(denominator) > 0.000001)
                        maskPosition = eye.xy + (v.vertex.xy - eye.xy) * (-eye.z / denominator);
                    maskW = UnityObjectToClipPos(float4(maskPosition, 0, 1)).w;
                }
                float2 pixelSize = maskW / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(maskPosition * 2 - rect.xy - rect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                // The native dissolve vertex program passes COLOR0 unchanged
                // and applies the two independently authored texture STs.
                o.color = _GvrMenuDepthDissolve > 0.5 ? v.color : v.color * _Color;
                o.uv = _GvrMenuDepthDissolve > 0.5 ? TRANSFORM_TEX(v.uv, _MainTex) : v.uv;
                o.dissolveUV = TRANSFORM_TEX(v.uv, _Mask);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_GvrMenuDepthDissolve > 0.5)
                {
                    // Exact original ps_4_0 sequence from sharedassets2:136,
                    // pinned in NativeDissolve.provenance.json. No saturation,
                    // premultiplication, _Color or UI alpha rounding occurs in
                    // that native fragment program, including EdgeWeight > 1.
                    float m = tex2D(_Mask, i.dissolveUV).r;
                    clip(m - _Cutoff);
                    float4 color = tex2D(_MainTex, i.uv) * i.color;
                    float alpha = (m - _Cutoff) * color.a;
                    alpha = alpha / m;
                    color.a = alpha * _EdgeWeight;
                    #ifdef UNITY_UI_CLIP_RECT
                    half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                    color.a *= mask.x * mask.y;
                    #endif
                    // Native zero-alpha fragments contribute no colour. They
                    // must not occlude later artwork through our depth buffer.
                    if (color.a <= 0) discard;
                    return color;
                }
                i.color.a = round(i.color.a * 255.0) / 255.0;
                fixed4 color = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= mask.x * mask.y;
                #endif
                clip(color.a - 0.001);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
