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
//   Assets/Bundle/Environments/Env_Swamp.prefab  — night-sky FX shell: PHOTO
//       star dome (real night-sky panorama, see NIGHT SKY below; EnvStars shader
//       with painterly moon sprite + subtle star twinkle + slow drift — style
//       ruling round 3: procedurally generated skies were rejected twice, the
//       sky must be a real high-resolution photograph),
//       comet-tail shooting stars, two bokeh firefly swarms, ground-fog donut;
//       PLUS (custom-asset round, user ruling 2026-08-13: "nicht low-poly
//       sondern zum Styl des Spiels passendes") a 'RoomGeo' night-marsh
//       clearing built from CC0 photoscans — see BuildEnvironmentRooms.cs.
//   Assets/Bundle/Environments/Env_Cellar.prefab — indoor shell: the SAME
//       night-sky star dome as the swamp (user finding, ModBuild 129 round:
//       with the game's sky sphere hidden, everything above the generated
//       room was pure black — the dome shows through the barred window),
//       drifting dust motes + an INACTIVE 'GlowTemplate' torch-halo child
//       (runtime may clone it onto light sources later);
//       PLUS a 'RoomGeo' candle-lit stone cellar (BuildEnvironmentRooms.cs).
//       NO shooting stars / fireflies / ground fog — those are swamp-flavor.
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
using System.Globalization;
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

        // Moon bearing baked into the star-dome shader. Same azimuth as the old
        // swamp moon, but raised from 22 deg to 40 deg elevation for the night
        // forest: at 22 deg the moon sat behind the tree ring, and its shafts
        // raked so flat they never reached the clearing. At 40 deg it is seen
        // through the tear in the canopy and the shafts cross the play space.
        // EnvRoomBuilder.MoonDir MUST match (shafts, rim light, water glints).
        public static readonly Vector3 MoonDir = new Vector3(0.49262f, 0.64279f, 0.58686f).normalized;

        // ------------------------------------------------------------ star sky
        // Yale Bright Star Catalogue (public domain) -> real star point sprites.
        // See Assets/Editor/star_catalogue.py for the source and the parse; the
        // CSV lives in Assets/Editor (editor-only: never in the player build or
        // the bundle), only the derived MESH ships.
        private const string StarCsv = "Assets/Editor/bsc5_stars.csv";
        private const float StarMagLimit = 6.0f;   // full naked-eye sky (~5000 stars)
        private const float StarRadius = 44f;      // just inside the 45 m photo dome
        private const float ObserverLatDeg = 48f;  // central-European sky: Polaris at 48 deg

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
            EnvRoomBuilder.EnforceImports();          // CC0 photoscan models/textures (Imported/)
            GenerateMeshes();
            BuildMaterials();
            AssetDatabase.SaveAssets();
            BuildCellar();
            BuildForest();
            AssetDatabase.SaveAssets();
            PruneUnreferenced();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Delete generated materials/meshes/textures no prefab uses any
        /// more. EVERYTHING under Assets/Bundle ships (BuildBundles collects the
        /// whole tree), so a prop dropped from a room would otherwise keep paying
        /// bundle bytes forever — the swamp's ponds, water shader and quiver-tree
        /// materials all died that way.</summary>
        private static void PruneUnreferenced()
        {
            var prefabs = new[] { Root + "/Env_Cellar.prefab", Root + "/Env_Swamp.prefab" }
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p != null).Cast<UnityEngine.Object>().ToArray();
            if (prefabs.Length != 2) throw new Exception("PruneUnreferenced: a room prefab is missing.");
            var keep = new HashSet<string>(EditorUtility.CollectDependencies(prefabs)
                .Select(AssetDatabase.GetAssetPath)
                .Where(s => !string.IsNullOrEmpty(s)));
            int n = 0;
            foreach (var dir in new[] { MatDir, MeshDir, TexDir })
                foreach (var f in Directory.GetFiles(dir))
                {
                    string p = f.Replace('\\', '/');
                    if (p.EndsWith(".meta") || keep.Contains(p)) continue;
                    AssetDatabase.DeleteAsset(p);
                    Debug.Log("[GloomhavenVR][Env] pruned unreferenced " + p);
                    n++;
                }
            if (n > 0) AssetDatabase.Refresh();
            Debug.Log($"[GloomhavenVR][Env] Prune: {n} orphan asset(s) removed, {keep.Count} referenced.");
        }

        // ---------------------------------------------------------------- NIGHT SKY
        // The sky texture is NOT generated: it is a processed REAL photograph
        // (style ruling round 3 — two procedural skies were rejected for banding /
        // low resolution / synthetic look; user demanded a sourced photo).
        //
        //   Source   : "Rogland Clear Night" by Greg Zaal, Poly Haven — CC0.
        //              https://polyhaven.com/a/rogland_clear_night
        //              16k unclipped linear equirect HDR (16384x8192):
        //              https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/16k%2B/rogland_clear_night_16k.hdr
        //   Content  : dense photographic starfield, full milky-way arch (galactic
        //              core, dark rift, Carina nebula, Magellanic clouds), no moon,
        //              minimal light pollution. Desert terrain occupied the lowest
        //              ~22 deg — replaced by a starlit mist band (see below), which
        //              is exactly what the swamp horizon wants.
        //   Pipeline : scripted, reproducible (float32 end-to-end, dither only at
        //              the final 8-bit quantization). The exact processing script
        //              is embedded verbatim at the END OF THIS FILE; summary:
        //                1. remap the -20..+90 deg elevation band to WxH*2
        //                   (supersampled) with bilinear taps, U wraps;
        //                2. tone map: normalize sky background (lum 0.30) to 1.0,
        //                   log-contrast ^1.6 around that pivot, scale background
        //                   to 0.055 linear, Reinhard shoulder above 0.75 for star
        //                   cores, +12% saturation;
        //                3. terrain removal: per-column silhouette detection
        //                   (sky lum > 0.16 sustained), profile smoothed with a
        //                   wrapping gaussian +2.5 deg margin (min 5 deg); below it
        //                   a mist band — per-column MEDIAN sky colour sampled
        //                   above the profile (median rejects stars), luminance-
        //                   capped at 1.25x its own median, cooled/desaturated,
        //                   shaded darker toward the horizon, melted over 9 deg;
        //                4. fade to black from the horizon down to band bottom
        //                   (the dome shows the water rim there);
        //                5. alpha = twinkle mask: tone-mapped luminance minus a
        //                   sigma-6 gaussian background, threshold 0.10, only above
        //                   the mist;
        //                6. 2x box downsample, sRGB encode, TPDF +-0.5 LSB dither,
        //                   write RGBA PNG 8192x2560.
        //
        // The dome mesh maps ONLY that -20..+90 deg band onto V (see
        // GenerateMeshes): no texture memory is wasted on the never-visible lower
        // hemisphere, so 8192x2560 delivers the full angular resolution of an
        // 8192x4096 equirect (~23 px/deg, ~4x the rejected 2048 sky in each axis).
        private const string NightSkyPng = TexDir + "/Env_NightSky.png";
        private const float SkyBandMinDeg = -20f;   // texture V=0 elevation
        private const float SkyBandMaxDeg = 90f;    // texture V=1 elevation

        // ================================================================ textures
        // Sprites are procedural, seeded => deterministic. The night sky is a
        // shipped processed photograph (see NIGHT SKY above) — only its import
        // settings are enforced here.
        private static void GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);

            WritePng(TexDir + "/Env_Spark.png", MakeSpark(64), 64, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Glow.png", MakeGlow(128), 128, 128, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Streak.png", MakeStreak(256, 64), 256, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), 256, 256, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(512), 512, 512, sRGB: true, clamp: true);
            // haze veil for the sky dome — tiles in BOTH axes (torus blend), so
            // the two scrolling octaves never show a seam as they cross.
            WritePng(TexDir + "/Env_Haze.png", MakeHaze(256), 256, 256, sRGB: false, clamp: false,
                comp: TextureImporterCompression.Compressed, alphaDilate: false);
            ImportNightSky();
            AssetDatabase.Refresh();
        }

        private static Color[] MakeHaze(int n)
        {
            // thin high cloud / airglow veil: soft multi-octave value noise,
            // seamlessly tileable by blending the four torus corners.
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (float)n, fy = y / (float)n;
                    float H(float ox, float oy) => Fbm3(new Vector3((fx + ox) * 3.4f, (fy + oy) * 3.4f, 5.1f), 4, 771);
                    float h = Mathf.Lerp(Mathf.Lerp(H(0, 0), H(-1, 0), fx),
                                         Mathf.Lerp(H(0, -1), H(-1, -1), fx), fy);
                    h = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.80f, h));
                    px[y * n + x] = new Color(h, h, h, 1f);
                }
            return px;
        }

        private static void ImportNightSky()
        {
            if (!File.Exists(NightSkyPng))
                throw new Exception(NightSkyPng + " missing — it is a shipped asset (see NIGHT SKY comment), not generated.");
            AssetDatabase.ImportAsset(NightSkyPng);
            var ti = (TextureImporter)AssetImporter.GetAtPath(NightSkyPng);
            // sRGB=TRUE (unlike the old linear-encoded painted sky): the photo is
            // stored gamma-encoded, which spends the 8-bit codes perceptually —
            // the dark end gets ~4x the precision, killing gradient banding.
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = false;         // alpha is a twinkle MASK
            ti.mipmapEnabled = false;               // dome never minifies it
            ti.wrapModeU = TextureWrapMode.Repeat;  // sky drift wraps U
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 8192;               // default cap 2048 would crush it
            // 8192x2560 is NPOT in height — the default npotScale=ToNearest silently
            // resampled it to 8192x2048 (caught in review). 2560 is a multiple of 4,
            // which is all BC7 needs.
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7
            ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(NightSkyPng);
            if (tex == null || tex.width != 8192 || tex.height != 2560)
                throw new Exception($"Env_NightSky import lost resolution: {(tex ? tex.width : 0)}x{(tex ? tex.height : 0)}, expected 8192x2560.");
            Debug.Log($"[GloomhavenVR][Env] Night sky imported {tex.width}x{tex.height} format={tex.format}");
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
            // The dome UVs map ONLY the SkyBand elevation range onto V (the photo
            // texture covers -20..+90 deg; below the band V clamps to the black
            // bottom row) — full angular resolution, no wasted texture memory.
            SaveMesh(MeshDir + "/Env_Dome.asset",
                BuildSphere(64, 32, inward: true, bandMinDeg: SkyBandMinDeg, bandMaxDeg: SkyBandMaxDeg));
            SaveMesh(MeshDir + "/Env_GlowSphere.asset", BuildSphere(16, 8, inward: false));
            SaveMesh(MeshDir + "/Env_StarField.asset", BuildStarField());
        }

        // ============================================================ STAR FIELD
        // One quad per catalogue star, in the star's OWN frame — EnvStarPoints
        // rotates it about the celestial pole in the vertex shader (no scripts,
        // no camera coupling). Deterministic: same CSV in, same mesh out.
        //
        // Frame (Unity is left-handed with +X east, +Y up, +Z north — a correct
        // geographic frame, verified below):
        //   P = (0, sin lat,  cos lat)   celestial north pole
        //   M = (0, cos lat, -sin lat)   celestial equator on the meridian
        //                                (altitude 90-lat, due SOUTH — correct)
        //   W = P x M = (-1, 0, 0)       due WEST
        //   d(H) = sin(dec) P + cos(dec) cos(H) M + cos(dec) sin(H) W
        // A star at dec=0, H=+6h lands on W (just set) and at H=-6h on +X (east,
        // rising): the hour angle runs the right way, so the constellations come
        // out un-mirrored and Rodrigues rotation about P with a POSITIVE angle
        // carries them east -> meridian -> west, as the real sky does.
        private static Mesh BuildStarField()
        {
            string[] lines = File.Exists(StarCsv)
                ? File.ReadAllLines(StarCsv)
                : throw new Exception(StarCsv + " missing — run Assets/Editor/star_catalogue.py.");

            float lat = ObserverLatDeg * Mathf.Deg2Rad;
            var P = new Vector3(0f, Mathf.Sin(lat), Mathf.Cos(lat));
            var M = new Vector3(0f, Mathf.Cos(lat), -Mathf.Sin(lat));
            var W = new Vector3(-1f, 0f, 0f);

            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var uv2 = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            int used = 0;
            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(',');
                if (f.Length < 5) continue;
                int hr = int.Parse(f[0], CultureInfo.InvariantCulture);
                float ra = float.Parse(f[1], CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                float dec = float.Parse(f[2], CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                float mag = float.Parse(f[3], CultureInfo.InvariantCulture);
                float bv = float.Parse(f[4], CultureInfo.InvariantCulture);
                if (mag > StarMagLimit) continue;

                float h0 = -ra;                                     // hour angle at LST 0
                Vector3 d = P * Mathf.Sin(dec)
                          + M * (Mathf.Cos(dec) * Mathf.Cos(h0))
                          + W * (Mathf.Cos(dec) * Mathf.Sin(h0));
                d.Normalize();

                // brightness: the true flux ratio spans 1500x, which would be one
                // white blob and 5000 invisible dots — compress it, and let the
                // bright ones grow a bigger point-spread instead (as a real one
                // does in the eye).
                float flux = Mathf.Pow(10f, -0.4f * (mag - StarMagLimit) / 3.0f);
                float bright = 0.055f * flux;
                float size = 0.028f + 0.0055f * Mathf.Max(0f, StarMagLimit - mag);
                var c = BvToRgb(bv);
                float phase = Hash3(hr, 17, 3, 6101) * 6.2831853f;

                int b0 = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    v.Add(d * StarRadius);
                    uv.Add(new Vector2(k == 0 || k == 3 ? -1f : 1f, k < 2 ? -1f : 1f));
                    uv2.Add(new Vector2(size, phase));
                    col.Add(new Color(c.x, c.y, c.z, bright));
                }
                tri.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
                used++;
            }

            var m = new Mesh { name = "Env_StarField" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetUVs(1, uv2);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            // the shader moves every vertex; a bounds box that follows the real
            // sphere keeps the whole field from being frustum-culled mid-rotation
            m.bounds = new Bounds(Vector3.zero, Vector3.one * (StarRadius * 2.2f));
            Debug.Log($"[GloomhavenVR][Env] Star field: {used} catalogue stars (V<={StarMagLimit}), "
                      + $"{v.Count} verts, {tri.Count / 3} tris.");
            return m;
        }

        /// <summary>B-V colour index -> linear RGB, normalized to unit luminance.
        /// Ballesteros (2012, EPL 97 34008) B-V -> blackbody temperature, then
        /// Tanner Helland's Kelvin -> RGB fit (valid 1000..40000 K, so clamp:
        /// the catalogue's reddest star, B-V 5.74, would land at 1439 K).</summary>
        private static Vector3 BvToRgb(float bv)
        {
            float T = 4600f * (1f / (0.92f * bv + 1.7f) + 1f / (0.92f * bv + 0.62f));
            T = Mathf.Clamp(T, 1000f, 40000f);
            float t = T / 100f;
            float r = t <= 66f ? 255f : 329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f);
            float g = t <= 66f ? 99.4708025861f * Mathf.Log(t) - 161.1195681661f
                               : 288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f);
            float b = t >= 66f ? 255f : (t <= 19f ? 0f : 138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f);
            var c = new Vector3(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f));
            // equal-luminance normalization: without it the hot blue-white stars
            // read DIMMER than the cool orange ones at the same magnitude
            float lum = Mathf.Max(0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z, 1e-3f);
            return c / lum;
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
                var bounds = src.bounds;
                existing.Clear();
                existing.vertices = src.vertices;
                existing.normals = src.normals;
                existing.uv = src.uv;
                existing.uv2 = src.uv2;
                existing.colors = src.colors;
                existing.triangles = src.triangles;
                existing.RecalculateBounds();
                // the star field's vertices are moved by the shader — keep the
                // authored (oversized) bounds so rotation cannot cull it
                if (src.name == "Env_StarField") existing.bounds = bounds;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(src);
            }
        }

        private static Mesh BuildSphere(int lon, int lat, bool inward,
            float bandMinDeg = -90f, float bandMaxDeg = 90f)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float vv = y / (float)lat;
                float latAng = (vv - 0.5f) * Mathf.PI; // -90..+90
                float r = Mathf.Cos(latAng), py = Mathf.Sin(latAng);
                // V maps the [bandMinDeg..bandMaxDeg] elevation band (default:
                // whole sphere — then use vv EXACTLY, the deg round-trip added
                // 1-ulp noise to otherwise identical meshes). Linear in latitude,
                // so interpolation across the uniform-latitude rings stays exact;
                // below the band V clamps to 0.
                float bandV = (bandMinDeg == -90f && bandMaxDeg == 90f)
                    ? vv
                    : Mathf.Clamp01((latAng * Mathf.Rad2Deg - bandMinDeg) / (bandMaxDeg - bandMinDeg));
                for (int x = 0; x <= lon; x++)
                {
                    float uu = x / (float)lon;
                    float lonAng = uu * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(lonAng) * r, py, Mathf.Cos(lonAng) * r));
                    uv.Add(new Vector2(uu, bandV));
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

            // ---- sky backdrop: photographic Milky Way + moon + haze veil ----
            // The moon is baked into this shader (a separate blended quad left a
            // visible seam against the sky gradient).
            var stars = LoadOrNewMat(MatDir + "/Swamp_StarDome.mat", "GloomhavenVR/EnvStars");
            stars.SetTexture("_MainTex", T("Env_NightSky.png")); // real photo sky (see NIGHT SKY)
            // gradient is now only a faint backstop UNDER the photo (the photo
            // carries its own airglow/haze) — the old brighter horizon colour
            // stacked with the photo's mist into a washed-out band
            stars.SetColor("_TopCol", new Color(0.004f, 0.006f, 0.014f));
            stars.SetColor("_HorizonCol", new Color(0.008f, 0.012f, 0.021f));
            stars.SetFloat("_SkyBoost", 0.78f);
            // the photo's own star cores are pushed down: the catalogue layer
            // (EnvStarPoints) owns the point stars now, and two unaligned
            // starfields would read as a double exposure
            stars.SetFloat("_CoreSuppress", 0.88f);
            stars.SetColor("_StarCol", new Color(0.85f, 0.90f, 1.0f));
            stars.SetFloat("_TwinkleSpeed", 1.2f);
            stars.SetFloat("_TwinkleAmp", 0.30f);
            // one full turn per 2880 s — the SAME period the star layer rotates
            // in, so backdrop and stars read as ONE turning sky
            stars.SetFloat("_DriftSpeed", 1f / 2880f);
            stars.SetTexture("_HazeTex", T("Env_Haze.png"));
            stars.SetColor("_HazeCol", new Color(0.038f, 0.048f, 0.072f));
            stars.SetFloat("_HazeAmt", 0.42f);
            // dim the lowest ~10 deg so a treeline never silhouettes on a bright band
            stars.SetFloat("_HorizonDim", 0.14f);
            stars.SetTexture("_MoonTex", T("Env_Moon.png"));
            stars.SetVector("_MoonDir", MoonDir);
            stars.SetColor("_MoonCol", new Color(1f, 0.98f, 0.92f));
            // disc fills only 0.44 of the sprite (R=0.22/0.5) — extent sized so the
            // disc stays ~3.3° radius while the halo gets real room to breathe
            stars.SetFloat("_MoonExtent", 0.13f);

            // ---- real catalogue stars (see BuildStarField / EnvStarPoints) ----
            var pts = LoadOrNewMat(MatDir + "/Sky_StarPoints.mat", "GloomhavenVR/EnvStarPoints");
            float lat = ObserverLatDeg * Mathf.Deg2Rad;
            pts.SetVector("_Pole", new Vector4(0f, Mathf.Sin(lat), Mathf.Cos(lat), 0f));
            // 2*pi / 2880 s = 0.125 deg/s ~= 30x sidereal. Real 15 deg/h is
            // imperceptible; games run 20-70x (Skyrim 20x, Minecraft 72x). Above
            // ~100x a rotating sky starts to induce vection in VR.
            pts.SetFloat("_RotSpeed", 2f * Mathf.PI / 2880f);
            pts.SetFloat("_Gain", 2.1f);
            pts.SetFloat("_TwinkleAmp", 0.80f);
            pts.SetFloat("_TwinkleSpeed", 1.9f);
            pts.SetFloat("_Core", 34f);

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

        // Night-sky dome shared by every night shell (swamp + cellar). One
        // inward-facing photo dome; the painted moon is baked into the EnvStars
        // shader (a separate blended quad left a visible seam against the sky
        // gradient), so this single node IS dome + moon.
        // The node NAME 'StarDome' is a CONTRACT with src/ (runtime splits shell
        // children onto sky/room branches BY NODE NAME) — never rename it.
        private static void AddNightSky(Transform parent)
        {
            var dome = Solid(parent, "StarDome", "Env_Dome.asset", Mat("Swamp_StarDome.mat"),
                Vector3.zero, Vector3.zero, Vector3.one * 45f);
            // The real catalogue stars ride as a CHILD of StarDome (contract: new
            // sky FX are children of that single root node) so they inherit the
            // sky branch's anchoring exactly. Its own transform is identity-in-
            // world: the dome is scaled 45x, so undo that on the child — the star
            // positions are already baked at StarRadius metres.
            var field = Solid(dome.transform, "StarField", "Env_StarField.asset",
                Mat("Sky_StarPoints.mat"), Vector3.zero, Vector3.zero, Vector3.one / 45f);
            field.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            field.GetComponent<MeshRenderer>().receiveShadows = false;
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
        // FX shell: night-sky dome + drifting dust motes + an inactive torch-halo
        // template. The room geometry itself comes from the game (Apparance
        // scenario tiles). NO shooting stars / fireflies / ground fog here —
        // those are swamp-flavor.
        private static void BuildCellar()
        {
            var root = new GameObject("Env_Cellar");
            try
            {
                var t = root.transform;

                // ---- sky: same star dome as the swamp (user finding, ModBuild 129
                // round — with the game's sky sphere hidden, the void above the
                // generated room was pure black) ----
                AddNightSky(t);

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

                // ---- room interior (custom-asset round, 2026-08-13): stone cellar
                // assembled from CC0 photoscans under the 'RoomGeo' node ----
                EnvRoomBuilder.BuildCellarRoom(t);

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

        // ================================================================== FOREST
        // FX shell for the night forest (prefab file name Env_Swamp.prefab is a
        // runtime contract and does not change — only the content did, see
        // BuildEnvironmentRooms.BuildForestRoom): star dome + real catalogue
        // stars + shooting stars + wisps + two mist layers between the tree rows.
        private static void BuildForest()
        {
            var root = new GameObject("Env_Swamp");
            try
            {
                var t = root.transform;

                // ---- sky: photo dome + real Yale-catalogue stars (children) ----
                AddNightSky(t);

                // ---- ground mist, layer 1: drifting between the near trunks ----
                var fog = NewPS(t, "GroundFog", new Vector3(0, 0.45f, 0), new Vector3(-90, 0, 0), Mat("FX_Fog.mat"));
                var fm = fog.main;
                fm.simulationSpace = ParticleSystemSimulationSpace.World;
                fm.duration = 40f;
                fm.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
                fm.startSpeed = 0f;
                fm.startSize = new ParticleSystem.MinMaxCurve(6f, 11f); // bigger + dimmer = softer overlap
                fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                // cold and thin: mist BETWEEN the trunks, so the first row of
                // trees is sharp and the second is already half-dissolved
                fm.startColor = new Color(0.20f, 0.25f, 0.33f, 0.045f);
                fm.maxParticles = 34;
                var fe = fog.emission; fe.rateOverTime = 1.7f;
                var fsh = fog.shape; fsh.enabled = true; fsh.shapeType = ParticleSystemShapeType.Donut;
                fsh.radius = 8.0f; fsh.donutRadius = 3.8f; fsh.radiusThickness = 1f;
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

                // ---- ground mist, layer 2: a second, larger donut over the
                // 14–24 m band. This is the layer that makes the wood have DEPTH:
                // it sits between the second and third rows of trunks, so the far
                // trees are read through it and the ground disc's rim dissolves
                // long before it ends. Same HorizontalBillboard constraint. ----
                var farFog = NewPS(t, "GroundFogFar", new Vector3(0, 2.1f, 0), new Vector3(-90, 0, 0), Mat("FX_Fog.mat"));
                var ffm = farFog.main;
                ffm.simulationSpace = ParticleSystemSimulationSpace.World;
                ffm.duration = 40f;
                ffm.startLifetime = new ParticleSystem.MinMaxCurve(16f, 26f);
                ffm.startSpeed = 0f;
                ffm.startSize = new ParticleSystem.MinMaxCurve(15f, 26f);
                ffm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ffm.startColor = new Color(0.17f, 0.21f, 0.29f, 0.055f);
                ffm.maxParticles = 22;
                var ffe = farFog.emission; ffe.rateOverTime = 0.95f;
                var ffsh = farFog.shape; ffsh.enabled = true; ffsh.shapeType = ParticleSystemShapeType.Donut;
                ffsh.radius = 18f; ffsh.donutRadius = 5.5f; ffsh.radiusThickness = 1f;
                var ffv = farFog.velocityOverLifetime; ffv.enabled = true;
                ffv.space = ParticleSystemSimulationSpace.World;
                ffv.x = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
                ffv.z = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
                var ffcol = farFog.colorOverLifetime; ffcol.enabled = true;
                ffcol.color = new ParticleSystem.MinMaxGradient(Grad(
                    (0f, new Color(1, 1, 1, 0f)), (0.18f, new Color(1, 1, 1, 1f)),
                    (0.75f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));
                var ffr = farFog.GetComponent<ParticleSystemRenderer>();
                ffr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                ffr.maxParticleSize = 2.5f;
                ffr.sortMode = ParticleSystemSortMode.Distance;

                // ---- will-o'-the-wisps: two slow swarms deep BETWEEN the trunks
                // (node name 'Fireflies' is the runtime contract). Not in the
                // clearing: the point is that something is moving out there. ----
                FireflyPS(t, new Vector3(-5.0f, 0.75f, -6.6f));
                FireflyPS(t, new Vector3(7.6f, 0.95f, 5.2f));

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

                // ---- room interior: the night forest and its clearing, under
                // 'RoomGeo' (see BuildEnvironmentRooms.BuildForestRoom) ----
                EnvRoomBuilder.BuildForestRoom(t);

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
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); // bokeh sprite is softer => a touch larger
            // wisp, not firefly: cold marsh-light green, sparse and slow
            m.startColor = new Color(0.40f, 0.80f, 0.50f, 1f);
            m.maxParticles = 14;
            var e = ps.emission; e.rateOverTime = 1.2f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 3.1f;
            var n = ps.noise; n.enabled = true; n.strength = 0.30f; n.frequency = 0.22f; n.scrollSpeed = 0.10f;
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

// ===========================================================================
// NIGHT-SKY PROCESSING SCRIPT (verbatim, reproducible) — run with python3 +
// numpy + opencv (pip install opencv-python-headless numpy) on the CC0 source
// HDR named in the NIGHT SKY comment above:
//   python3 process_sky.py rogland_clear_night_16k.hdr Env_NightSky.png 8192 2560
// ---------------------------------------------------------------------------
// #!/usr/bin/env python3
// """GloomhavenVR night-sky panorama processing.
//
// Source: Poly Haven 'Rogland Clear Night' (CC0), equirectangular .hdr (linear).
// Output: sky-band texture (elevation EL_MIN..90 deg), sRGB PNG with:
//   RGB = tone-mapped night sky (terrain replaced by horizon haze)
//   A   = star-core twinkle mask
// Run: venv/bin/python process_sky.py <input.hdr> <out.png> <outW> <outH> [--preview]
// """
// import sys, numpy as np, cv2
//
// EL_MIN = -20.0     # band bottom (deg)
// EL_MAX = 90.0
// SKY_BG = 0.30      # source luminance of the empty sky background
// BG_TARGET = 0.055  # target linear luminance for the sky background (pre-boost)
// CONTRAST = 1.6     # log-space contrast around the background pivot
// KNEE = 0.75        # highlight soft-clip knee
// SAT = 1.12         # slight saturation recovery (night photos are flat)
//
// def lum(x):
//     return 0.2126*x[...,0] + 0.7152*x[...,1] + 0.0722*x[...,2]
//
// def srgb_encode(x):
//     x = np.clip(x, 0.0, 1.0)
//     return np.where(x <= 0.0031308, x*12.92, 1.055*np.power(x, 1/2.4) - 0.055)
//
// def terrain_profile(img):
//     """Per-column terrain-top elevation (deg, >=0) from silhouette luminance."""
//     H, W, _ = img.shape
//     L = lum(img)
//     horizon = H // 2
//     scan_top = int(H*0.25)              # +45 deg — no terrain higher than that
//     sky_mask = L[scan_top:horizon] > 0.16   # True = sky
//     # first row (from the top) where the next 12 rows are all terrain
//     terr = ~sky_mask
//     run = np.zeros_like(terr[0], dtype=np.int32)
//     top_row = np.full(W, horizon, dtype=np.int32)
//     found = np.zeros(W, dtype=bool)
//     consec = np.zeros(W, dtype=np.int32)
//     for r in range(terr.shape[0]):
//         consec = np.where(terr[r], consec+1, 0)
//         newly = (~found) & (consec >= 12)
//         top_row[newly] = scan_top + r - 11
//         found |= newly
//     elev = (horizon - top_row) / (H/2) * 90.0   # deg above horizon
//     elev[~found] = 0.0
//     return elev
//
// def smooth_wrap(x, sigma_px):
//     k = int(sigma_px*3)*2+1
//     xw = np.concatenate([x[-k:], x, x[:k]])
//     xs = cv2.GaussianBlur(xw.reshape(1,-1).astype(np.float32), (k,1), sigma_px).ravel()
//     return xs[k:-k]
//
// def main():
//     src, out, outW, outH = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
//     img = cv2.imread(src, cv2.IMREAD_UNCHANGED)[:, :, ::-1].astype(np.float32)
//     H, W, _ = img.shape
//     print(f'source {W}x{H}')
//
//     # ---------------- terrain silhouette -> haze top profile ----------------
//     prof = terrain_profile(img)                     # deg, at source W
//     prof_s = smooth_wrap(prof, W/256) + 2.5         # smoothed + margin (deg)
//     prof_s = np.maximum(prof_s, 5.0)                # minimum haze height
//     print(f'terrain profile: max {prof.max():.1f} deg, haze top max {prof_s.max():.1f} deg')
//
//     # ---------------- resample to output band (supersample 2x) ----------------
//     ssW, ssH = outW*2, outH*2
//     el = EL_MIN + (EL_MAX-EL_MIN) * ((ssH-0.5-np.arange(ssH))+0.5)/ssH  # row -> elevation
//     # source row for elevation: srcRow = (90-el)/180*H
//     map_y = ((90.0-el)/180.0*H - 0.5).astype(np.float32)
//     map_x = ((np.arange(ssW)+0.5)/ssW*W - 0.5).astype(np.float32)
//     mx, my = np.meshgrid(map_x, map_y)
//     band = cv2.remap(img[:,:,::-1], mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)[:,:,::-1]
//     band = np.ascontiguousarray(band)
//
//     # ---------------- tone map ----------------
//     x = band / SKY_BG                                # sky background == 1.0
//     L0 = np.maximum(lum(x), 1e-6)
//     Lc = np.power(L0, CONTRAST)                      # contrast around pivot 1.0
//     y = x * (Lc/L0)[...,None] * BG_TARGET
//     # highlight soft clip (Reinhard-ish shoulder above the knee)
//     Ly = np.maximum(lum(y), 1e-9)
//     Ls = np.where(Ly > KNEE, KNEE + (1.0-KNEE)*(Ly-KNEE)/(Ly-KNEE+ (1.0-KNEE)), Ly)
//     y *= (Ls/Ly)[...,None]
//     # saturation
//     Lg = lum(y)[...,None]
//     y = np.clip(Lg + (y-Lg)*SAT, 0.0, None)
//
//     # ---------------- haze band over terrain ----------------
//     elev_col = el[:,None]                            # ssH x 1
//     prof_ss = np.interp((np.arange(ssW)+0.5)/ssW, (np.arange(len(prof_s))+0.5)/len(prof_s), prof_s)
//     # haze factor: 0 above (profile+9deg), 1 below profile
//     t = np.clip((prof_ss[None,:]+9.0 - elev_col)/9.0, 0.0, 1.0)
//     t = t*t*(3-2*t)
//     # haze colour: per-column MEDIAN of the sky just above the haze top (median
//     # rejects stars), then wrap-smoothed hard so no column-rate detail survives
//     r0 = np.clip(((EL_MAX-(prof_ss+16.0))/(EL_MAX-EL_MIN)*ssH).astype(int), 0, ssH-1)
//     r1 = np.clip(((EL_MAX-(prof_ss+7.0))/(EL_MAX-EL_MIN)*ssH).astype(int), 0, ssH-1)
//     sky_ref = np.empty((ssW,3), np.float32)
//     band_h = int(np.max(r1-r0))+1
//     rows = (r0[None,:] + np.arange(band_h)[:,None]).clip(0, ssH-1)   # band_h x ssW
//     cols = np.broadcast_to(np.arange(ssW), (band_h, ssW))
//     sky_ref = np.median(y[rows, cols], axis=0)       # ssW x 3
//     for c in range(3):
//         sky_ref[:,c] = smooth_wrap(sky_ref[:,c], ssW/64)
//     # cap over-bright columns (milky-way limb) so the mist never glows
//     refL = 0.2126*sky_ref[:,0]+0.7152*sky_ref[:,1]+0.0722*sky_ref[:,2]
//     cap = 1.25*np.median(refL)
//     sky_ref *= np.minimum(1.0, cap/np.maximum(refL,1e-6))[:,None]
//     # cool + desaturate the mist slightly (starlit fog, not glowing smoke)
//     refL = (0.2126*sky_ref[:,0]+0.7152*sky_ref[:,1]+0.0722*sky_ref[:,2])[:,None]
//     sky_ref = (0.65*sky_ref + 0.35*refL) * np.array([0.88,0.95,1.06], np.float32)
//     # vertical shading inside the haze: dimmer toward the horizon (mist, not a wall)
//     vshade = np.clip((elev_col - 0.0) / np.maximum(prof_ss[None,:]+9.0, 1e-3), 0.0, 1.0)
//     vshade = 0.30 + 0.38*vshade
//     haze = sky_ref[None,:,:] * vshade[...,None]
//     y = y*(1-t[...,None]) + haze*t[...,None]
//     # fade everything to black below the horizon (dome shows the water rim there)
//     fade = np.clip((elev_col - EL_MIN) / (0.0 - EL_MIN), 0.0, 1.0)  # 0 at band bottom, 1 at horizon
//     fade = fade*fade*(3-2*fade)
//     y *= fade[...,None]
//
//     # ---------------- star-core twinkle mask ----------------
//     Ly = lum(y).astype(np.float32)
//     bgL = cv2.GaussianBlur(Ly, (0,0), 6.0)
//     stars = np.clip((Ly - bgL - 0.10)*4.0, 0.0, 1.0)
//     stars *= (elev_col > prof_ss[None,:]+4.0)        # no twinkle in the haze
//     alpha = stars
//
//     # ---------------- downsample 2x, encode, dither ----------------
//     rgba = np.dstack([y, alpha[...,None]])
//     rgba = cv2.resize(rgba, (outW, outH), interpolation=cv2.INTER_AREA)
//     outp = np.empty((outH, outW, 4), np.float32)
//     outp[...,:3] = srgb_encode(rgba[...,:3])
//     outp[...,3] = np.clip(rgba[...,3], 0, 1)
//     rng = np.random.default_rng(4404)
//     dith = (rng.random((outH,outW,1), np.float32) - rng.random((outH,outW,1), np.float32))  # TPDF +-1 LSB
//     q = np.clip(np.round(outp*255.0 + dith), 0, 255).astype(np.uint8)
//     cv2.imwrite(out, q[:,:,[2,1,0,3]])
//     print('wrote', out, q.shape)
//
// if __name__ == '__main__':
//     main()
