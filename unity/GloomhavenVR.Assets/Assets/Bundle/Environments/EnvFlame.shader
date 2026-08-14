// GloomhavenVR — candle/lantern flame shader (shader-animated, script-free).
//
// Drawn on small static CROSS-QUAD meshes (two quads at 90°), NOT camera-facing
// billboards — the permanent VR rule forbids sprites that can visibly re-orient
// with the head (BuildEnvironments.cs header). A cross-quad flame is world-
// anchored geometry; _Time drives a gentle sway + UV wobble + brightness
// flicker matched to the EnvRoom light flicker (same sine family, so the flame
// and the light it casts breathe together). Additive, no depth write.
//
// ============================ REAL FIRE — USER VERDICT, ModBuild 142 =========
// "Das Feuer im Keller ist eher ein rötlicher Schein - ich möchte lieber das
//  Teile des Kellers wirklich brennen."
//
// The cellar's Fire was a warm RIM on the stone (EnvRoom's elemAdd) plus an
// ember ring at the periphery: light the colour of fire with nothing burning in
// it. This shader is now also the burning half, because a fire and a candle are
// the same drawing problem twice: alpha-blended tongues on world-anchored
// crossed cards, animated from the shared clock.
//
// THE GATE (_FireGate). Everything this shader draws for the burning cellar
// exists ONLY under the Fire infusion, and it has to cost nothing when Fire is
// down — so a gated flame COLLAPSES to a point in the vertex shader exactly as
// the gated particle emitters do (EnvParticleAdd's header): zero-area triangles,
// no fragment ever shaded, and the zero state is the same instructions on the
// same data rather than "close enough". _FireGate = 0 is the candles: they burn
// whatever the elements are doing, because they are the room.
// ============================================================================
//
// ==================== FIRE REAL — USER VERDICT, ModBuild 144 ================
// "Das Feuer im Keller sieht eher aus wie viele Kerzenflammen statt wirklich ein
//  bedrohliches Brennen der Möbel! Überarbeite das Feuer nochmal komplett, es
//  soll realistisch und bedrohlich wirken und auch die Lichtverhältnisse
//  entsprechend anpassen."
//
// He is describing the previous _Bonfire mode exactly, and the renders agree:
// the shipped fire is a ROW OF TALL AMBER SPIKES with black between them, each
// one a closed smooth teardrop, standing on a crate. Every one of those words is
// a candle. The three faults, and where each is now fixed:
//
//   1. THE SPRITE WAS A CANDLE FLAME. `candle_flame_alb` is one laminar
//      teardrop with a hard silhouette; enlarging it enlarges a candle. It is
//      replaced for bonfire materials by a four-cell procedural fire atlas
//      (BuildEnvironments.MakeFireAtlas) whose cells are a BED, two torn tongues
//      and a detached PUFF — shapes with holes and ragged tops, which is what
//      makes two overlapping cards read as one mass instead of as two objects.
//      The candles keep their own sprite and are untouched.
//   2. THE MOTION WAS A SWAY, NOT TURBULENCE. Measured off the shipped
//      material: surge = sin(lt*2.9) + sin(lt*4.7) with lt = t*_Rate*_LickRate,
//      _Rate 1.19, _LickRate 1.15 — i.e. 0.63 Hz and 1.03 Hz. A one-hertz
//      undulation IS a candle in a draught. The bonfire branch now runs on
//      GhvrFireFlicker's bands at _FireHz (shipped 4.6 Hz), so the tongues turn
//      over four to eight times a second.
//   3. NOTHING DETACHED. A candle flame is attached at all times; a fire throws
//      pieces of itself up that separate, cool, redden and go out. GHVR_FKIND_PUFF
//      cards do exactly that, on their own cycle, as a pure function of the
//      clock.
// Plus the light, which was the loudest omission: see EnvFire.cginc.
//
// A BURNING CRATE IS NOT A BIG CANDLE, and _Bonfire is the whole of that
// difference. A bonfire material is fed a mesh of many cards
// (EnvRoomBuilder.FireMesh) whose VERTEX COLOUR carries the per-card variation:
//     COLOR.r  the card's own phase, 0..1 of a turn
//     COLOR.g  how hard it surges (the bed barely; the tallest tongues most)
//     COLOR.b  how far it stands from the seat, 0..1 — the lean and the tear
//     COLOR.a  its share of the fire's brightness
// ...and UV1 the things that are not amounts:
//     UV1.x    which atlas cell it is drawn with
//     UV1.y    its KIND (GHVR_FKIND_* in EnvFire.cginc)
//     UV1.z    a puff's rise, in metres
//     UV1.w    a puff's place in its own cycle, 0..1
// None of that is read while _Bonfire is 0, which is what lets the room's three
// candle materials go on using a mesh that has neither stream.
// ============================================================================
//
// ==================== SHELF RIDERS — USER VERDICT, ModBuild 144 =============
// "Die Kerzen und das Feuer, die auf dem Bücherregal stehen, kippen nicht mit -
//  das musst du beheben das ist ein echter Bug."
//
// Three of this shader's materials stand ON the cellar's tipping bookshelf: the
// shelf candle's flame and the two fires seated on its boards. They take the
// shelf's pose from EnvShelfTip.cginc — the same call the shelf itself makes —
// and then do the two things a FLAME does that a rigid body does not:
//   * it does not turn with the object. Hot gas goes up whatever the wax is
//     doing, so the card is rigidly rotated (which keeps the wick welded to the
//     candle) and then bent BACK about its own base, weighted by height. At 88
//     degrees of shelf the plume is still within twenty of vertical.
//   * it lags, gutters and goes out. See GhvrTipFlameLife: the flame dies about
//     a quarter turn into the fall, is out for the whole of the ten seconds the
//     shelf lies on the floor, and lights itself again late in the recovery with
//     one flare. The LIGHT it casts takes the same number in the same frame
//     (EnvRoom's GhvrTipSlot), which is the only way a candle going out is not
//     also a pool of light that outlives it.
// The two SEATED FIRES ride rigidly and do not gutter: a burning bookshelf that
// falls over is still a burning bookshelf.
// ============================================================================
//
// ============== THE FIVE PAIRINGS THAT INVOLVE FIRE — ModBuild 145 ==========
// USER: "Schau dir auch jede mögliche Kombination der Elemente an ... So zB das
//  das Feuer der brennenden Bäume noch mehr Glut wirft und flackert wenn Wind
//  an ist etc."
//
// The composition rule, the reason there is one, and what each pair means are
// in EnvFire.cginc's PAIRINGS block. What this shader contributes:
//   FIRE+AIR   the rate (GhvrFireHz) and the depth (GhvrFireDepth) — the same
//              two functions the wash takes, so the flame and its light cannot
//              come apart under wind; a harder outward tear; the WHOLE fire
//              leaning, bed included; and detached pieces thrown three times as
//              far downwind and alive for three quarters of their cycle instead
//              of a third, which is "noch mehr Glut" made of one number.
//   FIRE+DARK  the bonfire stops taking the CANDLE's Dark response (which would
//              have dimmed it to 55% — the exact opposite of the pairing) and
//              gains a third instead.
//   FIRE+LIGHT a detached piece becomes SMOKE: pale, climbing far past where an
//              ember dies, spreading as it goes. Only here, because smoke is
//              only visible when something lights it.
//   FIRE+ICE   a detached piece becomes STEAM: white, low, slow, thickest at
//              the seat; and the flame's own ramp is entered further along,
//              because something is taking heat out of it.
//   FIRE+EARTH it SMOULDERS: shorter tongues, the ramp entered further along
//              still, dimmer, and pieces that barely lift before they sink.
// Every one of them is a product of two element strengths and is exactly zero
// unless both are up.
// ============================================================================
//
// ============== IT ZAPPELT — USER VERDICT, ModBuild 145 =====================
// "Das Feuer zappelt viel zu schnell und ist damit nicht sehr immersiv."
//
// The full diagnosis, the frequency/structure-size table it rests on and the
// three fixes that were REJECTED are in EnvFire.cginc's own ModBuild 145 block;
// read that first, because it is the argument and this is only the half of it
// that moves vertices. In one line: the clock is right and always was, but
// every band was pointed at the wrong SIZE of thing. The frequency of a
// structure in a fire is set by its size (f = 1.5/sqrt(D)), so
//
//     w.w 1.08 Hz -> 1.9 m      w.y 2.81 Hz -> 29 cm
//     w.x 4.60 Hz -> 11 cm      w.z 7.96 Hz -> 3.6 cm
//
// ...and this shader was moving a 40 cm TONGUE with w.x and w.z, and moving the
// 3 cm texture detail at 1-2 Hz. Exactly inverted. What changed here:
//
//   1. THE SURGE IS NOW TWO SURGES, split by height. A tongue's LENGTH is a
//      30-60 cm structure and now rides the slow trio (1.1/2.8/4.6 weighted
//      0.34/0.44/0.22); the fast pair survives only as a TIP FLUTTER, entering
//      on h*h so it is nothing at the tongue's foot and 30 % at its tip — which
//      is not a compromise but the actual shape of a flame, whose base is
//      pinned where it is fed and whose last hand's breadth whips. Measured at
//      the tip: mean frequency 6.0 -> 3.8 Hz, rms tip speed 3.33 -> 1.61 m/s,
//      rms acceleration 144 -> 61 m/s^2, and the excursion is UNCHANGED at
//      +-17.8 cm. It moves exactly as far as it did, in half the hurry.
//   2. THE LATERAL WANDER STOPS BEING AN 8 Hz SHAKE. `p.x += w.z * wob` moved
//      the whole card 10.4 cm sideways at 8 Hz — 3.6 m/s, 183 m/s^2, sixteen
//      direction reversals a second, on a card 37 cm long. It is the single
//      loudest jitter in the shader and the most likely literal referent of the
//      word "zappelt". The body of the tongue now wanders on the 2.8/1.1 pair
//      (and on both axes, out of phase, so it circles instead of shivering
//      along one axis), and the fast band stays as a h^4 wrinkle worth 2.5 cm
//      at the very tip.
//   3. THE FAST CONTENT IS NOT DELETED, IT IS MOVED TO WHERE IT LIVES. The
//      fragment's UV wobble is the one term in this shader that displaces
//      TEXTURE detail — holes and torn edges a few centimetres across, i.e.
//      exactly the 3.6 cm structure w.z names — and it was running at 2.1 Hz.
//      The bonfire path now adds a small ripple on the fire's own w.z band,
//      travelling up the card. So the fire keeps its 8 Hz life; it spends it on
//      the scale where 8 Hz is what a fire does, and where the amplitude is
//      millimetres and cannot read as a twitch.
//   4. A CARD'S SHAPE AND ITS BRIGHTNESS ARE NOW THE SAME EVENT. The flicker
//      call was phased with `v.color.r * 6.2831853` while the surge was phased
//      with `v.color.r` — a leftover from a radians formulation, and since the
//      wave argument is in CYCLES it meant every card's glow was uncorrelated
//      with its own leap. Two independent random signals per card is twice the
//      visual noise of one and says nothing more. Same phase now.
// Nothing here changes a rate, a bake constant, or one instruction of the
// CANDLE path: every edit is inside `if (_Bonfire > 0.5)`.
// ============================================================================
Shader "GloomhavenVR/EnvFlame"
{
    Properties
    {
        _MainTex ("Flame sprite", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Sway ("Sway amount", Range(0,0.2)) = 0.05
        _Flicker ("Brightness flicker", Range(0,1)) = 0.35
        _Phase ("Phase offset", Float) = 0
        // MUST equal the rate of the EnvRoom light slot this flame belongs to
        // (EnvRoom: slot0 1.00, slot1 0.83, slot2 1.19) — otherwise the flame
        // you see and the light it casts drift apart. A gated FIRE does not use
        // this at all: its rate is _FireHz and its light is the fire wash, which
        // is written with that same number (see _FireHz).
        _Rate ("Flicker rate (match the light slot)", Float) = 1
        // The DRAFT. Deliberately UNPHASED and slow, so every flame in the room
        // leans the same way at the same moment: that is what reads as one
        // draught through the cellar rather than three independent candles.
        _Gust ("Draft amount", Range(0,0.3)) = 0
        _GustDir ("Draft direction (OBJECT space XZ)", Vector) = (1,0,0,0)

        // ---- ELEMENT ART: AIR, and why this is a per-material number --------
        // USER VERDICT, ModBuild 142: "Im Keller sollte es noch mehr wie ein
        // Windzug wirken der insbesondere aus dem Fenster kommt." Air used to
        // multiply every flame's draught by the same 3.4, which is a room that
        // is uniformly windy — the opposite of a draught with a SOURCE. The
        // builder now writes this per flame from its distance to the window
        // opening, so under Air the flame nearest the aperture is laid almost
        // flat and the one in the far corner barely stirs, and the eye can find
        // the window by watching the fire. 3.4 is the old constant, so a
        // material that does not set it is unchanged.
        _AirGust ("Air: draught multiplier at this flame", Float) = 3.4

        // ---- REAL FIRE ------------------------------------------------------
        _Bonfire ("Bonfire mode (0 = candle, 1 = burning object)", Range(0,1)) = 0
        _FireGate ("Exists only under the Fire infusion", Range(0,1)) = 0
        _Lick ("Bonfire: tongue surge amount", Range(0,1.5)) = 0.55
        _Flare ("Bonfire: outward tear of the outer tongues", Range(0,0.5)) = 0.10
        // THE ONE RATE. In HERTZ, and it is the number the wash on the wall is
        // written with as well (EnvFire.cginc / _FireRate), so the standing rule
        // — a flame and the light it casts share a rate — is now one constant in
        // two properties rather than two families of sines that happen to agree.
        _FireHz ("Bonfire: turbulence rate (Hz) — SHARED with the fire wash", Float) = 4.6
        _PuffHz ("Bonfire: detachment rate (puffs per second per card)", Float) = 0.55
        // THE HEIGHT OF THE WHOLE FIRE, in the mesh's own units. The temperature
        // ramp is a property of the FIRE and not of a card: a 12 cm bed card
        // whose own uv.y ran the full ramp would be dark red at its top, which
        // is exactly the wrong end of the gradient for the hottest thing in the
        // frame. Everything is measured against this instead.
        _FireH ("Bonfire: total fire height (object units)", Float) = 1
        _BaseCol ("Bonfire: colour in the seat (white-blue)", Color) = (1.45,1.28,1.10,1)
        _CoreCol ("Bonfire: colour through the body", Color) = (1.30,0.72,0.26,1)
        _TipCol ("Bonfire: colour where the tongues tear off", Color) = (0.62,0.13,0.03,1)
        // What a detached piece BECOMES under the two pairings that change what
        // it is (see the PAIRINGS block below). Neither is ever reached unless
        // both of its elements are up, so both are inert by construction.
        _SmokeCol ("Fire+Light: moonlit smoke", Color) = (0.60,0.66,0.78,1)
        _SteamCol ("Fire+Ice: steam", Color) = (0.66,0.74,0.86,1)

        // ---- SHELF RIDERS (EnvShelfTip.cginc owns all five; see the block
        // above for what a flame does with them that a rigid body does not).
        // All zero = "this flame is not standing on the bookshelf", which is
        // every flame in both rooms except three.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, how much the flame refuses", Vector) = (0,-1,0,0)
    }
    // ELEMENT ART (EnvElement.cginc). The flame is the cellar's Fire, Air, Light
    // and Dark all at once, and it is the one object in the room that can show
    // all four without a single new triangle:
    //   FIRE  the flame flares — taller, brighter, whiter at the core.
    //   AIR   the AUTHORED draught strengthens: the same _GustDir it already
    //         leans along, several times over, so the room's one draught becomes
    //         a wind. Nothing new to explain; the player has already seen it.
    //   LIGHT it is a SOURCE, so Light drives it (see the split).
    //   DARK  the candles duck: shorter and dimmer, but NOT out. Under Light+Dark
    //         both apply and Light wins on the flame while Dark wins on the room
    //         around it — which is the split, told on one candle.
    // Ice deliberately does NOTHING here. The design's Fire+Ice mixture is
    // "embers rising through falling snow"; an Ice that shrank the flame would
    // cancel Fire's flare and turn a mixture into an average, which the brief
    // forbids.
    SubShader
    {
        Tags { "Queue"="Transparent+15" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            // The fire's shared numbers (the waveform, the ramp, the card kinds)
            // and — through it — the tipping shelf's pose.
            #include "EnvFire.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint, _BaseCol, _CoreCol, _TipCol, _SmokeCol, _SteamCol;
            float _Sway, _Flicker, _Phase, _Rate, _Gust, _AirGust;
            float _Bonfire, _FireGate, _Lick, _Flare;
            float _FireHz, _PuffHz, _FireH;
            float4 _GustDir;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // ATLAS GEOMETRY. 2x2 cells; mirrored in BuildEnvironments
            // (FireAtlasCols/Rows, FireTile) — change one, change both. The inset
            // is what stops a card's own bilinear tap reaching into its
            // neighbour's cell at the mip levels a fire is seen at across a room.
            #define GHVR_FIRE_COLS  2.0
            #define GHVR_FIRE_CELL  0.5
            #define GHVR_FIRE_INSET 0.013

            // COLOR and UV1 are the per-card streams and are read ONLY inside the
            // `_Bonfire` branch (a uniform, so the candles never touch them). The
            // candle mesh has neither channel; Unity supplies white for a missing
            // colour stream and zero for a missing texcoord, and neither is used.
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;   // 0..1 up and across THIS card, always
                float4 fp : TEXCOORD1;   // bonfire: cell, kind, puff rise, puff cycle
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed fl : TEXCOORD1;
                fixed fire : TEXCOORD2;
                // bonfire: x = height in the FIRE's frame (0 at the seat),
                //          y = a detached puff's age 0..1, z = its cell index,
                //          w = its alpha
                float4 fx : TEXCOORD3;
                // ...and WHAT a detached piece has turned into, which is the one
                // thing the pairings change about a card rather than about a
                // number: x = smoke (Fire+Light), y = steam (Fire+Ice),
                // z = smoulder (Fire+Earth). All exactly 0 unless both of the
                // pair's elements are up. See the PAIRINGS block.
                //
                // ...and w = THIS CARD'S PLACE IN THE FAST BAND, 0..1, which the
                // fragment needs to ripple the texture on the fire's own w.z
                // (ModBuild 145; see the ZAPPELT block). Three things about it:
                //   * it is FRAC'D in the vertex shader. A raw clock through an
                //     interpolator grows without bound and a mobile GPU is free
                //     to carry a varying at half precision, where t = 500 s
                //     quantises to steps of 4; GhvrWave4 takes frac() of its
                //     argument anyway, so pre-wrapping is exact, not a
                //     rounding. (Same reason the puff's `age` is a frac.)
                //   * it is CONSTANT over the card — all eight vertices of a
                //     cross carry one COLOR.r — so it interpolates exactly and
                //     the wrap can never land inside a triangle.
                //   * it costs nothing: TEXCOORD4 was a float3 in a register
                //     four floats wide.
                float4 pr : TEXCOORD4;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float t = _Time.y + _GhvrTimeOfs;
                float ft = t * _Rate;

                // ---- ELEMENT ART: the four modifiers, identity when nothing is up
                GhvrElem e = GhvrElems();

                // REAL FIRE — the gate. A burning crate exists because the Fire
                // infusion is up and for no other reason, so with Fire down the
                // whole object is collapsed to a point: two zero-area triangles
                // per card, nothing rasterised, and the room is bit-for-bit the
                // room that was tuned without it. This is the FIRST thing the
                // shader does, before any of the arithmetic below.
                float gate = 1.0;
                if (_FireGate > 0.5)
                {
                    gate = saturate(e.fire);
                    if (gate <= 0.0)
                    {
                        o.pos = float4(0, 0, 0, 1);
                        o.uv = float2(0, 0); o.fl = 0; o.fire = 0;
                        o.fx = float4(0, 0, 0, 0);
                        o.pr = float4(0, 0, 0, 0);
                        return o;
                    }
                }

                // THE FIVE PAIRINGS THAT INVOLVE FIRE — see the block in
                // EnvFire.cginc for the composition rule. Every field is a
                // product of two element strengths and is therefore exactly 0
                // unless both are up, which is what makes the six single-element
                // states below the ones that were tuned.
                GhvrFirePair pair = GhvrFirePairs(e);
                float gustMul = 1.0, swayMul = 1.0, tall = 1.0, bright = 1.0;
                if (e.live > 0.0)
                {
                    // _AirGust is per material and comes from the flame's own
                    // distance to the window (see the property). The draught has
                    // a source, so its strength has a gradient.
                    gustMul = 1.0 + _AirGust * e.air;
                    swayMul = 1.0 + 1.3 * e.air;
                    // a flame laid over by a draught is also LONGER, and a
                    // ducking one is shorter: one number carries both
                    tall = max(1.0 + 0.55 * e.fire + 0.25 * e.air - 0.40 * e.dark, 0.05);
                    // THE FIRE TERM IS FOR CANDLES ONLY. `1.05 * e.fire` is "the
                    // candles flare while the room is infused", and on a bonfire —
                    // which exists ONLY under that infusion — it is not a response
                    // to anything, it is a hidden constant multiplier of 2.05 on
                    // top of every energy number in the mesh. It cost this round a
                    // bake: with it, thirty overlapping additive cards clipped to
                    // white over their whole area and the fire's own card edges
                    // showed as hard white parallelograms.
                    bright = max(1.0 + (_Bonfire > 0.5 ? 0.0 : 1.05 * e.fire)
                                     + 0.55 * e.light - 0.45 * e.dark, 0.0);
                    if (_Bonfire > 0.5)
                    {
                        // A BONFIRE IS NOT A CANDLE UNDER LIGHT AND DARK. The
                        // candle line above is the ModBuild 142 design ("the
                        // candles duck under Dark, they lift under Light"), and
                        // applied to a burning crate it says the exact opposite
                        // of the two pairings: FIRE+DARK is "the fire is the only
                        // light left" and would have dimmed it to 55%, FIRE+LIGHT
                        // is "it gains a plume, not a boost". So the bonfire path
                        // replaces both with its own pair terms.
                        bright = max(1.0 + 0.34 * pair.dark - 0.30 * pair.earth
                                         - 0.10 * pair.light, 0.0);
                    }
                }
                o.fire = e.fire;

                // sway grows with height (uv.y=0 at flame base) — the tip dances
                float h = v.uv.y;
                float sway = (sin(ft * 5.7 + _Phase) * 0.6 + sin(ft * 9.3 + 1.3 + _Phase) * 0.4)
                             * _Sway * swayMul * h * h;
                // the draft: slow, shared, unphased (see _Gust)
                float g = sin(t * 0.37) * 0.62 + sin(t * 0.83 + 1.1) * 0.38;
                float4 p = v.vertex;

                // ---- FIRE REAL: what a crowd of tongues does that one does not
                float lick = 1.0, lickE = 1.0, age = 0.0, cardA = 1.0, cell = 0.0;
                float3 pr = float3(0, 0, 0);
                float texPh = 0.0;
                if (_Bonfire > 0.5)
                {
                    cell = v.fp.x;
                    float kind = v.fp.y;
                    float tp = v.color.r;                 // this card's own turn
                    // FIRE+AIR — the rate, through the SAME function the wash
                    // takes it through (EnvFire.cginc). A fire in a draught turns
                    // over faster; the pool it throws turns over with it.
                    float bt = t * GhvrFireHz(pair, _FireHz);   // CYCLES, not radians

                    // FOUR BANDS OFF ONE CALL, and the numbers are the point: at
                    // the shipped 4.6 Hz these are 4.6 / 2.8 / 8.0 / 1.1 Hz. The
                    // ratios live in EnvFire.cginc next to the table that says
                    // which SIZE of structure each of them is the frequency of;
                    // this used to be a hand-kept copy of those four arguments.
                    float4 w = GhvrFireBands(bt, tp);
                    // the cards do not surge TOGETHER — each one is on its own
                    // phase, so the silhouette of the fire is never the same
                    // twice. That, and not brightness, is what says "burning" at
                    // two to five metres.
                    //
                    // ---- ModBuild 145, "das Feuer zappelt viel zu schnell" ----
                    // The surge is the tongue's LENGTH, i.e. it moves the whole
                    // 30-60 cm structure, and it used to be 0.58*w.x + 0.42*w.z —
                    // the 11 cm band and the 3.6 cm band, both several times too
                    // fast for the thing they were moving, with nothing at all in
                    // the 1.4-2.8 Hz where a fire this size actually puffs.
                    //
                    // SLOW is that structure's own spectrum: peaked at the
                    // puffing band (w.y, 2.8 Hz), with the swell under it (w.w)
                    // and a roll-off above (w.x). Weights sum to 1.
                    float slow = 0.34 * w.w + 0.44 * w.y + 0.22 * w.x;
                    // FAST is what is left of the old pair, and it is not thrown
                    // away — a flame's last hand's breadth really does whip at
                    // several hertz. It sums to 1 as well.
                    float fast = 0.45 * w.x + 0.55 * w.z;
                    // ...and it enters BY HEIGHT UP THE CARD. h*h is zero at the
                    // foot, which is where the tongue is anchored in the bed and
                    // physically cannot flick, and 30 % at the tip, which is the
                    // part that has left the fuel behind. This is the whole of
                    // the fix: not less fast motion, fast motion CONFINED TO THE
                    // SMALL END of a big structure.
                    //
                    // The pair is MIXED and not summed, so |surge| <= 1 at every
                    // height and _Lick 0.48 keeps meaning exactly what it says on
                    // the bake — the excursion is unchanged at +-17.8 cm on a
                    // 37 cm tongue and only its spectrum has moved. A sum would
                    // have doubled the tongue's reach and cost the round a bake.
                    float mix = 0.30 * h * h;
                    float surge = (1.0 - mix) * slow + mix * fast;
                    lick = max(1.0 + _Lick * v.color.g * surge, 0.10);
                    // THE ENERGY TAKES THE SLOW HALF ONLY, and per CARD rather
                    // than per vertex. `lick` is now a function of height, and
                    // feeding that to o.fl would paint a brightness gradient up
                    // every card that fights the temperature ramp already there.
                    // Physically it is also the right half: a tongue that is
                    // stretching as a whole is being fed harder and glows; a tip
                    // that flutters is not burning any harder for it.
                    lickE = max(1.0 + _Lick * v.color.g * slow, 0.10);
                    // THE FAST BAND'S PHASE, handed to the fragment so that the
                    // 8 Hz life the geometry just gave up comes back on the scale
                    // it belongs to — a ripple in the TEXTURE, i.e. in holes and
                    // torn edges a few centimetres across (see the ZAPPELT block
                    // and the note on v2f.pr.w for why it is frac'd here rather
                    // than reconstructed there). It is bt's w.z band with its own
                    // offsets, so it rides GhvrFireHz and speeds up under
                    // Fire+Air with everything else, and it is decorrelated from
                    // the geometric w.z so the wrinkle and the ripple are two
                    // eddies rather than one drawn twice.
                    texPh = frac(bt * 1.73 + tp * 2.3 + 0.19);
                    // a tongue standing at the RIM of the fire is torn outward and
                    // dies sooner; COLOR.b is how far out it stands. Without this
                    // the tongues all point straight up and the fire reads as a
                    // bush.
                    // FIRE+AIR: a tongue in a draught is torn outward harder and
                    // dies back sooner. Same term, more of it.
                    float tear = _Flare * (1.0 + 1.9 * pair.air) * v.color.b * (0.55 + 0.45 * w.y);
                    p.x += p.x * tear * h;
                    p.z += p.z * tear * h;
                    // FIRE+AIR: ...and the WHOLE fire leans, bed included. The
                    // bed is the one part that does not wander on its own (see
                    // COLOR.g), so without this a windblown fire is tongues
                    // leaning off a seat that is standing still — which is a fire
                    // in a room with a draught, not a fire being blown.
                    // ...and it leans on the SLOW pair. The whole fire is the
                    // largest structure in the picture (0.5-1.2 m across, i.e.
                    // 1.35-2.17 Hz by f = 1.5/sqrt(D)), so of the four bands it
                    // must take the two slowest; on w.x it was a metre of fire
                    // being shoved about at the rate of an 11 cm eddy.
                    p.xz += _GustDir.xz * (0.55 * pair.air * h
                                           * (0.6 + 0.4 * (0.6 * w.w + 0.4 * w.y)));
                    // ...and every tongue wanders on its OWN phase. _Sway is one
                    // number per material, so without this the whole fire leans
                    // as a single bush and the tongues stay in the rows the mesh
                    // put them in. Weighted by COLOR.g, so the BED (which barely
                    // surges) also barely wanders: a bed of embers that slid
                    // about would read as a puddle of light rather than as the
                    // seat of a fire.
                    //
                    // ---- ModBuild 145: THIS WAS THE LOUDEST "ZAPPELN" --------
                    // `p.x += w.z * wob` displaced the WHOLE card (h*h, so the
                    // entire upper two thirds of a 37 cm tongue) by up to 10.4 cm
                    // sideways at 7.96 Hz: 3.6 m/s rms, 183 m/s^2 rms, sixteen
                    // direction reversals a second. Nothing else in the shader
                    // came close, and a 3.6 cm band was driving a 40 cm object.
                    // Worse, X took w.z and Z took w.y, so the two axes ran at
                    // 8 Hz and 2.8 Hz and the card shivered along a LINE.
                    //
                    // The body of the tongue now takes the two slow bands on both
                    // axes with the weights swapped and one sign flipped, so the
                    // tip traces an ellipse rather than a line — one wave call,
                    // no extra cost, and a wander that reads as gas turning over
                    // rather than as a tremble. Each axis' weights still sum to
                    // 1, so the authored 10.4 cm is exactly preserved.
                    float wob = _Sway * 1.9 * v.color.g * h * h;
                    p.x += (0.62 * w.y + 0.38 * w.w) * wob;
                    p.z += (0.62 * w.w - 0.38 * w.y) * wob;
                    // ...and the fast pair survives as a WRINKLE at the very tip:
                    // h^4, so it is 6 % of itself at half height and worth 2.5 cm
                    // where the tongue is thinnest. That is a 3.6 cm structure
                    // moving 2.5 cm at 8 Hz, which is precisely what the band is
                    // for. rms speed on X falls 3.64 -> 1.19 m/s and rms
                    // acceleration 183 -> 46 m/s^2, with only a tenth of the
                    // power left above 6 Hz and the tip's liveliness still
                    // visibly there.
                    float wrinkle = _Sway * 0.45 * v.color.g * h * h * h * h;
                    p.x += w.z * wrinkle;
                    p.z += w.x * wrinkle;
                    // The whole fire grows with the infusion: at the waning
                    // plateau it is a fire DYING BACK — smaller and lower — not
                    // the same fire turned down.
                    //
                    // sqrt, not the gate itself, and the first bake is why: with
                    // a linear gate the waning plateau (0.40, breathing
                    // 0.28..0.52) came out at 40% of the fire's energy and the
                    // previews of it showed a room with a few embers in it. A
                    // waning infusion is still an infusion and the user has to
                    // see it burning. sqrt(0.40) = 0.63 is a fire on its way out;
                    // it is still 0 at 0, so nothing pops in, and the curve is
                    // steepest near zero, which is where the ramp needs to be
                    // gentle.
                    gate = sqrt(gate);
                    tall *= 0.50 + 0.50 * gate;
                    // FIRE+EARTH — it SMOULDERS. Wet growth on burning wood does
                    // not flame cleanly: the tongues are shorter and the energy
                    // stays down in the glowing bed. `tall` is the parameter the
                    // single-element path already owns, so this is a modulation
                    // and not a new layer; the bed is unaffected because the bed
                    // is short already and the multiply is on the whole fire.
                    tall *= 1.0 - 0.42 * pair.earth;

                    // ---- THE DETACHED PUFF, i.e. the thing that was missing.
                    // A card that leaves the fire, rises, cools and goes out, on
                    // its own cycle, from its own place in that cycle. frac() of
                    // a clock is a pure function with no state and no birth
                    // event: at any instant the puffs of one fire are spread
                    // through their lives because UV1.w spreads them, and a
                    // player who looks away and back sees a different set.
                    if (kind > 1.5)
                    {
                        age = frac(bt * (_PuffHz / max(_FireHz, 0.01)) + v.fp.w);
                        // ---- WHAT A DETACHED PIECE IS, and it is the one thing
                        // the pairings change about a CARD rather than about a
                        // number. All three are products of two elements, so a
                        // fire with one element up sheds ordinary embers.
                        //   SMOKE  (Fire+Light) it is not burning any more, it is
                        //          the soot above the flame — and it is only
                        //          visible because there is a swollen moon to
                        //          light it. This is the pairing's whole content:
                        //          a fire next to a brighter moon that gains a
                        //          plume instead of merely looking weaker.
                        //   STEAM  (Fire+Ice) it is water, not soot: white, low,
                        //          slow, and thickest right at the seat where the
                        //          two are actually meeting.
                        //   SMOULDER (Fire+Earth) it barely leaves at all — a
                        //          dull red ember that lifts a hand's breadth and
                        //          sinks back.
                        pr = float3(pair.light, pair.ice, pair.earth);
                        // ...and each of them changes how LONG the piece lasts,
                        // which is the same alpha envelope the ember already has:
                        // smoke outlives an ember by a long way, steam a little,
                        // a smoulder hardly at all.
                        float tail = 0.30 + 0.55 * pr.x + 0.22 * pr.y - 0.14 * pr.z;
                        cardA = smoothstep(0.0, 0.10, age)
                              * (1.0 - smoothstep(tail, 1.0, age));
                        // FIRE+AIR: MORE GLUT, and it lives longer because it is
                        // being fed on the way. His own example, so it is the one
                        // that has to be unmistakable — the piece is visible over
                        // three quarters of its cycle instead of a third, which
                        // is the same thing as several times as many in the air.
                        cardA = max(cardA, smoothstep(0.0, 0.07, age)
                                           * (1.0 - smoothstep(0.55 + 0.40 * pair.air, 1.0, age))
                                           * saturate(pair.air * 1.6));
                    }
                }

                // the flame grows from its WICK: the mesh's origin is the wick, so
                // scaling y about it lengthens the flame instead of lifting it off
                // the candle (CrossQuadMesh puts uv.y=0 at y=0). For a bonfire the
                // origin is the seat of the fire and the same argument holds.
                p.y *= tall * lick;
                p.x += sway + _GustDir.x * _Gust * gustMul * g * h * h;
                p.z += sway * 0.6 + _GustDir.z * _Gust * gustMul * g * h * h;
                // ...and a detached puff travels, AFTER the fire's own growth: it
                // has already left, so it does not grow with what it left.
                if (_Bonfire > 0.5 && v.fp.y > 1.5)
                {
                    // HOW FAR IT GOES. Four terms on ONE parameter — the authored
                    // rise — rather than four behaviours:
                    //   AIR   thrown much further (his example: "noch mehr Glut")
                    //   LIGHT smoke climbs far past where an ember dies
                    //   ICE   steam is heavy and slow, and hangs at the seat
                    //   EARTH a smoulder barely lifts at all
                    // IT GROWS AS IT GOES, and that is what turns a scatter of
                    // embers into a PLUME. A smoke puff entrains air and swells;
                    // a steam puff does the same only more so and lower down; a
                    // smoulder's murk creeps. The scale is about the fire's own
                    // origin — which for a card that has already left is exactly
                    // right, because it enlarges the card AND lifts it in one
                    // multiply. (The card cannot be scaled about its own base:
                    // it does not know where that is, UV1 being full. The
                    // difference is that the plume climbs a little faster than
                    // authored, which is the direction a plume errs in anyway.)
                    float grow = 1.0 + 1.45 * pr.x * age + 0.95 * pr.y * age
                                     + 0.55 * pr.z * age;
                    p.y *= grow;
                    p.xz *= grow;
                    float climb = v.fp.z * (1.0 + 1.30 * pair.air + 2.40 * pr.x
                                            - 0.55 * pr.y - 0.62 * pr.z);
                    p.y += climb * age;
                    // it drifts with the room's draught as it rises, which is what
                    // ties it to the same air the flames lean in — and under
                    // FIRE+AIR that drift becomes a visible EMBER TRAIL running
                    // downwind off whatever is burning, which in the wood is the
                    // thing the pairing is meant to be recognised by.
                    float drift = v.fp.z * age * age * (0.35 + 3.10 * pair.air);
                    p.x += _GustDir.x * drift;
                    p.z += _GustDir.z * drift;
                }
                // HEIGHT IN THE FIRE'S OWN FRAME — the temperature ramp's input.
                // Taken after every displacement, so a surging tongue really does
                // redden as it stretches.
                o.fx = float4(saturate(p.y / max(_FireH, 1e-3)), age, cell, cardA);
                o.pr = float4(pr, texPh);   // .w = this card's place in the fast band

                // ---- SHELF RIDERS: is this flame standing on the bookshelf? ---
                GhvrTip tip = GhvrTipNow(t);
                float life = 1.0;
                if (tip.live > 0.5)
                {
                    // 1. THE BEND, about the flame's OWN base (the mesh origin is
                    //    the wick / the seat), against the shelf's rotation and
                    //    weighted by height. A flame goes up whatever the thing
                    //    holding it is doing; _TipUse.w is how much of the
                    //    rotation it refuses (0.8 for a candle, less for a bed of
                    //    fire lying on a board that is turning under it).
                    p.xyz = GhvrTipRot(p.xyz, float3(0, 0, 0), tip.axis,
                                       -tip.ang * _TipUse.w * h * h);
                    // 2. THE RIGID PART, which is what keeps the wick welded to
                    //    the candle and the candle standing on the shelf.
                    p.xyz = GhvrTipRot(p.xyz, tip.pivot, tip.axis, tip.ang);
                    // 3. THE LAG. Hot gas has momentum the wax does not, so the
                    //    plume trails the swing instead of arriving with it.
                    p.xyz += GhvrTipLag(tip, p.xyz, h);

                    if (_TipUse.z > 0.5)
                    {
                        life = GhvrTipFlameLife(tip);
                        // A GUTTERING FLAME IS SHORT AND UNEVEN. The length goes
                        // with the life so the flame shrinks into the wick rather
                        // than fading as a full-size ghost of itself.
                        p.y *= 0.30 + 0.70 * life;
                        bright *= life * GhvrTipRelightFlare(tip, life);
                        // ...and while the shelf is actually swinging it flutters
                        // hard, four times faster than it breathes at rest
                        bright *= 1.0 - 0.45 * saturate(abs(tip.vel) * 2.2)
                                  * saturate(0.5 + 0.5 * GhvrWave4(float4(t * 11.0, 0, 0, 0)).x);
                    }
                }

                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                if (_Bonfire > 0.5)
                {
                    // ONE waveform for the flame and for the light it casts (see
                    // EnvFire.cginc). Per-card phase, shared rate: the parts of
                    // one fire are independent, a fire and its wash are not.
                    // Both the rate AND the depth go through the pair functions,
                    // so FIRE+AIR reaches the flame and the pool identically.
                    //
                    // ModBuild 145 — THE PHASE IS `v.color.r`, NOT `* 6.2831853`.
                    // The wave's argument is in CYCLES (GhvrWave4 takes frac of
                    // it), so the 2*pi was a leftover from a radians formulation
                    // and made the card's brightness wrap 6.28 times across the
                    // same COLOR.r that phases its shape: a card's glow and a
                    // card's leap were two uncorrelated signals. Two independent
                    // random signals per card is twice the visual noise of one
                    // and carries no more information — and "more uncorrelated
                    // noise" is a fair paraphrase of "zappelt". Now a tongue
                    // brightens as it rises, which is also what a tongue does.
                    o.fl = GhvrFireFlicker(t, GhvrFireHz(pair, _FireHz),
                                           v.color.r,
                                           GhvrFireDepth(pair, _Flicker))
                         * bright
                         // per-card share of the fire's energy, and the surge
                         // shows in the light as well as in the shape — a tongue
                         // that leaps is a tongue that is burning harder. The
                         // SLOW half of the surge (lickE), so that a card is one
                         // brightness and a fluttering tip does not paint a
                         // gradient up it — see lickE where it is computed.
                         * v.color.a * (0.55 + 0.45 * lickE) * gate * cardA
                         // SMOKE AND STEAM CARRY MORE THAN AN EMBER DOES. A
                         // detached ember is a spark's worth of energy; a lit
                         // plume is a body of scattering matter and is the whole
                         // content of Fire+Light. Same parameter (this card's
                         // share), more of it, and only when both elements are up.
                         * (1.0 + 2.30 * pr.x + 1.10 * pr.y);
                }
                else
                {
                    // brightness flicker — same sine family AND same rate as the
                    // EnvRoom light slot this candle drives
                    float f = 0.42 * sin(ft * 11.3 + _Phase)
                            + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                            + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                    f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                    // a flame that is bent by a draught also burns brighter
                    o.fl = (1.0 + _Flicker * 0.35 * f)
                         * (1.0 + 0.55 * _Gust * gustMul * abs(g)) * bright;
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // wick-anchored UV wobble: zero at base, grows toward the tip
                float t = (_Time.y + _GhvrTimeOfs) * _Rate;
                float wob = (sin(t * 13.1 + i.uv.y * 9.0 + _Phase)
                           + sin(t * 7.3 + 2.1 + _Phase)) * 0.012 * i.uv.y;
                float2 uv = i.uv + float2(wob, 0);
                fixed4 c;
                if (_Bonfire > 0.5)
                {
                    // ---- ModBuild 145: WHERE THE FAST BAND WENT ---------------
                    // "Das Feuer zappelt viel zu schnell" was answered in the
                    // vertex shader by taking the 8 Hz band off the tongue, which
                    // is a 40 cm object that has no business moving at the rate
                    // of a 3.6 cm one. It is not answered by DELETING 8 Hz: a
                    // fire really does have structure turning over that fast —
                    // just not structure that size. This is the term that has the
                    // right size. The atlas cells are torn silhouettes with holes
                    // two to five centimetres across on a 30 cm card, so
                    // f = 1.5/sqrt(0.035) = 8.0 Hz is exactly the band they want,
                    // and the displacement is under two centimetres of texture:
                    // small, fast and near, which is what the eye reads as fire
                    // being alive, as against large, fast and far, which is what
                    // it reads as a twitch.
                    //
                    // It TRAVELS UP THE CARD (the -i.uv.y terms), because that is
                    // the one direction the gas is going, and it is zero at the
                    // base for the same reason the wander is: the sheet is
                    // anchored where it is fed. The vertical half is deliberately
                    // the smaller: `a` below is a saturate()d lookup, so a
                    // vertical offset clamps rather than wraps, and 1.1 % is
                    // inside the fade every atlas cell already has over its
                    // bottom 10-12 % (MakeFireAtlas) — nothing that reaches the
                    // clamp has any alpha left to smear.
                    //
                    // The pre-frac'd per-card phase arrives in i.pr.w; see v2f.
                    //
                    // GhvrWave4's arithmetic on TWO lanes instead of four, and
                    // written out rather than called with two zeros. This is a
                    // fragment on a stack of large additive cards — the one place
                    // in the room with heavy overdraw — and on the tile GPUs this
                    // ships to a float4 of which half is discarded is half of the
                    // work discarded, per pixel, per layer. It is the same
                    // function to the last bit (see EnvGrowth.cginc): a triangle
                    // wave of period 1 put through 3v^2-2v^3 and mapped to
                    // [-1,1]. If that ever changes there, this changes with it.
                    float2 rv = abs(frac(float2(i.pr.w - i.uv.y * 0.62,
                                                i.pr.w + 0.41 - i.uv.y * 0.28)
                                         + 0.5) * 2.0 - 1.0);
                    float2 rip = (rv * rv * (3.0 - 2.0 * rv) - 0.5) * 2.0;
                    uv += float2(rip.x * 0.018, rip.y * 0.011) * i.uv.y;
                    // THE ATLAS. ROUND FIRST — `cell` is a small integer that has
                    // been through a perspective-correct interpolator and that is
                    // not exact; floor(1.9999998) is 1 and the card would be drawn
                    // with its neighbour's shape. (The same last-bit trap cost
                    // this project a review round in EnvHaunt's atlas; the fix is
                    // one add.)
                    float ci = floor(i.fx.z + 0.5);
                    float2 cel = float2(fmod(ci, GHVR_FIRE_COLS), floor(ci / GHVR_FIRE_COLS));
                    float2 a = saturate(uv) * (1.0 - 2.0 * GHVR_FIRE_INSET) + GHVR_FIRE_INSET;
                    c = tex2D(_MainTex, (a + cel) * GHVR_FIRE_CELL);
                    c.a *= i.fx.w;      // a detached puff is born and dies
                }
                else
                {
                    c = tex2D(_MainTex, uv);
                }
                c *= _Tint;
                // ELEMENT ART: under Fire the core burns toward white while the
                // edge keeps the candle's own amber — a hotter flame, not a
                // recoloured one. i.uv.y is the height up the sprite, so the
                // whitening is strongest at the base where a real flame is
                // hottest. Exactly 0 when Fire is 0.
                c.rgb = lerp(c.rgb, c.rgb * float3(1.16, 1.06, 0.82),
                             saturate(i.fire * (1.0 - i.uv.y * 0.6)));
                // FIRE REAL — the vertical temperature ramp, THREE stops and
                // measured up the whole FIRE rather than up this card (see
                // _FireH). A candle flame is one colour because it is 2 cm tall;
                // a burning crate is white-blue where it is fed, orange through
                // the body and dark red where the tongues tear off, and a piece
                // that has detached is cooling all the way down. That gradient is
                // most of what makes a thing read as BURNING rather than as a
                // warm sprite — and the previous two attempts had no white in
                // them anywhere, which is why they read as light the colour of
                // fire. Bonfire materials only; the candles keep their _Tint.
                if (_Bonfire > 0.5)
                {
                    // FIRE+EARTH: a smouldering fire is COOLER all the way up —
                    // the same ramp, entered further along, so the white-hot band
                    // shrinks toward nothing and the body is already at the tip
                    // colour. `age` is what the ramp already uses to cool a piece
                    // that has detached, so adding to it is a modulation of an
                    // existing parameter rather than a second colour path.
                    // FIRE+ICE does a little of the same, and for the same
                    // physical reason: something is taking heat out of the flame.
                    float cool = i.fx.y + 0.42 * i.pr.z + 0.18 * i.pr.y;
                    float3 fc = GhvrFireRamp(_BaseCol.rgb, _CoreCol.rgb, _TipCol.rgb,
                                             i.fx.x, cool);
                    // ...and WHAT a detached piece is made of. Both of these are
                    // reached only when the piece is old (it has left the flame)
                    // AND both of the pair's elements are up, so an ordinary
                    // ember is untouched. Additive is the right blend for both:
                    // moonlit smoke and steam are things that SCATTER light
                    // toward you, and neither may occlude — see the report for
                    // the one thing that costs (a smoke plume cannot darken what
                    // is behind it in a single additive pass).
                    float t2 = saturate(i.fx.y * 1.6);
                    fc = lerp(fc, _SmokeCol.rgb, saturate(i.pr.x * t2));
                    fc = lerp(fc, _SteamCol.rgb, saturate(i.pr.y * t2));
                    c.rgb *= fc;
                }
                c.rgb *= c.a * i.fl; // premodulate: alpha drives additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
