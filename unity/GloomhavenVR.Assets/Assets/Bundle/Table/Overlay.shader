// Self-contained UNLIT OVERLAY shader for board-docked HUD widgets (round readout plate,
// settings gear / follow-toggle bodies, card-slot insert glows, the action ButtonCluster) and
// for the figure-grab overlays (the additive pre-grab glow and the translucent home ghost).
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
//
// ModBuild 339 added two properties, BOTH DEFAULTED SO EVERY EXISTING MATERIAL IS UNCHANGED:
//
//   _VertexColor (1 = keep, 0 = ignore). The fragment always multiplied by the mesh's own
//     COLOR stream. That is right for a widget whose mesh was authored FOR this shader, and
//     wrong for an OVERLAY, which re-draws SOMEBODY ELSE'S mesh: a character mesh that bakes
//     a mask into its vertex colours (black rgb, or alpha 0) multiplies an additive glow to
//     zero and an alpha ghost to nothing, and the result is a clone that exists, is enabled,
//     is on a drawn layer, sits exactly on the figure, is reported visible by every camera —
//     and paints no pixels. FigureOverlay.MakeOverlayMaterial sets this to 0 so an overlay
//     TINT is the tint the mod asked for and not the tint the source artist baked.
//
//   _OffsetFactor / _OffsetUnits (0,0 = no bias). An overlay re-draws the SAME triangles the
//     figure already drew, so its depth is an EQUALITY against the depth buffer — and the two
//     values come out of two DIFFERENT vertex programs (this one and the game's Amp_Char
//     shader), which need not agree to the last bit. A small negative polygon offset biases
//     the overlay toward the camera by a fraction of a depth unit. It CANNOT let the overlay
//     pierce a wall: a wall in front is many depth units nearer, and `Offset -1,-1` moves the
//     fragment by about one.
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
        _VertexColor ("Use Mesh Vertex Colour", Range(0,1)) = 1
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Offset [_OffsetFactor], [_OffsetUnits]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color;
            fixed _VertexColor;

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
                // _VertexColor = 0 -> the SOURCE mesh's authored vertex colours cannot dim or
                // erase an overlay that was never authored for this mesh. 1 -> historic behaviour.
                fixed4 vc = lerp(fixed4(1,1,1,1), i.color, _VertexColor);
                return tex2D(_MainTex, i.uv) * _Color * vc;
            }
            ENDCG
        }
    }
}
