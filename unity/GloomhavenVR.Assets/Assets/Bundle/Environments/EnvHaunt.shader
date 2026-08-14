// GloomhavenVR — THE HAUNTS. HAUNT: the creepy easter eggs, drawn.
//
// USER REQUEST, 2026-08-14 (verbatim, abridged): "'Grusel-Easter-Eggs' in den
// Umgebungen. Also grusilige Animationen (ohne sound) die ab und zu auftreten
// ... (nur Wald und Keller) ... nicht aufdringlich, eher im Hintergrund aber
// einen ordnelichen Gruselfaktor auslösen - wie zB eine lächelnde fratze die
// hinter einem Baum hervorguckt etc. ... sollen niemals den Spielfluss stören
// ... sollen sie synchron von allen Spielern an den selben Stellen sichtbar
// sein. Weiterhin sollen sie sich auch mit den aktuellen Elementen nicht im weg
// stehen oder deswegen ihren gruselfaktor verlieren."
//
// USER VERDICT ON THE FIRST BUILD OF IT (hardware, ModBuild 141): "Ich habe nur
// einmal ein Easter Egg im Keller gesehen, das war so ein lächelndes 2D Gesicht
// im Schrank - das ist weit entfernt von echtem Horror - das sah eher Lächerlich
// aus. Die Easter eggs sollen echten Horror verbreiten, kein Kindergeburtstag
// sein." And: "Prüfe alle Easter eggs auf den horror faktor. Der soll hoch sein!"
//
// WHEN anything happens is EnvHaunt.cginc's problem — read that first; this
// file is only WHAT it looks like. The two halves meet at exactly one place:
// the vertex shader asks the schedule whether THIS card is this slot's event
// and how far into it we are.
//
// ============================================================================
// ONE MESH, ONE MATERIAL, ONE DRAW CALL, SIX EVENTS.
//
// A room's whole catalogue is four vertices per event welded into a single
// mesh. Every vertex of a card carries the same room-space CENTRE, and the quad
// is expanded in the vertex shader out of two extent vectors the bake stored in
// TANGENT (right * halfWidth) and NORMAL (up * halfHeight). A card that is not
// this slot's event is simply never expanded: all four vertices stay on the
// centre point, the triangles are degenerate, nothing rasterises. That is the
// rat's trick (EnvCritter's `vis`), and it is what makes "the feature is off"
// cost one uniform read and nothing else.
//
// NORMAL IS NOT A SHADING NORMAL HERE and that is deliberate, not lazy: these
// cards are never lit BY THE SCENE — they carry their own baked lighting, and
// the two light COLOURS the card mixes it with come from the vertex stream. The
// channel was free, it is exactly the right size, and using it keeps the vertex
// layout to what Unity already ships in every mesh.
// ============================================================================
//
// ============================================================================
// VR SAFETY — the sharpest constraint on this feature, stated before the code.
//
// PERMANENT USER RULING: "Das der nebel beim Kopfschütteln sich dreht vermutlich
// zu einem gerichtet. ... versichere dich das er nicht dieses Verhlaten hat."
// Nothing in this mod may re-orient with the head.
//
// A face that looks AT you is the one thing that most wants to be a billboard,
// and a billboard is exactly what is forbidden — it would also break stereo,
// because a camera-facing quad is at a different angle in each eye. So the
// cards do not face the camera. They face the BOARD / ROOM CENTRE, which is
// world-fixed, is baked once, and is within a metre or so of where the player's
// head actually is. A card facing the board is legal; a card facing the head is
// not. GREP TEST FOR A REVIEWER: there is no _WorldSpaceCameraPos, no
// UNITY_MATRIX_V, no unity_CameraToWorld and no ComputeScreenPos anywhere below
// this line. The picture is a pure function of (room-space position, shared
// clock, element board) — identical in both eyes, and identical on every client.
// ============================================================================
//
// ============================================================================
// WHY THE APPARITIONS ARE SAMPLED AND NO LONGER DRAWN.
//
// THIS FILE USED TO BUILD THEM OUT OF SIGNED-DISTANCE PRIMITIVES, on the
// argument that SDFs are crisp at any distance, cost no memory and can MORPH (a
// grin that widens while it watches). All three claims are true. The pictures
// were still cartoons, and the user's verdict above is the proof: the cellar's
// "grinning face" rendered as a flat uniform white oval with two round dots and
// a symmetric smile arc — an emoji, at furniture height, on a candle-lit wall,
// as the brightest object in the frame. The crouching thing rendered as two
// glowing blobs; the forest's watcher as a chess pawn; the handprints as three
// copies of the waving-hand emoji.
//
// The failure is structural, not a matter of tuning. A handful of SDF
// primitives can only produce A SILHOUETTE WITH FEATURES DRAWN ON IT, and a
// silhouette with features drawn on it IS the grammar of a pictogram. What
// makes a face in the dark frightening is not its outline: it is VALUE. Most of
// it must be indistinguishable from the dark, with a cheekbone, a jaw edge and
// one wet gleam coming out of it. Value needs shading; shading needs a modelled
// surface with cast shadows and cavity occlusion; and none of that fits in a
// fragment program that also has to run six of these.
//
// So the likenesses are RENDERED ON THE CPU AT BAKE TIME
// (BuildEnvironments.MakeHauntAtlas) — a real depth field, a grazing key light,
// a 40-tap heightfield shadow march, cavity occlusion, noise-broken skin,
// chewed outlines, hair. What this shader kept is everything that has to be
// live: the schedule, the envelope, the element compensation, the occlusion
// against real geometry, and the MICRO-MOTION. The one thing given up is the
// morph — and losing it is a gain, because a widening grin is precisely the
// cartoon element the user rejected. A slow head TILT, a damped sway and a
// per-eye blink replaced it, and all three are UV transforms.
//
// THE ATLAS FORMAT (Env_Haunt.png, 1024x1024, 4x4 cells of 256, uncompressed):
//   R = KEY value    what the room's key light puts on the surface
//   G = RIM band     1 on the outline, falling inward — the element compensation
//   B = FILL value   a second, opposing light
//   A = COVERAGE     the occlusion; a dark apparition is A ~ 1, R,B ~ 0, i.e. a
//                    HOLE in the scene. That is why the blend is ordinary alpha
//                    and not additive: half this catalogue is DARKER than what
//                    is behind it, and additive cannot darken.
// ============================================================================
//
// ============================================================================
// THE KINDS. There are now four, not eight — the variety moved into the atlas,
// where it can be LOOKED AT. WHERE each apparition is placed, and which tile it
// uses, is in BuildEnvironmentRooms's two catalogues, next to the coordinates.
//   0 TILE  — one apparition, sampled, optionally sliding out from behind a real
//             edge, with a slow tilt or a damped sway.
//   1 EYES  — the eye tile placed TWICE, at two sizes, two heights and two blink
//             times. Nothing about the pair matches.
//   2 CROSS — a tile that crosses the card, optionally smeared along its travel.
//   3 NONE  — draws nothing. The card exists only to OCCUPY a schedule slot for
//             an event that happens in another shader (the cobwebs shivering as
//             if something large had just passed behind them). Being a real card
//             is what puts it under the same "never twice running" and "never
//             two at once" rules as the visible events.
// ============================================================================
//
// ============================================================================
// THE ELEMENTS — "sollen sich mit den aktuellen Elementen nicht im weg stehen
// oder deswegen ihren gruselfaktor verlieren."
//
// SEPARATE CHANNELS, so they cannot fight: the elements own COLOUR CAST and
// particles, the haunt owns SILHOUETTE and brief motion. Then, rather than
// merely not-breaking, each element FLAVOURS it — and the two that could
// destroy the effect are COMPENSATED, in opposite directions:
//   LIGHT  the room is bright: a dark apparition would wash out. So its unlit
//          parts go BLACKER (x0.40) and it gains a dark CONTOUR along the baked
//          rim band. It also becomes rarer (x0.65 at full Light).
//   DARK   the room went black: a black apparition would be invisible. So the
//          rim band lights up (0.30 -> 0.85) and it becomes more frequent
//          (x1.60 at full Dark).
//   FIRE   the rim becomes unsteady and warm — lit by something that is not
//          there.
//   ICE    it HOLDS LONGER (duration x1.35) and its motion freezes (x0.35).
//   AIR    it DRIFTS sideways over the hold, as if something moved it.
//   EARTH  it sits LOWER and its bottom edge is eaten by the ground.
// Frequency changes are deterministic and stay client-identical: they are a
// hash of the slot compared against an element-derived threshold, and the
// element board is scenario-wide state the game itself desync-checks every
// round (Core/ElementMood.cs, "MULTIPLAYER: ZERO NEW WIRE BYTES").
// ============================================================================
Shader "GloomhavenVR/EnvHaunt"
{
    Properties
    {
        // The apparition atlas. NOT a colour map: see the channel packing above.
        [NoScaleOffset] _Atlas ("Apparition atlas (R key, G rim, B fill, A cover)", 2D) = "black" {}
        _Period ("Slot beat (s) — one event at most per slot", Float) = 83
        _Cards ("Event count in this room (a multiple of 3)", Float) = 6
        // THE RIM IS THE PRIMARY READ FOR EVERY DARK KIND, and that is a finding
        // and not a preference. The first bake gave the silhouettes a body colour
        // of 0.020 linear, on the reasoning that "nearly black" is nearly black.
        // It is not: the night forest's own blacks sit around 0.002-0.005 linear
        // and the cellar's stair recess is 0.000, so 0.020 came out as a PALE GREY
        // FIGURE — three to ten times brighter than everything around it, i.e. the
        // brightest object in the frame, which is the exact opposite of "eher im
        // Hintergrund". The previews are what caught it (env_swamp_HauntWatcher,
        // env_cellar_HauntStair), and the mistake is worth writing down because the
        // gamma encode makes it invisible in a colour picker: linear 0.020 is sRGB
        // 0.13, i.e. 34/255, against a background of 0.
        //
        // So the bodies are a HOLE in the scene rather than an object in it — the
        // atlas gives them coverage and almost no value — and the thing that makes
        // a hole findable is its edge. Every dark card carries its own rim
        // multiplier (par.w), because how much edge a shape needs depends entirely
        // on what is behind it: the thing at the window is backlit by the moon and
        // needs almost none, while the figure crossing the stair doorway is a black
        // shape on a black recess and is nothing at all without one.
        // The room's FILL light — one colour for the whole room, scaled per card
        // by uv0.z. What it multiplies is the atlas's B channel, a second baked
        // lighting solution from the side the key does not come from.
        _Fill ("Room fill light", Color) = (0.06,0.08,0.13,1)
        _Rim ("Self-lit rim, base amount", Range(0,1)) = 0.08
        _RimCold ("Rim colour (cold)", Color) = (0.42,0.56,0.78,1)
        _RimWarm ("Rim colour under Fire", Color) = (0.95,0.48,0.16,1)
    }

    SubShader
    {
        // Transparent+2: BEFORE the moonbeam (Transparent+10) and the halos
        // (+5), so the beam's additive light falls ON the thing at the window
        // rather than under it — which is what makes a head that interrupts the
        // beam read as being IN the beam. Ordinary alpha, not additive: half
        // this catalogue is DARKER than what is behind it, and additive cannot
        // darken.
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // ZTest LEqual (the default) is LOAD-BEARING and is how the user's own
        // example is built: the forest face card sits just BEHIND the trunk's
        // axis, so the trunk's real opaque geometry depth-rejects whatever part
        // of the face has not come out from behind it yet. The face is occluded
        // by the tree because it IS behind the tree, not because anything was
        // masked.
        //
        // Cull Off, and here that is a decision rather than a default. This
        // project has been bitten three times by geometry wound against the side
        // it is seen from (most recently the rat, which now has a
        // closed-and-outward build gate). A flat card has no inside to be wrong
        // about and its atlas lookup is in its own uv, so drawing both faces
        // removes that entire class of bug at a cost of exactly zero: five of
        // the six cards in a room are collapsed to a point at any instant, and
        // the sixth is a few hundred pixels. The bake still ASSERTS that every
        // card's normal points at the room centre (AssertHauntCards) — not
        // because the shader needs it, but because a card facing away is a card
        // whose apparition is mirrored, and these apparitions are asymmetric on
        // purpose, so mirroring one is visible.
        Cull Off
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
            float _Period, _Cards, _Rim;
            fixed4 _RimCold, _RimWarm, _Fill;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            #define GHVR_PI 3.14159265
            // Atlas geometry. Mirrored in BuildEnvironments (HauntAtlasCols/Rows,
            // HauntTile) — change one, change both.
            #define GHVR_ATLAS_COLS 4.0
            #define GHVR_ATLAS_CELL 0.25
            // Half a mip-2 texel, as a fraction of a cell. The bake already
            // clears a five-texel guard band round every cell, so this is the
            // second of two independent defences against a trilinear tap
            // wandering into the neighbouring apparition at distance.
            #define GHVR_ATLAS_INSET 0.016

            struct appdata
            {
                float4 vertex  : POSITION;   // the card CENTRE, room space (all 4 verts)
                float3 normal  : NORMAL;     // card UP    * halfHeight (NOT a shading normal)
                float4 tangent : TANGENT;    // card RIGHT * halfWidth
                float4 uv      : TEXCOORD0;  // xy = corner (+-1,+-1), z = fill share, w unused
                float4 id      : TEXCOORD1;  // x card index, y kind, z aspect (hw/hh), w ATLAS TILE
                float4 env     : TEXCOORD2;  // reveal, hold, fade, sway (or blink lag)
                float4 par     : TEXCOORD3;  // slide-u / travel, tilt, slide-v / smear, rim mul
                fixed4 color   : COLOR;      // rgb = the KEY light's colour, a = base opacity
                // EIGHT VERTEX ATTRIBUTES IS THE CEILING, and it is a hard one:
                // POSITION, NORMAL, TANGENT, COLOR and UV0..UV3 is exactly eight.
                // A round of this file carried the per-card fill COLOUR in a ninth
                // (TEXCOORD4) and it did not fail loudly — the streams aliased, the
                // fragment read the `par` vector as a colour, and every apparition
                // in both rooms came out with a bright red outline. Anything new
                // goes into the spare lanes above, never into a ninth channel.
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 uv    : TEXCOORD0;   // xy card uv, z presence, w phase
                float4 id    : TEXCOORD1;
                float4 par   : TEXCOORD2;
                // Only .w (the sway / blink lag) is read in the fragment — the
                // three durations are consumed in the vertex shader. Carried whole
                // anyway: splitting one float out of an authored vector so that a
                // reader has to look in two places to find "the event's shape" is
                // a false economy on a mesh with twenty-four vertices.
                // REPACKED by the vertex shader: (fill share, 0, 0, sway). The
                // three durations are consumed up there and the fragment never
                // needs them, so the lane carries what the fragment does need.
                float4 env   : TEXCOORD3;
                fixed4 color : COLOR;
            };

            /// Sample one apparition out of the atlas. `t` is TILE SPACE: the tile
            /// is a SQUARE IN METRES of side 2 * the card's half-height, centred on
            /// the card, so a head is a head whatever proportions the card has.
            /// Anything outside the tile returns zero rather than the neighbouring
            /// cell — with Clamp wrap an atlas is not self-clamping, and a card
            /// that is wider than its tile (every sliding one is) samples outside
            /// on every frame.
            float4 GhvrHauntSample (float2 t, float tile)
            {
                float inside = step(max(abs(t.x), abs(t.y)), 1.0);
                float2 uv = saturate(t * 0.5 + 0.5);
                uv = uv * (1.0 - 2.0 * GHVR_ATLAS_INSET) + GHVR_ATLAS_INSET;
                // ROUND FIRST. `tile` is a small integer that has been through a
                // perspective-correct interpolator, and that is not exact: the GPU
                // divides two interpolated quantities, so a constant 4.0 arrives
                // as 4.0 +- an ulp. floor(3.9999998 / 4) is 0 and
                // fmod(3.9999998, 4) is 3.9999998, so a single last-bit wobble
                // does not shift the lookup by a texel — it lands in a DIFFERENT
                // APPARITION and, because the u coordinate then leaves the atlas
                // and clamps, smears one column of it across the card. The preview
                // that caught it (env_swamp_HauntWatcher) looked like a corrupted
                // scanline effect over the figure, per row, which is exactly what
                // a per-pixel wobble across a quad produces.
                //
                // This file already carries the same warning for the card index
                // ("an exact float compare on a value that has been through an
                // interpolator is a bug waiting for a driver"); floor() and fmod()
                // are the same hazard with a louder failure.
                float ti = floor(tile + 0.5);
                float2 cell = float2(fmod(ti, GHVR_ATLAS_COLS),
                                     floor(ti / GHVR_ATLAS_COLS));
                return tex2D(_Atlas, (uv + cell) * GHVR_ATLAS_CELL) * inside;
            }

            /// Rotate tile space about a pivot. THE MICRO-MOTION LIVES HERE, and
            /// it is deliberately the only motion most of these events have: "a
            /// three-degree head tilt over four seconds, a single blink, a tremor.
            /// Nothing should sweep or wave." A monotone tilt is also unfalsifiable
            /// in the way that matters — you cannot tell afterwards whether the
            /// head moved or you did.
            float2 GhvrHauntTurn (float2 t, float ang, float pivotY)
            {
                float2 q = t - float2(0.0, pivotY);
                float s, c; sincos(ang, s, c);
                return float2(q.x * c - q.y * s, q.x * s + q.y * c) + float2(0.0, pivotY);
            }

            // ------------------------------------------------------------ vertex
            v2f vert (appdata v)
            {
                float t = _Time.y + _GhvrTimeOfs;
                GhvrHaunt h = GhvrHauntAt(t, _Period, _Cards);

                float phase;
                float a = GhvrHauntEnvelope(h.sIn, h.start, v.env.x, v.env.y, v.env.z,
                                            h.durMul, phase);
                // abs()<0.5, not ==: see GhvrHauntPresence.
                float mine = step(abs(h.card - v.id.x), 0.5);
                float vis = a * h.live * mine;

                // COLLAPSE OR FULL SIZE — never in between. Scaling the quad by
                // `vis` would shrink the apparition as it faded, and a face that
                // gets smaller while it goes is a completely different (and much
                // sillier) effect than one that goes.
                float on = step(1e-4, vis);
                float3 P = v.vertex.xyz
                         + on * (v.uv.x * v.tangent.xyz + v.uv.y * v.normal.xyz);

                v2f o;
                o.pos = UnityObjectToClipPos(float4(P, 1.0));
                o.uv = float4(v.uv.xy, vis, phase);
                o.id = v.id;
                o.par = v.par;
                o.env = float4(v.uv.z, 0.0, 0.0, v.env.w);
                o.color = v.color;
                return o;
            }

            // ---------------------------------------------------------- fragment
            fixed4 frag (v2f i) : SV_Target
            {
                // vis == 0 => the card collapsed to a point; nothing may flash.
                clip(i.uv.z - 1e-4);

                float t = _Time.y + _GhvrTimeOfs;
                GhvrElem e = GhvrHauntElems();

                float kind   = i.id.y;
                float aspect = max(i.id.z, 1e-3);
                float tile   = i.id.w;
                float pres   = i.uv.z;      // 0..1 presence (the envelope)
                float phase  = i.uv.w;      // 0..1 through the whole event

                // isotropic card space: y in [-1,1], x in [-aspect,+aspect], so a
                // metre is a metre whatever the card's proportions are — and so the
                // tile, which is authored as a square in metres, is sampled as one.
                float2 p = float2(i.uv.x * aspect, i.uv.y);

                // ---- ELEMENT DISPLACEMENT (see the header) ----
                // AIR: it drifts over the hold. ICE freezes the drift with
                // everything else that moves.
                float still = 1.0 - 0.65 * e.ice;
                p.x -= 0.22 * e.air * still * sin(phase * GHVR_PI);
                // EARTH: it sits lower, and the ground eats its bottom edge.
                p.y += 0.15 * e.earth;

                float4 T = 0.0;             // the sampled apparition
                float alpha = i.color.a;

                if (kind < 0.5)
                {
                    // ---------------------------------------------------- TILE
                    // It eases OUT from behind the edge it is hiding behind, and
                    // withdraws the same way. `pres` is already the smoothstep
                    // in/out, so the motion and the envelope cannot disagree.
                    float outAmt = pres * still + (1.0 - still) * 0.75;
                    float2 q = p - float2(i.par.x, i.par.z) * outAmt;
                    // MICRO-MOTION. A head pivots at the NECK, below it; a thing
                    // hung by the feet pivots at the BRANCH, above it — which is
                    // why the pivot follows whether this card sways at all.
                    float pivotY = (abs(i.env.w) > 1e-4) ? 1.0 : -1.0;
                    float ang = i.par.y * smoothstep(0.10, 0.90, phase) * still
                              + i.env.w * sin(phase * 6.0) * exp(-phase * 2.2) * still;
                    q = GhvrHauntTurn(q, ang, pivotY);
                    T = GhvrHauntSample(q, tile);
                    // the alpha ramps FASTER than the slide, so it is mostly there
                    // by the time it clears the edge rather than fading in in the
                    // open where the eye can catch it arriving
                    alpha *= smoothstep(0.08, 0.48, pres);
                }
                else if (kind < 1.5)
                {
                    // ---------------------------------------------------- EYES
                    // ONE tile, placed TWICE. par.x is the half-separation, par.y
                    // the second eye's size ratio and par.z its height offset —
                    // so the pair is asymmetric by construction. env.w lags the
                    // second blink behind the first: two eyes that close together
                    // belong to a face, two that do not belong to something that
                    // is not built like a face.
                    float lidA = 1.0 - 0.97 * exp(-pow((phase - 0.55) / 0.045, 2.0));
                    float lidB = 1.0 - 0.97 * exp(-pow((phase - 0.55 - i.env.w) / 0.052, 2.0));
                    float openA = smoothstep(0.0, 0.18, phase) * lidA;
                    float openB = smoothstep(0.0, 0.26, phase) * lidB;

                    float2 ca = float2(-i.par.x, 0.0);
                    float2 cb = float2(i.par.x * 0.86, i.par.z);
                    float4 A = GhvrHauntSample((p - ca) / float2(0.62, 0.62 * max(openA, 0.05)),
                                               tile) * openA;
                    float4 B = GhvrHauntSample((p - cb) / float2(0.62 * i.par.y,
                                                                 0.62 * i.par.y * max(openB, 0.05)),
                                               tile) * openB;
                    T = max(A, B);
                    alpha *= smoothstep(0.02, 0.20, pres);
                }
                else if (kind < 2.5)
                {
                    // --------------------------------------------------- CROSS
                    // Something goes past — a few tenths of a second, so you get
                    // the motion and not the shape. Smeared along its own travel:
                    // that is what a thing seen for 0.3 s actually looks like, and
                    // it costs one divide.
                    float x0 = lerp(-1.25, 1.25, phase) * aspect * i.par.x;
                    float2 q = float2((p.x - x0) / (1.0 + i.par.z), p.y);
                    T = GhvrHauntSample(q, tile);
                    alpha *= smoothstep(0.0, 0.22, pres);
                }
                else
                {
                    // ---------------------------------------------------- NONE
                    // An event that happens somewhere else (the cobwebs shiver;
                    // see EnvRoomCutout). Occupying a card is what keeps it inside
                    // the schedule's no-repeat and no-collision rules.
                    discard;
                }

                // ---- THE COMPOSITE ----
                // Two coloured lights per card: the card's vertex COLOR is the key
                // (the cellar's candles are warm; the forest's moon is cold) and
                // TEXCOORD4 is the fill. One baked head can therefore stand in a
                // warm room and a cold one without being baked twice.
                float3 col = i.color.rgb * T.r + _Fill.rgb * i.env.x * T.b;
                alpha *= T.a * pres;

                // ---- ELEMENT COMPENSATION (the half that keeps it frightening) ----
                // LIGHT: what is NOT lit goes blacker rather than washing out, and
                // the whole apparition gains a dark contour along its baked rim.
                col *= lerp(1.0, 1.0 - 0.60 * e.light, saturate(1.0 - T.r));
                col = lerp(col, col * 0.10, e.light * T.g * 0.85);

                // DARK: a faint self-lit rim, or the shape is a black hole in a
                // black room. FIRE makes that rim unsteady and warm. par.w is the
                // per-card rim multiplier — clamped at 0 rather than defaulted, so
                // a card that leaves it unset simply has no rim, which is right for
                // the pale kinds whose read is their own brightness.
                float rimAmt = (_Rim + 0.55 * e.dark) * max(i.par.w, 0.0)
                             * (1.0 + 0.32 * e.fire * sin(t * 9.3 + i.id.x * 2.1));
                float3 rimCol = lerp(_RimCold.rgb, _RimWarm.rgb, saturate(e.fire));
                col += rimCol * (T.g * rimAmt);

                // EARTH: the ground eats the lower edge.
                alpha *= 1.0 - 0.85 * e.earth * smoothstep(-0.55, -0.98, p.y);

                clip(alpha - 0.004);
                return fixed4(col, saturate(alpha));
            }
            ENDCG
        }
    }
    Fallback Off
}
