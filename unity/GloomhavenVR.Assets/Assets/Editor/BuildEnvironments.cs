// GloomhavenVR companion project — ambient environment FX assembler.
//
// HISTORY: this builder used to assemble full low-poly rooms (Quaternius FBX
// dungeon/nature packs, EnvLit palette materials, layout tables). That geometry
// was rejected — the game generates the scenario geometry itself (Apparance) —
// so the prefabs are now FX-ONLY shells layered on top of the game's own tiles.
//
// Menu:  GloomhavenVR > Build Environments
// Batch: Unity -batchmode -nographics -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll -logFile env-build.log
//        (do NOT pass -quit; BuildAll exits itself. Does NOT build the game bundle.)
//
// Produces, deterministically (re-run => identical output):
//   Assets/Bundle/Environments/Env_Swamp.prefab  — night-sky FX shell: PAINTERLY
//       star dome (milky-way/nebula wash + PSF-halo stars, EnvStars shader with
//       baked painterly moon + twinkle + slow drift — style ruling ModBuild 127:
//       must match Gloomhaven's painted look, never crisp geometric dots),
//       comet-tail shooting stars, two bokeh firefly swarms, ground-fog donut.
//       NO ground plane / trees / water — the game's marsh tiles provide those.
//   Assets/Bundle/Environments/Env_Cellar.prefab — indoor FX shell: drifting
//       dust motes + an INACTIVE 'GlowTemplate' torch-halo child (runtime may
//       clone it onto game torches later).
// plus the procedural textures/meshes/materials those FX reference.
//
// Hard VR rules honoured throughout (user requirement):
//  - everything is WORLD-anchored: world/local-simulated Shuriken particles on
//    static anchors — nothing camera-attached, no screen-space effects.
//  - no MonoBehaviours in the bundle: all animation is Shuriken or shader-_Time-driven.
//  - all materials use bundled GloomhavenVR/Env* shaders (never builtin Standard —
//    the pink-material trap, TOOLCHAIN.md §4.1).
//  - no colliders: the environments must never intercept game/mod raycasts.
//  - PERMANENT CONSTRAINT (user, 2026-08-12): "Beim Bodennebel hatte ich diesen
//    typischen Effekt bemerkt den man öfters in VR mods sieht: Das der nebel
//    beim Kopfschütteln sich dreht vermutlich zu einem gerichtet. Wenn du es
//    den Nebel behälst, versichere dich das er nicht dieses Verhlaten hat."
//    => NO camera-facing billboards whose sprite could show its own rotation.
//    Ground fog is HorizontalBillboard (world-XZ flat, zero head coupling).
//    Dust/firefly billboards are allowed ONLY because Env_Spark.png is radially
//    symmetric (pure function of r² — a symmetric dot cannot show rotation).
//    Shooting stars are Stretch mode with cameraVelocityScale=0: orientation
//    follows the streak's WORLD velocity, never the head.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class EnvironmentsBuilder
    {
        private const string Root = "Assets/Bundle/Environments";
        private const string TexDir = Root + "/Textures";
        private const string MeshDir = Root + "/Meshes";
        private const string MatDir = Root + "/Materials";

        // Moon bearing baked into the star-dome shader.
        private static readonly Vector3 MoonDir = new Vector3(0.596f, 0.374f, 0.710f).normalized;

        // ------------------------------------------------------------------ entry
        [MenuItem("GloomhavenVR/Build Environments")]
        public static void BuildFromMenu() => Build();

        /// <summary>Batch entry — exits the editor 0/1. Does NOT build the bundle.</summary>
        public static void BuildAll()
        {
            try
            {
                Build();
                Debug.Log("[GloomhavenVR][Env] BuildAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][Env] BuildAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Build()
        {
            AssetDatabase.Refresh();
            GenerateTextures();
            GenerateMeshes();
            BuildMaterials();
            AssetDatabase.SaveAssets();
            BuildCellar();
            BuildSwamp();
            AssetDatabase.SaveAssets();
        }

        // ================================================================ textures
        // All procedural, seeded => deterministic. No external downloads.
        // STYLE (user, ModBuild 127 round): the crisp geometric dots / flat-gradient
        // sky read as "low-poly" and clashed with Gloomhaven's painterly art. All
        // sprites and the sky are now PAINTED: layered fBM washes, gaussian-PSF
        // star splats with halos, soft irregular edges — never hard geometry.
        private static void GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);

            WritePng(TexDir + "/Env_Spark.png", MakeSpark(64), 64, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Glow.png", MakeGlow(128), 128, 128, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Streak.png", MakeStreak(256, 64), 256, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), 256, 256, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(512), 512, 512, sRGB: true, clamp: true);
            // night sky: RGB = painted layer (milky way, nebula washes, star PSFs
            // with halos, horizon haze), A = twinkle mask (bright star cores only).
            // No mips (the dome magnifies the texture ~3.5x on screen — never
            // minified) and BC7 (CompressedHQ): the soft painted content compresses
            // cleanly; the old RGBA32-uncompressed import alone was ~8 MB of bundle.
            WritePng(TexDir + "/Env_Stars.png", MakeNightSky(2048, 1024), 2048, 1024,
                sRGB: false, clamp: false, clampV: true, mips: false,
                comp: TextureImporterCompression.CompressedHQ, alphaDilate: false);
            AssetDatabase.Refresh();
        }

        private static Color[] MakeSpark(int n)
        {
            // tiny soft mote (dust). RADIALLY SYMMETRIC — pure function of r², so a
            // camera-facing billboard can never show its own rotation (VR rule).
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r2 = (dx * dx + dy * dy) * 4f; // 0..1 at edge
                    float a = Mathf.Exp(-r2 * 8f) + 0.30f * Mathf.Exp(-r2 * 2.2f);
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            return px;
        }

        private static Color[] MakeGlow(int n)
        {
            // firefly bokeh: soft gaussian core, wide haze, faint out-of-focus rim.
            // RADIALLY SYMMETRIC (pure function of r) — billboard-safe in VR.
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 1 at edge
                    float r2 = r * r;
                    float a = 0.85f * Mathf.Exp(-r2 * 5.5f)
                            + 0.32f * Mathf.Exp(-r2 * 1.6f)
                            + 0.15f * Mathf.Exp(-((r - 0.60f) * (r - 0.60f)) / 0.017f);
                    a *= Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.78f, 1f, r)); // reach 0 at edge
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            return px;
        }

        private static Color[] MakeStreak(int w, int h)
        {
            // shooting-star comet: bright soft head near u=0.82, glowing tail that
            // tapers and fades toward u=0. SYMMETRIC ABOUT v=0.5 — in Stretch render
            // mode the head-driven roll around the velocity axis stays invisible.
            const float uh = 0.82f;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float sx = u - uh, sy = (v - 0.5f) * 2f; // sy: -1..1
                    float head = Mathf.Exp(-(sx * sx * 260f + sy * sy * 7.5f));
                    float a = head;
                    if (u < uh)
                    {
                        float t = u / uh;                                  // 0 tail-tip .. 1 head
                        float fall = Mathf.Exp(-(uh - u) * 4.0f);          // brightness decay
                        float wdt = Mathf.Lerp(0.10f, 0.52f, Mathf.Pow(t, 0.8f)); // taper
                        float flick = 0.85f + 0.15f * Noise3(u * 11f, 3.7f, 9.1f, 517);
                        a += 0.75f * fall * Mathf.Exp(-(sy * sy) / (wdt * wdt)) * flick;
                    }
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.08f, u))
                       * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.94f, 1f, u));
                    px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            return px;
        }

        private static Color[] MakeFogPuff(int n)
        {
            // wispy marsh-mist puff: domain-warped fBM inside a soft round falloff —
            // painterly torn edges instead of a hard-rimmed disc.
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 1 at edge
                    // domain warp => curling, uneven internal structure
                    float wx = u + 0.45f * (Noise3(u * 2.3f, v * 2.3f, 4.7f, 811) - 0.5f);
                    float wy = v + 0.45f * (Noise3(u * 2.3f, v * 2.3f, 9.2f, 812) - 0.5f);
                    float nse = Fbm3(new Vector3(wx * 3.2f, wy * 3.2f, 1.5f), 4, 813);
                    float blob = Mathf.Exp(-r * r * 2.6f);
                    float edge = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, r));
                    float a = blob * (0.30f + 0.80f * Mathf.Pow(nse, 1.4f)) * edge;
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            return px;
        }

        private static Color[] MakeMoon(int n)
        {
            // painterly moon: one CONTINUOUS radial profile — disc melts through a
            // wide luminous limb into a layered halo. No branch, no alpha step: the
            // earlier two-branch version drew a hard rim ring ("ball in a donut",
            // iteration-6 lesson). Maria are soft multi-octave washes, craters are
            // barely-there accents, shading is a hint of form only.
            var px = new Color[n * n];
            var ivory = new Vector3(1.00f, 0.955f, 0.86f);
            var mareCol = new Vector3(0.66f, 0.685f, 0.70f); // grey-teal seas
            var lightDir = new Vector3(0.42f, 0.30f, 0.855f).normalized;
            const float R = 0.22f; // small disc => plenty of sprite left for the halo
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    // hand-drawn limb: radius wobbles ~1.5%, edge is WIDE and soft
                    float Rw = R * (1f + 0.03f * (Noise3(Mathf.Cos(ang) * 2.6f, Mathf.Sin(ang) * 2.6f, 7.7f, 2211) - 0.5f));
                    float inside = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(Rw - 0.018f, Rw + 0.022f, r));

                    // ---- disc paint ----
                    float f = Mathf.Clamp01(r / Rw);
                    float nz = Mathf.Sqrt(Mathf.Max(0.02f, 1f - f * f));
                    var nrm = new Vector3(dx / Rw, dy / Rw, nz).normalized;
                    float lam = Mathf.Clamp01(Vector3.Dot(nrm, lightDir));
                    float shade = 0.80f + 0.20f * lam;            // hint of form only
                    float limb = 1f - 0.16f * Mathf.Pow(f, 3f);
                    float m = Fbm3(new Vector3(u * 4.6f, v * 4.6f, 3.3f), 5, 2222);
                    float mare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.74f, m)) * 0.42f;
                    float c1 = Mathf.Exp(-((u - 0.44f) * (u - 0.44f) + (v - 0.57f) * (v - 0.57f)) / 0.0011f);
                    float c2 = Mathf.Exp(-((u - 0.56f) * (u - 0.56f) + (v - 0.455f) * (v - 0.455f)) / 0.0006f);
                    var discRgb = Vector3.Lerp(ivory, mareCol, Mathf.Clamp01(mare + 0.14f * c1 + 0.11f * c2))
                                  * (limb * shade);

                    // ---- halo paint: warm inner veil cooling outward, uneven rim ----
                    float xr = Mathf.Max(0f, r - Rw);
                    float halo = 0.34f * Mathf.Exp(-xr * 13f) + 0.18f * Mathf.Exp(-xr * 3.8f);
                    halo *= 0.82f + 0.18f * Noise3(Mathf.Cos(ang) * 3.1f, Mathf.Sin(ang) * 3.1f, 1.9f, 2233);
                    var haloRgb = Vector3.Lerp(new Vector3(1.00f, 0.94f, 0.80f),
                                               new Vector3(0.74f, 0.81f, 0.93f), Mathf.Clamp01(xr * 3.5f));

                    // ---- continuous blend ----
                    var rgb = Vector3.Lerp(haloRgb, discRgb, inside);
                    float a = Mathf.Lerp(halo, 1f, inside);
                    // force alpha to 0 at the sprite border — residual 2% halo showed
                    // as a hard square against the night sky (iteration-4 lesson)
                    a *= Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.40f, 0.5f, r));
                    px[y * n + x] = new Color(rgb.x, rgb.y, rgb.z, Mathf.Clamp01(a));
                }
            return px;
        }

        // Milky-way band plane normal — chosen so the band arcs high across the sky
        // well away from the moon bearing (composition: moon SE-ish, band NW arc).
        private static readonly Vector3 MwNormal = new Vector3(0.62f, 0.50f, 0.60f).normalized;

        private static Color[] MakeNightSky(int w, int h)
        {
            // RGB = painted additive layer over the shader's vertical gradient:
            //   milky-way band (fBM patchiness + dark rift), broad teal/indigo nebula
            //   washes, warm umber horizon haze, stars as gaussian PSFs WITH soft
            //   halos and colour temperature variation (warm/neutral/blue).
            // A   = twinkle mask: bright star CORES only (shader modulates a top-up).
            // v=0 is the nadir, v=1 the zenith (matches the dome mesh UVs).
            var px = new Color[w * h];

            // ---- painterly wash, computed at half res (it is low-frequency by
            // design) and bilinearly upsampled — also grants extra softness ----
            int ww = w / 2, wh = h / 2;
            var wash = new Vector3[ww * wh];
            for (int y = 0; y < wh; y++)
            {
                float lat = ((y + 0.5f) / wh - 0.5f) * Mathf.PI;
                float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                for (int x = 0; x < ww; x++)
                {
                    float lon = (x + 0.5f) / ww * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Sin(lon) * cl, sl, Mathf.Cos(lon) * cl);
                    wash[y * ww + x] = NightWash(d);
                }
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = SampleBilinear(wash, ww, wh, (x + 0.5f) / w, (y + 0.5f) / h);
                    px[y * w + x] = new Color(c.x, c.y, c.z, 0f);
                }

            // ---- stars: uniform on the sphere, density boosted inside the band ----
            var rnd = new System.Random(4404);
            int placed = 0;
            const int starTotal = 4200;
            int guard = 0;
            while (placed < starTotal && guard++ < starTotal * 40)
            {
                float su = (float)rnd.NextDouble();
                float sy = (float)rnd.NextDouble() * 2f - 1f;          // dir.y uniform
                float vAng = Mathf.Asin(sy);
                float lon = su * Mathf.PI * 2f;
                float cl = Mathf.Cos(vAng);
                var dir = new Vector3(Mathf.Sin(lon) * cl, sy, Mathf.Cos(lon) * cl);
                if (dir.y < -0.05f) continue;                          // below horizon: skip
                // more stars inside the milky-way band
                float band = Mathf.Exp(-Mathf.Pow(Vector3.Dot(dir, MwNormal) / 0.20f, 2f));
                if ((float)rnd.NextDouble() > 0.35f + 0.65f * band) continue;

                float cx = su * w;
                float cy = (vAng / Mathf.PI + 0.5f) * h;
                // size classes. Cores stay SMALL (the dome magnifies the texture
                // ~3.5x on screen — fat gaussians read as bokeh, iteration-1 lesson);
                // the glow comes from LOW-AMPLITUDE halos, not fat cores.
                float roll = (float)rnd.NextDouble();
                bool hero = roll < 0.015f, brightStar = roll < 0.09f;
                float sigma = hero ? 1.1f + (float)rnd.NextDouble() * 0.4f
                            : brightStar ? 0.75f + (float)rnd.NextDouble() * 0.35f
                                         : 0.5f + (float)rnd.NextDouble() * 0.3f;
                float amp = hero ? 0.80f + (float)rnd.NextDouble() * 0.20f
                          : brightStar ? 0.45f + (float)rnd.NextDouble() * 0.30f
                                       : 0.08f + (float)rnd.NextDouble() * 0.32f;
                // colour temperature: warm gold / parchment white / cold blue
                float temp = (float)rnd.NextDouble();
                Vector3 scol = temp < 0.22f
                    ? Vector3.Lerp(new Vector3(1.00f, 0.86f, 0.64f), new Vector3(1.00f, 0.94f, 0.82f), temp / 0.22f)
                    : Vector3.Lerp(new Vector3(0.94f, 0.96f, 1.00f), new Vector3(0.72f, 0.82f, 1.00f), (temp - 0.22f) / 0.78f);
                // fade toward the horizon haze
                amp *= Mathf.Clamp01(dir.y * 3.5f + 0.25f);
                // equirect: one texel spans cos(lat) less longitude arc — widen the
                // splat in x so stars stay ROUND on the dome (zenith is visible!)
                float xStretch = 1f / Mathf.Max(cl, 0.20f);

                // halos stay TIGHT and FAINT — iteration-5 lesson: 5σ halos at 16%
                // amp rendered as giant bokeh balls, exactly the cheap look we're
                // replacing. A halo may only read as "glow", never as a disc.
                float haloAmp = amp * (hero ? 0.07f : brightStar ? 0.05f : 0.03f);
                float haloSigma = sigma * (hero ? 3.0f : 2.4f);
                int rad = Mathf.CeilToInt(haloSigma * 2.2f * xStretch);
                for (int oy = -rad; oy <= rad; oy++)
                {
                    int yy = (int)cy + oy;
                    if (yy < 0 || yy >= h) continue;
                    for (int ox = -rad; ox <= rad; ox++)
                    {
                        int xx = ((int)cx + ox + w * 4) % w; // wrap U
                        float ex = ox / xStretch;
                        float d2 = ex * ex + oy * oy;
                        float core = amp * Mathf.Exp(-d2 / (sigma * sigma) * 1.6f);
                        float halo = haloAmp * Mathf.Exp(-d2 / (haloSigma * haloSigma));
                        int idx = yy * w + xx;
                        var p = px[idx];
                        p.r += scol.x * (core + halo);
                        p.g += scol.y * (core + halo);
                        p.b += scol.z * (core + halo);
                        if (hero || brightStar) p.a += core;   // twinkle mask: cores only
                        px[idx] = p;
                    }
                }
                placed++;
            }

            // clamp + triangular dither (±0.5 LSB): the washes live in the darkest
            // 8-bit codes — undithered they band into visible contour blotches
            var drnd = new System.Random(7707);
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                float dth = ((float)drnd.NextDouble() - (float)drnd.NextDouble()) / 255f;
                px[i] = new Color(Mathf.Clamp01(p.r + dth), Mathf.Clamp01(p.g + dth),
                                  Mathf.Clamp01(p.b + dth), Mathf.Clamp01(p.a));
            }
            return px;
        }

        /// <summary>Painted additive sky wash for direction d (linear RGB).</summary>
        private static Vector3 NightWash(Vector3 d)
        {
            var col = Vector3.zero;

            // milky way: soft great-circle band — granular fBM clumps (it must read
            // as "made of stars", not a searchlight beam), dark central rift, and a
            // narrow brighter spine. Fades out well above the horizon haze.
            float bd = Vector3.Dot(d, MwNormal);
            float band = Mathf.Exp(-Mathf.Pow(bd / 0.12f, 2f));
            if (band > 0.002f)
            {
                float patch = Mathf.Pow(Fbm3(d * 5.2f, 5, 9001), 2.2f);
                // second, finer granularity so no stretch of the band is ever smooth
                patch *= 0.55f + 0.45f * Mathf.Pow(Fbm3(d * 9.5f, 3, 9007), 1.5f) * 2f;
                float rift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.75f,
                    Fbm3(d * 2.3f + new Vector3(7.7f, 1.3f, 4.1f), 3, 9002)));
                float spine = Mathf.Exp(-Mathf.Pow(bd / 0.045f, 2f));
                float mw = (band * (0.15f + 0.85f * patch) * 0.085f + spine * patch * 0.055f)
                           * (1f - 0.65f * rift * band);
                float hue = Fbm3(d * 1.7f + new Vector3(2.2f, 8.8f, 5.5f), 3, 9003);
                var mwCol = Vector3.Lerp(new Vector3(0.55f, 0.63f, 0.75f),   // pale starlight blue
                                         new Vector3(0.58f, 0.49f, 0.38f),   // warm galactic dust
                                         Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.70f, hue)));
                col += mwCol * mw * Mathf.Clamp01(d.y * 2.2f + 0.25f);
            }

            // broad nebula washes — barely-there colour variation across the vault
            float neb1 = Mathf.Pow(Mathf.Clamp01(Fbm3(d * 1.15f + new Vector3(3.1f, 0.4f, 6.9f), 3, 9004) * 1.3f - 0.30f), 1.5f);
            col += new Vector3(0.006f, 0.026f, 0.030f) * neb1;                 // deep teal
            float neb2 = Mathf.Pow(Mathf.Clamp01(Fbm3(d * 0.9f + new Vector3(8.4f, 4.2f, 1.7f), 3, 9005) * 1.3f - 0.32f), 1.5f);
            col += new Vector3(0.020f, 0.013f, 0.034f) * neb2;                 // dusky indigo

            // horizon haze: a low, subtle band of warm umber — mist over the marsh,
            // NOT a glowing wall (iteration-5 lesson: 0.085 amp read as a dust storm)
            float hz = Mathf.Exp(-Mathf.Pow(Mathf.Max(d.y, 0f) / 0.10f, 1.6f));
            float hn = 0.55f + 0.45f * Fbm3(d * 2.4f + new Vector3(1.1f, 5.5f, 3.3f), 3, 9006);
            col += new Vector3(0.038f, 0.026f, 0.015f) * hz * hn;

            // nothing painted below the water line
            col *= Mathf.Clamp01(d.y * 8f + 1f);
            return col;
        }

        private static Vector3 SampleBilinear(Vector3[] grid, int gw, int gh, float u, float v)
        {
            float fx = u * gw - 0.5f, fy = v * gh - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int x1 = (x0 + 1 + gw) % gw; x0 = (x0 + gw) % gw;                 // wrap U
            int y1 = Mathf.Clamp(y0 + 1, 0, gh - 1); y0 = Mathf.Clamp(y0, 0, gh - 1); // clamp V
            var a = Vector3.Lerp(grid[y0 * gw + x0], grid[y0 * gw + x1], tx);
            var b = Vector3.Lerp(grid[y1 * gw + x0], grid[y1 * gw + x1], tx);
            return Vector3.Lerp(a, b, ty);
        }

        // ---- seam-free 3D value noise (evaluated on sphere directions => no
        // equirect seam, no pole pinching) ----
        private static float Hash3(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 974711);
                n *= 1274126177u;
                n ^= n >> 16;
                n *= 2246822519u;
                n ^= n >> 13;
                return (n & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Noise3(float x, float y, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float tx = x - x0, ty = y - y0, tz = z - z0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty); tz = tz * tz * (3f - 2f * tz);
            float c000 = Hash3(x0, y0, z0, seed), c100 = Hash3(x0 + 1, y0, z0, seed);
            float c010 = Hash3(x0, y0 + 1, z0, seed), c110 = Hash3(x0 + 1, y0 + 1, z0, seed);
            float c001 = Hash3(x0, y0, z0 + 1, seed), c101 = Hash3(x0 + 1, y0, z0 + 1, seed);
            float c011 = Hash3(x0, y0 + 1, z0 + 1, seed), c111 = Hash3(x0 + 1, y0 + 1, z0 + 1, seed);
            float a = Mathf.Lerp(Mathf.Lerp(c000, c100, tx), Mathf.Lerp(c010, c110, tx), ty);
            float b = Mathf.Lerp(Mathf.Lerp(c001, c101, tx), Mathf.Lerp(c011, c111, tx), ty);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Multi-octave 3D value noise, normalized ~0..1. p in "cell" units.</summary>
        private static float Fbm3(Vector3 p, int octaves, int seed)
        {
            float acc = 0f, amp = 1f, ampSum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                acc += amp * Noise3(p.x, p.y, p.z, seed + o * 131);
                ampSum += amp;
                amp *= 0.55f;
                p *= 2.03f;
            }
            return acc / ampSum;
        }

        private static void WritePng(string path, Color[] px, int w, int h, bool sRGB, bool clamp,
            bool clampV = false, bool mips = true,
            TextureImporterCompression comp = TextureImporterCompression.Compressed,
            bool alphaDilate = true)
        {
            if (px.Length != w * h) throw new Exception($"WritePng {path}: {px.Length} px != {w}x{h}");
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = sRGB;
            ti.alphaIsTransparency = alphaDilate;
            ti.mipmapEnabled = mips;
            ti.wrapModeU = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.wrapModeV = (clamp || clampV) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = comp;
            ti.SaveAndReimport();
        }

        // ================================================================== meshes
        private static void GenerateMeshes()
        {
            Directory.CreateDirectory(MeshDir);
            // 64x32: at 45 m radius the silhouette must never read faceted (style
            // complaint round 2 — "low-poly"). ~4k tris, still trivial for the dome.
            SaveMesh(MeshDir + "/Env_Dome.asset", BuildSphere(64, 32, inward: true));
            SaveMesh(MeshDir + "/Env_GlowSphere.asset", BuildSphere(16, 8, inward: false));
        }

        private static void SaveMesh(string path, Mesh src)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                src.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(src, path);
            }
            else
            {
                existing.Clear();
                existing.vertices = src.vertices;
                existing.normals = src.normals;
                existing.uv = src.uv;
                existing.triangles = src.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(src);
            }
        }

        private static Mesh BuildSphere(int lon, int lat, bool inward)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float vv = y / (float)lat;
                float latAng = (vv - 0.5f) * Mathf.PI; // -90..+90
                float r = Mathf.Cos(latAng), py = Mathf.Sin(latAng);
                for (int x = 0; x <= lon; x++)
                {
                    float uu = x / (float)lon;
                    float lonAng = uu * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(lonAng) * r, py, Mathf.Cos(lonAng) * r));
                    uv.Add(new Vector2(uu, vv));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    if (inward) t.AddRange(new[] { a, b, c, b, d, c });
                    else t.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetNormals(v.Select(p => (inward ? -1f : 1f) * p.normalized).ToList());
            m.SetTriangles(t, 0);
            return m;
        }

        // =============================================================== materials
        private static Material LoadOrNewMat(string path, string shaderName)
        {
            var sh = Shader.Find(shaderName) ?? throw new Exception($"Bundled shader '{shaderName}' not found (compile error?)");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        private static void BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);

            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + n);

            var fog = LoadOrNewMat(MatDir + "/FX_Fog.mat", "GloomhavenVR/EnvParticleAlpha");
            fog.SetTexture("_MainTex", T("Env_FogPuff.png"));
            fog.SetColor("_Tint", Color.white);

            var dust = LoadOrNewMat(MatDir + "/FX_Dust.mat", "GloomhavenVR/EnvParticleAlpha");
            dust.SetTexture("_MainTex", T("Env_Spark.png"));
            dust.SetColor("_Tint", Color.white);

            var firefly = LoadOrNewMat(MatDir + "/FX_Firefly.mat", "GloomhavenVR/EnvParticleAdd");
            firefly.SetTexture("_MainTex", T("Env_Glow.png")); // soft bokeh, radially symmetric
            firefly.SetColor("_Tint", Color.white);

            var streak = LoadOrNewMat(MatDir + "/FX_StarStreak.mat", "GloomhavenVR/EnvParticleAdd");
            streak.SetTexture("_MainTex", T("Env_Streak.png")); // comet head + tapering tail
            streak.SetColor("_Tint", Color.white);

            // warm torch halo — used by the cellar shell's inactive GlowTemplate
            var glowWarm = LoadOrNewMat(MatDir + "/FX_GlowWarm.mat", "GloomhavenVR/EnvGlow");
            glowWarm.SetColor("_Tint", new Color(1f, 0.55f, 0.20f, 0.65f));
            glowWarm.SetFloat("_Falloff", 2.2f);

            // the moon is baked into the star-dome shader (a separate blended quad
            // left a visible seam against the sky gradient)
            var stars = LoadOrNewMat(MatDir + "/Swamp_StarDome.mat", "GloomhavenVR/EnvStars");
            stars.SetTexture("_MainTex", T("Env_Stars.png"));
            stars.SetColor("_TopCol", new Color(0.009f, 0.014f, 0.030f));
            stars.SetColor("_HorizonCol", new Color(0.034f, 0.050f, 0.076f));
            stars.SetFloat("_SkyBoost", 1.0f);
            stars.SetColor("_StarCol", new Color(0.85f, 0.90f, 1.0f));
            stars.SetFloat("_TwinkleSpeed", 1.6f);
            stars.SetFloat("_TwinkleAmp", 0.5f);
            stars.SetFloat("_DriftSpeed", 0.00035f); // full sky revolution ~48 min
            stars.SetTexture("_MoonTex", T("Env_Moon.png"));
            stars.SetVector("_MoonDir", MoonDir);
            stars.SetColor("_MoonCol", new Color(1f, 0.98f, 0.92f));
            // disc fills only 0.44 of the sprite (R=0.22/0.5) — extent sized so the
            // disc stays ~3.3° radius while the halo gets real room to breathe
            stars.SetFloat("_MoonExtent", 0.13f);

            AssetDatabase.SaveAssets();
        }

        // ============================================================ scene helpers
        private static Material Mat(string file) =>
            AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + file)
            ?? throw new Exception("Material not found: " + file);

        private static GameObject Solid(Transform parent, string goName, string meshAsset, Material mat,
            Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/" + meshAsset)
                       ?? throw new Exception("Mesh not found: " + meshAsset);
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static ParticleSystem NewPS(Transform parent, string name, Vector3 pos, Vector3 euler, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            return ps;
        }

        private static Gradient Grad(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            g.SetKeys(
                keys.Select(k => new GradientColorKey(k.c, k.t)).ToArray(),
                keys.Select(k => new GradientAlphaKey(k.c.a, k.t)).ToArray());
            return g;
        }

        private static void LogStats(GameObject root, string label)
        {
            long tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            int psCount = root.GetComponentsInChildren<ParticleSystem>(true).Length;
            int maxAlive = root.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.main.maxParticles);
            Debug.Log($"[GloomhavenVR][Env] {label}: {tris} triangles, {psCount} particle systems, max alive {maxAlive}.");
        }

        // ================================================================== CELLAR
        // FX shell: drifting dust motes + an inactive torch-halo template. The room
        // geometry itself comes from the game (Apparance scenario tiles).
        private static void BuildCellar()
        {
            var root = new GameObject("Env_Cellar");
            try
            {
                var t = root.transform;

                // drifting dust motes in the candlelight (world-space room volume)
                var dust = NewPS(t, "DustMotes", new Vector3(0, 1.8f, 0), Vector3.zero, Mat("FX_Dust.mat"));
                var dm = dust.main;
                dm.simulationSpace = ParticleSystemSimulationSpace.World;
                dm.duration = 30f;
                dm.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
                dm.startSpeed = 0f;
                dm.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
                dm.startColor = new Color(1f, 0.88f, 0.65f, 0.32f); // subtle: barely-there motes
                dm.maxParticles = 30;
                var de = dust.emission; de.rateOverTime = 1.6f;
                var dsh = dust.shape; dsh.enabled = true; dsh.shapeType = ParticleSystemShapeType.Box; dsh.scale = new Vector3(6.5f, 3.2f, 6.5f);
                var dn = dust.noise; dn.enabled = true; dn.strength = 0.03f; dn.frequency = 0.25f; dn.scrollSpeed = 0.1f;
                var dcol = dust.colorOverLifetime; dcol.enabled = true;
                dcol.color = new ParticleSystem.MinMaxGradient(Grad((0f, new Color(1, 1, 1, 0f)), (0.15f, new Color(1, 1, 1, 1f)), (0.85f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));

                // torch-halo template: DISABLED by default; runtime may clone it onto
                // the game's torches later. 0.30 m radius = the old wall-torch halo.
                var glow = Solid(t, "GlowTemplate", "Env_GlowSphere.asset", Mat("FX_GlowWarm.mat"),
                    Vector3.zero, Vector3.zero, Vector3.one * 0.30f);
                glow.SetActive(false);

                LogStats(root, "Env_Cellar");
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/Env_Cellar.prefab", out bool ok);
                if (!ok) throw new Exception("SaveAsPrefabAsset failed for Env_Cellar");
                Debug.Log("[GloomhavenVR][Env] Prefab written: " + Root + "/Env_Cellar.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // =================================================================== SWAMP
        // FX shell: star dome + shooting stars + fireflies + ground fog. NO ground
        // plane, trees or water — the game's marsh tiles provide those at runtime.
        private static void BuildSwamp()
        {
            var root = new GameObject("Env_Swamp");
            try
            {
                var t = root.transform;

                // ---- sky: star dome (shader-twinkled, moon baked into the shader) ----
                Solid(t, "StarDome", "Env_Dome.asset", Mat("Swamp_StarDome.mat"),
                    Vector3.zero, Vector3.zero, Vector3.one * 45f);

                // ---- ground fog: big slow WORLD-SPACE puffs standing over the marsh ----
                var fog = NewPS(t, "GroundFog", new Vector3(0, 0.45f, 0), new Vector3(-90, 0, 0), Mat("FX_Fog.mat"));
                var fm = fog.main;
                fm.simulationSpace = ParticleSystemSimulationSpace.World;
                fm.duration = 40f;
                fm.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
                fm.startSpeed = 0f;
                fm.startSize = new ParticleSystem.MinMaxCurve(7f, 13f); // bigger + dimmer = softer overlap
                fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                fm.startColor = new Color(0.54f, 0.62f, 0.76f, 0.07f); // dim: mist, not snowdrifts
                fm.maxParticles = 34;
                var fe = fog.emission; fe.rateOverTime = 1.7f;
                var fsh = fog.shape; fsh.enabled = true; fsh.shapeType = ParticleSystemShapeType.Donut;
                fsh.radius = 8.5f; fsh.donutRadius = 3.5f; fsh.radiusThickness = 1f;
                var fv = fog.velocityOverLifetime; fv.enabled = true;
                fv.space = ParticleSystemSimulationSpace.World;
                fv.x = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                fv.z = new ParticleSystem.MinMaxCurve(0.03f, 0.10f);
                var frot = fog.rotationOverLifetime; frot.enabled = true;
                frot.z = new ParticleSystem.MinMaxCurve(-3f * Mathf.Deg2Rad, 3f * Mathf.Deg2Rad);
                var fcol = fog.colorOverLifetime; fcol.enabled = true;
                fcol.color = new ParticleSystem.MinMaxGradient(Grad(
                    (0f, new Color(1, 1, 1, 0f)), (0.18f, new Color(1, 1, 1, 1f)),
                    (0.75f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));
                var fr = fog.GetComponent<ParticleSystemRenderer>();
                // VR constraint (see header): camera-facing billboards made the fog
                // visibly re-orient on head shake. HorizontalBillboard locks every
                // puff flat in world XZ — random spawn yaw + slow world-driven drift
                // are the ONLY rotations; nothing follows the camera. (Vertical
                // billboards would still yaw toward the head — equally forbidden.)
                fr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                fr.maxParticleSize = 2.5f; // don't clamp big close puffs
                fr.sortMode = ParticleSystemSortMode.Distance;

                // ---- fireflies: two swarms flanking the play space ----
                FireflyPS(t, new Vector3(2.6f, 0.55f, -2.4f));
                FireflyPS(t, new Vector3(-3.6f, 0.6f, 3.4f));

                // ---- shooting stars: infrequent streaks across the sky ----
                var meteor = NewPS(t, "ShootingStars", new Vector3(0, 30f, 0), new Vector3(115f, 30f, 0f), Mat("FX_StarStreak.mat"));
                var mm = meteor.main;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                mm.duration = 20f;
                mm.startLifetime = 1.4f;
                mm.startSpeed = new ParticleSystem.MinMaxCurve(26f, 38f); // a touch slower = elegant
                mm.startSize = 0.45f;
                mm.startColor = new Color(0.95f, 0.93f, 0.85f, 0.9f); // warm-white ember
                mm.maxParticles = 4;
                var me = meteor.emission; me.rateOverTime = 0.13f; // infrequent: ~one every 8 s
                var msh = meteor.shape; msh.enabled = true; msh.shapeType = ParticleSystemShapeType.Box;
                msh.scale = new Vector3(36f, 36f, 0.1f); // spawn plane ⟂ travel direction
                var mcol = meteor.colorOverLifetime; mcol.enabled = true;
                mcol.color = new ParticleSystem.MinMaxGradient(Grad(
                    (0f, new Color(1, 1, 1, 0f)), (0.1f, new Color(1, 1, 1, 1f)),
                    (0.7f, new Color(1, 1, 1, 0.8f)), (1f, new Color(1, 1, 1, 0f))));
                var mr = meteor.GetComponent<ParticleSystemRenderer>();
                // Stretch aligns the quad to the particle's WORLD velocity — the
                // head only picks the (invisible, sprite is radially symmetric)
                // roll around that axis. cameraVelocityScale pinned to 0 so no
                // camera-motion term can ever creep into the stretch.
                // The comet texture (Env_Streak.png) is symmetric about its long
                // axis, so that roll stays invisible — same rule as the old dot.
                mr.renderMode = ParticleSystemRenderMode.Stretch;
                mr.velocityScale = 0.11f;
                mr.lengthScale = 1f;
                mr.cameraVelocityScale = 0f;

                LogStats(root, "Env_Swamp");
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/Env_Swamp.prefab", out bool ok);
                if (!ok) throw new Exception("SaveAsPrefabAsset failed for Env_Swamp");
                Debug.Log("[GloomhavenVR][Env] Prefab written: " + Root + "/Env_Swamp.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void FireflyPS(Transform parent, Vector3 pos)
        {
            var ps = NewPS(parent, "Fireflies", pos, Vector3.zero, Mat("FX_Firefly.mat"));
            var m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.duration = 24f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            m.startSpeed = 0.02f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.10f); // bokeh sprite is softer => a touch larger
            m.startColor = new Color(0.85f, 1f, 0.42f, 1f); // warm green-gold
            m.maxParticles = 34;
            var e = ps.emission; e.rateOverTime = 3.4f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.4f;
            var n = ps.noise; n.enabled = true; n.strength = 0.35f; n.frequency = 0.35f; n.scrollSpeed = 0.15f;
            // gentle breathing pulse (the old hard on/off blink read as a cheap LED)
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(0.15f, 0.9f), new Keyframe(0.35f, 0.4f),
                new Keyframe(0.55f, 1f), new Keyframe(0.75f, 0.35f), new Keyframe(0.9f, 0.8f), new Keyframe(1f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(
                (0f, new Color(1, 1, 1, 0f)), (0.1f, new Color(1, 1, 1, 1f)),
                (0.9f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));
        }
    }
}
