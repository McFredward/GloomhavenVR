// ============================================================================
//  SURFACE GROWTH — frost and moss as a COVERAGE THAT ADVANCES, plus the WIND
//  that moves the things that grew.
//
//  This is a companion to EnvElement.cginc, which owns the CHANNEL (_GhvrElemA/B,
//  the master switch, the periphery ramp) and whose five rules this file obeys
//  without restating them. What lives here is the one mechanism three shaders
//  need to spell identically — EnvRoom (cellar masonry AND forest trunks),
//  EnvGround (forest floor) and EnvRoomCutout (every alpha-cut card) — because
//  a wall and the flagstone at its foot growing two different mosses is worse
//  than neither growing at all.
//
//  USER FINDING, ModBuild 142 (hardware, verbatim):
//    "Beim Eis würde ich gerne Frost auf dem Boden wachsen sehen an den Wänden
//     oder Bäumen - nicht komplett flächendeckend aber animiert und immersiv."
//    "Bei Erde möchte das Wände und Böden teilweise mit Moos bewachsen - auch
//     im Wald das die Stämme teilweise und der Boden mit Moos bzw. Gras
//     bewachsen wird."
//    "Bei der Luft bzw Wind möchte ich das die Blätter der Bäume wackeln!"
//
//  The first two sentences are one request made twice, and ModBuild 142 answered
//  both with a FADE — alb = lerp(alb, colour, intensity * k). That is why he is
//  asking again. A fade over a whole wall says "this wall is being LIT
//  differently"; what he asked for says "this wall is being COVERED". The
//  difference is not the colour. It is that a covering has a FRONTIER: an edge,
//  in an irregular place, that moves.
//
//  THE MECHANISM, and it is deliberately ONE idea used four times (ice, earth,
//  the grass that grows in, and — as a threshold on position rather than on a
//  surface — which clumps have come up yet):
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
//           consumers compute this differently and that is the whole reason
//           frost and moss look like different substances rather than like two
//           colours of the same stain.
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
/// what lets a consumer write `lerp(alb, moss, GhvrGrow(...))` and still be
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
float3 GhvrFrostOn (float3 alb, float lum, float m)
{
    return lerp(alb, float3(0.66, 0.76, 0.94) * (0.34 + 0.95 * lum), m);
}

// ============================================================================
//  MOSS REAL — moss as a SURFACE and not as a colour.
//
//  USER VERDICT, ModBuild 143 (hardware, both rooms, verbatim):
//    forest: "Erde sind einfach grüne Flecken die hier und da zu sehen sind und
//             nicht wirklich wie Moos, das musst du überarbeiten"
//    cellar: "Bei Erde ist ähnlich wie im Wald einfach grüne Flecken statt
//             wirklich 'Moos' und Bewachsung, nicht sehr glaubwürdig"
//
//  He is NOT complaining about where it grows. ModBuild 143's frontier put the
//  patches at the foot of the wall, in the mortar courses and in the wet
//  hollows, and none of that is in the verdict. He is complaining that a patch
//  is a FLAT GREEN SHAPE — which it was, exactly: `lerp(alb, oneGreen, m)`.
//  A single chroma with the stone's own luminance showing through it is the
//  definition of a stain, and no amount of moving it or reshaping its outline
//  can make a stain read as a plant.
//
//  So the attack is on the SURFACE, and it is five things a stain cannot have.
//  Every one of them is a property of moss that survives being seen at three
//  metres in a room lit at 0.03, which is the only test that counts here:
//
//   1. ITS OWN MICRO-TEXTURE. Moss is a mat of fronds a few millimetres across;
//      at arm's length you see the fronds, at three metres you see the mottling
//      they make. GhvrMossRelief below is that mottling, at ~4 cm and ~14 cm.
//   2. ITS OWN NORMAL. A stain is as flat as what it is on; a cushion has
//      relief, and it also DESTROYS the relief underneath (the mortar course
//      under 2 cm of moss is gone). Both halves matter — the consumers scale
//      the material's own normal DOWN by the coverage and add the moss's own.
//      The gradient is analytic (see GhvrTri4), so this costs no extra fetch
//      and no second noise octave.
//   3. COLOUR THAT VARIES WITHIN ONE PATCH. Two greens, mixed by the relief:
//      near-black in the gaps between the fronds, a yellow-green on the tips.
//      This is the single biggest difference in the picture, and it is what
//      makes the patch look GRANULAR instead of poured.
//   4. A SOFT RAISED EDGE. A cushion stands proud of the stone, so its border
//      is a crease with an occlusion line in it, not a cut. GhvrMossOn's `band`
//      is three instructions and it is the cue that reads as "raised" — the
//      first render with the colours in and the lip out still looked painted.
//   5. IT KILLS THE SPECULAR AND SWALLOWS LIGHT. ModBuild 143 gave the moss a
//      wet GRAZING SHEEN, which is precisely a varnish: it made the patches
//      look like paint that had not dried. It is now weighted by (1 - thick),
//      so only the thin frontier — where the stone really is wet — is glossy,
//      and the deep middle of a cushion is matt and slightly darker than the
//      room around it.
//
//  ...and one thing that is NOT in this file, because a shader cannot do it:
//  where a patch is thick enough to be a CUSHION rather than a film, it needs a
//  silhouette. That is real geometry, and it is the growth cards — see the
//  SURFACE GROWTH block in BuildEnvironmentRooms.cs, where the cellar now grows
//  cushions up the wall face as well as along its foot and the forest floor
//  gets half as many tufts again.
//
//  REJECTED:
//   * a second value-noise octave for the micro-texture (8 more hashes, ~40
//     ALU, on the one path that already runs a full octave). The corrugation
//     below is quasi-periodic rather than random, which for a 4 cm frond mat
//     under moonlight is a distinction without a difference — and it hands
//     over an exact derivative, which a value noise would charge two more
//     evaluations for.
//   * a moss NORMAL MAP. There is no second UV set on any of these meshes
//     (walls, welded trunk bands, the forest floor), so it would have to be
//     triplanar: three fetches where this is one ALU block.
//   * screen-space derivatives of the albedo as a cheap bump (ddx/ddy). It is
//     two instructions and it looks right in a screenshot — and it is
//     per-EYE, so the two eyes get different relief on the same pixel. That is
//     the stereo-rivalry trap this project has already been bitten by once
//     (the masonry wall fade); it is not being walked into for a moss.
// ============================================================================

/// The cubic-smoothed triangle wave AND its exact derivative, four at a time.
/// Same wave as GhvrWave4 (see there for the SpeedTree/Crytek provenance and
/// for why it is not a sine); this form additionally returns d/dx, which is
/// what buys the moss a normal for free.
///
///   g(v) = 2*(3v^2 - 2v^3) - 1   with  v = |frac(x + 0.5)*2 - 1|
///   dg/dx = dg/dv * dv/dx = 12v(1-v) * 2*sign(...) = 24 v(1-v) sign(...)
/// so |value| <= 1 and |derivative| <= 6, both by construction rather than by
/// measurement — which is what lets the bump strength below be a constant.
void GhvrTri4 (float4 x, out float4 v, out float4 d)
{
    float4 s = frac(x + 0.5) * 2.0 - 1.0;
    float4 a = abs(s);
    v = (a * a * (3.0 - 2.0 * a) - 0.5) * 2.0;
    // sign(), and the s = 0 case needs no thought: a is 0 there too, so the
    // product is 0 whatever sign(0) returns.
    d = 24.0 * a * (1.0 - a) * sign(s);
}

/// The moss's own micro-relief: value in [-1,1] in .x, and its GRADIENT in the
/// same frame `q` is measured in, in .yzw.
///
/// Four incommensurate corrugations: two fine, one middling and one coarse, and
/// the WEIGHTS lean hard on the coarse ones. None of the four axes is parallel
/// to another and no two lengths are in a small integer ratio, so the sum does
/// not repeat anywhere a player can walk to; it is the same trick the flicker
/// uses on three sines, done in one float4.
///
/// THE SCALES ARE WHAT THE FIRST RENDER GOT WRONG, and it is worth recording
/// because it is a trap this kind of function walks into every time. The first
/// pass put all four between 3.5 and 14 cm, which is the physical size of a
/// moss frond and is therefore "correct" — and the picture came out as a green
/// CHECKERBOARD. Two reasons, both fatal:
///   * ALIASING. This is an analytic pattern, so it has no mip chain and
///     nothing filters it. A 4 cm feature at four metres is well under a pixel,
///     and an unfiltered sub-pixel pattern does not become smooth, it becomes
///     a moiré — which is exactly the regular speckle the render showed.
///     (fwidth-based fading was rejected: it is a SCREEN-space quantity, i.e.
///     per-eye, and this project has already lost a round to stereo rivalry on
///     the masonry wall fade. A pattern coarse enough not to need filtering is
///     the answer that has no per-eye term in it at all.)
///   * REGULARITY. Four waves at roughly equal weights and roughly equal
///     lengths read as a lattice however incommensurate the axes are. Moss is
///     CLUMPY: big soft lumps with fine texture riding on them, not a weave.
/// So the coarse pair carries two thirds of the weight and the sizes span
/// 7 cm to 25 cm — the band that survives being seen from across a room, which
/// is the only distance this is ever judged at.
///
/// THE WARP is the third thing the first render taught, and it is the one that
/// finally killed the weave. Four waves with fixed axes are QUASI-PERIODIC
/// however carefully the lengths are chosen: on a lit floor the eye finds the
/// repeat in about a second, and what it found was a honeycomb. `warp` is the
/// patch field the frontier already computed — an organic value that varies
/// over ~33 cm — and offsetting the whole lattice by it drags the corrugation
/// about by a wavelength or two per patch. Three adds, no extra evaluation, and
/// the result has no repeat at all because the thing displacing it has none.
/// (The gradient does not carry the warp's own derivative and is therefore
/// approximate. It is a bump on a moss cushion; being a few degrees off the
/// exact normal of a fictional height field is not a defect anybody can name.)
///
/// It is evaluated in `q` — GhvrGrowQ's coordinate, i.e. the room's own metric
/// frame relative to the room centre — so it obeys the same rule the patch
/// field does: two identical walls in two yaws grow two different mosses, and
/// two clients compute the same one.
float4 GhvrMossRelief (float3 qIn, float warp)
{
    float3 q = qIn + warp * float3(3.71, -2.93, 5.27);
    const float3 M0 = float3( 3.41,  1.62, -1.10);   // |M| 3.93 ->  8.5 cm
    const float3 M1 = float3(-1.23,  3.05,  3.26);   // |M| 4.63 ->  7.2 cm
    const float3 M2 = float3( 1.79, -1.06,  1.31);   // |M| 2.46 -> 13.6 cm
    const float3 M3 = float3( 0.71,  0.94, -0.62);   // |M| 1.33 -> 25.1 cm
    float4 v, d;
    GhvrTri4(float4(dot(q, M0), dot(q, M1), dot(q, M2), dot(q, M3)), v, d);
    const float4 W = float4(0.18, 0.16, 0.28, 0.38); // sums to 1 => |value| <= 1
    float3 g = M0 * (d.x * W.x) + M1 * (d.y * W.y)
             + M2 * (d.z * W.z) + M3 * (d.w * W.w);
    // 0.022 puts the RMS slope of the sum near 0.11 in tangent-space units,
    // which is a moss cushion and not a rock face. The bound is
    // 0.022 * 6 * sum(|M| * W) = 0.35, so a consumer adding this to n_ts.xy
    // tilts the normal by at most 19 deg and can never invert it. (0.030 was
    // the first render's number and it made a candle-lit floor of moss boil:
    // a relief this size only has to be FELT, and the moment it can be read
    // as a shape it is a pattern again.)
    return float4(v.x * W.x + v.y * W.y + v.z * W.z + v.w * W.w, g * 0.022);
}

/// How DEEP the cushion is here, 0..1 — the number that separates a film from a
/// cushion, and the one every "is this paint?" cue is weighted by.
///
/// m*m is the taper: a cushion has no vertical wall at its frontier, it thins
/// to nothing, and the square is what stops the whole patch reading as one
/// slab of uniform thickness (which is what ModBuild 143's single `m` did).
/// `field` is how long this pixel has been covered — the same patch field the
/// frontier advanced through, so the middle of an old patch is the deep part.
/// `grain` is the surface's own relief: moss is deeper in the mortar course and
/// in the fissure than on the face of the stone, because that is where it had
/// somewhere to sit.
float GhvrMossThick (float m, float field, float grain, float relief)
{
    return m * m * saturate(0.26 + 0.52 * field + 0.34 * grain)
             * (0.72 + 0.28 * (relief * 0.5 + 0.5));
}

/// Moss, laid on. A PIGMENT and not a light (an additive green over dark bark
/// glows like a screen), and a REPLACEMENT where it covers: at m = 1 the pixel
/// is moss, not greenish stone.
///
/// CHROMA, NOT VALUE, is what makes this readable, and the night wood is the
/// reason. Everything there is a dark blue-grey; a moss that is merely a darker
/// grey-green disappears into it, and a moss that is BRIGHTER is a lamp. So the
/// colour is strongly green and only slightly dark: the patch reads as a hue
/// against the moonlight without adding a single photon to the room.
///
/// TWO greens and not one — see MOSS REAL point 3. The gaps between the fronds
/// are nearly black and the tips are yellow-green; mixed by the micro-relief,
/// that is a granular surface rather than a poured one, and it is the single
/// change that most moves the picture away from "grüne Flecken".
///
/// EXACT AT ZERO: m = 0 gives lerp(alb, .., 0) = alb and band = 0, so the
/// return is alb * 1.0 — the same bits, which is what the zero-state rule in
/// EnvElement.cginc requires of every consumer.
float3 GhvrMossOn (float3 alb, float lum, float m, float thick, float relief)
{
    // The SPREAD between the two is a tuning, and it was halved after the first
    // render: near-black to bright green over a 4 cm cell is not a granular
    // surface, it is a pattern, and it made the moss read as printed fabric.
    // What is wanted is a patch that is visibly UNEVEN, not one that is visibly
    // PATTERNED — 3:1 in value across the mottle, which on a wall lit at 0.03
    // is about as much variation as the eye can still call one material.
    float3 c = lerp(float3(0.048, 0.090, 0.038),   // in the gaps: deep and dull
                    float3(0.175, 0.315, 0.110),   // on the lumps: yellow-green
                    saturate(relief * 0.85 + 0.5));
    // The stone shows through the THIN edge of the cushion and not through its
    // middle: 2 cm of moss does not carry the mortar course under it, a film
    // does. (ModBuild 143 carried the surface's luminance at full strength
    // everywhere, which is exactly how a patch stays legible AS the stone.)
    c *= lerp(0.50 + 1.00 * lum, 0.94, thick);
    // THE LIP. A cushion stands proud of what it grew on, so its border is a
    // crease and not a cut: a narrow occlusion band right through the frontier.
    // Three instructions, no geometry, and without it the patch still reads as
    // paint no matter how good its interior is.
    float band = m * (1.0 - m) * 4.0;
    return lerp(alb, c, m) * (1.0 - 0.34 * band * band);
}

/// The one channel of the albedo the two look functions need. Green, not a
/// luminance dot: it is one component instead of three multiplies and an add,
/// and on every texture in both rooms it tracks the luminance closely enough to
/// carry the modelling (that much ModBuild 142 had right).
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

/// The wind offset for one vertex, in OBJECT units.
///   p     object-space vertex position
///   w     per-vertex freedom, 0 at the attachment, 1 at the tip
///   t     the shared clock
///   dir   wind bearing in OBJECT space (unit)
///   side  a unit vector across the wind, for the flutter
///   amp   tip amplitude in object units — the STANDING breeze, always on
///   storm Air's strength, 0..1. 0 is exactly the ModBuild 143 breeze.
/// The returned offset has magnitude <= 1.06 * amp at storm 0 and <= 2.04 * amp
/// at storm 1, always (GhvrWave4 is bounded and so is every factor below).
float3 GhvrWind (float3 p, float w, float t, float3 dir, float3 side, float amp, float storm)
{
    // Phase in CYCLES from the vertex's own position, on a bearing that is not
    // the wind's: a constant with all three components means two boughs one
    // above the other are out of step as well as two side by side. |k| is
    // 0.12 cycles/m, an 8.3 m wave — see WITHIN-CARD SHEAR above.
    float ph = dot(p, float3(0.062, 0.041, 0.094));
    // THE GUST: a swell travelling DOWN-WIND at 1.36 m/s (0.075 cycles/s over
    // 0.055 cycles/m) at rest and 2.9 m/s in the storm — you see it cross the
    // clearing before it reaches you.
    float gust = dot(p, dir) * 0.055 - t * (0.075 + 0.085 * storm);
    // Three carriers and the gust, one float4, all independent. 4.3 s and 6.1 s
    // for the bend — a bough leans, it does not buzz — and 1.6 s for the leaf's
    // own flutter, falling to 0.6 s at full Air.
    float4 s = GhvrWave4(float4(t * float3(0.235, 0.163, 0.612 + 1.05 * storm) + ph, gust));
    float bend = s.x * 0.62 + s.y * 0.38;                 // exactly [-1, 1]
    // [0.62, 1] at rest, [0.26, 1] in the storm: the canopy half-stills and is
    // then shoved, which is the whole difference between wind and vibration.
    float env = lerp(0.62, 0.26, storm) + lerp(0.38, 0.74, storm) * (s.w * 0.5 + 0.5);
    // w*w, not w: the stiff half of a bough hardly moves and only the last
    // quarter really flies, which is what a conifer does and what keeps the
    // shear at the attachment invisible.
    float a = amp * w * w * env;
    // Across the wind and a little up: a card that only slid down-wind reads as
    // a sheet on a rail. Never along the NORMAL (see the Tree Creator note).
    float bendA = a * (1.0 + 0.85 * storm);
    float sideA = a * (0.30 + 0.45 * storm);
    float upA   = a * (0.16 + 0.24 * storm);
    return dir * (bend * bendA) + side * (s.z * sideA) + float3(0.0, s.z * upA, 0.0);
}

/// The GROW-IN of a whole card, for the grass and moss that Earth brings up.
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
float GhvrGrowCard (float3 q, float cover, float t)
{
    float A = GhvrGrowField(q);
    float g = GhvrGrow(A, cover, GhvrGrowCreep(q, t));
    // A blade that reaches full height the instant it is over the threshold pops.
    // Squaring the ease-out is the settle: fast out of the ground, slow to full.
    return g * (2.0 - g);
}

#endif // GHVR_ENV_GROWTH_INCLUDED
