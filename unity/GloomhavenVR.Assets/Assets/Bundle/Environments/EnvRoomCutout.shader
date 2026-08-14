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
        _SwayDir ("Sway direction (OBJECT space)", Vector) = (0,0,1,0)

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
        _ElemMoss ("Element: earth moss susceptibility", Range(0,2)) = 0
        _ElemGrowFreq ("Element: growth cells per metre", Float) = 3.0
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
            float _ElemRad, _ElemScl, _ElemFrost, _ElemMoss, _ElemGrowFreq, _ElemGrow;
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
                float s = (sin(st) * 0.62 + sin(st * 1.73 + 2.1) * 0.38)
                          * _Sway * v.color.r;

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
                if (e.live > 0.0) s *= 1.0 + 1.20 * e.air;
                p.xyz += _SwayDir.xyz * s;

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
                // Frost on the leaves, and the deeper green Earth puts on a
                // living plant. A card is not a wall, so the affinity is
                // simpler: there is no wall foot and no mortar course, and what
                // a frond knows about itself is only how high it is and which
                // way it faces. `place` therefore leans on the sky term (frost
                // settles on top of a fern, not under it) and, for moss, on
                // being LOW, which is what separates the ground moss from the
                // canopy 7 m up.
                GhvrElem e = GhvrHauntElems();
                float eLive = _GhvrElemB.w * _GhvrElemB.z;
                float frost = 0.0, moss = 0.0, mthk = 0.0;
                if (eLive > 0.0 && (_ElemFrost > 0.0 || _ElemMoss > 0.0))
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
                    // MOSS REAL — the same surface treatment the walls and the
                    // floor get (EnvGrowth.cginc), and a card wants it more than
                    // either: a fern has no normal map at all, so before this
                    // round a mossed frond was a FLAT green shape on a flat
                    // green shape, which is "grüne Flecken" in its purest form.
                    // Here the moss's own relief is the only relief there is.
                    float ea = e.earth * _ElemMoss;
                    float4 mrel = float4(0, 0, 0, 0);
                    if (ea > 0.0)
                    {
                        float mfld = GhvrGrowField(q + 37.1);
                        moss = GhvrGrow(GhvrGrowA(mfld, grain,
                                                  saturate(0.62 * low + 0.38 * sky)),
                                        ea * (0.20 + 1.55 * rr), -creep);
                        mrel = GhvrMossRelief(q, mfld);
                        mthk = GhvrMossThick(moss, mfld, grain, mrel.x);
                    }
                    float lum = GhvrGrowLum(alb.rgb);
                    alb.rgb = GhvrFrostOn(alb.rgb, lum, frost);
                    alb.rgb = GhvrMossOn(alb.rgb, lum, moss, mthk, mrel.x);
                    n_ts.xy -= float2(dot(mrel.yzw, i.t), dot(mrel.yzw, i.b)) * mthk;
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
                float ambGain = 1.0, dirGain = 1.0;
                if (eLive > 0.0)
                {
                    ambGain = GhvrAmbGain(e);
                    dirGain = GhvrDirGain(e) * GhvrMoonLight();
                }

                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5)
                               * (ambGain + 0.30 * frost - 0.12 * mthk);
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
