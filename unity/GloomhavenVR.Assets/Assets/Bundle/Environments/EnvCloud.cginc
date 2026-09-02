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
float _CloudScatBase;   // AMBIENT in-scatter — the floor, everywhere in the sky
float _CloudScatWide;   // side-scatter lobe (wide, moon-facing hemisphere)
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

    // -------- in-scatter, IN THREE TERMS, AND THE FIRST OF THEM IS THE ONE
    // THAT WAS MISSING.
    //
    // USER, 2026-09-02 hardware test, verbatim: "Ich mag die Wolken, um den Mond
    // herum sehen sie gut aus, aber ausserhalb des Monds sieht es eher aus wie
    // als waeren die Wolken ein Negativbild ... Ich will dass die Wolken auch gut
    // aussehen wenn sie nicht direkt vom Mond angestrahlt werden."
    //
    // HE IS DESCRIBING AN ARITHMETIC FACT AND NOT A TASTE. This layer composites
    // premultiplied, so what the frame buffer ends up with is
    //     out = L + (1 - a) * bg    and therefore    out - bg = a * (C - bg)
    // where C = L/a is the cloud's INTRINSIC radiance — what an opaque patch of
    // it would show. The sign of (C - bg) is the whole of his complaint:
    //
    //   * C > bg  -> a thicker wisp is BRIGHTER. That is a cloud.
    //   * C < bg  -> a thicker wisp is DARKER. The layer's own structure is drawn
    //                INVERTED against whatever it lies on. That is a negative.
    //
    // With the old single ambient term C was tint x 0.028, i.e. a LUMINANCE of
    // 0.0218, flat over the whole sky outside the corona. The sky it lies on runs
    // from 0.0035 (horizon gradient) through 0.0121 (zenith gradient) to about
    // 0.064 over the Milky Way band and past 0.12 in the galactic bulge. So the
    // shipped cloud was brighter than the empty gaps and three times darker than
    // the band — which is exactly a negative image, and exactly where the eye
    // looks. Near the moon the forward lobe put C at 0.45 and swamped the whole
    // question, which is why the corona was the one part he liked.
    //
    // THE FIX IS A FLOOR, and it is physical rather than a fudge. A cloud under a
    // gibbous moon is not lit only by the single-scattered beam: it is lit by
    // MULTIPLE scattering inside its own droplets, by the moonlit air column
    // under it, and by the skyglow. All three are near-isotropic and none of them
    // dies when you turn away from the moon, which is why real thin cloud is
    // visibly grey right across a moonlit sky. Three terms now, cheapest first:
    //
    //   BASE   isotropic. The floor. Sized so that C at its MINIMUM (the point of
    //          the sky opposite the moon) is at or over the sky's own band
    //          luminance, which is what makes "never a negative" a property of
    //          the numbers rather than of where the wisp happens to be.
    //   WIDE   a broad side-scatter lobe over the moon-facing hemisphere. Real
    //          droplet and ice phase functions are forward-peaked but they are not
    //          a spike — there is real scattering at 90 deg. Without this the
    //          floor is a perfectly flat grey and the layer reads as paint; with
    //          it the veil is about 1.5x brighter 45 deg from the moon than it is
    //          opposite, which is the gradient a real deck has. ((m+1)/2)^2 is two
    //          instructions and is the cheap stand-in.
    //   FWD    the corona. UNCHANGED, at the same peak and the same exponent: it
    //          is the part he said was right.
    float fwd = pow(saturate(m), _CloudScatPow);
    float side = 0.5 * m + 0.5;                      // 1 at the moon, 0 opposite
    float3 L = _CloudTint.rgb * (_CloudScatBase + _CloudScatWide * (side * side)
                                                + _CloudScatFwd * fwd) * a;

    // .a IS STILL THE PLAIN OPTICAL DEPTH. It is deliberately NOT scaled here:
    // every guarantee this effect carries ("niemals dicht", the moon's transmitted
    // floor) is a statement about this number, the bake asserts them from
    // CloudAlpha x CloudMoonMin, and PreviewClouds recovers it from a black/white
    // clear pair. A layer whose alpha meant something different from its ceiling
    // would make all three of those quietly wrong.
    return float4(L, a);
}
#endif // GHVR_ENV_CLOUD_INCLUDED
