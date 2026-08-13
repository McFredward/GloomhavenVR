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
    // Shared with every other Env* shader that reads _Time: in multiplayer the elected
    // owner's epoch arrives on Net record 31 so both skies stand at the same hour; 0 offline.
    float _GhvrTimeOfs;

            sampler2D _MoonTex, _HazeTex;
            fixed4 _TopCol, _HorizonCol, _MoonCol, _HazeCol, _MwWarm, _MwCool;
            float _RotSpeed, _MwGain, _MwThin, _MwThick, _MwBulge, _MwDust;
            float _DustGain, _DustDens, _DustScale, _DustCore;
            float _Extinct, _HazeAmt, _MoonExtent;
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

                col += (1.0 / 255.0) * Ign(i.pos.xy) - (0.5 / 255.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
