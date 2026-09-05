// ============================================================================
//  SURFACE GROWTH — FROST as a COVERAGE THAT ADVANCES, the GROW-IN of the cards
//  Earth brings up, and the WIND that moves the things that grew.
//
//  This is a companion to EnvElement.cginc, which owns the CHANNEL (_GhvrElemA/B,
//  the master switch, the periphery ramp) and whose five rules this file obeys
//  without restating them. What lives here is the one mechanism three shaders
//  need to spell identically — EnvRoom (cellar masonry AND forest trunks),
//  EnvGround (forest floor) and EnvRoomCutout (every alpha-cut card) — because
//  a wall and the flagstone at its foot frosting two different frosts is worse
//  than neither frosting at all.
//
//  ============ THERE IS NO PAINTED MOSS IN THIS FILE ANY MORE ============
//  It was removed in full — not disabled, not faded to zero — on the FOURTH
//  rejection of it. Read THE MOSS IS GONE below before adding anything that
//  puts a colour on a surface under Earth. That block is the whole argument and
//  it is the reason this header no longer says "frost and moss".
//  ========================================================================
//
//  USER FINDING, ModBuild 142 (hardware, verbatim):
//    "Beim Eis würde ich gerne Frost auf dem Boden wachsen sehen an den Wänden
//     oder Bäumen - nicht komplett flächendeckend aber animiert und immersiv."
//    "Bei der Luft bzw Wind möchte ich das die Blätter der Bäume wackeln!"
//
//  ModBuild 142 answered the first with a FADE — alb = lerp(alb, colour,
//  intensity * k) — and that is why he asked again. A fade over a whole wall
//  says "this wall is being LIT differently"; what he asked for says "this wall
//  is being COVERED". The difference is not the colour. It is that a covering
//  has a FRONTIER: an edge, in an irregular place, that moves.
//
//  THE MECHANISM, and it is deliberately ONE idea used twice (ice, and — as a
//  threshold on position rather than on a surface — which clumps of the growth
//  cards have come up yet):
//
//    * every pixel computes an AFFINITY A in 0..1: how eager THIS pixel is to be
//      grown on. It is a property of the SURFACE and not of the element — the
//      joint between two stones, the foot of a wall, the shaded side of a trunk,
//      the damp hollow. It does not move when the element does.
//    * the element's intensity slides a THRESHOLD T down through A. Pixels with
//      A above T are covered. Coverage therefore ADVANCES through a fixed
//      pattern as the element rises and RETREATS through the same pattern as it
//      falls — which is what growth looks like, and what no fade can look like,
//      because a fade has no edge to advance.
//    * T starts ABOVE the top of A's range. At intensity 0 nothing is covered —
//      not "almost nothing", nothing, by construction and not by tuning
//      (GhvrGrow below, and the zero-state rule in EnvElement.cginc).
//    * T stops at GHVR_GROW_FULL, comfortably inside A's range, so at FULL
//      strength the surface is PARTLY covered. "Nicht komplett flächendeckend"
//      is a requirement, so it is a constant here and not a level in a material.
//
//  WHY THE AFFINITY IS BUILT THE WAY IT IS. Three terms, and each answers a
//  different half of "where would this actually start":
//    field  a value-noise cloud in ROOM METRES — the patchiness. This is what
//           makes the frontier irregular, and it is sampled in object space
//           relative to the room centre (see GhvrGrowQ), so two identical walls,
//           two ferns off the same photoscan and forty trunks welded into one
//           mesh all grow DIFFERENT patterns without a seed, a Random or a
//           per-instance anything.
//    grain  the surface's OWN relief, read off the normal map that was sampled
//           anyway. This is the second octave, and taking it from the material
//           instead of from a second noise is the point rather than a saving:
//           the frontier then follows the mortar courses of THIS stone and the
//           fissures of THIS bark, which is what makes it read as the surface
//           changing instead of as a pattern laid over it.
//    place  what each surface knows about itself that the other two cannot —
//           height above the floor, how much sky it can see, which side of it
//           the moon is on, whether it is the wet half of the ground. The three
//           consumers compute this differently, which is what makes frost start
//           where cold actually settles on THAT surface rather than everywhere
//           at once.
//
//  REJECTED, and why:
//    * a second noise octave (8 more hashes/pixel) — `grain` is free and truer.
//    * triplanar or dominant-axis projection of a 2D noise, to save four
//      hashes. The dominant-axis form puts four hard seams down every trunk,
//      exactly where a patch of frost would appear to be cut with a knife.
//    * a baked coverage/AO texture per surface. It would be cheaper per pixel
//      and it is what an offline renderer would do, but every wall, trunk band
//      and floor in both rooms is procedural and welded, so it would mean a
//      second UV set and a bake pass for a pattern that costs 60 ALU to invent.
//    * driving the threshold off the albedo (ModBuild 142's `alb.g` term). The
//      albedo of a photoscan carries its own baked lighting, so frost preferred
//      the LIT half of every surface — the opposite of where frost forms.
// ============================================================================
#ifndef GHVR_ENV_GROWTH_INCLUDED
#define GHVR_ENV_GROWTH_INCLUDED

// ------------------------------------------------------------- THE CHANNEL
// EnvElement.cginc is the ONE canonical quotation of ElementMood's contract and
// every consumer of this file should include it. Exactly one shader cannot:
// EnvRoomCutout needs the HAUNT schedule as well, and EnvHaunt.cginc declares
// _GhvrElemA/_GhvrElemB AND a struct named GhvrElems itself — it predates the
// shared include — so a shader that includes both gets a redefinition error on
// each, and no amount of include-guarding fixes it, because the guards are per
// FILE and the collision is per SYMBOL.
//
// So that shader reads the mood through EnvHaunt.cginc's own accessor
// (GhvrHauntElems), which is the same six values folded with the same master,
// and spells only the one field EnvHaunt does not carry — `live`, which is a
// single multiply. Nothing is duplicated: the contract has one quotation in
// EnvElement.cginc and one legacy quotation in EnvHaunt.cginc, and this round
// adds neither.
//
// What DOES have to be supplied is the periphery ramp, which EnvHaunt has no
// use for and therefore does not carry. It is four tokens long and it is the
// only thing in this file that shadows EnvElement.cginc, behind that file's own
// include guard so the two can never both exist.
//
// FOLLOW-UP owed to whoever next owns EnvHaunt.cginc: replace its two float4s
// and its GhvrElems struct with an #include of EnvElement.cginc, and this whole
// paragraph becomes untrue in the good way.
#ifndef GHVR_ENV_ELEMENT_INCLUDED
  #ifndef GHVR_ENV_HAUNT_INCLUDED
    #error "EnvGrowth.cginc needs the element channel: include EnvElement.cginc (preferred) or EnvHaunt.cginc first."
  #endif
/// GhvrRim, verbatim from EnvElement.cginc. See there for what the ramp is for.
float GhvrRim (float r, float rad, float lo)
{
    return smoothstep(lo, 1.0, saturate(r / max(rad, 0.01)));
}
#endif

// Where the frontier stops at FULL strength. The affinity of a typical grown
// surface is centred near 0.55 with a spread of about 0.16 (measured, see the
// coverage table the bake prints), so 0.60 leaves roughly two thirds of it bare
// at intensity 1. This single constant is "nicht komplett flächendeckend", and
// it lives here rather than in a material so that no room can quietly repaint
// itself past what the user asked for.
#define GHVR_GROW_FULL   0.60
// Half-width of the frontier, in affinity units. THE RATIO OF THIS TO THE
// SPREAD OF A IS THE WHOLE EFFECT: at 0.16 the frontier would be as wide as the
// affinity's entire range and the "edge" would be a gradient across the surface,
// which is the wash ModBuild 142 shipped. At 0.06 it is about 1.5 cm of stone —
// crisp enough to read as an edge, soft enough not to crawl or alias.
#define GHVR_GROW_EDGE   0.06

// -------------------------------------------------------------- THE FIELD
/// Hash for the value noise. Dave Hoskins' hash13 form — three frac() folds and
/// a dot, and NO sin(), which EnvElement.cginc rule 4 forbids inside a hash.
/// A last-bit difference between GPU vendors here moves a lattice VALUE by an
/// ulp, which moves the frontier by far less than a pixel; that is a different
/// thing from a hash that PICKS something, where an ulp is a different choice.
float GhvrGrowHash (float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

/// Trilinear value noise, 0..1. Eight hashes; the smoothstep on the fraction is
/// what keeps the frontier from showing the lattice it was built on.
float GhvrGrowNoise (float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = GhvrGrowHash(i + float3(0, 0, 0));
    float b = GhvrGrowHash(i + float3(1, 0, 0));
    float c = GhvrGrowHash(i + float3(0, 1, 0));
    float d = GhvrGrowHash(i + float3(1, 1, 0));
    float e = GhvrGrowHash(i + float3(0, 0, 1));
    float g = GhvrGrowHash(i + float3(1, 0, 1));
    float h = GhvrGrowHash(i + float3(0, 1, 1));
    float k = GhvrGrowHash(i + float3(1, 1, 1));
    float2 u = lerp(float2(lerp(a, b, f.x), lerp(c, d, f.x)),
                    float2(lerp(e, g, f.x), lerp(h, k, f.x)), f.z);
    return lerp(u.x, u.y, f.y);
}

/// The noise coordinate for a fragment: the room's own frame, in metres, times
/// the material's cell density.
///
/// `opos - centre` is the position RELATIVE TO THE ROOM CENTRE expressed in this
/// object's own axes, and both halves of that matter. In metres, because
/// `scl` (ApplyRig's _ElemScl) is the object's build-time scale, so a 0.45 m
/// patch is 0.45 m on a prop scaled 2.0 as well as on the welded floor. And in
/// the object's OWN axes, because that is what makes the pattern differ per
/// surface for free: the four cellar walls carry the same mesh in four yaws, so
/// the same fragment position maps to four uncorrelated points of the noise;
/// three ferns off one photoscan sit at three places, so they sample three
/// different clouds. No seed, no Random, no per-instance state — the variation
/// IS the placement, which is exactly the kind of variation that survives being
/// welded into one mesh and being rendered on two clients.
float3 GhvrGrowQ (float3 opos, float3 centre, float scl, float freq)
{
    return (opos - centre) * (scl * freq);
}

/// The patch field: value noise with its contrast stretched.
///
/// Raw trilinear value noise sits in a narrow band about 0.5 — its spread is
/// ~0.19, and most of that is near the lattice points. A frontier the width of
/// that band is not a frontier, it is a gradient across the whole surface, i.e.
/// exactly the wash this file exists to replace. Stretching by 2.4 and clipping
/// fills the range and flattens the distribution, so the threshold has hard
/// ground to bite on and the covered patches have real edges.
float GhvrGrowField (float3 p)
{
    return saturate((GhvrGrowNoise(p) - 0.5) * 2.4 + 0.5);
}

/// The affinity, assembled. Weights sum to 1 and every input is 0..1, so the
/// result is 0..1 — which is not cosmetic: GhvrGrow's exact zero depends on A
/// never exceeding 1.
///   0.46 field — the patchiness leads, or the frontier looks drawn. It is also
///                where nearly all of A's SPREAD comes from, and the spread is
///                what the frontier width is judged against.
///   0.18 grain — enough for the joints to catch it first, little enough that a
///                strongly bumped material is not simply covered everywhere.
///   0.36 place — it barely varies within one surface, so it acts as an OFFSET:
///                it decides which surfaces grow early and which late, which is
///                how "the foot of the wall" and "the shaded side" become
///                statements about coverage rather than about opacity.
float GhvrGrowA (float field, float grain, float place)
{
    return saturate(0.46 * field + 0.18 * grain + 0.36 * place);
}

/// THE FRONTIER. `cover` is the element's intensity for this material, already
/// weighted by susceptibility and by the periphery ramp; `creep` is a small
/// signed nudge (see GhvrGrowCreep) that keeps the edge alive at full strength.
///
/// At cover = 0 the threshold sits at 1 + GHVR_GROW_EDGE, so smoothstep's lower
/// bound is exactly 1.0 and A <= 1 returns EXACTLY 0. Not small: zero. That is
/// what lets a consumer write `lerp(alb, frost, GhvrGrow(...))` and still be
/// bit-identical with the element down, and it is why A is clamped above.
///
/// THE EASE. `cover` is squashed toward 1 before it drives the threshold, and
/// the first bake is why: linear in cover, a Waning element (ElementMood's 0.40
/// plateau) left the threshold at 0.83 while the affinity's mean is 0.55 and its
/// spread 0.18 — measured coverage 0.5%, i.e. an element the player is watching
/// fade would simply be gone, and the breath that is supposed to make it "live"
/// would be a breath of nothing. c*(2-c) puts 0.40 at 0.64 of the travel, so a
/// waning frost sits at a real, visibly moving fringe, and Strong is untouched
/// (c = 1 maps to 1). It is also the right SHAPE for growth: fast to establish,
/// slow to fill.
float GhvrGrow (float A, float cover, float creep)
{
    float c = saturate(cover);
    c = c * (2.0 - c);
    float T = lerp(1.0 + GHVR_GROW_EDGE, GHVR_GROW_FULL, c) - creep * c;
    return smoothstep(T - GHVR_GROW_EDGE, T + GHVR_GROW_EDGE, A);
}

// ---------------------------------------------------------- THE WAVEFORM
/// Four waves at once, each exactly in [-1,1]: the cubic-smoothed triangle wave
/// that Crytek, Unity's Tree Creator and SpeedTree all ship instead of a sine
/// (SpeedTreeWind.cginc calls it TrigApproximate; it approximates -cos(2*pi*x)
/// to about 2%). The argument is in CYCLES, not radians.
///
/// Three reasons it is here rather than sin(), and the third is a requirement:
///  * cost — frac/abs/mad only, no quarter-rate special-function unit, and one
///    float4 of INDEPENDENT work rather than a dependent chain of scalars (an
///    Adreno cat2 ALU result is not available for three instructions).
///  * precision — frac() keeps the argument bounded near zero forever, so the
///    animation does not go jerky after twenty minutes of _Time.y. The
///    composition stays continuous because frac's 1->0 jump is exactly two
///    periods of the period-1 triangle wave.
///  * THE AMPLITUDE IS PROVABLE. |wave| <= 1 by construction, not by tuning, so
///    a caller can state a displacement budget in centimetres and be right.
///    That is what lets the wind below be reconciled with a shadow map baked
///    against the static geometry, on paper, instead of by eye.
float4 GhvrWave4 (float4 v)
{
    v = abs(frac(v + 0.5) * 2.0 - 1.0);      // triangle, [0,1], period 1
    return (v * v * (3.0 - 2.0 * v) - 0.5) * 2.0;
}

/// The same wave, two lanes — for the STORM's two extra carriers below, which
/// are two and not four. Spelled out rather than passed to GhvrWave4 with two
/// zeroed lanes because the zeroed lanes are not free on every compiler and
/// because "there are exactly two extra carriers" is the cost claim this round
/// makes; a float4 call would hide a doubling of the wave cost behind a
/// dead-code elimination nobody re-checks.
///
/// Its maximum slope is the same and it is worth stating once, because Task A's
/// proof rests on it: with u = |frac(x+0.5)*2-1|, du/dx = +-2 and
/// d/du[(u*u*(3-2u)-0.5)*2] = 12u(1-u) <= 3, so |dW/dx| <= 6 per unit of
/// argument. An argument in CYCLES at rate f therefore has |dW/dt| <= 6f, for
/// EVERY carrier here, always.
float2 GhvrWave2 (float2 v)
{
    v = abs(frac(v + 0.5) * 2.0 - 1.0);
    return (v * v * (3.0 - 2.0 * v) - 0.5) * 2.0;
}

/// "Animiert": the frontier creeps even while the element holds still.
///
/// The element's own published value already breathes while it WANES (0.28-0.52
/// on a 2.4 s cycle, ElementMood), so a waning frost visibly advances and
/// retreats without any help. At STRONG it is pinned at 1.0 and would be a
/// still picture, which is the one state a player looks at longest. So the
/// threshold gets a slow travelling wobble: two waves crossing the room, worth
/// +-0.062 of coverage together. On a frontier 0.18 wide that moves the edge by
/// a few centimetres either way — ice creaking outward and back, never a
/// pulsing tint.
///
/// `t` MUST be the shared clock (_Time.y + _GhvrTimeOfs): EnvElement rule 3.
/// `q` is GhvrGrowQ's coordinate, i.e. cells; at the default density the two
/// waves are ~21 m and ~17 m long and travel at 0.5 and 0.3 m/s.
float GhvrGrowCreep (float3 q, float t)
{
    float4 w = GhvrWave4(float4(t * 0.049 + q.x * 0.021 + q.z * 0.014,
                                t * 0.027 - q.y * 0.026 + 0.31, 0.0, 0.0));
    return 0.040 * w.x + 0.022 * w.y;
}

/// The frontier AND its thickness, in one call — what a consumer actually wants.
///
/// A covering is not a decal of uniform opacity. It is thin where it has just
/// arrived and deep where it has been longest, and the field it grew through is
/// exactly that map, already computed. The first render of this feature skipped
/// the term and the frost came out looking POURED: an even, opaque white with a
/// hard rim, i.e. spilled paint rather than a crust. 0.74..1.00, so the surface
/// under it is thinned but never entirely gone.
float GhvrGrown (float field, float grain, float place, float cover, float creep)
{
    return GhvrGrow(GhvrGrowA(field, grain, place), cover, creep) * (0.74 + 0.26 * field);
}

// --------------------------------------------------------------- THE LOOK
// ======================= THE FROST IS A SOLID, NOT A BLUE ====================
//  USER VERDICT, ModBuild 151 (hardware, verbatim): "Im Keller die Blauen
//  Flecken von Eis, sehen nicht sehr wie Eis aus sondern eher wie
//  Wasserpfützen, gib den Eisflecken eventuell auch noch eine Eis-Textur statt
//  nur blau. (Gilt auch für den Wald)"
//
//  HE IS DESCRIBING WHAT THE CODE DID. GhvrFrostOn was, in full,
//      lerp(alb, float3(0.66,0.76,0.94) * (0.34 + 0.95*lum), m)
//  — a per-pixel lerp toward ONE CONSTANT BLUE, modulated only by the surface's
//  own green channel. No texture, no relief, no normal of its own. And both
//  call sites then went on to FLATTEN the surface's normal map:
//      n_ts.xy *= 1.0 - 0.62 * frost;      (EnvRoom:370, EnvGround:412)
//  A patch that is uniformly blue, perfectly smooth, and flatter than the stone
//  around it is a description of standing water. The complaint was exact.
//
//  WHY IT IS NOT THE MOSS'S MISTAKE AGAIN. A FUNCTION OF THE ALBEDO HAS NO
//  SILHOUETTE, and that is why the painted moss was deleted (see THE MOSS IS
//  GONE below) — but frost genuinely IS a film lying on a surface, inside that
//  surface's own outline. A film is the one thing a fragment function can draw
//  honestly. What the film was missing was not a silhouette; it was STRUCTURE.
//
//  WHERE THE STRUCTURE COMES FROM: EnvPuddle already ships a good sheet of ice
//  (its IceH / IceN / IceGrain / IceLip, EnvPuddle.shader:375-449) and its
//  vocabulary is ported here rather than reinvented, so the frozen puddle in
//  the corner and the frost on the wall behind it are made of the same four
//  ideas and cannot read as two different substances:
//    PLATES   three crossing plane waves at incommensurate directions and
//             frequencies, DOMAIN-WARPED so their level sets are not straight
//             (see THE STRIPES WERE A LATTICE below — the unwarped version of
//             this sentence is what ModBuild 149 shipped and what the user
//             photographed). NOT an angular term: EnvPuddle's own note records
//             that sin(ang*k) makes a ROSETTE — k identical spokes meeting at a
//             singular point, a flower painted on the floor — and a wall has no
//             centre to hang one on in the first place. Plates are a cartesian
//             phenomenon. Here the waves are 3D (a solid field, evaluated at the
//             fragment's position in ROOM METRES), which is what lets the same
//             function serve a wall, a floor, a barrel and a trunk with no
//             projection, no seams and no second UV set.
//    NORMAL   the ANALYTIC gradient of that height. The puddle central-
//             differences its IceH because it wants three fewer transcendentals;
//             here sincos gives the cosines for free alongside the sines that
//             were needed anyway, so the gradient costs three multiply-adds and
//             not three more wave evaluations.
//    GRAIN    trapped air (isolated bright specks, a product of two of the same
//             waves raised to a power) and the PLATE BOUNDARIES (a network of
//             BRIGHT hairlines where the coarse waves cross zero, appearing in
//             regions rather than everywhere). The puddle's cracks are radial
//             because a puddle freezes from its rim inward toward one centre; a
//             wall does not, so the boundary network is the honest form of the
//             same "this is the sharpest edge in it, and it is what says SOLID
//             cheaply".
//    RELIEF   and the normal is no longer flattened but REPLACED — the crust
//             buries a quarter of the stone's own bump and lays its own on top.
//             A frozen surface is more structured and more specular than a wet
//             one, not less.
//
//  COST, because this runs on every wall, floor and prop fragment in the cellar
//  and on the whole forest floor: six dot3, two sincos3, and about seventy mads
//  — +36 ALU and +6 transcendental slots against the ModBuild 142 function for
//  the crust itself, and the warp that ModBuild 150 added on top of it is +30
//  ALU and +3 transcendental more (three warp dots, one sincos3, six float3 mads
//  for the chain rule, one smoothstep). ALL of it inside the existing
//  `if (ice > 0.0)` branch, which is a uniform compare on a global. With Ice
//  down not one of these instructions is executed and the surface is
//  bit-identical to the shipped build.
//
//  REJECTED: sampling a tiling ice TEXTURE, which is what the verdict literally
//  asks for ("gib den Eisflecken eventuell auch noch eine Eis-Textur"). It needs
//  a second sampler and a second UV set on every one of the ~70 room materials
//  (the walls, floors, trunks and props are procedural and welded — the same
//  argument that rejected a baked coverage map at the top of this file), and it
//  would still be flat, because a texture without a normal is a picture of ice.
//  What he is asking for is that the patch stop being featureless; a solid field
//  with a real gradient answers that at a third of the bandwidth and no bake.
//  REJECTED: reusing GhvrGrowNoise for the relief. It is eight hashes for a
//  BLOBBY field, and ice is not blobby — it is faceted. The patch noise already
//  does the blobby job one level up, as the frontier.

// ================== THE STRIPES WERE A LATTICE, AND A CRACK IS BRIGHT ========
//  USER VERDICT, ModBuild 149 (hardware, verbatim, and BOTH rooms):
//    Cellar: "Das Eis auf dem Boden hat so komische schwarze Streifen (siehe
//            eis_boden.jpg)."
//    Forest: "Auch hier sind beim Eis diese schwarzen Streifen zu sehen wie im
//            Screenshot im Keller."
//  The photograph is a regular diagonal criss-cross of thin dark lines lying
//  over the whole frosted floor. It reads as painted-on lattice, or as grout.
//
//  TWO INDEPENDENT FAULTS, and this block fixes both. The paragraph that used
//  to stand under f.seam claimed "three incommensurate families cut the crust
//  into irregular cells, and the frontier and the patch field break the
//  residual regularity long before the eye can find it". The photograph is the
//  counter-example, and the claim was wrong on its own terms:
//
//  (1) INCOMMENSURATE PERIODS DO NOT BUY IRREGULARITY IN DIRECTION. Three plane
//      waves have three FIXED wave vectors; on a flat floor every zero crossing
//      of every family is a straight line, all of a family's lines are parallel,
//      and they run unbroken from wall to wall. Incommensurate |k| only means
//      the cells are not all the same SIZE — they are still a parallelogram
//      tiling, and a parallelogram tiling four metres wide is the most visible
//      thing in the room. MEASURED (autocorrelation of the seam signal along a
//      6 m line across a floor, 0.25 mm samples, eight bearings): the shipped
//      field peaks at +0.92 at a lag of 14 cm. That is not "residual".
//      THE FIX IS A DOMAIN WARP. Each family's phase is displaced by two of
//      three LONG waves (1.5-2.6 m) before it is evaluated, so a plate boundary
//      MEANDERS: its local wave vector turns by up to 22-24 degrees and its
//      phase wanders by +-4.2 rad, i.e. two thirds of a cycle either way over a
//      couple of metres. The warp is far too weak to fold the field (the worst
//      perturbation is 44% of the smallest |K|, so the level sets never double
//      back and no plate turns inside out) and it does not touch a single
//      amplitude, so the crust's height swing is the same +-0.046 to the digit.
//      MEASURED after: worst peak +0.17, i.e. 5.6x weaker, and the residue sits
//      at a different lag on every bearing — which is what "no periodicity"
//      looks like in this statistic, since a genuinely periodic signal peaks at
//      the SAME lag from every direction that crosses it.
//      REJECTED: value noise (GhvrGrowNoise) as the warp. It is genuinely
//      aperiodic and it is eight hashes PER WARP AXIS, i.e. ~240 ALU on every
//      frosted fragment of two whole rooms, against +30 for three more sines.
//      REJECTED: rotating the wave vectors per region. A rotation needs a
//      region, a region needs a boundary, and a visible boundary between two
//      tilings is worse than one tiling.
//
//  (2) A CRACK IN ICE IS BRIGHT. The shipped line was `ice *= 1 - 0.42*seam`
//      and its comment defended the sign: "a hairline that is DARKER than what
//      it separates is a crack; one that is brighter is a weld". That is the
//      wrong way round for ice and it is the whole of the word "schwarze". A
//      fracture surface inside ice is a mass of internal reflections — it
//      SCATTERS the light that reaches it, which is why every crack in a frozen
//      puddle, every plate boundary in lake ice and every pressure ridge
//      photographs WHITER than the sheet around it. Dark hairlines in a pale
//      surface are what grout is, and grout is exactly what he saw.
//      So the boundary now BRIGHTENS by 45%, and multiplicatively rather than
//      as an additive white: it rides the same (0.34 + 0.95*lum) the plate body
//      does, so a boundary in a black corner of the cellar stays dark and only
//      a lit one flares. Sharpness — not darkness — was always what read as
//      SOLID, and a bright hairline is exactly as sharp.
//      (EnvPuddle keeps its own cracks DARK and stays as it is. Nobody has
//      complained about them and the reason is legible in the code: there are
//      four of them, they are radial, they are confined to one 60 cm sheet and
//      they are paired with the bright IceLip. Four dark radials on a small
//      disc is a broken pane; a lattice of them across a whole floor is tiling.)
//
//  (3) AND THEY DO NOT COVER EVERYTHING. Even meandering and bright, a network
//      that is present at every point of the floor is a texture rather than a
//      feature. The boundaries are now gated by a coarse field built from two
//      of the warp waves that were computed anyway — free — so some square
//      metres of the crust are a clean sheet and others are visibly cracked.
//      MEASURED: the fraction of surface with seam > 0.5 falls from 15.8% to
//      5.1%, and the mean seam from 0.160 to 0.064.
//
//  WHAT IS DELIBERATELY UNCHANGED, because it is not what he objected to and
//  because the crust's structure was the whole point of the round before: the
//  dome height (9*h, +-41% of the plate body), the trapped air, the analytic
//  normal and GHVR_FROST_RELIEF. The gradient is still EXACT — the chain rule
//  through the warp is carried in ke0/ke1/ke2 below, verified against a central
//  difference to 1e-5 — so the shading still agrees with the height it claims.
// ----------------------------------------------------------------------------

// ============ THE CRUST PAINTS AT THE DEPTH ITS ROOM CAN CARRY ==============
//  USER VERDICT, ModBuild 150 (hardware, verbatim, and ONE room only):
//    "Eis: man sieht statt schwarze Streifen bzw. so ein Muster nun weiße
//     Muster im Keller. Im Wald fällt das nicht auf, da passt es."
//  eis_boden2.jpg is the cellar's flagstone floor under Ice=Strong: pale
//  feathery strokes lying across the stones with a fine bright hatch inside
//  them. The block above did its job — the black lattice is gone, and the wood,
//  which got exactly the same change, is accepted. So this is not a defect of
//  the field. It is a defect of its AMPLITUDE, in one room.
//
//  IT IS NOT A SHAPE BUG, and that was the first thing checked, because a warp
//  that is large against the boundary width would turn a hairline into a
//  ribbon and the honest fix would then be a narrower warp rather than a
//  quieter one. MEASURED, over a 7 m floor slab, the width of the |sin| < 0.11
//  band ALONG THE FLOOR (i.e. of |k| projected into the floor plane, which is
//  what a floor actually shows, and not of the 3D |k| the comment above quotes):
//  family 0 6.8/8.2/10.1 mm at p5/p50/p95, family 1 7.4/9.9/15.3, family 2
//  3.5/4.3/5.5 — against plate periods of 23.3, 28.2 and 12.2 cm. A boundary is
//  3-5% of a plate at every percentile, and it cannot become a ribbon: the
//  warp's worst perturbation of a family's |k| is 44%, so the width can at most
//  double. The strokes in the photograph are 20-40 cm across. They are PLATES,
//  seen at their own scale, and not boundaries that have swollen.
//
//  WHY THE SAME CRUST FITS ONE ROOM AND NOT THE OTHER. Rendered-pixel
//  decomposition at the two stations that actually show frosted floor (cellar
//  Puddle, forest FloorToMoon, Ice=Strong, each term zeroed in turn and diffed
//  against the shipped frame; the swing is the term's signed change AS A
//  FRACTION OF THE SAME PIXEL WITH THAT TERM OFF, i.e. of the surface it sits
//  on, at p05..p95 over the frosted floor, and the mean is area-weighted):
//                          CELLAR                     FOREST
//    dome  (albedo)   -27.6% .. +27.9%, mean 13.2%   -19.1% .. +19.2%, mean  5.5%
//    seam  (albedo)     +1.3% .. +44.5%, mean  3.2%    +0.4% .. +33.2%, mean  1.1%
//    normal (shading) -25.8% .. +23.9%, mean 11.2%   -38.7% .. +35.6%, mean 19.3%
//    WHOLE CRUST      -32.3% .. +52.8%, mean 20.4%   -37.8% .. +52.8%, mean 22.5%
//  The crust takes the SAME share of the pixel in both rooms — 20.4% against
//  22.5%. What differs is WHICH CHANNEL DELIVERS IT: the two PAINT terms are
//  2.4x and 2.9x louder in the cellar, and the SHADING term is 0.58x, i.e.
//  quieter. In the wood the crust is three quarters SHADING: the moon is
//  _DirCol (0.70, 0.79, 0.94) at _DirScale
//  0.38 against an ambient of (0.024, 0.029, 0.040), i.e. 6.9:1 directional to
//  ambient, plus an unscaled (0.70, 0.79, 0.94) on the 44-power glint. In the
//  cellar the same moon is _DirCol (0.048, 0.070, 0.128) against an ambient of
//  (0.028, 0.032, 0.045) — 1.5:1, and a glint an order of magnitude down. There
//  is nothing indoors to shade a relief WITH, so the crust falls back on the
//  only channel that works without a light: it PAINTS. A pattern that is drawn
//  into the albedo and does not move with any light in the room is exactly what
//  "weiße Muster" describes, and it is what paint is.
//
//  ...AND THE FLOOR IT IS PAINTED ON, which is the other half. Plate-scale
//  contrast of the frosted floor itself — median |9-61 px band| over its own
//  mean luminance, with the crust removed and with it restored:
//    cellar flagstones  0.109 -> 0.161   the crust ADDS 47% of the floor's own
//                                        structure
//    forest litter      0.195 -> 0.207   +6%
//  "Im Wald fällt das nicht auf" is that 6%, measured. The wood's floor is
//  litter, roots and leaf edges and it carries 1.8x the cellar's own detail
//  before any frost lands on it; a crust laid on it is one more thing among
//  many. The cellar's is smooth flagstone, and there the same crust is half
//  again as much structure as the floor had.
//  (A per-pixel-normalised version of this statistic was tried first and thrown
//  away: a handful of near-black pixels put the ratio in the thousands and moved
//  the number by 2x for a 0.07% change of the mask. Anything quoted here is the
//  median form, which reproduces to 2% across two runs of the harness.)
//
//  THE FIX: indoors the two ALBEDO terms are scaled down and the shading terms
//  are not touched at all. 9.00 * 0.35 takes the dome's mean lift from 13.2% to
//  4.6% and 0.45 * 0.20 takes the boundary's from 3.2% to 0.6%, against the
//  wood's 5.5% and 1.1% — i.e. a little UNDER parity with the accepted room,
//  deliberately, because parity in absolute lift is not parity in prominence on
//  a floor that carries 1.8x less of its own detail. MEASURED AFTER: the whole
//  crust's mean lift falls 20.4% -> 13.7% and its plate-scale addition falls
//  from +47% of the bare floor to +31%. The crust's HEIGHT, its analytic normal,
//  GHVR_FROST_RELIEF, the glint, the trapped air and the coverage frontier are
//  all unchanged — the relief is still there and still lit by whatever light the
//  room has; what stops is drawing a second copy of it in paint, at a depth
//  calibrated against a floor that is not this one.
//
//  WHAT IS LEFT IF HE STILL SEES A PATTERN, so the next round does not have to
//  re-derive it: 82% of the cellar's remaining crust structure is the NORMAL
//  (mean lift 11.2%, untouched here because it is the term that is already
//  QUIETER indoors than out and because it is the one channel that reads as ice
//  rather than as a mark). The only lever left is GHVR_FROST_RELIEF, indoors,
//  and it would want the same treatment: 0.35 -> about 0.20.
//
//  WHY _GhvrIndoor AND NOT _ElemFrost. The cause measured above is a property of
//  the ROOM — its moon, its ambient, the floor it lit — and not of one material,
//  so the lever has to be the room. Every indoor surface is in the same
//  position: the cellar's walls carry the same crust at 14.5% coverage and its
//  crates and barrels at 100% susceptibility, all under the same 1.5:1 light.
//  _ElemFrost is the wrong lever twice over. It is the COVERAGE dial — it feeds
//  `cover` in GhvrGrown, not anything in this function — so turning it down
//  would delete frost rather than quieten it; and C_Floor's value is 1.55,
//  raised deliberately by the play-disc round to answer "Der Frost kann gerne
//  auch direkt in dem Bereich unter dem Spielfeld auch auftauchen"
//  (BuildEnvironmentRooms, THE FROST REACHES THE BOARD). Touching it would undo
//  a request the user has already been given.
//
//  REJECTED: moving the boundary's brightening out of the albedo and onto the
//  DIRECTIONAL term, which is the physically honest form of the argument the
//  block above makes (a fracture returns more of the light that REACHES it, and
//  a hemisphere of ambient is not a beam, so a crack under ambient alone should
//  not flare at all). It is the better model and it would be room-aware for
//  free. It is rejected because it changes the WOOD, which the verdict
//  explicitly accepts as it is, and no amount of being right about optics is
//  worth re-opening a room the user has signed off.
//  REJECTED: cutting the crust's amplitude everywhere. The wood is accepted at
//  22.5% and the same cut would take it out of a room nobody complained about.
//  REJECTED: raising GHVR_FROST_RELIEF indoors to buy back in shading what the
//  paint gives up. It is tempting — same total, delivered through the channel
//  that reads as ice rather than as a mark — but the cellar has 1.5:1 of
//  directional light to spend, so buying back 15% of paint costs a relief the
//  crust cannot carry without becoming crumpled foil, and it would move the one
//  term (the normal) that is already QUIETER indoors than out.
// ----------------------------------------------------------------------------

/// Half-amplitudes of the three plate waves, in metres of relief, and their wave
/// vectors in rad/m. |k0| = 28.5 (22 cm plates), |k1| = 34.8 (18 cm), |k2| = 64.4
/// (9.8 cm) — a quilt with a coarse family, a medium one and a fine chatter, none
/// of them commensurate with any other. That was never enough on its own (see
/// THE STRIPES WERE A LATTICE); what makes the quilt irregular is the warp.
/// The total height swing is +-0.046 m of "relief units"; what turns that into a
/// slope is GHVR_FROST_RELIEF below.
#define GHVR_FROST_K0 float3( 23.0,   9.0,  14.0)
#define GHVR_FROST_K1 float3(-11.0,  27.0, -19.0)
#define GHVR_FROST_K2 float3( 41.0, -37.0,  31.0)

/// THE WARP. Three LONG waves — |W| = 4.25, 2.44, 3.26 rad/m, i.e. 1.48 m,
/// 2.58 m and 1.93 m — whose sines displace the plate waves' phases and whose
/// cosines (free from the same sincos) carry the chain rule into the gradient.
/// They are metre-scale on purpose: a warp shorter than a plate would shred the
/// plates instead of bending them, and a warp longer than the room would tilt
/// the whole lattice without breaking it, which is the same lattice.
#define GHVR_FROST_W0 float3( 2.30, -1.10,  3.40)
#define GHVR_FROST_W1 float3(-1.90,  1.30, -0.80)
#define GHVR_FROST_W2 float3( 0.90,  2.70, -1.60)
/// ...and how far they push, in RADIANS of plate phase. Each family takes two
/// of the three warps, at these two weights, so its meander has two scales and
/// is not itself a regular wiggle. 2.60 + 1.60 = 4.20 rad of total excursion =
/// 0.67 of a plate period either way. Raising them further starts to fold the
/// field: the perturbation of a family's wave vector is bounded by
/// WA*|Wj| + WB*|Wk|, which is 11.6, 15.3 and 15.0 rad/m against |K| of 28.4,
/// 34.8 and 63.3 — 44% at worst, and a fold needs 100%.
#define GHVR_FROST_WA 2.60
#define GHVR_FROST_WB 1.60
/// Half-width of a plate boundary, in units of |sin| — 0.11 is about 3.5 mm of
/// hairline on a 22 cm plate. It was 0.20 (6.4 mm) and that width is half of
/// why the lattice was the dominant feature of the floor rather than a detail
/// in it.
#define GHVR_FROST_SEAM 0.11

/// How much of the crust's own gradient reaches the normal. The raw gradient
/// reaches 1.73, i.e. 60 degrees, which is crumpled foil; 0.35 caps the crust at
/// atan(0.49) = 26 degrees at the 99th percentile of the ridges (measured over a
/// 7 m cube; the warp moved that from 25.4 to 26.1 deg, since a warped family's
/// local |k| is a little larger than its nominal one) — the same neighbourhood
/// EnvPuddle's _IceRelief 0.45 lands its sheet at (22 deg), because a frost
/// crust on stone is rougher than a sheet that froze flat on water.
#define GHVR_FROST_RELIEF 0.35

/// HOW DEEP THE CRUST PAINTS ITSELF INTO THE ALBEDO, and how much of that depth
/// survives INDOORS — see THE CRUST PAINTS AT THE DEPTH ITS ROOM CAN CARRY.
///   DOME  9.00 spans 0.63..1.37 of the plate body, i.e. a dome is more than
///         twice its own trough. It is the strongest single cue that the patch
///         has a top surface, and it is what the strokes in eis_boden2.jpg are.
///   LIFT  0.45, the boundary's brightening — a fracture in ice scatters (see A
///         CRACK IN ICE IS BRIGHT). It is the term the ModBuild 150 verdict
///         names, since it is the one that used to be the black lattice.
/// The two INDOOR factors are not tuning-by-eye: they take the cellar's measured
/// mean lift from 13.2% to 4.6% and from 3.2% to 0.6%, against the wood's 5.5%
/// and 1.1% — just under parity with the room the user accepted. They apply to
/// the PAINT only; f.h itself, f.grad, GHVR_FROST_RELIEF and the glint are the
/// same in both rooms.
#define GHVR_FROST_DOME        9.00
#define GHVR_FROST_LIFT        0.45
#define GHVR_FROST_INDOOR_DOME 0.35
#define GHVR_FROST_INDOOR_LIFT 0.20

/// Everything the crust knows about itself at one fragment.
///   h     the surface height of the crust, +-0.046 "relief units"
///   grad  its gradient in the same frame as `pm` (rad/m x relief units)
///   bub   trapped air: isolated bright specks, 0..1
///   seam  the plate boundaries: a network of hairlines, 0..1
struct GhvrFrostIce
{
    float h;
    float3 grad;
    float bub;
    float seam;
};

/// The crust, evaluated. `pm` is the fragment's position in ROOM METRES —
/// GhvrGrowQ with freq 1, i.e. (opos - centre) * scl — so a plate is 22 cm on a
/// prop scaled 2.0 as well as on the welded floor, and two walls carrying the
/// same mesh at two yaws get uncorrelated quilts for the same reason the patch
/// field does (see GhvrGrowQ).
GhvrFrostIce GhvrFrostCrust (float3 pm)
{
    // THE WARP FIRST. Three long waves, one sincos3; the sines bend the plates
    // and the cosines are what the gradient below needs to stay exact.
    float3 warg = float3(dot(pm, GHVR_FROST_W0),
                         dot(pm, GHVR_FROST_W1) + 0.9,
                         dot(pm, GHVR_FROST_W2) + 2.3);
    float3 wsn, wcs;
    sincos(warg, wsn, wcs);

    // Each family is displaced by TWO of the three warps, and each takes a
    // different pair, so no two families meander together — three lattices that
    // bend in step are still a lattice.
    float3 arg = float3(dot(pm, GHVR_FROST_K0)
                            + GHVR_FROST_WA * wsn.y + GHVR_FROST_WB * wsn.z,
                        dot(pm, GHVR_FROST_K1) + 1.7
                            + GHVR_FROST_WA * wsn.z + GHVR_FROST_WB * wsn.x,
                        dot(pm, GHVR_FROST_K2) + 3.1
                            + GHVR_FROST_WA * wsn.x + GHVR_FROST_WB * wsn.y);
    float3 sn, cs;
    sincos(arg, sn, cs);

    // THE LOCAL WAVE VECTORS, i.e. d(arg)/dpm — the chain rule through the warp,
    // which is what keeps the gradient below an EXACT gradient of f.h rather
    // than a plausible-looking vector field. Drop these three lines and the
    // normal disagrees with the height by up to 44%, which is a lit ridge that
    // is not where the shading says it is. Six float3 mads.
    float3 ke0 = GHVR_FROST_K0 + GHVR_FROST_WA * wcs.y * GHVR_FROST_W1
                               + GHVR_FROST_WB * wcs.z * GHVR_FROST_W2;
    float3 ke1 = GHVR_FROST_K1 + GHVR_FROST_WA * wcs.z * GHVR_FROST_W2
                               + GHVR_FROST_WB * wcs.x * GHVR_FROST_W0;
    float3 ke2 = GHVR_FROST_K2 + GHVR_FROST_WA * wcs.x * GHVR_FROST_W0
                               + GHVR_FROST_WB * wcs.y * GHVR_FROST_W1;

    GhvrFrostIce f;
    f.h    = 0.022 * sn.x + 0.015 * sn.y + 0.009 * sn.z;
    // d/dpm of the line above. Free: the cosines came out of the same sincos.
    f.grad = ke0 * (0.022 * cs.x)
           + ke1 * (0.015 * cs.y)
           + ke2 * (0.009 * cs.z);
    // TRAPPED AIR. The product of the coarse and the fine wave is near +1 only
    // in small lens-shaped regions where both are near +1 or both near -1, and
    // the ninth power keeps just those: isolated specks a centimetre or two
    // across at irregular spacing, which is what a bubble in ice looks like and
    // is the one bright thing INSIDE a crust. Same construction as EnvPuddle's
    // IceGrain.x, and the same reason it is sines and not a hash — a sine used
    // as a spatial CURVE has no branch to pick, so an ulp of vendor difference
    // moves a bubble by a micron instead of deciding whether it exists.
    f.bub = pow(saturate(sn.x * sn.z), 9.0);
    // THE PLATE BOUNDARIES, and every clause of this is now an answer to
    // "komische schwarze Streifen" — see THE STRIPES WERE A LATTICE.
    //  * they are the zero crossings of the WARPED phases, so they meander;
    //  * MAX, not a sum. A sum piles three families up at every crossing into a
    //    blob and saturates there, which is what put a visible NODE at every
    //    vertex of the tiling — the strongest cue the eye had that the thing was
    //    periodic at all. A max leaves a network of crossing hairlines;
    //  * the fine family (9.8 cm) is down at 0.40 and the medium at 0.85: the
    //    finest family is the densest one, and it is the one that turns a
    //    network into a hatch;
    //  * and the whole network is gated by a COARSE field made of two warp waves
    //    that are already in registers, so the crust is a clean sheet in some
    //    square metres and cracked in others. 0.12 rather than 0 at the bottom:
    //    a plate boundary that vanished completely would make the gate itself
    //    visible as a shape.
    float3 e = 1.0 - smoothstep(0.0, GHVR_FROST_SEAM, abs(sn));
    float net = max(max(e.x, e.y * 0.85), e.z * 0.40);
    float patch = smoothstep(0.14, 0.74, 0.5 + 0.5 * wsn.x * wsn.z);
    f.seam = saturate(net * (0.12 + 1.30 * patch));
    return f;
}

/// A zeroed crust, for the fragments that never entered the ice branch. It has
/// to exist so the call sites can hoist the declaration out of a branch without
/// leaving anything uninitialised — the zero state is EXACT, not close.
GhvrFrostIce GhvrFrostCrustZero ()
{
    GhvrFrostIce f;
    f.h = 0.0; f.grad = float3(0, 0, 0); f.bub = 0.0; f.seam = 0.0;
    return f;
}

/// Frost, laid on. It takes the surface's own brightness with it (`lum`), so a
/// dark stone frosts dark and the room keeps its modelling — a constant white
/// would flatten every wall it touched into a sheet of paper, which is the
/// other way an element can repeal a hand-tuned room.
///
/// The modulation is STEEP — 0.34 + 0.95*lum against ModBuild 142's 0.50 + 0.55
/// — because that factor is the only thing carrying the stone's own texture
/// through the covering. At the shallow setting a frosted flagstone read as a
/// flat pale shape with the mortar courses gone; nearly doubling the slope puts
/// the joints and the pitting back into the frost, which is what tells the eye
/// it is looking at frost ON something rather than at a hole cut in the floor.
///
/// ...AND THE CRUST'S OWN THREE TERMS. 9.0*h spans 0.59..1.41, i.e. a plate's
/// dome is nearly two and a half times its trough — the strongest single cue,
/// and the one that makes the patch read as having a top surface at all. The
/// boundaries BRIGHTEN by up to 45% (a fracture in ice scatters; see A CRACK IN
/// ICE IS BRIGHT above — this factor used to be 1 - 0.42*seam and it is the
/// whole of the word "schwarze") and the bubbles are added rather than lerped,
/// because trapped air scatters light out of the solid and is not a colour of it.
///
/// BOTH OF THOSE TWO ARE PAINT, and paint is the only channel a room with no
/// directional light has left — which is why they are the two terms that carry a
/// ROOM factor. See THE CRUST PAINTS AT THE DEPTH ITS ROOM CAN CARRY: in the
/// wood the crust is three quarters shading and adds 6% to a floor that is
/// already busy; in the cellar it is mostly paint and adds 47% to a floor that
/// is not. The bubbles do NOT take the factor: they are 2.4% of the surface in
/// isolated specks, not a pattern, and nothing in the verdict is about them.
///
/// THE BOUNDARY IS MULTIPLICATIVE AND THE BUBBLE IS NOT, and that difference is
/// deliberate: a bubble is a body of its own with its own albedo, so it adds a
/// fixed white; a fracture has no substance at all, it only returns more of the
/// light the plate was already getting, so it multiplies and inherits the
/// (0.34 + 0.95*lum) that carries the stone's own modelling through. That is
/// what stops a hairline in a black cellar corner from glowing.
/// MEASURED, over a 7 m cube: at lum 0.15 / 0.42 / 0.80 the boundary now sits
/// 37-38% above the plate body it separates, where it used to sit at 63% OF it.
/// Exactly the shipped function when the crust is zero: 9*0 = 0, seam 0, bub 0.
float3 GhvrFrostOn (float3 alb, float lum, float m, GhvrFrostIce f)
{
    // THE ROOM'S SHARE OF THE PAINT. Two mads on a uniform, inside the caller's
    // existing `if (ice > 0.0)`. OUTDOORS IT IS THE IDENTITY TO THE BIT:
    // GhvrIndoor() is 0 in the wood and lerp(a, b, 0) is a + 0*(b - a) = a, so
    // the two coefficients below are 9.00 and 0.45 exactly and the forest's
    // fragment is the shipped one instruction for instruction.
    float2 paint = lerp(float2(1.0, 1.0),
                        float2(GHVR_FROST_INDOOR_DOME, GHVR_FROST_INDOOR_LIFT),
                        GhvrIndoor());
    float3 ice = float3(0.66, 0.76, 0.94) * (0.34 + 0.95 * lum);
    ice *= (1.0 + (GHVR_FROST_DOME * paint.x) * f.h)
         * (1.0 + (GHVR_FROST_LIFT * paint.y) * f.seam);
    ice += float3(0.95, 0.98, 1.00) * (f.bub * 0.30);
    return lerp(alb, ice, m);
}

/// The crust's relief as a TANGENT-SPACE slope, ready to be added to a normal
/// map's xy. `T`/`B` are the surface's object-space tangent and bitangent, which
/// is the frame `pm` and therefore `grad` live in.
///
/// The sign is negative for the same reason UnpackNormal's is: a normal map
/// stores the negated gradient of the height it represents.
float2 GhvrFrostSlope (GhvrFrostIce f, float3 T, float3 B)
{
    return -float2(dot(f.grad, T), dot(f.grad, B)) * GHVR_FROST_RELIEF;
}

// ============================================================================
//  THE MOSS IS GONE — and this block is the only thing left of it.
//
//  USER VERDICT, ModBuild 147 (hardware, verbatim, and the FOURTH rejection of
//  the same surface):
//    "Entferne das 'Moos' komplett, das sieht nicht gut aus (siehe moos.jpg).
//     Mach stattdessen mehr Bewachsung, auch zB im Wald innerhalb der Lichtung,
//     so dass man einen deutlichen Unterschied erkennt."
//    "Entferne bei 'Erde' im Keller diese Flecken auf dem Boden komplett — gehe
//     sonst auch im Keller in die Richtung von mehr sichtbarer Bewachsung an den
//     Wänden und im Boden, nicht diese Flecken mehr."
//
//  WHAT WAS DELETED, so that nobody has to go to the history to find out what
//  was tried: GhvrTri4 (the smoothed triangle wave and its analytic derivative),
//  GhvrMossRelief (four incommensurate corrugations warped by the patch field,
//  plus a value-noise CLUMP field and a hue TONE), GhvrMossThick (the cushion
//  depth) and GhvrMossOn (three tiers cut out of the clump field, two body hues
//  chosen by the tone, a pale crown, a dry rim, a luminance carry-through and an
//  occlusion lip, with a separate indoor lichen/fungus palette). Roughly 340
//  lines of shading and four rounds of tuning. Every call site went with it, and
//  so did the wet grazing SHEEN EnvRoom.shader used to add under Earth — a green
//  add on a trunk is a green surface however small it is.
//
//  THE ROOT CAUSE, stated once and plainly, because three previous rounds each
//  diagnosed a DIFFERENT cause and each was wrong in the same way. The verdicts
//  went: "einfach grüne Flecken" (143), "eher wie Schleim" (146), and now
//  "sieht nicht gut aus" with a photograph attached. Each round answered the
//  adjective: 143's answer was flat colour, so 145 gave it micro-relief, its own
//  normal, two greens and an occlusion lip; 146's answer was a smooth gradient
//  and one hue, so it gave it discrete bodies cut out of a value-noise clump
//  field, two body hues, a crown and a dry rim. Both were real improvements to
//  the thing that was wrong, and neither could work, because the defect is not
//  in the adjective. It is CATEGORICAL:
//
//      A FUNCTION OF THE ALBEDO HAS NO SILHOUETTE. Whatever it computes, it
//      computes it ON the surface it was handed, inside that surface's own
//      outline. A plant is legible because its edge is against the BACKGROUND —
//      you read a fern as a fern by the black between its fronds. Paint the
//      most convincing moss in the world onto a tree trunk and it is still a
//      shape lying inside the trunk's outline, i.e. a mark ON the bark. That is
//      what a stain is. Nothing that lives in a fragment shader can escape it.
//
//  moos.jpg is that argument as a picture, and it is worth describing because
//  the file itself will not survive the next cleanup: a green blob with darker
//  spots inside it, lying flat on a lit trunk — while the CARD-BASED grass and
//  ferns standing in front of the same trunk, in the same frame, at the same
//  three metres, read as plants without any help at all. The two treatments are
//  side by side and the verdict between them is not close. Card geometry reads
//  as vegetation; painted growth reads as a stain. That is the lesson, and it is
//  the reason the replacement is a BAKE-LANE job (more cards, denser, in more
//  places, including inside the forest clearing) and not another look function.
//
//  THE SECOND VERDICT IS THE SAME FAULT ON A FLOOR. "diese Flecken auf dem
//  Boden" in the cellar were this same code path on C_Floor (_ElemMoss 1.0):
//  patches of a different colour lying in the flagstones. A patch of colour on a
//  floor cannot be growth for exactly the reason above, and the cellar's own
//  bracket fungi — which ARE geometry — were never complained about once.
//
//  WHAT SURVIVED, and how:
//   * THE BRACKET FUNGI AND EVERY GROWTH CARD. They never went through this
//     path at all: C_Fungus.mat, C_Growth.mat, S_GrowthMoss.mat, S_GrowthGrass.
//     mat and S_Moss0..3.mat all carry _ElemMoss = 0 (checked, all 74 Env
//     materials), and they take their colour from their own textures
//     (fungus_alb, moss_01_alb) and their own _Tint. What Earth does to them is
//     GhvrGrowCard below — a VERTEX fold that stands them up out of the ground —
//     and that is untouched, because the grow-in is geometry appearing and is
//     the one thing about Earth the user has never objected to. (He objected to
//     it LOOPING two rounds later, which is a different complaint with a
//     different cause and left the grow-in itself standing: see IT TAKES NO TIME
//     on GhvrGrowCard.)
//   * FROST. GhvrFrostOn, the frontier, the affinity, the creep and the whole
//     of Ice are untouched: Ice was never the complaint, and frost genuinely IS
//     a film on a surface, which is the one thing this mechanism can honestly
//     draw.
//   * THE WIND and the grow-in, below.
//
//  WHAT IS OWED TO THE BAKE LANE, in the report and repeated here so the next
//  reader of this file finds it: _ElemMoss is no longer declared by EnvRoom,
//  EnvGround or EnvRoomCutout, so the ~30 SetFloat("_ElemMoss", ...) calls in
//  BuildEnvironmentRooms.cs are now writes to a property that does not exist.
//  They are harmless (Unity ignores them; the guarded ones test HasProperty
//  first) but they should go, together with the "earth moss" rows of
//  ReportGrowth, which now report a coverage nothing consumes.
//
//  REJECTED, this round:
//   * KEEPING THE FUNCTIONS AND SETTING THE SUSCEPTIBILITIES TO 0. It is the
//     one-line change and it is exactly what the user forbade ("Entferne ...
//     komplett"). A look function that four rounds have failed to make work,
//     left in the file behind a zero, is a look function that comes back.
//   * KEEPING A "SUBTLE" VERSION FOR THE CELLAR ONLY, on the argument that the
//     indoor lichen palette (sage/cream, the ModBuild 146 work) was never shown
//     to him separately. It was: "Entferne bei 'Erde' im Keller diese Flecken
//     auf dem Boden komplett" names the cellar explicitly.
//   * KEEPING THE MOSS NORMAL (the relief bump) without the colour, so that
//     Earth would still rough up a surface. Tempting, cheap, and wrong twice: a
//     normal perturbation with no albedo behind it reads as a dent, not a plant,
//     and it would keep GhvrTri4 + GhvrMossRelief alive as the seed of the next
//     revival.
// ============================================================================
/// The one channel of the albedo GhvrFrostOn needs. Green, not a luminance dot:
/// it is one component instead of three multiplies and an add, and on every
/// texture in both rooms it tracks the luminance closely enough to carry the
/// modelling (that much ModBuild 142 had right). It is kept as a named function
/// rather than inlined at the three call sites so that "what the covering
/// carries of the surface under it" stays one decision.
float GhvrGrowLum (float3 alb) { return alb.g; }

// ============================================================================
//  THE WIND — "Bei der Luft bzw Wind möchte ich das die Blätter der Bäume
//  wackeln! Das habe ich schon einmal öfters in anderen Spielen gesehen, such
//  nach performanten Möglichkeiten so etwas zu implementieren."
//
//  PRIOR ART. The user asked for this one to be researched ("such nach
//  performanten Möglichkeiten"), so it was, and the headline finding is worth
//  stating plainly: NONE of the three production systems uses sin(). Crytek,
//  Unity's Tree Creator and SpeedTree all ship the same cubic-smoothed triangle
//  wave, which is GhvrWave4 above.
//    * GPU Gems 3 ch. 16 (Crytek, "Vegetation Procedural Animation and Shading
//      in Crysis") — two tiers, main bending plus detail bending, with the
//      per-vertex weights in VERTEX COLOUR (R = leaf-edge stiffness, G = per-leaf
//      phase, B = overall stiffness) and four incommensurate carriers
//      (1.975, 0.793, 0.375, 0.193) evaluated as one float4. TAKEN: the tiering,
//      collapsed to bend + flutter because a card welded into a canopy has no
//      trunk of its own; and the float4 evaluation, which matters on Adreno,
//      where a dependent scalar chain stalls three instructions per step.
//      NOT TAKEN: its length-preserving normalize(vNewPos) * fLength. That is
//      for a plant bending about its own root; these cards pivot about an
//      attachment a few centimetres away and a renormalise would swing them.
//    * Unity's terrain grass (TerrainEngine.cginc, TerrainWaveGrass) — phase is
//      a dot product of the vertex position with a constant vector, and the
//      amplitude is `v.color.a`, commented in Unity's own source as "1 on top
//      vertices, 0 on bottom vertices". TAKEN: both, verbatim in spirit. That
//      one line is the entire "the attachment point stays fixed" requirement.
//      NOT TAKEN: its FastSinCos Taylor series (it carries a real bug in the
//      cosine term and a wrap discontinuity at 6.408849 != 2*pi).
//    * Unity's Tree Creator (AnimateVertex) — the same Crysis detail bend, and
//      the source of the frac()-into-triangle-wave trick that keeps the time
//      argument bounded forever. TAKEN. NOT TAKEN: it displaces along the
//      vertex NORMAL, which on a two-sided card system tears the two facings
//      apart; everything here moves along per-material constants instead.
//    * SpeedTree (SpeedTreeWind.cginc) — the Ripple/Tumble split, and the stock
//      gust envelope `x + y*y` (one signed wave plus one rectified one). TAKEN:
//      the split, as bend + flutter. Its LOD table is also the reason the
//      flutter is a single extra carrier and not a rotation: SpeedTree's own
//      docs put arbitrary-axis leaf TUMBLE, which needs a real sin/cos/acos
//      rotation matrix, in the most expensive tier by a wide margin.
//    * The widely copied "cheap mobile wind" (Cyanilux, GPU Gems 1 ch. 7) — 2-3
//      carriers plus a travelling gust, displacement in XZ only, amplitudes
//      around 3-4 cm ("-1 to 1 is far too large of an offset"). TAKEN: the gust,
//      which is the single term that separates wind from vibration.
//
//  WHY NOT A HASH OR A NOISE TEXTURE. Both were rejected for the same reason the
//  hash rule exists in EnvElement.cginc: frac(sin(dot(p,k))*43758.5) is decided
//  entirely by bits below the noise floor of an unspecified-precision sin, so it
//  returns different values on Adreno, Mali and desktop — in a mod where two
//  players must see the same branch in the same place, that is a correctness
//  bug. Phase-from-position needs no hash, no instance buffer and no per-client
//  state, which is also the only thing that CAN work here: the canopy is one
//  welded mesh, so there are no instances to seed.
//
//  WITHIN-CARD SHEAR, the one pitfall of taking the phase per VERTEX rather than
//  per card. Four corners with four phases twist a quad. The fix everyone else
//  uses is to bake the card's anchor into a second UV set; the fix here is
//  cheaper and needs no extra vertex channel: the phase rate is kept LOW
//  (0.12 cycles/m, an 8 m wave), so two corners of a 0.6 m card differ by under
//  a twelfth of a cycle while two boughs 2 m apart differ by a quarter of one.
//  The residual shear is under 8% of the amplitude, i.e. millimetres, and the
//  anchored corners do not move at all because their weight is exactly 0.
//
//  WHAT THIS IS NOT ALLOWED TO DO:
//    * detach a card from its branch. The weight is 0 at the attachment by
//      construction (vertex alpha on the built cards, height-above-base on the
//      photoscans), so the anchored end is not merely nearly still, it is still.
//    * disagree with the canopy shadow map, which another lane bakes against
//      the STATIC geometry. Because GhvrWave4 is bounded, the budget is exact:
//      the offset below never exceeds 1.06 * amp at rest and 2.04 * amp in the
//      storm. See THE STORM below and the amplitudes chosen in
//      BuildEnvironmentRooms (SURFACE GROWTH), which are set against that map's
//      5.5 x 4.4 cm texel and the blades' 0.22 m penumbra.
//    * differ between two players. Only frac/abs/mad, phase from position, time
//      from the shared clock: two clients render the same bough in the same
//      place at the same instant, by construction.
//
//  ------------------------------------------------------------- THE STORM
//  USER VERDICT, ModBuild 143 (hardware, verbatim): "Mir gefallen die
//  Bewegungen der Bäume gut, so wie du es gemacht hast sollte der Normalzustand
//  sein und immer sichtbar! (Nur die Bewegungen der Blätter, nicht der sichtbare
//  Wind). Wenn Wind aktiv ist sollte es deutlich heftiger sein mit den
//  Bewegungen der Blätter, so dass wirklich Starkwind bzw. ein aufkommender
//  Sturm zu bemerken ist."
//
//  Two separate instructions, and the second is not "more of the first".
//
//  (1) THE BASELINE IS NOT GATED ANY MORE. `amp` is now the STANDING breeze and
//      it runs with no element up at all — a wood in which nothing moves is a
//      photograph, and he is right that it should never have needed an
//      infusion. The parenthesis is a boundary he drew himself: only the LEAVES
//      are permanent. The visible airborne streaks are another lane's emitters
//      and stay element-gated; nothing in this file draws them.
//      The price is that the per-vertex cost below is now PERMANENT, on every
//      foliage vertex in the wood, in every scenario, forever. That is why this
//      function is still frac/abs/mad only, why it is still ONE float4 of
//      independent work, and why the storm adds not one carrier: the whole
//      escalation below is four extra mads on constants that were already
//      there.
//
//  (2) THE STORM IS NOT A BIGGER BREEZE. `storm` (Air's own strength) escalates
//      three DIFFERENT things, and only one of them is amplitude:
//        * the GUST gets deep. At rest the envelope is 0.62..1.00, i.e. the
//          motion never really stops; at full Air it is 0.26..1.00, so the
//          canopy goes half-still and is then shoved. Intermittency is what
//          reads as "aufkommender Sturm" — a uniformly larger wobble reads as
//          a bigger fan, and the gust also travels down-wind faster (2.9 m/s
//          against 1.36), so you SEE it arrive across the clearing.
//        * the FLUTTER gets fast. The leaf's own carrier goes from 1.6 s to
//          0.6 s. This is the term that costs the shadow map nothing at all
//          (see below) and it is the one the eye reads as wind SPEED.
//        * the BEND gets deeper, and only by 1.85x.
//
//  WHY THE STORM'S EXTRA AMPLITUDE IS RATIONED, and the number that rations it.
//  The canopy shadow map is baked from the STATIC canopy at 5.5 x 4.4 cm per
//  texel, and it is worth being exact about what actually reads it:
//    * the TRUNK layer casts the crisp shadows the user asked for on the floor
//      — and no trunk moves. Trunks are EnvRoom materials with no wind at all,
//      so the one part of the bake with a hard edge is animated by nothing.
//    * the CROWN layer reaches the floor through a 4x4-box-filtered COVERAGE at
//      a seventh of the blades' strength, and reaches the shafts through a
//      0.22 m penumbra. Its footprint on the floor is half-metre-scale mush.
//  So the question is not "is the displacement under a texel" but "can a
//  half-metre penumbra see it". At rest the tip moves 1.06 * amp = 4.8 cm on
//  the canopy, under one texel — the ModBuild 143 argument, unchanged. In the
//  storm it moves 2.04 * amp = 9.2 cm, which is 1.7 texels and 42% of the
//  penumbra: still inside the blur that the only animated layer is read
//  through, and the layer with the sharp edges did not move.
//  The understory (ferns, grass, the growth tufts) is in NO shadow bake, so its
//  amplitude is bounded by taste alone and it gets the full 1.85x.
//
//  REJECTED: giving the storm a MEAN LEAN down-wind, which is what a real gale
//  does to a tree. The baked shadow is the mean position; a zero-mean
//  oscillation disagrees with it for half of each cycle and agrees on average,
//  while a constant lean disagrees with it permanently and in one direction —
//  i.e. the cheap-looking option is also the one that breaks the bake. Every
//  term below is zero-mean in the bend and the flutter, and the gust envelope
//  only scales them.

//  ------------------------------- AN ELEMENT MAY NOT MOVE A FREQUENCY ------
//  USER VERDICT, ModBuild 151 (hardware, verbatim): "Wenn man 'Luft' ein oder
//  ausschaltet zucken die Bäume extrem unnatürlich in der fade-in oder fade-out
//  also in dem Moment wenn der Effekt gestartet wird oder abgeschaltet wird für
//  ca 1s. das soll nicht sein."
//
//  THE FADE WAS NEVER THE FAULT. ElementMood ramps `storm` with a closed-form
//  smoothstep over RampSeconds = 1.0 (ElementMood.cs:749-752, :172) and that is
//  as smooth as a ramp gets. The fault was two lines in THIS function, and they
//  are the same mistake twice:
//
//      gust = dot(p,dir)*0.055 - t * (0.075 + 0.085*storm)          // was
//      s    = GhvrWave4(... t * (0.612 + 1.05*storm) + ph ...)      // was
//
//  `storm` multiplied ABSOLUTE TIME. `t` is the shared environment clock and a
//  scenario runs for thousands of seconds, so sweeping storm 0 -> 1 across one
//  second sweeps the flutter's argument by 1.05 * t CYCLES — at t = 900 s that
//  is 945 cycles inside the ramp, i.e. a mean rate of ~945 Hz where the carrier
//  itself runs at 1.6. The phase scrubs at random for exactly the ramp's
//  duration and then locks. That is the twitch, it is symmetric on fade-in and
//  fade-out because the sweep is symmetric, and it lasts "ca 1s" because
//  RampSeconds is 1.0. It also gets WORSE the longer the scenario has been
//  running, which is the signature that identifies it beyond doubt.
//
//  THE RULE, and it is now stated in the one file that broke it: AN ELEMENT
//  STRENGTH MULTIPLIES AN AMPLITUDE, NEVER A FREQUENCY. A frequency multiplied
//  by anything that moves is a phase that moves by (rate change) x (elapsed
//  clock), and no element in this bundle owns a clock of its own to make that
//  small.
//
//  HOW THE STORM KEEPS ITS TWO SPEED-UPS ANYWAY. Both escalations were real
//  design (see THE STORM above: the flutter reads as wind SPEED, the gust's
//  travel reads as a swell ARRIVING), so neither is dropped. Each is now TWO
//  CARRIERS AT FIXED RATES that `storm` CROSSFADES between — the slow one and
//  the fast one the storm used to reach by sliding. Both endpoints are exactly
//  what they were (0.612 and 0.612+1.05 = 1.662 cycles/s; 0.075 and 0.075+0.085
//  = 0.160 cycles/s), so storm = 0 and storm = 1 are bit-for-bit the shipped
//  breeze and the shipped storm; only the JOURNEY between them changed, and the
//  journey is the whole bug. A crossfade of two bounded waves is bounded, so
//  the displacement budget the canopy shadow map was reconciled against
//  (1.06 * amp at rest, 2.04 * amp at full Air) is unchanged to the digit.
//
//  WHAT IT COSTS: one GhvrWave2 (two more carriers) and two lerps per vertex.
//
//  THE PROPERTY THIS NOW HOLDS, which the old form did not: the offset is
//    sum_i A_i(storm(t)) * W_i(f_i * t + phi_i)   with every f_i CONSTANT,
//  so for any continuous storm(t) the offset is continuous in t, and
//    |d offset/dt| <= sum_i ( |A_i'| |storm'| |W_i| + |A_i| * 6 f_i )
//  which is bounded by the amplitudes and the FIXED rates alone — it does not
//  contain `t`. The old form's bound contained `t` and therefore had no bound.
//
//  REJECTED: integrating the phase (theta += rate(storm) * dt). It is the
//  textbook fix and it needs STATE — a per-frame accumulator written by the CPU
//  and pushed as a uniform. Two clients would then have to agree about a value
//  produced by their own frame pacing, which is exactly the class of thing
//  EnvElement rule 3 (everything from the shared clock, nothing integrated)
//  exists to forbid; a dropped frame on one headset would leave the two woods
//  permanently out of phase.
//  REJECTED: shortening RampSeconds so the scrub is over quicker. It makes the
//  artefact briefer and louder, and it would break every other element's fade.

/// The wind offset for one vertex, in OBJECT units.
///   p     object-space vertex position
///   w     per-vertex freedom, 0 at the attachment, 1 at the tip
///   t     the shared clock
///   dir   wind bearing in OBJECT space (unit)
///   side  a unit vector across the wind, for the flutter
///   amp   tip amplitude in object units — the STANDING breeze, always on
///   storm Air's strength, 0..1. 0 is exactly the ModBuild 143 breeze.
/// The returned offset has magnitude <= 1.06 * amp at storm 0 and <= 2.04 * amp
/// at storm 1, always (GhvrWave4 is bounded and so is every factor below), and
/// it is CONTINUOUS IN TIME for any continuous storm — see the block above.
float3 GhvrWind (float3 p, float w, float t, float3 dir, float3 side, float amp, float storm)
{
    // Phase in CYCLES from the vertex's own position, on a bearing that is not
    // the wind's: a constant with all three components means two boughs one
    // above the other are out of step as well as two side by side. |k| is
    // 0.12 cycles/m, an 8.3 m wave — see WITHIN-CARD SHEAR above.
    float ph = dot(p, float3(0.062, 0.041, 0.094));
    // The gust's travel, in cycles per metre down-wind. Shared by both gust
    // carriers, so the slow swell and the fast one have the same CRESTS and
    // differ only in how fast those crests cross the clearing.
    float run = dot(p, dir) * 0.055;
    // The BREEZE's four carriers, one float4, all independent and all at rates
    // that no element can touch: 4.3 s and 6.1 s for the bend — a bough leans,
    // it does not buzz — 1.6 s for the leaf's own flutter, and the 1.36 m/s
    // gust (0.075 cycles/s over 0.055 cycles/m).
    float4 s = GhvrWave4(float4(t * float3(0.235, 0.163, 0.612) + ph, run - t * 0.075));
    // ...and the STORM's two, likewise at fixed rates: the 0.6 s flutter and
    // the 2.9 m/s gust. These are the two speeds the storm used to reach by
    // sliding a frequency through them.
    float2 f = GhvrWave2(float2(t * 1.662 + ph, run - t * 0.160));
    // `storm` is now an amplitude on every line it appears in. A crossfade of
    // two waves each in [-1,1] is in [-1,1], so `flut` and the gust envelope
    // keep exactly the ranges the shadow-map budget was computed from.
    float flut = lerp(s.z, f.x, storm);
    float swell = lerp(s.w, f.y, storm);
    float bend = s.x * 0.62 + s.y * 0.38;                 // exactly [-1, 1]
    // [0.62, 1] at rest, [0.26, 1] in the storm: the canopy half-stills and is
    // then shoved, which is the whole difference between wind and vibration.
    float env = lerp(0.62, 0.26, storm) + lerp(0.38, 0.74, storm) * (swell * 0.5 + 0.5);
    // w*w, not w: the stiff half of a bough hardly moves and only the last
    // quarter really flies, which is what a conifer does and what keeps the
    // shear at the attachment invisible.
    float a = amp * w * w * env;
    // Across the wind and a little up: a card that only slid down-wind reads as
    // a sheet on a rail. Never along the NORMAL (see the Tree Creator note).
    float bendA = a * (1.0 + 0.85 * storm);
    float sideA = a * (0.30 + 0.45 * storm);
    float upA   = a * (0.16 + 0.24 * storm);
    return dir * (bend * bendA) + side * (flut * sideA) + float3(0.0, flut * upA, 0.0);
}

/// The GROW-IN of a whole card, for the grass, the moss cushions and the tufts
/// Earth brings up. SINCE THE PAINTED MOSS WAS DELETED THIS IS THE WHOLE OF
/// WHAT EARTH DOES TO A SURFACE — the vegetation is geometry standing up out of
/// the ground, and nothing anywhere puts a colour on a wall any more. See THE
/// MOSS IS GONE above; the bake lane owns how MANY cards there are and where.
///
/// A card is collapsed onto its own base edge: `w` is 0 along the bottom edge
/// and 1 along the top, so subtracting (1-g) * span * w from y folds the quad
/// flat onto the line it stands on. At g = 0 the quad has ZERO AREA and the
/// rasteriser produces no fragments at all — which is how a whole mesh of grass
/// that does not exist yet costs nothing and, more to the point, is
/// bit-identical to a build without it. (Sinking the cards into the ground
/// instead was rejected: a card half through a floor still shades its own
/// fragments, and the forest floor is not flat enough to hide the other half.)
///
/// The threshold is the SAME frontier the pixels use, evaluated on the clump's
/// own position — so the grass comes up in patches that spread, rather than the
/// whole room rising like a lift.
///
/// ---------------------------------------------------------------------------
///  IT TAKES NO TIME, AND THAT IS THE WHOLE OF ModBuild 445's FIX.
///
///  USER, from hardware: "Das Gras im Wald, das wegen dem Element aufgetaucht
///  ist, wächst und verschwindet in einem Loop statt einmal zu wachsen und dann
///  konstant da zu sein! ... Es ist aufgefallen als das Element nur halb aktiv
///  war."
///
///  THERE WERE TWO CLOCKS ON THIS FOLD and both had to go. Neither of them is
///  the project's frequency-scrub class — every rate in this file and in
///  GhvrWind is a literal, and it was re-checked when this was written — they
///  are two correct PIXEL decisions applied to GEOMETRY, where the same numbers
///  mean something else entirely:
///
///   1. THE ELEMENT'S OWN BREATH. `cover` used to be Earth's STRENGTH, which
///      breathes 0.28..0.52 every 2.4 s while the element wanes. Through the
///      ease and the threshold that is a coverage swing of about 4x. On frost
///      that is a fringe advancing and retreating over a surface — the effect
///      the plateau was tuned for. On a card it is the quad's AREA going to zero
///      and back, i.e. a blade standing up and lying flat, twice every 2.4 s,
///      for as long as the element wanes. `cover` is now Earth's PRESENCE
///      (GhvrElem.grow), which is the same number for Strong and for Waning.
///
///   2. THE CREEP, which this function used to pass in and no longer takes at
///      all. GhvrGrowCreep is +-0.062 of threshold on two waves ~20 s and ~37 s
///      long, and its own doc says what it is for: "ice creaking outward and
///      back", so that a frost frontier is not a still picture at Strong. A
///      frontier that moves BOTH WAYS is a fine thing for a stain and an
///      impossible one for a plant — it is a card that grows, ungrows and grows
///      again on a 20 s cycle, which is the same complaint at a slower rate and
///      would have survived fixing (1) alone. The user's requirement is four
///      words long and rules on it directly: einmal wachsen, dann konstant.
///
///  So `g` here is now a pure function of POSITION and of a cover that only
///  changes when the game's element board does. The grass rises once, holds
///  exactly still, and comes down only when Earth is gone. THE FROST PATH IS
///  UNTOUCHED — GhvrGrow, GhvrGrown and GhvrGrowCreep are exactly as they were,
///  and the pixels still creep.
/// ---------------------------------------------------------------------------
float GhvrGrowCard (float3 q, float cover)
{
    float A = GhvrGrowField(q);
    float g = GhvrGrow(A, cover, 0.0);
    // A blade that reaches full height the instant it is over the threshold pops.
    // Squaring the ease-out is the settle: fast out of the ground, slow to full.
    return g * (2.0 - g);
}

#endif // GHVR_ENV_GROWTH_INCLUDED
