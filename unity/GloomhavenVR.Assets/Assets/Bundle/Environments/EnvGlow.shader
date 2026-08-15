// GloomhavenVR — soft light-glow shader for small emissive spheres around flames and
// window light. A real world-space sphere whose brightness falls off toward its
// silhouette (dot(N,V) falloff): reads as a volumetric halo from EVERY direction and
// in stereo, with no billboarding and nothing attached to the camera. Additive,
// no depth write.
//
// ---- ModBuild 148: THE HALO WAS NEVER DRAWN ---------------------------------
// USER, ModBuild 147: "Es sind mehrere sichtbare 'Striche' auf den assets
// drauf", with feuer1.jpg — thin lines through the fires, and a stepped dark-red
// block in the wood's misty gap, both only under Fire.
//
// Env_GlowSphere was WOUND INSIDE OUT (BuildSphere's "outward" order emits faces
// whose normal points at the centre). Under this shader's `Cull Back` the near
// hemisphere was culled and the FAR one rasterised, and on the far shell the
// outward vertex normal points away from the eye — so `dot(N,V)` was negative
// across the entire disc and `core` was exactly 0. Every halo in both rooms drew
// NOTHING but a one-pixel seam at the geometric limb, and that limb is the
// silhouette of a 16x8 UV sphere: straight segments, near-vertical along the
// meridian edges, stepped over the poles. The Striche ARE the halo's outline.
// Measured, FireSnagWide under full Fire: the halo's whole contribution to the
// frame was a single column of 23/255 with 0 on either side of it.
//
// Two things changed and they are one fix:
//   * the shell is built by BuildGlowSphere, wound to be seen from OUTSIDE and
//     circumscribing the unit sphere, behind a gate that is proven to reject the
//     winding that shipped;
//   * the falloff is solved ANALYTICALLY against that unit sphere instead of off
//     the interpolated normal (see THE FALLOFF, SOLVED ON THE SPHERE ITSELF),
//     which is the same expression on an infinitely fine sphere and therefore
//     has neither the Mach band at every triangle edge nor the hard stepped rim
//     a coarse shell's limb leaves behind.
// Vertex and triangle counts are unchanged; the rasterised hull grows 7.9 % in
// area, all of it in an annulus the falloff takes to zero.
Shader "GloomhavenVR/EnvGlow"
{
    Properties
    {
        _Tint ("Glow color (alpha = strength)", Color) = (1,0.6,0.2,0.5)
        _Falloff ("Edge falloff", Range(0.5,8)) = 2.5
        // A candle's HALO must breathe with the candle. All of the following
        // default to 0/1 => every existing glow (the forest's wisps, lantern and
        // eyes) is bit-identical without them.
        _Flicker ("Brightness flicker", Range(0,1)) = 0
        _Rate ("Flicker rate (match the light slot)", Float) = 1
        _Phase ("Phase offset", Float) = 0
        // Blink + absence, for eyes in the dark. _Blink closes them briefly every
        // _BlinkPeriod; _Away removes them for most of _AwayPeriod, so the pair
        // in the corner is not a permanent fixture you stop noticing.
        _Blink ("Blink depth", Range(0,1)) = 0
        _BlinkPeriod ("Blink period (s)", Float) = 4.7
        _Away ("Absence depth", Range(0,1)) = 0
        _AwayPeriod ("Absence period (s)", Float) = 26

        // ---- RETROREFLECTION (ModBuild 148) ---------------------------------
        // USER, ModBuild 148: "Bei der 'Fratze' im Wald erscheinen einfach so
        // zwei Tennisbälle. Nicht sehr viel Horror." He is right, and the reason
        // is physics rather than tuning.
        //
        // AN EYESHINE IS NOT A LAMP. It is a RETROREFLECTION: light goes in
        // through the pupil, bounces off the tapetum lucidum and comes back out
        // in a NARROW LOBE about the direction it arrived from. You see a deer's
        // eyes from behind your own torch and from nowhere else; step two paces
        // off the beam and they are gone. A sphere of constant brightness has no
        // lobe at all, and a thing that is equally bright from every direction
        // is not an eye, it is a ball — which is exactly the word he used.
        //
        // THERE IS NO GEOMETRIC WAY TO DO THIS. The projected area of ANY convex
        // shape falls off no faster than cos(theta): a disc at 20 deg off axis
        // still shows 94 % of its face, so no amount of flattening a bead makes a
        // narrow lobe. It has to be a term, and this is the term.
        //
        //   _Shine      0 = off (every other halo in both rooms), 1 = full lobe.
        //   _ShineAxis  xyz = the axis the lobe points down, in OBJECT space —
        //               i.e. from the eye toward the light-and-viewer. w = the
        //               exponent: cos^w, so w = 12 gives a half-brightness
        //               half-angle of 19.4 deg and a quarter at 27.7 deg.
        //
        // IT IS NOT A BILLBOARD AND NOTHING RE-ORIENTS. The geometry is the same
        // world-fixed sphere it always was; what varies with the eye is a
        // BRIGHTNESS, which is what every specular highlight in every renderer
        // does. The stereo cost is the disparity of the lobe across a 63 mm IPD
        // at 8-13 m, which is 0.3-0.45 deg out of a 19 deg half-angle, i.e. under
        // 2 % of brightness between the eyes — three orders below the masonry
        // dissolve that this project has already ruled unshippable.
        _Shine ("Retroreflective lobe (0 = off)", Range(0,1)) = 0
        _ShineAxis ("Shine axis (OBJECT space, w = cos exponent)", Vector) = (0,0,1,12)

        // ---- A HALO THAT ONLY EXISTS DURING A HAUNT EVENT --------------------
        // The wood's eyeshines are an EVENT (forest card 0): they open, hold,
        // blink and are gone. They used to be alpha-blended solids in the haunt
        // mesh, and an alpha-blended solid is PAINT — it can never be brighter
        // than the colour written into its vertices, which is why a 0.17 key came
        // out as a flat grey-yellow disc. Light is ADDITIVE, so an eyeshine
        // belongs in this shader and not in that mesh.
        //
        // The schedule is read through GhvrHauntPresence — the same call, on the
        // same two authored vectors, that the moonbeam uses to dim for the thing
        // at the window and the cobwebs use to shiver. There is no second
        // schedule and no second envelope: EnvHaunt.cginc is included already
        // (via EnvShelfTip.cginc), so this is one function call.
        //   _HauntSched  (slot period s, cards in the room, this card, 1 = ON)
        //   _HauntEnv    (reveal, hold, fade) — the card's authored envelope
        // w = 0 (the default, and every halo that shipped before) skips the
        // branch entirely, so their arithmetic is untouched to the bit.
        _HauntSched ("Haunt gate (period, cards, card, on)", Vector) = (0,0,0,0)
        _HauntEnv ("Haunt envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        // ELEMENT ART (EnvElement.cginc). A halo IS a source, so this is where
        // the Light/Dark split is most visible: Light makes every halo bigger and
        // brighter, Dark does not extinguish them but pulls them in — the falloff
        // exponent rises, so the glow stops being a soft veil and becomes a hard
        // little core. A black room with hard bright points in it is the split.
        // _ElemWarm (default 1) lets Fire swell the warm halos; the forest's cold
        // wisps and the rat's eyes are built with less of it, because a wisp that
        // turns orange is not a wisp any more.
        _ElemWarm ("Element: fire susceptibility", Range(0,2)) = 1

        // REAL FIRE — the gate. The cellar's burning crates, barrels and shelf
        // (EnvFlame's _FireGate) each carry a halo, and a halo for a fire that
        // is not burning must not exist at all: with the gate on and Fire down
        // the sphere is COLLAPSED to a point in the vertex shader, exactly as
        // the gated flame cards and the gated particle emitters are. The default
        // 0 is every halo that shipped before — the candles, the window, the
        // wisps and the rat's eyes — and they are untouched.
        //
        // WHY A HALO AT ALL, and what it is standing in for: EnvRoom's baked
        // light rig has exactly THREE point slots and all three are candles, so
        // a fourth source cannot be added without that shader (a different lane
        // this round). The halo is therefore the fire's light: a real world-space
        // volume that brightens the air around the fire and washes the wall it
        // stands against. It is not a substitute for a surface light and it is
        // not pretending to be one — see BuildEnvironmentRooms' FIRE LIGHT note.
        _ElemGate ("Element gate: 1 = exists only under Fire", Range(0,1)) = 0

        // ---- THE CANDLE FLAG (ModBuild 146) --------------------------------
        // TWO STANDING USER RULINGS, both verbatim, both about the cellar:
        //     "anstatt die Kerzenscheine, die sollte identisch beiben."
        //     "Auch bei Dunkelheit sollte es keinen Einfluss auf den Kerzenschein
        //      haben."
        // i.e. Light and Dark may not touch a candle's glow, in either
        // direction. The shading lane closed that for every reader of the
        // element SOURCE GAIN and the flame sprite is closed in EnvFlame — this
        // is the last hole, and it was in this shader's `fall`: the halo's edge
        // still tightened under Dark and opened out under Light.
        //
        // IT CANNOT BE GATED ON "INDOORS", which is why it is a per-material
        // flag and not GhvrIndoor(). This one shader serves three different
        // things through one path:
        //   * the CANDLE halos — frozen against Light and Dark (the rulings),
        //   * the FIRE halos   — must keep responding; they are fire and no
        //                        ruling touches them,
        //   * the WINDOW glow  — must respond to Light, because it IS moonlight
        //                        and this round is about Light lifting the moon.
        // "Indoors" is true of all three.
        //
        // THE DEFAULT 0 IS THE SAFETY PROPERTY: a material that never writes
        // this flag — every halo that shipped before, and anything a future
        // round adds without knowing about it — multiplies the light/dark terms
        // by exactly 1.0, and multiplication by 1.0 is exact in IEEE-754 with no
        // change to the expression's association. The zero state is therefore
        // bit-identical to the arithmetic this shader had before the flag
        // existed, not merely close to it.
        _ElemCandle ("Element: 1 = a candle halo, frozen against Light/Dark", Range(0,1)) = 0

        // ---- THE MOONLIGHT FLAG (ModBuild 149) -----------------------------
        // USER, verbatim, with Kellerfenster_Dunkel.jpg: "Bei Dunkelheit im
        // Keller über dem Kellerfenster ist noch so etwas helles zu sehen
        // entferne das." An otherwise black frame with a soft blue oval in it.
        //
        // THE CAUSE IS NOT `ld`. For C_GlowMoon _ElemCandle is 0, so ld is 1 and
        // both Dark terms below apply IN FULL — they are simply not a collapse.
        // fall goes 2.0 -> 4.3, which tightens a sphere that is squashed to 0.22
        // in z and therefore hardly changes what a head-on view of it covers, and
        // elemAmp goes to GhvrSrcGain(e) - 0.35, i.e. 0.65. The 0.35 is the ONLY
        // Dark subtraction an indoor halo has, because GhvrSrcGain is EXACTLY 1.0
        // indoors at every element state (EnvElement.cginc:304-307, the candle
        // ruling). Measured on the ModBuild 149 bake, env_cellar_Window_edarkS
        // against _ebase over the aperture: mean (19.6, 28.2, 49.9) -> (8.4,
        // 13.6, 27.0), i.e. 54 % of it survives full Dark in a room where every
        // other moonlight term is down to 0.0275x.
        //
        // AND THAT IS THE BUG, stated properly: the aperture glow IS MOONLIGHT —
        // it is the air in the hole lit by the moon — and it was the one
        // moonlight term in the cellar not riding the moon. EnvElement publishes
        // the contract for exactly this ("Multiply any MOONLIGHT term by this",
        // GhvrMoonLight()), and the beam, the pool, the puddle's mirror and the
        // cold rims all already do. So this flag does not invent a Dark response;
        // it puts this halo back on the one the room already has, and at full
        // Dark the eclipse floor (0.05) takes it to 0.0325 of its rest value —
        // 0.6/255 in its own strongest channel, i.e. below what an 8-bit frame
        // can hold.
        //
        // IT DOES NOT TOUCH THE LIGHT SIDE beyond the moon's own swell (x1.34 at
        // full Light, GHVR_MOON_SWELL): lifting the aperture with GhvrDirGain as
        // well is the "Light must brighten the moonlight from the window" work
        // and it belongs to the lane doing it, not to a Dark bug fix. _ElemCandle
        // stays 0 on that material, so Light still opens the halo's edge exactly
        // as it did.
        //
        // WHY A FLAG AND NOT "every indoor non-candle halo": the cellar's FIRE
        // halos are also indoor non-candles, and Fire+Dark deliberately makes
        // them BIGGER and brighter (the pairing block below). A blanket indoor
        // rule would extinguish the one halo in the room that is supposed to be
        // the only light left. Default 0 multiplies by a literal 1.0, so every
        // material that never writes it is bit-identical.
        _ElemMoon ("Element: 1 = this halo is moonlight, rides GhvrMoonLight", Range(0,1)) = 0

        // ---- SHELF RIDERS (EnvShelfTip.cginc) -------------------------------
        // USER, ModBuild 144: "Die Kerzen und das Feuer, die auf dem Bücherregal
        // stehen, kippen nicht mit". Two of this shader's spheres stand on that
        // shelf — the shelf candle's halo and the near halo of the fire seated on
        // its top boards — and a halo that stays where the candle WAS is worse
        // than no halo at all, because it is a light with nothing in it.
        //
        // A halo is a real world-space sphere and the pose is a rigid transform,
        // so the whole sphere simply goes with the flame: the vertex path is one
        // GhvrTipRot behind one uniform compare. The candle's halo also DIES with
        // the candle, on the same GhvrTipFlameLife the flame and the baked light
        // slot take, which is the only way the three can agree about a candle
        // being out. Zero on every other glow in both rooms.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, flame stiffness", Vector) = (0,-1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            #include "EnvShelfTip.cginc"
            // ...and the five Fire pairings, for the GATED halos only: a halo on
            // a burning object is part of that fire and has to answer the same
            // combinations it does. See the PAIRINGS block in EnvFire.cginc for
            // the composition rule.
            #include "EnvFire.cginc"

            fixed4 _Tint;
            float _Falloff, _Flicker, _Rate, _Phase, _Blink, _BlinkPeriod, _Away, _AwayPeriod, _ElemWarm;
            float _ElemGate, _ElemCandle, _ElemMoon, _Shine;
            float4 _ShineAxis, _HauntSched, _HauntEnv;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; };
            // SPHERE SPACE — the frame the falloff is solved in. Origin at the
            // halo's centre, unit radius, axes the object's own (so a squashed
            // halo like the window's stays squashed). `sp` is the rasterised hull
            // point in it and `sc` the eye; `sc` is the same value at every
            // vertex, so its interpolation is exact.
            struct v2f { float4 pos : SV_POSITION; float3 sp : TEXCOORD0; float3 sc : TEXCOORD1;
                         // SHELF RIDERS: the flame's life, so a halo goes out with
                         // the candle it belongs to. NEGATIVE means "not riding",
                         // and it is a sentinel rather than a neutral 1 for the
                         // reason spelled out at GhvrTipLight: an interpolated
                         // constant is not the constant, and every other halo in
                         // both rooms has to stay bit-identical.
                         float life : TEXCOORD2;
                         // HAUNT-GATED halos: the card's envelope, solved once in
                         // the vertex shader. Written to a literal 1 for every
                         // other halo and READ ONLY behind the same uniform
                         // branch that wrote it — an interpolated constant is not
                         // the constant, and the zero state has to be exact.
                         float pres : TEXCOORD3; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pres = 1.0;
                // A HALO ON A HAUNT SLOT exists only while its card is running.
                // Collapsed, not merely transparent, for the same reason the
                // element gate collapses: an invisible sphere still costs the
                // fill of every pixel it covers, twice, under MultiPass.
                if (_HauntSched.w > 0.5)
                {
                    o.pres = GhvrHauntPresence(_Time.y + _GhvrTimeOfs,
                                               _HauntSched.x, _HauntSched.y, _HauntSched.z,
                                               _HauntEnv.x, _HauntEnv.y, _HauntEnv.z);
                    if (o.pres <= 1e-4)
                    {
                        o.pos = float4(0, 0, 0, 1);
                        o.sp = float3(0, 0, 2); o.sc = float3(0, 0, 4); o.life = -1;
                        return o;
                    }
                }
                // REAL FIRE: collapse a gated halo whose element is down. Not an
                // alpha of zero — that would still cost the fill of a sphere that
                // covers a good part of the frame from close to.
                if (_ElemGate > 0.5 && GhvrElems().fire <= 0.0)
                {
                    o.pos = float4(0, 0, 0, 1);
                    o.sp = float3(0, 0, 2); o.sc = float3(0, 0, 4); o.life = -1;
                    return o;
                }
                // SHELF RIDERS. A halo is a sphere of light around a flame, so
                // it takes the flame's rigid transform whole — there is nothing
                // about a sphere to bend. One uniform compare when the shelf is
                // standing, which is always outside the event.
                //
                // The CENTRE takes the same transform as the hull, and that is
                // what makes the analytic falloff below survive the topple: the
                // sphere the fragment shader solves against is the rotated one,
                // not the one the mesh was authored at.
                float3 p = v.vertex.xyz;
                float3 cen = float3(0, 0, 0);
                o.life = -1;
                GhvrTip tip = GhvrTipNow(_Time.y + _GhvrTimeOfs);
                if (tip.live > 0.5 && _TipUse.x > 0.5)
                {
                    p   = GhvrTipRot(p,   tip.pivot, tip.axis, tip.ang);
                    cen = GhvrTipRot(cen, tip.pivot, tip.axis, tip.ang);
                    if (_TipUse.z > 0.5)
                    {
                        float life = GhvrTipFlameLife(tip);
                        o.life = life * GhvrTipRelightFlare(tip, life);
                    }
                }
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                o.sp  = p - cen;
                o.sc  = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz - cen;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {

                // ---- ELEMENT ART: the halo's size, colour and edge -----------
                GhvrElem e = GhvrElems();
                float fall = _Falloff, elemAmp = 1.0;
                float3 elemCol = _Tint.rgb;
                if (e.live > 0.0)
                {
                    float warm = e.fire * _ElemWarm;
                    // Dark tightens the edge (the corners swallow light), Light
                    // opens it out; Fire swells the warm halos a little.
                    // `ld` is 1 for everything that is not a candle halo, and
                    // 0 for the ones that are (see _ElemCandle). It multiplies
                    // the Light and Dark terms ONLY: Fire still swells a candle's
                    // halo, because no ruling says otherwise and a candle in a
                    // burning room is in a burning room.
                    float ld = 1.0 - _ElemCandle;
                    fall = max(_Falloff * (1.0 + 1.15 * e.dark * ld - 0.30 * e.light * ld - 0.25 * warm), 0.30);
                    elemAmp = max(GhvrSrcGain(e) + 0.85 * warm - 0.35 * e.dark * ld * (1.0 - e.light), 0.0);
                    // A HALO THAT IS MOONLIGHT rides the moon (see THE MOONLIGHT
                    // FLAG). lerp rather than a branch: with _ElemMoon at 0 this
                    // is a multiplication by a literal 1.0, which is exact, and
                    // the whole line already sits inside `if (e.live > 0.0)` so
                    // the zero state does not run it at all.
                    elemAmp *= lerp(1.0, GhvrMoonLight(), _ElemMoon);
                    // a fire-fed halo goes ember; nothing else recolours it
                    elemCol = lerp(_Tint.rgb, float3(1.00, 0.46, 0.14), saturate(warm * 0.85));
                }
                // ---- THE FALLOFF, SOLVED ON THE SPHERE ITSELF ----------------
                // dot(N,V) at the near intersection of a ray with a unit sphere
                // is EXACTLY sqrt(1 - d^2), where d is the ray's perpendicular
                // distance from the centre — independent of how far away the eye
                // is, and independent of how the shell happens to be tessellated.
                // Taking it analytically is therefore not an approximation of the
                // old expression: it is the same expression evaluated on an
                // infinitely fine sphere, which is the shape the shader has
                // always claimed to be shading.
                //
                // WHY IT MATTERS HERE. Off the interpolated normal, a 16x8 shell
                // has a C1 crack at every triangle edge (a Mach band along every
                // meridian and parallel) and, worse, its limb sits INSIDE the true
                // limb: the halo would stop at pow(sin(11.25 deg), fall) instead of
                // at zero, which is a hard stepped rim. Both are gone by
                // construction. The mesh's only remaining duty is to cover the
                // true silhouette, and BuildGlowSphere's gate enforces exactly
                // that.
                float3 rd = i.sp - i.sc;                    // eye -> fragment
                float  rr = max(dot(rd, rd), 1e-12);
                float3 q  = i.sc - rd * (dot(i.sc, rd) / rr);// closest point on the ray
                float  core = pow(sqrt(saturate(1.0 - dot(q, q))), fall);

                float t = _Time.y + _GhvrTimeOfs;
                float ft = t * _Rate;
                float f = 0.42 * sin(ft * 11.3 + _Phase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                float amp = 1.0 + _Flicker * 0.35 * f;

                // lid: shut for ~2.4% of the period, centred on its half
                float bp = frac(t / max(_BlinkPeriod, 0.01) + _Phase * 0.11);
                amp *= 1.0 - _Blink * (1.0 - smoothstep(0.0, 0.012, abs(bp - 0.5)));
                // absence: present for the first ~45% of the long cycle only
                float ap = frac(t / max(_AwayPeriod, 0.01) + _Phase * 0.037);
                float present = smoothstep(0.02, 0.10, ap) * smoothstep(0.47, 0.38, ap);
                amp *= lerp(1.0, present, _Away);

                // REAL FIRE: a gated halo RAMPS with its element rather than
                // snapping on. ElementMood smooths over about a second and its
                // waning plateau breathes 0.28..0.52, so a fire whose light
                // arrived at full strength would throw away the one cue the
                // smoothing exists to give — that the infusion is going out.
                // sqrt for the same reason EnvFlame's gate takes it: the waning
                // plateau is 0.40 and a fire that is dying back still lights the
                // wall it stands against. Zero at zero, so nothing pops in.
                if (_ElemGate > 0.5)
                {
                    amp *= sqrt(saturate(e.fire));
                    // ---- THE FIVE FIRE PAIRINGS, on the halo. Every one of them
                    // modulates a number this shader already owns — the
                    // amplitude, the flicker rate, the edge and the colour — so
                    // a halo under Fire+Air+Dark is ONE halo that is windblown
                    // and alone. All exactly zero unless both elements are up.
                    GhvrFirePair p = GhvrFirePairs(e);
                    // FIRE+DARK: it is the only light there is, so the air around
                    // it glows harder and the edge opens out (a bright source in
                    // a black room has a bigger visible halo — that is what a
                    // halo IS). This is the counterweight to _Falloff's own Dark
                    // term above, which tightens every OTHER glow in the room:
                    // the split is "hard little points everywhere, except where
                    // something is actually burning".
                    fall = max(fall * (1.0 - 0.45 * p.dark), 0.30);
                    amp *= 1.0 + 1.35 * p.dark;
                    // FIRE+AIR: the halo breathes at the flame's rate, and the
                    // flame's rate has gone up — so this has to as well, or the
                    // one thing the whole feature is built around (a flame and
                    // the light it casts share a rate) breaks under wind. The
                    // rate itself is applied above, so what is left here is the
                    // DEPTH, which is the same +70% GhvrFireDepth gives the wash.
                    amp = 1.0 + (amp - 1.0) * (1.0 + 0.70 * p.air);
                    // FIRE+EARTH: a smoulder glows more than it flames — MORE
                    // halo, redder, and tighter to the seat.
                    elemCol = lerp(elemCol, float3(0.86, 0.20, 0.05), saturate(p.earth * 0.9));
                    amp *= 1.0 + 0.55 * p.earth;
                    fall = fall * (1.0 + 0.60 * p.earth);
                    // FIRE+ICE: steam. The air around the fire is full of it, so
                    // the halo is BIGGER and much less warm — a fire seen through
                    // its own steam has a pale corona, not an amber one.
                    elemCol = lerp(elemCol, float3(0.72, 0.80, 0.92), saturate(p.ice * 0.75));
                    fall = max(fall * (1.0 - 0.35 * p.ice), 0.30);
                    amp *= 1.0 + 0.40 * p.ice;
                    // FIRE+LIGHT: nothing. The plume is the pairing (EnvFlame),
                    // and a halo added on top of a moonlit smoke column would be
                    // the second visual layer the composition rule forbids.
                }

                // ---- THE RETROREFLECTIVE LOBE -------------------------------
                // `i.sc` is the eye in SPHERE SPACE, i.e. the vector from this
                // halo's own centre to the camera — the same value at every
                // vertex, so its interpolation is exact and the lobe is one
                // number for the whole bead. dot() it against the authored axis
                // and the shine only fires when the eye is near the axis the
                // light arrives down. Nothing here moves a vertex.
                if (_Shine > 0.001)
                {
                    float3 vd = normalize(i.sc);
                    float lobe = pow(saturate(dot(vd, normalize(_ShineAxis.xyz))),
                                     max(_ShineAxis.w, 1.0));
                    amp *= lerp(1.0, lobe, _Shine);
                }

                // HAUNT-GATED: the card's envelope, computed in the vertex shader
                // (which is also where a halo outside its event was collapsed, so
                // this only ever multiplies by a value in (0,1]).
                if (_HauntSched.w > 0.5) amp *= i.pres;

                // SHELF RIDERS: untouched unless this halo belongs to a candle
                // that has just been tipped over — see the vertex shader.
                if (i.life >= 0.0) amp *= i.life;

                return fixed4(elemCol, core * _Tint.a * max(amp, 0.0) * elemAmp);
            }
            ENDCG
        }
    }
    Fallback Off
}
