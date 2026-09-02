// GloomhavenVR — THIN NIGHT CLOUD over the forest clearing.
//
// The arithmetic and the four guarantees are in EnvCloud.cginc; this file is the
// thin wrapper that declares the dials and composites the result. Read the
// .cginc first — everything load-bearing is there, and it is an include rather
// than inline code so that the SAME function can be folded straight into
// EnvStars.shader's fragment stage later (two lines: an #include and one call)
// if the extra blend turns out to be worth removing.
//
// WHY A SEPARATE LAYER AT ALL, since one more term inside EnvStars would cost no
// draw call and no blend. Two reasons, one of them mine and one of them not:
//   * GEOMETRY IS THE CHEAPEST MASK THERE IS. The clouds live above ~11 deg
//     elevation. A shell that starts at 9 deg simply never rasterises the sky
//     below the ridge, so the fragments that would have run the whole cloud term
//     and multiplied it by zero are never issued at all. Folded into EnvStars
//     the term runs on every sky pixel, including all the ones under the
//     horizon where it is guaranteed to be zero.
//   * this worktree does not own EnvStars.shader. Said plainly rather than
//     dressed up: the fold-in is a real alternative, it is measured against this
//     one in .planning/FOREST-CLOUDS.md, and offered there to whoever owns it.
//
// COMPOSITING. `Blend One OneMinusSrcAlpha` — PREMULTIPLIED, not the usual
// SrcAlpha blend, and the choice is physical rather than stylistic. Single
// scattering through a thin slab is
//     L_out = L_inscatter + T * L_background,   T = 1 - a
// which is exactly what One/OneMinusSrcAlpha computes when .rgb already carries
// the in-scattered radiance and .a carries the extinction. With a straight
// SrcAlpha blend the same picture needs the scatter divided back out by a, which
// explodes at the wisp edges where a -> 0 — the classic dark-fringe artefact.
//
// QUEUE. Background+7: after the sky's continuous layer (EnvStars, +5) and after
// the catalogue stars (EnvStarPoints, +6), so a cloud dims the stars behind it
// the way a real one does; and before every Geometry-queue thing in the room, so
// the canopy and the trunks still cover it. No depth write, no depth cost.
//
// CULL OFF, and it costs nothing. The shell is a piece of a sphere and the
// viewer is inside it: every ray out of the play space leaves that sphere
// exactly once, so at most ONE cloud fragment is ever produced per pixel,
// whatever the winding is. Overdraw is 1 by construction, and the winding-bug
// class this project has paid for nine times cannot reach this mesh. (The gate
// in BuildEnvironments checks the winding anyway — a mesh that is right for a
// reason should also be right in fact.)
//
// NAMING, and both halves of it are load-bearing:
//   * the moon bearing is `_CloudMoonDir`, NOT `_MoonDir`. SkyAlternative's
//     ReadMoonFrom walks the sky branch's renderers and takes the FIRST material
//     that has a `_MoonDir`, and that answer aims the map room's light. StarDome
//     answers first today only because a parent precedes its children in
//     hierarchy order — which is an ordering accident, not a contract. A cloud
//     material that never declares the property cannot become the moon oracle.
//   * nothing here contains the substrings "skysphere" / "skyshader" / "amp_sky".
//     SkyBackdrop.FindSky sweeps every renderer in the process and disables the
//     first node OR SHADER whose name contains one of those, as "the game's sky
//     sphere". A cloud layer called SkyClouds would switch itself off.
Shader "GloomhavenVR/EnvCloud"
{
    Properties
    {
        // THE DEFAULTS BELOW ARE NOT THE CONTRACT. Every one of them is
        // overwritten by BuildEnvironments.BuildMaterials from the constants in
        // its THIN NIGHT CLOUD block, which is where the arguments live and where
        // AssertCloudsClearTheMoon can see them. They are kept in step with the
        // shipped values anyway — a Range() default that disagrees with the
        // material is a number a future reader will quote at somebody.
        _CloudTex ("Cloud noise (R coarse, G fine)", 2D) = "black" {}

        _CloudScale ("Ground-plane units -> uv", Range(0.02,3)) = 0.30
        _CloudRatio ("Fine layer frequency ratio", Range(1,8)) = 3.1
        _CloudMix ("Coarse layer weight", Range(0,1)) = 0.68
        // uv offset PER SECOND. Every component is an integer divided by
        // GHVR_CLOUD_PERIOD, so the whole field returns to its t=0 state exactly.
        _CloudWind ("Wind (A.xy, B.xy) uv/s", Vector) = (0.000694,0,0.001389,0.000694)

        _CloudCut ("Coverage threshold", Range(0,1)) = 0.485
        _CloudSharp ("Coverage hardness", Range(0.5,12)) = 3.6
        _CloudAlpha ("MAX opacity, anywhere, ever", Range(0,1)) = 0.62

        _CloudElevLo ("sin(elev) layer starts", Range(0,0.6)) = 0.191
        _CloudElevHi ("sin(elev) layer full", Range(0,0.9)) = 0.375

        _CloudMoonDir ("Moon direction (object space)", Vector) = (0.6,0.37,0.71,0)
        _CloudMoonMin ("Taper floor over the moon", Range(0,1)) = 0.35
        _CloudMoonIn ("cos(inner clear angle)", Range(0.9,1)) = 0.99255
        _CloudMoonOut ("cos(outer clear angle)", Range(0.5,1)) = 0.91355
        _CloudEdge ("Clear-patch rim wobble (cos units)", Range(0,0.05)) = 0.010

        _CloudTint ("Cloud colour", Color) = (0.72,0.78,0.92,1)
        _CloudScatBase ("Ambient in-scatter", Range(0,1)) = 0.028
        _CloudScatFwd ("Forward-scatter peak", Range(0,4)) = 0.55
        _CloudScatPow ("Forward lobe hardness", Range(4,400)) = 90
    }
    SubShader
    {
        Tags { "Queue"="Background+7" "RenderType"="Background" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            #include "EnvCloud.cginc"

            // Shared with every other Env* shader that reads _Time: in multiplayer the elected
            // owner's epoch arrives on Net record 31 so both skies stand at the same hour; 0 offline.
            float _GhvrTimeOfs;

            // The SAME wrapped clock EnvStars uses, for the same reason: _Time.y in
            // a float loses precision after hours of play and Adreno stutters on the
            // big values. GHVR_CLOUD_PERIOD divides SKY_PERIOD, so the drift crosses
            // the wrap without a jump.
            #define SKY_PERIOD 2880.0
            float SkyTime() { return fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD); }

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 opos : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // OBJECT space, exactly as EnvStars does it, and this is the whole
                // of the stereo argument: the shading of a point on this shell is a
                // function of THAT POINT, so both eyes get the same colour for it.
                // It is also the same frame, at the same radius, as the moon sprite
                // on the dome — so no viewer position can slide the clear patch off
                // the moon. (Uniform scale is irrelevant: the fragment normalises.)
                o.opos = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 u = normalize(i.opos);
                float4 c = GhvrCloudLayer(u, SkyTime());

                // ELEMENT ART. The sky's continuous layer is lifted by Light and
                // crushed by Dark (EnvStars). A cloud that ignored that would be
                // the one thing left standing in a blacked-out sky, so it takes
                // the SAME gain — on its IN-SCATTER only: what an element changes
                // is how much moonlight this layer throws back, never how much
                // sky it swallows. Extinction is geometry.
                // Bracketed by e.live, so with nothing up the code does not run
                // and the zero state is bit-identical, as EnvElement.cginc rule 2
                // requires.
                GhvrElem e = GhvrElems();
                if (e.live > 0.0)
                    c.rgb *= max(1.0 + 1.60 * e.light * (1.0 - e.dark) - 0.85 * e.dark, 0.0);

                // Anything under half an 8-bit step is a blend that changes
                // nothing. Skipping it saves the frame buffer read-modify-write
                // over the majority of the shell, which is clear sky. The picture
                // is identical either way — a bandwidth saving, not a look. (Safe
                // here specifically because ZWrite is already off, so there is no
                // early-Z fast path for the discard to cost.)
                clip(c.a - (0.5 / 255.0));
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
