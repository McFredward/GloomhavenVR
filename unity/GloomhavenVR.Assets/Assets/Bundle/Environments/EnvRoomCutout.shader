// GloomhavenVR — two-sided alpha-cutout variant of EnvRoom (grass, ferns,
// hanging moss). Same baked-light model as EnvRoom (see there); Cull Off with
// VFACE so both sides of a foliage card are lit as seen. Kept as its own
// shader (not a multi_compile) so materials stay dead simple and deterministic.
Shader "GloomhavenVR/EnvRoomCutout"
{
    Properties
    {
        _MainTex ("Albedo (A=opacity)", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
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
        _VCol ("Vertex color amount", Range(0,1)) = 0
        _Cutoff ("Cutout threshold", Range(0,1)) = 0.35
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        // ---- SHELF RIDERS (EnvShelfTip.cginc). Nothing drawn by this shader
        // stands on the bookshelf — a cobweb is anchored to masonry and a fern
        // grows in a wood — but the cellar's webs are LIT by the candle that
        // does, so _TipUse.y names the slot that travels. All zero elsewhere.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, flame stiffness", Vector) = (0,-1,0,0)
        // Cobweb billow. 0 (default) = no vertex motion at all, so the forest's
        // ferns/grass/canopy are bit-identical. The per-vertex WEIGHT is
        // vertex-colour RED, authored by the web builder (1 = free centre of the
        // web, 0 = where it is anchored to the stone).
        _Sway ("Sway amplitude (m)", Range(0,0.3)) = 0
        _SwayRate ("Sway rate", Float) = 0.55
        _SwayPhase ("Sway phase", Float) = 0
        // xyz = the sway direction in OBJECT space. w = THE WHIRL: how freely
        // this material's silk may orbit about that direction (0 = a membrane,
        // it may only go perpendicular to itself; 1 = a thread held at one end).
        // The default is 0, so every material that has never heard of this — the
        // forest's ferns, grass, moss and canopy, and every cobweb SHEET — is
        // bit-identical. See THE WHIRL in the vertex shader.
        _SwayDir ("Sway direction (OBJECT space), w = whirl", Vector) = (0,0,1,0)

        // HAUNT — the cobweb tremble. 0 (the default) is a hard off: every
        // material that does not set _HauntTremble skips the whole block below,
        // so the forest's ferns, grass, moss and canopy are bit-identical.
        //
        // One of the cellar's six easter eggs draws NOTHING (EnvHaunt kind 7):
        // its entire content is that every web in the room shivers for about two
        // seconds, as if something large had just gone past behind them. Doing it
        // HERE, on the real webs, rather than drawing a shivering web somewhere,
        // is what makes it believable — it is the room's own silk, in the room's
        // own draught, briefly disturbed by nothing you can see.
        _HauntTremble ("Haunt tremble amplitude (m)", Range(0,0.2)) = 0
        _HauntPeriod ("Haunt slot beat (s)", Float) = 83
        _HauntCards ("Haunt event count in this room", Float) = 6
        _HauntWatch ("Which haunt event this reacts to (-1 = none)", Float) = -1
        _HauntEnv ("Watched event envelope: reveal, hold, fade, (unused)", Vector) = (0,1,1,0)

        // ---- ELEMENT ART / SURFACE GROWTH (EnvElement.cginc, EnvGrowth.cginc) ----
        // The other half of ModBuild 142's KNOWN GAP: every alpha-cut card in
        // both rooms — the canopy, the ferns, the grass, the hanging moss, the
        // cobwebs — was outside the element channel entirely, so the wood's
        // FOLIAGE could not answer Ice, Earth or Air. All four blocks below are
        // opt-in and default to a hard off, so the cobwebs are untouched.
        _ElemCentre ("Element: room centre (OBJECT space)", Vector) = (0,0,0,0)
        _ElemRad ("Element: room outer radius (object units)", Float) = 6
        _ElemScl ("Element: object units in metres", Float) = 1
        _ElemFrost ("Element: ice frost susceptibility", Range(0,2)) = 0
        // _ElemMoss IS GONE. The painted green a card used to take under Earth
        // was deleted on the user's fourth rejection of that surface (see THE
        // MOSS IS GONE in EnvGrowth.cginc). It is worth being exact about what
        // this shader loses, because it is the shader the CARDS run through:
        // nothing about the cards themselves changes. They keep their own
        // textures, their own _Tint, their frost, their wind and — above all —
        // _ElemGrow below, the vertex fold that stands them up out of the
        // ground. That fold IS Earth's statement now. What is gone is only the
        // green that was being painted over a fern that was already green.
        _ElemGrowFreq ("Element: growth cells per metre", Float) = 3.0

        // ---- THE WINDOW'S THROW (the indoor Light lift's mask) --------------
        // ModBuild 151, user: "Bei 'Licht' im Keller statt den ganzen Keller
        // mehr zu beleuchten mach ausschließlich das Licht aus dem Kellerfenster
        // vom Mond heller." The mechanism, the rule and every composite gain are
        // in EnvElement.cginc (...AND INDOORS IT MAY ONLY BRIGHTEN WHAT THE
        // WINDOW SEES); these two vectors are only the opening's geometry.
        //
        // THEY ARE DEFAULTS AND NOTHING WRITES THEM, and that is a deliberate
        // choice with a gate behind it rather than an oversight. Every EnvRoom
        // material in the cellar would otherwise need the values pushed onto it
        // by the light rig, which is a pass this lane does not own; and the
        // opening is a build-time CONSTANT of a room that exists once, so a
        // default is the honest shape for it. What makes that safe is
        // AssertMoonWindowMirror in BuildEnvironmentRooms: every bake
        // instantiates this shader, reads these two defaults back and fails the
        // build if they disagree with the opening the wall mesh was actually cut
        // to. The numbers cannot drift, because a drift is a build error.
        // OUTDOORS THEY ARE UNUSED — GhvrIndoor() lerps the whole mask out.
        _MoonWin ("Window opening in ROOM metres (x0,y0,x1,y1)", Vector) = (-2.068, 2.2, -0.636, 2.986)
        _MoonWinZ ("Window plane z, feather (ROOM metres)", Vector) = (4.5, 0.22, 0, 0)
        // AIR. x is the tip amplitude in OBJECT units and 0 is off; y and z are
        // the object-space base and 1/height of this mesh, which is how a
        // photoscanned fern says "my roots are at the bottom"; w picks vertex
        // ALPHA as the freedom weight instead, which is what the built cards
        // (AddCard) carry — 0 at the stem corner, 1 at the tip corner.
        _ElemWind ("Wind: amplitude, base y, 1/span, use vertex alpha", Vector) = (0,0,0,0)
        _ElemWindDir ("Wind: bearing (OBJECT space), w = card height", Vector) = (0,0,1,0)
        // EARTH, as GEOMETRY rather than as pigment: the grass and moss that
        // come up when Earth rises. 0 (the default) means this mesh is not a
        // growth mesh and the whole block is skipped; above 0, every card is
        // folded flat onto its own base edge until the frontier reaches it.
        _ElemGrow ("Earth: grow-in susceptibility", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // EnvHaunt.cginc INCLUDES EnvElement.cginc, so this one line brings
            // both the schedule and the element channel, and GhvrHauntElems() is
            // now a one-line alias for the canonical GhvrElems(). It was not
            // always so: for one round each header declared the element globals
            // and its own `GhvrElems`, which compiled everywhere except in a
            // shader that wanted both — the follow-up EnvGrowth.cginc's THE
            // CHANNEL block asks for, now paid.
            #include "EnvHaunt.cginc"
            #include "EnvGrowth.cginc"
            // ...and the tipping shelf's pose, for the ONE thing this shader has
            // to take from it: the candle standing on that shelf lights these
            // webs. See the vertex shader.
            #include "EnvShelfTip.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            float _BumpScale, _VCol, _Cutoff, _PtHard, _Sway, _SwayRate, _SwayPhase;
            float _HauntTremble, _HauntPeriod, _HauntCards, _HauntWatch;
            float _ElemRad, _ElemScl, _ElemFrost, _ElemGrowFreq, _ElemGrow;
            float4 _MoonWin, _MoonWinZ;
            fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos, _SwayDir, _HauntEnv;
            float4 _ElemCentre, _ElemWind, _ElemWindDir;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
                fixed4 color   : COLOR;
            };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                float3 opos : TEXCOORD1;
                float3 n    : TEXCOORD2;
                float3 t    : TEXCOORD3;
                float3 b    : TEXCOORD4;
                // SHELF RIDERS: the baked light slot that stands on the cellar's
                // tipping bookshelf, moved with it, w = that flame's life (w < 0
                // means nothing rides and the fragment must not touch the light).
                // See EnvShelfTip.cginc.
                float4 tipL : TEXCOORD5;
                fixed4 vcol : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 p = v.vertex;
                // cobweb billow: two slow incommensurate sines, weighted by the
                // authored freedom (vertex red) and de-phased along the web so
                // it ripples rather than translating as a slab
                float t = _Time.y + _GhvrTimeOfs;
                float st = t * _SwayRate + _SwayPhase;
                // sincos rather than two sins: the COSINES are the quadrature
                // partners THE WHIRL needs below, and on every target here they
                // come out of the same special-function pair as the sines.
                float2 sw, cw;
                sincos(float2(st, st * 1.73 + 2.1), sw, cw);
                float s = (sw.x * 0.62 + sw.y * 0.38) * _Sway * v.color.r;

                // HAUNT — the tremble. A uniform branch, so it is coherent across
                // every invocation and the materials that leave _HauntTremble at 0
                // (all of the forest's foliage) never evaluate the schedule at all.
                //
                // It is a SHIVER, not a bigger sway: 14 Hz against the draught's
                // 0.42, decaying over the event, and along the web's own normal
                // rather than along the draught — silk that something brushed
                // moves perpendicular to itself, and moving it along _SwayDir
                // would just look like a gust, which the room already has.
                if (_HauntTremble > 1e-4)
                {
                    float trem = GhvrHauntPresence(t, _HauntPeriod, _HauntCards, _HauntWatch,
                                                   _HauntEnv.x, _HauntEnv.y, _HauntEnv.z);
                    s += sin(t * 88.0 + _SwayPhase * 3.7) * exp(-(1.0 - trem) * 2.0)
                         * _HauntTremble * trem * v.color.r * 0.35;
                    p.xyz += v.normal * (sin(t * 71.0 + _SwayPhase * 2.3)
                                         * _HauntTremble * trem * v.color.r);
                }

                // ==================================== ELEMENT ART: THE DRAUGHT
                // AIR strengthens the authored draught, which in the cellar is
                // the cobwebs (the candles get the same treatment in EnvRoom's
                // PointLight). Exactly x1.0 with Air down, so a room with no
                // element up billows precisely as it did before.
                // GhvrHauntElems(), not EnvElement's GhvrElems(): this shader
                // reads the mood through GhvrHauntElems(). That used to be a
                // SEPARATE struct declared in EnvHaunt.cginc, which is why this
                // block once had to spell `live` by hand — and why a shader that
                // wanted both headers would not compile at all. EnvHaunt now
                // includes EnvElement and GhvrHauntElems() is a one-line alias
                // for GhvrElems(), so this is the canonical GhvrElem, `live`
                // included, and there is no third quotation of the channel left
                // in the bundle.
                GhvrElem e = GhvrHauntElems();
                // AND IT LEANS. ModBuild 151, user: "Häng irgendwas an die
                // Gitterstäbe beim Keller zB Spinnweben das dann durch den Wind
                // in eine Richtung weht um visuell noch besser visible zu
                // machen." The x1.20 above is zero-mean — it makes the silk
                // billow HARDER but leaves it hanging about the same place, and
                // a bigger symmetric wobble reads as a bigger fan, not as a
                // draught. What says "the air is moving THAT WAY" is a steady
                // displacement DOWN-WIND with the flutter on top of it, which is
                // what a rag in a doorway actually does.
                //
                // It is along _SwayDir, and what that vector IS differs between
                // the two kinds of silk in the room, correctly: for a SHEET it is
                // the sheet's own normal, so a web stretched between two anchors
                // is pushed perpendicular to itself — which is what a draught
                // does to anything spanning an aperture, and the only direction a
                // taut membrane can actually go; for a LOOSE STRAND, held at one
                // end, it is the room's own DraftDir, the same vector the flames
                // lean along and the beam's mottling travels along, so the free
                // ends trail down-wind and the whole room agrees which way the
                // air goes.
                //
                // 0.90, i.e. at full Air the lean is nine tenths of the authored
                // billow amplitude: enough that a web is visibly HELD to one
                // side rather than swinging through the middle, not so much that
                // the silk leaves the bars it is strung between. AMPLITUDE, not
                // frequency, and exactly 0 with Air down — see AN ELEMENT MAY NOT
                // MOVE A FREQUENCY in EnvGrowth.cginc for why that distinction is
                // load-bearing and what it cost to learn.
                //
                // REJECTED: leaning along the web's own normal. A web pushed
                // perpendicular to itself is a web being poked, which is what the
                // HAUNT tremble above already means; a draught pushes it along
                // the flow.
                if (e.live > 0.0) s = s * (1.0 + 1.20 * e.air) + _Sway * v.color.r * (0.90 * e.air);
                p.xyz += _SwayDir.xyz * s;

                // ============================================== THE WHIRL ====
                // USER, ModBuild 149: "Wind gefaellt mir sehr gut, nur eine
                // Kleinigkeit noch: An einer Stelle im Keller haengt so ein
                // Faden (Spinnweben) von einem Balken herab, auch der sollte bei
                // Wind etwas mehr herumwirbeln."
                //
                // EVERYTHING ABOVE MOVES ON ONE AXIS, and for a SHEET that is
                // right — a membrane with a pinned rim has exactly one degree of
                // freedom. A thread held at ONE end has three: it is torsionally
                // free at the bottom, so a draught does not swing it in a plane,
                // it makes the tip ORBIT. That is the whole of the complaint: the
                // strand was a pendulum in a room where everything else was
                // already turning.
                //
                // SO IT GETS A SECOND AXIS, and three things about it matter:
                //  * WHICH axis. cross(object up, _SwayDir) — the horizontal
                //    perpendicular to the draught. No new vector, no new
                //    property: for a strand _SwayDir is already DraftDir, so the
                //    orbit is automatically square to the lean the room agrees
                //    on, and if a material ever set a VERTICAL _SwayDir the cross
                //    degenerates and the term switches itself off rather than
                //    exploding.
                //  * IN QUADRATURE. The slow carrier is COS of exactly the two
                //    arguments `s` took the SIN of, so the two axes together are
                //    a circle of 0.62 with a circle of 0.38 rolling round it at
                //    1.73x — an epicycle, which never closes and never repeats.
                //    A second INDEPENDENT wobble would only have made the line
                //    the tip travels along a fatter line.
                //  * ONLY A STRAND DOES IT. _SwayDir.w is the per-material whirl
                //    and it is 0 for every sheet and for all of the forest, so
                //    this whole block is a uniform branch that those materials
                //    never enter. The strands hanging off the beams run at 1.0;
                //    the four streamers in the window at 0.30, because they hang
                //    between bars 23.9 cm apart and 1.0 would swing them through
                //    the ironwork (0.30 keeps the tip inside +-3.9 cm of x).
                //
                // AND IT OBEYS "AN ELEMENT MAY NOT MOVE A FREQUENCY"
                // (EnvGrowth.cginc). He asked for a FASTER whirl under wind and
                // that is exactly the shape of this project's most repeated bug:
                // Air on a rate, times an absolute clock in the thousands of
                // seconds, scrubs the phase by hundreds of cycles during the 1 s
                // ramp — and it looks CORRECT AT t = 0, which is why it keeps
                // shipping. So the speed-up is TWO CARRIERS AT FIXED RATES that
                // Air CROSSFADES, the same repair GhvrWind and EnvBeam took: the
                // slow one at _SwayRate and the fast one at 2.35x _SwayRate, both
                // running off the shared clock forever, with Air touching nothing
                // but the two weights.
                //
                // MEASURED, worst per-frame tip step at 90 Hz across the whole
                // 1 s ramp, with the ramp started at five points on the shared
                // clock (strand B, tip, both axes):
                //     t0 =      0 s   30 s   600 s   1800 s   3600 s
                //     this     2.59   1.45    1.31     2.78     1.83  mm
                //     if Air
                //     scaled
                //     the rate 2.62  34.07  263.88   186.15   168.95  mm
                // The right-hand row is the bug, and note that it is CORRECT AT
                // t = 0 — which is the whole reason it is hard to catch and the
                // reason this table exists. The shipped row does not contain t:
                // the offset is sum_i A_i(air) * C_i(f_i * t + phi_i) with every
                // f_i constant, so |d/dt| is bounded by the amplitudes and the
                // fixed rates alone.
                if (_SwayDir.w > 1e-4 && e.live > 0.0)
                {
                    float3 oc = cross(float3(0.0, 1.0, 0.0), _SwayDir.xyz);
                    float ol = length(oc);
                    float3 oax = oc * (ol > 1e-3 ? 1.0 / ol : 0.0);

                    float slow = cw.x * 0.62 + cw.y * 0.38;
                    float ft = t * (_SwayRate * 2.35) + _SwayPhase;
                    float2 fw = cos(float2(ft, ft * 1.73 + 2.1));
                    float fast = fw.x * 0.62 + fw.y * 0.38;
                    // AMPLITUDE ONLY: 0.35 of the in-plane billow at rest (a
                    // hanging thread always turns a little — the cellar's draught
                    // is permanent) growing to 2.20 at full Air, which is exactly
                    // what the in-plane axis grows to, so the tip's path opens
                    // from a flat ellipse into a full loop instead of just
                    // getting longer. e.live rather than a constant on the first
                    // term so the whole thing is continuous through the master's
                    // own ramp and EXACTLY zero with the channel down.
                    p.xyz += oax * (lerp(slow, fast, saturate(e.air))
                                    * _Sway * v.color.r * _SwayDir.w
                                    * (0.35 * e.live + 1.85 * e.air));
                }

                // ================================ SURFACE GROWTH + THE WIND ==
                // Two vertex effects on one weight, because they want the same
                // number: how FREE is this corner of this card. 0 at the point
                // where the card is attached to its branch or standing in the
                // ground, 1 at the tip. See EnvGrowth.cginc.
                //
                // The outer test is on the MATERIAL, not on the element, and
                // that is load-bearing: a growth mesh must collapse when Earth
                // is down even when the whole element channel is unset, or a
                // room with no elements would be standing in grass it should not
                // have. Every other material has _ElemGrow = 0 and _ElemWind.x =
                // 0 and skips the block entirely.
                if (_ElemGrow > 1e-5 || _ElemWind.x > 1e-5)
                {
                    // vertex ALPHA on the built cards (AddCard authors it),
                    // height-above-the-mesh's-own-base on the photoscans
                    float wgt = lerp(saturate((v.vertex.y - _ElemWind.y) * _ElemWind.z),
                                     v.color.a, _ElemWind.w);
                    // ...and the weight the WIND uses, which is the same number
                    // except on a growth mesh — see the fold below.
                    float wwind = wgt;
                    if (_ElemGrow > 1e-5)
                    {
                        // The frontier, evaluated on the CARD's own position: the
                        // grass comes up in spreading patches rather than the
                        // whole room rising like a lift. A coarser cell than the
                        // pixels use (0.55/m, ~1.8 m patches) — a clump is a
                        // clump, and a frontier finer than the clumps it moves
                        // would just look like flicker.
                        float g = 0.0;
                        if (e.earth > 0.0)
                            g = GhvrGrowCard(GhvrGrowQ(v.vertex.xyz, _ElemCentre.xyz,
                                                       _ElemScl, 0.55),
                                             e.earth * _ElemGrow, t);
                        // fold the card flat onto its base edge => zero area =>
                        // no fragments at all, which is what makes a mesh of
                        // grass that has not grown yet bit-identical to no mesh.
                        p.y -= _ElemWindDir.w * wgt * (1.0 - g);
                        // A BLADE THAT HAS NOT COME UP DOES NOT WAVE — and this
                        // line is load-bearing rather than decorative now that
                        // the wind is permanent. The fold lands a card's top
                        // edge EXACTLY on its bottom edge (see AddGrowthCard on
                        // why "exactly" is achievable at all); the wind's offset
                        // depends on the vertex WEIGHT, so a folded quad whose
                        // two edges got different offsets would have area again
                        // — the grass that is not there yet would come back as a
                        // shimmer of slivers, with Earth down, forever. g = 0
                        // makes the weight exactly 0 and GhvrWind's offset
                        // exactly (0,0,0). It is also simply true: grass grows
                        // into the wind, it does not wave its way out of the
                        // ground.
                        wwind = wgt * g;
                    }
                    // ...and a GROWTH mesh pays for the wind only once it has
                    // grown. Both halves of the test are uniform (a material
                    // constant and an element global), so the branch stays
                    // coherent across the whole draw — which is the only reason
                    // it is worth having: the forest's two growth meshes are
                    // 11.5k vertices that are folded to nothing in every
                    // scenario without Earth, and a permanent breeze must not
                    // charge them 35 ALU each for an offset of exactly zero.
                    if (_ElemWind.x > 1e-5 && (_ElemGrow < 1e-5 || e.earth > 0.0))
                    {
                        // NOT GATED ON AIR ANY MORE (user verdict, ModBuild 143:
                        // "so wie du es gemacht hast sollte der Normalzustand
                        // sein und immer sichtbar"). The amplitude is now the
                        // standing breeze and e.air is the STORM multiplier
                        // inside GhvrWind — read THE STORM in EnvGrowth.cginc
                        // for what the storm actually escalates and for the
                        // shadow-map budget that rations it. With the element
                        // channel unset e.air is 0 and this is exactly the
                        // ModBuild 143 breeze, which is what he approved.
                        //
                        // `side` from object UP: every material that carries a
                        // wind is placed by yaw alone, so object up is world up
                        // and the flutter really is across the wind.
                        float3 side = normalize(cross(_ElemWindDir.xyz, float3(0, 1, 0)));
                        p.xyz += GhvrWind(p.xyz, wwind, t, _ElemWindDir.xyz, side,
                                          _ElemWind.x, e.air);
                    }
                }
                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.opos = p.xyz;
                o.n = v.normal;
                o.t = v.tangent.xyz;
                o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
                // SHELF RIDERS. The cobwebs over the cellar's east wall are lit by
                // the candle standing on the bookshelf, and the first bake of the
                // rider fix showed exactly what happens when a shader is left out
                // of it: the shelf went over, every stone surface in the room went
                // dark as the candle travelled and died, and the ONE thing still
                // lit by a candle that was no longer there was the web in the
                // corner. Nothing here rides — a web is anchored to masonry — but
                // the light it is lit BY does.
                o.tipL = GhvrTipLight(GhvrTipNow(t), _L0Pos, _L1Pos, _L2Pos);
                o.vcol = v.color;
                return o;
            }

            // identical to EnvRoom.shader's — kept copied, not #included, so the
            // bundle ships shaders and nothing else (see the header)
            float Flicker (float amt, float phase, float rate)
            {
                float t = (_Time.y + _GhvrTimeOfs) * rate;
                float f = 0.42 * sin(t * 11.3 + phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
                f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
                return 1.0 + amt * 0.35 * f;
            }

            float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate)
            {
                float3 lv = lpos.xyz - opos;
                float d2 = max(dot(lv, lv), 1e-8);
                float d = sqrt(d2);
                float q = d2 * lpos.w * lpos.w;
                float x = saturate(1.0 - q);
                float atten = x * x / (1.0 + _PtHard * q);
                float ndl = saturate(dot(N, lv / d));
                return lcol.rgb * (atten * ndl * Flicker(lcol.a, phase, rate));
            }

            fixed4 frag (v2f i, fixed face : VFACE) : SV_Target
            {
                fixed4 alb = tex2D(_MainTex, i.uv) * _Tint;
                clip(alb.a - _Cutoff);

                float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.uv));
                n_ts.xy *= _BumpScale;

                // ============================================ SURFACE GROWTH ==
                // Frost on the leaves. A card is not a wall, so the affinity is
                // simpler: there is no wall foot and no mortar course, and what
                // a frond knows about itself is only how high it is and which
                // way it faces. `place` therefore leans on the sky term (frost
                // settles on top of a fern, not under it) and on being LOW,
                // where the cold air lies.
                //
                // THE DEEPER GREEN Earth used to put on a living plant is gone —
                // deleted, not zeroed, after the fourth rejection of that
                // surface (THE MOSS IS GONE, EnvGrowth.cginc). It was at its
                // most pointless exactly here: painting a green film over a
                // photoscanned FERN, i.e. over the one kind of surface in this
                // project that already reads as a plant because it has an
                // outline. That contrast — cards fine, paint a stain — is what
                // the user's own screenshot shows, side by side, in one frame.
                GhvrElem e = GhvrHauntElems();
                float eLive = _GhvrElemB.w * _GhvrElemB.z;
                float frost = 0.0;
                if (eLive > 0.0 && _ElemFrost > 0.0)
                {
                    float gt = _Time.y + _GhvrTimeOfs;
                    float rr = GhvrRim(length(i.opos.xz - _ElemCentre.xz), _ElemRad, 0.18);
                    float3 q = GhvrGrowQ(i.opos, _ElemCentre.xyz, _ElemScl, _ElemGrowFreq);
                    float creep = GhvrGrowCreep(q, gt);
                    float3 gN = normalize(i.n) * (face >= 0 ? 1.0 : -1.0);
                    float sky = saturate(normalize(mul((float3x3)unity_ObjectToWorld, gN)).y);
                    float hgt = (i.opos.y - _ElemCentre.y) * _ElemScl;
                    float low = saturate(1.0 - hgt * 0.55);
                    // no normal map on any foliage material in either room, so
                    // `grain` is 0 and the frontier is noise and place alone —
                    // which is right: a fern has no joints for frost to find.
                    float grain = saturate((1.0 - n_ts.z) * 2.2);
                    float ice = e.ice * _ElemFrost;
                    if (ice > 0.0)
                    {
                        frost = GhvrGrown(GhvrGrowField(q), grain,
                                          saturate(0.58 * sky + 0.42 * low),
                                          ice * (0.25 + 1.20 * rr), creep);
                    }
                    float lum = GhvrGrowLum(alb.rgb);
                    // ...and WHAT the covering is made of. ModBuild 151, "sehen
                    // nicht sehr wie Eis aus sondern eher wie Wasserpfützen":
                    // the plates, the boundaries and the trapped air, in ROOM
                    // METRES so a frosted fern frond and the flagstone under it
                    // carry the same 22 cm quilt. This shader deliberately does
                    // NOT take the crust's normal: every material that reaches
                    // this branch is a two-sided alpha-tested CARD with no normal
                    // map at all (`grain` is 0 three lines up and says so), and
                    // perturbing the flat normal of a 6 cm leaf by a 22 cm plate
                    // pattern would light it from a direction its own geometry
                    // contradicts. What a frosted leaf needs is the albedo, and
                    // the silhouette it already has.
                    GhvrFrostIce crust = GhvrFrostCrust(
                        GhvrGrowQ(i.opos, _ElemCentre.xyz, _ElemScl, 1.0));
                    alb.rgb = GhvrFrostOn(alb.rgb, lum, frost, crust);
                }
                // =============================================================

                float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);
                N *= face >= 0 ? 1.0 : -1.0;

                // ====================================== LIGHT AND DARK ========
                // The wood's foliage answers the moon exactly as its floor and
                // its trunks do — see the LIGHT AND DARK block in
                // EnvGround.shader for the user verdict and for why the pools
                // below are deliberately NOT in this. Both gains are exactly 1
                // with nothing up, so the branch is for cost only.
                //
                // THIS SHADER SERVES BOTH ROOMS (the wood's ferns and canopy,
                // the cellar's cobwebs and sacking), and since ModBuild 146 the
                // two functions below answer differently in each: the cellar's
                // ambient no longer lifts under Light at all and its moon lifts
                // 3.22x instead of 2.55x, on the verdict "es soll wirklich den
                // Mondschein heller machen statt den ganzen Raum". Nothing is
                // spelled here — GhvrAmbGain and GhvrDirGain read GhvrIndoor()
                // themselves, which is the whole reason the room is a global and
                // not a per-material property: a cobweb does not have to know
                // which cellar it is hanging in.
                float ambGain = 1.0, dirGain = 1.0;
                if (eLive > 0.0)
                {
                    ambGain = GhvrAmbGain(e);
                    // ...and indoors the LIFT is confined to the window's own
                    // throw, exactly as EnvRoom's masonry is (ModBuild 151). It
                    // has to be the same call in both files or the cobwebs, the
                    // sacking and the growth cards standing against a wall would
                    // brighten 3.22x under Light while the wall behind them
                    // stayed at 1.00 — a room lit through a window with the
                    // things IN it lit by something else. Outdoors the mask is
                    // not evaluated and the forest's foliage is untouched.
                    float thrown = GhvrIndoor() > 0.0
                        ? GhvrMoonWindow(i.opos, _ElemCentre.xyz, _ElemScl, _DirDir.xyz,
                                         _MoonWin, _MoonWinZ.xy)
                        : 1.0;
                    dirGain = GhvrDirGainThrown(e, GhvrMoonLight(), thrown);
                }

                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5)
                               * (ambGain + 0.30 * frost);
                light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz))) * dirGain;
                // SHELF RIDERS — GhvrTipSlot touches nothing at all unless a slot
                // is really riding, so the three accumulations below are the
                // shipped ones bit for bit in the forest and in a standing cellar.
                float4 p0 = _L0Pos, p1 = _L1Pos, p2 = _L2Pos;
                fixed4 c0 = _L0Col, c1 = _L1Col, c2 = _L2Col;
                GhvrTipSlot(i.tipL, 0.0, p0, c0);
                GhvrTipSlot(i.tipL, 1.0, p1, c1);
                GhvrTipSlot(i.tipL, 2.0, p2, c2);
                light += PointLight(p0, c0, i.opos, N, 0.0, 1.00);
                light += PointLight(p1, c1, i.opos, N, 2.1, 0.83);
                light += PointLight(p2, c2, i.opos, N, 4.4, 1.19);

                float3 col = alb.rgb * light;
                // KNOWN DEBT of ModBuild 135, paid here: _VCol was declared and
                // written by the builder but never APPLIED, so every per-vertex
                // tint baked into a cutout mesh did nothing — including the
                // forest canopy's Depth() fade, which is the single curve that
                // is supposed to dissolve the wood into black. EnvRoom.shader
                // has always had this line; this shader was the odd one out.
                // Materials that do not set _VCol default to 0 and are therefore
                // bit-identical (the cobwebs' vertex RED is a sway weight, not a
                // tint — they must keep _VCol = 0).
                col *= lerp(float3(1, 1, 1), i.vcol.rgb, _VCol);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
