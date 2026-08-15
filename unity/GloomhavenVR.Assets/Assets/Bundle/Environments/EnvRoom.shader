// GloomhavenVR — self-lit room-interior shader (custom-asset environments).
//
// The environments are script-free prefabs on the mod layer; the game scene
// contributes NO usable lights, so ALL lighting is baked into material
// parameters and evaluated here:
//   - hemisphere ambient (world-up based: _AmbUp / _AmbDown),
//   - one directional "moon" light (direction in OBJECT space, set per
//     placement by BuildEnvironments.cs),
//   - up to 3 point lights (candles/lanterns; positions in OBJECT space,
//     w = 1/range in object units; alpha of the color = flicker amount,
//     animated purely by _Time — data-driven, no scripts, stereo-correct).
// Per-pixel normal mapping works naturally against these lights (TBN stays
// in object space, no per-frame CPU cost). Vertex color (lerped in by _VCol)
// carries baked large-scale shading such as the swamp ground's radial
// fade-to-darkness. Diffuse only: no view-dependent terms except none — the
// safest possible shader for stereo.
//
// Written into the bundle because builtin shaders may be stripped from the
// game player (the pink-material trap, TOOLCHAIN.md §4.1).
Shader "GloomhavenVR/EnvRoom"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
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
        // Cold moon rim (forest trunks). Defaults to BLACK so every existing
        // material — the whole cellar — is bit-identical without it.
        _RimCol ("Rim light color", Color) = (0,0,0,1)
        _RimPow ("Rim tightness", Range(0.5,8)) = 3.0
        _RimDir ("Rim gate: light dir (OBJECT space)", Vector) = (0,1,0,0)
        // Point-light NEAR-FIELD hardness (see PointLight below). 0 reproduces
        // the old pure (1-(d/r)^2)^2 window exactly => the forest is untouched.
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        [Toggle] _Cutout ("Alpha cutout", Float) = 0
        _Cutoff ("Cutout threshold", Range(0,1)) = 0.5

        // ---- ELEMENT ART (see EnvElement.cginc) ----
        // The room's own frame, written for every material by ApplyRig, so that
        // "the periphery" means the same thing for a wall authored around the
        // origin and for a barrel with its own transform. Defaults are harmless:
        // a centre of 0 and a radius of 6 m only decide WHERE an element blooms,
        // and no element blooms at all until the master is up.
        _ElemCentre ("Element: room centre (OBJECT space)", Vector) = (0,0,0,0)
        _ElemRad ("Element: room outer radius (object units)", Float) = 6
        // Per-material susceptibility. Frost and the fire rim are ON by default
        // (stone, bark, wood — everything the elements should reach).
        //
        // _ElemMoss IS GONE, and it is worth one line here because thirty
        // materials in the bundle still carry the value: the painted moss was
        // deleted on the user's fourth rejection of it (see THE MOSS IS GONE in
        // EnvGrowth.cginc). This shader no longer declares the property, so
        // those material values are inert data. Earth reaches this room through
        // the GROWTH CARDS (EnvRoomCutout/_ElemGrow) — geometry standing up out
        // of the ground — and through nothing else.
        _ElemFrost ("Element: ice frost susceptibility", Range(0,2)) = 1
        _ElemWarm ("Element: fire rim susceptibility", Range(0,2)) = 1
        // SURFACE GROWTH (EnvGrowth.cginc). _ElemScl is this material's object
        // units expressed in metres, written by ApplyRig beside _ElemCentre, so
        // a 0.45 m patch of frost is 0.45 m on a prop scaled 2.0 as well as on
        // the welded floor. Defaults are the identity: scale 1 and the density
        // both rooms were tuned at.
        _ElemScl ("Element: object units in metres", Float) = 1
        _ElemGrowFreq ("Element: growth cells per metre", Float) = 3.0

        // ---- FIRE SEATS (the receiving half of the fire lane's contract) ----
        // A real fire standing in the room has to light the wall behind it, and
        // the wall is this shader. The FIRE lane writes, per material and in
        // THAT material's object space, up to three seats (xyz, w = 1/range),
        // one wash colour (a = flicker depth) and one flicker rate; this side
        // owns nothing but the receiving term.
        //
        // The defaults are a hard off in two independent ways — a black colour
        // and a zero flicker — so a build in which the other half does not exist
        // yet simply leaves them unread, and the zero state is untouched either
        // way (the whole term is inside `if (e.fire > 0)`).
        _FirePos0 ("Fire seat 0 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FirePos1 ("Fire seat 1 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FirePos2 ("Fire seat 2 (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _FireCol ("Fire wash colour (a = flicker depth)", Color) = (0,0,0,0)
        _FireRate ("Fire flicker rate (Hz)", Float) = 6
        // ...and which of the three seats is standing on the bookshelf that
        // topples. See EnvFire.cginc; zero on every material in the forest and
        // on the two cellar sites that stand on the floor.
        _FireRide ("Fire seats that ride the tipping shelf", Vector) = (0,0,0,0)

        // ---- SHELF RIDERS (EnvShelfTip.cginc) -------------------------------
        // USER, ModBuild 144: "Die Kerzen und das Feuer, die auf dem Bücherregal
        // stehen, kippen nicht mit - das musst du beheben das ist ein echter
        // Bug." Two DIFFERENT things in this shader answer that:
        //   * the WAX of the shelf candle is an EnvRoom material, so _TipUse.x
        //     moves its geometry with the shelf;
        //   * every lit surface in the cellar is lit BY that candle, so
        //     _TipUse.y names the baked light slot whose position travels with
        //     it and whose colour dies with its flame. That second one is why
        //     the whole room carries these five and not just the candle.
        // All zero = the material has never heard of the bookshelf.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, flame stiffness", Vector) = (0,-1,0,0)
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "EnvElement.cginc"
    #include "EnvGrowth.cginc"
    // The fire wash and, through it, the tipping shelf's pose. Both channels are
    // declared there — _FirePos0..2/_FireCol/_FireRate/_FireRide and the five
    // _Tip* vectors — so this shader declares neither a second time.
    #include "EnvFire.cginc"

    sampler2D _MainTex; float4 _MainTex_ST;
    sampler2D _BumpMap;
    float _BumpScale, _VCol, _Cutout, _Cutoff, _RimPow, _PtHard;
    fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col, _RimCol;
    float4 _DirDir, _L0Pos, _L1Pos, _L2Pos, _RimDir;
    float4 _ElemCentre;
    float _ElemRad, _ElemFrost, _ElemWarm, _ElemScl, _ElemGrowFreq;

    // PREVIEW-ONLY global clock offset. Never set at runtime (=> 0, the shipped
    // behaviour); EnvironmentsPreview sets it with Shader.SetGlobalFloat so a
    // still frame can be rendered at an arbitrary point of every animation.
    // Declared in every Env* shader that reads _Time — the whole room has to
    // move to the SAME offset or a time series proves nothing.
    float _GhvrTimeOfs;

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
        float4 pos    : SV_POSITION;
        float2 uv     : TEXCOORD0;
        float3 opos   : TEXCOORD1; // object-space position
        float3 n      : TEXCOORD2; // object-space normal
        float3 t      : TEXCOORD3; // object-space tangent
        float3 b      : TEXCOORD4; // object-space bitangent
        float3 ov     : TEXCOORD5; // object-space view vector (rim light only)
        // SHELF RIDERS: the one baked light slot that stands on the tipping
        // bookshelf, moved with it, plus that flame's life in w. w < 0 means
        // "nothing rides" and is the only value the fragment tests, so every
        // material in both rooms that is not near the shelf takes exactly the
        // path it took before this existed. See GhvrTipLight.
        float4 tipL   : TEXCOORD6;
        fixed4 vcol   : COLOR;
    };

    v2f vert (appdata v)
    {
        v2f o;
        // SHELF RIDERS — the WAX. The shelf candle's wax is welded into an
        // EnvRoom mesh (CandleGroup), so the only thing that can move it at
        // runtime is this vertex shader; _TipUse.x is what says it must.
        // GhvrTipPoint/GhvrTipDir are the identity — the same registers, not a
        // rotation by zero — whenever the shelf is standing.
        GhvrTip tip = GhvrTipNow(_Time.y + _GhvrTimeOfs);
        float4 vpos = v.vertex;
        float3 vnrm = v.normal;
        float3 vtan = v.tangent.xyz;
        if (tip.live > 0.5 && _TipUse.x > 0.5)
        {
            vpos.xyz = GhvrTipPoint(tip, vpos.xyz);
            vnrm = GhvrTipDir(tip, vnrm);
            vtan = GhvrTipDir(tip, vtan);
        }
        o.pos = UnityObjectToClipPos(vpos);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.opos = vpos.xyz;
        o.n = vnrm;
        o.t = vtan;
        o.b = cross(vnrm, vtan) * v.tangent.w;
        o.ov = ObjSpaceViewDir(vpos);
        o.tipL = GhvrTipLight(tip, _L0Pos, _L1Pos, _L2Pos);
        o.vcol = v.color;
        return o;
    }

    // Candle flicker — three incommensurate sines plus one slow "breath", each
    // light on its OWN phase AND its own rate, so two candles in one room never
    // pulse as a pair. Amplitude is exactly +-amt*0.35 (the sine weights sum to
    // 1 and the breath is mixed in, not added on top).
    // amt is the alpha of the light colour, written by EnvRoomBuilder's rig.
    float Flicker (float amt, float phase, float rate)
    {
        float t = (_Time.y + _GhvrTimeOfs) * rate;
        float f = 0.42 * sin(t * 11.3 + phase)
                + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
        // the slow term is what a draft does to a flame: the whole pool swells
        // and sinks over a couple of seconds instead of only buzzing
        f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
        return 1.0 + amt * 0.35 * f;
    }

    // Attenuation window (1-(d/r)^2)^2 divided by a near-field inverse-square
    // term. _PtHard = 0 is EXACTLY the old window (the forest's three points);
    // _PtHard > 0 collapses the lit pool toward the source, which is the only
    // way a candle can light its own table and leave the far wall black
    // (user finding, ModBuild 134: "die Kerzen beleuchten hier viel zu viel").
    // THE CANDLES BELONG TO NOBODY. Exactly ONE element knob is left here, and
    // which one that is, is a user ruling rather than a taste (ModBuild 143,
    // cellar, verbatim):
    //   "Bei Licht sollte auch der Mondschein aus dem Fenster viel intensiver
    //    sein und den Raum mehr erhellen, anstatt die Kerzenscheine, die sollten
    //    identisch bleiben."
    //   "Auch bei Dunkelheit sollte es keinen Einfluss auf den Kerzenschein
    //    haben - eine viel bessere Idee wäre hier den Mondschein extrem zu
    //    reduzieren, so dass der Raum insgesamt deutlich dunkler wird und der
    //    Kerzenschein einer der wenigen Stellen ist, die überhaupt noch gut
    //    erkennbar sind."
    // So ModBuild 142's two Light/Dark knobs on this function are GONE — the
    // `* srcGain` on each accumulated slot (Light lifted the pools) and the
    // `hardMul` inside the falloff (Dark collapsed them toward their flames).
    // Both were defensible under EnvElement.cginc's split, and both are refused:
    // a candle is not lit by the moon and is not put out by an eclipse. What
    // Light and Dark do instead is move the MOON — see dirGain below — which is
    // exactly the picture he asked for, because with the moonlight crushed and
    // the candles untouched the candle pools are the only well-lit places left
    // in the room without a single number on them having changed.
    //
    // ...AND THAT RULING IS NO LONGER ENFORCED HERE ALONE. Deleting the two
    // knobs from THIS function fixed the walls and left every other reader of
    // the shared source gain still moving the cellar's candles — the halos
    // (EnvGlow), the drips (EnvDrip), the reflected shard in the puddle
    // (EnvPuddle) and the tipping bookshelf (EnvHaunt) all brightened 2.10x
    // under Light in a room whose walls did not. Since ModBuild 146
    // GhvrSrcGain and GhvrSrcHard are themselves the exact identity indoors, so
    // the ruling holds for every consumer at once and a shader added next round
    // inherits it instead of having to remember it. This function is unchanged
    // and stays unchanged: it is simply no longer the only thing standing
    // between the ruling and the room.
    //
    // `flickMul` stays, and it is not the same kind of thing: AIR works the
    // flames, so the POOLS shiver and not just the sprites. A draught you can
    // see on the wall is a draught. It is exactly 1 with Air down.
    float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate,
                       float flickMul)
    {
        float3 lv = lpos.xyz - opos;
        float d2 = max(dot(lv, lv), 1e-8);
        float d = sqrt(d2);
        float q = d2 * lpos.w * lpos.w;                  // (d/range)^2
        float x = saturate(1.0 - q);
        float atten = x * x / (1.0 + _PtHard * q);
        float ndl = saturate(dot(N, lv / d));
        return lcol.rgb * (atten * ndl * Flicker(lcol.a * flickMul, phase, rate));
    }

    // ---- FIRE SEATS ------------------------------------------------------
    // The receiving half of the fire contract used to be implemented here AND,
    // character for character, in EnvGround.shader. It is now GhvrFireSeats() in
    // EnvFire.cginc, ONCE, because ModBuild 145 gave it a third obligation it
    // could not have met as two copies: the flicker has to agree with the FLAME
    // as well as with the other room's floor, and two fires seated on a
    // bookshelf have to move when the bookshelf does. Read that file for the
    // window, for why _PtHard is deliberately left out of it, and for why the
    // whole term is exactly zero with Fire down.

    fixed4 fragCore (v2f i, float face)
    {
        fixed4 alb = tex2D(_MainTex, i.uv) * _Tint;
        if (_Cutout > 0.5) clip(alb.a - _Cutoff);

        float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.uv));
        n_ts.xy *= _BumpScale;

        // ================================================= SURFACE GROWTH ====
        // ICE: a coverage that advances over this surface and retreats when the
        // element falls. The mechanism, the affinity terms and the argument
        // against the ModBuild 142 fade this replaces are all in
        // EnvGrowth.cginc; what is chosen HERE is only where frost STARTS on a
        // wall, a flagstone or a trunk.
        //
        // EARTH IS NO LONGER IN THIS BLOCK. It used to grow a second, green
        // covering here on the same machinery, and the user has now rejected
        // that surface four times ("Entferne das 'Moos' komplett", ModBuild
        // 147). It is deleted rather than disabled — the look functions, the
        // frontier call, the thickness, the moss normal and the wet sheen are
        // all gone from this file, and _ElemMoss is no longer declared. The full
        // argument, including why no amount of reworking a fragment function
        // could have fixed it, is THE MOSS IS GONE in EnvGrowth.cginc. What
        // Earth does instead is stand growth CARDS up out of the ground
        // (EnvRoomCutout/_ElemGrow), which is geometry and therefore has a
        // silhouette.
        //
        // It runs BEFORE the normal is assembled on purpose: a crust fills the
        // relief it grew into, so the mask also flattens the normal map. That is
        // the difference between something growing ON the stone and something
        // painted over a picture of stone, and it costs one multiply.
        //
        // Behind the same single uniform compare as everything else, and then
        // behind one more per element: with only Fire or Light up, the noise is
        // not sampled at all.
        GhvrElem e = GhvrElems();
        float frost = 0.0, rr = 0.0;
        if (e.live > 0.0)
        {
            float gt = _Time.y + _GhvrTimeOfs;
            rr = GhvrRim(length(i.opos.xz - _ElemCentre.xz), _ElemRad, 0.18);
            // the GEOMETRIC normal, not the mapped one: the frost follows the
            // shape of the wall, and the texture's own bumps come in through
            // `grain` instead, where they belong.
            float3 gN = normalize(i.n) * face;
            float3 gnw = normalize(mul((float3x3)unity_ObjectToWorld, gN));
            float3 q = GhvrGrowQ(i.opos, _ElemCentre.xyz, _ElemScl, _ElemGrowFreq);
            float creep = GhvrGrowCreep(q, gt);
            // the joints, the chips and the fissures — the normal map's own
            // departure from flat, which is exactly the map of where water sits
            float grain = saturate((1.0 - n_ts.z) * 2.2);
            // metres above the room's floor plane (_ElemCentre carries the room
            // origin in THIS material's object space, so a barrel three metres
            // out measures from the floor and not from its own pivot)
            float hgt = (i.opos.y - _ElemCentre.y) * _ElemScl;
            float foot = saturate(1.0 - hgt * 0.80);                 // gone by 1.25 m
            float sky = saturate(gnw.y);                             // what the cold sees
            float shade = 1.0 - saturate(dot(gN, normalize(_DirDir.xyz)) * 0.5 + 0.5);

            // FROST starts where the cold does: on faces that look at the sky,
            // on the side the moon never reaches, and low down where the cold
            // air lies. Not primarily at the foot, which is where DAMP collects
            // — the two used to be told apart here because two coverings shared
            // this block; the distinction survives the moss's deletion because
            // it is what makes frost read as cold rather than as wear.
            float ice = e.ice * _ElemFrost;
            if (ice > 0.0)
            {
                // The periphery weighting keeps ModBuild 142's job and changes
                // its units: it is now a COVERAGE FRACTION and not an opacity,
                // so the same shape needed different numbers. Inside a metre of
                // the room centre the ramp is 0 and this is 0.15, which puts the
                // threshold above the top of A — the board's own flagstones are
                // not dusted at all, at any strength. At the walls it saturates.
                frost = GhvrGrown(GhvrGrowField(q), grain,
                                  saturate(0.42 * sky + 0.34 * shade + 0.24 * foot),
                                  ice * (0.15 + 1.15 * rr), creep);
            }

            // `foot` and `shade` used to feed a SECOND frontier here — the moss —
            // which started where the damp is: the foot of the wall above all,
            // the side that never dries, the upward faces that catch what drips.
            // The placement was never what was rejected; the fact that the
            // result was a colour lying inside the wall's own outline was. See
            // THE MOSS IS GONE in EnvGrowth.cginc. `foot` survives because
            // frost's own `place` uses it.

            float lum = GhvrGrowLum(alb.rgb);
            alb.rgb = GhvrFrostOn(alb.rgb, lum, frost);
            // the crust fills what it grew into. Exactly 1.0 where nothing grew,
            // so a lit-but-ungrown pixel is untouched.
            n_ts.xy *= 1.0 - 0.62 * frost;
        }
        // =====================================================================

        float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);
        N *= face; // two-sided foliage: light the visible side

        // hemisphere ambient against WORLD up (correct under prefab yaw/scale)
        float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));

        // ================================================== ELEMENT ART ======
        // The whole block is behind ONE uniform compare on peak x master. With
        // no element up it does not execute, and the six modifiers below stay at
        // their identity values — which is why the zero state is not merely
        // "close to" the tuned room but the same instructions on the same data.
        // See EnvElement.cginc for the contract and for the Light/Dark split.
        // (`e` and the periphery ramp `rr` are hoisted above, where SURFACE
        // GROWTH needs them; the mood is read once for the whole shader.)
        float ambGain = 1.0, dirGain = 1.0, flickMul = 1.0;
        float3 elemAdd = float3(0, 0, 0);
        // ...and the fire wash, kept SEPARATE because it is added at a different
        // point — after the vertex fade. See the bottom of this function.
        float3 fireAdd = float3(0, 0, 0);
        float3 rimTint = float3(1, 1, 1);
        if (e.live > 0.0)
        {
            float t = _Time.y + _GhvrTimeOfs;

            // THE AMBIENT — and this line no longer means the same thing in the
            // two rooms it serves. EnvRoom is the cellar's masonry AND the
            // wood's trunks, so it is the shader the ModBuild 146 verdict is
            // most visible in.
            //
            // USER VERDICT, ModBuild 146 (hardware, cellar, verbatim): "Der
            // 'Hell'-Effekt im Keller gefällt mir noch nicht, es soll wirklich
            // den Mondschein heller machen statt den ganzen Raum."
            //
            // WHAT THIS COMMENT USED TO SAY, and why it was wrong. It defended
            // the previous behaviour by quoting ModBuild 143's "und den Raum
            // mehr erhellen" — the room SHOULD get brighter under Light — and
            // then delivered that through GhvrAmbGain, i.e. by raising the
            // hemisphere floor 1.85x. That lifts the far corners, the ceiling,
            // the underside of the stair and the shadowed side of every barrel
            // by the same 85%, all of them out of the reach of any window, and
            // no arrangement of that reads as moonlight. The two sentences are
            // not in conflict: the second names the MECHANISM the first was
            // supposed to arrive by. So the ambient is now held at exactly 1.00
            // indoors (GHVR_AMB_LIFT_IN = 0.00 — zero, so it is the same bits,
            // not merely a small number) and the whole of Light arrives through
            // the window on the line below or does not arrive.
            //
            // The forest is UNTOUCHED and its opposite ruling still stands
            // ("Bei Helligkeit sollten diese Dinge intensiver werden"): a
            // clearing is lit by its own sky, so out there the ambient IS a
            // moonlight term and still lifts by 0.85. One function, two rooms,
            // GhvrIndoor() between them.
            ambGain = GhvrAmbGain(e);
            // THE MOON, and it is now the ONLY thing Light and Dark are allowed
            // to move in this shader at all indoors (see PointLight for what
            // they are no longer allowed to move, and the block above for the
            // ambient). GhvrMoonLight is EnvElement.cginc's contract: 1.0 at
            // rest, below 1 while the eclipse eats the disc under Dark, above 1
            // while Light swells it — and the SAME function scales the cellar's
            // beam, the wood's shafts and the puddle's mirror, so the light on
            // the floor and the light in the air can never disagree about how
            // much moon there is. Under full Dark at totality the product is
            // 0.55 * 0.05 = 0.0275: the moonlight is gone, the ambient falls to
            // a fifth with it (GhvrAmbGain), and the candles keep every photon
            // they had. Under full Light it is 1.90 * 1.34 = 2.55 in the wood
            // and 2.40 * 1.34 = 3.22 in the cellar — the cellar's moon is
            // spending the ambient's share as well as its own, which is exactly
            // what "wirklich den Mondschein heller machen statt den ganzen
            // Raum" asks for, and it lands on the surfaces the moon can
            // actually see because that is what a directional term is.
            dirGain = GhvrDirGain(e) * GhvrMoonLight();
            // AIR: the draught works the candles (see PointLight). The forest's
            // three "points" are a wisp, a far lantern and the shafts' landing
            // pool, all with a flicker alpha near zero, so this is felt in the
            // cellar and is a no-op in the wood — which is where the design puts
            // Air's cellar channel ("the authored draught strengthens").
            flickMul = 1.0 + 1.20 * e.air;

            // ---- ICE: the frost is grown above; here is what it does to the
            // LIGHT. A frost crust is a diffuse white, so it answers the room's
            // ambient more strongly than the wet stone under it did.
            ambGain += 0.30 * frost;
            // the moon rim goes cold with Ice (the forest's Ice channel)
            rimTint = lerp(float3(1, 1, 1), float3(0.70, 0.88, 1.30), saturate(e.ice));

            // ---- FIRE: a warm rim on the faces that look INTO the room ------
            // Not a fill light: a fire in the room lights the sides of things
            // that face it, and everything faces the middle. Gated on the
            // inward-facing term so the outside of a barrel stays cold, and on
            // the grazing term so it reads as a rim rather than as a repaint.
            float3 toC = _ElemCentre.xyz - i.opos;
            float inward = saturate(dot(N, toC * rsqrt(max(dot(toC, toC), 1e-4))));
            float graze = 1.0 - saturate(dot(N, normalize(i.ov)));
            elemAdd += float3(1.00, 0.36, 0.10)
                     * (e.fire * _ElemWarm * 0.26 * inward * (0.30 + 0.70 * graze * graze)
                        * GhvrEmberBreath(t, rr * 5.3));

            // ...and the SEATED fires, which are a different claim from the rim
            // above: the rim says "there is fire in this room", a seat says
            // "there is a fire HERE, and this is the wall it stands against".
            // The fire lane writes the seats; this is the receiving term.
            if (e.fire > 0.0)
                fireAdd = GhvrFireSeats(i.opos, N, t, GhvrTipNow(t), e) * e.fire;

            // ---- EARTH: NOTHING HAPPENS TO A SURFACE HERE ANY MORE.
            //
            // What stood here was the moss's SHEEN — a cold grazing wet gloss,
            // float3(0.014, 0.026, 0.017) weighted by the coverage, the thin
            // frontier and the periphery, and already multiplied out to zero
            // indoors after ModBuild 146's "es sieht eher aus wie Schleim" — and
            // beside it `ambGain -= 0.12 * mthk`, the moss swallowing the room's
            // ambient. Both are deleted with the moss itself (THE MOSS IS GONE,
            // EnvGrowth.cginc).
            //
            // THIS IS NOT AN OVERSIGHT AND IT IS THE POINT OF THE ROUND: a green
            // ADD on a trunk is a green trunk, whatever it is called. A sheen
            // that only exists where a covering was grown cannot survive the
            // covering, and one that exists everywhere is the ModBuild 142 wash
            // this whole feature replaced. Earth's statement in both rooms is
            // now vegetation STANDING UP out of the ground — real cards with
            // real outlines, EnvRoomCutout/_ElemGrow — and it is the bake lane's
            // to make denser. Nothing in this shader paints anything green.
        }
        // =====================================================================

        float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5) * ambGain;

        light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz))) * dirGain;
        // slot phases AND rates are incommensurate: the room breathes, it does
        // not pulse (user, ModBuild 134: "Eine flackernde Kerze sollte auch das
        // Licht drumrum zum flackern bekommen")
        // Each slot is accumulated SEPARATELY, and that is not a style choice:
        // summing the three first would re-associate three floating-point
        // additions, and a re-associated sum can differ in its last bit. The
        // zero state has to be identical, not nearly identical, so the
        // accumulation order stays exactly as it was.
        // The `* srcGain` that stood on each of these three lines in ModBuild
        // 142/143 is gone: see the ruling quoted at PointLight.
        //
        // SHELF RIDERS — THE LIGHT FOLLOWS ITS SOURCE. One of these three slots
        // may be a candle standing on the bookshelf that topples; GhvrTipSlot
        // moves that one to where the candle now is and dims it with the flame's
        // life, and TOUCHES NOTHING when nothing is riding, so the accumulation
        // below is the shipped one bit for bit. (The order of the three
        // accumulations still may not change — see the note above.)
        float4 p0 = _L0Pos, p1 = _L1Pos, p2 = _L2Pos;
        fixed4 c0 = _L0Col, c1 = _L1Col, c2 = _L2Col;
        GhvrTipSlot(i.tipL, 0.0, p0, c0);
        GhvrTipSlot(i.tipL, 1.0, p1, c1);
        GhvrTipSlot(i.tipL, 2.0, p2, c2);
        light += PointLight(p0, c0, i.opos, N, 0.0, 1.00, flickMul);
        light += PointLight(p1, c1, i.opos, N, 2.1, 0.83, flickMul);
        light += PointLight(p2, c2, i.opos, N, 4.4, 1.19, flickMul);

        float3 col = alb.rgb * light + elemAdd * alb.rgb;

        // Cold rim: a grazing-angle wrap of the moon, gated so only the moonlit
        // SIDE of a trunk catches it. This is what makes a night forest read as
        // volumes instead of flat silhouettes. It is view-dependent and so
        // differs slightly between the eyes — which is physically what a rim IS,
        // and the gradient is smooth, so it fuses (unlike a screen-space
        // pattern, which would not). Black by default => opt-in per material.
        float rim = pow(1.0 - saturate(dot(N, normalize(i.ov))), _RimPow)
                  * saturate(dot(N, normalize(_RimDir.xyz)) * 0.5 + 0.55);
        // ELEMENT ART — the rim IS the moon, so it follows the moon's own gain
        // (Light lifts it, Dark takes it away only while Light is not up), and
        // Ice pushes it toward the blue end. That is the forest's authored Ice
        // channel: "a cold cast on the moon term". It is deliberately the same
        // dirGain the directional term uses, so a trunk's lit side and its rim
        // can never disagree about how bright the moon is.
        col += _RimCol.rgb * (rim * dirGain) * rimTint;
        col *= lerp(float3(1, 1, 1), i.vcol.rgb, _VCol);
        // ...AND THE FIRE WASH, AFTER the vertex fade, which is the one term in
        // this shader deliberately outside it.
        //
        // _VCol is the forest's DEPTH DISSOLVE (EnvRoomBuilder's Depth(): 1.0 in
        // the clearing falling to 0.010 by ten metres), i.e. aerial perspective
        // on MOONLIT surfaces — a stand-in for "you cannot see that far in a wood
        // at night", and the single number the "man traut sich nicht dahinter"
        // ruling is made of. A tree that is ON FIRE at seven metres is exactly
        // the thing you CAN see that far, and the first bake proved it by
        // contradiction: the burning snag's bark stayed blue-grey because its own
        // fire's wash had been multiplied by 0.35. In the cellar _VCol is 0 and
        // this line is identical to adding it above. Still zero with Fire down.
        col += fireAdd * alb.rgb;
        return fixed4(col, 1.0);
    }
    ENDCG

    // -------- opaque (default) --------
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return fragCore(i, 1.0); }
            ENDCG
        }
    }
    Fallback Off
}
