// GloomhavenVR — additive unlit particle shader (flames, fireflies, shooting stars,
// glows). Written into the bundle because the builtin 'Particles/*' shaders may be
// stripped from the game player (the pink-material trap, TOOLCHAIN.md §4.1).
// Texture × per-particle vertex color; no depth write; world-space geometry only —
// stereo-correct by construction.
//
// ELEMENT ART (EnvElement.cginc) — this shader carries TWO different jobs for
// the elements, and they are different because the bundle ships no
// MonoBehaviours: nothing can enable or disable a particle system at runtime.
//
//  1. A GATE (_ElemOwn/_ElemOwn2). An emitter that EXISTS FOR one element —
//     the embers, the spores, the driven needles — declares which element owns
//     it. While that element is down the quad is COLLAPSED TO A POINT in the
//     vertex shader, so its two triangles are zero-area and not one fragment is
//     ever shaded. That is the same trick the haunt cards use, for the same
//     reason, and it is the whole of what "costs nothing when the master is 0"
//     can mean for a system that has to keep simulating.
//     (What it does NOT save: the Shuriken simulation and the draw call. Both
//     would need a script to switch off. The bake log prints what they cost.)
//  2. A MODULATION (_ElemMod/_ElemMod2). An emitter that exists ANYWAY — the
//     fireflies, the dust — states how the elements colour it. The fireflies
//     turning to sparks under Fire is this: same swarm, same motion, ember
//     colour, twice the energy and a fast twinkle, which is the design's
//     "the fireflies turn to sparks" without a second emitter to pay for.
//
// Both are folded into the per-particle colour IN THE VERTEX SHADER, so the
// fragment stage is bit-for-bit what it was before the feature existed.
Shader "GloomhavenVR/EnvParticleAdd"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        // ---- ELEMENT ART: the gate (all zero = this emitter is unconditional)
        _ElemOwn ("Element gate (fire,ice,air,earth)", Vector) = (0,0,0,0)
        _ElemOwn2 ("Element gate (light,dark,-,-)", Vector) = (0,0,0,0)
        // ---- ...and the modulation
        _ElemMod ("Element modulation (fire,ice,air,earth)", Vector) = (0,0,0,0)
        _ElemMod2 ("Element modulation (light,dark,-,-)", Vector) = (0,0,0,0)
        _ElemGain ("Element: brightness response", Float) = 0
        _ElemAlpha ("Element: alpha response", Float) = 0
        _ElemCol ("Element: colour it moves toward", Color) = (1,1,1,1)
        _ElemTintAmt ("Element: how far it moves", Range(0,2)) = 0
        _ElemSpark ("Element: fast twinkle amount", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            #include "EnvParticleElem.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                float4 col = v.color * _Tint;
                bool alive = GhvrParticleElem(v.vertex, col);   // gate + modulation
                // COLLAPSED, not merely transparent: all four corners land on the
                // same clip position, both triangles are zero-area, and the
                // rasteriser produces nothing. An alpha of 0 would still cost the
                // fill of every quad in the swarm.
                o.pos = alive ? UnityObjectToClipPos(v.vertex) : float4(0, 0, 0, 1);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = col;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed4 c = t * i.color;
                c.rgb *= c.a; // premodulate so alpha drives the additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
