// GloomhavenVR — THE WATER FILM's whole program, shared by both of WaterVR.shader's SubShaders.
//
// ============================================================================
//  WHY THIS IS AN INCLUDE AND NOT A PASS
// ============================================================================
//  WaterVR.shader declares the SAME surface twice: once with GPU tessellation
//  (LOD 300) and once without (LOD 100). Unity picks the first SubShader the
//  device supports, and `Shader.maximumLOD` lets the offscreen harness force
//  either one — which is how the contact sheet can PROVE that the tessellator
//  added geometry rather than assert it. Two SubShaders that drifted apart
//  would make that A/B a comparison of two different waters, so there is
//  exactly one copy of the maths and it lives here.
//
// ============================================================================
//  REQUIREMENT ONE, ABSOLUTE: NOTHING HERE MAY DEPEND ON THE VIEW DIRECTION
// ============================================================================
//  The defect five hardware rounds were spent on was, in the user's own words,
//  "die kopf-gebundene Reflektion" that "bewegen sich schnell mit den
//  Kopfbewegungen mit". There is no `unity_SpecCube0`, no `reflect()`, no
//  `texCUBE`/`samplerCUBE`/`UNITY_SAMPLE_TEXCUBE`, no `worldRefl`, no Fresnel
//  and NO VIEW VECTOR OF ANY KIND below. Not even a Blinn-Phong specular: a
//  half-vector highlight slides across the surface as the head moves, which is
//  the reported symptom re-created out of the mod's own shader.
//
//  AND IT IS NOT ONLY ABOUT THAT REPORT. Under MULTIPASS stereo each eye is a
//  separate render pass, so ANY view-dependent term produces a DIFFERENT image
//  per eye. This project has already parked one feature permanently over
//  exactly that (.planning/wall-fade-stereo-rivalry.md). A shader with no view
//  vector in it is PER-EYE IDENTICAL BY CONSTRUCTION.
//
//  THE TESSELLATION FACTOR IS PART OF THAT PROMISE. The textbook tessellation
//  shader scales its factor by the distance to the camera. That is a view
//  dependency in the GEOMETRY: the two MultiPass eyes stand ~6.4 cm apart, so
//  they would subdivide the same patch to different densities and the sampled
//  crest heights would differ between them — stereo rivalry along every
//  silhouette, which is the exact class of defect this module exists to remove.
//  So the factor is a FIXED uniform (_TessFactor) and the patch-constant
//  function reads nothing but that.
//
//  tests/GloomhavenVR.WireTests/WaterOwnSurfaceVectors.cs sweeps this file's
//  SOURCE — and the .shader's, and every other .cginc either includes — for
//  every spelling of an environment sample or a view vector, and fails the
//  build gate on a hit. That lint is the regression guard.
//
// ============================================================================
//  REQUIREMENT TWO, AND IT IS A USER RULING RATHER THAN A TUNING CHOICE:
//  THE PATTERN MUST NOT TRANSLATE. AT ALL. IN ANY DIRECTION. EVER.
// ============================================================================
//  ModBuild 165's verdict, verbatim: "Immer noch viel zu hektisch und es fließt
//  jetzt einmal in die eine Richtung, stoppt kurz und fließt dann wieder in die
//  andere. Erscheint nicht mehr immersiv. Ich will außerdem so gut wie KEIN
//  fließen, es ist kein Fluss sondern eine Pfütze. Die animationen sollen sehr
//  dezent und random sein!"
//
//  What he is describing is exactly what ModBuild 165 shipped. That build kept
//  a residual translation and made the rest of it a SWAY — an offset of the form
//  `rate * (P/2pi) * sin(2pi t/P)`, which travels one way, stops, and travels
//  back. The reasoning was that a motion netting to zero over a cycle is not a
//  current. That reasoning was wrong twice over: netting to zero over time is
//  not the same as not moving, and a pattern that reverses reads as MORE
//  artificial than a steady drift, because nothing on a real water surface does
//  it.
//
//  So the translation is not set to zero here. IT IS DELETED. There is no
//  `_SwellSpeed`, no `_WaterUVAnimSpeed*`, no drift share, no sway period and no
//  scroll term of any kind in this file — nothing a dial can raise and nothing a
//  future edit can re-enable by changing a number. Every coordinate that a wave
//  or a texture is sampled at is a function of WORLD POSITION ALONE; the clock
//  reaches the surface only through AMPLITUDES and WEIGHTS. That is the whole
//  motion model and it is the third time the user has said "kein Fluss, eine
//  Pfütze".
//
//  WHAT MOVES INSTEAD. Still water is not static; nothing about it travels.
//
//   (a) THE HEIGHT FIELD IS A SUM OF STANDING COMPONENTS,
//       sin(k d.p + phi) * cos(w t + psi). The spatial shape stays exactly where
//       it is and its amplitude breathes through zero and back, so crests rise
//       and fall IN PLACE. There are six of them at mutually incommensurate
//       wavelengths and therefore at six incommensurate bob periods, which is
//       what stops the sum from having any perceptible beat or rhythm.
//   (b) THE RIPPLE CROSSFADES INSTEAD OF SCROLLING. Each normal layer is sampled
//       at THREE fixed frames of the world XZ plane — turned by three fixed
//       angles and offset by three fixed vectors, with no time in the coordinate
//       at all — and the three are blended by weights that move on three
//       incommensurate cycles. Features therefore fade in and out where they
//       are, which is what fine structure on standing water does, and there is
//       no coordinate for a translation to hide in.
//   (c) THE BLOOM. A large-scale envelope of three long crossing STANDING
//       modulations decides which part of the pool is lively and which is nearly
//       still, and because the three breathe at three different rates the lively
//       part is somewhere else a few minutes later — a disturbance that swells
//       and decays in one place and then in another, with no travelling front
//       anywhere in it. That is what "random" looks like on a puddle.
//
//  AND THE IRREGULARITY IS A PROPERTY OF THE FIELD, NOT OF THE QUAD. A per-quad
//  random phase or seed is the obvious way to make tiles differ and it is
//  forbidden: it puts a step discontinuity in the height at every tile seam, and
//  17 films with visible cracks between them is a worse bug than the one being
//  fixed. The game lays these films out on a lattice of 1.73 x 1.998 m (hardware
//  census: TERRAIN_Water_Plane local bounds), the six components below are at
//  irrational ratios of each other and point in six unrelated directions, and
//  WaterOwnSurface.LatticeMismatch computes from the same table how far the
//  worst of them is from repeating on that lattice. The wire test fails the
//  build if it drops.
//
// ============================================================================
//  AND EVERYTHING IS SUBTLER THAN MODBUILD 165, BY A LARGE FACTOR
// ============================================================================
//  "Viel zu hektisch" was said of a surface with a 3.6 cm peak bobbing once
//  every 9 seconds. The shipped dials now give 1.3 cm once every 63 seconds on
//  the same film — a third of the height and a seventh of the rate — and the
//  ripple's own crossfades run on cycles of 102, 165 and 266 seconds. ModBuild
//  166's look was ACCEPTED and only its tempo was not ("nur finde ich es immer
//  noch schnell. Mach die animation halb so schnell"), so ModBuild 167 halved
//  ONE number — [Water] RippleSpeed, 0.035 -> 0.0175 — and every period above
//  doubled with it, leaving the relation between swell, crossfade and bloom
//  exactly as it was. The offscreen harness MEASURES both halves of the claim
//  (Assets/Editor/PreviewWaterVR.cs): the net translation of the pattern between
//  two instants, which must come back as exactly (0,0), and the fraction of
//  pixels that change per second, which must come back at about half ModBuild
//  166's figure.
#ifndef GHVR_WATERVR_INCLUDED
#define GHVR_WATERVR_INCLUDED

#include "UnityCG.cginc"

// No _MainTex_ST: the driver never sets a tiling or offset on the body texture (it binds none at
// all), and an unused _ST is a knob that reads as a setting.
sampler2D _MainTex;
sampler2D _Normal_Map;
fixed4 _Color;
float4 _NormalTilings, _LayerWeights, _LightDir, _RippleFade;
float _NormalStrength, _ProcNormal, _Shimmer, _WaveShade, _Smoothness;
float _SwellAmp, _SwellWave, _SwellPeriod, _SwellCalm;
float _TessFactor;
float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

#define GHVR_TAU 6.2831853

struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

struct v2f
{
    float4 pos  : SV_POSITION;
    // xy = the mesh's own UV (for _MainTex). zw = the WORLD XZ of this vertex, which is both the
    // ripple coordinate and the swell's phase coordinate — see EVERY HEX WAS THE SAME HEX.
    float4 uv   : TEXCOORD0;
    fixed4 color : COLOR;
};

// ================================================================== THE SWELL ==
// ONE STANDING COMPONENT: sin(k d.p + phi) * cos(w t + psi).
//
// THE SPATIAL PHASE CONTAINS NO CLOCK, AND THAT IS THE USER RULING OF MODBUILD 165 WRITTEN AS AN
// EXPRESSION. Up to and including that build this line read
//
//     float sp = k * (dot(dir, p) - drift * t) + spatialPhase;
//
// and the `- drift * t` is a translation: the whole component slides across the pool at `drift`
// metres per second. It was 2 cm/s, it was justified in a comment as "a trace so the nodes are not
// nailed to the stone", and the verdict on it was "Ich will außerdem so gut wie KEIN fließen, es ist
// kein Fluss sondern eine Pfütze". The term is deleted rather than set to zero precisely so that
// there is no dial, no property and no zero-valued parameter for a later edit to raise: the only
// way to bring the flow back is to write a new one, which the wire test
// WaterOwnSurfaceVectors.PatternNeverTranslates fails the build for.
//
// The product form is what makes this standing rather than travelling. A travelling train
// sin(k d.p - w t) moves bodily at w/k metres per second and reads as a current; this one has a
// spatial shape that never moves and an amplitude that breathes through zero and back. That is what
// the surface of a puddle does.
//
// Returns (h, dh/dx, dh/dz) together, because the fragment needs the DERIVATIVE of the very
// function the vertex program displaced by: a surface lit as though it were flat is the reported
// defect ("weiße streifen auf einer FLACHEN Oberfläche"), and the only way to be sure the light
// agrees with the relief is to differentiate the same expression rather than to author a second
// one that resembles it.
float3 GhvrStanding(float2 p, float t, float2 dir, float wavelength, float period,
                    float amp, float spatialPhase, float temporalPhase)
{
    float k = GHVR_TAU / max(wavelength, 0.05);
    float w = GHVR_TAU / max(period, 0.25);
    float sp = k * dot(dir, p) + spatialPhase;
    float bob = cos(w * t + temporalPhase);
    float c = cos(sp);
    return float3(amp * sin(sp) * bob, amp * k * dir.x * c * bob, amp * k * dir.y * c * bob);
}

// SIX COMPONENTS, AND THE NUMBERS ARE CHOSEN AGAINST THE TILE LATTICE.
//
// DIRECTIONS 17, 103, 61, 148, 47 and 164 degrees: none is axis-aligned, no two are 90 degrees
// apart, no two are within 13 degrees of each other, and none is the hex lattice's own 60-degree
// family — so no pair can conspire into a corrugation and none is aligned with a row of tiles.
//
// WAVELENGTH RATIOS 1, 1/phi, sqrt(2)-1, 2-sqrt(3), sqrt(3)-1 and 2sqrt(2)-2. These are irrational
// and mutually incommensurate, so the sum has no finite period in ANY direction: the field
// genuinely never repeats, rather than repeating on a cycle longer than the pool. phi is the "most
// irrational" number, which is why the largest two are a golden-ratio pair.
//
// SIX AND NOT FOUR, and the reason is TEMPORAL rather than spatial. The periods below are
// sqrt(wavelength ratio) of one another, so six wavelengths are six incommensurate bob rates; with
// four of them the sum still had a legible envelope — a few seconds of activity, then a lull — and
// a rhythm is exactly what "die animationen sollen sehr dezent und random sein" rules out. Six
// rates whose ratios share no common measure have no repeat and no beat at any timescale a player
// sits at a table for. It also divides the same peak amplitude over more crests, so each individual
// one is gentler.
//
// AMPLITUDES fall as ratio^1.25, normalised so the six together peak at exactly _SwellAmp. The
// exponent is what makes the SHORT components gentler in slope as well as in height (slope goes as
// ratio^0.25), so the longest wave carries the shape and the rest decorate it rather than competing.
// The amplitude property has to MEAN the peak displacement, because the bounds pad the driver
// writes is computed from it and a surface that travelled further than its own stated amplitude
// would be culled at the crests — one eye first, under MultiPass. |sin| <= 1, |cos| <= 1 and the
// modulation below is <= 1, so |h| <= _SwellAmp is an exact bound and not a hope.
//
// PERIODS scale as sqrt(wavelength ratio), which is deep-water dispersion (c = sqrt(gL/2pi))
// reduced to its shape: the short components bob faster than the long one, so the six never lock
// into one rigid figure. The ABSOLUTE rate is not physical and deliberately so — real water this
// size bobs about once a second, and the ruling is that this be a puddle.
// THE TWO NEW COMPONENTS WERE SEARCHED FOR, NOT CHOSEN. Every candidate ratio of the same
// irrational family was tried against every whole-degree direction at least 13 degrees from all the
// others, keeping only pairs that leave the lattice mismatch at or above 0.145 cycles at
// [Water] WaveScale 1, a median of at least 0.055 over the whole dial, and no round dial setting
// scoring better than the shipped one. The winner is sqrt(3)-1 at 47 degrees and 2sqrt(2)-2 at 164,
// and the number it leaves is 0.156 — the SAME 0.156 the four-component table scored, i.e. neither
// new component ever becomes the worst one. WaterOwnSurface.LatticeMismatch recomputes it from the
// C# copy of this table and the wire test fails the build if it falls under a tenth of a cycle.
#define GHVR_SWELL_ASUM 3.540547

// THE BLOOM — the slow, large-scale envelope that decides WHERE the pool is lively, and the term
// that answers "die animationen sollen sehr dezent und random sein" most directly.
//
// THREE LONG CROSSING STANDING MODULATIONS, at 4.1, 6.7 and 9.3 times the swell's own wavelength —
// 10, 16 and 22 m at the shipped size, all larger than the 8.6 m pool. Where they coincide the
// water is at full amplitude; where they cancel it is nearly still. So one end of the pool is quiet
// while the other moves.
//
// AND THEY ARE STANDING, WHICH IS THE MODBUILD 165 CORRECTION. That build wrote each one as
// sin(k d.p + w t), which is a TRAVELLING wave: the envelope of activity swept the pool at w/k, and
// a band of "livelier water" crossing the pool is exactly the flow the ruling forbids, only slower
// and larger than the one everybody was looking at. Each is now sin(k d.p + phi) * cos(w t + psi),
// so its bright and quiet regions are pinned to fixed places and only breathe.
//
// A SINGLE STANDING MODULATION WOULD PIN THE QUIET END FOR EVER, which is its own kind of frozen
// pattern. There are three, at three unrelated rates, so which of them dominates changes over
// minutes: the composite envelope's maxima are in one part of the pool now and in another part
// later, and they get there by fading rather than by travelling. That is the "very occasional, very
// small localised disturbance that swells and decays somewhere, then somewhere else" — built out of
// the continuous world-space field, so it is seam-free across every tile boundary by construction.
//
// It is BOUNDED IN [1-_SwellCalm, 1], never above 1, because the peak amplitude is what the
// renderer's bounds were padded for.
float GhvrSwellBloom (float2 p, float t, float L, float period, out float2 grad)
{
    const float2 dA = float2( 0.798636, 0.601815);   // 37 degrees
    const float2 dB = float2(-0.587785, 0.809017);   // 126 degrees
    const float2 dC = float2( 0.309017, 0.951057);   // 72 degrees
    float kA = GHVR_TAU / max(L * 4.1, 0.2);
    float kB = GHVR_TAU / max(L * 6.7, 0.2);
    float kC = GHVR_TAU / max(L * 9.3, 0.2);
    float T = max(period, 0.5);
    // THE BREATHING RATES ARE MULTIPLES OF THE SWELL'S OWN PERIOD, so [Water] RippleSpeed 0 freezes
    // the whole surface — envelope included — instead of leaving a slow pulse running under a
    // motionless swell. 3.1 / 4.7 / 7.3 are mutually incommensurate for the same reason the swell's
    // are: an envelope with a beat is a rhythm, and a rhythm is not what a puddle has.
    float bA = cos(GHVR_TAU * t / (T * 3.1));
    float bB = cos(GHVR_TAU * t / (T * 4.7) + 2.1);
    float bC = cos(GHVR_TAU * t / (T * 7.3) + 4.3);
    float qA = kA * dot(dA, p);
    float qB = kB * dot(dB, p) + 1.3;
    float qC = kC * dot(dC, p) + 3.7;
    float m = (sin(qA) * bA + sin(qB) * bB + sin(qC) * bC) / 3.0;          // [-1, 1]
    float2 gm = (kA * dA * cos(qA) * bA + kB * dB * cos(qB) * bB
                 + kC * dC * cos(qC) * bC) / 3.0;
    float calm = saturate(_SwellCalm);
    grad = 0.5 * calm * gm;
    return 1.0 - calm * (0.5 - 0.5 * m);                                   // [1-calm, 1]
}

float3 GhvrSwell (float2 p, float t)
{
    float L = max(_SwellWave, 0.15);
    float T = max(_SwellPeriod, 0.5);

    // The six components. Written out rather than looped over a constant array: every number sits
    // next to the sentence that justifies it, and nothing here has to survive an array-indexing
    // translation into GLSL on the one platform that would silently unroll it differently.
    float3 s;
    s  = GhvrStanding(p, t, float2( 0.956305, 0.292372), L * 1.000000,
                      T * 1.000000, 1.000000 / GHVR_SWELL_ASUM, 0.000, 0.000);
    s += GhvrStanding(p, t, float2(-0.224951, 0.974370), L * 0.618034,
                      T * 0.786151, 0.547981 / GHVR_SWELL_ASUM, 2.399, 1.777);
    s += GhvrStanding(p, t, float2( 0.484810, 0.874620), L * 0.414214,
                      T * 0.643595, 0.332300 / GHVR_SWELL_ASUM, 4.113, 3.412);
    s += GhvrStanding(p, t, float2(-0.848048, 0.529919), L * 0.267949,
                      T * 0.517638, 0.192781 / GHVR_SWELL_ASUM, 1.071, 5.108);
    s += GhvrStanding(p, t, float2( 0.681998, 0.731354), L * 0.732051,
                      T * 0.855600, 0.677137 / GHVR_SWELL_ASUM, 5.602, 2.483);
    s += GhvrStanding(p, t, float2(-0.961262, 0.275637), L * 0.828427,
                      T * 0.910180, 0.790347 / GHVR_SWELL_ASUM, 3.246, 0.914);

    float2 bloomGrad;
    float bloom = GhvrSwellBloom(p, t, L, T, bloomGrad);

    // PRODUCT RULE, because the envelope is a function of position too. h = A(p,t) * S(p,t), so
    // grad h = A grad S + S grad A. Dropping the second term would light the pool as though the
    // quiet end were as steep as the busy one — small here (A varies over 10 m) but it is the
    // whole reason the gradient is derived rather than authored.
    float h = _SwellAmp * bloom * s.x;
    float2 g = _SwellAmp * (bloom * s.yz + s.x * bloomGrad);
    return float3(h, g);
}

// THE VERTEX PROGRAM MOVES THE VERTEX, and only along world Y.
//
// CULLING CANNOT SEE A VERTEX PROGRAM — nor a domain program. Unity culls a renderer against its
// bounds, and geometry pushed outside them is culled anyway: a displaced surface vanishes as you
// walk up to it and, under MultiPass, vanishes in ONE EYE FIRST because the two eye frustums
// differ. This project has already lost a build to that. Here the displacement is a translation
// along world Y bounded by _SwellAmp, so the swept volume is exactly the rest bounds
// Minkowski-summed with a segment of length twice that — and the driver writes
// Renderer.localBounds padded by WaterOwnSurface.MaxSwellAmplitude on every axis, which contains
// that segment whatever the quad's orientation. It is the true swept volume, not a guess-pad.
v2f GhvrWaterVert (appdata v)
{
    v2f o;
    float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
    float t = _Time.y + _GhvrTimeOfs;
    wp.y += GhvrSwell(wp.xz, t).x;
    o.pos = UnityWorldToClipPos(wp);
    // ===================================== EVERY HEX WAS THE SAME HEX ==
    // The game places its water as SEPARATE quads — 17 of them in the report's room, each a
    // TERRAIN_Water_Plane instance with its own transform — and each one's UV runs 0..1 across
    // itself. A ripple keyed on that UV, or on the UV shifted by the quad's own world origin,
    // therefore draws the IDENTICAL pattern on every hex in the pool.
    //
    // The ripple and the swell are both keyed on the vertex's WORLD XZ instead, which is exact
    // rather than approximate: every tiling below is expressed in repeats per WORLD UNIT, so the
    // pattern is one continuous field the quads are cut out of, and no seam and no per-quad
    // discontinuity can exist by construction. That leaves exactly one way a per-tile pattern can
    // come back — a field whose own period happens to divide the tile lattice — and the six
    // incommensurate components above are what closes it.
    //
    // The displacement is VERTICAL, so XZ is untouched by it and the fragment can re-evaluate the
    // swell at exactly the position the vertex was moved from.
    o.uv = float4(v.uv, wp.xz);
    o.color = v.color;
    return o;
}

// ================================================== UNPACKING THE BUMP ==
// The RG-or-AG form, written out rather than taken from UnpackNormal, because the texture is the
// GAME's and its import settings are not ours to know. DXT5nm stores x in .a with .r pinned at 1;
// a plain RGB normal map stores x in .r with .a at 1. `r * a` is therefore x under BOTH
// conventions, and a shader that guessed wrong would produce a ripple that only tilts along one
// axis — which reads as corduroy, not as water, and would be blamed on the maths rather than on an
// importer.
float2 GhvrBumpXY (float4 packed)
{
    return float2(packed.r * packed.a, packed.g) * 2.0 - 1.0;
}

// ================================================== THE FALLBACK RIPPLE ==
// Used ONLY when the driver could not read _Normal_Map off the game material (_ProcNormal = 1);
// the census says so in as many words when it happens, because a still sheet and a sheet rippling
// off the wrong source look identical in a report.
//
// The analytic gradient of two crossing wave trains whose crests do not line up:
//   h = sin(k u + 0.8 sin(k v)) + 0.6 sin(k (2v - u) - 1.1)
// The cross-modulation in the first term is what stops a pair of plane waves reading as a
// corrugated sheet.
//
// EVERY COEFFICIENT INSIDE A sin() IS AN INTEGER MULTIPLE OF k, so this field has period exactly 1
// in both axes — which is what makes the fixed fractional offsets below land on a seamless field
// here as well as on the Repeat-wrapped texture path.
float2 GhvrProcBumpXY (float2 p)
{
    const float k = GHVR_TAU;
    float a = k * p.x + 0.8 * sin(k * p.y);
    float b = k * (2.0 * p.y - p.x) - 1.1;
    float2 g;
    g.x = k * cos(a) - 0.6 * k * cos(b);
    g.y = 0.8 * k * cos(k * p.y) * cos(a) + 1.2 * k * cos(b);
    return g * 0.05;
}

// ============================================== ONE SAMPLING FRAME, AND IT HAS NO CLOCK IN IT ==
// THIS FUNCTION TAKES NO TIME ARGUMENT, AND THAT IS THE POINT. Up to and including ModBuild 165 the
// coordinate the normal map was sampled at was `worldXZ * tiling + GhvrRippleOffset(rate, ..., t)`,
// and that offset was a one-way drift plus a sway — a term that moved the pattern bodily across the
// pool, first one way and then the other. Both are gone: there is no rate property left to resolve,
// no offset function left to call, and this signature has nowhere to put a clock. The sampled
// coordinate is a fixed, invertible map of the WORLD position and nothing else, so the features it
// carries are nailed to the stone and cannot travel however the weights below move.
//
// WHY A ROTATION AND NOT JUST AN OFFSET. The three frames exist so that three DIFFERENT patterns can
// be crossfaded in place. Three offsets of one texture are three copies of the same pattern in three
// places, and a crossfade between two of those is a smeared translation — the exact thing being
// removed, arrived at by the back door. A rotation by an angle that is not a multiple of the
// texture's own symmetry gives genuinely unrelated structure, so a fade between two frames is a fade
// and never a slide.
//
// AND THE GRADIENT IS ROTATED BACK. Sampling at q = R p means the height field seen here is
// H(R p), whose gradient in the SURFACE's own frame is R^T (grad H)(R p) — so the unpacked bump,
// which is a gradient in the texture's frame, is turned by -theta to come back into the frame the
// shading is done in. Skipping that would tilt every layer's detail into its own private direction
// and the three frames would disagree about which way "along U" is.
float2 GhvrRippleUV (float2 p, float2 tiling, float2 cs, float2 ofs)
{
    return float2(p.x * cs.x - p.y * cs.y, p.x * cs.y + p.y * cs.x) * tiling + ofs;
}

float2 GhvrRippleBump (float2 p, float2 tiling, float2 cs, float2 ofs)
{
    float2 q = GhvrRippleUV(p, tiling, cs, ofs);
    // The branch is on a UNIFORM (_ProcNormal is written once per material by the driver), so it is
    // fully coherent across every fragment in flight and costs a scalar compare rather than both
    // paths. It is a property and not a keyword so the census can read back which one is live.
    float2 b = (_ProcNormal > 0.5) ? GhvrProcBumpXY(q) : GhvrBumpXY(tex2D(_Normal_Map, q));
    return float2(b.x * cs.x + b.y * cs.y, -b.x * cs.y + b.y * cs.x);
}

// ============================================== THE CROSSFADE, AND IT IS ALL THAT MOVES HERE ==
// Three weights that ALWAYS SUM TO EXACTLY ONE, moving on the three incommensurate cycles the
// driver resolves into _RippleFade (102, 165 and 266 seconds at the shipped dial). Normalising rather
// than just adding three cosines is what keeps the ripple's total strength constant: an unnormalised
// sum would breathe in AMPLITUDE, and a whole pool getting rougher and smoother together is a pulse,
// which is a rhythm, which is what the ruling forbids. What changes is only WHICH of the three fixed
// patterns is showing, and it changes by fading, in place.
//
// THE DEPTH IS 0.5 rather than 1, and that number is what stops the crossfade reading as a pulse.
// Each raw term is 1 +/- 0.5, so after normalising, a weight can only reach 0.14 at its quietest and
// 0.60 at its loudest — no frame ever vanishes and no frame ever stands alone. Three unrelated
// fields blended at weights w add in quadrature, so the blended detail's RMS goes as sqrt(sum w^2),
// which over that whole range runs 0.577 (all three equal) to 0.663 (one at its corner): a seventh,
// at the extreme, and the extreme needs all three cosines lined up, which periods sharing no common
// measure reach only rarely. The surface therefore does not visibly roughen and smooth as the mix
// moves; only WHICH pattern is showing changes.
float3 GhvrFadeWeights (float t)
{
    float3 a = 1.0 + 0.5 * float3(
        cos(GHVR_TAU * t / max(_RippleFade.x, 1.0)),
        cos(GHVR_TAU * t / max(_RippleFade.y, 1.0) + 2.094),
        cos(GHVR_TAU * t / max(_RippleFade.z, 1.0) + 4.189));
    return a / (a.x + a.y + a.z);
}

fixed4 GhvrWaterFrag (v2f i) : SV_Target
{
    // ONE CLOCK. _Time.y is the shared frame clock and _GhvrTimeOfs is the preview-only offset the
    // batch renderer steps (EnvRoom.shader), which is what lets an offscreen render show this
    // surface at two different instants without a headset.
    float t = _Time.y + _GhvrTimeOfs;

    // TWO RIPPLE LAYERS at the two resolved tilings, each sampled in THREE fixed frames of the world
    // XZ plane. The three angles (23, 67 and 138 degrees) and the three offsets are compile-time
    // constants: they are what makes the three frames carry unrelated structure, and none of them is
    // a setting — a dial on them would be a dial on which pattern is being crossfaded, which is not
    // a thing anyone can judge.
    const float2 cs0 = float2( 0.920505,  0.390731);   // 23 degrees
    const float2 cs1 = float2( 0.390731,  0.920505);   // 67 degrees
    const float2 cs2 = float2(-0.743145,  0.669131);   // 138 degrees
    const float2 o0  = float2( 0.000000,  0.000000);
    const float2 o1  = float2( 0.381966,  0.618034);
    const float2 o2  = float2( 0.726543,  0.171573);

    float2 tilA = _NormalTilings.xy, tilB = _NormalTilings.zw;

    // ONE WEIGHT SET, AND LAYER B TAKES IT ROTATED. The two layers must not reach the same frame at
    // the same moment — a coarse and a fine layer crossfading in lockstep is one event on the whole
    // surface rather than two independent ones — and a cyclic permutation guarantees that for free:
    // it still sums to exactly 1, it costs no extra trig, and it puts B's fade a third of the way
    // round the cycle from A's whatever the periods are.
    float3 wA = GhvrFadeWeights(t);
    float3 wB = wA.yzx;

    float2 bumpA = wA.x * GhvrRippleBump(i.uv.zw, tilA, cs0, o0)
                 + wA.y * GhvrRippleBump(i.uv.zw, tilA, cs1, o1)
                 + wA.z * GhvrRippleBump(i.uv.zw, tilA, cs2, o2);
    float2 bumpB = wB.x * GhvrRippleBump(i.uv.zw, tilB, cs0, o0)
                 + wB.y * GhvrRippleBump(i.uv.zw, tilB, cs1, o1)
                 + wB.z * GhvrRippleBump(i.uv.zw, tilB, cs2, o2);

    // WEIGHTED, NOT SUMMED. Equal weights let the fine layer compete with the coarse one; the
    // weights the driver resolves are inversely proportional to each layer's own frequency and sum
    // to 1, so the coarse layer carries the shape and the fine one decorates it.
    float2 bump = _LayerWeights.x * bumpA + _LayerWeights.y * bumpB;

    // THE FRAME. The blended tangent-space normal is used directly as a SURFACE-LOCAL normal: x
    // along U, y along V, z along the surface normal. That is exact here rather than approximate —
    // the game's water film is a flat horizontal quad (hardware FLOOR CENSUS: 'TERRAIN_Water_Plane'
    // local bounds size (1.73, 0, 1.998)) — and it is chosen over a world-space TBN because
    // building one needs the MESH's tangents, and an Apparance-instanced quad whose importer
    // settings we do not own may not carry any. A missing tangent is a black surface; a
    // surface-local frame cannot fail that way.
    //
    // AND THE SWELL'S OWN SLOPE IS THE FIRST TERM. A height field h(x,z) has normal
    // (-dh/dx, -dh/dz, 1) in exactly this frame, so the analytic gradient of the wave that displaced
    // the vertex enters the same expression the ripple does — the light and the geometry cannot
    // disagree, because they are one function.
    float3 sw = GhvrSwell(i.uv.zw, t);
    float3 n = normalize(float3(-sw.yz + bump * _NormalStrength, 1.0));

    // THE ONLY DIRECTION IN THIS SHADER, and it is a UNIFORM: it comes from the scene's own main
    // directional light (or a fixed constant when the scene has none — the driver logs which). It
    // does not depend on where the camera is, so this dot product is identical in both eyes'
    // passes by construction.
    float3 L = normalize(_LightDir.xyz + float3(0, 0, 1e-4));
    float ndl = saturate(dot(n, L));

    // THE UNDISTURBED SHEET IS THE ZERO POINT, and this is what keeps a calm surface calm. A flat
    // film has n = (0,0,1), so its response is exactly L.z. Referencing both the shading and the
    // glint to ndl0 makes still water EXACTLY the authored tint and lets only the waves move it,
    // which is also the standing invariant of this module: the replacement may never be brighter
    // than what the tileset authored.
    float ndl0 = saturate(L.z);

    // THE BODY. Crests lean into the light and troughs away from it, so the sheet has moving
    // structure even where nothing glints. The soft knee (x/(1+|x|)) keeps the response inside
    // (-1,1) without a clamp: a clamp would flatten the deepest troughs into plateaus of one flat
    // colour with a visible edge, which reads as a stain rather than as water.
    float rel = (ndl - ndl0) / max(1.0 - ndl0, 0.05);
    rel = rel / (1.0 + abs(rel));
    float3 body = tex2D(_MainTex, i.uv.xy).rgb * _Color.rgb * i.color.rgb
                * (1.0 + _WaveShade * rel);

    // THE GLINT, BROAD AND DIM. pow() of the same view-independent dot, widened by the tileset's
    // own _Smoothness. Subtracting the flat sheet's own response makes still water glint EXACTLY
    // zero, so the broad lobe cannot become a pedestal over the whole pool; only a crest tilted
    // toward the light adds light.
    float e = lerp(1.0, 8.0, saturate(_Smoothness));
    float glint = _Shimmer * max(pow(ndl, e) - pow(ndl0, e), 0.0);

    // THE GLINT DOES NOT TOUCH THE ALPHA. It used to be added into it, so a highlight went bright
    // AND opaque in the same pixel — which is a white streak by construction. The film's opacity is
    // the tileset's authored alpha and nothing else.
    float alpha = saturate(_Color.a * i.color.a);
    return fixed4(body + glint.xxx, alpha);
}

#endif // GHVR_WATERVR_INCLUDED
