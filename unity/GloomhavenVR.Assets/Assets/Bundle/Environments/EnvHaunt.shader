// GloomhavenVR — THE HAUNTS. HAUNT: the creepy easter eggs, as SOLIDS.
//
// USER REQUEST, 2026-08-14 (verbatim, abridged): "'Grusel-Easter-Eggs' in den
// Umgebungen. Also grusilige Animationen (ohne sound) die ab und zu auftreten
// ... (nur Wald und Keller) ... nicht aufdringlich, eher im Hintergrund aber
// einen ordnelichen Gruselfaktor auslösen - wie zB eine lächelnde fratze die
// hinter einem Baum hervorguckt etc. ... sollen niemals den Spielfluss stören
// ... sollen sie synchron von allen Spielern an den selben Stellen sichtbar
// sein."
//
// WHEN anything happens is EnvHaunt.cginc's problem — read that first; this
// file is only WHAT it looks like.
//
// ============================================================================
// HAUNT SOLID — THE RULING THIS FILE WAS REWRITTEN FOR.
//
// USER VERDICT, hardware, ModBuild 143. He says it SEVEN times, so it is quoted
// at length rather than paraphrased:
//   forest 6  "Die Fratzenidee hinter Baum ist gut, aber mach keine 2D Fratzen,
//              das sieht man, dass es 2D ist."
//   forest 7  "Auch die Beobachter Idee ist gut aber auch ein 2D Pappaufsteller,
//              lieber wirklich eine Horrorgestalt die einfach da steht."
//   forest 8  "Genauso das 'etwas huscht herbei' — generell keine 2D
//              Pappaufsteller."
//   forest 9  "Dunkle Masse sehe ich gar nichts."
//   forest 10 "Auch hängender Körper sehe ich nichts, aber auch die Idee hier
//              ist gut, dass man jemanden/eine Silhouette erkennt von jemandem
//              der sich erhängt hat an einem Baum."
//   cellar 7  "Die Fratze sieht man deutlich, dass sie 2D ist, bitte keine 2D
//              Effekte, lieber einen 3D Kopf und Silhouette die durch Fenster
//              schaut — gruselig wäre auch wenn sie beim Mondlicht einen
//              Schatten wirft wenn sie durchs Fenster schaut."
//   cellar 9  "Gesicht am Boden sehe ich gar nicht."
//   cellar 10 "Auch die Treppenerscheinung ist offensichtlich 2D."
//
// THE PREVIOUS ROUND FIXED THE WRONG HALF. It replaced signed-distance drawing
// with a CPU-baked likeness atlas, which cured the emoji problem — the faces
// stopped being pictograms — and left the cardboard problem exactly where it
// was, because a baked likeness on a quad is still a quad. In stereo the two
// eyes disagree about a flat card's depth in a way they never disagree about a
// solid's, and at 8-16 m the player walks around the room and the card does not
// change. He caught every single one.
//
// So the apparitions are MESHES now. A head is a head-shaped mesh; a standing
// figure is a body; the hanged man is a body with a rope. Leaning changes what
// you see of them, the trunk and the barrels occlude them honestly, and the
// thing at the window casts a real shadow because there is a real occluder in
// the opening. The geometry comes from a CC0 human base mesh, decimated and
// posed at bake time — see haunt_figures_pipeline.py for the licence, the
// search that preceded it, and what was rejected.
//
// WHAT WAS KEPT FROM THE ATLAS ROUND:
//   * the SCHEDULE, whole and unchanged (EnvHaunt.cginc);
//   * the ENVELOPE with its asymmetric fade and its instant vanish;
//   * the ELEMENT compensation, both directions, and the element flavours;
//   * the COLLAPSE-TO-A-POINT trick, which is what makes five idle apparitions
//     cost one uniform read — it now collapses a whole body instead of a quad;
//   * the ATLAS ITSELF, for the one card that is honestly flat: the handprints
//     on the wet wall. A print IS two-dimensional, it lies IN the wall plane,
//     and its parallax is therefore correct. "Keine 2D Pappaufsteller" is about
//     things that stand up in the air, and a print does not.
// What went is the quad, the tile-space UV transform, and the micro-motion
// expressed as a UV shear — all three are now rigid-body motion of real
// geometry, which is the same effect with a shape behind it.
// ============================================================================
//
// ============================================================================
// WHY THE LIGHTING IS BAKED INTO THE VERTEX COLOUR, and why that is not a
// compromise.
//
// Each apparition carries its own key light — the moon for the wood, the
// candle it is nearest for the cellar — and diffuse shading N·L is
// VIEW-INDEPENDENT, so evaluating it per vertex at bake time is not an
// approximation of evaluating it per fragment, it is the same number. The
// builder knows every apparition's position and its key direction, so it
// computes key·N + a hemispherical fill per vertex and writes it into COLOR.
// What the fragment then does is what actually has to be live: the envelope,
// the element compensation and the rim.
//
// The one thing this gives up is that an apparition's baked light TURNS WITH IT
// when it rotates. The largest rotation in either catalogue is the hanged
// body's slow turn; at 9 m, in a wood whose ambient is 0.024, nobody can see
// that the moon turned twenty degrees with the corpse. It is written down
// because the next person to add a spinning apparition needs to know the limit.
// ============================================================================
//
// ============================================================================
// VR SAFETY — the sharpest constraint on this feature, stated before the code.
//
// PERMANENT USER RULING: "Das der nebel beim Kopfschütteln sich dreht vermutlich
// zu einem gerichtet. ... versichere dich das er nicht dieses Verhlaten hat."
// Nothing in this mod may re-orient with the head.
//
// With solids this stops being a temptation as well as a rule: a mesh does not
// need to face anybody. GREP TEST FOR A REVIEWER: there is no
// _WorldSpaceCameraPos, no UNITY_MATRIX_V, no unity_CameraToWorld, no
// ObjSpaceViewDir and no ComputeScreenPos anywhere below this line in the
// apparition path. The picture is a pure function of (room-space geometry,
// shared clock, element board) — identical in both eyes and on every client.
// Even the RIM, which is a view-dependent quantity in every other shader in
// this bundle, is baked here against the direction of the ROOM CENTRE instead
// of the camera, for exactly that reason.
// ============================================================================
//
// ============================================================================
// THE KINDS. One material, one mesh, one draw call per room.
//   0 SOLID  — a placed body or head. It may slide out from behind a real edge
//              along `move`, and rotate once about `rotAxis` through a pivot on
//              its own vertical (a lean, a head tilt, a slow turn), plus a
//              damped sway on the same axis.
//   1 DECAL  — a flat mark lying IN a real surface: the handprints (atlas) and
//              the shadow the thing at the window throws on the floor (no
//              texture; its softness is per-vertex alpha).
//   2 CROSS  — a solid that travels the whole span of `move` across the event.
//   3 NONE   — never emitted; the card exists only to occupy a schedule slot
//              for an event another shader draws (the cobwebs' tremble).
//   5 EYES   — a solid with a blink: two eyeshines that do not close together.
//
//   4 PROP   — THE ODD ONE, and it is on the OPAQUE material. See below.
// ============================================================================
//
// ============================================================================
// KIND 4, THE PROP — why a bookshelf is compiled into an apparition shader.
//
// USER, cellar 11: "Genauso wie beim Bücherregal. Statt da auch ne Fratze zu
// machen: Wie wär es wenn das Bücherregal umkippt, und sich dann nach ner Zeit
// wieder von selbst aufstellt."
//
// That is a furniture animation, and this mod ships SCRIPT-FREE PREFABS: the
// only thing that can move a vertex at runtime is a vertex shader, so whatever
// moves the shelf has to be the shader the shelf is drawn with. It cannot be
// EnvRoom — that shader is shared by every prop in both rooms and has no
// business knowing the haunt schedule — so the shelf is drawn HERE, by the one
// shader that already owns the clock it has to obey.
//
// Two materials, one shader. The apparitions' material is transparent
// (Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off, Queue Transparent+2); the
// shelf's is opaque (Blend One Zero, ZWrite On, Queue Geometry). The render
// state is therefore material-controlled — [_SrcBlend]/[_DstBlend]/[_ZWrite]/
// [_Cull] — which is the standard Unity way to put two render states on one
// program and is what lets the shelf keep its normal map and the room's real
// point lights while the apparitions keep their baked ones.
//
// THE POSE IS A PURE FUNCTION OF THE CLOCK. angle = maxAngle * curve(phase),
// nothing accumulates, nothing integrates, and outside the event phase is 0 and
// the shelf stands. An interrupted event therefore cannot leave the shelf on
// its face: there is no state to be left in. That was the explicit requirement.
// ============================================================================
Shader "GloomhavenVR/EnvHaunt"
{
    Properties
    {
        // The apparition atlas — now used by ONE card, the handprints. See the
        // HAUNT SOLID block: a print is flat because a print is flat.
        [NoScaleOffset] _Atlas ("Flat-mark atlas (R key, G rim, B fill, A cover)", 2D) = "black" {}
        _Period ("Slot beat (s) — one event at most per slot", Float) = 83
        _Cards ("Event count in this room (a multiple of 3)", Float) = 6
        // The room's FILL light — one colour for the whole room. The solids have
        // their fill baked into COLOR already; this is what the DECAL kind
        // multiplies the atlas's B channel with.
        _Fill ("Room fill light", Color) = (0.06,0.08,0.13,1)
        _Rim ("Self-lit rim, base amount", Range(0,1)) = 0.08
        _RimCold ("Rim colour (cold)", Color) = (0.42,0.56,0.78,1)
        _RimWarm ("Rim colour under Fire", Color) = (0.95,0.48,0.16,1)

        // ---- KIND 4, THE PROP (the tipping bookshelf) ----
        _MainTex ("Prop albedo", 2D) = "white" {}
        _BumpMap ("Prop normal map", 2D) = "bump" {}
        _BumpScale ("Prop normal strength", Range(0,2)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        // The baked rig, written by EnvRoomBuilder.ApplyRig exactly as it is for
        // every EnvRoom material — same names, same object-space contract, so a
        // prop drawn here is lit by the same three candles as the prop beside it.
        _AmbUp ("Hemisphere ambient - sky", Color) = (0.05,0.06,0.08,1)
        _AmbDown ("Hemisphere ambient - ground", Color) = (0.015,0.015,0.015,1)
        _DirDir ("Directional dir (OBJECT space, toward light)", Vector) = (0,1,0,0)
        _DirCol ("Directional color", Color) = (0,0,0,1)
        _L0Pos ("Light0 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L0Col ("Light0 color (a=flicker)", Color) = (0,0,0,0)
        _L1Pos ("Light1 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L1Col ("Light1 color (a=flicker)", Color) = (0,0,0,0)
        _L2Pos ("Light2 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L2Col ("Light2 color (a=flicker)", Color) = (0,0,0,0)
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        _RimDir ("Rim gate: light dir (OBJECT space)", Vector) = (0,1,0,0)

        // ---- render state, so two materials can share one program ----
        [HideInInspector] _SrcBlend ("src blend", Float) = 5   // SrcAlpha
        [HideInInspector] _DstBlend ("dst blend", Float) = 10  // OneMinusSrcAlpha
        [HideInInspector] _ZWrite ("zwrite", Float) = 0
        [HideInInspector] _Cull ("cull", Float) = 2            // Back
    }

    SubShader
    {
        // Transparent+2 for the apparitions: BEFORE the moonbeam (Transparent+10)
        // and the halos (+5), so the beam's additive light falls ON the thing at
        // the window rather than under it. The opaque prop material overrides the
        // queue to Geometry from C# (Material.renderQueue).
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        // CULL BACK, and unlike the card round this is now load-bearing rather
        // than free. The apparitions are CLOSED SOLIDS: the builder proves every
        // one of them is watertight and outward-wound before it welds it
        // (AssertClosedAndOutward), because this project has shipped inward-wound
        // geometry three times — the window bars, the moonbeam blades, and the
        // rat you could see the inside of. Culling the back faces is also what
        // keeps an alpha-blended solid from compositing itself twice.
        Cull [_Cull]
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvHaunt.cginc"

            sampler2D _Atlas;
            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            float _Period, _Cards, _Rim, _BumpScale, _PtHard;
            fixed4 _RimCold, _RimWarm, _Fill, _Tint;
            fixed4 _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos, _RimDir;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            #define GHVR_PI 3.14159265
            // Atlas geometry. Mirrored in BuildEnvironments (HauntAtlasCols/Rows,
            // HauntTile) — change one, change both.
            #define GHVR_ATLAS_COLS 4.0
            #define GHVR_ATLAS_CELL 0.25
            #define GHVR_ATLAS_INSET 0.016

            #define KIND_SOLID 0
            #define KIND_DECAL 1
            #define KIND_CROSS 2
            #define KIND_PROP  4
            #define KIND_EYES  5

            struct appdata
            {
                // EIGHT VERTEX ATTRIBUTES IS THE CEILING, and it is a hard one:
                // POSITION, NORMAL, TANGENT, COLOR and UV0..UV3 is exactly eight.
                // A round of the card version carried a ninth (TEXCOORD4) and it
                // did NOT fail loudly — the streams aliased, the fragment read a
                // parameter vector as a colour, and every apparition in both rooms
                // came out with a bright red outline. Anything new goes into the
                // spare lanes below, never into a ninth channel. Which is why
                // three kinds REINTERPRET the same lanes; each one says so.
                float4 vertex  : POSITION;   // room-space vertex, in its ACTIVE pose
                float3 normal  : NORMAL;     // shading normal (read by PROP only)
                float4 tangent : TANGENT;    // SOLID/DECAL: anchor.xyz, w = pivot height
                                             // PROP: the real tangent + sign
                float4 uv      : TEXCOORD0;  // x card index, y kind,
                                             // SOLID/DECAL: z rim, w atlas tile (<0 = none)
                                             // PROP: zw = albedo uv
                float4 env     : TEXCOORD1;  // reveal, hold, fade, sway (or blink lag)
                float4 mv      : TEXCOORD2;  // SOLID/CROSS: move dir.xyz, metres
                                             // DECAL: xy = atlas tile-space uv
                                             // PROP: xyz = the tipping pivot
                float4 rot     : TEXCOORD3;  // rotation axis.xyz, angle (rad)
                fixed4 color   : COLOR;      // SOLID: BAKED lit colour, a = opacity
                                             // DECAL: colour, a = per-vertex softness
                                             // PROP: tint
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 uv    : TEXCOORD0;   // x rim, y presence, z phase, w atlas tile
                float4 mv    : TEXCOORD1;   // xy atlas/albedo uv, z height over anchor, w kind
                float3 opos  : TEXCOORD2;   // object-space position (PROP lighting)
                float3 n     : TEXCOORD3;
                float3 t     : TEXCOORD4;
                float3 b     : TEXCOORD5;
                fixed4 color : COLOR;
            };

            /// Rodrigues, about `axis` through `pivot`. The apparitions rotate
            /// RIGIDLY — no per-vertex weight — because they are solids and a
            /// solid that bends while it leans is a solid with a rig, which is
            /// the thing this feature is built to do without.
            float3 GhvrHauntRot (float3 p, float3 pivot, float3 axis, float ang)
            {
                float3 q = p - pivot;
                float s, c; sincos(ang, s, c);
                return pivot + q * c + cross(axis, q) * s + axis * dot(axis, q) * (1.0 - c);
            }

            /// Sample the flat-mark atlas. `t` is TILE SPACE, [-1,1] square;
            /// outside it the lookup returns zero rather than the neighbouring
            /// cell, because with Clamp wrap an atlas is not self-clamping.
            float4 GhvrHauntSample (float2 t, float tile)
            {
                float inside = step(max(abs(t.x), abs(t.y)), 1.0);
                float2 uv = saturate(t * 0.5 + 0.5);
                uv = uv * (1.0 - 2.0 * GHVR_ATLAS_INSET) + GHVR_ATLAS_INSET;
                // ROUND FIRST. `tile` is a small integer that has been through a
                // perspective-correct interpolator, and that is not exact: the GPU
                // divides two interpolated quantities, so a constant 4.0 arrives
                // as 4.0 +- an ulp. floor(3.9999998 / 4) is 0, so a single
                // last-bit wobble lands in a DIFFERENT tile and, because u then
                // leaves the atlas and clamps, smears one column of it across the
                // card. That bug shipped once (env_swamp_HauntWatcher looked like
                // a corrupted scanline effect) and is cheap to make impossible.
                float ti = floor(tile + 0.5);
                float2 cell = float2(fmod(ti, GHVR_ATLAS_COLS),
                                     floor(ti / GHVR_ATLAS_COLS));
                return tex2D(_Atlas, (uv + cell) * GHVR_ATLAS_CELL) * inside;
            }

            // Candle flicker — EnvRoom's, term for term, because the shelf stands
            // in the same candle pool as the crates beside it and the two may not
            // disagree about how that candle breathes.
            float GhvrHauntFlicker (float amt, float phase, float rate)
            {
                float t = (_Time.y + _GhvrTimeOfs) * rate;
                float f = 0.42 * sin(t * 11.3 + phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
                f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
                return 1.0 + amt * 0.35 * f;
            }

            float3 GhvrHauntPoint (float4 lpos, fixed4 lcol, float3 opos, float3 N,
                                   float phase, float rate, float flickMul, float hardMul)
            {
                float3 lv = lpos.xyz - opos;
                float d2 = max(dot(lv, lv), 1e-8);
                float q = d2 * lpos.w * lpos.w;
                float x = saturate(1.0 - q);
                float atten = x * x / (1.0 + _PtHard * hardMul * q);
                float ndl = saturate(dot(N, lv / sqrt(d2)));
                return lcol.rgb * (atten * ndl * GhvrHauntFlicker(lcol.a * flickMul, phase, rate));
            }

            /// The bookshelf's tip-over, as a pure function of the event phase.
            ///
            /// IT FALLS FAST AND GETS UP SLOWLY, which is the whole picture: a
            /// shelf goes over in about a second under gravity and then stands
            /// itself back up over five, against nothing, which is the part that
            /// is wrong. Both halves are shaped curves of `phase` and NOTHING IS
            /// INTEGRATED — the schedule is the only thing that decides where the
            /// shelf is at time t, so a player who joins mid-event, or an event
            /// cut off by the slot ending, cannot leave it lying down.
            float GhvrShelfTip (float ph)
            {
                // 0.00-0.18  the topple: a quadratic, i.e. constant angular
                //            acceleration, which is what a falling body does
                // 0.18-0.24  the landing, held flat with one small bounce
                // 0.24-0.62  it lies there. This is the part that has to be long
                //            enough for somebody to walk over and look at it.
                // 0.62-1.00  it comes back up, slowly and evenly, and eases into
                //            standing so the last degree is not a snap.
                float fall  = saturate(ph / 0.18);
                float down  = fall * fall;
                float bounce = -0.055 * sin(saturate((ph - 0.18) / 0.06) * GHVR_PI)
                             * step(0.18, ph) * step(ph, 0.24);
                float rise  = smoothstep(0.62, 1.00, ph);
                return saturate(down - rise) + bounce * step(ph, 0.62);
            }

            // ------------------------------------------------------------ vertex
            v2f vert (appdata v)
            {
                float t = _Time.y + _GhvrTimeOfs;
                GhvrHaunt h = GhvrHauntAt(t, _Period, _Cards);
                int kind = (int)(v.uv.y + 0.5);

                float phase;
                float a = GhvrHauntEnvelope(h.sIn, h.start, v.env.x, v.env.y, v.env.z,
                                            h.durMul, phase);
                // abs()<0.5, not ==: see GhvrHauntPresence.
                float mine = step(abs(h.card - v.uv.x), 0.5);
                float vis = a * h.live * mine;
                // A card that is not this slot's event never runs at all, so its
                // phase must not be read either — a prop whose event is not
                // happening has to sit at phase 0, not at whatever the ACTIVE
                // card's phase happens to be.
                phase *= mine * h.live;

                GhvrElem e = GhvrHauntElems();
                float still = 1.0 - 0.65 * e.ice;   // ICE freezes what moves

                float3 P = v.vertex.xyz;
                float3 N = v.normal;
                float3 anchor = v.tangent.xyz;

                if (kind == KIND_PROP)
                {
                    // ---- the tipping bookshelf. Always drawn; the schedule only
                    // decides the angle, and outside the event that angle is 0.
                    float ang = v.rot.w * GhvrShelfTip(phase);
                    float3 ax = normalize(v.rot.xyz + float3(0, 1e-6, 0));
                    P = GhvrHauntRot(P, v.mv.xyz, ax, ang);
                    N = GhvrHauntRot(N, float3(0, 0, 0), ax, ang);
                    // the tangent has to turn with it or the normal map shears
                    float3 T = GhvrHauntRot(v.tangent.xyz, float3(0, 0, 0), ax, ang);
                    v2f o;
                    o.pos = UnityObjectToClipPos(float4(P, 1.0));
                    o.uv = float4(0, 1, phase, -1);
                    o.mv = float4(v.uv.zw, 0, (float)kind);
                    o.opos = P;
                    o.n = N;
                    o.t = T;
                    o.b = cross(N, T) * v.tangent.w;
                    o.color = v.color;
                    return o;
                }

                // COLLAPSE OR FULL SIZE — never in between. Scaling an apparition
                // by `vis` would shrink it as it faded, and a figure that gets
                // smaller as it goes is a completely different (and much sillier)
                // effect than one that goes. Every vertex of an idle apparition
                // sits on its anchor, so its triangles are degenerate and nothing
                // rasterises: five of six cost one uniform read.
                float on = step(1e-4, vis);

                if (kind == KIND_CROSS)
                {
                    // travels the whole span across the event
                    P += v.mv.xyz * (v.mv.w * (phase - 0.5));
                }
                else
                {
                    // eases OUT from behind the edge it hides behind, and
                    // withdraws the same way. `vis` is already the smoothstep in
                    // and out, so the motion and the envelope cannot disagree.
                    float outAmt = vis * still + (1.0 - still) * 0.75;
                    P += v.mv.xyz * (v.mv.w * outAmt);
                }
                // AIR drifts it sideways over the hold, as if something moved it;
                // EARTH sits it lower and the ground eats its foot.
                P.x -= 0.22 * e.air * still * sin(phase * GHVR_PI);
                P.y -= 0.15 * e.earth;

                // the one-shot rotation (a lean, a head tilt, a slow turn) plus a
                // damped sway on the same axis. The pivot is on the apparition's
                // own vertical, `pivot height` above its anchor — which is how a
                // head pivots at the neck below it and a hanged body at the branch
                // above it, out of one number.
                float ang = v.rot.w * smoothstep(0.10, 0.90, phase) * still
                          + v.env.w * sin(phase * 6.0) * exp(-phase * 2.2) * still;
                float3 axis = normalize(v.rot.xyz + float3(0, 1e-6, 0));
                float3 pivot = anchor + float3(0, v.tangent.w, 0);
                P = GhvrHauntRot(P, pivot, axis, ang);

                float alpha = v.color.a;
                if (kind == KIND_EYES)
                {
                    // two eyeshines that do not blink together: env.w lags the
                    // second one, and which one this vertex belongs to rides in
                    // the sign of the sway lane. A matched pair belongs to a face;
                    // a mismatched pair belongs to something that is not built
                    // like one.
                    float lag = max(v.env.w, 0.0) * step(0.0, v.tangent.w);
                    // x*x and not pow(x,2): pow() of a negative base is
                    // undefined in HLSL and this base is negative for the whole
                    // first half of the event.
                    // the blink sits at 0.66 of the event and not at 0.55: 0.55
                    // is the middle, which is exactly where every preview and
                    // every screenshot of this feature is taken, so the eyes were
                    // photographed SHUT every single time. Late is also the better
                    // place for it — you have already decided they are eyes by
                    // then, and the blink is the confirmation.
                    float x = (phase - 0.66 - lag) / 0.048;
                    float lid = 1.0 - 0.97 * exp(-x * x);
                    alpha *= smoothstep(0.0, 0.18, phase) * lid;
                }

                v2f o;
                o.pos = UnityObjectToClipPos(float4(lerp(anchor, P, on), 1.0));
                o.uv = float4(v.uv.z, vis * alpha, phase, v.uv.w);
                o.mv = float4(v.mv.xy, P.y - anchor.y, (float)kind);
                o.opos = P;
                o.n = N;
                o.t = float3(1, 0, 0);
                o.b = float3(0, 0, 1);
                o.color = v.color;
                return o;
            }

            // ---------------------------------------------------------- fragment
            fixed4 frag (v2f i) : SV_Target
            {
                int kind = (int)(i.mv.w + 0.5);
                GhvrElem e = GhvrHauntElems();

                if (kind == KIND_PROP)
                {
                    // ---- the tipping bookshelf: an ordinary lit prop, in the one
                    // shader that knows when it has to fall over.
                    fixed4 alb = tex2D(_MainTex, i.mv.xy) * _Tint;
                    float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.mv.xy));
                    n_ts.xy *= _BumpScale;
                    float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + normalize(i.n) * n_ts.z);
                    float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));

                    // ELEMENT ART — the same three gains every EnvRoom material
                    // takes, so a frosted room does not leave one prop unfrosted.
                    // The GROWN frost/moss patches (EnvGrowth) are not here: they
                    // need the growth field and a second noise, and this shader
                    // exists to tip a shelf over, not to re-implement a surface.
                    float ambGain = 1.0, srcGain = 1.0, dirGain = 1.0;
                    float hardMul = 1.0, flickMul = 1.0;
                    if (e.live > 0.0)
                    {
                        ambGain = GhvrAmbGain(e); srcGain = GhvrSrcGain(e);
                        dirGain = GhvrDirGain(e); hardMul = GhvrSrcHard(e);
                        flickMul = 1.0 + 1.20 * e.air;
                    }
                    float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5) * ambGain;
                    light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz))) * dirGain;
                    light += GhvrHauntPoint(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00, flickMul, hardMul) * srcGain;
                    light += GhvrHauntPoint(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83, flickMul, hardMul) * srcGain;
                    light += GhvrHauntPoint(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19, flickMul, hardMul) * srcGain;
                    return fixed4(alb.rgb * light, 1.0);
                }

                // vis == 0 => the apparition collapsed to a point; nothing may flash.
                float pres = i.uv.y;
                clip(pres - 1e-4);

                float t = _Time.y + _GhvrTimeOfs;
                float rim = i.uv.x;
                float3 col = i.color.rgb;     // BAKED key + fill (see the header)
                float alpha = pres;

                if (kind == KIND_DECAL && i.uv.w >= 0.0)
                {
                    // the handprints — the one honestly flat card left. Its value
                    // comes out of the atlas the way the whole catalogue used to.
                    float4 T = GhvrHauntSample(i.mv.xy, i.uv.w);
                    col = i.color.rgb * T.r + _Fill.rgb * T.b;
                    rim *= T.g;
                    alpha *= T.a;
                }

                // ---- ELEMENT COMPENSATION (the half that keeps it frightening) --
                // LIGHT: the room is bright, so a dark apparition would wash out.
                // What is NOT lit goes blacker rather than fading, and the whole
                // shape gains a dark contour along its rim.
                float lum = saturate(dot(col, float3(0.30, 0.59, 0.11)) * 3.0);
                col *= lerp(1.0, 1.0 - 0.60 * e.light, 1.0 - lum);
                col = lerp(col, col * 0.10, e.light * rim * 0.85);

                // DARK: a faint self-lit rim, or the shape is a black hole in a
                // black room. FIRE makes that rim unsteady and warm.
                float rimAmt = (_Rim + 0.16 * e.dark)
                             * (1.0 + 0.32 * e.fire * sin(t * 9.3 + i.uv.z * 6.3));
                float3 rimCol = lerp(_RimCold.rgb, _RimWarm.rgb, saturate(e.fire));
                col += rimCol * (rim * rimAmt);

                // EARTH: the ground eats the lower edge of whatever stands in it.
                alpha *= 1.0 - 0.85 * e.earth * smoothstep(0.10, -0.05, i.mv.z);

                clip(alpha - 0.004);
                return fixed4(col, saturate(alpha));
            }
            ENDCG
        }
    }
    Fallback Off
}
