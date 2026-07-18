// Self-contained UNLIT OVERLAY shader for board-docked HUD widgets (round readout plate,
// settings gear / follow-toggle bodies, card-slot insert glows, the action ButtonCluster).
//
// WHY it exists: those widgets were built on the built-in `Sprites/Default` and `Standard`
// shaders, NEITHER of which exposes a `_ZTest` property. The mod's RenderOnTop helper sets
// `_ZTest = Always` via HasProperty(...) to draw a widget OVER the opaque two-sided board —
// but with no such property the set was a silent no-op, so `ZTest LEqual` stayed baked and
// the board (which writes depth at ~Geometry queue) occluded the widget. This recurred 3×.
//
// This shader compiles INTO gloomhavenvr.bundle (like BoardLit), avoiding the pink-material
// trap of referencing an unbundled built-in shader. It is unlit (samples _MainTex * _Color),
// and — crucially — EXPOSES _ZTest, _ZWrite, _Cull, and the blend factors as properties, so
// RenderOnTop's existing `_ZTest`/`_ZWrite` sets take effect. Defaults are the correct
// depth-tested alpha-blended look (LEqual) for when a widget is NOT drawn on top; glows opt
// into additive blending by setting _SrcBlend=One,_DstBlend=One.
Shader "GloomhavenVR/Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4 // LEqual
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0          // Off (two-sided)
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10 // OneMinusSrcAlpha
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color * i.color;
            }
            ENDCG
        }
    }
}
