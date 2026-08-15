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
// ELEMENT ART (ModBuild 141) — the six element infusions now reach the
// environment. The sensing half is src/GloomhavenVR/Core/ElementMood.cs, which
// publishes _GhvrElemA/_GhvrElemB; the art half is:
//   * EnvElement.cginc — the channel, the Light/Dark SPLIT and the periphery
//     ramp, quoted once for every shader that reads them;
//   * EnvRoom / EnvFlame / EnvGlow / EnvPuddle / EnvDrip / EnvStars /
//     EnvStarPoints — frost, the fire rim, the moss green, the flames' flare and
//     lean, the glazed puddle, the sky;
//   * the two particle shaders, which gained a GATE (an emitter that exists for
//     one element collapses its quads while that element is down) and a
//     MODULATION (the ground fog thickens under Dark, the fireflies turn to
//     sparks under Fire) — see EnvParticleAdd.shader;
//   * EnvRoomBuilder.AddElementFX — six gated emitters, hung under RoomGeo
//     because the runtime's shell splitter would otherwise put them on the SKY
//     branch. The bake log prints every one of them and what it costs standing.
// Every effect is `element * _GhvrElemB.z`, so one uniform switches the whole
// feature off, and every shader skips its element block entirely when nothing is
// up — which is why the no-element render is bit-identical to the previous
// round's, not merely close to it.
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
        // Env_Moon.png paints the disc out to MoonSpriteDiscR of the sprite's 0.5 half-size
        // (MakeMoon), and the sprite spans +-_MoonExtent in tan units, so the
        // DISC's angular radius is atan(2 * MoonSpriteDiscR * _MoonExtent) —
        // everything outside that is halo. The sprite draw, the star cull and
        // (ModBuild 143) the eclipse's umbra all derive from here, so they can
        // never disagree about where the moon's edge is.
        private const float MoonExtent = 0.055f;
        // ...and this is the ONE place the 0.22 is typed. It used to be typed
        // twice — once as MakeMoon's R, once folded into the 0.44 below — and
        // the eclipse needs it a third time, in the shader, which is the point
        // at which a repeated constant stops being harmless.
        private const float MoonSpriteDiscR = 0.22f;
        private static float MoonDiscRad => Mathf.Atan(2f * MoonSpriteDiscR * MoonExtent);  // ~1.4 deg

        // ---------------------------------------------------------- MOON PHASE
        // MIRRORS EnvElement.cginc's GHVR_ECL_* / GHVR_MOON_SWELL. These live in
        // the shader because GhvrMoonLight() must be callable from any room
        // shader without a uniform to set (that is the contract); the copies
        // here exist only so the bake LOG can state the eclipse's geometry and
        // size, which is the only way to check them without opening Unity.
        // CHANGE ONE, CHANGE BOTH — the log below prints the numbers it derives,
        // so a disagreement shows up as a log that does not match the picture.
        //
        // MOON HELD (ModBuild 144). EclipsePeriod, EclipseMiss and EclipseTrack
        // are DELETED with the transit they described. The user kept the blood
        // moon and threw out the crossing, so there is no period to state, no
        // contact times to solve for and no divisibility against the sky's own
        // 2880 s wrap to check: the shadow is where it is, and Dark's own ramp
        // is the only thing that changes.
        private const float EclipseUmbra = 2.60f;  // umbra radius, in moon radii
        private const float EclipseCx = 0.862f;    // the HELD umbra centre,
        private const float EclipseCy = -0.759f;   // in moon radii
        private const float EclipseFloor = 0.05f;  // moonlight left under full Dark
        private const float MoonSwell = 0.34f;     // disc radius gain at full Light

        /// <summary>The C# mirror of EnvElement.cginc's GhvrMoonLight(), for the bake log.
        /// No time argument any more — see MOON HELD above.</summary>
        private static float MoonLightAt(float light, float dark)
        {
            float d = Mathf.Sqrt(EclipseCx * EclipseCx + EclipseCy * EclipseCy);
            // HLSL smoothstep(edge0, edge1, x) written out — Mathf.SmoothStep is
            // a different function (it interpolates BETWEEN its first two
            // arguments), and using it here would quietly mis-state the log.
            float s = Mathf.Clamp01((d - (EclipseUmbra - 1f)) * 0.5f);
            float cov = 1f - s * s * (3f - 2f * s);
            return (1f + MoonSwell * light) * (1f - cov * dark * (1f - EclipseFloor));
        }

        /// <summary>The held eclipse and the swell, in numbers, in the bake log. A still
        /// preview shows the shadow ON the disc but cannot show that it is wholly inside
        /// the umbra, that the terminator clears the limb, or how far the room's light
        /// has actually fallen — and nothing in the bundle carries any of it at runtime.
        ///
        /// <para>The two CLEARANCES below are the acceptance test for the composition and
        /// the reason they are computed rather than asserted: move the centre and this
        /// log says immediately whether the disc is still wholly umbral. A negative
        /// number here is a terminator frozen across the moon's face.</para></summary>
        private static void LogMoonPhase()
        {
            float discDeg = MoonDiscRad * Mathf.Rad2Deg;
            float off = Mathf.Sqrt(EclipseCx * EclipseCx + EclipseCy * EclipseCy);
            const float eclEdge = 0.085f;                  // GHVR_ECL_EDGE
            float covClear = (EclipseUmbra - 1f) - off;             // >= 0 => coverage is 1
            float termClear = (EclipseUmbra - 1f - eclEdge) - off;  // >= 0 => no terminator
            // the Danjon ramp across the face: how far the nearest and the farthest
            // point of the disc lie from the shadow's core, in umbra radii
            float ddNear = Mathf.Abs(off - 1f), ddFar = off + 1f;
            float CoreAt(float dd)
            {
                float s = Mathf.Clamp01(dd / EclipseUmbra);
                return 1f - s * s * (3f - 2f * s);
            }
            // the covered fraction the LIGHT is driven by — GhvrEclipseCover, written out
            float cs = Mathf.Clamp01((off - (EclipseUmbra - 1f)) * 0.5f);
            float cover = 1f - cs * cs * (3f - 2f * cs);

            Debug.Log("[GloomhavenVR][Env] Moon eclipse (Dark), HELD — these MIRROR "
                      + "EnvElement.cginc's GHVR_ECL_* constants; if they disagree, the shader "
                      + "wins and this log is a lie.\n"
                      + "   STATIC: no period, no transit, no phase. The umbra sits at one place "
                      + "and Dark's own ramp fades the blood moon in and out (user, ModBuild 143: "
                      + "\"lass einen Blutmond statisch solange das aktiv ist\").\n"
                      + $"   umbra {EclipseUmbra:F2} moon radii = {EclipseUmbra * discDeg:F2} deg "
                      + $"(the Earth's real shadow at the moon's distance)\n"
                      + $"   held centre ({EclipseCx:F3},{EclipseCy:F3}) R, offset {off:F3} R\n"
                      + $"   coverage {cover:F3}"
                      + $"{(covClear >= 0f ? " TOTAL" : " PARTIAL — THE DISC IS NOT WHOLLY COVERED")}, "
                      + $"clearance {covClear:F3} R; terminator clearance {termClear:F3} R"
                      + $"{(termClear >= 0f ? " (the soft edge never touches the limb)" : " *** THE TERMINATOR IS ON THE DISC ***")}\n"
                      + $"   Danjon ramp across the face: near limb {ddNear:F2} R from the core "
                      + $"(core weight {CoreAt(ddNear):F2}, grey-brown) -> far limb {ddFar:F2} R "
                      + $"(core weight {CoreAt(ddFar):F2}, bright copper)\n"
                      + $"   GhvrMoonLight() at full Dark {MoonLightAt(0f, 1f):F3} "
                      + $"(floor {EclipseFloor:F2}); x GhvrDirGain's 0.55 a room sees "
                      + $"{MoonLightAt(0f, 1f) * 0.55f:F4} of the authored moonlight — the shafts "
                      + "and the beam go out and the candles are what is left.");
            Debug.Log($"[GloomhavenVR][Env] Moon swell (Light): radius x{1f + MoonSwell:F2} — disc "
                      + $"{discDeg:F2} -> {Mathf.Atan(2f * MoonSpriteDiscR * MoonExtent * (1f + MoonSwell)) * Mathf.Rad2Deg:F2} deg, "
                      + $"star cull {Mathf.Acos(Mathf.Cos(MoonDiscRad * 1.04f)) * Mathf.Rad2Deg:F2} -> "
                      + $"{Mathf.Acos(1f - (1f - Mathf.Cos(MoonDiscRad * 1.04f)) * (1f + MoonSwell) * (1f + MoonSwell)) * Mathf.Rad2Deg:F2} deg. "
                      + $"GhvrMoonLight() {MoonLightAt(1f, 0f):F3}; with GhvrDirGain's 1.90 a room sees "
                      + $"{MoonLightAt(1f, 0f) * 1.90f:F2}x the authored moonlight, and the sprite itself "
                      + "is x GhvrSrcGain = 2.10 on top of that.\n"
                      + $"   Light AND Dark at once: GhvrMoonLight() {MoonLightAt(1f, 1f):F3} "
                      + "— a swollen moon still gets eaten.");
        }

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
            // THE DRAUGHT'S CARRIED MATTER. See MakeWisp for why the cellar's
            // wind could not go on using Env_Spark.
            WritePng(TexDir + "/Env_Wisp.png", MakeWisp(256, 32), 256, 32, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), 256, 256, sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(512), 512, 512, sRGB: true, clamp: true);
            // The ORB WEB itself is no longer drawn here — it is a real CC0
            // photoscanned opacity map now (Imported/Textures/cobweb_alb.png,
            // TextureCan others_0015; see Environments/License.md). User finding,
            // ModBuild 135: "Im Keller die Spinnwebe sehen sehr low-poly aus".
            // What is left procedural is the LOOSE STRANDS that hang off it,
            // because those want a specific shape no photo happens to contain.
            // mipCoverage MUST equal the web material's _Cutoff (see WritePng).
            WritePng(TexDir + "/Env_Strand.png", MakeStrand(192, 512), 192, 512, sRGB: true, clamp: true,
                mipCoverage: WebCutoff);
            // Tiling noise, in BOTH axes (torus blend), so no octave can ever
            // show a seam. Used twice by EnvStars: as the horizon haze veil and
            // — sampled at integer multiples of a full galactic turn — as the
            // Milky Way's mottling and dust lanes.
            WritePng(TexDir + "/Env_Haze.png", MakeHaze(256), 256, 256, sRGB: false, clamp: false,
                comp: TextureImporterCompression.Compressed, alphaDilate: false);
            // THE APPARITION ATLAS — the creepy easter eggs' actual likenesses.
            // UNCOMPRESSED, deliberately: see the channel packing block below.
            WritePng(TexDir + "/Env_Haunt.png", MakeHauntAtlas(),
                HauntTile * HauntAtlasCols, HauntTile * HauntAtlasRows,
                sRGB: true, clamp: true,
                comp: TextureImporterCompression.Uncompressed);
            // THE FIRE ATLAS. See MakeFireAtlas for why a fire could not go on
            // being drawn with the candle sprite.
            WritePng(TexDir + "/Env_Fire.png", MakeFireAtlas(),
                FireTile * FireAtlasCols, FireTile * FireAtlasRows,
                sRGB: true, clamp: true);
            AssetDatabase.Refresh();
        }

        // ==================================================== APPARITION ATLAS
        // HAUNT DREAD. The creepy easter eggs' likenesses, rendered on the CPU at
        // bake time.
        //
        // WHY THIS REPLACED THE SIGNED-DISTANCE SHAPES IT USED TO DRAW IN THE
        // FRAGMENT SHADER. The first pass argued that SDFs were right because
        // they are crisp at any distance, cost no memory and can MORPH. All three
        // are true and none of them mattered, because the pictures they produced
        // were cartoons — the user's verdict on the shipped build, verbatim: "Ich
        // habe nur einmal ein Easter Egg im Keller gesehen, das war so ein
        // lächelndes 2D Gesicht im Schrank - das ist weit entfernt von echtem
        // Horror - das sah eher Lächerlich aus. Die Easter eggs sollen echten
        // Horror verbreiten, kein Kindergeburtstag sein." The previews confirm it
        // exactly: env_cellar_HauntGrin was a flat white oval with two round dots
        // and a symmetric smile arc — an emoji; the cellar's crouching thing was
        // two glowing blobs; the forest's watcher was a chess pawn.
        //
        // The reason is structural rather than a matter of taste, and it is worth
        // stating because it is the argument for everything below. A handful of
        // SDF primitives can only produce a SILHOUETTE WITH FEATURES DRAWN ON IT,
        // and a silhouette with features drawn on it is exactly the grammar of a
        // pictogram. What makes a face in the dark frightening is not its outline;
        // it is VALUE — most of it indistinguishable from the dark, with a
        // cheekbone, a jaw edge and one wet gleam coming out of it. Value needs
        // shading, shading needs a surface, and a surface needs either a mesh (the
        // bundle forbids the rigs that would animate one) or a texture.
        //
        // A CPU renderer at bake time has no ALU budget, no instruction limit and
        // no register pressure. What it buys, and what no plausible fragment
        // program was going to buy:
        //   * real Lambert shading off a modelled depth field, with a GRAZING key
        //     light so only what is tilted toward it comes out of the black;
        //   * HEIGHTFIELD SHADOWING — the brow's shadow falling on the forehead.
        //     This is the single biggest contributor and it is a 40-step march
        //     per pixel, i.e. unthinkable in the fragment shader;
        //   * cavity occlusion, so an eye socket is a hole and not a dent;
        //   * multi-octave noise on the skin, on the outline and on the value, so
        //     nothing is uniform and no edge is an analytic curve;
        //   * hair and rags, which are the things that stop a silhouette being a
        //     closed convex shape.
        // What was given up is the morph — the grin that widened. That loss is a
        // gain: a widening grin is the cartoon element the user objected to. The
        // micro-motion that replaced it (a slow head TILT, a damped sway, a
        // per-eye blink) is a UV rotation and is still fully parametric.
        //
        // THE CHANNEL PACKING, and why the texture is UNCOMPRESSED.
        //   R = KEY value    the light the room's own key source puts on it
        //                    (the cellar candles from below; the moon from its
        //                    real bearing in the forest).
        //   G = RIM band     1 on the outline, falling inward. This is what the
        //                    element compensation needs: under full Light it is
        //                    multiplied in as a DARK CONTOUR, under full Dark as a
        //                    faint self-lit edge. Baking it removes the whole
        //                    _RimWidth/_Edge machinery from the shader and gives
        //                    an edge that is irregular instead of analytic.
        //   B = FILL value   a second, opposing light. The shader mixes R and B
        //                    with two per-card COLOURS, so one tile can be lit by
        //                    candlelight in the cellar and by the moon in the
        //                    forest without being baked twice.
        //   A = COVERAGE     the occlusion. Note that a dark apparition has A near
        //                    1 and R,B near 0: it is a HOLE in the scene, which is
        //                    what a real body in the dark is, and it is why the
        //                    blend is ordinary alpha and not additive.
        // Those three RGB channels are three unrelated masks. BC3/DXT5 encodes RGB
        // as two endpoint colours per 4x4 block and assumes the channels move
        // together; three independent masks are the worst case for it, and the
        // artefact would land on the one asset in this feature that may not look
        // cheap. So the atlas ships uncompressed: 1024x1024 RGBA32 = 4 MiB, 5.3
        // MiB with mips. It is mostly transparent black and costs far less than
        // that in the bundle stream.
        //
        // POWER OF TWO, and it has to be: the importer's default npotScale is
        // ToNearest, so a 1024x768 atlas would be silently RESCALED and every tile
        // would land off its cell. 4x4 cells of 256, ten of them used.
        public const int HauntTile = 256;
        public const int HauntAtlasCols = 4, HauntAtlasRows = 4;

        /// <summary>Tile indices in Env_Haunt.png. EnvHaunt.shader is handed one
        /// of these per card in TEXCOORD1.w, and BuildEnvironmentRooms names them
        /// in its catalogues — so this is the one place the numbering lives.</summary>
        // HAUNT SOLID, ModBuild 144 — ONLY THE HANDPRINT TILES ARE STILL DRAWN. The user's
        // verdict ("generell keine 2D Pappaufsteller") moved every apparition onto
        // real geometry (haunt_figures_pipeline.py, EnvHaunt.shader), and a
        // CPU-rendered likeness on a quad is exactly the thing that had to go. What
        // survives is the one card that is honestly flat: a HANDPRINT is
        // two-dimensional, it lies IN the wall's own plane, and its parallax there
        // is correct.
        //
        // THE OTHER NINE TILES ARE STILL BAKED, and that is a decision rather than
        // an oversight. They cost ~1 MiB of a mostly-transparent PNG, the code that
        // draws them is the only CPU renderer in this project and took a whole
        // round to get right, and the geometry that replaced them has to survive a
        // hardware test first. If ModBuild 144 comes back clean, the eight unused
        // generators below and their tile constants are the next thing to delete —
        // and this comment is the note that says so.
        public const int HTileFaceCellar = 0;   // gaunt head, lit from below by a candle
        public const int HTileFaceForest = 1;   // the same species of head, moon-rimmed
        public const int HTileBust = 2;         // head and shoulders filling an opening
        public const int HTileLowFace = 3;      // a head at floor level, looking UP
        public const int HTileWatcher = 4;      // too tall, too thin, standing still
        public const int HTileTallFig = 5;      // crossing; its head is above the card
        public const int HTileHang = 6;         // hung by the feet, head down
        public const int HTileLoom = 7;         // a featureless mass, occlusion only
        // 8, 10 and 11 — ONE HANDPRINT EACH, photo-derived. See HandMarks and
        // HauntHandTiles. Tiles 10..15 were empty, which is what made the fix to
        // "sie sind keine wirklichen Hände" free: three prints that used to share
        // one 256 tile (and therefore one 1.24 m card) get a tile apiece, and with
        // it a 0.9-1.5 mm texel instead of a 3.9 mm one.
        public const int HTileHandContact = 8;  // the first contact, with its drips
        public const int HTileEye = 9;          // ONE eye; the shader places two
        public const int HTileHandChild = 10;   // a CHILD's hand among the adults'
        public const int HTileHandSmear = 11;   // the one that slid

        // ---------------------------------------------------------- tiny kit
        // A tile is four float planes of HauntTile^2. Index [iy * T + ix] with
        // iy = 0 at the BOTTOM, matching every other texture in this file.
        private sealed class HauntPlane
        {
            public readonly float[] V;
            public HauntPlane() { V = new float[HauntTile * HauntTile]; }
            public float this[int i] { get { return V[i]; } set { V[i] = value; } }
        }

        private static float HSStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a + 1e-9f));
            return t * t * (3f - 2f * t);
        }

        private static float HGauss(float dx, float dy, float rx, float ry)
        {
            float u = dx / rx, v = dy / ry;
            return Mathf.Exp(-(u * u + v * v));
        }

        /// <summary>fBm in the tile's own 2D space. Fbm3 with a fixed z — the
        /// atlas needs no third dimension and reusing the file's own noise keeps
        /// one hash in the builder.</summary>
        private static float HFbm(float x, float y, int octaves, int seed)
        {
            return Fbm3(new Vector3(x, y, 0.5f), octaves, seed);
        }

        /// <summary>Distance from p to the segment a..b, and the parameter along
        /// it. Every limb, torso, rag and finger below is one of these.</summary>
        private static float HSegDist(float px, float py, float ax, float ay,
                                      float bx, float by, out float t)
        {
            float dx = bx - ax, dy = by - ay;
            float qx = px - ax, qy = py - ay;
            t = Mathf.Clamp01((qx * dx + qy * dy) / (dx * dx + dy * dy + 1e-9f));
            float ex = qx - dx * t, ey = qy - dy * t;
            return Mathf.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>Surface normal of a depth plane, by central difference.</summary>
        private static void HNormals(HauntPlane z, out float[] nx, out float[] ny, out float[] nz)
        {
            int T = HauntTile;
            nx = new float[T * T]; ny = new float[T * T]; nz = new float[T * T];
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    int xm = Mathf.Max(ix - 1, 0), xp = Mathf.Min(ix + 1, T - 1);
                    int ym = Mathf.Max(iy - 1, 0), yp = Mathf.Min(iy + 1, T - 1);
                    // d/dx and d/dy in TILE units (the tile spans 2 units), so the
                    // normal's slope is comparable with the depth's own scale.
                    float gx = (z[iy * T + xp] - z[iy * T + xm]) * (T / 4f);
                    float gy = (z[yp * T + ix] - z[ym * T + ix]) * (T / 4f);
                    float l = Mathf.Sqrt(gx * gx + gy * gy + 1f);
                    nx[i] = -gx / l; ny[i] = -gy / l; nz[i] = 1f / l;
                }
        }

        private static float HDot(float[] nx, float[] ny, float[] nz, int i, Vector3 L)
        {
            return Mathf.Max(0f, nx[i] * L.x + ny[i] * L.y + nz[i] * L.z);
        }

        /// <summary>A GRAZING/rim response: the surface must both face the light
        /// and be turning away from the viewer. "Lit only along one edge", which
        /// is what every distant silhouette in this catalogue is.</summary>
        private static float HGraze(float[] nx, float[] ny, float[] nz, int i, Vector3 L, float p)
        {
            return HDot(nx, ny, nz, i, L) * Mathf.Pow(Mathf.Clamp01(1f - nz[i]), p);
        }

        /// <summary>HEIGHTFIELD SHADOWING. March along the light's screen-space
        /// direction and ask whether anything on the way is high enough to block
        /// it. Returns 1 = lit, 0 = shadowed.
        ///
        /// <para>This is what makes a face lit from below read as a face lit from
        /// below. A Lambert term alone gives every patch with the same normal the
        /// same value wherever it sits; only an occlusion march puts the brow's
        /// shadow ON the forehead and the nose's shadow across the cheek — and
        /// those two shadows are most of the picture. It is 40 taps per pixel,
        /// which is why this belongs at bake time and could never have been the
        /// fragment shader's job.</para></summary>
        private static float[] HShadow(HauntPlane z, Vector3 L, int steps = 40, float reach = 0.55f)
        {
            int T = HauntTile;
            var lit = new float[T * T];
            for (int i = 0; i < lit.Length; i++) lit[i] = 1f;
            float lxy = Mathf.Sqrt(L.x * L.x + L.y * L.y) + 1e-6f;
            float dx = L.x / lxy, dy = L.y / lxy, slope = L.z / lxy;
            for (int k = 1; k <= steps; k++)
            {
                float t = reach * k / steps;                 // travel, tile units
                int px = Mathf.RoundToInt(dx * t * T * 0.5f);
                int py = Mathf.RoundToInt(dy * t * T * 0.5f);
                if (px == 0 && py == 0) continue;
                for (int iy = 0; iy < T; iy++)
                {
                    int sy = iy + py; if (sy < 0 || sy >= T) continue;
                    for (int ix = 0; ix < T; ix++)
                    {
                        int sx = ix + px; if (sx < 0 || sx >= T) continue;
                        int i = iy * T + ix;
                        float blocked = Mathf.Clamp01((z[sy * T + sx] - z[i] - slope * t) / 0.025f);
                        if (1f - blocked < lit[i]) lit[i] = 1f - blocked;
                    }
                }
            }
            return lit;
        }

        /// <summary>Two-pass chamfer distance transform: for every pixel, the
        /// distance in TILE UNITS to the nearest pixel where `inside` is true.
        /// Deterministic, no allocation per pass, and exact enough for a rim band
        /// that is six per cent of a tile wide.</summary>
        private static float[] HDistanceTo(bool[] inside)
        {
            int T = HauntTile;
            const float BIG = 1e6f, A = 1f, B = 1.41421356f;
            var d = new float[T * T];
            for (int i = 0; i < d.Length; i++) d[i] = inside[i] ? 0f : BIG;
            for (int iy = 1; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix; float v = d[i];
                    if (ix > 0) v = Mathf.Min(v, d[i - 1] + A);
                    v = Mathf.Min(v, d[i - T] + A);
                    if (ix > 0) v = Mathf.Min(v, d[i - T - 1] + B);
                    if (ix < T - 1) v = Mathf.Min(v, d[i - T + 1] + B);
                    d[i] = v;
                }
            for (int iy = T - 2; iy >= 0; iy--)
                for (int ix = T - 1; ix >= 0; ix--)
                {
                    int i = iy * T + ix; float v = d[i];
                    if (ix < T - 1) v = Mathf.Min(v, d[i + 1] + A);
                    v = Mathf.Min(v, d[i + T] + A);
                    if (ix < T - 1) v = Mathf.Min(v, d[i + T + 1] + B);
                    if (ix > 0) v = Mathf.Min(v, d[i + T - 1] + B);
                    d[i] = v;
                }
            for (int i = 0; i < d.Length; i++) d[i] *= 2f / T;
            return d;
        }

        /// <summary>The G channel: 1 on the outline, falling inward over `reach`
        /// tile units. Derived from the coverage's own DEPTH, so the band follows
        /// every notch the noise chewed into the silhouette — which is the whole
        /// reason it is baked rather than computed from an analytic distance.</summary>
        private static float[] HRimBand(float[] cov, int seed, float reach = 0.135f)
        {
            int T = HauntTile;
            var outside = new bool[cov.Length];
            for (int i = 0; i < cov.Length; i++) outside[i] = cov[i] < 0.5f;
            var depth = HDistanceTo(outside);
            var rim = new float[cov.Length];
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    if (cov[i] <= 0.5f) continue;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    float f = Mathf.Clamp01(1f - depth[i] / reach);
                    // WIDE AND UNEVEN. The first version was 0.055 of a tile with a
                    // ^1.5 profile, which on a 2.7 m card is a 15 cm bright line
                    // that traces the whole silhouette — and a shape with an even
                    // bright line all the way round it is a STICKER. The previews
                    // were unambiguous: the hanging figure came out as a neon
                    // wireframe and the mass in the trees as an outlined cartoon.
                    // Three times the reach and a cubic profile make it an inward
                    // GLOW instead of an outline; the noise then breaks it, so the
                    // edge is lit in some places and gone in others, the way an
                    // edge catching a real light actually is.
                    rim[i] = f * f * f * (0.20f + 1.15f * HFbm(x * 3.1f + 7f, y * 3.1f, 3, seed + 91));
                }
            return rim;
        }

        /// <summary>Blur a plane in place, five-tap, `n` times. Used only to get
        /// the LARGE-SCALE surface, against which the small-scale relief becomes
        /// a cavity-occlusion term.</summary>
        private static HauntPlane HBlur(HauntPlane src, int n)
        {
            int T = HauntTile;
            var a = new HauntPlane(); Array.Copy(src.V, a.V, src.V.Length);
            var b = new HauntPlane();
            for (int p = 0; p < n; p++)
            {
                for (int iy = 0; iy < T; iy++)
                    for (int ix = 0; ix < T; ix++)
                    {
                        int i = iy * T + ix;
                        int xm = Mathf.Max(ix - 1, 0), xp = Mathf.Min(ix + 1, T - 1);
                        int ym = Mathf.Max(iy - 1, 0), yp = Mathf.Min(iy + 1, T - 1);
                        b[i] = (a[i] + a[iy * T + xm] + a[iy * T + xp]
                                + a[ym * T + ix] + a[yp * T + ix]) * 0.2f;
                    }
                var t = a; a = b; b = t;
            }
            return a;
        }

        /// <summary>One finished apparition: the four planes the atlas packs.</summary>
        private struct HauntTileData
        {
            public float[] key, rim, fill, cov;
        }

        /// <summary>Every knob of the head renderer. Defaults are the cellar's.</summary>
        private struct HauntFaceCfg
        {
            public int seed;
            public float lean;        // the head leans off vertical
            public float jaw;         // jaw length multiplier
            public float hairSide;    // +1 hair down the right, -1 down the left
            public float mouthOpen;   // half-height of the gap, tile units
            public float scale, drop; // frame the head inside the tile
            public float lookUp;      // foreshorten the cranium: it is looking UP
            public float shoulders;   // >0 adds a shoulder line at this depth
            public float hair;        // 0 = bald; scales the strand mass
            public float shoulderTilt;
            public Vector3 key, fill;
            public float keyPow, keyGain, fillPow, fillGain;
        }

        /// <summary>THE HEAD. Rendered, not drawn: a depth field with a brow, two
        /// pits, cheekbones, a broken nose and an opening for a mouth, shaded by a
        /// grazing key with real cast shadows, then chewed at the edge and hung
        /// with hair.
        ///
        /// <para>NOTHING IN IT IS SYMMETRIC, and that is a rule rather than a
        /// flourish. The skull is rotated 4.5 degrees; one side of the outline is
        /// 7 % wider than the other; the left socket is deeper, larger and sits
        /// higher than the right; the brows differ; the mouth is wider on one
        /// side and its line is bent; the single wet gleam is in ONE socket and
        /// off its centre; the hair is on one side only. Symmetric ovals and
        /// symmetric arcs are the grammar of a pictogram, and the pictogram is
        /// exactly what the user rejected.</para>
        ///
        /// <para>THE KEY LIGHT GRAZES — |z| around 0.1 of a unit vector. A light
        /// with any real z component lights the whole flat front of a face to ONE
        /// value, and one value across a face is the flat printed-paper read that
        /// the first version shipped. Grazing means only what is TILTED toward it
        /// comes out of the dark at all: the underside of the brow, of a
        /// cheekbone, of the nose, and the jaw line. Everything else stays in the
        /// black and the player's own eye finishes the head.</para></summary>
        private static HauntTileData HauntFace(HauntFaceCfg c)
        {
            int T = HauntTile;
            var z = new HauntPlane();
            var covH = new float[T * T];
            var holes = new float[T * T];
            var mouthM = new float[T * T];
            // keep the frame coordinates: the shoulder pass needs them again
            var fxA = new float[T * T]; var fyA = new float[T * T];
            var xrA = new float[T * T]; var yrA = new float[T * T];

            float ca = Mathf.Cos(4.5f * Mathf.Deg2Rad), sa = Mathf.Sin(4.5f * Mathf.Deg2Rad);
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = ((ix + 0.5f) / T * 2f - 1f) / c.scale;
                    float y = ((iy + 0.5f) / T * 2f - 1f + c.drop) / c.scale;
                    fxA[i] = x; fyA[i] = y;
                    float xr = x * ca - y * sa;
                    float yr = x * sa + y * ca;
                    xr += c.lean * yr;
                    // LOOKING UP AT YOU: the cranium foreshortens away and the jaw
                    // comes toward the viewer. A head at floor level drawn in
                    // front elevation is a head lying on its back, which is a
                    // corpse; a head that is looking up is a threat.
                    if (c.lookUp > 0f)
                        yr = yr * (1f - c.lookUp * HSStep(0f, 0.95f, yr)) + c.lookUp * 0.18f;
                    xrA[i] = xr; yrA[i] = yr;

                    // ---- outline. Cranium NARROW, jaw LONG and slab-sided: an
                    // egg with a point is a grey alien, a different and sillier
                    // monster, and it is what the first attempt produced.
                    float ax = 0.355f * (1f + 0.10f * HSStep(0.55f, 0.05f, yr))
                                      * (1f - 0.30f * Mathf.Pow(HSStep(-0.28f, -0.95f, yr), 1.6f))
                                      * (1f - 0.20f * HSStep(0.30f, 0.95f, yr));
                    ax *= xr < 0f ? 1.07f : 0.95f;
                    float ayy = yr > 0f ? 0.62f : 0.95f * c.jaw;
                    float s = 1f - Mathf.Pow(Mathf.Abs(xr / ax), 2.4f)
                                 - Mathf.Pow(Mathf.Abs(yr / ayy), 2.6f);
                    float edgeN = HFbm(xr * 3.4f + 5f, yr * 3.4f, 4, c.seed) - 0.5f;
                    covH[i] = HSStep(-0.03f, 0.04f, s + 0.13f * edgeN);

                    float d = Mathf.Pow(Mathf.Max(s, 0f), 0.45f) * 0.30f;

                    const float ex = 0.165f;
                    // SOCKETS, not eyes: pits. The left is deeper, larger, higher.
                    d -= 0.190f * HGauss(xr + ex * 1.10f, yr - 0.190f, 0.135f, 0.115f);
                    d -= 0.150f * HGauss(xr - ex * 0.92f, yr - 0.150f, 0.112f, 0.094f);
                    // the brow shelf that casts the shadow which hides them
                    d += 0.085f * HGauss(xr + 0.16f, yr - 0.345f, 0.23f, 0.075f);
                    d += 0.062f * HGauss(xr - 0.15f, yr - 0.318f, 0.19f, 0.066f);
                    d -= 0.060f * HGauss(Mathf.Abs(xr) - 0.33f, yr - 0.34f, 0.12f, 0.15f);
                    // cheekbones high and sharp, cheeks sunken under them
                    d += 0.062f * HGauss(xr + 0.245f, yr + 0.005f, 0.135f, 0.105f);
                    d += 0.050f * HGauss(xr - 0.225f, yr + 0.045f, 0.120f, 0.098f);
                    d -= 0.075f * HGauss(Mathf.Abs(xr) - 0.185f, yr + 0.245f, 0.115f, 0.165f);
                    // nose: a narrow ridge that stops, then two slots, bent off axis
                    float nx0 = xr - 0.015f + 0.05f * (yr + 0.1f);
                    d += 0.046f * HGauss(nx0, yr - 0.02f, 0.034f, 0.17f);
                    d -= 0.085f * HGauss(Mathf.Abs(nx0) - 0.050f, yr + 0.135f, 0.032f, 0.030f);

                    // MOUTH. NOT a depression — a modelled slot has lit lips, and
                    // a lit lip under a grazing key came out as a chrome bar
                    // across the face two attempts running. It is an OPENING:
                    // coverage stays, light does not.
                    const float mw = 0.150f;
                    float my = yr + 0.330f + 0.075f * xr + 0.055f * xr * xr;
                    float mn = 0.030f * (HFbm(xr * 9f, yr * 9f + 3f, 3, c.seed + 5) - 0.5f);
                    float mouth = HSStep(c.mouthOpen + 0.016f, c.mouthOpen - 0.012f, Mathf.Abs(my + mn))
                                * HSStep(mw + 0.02f, mw - 0.05f,
                                         Mathf.Abs(xr + 0.02f) * (xr < -0.02f ? 1.15f : 0.80f));
                    mouthM[i] = mouth;
                    d -= 0.020f * mouth;
                    d += 0.028f * HGauss(xr + 0.02f, yr + 0.62f, 0.12f, 0.14f);
                    z[i] = d;

                    // CAVITIES ARE HOLES. No light reaches into a socket or into
                    // an open mouth, and a shading model that lets one brighten
                    // has drawn a mask.
                    float h = Mathf.Clamp01(mouth * 1.6f);
                    h = Mathf.Max(h, HSStep(c.mouthOpen + 0.085f, c.mouthOpen + 0.010f, Mathf.Abs(my + mn))
                                     * HSStep(mw + 0.055f, mw - 0.02f, Mathf.Abs(xr + 0.02f)));
                    h = Mathf.Max(h, HSStep(0.35f, 0.85f, HGauss(xr + ex * 1.10f, yr - 0.190f, 0.115f, 0.098f)));
                    h = Mathf.Max(h, HSStep(0.40f, 0.88f, HGauss(xr - ex * 0.92f, yr - 0.150f, 0.095f, 0.080f)));
                    holes[i] = h;
                }

            var zb = HBlur(z, 9);
            HNormals(z, out var nx, out var ny, out var nz);
            var shK = HShadow(z, c.key.normalized, 40, 0.55f);

            var litK = new float[T * T];
            var litF = new float[T * T];
            var cov = new float[T * T];
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float xr = xrA[i], yr = yrA[i];
                    float ao = Mathf.Pow(Mathf.Clamp01(0.40f + (z[i] - zb[i]) * 30f), 1.7f);
                    float k = Mathf.Pow(HDot(nx, ny, nz, i, c.key.normalized), c.keyPow)
                              * c.keyGain * ao * (0.06f + 0.94f * shK[i]);
                    float f = HGraze(nx, ny, nz, i, c.fill.normalized, c.fillPow)
                              * c.fillGain * (0.25f + 0.75f * ao);
                    k *= 1f - holes[i]; f *= 1f - holes[i];
                    // ...and the key is CLOSE and LOW, so it falls off up the
                    // head. The crown is not dim, it is GONE — most of an
                    // apparition has to be indistinguishable from the dark.
                    k *= 0.06f + 0.94f * HSStep(0.72f, -0.55f, yr);
                    float vb = 0.35f + 1.05f * HFbm(xr * 1.9f + 2f, yr * 1.9f, 3, c.seed + 61);
                    k *= vb; f *= 0.5f + 0.5f * vb;
                    float skin = (0.60f + 0.66f * HFbm(xr * 5.5f, yr * 5.5f, 4, c.seed + 3))
                               * (0.76f + 0.44f * HFbm(xr * 17f, yr * 17f, 3, c.seed + 9));
                    k *= skin; f *= skin;
                    // ONE wet gleam, in ONE socket, off its centre. A pair of
                    // symmetric highlights is a pair of eyes; a single one is a
                    // thing that is wet.
                    k = Mathf.Max(k, HGauss(xr + 0.165f * 1.10f + 0.042f, yr - 0.215f, 0.014f, 0.011f));

                    // ---- HAIR: strands hanging down ONE side and past the jaw,
                    // so the silhouette stops being a closed curve. Confined to a
                    // narrow band around the outline on that side — a full-tile
                    // stripe field reads as a barcode, which the first attempt
                    // duly produced.
                    float hx = xr * c.hairSide;
                    float warp = 0.14f * (HFbm(hx * 2.4f, yr * 1.5f + 4f, 3, c.seed + 41) - 0.5f);
                    float u = hx + warp - 0.10f * (yr - 0.35f);
                    float st = Mathf.Abs(Mathf.Sin(u * 34f + 5f * HFbm(hx * 3f, yr * 1.1f, 2, c.seed + 43)));
                    st = HSStep(0.72f, 0.06f, st);
                    float band = HSStep(0.13f, 0.21f, u) * HSStep(0.54f, 0.38f, u)
                               * HSStep(-0.80f, -0.52f, yr) * HSStep(0.84f, 0.58f, yr);
                    // A head TILTED BACK on the floor has its hair behind it, not
                    // standing up beside its ear: the first bake of the
                    // floor-level face put three bright strands over the
                    // barrel it was behind and they read as CLAWS.
                    float hair = Mathf.Clamp01(st * band
                                 * (0.5f + 1.0f * HFbm(hx * 4f, yr * 3f, 3, c.seed + 47))
                                 * 1.7f * c.hair);
                    cov[i] = Mathf.Clamp01(covH[i] + hair);
                    litK[i] = k * (1f - hair) + hair * 0.022f;
                    litF[i] = f * (1f - hair) + hair * 0.10f * st;
                }

            // ---- SHOULDERS, for the cards that are a head AND a body in a frame.
            if (c.shoulders > 0f)
            {
                var sz = new HauntPlane();
                var sm = new float[T * T];
                for (int i = 0; i < T * T; i++)
                {
                    float sd = HSegDist(fxA[i], fyA[i],
                                        -1.60f, -c.shoulders - 0.22f * c.shoulderTilt,
                                        1.60f, -c.shoulders + 0.72f * c.shoulderTilt, out _);
                    sm[i] = HSStep(0.62f, 0.52f,
                                   sd + 0.16f * (HFbm(fxA[i] * 3.2f, fyA[i] * 3.2f + 9f, 4, c.seed + 71) - 0.5f));
                    float r = Mathf.Min(sd, 0.62f);
                    sz[i] = Mathf.Sqrt(Mathf.Max(0.38f - r * r, 0f));
                }
                HNormals(sz, out var sx, out var sy, out var sn);
                for (int i = 0; i < T * T; i++)
                {
                    float lk = HGraze(sx, sy, sn, i, c.key.normalized, 2.2f) * c.keyGain * 0.55f;
                    float lf = HGraze(sx, sy, sn, i, c.fill.normalized, c.fillPow) * c.fillGain * 0.5f;
                    cov[i] = Mathf.Clamp01(cov[i] + sm[i]);
                    litK[i] = Mathf.Max(litK[i] * (1f - sm[i]), lk * sm[i]);
                    litF[i] = Mathf.Max(litF[i] * (1f - sm[i]), lf * sm[i]);
                }
            }

            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    cov[i] *= HSStep(1.03f, 0.95f, Mathf.Sqrt(x * x + y * y));
                    cov[i] = Mathf.Clamp01(cov[i]);
                    litK[i] = Mathf.Clamp01(litK[i] * cov[i]);
                    litF[i] = Mathf.Clamp01(litF[i] * cov[i]);
                }
            return new HauntTileData { key = litK, rim = HRimBand(cov, c.seed), fill = litF, cov = cov };
        }

        /// <summary>One tapered capsule of a body: a..b in tile units, radius r0
        /// at a tapering to r1 at b.</summary>
        private struct HauntLimb
        {
            public float ax, ay, bx, by, r0, r1;
            public HauntLimb(float ax, float ay, float bx, float by, float r0, float r1)
            { this.ax = ax; this.ay = ay; this.bx = bx; this.by = by; this.r0 = r0; this.r1 = r1; }
        }

        /// <summary>THE BODIES. A figure out of tapered capsules, given a depth so
        /// that it can be LIT rather than merely outlined.
        ///
        /// <para>Every distant silhouette in this catalogue is drawn here, and its
        /// content is not its features — at 13 to 17 m there are none. It is (a)
        /// an outline that is irregular the way cloth and hair are irregular, and
        /// (b) ONE edge that catches the room's light. `key` is therefore a
        /// GRAZING direction: it produces a bright line down one side, and
        /// nothing else at all.</para>
        ///
        /// <para>THE TILE IS A SQUARE IN METRES of side 2 * halfH, and the parts
        /// below are in those units. This matters: the first version authored the
        /// figures in the CARD's stretched space, and a 2 m card that is 0.8 m
        /// wide turned every one of them into a pole. Radii here are real — 0.098
        /// is a 20 cm skull on a 1 m half-height.</para></summary>
        private static HauntTileData HauntFigure(HauntLimb[] parts, int seed,
            Vector3 key, float keyGain, float keyPow,
            Vector3 fill, float fillGain, float fillPow,
            float ragged = 0.055f, float hem = 0f, float hemY = -0.80f, float hemDir = -1f,
            float margin = 1.02f)
        {
            int T = HauntTile;
            var z = new HauntPlane();
            var zr = new HauntPlane();
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    float best = 0f;
                    foreach (var p in parts)
                    {
                        float d = HSegDist(x, y, p.ax, p.ay, p.bx, p.by, out float t);
                        float r = p.r0 + (p.r1 - p.r0) * t;
                        float h = Mathf.Sqrt(Mathf.Max(r * r - d * d, 0f));
                        if (h > best) best = h;
                    }
                    z[i] = best;
                }
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    float n1 = HFbm(x * 4.5f + 3f, y * 4.5f, 4, seed) - 0.5f;
                    float n2 = HFbm(x * 13f, y * 13f + 7f, 3, seed + 5) - 0.5f;
                    zr[i] = z[i] + ragged * (n1 * 1.4f + n2 * 0.5f) * HSStep(0f, 0.06f, z[i]);
                }

            // A TORN BAND — rags, hair or roots reaching off the body from the
            // line `hemY`. It may only exist WHERE THE BODY IS: the first attempt
            // masked on the row alone and drew a dashed line straight across the
            // tile, which is a fence and not a rag.
            if (hem > 0f)
            {
                int row = Mathf.Clamp(Mathf.RoundToInt((hemY + 1f) * 0.5f * T), 0, T - 1);
                var onBody = new bool[T];
                for (int ix = 0; ix < T; ix++) onBody[ix] = z[row * T + ix] > 0.006f;
                var wide = new bool[T];
                for (int ix = 0; ix < T; ix++)
                    for (int k = -3; k <= 3; k++)
                    {
                        int j = ix + k;
                        if (j >= 0 && j < T && onBody[j]) { wide[ix] = true; break; }
                    }
                for (int iy = 0; iy < T; iy++)
                    for (int ix = 0; ix < T; ix++)
                    {
                        if (!wide[ix]) continue;
                        int i = iy * T + ix;
                        float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                        float band = (hemY - y) * hemDir;
                        if (band <= 0f) continue;
                        float st = Mathf.Abs(Mathf.Sin(x * 47f + 7f * HFbm(x * 3f, y, 2, seed + 11)));
                        st = HSStep(0.75f, 0.15f, st) * (0.4f + 1.2f * HFbm(x * 6f, y * 2f, 3, seed + 13));
                        float v = 0.05f * st * HSStep(hem * st, 0f, band);
                        if (v > zr[i]) zr[i] = v;
                    }
            }

            var zs = new HauntPlane();
            for (int i = 0; i < T * T; i++) zs[i] = zr[i] * 0.9f;
            HNormals(zs, out var nx, out var ny, out var nz);

            var litK = new float[T * T];
            var litF = new float[T * T];
            var cov = new float[T * T];
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    float c = HSStep(0.004f, 0.020f, zr[i]);
                    c *= HSStep(margin, margin - 0.08f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
                    float br = 0.45f + 1.05f * HFbm(x * 2.2f + 9f, y * 2.2f, 3, seed + 21);
                    cov[i] = Mathf.Clamp01(c);
                    litK[i] = Mathf.Clamp01(HGraze(nx, ny, nz, i, key.normalized, keyPow) * keyGain * br * c);
                    litF[i] = Mathf.Clamp01(HGraze(nx, ny, nz, i, fill.normalized, fillPow) * fillGain * br * c);
                }
            return new HauntTileData { key = litK, rim = HRimBand(cov, seed), fill = litF, cov = cov };
        }

        // ====================================================== THE HANDPRINTS
        // USER, wave 2, verbatim: "Sie sind keine wirklichen Hände / es schwebt
        // über den Mauern / es ist kackbraun statt blutig / die Position ist
        // nicht gut, da ein Teil davon über dem Eingang schwebt wo gar keine
        // Mauer ist. Nutze hier irgendwelche Texturen aus dem Internet die
        // tatsächlich Horror verursachen könnten."
        //
        // WHAT WAS HERE. A signed-distance handprint generator: a capsule for the
        // palm, n capsules radiating for the fingers, one for the thumb, all
        // min-unioned and smoothed. That grammar can only ever produce A MITTEN
        // WITH SAUSAGES, and it is the exact structural failure this file's own
        // header already diagnosed for the SDF faces — "a silhouette with
        // features drawn on it is exactly the grammar of a pictogram". The prints
        // were the one card that never got the fix. They also came out at 0.41 x
        // 1.09 m each on a 1.24 m card, i.e. 5.5x LIFE SIZE, which in VR — where
        // the player has his own hands in the frame — no texture survives.
        //
        // WHAT REPLACES IT. Three real photographed prints, keyed out of a CC0
        // photograph by handprint_atlas_pipeline.py (see there for the licence
        // trail, the channel algebra and every step's reason; the licence itself
        // is quoted verbatim in Environments/License.md). They carry what the
        // capsules could not fake: MISSING PALM ARCHES, DETACHED THUMBS, FINGERS
        // BROKEN INTO PAD SEGMENTS, real gravity drips, and four distinct hands.
        //
        // ONE PRINT PER TILE, at LIFE SIZE (190 mm adult, 132 mm child), each on
        // its own small card. That is what buys back the resolution: 0.9-1.5 mm
        // per texel instead of 3.9, so a pad gap is 5 texels instead of one.
        //
        // The derived PNG is a BAKE-TIME INPUT ONLY. It is decoded from its own
        // bytes (ImageConversion) exactly as the vegetation lane decodes the twig
        // atlas and the floor lane the flagstones — never by flipping isReadable
        // on the imported asset, which would keep a CPU copy alive in the bundle
        // for a build-time question. Its importer is deliberately set to a tiny
        // maxTextureSize: nothing at runtime samples it, and the pixels the bake
        // needs come from the file, not from the import. DO NOT "fix" that.

        /// <summary>Where one photographed print lands on the cellar's west wall,
        /// and how big its card is. THIS IS THE ONE PLACE THE PLACEMENT LIVES:
        /// the atlas needs it (to mask the print out of the masonry's mortar
        /// grooves, which needs the wall UV) and EnvRoomBuilder needs it (to build
        /// the quad). Two copies of these numbers would be two chances to drift.
        ///
        /// <para>z is room-space z of the card centre, y its height. The wall runs
        /// -4.5..+4.5 in z and the STAIR DOORWAY is cut out of it; the clearance
        /// against that opening is asserted in EnvRoomBuilder, where the hole's
        /// snapped rect lives.</para></summary>
        public struct HandMark
        {
            public string name;
            public int tile;
            public float side;   // the card is `side` x `side` metres
            public float z, y;   // room-space centre, on the west wall
            public string why;
        }

        // THE STORY THE ARRANGEMENT TELLS, since the shader cannot tell it in
        // time (see EnvRoomBuilder's "Hands" card for the proof that a delayed
        // onset is not reachable): three prints DESCENDING along the wall, 0.42
        // and 0.44 m apart, the last one a SMEAR whose drag trails back the way
        // the body came. That is a body going along this wall and down it. The
        // middle print is a CHILD'S — an anomaly of SCALE, which works
        // pre-attentively, and it replaces the old middle print's SIX FINGERS.
        // A wrongness the player has to count is a joke, not a fright.
        public static readonly HandMark[] HandMarks =
        {
            new HandMark
            {
                name = "contact", tile = HTileHandContact, side = 0.340f,
                z = 1.26f, y = 1.46f,
                why = "first contact at 1.46 m, nearest the stair doorway — the height a "
                      + "standing adult braces at, and its drips have run furthest",
            },
            new HandMark
            {
                name = "child", tile = HTileHandChild, side = 0.220f,
                z = 0.84f, y = 1.22f,
                why = "0.42 m along and 0.24 m lower — and it is a CHILD'S hand, "
                      + "132 mm, between two adult ones",
            },
            new HandMark
            {
                name = "smear", tile = HTileHandSmear, side = 0.380f,
                z = 0.42f, y = 0.94f,
                why = "0.42 m further and 0.28 m lower, and it SLID: the drag "
                      + "trails back toward +z, the way the body came",
            },
        };

        // The cellar west wall's texture mapping, MIRRORED from EnvRoomBuilder
        // (WallMesh's uv = ((x + uOff) / uvScale, y / uvScale), the wall placed at
        // z = -CD/2 running +z). EnvRoomBuilder asserts these three against its
        // own constants, so the mirror cannot drift silently.
        public const float WallUvScale = 3.3f;
        public const float WallWUOff = 0.73f;
        public const float CellarHalfDepth = 4.5f;

        // How much of the print the mortar grooves eat. A hand pressed to a block
        // wall marks the FACES and skips the recesses; that one multiply is what
        // makes it look pressed INTO the stone rather than laid over it.
        private const float MortarKeep = 0.12f;   // alpha left in the deepest joint
        private const int MortarN = 512;          // 6.4 mm per texel over the 3.3 m tile

        /// <summary>The masonry's own cavity field, from medieval_blocks_05's
        /// albedo. Same detector as EnvRoomBuilder.CellarJointMask (a wrapping
        /// high pass, thresholded by PERCENTILE so it cannot be thrown off by the
        /// exposure of whatever texture a future round swaps in) — but returned
        /// as a continuous 0..1 depth rather than a boolean, because a print does
        /// not stop dead at a joint edge.</summary>
        private static float[] _mortar;

        private static float[] MortarCavity()
        {
            if (_mortar != null) return _mortar;
            string path = Root + "/Imported/Textures/medieval_blocks_05_alb.jpg";
            if (!File.Exists(path))
                throw new Exception($"{path} is missing — the handprints are masked out of "
                                    + "THIS masonry's mortar grooves, so without it they would "
                                    + "have to lie over the joints, which is the 'sticker' look "
                                    + "this pass exists to remove.");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
                throw new Exception($"Could not decode {path} for the mortar cavity field.");
            int w = tex.width, h = tex.height;
            var src = tex.GetPixels32();
            var lum = new float[MortarN * MortarN];
            var cnt = new float[MortarN * MortarN];
            for (int y = 0; y < h; y++)
            {
                int jy = y * MortarN / h;
                for (int x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    int k = jy * MortarN + x * MortarN / w;
                    lum[k] += (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    cnt[k] += 1f;
                }
            }
            for (int i = 0; i < lum.Length; i++) lum[i] /= Mathf.Max(cnt[i], 1f);
            UnityEngine.Object.DestroyImmediate(tex);

            // wrapping separable box blur over ~13 cm — wider than a joint, far
            // narrower than a block, so what survives the subtraction IS the joint
            const int R = 20;
            var tmp = new float[lum.Length];
            var blur = new float[lum.Length];
            for (int j = 0; j < MortarN; j++)
                for (int i = 0; i < MortarN; i++)
                {
                    float s = 0f;
                    for (int k = -R; k <= R; k++)
                        s += lum[j * MortarN + ((i + k) % MortarN + MortarN) % MortarN];
                    tmp[j * MortarN + i] = s / (2 * R + 1);
                }
            for (int j = 0; j < MortarN; j++)
                for (int i = 0; i < MortarN; i++)
                {
                    float s = 0f;
                    for (int k = -R; k <= R; k++)
                        s += tmp[(((j + k) % MortarN + MortarN) % MortarN) * MortarN + i];
                    blur[j * MortarN + i] = s / (2 * R + 1);
                }
            var dark = new float[lum.Length];
            for (int i = 0; i < lum.Length; i++) dark[i] = blur[i] - lum[i];
            var sorted = (float[])dark.Clone();
            Array.Sort(sorted);
            // the 78th and 96th percentiles of "how far below its own
            // neighbourhood" — between them the joint fades in
            float lo = sorted[(int)(sorted.Length * 0.78f)];
            float hi = sorted[(int)(sorted.Length * 0.96f)];
            if (hi - lo < 1e-4f)
                throw new Exception("medieval_blocks_05 has no mortar contrast at all "
                                    + $"(p78 {lo:F5}, p96 {hi:F5}) — the cavity detector would "
                                    + "return noise and the prints would be masked at random.");
            var cav = new float[lum.Length];
            float sum = 0f;
            for (int i = 0; i < cav.Length; i++)
            {
                cav[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, dark[i]));
                sum += cav[i];
            }
            Debug.Log($"[GloomhavenVR][Env] HANDPRINT mortar mask: medieval_blocks_05_alb {w}x{h} "
                      + $"-> {MortarN}^2 cavity field over {WallUvScale:F1} m "
                      + $"({WallUvScale / MortarN * 1000f:F1} mm/texel); mean cavity {sum / cav.Length:F3}, "
                      + $"joint band {lo:F4}..{hi:F4} of local darkening.");
            _mortar = cav;
            return _mortar;
        }

        /// <summary>Read the derived print tiles and turn each into a
        /// HauntTileData, masked out of the masonry's mortar grooves.
        ///
        /// <para>THE CHANNELS ARE NOT WHAT THE OTHER TILES PUT IN THEM, and they
        /// cannot be: EnvHaunt's decal path composes `col = COLOR * T.r +
        /// _Fill * T.b`, and that shader belongs to another lane this round. So
        /// the two weights are solved in the pipeline against a THICKNESS RAMP
        /// (thin edge bright red, bulk near-black, drip head black) and the two
        /// basis colours are set on the card and on the material. R and B here
        /// are therefore ramp WEIGHTS, not values; G is the wet specular mask
        /// the shader adds as a rim; A is coverage.</para></summary>
        private static HauntTileData[] HauntHandTiles()
        {
            int T = HauntTile;
            string path = Root + "/Imported/Textures/handprints_alb.png";
            if (!File.Exists(path))
                throw new Exception($"{path} is missing. It is derived from a CC0 photograph by "
                                    + "Assets/Editor/handprint_atlas_pipeline.py (which downloads its own "
                                    + "source; the photograph is never committed). Without it the "
                                    + "handprints would fall back to nothing at all.");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
                throw new Exception($"Could not decode {path}.");
            int w = tex.width, h = tex.height;
            if (h != T || w != T * HandMarks.Length)
                throw new Exception($"{path} is {w}x{h}; the bake wants {T * HandMarks.Length}x{T} "
                                    + $"({HandMarks.Length} tiles of {T}). Re-run "
                                    + "handprint_atlas_pipeline.py.");
            // GetPixels32 index 0 is BOTTOM-left (Unity flips the PNG's rows on
            // decode), which is the same convention every HauntPlane in this file
            // uses — so a row copied straight across keeps the print upright.
            var src = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);

            var cav = MortarCavity();
            var outTiles = new HauntTileData[HandMarks.Length];
            float allTotal = 0f, allEaten = 0f;
            for (int m = 0; m < HandMarks.Length; m++)
            {
                var mark = HandMarks[m];
                var key = new float[T * T];
                var rim = new float[T * T];
                var fill = new float[T * T];
                var cov = new float[T * T];
                float eaten = 0f, total = 0f;
                for (int iy = 0; iy < T; iy++)
                    for (int ix = 0; ix < T; ix++)
                    {
                        var c = src[iy * w + m * T + ix];
                        float a = c.a / 255f;
                        // where this texel lands on the wall, and therefore on the
                        // masonry's own texture
                        float z = mark.z + ((ix + 0.5f) / T - 0.5f) * mark.side;
                        float y = mark.y + ((iy + 0.5f) / T - 0.5f) * mark.side;
                        float u = (z + CellarHalfDepth + WallWUOff) / WallUvScale;
                        float v = y / WallUvScale;
                        int cx = Mathf.FloorToInt(u * MortarN);
                        int cy = Mathf.FloorToInt(v * MortarN);
                        cx = ((cx % MortarN) + MortarN) % MortarN;
                        cy = ((cy % MortarN) + MortarN) % MortarN;
                        float keep = Mathf.Lerp(1f, MortarKeep, cav[cy * MortarN + cx]);
                        total += a;
                        eaten += a * (1f - keep);
                        int i = iy * T + ix;
                        cov[i] = a * keep;
                        key[i] = c.r / 255f;
                        rim[i] = c.g / 255f;
                        fill[i] = c.b / 255f;
                    }
                outTiles[m] = new HauntTileData { key = key, rim = rim, fill = fill, cov = cov };
                if (total < 400f)
                    throw new Exception($"handprint '{mark.name}' has {total:F0} texels of coverage — "
                                        + "the tile is empty.");
                float frac = eaten / total;
                allTotal += total; allEaten += eaten;
                // NOT A PER-MARK GATE. A 220 mm print can honestly land wholly on
                // one block face — medieval_blocks_05's stones are much bigger
                // than that at 3.3 m per tile — so "this one mark met no joint" is
                // a legitimate outcome and only the AGGREGATE says whether the
                // cavity field is aligned with the wall at all. What IS a per-mark
                // error is a mark that has been eaten alive.
                if (frac > 0.55f)
                    throw new Exception($"handprint '{mark.name}': the mortar grooves eat {frac * 100f:F1}% "
                                        + "of it — the print is sitting in a joint and there is nothing "
                                        + "left of it.");
                Debug.Log($"[GloomhavenVR][Env] HANDPRINT '{mark.name}' -> atlas tile {mark.tile}: "
                          + $"{mark.side * 1000f:F0} mm card ({mark.side / T * 1000f:F2} mm/texel), "
                          + $"coverage {total / (T * T) * 100f:F2}% of the tile, mortar takes "
                          + $"{frac * 100f:F1}% of it — {mark.why}.");
            }
            float all = allEaten / Mathf.Max(allTotal, 1f);
            if (all < 0.02f || all > 0.45f)
                throw new Exception($"the mortar grooves eat {all * 100f:F1}% of the handprints taken "
                                    + "together. Under 2% means the cavity field is not aligned with the "
                                    + "wall they are on at all (check EnvRoomBuilder's WallW UV arguments "
                                    + "against WallUvScale/WallWUOff/CellarHalfDepth); over 45% means the "
                                    + "detector is firing on the stones and not on the joints.");
            Debug.Log($"[GloomhavenVR][Env] HANDPRINTS: the masonry's own mortar takes {all * 100f:F1}% of "
                      + "the three prints taken together — a hand pressed to a block wall marks the FACES "
                      + "and skips the recesses, and that one multiply is what makes it read as pressed "
                      + "into the stone rather than laid over it.");
            return outTiles;
        }

        /// <summary>ONE EYE. The shader places it TWICE, at two different sizes,
        /// two heights and two blink times, so the pair is asymmetric by
        /// construction and one lid can lag the other.
        ///
        /// <para>EYESHINE IS NOT A WHITE DOT. It is the tapetum behind a slit
        /// pupil: a lens-shaped sliver, pointed at both corners, brightest along
        /// the lower rim where the wet lid catches, with the pupil cutting a dark
        /// line up the middle. The previous pass used two round dots, which is the
        /// single most cartoonish choice available — so nothing here is round and
        /// nothing is level.</para></summary>
        private static HauntTileData HauntEye(int seed = 53)
        {
            int T = HauntTile;
            var cov = new float[T * T];
            var lit = new float[T * T];
            for (int iy = 0; iy < T; iy++)
                for (int ix = 0; ix < T; ix++)
                {
                    int i = iy * T + ix;
                    float x = (ix + 0.5f) / T * 2f - 1f, y = (iy + 0.5f) / T * 2f - 1f;
                    float xr = x + 0.16f * y;                    // canted, never level
                    float q = 1f - (xr / 0.72f) * (xr / 0.72f);
                    float top = 0.30f * q;
                    float bot = -0.20f * Mathf.Pow(Mathf.Max(q, 0f), 0.75f);
                    float inside = HSStep(0.02f, -0.02f, y - top)
                                 * HSStep(-0.02f, 0.02f, y - bot)
                                 * HSStep(0.76f, 0.68f, Mathf.Abs(xr));
                    float sh = inside * (0.40f + 0.85f * HSStep(0.26f, -0.10f, y - bot * 0.2f));
                    sh *= 0.55f + 0.85f * HFbm(xr * 5f, y * 5f, 3, seed);
                    float slit = HSStep(0.075f, 0.020f, Mathf.Abs(xr + 0.06f - 0.12f * y));
                    sh *= 1f - 0.92f * slit * inside;
                    sh = Mathf.Clamp01(sh * 1.5f);
                    float c = Mathf.Clamp01(inside * 0.95f + sh * 0.6f)
                              * HSStep(1.00f, 0.90f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
                    cov[i] = c; lit[i] = sh * c;
                }
            var fill = new float[T * T];
            for (int i = 0; i < fill.Length; i++) fill[i] = lit[i] * 0.25f;
            return new HauntTileData { key = lit, rim = new float[T * T], fill = fill, cov = cov };
        }

        /// <summary>Bake all ten apparitions into one atlas.
        ///
        /// <para>THE PLACEMENTS THESE ARE LIT FOR are the reason each tile's key
        /// direction is what it is. The cellar's candles are low and warm and sit
        /// on the floor and the table, so its heads are lit from BELOW; the
        /// forest's only light is the moon on MoonDir, so its figures carry a cold
        /// rim down the side that faces it and are otherwise a hole in the wood.
        /// The card supplies both light COLOURS at draw time (vertex COLOR for the
        /// key, TEXCOORD4 for the fill), so a tile can serve a warm room and a
        /// cold one without being baked twice.</para></summary>
        private static Color[] MakeHauntAtlas()
        {
            int T = HauntTile, W = T * HauntAtlasCols, H = T * HauntAtlasRows;
            var px = new Color[W * H];

            var faceBase = new HauntFaceCfg
            {
                seed = 11, lean = 0.06f, jaw = 1f, hairSide = -1f, mouthOpen = 0.030f,
                scale = 1f, drop = 0f, lookUp = 0f, shoulders = 0f, shoulderTilt = 0.05f,
                // KEY FROM BELOW-RIGHT and HAIR ON THE LEFT, because of which side
                // of the head comes out from behind the shelf. A card slides toward
                // its +u, so the +x side of the tile is the side the player sees
                // first — and the first bake lit the OTHER one, so what emerged was
                // the back of a head with three strands of hair over it. The lit
                // cheek has to be the part that clears the post.
                key = new Vector3(0.50f, -0.85f, 0.13f), fill = new Vector3(-0.90f, 0.10f, 0.24f),
                keyPow = 1.15f, keyGain = 2.30f, fillPow = 3.2f, fillGain = 3.4f,
                hair = 1f,
            };

            var tiles = new HauntTileData[HauntAtlasCols * HauntAtlasRows];

            // [0] the cellar's head: lit from BELOW-LEFT by a candle it is nowhere near.
            tiles[HTileFaceCellar] = HauntFace(faceBase);

            // [1] the forest's head: the moon RAKES it from the right, |z| = 0.055,
            // so what you get is one bright edge and a black hole where a face is.
            var ff = faceBase;
            ff.seed = 77; ff.lean = -0.09f; ff.jaw = 1.10f; ff.hairSide = -1f;
            ff.mouthOpen = 0.055f;
            ff.key = new Vector3(0.93f, 0.34f, 0.055f); ff.keyPow = 1.9f; ff.keyGain = 3.4f;
            ff.fill = new Vector3(-0.62f, -0.70f, 0.14f); ff.fillPow = 2.4f; ff.fillGain = 0.8f;
            tiles[HTileFaceForest] = HauntFace(ff);

            // [2] the thing at the barred window: head AND shoulders, backlit by
            // the moon behind it, framed by the opening.
            var bu = faceBase;
            bu.seed = 97; bu.lean = -0.05f; bu.jaw = 0.92f; bu.hairSide = -1f;
            bu.mouthOpen = 0.020f; bu.scale = 0.62f; bu.drop = -0.30f;
            bu.key = new Vector3(0.62f, 0.34f, 0.10f); bu.keyPow = 2.6f; bu.keyGain = 3.2f;
            bu.fill = new Vector3(-0.70f, -0.30f, 0.16f); bu.fillPow = 2.8f; bu.fillGain = 0.9f;
            bu.shoulders = 0.62f; bu.shoulderTilt = 0.22f;
            tiles[HTileBust] = HauntFace(bu);

            // [3] the head at FLOOR level, looking up. Head height for a standing
            // adult is the least frightening option available and it is what the
            // first pass shipped; a face on the floor is not.
            var lf = faceBase;
            lf.seed = 131; lf.lean = 0.16f; lf.jaw = 0.78f; lf.hairSide = 1f;
            lf.mouthOpen = 0.060f; lf.lookUp = 0.55f; lf.scale = 0.80f; lf.drop = 0.10f;
            lf.key = new Vector3(-0.42f, 0.80f, 0.24f); lf.keyPow = 1.4f; lf.keyGain = 1.9f;
            lf.hair = 0.20f;
            lf.fill = new Vector3(0.88f, -0.24f, 0.18f); lf.fillPow = 2.8f; lf.fillGain = 1.4f;
            tiles[HTileLowFace] = HauntFace(lf);

            // [4] THE WATCHER — too tall, too thin, head turned, arms past the knee.
            tiles[HTileWatcher] = HauntFigure(new[]
            {
                new HauntLimb(0.055f, 0.900f, 0.038f, 0.790f, 0.098f, 0.082f),   // head, narrow, turned
                new HauntLimb(0.030f, 0.800f, 0.018f, 0.720f, 0.046f, 0.060f),   // a long neck
                new HauntLimb(0.018f, 0.675f, -0.020f, 0.190f, 0.215f, 0.185f),  // shoulders -> waist
                new HauntLimb(-0.020f, 0.200f, -0.050f, -0.955f, 0.190f, 0.310f),// robe to the ground
                new HauntLimb(-0.185f, 0.690f, -0.290f, -0.340f, 0.062f, 0.034f),// arms, far too long
                new HauntLimb(0.200f, 0.700f, 0.320f, -0.270f, 0.058f, 0.030f),
            }, 41, new Vector3(0.90f, 0.24f, 0.12f), 2.4f, 3.0f,
               new Vector3(-0.80f, -0.20f, 0.30f), 0.9f, 3.0f, hem: 0.16f, hemY: -0.72f);

            // [5] the thing that crosses the stair doorway. ITS HEAD IS ABOVE THE
            // CARD: what walks past is a body whose top you never see, which is a
            // great deal worse than a body you can measure.
            tiles[HTileTallFig] = HauntFigure(new[]
            {
                new HauntLimb(0.02f, 1.18f, 0.00f, 0.99f, 0.120f, 0.105f),       // head, cropped
                new HauntLimb(0.00f, 1.00f, -0.03f, 0.34f, 0.235f, 0.205f),
                new HauntLimb(-0.03f, 0.36f, -0.05f, -0.98f, 0.210f, 0.300f),
                new HauntLimb(-0.235f, 0.92f, -0.330f, -0.360f, 0.066f, 0.036f),
                new HauntLimb(0.245f, 0.94f, 0.355f, -0.300f, 0.062f, 0.034f),
            }, 59, new Vector3(0.86f, 0.30f, 0.14f), 2.0f, 3.2f,
               new Vector3(-0.80f, -0.20f, 0.30f), 0.6f, 3.0f,
               hem: 0.12f, hemY: -0.80f, margin: 1.20f);

            // [6] HUNG BY THE FEET. Head at the BOTTOM with hair falling off it —
            // that hair is what reads as 'upside down' at 15 m when nothing else can.
            tiles[HTileHang] = HauntFigure(new[]
            {
                new HauntLimb(0.02f, -0.58f, 0.00f, -0.76f, 0.098f, 0.112f),     // head, lowest
                new HauntLimb(0.02f, -0.44f, 0.02f, -0.58f, 0.050f, 0.062f),     // neck: the narrow part
                new HauntLimb(0.00f, 0.16f, 0.02f, -0.44f, 0.230f, 0.150f),      // shoulders -> waist
                new HauntLimb(0.00f, 0.64f, 0.00f, 0.16f, 0.130f, 0.150f),       // hips
                new HauntLimb(-0.070f, 0.62f, -0.105f, 1.02f, 0.088f, 0.055f),   // legs, up to the branch
                new HauntLimb(0.075f, 0.62f, 0.110f, 1.02f, 0.086f, 0.053f),
                new HauntLimb(-0.215f, -0.24f, -0.330f, -0.96f, 0.080f, 0.042f), // arms hanging PAST the head
                new HauntLimb(0.225f, -0.20f, 0.360f, -0.90f, 0.078f, 0.040f),
                new HauntLimb(-0.055f, -0.72f, -0.085f, -0.99f, 0.075f, 0.030f), // hair
                new HauntLimb(0.060f, -0.70f, 0.095f, -0.99f, 0.070f, 0.028f),
            }, 67, new Vector3(0.88f, -0.24f, 0.14f), 2.2f, 3.0f,
               new Vector3(-0.80f, -0.20f, 0.30f), 0.9f, 3.0f,
               hem: 0.30f, hemY: -0.66f, hemDir: 1f);

            // [7] THE MASS. No features at all — one shoulder much higher than the
            // other, a head that is barely one and far off centre. Its whole event
            // is that it blots out what is behind it.
            tiles[HTileLoom] = HauntFigure(new[]
            {
                // The head is SUNK into the shoulder line, not perched on it: the
                // first attempt put a round bump between two round shoulders and
                // the silhouette read as a bear. What is wanted is a mass with a
                // slope on it that you only later realise has a head in it.
                new HauntLimb(-0.34f, 0.56f, -0.24f, 0.40f, 0.150f, 0.230f),
                new HauntLimb(-0.44f, 0.44f, 0.46f, 0.16f, 0.240f, 0.180f),
                new HauntLimb(0.02f, 0.30f, -0.06f, -0.99f, 0.500f, 0.760f),
            }, 83, new Vector3(0.92f, 0.18f, 0.10f), 0.55f, 4.5f,
               new Vector3(-0.80f, -0.20f, 0.30f), 0.25f, 3.0f,
               ragged: 0.085f, hem: 0.10f, hemY: -0.90f);

            var hands = HauntHandTiles();
            for (int i = 0; i < HandMarks.Length; i++) tiles[HandMarks[i].tile] = hands[i];
            tiles[HTileEye] = HauntEye();

            // ---- compose. Unused cells stay fully transparent black, which is
            // also what a bilinear tap that strays over a tile border must find.
            for (int t = 0; t < tiles.Length; t++)
            {
                if (tiles[t].cov == null) continue;
                int cx = (t % HauntAtlasCols) * T, cy = (t / HauntAtlasCols) * T;
                for (int iy = 0; iy < T; iy++)
                    for (int ix = 0; ix < T; ix++)
                    {
                        int si = iy * T + ix;
                        // A FIVE-TEXEL GUARD BAND round every cell, forced to zero.
                        // Trilinear filtering at distance averages a neighbourhood
                        // that is several texels wide at low mips, and an atlas is
                        // not self-clamping: without this, a watcher seen at 16 m
                        // would pick up a smear of whatever tile sits beside it.
                        // The shader insets its lookup as well — two independent
                        // defences, because the failure mode is subtle and would
                        // only ever show up on hardware.
                        float g = (ix < 5 || iy < 5 || ix >= T - 5 || iy >= T - 5) ? 0f : 1f;
                        px[(cy + iy) * W + (cx + ix)] = new Color(
                            tiles[t].key[si] * g, tiles[t].rim[si] * g,
                            tiles[t].fill[si] * g, tiles[t].cov[si] * g);
                    }
            }
            Debug.Log($"[GloomhavenVR][Env] HAUNT ATLAS baked — {W}x{H} RGBA32, {HauntAtlasCols}x{HauntAtlasRows} "
                      + $"cells of {T}, 12 used. R = key value, G = rim band, B = fill value, A = coverage — "
                      + "except the three photo-derived HANDPRINT tiles, whose R and B are the two weights of a blood THICKNESS RAMP and whose G is a wet specular mask (see HauntHandTiles). "
                      + "Uncompressed on purpose: the three RGB channels are independent masks and BC3 "
                      + "encodes RGB as one interpolated pair per block.");
            return px;
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

        // ======================================================== FIRE ATLAS ====
        // FIRE REAL. USER VERDICT, hardware, ModBuild 144: "Das Feuer im Keller
        // sieht eher aus wie viele Kerzenflammen statt wirklich ein bedrohliches
        // Brennen der Möbel!"
        //
        // WHY A NEW SPRITE IS THE FIRST OF THE THREE FIXES, and not a tuning. The
        // cellar's six fires were drawn with `candle_flame_alb` — a photograph of
        // ONE CANDLE FLAME. A candle flame is laminar: a single smooth teardrop
        // with a closed, continuous silhouette and no internal structure, because
        // at two centimetres the flow never goes turbulent. Enlarging that to half
        // a metre does not make a fire; it makes a large candle flame, and eleven
        // of them side by side make eleven large candle flames. That is what he
        // photographed, word for word.
        //
        // What a burning object's flame actually looks like, and what each cell
        // below is drawn to be:
        //   0 BED     the seat. WIDER THAN TALL (about 2:1 of the drawn mass),
        //             dense and near-solid across its middle, ragged along its
        //             top, thinning to nothing at its ends. Several of these
        //             overlap additively into the one continuous incandescent
        //             body a fire has and a candle does not.
        //   1,2 TONGUE two different tongues. Tapered but NOT closed: each one is
        //             cut by holes and its edge is torn, so two overlapping cards
        //             merge into one mass instead of reading as two objects with
        //             outlines. That is the single most important difference from
        //             the candle sprite, and it is why there are two of them —
        //             one silhouette repeated a dozen times is a pattern.
        //   3 PUFF    a piece that has torn off: a lopsided ragged blob with no
        //             stem at all. Its whole job is to not be attached.
        //
        // ALPHA ONLY (RGB is white). All colour comes from the shader's three-stop
        // temperature ramp, which is measured up the whole FIRE rather than up the
        // card — so one bed sprite is white-blue at the seat of a big fire and
        // orange at the seat of a small one, out of one texture.
        //
        // COMPRESSED, unlike the haunt atlas: this is a single channel that varies
        // smoothly, i.e. the best case for BC3's alpha block rather than the worst
        // case its RGB blocks are. 512x512 with mips is 170 KiB.
        public const int FireTile = 256;
        public const int FireAtlasCols = 2, FireAtlasRows = 2;

        private static Color[] MakeFireAtlas()
        {
            int W = FireTile * FireAtlasCols, H = FireTile * FireAtlasRows;
            var px = new Color[W * H];

            // HLSL's smoothstep(edge0, edge1, x), which is NOT Unity's
            // Mathf.SmoothStep(from, to, t) — that one is a LERP FROM `from` TO
            // `to` with a smoothed t, and the two have the same three arguments in
            // the same order. The first bake of this atlas wrote
            // Mathf.SmoothStep(0f, 0.055f, v), meaning "fade in over the bottom
            // 5%", and got back a number that never exceeds 0.055: the bed came
            // out at an alpha of 3/255 and was invisible, so the fire in the
            // preview was tongues alone — which is precisely the picture this
            // round exists to stop producing. The rest of this file uses the
            // Unity form correctly and always with an InverseLerp inside it; a
            // named local is cheaper than remembering which is which.
            float Ss(float e0, float e1, float x)
                => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e0, e1, x));

            // A tongue: `taper` is how fast it narrows toward the tip, `lean`
            // bends its axis, `seed` picks the tears. u,v are 0..1 inside the cell
            // with v = 0 at the base.
            float Tongue(float u, float v, float taper, float lean, int seed)
            {
                // the axis wanders, so the tongue is not an axis-aligned spindle
                float axis = 0.5f + lean * v * v
                             + 0.13f * (Noise3(v * 3.1f, seed * 0.13f, 2.2f, seed) - 0.5f);
                // FAT LOW, CLOSING HIGH. A candle flame is widest at its middle and
                // perfectly symmetric about its axis; a tongue of fire is widest
                // near where it is fed and its two sides are not the same shape.
                // So the half-widths left and right are drawn from two independent
                // noises — that asymmetry is most of what separates this
                // silhouette from a teardrop, and it costs one extra lookup.
                float wdt = 0.30f * (0.38f + 0.62f * Ss(0f, 0.13f, v))
                            * Mathf.Pow(Mathf.Clamp01(1f - v), taper);
                float wl = wdt * (0.72f + 0.58f * Noise3(v * 4.6f, 1.7f, seed * 0.21f, seed + 11));
                float wr = wdt * (0.72f + 0.58f * Noise3(v * 4.6f, 5.3f, seed * 0.21f, seed + 23));
                float s = u - axis;
                float d = s < 0f ? -s / Mathf.Max(wl, 1e-4f) : s / Mathf.Max(wr, 1e-4f);
                // NO PLATEAU. The obvious profile — opaque out to half the width,
                // then an edge — puts a region of alpha 1 down the tongue's spine,
                // and in an additive pass a region of alpha 1 clips to white with
                // a STRAIGHT SIDE. The third bake's crate fire had two of those in
                // it and they read as two white candles standing in the flames,
                // which is the exact word the user used about the whole feature.
                // Falling from the axis outward with no flat top costs nothing and
                // there is no straight edge left anywhere in the sprite.
                float body = 1f - Ss(0.08f, 1.06f, d);
                // TORN, in two octaves. The coarse one eats bites out of the
                // silhouette; the fine one cuts HOLES through the body, and the
                // holes are the important half: they are what lets two overlapping
                // cards merge into one mass instead of showing two outlines.
                // Without them the silhouette is an analytic curve, which is the
                // grammar of a sprite rather than of a flame.
                body *= 0.28f + 0.72f * Ss(0.30f, 0.62f,
                    Fbm3(new Vector3(u * 3.5f, v * 6.5f - 2f, seed * 0.7f), 4, seed));
                body *= 0.42f + 0.58f * Ss(0.34f, 0.70f,
                    Fbm3(new Vector3(u * 9f, v * 13f, seed * 1.9f), 3, seed + 77));
                // the base is fed and the tip is dying
                body *= Mathf.Lerp(1f, 0.20f, Ss(0.40f, 1.0f, v));
                // ...and it is not cut off flat at the very bottom. 10% of the
                // card, not the first bake's 3.5%: a tongue whose base is inside
                // the bed can afford a long fade, and one that is climbing a
                // trunk with no bed under it at all (bedFrac 0) has nothing else
                // to hide a straight bottom edge behind.
                body *= Ss(0f, 0.10f, v);
                return Mathf.Clamp01(body * 1.60f);
            }

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int cx = x / FireTile, cy = y / FireTile;
                    int cell = cy * FireAtlasCols + cx;
                    float u = (x % FireTile + 0.5f) / FireTile;
                    float v = (y % FireTile + 0.5f) / FireTile;
                    float a;
                    if (cell == 0)
                    {
                        // THE BED. The drawn mass fills the cell across and sits in
                        // its lower half, so a card whose quad is twice as wide as
                        // it is tall carries a mass of roughly the right aspect
                        // without the sprite being stretched.
                        // SOFT ALL ROUND. Every straight edge in this cell becomes
                        // a straight edge in the picture the moment two beds
                        // overlap and the sum clips — the second bake's fires had
                        // visible white parallelograms in them for exactly that
                        // reason, and half of that was the energy and half was
                        // this: a fade over 24% of the width and 12% of the height
                        // costs nothing and there is no card edge left to see.
                        float across = 1f - Ss(0.18f, 0.50f, Mathf.Abs(u - 0.5f));
                        // solid to about a third of the cell, then a ragged top
                        float top = 0.34f + 0.20f * Fbm3(new Vector3(u * 5.0f, 1.3f, 0.7f), 3, 6101);
                        float up = 1f - Ss(top * 0.45f, top, v);
                        // ...and it is not flat underneath either: a bed sits INTO
                        // whatever it is burning on
                        float under = Ss(0f, 0.12f, v);
                        float lump = 0.55f + 0.75f * Fbm3(new Vector3(u * 5.5f, v * 7f, 3.3f), 4, 6102);
                        // gaps, so that several overlapping beds are a glowing MASS
                        // and not a flat slab of light
                        float gap = 0.52f + 0.66f * Ss(0.30f, 0.68f,
                            Fbm3(new Vector3(u * 11f, v * 9f, 8.1f), 3, 6103));
                        // DENSE. This is the brightest thing in the room and it has
                        // to be nearly opaque in its middle, or several overlapping
                        // beds still add up to a haze.
                        a = Mathf.Clamp01(across * up * under * Mathf.Clamp01(lump) * gap * 1.5f);
                    }
                    else if (cell == 1) a = Tongue(u, v, 1.30f, 0.16f, 6201);
                    else if (cell == 2) a = Tongue(u, v, 0.80f, -0.21f, 6301);
                    else
                    {
                        // THE PUFF: lopsided, ragged, no stem. Its centre is off
                        // the cell's centre on purpose — a detached piece of fire
                        // that is radially symmetric reads as a spark.
                        float dx = (u - 0.47f) * 2.10f, dy = (v - 0.55f) * 1.75f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float warp = 0.50f * (Fbm3(new Vector3(u * 3.8f, v * 3.8f, 5.1f), 3, 6401) - 0.5f);
                        float blob = 1f - Ss(0.26f, 0.95f, r + warp);
                        float grain = 0.30f + 0.95f * Ss(0.28f, 0.72f,
                            Fbm3(new Vector3(u * 8f, v * 8f, 1.9f), 4, 6402));
                        a = Mathf.Clamp01(blob * grain * 1.25f);
                    }
                    px[y * W + x] = new Color(1, 1, 1, a);
                }
            return px;
        }

        /// <summary>Alpha-test threshold for the cobwebs. Shared by the STRAND
        /// texture's mip-coverage setting below, by the imported cobweb alpha's
        /// (EnvRoomBuilder.CoverageCutoff) and by both web materials' `_Cutoff`.
        /// They are the same number or the threads disappear at distance —
        /// mip-coverage preservation is defined relative to a threshold, and a
        /// material that clips at a different one gets no benefit from it.
        /// 0.09 -> 0.12 with the photoscanned alpha, whose threads reach 1.0
        /// where the procedural one's peaked around 0.4.</summary>
        public const float WebCutoff = 0.12f;

        /// <summary>Three loose gossamer strands, side by side in one strip, so a
        /// hanging thread can pick a column and not look like its neighbours.
        /// u across a column, v from the anchor (0) to the free end (1).
        ///
        /// Each is a single sinuous thread of the same weight as one thread of
        /// the photoscanned web, with the SNAGGED DUST a hanging strand collects
        /// (small bright nodules, denser toward the bottom, which is what makes
        /// a loose strand read as old and catch the candlelight) and a short
        /// forked tail on one of them.</summary>
        private static Color[] MakeStrand(int w, int h)
        {
            var px = new Color[w * h];
            int cols = 3, cw = w / cols;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = Mathf.Min(x / cw, cols - 1);
                    float u = (x - c * cw + 0.5f) / cw;        // 0..1 within the column
                    float v = (y + 0.5f) / h;                  // 0 anchor .. 1 free end
                    int sd = 4100 + c * 37;
                    // the thread wanders: two slow sines plus noise, amplitude
                    // growing toward the free end (the anchor cannot move)
                    float amp = 0.06f + 0.26f * v * v;
                    float cx = 0.5f + amp * (0.62f * Mathf.Sin(v * 4.1f + c * 2.3f)
                                             + 0.38f * Mathf.Sin(v * 9.7f + c * 1.1f))
                             + 0.10f * (Noise3(v * 3.3f, c * 1.7f, 0.5f, sd) - 0.5f);
                    float tw = 0.016f * (1f - 0.35f * v);      // ~3 px at 64 wide
                    float d = Mathf.Abs(u - cx);
                    float a = Mathf.Exp(-(d * d) / (tw * tw));
                    // one strand forks near the bottom
                    if (c == 2 && v > 0.55f)
                    {
                        float fx = cx + 0.22f * (v - 0.55f);
                        float df = Mathf.Abs(u - fx);
                        a = Mathf.Max(a, 0.85f * Mathf.Exp(-(df * df) / (tw * tw)));
                    }
                    // snagged dust: bright nodules strung along the thread
                    for (int k = 0; k < 7; k++)
                    {
                        float kv = Hash01(k * 13 + c * 5 + 3, 4201);
                        float nv = 0.12f + 0.84f * kv;
                        if (Hash01(k * 29 + c * 7, 4211) < 0.35f) continue;
                        float nr = 0.030f + 0.045f * Hash01(k * 11 + c, 4217);
                        float ncx = 0.5f + amp * (0.62f * Mathf.Sin(nv * 4.1f + c * 2.3f)
                                                  + 0.38f * Mathf.Sin(nv * 9.7f + c * 1.1f));
                        float dx = (u - ncx) * 1.6f, dy = (v - nv) * (h / (float)cw) * 1.6f;
                        a = Mathf.Max(a, Mathf.Exp(-(dx * dx + dy * dy) / (nr * nr)));
                    }
                    // thins and frays out at the free end; solid at the anchor
                    a *= Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.86f, 1f, v));
                    a *= 0.70f + 0.45f * Fbm3(new Vector3(u * 3.0f, v * 7.0f, c * 2.1f), 3, 4231);
                    px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(a * 1.6f));
                }
            return px;
        }

        private static float Hash01(int k, int seed) => Noise3(k * 0.731f, seed * 0.013f, 1.7f, seed);

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

        /// <summary>THE DRAUGHT'S CARRIED MATTER — a soft, irregular filament.
        ///
        /// <para>USER VERDICT, ModBuild 143 (verbatim): "Bei der Luft finde ich die
        /// Idee gut, dass es aus dem Fenster kommt, sollte aber auch wirklich mehr
        /// wie Wind wirken, aktuell diese Pünktchen erinnern eher an weiße Funken,
        /// das ist nicht immersiv oder realistisch."</para>
        ///
        /// <para>He is describing Env_Spark, and he is right about it. That sprite
        /// is a tight gaussian CORE with a soft skirt — a point of light. Stretched
        /// along a 1.3 m/s velocity it elongates by about eight centimetres, which
        /// at four metres is under a sprite width: it stays a dot, it stays bright
        /// in the middle, and a bright dot on black in a dark room is a spark. No
        /// amount of tinting or dimming fixes that, because what reads as "spark"
        /// is the CONCENTRATION, not the colour.</para>
        ///
        /// <para>So this is the opposite sprite by construction. There is no core:
        /// the profile along the filament is a broad, flat-topped hump with the
        /// peak alpha barely over half, torn by two octaves of noise so no two
        /// motes are the same shape, and it tapers to nothing at BOTH ends. It is
        /// four times as long as it is wide before the renderer stretches it at
        /// all, so a mote in the cellar's draught is a 20-40 cm hair of dust
        /// rather than a point — extent and irregularity, which is what the eye
        /// separates carried matter from sparks by.</para>
        ///
        /// <para>SYMMETRIC ABOUT v = 0.5, exactly as MakeStreak is and for the same
        /// reason: in Stretch render mode the sprite may roll about its own
        /// velocity axis, and a filament that is symmetric about that axis cannot
        /// show the roll. (It is deliberately NOT symmetric along u — a real mote
        /// is lopsided — but u-asymmetry is invisible under roll.)</para></summary>
        private static Color[] MakeWisp(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float sy = (v - 0.5f) * 2f;                       // -1..1
                    // ALONG the filament: a wide flat hump, not a head. The
                    // exponent 0.55 on the sine is what flattens the top — a
                    // plain sin would peak in the middle and read as a bead.
                    float along = Mathf.Pow(Mathf.Max(Mathf.Sin(u * Mathf.PI), 0f), 0.55f);
                    // ...torn: two octaves of noise on the length, so the mote is
                    // uneven and no two are alike (the noise is seeded, so every
                    // client bakes the same sprite).
                    float tear = 0.62f + 0.38f * Noise3(u * 6.5f, 1.7f, 0.4f, 4471)
                                       + 0.22f * (Noise3(u * 17f, 5.1f, 2.3f, 4472) - 0.5f);
                    along *= Mathf.Clamp01(tear);
                    // ACROSS it: soft, and it THINS toward the ends, so the thing
                    // has a shape instead of being a bar with rounded caps.
                    float wdt = Mathf.Lerp(0.34f, 1.0f, along);
                    float across = Mathf.Exp(-(sy * sy) / (wdt * wdt));
                    // 0.55 peak, not 1.0: this is dust seen by a moonbeam, and it
                    // is meant to be at the edge of legibility. The emitter's own
                    // start colour dims it further.
                    float a = 0.55f * along * across;
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
            // small disc => plenty of sprite left for the halo. The constant is
            // MoonSpriteDiscR, not a literal: EnvStars needs the same number to
            // measure the eclipse's umbra against the disc (_MoonDiscR).
            const float R = MoonSpriteDiscR;
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

        /// <param name="mipCoverage">If >= 0, preserve alpha COVERAGE through the
        /// mip chain against this cutout threshold. Without it a texture of thin
        /// alpha threads (the cobweb) simply vanishes at distance: box-filtering
        /// a 3-pixel thread into a 1-pixel one divides its alpha by three, the
        /// whole mip drops under the material's _Cutoff, and every fragment is
        /// clipped. Must match the material's _Cutoff.</param>
        private static void WritePng(string path, Color[] px, int w, int h, bool sRGB, bool clamp,
            bool clampV = false, bool mips = true,
            TextureImporterCompression comp = TextureImporterCompression.Compressed,
            bool alphaDilate = true, float mipCoverage = -1f)
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
            ti.mipMapsPreserveCoverage = mipCoverage >= 0f;
            if (mipCoverage >= 0f) ti.alphaTestReferenceValue = mipCoverage;
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
            // THE HALO SHELL. Its own builder, and not BuildSphere(..., inward:
            // false), because that call was WOUND INSIDE OUT and every EnvGlow
            // halo in both rooms was drawing its own far shell — see
            // BuildGlowSphere and AssertGlowShellSeenFromOutside below.
            SaveMesh(MeshDir + "/Env_GlowSphere.asset", BuildGlowSphere(16, 8));
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

        // ======================================================== THE HALO SHELL
        // WHICH SIDE THIS MESH MUST BE SEEN FROM: from OUTSIDE. Every one of its
        // faces is wound so that its own geometric normal points away from the
        // centre, and EnvGlow draws it `Cull Back`. That sentence is the whole
        // reason this function exists instead of a fourth call to BuildSphere.
        //
        // WHAT WENT WRONG, and it is the winding class this project has now paid
        // for five times (the moonbeam hull, the rat's body, the puddle, the two
        // growth meshes). `BuildSphere(..., inward: false)` emits its faces as
        // {a,c,b} / {b,c,d}, whose cross product points at the CENTRE — i.e. the
        // "outward" sphere was wound to be seen from the inside. Under
        // EnvGlow's `Cull Back` the near hemisphere was therefore culled and the
        // FAR one rasterised, and on the far shell the vertex normal points away
        // from the eye, so `dot(N,V)` is negative over the whole disc and
        // EnvGlow's falloff is exactly zero. Every halo in both rooms — the
        // candle scheines, the window, the wisps, the lantern, the rat's
        // eyeshines and all nine fire halos — has been drawing NOTHING except a
        // one-pixel seam at the geometric limb, where the interpolated normal
        // crosses zero. That seam is the silhouette of a 16x8 UV sphere: a chain
        // of straight segments, near-vertical where the meridian edges run and a
        // stepped cap over each pole. It is the user's "mehrere sichtbare
        // Striche" (ModBuild 147, feuer1.jpg) and the stepped block in the wood's
        // misty gap, and it is gated on Fire for two independent reasons — the
        // nine fire halos carry _ElemGate=1 and so exist only under Fire, and
        // Fire is also the only mood that lifts the seam above the noise floor.
        //
        // ...AND THE SHELL CIRCUMSCRIBES THE UNIT SPHERE. EnvGlow now evaluates
        // its falloff ANALYTICALLY against the object-space unit sphere (see
        // EnvGlow.shader) rather than off the interpolated normal, so the mesh's
        // only remaining job is to COVER that sphere's silhouette. An inscribed
        // polyhedron does not: its limb sits inside the true limb, so the halo
        // would be cut off at pow(sin(11.25 deg), falloff) instead of at zero —
        // a hard stepped rim, which is the same artefact by another route. Every
        // vertex is therefore pushed out by 1/(closest face-plane distance), the
        // smallest factor for which no face plane cuts the sphere. The halo's
        // world size is unchanged (the analytic sphere is still radius 1 in
        // object space); what grows is the rasterised hull, by k^2 - 1 = 7.9 %
        // of fill at 16x8, all of it in an annulus the shader takes to zero.
        //
        // Vertex and triangle counts are identical to the mesh this replaces:
        // 153 vertices, 256 triangles. Nothing here costs a vertex.
        private static Mesh BuildGlowSphere(int lon, int lat)
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
            // {a,b,c} / {b,d,c}: the order whose cross product comes out ALONG the
            // radius, i.e. the face is seen from outside. The mirror order is the
            // one that shipped, and the self-test below builds exactly it.
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    t.AddRange(new[] { a, b, c, b, d, c });
                }
            // push the hull out until it encloses the unit sphere
            float k = 1f / GlowShellClosestPlane(v, t);
            var vs = v.Select(p => p * k).ToList();

            var m = new Mesh();
            m.SetVertices(vs);
            m.SetUVs(0, uv);
            // normals stay the true outward radial direction. EnvGlow no longer
            // reads them (the falloff is analytic), but a mesh whose normals
            // disagreed with its winding is exactly the state this whole comment
            // is about, and the gate checks both.
            m.SetNormals(v.Select(p => p.normalized).ToList());
            m.SetTriangles(t, 0);
            AssertGlowShellSeenFromOutside(m, k, lon, lat);
            return m;
        }

        /// The smallest distance from the centre to any (non-degenerate) face
        /// plane of the unit-radius shell. 1/this is the factor that makes the
        /// hull circumscribe rather than inscribe the sphere.
        private static float GlowShellClosestPlane(List<Vector3> v, List<int> t)
        {
            float min = float.MaxValue;
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-12f) continue;   // the pole fan's degenerate half
                min = Mathf.Min(min, Mathf.Abs(Vector3.Dot(n.normalized, p0)));
            }
            if (min == float.MaxValue)
                throw new Exception("Env_GlowSphere: every face is degenerate.");
            return min;
        }

        private static bool GlowShellGateProven;

        /// <summary>The gate for "this shell is seen from OUTSIDE". Proven to
        /// fire: the first call runs it on the same shell with every triangle
        /// reversed and requires it to throw.</summary>
        private static void AssertGlowShellSeenFromOutside(Mesh m, float k, int lon, int lat)
        {
            if (!GlowShellGateProven)
            {
                GlowShellGateProven = true;
                GlowShellGateSelfTest(lon, lat);
            }
            GlowShellCheck(m, k, "Env_GlowSphere");
            Debug.Log($"[GloomhavenVR][Env] glow-shell gate: 'Env_GlowSphere' PASSED — "
                      + $"{m.vertexCount} vertices, {m.triangles.Length / 3} triangles, every face "
                      + "wound to be seen from OUTSIDE (EnvGlow is Cull Back), every vertex normal "
                      + $"radially outward, and the hull circumscribes the unit sphere (k = {k:F4}, "
                      + $"+{(k * k - 1f) * 100f:F1}% fill) so EnvGlow's analytic falloff reaches zero "
                      + "inside the geometry instead of being cut off at the limb.");
        }

        /// The measurable half, split out so the self-test can run it on a
        /// deliberately reversed shell.
        private static void GlowShellCheck(Mesh m, float k, string name)
        {
            var V = m.vertices; var N = m.normals; var T = m.triangles;
            if (V.Length != N.Length)
                throw new Exception($"Glow shell '{name}': {V.Length} vertices but {N.Length} normals.");
            int bad = 0, faces = 0;
            float minPlane = float.MaxValue;
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 p0 = V[T[i]], p1 = V[T[i + 1]], p2 = V[T[i + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-12f) continue;    // the pole fan's degenerate half
                faces++;
                Vector3 c = (p0 + p1 + p2) / 3f;
                // SEEN FROM OUTSIDE: the face's own normal must point AWAY from
                // the centre, which is the only place this shell is ever looked
                // at from (a camera inside a halo sees nothing today and saw
                // nothing before — Cull Back, both windings).
                if (Vector3.Dot(n.normalized, c) <= 0f) bad++;
                minPlane = Mathf.Min(minPlane, Mathf.Abs(Vector3.Dot(n.normalized, p0)));
            }
            if (bad > 0)
                throw new Exception($"Glow shell '{name}': {bad} of {faces} faces are wound to be "
                                    + "seen from INSIDE. EnvGlow draws this mesh Cull Back from "
                                    + "outside, so those faces are culled and their opposite number "
                                    + "on the far shell is rasterised instead — where the outward "
                                    + "normal points away from the eye, dot(N,V) is negative and the "
                                    + "halo is exactly zero everywhere except a one-pixel seam at the "
                                    + "limb. That seam is the 'Striche' of ModBuild 147.");
            for (int i = 0; i < V.Length; i++)
            {
                if (V[i].sqrMagnitude < 1e-12f) continue;
                if (Vector3.Dot(N[i], V[i].normalized) < 0.999f)
                    throw new Exception($"Glow shell '{name}': vertex {i}'s normal is not the outward "
                                        + "radial direction. A shell whose normals disagree with its "
                                        + "winding is the fault this gate exists for.");
            }
            // ...and the property EnvGlow's analytic falloff depends on: the hull
            // must ENCLOSE the unit sphere, or the halo is cut off at its limb.
            if (minPlane < 1f - 1e-4f)
                throw new Exception($"Glow shell '{name}': the closest face plane is {minPlane:F4} "
                                    + "from the centre, i.e. the hull cuts into the unit sphere "
                                    + "EnvGlow shades against. The halo would end at a hard stepped "
                                    + "rim. Push the vertices out by 1/(closest plane distance).");
            if (k < 1f) throw new Exception($"Glow shell '{name}': hull factor {k} < 1.");
        }

        /// <summary>Proof that the gate fires. Builds the shell with the winding
        /// that actually shipped — {a,c,b} / {b,c,d}, BuildSphere's "outward"
        /// order — and requires the check to reject it.</summary>
        private static void GlowShellGateSelfTest(int lon, int lat)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float latAng = (y / (float)lat - 0.5f) * Mathf.PI;
                float r = Mathf.Cos(latAng), py = Mathf.Sin(latAng);
                for (int x = 0; x <= lon; x++)
                {
                    float lonAng = x / (float)lon * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(lonAng) * r, py, Mathf.Cos(lonAng) * r));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });   // the shipped, inside-out order
                }
            var bad = new Mesh();
            bad.SetVertices(v);
            bad.SetNormals(v.Select(p => p.normalized).ToList());
            bad.SetTriangles(t, 0);
            bool threw = false;
            try { GlowShellCheck(bad, 1f, "<self-test: the shipped winding>"); }
            catch (Exception) { threw = true; }
            UnityEngine.Object.DestroyImmediate(bad);
            if (!threw)
                throw new Exception("Glow-shell gate SELF-TEST FAILED: the check accepted the "
                                    + "inside-out winding that shipped in ModBuild 147. A gate that "
                                    + "cannot reject the fault it was written for is not a gate.");
            Debug.Log("[GloomhavenVR][Env] glow-shell gate self-test: the shipped inside-out "
                      + "winding was REJECTED, so the gate below is load-bearing.");
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

        // ==================================================== ELEMENT ART =====
        /// <summary>Write a particle material's element response (see
        /// EnvParticleAdd.shader and EnvParticleElem.cginc for the two
        /// mechanisms). EVERY argument defaults to the neutral value, so a
        /// material that says nothing behaves exactly as it did before the
        /// element feature existed — which is the property the zero-state proof
        /// rests on.</summary>
        /// <param name="own">Gate weights (fire, ice, air, earth): this emitter
        /// EXISTS FOR that element and its quads collapse while it is down.</param>
        /// <param name="ownLD">Gate weights (light, dark).</param>
        /// <param name="mod">Modulation weights (fire, ice, air, earth): this
        /// emitter exists anyway and merely answers. May be signed.</param>
        /// <param name="modLD">Modulation weights (light, dark). May be signed.</param>
        /// <param name="gain">Brightness response to the modulation.</param>
        /// <param name="alpha">Alpha response to the modulation.</param>
        /// <param name="col">Colour it moves toward under modulation.</param>
        /// <param name="tint">How far it moves (1 = all the way at m = 1).</param>
        /// <param name="spark">Fast positional twinkle amount.</param>
        internal static void ElemFX(Material m,
            Vector4 own = default, Vector2 ownLD = default,
            Vector4 mod = default, Vector2 modLD = default,
            float gain = 0f, float alpha = 0f, Color col = default, float tint = 0f,
            float spark = 0f)
        {
            m.SetVector("_ElemOwn", own);
            m.SetVector("_ElemOwn2", new Vector4(ownLD.x, ownLD.y, 0f, 0f));
            m.SetVector("_ElemMod", mod);
            m.SetVector("_ElemMod2", new Vector4(modLD.x, modLD.y, 0f, 0f));
            m.SetFloat("_ElemGain", gain);
            m.SetFloat("_ElemAlpha", alpha);
            m.SetColor("_ElemCol", col == default ? Color.white : col);
            m.SetFloat("_ElemTintAmt", tint);
            m.SetFloat("_ElemSpark", spark);
        }

        private static void BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);

            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + n);

            var fog = LoadOrNewMat(MatDir + "/FX_Fog.mat", "GloomhavenVR/EnvParticleAlpha");
            fog.SetTexture("_MainTex", T("Env_FogPuff.png"));
            fog.SetColor("_Tint", Color.white);
            // ELEMENT ART — DARK: "ground fog rises and thickens". ONE signed dot
            // product carries both halves of the split: dark - 0.5*light. Denser
            // (alpha +1.7) and at the same time DARKER (brightness -0.55), because
            // ModBuild 134 established that a lit puff over black IS a raised
            // floor — Dark's fog has to swallow the far trunks, not veil them in
            // grey. Under Light the same term goes negative and the air clears,
            // which is what leaves the sources standing alone.
            ElemFX(fog, modLD: new Vector2(-0.5f, 1f), gain: -0.55f, alpha: 1.7f,
                   col: new Color(0.10f, 0.12f, 0.16f), tint: 0.5f);

            var dust = LoadOrNewMat(MatDir + "/FX_Dust.mat", "GloomhavenVR/EnvParticleAlpha");
            dust.SetTexture("_MainTex", T("Env_Spark.png"));
            dust.SetColor("_Tint", Color.white);
            // ELEMENT ART — the cellar's motes already drift along the authored
            // draught (DraftDir), so AIR only has to make them legible; ICE turns
            // the same specks cold and hard, i.e. into what is hanging in the air
            // of a room that has just frozen. No new emitter for either: this is
            // the modulation half of the mechanism doing exactly its job.
            ElemFX(dust, mod: new Vector4(0f, 1.0f, 0.55f, 0f), gain: 0.9f, alpha: 1.1f,
                   col: new Color(0.74f, 0.86f, 1.00f), tint: 0.9f);

            var firefly = LoadOrNewMat(MatDir + "/FX_Firefly.mat", "GloomhavenVR/EnvParticleAdd");
            firefly.SetTexture("_MainTex", T("Env_Glow.png")); // soft bokeh, radially symmetric
            firefly.SetColor("_Tint", Color.white);
            // ELEMENT ART — FIRE: "the fireflies turn to sparks". The swarm keeps
            // its place, its motion and its slow breathing size curve; what
            // changes is that a cold marsh-green bokeh becomes an ember with a
            // fast twinkle on it. Same fourteen particles, no second emitter.
            ElemFX(firefly, mod: new Vector4(1f, 0f, 0f, 0f), gain: 1.3f, alpha: 0.35f,
                   col: new Color(1.00f, 0.45f, 0.12f), tint: 1.0f, spark: 0.55f);

            var streak = LoadOrNewMat(MatDir + "/FX_StarStreak.mat", "GloomhavenVR/EnvParticleAdd");
            streak.SetTexture("_MainTex", T("Env_Streak.png")); // comet head + tapering tail
            streak.SetColor("_Tint", Color.white);
            // A shooting star belongs to the SKY, not to the room's mood, and the
            // sky's own answer to Light/Dark is in EnvStars/EnvStarPoints. Left
            // neutral on purpose.
            ElemFX(streak);

            // ---- ELEMENT ART: the six gated emitters --------------------------
            // Each of these EXISTS FOR one element: while it is down every quad
            // collapses to a point in the vertex shader and nothing is shaded.
            // They are built here, with the other FX materials, and used by
            // EnvRoomBuilder.AddElementFX, which hangs the emitters under RoomGeo
            // — the runtime splits the shell by node name and only RoomGeo's
            // branch is board-anchored (SkyAlternative.RoomBoundShellChildren),
            // so an element emitter parented to the shell root would be scaled
            // like the star dome.
            var ember = LoadOrNewMat(MatDir + "/FX_ElemEmber.mat", "GloomhavenVR/EnvParticleAdd");
            ember.SetTexture("_MainTex", T("Env_Glow.png"));
            ember.SetColor("_Tint", new Color(1f, 0.42f, 0.13f, 1f));
            ElemFX(ember, own: new Vector4(1f, 0f, 0f, 0f), spark: 0f);

            var snow = LoadOrNewMat(MatDir + "/FX_ElemSnow.mat", "GloomhavenVR/EnvParticleAlpha");
            snow.SetTexture("_MainTex", T("Env_Spark.png")); // radially symmetric: billboard-legal
            snow.SetColor("_Tint", new Color(0.86f, 0.92f, 1f, 1f));
            ElemFX(snow, own: new Vector4(0f, 1f, 0f, 0f));

            // Env_Spark, NOT Env_Streak. The first bake used the comet sprite on
            // the driven air and the previews showed exactly what that is: three
            // shooting stars flying sideways through a forest. A radially
            // symmetric DOT stretched along its own velocity is what a mote going
            // past at three metres a second actually looks like — and it is the
            // same sprite, and the same VR argument, as the dust motes.
            var gust = LoadOrNewMat(MatDir + "/FX_ElemGust.mat", "GloomhavenVR/EnvParticleAdd");
            gust.SetTexture("_MainTex", T("Env_Spark.png"));
            gust.SetColor("_Tint", new Color(0.50f, 0.58f, 0.70f, 1f));
            ElemFX(gust, own: new Vector4(0f, 0f, 1f, 0f));

            var spore = LoadOrNewMat(MatDir + "/FX_ElemSpore.mat", "GloomhavenVR/EnvParticleAdd");
            spore.SetTexture("_MainTex", T("Env_Glow.png"));
            spore.SetColor("_Tint", new Color(0.52f, 0.86f, 0.42f, 1f));
            ElemFX(spore, own: new Vector4(0f, 0f, 0f, 1f));

            var sift = LoadOrNewMat(MatDir + "/FX_ElemSift.mat", "GloomhavenVR/EnvParticleAlpha");
            sift.SetTexture("_MainTex", T("Env_Spark.png"));
            sift.SetColor("_Tint", new Color(0.72f, 0.63f, 0.50f, 1f));
            ElemFX(sift, own: new Vector4(0f, 0f, 0f, 1f));

            // THE CELLAR'S DRAUGHT — rebuilt after the ModBuild 143 verdict
            // ("aktuell diese Pünktchen erinnern eher an weiße Funken"). Three
            // changes, and all three are about the same thing, which is that a
            // bright concentrated dot is a spark whatever colour it is:
            //   * Env_Wisp, not Env_Spark: a torn filament with no core, four
            //     times as long as it is wide before the stretch (see MakeWisp);
            //   * the tint goes from 0.80/0.74/0.62 — a warm near-white, i.e. the
            //     colour of an ember — to a cold, dark grey-blue at 0.42 of the
            //     brightness. Dust in a moonbeam is not white, it is the moon's
            //     own colour at a fraction of its intensity;
            //   * and it stays ALPHA-blended, which for once matters: an additive
            //     mote can only ever add light, so it always reads as glowing.
            //     Carried dust OCCLUDES as much as it scatters.
            // The emitters that use it (EnvRoomBuilder.AddElementFX and
            // AddCellarDraught) carry the other half: many more, much longer,
            // much slower to line up, and tumbling.
            var draught = LoadOrNewMat(MatDir + "/FX_ElemDraught.mat", "GloomhavenVR/EnvParticleAlpha");
            draught.SetTexture("_MainTex", T("Env_Wisp.png"));
            draught.SetColor("_Tint", new Color(0.46f, 0.52f, 0.62f, 1f));
            ElemFX(draught, own: new Vector4(0f, 0f, 1f, 0f));

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
            // how much of the sprite is disc — the eclipse's unit of length
            stars.SetFloat("_MoonDiscR", MoonSpriteDiscR);

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
                      + $"alt {Mathf.Asin(MoonDir.y) * Mathf.Rad2Deg:F1} deg), disc radius {MoonDiscRad * Mathf.Rad2Deg:F2} deg "
                      + $"(sprite disc R={MoonSpriteDiscR:F3}), stars culled inside "
                      + $"{Mathf.Acos(Mathf.Cos(MoonDiscRad * 1.04f)) * Mathf.Rad2Deg:F2} deg.");
            LogMoonPhase();

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

                // Drifting dust motes in the candlelight (world-space room volume).
                // ModBuild 135: they now DRIFT, along the same bearing the candle
                // flames lean in (EnvRoomBuilder.DraftDir, in at the window and
                // out under the stair door). A draught you can see in two
                // unrelated places at once is a draught; motes that only jitter
                // are a screensaver.
                var dust = NewPS(t, "DustMotes", new Vector3(0, 1.8f, 0), Vector3.zero, Mat("FX_Dust.mat"));
                var dm = dust.main;
                dm.simulationSpace = ParticleSystemSimulationSpace.World;
                dm.duration = 30f;
                dm.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
                dm.startSpeed = 0f;
                dm.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.012f);
                dm.startColor = new Color(1f, 0.88f, 0.65f, 0.30f); // subtle: barely-there motes
                dm.maxParticles = 34;
                var de = dust.emission; de.rateOverTime = 1.9f;
                var dsh = dust.shape; dsh.enabled = true; dsh.shapeType = ParticleSystemShapeType.Box; dsh.scale = new Vector3(7.5f, 3.0f, 6.5f);
                var dn = dust.noise; dn.enabled = true; dn.strength = 0.05f; dn.frequency = 0.22f; dn.scrollSpeed = 0.12f;
                var dv = dust.velocityOverLifetime; dv.enabled = true;
                dv.space = ParticleSystemSimulationSpace.World;
                dv.x = new ParticleSystem.MinMaxCurve(-0.075f, -0.030f);   // the draught, XZ
                dv.z = new ParticleSystem.MinMaxCurve(-0.038f, -0.014f);
                dv.y = new ParticleSystem.MinMaxCurve(-0.010f, 0.016f);
                var dcol = dust.colorOverLifetime; dcol.enabled = true;
                dcol.color = new ParticleSystem.MinMaxGradient(Grad((0f, new Color(1, 1, 1, 0f)), (0.15f, new Color(1, 1, 1, 1f)), (0.85f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));

                // An occasional settling of dust off the beams: nothing for
                // twenty seconds, then a small fall of grit somewhere overhead.
                // Bursts, not a rate — the point is that it is an EVENT.
                var grit = NewPS(t, "DustFall", new Vector3(1.4f, 3.02f, -1.9f), Vector3.zero, Mat("FX_Dust.mat"));
                var gm = grit.main;
                gm.simulationSpace = ParticleSystemSimulationSpace.World;
                gm.duration = 21f;
                gm.prewarm = false;                       // bursts are incompatible with prewarm
                gm.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.4f);
                gm.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
                gm.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.010f);
                gm.startColor = new Color(1f, 0.90f, 0.72f, 0.30f);
                gm.gravityModifier = 0.055f;
                gm.maxParticles = 26;
                var ge = grit.emission;
                ge.rateOverTime = 0f;
                ge.SetBursts(new[] { new ParticleSystem.Burst(1.5f, 9, 14, 1, 0.01f) });
                var gsh = grit.shape; gsh.enabled = true; gsh.shapeType = ParticleSystemShapeType.Box;
                gsh.scale = new Vector3(0.9f, 0.05f, 0.25f);
                var gn = grit.noise; gn.enabled = true; gn.strength = 0.05f; gn.frequency = 0.6f; gn.scrollSpeed = 0.2f;
                var gcol = grit.colorOverLifetime; gcol.enabled = true;
                gcol.color = new ParticleSystem.MinMaxGradient(Grad(
                    (0f, new Color(1, 1, 1, 0f)), (0.12f, new Color(1, 1, 1, 1f)),
                    (0.6f, new Color(1, 1, 1, 0.7f)), (1f, new Color(1, 1, 1, 0f))));

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
                // Y IS SET DELIBERATELY, and it is set as a TWO-CONSTANT range.
                // The user's Player.log was full of "Particle Velocity curves
                // must all be in the same mode": Shuriken stores one curve mode
                // per module and warns every frame it evaluates a module whose
                // x/y/z disagree. x and z here are TwoConstants; y, never
                // assigned, kept the default Constant — two modes in one module.
                // A range of 0 would be Constant again, so the fix has to be a
                // real range, and a mist bank that breathes a centimetre a
                // second is the right one anyway.
                fv.y = new ParticleSystem.MinMaxCurve(-0.008f, 0.012f);
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
                ffv.y = new ParticleSystem.MinMaxCurve(-0.006f, 0.009f);  // see GroundFog above
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
                // User finding, ModBuild 134: "Genauso wie die Sternschnuppen -
                // die auch gerne aber weiter entfernt und nicht so groß." Two
                // separate changes, and both were needed: the anchor went from
                // 30 m to 40 m (the star dome is at 45 m, so they now happen
                // among the stars instead of over the treetops) and the sprite
                // from 0.45 to 0.21 with the stretch pulled back to match. The
                // ANGULAR size therefore drops by 0.21/0.45 * 30/40 = 0.35x —
                // they read as distant events, not as nearby streaks.
                var meteor = NewPS(t, "ShootingStars", new Vector3(0, 40f, 0), new Vector3(115f, 30f, 0f), Mat("FX_StarStreak.mat"));
                var mm = meteor.main;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                mm.duration = 20f;
                mm.startLifetime = 1.6f;
                mm.startSpeed = new ParticleSystem.MinMaxCurve(26f, 38f); // a touch slower = elegant
                mm.startSize = 0.21f;
                mm.startColor = new Color(0.95f, 0.93f, 0.85f, 0.78f); // warm-white ember
                mm.maxParticles = 4;
                var me = meteor.emission; me.rateOverTime = 0.13f; // infrequent: ~one every 8 s
                var msh = meteor.shape; msh.enabled = true; msh.shapeType = ParticleSystemShapeType.Box;
                msh.scale = new Vector3(44f, 44f, 0.1f); // spawn plane ⟂ travel direction
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
                mr.velocityScale = 0.075f;   // was 0.11: the streak shortens with the head
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
            // User finding, ModBuild 134: "Die Glühwürmchen sind zu groß und
            // gerne dezenter." Size 0.05-0.12 -> 0.032-0.070 m (~40% smaller) and
            // the colour pulled back to 0.80 alpha, so they glimmer between the
            // trunks rather than hanging there as green lamps. Not smaller than
            // this: at 8 m — where the swarms are — 0.03 m is already 4 pixels,
            // and "dezent" must not become "gone".
            m.startSize = new ParticleSystem.MinMaxCurve(0.032f, 0.070f);
            // wisp, not firefly: cold marsh-light green, sparse and slow
            m.startColor = new Color(0.36f, 0.74f, 0.46f, 0.80f);
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
