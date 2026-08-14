// GloomhavenVR — the cellar's puddle: a wet patch of flagstone that a drip from
// the ceiling keeps ringing, and that catches the candle and the moon.
//
// USER RULING, ModBuild 134: "zB tropft Wasser von irgendwo runter in eine
// pütze". So the puddle has to be a LIGHT event, not a texture: the reflections
// are what makes a dark room read as wet, and the expanding rings are what makes
// the drip land instead of merely falling.
//
// Two tiny passes over ~600 triangles, both script-free (_Time only):
//   1. MULTIPLY (Blend DstColor Zero) — darkens the flagstones underneath by
//      _Wet, feathered out by the mesh's own vertex alpha, so the puddle is a
//      wet patch OF the floor rather than a plate lying on it.
//   2. ADD (Blend One One) — the mirror images. For a flat mirror the image of
//      a light sits exactly where the REFLECTED view ray points at it, so
//      pow(saturate(dot(reflect(-V,N), toLight)), p) IS the reflection, blurred
//      by p. Done for the moon (a direction) and for one candle (a position,
//      flickering on the same sine family and rate as its EnvRoom light slot).
//      N is perturbed by the ripple, which is what makes the highlights break
//      into moving shards every time a drop lands.
//
// The ripple clock is the drip PERIOD. EnvironmentsBuilder emits the drip
// particles at exactly 1/_Period and delays the splash ring by the fall time, so
// all three (drop, ring, reflected shatter) belong to the same event.
//
// Everything is evaluated in OBJECT space; the puddle mesh is authored in room
// coordinates at identity, so object space IS room space and the light
// positions handed in by EnvRoomBuilder need no transform.
//
// ELEMENT ART. Air ruffles the water and runs cat's paws along the draught; Ice
// FREEZES it, and after the ModBuild 143 verdict that is a rebuild rather than a
// tuning — see THE ICE IS A SOLID in the CGINCLUDE below. Every moonlight term
// (the mirror image, the sky sheen, the light on the sheet) is multiplied by the
// shared GhvrDirGain(e) * GhvrMoonLight(), so the reflection of the moon and the
// moon cannot be in different states.
Shader "GloomhavenVR/EnvPuddle"
{
    Properties
    {
        _Wet ("Wet darkening (multiplied onto the floor)", Color) = (0.34,0.36,0.40,1)
        _Center ("Ripple centre (OBJECT space, xz)", Vector) = (0,0,0,0)
        _Radius ("Puddle radius (m)", Float) = 0.6
        _Period ("Seconds between drips (MUST equal EnvDrip._Period)", Float) = 2.6
        _Phase ("Phase (s) (MUST equal EnvDrip._Phase)", Float) = 0
        _Impact ("Seconds from cycle start to impact (EnvDrip._Hang + fall)", Float) = 2.3
        _RingFreq ("Rings per metre", Float) = 6.0
        _RingAmp ("Ripple slope", Float) = 0.35
        _RingCon ("Ripple contrast in the wet darkening", Range(0,1)) = 0.5
        _Calm ("Resting ripple slope", Float) = 0.06
        _SkyCol ("Broad sky/moon sheen", Color) = (0.10,0.13,0.20,1)
        _MoonDir ("Direction TOWARD the moon (OBJECT space)", Vector) = (0,1,0,0)
        _MoonCol ("Moon reflection colour", Color) = (0.42,0.52,0.78,1)
        _MoonPow ("Moon reflection tightness", Float) = 220
        _CandPos ("Candle position (OBJECT space)", Vector) = (0,1,0,0)
        _CandCol ("Candle reflection colour (a = flicker)", Color) = (1.0,0.55,0.22,0.9)
        _CandPow ("Candle reflection tightness", Float) = 90
        _CandRate ("Candle flicker rate (match its light slot)", Float) = 1
        _CandPhase ("Candle flicker phase (match its light slot)", Float) = 0
        _Fresnel ("Grazing-angle bias", Range(0,1)) = 0.85
        // ELEMENT ART — AIR. The draught's direction (EnvRoomBuilder.DraftDir,
        // OBJECT space) and the slope of the cat's paws it drags across the
        // water. The puddle already ruffles under Air (ElemRippleAmp), but a
        // rougher CONCENTRIC ripple is just a busier drip — it says the air is
        // moving and not which way. These waves travel, and they travel the way
        // the draught travels, so the water points at the window.
        _DraftDir ("Draught direction (OBJECT space)", Vector) = (0,0,0,0)
        _DraftWave ("Draught wave slope at full Air", Float) = 0
        // ELEMENT ART — ICE. See THE ICE IS A SOLID below for what these three
        // do and why the frozen puddle needed rebuilding rather than retuning.
        _IceCol ("Ice sheet colour", Color) = (0.74,0.83,0.97,1)
        _IceBody ("Ice scattering brightness (view-independent)", Range(0,2)) = 0.5
        _IceRelief ("Ice surface relief (dome + dendrites)", Range(0,2)) = 0.55
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "EnvElement.cginc"

    fixed4 _Wet, _MoonCol, _CandCol, _SkyCol, _IceCol;
    float4 _Center, _MoonDir, _CandPos, _DraftDir;
    float _Radius, _Period, _Phase, _Impact, _RingFreq, _RingAmp, _RingCon, _Calm;
    float _MoonPow, _CandPow, _CandRate, _CandPhase, _Fresnel, _DraftWave;
    float _IceBody, _IceRelief;
    float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

    struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
    struct v2f
    {
        float4 pos  : SV_POSITION;
        float3 opos : TEXCOORD0;
        float3 ov   : TEXCOORD1;   // object-space view vector
        // PuddleMesh writes uv.y = the NORMALISED radial coordinate: 0 at the
        // centre, 1 on the (irregular) rim. It is not the same thing as vcol.a,
        // which is a feathered MASK and saturates over the inner 55% — and the
        // ice needs a coordinate that keeps running all the way in, because the
        // sheet grows from the rim toward the middle. One extra interpolator.
        float2 uv   : TEXCOORD2;
        fixed4 vcol : COLOR;       // .a = puddle mask (1 deep, 0 at the rim)
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.opos = v.vertex.xyz;
        o.ov = ObjSpaceViewDir(v.vertex);
        o.uv = v.uv;
        o.vcol = v.color;
        return o;
    }

    /// Surface height of the ripple field at radius r, in metres of "slope
    /// units". One expanding train per drip plus a permanent, much slower
    /// breathing so the puddle is never a mirror-flat sheet of glass.
    // ======================================= ELEMENT ART: THE ICE IS A SOLID =
    // USER VERDICT, ModBuild 143 (verbatim): "Das Eis im Keller erscheint mir
    // eher wie 'Pfützen' zu sein als wirklich Eis. Es sollte mehr wie Eis
    // rüberkommen."
    //
    // HE IS RIGHT, AND THE OLD MODEL COULD NOT HAVE BEEN TUNED INTO ICE. It did
    // three things — kill the rings, pale the wet darkening, sharpen the mirror
    // — and all three leave a FLAT WET SURFACE WITH DIFFERENT NUMBERS ON IT. Two
    // of them were actively wrong:
    //   * "a sheet of ice is a better mirror than a rippled puddle" is false.
    //     Ice is a rough, scattering dielectric: it is DULLER and BROADER than
    //     water, and it is bright from directly above, where water is a black
    //     hole in the floor. Sharpening the lobe made it a stiller puddle.
    //   * killing the rings globally made the whole patch stop moving at once,
    //     which is a switch, not a freeze.
    // And what it never had was the thing that separates a solid from a liquid
    // at a glance: THICKNESS, an EDGE, and a surface that is not flat.
    //
    // WHAT IT IS NOW — five properties of a real sheet of ice on a flagstone,
    // each of them a cue the eye reads without being told:
    //   1. IT GROWS FROM THE RIM INWARD (ElemIceMask). A puddle freezes at its
    //      shallow edge first and closes in the middle last, so partial Ice is a
    //      ring of ice around open water — and the drip still rings the water
    //      that is left, which is the mixture Air+Ice needs anyway.
    //   2. IT HAS A FRONT AND AN EDGE (the `lip`): a raised, whiter band where
    //      the sheet is still growing, and the sheet's own thick rim where it
    //      meets stone. An edge is what a solid has and a wet patch does not.
    //   3. IT IS NOT FLAT (IceH/IceN). Freezing water expands and DOMES, and the
    //      advancing front leaves radial dendrites behind it. The relief is
    //      STATIC — no clock enters it — and that stillness is doing as much
    //      work as the shape: water moves, ice does not.
    //   4. IT SCATTERS INSTEAD OF MIRRORING. The specular lobe BROADENS with the
    //      freeze, and a view-independent body term is added on top, so the sheet
    //      is bright from every angle rather than only from the one place that
    //      catches the moon. This is the single biggest reason the old version
    //      read as a puddle: a surface you can only see from one seat is water.
    //   5. IT HAS THINGS IN IT (IceGrain): trapped air bubbles and radial cracks,
    //      both static, both impossible in a liquid.
    // Air still ruffles what is left unfrozen, so Air+Ice is a half-frozen
    // puddle with the wind working the open middle — a mixture, not an average.

    /// How far in the freeze front has come, as a value of the mesh's own
    /// normalised radius (uv.y: 0 centre, 1 rim). 0.98 = nothing but the very
    /// rim, -0.18 = past the centre, i.e. closed.
    float ElemIceFront (float ice)
    {
        return 0.98 - 1.16 * ice;
    }

    /// 0 = open water, 1 = under the sheet. `f` is uv.y.
    float ElemIceMask (float f, float ice)
    {
        float fr = ElemIceFront(ice);
        return smoothstep(fr, fr + 0.15, f);
    }

    /// The sheet's own surface height, in metres of relief. A dome plus the
    /// dendrites the front leaves behind. NOTHING HERE IS ANIMATED, and that is
    /// deliberate: the resting puddle breathes (RippleH's `calm`), so a patch
    /// that has stopped breathing reads as frozen before any of the colour does.
    float IceH (float2 d, float r)
    {
        // NO ANGULAR TERM, and that is a correction rather than a preference.
        // The first two bakes built the dendrites out of sin(ang * k), which is
        // the obvious way to write "radial ridges" and produces, at any k, a
        // ROSETTE: k identical spokes meeting at a singular point, i.e. a flower
        // painted on the floor. Real ice on a puddle is a quilt of interlocking
        // PLATES, and plates are a cartesian phenomenon — so the relief is three
        // crossing plane waves at incommensurate angles and frequencies, which
        // has no centre, no symmetry and no singularity...
        float h = -0.35 * r * r                           // ...over the dome the
                + 0.022 * sin(d.x * 23.0 + d.y *  9.0)    // freeze lifts
                + 0.015 * sin(d.x * -11.0 + d.y * 27.0 + 1.7)
                + 0.009 * sin(d.x * 41.0 - d.y * 37.0 + 3.1);
        // ...and ONE radial family, weak and concentric: the growth rings the
        // front leaves behind it as it closes inward. Concentric rings have no
        // spokes, so this is the one place a radial term is safe.
        return h + 0.010 * sin(r * 38.0);
    }

    /// ...as a normal. Central-differenced over 1.5 cm for the same reason the
    /// wind waves are: an analytic gradient of three nested sines is more
    /// transcendentals than the difference, for a vector that is normalised
    /// afterwards anyway.
    float3 IceN (float2 d, float r)
    {
        float h0 = IceH(d, r);
        float2 dx = d + float2(0.015, 0), dz = d + float2(0, 0.015);
        float2 g = float2(IceH(dx, length(dx)) - h0, IceH(dz, length(dz)) - h0)
                   * (_IceRelief / 0.015);
        return normalize(float3(-g.x, 1.0, -g.y));
    }

    /// What is INSIDE the sheet: x = trapped air bubbles, y = cracks.
    ///
    /// The bubble field is a product of four incommensurate spatial sines raised
    /// to a power — isolated bright specks at irregular spacing. NOT a hash, and
    /// deliberately not: EnvElement.cginc rule 4 bans sin() inside a hash because
    /// its last bits differ between GPU vendors and a hash whose last bits differ
    /// picks a different branch. A sine used as a spatial CURVE has no branch to
    /// pick — an ulp of phase error moves a bubble by a micron — so this is the
    /// cheap way to get scattered specks that every client agrees about.
    ///
    /// The cracks are radial because a puddle that froze from the rim inward
    /// cracks toward its own centre. A crack is the sharpest EDGE anything in
    /// this puddle has, which is why it says "solid" so cheaply.
    float2 IceGrain (float2 p, float2 d, float r)
    {
        float b = sin(p.x * 61.0 + 0.7) * sin(p.y * 47.0 + 1.3)
                * sin(p.x * 29.0 - 2.1) * sin(p.y * 37.0 + 0.4);
        float bub = pow(saturate(b), 9.0);
        // The cracks are measured from a point OFF the puddle's centre (7 cm
        // downwind), so they do not all radiate from the same pinhole the
        // dendrites already own — the first bake put both at the origin and the
        // sheet acquired a hub. 0.032 rather than 0.055: a crack in ice is a
        // hairline, and a wide one reads as a join.
        float2 dc = d - float2(0.07, -0.04);
        float ang = atan2(dc.y, dc.x);
        float c = abs(sin(ang * 2.5 + 0.9) * sin(ang * 1.5 - 2.2));
        float crk = (1.0 - smoothstep(0.0, 0.032, c)) * smoothstep(0.05, 0.26, r);
        return float2(bub, crk);
    }

    /// The freeze front's raised lip, and — once the sheet has closed over the
    /// middle — its own thick edge against the stone. Both are the same feature
    /// seen at two moments of the same freeze, so they are one term.
    float IceLip (float f, float ice)
    {
        float fr = ElemIceFront(ice);
        float a = f - fr;
        float front = exp(-a * a * 169.0);                     // sigma ~ 0.077
        float b = f - 0.88;
        float edge = exp(-b * b * 256.0) * saturate(ice * 1.7 - 0.7);
        return max(front, edge);
    }

    /// Ripple amplitude multiplier. Exactly 1 when nothing is up. The ICE part
    /// is per-fragment now (`iceMask`) rather than a global fade: the open water
    /// inside a partly frozen puddle goes on ringing, which is both true and the
    /// only way Air+Ice can be a mixture rather than an average. 0.06 rather
    /// than 0 under the sheet — a thin sheet over water still flexes, and an
    /// exact 0 made the freeze read as a pause button.
    float ElemRippleAmp (GhvrElem e, float iceMask)
    {
        return max((1.0 - 0.94 * iceMask) * (1.0 + 1.8 * e.air), 0.0);
    }

    float RippleH (float r, float t, float amp)
    {
        // 0 exactly when the drop from EnvDrip touches the water: same clock,
        // same period, same phase, minus the hang+fall the drop spends in the air
        float P = max(_Period, 0.05);
        float w = frac((t + _Phase - _Impact) / P);
        float ringR = w * _Radius * 1.25;                  // the front runs outward
        float rel = r - ringR;
        float train = sin(rel * _RingFreq * 6.2831853) * exp(-abs(rel) * 3.0);
        float decay = (1.0 - w) * (1.0 - w);               // the ring dies as it spreads
        // the PREVIOUS drop's train is still on the water when the next lands
        float w2 = frac((t + _Phase - _Impact) / P + 0.5);
        float rel2 = r - w2 * _Radius * 1.25;
        float train2 = sin(rel2 * _RingFreq * 6.2831853) * exp(-abs(rel2) * 3.0)
                       * (1.0 - w2) * (1.0 - w2);
        float calm = sin(r * 9.0 - t * 1.1) * 0.5 + sin(r * 5.3 + t * 0.7) * 0.5;
        return ((train * decay + train2 * 0.55) * _RingAmp + calm * _Calm) * amp;
    }

    /// ELEMENT ART — AIR: one train of cat's paws, running along the draught.
    /// Crosswise it is broken up by a slow second wave, because a wind ripple on
    /// water is a set of short crests that do not line up, not a corrugation.
    /// Returned in the same "slope units" as RippleH so the two can be added.
    float WindH (float2 p, float t)
    {
        float2 d = p - _Center.xz;
        float u = dot(d, _DraftDir.xz);                          // along the draught
        float v = dot(d, float2(-_DraftDir.z, _DraftDir.x));     // across it
        return sin(u * 7.3 - t * 3.1 + sin(v * 3.7 + t * 0.6) * 0.85);
    }

    float3 RippleN (float3 opos, float t, float amp)
    {
        float2 d = opos.xz - _Center.xz;
        float r = length(d);
        float h0 = RippleH(r, t, amp);
        float h1 = RippleH(r + 0.012, t, amp);
        float slope = (h1 - h0) / 0.012;
        float2 dir = d / max(r, 1e-4);
        return normalize(float3(-dir.x * slope, 1.0, -dir.y * slope));
    }
    ENDCG

    SubShader
    {
        // after the opaque floor, before anything transparent: the puddle is
        // part of the floor, and nothing may sort against it
        Tags { "Queue"="Geometry+20" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Back
        Offset -1, -1

        // ---- 1. the wet patch: darken the stone underneath ----
        Pass
        {
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                // The rings have to be visible even when the water is mirroring
                // nothing: a reflection only exists at the one viewing angle that
                // catches the moon, and a ripple that can only be seen from there
                // is a ripple nobody sees. So the ring train also modulates how
                // dark the wet stone is — which is what a real ripple does, by
                // tilting the surface between "you see the dark bottom" and "you
                // see the sky".
                float t = _Time.y + _GhvrTimeOfs;
                GhvrElem e = GhvrElems();
                float2 d = i.opos.xz - _Center.xz;
                float r = length(d);
                float iceMask = e.ice > 0.0 ? ElemIceMask(i.uv.y, e.ice) : 0.0;
                float h = RippleH(r, t, ElemRippleAmp(e, iceMask));
                if (e.air > 0.0) h += _DraftWave * e.air * WindH(i.opos.xz, t);
                float m = saturate(i.vcol.a);
                float3 wet = _Wet.rgb * (1.0 + _RingCon * h);
                if (e.ice > 0.0)
                {
                    // THE SHEET, as the floor sees it: pale, low-contrast and
                    // OPAQUE-ish. Ice does not darken a flagstone the way water
                    // does — it hides it. So the multiply stops multiplying by
                    // 0.46-ish and starts multiplying by 0.74-ish, which is what
                    // "the stone went away and a white solid is lying on it"
                    // looks like through a pass that can only scale.
                    float3 sheet = _IceCol.rgb;
                    // ...shaded by its own STATIC relief, not by the ring train:
                    // the frozen part must stop pulsing. The dome makes the
                    // middle of the sheet the brightest part of it, which is
                    // exactly what a lifted freeze looks like from above.
                    sheet *= 1.0 + 0.55 * _IceRelief * IceH(d, r);
                    float2 g = IceGrain(i.opos.xz, d, r);
                    sheet *= 1.0 - 0.45 * g.y;                 // cracks are the dark thing in it
                    sheet = lerp(sheet, float3(0.95, 0.98, 1.02),
                                 saturate(IceLip(i.uv.y, e.ice) * 0.45));
                    wet = lerp(wet, sheet, iceMask);
                }
                return fixed4(lerp(float3(1,1,1), saturate(wet), m), 1.0);
            }
            ENDCG
        }

        // ---- 2. what the water reflects ----
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y + _GhvrTimeOfs;
                GhvrElem e = GhvrElems();
                float2 d2 = i.opos.xz - _Center.xz;
                float rr = length(d2);
                float iceMask = e.ice > 0.0 ? ElemIceMask(i.uv.y, e.ice) : 0.0;
                float3 N = RippleN(i.opos, t, ElemRippleAmp(e, iceMask));
                if (e.air > 0.0)
                {
                    // The cat's paws tilt the surface too, and that is where they
                    // are actually SEEN: the moon's reflection breaks into bands
                    // that crawl toward the stair door. Central-differenced over
                    // 2 cm — an analytic gradient of the nested sine is three
                    // more transcendentals for a normal that is then normalised
                    // anyway. Left entirely outside the resting path so the
                    // no-element frame keeps its exact radial normal.
                    float2 p = i.opos.xz;
                    // 0.30 of the authored slope, and the factor is arithmetic
                    // rather than taste: the wave's own spatial frequency is 7.3
                    // rad/m, so its DERIVATIVE is ~7x its amplitude. Feeding the
                    // amplitude that the wet-darkening pass wants straight into a
                    // normal would tilt the surface by more than a right angle
                    // and the reflections would go to noise.
                    float a = _DraftWave * e.air * 0.30;
                    float w0 = WindH(p, t);
                    float2 gr = float2(WindH(p + float2(0.02, 0), t) - w0,
                                       WindH(p + float2(0, 0.02), t) - w0) * (a / 0.02);
                    N = normalize(float3(N.x - gr.x, N.y, N.z - gr.y));
                }
                // ELEMENT ART: the sheet's own relief tilts the surface, and it
                // is what breaks the moon's image into glints along the
                // dendrites. Blended in with the mask rather than replacing the
                // water normal, so a half-frozen puddle really is half of each.
                if (e.ice > 0.0)
                    N = normalize(lerp(N, IceN(d2, rr), iceMask));

                float3 V = normalize(i.ov);
                float3 R = reflect(-V, N);

                // water only mirrors properly at a grazing angle; seen from
                // straight above it is a dark hole in the floor
                float fres = lerp(1.0, pow(1.0 - saturate(dot(N, V)), 2.5), _Fresnel);

                // MOONLIGHT, on the contract's own call-site form. Everything the
                // water mirrors OF THE MOON has to die with the moon, or the one
                // wet surface in the cellar goes on showing a moon that has been
                // eclipsed. (The candle shard does not: Dark never dims a source.)
                float moonGain = 1.0;
                if (e.live > 0.0) moonGain = GhvrDirGain(e) * GhvrMoonLight();

                // Two lobes for the moon, not one. The tight lobe IS the mirror
                // image and only exists from the one place you can stand to see
                // it; the broad one is the sky around the moon, which a rippled
                // puddle scatters over most of the hemisphere — that is the term
                // that makes the water read as water from anywhere in the room.
                //
                // ELEMENT ART: ICE BROADENS THE LOBE, it does not sharpen it, and
                // this line used to say the opposite. Ice is a rough scattering
                // dielectric — frosted, micro-fractured, full of trapped air —
                // and it is DULLER and WIDER than still water, not a better
                // mirror. Sharpening it is what made the frozen puddle read as a
                // very calm puddle. Light drives every reflected SOURCE.
                float md = saturate(dot(R, normalize(_MoonDir.xyz)));
                float moon = pow(md, _MoonPow / (1.0 + 3.4 * iceMask)) + 0.16 * pow(md, 3.0);
                // ...and it reflects far less of it. 0.80, not 0.45: broadening
                // an exponent from 45 to 10 turns a glint into a lobe covering
                // most of the hemisphere, so the SAME coefficient delivers three
                // times the light — the second bake's sheet was mostly a very
                // wide mirror of the moon and read as wet glass. A rough
                // dielectric scatters most of what it does not transmit, and the
                // scattering is the `body` term below, where it belongs.
                moon *= 1.0 - 0.80 * iceMask;
                moon *= moonGain;

                float3 toC = _CandPos.xyz - i.opos;
                float cd2 = max(dot(toC, toC), 1e-4);
                float cand = pow(saturate(dot(R, toC * rsqrt(cd2))), _CandPow) / (1.0 + cd2 * 0.07);

                // same sine family, rate and phase as the candle's EnvRoom slot
                float ft = t * _CandRate;
                float f = 0.42 * sin(ft * 11.3 + _CandPhase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _CandPhase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _CandPhase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _CandPhase * 0.5);
                cand *= 1.0 + _CandCol.a * 0.35 * f;

                // ...plus the plain sheen of a wet surface: grazing angles see
                // the sky, and the ripple slope decides which way each band tips
                float sheen = fres * (0.65 + 0.35 * saturate(N.y * 4.0 - 3.0)) * moonGain;
                // the candle's shard warms and grows with Fire; every reflected
                // source follows the split's source gain (Light lifts, Dark does
                // not dim — see EnvElement.cginc)
                //
                // THIS PUDDLE IS INDOORS, so since ModBuild 146 GhvrSrcGain is
                // the exact identity here and the line below multiplies by 1.0
                // whatever Light and Dark are doing. It is kept, rather than
                // deleted, because it is the correct expression and it costs a
                // multiply by a uniform-derived constant: the shader states that
                // a reflection follows the room's source gain, and the room's
                // source gain happens to be 1 in the only room a puddle stands
                // in. Two things fall out and both are wanted:
                //   * THE CANDLE'S SHARD DOES NOT MOVE under Light or Dark, which
                //     is the standing ruling ("die Kerzenscheine, die sollten
                //     identisch bleiben") applied to the one surface in the
                //     cellar that mirrors a candle.
                //   * THE MOON'S IMAGE STOPS BEING DOUBLE-COUNTED. `moon` above
                //     already carries GhvrDirGain x GhvrMoonLight; multiplying
                //     the reflected moon by the source gain as well made the
                //     puddle 2.10x brighter than the moonlight it was reflecting
                //     (5.36x against the beam's 2.55x under full Light), i.e. the
                //     one mirror in the room disagreed with its own subject.
                //     It is now 3.22x, the same as the beam and the pool.
                float3 col = _MoonCol.rgb * moon
                           + _CandCol.rgb * (cand * (1.0 + 0.85 * e.fire))
                           + _SkyCol.rgb * sheen;
                col *= GhvrSrcGain(e);
                // EVERYTHING ABOVE IS A REFLECTION, so it is gated by Fresnel:
                // it only exists at the angles that catch a source. THE ICE IS
                // NOT. A sheet of ice is a scattering solid — it is bright from
                // directly above, where water is a black hole in the floor — so
                // its body is added OUTSIDE the Fresnel gate, and that one
                // structural difference is most of what stops the frozen puddle
                // from reading as a paler puddle.
                float3 body = float3(0, 0, 0);
                if (e.ice > 0.0)
                {
                    float2 g = IceGrain(i.opos.xz, d2, rr);
                    // the sheet itself: its own relief shades it, the dome making
                    // the middle the brightest part
                    body = _IceCol.rgb * (_IceBody * iceMask * (0.45 + 0.55 * saturate(N.y)));
                    // trapped air — hard little white specks INSIDE the solid
                    body += float3(1, 1, 1) * (g.x * 0.22 * iceMask);
                    // the thick edge / the growing front, which is where a sheet
                    // of ice is whitest and where its THICKNESS is legible at all
                    body += float3(0.92, 0.97, 1.02) * (IceLip(i.uv.y, e.ice) * 0.26 * iceMask);
                    body *= 1.0 - 0.55 * g.y;                  // cracks stay dark
                    // ...and it is lit by the room: mostly by the moon through
                    // the window (it lies in the beam's own pool), partly by the
                    // candles, so it goes out with the moonlight under Dark
                    // instead of glowing on in a black cellar.
                    body *= (0.35 + 0.65 * moonGain) * GhvrSrcGain(e);
                }
                return fixed4((col * fres + body) * saturate(i.vcol.a), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
