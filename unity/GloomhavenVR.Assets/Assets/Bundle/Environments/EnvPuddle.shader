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
// (the mirror image, the sky sheen, the shoreline, the light on the sheet) is
// multiplied by the shared GhvrDirGain(e) * GhvrMoonLight(), so the reflection
// of the moon and the moon cannot be in different states.
//
// ============================================================================
//  THE PUDDLE VANISHED — ModBuild 147, and it did NOT stop being drawn
// ============================================================================
//  USER, hardware, verbatim: "Außerdem ist die Pfütze ganz verschwunden,
//  wieso?" — together with "Bei der Dunkelheit im Keller ist wo die Pfütze war
//  immer noch ein heller Fleck, obwohl der Mond nicht mehr scheint" and "Die
//  'Wellen' die durch den Tropfen entstehen sind viel zu extrem und überhaupt
//  nicht immersiv. Das soll ganz dezent sein und auch nur wenn ein Tropfen auf
//  die Pfütze trifft."
//
//  THE FIRST THING RULED OUT, because this project has shipped FIVE meshes wound
//  against the side they are seen from and this very mesh was one of them (see
//  PuddleMesh's own winding note, invisible for ten builds). A LOCATE pass —
//  both puddle passes forced to flat magenta, rendered twice, once with the
//  shipped `Cull Back` and once with `Cull Off`:
//        Puddle view      45 895 px  (Cull Back)   45 895 px  (Cull Off)
//        PuddleLow view   88 460 px  (Cull Back)   88 460 px  (Cull Off)
//  Identical to the pixel. The mesh is wound correctly, every triangle survives
//  the cull, and both passes run. "It is not being drawn" is dead, measured.
//
//  WHAT IT ACTUALLY IS. A second pair of renders, one shipped and one with both
//  passes neutralised (pass 1 white, pass 2 black), isolates the puddle's own
//  contribution inside that 45 895 px footprint. At rest it is +0.0121 of mean
//  linear luminance over a floor sitting at 0.0079 — the puddle is not faint, it
//  MORE THAN DOUBLES the brightness of the patch it covers. It is not missing.
//  It is unrecognisable, and the frames say exactly why: it renders as a
//  structureless milky-blue veil with no edge, lying on top of the moonbeam's
//  landing pool, which is a structureless blue ellipse of the same colour. Two
//  smudges of one colour in one place is one smudge.
//
//  THREE CAUSES, and ModBuild 147's slope correction is only the third:
//
//   V1. THE SKY SHEEN WAS A FLAT WASH. `sheen` was gated by `fres`, and `fres`
//       is lerp(1, pow(1-N.V, 2.5), _Fresnel) with _Fresnel authored at 0.50 —
//       i.e. HALF of it is angle-independent, a pedestal that no viewing angle
//       can remove. Measured across this view the sky term varied only 0.27 to
//       0.54 of _SkyCol and never fell below a quarter of it, which on a floor
//       lit at 0.008 is a five-fold flat lift over the whole disc. That is the
//       milk, and it is the single largest term in the puddle. A sky reflection
//       off water is very nearly ALL grazing — look straight down at still water
//       and you see the bottom, not the sky — so the sky now carries its own
//       full Fresnel, pow(1-N.V, 2.5), independent of _Fresnel, which keeps
//       governing the point-source lobes (a mirror image of a candle IS visible
//       near-normal, so a pedestal there is defensible). Same grazing brightness,
//       six times less wash when looked down at.
//   V2. THE PUDDLE HAD NO SHORE. PuddleMesh's own comment states the intent:
//       "its vertex ALPHA carrying the wet mask so the edge feathers into damp
//       stone instead of ending in a rim" — the mask ramps to zero over the
//       outer 45% of the radius. That was a reasonable choice while the water
//       was legible from its ripples; with the ripples correctly calmed it means
//       the object has no boundary anywhere, and a shape with no boundary is not
//       an object. Water in a dark room is read by its SHORELINE before anything
//       else: a definite wet edge, the darkest ring just inside it (shallow
//       water over soaked stone), and the bright hairline of the meniscus where
//       the water climbs the grit. All three are now built from `uv.y`, the
//       mesh's own normalised radius, which the ice already uses — no mesh
//       change, no new interpolator, and the rim follows the mesh's irregular
//       fbm outline so it reads as a shoreline and not as a drawn circle.
//   V3. THE RIPPLE WAS CARRYING THE VISIBILITY. ModBuild 147 cut _RingAmp
//       0.55 -> 0.022 and _Calm 0.06 -> 0.010, correctly: the surface was being
//       tilted to within four degrees of vertical. But every cue the puddle had
//       was riding on that tilt — the reflections swept about, `fres` mottled,
//       the N.y term in the sheen swung — so correcting the physics removed the
//       only structure the water had. The answer is NOT to give the slope back
//       (see THE RIPPLES below): it is that a still puddle must be legible while
//       still, which is V1 and V2.
//
//  AND THE BRIGHT PATCH THAT SURVIVES FULL DARK IS NOT THIS SHADER. Proven, not
//  argued, because the same mistake has been made here before. Inside the
//  puddle's own footprint, at Dark = 1, the frame with the puddle NEUTRALISED
//  measures 0.00733 mean linear against the shipped 0.00762 — the puddle
//  accounts for 4% of what is there. The other 96% is the moonbeam's landing
//  pool, "MoonPool"/"MoonPoolAir" (C_MoonPool.mat, C_MoonPoolAir.mat, shader
//  GloomhavenVR/EnvParticleAdd), an additive re-add of the floor's own albedo
//  whose mesh centre sits 31 cm from this one and whose AABB covers it. That
//  shader contains no moonlight term of any kind: measured over the lit pixels
//  of this view, at full Dark it retains p75 0.82 / p90 0.90 / p99 0.96 of its
//  resting brightness while every term in THIS file falls to
//  GhvrDirGain * GhvrMoonLight = 0.0275. The colour settles it independently:
//  the residue measures R/B = 0.34 (cold blue, the pool's tint over grey
//  flagstone) where the only ungated term this shader has under Dark is the
//  candle's reflected shard at _CandCol R/B = 5.0 (orange). The fix belongs to
//  whoever owns EnvParticleAdd/EnvParticleElem; it is one factor, and the report
//  carries it.
// ============================================================================
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
        // _Calm IS GONE. It was the RESTING ripple — a permanent pair of
        // concentric standing waves that ran whether or not a drop had landed,
        // and it is precisely what the user asked to be removed ("auch nur wenn
        // ein Tropfen auf die Pfütze trifft"). It is deleted rather than set to
        // zero: a knob that makes the water move when nothing has touched it is
        // a knob somebody turns back up. C_Puddle.mat still writes 0.010; the
        // write is inert and the bake lane should drop it.
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
    float _Radius, _Period, _Phase, _Impact, _RingFreq, _RingAmp, _RingCon;
    float _MoonPow, _CandPow, _CandRate, _CandPhase, _Fresnel, _DraftWave;
    float _IceBody, _IceRelief;
    float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

    // ======================================================== THE SHORELINE ==
    // See V2 in the header. These five are the puddle's own EDGE, in units of
    // uv.y (0 at the centre, 1 at the mesh's irregular rim), and they are
    // constants here rather than material properties for the same reason
    // GHVR_GROW_FULL is a constant in EnvGrowth.cginc: "a puddle has a shore" is
    // a requirement that came out of a user finding, not a level to be dialled.
    // The puddle is 0.72 m of authored radius, so one unit of uv.y is 72 cm and
    // the numbers below can be read straight off as centimetres.
    #define GHVR_PUD_SHORE_OUT 0.985   // dry stone beyond here (the mesh ends at 1)
    #define GHVR_PUD_SHORE_IN  0.905   // ...wet within: a 6 cm edge, not a 32 cm fade
    #define GHVR_PUD_DEEP_OUT  0.955   // shallow at the rim...
    #define GHVR_PUD_DEEP_IN   0.700   // ...deep over the middle 70%
    // How much darker the SHALLOW ring is than the deep middle. Shallow water
    // over soaked stone is the darkest thing in the whole patch — there is not
    // enough depth to reflect and the stone under it is at its wettest — and
    // that dark ring just inside the rim is most of what makes a puddle read as
    // a hollow with water in it rather than as a light on the floor.
    #define GHVR_PUD_SHALLOW   0.80
    // THE MENISCUS: a hairline where the water climbs the grit. It is placed
    // INSIDE the shore ramp on purpose (0.930 against SHORE_OUT 0.985) so that
    // the brightest line in the patch can never be the mesh's own polygon
    // boundary — a 30-segment rim drawn as a bright ring would read as a
    // faceted disc. sigma 0.038 is 2.7 cm: at the 1.5-3 m this is ever seen
    // from that is several headset pixels wide, i.e. a line the eye resolves.
    // GAIN 0.13, AND THE FIRST RENDER IS WHY THE NUMBER IS STATED THIS
    // PRECISELY. At 0.45 the shoreline came out as a neon ring — the brightest
    // thing in the room by a wide margin, and wide enough that the mesh's own
    // 30-segment outline read as a polygon. The meniscus has to be the thing
    // that TELLS you where the water ends, not the thing you look at: 0.13 puts
    // it at roughly twice the brightness of the water it borders, which is what
    // a wet edge does under a single cold light, and 0.026 (1.9 cm) keeps it a
    // line rather than a band.
    #define GHVR_PUD_LIP_AT    0.930
    #define GHVR_PUD_LIP_SIG   0.026
    #define GHVR_PUD_LIP_GAIN  0.13

    // ========================================================== THE RIPPLES ==
    // USER, ModBuild 147, verbatim: "Die 'Wellen' die durch den Tropfen
    // entstehen sind viel zu extrem und überhaupt nicht immersiv. Das soll ganz
    // dezent sein und auch nur wenn ein Tropfen auf die Pfütze trifft."
    //
    // Two requirements, and only the first is about amplitude. What the shipped
    // RippleH actually did between drops was: a permanent two-term standing
    // wave (`calm`, gone — see the Properties block), PLUS the previous drop's
    // train at 0.55 weight, PLUS the current train running the whole 2.85 s
    // cycle with an exp(-3|rel|) envelope 33 cm wide on a 72 cm puddle. Three
    // separate things, all of them continuous, and between them the entire
    // surface of the water was in motion at every instant of every scenario.
    // That is what "überhaupt nicht immersiv" is naming: not one wave that is
    // too big, but water that never stops.
    //
    // So the rest state is now EXACTLY STILL, by construction and not by
    // smallness: `live` is saturate(1 - w/LIFE), which is exactly 0 for
    // w >= LIFE, and it multiplies the only remaining term. For 2.05 s of every
    // 2.85 s cycle RippleShape returns a hard 0 and the normal is the exact
    // (0,1,0) of a mirror.
    //   LIFE 0.28 of the cycle = 0.80 s. In that time the front travels
    //   0.28 * 1.25 * 0.72 m = 25 cm at 0.32 m/s, which is what a
    //   gravity-capillary ring on standing water actually does — so the ring
    //   visibly expands and dies about a third of the way out rather than
    //   reaching the shore and reflecting, which real ones on a puddle this
    //   size do not do either.
    //   RISE 0.020 = 57 ms of attack, so the ring grows out of the impact
    //   instead of appearing whole; without it the first frame after w = 0 is a
    //   full-amplitude wave and the drop reads as a switch.
    //   TIGHT 9.0 gives an 11 cm radial envelope against the old 33 cm: ONE
    //   crest and its trough travelling outward — a ring — where before two and
    //   a half wavelengths covered most of the disc at once. This is the single
    //   biggest part of "dezent": it takes the disturbed area of the puddle from
    //   effectively all of it to about a quarter.
    #define GHVR_PUD_RING_LIFE  0.28
    #define GHVR_PUD_RING_RISE  0.020
    #define GHVR_PUD_RING_TIGHT 9.0
    // The wet-darkening's OWN ripple contrast, and it exists because the
    // builder handed the problem over in as many words (BuildEnvironmentRooms,
    // _RingCon): "`h` feeds TWO consumers whose sensitivities differ by about
    // forty times ... Scaling h to make the normal physical necessarily takes
    // the darkening down with it, from a +-38% band to about +-2%, and _RingCon
    // is capped at 1 so it cannot be bought back from out here. HANDED TO THE
    // SHADER LANE." Taken: pass 1 no longer reads the slope-scaled height at
    // all, it reads the dimensionless SHAPE and applies its own contrast. The
    // two are now independent by construction, which is what lets the normal be
    // as gentle as physics wants while the ring stays visible from an angle
    // that catches no reflection at all — the original reason the darkening
    // carries the ring in the first place.
    #define GHVR_PUD_WET_RING   0.16

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
        // .a WAS the puddle mask (1 deep, 0 at the rim) and is no longer read by
        // either pass: it is a 32 cm feather, and a feather is the one thing a
        // puddle may not have (THE SHORELINE). Both passes now cut the shape out
        // of uv.y instead. The channel is left in the interpolator because it
        // costs nothing beside the uv it travels with and because the mesh still
        // writes it — deleting it would be a change to a mesh this lane does not
        // own, for no gain.
        fixed4 vcol : COLOR;
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

    /// (The ripple field itself is further down — RippleShape/RippleH. It is ONE
    /// expanding train per drip and nothing else: the "permanent, much slower
    /// breathing so the puddle is never a mirror-flat sheet of glass" that used
    /// to be described here is exactly what the user asked to be removed, and a
    /// mirror-flat sheet of glass between drops is now the intended state.)
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
    //      meets stone. (This used to add "an edge is what a solid has and a wet
    //      patch does not". Half of that is no longer true: the water has a
    //      SHORELINE of its own since ModBuild 147 — see THE SHORELINE. The two
    //      do not compete, they are at different radii and made of different
    //      things: the ice's is a raised white band that travels inward as the
    //      element rises, the water's is a fixed dark ring and a hairline where
    //      the surface meets stone. A half-frozen puddle now shows both, which
    //      is what a half-frozen puddle looks like.)
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
    /// deliberate — though the argument for it has had to change. It used to be
    /// "the resting puddle breathes (RippleH's `calm`), so a patch that has
    /// stopped breathing reads as frozen before any of the colour does". The
    /// water no longer breathes at rest (THE RIPPLES), so that contrast is now
    /// only available for the 0.8 s after each drop lands — and it is still the
    /// right one: unfrozen water RINGS when the drop hits it and the sheet does
    /// not, which is a sharper statement than a permanent wobble ever was,
    /// because it is tied to a visible cause. ElemRippleAmp is what carries it.
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

    /// THE RING, dimensionless, in [-1,1] — the SHAPE only, with no amplitude in
    /// it. See THE RIPPLES above for the user verdict and for every constant.
    ///
    /// Two consumers with sensitivities forty times apart read this: the normal
    /// (through a finite difference, so it sees the shape TIMES the wavenumber)
    /// and the wet darkening (directly). Returning the shape rather than a
    /// height is what lets each scale it for itself.
    ///
    /// EXACTLY 0 BETWEEN DROPS, and that is the requirement rather than a
    /// tuning: `live` is 0 for w >= GHVR_PUD_RING_LIFE, so this returns a hard
    /// zero — not a small number — for 72% of every drip cycle, and the water is
    /// then a mirror with the exact normal (0,1,0).
    float RippleShape (float r, float t)
    {
        // 0 exactly when the drop from EnvDrip touches the water: same clock,
        // same period, same phase, minus the hang+fall the drop spends in the air
        float P = max(_Period, 0.05);
        float w = frac((t + _Phase - _Impact) / P);
        // the ring's whole life, and nothing outside it
        float live = saturate(1.0 - w * (1.0 / GHVR_PUD_RING_LIFE));
        float ringR = w * _Radius * 1.25;                  // the front runs outward
        float rel = r - ringR;
        float train = sin(rel * _RingFreq * 6.2831853)
                    * exp(-abs(rel) * GHVR_PUD_RING_TIGHT);
        // the crown grows out of the impact (RISE), then the ring dies as it
        // spreads (live*live). At w = 0 the attack is exactly 0, so there is no
        // frame in which a full-amplitude wave simply exists.
        return train * live * live * smoothstep(0.0, GHVR_PUD_RING_RISE, w);
    }

    /// ...as a surface height, in the "slope units" RippleN differentiates.
    float RippleH (float r, float t, float amp)
    {
        return RippleShape(r, t) * _RingAmp * amp;
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

    /// THE SHORE, cut out of the mesh's own normalised radius. See THE SHORELINE
    /// above for why a puddle needs one and why this is the coordinate to build
    /// it from (uv.y is what PuddleMesh writes and what the ice already reads;
    /// the mesh's rim is fbm-irregular, so a band at constant uv.y is an
    /// irregular shoreline and not a drawn circle).
    ///   x  WET   1 over the water, 0 on dry stone, across a 6 cm edge.
    ///   y  DEEP  1 over the middle, 0 at the rim — how much water there is to
    ///            reflect in and how dark the soaked stone under it looks.
    ///   z  LIP   the meniscus hairline where the water climbs the grit.
    /// All three are pure functions of a vertex-interpolated scalar: no clock,
    /// no view vector, nothing per-eye, and identical on two clients.
    float3 PuddleShore (float f)
    {
        float g = (f - GHVR_PUD_LIP_AT) / GHVR_PUD_LIP_SIG;
        return float3(smoothstep(GHVR_PUD_SHORE_OUT, GHVR_PUD_SHORE_IN, f),
                      smoothstep(GHVR_PUD_DEEP_OUT,  GHVR_PUD_DEEP_IN,  f),
                      exp(-g * g));
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
                float amp = ElemRippleAmp(e, iceMask);
                // THE RIPPLE, ON THIS PASS'S OWN SCALE. Not RippleH: the height
                // is scaled for a finite difference and arrives here forty times
                // too small (the builder's _RingCon note hands this over in as
                // many words — see THE RIPPLES). The dimensionless shape times
                // this pass's own contrast, so the two consumers are independent
                // and the ring stays legible from a seat that catches no
                // reflection at all. Exactly 0 between drops, so `wet` is
                // _Wet * depth to the bit for 72% of the cycle.
                float con = GHVR_PUD_WET_RING * RippleShape(r, t) * amp;
                // ...and the draught, which is Air's own statement and is
                // likewise given a contrast rather than a slope. Exactly 0 with
                // Air down. (_DraftWave still drives the NORMAL, in pass 2,
                // where it is a slope and belongs.)
                if (e.air > 0.0) con += 0.10 * e.air * WindH(i.opos.xz, t);
                // THE SHORE. `m` used to be the mesh's vertex alpha, a 32 cm
                // feather that left the puddle with no boundary at all — see V2
                // in the header. It is now a 6 cm edge, and the water is deeper
                // in the middle than at the rim.
                float3 sh = PuddleShore(i.uv.y);
                float m = sh.x;
                // SHALLOW WATER OVER SOAKED STONE IS THE DARKEST PART. There is
                // not enough depth there to reflect anything and the stone is at
                // its wettest, so the ring just inside the shore goes darker
                // than the middle — which is the cue that says "hollow with
                // water in it" rather than "light on the floor".
                float3 wet = _Wet.rgb * lerp(GHVR_PUD_SHALLOW, 1.0, sh.y)
                                      * (1.0 + _RingCon * con);
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
                float3 sh = PuddleShore(i.uv.y);

                // water only mirrors properly at a grazing angle; seen from
                // straight above it is a dark hole in the floor.
                //
                // TWO GATES, NOT ONE, AND THAT IS V1 OF THE VANISHING (header).
                //  `gz`   the true grazing term. A DIFFUSE SKY reflection off
                //         water is essentially all grazing — the sky term has to
                //         go to nothing when you look straight down at the
                //         surface, or the puddle is a flat wash of _SkyCol over
                //         its whole area, which is exactly what it had become
                //         (measured: never below a quarter of _SkyCol anywhere,
                //         five times the floor it lies in, edge to edge).
                //  `fres` the same curve with _Fresnel's authored pedestal still
                //         in it, kept for the POINT SOURCES only. A mirror image
                //         of a candle or of the moon's disc really is visible at
                //         a fairly steep angle — that is what a specular lobe
                //         is — so the pedestal is defensible there and only
                //         there. _Fresnel therefore keeps its shipped 0.50 and
                //         its shipped meaning, and no material value moves.
                float gz = pow(1.0 - saturate(dot(N, V)), 2.5);
                float fres = lerp(1.0, gz, _Fresnel);

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
                float mdc = dot(R, normalize(_MoonDir.xyz));
                float md = saturate(mdc);
                float moon = pow(md, _MoonPow / (1.0 + 3.4 * iceMask)) + 0.16 * pow(md, 3.0);
                // THE BROAD SHEEN IS NOT A SKY, IT IS A WINDOW. `_SkyCol` is
                // named for the outdoor case and applied here to a puddle four
                // metres underground, where the hemisphere over the water is a
                // stone ceiling except for one barred opening. A sheen of equal
                // strength in every direction is therefore wrong twice: it is
                // physically a lie about the room, and it is what made the water
                // read as a uniform milky disc rather than as a surface with a
                // near side and a far side. This is the same half-angle weight
                // the moon lobe already needs, taken unsaturated so the half of
                // the puddle facing away from the window is dimmed rather than
                // switched off — there IS bounce off the flagstones. 0.30..1.00.
                float winFace = saturate(mdc * 0.5 + 0.5);
                // ...and it reflects far less of it. 0.80, not 0.45: broadening
                // an exponent from 45 to 10 turns a glint into a lobe covering
                // most of the hemisphere, so the SAME coefficient delivers three
                // times the light — the second bake's sheet was mostly a very
                // wide mirror of the moon and read as wet glass. A rough
                // dielectric scatters most of what it does not transmit, and the
                // scattering is the `body` term below, where it belongs.
                //
                // 0.80 -> 0.92, ModBuild 151, AND IT IS THE OTHER HALF OF A
                // HAND-OVER THIS LANE COULD NOT COMPLETE LAST ROUND. The bake
                // lane's own note at C_Puddle's _IceBody (BuildEnvironmentRooms,
                // "HANDED TO THE SHADER LANE, honestly, because this lane may not
                // edit EnvPuddle") measured the sheet's peak at 0.478 linear
                // while the BODY term — the part that is supposed to make ice
                // read as a scattering solid — was capped at 0.083 by a material
                // value cut twice for brightness. So four fifths of the sheet's
                // light was this lobe: a very wide MIRROR, i.e. the one thing
                // that makes ice look like water, and the only term the cap could
                // not reach. Cutting the lobe to 0.08 and spending the budget on
                // the body instead is the same total light rearranged into the
                // half of it that carries structure — the plates, the bubbles and
                // the growing edge are all in `body`, and none of them is in
                // `moon`. See _IceBody 0.09 -> 0.20 in the same round.
                moon *= 1.0 - 0.92 * iceMask;
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
                // the sky, and the ripple slope decides which way each band
                // tips. ON `gz` AND NOT ON `fres` — see the two gates above.
                float sheen = gz * (0.65 + 0.35 * saturate(N.y * 4.0 - 3.0))
                            * (0.30 + 0.70 * winFace) * moonGain;
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
                // The point lobes take `fres` HERE rather than at the return, so
                // that the sky term can be gated by `gz` alone. Arithmetically
                // the moon and the candle are unchanged — they were multiplied
                // by fres exactly once before as well, on the way out.
                float3 col = (_MoonCol.rgb * moon
                              + _CandCol.rgb * (cand * (1.0 + 0.85 * e.fire))) * fres
                           + _SkyCol.rgb * sheen;
                col *= GhvrSrcGain(e);
                // A MIRROR NEEDS WATER IN IT. Over the shallow ring the surface
                // is a film on stone and reflects a fraction of what the middle
                // does; this is the other half of the depth cue the darkening
                // pass makes with GHVR_PUD_SHALLOW, and the two agree because
                // they read the same `sh.y`.
                col *= lerp(0.45, 1.0, sh.y);
                // THE MENISCUS — the one hairline that says "water", and the
                // cheapest legible thing in the whole shader. Where the surface
                // meets the stone it curves up the grit, so it presents every
                // angle at once and catches the sky from anywhere in the room:
                // deliberately NOT behind either grazing gate, which is what
                // makes it survive being looked straight down at, i.e. the pose
                // that kills every other term here.
                //   It IS moonlight, so it dies with the moon (moonGain) — under
                // full Dark the shoreline goes out with the rest of the room and
                // the candle's shard is what is left, which is the cellar's
                // standing ruling and not an exception to it.
                //   ...and it leans the same way the sheen does. A meniscus
                // presents every angle, but it is still only lit by what is in
                // the room: the far shore, curving toward the window, is
                // brighter than the near one. That asymmetry is also what stops
                // an even ring reading as a drawn outline.
                col += (_SkyCol.rgb + _MoonCol.rgb * 0.12)
                       * (sh.z * GHVR_PUD_LIP_GAIN * (0.35 + 0.65 * winFace) * moonGain);
                // EVERYTHING ABOVE IS A REFLECTION (bar the meniscus, which is a
                // curved surface and says so): it only exists at the angles that
                // catch a source, and each term is already behind its own gate —
                // `fres` for the point lobes, `gz` for the sky. THE ICE IS
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
                    //
                    // 0.35 -> 0.15 OF PEDESTAL, and it moves in the same breath
                    // as _IceBody's 0.09 -> 0.20 for one reason: the pedestal is
                    // what the sheet keeps when the moon is gone, so raising the
                    // body without lowering it would have re-created exactly the
                    // failure ModBuild 147 cut the body for — "under {Dark, Ice}
                    // an ungated ice sheet is the only bright thing left in a
                    // room the user has just asked to go black". The two numbers
                    // are chosen together so the DARK end does not move:
                    //   was  0.09 * (0.35 + 0.65 * 0.0275) = 0.0331
                    //   now  0.14 * (0.15 + 0.85 * 0.0275) = 0.0243
                    // i.e. the {Dark, Ice} residual falls by 27% rather than
                    // merely holding, while the RESTING body goes 0.090 -> 0.140.
                    // The sheet gets its solidity back at rest and gets FURTHER
                    // out of the way in the dark, which is the pair of things
                    // ModBuild 147 wanted and could only have one of.
                    body *= (0.15 + 0.85 * moonGain) * GhvrSrcGain(e);
                }
                // ...and the whole thing is bounded by the SHORE and no longer by
                // the mesh's vertex alpha. `sh.x` reaches 0 at uv.y = 0.985,
                // inside the last ring of vertices, so nothing can leak past the
                // geometry — and the puddle now ends somewhere instead of fading
                // out over the outer 45% of its own radius.
                return fixed4((col + body) * sh.x, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
