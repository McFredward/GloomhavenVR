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
// A BURNING CRATE IS NOT A BIG CANDLE, and _Bonfire is the whole of that
// difference. A candle flame is ONE laminar teardrop that sways; a fire is a
// crowd of TONGUES that surge, tear off and die back at their own rates, each
// one leaning further out the further it stands from the seat of the fire. So a
// bonfire material is fed a mesh of many tongues (EnvRoomBuilder.FireMesh) whose
// VERTEX COLOUR carries the per-tongue variation:
//     COLOR.r  the tongue's own phase, 0..1 of a turn
//     COLOR.g  how hard it surges (the tallest tongues surge most)
//     COLOR.b  how far it stands from the seat, 0..1 — the lean and the tear
//     COLOR.a  its share of the fire's brightness
// None of that is read while _Bonfire is 0, which is what lets the room's three
// candle materials go on using a mesh that has no colour stream at all.
//
// THE GATE (_FireGate). Everything this shader draws for the burning cellar
// exists ONLY under the Fire infusion, and it has to cost nothing when Fire is
// down — so a gated flame COLLAPSES to a point in the vertex shader exactly as
// the gated particle emitters do (EnvParticleAdd's header): zero-area triangles,
// no fragment ever shaded, and the zero state is the same instructions on the
// same data rather than "close enough". _FireGate = 0 is the candles: they burn
// whatever the elements are doing, because they are the room.
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
        // you see and the light it casts drift apart. A gated FIRE has no
        // EnvRoom slot of its own (the room shader has exactly three and they
        // are the candles); its light is the EnvGlow halo the builder puts on
        // it, which is written with this same rate and phase — so the rule
        // holds, it is just a different lamp.
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
        _LickRate ("Bonfire: tongue surge rate", Float) = 1.0
        _Flare ("Bonfire: outward tear of the outer tongues", Range(0,0.5)) = 0.10
        _CoreCol ("Bonfire: colour at the seat of the fire", Color) = (1.30,0.98,0.60,1)
        _TipCol ("Bonfire: colour at the tips", Color) = (1.00,0.42,0.14,1)
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
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint, _CoreCol, _TipCol;
            float _Sway, _Flicker, _Phase, _Rate, _Gust, _AirGust;
            float _Bonfire, _FireGate, _Lick, _LickRate, _Flare;
            float4 _GustDir;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // COLOR is the per-tongue stream and is read ONLY inside the
            // `_Bonfire` branch (a uniform, so the candles never touch it). The
            // candle mesh has no colour channel at all; Unity supplies white for
            // a missing vertex stream, and white is never used here.
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed fl : TEXCOORD1;
                         fixed fire : TEXCOORD2; };

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
                        return o;
                    }
                }

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
                    bright = max(1.0 + 1.05 * e.fire + 0.55 * e.light - 0.45 * e.dark, 0.0);
                }
                o.fire = e.fire;

                // sway grows with height (uv.y=0 at flame base) — the tip dances
                float h = v.uv.y;
                float sway = (sin(ft * 5.7 + _Phase) * 0.6 + sin(ft * 9.3 + 1.3 + _Phase) * 0.4)
                             * _Sway * swayMul * h * h;
                // the draft: slow, shared, unphased (see _Gust)
                float g = sin(t * 0.37) * 0.62 + sin(t * 0.83 + 1.1) * 0.38;
                float4 p = v.vertex;

                // ---- REAL FIRE: what a crowd of tongues does that one does not
                float lick = 1.0;
                if (_Bonfire > 0.5)
                {
                    float tp = v.color.r * 6.2831853;
                    float lt = ft * _LickRate;
                    // TWO incommensurate waves on the tongue's OWN phase. This is
                    // the difference between a fire and a candle: the tongues do
                    // not flicker together, they take turns surging, so the
                    // silhouette of the fire is never the same twice. (Fast
                    // enough to read at 2-5 m — the surge, not the brightness, is
                    // what says "burning" at that distance.)
                    float surge = 0.62 * sin(lt * 2.9 + tp) + 0.38 * sin(lt * 4.7 + tp * 1.7 + 1.1);
                    lick = max(1.0 + _Lick * v.color.g * surge, 0.10);
                    // a tongue standing at the RIM of the fire is torn outward and
                    // dies sooner; COLOR.b is how far out it stands. Without this
                    // the tongues all point straight up and the fire reads as a
                    // bush.
                    float tear = _Flare * v.color.b * (0.55 + 0.45 * sin(lt * 3.3 + tp * 2.1));
                    p.x += p.x * tear * h;
                    p.z += p.z * tear * h;
                    // ...and every tongue wanders on its OWN phase. _Sway is one
                    // number per material, so without this the whole fire leans
                    // as a single bush and the tongues stay in the rows the mesh
                    // put them in — the second half of what made the first bake
                    // read as a row of candles. Weighted by COLOR.g, so the bed
                    // (which barely surges) also barely wanders: a bed of embers
                    // that slid about would read as a puddle of light.
                    float wob = _Sway * 1.9 * v.color.g * h * h;
                    p.x += sin(lt * 3.7 + tp * 1.3) * wob;
                    p.z += sin(lt * 4.3 + tp * 2.2 + 0.9) * wob;
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
                }

                // the flame grows from its WICK: the mesh's origin is the wick, so
                // scaling y about it lengthens the flame instead of lifting it off
                // the candle (CrossQuadMesh puts uv.y=0 at y=0). For a bonfire the
                // origin is the seat of the fire and the same argument holds.
                p.y *= tall * lick;
                p.x += sway + _GustDir.x * _Gust * gustMul * g * h * h;
                p.z += sway * 0.6 + _GustDir.z * _Gust * gustMul * g * h * h;
                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // brightness flicker — same sine family AND same rate as the
                // EnvRoom light slot this candle drives
                float f = 0.42 * sin(ft * 11.3 + _Phase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                // a flame that is bent by a draught also burns brighter
                o.fl = (1.0 + _Flicker * 0.35 * f) * (1.0 + 0.55 * _Gust * gustMul * abs(g)) * bright;
                if (_Bonfire > 0.5)
                {
                    // per-tongue share of the fire's energy, and the surge shows
                    // in the light as well as in the shape — a tongue that leaps
                    // is a tongue that is burning harder.
                    o.fl *= v.color.a * (0.55 + 0.45 * lick) * gate;
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // wick-anchored UV wobble: zero at base, grows toward the tip
                float t = (_Time.y + _GhvrTimeOfs) * _Rate;
                float wob = (sin(t * 13.1 + i.uv.y * 9.0 + _Phase)
                           + sin(t * 7.3 + 2.1 + _Phase)) * 0.012 * i.uv.y;
                fixed4 c = tex2D(_MainTex, i.uv + float2(wob, 0));
                c *= _Tint;
                // ELEMENT ART: under Fire the core burns toward white while the
                // edge keeps the candle's own amber — a hotter flame, not a
                // recoloured one. i.uv.y is the height up the sprite, so the
                // whitening is strongest at the base where a real flame is
                // hottest. Exactly 0 when Fire is 0.
                c.rgb = lerp(c.rgb, c.rgb * float3(1.16, 1.06, 0.82),
                             saturate(i.fire * (1.0 - i.uv.y * 0.6)));
                // REAL FIRE — the vertical temperature ramp. A candle flame is
                // one colour because it is 2 cm tall; a burning crate is white-
                // hot in the seat, orange at mid-height and dull red where the
                // tongues tear off, and that gradient is most of what makes a
                // thing read as BURNING rather than as a warm sprite. Bonfire
                // materials only — the candles keep their authored _Tint.
                if (_Bonfire > 0.5)
                    c.rgb *= lerp(_CoreCol.rgb, _TipCol.rgb, saturate(i.uv.y * 1.15));
                c.rgb *= c.a * i.fl; // premodulate: alpha drives additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
