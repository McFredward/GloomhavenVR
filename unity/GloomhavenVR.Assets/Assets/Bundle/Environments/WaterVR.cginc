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
//  function reads nothing but that. It costs more triangles far away than a
//  distance ramp would; it is the only version that is safe here.
//
//  tests/GloomhavenVR.WireTests/WaterOwnSurfaceVectors.cs sweeps this file's
//  SOURCE — and the .shader's, and every other .cginc either includes — for
//  every spelling of an environment sample or a view vector, and fails the
//  build gate on a hit. That lint is the regression guard.
//
// ============================================================================
//  REQUIREMENT TWO: IT MUST LOOK LIKE A PUDDLE OF STANDING WATER
// ============================================================================
//  ModBuild 164's verdict, verbatim: "Schon deutlich besser! Allerdings: a) Es
//  fließt noch viel zu schnell! Das ist kein Fluss sondern soll eher eine
//  Pfütze stehendes Wasser simulieren mit nur minimal Bewegungen. b) Aktuell
//  scheint die Animation bei jedem tile identisch zu sein, bring mehr
//  randomness rein! ... Aber auch deutlich ruhiger und eher dezent."
//
//  Both halves are answered by the SWELL below, and neither by a smaller
//  number on the old one:
//
//   (a) STANDING WATER HAS NO NET DRIFT. Through ModBuild 164 the swell was two
//       TRAVELLING trains, sin(k·x - wt): a pattern that moves bodily across
//       the pool, which is what a current looks like and is exactly what "das
//       ist kein Fluss" rejects. It is now a sum of four STANDING components,
//       sin(k·x + phi) * cos(wt + psi): the crests rise and fall IN PLACE. A
//       trace of drift (_SwellSpeed, shipped at 2 cm/s) is left so the pattern
//       is not nailed to the stone, and that is the ONLY bodily motion left in
//       the geometry.
//   (b) A PER-TILE PATTERN CANNOT EXIST IN A CONTINUOUS WORLD-SPACE FIELD —
//       unless the field happens to repeat on the tile lattice, which is what
//       ModBuild 164's did. The game lays these films out on a lattice of
//       1.73 x 1.998 m (hardware census: TERRAIN_Water_Plane local bounds), and
//       164 ran ONE wavelength of 1.1 m: two of its own periods land within 6%
//       of that lattice, so the field very nearly repeated tile for tile even
//       though it was keyed on world position. The four components below are at
//       IRRATIONAL ratios of each other (1, 1/phi, sqrt(2)-1, 2-sqrt(3)) and
//       point in four unrelated directions, and at the shipped wavelength every
//       one of them is at least 0.156 of a cycle away from repeating on either
//       lattice vector. WaterOwnSurface.LatticeMismatch computes that number
//       from the same table and the wire test fails the build if it drops.
//
//       AND THE IRREGULARITY IS A PROPERTY OF THE FIELD, NOT OF THE QUAD. A
//       per-quad random phase or seed would be the obvious way to make tiles
//       differ and it is forbidden: it puts a step discontinuity in the height
//       at every tile seam, and 17 films with visible cracks between them is a
//       worse bug than the one being fixed.
#ifndef GHVR_WATERVR_INCLUDED
#define GHVR_WATERVR_INCLUDED

#include "UnityCG.cginc"

// No _MainTex_ST: the driver never sets a tiling or offset on the body texture (it binds none at
// all), and an unused _ST is a knob that reads as a setting.
sampler2D _MainTex;
sampler2D _Normal_Map;
fixed4 _Color;
float4 _NormalTilings, _LayerWeights, _WaterUVAnimSpeedA, _WaterUVAnimSpeedB, _LightDir, _RippleSway;
float _NormalStrength, _ProcNormal, _Shimmer, _WaveShade, _Smoothness;
float _SwellAmp, _SwellWave, _SwellSpeed, _SwellPeriod, _SwellCalm;
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
// ONE STANDING COMPONENT: sin(k(d.p - v t) + phi) * cos(w t + psi).
//
// The product form is what makes this standing rather than travelling. A travelling train
// sin(k d.p - w t) moves bodily at w/k metres per second and reads as a current; this one has a
// spatial shape that stays where it is and an amplitude that breathes through zero and back. That
// is what the surface of a puddle does, and "das ist kein Fluss sondern soll eher eine Pfütze
// stehendes Wasser simulieren" is the whole brief.
//
// `drift` is the trace of real translation left in it, in world units per second. Without it the
// component's NODES — the lines where sin(k d.p + phi) is zero — would sit at fixed world
// positions forever, which is its own kind of frozen pattern. Four components at four directions
// already break that up; the drift makes it impossible.
//
// Returns (h, dh/dx, dh/dz) together, because the fragment needs the DERIVATIVE of the very
// function the vertex program displaced by: a surface lit as though it were flat is the reported
// defect ("weiße streifen auf einer FLACHEN Oberfläche"), and the only way to be sure the light
// agrees with the relief is to differentiate the same expression rather than to author a second
// one that resembles it.
float3 GhvrStanding(float2 p, float t, float2 dir, float wavelength, float period,
                    float amp, float spatialPhase, float temporalPhase, float drift)
{
    float k = GHVR_TAU / max(wavelength, 0.05);
    float w = GHVR_TAU / max(period, 0.25);
    float sp = k * (dot(dir, p) - drift * t) + spatialPhase;
    float bob = cos(w * t + temporalPhase);
    float c = cos(sp);
    return float3(amp * sin(sp) * bob, amp * k * dir.x * c * bob, amp * k * dir.y * c * bob);
}

// FOUR COMPONENTS, AND THE NUMBERS ARE CHOSEN AGAINST THE TILE LATTICE.
//
// DIRECTIONS 17, 103, 61 and 148 degrees: none is axis-aligned, no two are 90 degrees apart, and
// none is the hex lattice's own 60-degree family — so no pair can conspire into a corrugation and
// none is aligned with a row of tiles.
//
// WAVELENGTH RATIOS 1, 1/phi, sqrt(2)-1 and 2-sqrt(3). These are irrational and mutually
// incommensurate, so the sum has no finite period in ANY direction: the field genuinely never
// repeats, rather than repeating on a cycle longer than the pool. phi is the "most irrational"
// number, which is why the largest two are a golden-ratio pair.
//
// AMPLITUDES 1.00 / 0.55 / 0.30 / 0.16, normalised so the four together peak at exactly _SwellAmp.
// The amplitude property has to MEAN the peak displacement, because the bounds pad the driver
// writes is computed from it and a surface that travelled further than its own stated amplitude
// would be culled at the crests — one eye first, under MultiPass. |sin| <= 1, |cos| <= 1 and the
// modulation below is <= 1, so |h| <= _SwellAmp is an exact bound and not a hope.
//
// PERIODS scale as sqrt(wavelength ratio), which is deep-water dispersion (c = sqrt(gL/2pi))
// reduced to its shape: the short components bob faster than the long one, so the four never lock
// into one rigid figure. The ABSOLUTE rate is not physical and deliberately so — real water this
// size bobs about once a second, and the ruling is "nur minimal Bewegungen".
#define GHVR_SWELL_ASUM 2.01

// THE SLOW, LARGE-SCALE AMPLITUDE MODULATION, and it is the term that answers "es soll sich nicht
// auf jeden tile exakt gleichen was passiert" most directly. Two very long crossing waves — 4.1 and
// 6.7 times the swell's own wavelength, i.e. 10 and 16 m at the shipped size, both larger than the
// 8.6 m pool. Where they coincide the water is at full amplitude; where they cancel it is nearly
// still. So one end of the pool is quiet while the other moves.
//
// IT IS A SPATIAL FACT THAT BARELY CHANGES, and the numbers are what make that true. It travels at
// the same `drift` as the components (2.4 mm/s at the shipped dial) plus its own 480 and 660 second
// cycles, which over the same 10 m wavelength is another 2 cm/s — so the quiet end of the pool
// stays the quiet end for minutes. THE FIRST DRAFT USED 37 AND 53 SECONDS, and 10 m over 37 s is
// 0.27 m/s: an envelope of activity sweeping the pool faster than the current this whole build
// exists to remove. A modulation is only "slow and large-scale" if its period is long RELATIVE TO
// its wavelength, and that ratio is a speed, so it is written as one.
//
// It is BOUNDED IN [1-_SwellCalm, 1], never above 1, because the peak amplitude is what the
// renderer's bounds were padded for.
float GhvrSwellCalm (float2 p, float t, float L, float drift, out float2 grad)
{
    const float2 dA = float2( 0.798636, 0.601815);   // 37 degrees
    const float2 dB = float2(-0.587785, 0.809017);   // 126 degrees
    float kA = GHVR_TAU / max(L * 4.1, 0.2);
    float kB = GHVR_TAU / max(L * 6.7, 0.2);
    float qA = kA * (dot(dA, p) - drift * t) + (GHVR_TAU / 480.0) * t;
    float qB = kB * (dot(dB, p) - drift * t) + (GHVR_TAU / 660.0) * t + 2.1;
    float m = 0.5 * (sin(qA) + sin(qB));                       // [-1, 1]
    float2 gm = 0.5 * (kA * dA * cos(qA) + kB * dB * cos(qB));
    float calm = saturate(_SwellCalm);
    grad = 0.5 * calm * gm;
    return 1.0 - calm * (0.5 - 0.5 * m);                       // [1-calm, 1]
}

float3 GhvrSwell (float2 p, float t)
{
    float L = max(_SwellWave, 0.15);
    float T = max(_SwellPeriod, 0.5);
    float v = _SwellSpeed;

    // The four components. Written out rather than looped over a constant array: every number sits
    // next to the sentence that justifies it, and nothing here has to survive an array-indexing
    // translation into GLSL on the one platform that would silently unroll it differently.
    float3 s;
    s  = GhvrStanding(p, t, float2( 0.956305, 0.292372), L * 1.000000,
                      T * 1.000000, 1.00 / GHVR_SWELL_ASUM, 0.000, 0.000, v);
    s += GhvrStanding(p, t, float2(-0.224951, 0.974370), L * 0.618034,
                      T * 0.786151, 0.55 / GHVR_SWELL_ASUM, 2.399, 1.777, v);
    s += GhvrStanding(p, t, float2( 0.484810, 0.874620), L * 0.414214,
                      T * 0.643595, 0.30 / GHVR_SWELL_ASUM, 4.113, 3.412, v);
    s += GhvrStanding(p, t, float2(-0.848048, 0.529919), L * 0.267949,
                      T * 0.517638, 0.16 / GHVR_SWELL_ASUM, 1.071, 5.108, v);

    float2 calmGrad;
    float calm = GhvrSwellCalm(p, t, L, v, calmGrad);

    // PRODUCT RULE, because the modulation is a function of position too. h = A(p,t) * S(p,t), so
    // grad h = A grad S + S grad A. Dropping the second term would light the pool as though the
    // quiet end were as steep as the busy one — small here (A varies over 10 m) but it is the
    // whole reason the gradient is derived rather than authored.
    float h = _SwellAmp * calm * s.x;
    float2 g = _SwellAmp * (calm * s.yz + s.x * calmGrad);
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
    // come back — a field whose own period happens to divide the tile lattice — and the four
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
// in both axes — which is what makes the frac() on the drift offset below safe here as well as on
// the texture path.
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

// ============================================== THE RIPPLE'S OWN MOTION ==
// STANDING WATER DOES NOT SCROLL EITHER. Through ModBuild 164 both normal layers were offset by
// `rate * t`, i.e. they translated forever, and at the shipped dial that resolved to 0.25-0.50
// world units per second — a visible current over a 1.7 m tile, and half of "es fließt noch viel
// zu schnell".
//
// The offset is now mostly a SWAY: `rate * (P/2pi) * sin(2pi t/P)` travels at a peak speed of
// `rate`, reverses, and has ZERO net displacement — the texture breathes back and forth over about
// 10 cm instead of running away. _RippleSway.z is the share that is still a genuine drift (shipped
// at a quarter), because a pattern that only ever retraces its own path reads as a video being
// rewound; a quarter of a very small rate is a creep of about 2 cm/s and nothing that could be
// called a flow.
//
// THE DRIFT PART IS STILL WRAPPED and that is a precision guard rather than a look: `frac()` of the
// product is the same expression modulo one whole texture repeat, and both the sampled texture
// (Repeat) and the analytic field above have period exactly 1, so dropping whole periods is
// invisible. An unwrapped product reaches four figures over a long session and starts eating the
// fractional bits that carry the ripple's detail — the surface would go gradually blocky over an
// evening and nothing would say why. The SWAY part is bounded by construction and needs no wrap.
float2 GhvrRippleOffset (float2 rate, float period, float2 tiling, float t)
{
    float share = saturate(_RippleSway.z);
    float P = max(period, 0.5);
    float2 drift = frac(rate * share * t * tiling);
    float2 sway = rate * (1.0 - share) * (P / GHVR_TAU) * sin(GHVR_TAU * t / P) * tiling;
    return drift + sway;
}

fixed4 GhvrWaterFrag (v2f i) : SV_Target
{
    // ONE CLOCK. _Time.y is the shared frame clock and _GhvrTimeOfs is the preview-only offset the
    // batch renderer steps (EnvRoom.shader), which is what lets an offscreen render show this
    // surface at two different instants without a headset.
    float t = _Time.y + _GhvrTimeOfs;

    // TWO RIPPLE LAYERS, at the two resolved tilings, each swaying at its own resolved rate and its
    // own period (13 and 17 seconds at the shipped values — deliberately not a ratio of small whole
    // numbers, so the two layers never reverse together, which would read as the whole pool
    // twitching). They are the second-order detail that rides on the swell, not the whole of the
    // surface.
    float2 tilA = _NormalTilings.xy, tilB = _NormalTilings.zw;
    float2 uvA = i.uv.zw * tilA + GhvrRippleOffset(_WaterUVAnimSpeedA.xy, _RippleSway.x, tilA, t);
    float2 uvB = i.uv.zw * tilB + GhvrRippleOffset(_WaterUVAnimSpeedB.xy, _RippleSway.y, tilB, t);

    // WEIGHTED, NOT SUMMED. Equal weights let the fine layer compete with the coarse one; the
    // weights the driver resolves are inversely proportional to each layer's own frequency and sum
    // to 1, so the coarse layer carries the shape and the fine one decorates it.
    float2 wgt = _LayerWeights.xy;
    float2 bump;
    if (_ProcNormal > 0.5)
        bump = wgt.x * GhvrProcBumpXY(uvA) + wgt.y * GhvrProcBumpXY(uvB);
    else
        bump = wgt.x * GhvrBumpXY(tex2D(_Normal_Map, uvA))
             + wgt.y * GhvrBumpXY(tex2D(_Normal_Map, uvB));

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
