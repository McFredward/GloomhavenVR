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
//   Assets/Bundle/Environments/Env_Swamp.prefab  — night-sky FX shell: the
//       FULLY DYNAMIC star dome (user ruling, ModBuild 133: "entferne das
//       statische Bild und gehe voll zu einem dynamischen Sternenhimmel
//       (ausschließlich)") — see NIGHT SKY below and the two sky shaders;
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
        // ModBuild 134: this is ALSO the anchor of the sky now — the moon sprite
        // in EnvStars and the star-occlusion disc in EnvStarPoints both read it,
        // which is why the moon is the one celestial object that does NOT ride
        // the sky's rotation. Move it and the shafts, the canopy tear, the rim
        // light and the moon in the sky all move together, by construction.
        public static readonly Vector3 MoonDir = new Vector3(0.49262f, 0.64279f, 0.58686f).normalized;

        // Moon sprite half-extent in gnomonic tan units (EnvStars/_MoonExtent).
        // Env_Moon.png paints the disc out to r=0.22 of the sprite's 0.5 half-size
        // (MakeMoon), and the sprite spans +-_MoonExtent in tan units, so the
        // DISC's angular radius is atan(0.44 * _MoonExtent) — everything outside
        // that is halo. Both the sprite draw and the star cull derive from here,
        // so they can never disagree about where the moon's edge is.
        private const float MoonExtent = 0.055f;
        private static float MoonDiscRad => Mathf.Atan(0.44f * MoonExtent);   // ~1.4 deg

        // ------------------------------------------------------------ star sky
        // Yale Bright Star Catalogue (public domain) -> real star point sprites.
        // See Assets/Editor/star_catalogue.py for the source and the parse; the
        // CSV lives in Assets/Editor (editor-only: never in the player build or
        // the bundle), only the derived MESH ships.
        private const string StarCsv = "Assets/Editor/bsc5_stars.csv";
        // ModBuild 134: was 6.0 (5080 stars). With the photographic backdrop
        // deleted the sky between the catalogue stars read empty, so the cut goes
        // to the catalogue's own completeness limit — every star the naked eye
        // can reach, 8404 of them, +65% for 3324 * 2 = 6648 extra triangles.
        // Everything fainter than this is the procedural dust in EnvStars.
        private const float StarMagLimit = 6.5f;
        private const float StarRadius = 44f;      // just inside the 45 m dome
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
        // USER RULING, ModBuild 133: "Die Skybox sind immer noch die fixen Sterne
        // UND ein Mond der dahinter ist, entferne das statische Bild und gehe voll
        // zu einem dynamischen Sternenhimmel (ausschließlich)."
        //
        // The 8192x2560 processed photograph (Poly Haven 'Rogland Clear Night',
        // ~21 MB of BC7 in the bundle), its import step and the python pipeline
        // that baked it are DELETED. Nothing in this sky is a picture any more.
        // What the sky is made of now, in one rotating celestial frame:
        //   * 8404 Yale Bright Star Catalogue stars at their true RA/Dec, with
        //     catalogue magnitudes and B-V colours (BuildStarField below,
        //     EnvStarPoints.shader) — the backbone.
        //   * a procedural MILKY WAY evaluated in real galactic coordinates
        //     (GalacticBasis below, EnvStars.shader) so the band lies where it
        //     belongs among those constellations and turns with them.
        //   * procedural sub-visual STAR DUST in the same frame, denser inside
        //     the band, which is physically what the Milky Way is.
        //   * the moon: a sprite fixed in the ROOM frame (it anchors the baked
        //     moonlight shafts and rim light) that occludes the stars behind it.
        // Extinction, per-star scintillation, rise/set and the wrapped _Time
        // clock are unchanged. Still script-free: all of it is _Time in shaders.
        //
        // ------------------------------------------------------------ GALACTIC FRAME
        // Derived, not eyeballed. IAU (Blaauw et al. 1960) galactic pole and
        // centre, in J2000 equatorial coordinates:
        //     north galactic pole  RA 192.85948 deg, Dec +27.12825 deg
        //     galactic centre      RA 266.40510 deg, Dec -28.936175 deg
        //     position angle of the NCP  l = 122.93192 deg
        // GalacticBasis() builds the orthonormal triple (x = centre, y = z x x,
        // z = pole) from the first two, ASSERTS that they are perpendicular to
        // 1e-3 rad (they are two independently published numbers describing one
        // rotation — if the assert fired, one of them would be wrong), and
        // ASSERTS that the recovered galactic longitude of the celestial pole
        // comes back as 122.93192 deg. Then it maps that triple through the very
        // same equatorial -> object basis BuildStarField uses for the stars, so
        // band and constellations cannot drift apart by construction.
        //
        // Historic note — the photographic era (kept because the style ruling it
        // came from is still on the record, and because a future round asking for
        // a photo again should not have to re-derive the pipeline):
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
        //   The full processing script used to be embedded at the end of this
        //   file; it went with the texture. Recover it from git history
        //   (BuildEnvironments.cs before ModBuild 134) if a photo is ever wanted
        //   again — but note it is a STATIC sky and was rejected as such.

        // ================================================================ textures
        // All sprites are procedural and seeded => deterministic. Nothing here is
        // a photograph any more.
        private static void GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);

            WritePng(TexDir + "/Env_Spark.png", MakeSpark(64), 64, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Glow.png", MakeGlow(128), 128, 128, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Streak.png", MakeStreak(256, 64), 256, 64, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), 256, 256, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(512), 512, 512, sRGB: true, clamp: true);
            // Tiling noise, in BOTH axes (torus blend), so no octave can ever
            // show a seam. Used twice by EnvStars: as the horizon haze veil and
            // — sampled at integer multiples of a full galactic turn — as the
            // Milky Way's mottling and dust lanes.
            WritePng(TexDir + "/Env_Haze.png", MakeHaze(256), 256, 256, sRGB: false, clamp: false,
                comp: TextureImporterCompression.Compressed, alphaDilate: false);
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
            var mareCol = new Vector3(0.56f, 0.585f, 0.61f); // grey-teal seas (deeper: the maria are the only face detail left at 2.8 deg)
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
                    float mare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.46f, 0.72f, m)) * 0.58f;
                    float c1 = Mathf.Exp(-((u - 0.44f) * (u - 0.44f) + (v - 0.57f) * (v - 0.57f)) / 0.0011f);
                    float c2 = Mathf.Exp(-((u - 0.56f) * (u - 0.56f) + (v - 0.455f) * (v - 0.455f)) / 0.0006f);
                    var discRgb = Vector3.Lerp(ivory, mareCol, Mathf.Clamp01(mare + 0.14f * c1 + 0.11f * c2))
                                  * (limb * shade);

                    // ---- halo paint: warm inner veil cooling outward, uneven rim ----
                    // ModBuild 134 rebuild. The old profile had a slow second lobe
                    // (exp(-xr*3.8)) and painted it at FULL brightness, so once the
                    // moon shrank to its real-ish 2.8 deg the sprite read as a grey
                    // donut with a golden ring welded to the limb. Now: one monotone
                    // falloff, and the halo colour is deliberately DIMMER than the
                    // disc surface (~0.85 of it), so the limb has nothing to step up
                    // to and the disc simply melts outward.
                    float xr = Mathf.Max(0f, r - Rw);
                    float halo = 0.62f * Mathf.Exp(-xr * 38f)
                               + 0.13f * Mathf.Exp(-xr * 11f)
                               + 0.028f * Mathf.Exp(-xr * 3.2f);
                    halo *= 0.86f + 0.14f * Noise3(Mathf.Cos(ang) * 3.1f, Mathf.Sin(ang) * 3.1f, 1.9f, 2233);
                    var haloRgb = Vector3.Lerp(new Vector3(0.88f, 0.83f, 0.70f),
                                               new Vector3(0.60f, 0.67f, 0.80f), Mathf.Clamp01(xr * 4.0f));

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
            // The UVs are vestigial now: with the photo gone EnvStars derives
            // EVERYTHING from the object-space vertex direction, which is also the
            // only space in which it cannot slide against the star geometry.
            SaveMesh(MeshDir + "/Env_Dome.asset", BuildSphere(64, 32, inward: true));
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
        /// <summary>The equatorial -> object mapping this whole sky is built on.
        /// A celestial unit vector is given by its standard components
        /// (cos dec cos RA, cos dec sin RA, sin dec); this turns those into a
        /// direction in the star mesh's object frame at LST 0.
        /// Derivation: the star loop below places a star at hour angle H = -RA as
        /// sin(dec) P + cos(dec) cos(H) M + cos(dec) sin(H) W, and cos(-RA) = cos RA,
        /// sin(-RA) = -sin RA — so the basis is exactly (M, -W, P).</summary>
        private static Vector3 EquatorialToObject(Vector3 eq)
        {
            float lat = ObserverLatDeg * Mathf.Deg2Rad;
            var P = new Vector3(0f, Mathf.Sin(lat), Mathf.Cos(lat));   // celestial north pole
            var M = new Vector3(0f, Mathf.Cos(lat), -Mathf.Sin(lat));  // RA 0, Dec 0 at LST 0 (due south)
            var W = new Vector3(-1f, 0f, 0f);                          // due west
            return M * eq.x + (-W) * eq.y + P * eq.z;
        }

        private static Vector3 RaDecToEq(double raDeg, double decDeg)
        {
            double ra = raDeg * Math.PI / 180.0, dc = decDeg * Math.PI / 180.0;
            return new Vector3((float)(Math.Cos(dc) * Math.Cos(ra)),
                               (float)(Math.Cos(dc) * Math.Sin(ra)),
                               (float)Math.Sin(dc));
        }

        /// <summary>Galactic basis (x = galactic centre, y = z x x, z = north
        /// galactic pole), expressed in the star mesh's OBJECT frame at LST 0 —
        /// which is the frame EnvStars un-rotates a fragment direction back into.
        /// See GALACTIC FRAME in the NIGHT SKY comment; both self-checks below are
        /// hard failures, because a silently wrong matrix would put the Milky Way
        /// through the wrong constellations and nobody would notice for months.</summary>
        private static (Vector3 x, Vector3 y, Vector3 z) GalacticBasis()
        {
            // IAU 1958 galactic frame, J2000 equatorial coordinates.
            var zg = RaDecToEq(192.85948, 27.12825);      // north galactic pole
            var xg = RaDecToEq(266.40510, -28.936175);    // galactic centre, l=0 b=0

            float perp = Vector3.Dot(zg, xg);
            if (Mathf.Abs(perp) > 1e-3f)
                throw new Exception($"Galactic pole and centre are not perpendicular (dot={perp:E3}) — one of the IAU constants is wrong.");
            xg = (xg - zg * perp).normalized;             // orthogonalize the 1e-4 residue away
            // right-handed cross in EQUATORIAL component space (NOT Unity's
            // left-handed cross in object space: that would mirror the sense of
            // galactic longitude and put Cygnus where Carina belongs).
            var yg = new Vector3(zg.y * xg.z - zg.z * xg.y,
                                 zg.z * xg.x - zg.x * xg.z,
                                 zg.x * xg.y - zg.y * xg.x).normalized;

            // independent check: the galactic longitude of the CELESTIAL pole must
            // come back as the third IAU constant, 122.93192 deg.
            var ncp = new Vector3(0f, 0f, 1f);
            float lNcp = Mathf.Atan2(Vector3.Dot(yg, ncp), Vector3.Dot(xg, ncp)) * Mathf.Rad2Deg;
            if (lNcp < 0f) lNcp += 360f;
            if (Mathf.Abs(lNcp - 122.93192f) > 0.01f)
                throw new Exception($"Galactic basis wrong: l(NCP) = {lNcp:F5} deg, expected 122.93192 deg.");

            var ox = EquatorialToObject(xg).normalized;
            var oy = EquatorialToObject(yg).normalized;
            var oz = EquatorialToObject(zg).normalized;
            Debug.Log($"[GloomhavenVR][Env] Galactic frame OK: l(NCP)={lNcp:F5} deg, pole.centre dot={perp:E2}; "
                      + $"in object space centre={ox:F4} (alt {Mathf.Asin(ox.y) * Mathf.Rad2Deg:F1} deg at LST 0), NGP={oz:F4}.");
            return (ox, oy, oz);
        }

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
                // /2.6 (was /3.0): the cut moved half a magnitude fainter, and at
                // /3.0 the new mag-6.0..6.5 stars sat within 6% of each other —
                // a flat carpet of identical dots instead of a fading tail.
                float flux = Mathf.Pow(10f, -0.4f * (mag - StarMagLimit) / 2.6f);
                float bright = 0.048f * flux;
                float size = 0.026f + 0.0052f * Mathf.Max(0f, StarMagLimit - mag);
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

            // ---- the sky's continuous layer: gradient + Milky Way + dust + moon ----
            // Everything below is procedural and lives in ONE celestial frame with
            // the catalogue stars (see NIGHT SKY). Numbers a future round tunes:
            //   _MwGain     Milky Way peak, LINEAR. The band peaks near 1.7 of its
            //               own profile, so peak luminance ~ 1.7 * _MwGain: at
            //               0.016 that is ~0.027, about 2-3x the zenith sky —
            //               which is the real contrast ratio, and the ceiling
            //               above which it reads as a "bright smear".
            //   _DustGain   brightest sub-visual dot; must stay well under the
            //               faintest catalogue star (0.048 * _Gain).
            //   _Extinct    mag/airmass. THE horizon-darkness knob: it is what
            //               kills the lifted band above the ridge.
            const float rotSpeed = 2f * Mathf.PI / 2880f;
            const float extinct = 0.24f;
            var lat = ObserverLatDeg * Mathf.Deg2Rad;
            var pole = new Vector4(0f, Mathf.Sin(lat), Mathf.Cos(lat), 0f);
            var (galX, galY, galZ) = GalacticBasis();

            var stars = LoadOrNewMat(MatDir + "/Swamp_StarDome.mat", "GloomhavenVR/EnvStars");
            // zenith slightly blue, horizon nearly black — the INVERSE of a real
            // light-polluted sky, on purpose (user, ModBuild 133: it must be so
            // dark behind the trees that you would not dare walk there).
            stars.SetColor("_TopCol", new Color(0.0092f, 0.0120f, 0.0212f));
            stars.SetColor("_HorizonCol", new Color(0.0030f, 0.0039f, 0.0068f));
            stars.SetVector("_Pole", pole);
            stars.SetFloat("_RotSpeed", rotSpeed);
            stars.SetVector("_GalX", galX);
            stars.SetVector("_GalY", galY);
            stars.SetVector("_GalZ", galZ);
            stars.SetFloat("_MwGain", 0.040f);
            stars.SetFloat("_MwThin", 5.0f);
            stars.SetFloat("_MwThick", 17.0f);
            stars.SetFloat("_MwBulge", 1.35f);
            stars.SetFloat("_MwDust", 0.80f);
            stars.SetColor("_MwWarm", new Color(1.00f, 0.86f, 0.70f));
            stars.SetColor("_MwCool", new Color(0.78f, 0.85f, 1.00f));
            stars.SetFloat("_DustGain", 0.085f);
            stars.SetFloat("_DustDens", 0.20f);
            stars.SetFloat("_DustScale", 330f);
            stars.SetFloat("_DustCore", 24f);
            stars.SetFloat("_Extinct", extinct);
            stars.SetTexture("_HazeTex", T("Env_Haze.png"));
            stars.SetColor("_HazeCol", new Color(0.010f, 0.013f, 0.019f));
            stars.SetFloat("_HazeAmt", 0.40f);
            stars.SetTexture("_MoonTex", T("Env_Moon.png"));
            stars.SetVector("_MoonDir", MoonDir);
            stars.SetColor("_MoonCol", new Color(1f, 0.98f, 0.92f));
            stars.SetFloat("_MoonExtent", MoonExtent);

            // ---- real catalogue stars (see BuildStarField / EnvStarPoints) ----
            var pts = LoadOrNewMat(MatDir + "/Sky_StarPoints.mat", "GloomhavenVR/EnvStarPoints");
            pts.SetVector("_Pole", pole);
            // 2*pi / 2880 s = 0.125 deg/s ~= 30x sidereal. Real 15 deg/h is
            // imperceptible; games run 20-70x (Skyrim 20x, Minecraft 72x). Above
            // ~100x a rotating sky starts to induce vection in VR.
            pts.SetFloat("_RotSpeed", rotSpeed);
            pts.SetFloat("_Gain", 2.6f);
            pts.SetFloat("_TwinkleAmp", 0.80f);
            pts.SetFloat("_TwinkleSpeed", 1.9f);
            pts.SetFloat("_Core", 34f);
            pts.SetFloat("_Extinct", extinct);
            // the moon OCCLUDES: same direction, same disc, so no star can shine
            // through it (user finding, ModBuild 133 — "ein Mond der dahinter ist")
            pts.SetVector("_MoonDir", MoonDir);
            pts.SetFloat("_MoonCos", Mathf.Cos(MoonDiscRad * 1.04f));
            Debug.Log($"[GloomhavenVR][Env] Moon: dir={MoonDir:F4} (az {Mathf.Atan2(MoonDir.x, MoonDir.z) * Mathf.Rad2Deg:F1} deg, "
                      + $"alt {Mathf.Asin(MoonDir.y) * Mathf.Rad2Deg:F1} deg), disc radius {MoonDiscRad * Mathf.Rad2Deg:F2} deg, "
                      + $"stars culled inside {Mathf.Acos(Mathf.Cos(MoonDiscRad * 1.04f)) * Mathf.Rad2Deg:F2} deg.");

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
        // inward-facing sphere carrying the sky's continuous layer (gradient,
        // Milky Way, star dust, moon — all procedural, see NIGHT SKY), plus the
        // catalogue star geometry as its child.
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

        // ------------------------------------------------------------ PLAY SPACE
        // CONTRACT with src/ (ModBuild 134): every environment prefab carries an
        // empty child named exactly 'PlaySpace' whose localScale.x is the
        // AUTHORED DIAMETER, in authored metres, of the usable open area — the
        // forest clearing, the cellar's free floor. The runtime normalizes the
        // whole prefab so that this diameter becomes ~4.5x the game board's world
        // extent, i.e. the board floats as a small diorama in the middle of it and
        // everything beyond (tree bands, walls) lands far outside.
        //
        // Consequences the author owns, enforced at build time by
        // EnvRoomBuilder.AssertPlaySpaceClear: NOTHING may intrude into that disc
        // — no prop, root, trunk, mist emitter. The board and the players' hands
        // live there. The environment OUTSIDE it may be any size; it no longer
        // sets the scale of anything.
        //
        // No renderer, no collider, no script: it is pure metadata, and its
        // localScale is the only thing about it that means anything (y and z are
        // set to the same value so a stray uniform read cannot be surprised).
        private static void AddPlaySpace(Transform parent, float diameter)
        {
            var go = new GameObject("PlaySpace");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * diameter;
            Debug.Log($"[GloomhavenVR][Env] PlaySpace on {parent.name}: authored diameter {diameter:F2} m.");
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
                AddPlaySpace(t, EnvRoomBuilder.CellarPlaySpaceDia);

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
                AddPlaySpace(t, EnvRoomBuilder.ForestPlaySpaceDia);

                // ---- sky: procedural celestial dome + real Yale-catalogue stars ----
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
                // Cold and thin: mist BETWEEN the trunks, so the first row of
                // trees is sharp and the second is already half-dissolved.
                // ModBuild 134: darker and thinner (was 0.20,0.25,0.33 @ 0.045) —
                // an alpha-blended lit puff over black IS a raised floor, and the
                // wood beyond the clearing has to read as black, not blue-grey.
                fm.startColor = new Color(0.12f, 0.15f, 0.20f, 0.026f);
                fm.maxParticles = 34;
                var fe = fog.emission; fe.rateOverTime = 1.7f;
                var fsh = fog.shape; fsh.enabled = true; fsh.shapeType = ParticleSystemShapeType.Donut;
                // inner rim 8.4-3.6 = 4.8 m, i.e. OUTSIDE the 9.0 m play-space
                // disc (radius 4.5): no mist emitter may intrude on the board.
                fsh.radius = 8.4f; fsh.donutRadius = 3.6f; fsh.radiusThickness = 1f;
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
                // ModBuild 134: this layer WAS the "blue-grey haze floor" behind
                // the trees (0.17,0.21,0.29 @ 0.055). It stays, because without it
                // the far wood has no depth at all, but at a third of the density
                // and much darker: it should suggest, never illuminate.
                ffm.startColor = new Color(0.070f, 0.086f, 0.112f, 0.012f);
                ffm.maxParticles = 18;
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
