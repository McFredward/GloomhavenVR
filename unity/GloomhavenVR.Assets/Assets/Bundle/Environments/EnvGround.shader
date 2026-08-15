// GloomhavenVR — swamp-ground shader: EnvRoom's baked-light model plus a
// two-texture-set blend driven by vertex color:
//   vcol.a   = blend factor between set 1 (mud) and set 2 (leaf litter),
//              painted by the heightfield generator (wet hollows = mud),
//   vcol.rgb = large-scale tint — carries the radial fade into darkness that
//              hides the 30 m ground rim (permanent rule: the edge of the
//              ground must vanish in night + fog, never show a horizon line).
// Same object-space light rig as EnvRoom (see there for the model).
Shader "GloomhavenVR/EnvGround"
{
    Properties
    {
        _MainTex ("Albedo A (mud)", 2D) = "white" {}
        _BumpMap ("Normal A", 2D) = "bump" {}
        _MainTex2 ("Albedo B (leaves)", 2D) = "white" {}
        _BumpMap2 ("Normal B", 2D) = "bump" {}
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
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        // How much of the directional (moon) term the GROUND takes. 1 = as every
        // other surface, which is what it was until ModBuild 136.
        // USER FINDING, ModBuild 135 (hardware): "Pass nochmal die
        // Lichtverhältnisse im Wald auf dem Boden an - der erscheint viel zu
        // hell bei den Lichtverältnissen. Er soll eher leicht angestrahlt werden
        // von Mond." The forest floor is very nearly horizontal, so N.L against
        // a moon at 40 deg altitude is ~0.64 EVERYWHERE — the one surface in the
        // room that is uniformly and fully lit, which is exactly why it read as
        // a lit floor instead of a floor a little moonlight falls on. This is
        // the knob for that, and it is on the GROUND only: turning the moon
        // itself down would flatten the trunk rim, which is the contrast recipe
        // ModBuild 134 spent a round building.
        _DirScale ("Directional (moon) response", Range(0,2)) = 1
        // CANOPY SHADOW — baked by BuildEnvironmentRooms.cs (CanopyShadowBake).
        // It multiplies the DIRECTIONAL term and nothing else, so it can only
        // ever subtract moonlight: the hemisphere ambient and all three point
        // lights are separate addends and are not in its reach. That matters
        // here more than anywhere, because the levels this shader carries were
        // hand-tuned to a user ruling (see _DirScale) and the landing pool the
        // board is read by is a POINT light. Nothing in this room gets brighter.
        //
        // Defaults are "no shadow map": a white map decodes to depth 1.0, which
        // is the bake's own "nothing here" sentinel, and _CsFlt.w = 0 takes the
        // whole term out.
        _CsMap ("Canopy shadow depth (R:G = 16-bit)", 2D) = "white" {}
        _CsOrg ("Light-plane origin (OBJECT space, w = 1/depth span)", Vector) = (0,0,0,0)
        _CsU ("Light-plane axis U (w = 1/extent)", Vector) = (1,0,0,0)
        _CsV ("Light-plane axis V (w = 1/extent)", Vector) = (0,1,0,0)
        _CsDir ("Light travel direction (w = -near depth)", Vector) = (0,-1,0,0)
        _CsFlt ("Penumbra u, penumbra v, depth bias, 1 - minimum visibility", Vector) = (0,0,0,0)
        _CsThrow ("Max throw, 1/release (encoded depth), bite lo, 1/bite span", Vector) = (0,0,0,0)
        // The crown mass (SHAFT MASS, ModBuild 142) — the second map, see the
        // block at CsFol below. "black" is "no crowns".
        _CsFol ("Canopy mass (R,G = near/far depth, B,A = their coverage)", 2D) = "black" {}
        _CsFolP ("Crown reach, 1/release, 1/onset (encoded depth), strength", Vector) = (0,0,0,0)

        // ---- ELEMENT ART / SURFACE GROWTH (EnvElement.cginc, EnvGrowth.cginc) ----
        // KNOWN GAP OF ModBuild 142, paid here. Earth and Ice reached the forest's
        // rocks, roots and deadfall (EnvRoom materials) and stopped dead at the
        // FLOOR, because this shader belonged to another lane that round — so the
        // user's "auch im Wald ... der Boden mit Moos bzw. Gras bewachsen" had
        // nowhere to land. The frame comes from the same ApplyRig write every
        // other lit material gets; the defaults are harmless and the feature is
        // off until the master is up.
        _ElemCentre ("Element: room centre (OBJECT space)", Vector) = (0,0,0,0)
        _ElemRad ("Element: room outer radius (object units)", Float) = 6
        _ElemScl ("Element: object units in metres", Float) = 1
        // _ElemMoss IS GONE — the painted moss was deleted on the user's fourth
        // rejection of it (see THE MOSS IS GONE in EnvGrowth.cginc). S_Ground.mat
        // still carries the value; nothing reads it. Earth reaches the forest
        // floor through the GRASS AND TUFT CARDS that stand up out of it
        // (EnvRoomCutout/_ElemGrow) and through nothing else.
        _ElemFrost ("Element: ice frost susceptibility", Range(0,2)) = 0
        _ElemGrowFreq ("Element: growth cells per metre", Float) = 3.0

        // ---- FIRE SEATS (the receiving half of the fire lane's contract) ----
        // Identical to EnvRoom's — see the block there. The floor is the other
        // surface a seated fire has to light, and in the wood it is the only
        // one: a fire in a clearing throws its wash on the ground around it and
        // on nothing else within ten metres.
        _FirePos0 ("Fire seat 0 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FirePos1 ("Fire seat 1 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FirePos2 ("Fire seat 2 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FireCol ("Fire wash colour (a = flicker depth)", Color) = (0,0,0,0)
        _FireRate ("Fire flicker rate (Hz)", Float) = 6
        // Nothing in the forest stands on the cellar's bookshelf, so this is
        // always zero here; it exists because the receiving term is shared with
        // EnvRoom and a shared term has one signature. See EnvFire.cginc.
        _FireRide ("Fire seats that ride the tipping shelf", Vector) = (0,0,0,0)
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, flame stiffness", Vector) = (0,-1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0        // four albedo/normal reads plus seven shadow taps
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            #include "EnvGrowth.cginc"
            // ...and the fire wash, which used to be a verbatim copy of
            // EnvRoom's in this file. See GhvrFireSeats.
            #include "EnvFire.cginc"

            float4 _ElemCentre;
            float _ElemRad, _ElemScl, _ElemFrost, _ElemGrowFreq;

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _BumpMap;
            sampler2D _MainTex2; float4 _MainTex2_ST;
            sampler2D _BumpMap2;
            float _BumpScale, _PtHard, _DirScale;
            fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col;
            float4 _DirDir, _L0Pos, _L1Pos, _L2Pos;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // ------------------------------------------------------ CANOPY SHADOW
            // An orthographic depth map of the trees along the moon bearing, baked
            // by BuildEnvironmentRooms.cs (CanopyShadowBake — read the block above
            // that class for WHY it stores a depth and not an occlusion mask).
            // R:G is a 16-bit linear depth measured DOWN-LIGHT from a plane just
            // in front of the tallest tree; 1.0 (white) means "no occluder", which
            // is why real depths only ever reach 0.98.
            //
            // Everything is in OBJECT space — the frame the ground mesh is built
            // in and the only one the runtime's placement yaw and scale cannot
            // move under it, exactly like _DirDir and _L0Pos above.
            //
            // Duplicated from EnvShaft.shader on purpose (see the note there):
            // the floor takes SIX taps where a blade of mist takes seven, because
            // the floor covers far more of the view and its shadow edge is
            // already softened by the normal map and the vertex fade.
            sampler2D _CsMap;
            sampler2D _CsFol;
            float4 _CsOrg, _CsU, _CsV, _CsDir, _CsFlt, _CsThrow, _CsFolP;

            float3 CsCoord (float3 op)
            {
                float3 r = op - _CsOrg.xyz;
                return float3(dot(r, _CsU.xyz) * _CsU.w + 0.5,
                              dot(r, _CsV.xyz) * _CsV.w + 0.5,
                              (dot(r, _CsDir.xyz) + _CsDir.w) * _CsOrg.w);
            }

            // Each 16-bit layer arrives as two bytes over 255, so a stored depth
            // is (hi*255*256 + lo*255) / 65535. R:G is the NEAREST occluder in
            // the texel and B:A the DEEPEST — one value cannot serve both
            // receivers, see the two-layer note in BuildEnvironmentRooms.cs.
            //
            // MAXIMUM THROW. d is how far DOWN-LIGHT of a stored occluder this
            // fragment lies; d <= 0 means the occluder is behind me and I am lit.
            // A physically exact test would stop there, and in this room it would
            // answer "shadowed" everywhere: the moon sits at 40 deg, so the ray
            // from the clearing floor to the moon spends the next 20-30 m inside
            // the wood, and a wood at night genuinely has no moonlight on its
            // floor. The clearing, the canopy tear and the three shafts are an
            // AUTHORED FICTION and it is the fiction the user approved. So an
            // occluder only casts for _CsThrow.x of depth and then releases
            // smoothly over 1/_CsThrow.y: the trunk a beam passes through shadows
            // it, the roof 25 m up-light does not. The builder derives both
            // numbers — see the MAXIMUM THROW block in BuildEnvironmentRooms.cs.
            float CsThrow (float d)
            {
                return step(0.0, d) * saturate((_CsThrow.x - d) * _CsThrow.y);
            }

            // The floor reads the FAR layer alone, and for a floor that is exact
            // rather than an approximation: the floor lies below everything in
            // its texel, so the deepest occluder is always the nearest one
            // up-light of it. Reading R:G here would store the canopy 20-30 m
            // overhead, put it past the throw, and report the floor lit while the
            // trunk 4 m away casts nothing — which is what the first bake did.
            float CsTap (float2 uv, float z)
            {
                float2 e = tex2D(_CsMap, uv).ba;
                float f = (e.x * 65280.0 + e.y * 255.0) * (1.0 / 65535.0);
                // 0 is "nothing here"; see the note in EnvShaft.shader
                return 1.0 - step(0.00002, f) * CsThrow(z - f);
            }

            // ------------------------------------------------------- SHAFT MASS
            // The crowns, in their own map (ModBuild 142 — the full argument is in
            // EnvShaft.shader, where the comb the user photographed actually
            // appeared). Two things changed for the FLOOR, and only two:
            //   * the crowns left _CsMap, so the bite ramp above is now looking at
            //     trunk silhouettes and nothing else, which is the only thing it
            //     was ever right for;
            //   * the crowns come back through here at a SEVENTH of the blades'
            //     strength — enough to put a slow, half-metre-scale unevenness
            //     under the canopy where the moon used to be mathematically flat,
            //     and far too little to darken a floor the user tuned by hand.
            // The floor was already refusing the crown wash: _CsThrow.z sat at 0.26
            // precisely to throw away the 0.1-0.25 dusting from the roof. This does
            // the same job honestly instead of by threshold, and it is measured —
            // Report() prints the lit-to-shadowed ratio over the whole clearing,
            // which is the number that ruling was made on.
            float CsFolThrow (float d)
            {
                float t = saturate(d * _CsFolP.z);
                return t * t * (3.0 - 2.0 * t) * saturate((_CsFolP.x - d) * _CsFolP.y);
            }

            // z is the BIASED depth, the same one the trunk taps compare against;
            // the builder's Visible() mirrors this exactly.
            float CsFol (float2 uv, float z)
            {
                // One BILINEAR fetch, no tap disc: the low-pass was done exactly at
                // bake time by a 4x4 box filter, and a coverage may be interpolated
                // where a depth may not. An empty texel has zero coverage in both
                // layers, so no sentinel gate is needed.
                float4 e = tex2D(_CsFol, uv);
                return max(e.b * CsFolThrow(z - e.r),
                           e.a * CsFolThrow(z - e.g));
            }

            float CsVisible (float3 sc)
            {
                float z = sc.z - _CsFlt.z;
                float2 f = _CsFlt.xy;
                // PERCENTAGE-CLOSER filtering: compare first, average after. The
                // map is point-sampled on purpose — bilinear interpolation of a
                // DEPTH blends a trunk against the open sky beside it and invents
                // an occluder halfway between the two. Six taps on a small disc
                // (0.18 m radius in the wood, 3-4 texels) give a trunk's shadow a
                // soft rim instead of the map's own grid; a single tap is a stencil.
                float v = CsTap(sc.xy, z)
                        + CsTap(sc.xy + float2( 0.951,  0.309) * f, z)
                        + CsTap(sc.xy + float2( 0.000,  1.000) * f, z)
                        + CsTap(sc.xy + float2(-0.951,  0.309) * f, z)
                        + CsTap(sc.xy + float2(-0.588, -0.809) * f, z)
                        + CsTap(sc.xy + float2( 0.588, -0.809) * f, z);
                v *= (1.0 / 6.0);
                // SHAFT BITE (ModBuild 140) — the same remap EnvShaft runs, and
                // for the same reason, read here on the floor: the crowns 20-30 m
                // up-light speckle the whole clearing with 0.1-0.3 coverage, and a
                // LINEAR average of that is a faint uniform dimming of a floor the
                // user tuned by hand, while the one thing he asked for — a trunk's
                // shadow raking across the ground — is a SOLID occluder that this
                // ramp takes to full. Below _CsThrow.z the speckle counts for
                // nothing; above _CsThrow.z + span the occluder is solid and
                // counts for everything. The tap disc still averages first, so the
                // shadow's own EDGE keeps its penumbra.
                float sh = saturate(((1.0 - v) - _CsThrow.z) * _CsThrow.w);
                // Off the edge of the baked map, and anywhere in front of its near
                // plane, everything is lit. CLAMP addressing would otherwise drag
                // the border texels right across the room, and a hard cut-off
                // would draw a line on the floor — so it fades out over ~1/40th
                // of the map's own width, which is comfortably wider than the tap
                // disc, so no tap ever reaches past the border while it counts.
                float2 q = abs(sc.xy - 0.5);
                float edge = saturate((0.5 - max(q.x, q.y)) * 40.0)
                           * step(0.0, sc.z) * step(sc.z, 1.0);
                // _CsFlt.w is 1 - MINIMUM VISIBILITY (see the note in
                // EnvShaft.shader): a fully shadowed patch of floor keeps
                // 1 - _CsFlt.w of its MOON term, and the ambient, the three point
                // lights and the landing pool are untouched addends beside it. A
                // shadow in a night wood is not a hole.
                //
                // The two occluder classes multiply (transmittances do) under the
                // same floor: whatever the crowns add, a patch of clearing may
                // never lose more of its moon than the trunk rail allows.
                return max(1.0 - _CsFlt.w,
                           (1.0 - _CsFlt.w * sh * edge) * (1.0 - _CsFolP.w * CsFol(sc.xy, z) * edge));
            }

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
                float2 uv2  : TEXCOORD1;
                float3 opos : TEXCOORD2;
                float3 n    : TEXCOORD3;
                float3 t    : TEXCOORD4;
                float3 b    : TEXCOORD5;
                fixed4 vcol : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv, _MainTex2);
                o.opos = v.vertex.xyz;
                o.n = v.normal;
                o.t = v.tangent.xyz;
                o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
                o.vcol = v.color;
                return o;
            }

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

            // The FIRE SEATS used to be a verbatim copy of EnvRoom's here — the
            // copy even said so. It is GhvrFireSeats() in EnvFire.cginc now,
            // because ModBuild 145 made the flicker a thing the FLAME has to
            // agree with as well, and three copies of one waveform is one more
            // than the two that were already a liability.

            fixed4 frag (v2f i) : SV_Target
            {
                float blend = i.vcol.a;
                fixed4 alb = lerp(tex2D(_MainTex, i.uv), tex2D(_MainTex2, i.uv2), blend) * _Tint;
                float3 n_ts = lerp(UnpackNormal(tex2D(_BumpMap, i.uv)),
                                   UnpackNormal(tex2D(_BumpMap2, i.uv2)), blend);
                n_ts.xy *= _BumpScale;

                // The moon's visibility at this fragment, hoisted out of the
                // light sum below because SURFACE GROWTH asks it a question the
                // lighting does not: how much SKY does this patch of floor have
                // over it. Same call, same value, same place in the product.
                float vis = CsVisible(CsCoord(i.opos));

                // ============================================ SURFACE GROWTH ==
                // The forest floor's own answer to "Frost auf dem Boden". The
                // mechanism is EnvGrowth.cginc's; what this floor knows that a
                // wall does not is WHERE ITS WATER IS — vcol.a is the
                // heightfield generator's mud/litter blend, painted
                // wet-hollows-first, so `blend` is the damp map and the dry
                // litter can be told to frost first.
                //
                // "der Boden mit Moos bzw. Gras bewachsen" USED to be answered
                // here as well, by a second green frontier on the same
                // machinery. It is deleted — not zeroed — after the fourth
                // rejection ("Entferne das 'Moos' komplett", ModBuild 147); see
                // THE MOSS IS GONE in EnvGrowth.cginc for why a fragment
                // function was never going to be able to answer it. The "Gras"
                // half of that sentence is the part that always worked, and it
                // is the part that stays: GrowthGrass/GrowthMoss are real cards
                // that stand up out of this floor under Earth.
                GhvrElem e = GhvrElems();
                float frost = 0.0;
                float ambGain = 1.0, dirGain = 1.0, poolGain = 1.0;
                float3 elemAdd = float3(0, 0, 0);
                // the fire wash is kept SEPARATE from elemAdd because it is added
                // at a different point — see the bottom of this function
                float3 fireAdd = float3(0, 0, 0);
                if (e.live > 0.0)
                {
                    float gt = _Time.y + _GhvrTimeOfs;
                    float rr = GhvrRim(length(i.opos.xz - _ElemCentre.xz), _ElemRad, 0.18);
                    float3 q = GhvrGrowQ(i.opos, _ElemCentre.xyz, _ElemScl, _ElemGrowFreq);
                    float creep = GhvrGrowCreep(q, gt);
                    float grain = saturate((1.0 - n_ts.z) * 2.2);

                    // FROST: the clearing frosts and the wood under the canopy
                    // does not, which is a real thing about cold nights and is
                    // free here — `vis` is the canopy shadow the trees already
                    // cast. Then the dry leaf litter takes it before the mud.
                    float ice = e.ice * _ElemFrost;
                    if (ice > 0.0)
                    {
                        frost = GhvrGrown(GhvrGrowField(q), grain,
                                          saturate(0.50 * vis + 0.30 * (1.0 - blend) + 0.20 * rr),
                                          ice * (0.34 + 1.05 * rr), creep);
                    }
                    // A SECOND FRONTIER used to stand here: the moss, starting in
                    // the wet hollows (0.55 of its affinity was `blend`), then
                    // the shaded ground under the crowns, then outward. Nothing
                    // about that placement was ever what the user objected to —
                    // what he objected to, four times, is that the result is a
                    // colour lying inside the floor's own outline, which is a
                    // stain and not a plant. Deleted in full; THE MOSS IS GONE
                    // in EnvGrowth.cginc carries the argument and the list of
                    // what went with it.
                    float lum = GhvrGrowLum(alb.rgb);
                    alb.rgb = GhvrFrostOn(alb.rgb, lum, frost);
                    n_ts.xy *= 1.0 - 0.62 * frost;

                    // ================================== LIGHT AND DARK ======
                    // USER VERDICT, ModBuild 143 (forest, verbatim): "Licht und
                    // Dunkelheit beeinflussen zwar den Mond aber nicht die
                    // Lichtverhältnisse in der Lichtung. Bei Dunkelheit soll
                    // auch entsprechend die Lichtung dunkler werden, also der
                    // angeleuchtete Boden und die Lichtstrahlen verschwinden.
                    // Bei Helligkeit sollten diese Dinge intensiver werden."
                    //
                    // He is exactly right and the reason is embarrassing: this
                    // shader was in a different lane the round the element
                    // channel landed, so the forest FLOOR — the largest lit
                    // surface in the room and the one "die Lichtung" mostly IS —
                    // never read the channel at all. The three gains below are
                    // the whole of the fix, and they are the same three
                    // FUNCTIONS EnvRoom applies:
                    //
                    // ...THOUGH NO LONGER THE SAME NUMBERS, and it is worth being
                    // exact about that because this comment used to claim they
                    // were. ModBuild 146's cellar verdict ("es soll wirklich den
                    // Mondschein heller machen statt den ganzen Raum") is the
                    // exact opposite of the forest verdict quoted above, so
                    // GhvrAmbGain and GhvrDirGain now read GhvrIndoor() and give
                    // the two rooms different coefficients. THIS SHADER IS THE
                    // FOREST FLOOR AND NOTHING HERE MOVED: the clearing still
                    // lifts its ambient 1.85x under full Light and its moon
                    // 2.55x, exactly as ModBuild 143 asked. What changed is only
                    // that the cellar stopped copying it. One pair of functions,
                    // two rooms, and neither ruling has to lose:
                    //
                    //  * THE MOON is what Light and Dark move. dirGain is
                    //    GhvrDirGain folded with GhvrMoonLight — the eclipse
                    //    contract another lane scales the shafts by — so the
                    //    lit floor and the shafts standing on it darken on the
                    //    same curve, out of one cause. 0.55 * 0.34 = 0.19 at
                    //    totality under full Dark; 1.90 * 1.34 = 2.55 at Light.
                    //  * THE AMBIENT is the room's own floor of light and Dark
                    //    crushes it to a fifth. That is what makes the whole
                    //    clearing fall away rather than just its lit patches.
                    //  * THE POOLS — the wisp, the far lantern and the place the
                    //    shafts LAND — are the forest's candles, and the cellar
                    //    ruling ("die Kerzenscheine sollten identisch bleiben")
                    //    applies to them by the same argument: a pool of light
                    //    is a source you can point at. They are clamped rather
                    //    than left alone for ONE reason, and it is the permanent
                    //    board ruling: the landing pool is the light the players
                    //    read the board by, so it may follow the moon down to
                    //    0.45 and up to 1.60 and no further. At full Dark the
                    //    floor around the board goes to 0.19 and the ambient to
                    //    0.20 while the pool holds 0.45 — the clearing collapses
                    //    onto the board, which is the picture asked for AND the
                    //    one thing that may never become unreadable.
                    ambGain = GhvrAmbGain(e);
                    dirGain = GhvrDirGain(e) * GhvrMoonLight();
                    poolGain = clamp(dirGain, 0.45, 1.60);
                    // ...and the seated fires, if the fire lane has written any.
                    // Against the GEOMETRIC normal, not the mapped one, and not
                    // by accident: this is a wash from a fire two metres away
                    // over leaf litter with a strong normal map, and taking it
                    // per-texel would make the ground around a campfire boil.
                    if (e.fire > 0.0)
                        fireAdd = GhvrFireSeats(i.opos, normalize(i.n), gt, GhvrTipNow(gt), e) * e.fire;
                }
                // =============================================================

                float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);

                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                // frost answers the ambient more strongly than wet leaf litter,
                // exactly as it does on EnvRoom's stone. Exactly 1.0 with no ice
                // and no element. (A `- 0.12 * mthk` stood beside it: the moss
                // cushion swallowing the ambient. It went with the moss.)
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5)
                               * (ambGain + 0.30 * frost);
                // USER FINDING, ModBuild 137 (hardware): "... ich würde hier
                // gerne das die Bäume entsprechende Schatten werfen." Half of
                // "the trees cast shadows" is the trunk shadows lying across the
                // clearing floor, and this is it: a pure multiply on the MOON
                // term. It can only subtract — see the _CsMap block in the
                // Properties for why nothing here can get brighter. (dirGain is
                // a separate factor and is exactly 1 with no element up.)
                light += _DirCol.rgb * (_DirScale * saturate(dot(N, normalize(_DirDir.xyz)))
                                        * vis * dirGain);
                light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00) * poolGain;
                light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83) * poolGain;
                light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19) * poolGain;

                float3 col = (alb.rgb * light + elemAdd * alb.rgb) * i.vcol.rgb;
                // ...AND THE FIRE WASH, AFTER the vertex fade, which is the one
                // term in this shader that is deliberately outside it.
                //
                // i.vcol is the wood DISSOLVING WITH DISTANCE (GroundColor's
                // `fade` and `open`): out at the tree line it is 0.12, which is
                // the "hinter den Bäumen soll es so dunkel sein das man sich
                // nicht traut" ruling made of one number. That curve is aerial
                // perspective on MOONLIT ground — a stand-in for "you cannot see
                // that far in a wood at night". A fire burning out there is
                // precisely the thing you CAN see that far, and the first bake of
                // the forest fire proved the point by contradiction: the snag was
                // alight and the ground it stood on was black, because its wash
                // had been multiplied by 0.12. So the wash is added here, still
                // modulated by the ground's own albedo and normal, and still
                // exactly zero with Fire down.
                col += fireAdd * alb.rgb;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
