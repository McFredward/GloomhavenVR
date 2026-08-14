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
// cards are never lit — they are apparitions, they have no material and they
// take no light from the room. The channel was free, it is exactly the right
// size, and using it keeps the vertex layout to what Unity already ships in
// every mesh rather than adding a fifth UV set.
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
// WHY THE FACES ARE DRAWN AND NOT DOWNLOADED. (The user asked for an asset
// search; it happened, and this is the argument that settled it.)
//
// The bundle forbids MonoBehaviours (Editor/BuildEnvironments.cs:35), which
// takes every rigged or animated FBX off the table — an apparition can then
// only be a STATIC mesh translated rigidly, which is precisely the "sliding
// sprite" read the rat lane spent a whole round removing. A photo texture has
// the same problem one level down: it is a fixed image, so a grin cannot widen,
// an eye cannot close, and a hand cannot bloom; it also costs a texture fetch,
// a mip chain and an atlas slot, and it goes soft the moment the player is
// closer than the resolution it was authored at.
//
// Signed distance shapes have none of that. They are crisp at any distance
// because they are evaluated per pixel; they MORPH, which is where the dread
// actually lives (the mouth below widens while it watches you); they cost no
// memory at all; and they are trivially compensated against the element mood,
// because "make the silhouette sharper" is one number in a smoothstep rather
// than a second texture. The whole catalogue is ~120 ALU in one fragment
// program with no texture units bound.
// ============================================================================
//
// ============================================================================
// THE CATALOGUE (the kinds; WHERE each one is placed is in
// BuildEnvironmentRooms.HauntCards, next to the coordinates).
//   0 FACE   — a pale face easing out from behind an edge, holding, withdrawing.
//              The mouth WIDENS into a grin over the hold. The user's own
//              example, so it exists in both rooms.
//   1 EYES   — two eyes open in the undergrowth, hold, blink once, gone.
//   2 FIGURE — a motionless silhouette. Variant 0: a tall watcher standing on
//              the ground. Variant 1: head and shoulders leaning in at a window.
//              Variant 2: a crouched shape whose head LIFTS once.
//   3 CROSS  — something crossing the card. Variant 0: a fast dark smear
//              between distant trunks. Variant 1: a figure walking through a
//              doorway.
//   4 SWARM  — drifting points that order themselves into a face and scatter.
//   5 HANG   — a long-limbed thing hanging head-down from a branch, one sway.
//   6 HANDS  — handprints blooming on wet stone in sequence, and fading.
//   7 NONE   — draws nothing. The card exists only to OCCUPY a schedule slot
//              for an event that happens in another shader: the cobwebs
//              shivering as if something large had just passed behind them.
//              Being a real card is what puts it under the same "never twice
//              running" and "never two at once" rules as the visible events.
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
//   LIGHT  the room is bright: a dark apparition would wash out. So it gains
//          contrast instead — the silhouette gets SHARPER (edge x0.45) and
//          BLACKER (x0.40), and the pale kinds gain a dark CONTOUR they do not
//          have otherwise. It also becomes rarer (x0.65 at full Light).
//   DARK   the room went black: a black apparition would be invisible. So it
//          gains a faint self-lit RIM (0.30 -> 0.75) and becomes more frequent
//          (x1.60 at full Dark).
//   FIRE   the rim becomes unsteady and warm — lit by something that is not
//          there.
//   ICE    it HOLDS LONGER (duration x1.35) and its motion freezes (x0.35).
//   AIR    it DRIFTS sideways over the hold, as if something moved it.
//   EARTH  it sits LOWER and its bottom edge is eaten by the ground it is
//          coming out of.
// Frequency changes are deterministic and stay client-identical: they are a
// hash of the slot compared against an element-derived threshold, and the
// element board is scenario-wide state the game itself desync-checks every
// round (Core/ElementMood.cs, "MULTIPLAYER: ZERO NEW WIRE BYTES").
// ============================================================================
Shader "GloomhavenVR/EnvHaunt"
{
    Properties
    {
        _Period ("Slot beat (s) — one event at most per slot", Float) = 83
        _Cards ("Event count in this room (a multiple of 3)", Float) = 6
        // The soft edge, in card-half-height units. Small: an apparition with a
        // fuzzy outline is a smudge, and a smudge is not frightening.
        _Edge ("Silhouette softness", Range(0.002,0.08)) = 0.016
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
        // So the bodies went to ~0.002 — genuinely darker than the wood, a HOLE in
        // the scene rather than an object in it — and the thing that makes a hole
        // findable is its edge. Every dark card now carries its own rim multiplier
        // (par.w), because how much edge a shape needs depends entirely on what is
        // behind it: the thing at the window is backlit by the moon and needs
        // almost none, while the figure crossing the stair doorway is a black shape
        // on a black recess and is nothing at all without one.
        _Rim ("Self-lit rim, base amount", Range(0,1)) = 0.28
        // How far INTO the silhouette the rim reaches, in card half-heights. The
        // first attempt tied this to _Edge (a 1.6 % soft edge) and the rim was
        // therefore about 5 cm on a two-metre figure — three or four pixels at
        // 16 m, i.e. nothing: the full-Dark preview was indistinguishable from the
        // neutral one, which is the compensation failing silently. 0.09 is ~18 cm
        // on that figure and ~2 cm on the grin card, which is a soft inner glow
        // along the outline rather than a hairline.
        _RimWidth ("Rim reach into the silhouette (card units)", Range(0.01,0.4)) = 0.09
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
        // about and its SDF is evaluated in its own uv, so drawing both faces
        // removes that entire class of bug at a cost of exactly zero: five of
        // the six cards in a room are collapsed to a point at any instant, and
        // the sixth is a few hundred pixels. The bake still ASSERTS that every
        // card's normal points at the room centre (AssertHauntCards) — not
        // because the shader needs it, but because a card facing away is a card
        // whose apparition is mirrored.
        Cull Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 3.0, not the 2.5 default: the swarm alone is a 14-iteration loop.
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvHaunt.cginc"

            float _Period, _Cards, _Edge, _Rim, _RimWidth;
            fixed4 _RimCold, _RimWarm;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            #define GHVR_PI 3.14159265

            struct appdata
            {
                float4 vertex  : POSITION;   // the card CENTRE, room space (all 4 verts)
                float3 normal  : NORMAL;     // card UP    * halfHeight (NOT a shading normal)
                float4 tangent : TANGENT;    // card RIGHT * halfWidth
                float4 uv      : TEXCOORD0;  // xy = corner (+-1, +-1); zw unused
                float4 id      : TEXCOORD1;  // x card index, y kind, z aspect (hw/hh), w variant
                float4 env     : TEXCOORD2;  // reveal, hold, fade, shape param
                float4 par     : TEXCOORD3;  // four more per-event numbers
                fixed4 color   : COLOR;      // rgb apparition colour, a base opacity
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 uv    : TEXCOORD0;   // xy card uv, z presence, w phase
                float4 id    : TEXCOORD1;
                float4 par   : TEXCOORD2;
                // Only .w (the shape parameter) is read in the fragment — the
                // three durations are consumed in the vertex shader. Carried whole
                // anyway: splitting one float out of an authored vector so that a
                // reader has to look in two places to find "the event's shape" is
                // a false economy on a mesh with twenty-four vertices.
                float4 env   : TEXCOORD3;
                fixed4 color : COLOR;
            };

            // ---------------------------------------------------------- SDF kit
            // Approximate signed distances, in card-half-height units. Approximate
            // is correct here: the only consumer is a smoothstep across a ~1.6 %
            // edge, so exactness beyond that band buys nothing and costs ALU.
            float SdEll (float2 p, float2 r)
            {
                return (length(p / r) - 1.0) * min(r.x, r.y);
            }
            // A tapered, rounded segment: the one primitive every limb, torso and
            // robe below is made of.
            float SdSeg (float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h) - lerp(ra, rb, h);
            }

            // ------------------------------------------------------------- FACE
            // `grin` 0..1 widens and curves the mouth; `open` 0..1 is the lid.
            // skin < 0 inside the head, feat < 0 inside an eye or the mouth.
            void FaceSdf (float2 p, float grin, float open, out float skin, out float feat)
            {
                // an egg, narrower toward the chin — an ellipse alone reads as a
                // balloon, and a balloon is not a face
                float2 q = float2(p.x * (1.0 + 0.34 * saturate(-p.y - 0.05)), p.y);
                skin = SdEll(q, float2(0.60, 0.86));

                float2 e = float2(abs(p.x) - 0.255, p.y - 0.20);
                float eye = SdEll(e, float2(0.135, 0.078 * open + 0.008));
                float pup = SdEll(float2(abs(p.x) - 0.255, p.y - 0.19),
                                  float2(0.050, 0.050 * open + 0.005));

                // THE GRIN. A parabola through the mouth line whose width and
                // curvature both grow with `grin`, so the smile does not merely
                // brighten — it spreads. This is the one thing a downloaded face
                // texture could not do, and it is the whole reason this is drawn.
                float mw = lerp(0.17, 0.42, grin);
                float mc = lerp(0.03, 0.30, grin);
                float my = -0.34 + mc * (p.x * p.x) / max(mw * mw, 1e-4);
                float mouth = max(abs(p.y - my) - lerp(0.018, 0.052, grin), abs(p.x) - mw);

                feat = min(min(eye, pup), mouth);
            }

            // ----------------------------------------------------------- FIGURE
            // variant 0 = standing watcher, 1 = head and shoulders at a window,
            // 2 = a crouched shape (`lift` raises its head once).
            float FigureSdf (float2 p, float variant, float lift)
            {
                float d;
                if (variant < 0.5)
                {
                    d = SdEll(float2(p.x, p.y - 0.80), float2(0.105, 0.125));      // head
                    d = min(d, SdSeg(p, float2(0, 0.66), float2(0, -0.28), 0.085, 0.21)); // torso
                    d = min(d, SdSeg(p, float2(0, -0.28), float2(0, -1.02), 0.21, 0.30)); // robe
                    d = min(d, SdSeg(p, float2(-0.11, 0.56), float2(-0.17, 0.02), 0.038, 0.028));
                    d = min(d, SdSeg(p, float2( 0.11, 0.56), float2( 0.17, 0.02), 0.038, 0.028));
                }
                else if (variant < 1.5)
                {
                    // a BUST: head, neck and the tops of two shoulders, leaning
                    // slightly in. Nothing below the shoulders — it is looking
                    // through a hole in a wall, and the hole is the frame.
                    d = SdEll(float2(p.x - 0.06, p.y - 0.26), float2(0.30, 0.36));  // head
                    d = min(d, SdSeg(p, float2(0.02, -0.10), float2(0.0, -0.42), 0.14, 0.18));
                    d = min(d, SdSeg(p, float2(-0.72, -0.95), float2(0.72, -0.95), 0.34, 0.34));
                }
                else
                {
                    // CROUCHED, and the HEAD IS THE TOP OF THE SHAPE. The first
                    // version tucked the head low beside a hunched back, on the
                    // reasoning that a crouching thing folds its head down — which
                    // is true and was useless, because this card lives behind the
                    // cellar's barrels and everything below about 0.9 m is
                    // occluded by them. All that showed was the crown of the back:
                    // a featureless pale dome that reads as a lamp
                    // (env_cellar_HauntCrouch, two bakes running). Head at the top
                    // means the part that clears the barrel is the part that
                    // identifies it, and the one motion this event has — the lift —
                    // becomes a HEAD RISING OVER A BARREL rather than a bump
                    // getting slightly bumpier.
                    d = SdEll(float2(p.x + 0.12, p.y - 0.42 - lift), float2(0.160, 0.185)); // head
                    d = min(d, SdEll(float2(p.x - 0.06, p.y - 0.06), float2(0.42, 0.34)));  // shoulders
                    d = min(d, SdSeg(p, float2(0.30, -0.10), float2(0.16, -0.94), 0.105, 0.070));
                }
                return d;
            }

            // -------------------------------------------------------- THE SWARM
            // Fourteen points that ease from a scattered drift onto the outline of
            // a face and scatter again. sin/cos ARE allowed here — they are in the
            // DRAWING, where a last-bit difference between two GPUs is a last-bit
            // difference. They are banned only inside the schedule hash.
            float2 SwarmTarget (float k)
            {
                float ring = step(k, 7.5);                      // 0..7  the head oval
                float eyes = step(7.5, k) * step(k, 9.5);       // 8..9  the eyes
                float mou  = step(9.5, k);                      // 10..13 the mouth arc
                float a = k * (GHVR_PI * 2.0 / 8.0);
                float2 pr = float2(0.50 * sin(a), 0.70 * cos(a));
                float2 pe = float2((k - 8.5) * 0.46, 0.18);
                float u = (k - 11.5) / 2.0;                     // -1 .. +1
                float2 pm = float2(u * 0.32, -0.34 + 0.20 * u * u);
                return pr * ring + pe * eyes + pm * mou;
            }

            // --------------------------------------------------------- THE HANDS
            // A palm that is TALLER than it is wide, and fingers that start inside
            // it. The first version was a circle with four sticks on it, which
            // reads as a cartoon glove; a real print is a long pad with the digits
            // growing out of its top third.
            float HandSdf (float2 p, float spread)
            {
                float d = SdEll(float2(p.x, p.y - 0.02), float2(0.145, 0.215));   // palm
                for (int f = 0; f < 4; f++)
                {
                    float a = (f - 1.5) * 0.30 * (0.6 + spread);
                    float2 tip = float2(sin(a), cos(a)) * (0.36 + 0.055 * cos(f * 2.0));
                    d = min(d, SdSeg(p, float2(sin(a) * 0.085, 0.10), tip + float2(0, 0.055),
                                     0.038, 0.026));
                }
                d = min(d, SdSeg(p, float2(-0.10, -0.06), float2(-0.30, 0.09), 0.044, 0.030)); // thumb
                return d;
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
                o.env = v.env;
                o.color = v.color;
                return o;
            }

            // ---------------------------------------------------------- fragment
            fixed4 frag (v2f i) : SV_Target
            {
                // vis == 0 => the card collapsed to a point; nothing may flash.
                clip(i.uv.z - 1e-4);

                float t = _Time.y + _GhvrTimeOfs;
                GhvrElems e = GhvrHauntElems();

                float kind    = i.id.y;
                float aspect  = max(i.id.z, 1e-3);
                float variant = i.id.w;
                float pres    = i.uv.z;      // 0..1 presence (the envelope)
                float phase   = i.uv.w;      // 0..1 through the whole event

                // isotropic card space: y in [-1,1], x in [-aspect,+aspect], so a
                // circle drawn below is a circle in metres whatever the card's
                // proportions are.
                float2 p = float2(i.uv.x * aspect, i.uv.y);

                // ---- ELEMENT DISPLACEMENT (see the header) ----
                // AIR: it drifts over the hold. ICE freezes the drift with
                // everything else that moves.
                float still = 1.0 - 0.65 * e.ice;
                p.x -= 0.22 * e.air * still * sin(phase * GHVR_PI);
                // EARTH: it sits lower, and the ground eats its bottom edge.
                p.y += 0.15 * e.earth;

                float edge = _Edge * (1.0 - 0.55 * e.light);   // LIGHT sharpens
                float d = 1e6;             // silhouette distance
                float3 col = i.color.rgb;
                float alpha = i.color.a;
                float pale = 0.0;          // 1 = this kind is PALE, not a shadow

                if (kind < 0.5)
                {
                    // ---------------------------------------------------- FACE
                    // It eases OUT from behind the edge it is hiding behind, and
                    // withdraws the same way. `pres` is already the smoothstep
                    // in/out, so the motion and the envelope cannot disagree.
                    float outAmt = pres * still + (1.0 - still) * 0.75;
                    p.x -= i.par.x * outAmt;
                    // ...and the grin WIDENS while it watches. Monotone in phase,
                    // never reset: it is still spreading as it withdraws.
                    float grin = smoothstep(0.10, 0.92, phase) * i.env.w;
                    float skin, feat;
                    FaceSdf(p, grin, 1.0, skin, feat);
                    d = skin;
                    pale = 1.0;
                    // The features are darker flesh, not holes: a hole would let
                    // the tree behind it through and the face would read as a mask.
                    col = lerp(i.color.rgb, i.color.rgb * 0.16, smoothstep(edge, -edge, feat));
                    // the alpha ramps FASTER than the slide, so it is mostly there
                    // by the time it clears the trunk rather than fading in in the
                    // open where the eye can catch it arriving
                    alpha *= smoothstep(0.10, 0.52, pres);
                }
                else if (kind < 1.5)
                {
                    // ---------------------------------------------------- EYES
                    // Open, hold, ONE blink, gone. The blink is the event: a pair
                    // of steady points is a lamp, a pair that blinks is an animal.
                    float open = smoothstep(0.0, 0.20, phase)
                               * (1.0 - 0.97 * exp(-pow((phase - 0.62) / 0.040, 2.0)));
                    float2 q = float2(abs(p.x) - 0.34, p.y);
                    d = SdEll(q, float2(0.155, 0.070 * open + 0.006));
                    pale = 1.0;
                    alpha *= smoothstep(0.02, 0.20, pres);
                }
                else if (kind < 2.5)
                {
                    // -------------------------------------------------- FIGURE
                    // The crouched variant's one motion: the head comes up, once,
                    // in the middle of the hold. Nothing else about it ever moves,
                    // which is what makes the lift land.
                    float lift = 0.17 * smoothstep(0.34, 0.56, phase) * still;
                    d = FigureSdf(p, variant, lift);
                    alpha *= smoothstep(0.0, 0.30, pres);
                }
                else if (kind < 3.5)
                {
                    // --------------------------------------------------- CROSS
                    // Something goes past. Variant 0 is a smear (a few tenths of
                    // a second — you get the motion and not the shape); variant 1
                    // is a figure walking through a doorway.
                    float x0 = lerp(-1.25, 1.25, phase) * aspect * i.par.y;
                    float2 q = float2(p.x - x0, p.y);
                    if (variant < 0.5)
                    {
                        // smeared along its own travel: this is what a thing seen
                        // for 0.3 s looks like, and it is cheaper than any blur
                        q.x *= 1.0 / (1.0 + i.par.z);
                        d = SdSeg(q, float2(0.0, -0.72), float2(0.06, 0.70), 0.16, 0.11);
                    }
                    else
                    {
                        d = FigureSdf(q, 0.0, 0.0);
                    }
                    alpha *= smoothstep(0.0, 0.22, pres);
                }
                else if (kind < 4.5)
                {
                    // --------------------------------------------------- SWARM
                    // The one kind that is not a silhouette: fourteen drifting
                    // points that briefly agree on a face. The scatter is analytic
                    // (not hashed) so the loop stays cheap, and the two per-slot
                    // hashes in par.xy make the scattered state different every
                    // time it happens.
                    float morph = smoothstep(0.18, 0.46, phase) * smoothstep(0.86, 0.60, phase);
                    float acc = 0.0;
                    for (int k = 0; k < 14; k++)
                    {
                        float fk = (float)k;
                        float2 scat = float2(0.98 * sin(fk * 2.399 + i.par.x * 6.283 + t * 0.11),
                                             0.86 * cos(fk * 1.771 + i.par.y * 6.283 + t * 0.09));
                        float2 dp = lerp(scat, SwarmTarget(fk), morph) - p;
                        acc += exp(-dot(dp, dp) * 320.0);
                    }
                    // no silhouette to rim or contour: d stays far outside, so the
                    // element blocks below leave this kind alone by construction
                    col = i.color.rgb;
                    alpha = i.color.a * saturate(acc) * pres;
                    return fixed4(col, saturate(alpha));
                }
                else if (kind < 5.5)
                {
                    // ---------------------------------------------------- HANG
                    // Head DOWN. Hung by the feet from the branch above, arms
                    // dangling past the head, and ONE slow sway that damps out —
                    // the sway is what says it was put there, and recently.
                    // The order below is the body from the HEAD UP, because that is
                    // how it hangs. The first version put a 0.125-radius head just
                    // under a 0.165-radius torso end and the head simply vanished
                    // into it — the preview showed a tapering wedge with two prongs
                    // and no person in it. A hanging figure has to read as
                    // head / neck / shoulders / waist, and the NECK is what makes
                    // that work: it is the narrowest part of the whole shape and it
                    // is what separates the head from the body.
                    float sway = 0.13 * sin(phase * 6.0) * exp(-phase * 2.2) * still;
                    float2 q = float2(p.x - sway * saturate(0.85 - p.y * 0.85), p.y);
                    d = SdEll(float2(q.x, q.y + 0.86), float2(0.115, 0.140));            // head, lowest
                    d = min(d, SdSeg(q, float2(0, -0.74), float2(0, -0.58), 0.072, 0.080)); // neck
                    d = min(d, SdSeg(q, float2(0, -0.58), float2(0, 0.06), 0.200, 0.130)); // shoulders->waist
                    d = min(d, SdSeg(q, float2(0, 0.06), float2(0, 0.56), 0.145, 0.090));  // hips
                    d = min(d, SdSeg(q, float2(-0.055, 0.52), float2(-0.065, 1.02), 0.052, 0.038));
                    d = min(d, SdSeg(q, float2( 0.055, 0.52), float2( 0.065, 1.02), 0.052, 0.038));
                    // arms dangling PAST the head — the detail that says head-down
                    d = min(d, SdSeg(q, float2(-0.17, -0.50), float2(-0.23, -1.00), 0.048, 0.032));
                    d = min(d, SdSeg(q, float2( 0.17, -0.50), float2( 0.23, -1.00), 0.048, 0.032));
                    alpha *= smoothstep(0.0, 0.30, pres);
                }
                else if (kind < 6.5)
                {
                    // --------------------------------------------------- HANDS
                    // Three prints bloom IN SEQUENCE, not together: a set that
                    // appeared at once is a stencil, a set that arrives one after
                    // another is somebody pushing.
                    d = 1e6;
                    float best = 0.0;
                    for (int n = 0; n < 3; n++)
                    {
                        float fn = (float)n;
                        float b = saturate((phase - fn * 0.14) / 0.26);
                        float2 c = float2((fn - 1.0) * 0.62 + 0.10 * sin(fn * 3.1),
                                          -0.10 + 0.34 * sin(fn * 2.3 + 1.0));
                        float ang = (fn - 1.0) * 0.42 + 0.18;
                        float2 q = p - c;
                        q = float2(q.x * cos(ang) - q.y * sin(ang),
                                   q.x * sin(ang) + q.y * cos(ang)) / 0.60;
                        float hd = HandSdf(q, i.par.x) * 0.60;
                        // a print that has not bloomed yet is pushed out of
                        // existence rather than made transparent, so the ones
                        // still to come never ghost
                        hd += (1.0 - b) * 0.60;
                        if (hd < d) { d = hd; best = b; }
                    }
                    pale = 1.0;
                    alpha *= smoothstep(0.0, 0.22, pres) * best;
                }
                else
                {
                    // ---------------------------------------------------- NONE
                    // An event that happens somewhere else (the cobwebs shiver;
                    // see EnvRoomCutout). Occupying a card is what keeps it inside
                    // the schedule's no-repeat and no-collision rules.
                    discard;
                }

                // ---- SILHOUETTE ----
                float body = smoothstep(edge, -edge, d);

                // ---- ELEMENT COMPENSATION (the half that keeps it frightening) ----
                // LIGHT: the dark kinds go BLACKER rather than washing out, and
                // the pale kinds gain a dark contour they do not otherwise have.
                col *= lerp(1.0, 1.0 - 0.60 * e.light, 1.0 - pale);
                float contour = e.light * pale * smoothstep(edge * 3.2, 0.0, abs(d));
                col = lerp(col, col * 0.10, contour * 0.85);

                // DARK: a faint self-lit rim, or the shape is a black square hole
                // in a black room. FIRE makes that rim unsteady and warm.
                // par.w is the per-card rim multiplier (see the _Rim block). It is
                // clamped at 0 rather than defaulted, so a card that leaves it
                // unset simply has no rim — which is right for the pale kinds,
                // whose read is their own brightness.
                // Linear from the outline inward, squared for a tighter core —
                // and INSIDE only (step(d,0)), so nothing glows in the empty half
                // of the card where alpha is already 0.
                float rimBand = step(d, 0.0) * saturate(1.0 + d / max(_RimWidth, 1e-4));
                rimBand *= rimBand;
                float rimAmt = (_Rim + 0.55 * e.dark) * max(i.par.w, 0.0)
                             * (1.0 + 0.32 * e.fire * sin(t * 9.3 + i.id.x * 2.1));
                float3 rimCol = lerp(_RimCold.rgb, _RimWarm.rgb, saturate(e.fire));
                col += rimCol * (rimBand * rimAmt * (1.0 - pale));

                // EARTH: the ground eats the lower edge.
                alpha *= 1.0 - 0.85 * e.earth * smoothstep(-0.55, -0.98, p.y);

                alpha *= body;
                clip(alpha - 0.004);
                return fixed4(col, saturate(alpha));
            }
            ENDCG
        }
    }
    Fallback Off
}
