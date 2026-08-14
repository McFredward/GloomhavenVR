// GloomhavenVR — the night sky's CONTINUOUS layer (everything that is not a
// catalogue star): sky gradient, Milky Way, sub-visual star dust, the moon.
//
// User finding, ModBuild 133: "Die Skybox sind immer noch die fixen Sterne UND
// ein Mond der dahinter ist, entferne das statische Bild und gehe voll zu einem
// dynamischen Sternenhimmel (ausschließlich)."
// => The 8192x2560 photographic panorama (Poly Haven 'Rogland Clear Night')
//    and its baked-in moon are GONE, together with the ~20 MB they cost in the
//    bundle. Nothing in the sky is a picture any more. What replaced it:
//
//   1. GRADIENT — zenith slightly blue, horizon nearly black. Deliberately the
//      opposite of a real light-polluted sky: the previous round's complaint was
//      a lifted band above the ridge, and a forest silhouette only reads as
//      frightening when the sky BEHIND it dies toward the ground.
//   2. MILKY WAY — computed in real GALACTIC coordinates. The fragment's sky
//      direction is un-rotated back to the catalogue epoch (the same Rodrigues
//      rotation EnvStarPoints applies, negated), then projected onto the
//      galactic basis (_GalX = galactic centre, _GalY, _GalZ = north galactic
//      pole) that BuildEnvironments derives from the IAU pole/centre and hands
//      over in the star mesh's own object frame. So the band lies where it
//      belongs among the real constellations, and it turns WITH them: there is
//      exactly ONE celestial frame in this sky.
//   3. STAR DUST — a procedural sub-visual field in that same celestial frame,
//      denser inside the band (which is what the Milky Way physically is:
//      unresolved stars). Without it the sky between the 8404 catalogue stars
//      reads empty now that the photo is gone.
//   4. MOON — gnomonic sprite around _MoonDir. It does NOT ride the celestial
//      rotation: its direction is the same authored constant that aims the
//      forest's moonlight shafts, trunk rim light and canopy tear, and those are
//      baked geometry/vertex data that cannot follow it. A moon that drifted
//      away from its own shafts would be a far worse lie than a moon that hangs
//      still. It is drawn LAST here, so it covers band and dust; the catalogue
//      stars behind it are killed in EnvStarPoints (see _MoonCos there).
//      ModBuild 144, MOON HELD: under Dark it is a BLOOD MOON, held — the
//      Earth's umbra sits over it at one fixed offset and the disc goes copper,
//      and the only thing that moves is how far Dark is up. (It used to cross
//      in 30 s; the user liked the blood moon and rejected the crossing.) Under
//      Light it swells. Both are painted from EnvElement.cginc's MOON PHASE
//      helpers, the same ones GhvrMoonLight() hands the rooms, so the moon you
//      see and the moonlight you stand in can never disagree. Nothing in this
//      sky changes on its own any more except the celestial rotation.
//
// Directions are taken in OBJECT space (normalize of the dome vertex), NOT from
// the camera-relative world vector: the catalogue stars are real geometry in
// this same object frame, so object space is the only space in which the two
// layers cannot slide against each other. It also makes the whole sky immune to
// the prefab's runtime placement/scale.
//
// Everything is _Time-driven (no scripts — bundle rule), never screen-space (a
// screen-keyed pattern differs per eye under MultiPass and causes binocular
// rivalry), and stereo-correct: the dome is a real inverted sphere mesh.
// Background queue backdrop, no depth write.
Shader "GloomhavenVR/EnvStars"
{
    Properties
    {
        _TopCol ("Sky zenith color", Color) = (0.0060,0.0082,0.0150,1)
        _HorizonCol ("Sky horizon color", Color) = (0.0026,0.0035,0.0062,1)

        // celestial frame — MUST match EnvStarPoints' _Pole/_RotSpeed
        _Pole ("Celestial pole (object space)", Vector) = (0,0.743,0.669,0)
        _RotSpeed ("Sky rotation (rad/sec)", Range(0,0.05)) = 0.0021817
        _GalX ("Galactic centre axis (object space, epoch)", Vector) = (0,0,1,0)
        _GalY ("Galactic y axis (object space, epoch)", Vector) = (1,0,0,0)
        _GalZ ("North galactic pole (object space, epoch)", Vector) = (0,1,0,0)

        // Milky Way
        _MwGain ("Milky Way brightness", Range(0,0.2)) = 0.016
        _MwThin ("Milky Way thin-disc sigma (deg)", Range(1,20)) = 5.0
        _MwThick ("Milky Way thick-disc sigma (deg)", Range(5,60)) = 17.0
        _MwBulge ("Galactic bulge boost", Range(0,3)) = 1.35
        _MwDust ("Dust-lane depth", Range(0,1)) = 0.80
        _MwWarm ("Bulge color (reddened)", Color) = (1.00,0.86,0.70,1)
        _MwCool ("Outer-arm color", Color) = (0.78,0.85,1.00,1)

        // sub-visual procedural star dust
        // NOTE on _DustCore: the point's gaussian sigma is 1/sqrt(2*core) CELLS,
        // i.e. (1/_DustScale)/sqrt(2*core) radians. Below ~1.5 arcmin the dots
        // fall under one headset pixel and crawl; 20 puts them at ~2 arcmin.
        _DustGain ("Star-dust brightness", Range(0,1)) = 0.06
        _DustDens ("Star-dust density", Range(0,1)) = 0.16
        _DustScale ("Star-dust cells per radian", Range(50,900)) = 330
        _DustCore ("Star-dust point hardness", Range(4,400)) = 20

        // atmosphere
        _Extinct ("Extinction (mag per airmass)", Range(0,1)) = 0.26
        _HazeTex ("Haze veil (tiling noise)", 2D) = "black" {}
        _HazeCol ("Haze color", Color) = (0.012,0.015,0.022,1)
        _HazeAmt ("Haze amount", Range(0,2)) = 0.45

        // moon
        _MoonTex ("Moon sprite (RGBA)", 2D) = "black" {}
        _MoonDir ("Moon direction (object space)", Vector) = (0.6,0.37,0.71,0)
        _MoonCol ("Moon color", Color) = (1,0.98,0.92,1)
        _MoonExtent ("Moon half-extent (tan units)", Range(0.01,0.4)) = 0.115
        // How much of the sprite is MOON and not halo — MakeMoon's own R, handed
        // over rather than retyped. The eclipse measures its umbra against the
        // DISC (EnvElement.cginc works in moon radii), so it is the one number
        // that turns sprite uv into that unit, and the shader must not guess it.
        // The default is DELIBERATELY NOT the authored 0.22: a material only
        // serialises a property it was set to a DIFFERENT value than the
        // shader's default, so a matching default would have left the builder's
        // SetFloat a silent no-op and the coupling an illusion — the number
        // would have been typed twice and agreed by luck. 0.25 is close enough
        // to be a sane fallback and far enough to make the write real.
        _MoonDiscR ("Moon disc radius (sprite units)", Range(0.05,0.5)) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
    // Shared with every other Env* shader that reads _Time: in multiplayer the elected
    // owner's epoch arrives on Net record 31 so both skies stand at the same hour; 0 offline.
    float _GhvrTimeOfs;

            sampler2D _MoonTex, _HazeTex;
            fixed4 _TopCol, _HorizonCol, _MoonCol, _HazeCol, _MwWarm, _MwCool;
            float _RotSpeed, _MwGain, _MwThin, _MwThick, _MwBulge, _MwDust;
            float _DustGain, _DustDens, _DustScale, _DustCore;
            float _Extinct, _HazeAmt, _MoonExtent, _MoonDiscR;
            float4 _Pole, _GalX, _GalY, _GalZ, _MoonDir;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 opos : TEXCOORD0; };

            // _Time.y is seconds since load in a float: after hours of play its
            // precision collapses and Adreno GPUs visibly stutter on the big
            // values. Every animated term uses this wrapped clock instead; the
            // wrap equals the sky's own revolution period, so the rotation
            // crosses it without a jump.
            #define SKY_PERIOD 2880.0
            // MP-SHARED CLOCK: _GhvrTimeOfs carries the elected owner's epoch (Net record 31,
            // ExtIdEnvClock) so every player's sky stands at the same hour; it is 0 offline and
            // in single player, which is the shipped behaviour bit-for-bit.
            float SkyTime() { return fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD); }

            // Interleaved gradient noise (Jimenez) — 1 LSB of dither on the
            // near-black sky gradient. Without it a hemisphere spanning ~6 of
            // 256 blue levels contours into visible bands on the headset.
            float Ign(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

            // integer lattice hash (same mix as the C# builder's Hash3): a
            // sin()-based hash loses precision at the cell counts this field
            // uses and shows as smeared rows of dust.
            uint HashCell(int3 c, int seed)
            {
                uint n = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663)
                       ^ (uint)(c.z * 83492791) ^ (uint)(seed * 974711);
                n *= 1274126177u; n ^= n >> 16; n *= 2246822519u; n ^= n >> 13;
                return n;
            }
            float H01(uint n) { return (n & 0xFFFFFFu) / 16777216.0; }

            // One layer of sub-visual stars: at most one point per lattice cell,
            // kept clear of the cell walls so a single lookup cannot clip it.
            // `dens` is modulated by the Milky Way, so the band granulates into
            // unresolved stars instead of being a smooth wash.
            float DustLayer(float3 dc, float scale, int seed, float dens, float core)
            {
                float3 q = dc * scale;
                float3 cf = floor(q);
                int3 ci = int3(cf);
                uint h = HashCell(ci, seed);
                float on = H01(h);
                if (on > dens) return 0.0;
                float3 sp = float3(H01(HashCell(ci, seed + 7)),
                                   H01(HashCell(ci, seed + 13)),
                                   H01(HashCell(ci, seed + 23)));
                float3 dv = (q - cf) - (0.28 + 0.44 * sp);
                float b = on / max(dens, 1e-4);             // 0..1, uniform
                // magnitude-like distribution: many faint, a few that stand out
                float mag = pow(1.0 - b, 3.0);
                return exp(-dot(dv, dv) * core) * (0.06 + 0.94 * mag);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.opos = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = SkyTime();
                float3 u = normalize(i.opos);                  // sky direction, object space
                float h = u.y;                                 // 0 horizon, 1 zenith

                // ---------------- gradient (local, does NOT rotate) ----------
                float3 sky = lerp(_HorizonCol.rgb, _TopCol.rgb, pow(saturate(h), 0.75));
                sky *= smoothstep(-0.30, 0.02, h);             // die below the rim

                // ---------------- atmospheric extinction ---------------------
                // Rozenberg (1966) airmass — finite at the horizon (X -> 40),
                // unlike sec(z) — then Bouguer transmission, normalized to the
                // zenith. This is what makes the sky go black toward the ground
                // instead of showing the lifted band the last round flagged.
                float a = max(h, 0.0);
                float X = 1.0 / (a + 0.025 * exp(-11.0 * a));
                float k = _Extinct * 0.921034;                 // mag -> e-folds
                float ext = exp(-k * X) / exp(-k) * step(-0.002, h);

                // ---------------- into the celestial frame -------------------
                // negate the star layer's rotation: dc is the direction as it was
                // at the catalogue epoch, which is the frame _Gal* live in.
                float3 P = normalize(_Pole.xyz);
                float th = -_RotSpeed * t;
                float c = cos(th), s = sin(th);
                float3 dc = u * c + cross(P, u) * s + P * dot(P, u) * (1.0 - c);

                float gz = clamp(dot(dc, _GalZ.xyz), -1.0, 1.0);
                float gl = atan2(dot(dc, _GalY.xyz), dot(dc, _GalX.xyz));  // -pi..pi, 0 = centre
                float bdeg = degrees(asin(gz));

                // ---------------- Milky Way ---------------------------------
                // two co-planar populations (thin bright disc + thick faint halo)
                float b2 = bdeg * bdeg;
                float thin = exp(-b2 / (2.0 * _MwThin * _MwThin));
                float thick = exp(-b2 / (2.0 * _MwThick * _MwThick));
                // longitude profile: bright toward the centre, faint toward the
                // anticentre — 0.38 there, which is roughly the real ratio.
                float lon = 0.38 + 0.62 * saturate(cos(gl) * 0.5 + 0.5);
                float bulge = _MwBulge * exp(-(gl * gl) / (2.0 * 0.40 * 0.40))
                                       * exp(-b2 / (2.0 * 11.0 * 11.0));

                // mottling: the veil texture tiles, and the l axis is sampled at
                // INTEGER multiples of a full turn so it wraps without a seam.
                float2 nuv = float2(gl * 0.1591549431, bdeg * 0.0125);
                float n1 = tex2D(_HazeTex, nuv * float2(3.0, 2.2) + 0.13).r;
                float n2 = tex2D(_HazeTex, nuv * float2(9.0, 6.0) + 0.61).r;
                float n3 = tex2D(_HazeTex, nuv * float2(23.0, 13.0) + 0.29).r;
                float mottle = 0.55 + 0.95 * (0.55 * n1 + 0.30 * n2 + 0.15 * n3);

                // dust lanes: the Great Rift runs from the centre out toward
                // Cygnus, so the lanes are masked to that half and hug b=0.
                float riftMask = saturate(cos(gl - 0.30) * 1.25 + 0.15);
                float lane = smoothstep(0.52, 0.86, 1.0 - n2 * 0.55 - n1 * 0.45)
                           * exp(-b2 / (2.0 * 5.5 * 5.5)) * riftMask;

                float band = (thin * 0.72 + thick * 0.28) * lon + bulge * 0.55;
                band *= mottle;
                band *= 1.0 - _MwDust * lane;
                band = max(band, 0.0);

                float3 mwCol = lerp(_MwCool.rgb, _MwWarm.rgb,
                                    saturate(bulge * 0.8 + exp(-b2 / 50.0) * 0.25));
                float3 col = sky + mwCol * (band * _MwGain * ext);

                // ---------------- sub-visual star dust ----------------------
                // density follows the band: the Milky Way IS unresolved stars.
                float dens = _DustDens * (0.30 + 2.10 * saturate(band));
                float dust = DustLayer(dc, _DustScale, 7717, dens, _DustCore)
                           + DustLayer(dc, _DustScale * 0.61, 3391, dens * 0.60, _DustCore * 0.80);
                // faint scintillation, phase keyed on the celestial direction
                float ph = dc.x * 47.0 + dc.y * 61.0 + dc.z * 53.0;
                dust *= 0.80 + 0.20 * sin(t * 1.7 + ph);
                col += lerp(_MwCool.rgb, float3(1, 1, 1), 0.55) * (dust * _DustGain * ext);

                // ---------------- haze veil (local, absorptive) --------------
                // Two wrapping noise octaves crossing at different rates, low in
                // the sky where real extinction lives. It mostly SUBTRACTS: an
                // additive veil is exactly the lifted horizon band that was
                // rejected, so it only scatters back a few percent of itself.
                float2 huv = float2(atan2(u.x, u.z) * 0.1591549431, saturate(h) * 2.2);
                float v1 = tex2D(_HazeTex, huv * float2(2.0, 1.0) + float2( 0.00062, 0.00004) * t).r;
                float v2 = tex2D(_HazeTex, huv * float2(1.0, 0.7) + float2(-0.00031, 0.00011) * t).r;
                float veil = saturate(v1 * v2 * 2.6 - 0.35) * _HazeAmt * pow(1.0 - saturate(h), 2.6);
                col = col * (1.0 - 0.85 * veil) + _HazeCol.rgb * (veil * 0.30);

                // ---------------- moon --------------------------------------
                // Fixed in the room frame (see header): it is the anchor the
                // baked moonlight was authored against. Drawn last, so it hides
                // band and dust; the catalogue stars are culled against the same
                // disc in EnvStarPoints.
                float3 md = normalize(_MoonDir.xyz);
                float mt = dot(u, md);
                if (mt > 0.5)
                {
                    float3 right = normalize(cross(float3(0, 1, 0), md));
                    float3 up = cross(md, right);
                    float3 p = u / mt;
                    float2 muv = float2(dot(p, right), dot(p, up)) / (2.0 * _MoonExtent) + 0.5;
                    if (all(muv > 0.0) && all(muv < 1.0))
                    {
                        fixed4 mc = tex2D(_MoonTex, muv);
                        col = lerp(col, mc.rgb * _MoonCol.rgb, mc.a);
                    }
                }

                // ================================ ELEMENT ART ================
                // THE SKY IS THE PERIPHERY, and it is the same sky in both rooms
                // (the cellar sees it through the barred window), so this is the
                // one place an element can be read from anywhere without ever
                // touching the board.
                //
                // The split, in the air: LIGHT lifts the whole continuous layer —
                // the gradient, the Milky Way, the dust — so the night pales
                // toward moonrise; DARK crushes it, and crushes it HARDER than
                // Light lifts, so under both the field goes out and only the
                // MOON is left standing (it is a source, so it takes the source
                // gain and Dark never subtracts from it). The catalogue stars do
                // the other half in EnvStarPoints: Dark eats the faint ones and
                // leaves the bright ones brighter. Together: fewer, harder,
                // brighter things in a blacker sky, which is what the brief means
                // by "maximum contrast, not a grey average".
                //
                // ICE takes the moon cold — the forest's authored Ice channel is
                // "a cold cast on the moon term", and the moon in the sky and the
                // moon rim on the trunks (EnvRoom) must agree about its colour.
                //
                // MOON PHASE (EnvElement.cginc). The moon is redrawn here rather
                // than tinted in place, because under Light it is a DIFFERENT
                // SIZE: the sprite's extent is scaled, so disc and halo grow
                // together and the moon stays one object instead of a disc
                // rattling around inside a fixed glow. Under Dark the Earth's
                // umbra slides across it. Both are painted from the shared
                // helpers, so the light this shader shows and the light
                // GhvrMoonLight() hands the rooms cannot disagree.
                GhvrElem e = GhvrElems();
                if (e.live > 0.0)
                {
                    col *= max(1.0 + 1.60 * e.light * (1.0 - e.dark) - 0.85 * e.dark, 0.0);
                    if (mt > 0.5)
                    {
                        // re-apply the moon OVER the darkened field: it is drawn
                        // last for exactly this reason, and under Dark it has to
                        // survive at full strength or the split has no anchor.
                        float mext = _MoonExtent * GhvrMoonSize(e);
                        float3 right2 = normalize(cross(float3(0, 1, 0), md));
                        float3 up2 = cross(md, right2);
                        float3 p2 = u / mt;
                        float2 muv2 = float2(dot(p2, right2), dot(p2, up2)) / (2.0 * mext) + 0.5;
                        if (all(muv2 > 0.0) && all(muv2 < 1.0))
                        {
                            fixed4 mc2 = tex2D(_MoonTex, muv2);

                            // Into the unit the shared helpers speak: q is the
                            // position on the disc, 1 = the limb. Because muv2
                            // was built from the SCALED extent, the umbra scales
                            // with the moon — a swollen moon gets a proportionally
                            // swollen shadow, so the eclipse looks like the same
                            // event whatever Light is doing, instead of the swell
                            // silently making the bite look smaller.
                            float2 q = (muv2 - 0.5) / _MoonDiscR;
                            float inDisc = 1.0 - smoothstep(0.94, 1.06, length(q));

                            // THE SWELL IS READ ON THE HALO, NOT ON THE FACE.
                            // The source gain is 2.1 at full Light and the disc
                            // is already painted just under white, so applying it
                            // flat blows the maria out and the moon stops being a
                            // moon — it becomes a lamp, which is exactly the
                            // "headlight" the brief warns about, in the sky
                            // instead of on the ground. The disc takes a third of
                            // the gain and the halo takes all of it: bigger moon,
                            // far bigger glow, face still legible. (A real bright
                            // moon in damp air is read by its glow too.)
                            float src = GhvrSrcGain(e);
                            float3 mcol = _MoonCol.rgb * lerp(src, 1.0 + 0.35 * (src - 1.0), inDisc);
                            mcol = lerp(mcol, mcol * float3(0.74, 0.88, 1.22), saturate(e.ice));

                            // ---- THE ECLIPSE, HELD --------------------------
                            // MOON HELD (ModBuild 144). The umbra does not move
                            // and there is no phase to be in: the centre is a
                            // constant in EnvElement.cginc, chosen so the whole
                            // disc lies inside the shadow with the terminator
                            // clear of the limb. What is left to animate is the
                            // element itself — `e.dark` — so the blood moon
                            // FADES in and out with Dark instead of sliding
                            // across the sky. That is the user's own ruling:
                            // "lass ihn statisch ... lass einen Blutmond
                            // statisch solange das aktiv ist".
                            //
                            // `t` no longer enters here at all, and neither does
                            // the divisibility argument that used to justify
                            // SkyTime()'s wrap against the transit period. The
                            // sky and the rooms cannot disagree because there is
                            // nothing left for them to disagree ABOUT.
                            float2 ec = GhvrEclipseCentre();
                            float dd = length(q - ec);
                            float umb = (1.0 - smoothstep(GHVR_ECL_UMBRA - GHVR_ECL_EDGE,
                                                          GHVR_ECL_UMBRA + GHVR_ECL_EDGE, dd)) * e.dark;
                            // Danjon: an eclipsed moon is NOT a flat red disc.
                            // The umbral edge is bright copper and the core is a
                            // much darker grey-brown, and the gradient between
                            // them is the whole reason it reads as a shadow with
                            // depth instead of a coloured filter laid over.
                            // The numbers are DARK on purpose. The first bake used
                            // roughly twice these and the covered half read as
                            // orange PAINT laid over the moon rather than as a
                            // moon in shadow: an umbral surface has to lose most
                            // of its luminance, and only then does the colour
                            // read as the little light that bent around an
                            // atmosphere to get there.
                            //
                            // HELD, THIS GRADIENT IS THE WHOLE PICTURE. With the
                            // umbra centred it would be a ring — dark middle,
                            // bright rim, symmetric — which is a vignette and
                            // reads as a filter. Off-centre by 1.15 R it is a
                            // ramp running clean across the face: the lower-right
                            // limb sits 0.15 R from the shadow's core and goes
                            // grey-brown, the upper-left limb sits 2.15 R out and
                            // goes bright copper. Nothing moves and it still has
                            // a near side and a far side.
                            float core = 1.0 - smoothstep(0.0, GHVR_ECL_UMBRA, dd);
                            float3 umbCol = lerp(float3(0.46, 0.170, 0.085),
                                                 float3(0.155, 0.052, 0.040), core);
                            float3 tint = lerp(float3(1, 1, 1), umbCol, umb);
                            // DELETED WITH THE TRANSIT: the penumbral wash. It
                            // existed to make the shadow read as ARRIVING rather
                            // than switching on at first contact, and with the
                            // umbra held over the whole disc it is a constant 1
                            // everywhere — i.e. an 18% flat dim during the ramp,
                            // doing nothing the tint lerp above is not already
                            // doing better. Arrival is now the element's own
                            // envelope and belongs to ElementMood, not here.
                            //
                            // ...and the HALO is scattered moonlight, so it dies
                            // with the disc AS A WHOLE — one global coverage term
                            // outside the limb, not a shadow painted on the glow
                            // (a halo with a bite out of it is a sprite with a
                            // hole in it, which is the tell of a fake). The
                            // coverage is a constant 1 now, so this is simply
                            // "the halo goes to 15% at full Dark", on the
                            // element's ramp.
                            float halo = 1.0 - 0.85 * GhvrEclipseCover(ec) * e.dark;
                            tint = lerp(float3(halo, halo, halo), tint, inDisc);

                            col = lerp(col, mc2.rgb * mcol * tint, mc2.a);
                        }
                    }
                }

                col += (1.0 / 255.0) * Ign(i.pos.xy) - (0.5 / 255.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
