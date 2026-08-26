// GloomhavenVR — THIN NIGHT CLOUD, the whole effect, in one function.
//
// USER REQUEST (2026-08, verbatim): "Im Wald will ich noch etwas anderes:
// Leichte Wolken, diese sollen realistisch wirken, niemals dicht sein und den
// Mond nie voll verdecken. Sie sollen sich leicht bewegen. Auch wichtig: Der
// Fokus ist auf dem Spiel selber, die Umgebungen sind nur Beiwerk, d.h. auch
// die Wolken sollen zwar so gut es geht aussehen aber performant sein und so
// gut es geht die Performance nicht reduzieren, suche nach Lösungen die das
// erfüllen."
//
// Four falsifiable properties, and every one of them is a PROPERTY OF THE
// ARITHMETIC below rather than of the numbers a material happens to carry.
// They are re-derived in .planning/FOREST-CLOUDS.md; the short forms:
//
//  1. NEVER DENSE.  Opacity is  a = _CloudAlpha * ev * taper * cov  with
//     ev, taper, cov all in [0,1] by construction (two smoothsteps and a
//     saturate). So  a <= _CloudAlpha  EVERYWHERE AND ALWAYS. At the shipped
//     _CloudAlpha the thickest wisp in the sky still transmits (1-_CloudAlpha)
//     of whatever is behind it. There is no parameter combination and no
//     instant at which this layer can become a deck.
//
//  2. THE MOON IS NEVER FULLY COVERED.  `taper` is 1 far from the moon and
//     falls to _CloudMoonMin close to it, keyed on m = dot(u, moonDir) — a
//     TIME-INDEPENDENT quantity, because both u (the fragment's own object
//     direction) and the moon's bearing are constants of the geometry. The
//     only time-varying input to the taper is a bounded noise nudge on m of at
//     most +-0.5*_CloudEdge, and _CloudEdge is chosen (see the shipped values
//     and the assertion in BuildEnvironments) so that even the worst nudge
//     leaves the whole moon DISC inside the full-taper plateau. Therefore
//         a <= _CloudAlpha * _CloudMoonMin      over the moon's disc, for all t,
//     and the moon's transmitted fraction has a hard floor of
//         1 - _CloudAlpha * _CloudMoonMin.
//     This is a bound on the supremum, not the outcome of a lucky drift.
//
//  3. SLOW.  The wind enters as an ADDITIVE uv offset. Nothing multiplies a
//     frequency (the frequency-scrub bug class: an amplitude dial that scales a
//     frequency riding the shared clock is correct only at t=0). _CloudAlpha,
//     _CloudCut and _CloudSharp are pure amplitude; _CloudWind is pure rate.
//
//  4. EXACTLY PERIODIC.  Both uv offsets are integer multiples of the texture's
//     own period over one loop, so the ENTIRE FIELD repeats with period
//     GHVR_CLOUD_PERIOD. A time series over [0, GHVR_CLOUD_PERIOD) is therefore
//     not a sample of the behaviour, it is ALL of the behaviour.
//
// STEREO. Nothing here reads the camera. The only spatial input is `u`, the
// normalised OBJECT-SPACE position of the fragment on the cloud shell — the
// same convention EnvStars uses, and the only one in which this layer and the
// catalogue stars cannot slide against each other. No view vector, no
// reflect(), no Fresnel, no half-vector, no _WorldSpaceCameraPos, no screen
// coordinate, no dither keyed on SV_Position. A given point of the shell has
// the same colour in both eyes because its colour is a function of that point.
// Verified at the constant-buffer level, not by grepping identifiers — see
// FOREST-CLOUDS.md.
//
// BAND-LIMITING. The two noise fetches are ordinary tex2D on a mipmapped,
// wrap-mode-Repeat texture, so the hardware picks the mip from the screen-space
// derivative of uv and the field is low-pass filtered automatically exactly
// where the ground-plane projection compresses it (low elevations). On top of
// that the elevation envelope has already taken the amplitude to ~0 by the time
// the projection is under-sampled. Numbers in FOREST-CLOUDS.md.
#ifndef GHVR_ENV_CLOUD_INCLUDED
#define GHVR_ENV_CLOUD_INCLUDED

// The loop, in seconds. MUST divide EnvStars' SKY_PERIOD (2880) or the sky
// clock's wrap would cut the drift mid-stride. 1440 = 2880/2.
#define GHVR_CLOUD_PERIOD 1440.0

sampler2D _CloudTex;
// x,y = layer A uv offset per second; z,w = layer B's. Both are 1/period times
// an INTEGER, so t = GHVR_CLOUD_PERIOD returns the field bit-exactly to t = 0.
float4 _CloudWind;
float4 _CloudTint;
float4 _CloudMoonDir;
float _CloudScale;      // ground-plane units -> layer A uv
float _CloudRatio;      // layer B's frequency as a multiple of layer A's
float _CloudMix;        // weight of layer A in the coverage field
float _CloudCut;        // coverage threshold  (amplitude only)
float _CloudSharp;      // coverage hardness   (amplitude only)
float _CloudAlpha;      // THE CEILING on opacity, anywhere, ever
float _CloudElevLo;     // sin(elevation) where the layer starts
float _CloudElevHi;     // sin(elevation) where it reaches full strength
float _CloudMoonMin;    // taper floor on the moon's disc
float _CloudMoonIn;     // cos(inner clear angle)  - full taper inside this
float _CloudMoonOut;    // cos(outer clear angle)  - no taper outside this
float _CloudEdge;       // bounded noise nudge on m; |nudge| <= 0.5*_CloudEdge
float _CloudScatBase;   // ambient in-scatter (away from the moon)
float _CloudScatFwd;    // forward-scatter peak (the corona around the moon)
float _CloudScatPow;    // forward lobe hardness

/// The layer, PREMULTIPLIED: .rgb is the in-scattered moonlight this cloud adds
/// and .a is the fraction of the background it swallows. Blend One
/// OneMinusSrcAlpha composites both in one operation, which is exactly the
/// single-scattering form  L_out = L_scatter + T * L_background  — the reason
/// this shader is premultiplied rather than the usual SrcAlpha blend.
///
/// u  normalised object-space direction of the fragment (NOT a view vector)
/// t  the sky's shared wrapped clock (EnvStars' SkyTime())
float4 GhvrCloudLayer (float3 u, float t)
{
    // -------- elevation envelope: 0 low, 1 high. Also the band-limit guard:
    // the ground-plane projection's angular density goes as 1/sin^2, so the
    // amplitude has to be gone before the texture is under-sampled.
    float ev = smoothstep(_CloudElevLo, _CloudElevHi, u.y);

    // -------- the ground-plane projection. A flat slab of cloud at unit height
    // seen from underneath: this is the ONLY mapping in which the perspective
    // compression toward the horizon is the real one rather than a stylisation,
    // and it is what makes the layer read as a cloud deck instead of a texture
    // painted on a dome. Clamped at _CloudElevLo, below which ev is already 0.
    float2 pl = u.xz / max(u.y, _CloudElevLo);

    // Wind is an ADDITIVE offset — see property 3 in the header.
    float2 uvA = pl * _CloudScale + _CloudWind.xy * t;
    float2 uvB = pl * (_CloudScale * _CloudRatio) + _CloudWind.zw * t;
    float nA = tex2D(_CloudTex, uvA).r;      // coarse field
    float nB = tex2D(_CloudTex, uvB).g;      // fine field, different air speed

    // -------- coverage. AMPLITUDE shaping only: the threshold decides how much
    // of the sky is cloud at all and the hardness decides how fast an edge
    // goes from clear to full. Neither touches uv, so neither can scrub a
    // frequency that rides the clock.
    float d = nA * _CloudMix + nB * (1.0 - _CloudMix);
    float cov = saturate((d - _CloudCut) * _CloudSharp);
    cov = cov * cov * (3.0 - 2.0 * cov);     // soften both ends; still in [0,1]

    // -------- the moon. m is a pure function of geometry.
    float3 md = normalize(_CloudMoonDir.xyz);
    float m = dot(u, md);
    // The clear patch's rim is nudged by the fine field so it is not a stamped
    // circle. THE NUDGE IS BOUNDED: nB is in [0,1], so the nudge lies in
    // [-0.5*_CloudEdge, +0.5*_CloudEdge] at every fragment and every instant,
    // whatever the noise does. That bound is the whole of property 2.
    float mm = m + _CloudEdge * (nB - 0.5);
    float k = smoothstep(_CloudMoonOut, _CloudMoonIn, mm);
    float taper = 1.0 - (1.0 - _CloudMoonMin) * k;

    float a = _CloudAlpha * ev * taper * cov;

    // -------- in-scatter. A thin cloud at night is not a grey smudge: it is
    // dark almost everywhere and BRIGHT around the moon, because single
    // scattering in water/ice is strongly forward-peaked. pow(m, n) is a cheap
    // stand-in for that lobe; at the shipped exponent its half-width is about
    // sqrt(2/n) radians, which puts the corona right where the taper is
    // thinning the cloud. The two together are the actual look being aimed at:
    // the moon BURNING THROUGH a veil, not a hole cut around it.
    float fwd = pow(saturate(m), _CloudScatPow);
    float3 L = _CloudTint.rgb * (_CloudScatBase + _CloudScatFwd * fwd) * a;

    return float4(L, a);
}
#endif // GHVR_ENV_CLOUD_INCLUDED
