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
//      they make. GhvrMossRelief below is that mottling, at 7-25 cm (the
//      weighting moved in ModBuild 146 — see S3 below).
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
//      SUPERSEDED IN ModBuild 146, and it is worth saying why rather than
//      quietly deleting it: this was RIGHT about needing colour variation and
//      WRONG about how much of it and where. Two colours of one hue, mixed
//      smoothly, measurably spend half the patch in the middle of the mix — one
//      green, softly modulated, which is slime. See S1/S2 below.
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
// ============================================================================
//  MOSS REAL, SECOND PASS — the slime verdict, and why the FIVE ABOVE WERE NOT
//  ENOUGH. This is the third complaint about the same surface, so the response
//  is a rebuild of the look function and not a tuning of its constants.
//
//  USER VERDICT, ModBuild 146 (hardware, verbatim):
//    "Das Moos gefällt mir immer noch nicht insbesondere nicht im Keller - es
//     sieht eher aus wie Schleim, es soll eher aussehen wie wuchende Pflanzen
//     und Pilze die an den Wänden wachsen."
//
//  WHAT MAKES A GREEN SURFACE READ AS SLIME RATHER THAN AS A COLONY. Five
//  properties, and ModBuild 145's moss had all five. They were MEASURED off the
//  functions below (400k samples of the real GhvrTri4 sum, not an impression):
//
//   S1. THE VALUE STRUCTURE WAS A GRADIENT, NOT A STRUCTURE. The colour was
//       lerp(gapGreen, tipGreen, saturate(relief*0.85 + 0.5)), and `relief` is a
//       weighted sum of four smoothed triangle waves whose distribution is
//       concentrated about zero: measured sigma 0.370, so the blend factor spent
//       54.5% of the covered area between 0.25 and 0.75. More than half of every
//       patch was a smooth mix of the two greens — which is to say ONE green,
//       softly modulated. A smooth continuous ramp over a wet-looking film is
//       the definition of slime. A colony is the opposite: discrete bodies with
//       DARK BETWEEN THEM, and the eye reads the darkness, not the bodies.
//   S2. THERE WAS NO HUE VARIANCE AT ALL. Both colours were green
//       (0.048,0.090,0.038 and 0.175,0.315,0.110 — the same hue, the same
//       saturation, two values). Real moss, lichen and fungus growing together
//       are never one hue: yellow-green, grey-green, ochre, rust-brown at the
//       dry edges, and near-white on a bracket fungus. One hue over a whole
//       surface is a coating; several hues over the same surface are organisms.
//   S3. THE MOTTLING WAS AT THE WRONG SCALE. The four corrugations were weighted
//       0.18/0.16/0.28/0.38, i.e. two thirds of the amplitude on the 13.6 cm and
//       25.1 cm waves. So the surface undulated at BODY scale — big soft swells
//       across the whole patch — and the 7-8.5 cm waves that are the size of an
//       actual cushion or bracket only rippled its edges. Big soft swells of one
//       green is, again, a sheet of slime; and the scale is what it is because
//       ModBuild 144 backed away from a 3.5-14 cm lattice that aliased, which
//       was the right retreat from the wrong position.
//   S4. IT WAS SHINY EXACTLY WHERE IT WAS LOOKED AT. The wet grazing sheen was
//       cut down to the thin frontier in ModBuild 145 — and the frontier is the
//       one part of a patch the eye traces to find its shape. A dark green shape
//       with a glossy rim is a slick. (Fixed in EnvRoom.shader, which owns the
//       sheen: indoors it is now multiplied out entirely.)
//   S5. THE CELLAR AND THE WOOD GREW THE SAME ORGANISM. "insbesondere nicht im
//       Keller" is the user pointing straight at this. Lawn-green moss belongs
//       on a forest floor under an open sky; four metres underground, on damp
//       stone, with no sun at all, what grows is lichen crust, bracket fungus
//       and etiolated (light-starved, pale, drawn-out) growth — pale ochre,
//       bone, grey-green, rust. Painting the cellar the wood's green is not a
//       shade too far, it is the wrong kingdom, and it is the single largest
//       and cheapest correction available.
//
//  WHAT IS DONE ABOUT EACH, all of it in GhvrMossRelief and GhvrMossOn below:
//   S1 -> THE CLUMP TRANSFER. The colour is no longer blended by the relief at
//         all. It is cut into three tiers by a THRESHOLD on a separate clump
//         field, smoothstep(0.02, 0.26), giving 72% body, 11% flank and 17% deep
//         crease (measured). The creases are ~2.4 cm wide on the wall — at three
//         metres that is a third of a degree, seven headset pixels, so it is a
//         line the eye can actually see and not a sub-pixel pattern that would
//         moire.
//   S2 -> TWO BODY HUES AND A CROWN, chosen per organism by `tone`, a SECOND and
//         nearly independent combination of the same four waves (measured
//         correlation with the relief 0.15, so a pale individual is not
//         systematically the tall one). `tone` is only ever a smooth hue
//         modulation and is never thresholded, which is why it may still ride on
//         the four waves when the clump field may not — see S3. Plus a dry rim
//         colour driven by the cushion's own thickness, because a colony browns
//         off at its edge where it is losing its damp — which is also the
//         strongest single cue that the patch has a BOUNDARY it grew to rather
//         than one it was cut to.
//   S3 -> THE INDIVIDUALS COME FROM A VALUE NOISE, not from the four waves, and
//         this is the one thing this round paid a render to learn. The first
//         attempt levelled the wave weights to 0.26/0.24/0.26/0.24 so the 7-8 cm
//         pair would lead, and cut the tiers out of that. The cellar floor came
//         back a LEOPARD SKIN: a regular lattice of dark spots. Four plane waves
//         on fixed axes are a crystal, and thresholding a crystal gives a crystal
//         of dots — which also explains why ModBuild 145 read as slime, because
//         the only way to hide the lattice in that field is to keep its contrast
//         so low that the surface has no structure at all. Slime and lattice were
//         the same property. So the clump field is now GhvrGrowField at 3.6x the
//         frontier's density (~9 cm cells): aperiodic, organic islands, at the
//         cost of eight more hashes on a path that only runs under Earth. The
//         four waves keep the smooth micro-corrugation and its free gradient,
//         at their ORIGINAL ModBuild 145 weights and bump strength — the normal
//         was never what was complained about. Full argument in GhvrMossRelief.
//   S4 -> EnvRoom.shader, quoted there.
//   S5 -> GhvrIndoor(). One shader, two palettes, two biologies. The cellar
//         grows pale crusts and fungus, the wood grows moss.
//
//  WHAT A SHADER STILL CANNOT DO, and it is owed to the bake lane rather than
//  hidden: a bracket fungus is a HORIZONTAL SHELF standing out of a vertical
//  wall, and no albedo function has a silhouette. The growth cards the cellar
//  grows today are vertical plumb quads, which is a tuft of grass, not a
//  polypore. See the report note; this file paints what is there and cannot
//  put a shelf on a wall.
//
//  REJECTED, this round:
//   * cutting the individuals out of the four waves, to keep the round free of
//     any extra hash. Tried, rendered, rejected — it is the leopard skin in S3,
//     and no weighting fixes it because the defect is that a sum of plane waves
//     is periodic.
//   * a distance-based detail LOD (fine texture near, coarse far), which is the
//     standard answer to S3's aliasing. _WorldSpaceCameraPos differs between the
//     two eyes by the IPD, so a detail level derived from it is a per-eye
//     albedo. The masonry wall fade already cost this project a round to stereo
//     rivalry; a cue that has no per-eye term in it at all is worth more than a
//     sharper moss.
//   * making the cellar's growth WARM OCHRE, on the argument that fungus is warm
//     against cold moonlit stone. Tried, rendered, rejected: three quarters of
//     the cellar wall a player looks at is lit by CANDLES, and warm growth on
//     warm stone under orange light vanishes into the masonry. The palette that
//     shipped is sage and cream — the two hues that are neither candle-warm nor
//     moon-cold — and it leans on the tiers rather than on the hue.
//   * making the cellar's growth GREY. It reads as mould, and mould on a wall is
//     the flat stain this whole block exists to escape.
//
//  REJECTED (ModBuild 145, and the FIRST of these was overturned in 146 — see
//  S3 above and GhvrMossRelief; it is left standing here because the reasoning
//  was sound for what the field had to do at the time):
//   * a second value-noise octave for the micro-texture (8 more hashes, ~40
//     ALU, on the one path that already runs a full octave). The corrugation
//     below is quasi-periodic rather than random, which for a 4 cm frond mat
//     under moonlight is a distinction without a difference — and it hands
//     over an exact derivative, which a value noise would charge two more
//     evaluations for. OVERTURNED: it stopped being a distinction without a
//     difference the moment the same field had to be cut into visible bodies.
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
///
/// `org` IS THE SECOND OUTPUT, new in ModBuild 146, and it is what the three
/// tiers in GhvrMossOn are cut from:
///   org.x  THE CLUMP FIELD, 0..1 — WHERE ONE ORGANISM ENDS AND THE NEXT BEGINS.
///   org.y  THE TONE, 0..1 — WHICH ORGANISM this is, i.e. what colour it takes.
///
/// WHY THE CLUMP FIELD IS A VALUE NOISE AND NOT THESE FOUR WAVES, which is the
/// one thing this round paid a render to learn and the reason the cost below is
/// accepted. The first attempt cut the crease/body/crown tiers straight out of
/// the corrugation above, with its weights levelled so the 7-8 cm waves led. On
/// paper it is right — the individuals come out individual-sized and it costs
/// nothing. In the render the cellar floor came out as a LEOPARD SKIN: a regular
/// lattice of dark spots, dead obvious at two metres.
///   The cause is structural rather than a tuning. Four plane waves on fixed
/// axes are a CRYSTAL. The old code hid that by never letting the field's
/// contrast rise — which is precisely why it read as slime, so "slime" and
/// "lattice" were the same property seen from two sides, and no weighting of
/// four waves can give one without the other. The warp (below) drags the crystal
/// about but does not dissolve it, and M0/M1 are 8.5 and 7.2 cm — a ratio of
/// 1.18, which is the "roughly equal lengths" case this file's own header warns
/// about. THRESHOLDING A CRYSTAL GIVES A CRYSTAL OF DOTS.
///   So the tiers are cut from GhvrGrowField instead — the same trilinear value
/// noise the frontier already advances through, evaluated at 3.6x the frontier's
/// density so its cells are ~9 cm. Value noise thresholded gives irregular
/// islands with no repeat anywhere a player can walk to, because it has no
/// period at all. THE COST IS EIGHT MORE HASHES, about 40 ALU, and this file's
/// header rejected exactly that in ModBuild 145 ("a second value-noise octave
/// for the micro-texture ... the corrugation is quasi-periodic rather than
/// random, which for a 4 cm frond mat under moonlight is a distinction without a
/// difference"). That was true while the relief only had to be FELT. It stopped
/// being true the moment the same field had to be cut into visible individuals,
/// and the render is the proof. It is paid only inside `if (ea > 0)`, i.e. only
/// while Earth is up and only on a material with _ElemMoss > 0.
///
/// The four waves keep the two jobs they are still the best tool for: the smooth
/// micro-corrugation WITHIN one organism, and its exact analytic gradient, which
/// is what buys the moss a normal for free. Their weights are unchanged from
/// ModBuild 145 (0.18/0.16/0.28/0.38, bump 0.022) — the bump was never what was
/// complained about, and a coarse bump is also the one that cannot alias.
/// `org.y` is a second, sign-alternating combination of the same four wave
/// values, so it is free: it varies the HUE smoothly and is never thresholded,
/// which is why a residual lattice in it is invisible where one in the clump
/// field was fatal. Measured correlation with the returned relief: 0.15 — a pale
/// individual is not systematically the tall one.
///
/// (SIGNATURE CHANGED this round: `out float2 org` is new. All three consumers —
/// EnvRoom, EnvGround, EnvRoomCutout — are updated in the same change.)
float4 GhvrMossRelief (float3 qIn, float warp, out float2 org)
{
    float3 q = qIn + warp * float3(3.71, -2.93, 5.27);
    const float3 M0 = float3( 3.41,  1.62, -1.10);   // |M| 3.93 ->  8.5 cm
    const float3 M1 = float3(-1.23,  3.05,  3.26);   // |M| 4.63 ->  7.2 cm
    const float3 M2 = float3( 1.79, -1.06,  1.31);   // |M| 2.46 -> 13.6 cm
    const float3 M3 = float3( 0.71,  0.94, -0.62);   // |M| 1.33 -> 25.1 cm
    float4 v, d;
    GhvrTri4(float4(dot(q, M0), dot(q, M1), dot(q, M2), dot(q, M3)), v, d);
    const float4 W = float4(0.18, 0.16, 0.28, 0.38); // sums to 1 => |value| <= 1
    // THE CLUMP FIELD. 3.6x the frontier's own density puts the noise lattice at
    // ~9 cm, so the islands it makes are 9-14 cm across: a bracket fungus, a
    // lichen plate, a moss cushion. The offset keeps it uncorrelated with the
    // frontier's own field (which the consumers sample at q + 37.1) — the
    // frontier decides WHETHER this pixel is grown on, the clump field decides
    // WHICH BODY it belongs to, and those must not be the same question.
    org.x = GhvrGrowField(q * 3.6 + 71.3);
    // WHICH ORGANISM. |B| does not have to sum to anything: the consumer maps it
    // through a saturate, and the 1.35 gain there is chosen against the measured
    // sigma of 0.365 so that about a third of the area lands at each end of the
    // palette and a third mixes. See GhvrMossOn.
    const float4 B = float4(0.34, -0.30, 0.22, -0.14);
    org.y = saturate(dot(v, B) * 1.35 + 0.5);
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

/// Growth, laid on. A PIGMENT and not a light (an additive green over dark bark
/// glows like a screen), and a REPLACEMENT where it covers: at m = 1 the pixel
/// is the organism, not tinted stone.
///
/// REBUILT IN ModBuild 146 on the slime verdict. Read MOSS REAL, SECOND PASS
/// above for the five measured reasons the previous version read as slime; this
/// is the answer to four of them (the fifth, the wet sheen, is EnvRoom's). The
/// old body was three lines: one lerp between two greens weighted by the raw
/// relief, one luminance carry-through and one lip. Everything below that is not
/// the carry-through or the lip is new.
///
///   `org`   GhvrMossRelief's second output. org.x is THE CLUMP FIELD (where one
///           body ends and the next begins) and org.y is THE TONE (which
///           organism it is, i.e. what colour it takes). Both 0..1.
///
/// THE THREE TIERS, all cut from org.x, and each is a different claim about what
/// the eye is looking at:
///   CREASE  the dark between the bodies. This is the tier that did not exist
///           before, and it is the one that decides whether the surface reads as
///           a colony or as a coating: a colony is legible because of its
///           SHADOWS. Near-black, over a measured 17% of the covered area, in
///           lines ~2.4 cm wide — narrow lines, not a mottle.
///   BODY    the organism itself, one of two hues chosen by org.y. In the wood
///           that is a shaded deep green and a yellow-green; in the cellar a
///           sage lichen crust and a cream etiolated growth.
///   CROWN   the top of the field, and only where the cushion is already deep:
///           the pale, almost-white cap of a bracket fungus, the sun-dried tip
///           of a moss cushion. It is a small area on purpose — a highlight that
///           covers a quarter of a patch is just a lighter patch.
/// ...plus a DRY RIM, keyed to thickness rather than to the field: a colony
/// browns off where it is thin and losing its damp, which is both true and the
/// strongest available cue that the boundary is one the thing GREW to.
/// ...and `relief`, the four-wave corrugation, survives as a gentle value
/// modulation WITHIN one body — the texture of a single cushion, under the
/// structure that separates it from its neighbour. It is deliberately weak and
/// deliberately never thresholded; see GhvrMossRelief for why anything cut hard
/// out of that field comes out as a lattice.
///
/// CHROMA, NOT VALUE, is still what makes this readable in the wood, and the
/// night forest is still the reason: everything there is a dark blue-grey, a
/// growth that is merely a darker grey disappears into it and one that is
/// BRIGHTER is a lamp. The cellar palette cannot use hue the same way, because
/// most of that room is lit by candles and a warm growth on warm stone under
/// orange light is invisible (measured, first render of this round). It leans on
/// the tiers instead: sage and cream bodies with near-black creases between them
/// and bone crowns on top, which separate under a candle and under the moonbeam
/// alike because what separates them is structure and not colour.
///
/// EXACT AT ZERO: m = 0 gives thick = 0 (GhvrMossThick has an m*m in it),
/// lerp(alb, .., 0) = alb and band = 0, so the return is alb * 1.0 — the same
/// bits, which is what the zero-state rule in EnvElement.cginc requires of every
/// consumer. None of the tiers below can change that: they only decide `c`.
///
/// SIGNATURE CHANGED this round (`float2 org` added). All three consumers —
/// EnvRoom, EnvGround, EnvRoomCutout — are updated in the same change.
float3 GhvrMossOn (float3 alb, float lum, float m, float thick, float relief, float2 org)
{
    // ---- THE INDIVIDUALS ------------------------------------------------
    // The clump transfer, on the value-noise field (NOT on `relief` — see
    // GhvrMossRelief for the leopard skin that cost). Measured against that
    // field's own distribution: 72% body, 11% flank, 17% deep crease, and the
    // crease is ~2.4 cm wide on the wall — at three metres that is a third of a
    // degree, seven headset pixels, so it is a line the eye resolves rather than
    // a sub-pixel pattern that would moire.
    //   The old code used `relief` RAW as a two-colour blend weight, which put a
    // measured 54.5% of every patch in the middle of the mix: one green, softly
    // modulated, i.e. slime (S1).
    float cap = smoothstep(0.02, 0.26, org.x);
    // the very tops only, and only where there is a cushion to have a top. The
    // field's own 2.4x contrast stretch clamps 14% of it at exactly 1.0, so the
    // crown lands on real PLATEAUX — a bracket cap is flat, which is convenient
    // rather than a compromise — and the thickness gate takes the area back to
    // ~8%. The upper bound of 1.05 is past the top of the field on purpose: even
    // on a plateau this saturates at 0.77, so the palest colour in the palette is
    // never laid on at full strength anywhere. A crown that reached 1.0 read as a
    // bleached patch rather than as a cap.
    float crown = smoothstep(0.88, 1.05, org.x) * smoothstep(0.14, 0.48, thick);
    // WHICH ORGANISM (already mapped to 0..1 by GhvrMossRelief against the
    // measured sigma, so about a third of the area lands at each end of the
    // palette and a third mixes). The wall carries individuals of two colours
    // standing next to each other, which is the thing a single hue can never say
    // however it is modulated (S2).
    float who = org.y;

    // ---- TWO BIOLOGIES ---------------------------------------------------
    // GhvrIndoor() is 1 in the cellar and 0 in the wood (EnvElement.cginc), and
    // this is the whole of S5. Four metres underground, on damp stone, with no
    // sun that ever reaches it, what grows is lichen crust, bracket fungus and
    // etiolated — light-starved, drawn-out, pigment-less — growth. Lawn green
    // down there is not a shade too far, it is the wrong kingdom, and it is what
    // "insbesondere nicht im Keller" is pointing at.
    // The lerp is per-fragment on a global uniform, so both palettes are in the
    // constant buffer and neither room pays a branch.
    //
    // THE CELLAR PALETTE IS PALE AND ONLY HALF-DESATURATED, and the first render
    // of this round is why. A first pass painted the indoor growth warm ochre, on
    // the argument that fungus is warm against cold moonlit stone. It is — and
    // three quarters of the cellar wall a player actually looks at is lit by
    // CANDLES, which are the warmest thing in the room, so ochre growth on ochre
    // stone under orange light simply disappeared into the masonry. The colours
    // below answer both lights instead of one: a SAGE grey-green body (the only
    // hue in the room that is neither candle-warm nor moon-cold, so it separates
    // under either) and a near-neutral CREAM one, with bone crowns and a rust rim.
    // Under the candles the patch reads by hue and by its crevices; under the
    // moonbeam it reads because it is warmer and paler than the blue-grey stone.
    float ind = GhvrIndoor();
    float3 cCrease = lerp(float3(0.016, 0.024, 0.012),   // wood: black-green shade
                          float3(0.024, 0.027, 0.021),   // cellar: neutral, damp
                          ind);
    float3 cBodyA  = lerp(float3(0.058, 0.112, 0.040),   // wood: deep shade moss
                          float3(0.105, 0.128, 0.092),   // cellar: sage lichen crust
                          ind);
    float3 cBodyB  = lerp(float3(0.190, 0.310, 0.105),   // wood: yellow-green tips
                          float3(0.216, 0.223, 0.162),   // cellar: cream etiolated growth
                          ind);
    float3 cCrown  = lerp(float3(0.290, 0.300, 0.165),   // wood: sun-dried tip
                          float3(0.425, 0.415, 0.355),   // cellar: bone fungus cap
                          ind);
    float3 cDry    = lerp(float3(0.135, 0.105, 0.052),   // wood: ochre-brown edge
                          float3(0.150, 0.110, 0.065),   // cellar: rust on stone
                          ind);

    // ---- ASSEMBLE --------------------------------------------------------
    float3 body = lerp(cBodyA, cBodyB, who);
    // the pale caps belong to the pale individuals: a bracket fungus is not a
    // bleached patch of the moss beside it, it is a different organism, and
    // tying the crown to `who` is what stops the highlight reading as a lighting
    // artefact laid over everything equally.
    body = lerp(body, cCrown, crown * (0.30 + 0.70 * who));
    // ...and the four-wave corrugation as a gentle value modulation INSIDE one
    // body: the texture of a single cushion, ±14%, never thresholded. This is
    // all that is left of ModBuild 145's use of `relief`, and it is the part of
    // it that was right — what was wrong was making it carry the colour.
    body *= 0.86 + 0.28 * (relief * 0.5 + 0.5);
    float3 c = lerp(cCrease, body, cap);
    // THE DRY RIM. Where the cushion is thin it is at its frontier, losing its
    // damp to the bare stone, and it browns off. smoothstep(0, 0.40) means the
    // middle of any mature patch never sees this at all.
    c = lerp(cDry, c, smoothstep(0.0, 0.40, thick));

    // The stone shows through the THIN edge of the cushion and not through its
    // middle: 2 cm of moss does not carry the mortar course under it, a film
    // does. (ModBuild 143 carried the surface's luminance at full strength
    // everywhere, which is exactly how a patch stays legible AS the stone.)
    c *= lerp(0.50 + 1.00 * lum, 0.94, thick);
    // THE LIP. A cushion stands proud of what it grew on, so its border is a
    // crease and not a cut: a narrow occlusion band right through the frontier.
    // Three instructions, no geometry, and without it the patch still reads as
    // paint no matter how good its interior is.
    //
    // DEEPER INDOORS (0.46 against the wood's 0.34), because the cellar palette
    // is the pale one: a bone-and-sage crust on light masonry has less VALUE
    // separation from what it grew on than a green cushion on black bark does,
    // so the one cue that says "this stands off the wall" has to carry more.
    float band = m * (1.0 - m) * 4.0;
    return lerp(alb, c, m) * (1.0 - lerp(0.34, 0.46, ind) * band * band);
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
//          *** THIS BULLET WAS WRONG AND THE TERM IS GONE. It is the whole of
//          the ModBuild 147 verdict below. The paragraph is left standing
//          because it is the record of what was tried, and because the next
//          lane needs to see that "the one the eye reads as wind SPEED" was the
//          exact reasoning that produced the twitch he is now reporting. Read
//          THE TWITCH before putting a `storm` on any rate in this file. ***
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
//
//  --------------------------------------------------------------- THE TWITCH
//  USER VERDICT, ModBuild 146 (hardware, verbatim):
//    "Wind führt zu einem sehr hektischen unrealistischen Zucken der Pflanzen,
//     des Feuers und der Bäume - mach das es sich mehr random und immersiver im
//     Wind bewegt, nicht so hektisch, so Mikrozuckungen hat."
//
//  THE FAULT IS ONE TERM AND IT IS NAMED IN THE PARAGRAPH ABOVE: the storm made
//  a RATE bigger. `0.612 + 1.05 * storm`. Everything else Air does here is an
//  amplitude, and none of it is at fault.
//
//  WHY THAT IS NOT A TUNING MISTAKE BUT A PHYSICAL ONE, which is the reason it
//  is fixed by deletion and not by halving the coefficient. Every moving part
//  of a plant is a DAMPED OSCILLATOR, and the defining property of one is that
//  its natural frequency is a property of the STRUCTURE — of its length, its
//  stiffness and its mass — and not of the force applied to it. Push a bough
//  harder and it swings FURTHER at the same rate; that is the whole content of
//  the word "resonance". Wind does three things to a canopy and raising the
//  frequency of a branch is none of them:
//    * it drives the same modes harder, so everything moves further;
//    * it is intermittent, so the canopy is shoved and then released — the
//      gust, which this function already has and which is the good part;
//    * it excites SMALLER structures that a breeze leaves alone. A twig or a
//      blade tip has its own, higher natural frequency, and in a light wind it
//      simply is not kicked hard enough to show. That is a NEW BAND appearing,
//      not an old band accelerating, and the difference between those two is
//      exactly the difference between a canopy coming alive and a canopy
//      vibrating.
//  The size/frequency ladder for the things this function actually moves:
//      a mature trunk       0.1-0.5 Hz     (nothing here: trunks do not move)
//      a 2 m bough          0.2-0.6 Hz     s.x 0.235 / s.y 0.163 — the bend
//      a 0.5 m leafy shoot  0.5-0.9 Hz     s.z 0.612 — the flutter, at rest
//      a blade / frond tip  1.3-2.0 Hz     the band the storm may light up
//      one leaf blade       5-15 Hz        TEXTURE. Not geometry. Never here.
//
//  THE MEASUREMENT, over 200 s at 4 kHz on the exact arithmetic below, one
//  canopy vertex at w = 1 (tip amplitude 4.5 cm), whole offset:
//                       f_mean   rms disp   rms speed   rms ACCEL
//      rest             0.46 Hz    2.12 cm    4.3 cm/s    13.6 cm/s^2
//      full Air, BEFORE 1.61 Hz    3.41 cm   19.4 cm/s   200.2 cm/s^2
//      full Air, AFTER  1.21 Hz    2.99 cm    8.4 cm/s    71.2 cm/s^2
//  Acceleration is the number to read and it is why "it is barely visible in a
//  still frame" was never a defence: the eye is far more sensitive to
//  acceleration than to displacement, and Air was multiplying it by FIFTEEN
//  while multiplying the displacement by 1.6. A motion that goes fifteen times
//  more violent and 1.6 times further is, in one word, a twitch.
//
//  AND THE PART THAT MATTERS MOST — THE SAME VERTEX AT HALF FREEDOM (w = 0.5),
//  i.e. the middle of a bough rather than its tip:
//      rest        3.4 cm/s^2      full Air BEFORE 50.1     AFTER 6.1
//  Before, the MIDDLE of every bough was accelerating fifteen times harder
//  under wind than at rest. A 2 m canopy card is not a leaf and must be nearly
//  still and slow; it is now within a factor of 1.8 of its resting state while
//  its tip is still five times livelier. That is what "the fast content belongs
//  to the smallest structures" means when it is written as a number.
//
//  WHAT REPLACES IT, and every one of these is exactly zero at storm = 0, so
//  THE RESTING BREEZE HE APPROVED IS PRESERVED TO THE BIT (verified: the
//  returned float3 at storm = 0 is identical to the shipped one for every
//  input, because each new term enters through a lerp(old, new, storm)):
//    1. THE FLUTTER BAND SPLITS INTO THREE. Air no longer moves 100 % of the
//       side/up excursion to one frequency; it spreads that same excursion over
//       0.487 / 0.612 Hz (the shoot) and 1.37 / 1.63 Hz (the blade tip), with
//       the fast pair entering on w*w INSIDE the mix — so, with the w*w already
//       in the amplitude, the fast band's profile is w^4: nothing at the
//       attachment, 9.5 % at half height, 38 % at the tip. This is the fire
//       lane's tip-flutter fix (EnvFlame's ZAPPELT block, item 1) applied to a
//       bough, and for the identical reason.
//         1.63 Hz is deliberately almost the frequency the old code used
//       (1.662 Hz at full storm). The old number was never wrong AS A
//       FREQUENCY — it is a fine rate for a grass blade's tip. It was wrong as
//       the frequency of the ENTIRE excursion of every structure in the wood,
//       including a two-metre bough. The band survives; what it is allowed to
//       move does not.
//    2. THE FLUTTER STOPS BEING A LINE. Side and up were BOTH `s.z`, i.e.
//       perfectly correlated (measured correlation 1.000), so every card
//       reciprocated along one fixed diagonal — a shake, not a flutter. The two
//       now take swapped weights with one sign flipped on the slow pair and two
//       different lanes on the fast pair; measured correlation 0.001, so the
//       tip traces an ellipse. Same trick, same file family, as the fire's
//       lateral wander.
//    3. THE GUST STOPS BEING A METRONOME. At full Air the envelope was one tone
//       at 0.16 Hz: a gust every 6.25 seconds, forever, which is the most
//       visible periodicity in the whole effect and the most likely literal
//       referent of "mehr random". A second, much slower swell (0.038 Hz, 26 s)
//       is mixed in at 34 %, so some gusts arrive big and some barely arrive.
//       Real wind's spectrum is broad and low; two bands is the cheapest thing
//       that is not a single tone.
//
//  WHAT IS DELIBERATELY UNCHANGED, and it is most of the function:
//    * every amplitude. bendA 1.0->1.85, sideA 0.30->0.75, upA 0.16->0.40 are
//      the ModBuild 143 numbers untouched, so the BOUNDS ARE UNTOUCHED — still
//      exactly 1.06 * amp at rest and 2.04 * amp in the storm, which is what
//      the canopy shadow-map argument above and ElemWind's build log both
//      state. Nothing this round costs a bake or moves a printed number. (The
//      splits are MIXES whose weights sum to 1, never sums, precisely so that
//      this stays provable rather than measured.)
//    * the gust's travel: 1.36 m/s at rest, 2.9 m/s in the storm. That IS a
//      rate that scales with wind, correctly — it is the advection speed of the
//      air itself, not the resonance of a branch — and it is the one place
//      where "the wind is faster" belongs.
//    * the bend's two carriers and the whole rest state.
//
//  IS AIR STILL "DEUTLICH HEFTIGER"? It has to be; he asked for that and got
//  it, and losing it is the next verdict. Full Air against rest, after:
//      rms displacement  x1.41     peak displacement  x1.83
//      rms speed         x1.95     rms acceleration   x5.2
//      and on the RENDERED PIXELS (the phase harness, forest canopy view):
//      total change over 2 s x1.08, over 3.1 s x1.16 — the pixel figure is a
//      whole-frame L1 mean, so most of it is sky and trunk that never moved and
//      it understates the foliage badly; it is quoted because it is measured,
//      not because it is the better number.
//
//  THE PIXELS, and this is the part a numeric harness cannot say. A still frame
//  cannot show a twitch and the shipped preview harness has no time series for
//  the forest at all, so this round built a throwaway one: 48 frames one 72 Hz
//  frame apart plus 40 frames 80 ms apart, four forest views, at rest and at
//  full Air, run before and after. The quantity is the TEMPORAL STRUCTURE
//  FUNCTION of the actual pixels — of everything that changes over 0.2 s, how
//  much has already happened after ONE frame? That ratio IS the complaint.
//      canopy view, full Air     one-frame change   0.2 s   saturation
//      BEFORE                        0.00028       0.00252     11.1 %
//      AFTER                         0.00012       0.00144      8.0 %
//      ...and THE RESTING BREEZE                                 7.7 %
//  The last line is the result. Under wind the picture's per-frame busy-ness,
//  measured against its own 0.2 s change, is now the same as the breeze he
//  approved: the wind changes how MUCH the wood moves and no longer changes the
//  CHARACTER of the motion. Before, it moved differently as well as more, and
//  "differently" was 11.1 % against 7.7 %. All four views agree (11.2->8.6,
//  9.5->8.1, 9.8->8.5 against resting 8.2 / 7.8 / 7.6).
//  Meanwhile the total change over 3.1 s is 0.00407 against 0.00471 — 14 % less
//  over three seconds for 57 % less per frame. Less change per frame, the same
//  change per second, which is the same signature the fire lane measured.
//  The rest rows came back BYTE-IDENTICAL before and after, on real pixels,
//  which is the bit-identity claim above verified rather than argued.
//      envelope          0.62..1.00 -> 0.26..1.00, i.e. the canopy now half-
//                        stills between gusts and is then shoved to the full
//                        1.85x lean — and at the CREST of a gust the offset is
//                        arithmetically identical to what shipped, because no
//                        amplitude changed. The storm's peak is as big as it
//                        ever was. Only its hurry is gone.
//
//  REJECTED, and why:
//    * halving `1.05 * storm` to `0.5 * storm`. It is the same defect at half
//      strength and it would have to be halved again next round; and it keeps
//      the untrue claim that a branch resonates faster in a gale.
//    * lowering the amplitudes instead. Nothing in "hektisch / Mikrozuckungen"
//      is about how FAR anything moves, and the two bounds above are a written
//      contract with the shadow bake. (The same rejection the fire lane made
//      for _Lick and _Sway, for the same reason.)
//    * adding a 5-15 Hz leaf band. That is the real frequency of a real leaf
//      and it is exactly the trap: a canopy CARD is a 1-2 m painted bough, not
//      a leaf, and moving it at leaf frequency is the fault being fixed, one
//      octave up. A leaf's own flutter is TEXTURE — it belongs in the sprite,
//      the way the fire's 8 Hz went into its UV ripple, and no card in this
//      wood has a shader that could carry it. Noted for whoever next owns
//      EnvRoomCutout; nothing here pretends to do it.
//    * raising the SPATIAL phase rate of the new bands to break the near-
//      lockstep between neighbouring plants (at 0.12 cycles/m everything within
//      a few metres moves nearly together). It is a real observation and it is
//      reported rather than acted on: canopy cards are up to 2 m across, so any
//      increase multiplies WITHIN-CARD SHEAR on the largest cards in the room,
//      and this round is not spending a bough's shape on it.
//    * a per-vertex damped-oscillator integration, which is what would give the
//      gust a physically correct lagged response. It needs per-vertex state
//      across frames; a vertex shader has none. (Same rejection, same words, as
//      EnvFire.cginc's.)

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
/// ModBuild 147 did not move either bound by a millimetre — see THE TWITCH.
///
/// AT storm = 0 THIS FUNCTION IS BIT-IDENTICAL TO THE ModBuild 143 ONE HE
/// APPROVED. Every ModBuild 147 term enters through lerp(shipped, new, storm),
/// and lerp(a, b, 0) is exactly `a`, so the resting breeze is not "close", it is
/// the same float3. That is a property of the code and not of a tuning, and it
/// is the reason the branch below can be read as an optimisation rather than as
/// a behaviour: whether the compiler takes it or flattens it, the result at
/// storm 0 is identical.
float3 GhvrWind (float3 p, float w, float t, float3 dir, float3 side, float amp, float storm)
{
    // Phase in CYCLES from the vertex's own position, on a bearing that is not
    // the wind's: a constant with all three components means two boughs one
    // above the other are out of step as well as two side by side. |k| is
    // 0.12 cycles/m, an 8.3 m wave — see WITHIN-CARD SHEAR above. EVERY band in
    // this function, including the two added in ModBuild 147, uses this one
    // spatial rate unmultiplied; see the last REJECTED note in THE TWITCH.
    float ph = dot(p, float3(0.062, 0.041, 0.094));
    // THE GUST: a swell travelling DOWN-WIND at 1.36 m/s (0.075 cycles/s over
    // 0.055 cycles/m) at rest and 2.9 m/s in the storm — you see it cross the
    // clearing before it reaches you. THIS is the one rate `storm` is allowed
    // to move, because it is the speed of the AIR and not the resonance of a
    // branch (THE TWITCH).
    float gust = dot(p, dir) * 0.055 - t * (0.075 + 0.085 * storm);
    // Three carriers and the gust, one float4, all independent. 4.3 s and 6.1 s
    // for the bend — a bough leans, it does not buzz — and 1.6 s for the leafy
    // shoot's own flutter.
    //
    // THE .z LANE USED TO READ `0.612 + 1.05 * storm` AND THAT WAS THE ENTIRE
    // "hektisches Zucken". A branch is a damped oscillator: wind changes how
    // hard it is driven, never the rate at which it answers. Full argument,
    // ladder of structure sizes and the before/after acceleration table are in
    // THE TWITCH above; do not put a `storm` back on any of these three.
    float4 s = GhvrWave4(float4(t * float3(0.235, 0.163, 0.612) + ph, gust));
    float bend = s.x * 0.62 + s.y * 0.38;                 // exactly [-1, 1]

    // The three things the storm re-shapes. Initialised to the SHIPPED resting
    // arithmetic, so the block below is purely additive in the diff sense.
    float flutS = s.z;                       // the flutter, across the wind
    float flutU = s.z;                       // ...and its vertical half
    float swell = s.w * 0.5 + 0.5;           // the gust envelope's 0..1 shape
    // A UNIFORM BRANCH. `storm` is e.air, which comes from _GhvrElemA — a global
    // uniform — so every vertex in every draw takes the same side of it and the
    // branch is perfectly coherent. With Air down (the common case, and the
    // permanent breeze runs in it) the second wave is not evaluated at all,
    // which is what keeps the ModBuild 143 promise that the ALWAYS-ON cost of
    // this function did not grow. If a compiler flattens it anyway the result is
    // unchanged, because the lerps below collapse to the shipped values at 0.
    if (storm > 1e-4)
    {
        // THE SECOND OCTAVE, and it exists only while there is a wind to excite
        // it. Air does not speed the shoot up; it lights up the SMALLER things
        // the shoot carries, which have their own higher natural frequencies.
        //   .x 0.503 Hz  a second shoot band, so the flutter is not one tone
        //   .y 1.370 Hz  a frond/blade tip — the vertical half
        //   .z 1.673 Hz  a frond/blade tip — the crosswind half. Deliberately
        //                almost the 1.662 Hz the old code drove EVERYTHING at:
        //                the rate was never the mistake, its target was.
        //   .w 0.038 Hz  a 26 s swell under the 6 s gust, so gusts stop
        //                arriving on a metronome ("mehr random").
        // THE EXACT VALUES ARE PICKED AGAINST RATIOS, not by taste, because a
        // sum of tones at rational ratios RE-PHASES and the eye finds the
        // repeat — and "mehr random" is half a request to have no repeat to
        // find. Checked over all n:d up to 8:8 across the eight rates now in
        // play (0.163, 0.235, 0.503, 0.612, 1.370, 1.673, and the two gusts
        // 0.038 and 0.075-0.160): the tightest coincidence any of the three NEW
        // rates makes with anything is 0.503 against 0.163 at 3:1, which drifts
        // apart in 71 s — several times longer than anyone looks at one tree.
        //   The first draft used 0.487 and 1.630, and 1.630 is EXACTLY 10 x
        // 0.163, the bend's own slow carrier: zero drift, a hard 6.13 s repeat,
        // parked right in the middle of the band this round exists to calm. It
        // was found by running the ratio table rather than by looking at the
        // render, which is the only way that class of mistake is ever found.
        float4 s2 = GhvrWave4(float4(t * float3(0.503, 1.370, 1.673) + ph
                                     + float3(0.21, 0.57, 0.83),
                                     t * 0.038 + dot(p, dir) * 0.019 + 0.37));
        // HOW MUCH OF THE FLUTTER IS THE FAST PAIR, by freedom. `a` below
        // already carries w*w, so this w*w makes the fast band's profile w^4:
        // 0 at the attachment, 9.5 % at half height, 38 % at the tip. A blade
        // tip may flick; the middle of a two-metre bough may not.
        float m = 0.38 * w * w;
        // The slow pair, with the weights swapped and one sign flipped between
        // the two axes: the two combinations are then UNCORRELATED (measured
        // 0.001, against 1.000 for the shipped `s.z` on both), so the tip traces
        // an ellipse instead of reciprocating along one fixed diagonal.
        float sideSlow = 0.62 * s.z  + 0.38 * s2.x;
        float upSlow   = 0.62 * s2.x - 0.38 * s.z;
        // MIXED, never summed — each pair's weights sum to 1, so |flut| <= 1
        // still holds and the 1.06 / 2.04 bounds are preserved by construction
        // rather than by measurement.
        flutS = lerp(s.z, (1.0 - m) * sideSlow + m * s2.z, storm);
        flutU = lerp(s.z, (1.0 - m) * upSlow   + m * s2.y, storm);
        // ...and the gust envelope gains its slow companion. Still 0..1, so the
        // envelope's range below is exactly what it was.
        swell = lerp(swell, 0.66 * swell + 0.34 * (s2.w * 0.5 + 0.5), storm);
    }
    // [0.62, 1] at rest, [0.26, 1] in the storm: the canopy half-stills and is
    // then shoved, which is the whole difference between wind and vibration.
    float env = lerp(0.62, 0.26, storm) + lerp(0.38, 0.74, storm) * swell;
    // w*w, not w: the stiff half of a bough hardly moves and only the last
    // quarter really flies, which is what a conifer does and what keeps the
    // shear at the attachment invisible.
    float a = amp * w * w * env;
    // Across the wind and a little up: a card that only slid down-wind reads as
    // a sheet on a rail. Never along the NORMAL (see the Tree Creator note).
    // ALL THREE ARE THE ModBuild 143 AMPLITUDES, UNCHANGED. The storm still
    // moves everything 1.85x / 2.5x further; that half of "deutlich heftiger"
    // was never what he was complaining about.
    float bendA = a * (1.0 + 0.85 * storm);
    float sideA = a * (0.30 + 0.45 * storm);
    float upA   = a * (0.16 + 0.24 * storm);
    return dir * (bend * bendA) + side * (flutS * sideA) + float3(0.0, flutU * upA, 0.0);
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
