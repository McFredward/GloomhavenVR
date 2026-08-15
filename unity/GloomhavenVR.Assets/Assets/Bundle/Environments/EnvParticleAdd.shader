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
//  3. A POSE (_TipUse.z, EnvShelfTip.cginc). ModBuild 152, and it is the third
//     job for the same reason the first two exist: an emitter standing on the
//     cellar's tipping bookcase cannot be moved, switched off or re-aimed when
//     the bookcase goes over, because moving a world-simulated Shuriken system
//     needs a script. USER: "Beim umgekippten Bücherregal kippt die Funkenquelle
//     nicht mit um, wenn Feuer an ist" — and then, cutting the round down to its
//     honest size, "Um es einfach zu halten: Deaktivier die Funken einfach
//     (ausfaden) wenn das Regal kippt."
//     So an emitter that declares _TipUse.z = 1 FADES with the shelf's own
//     uprightness (GhvrTipUpright) and collapses when that reaches zero. What is
//     NOT claimed: the emitter still simulates and still costs its draw call for
//     the whole event — nothing in this bundle can stop either — and the sparks
//     do not follow the carcass down, they are simply not there while it is
//     lying on the floor.
//
// All three are folded into the per-particle colour IN THE VERTEX SHADER, so the
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
        // 1 = this emitter IS moonlight and dies with the moon under the eclipse.
        // Default 0 keeps every unwritten material bit-identical. See EnvParticleElem.cginc.
        _ElemMoon ("Element: emitter is moonlight", Range(0,1)) = 0

        // ---- THE BOOKSHELF THAT TOPPLES (EnvShelfTip.cginc) ----------------
        // The shelf's pose, in the SAME five vectors every other rider carries
        // and written by the SAME single writer (EnvRoomBuilder.WriteShelfTip) —
        // there is no particle-specific channel, because a second way of telling
        // a material about the hinge is a second thing that can disagree with it.
        // All five default to zero, i.e. "this emitter has never heard of the
        // bookshelf": _TipUse.z = 0 skips the branch entirely and _TipPivot.w = 0
        // would make the pose dead even if it did not, so every other emitter in
        // both rooms is bit-identical with what shipped.
        //
        // Declared HERE and not only in the include because WriteShelfTip gates
        // on Material.HasProperty, which reads this block and not the CGPROGRAM.
        //
        // ...and only _TipSched, _TipEnv and _TipAxis.w are ever read on this
        // path: the hinge and the axis DIRECTION are the geometry of riding, and
        // these emitters do not ride, they fade. Stated because the pivot this
        // material is given is expressed in the emitter transform's object space
        // while a world-simulated particle's vertex is not, so the one thing that
        // must never happen here is somebody using it to move a quad.
        _TipPivot ("Shelf hinge (object space)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis + max angle", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, this card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Shelf: what I do with the pose", Vector) = (0,0,0,0)
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
            // AFTER EnvParticleElem, which is what declares _GhvrTimeOfs for this
            // shader — EnvHaunt.cginc (pulled in by EnvShelfTip) reads the shared
            // clock through its callers and does not declare it itself.
            #include "EnvShelfTip.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                float4 col = v.color * _Tint;
                bool alive = GhvrParticleElem(v.vertex, col);   // gate + modulation
                // ---- ...and the bookcase going over takes its sparks with it --
                // A UNIFORM branch on a material constant, so it is coherent
                // across the draw and every emitter that is not standing on the
                // bookshelf pays one compare against a literal zero. Inside it,
                // GhvrTipUpright is 1.0 the instant the shelf is upright and
                // exactly 0.0 for the whole of the time it is on the floor.
                if (_TipUse.z > 0.5)
                {
                    float up = GhvrTipUpright(GhvrTipNow(_Time.y + _GhvrTimeOfs));
                    col.a *= up;
                    // COLLAPSED once it is out, exactly as the element gate does
                    // it and for the same reason: an alpha of 0 still costs the
                    // fill of every quad in the swarm, and this one is out for
                    // sixteen of the event's twenty-six seconds.
                    alive = alive && (up > 0.0);
                }
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
