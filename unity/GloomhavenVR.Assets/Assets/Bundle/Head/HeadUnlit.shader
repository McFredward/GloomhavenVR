// Self-contained UNLIT textured shader for the floating head-avatar "masks".
//
// WHY custom (not built-in Unlit/Texture): a bundled material referencing a built-in
// shader is the "pink-material trap" (TOOLCHAIN.md §4.1) — built-in shaders are not
// packed into AssetBundles, so at runtime the material resolves to nothing and renders
// magenta. This shader compiles INTO gloomhavenvr.bundle, so it is always present.
//
// Fully UNLIT: the head floats in the light-less VR void / mirror, where any scene-lit
// shader (Standard, BoardLit's baked rig included) would either render black or add
// shading the Hunyuan albedo doesn't expect. The Hunyuan texture already carries baked
// lighting/detail, so we emit it verbatim (albedo * tint) — the mask reads exactly as
// generated. Double-sided by default (_Cull Off) so the thin decimated mask shells and
// any backfaces never show as black holes from the mirror / other players' angles.
Shader "GloomhavenVR/HeadUnlit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0 // 0 = Off (two-sided)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
