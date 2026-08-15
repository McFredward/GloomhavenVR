// GloomhavenVR companion project — room-interior assembler for the ambient
// environments (invoked from BuildEnvironments.cs, which owns the FX shells).
//
// User ruling 2026-08-13: "Gehe wieder dazu über mit custom assets etwas zu
// bauen. Aber nicht low-poly sondern zum Styl des Spiels passendes." — the
// rooms are assembled from CC0 photoscanned Poly Haven models + PBR texture
// sets (see Environments/License.md), lit by a fully-baked light rig evaluated
// in the bundled EnvRoom/EnvGround/EnvRoomCutout shaders (no scene lights, no
// scripts, no colliders — Shuriken + shader-_Time animation only).
//
// Layout contract with src/ (SkyAlternative): FX node names are unchanged
// (StarDome/GroundFog/Fireflies/DustMotes/ShootingStars/GlowTemplate); ALL new
// room geometry lives under ONE new child node per prefab named "RoomGeo".
// Under the current splitter unknown nodes ride the sky branch
// (perceived-size-constant) — exactly what the world-place model wants.
// Both rooms have CLOSED opaque floors around the origin (hard user
// requirement after the see-through floor of the 130 round), floor at y=0,
// and a free 1.5 m radius at the origin for the play space.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class EnvRoomBuilder
    {
        private const string Root = "Assets/Bundle/Environments";
        private const string MeshDir = Root + "/Meshes";
        private const string MatDir = Root + "/Materials";
        private const string ImpModels = Root + "/Imported/Models";
        private const string ImpTex = Root + "/Imported/Textures";

        // ============================================================ PLAY SPACE
        // The authored DIAMETER of each room's usable open area, in authored
        // metres — see EnvironmentsBuilder.AddPlaySpace for the contract. These
        // are measured values, not wishes: AssertPlaySpaceClear() re-derives the
        // real clearance from the built geometry's own vertices at the end of
        // every room and FAILS the build if anything reaches inside.
        //
        //   Forest — the clearing. ClearR is 5.4 m of open ground, the first
        //   trunk band starts at 6.2 m, and the understory/deadfall ring is
        //   authored to stay outside 4.5 m. 9.0 m it is.
        //   Cellar — the free floor in the middle of a 10.5 x 9.0 m room. The
        //   props line the walls; the closest (the stool) stands at 3.85 m — it
        //   used to be 3.44 m, and it moved out when the stool was scaled to a
        //   real 0.45 m seat (user, cellar 12: "als ob es ein Hocker für eine
        //   Maus ist"), because a stool of the right size at the old spot has its
        //   near edge inside the radius. NOT the room's own 9.0 m: that would put
        //   the board's diorama scale on a circle that the table, stool and
        //   crates all stand inside of.
        public const float ForestPlaySpaceDia = 9.0f;
        public const float CellarPlaySpaceDia = 6.5f;

        // Moon bearing — taken FROM the star-dome shader's own constant so the
        // forest's directional light, trunk rim and moon shafts can never drift
        // out of agreement with the moon you can actually see in the sky.
        private static Vector3 MoonDir => EnvironmentsBuilder.MoonDir;

        // ================================================================ imports
        // Per-asset import caps — THE bundle-size knob. Values are chosen for
        // "reads painterly-real at arm's length in VR" vs the ≤ ~25 MB bundle
        // growth budget; big close surfaces get 2k, props 512–1k, normals half.
        private static readonly Dictionary<string, int> AlbSize = new Dictionary<string, int>
        {
            // surfaces
            ["medieval_blocks_05"] = 2048,
            ["monastery_stone_floor"] = 2048,
            ["dark_wooden_planks"] = 1024,
            // forest floor + trunk bark: the two big surfaces you stand on and
            // stand next to, so they get the resolution
            ["forest_ground_04"] = 2048, ["forest_leaves_04"] = 1024,
            ["pine_bark"] = 2048, ["bark_brown_02"] = 1024,
            // the fir twig atlas is EVERY needle in the forest — its alpha must
            // stay clean, so it is 1k and BC7 (see HqAlbedo)
            ["fir_twig"] = 1024,
            // the cobweb alpha (TextureCan CC0, see License.md). 1k, not the
            // source's 4k: at 1k a thread is ~1 px, which is as thin as an
            // alpha-tested thread may get before mip coverage cannot save it,
            // and 4k would have cost ~4.5 MB of bundle for detail nobody can
            // resolve on a 0.7 m web.
            ["cobweb"] = 1024,
            // hero props
            ["wine_barrel_01"] = 1024,
            ["dead_tree_trunk"] = 1024, ["dead_tree_trunk_02"] = 1024,
            ["rock_moss_set_01"] = 1024, ["tree_stump_01"] = 1024,
            ["wooden_crate_01"] = 1024, ["small_wooden_table_01"] = 1024,
            ["wooden_bookshelf_worn"] = 1024,
            // foliage cards are small + night-dark: 512 reads fine
            ["grass_medium_02"] = 512, ["fern_02"] = 512,
            ["shrub_03"] = 512, ["moss_01"] = 512,
            // THE BRACKET FUNGI (ModBuild 146). 1k and not 512, for the same
            // reason the cobweb is 1k: this is an ATLAS of isolated cutouts on
            // transparency and every one of them is alpha-tested at its own
            // silhouette, so the resolution has to be enough that a cap's edge is
            // a curve and not a staircase. It is also the only growth a player
            // gets within arm's length of - the cellar's brackets sit at
            // 0.20-1.70 m on walls he stands next to.
            ["fungus"] = 1024,
            // THE FIRE ATLAS (ModBuild 147). 512, which is exactly what the
            // procedural atlas it replaces was (BuildEnvironments.FireTile 256
            // x FireAtlasCols 2) — the swap is the ART, not the budget, and the
            // bundle does not grow by a byte. 512 is also the honest ceiling
            // here: the four sources are 512, 512, 256 and a 512 crop of a 1k
            // sheet, so a 1k atlas would be upsampling three of them.
            // NOT in HqAlbedo, deliberately, and for the reason MakeFireAtlas
            // already wrote down: this is a single channel that varies smoothly,
            // i.e. the BEST case for BC3's alpha block rather than the worst
            // case its RGB blocks are. RGB is a constant 255 and compresses to
            // nothing. ~170 KiB with mips.
            ["fire_atlas"] = 512,
            // minor props
            ["wooden_stool_02"] = 512, ["wooden_bucket_01"] = 512,
            ["jug_01"] = 512, ["root_cluster_01"] = 512,
            ["root_cluster_02"] = 512, ["single_root"] = 512,
            ["tree_stump_02"] = 512, ["rock_moss_set_02"] = 512,
            ["wooden_axe_02"] = 512, ["dry_branches_medium_01"] = 512,
            ["candle_flame"] = 256,
        };
        // Normal maps: 1k only where candlelight rakes a big close surface or the
        // moon rakes a trunk you can walk up to; forest props live ≥3 m away in
        // moonlight — 256 is invisible there.
        private static readonly Dictionary<string, int> NrmSize = new Dictionary<string, int>
        {
            ["medieval_blocks_05"] = 1024, ["monastery_stone_floor"] = 1024,
            ["dark_wooden_planks"] = 512,
            ["forest_ground_04"] = 1024, ["forest_leaves_04"] = 512,
            ["pine_bark"] = 1024, ["bark_brown_02"] = 512,
            ["wine_barrel_01"] = 512,
            ["dead_tree_trunk"] = 256, ["dead_tree_trunk_02"] = 256,
            ["rock_moss_set_01"] = 256, ["tree_stump_01"] = 256,
            ["wooden_crate_01"] = 512, ["small_wooden_table_01"] = 512,
            ["wooden_bookshelf_worn"] = 512,
            ["wooden_stool_02"] = 256, ["wooden_bucket_01"] = 256,
            ["jug_01"] = 256, ["root_cluster_01"] = 256,
            ["root_cluster_02"] = 256, ["single_root"] = 256,
            ["tree_stump_02"] = 256, ["rock_moss_set_02"] = 256,
            ["wooden_axe_02"] = 256, ["dry_branches_medium_01"] = 256,
        };
        // BC7 for the surfaces the player studies up close (candle-raked wall +
        // floor, the forest floor and the bark right beside them) and for the
        // twig atlas, whose alpha would tear into blocky needles under BC1/BC3.
        private static readonly HashSet<string> HqAlbedo = new HashSet<string>
        {
            "medieval_blocks_05", "monastery_stone_floor",
            "forest_ground_04", "pine_bark", "fir_twig",
            // the fungus atlas: cutouts on transparency, same argument as the
            // twig atlas and the cobweb - BC1/BC3's 3-bit interpolated alpha
            // turns a keyed silhouette into a ragged one
            "fungus",
            // the cobweb: 1-px-wide alpha threads. Under BC1/BC3's 3-bit alpha
            // interpolation a thread becomes a dotted line.
            "cobweb",
        };

        /// <summary>Imported textures whose ALPHA is alpha-tested against a known
        /// cutoff. THE MIP TRAP: a 1-px thread has ~11% coverage at mip 0 and
        /// ~1.5% four mips down, so at any distance the whole web clips away and
        /// simply is not there any more. `mipMapsPreserveCoverage` re-normalises
        /// every mip so the fraction of texels above `alphaTestReferenceValue`
        /// stays constant — which only works if that value is EXACTLY the
        /// material's `_Cutoff`. Both come from EnvironmentsBuilder.WebCutoff.</summary>
        private static readonly Dictionary<string, float> CoverageCutoff =
            new Dictionary<string, float>
            {
                ["cobweb"] = EnvironmentsBuilder.WebCutoff,
                // The fungus atlas is alpha-tested at 0.35 (FungusCutoff), and
                // without coverage-preserving mips every bracket loses its rim to
                // the mip chain as it recedes: a 0.14 m cap at 3 m is four texels,
                // and a box-filtered alpha of four texels against transparency is
                // below the cutoff before it is below the eye's resolution. The
                // caps would visibly SHRINK as you back away.
                ["fungus"] = FungusCutoff,
            };
        /// <summary>The alpha cutoff every fungus card is tested at, and the value
        /// the importer preserves mip coverage against. One constant, because a
        /// mip chain built for one cutoff and sampled at another is exactly the
        /// vanishing-detail bug it exists to prevent.</summary>
        private const float FungusCutoff = 0.35f;

        public static void EnforceImports()
        {
            if (!Directory.Exists(ImpTex) || !Directory.Exists(ImpModels))
                throw new Exception("Imported/ assets missing — run scratchpad ph_pipeline.py first.");

            foreach (var path in Directory.GetFiles(ImpTex).Where(p => !p.EndsWith(".meta")))
            {
                string file = Path.GetFileNameWithoutExtension(path); // e.g. castle_brick_07_alb
                bool isNrm = file.EndsWith("_nrm");
                string baseName = file.Substring(0, file.Length - 4);
                string assetPath = path.Replace('\\', '/');
                AssetDatabase.ImportAsset(assetPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                if (ti == null) throw new Exception("No importer for " + assetPath);
                bool dirty = false;
                void Set<T>(T cur, T want, Action apply)
                { if (!EqualityComparer<T>.Default.Equals(cur, want)) { apply(); dirty = true; } }

                if (isNrm)
                {
                    Set(ti.textureType, TextureImporterType.NormalMap, () => ti.textureType = TextureImporterType.NormalMap);
                    int sz = NrmSize.TryGetValue(baseName, out var s) ? s : 512;
                    Set(ti.maxTextureSize, sz, () => ti.maxTextureSize = sz);
                    Set(ti.textureCompression, TextureImporterCompression.Compressed,
                        () => ti.textureCompression = TextureImporterCompression.Compressed);
                }
                else
                {
                    Set(ti.textureType, TextureImporterType.Default, () => ti.textureType = TextureImporterType.Default);
                    Set(ti.sRGBTexture, true, () => ti.sRGBTexture = true);
                    bool hasAlpha = path.EndsWith(".png");
                    Set(ti.alphaIsTransparency, hasAlpha, () => ti.alphaIsTransparency = hasAlpha);
                    int sz = AlbSize.TryGetValue(baseName, out var s) ? s : 512;
                    Set(ti.maxTextureSize, sz, () => ti.maxTextureSize = sz);
                    var comp = HqAlbedo.Contains(baseName)
                        ? TextureImporterCompression.CompressedHQ
                        : TextureImporterCompression.Compressed;
                    Set(ti.textureCompression, comp, () => ti.textureCompression = comp);
                }
                Set(ti.wrapMode, TextureWrapMode.Repeat, () => ti.wrapMode = TextureWrapMode.Repeat);
                Set(ti.filterMode, FilterMode.Trilinear, () => ti.filterMode = FilterMode.Trilinear);
                Set(ti.anisoLevel, 4, () => ti.anisoLevel = 4);
                Set(ti.mipmapEnabled, true, () => ti.mipmapEnabled = true);
                bool cover = !isNrm && CoverageCutoff.ContainsKey(baseName);
                Set(ti.mipMapsPreserveCoverage, cover, () => ti.mipMapsPreserveCoverage = cover);
                if (cover)
                    Set(ti.alphaTestReferenceValue, CoverageCutoff[baseName],
                        () => ti.alphaTestReferenceValue = CoverageCutoff[baseName]);
                if (dirty) ti.SaveAndReimport();
            }

            foreach (var path in Directory.GetFiles(ImpModels, "*.obj"))
            {
                string assetPath = path.Replace('\\', '/');
                AssetDatabase.ImportAsset(assetPath);
                var mi = (ModelImporter)AssetImporter.GetAtPath(assetPath);
                if (mi == null) throw new Exception("No model importer for " + assetPath);
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None)
                { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.importNormals != ModelImporterNormals.Import)
                { mi.importNormals = ModelImporterNormals.Import; dirty = true; }
                if (mi.importTangents != ModelImporterTangents.CalculateMikk)
                { mi.importTangents = ModelImporterTangents.CalculateMikk; dirty = true; }
                if (mi.meshCompression != ModelImporterMeshCompression.Medium)
                { mi.meshCompression = ModelImporterMeshCompression.Medium; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (mi.importBlendShapes) { mi.importBlendShapes = false; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
            AssetDatabase.Refresh();
        }

        // ============================================================= light rigs
        private struct PLight
        {
            public Vector3 pos; public float range; public Color col; public float flicker;
            public PLight(Vector3 p, float r, Color c, float f) { pos = p; range = r; col = c; flicker = f; }
        }

        private class LightRig
        {
            public Color ambUp, ambDown;
            public Vector3 dirWorld; public Color dirCol;
            public PLight[] points = Array.Empty<PLight>();
            // Near-field hardness of the point falloff (EnvRoom/_PtHard). 0 is
            // the historical pure (1-(d/r)^2)^2 window — the forest keeps it.
            // The cellar needs it high: a candle must light its own table and
            // leave the far wall black (user, ModBuild 134).
            public float ptHard = 0f;
            // ELEMENT ART — the room's outer radius in authored metres, i.e. how
            // far "the periphery" is (EnvRoom/_ElemRad, GhvrRim in
            // EnvElement.cginc). It rides on the LIGHT RIG rather than being a
            // constant per room because it goes out through ApplyRig, which is
            // already the one place that knows every lit material AND its
            // transform — an element frame written anywhere else would have to
            // re-derive the object-space conversion and could disagree with the
            // light positions about where the middle of the room is.
            public float elemRad = 6f;
            // ELEMENT ART — how hard Fire's warm rim pushes in THIS room
            // (EnvRoom/_ElemWarm). It is a per-room number and it has to be,
            // because the two rooms answer the same term at completely different
            // levels: the cellar's ambient is 0.03 and its walls are two metres
            // away, so a rim that reads there floods a wood whose moon term is
            // 0.70 and whose trunks are eight metres out — and vice versa. The
            // first bake proved both halves of that at once (the cellar's darkest
            // corner was fully revealed at Fire; the forest's trunks barely
            // moved). Measured, not guessed: see the renders in the report.
            public float elemWarm = 1f;

            // ---- FIRE REAL: the SENDING half of the fire-wash contract -------
            // USER, ModBuild 144: "... und auch die Lichtverhältnisse
            // entsprechend anpassen." EnvRoom and EnvGround have carried the
            // receiving term since ModBuild 144 (_FirePos0..2, _FireCol,
            // _FireRate) and NOTHING WROTE IT, so a room could be on fire and
            // the flagstones under the fire stayed the colour of a cellar with
            // three candles in it. These three fields are the write.
            //
            // THREE SEATS, and they are SITES rather than fires. The cellar
            // burns in six places and the shader has three slots; that is not a
            // shortage, it is the right granularity. A crate top and the litter
            // burning at its foot are forty centimetres apart and everything
            // more than a metre away is lit by their sum — resolving them as two
            // point lights would cost a slot to reproduce a difference no
            // surface in the room can show. So: the crate stack, the casks, the
            // bookshelf.
            public FireSeat[] fires = Array.Empty<FireSeat>();
            // The wash's colour, and its flicker DEPTH in the alpha. One colour
            // per room for the same reason the ambient is one colour per room.
            public Color fireWash = new Color(0f, 0f, 0f, 0f);
            // ...and the rate, in Hz, which is the SAME number every bonfire
            // material's _FireHz carries. See FireHz.
            public float fireHz = FireHz;
        }

        /// <summary>One seated fire, as the room's lighting sees it.</summary>
        private struct FireSeat
        {
            public string name;      // for the bake log only
            public Vector3 pos;      // world/room space; ApplyRig pulls it into each material's
            public float range;      // metres to full darkness
            public bool ridesShelf;  // 1 = it is standing on the tipping bookshelf
            public FireSeat(string n, Vector3 p, float r, bool ride)
            { name = n; pos = p; range = r; ridesShelf = ride; }
        }

        // FIRE REAL — THE ONE RATE. A flame and the light it casts must share a
        // rate (the standing rule in EnvFlame.shader's header). Up to ModBuild
        // 144 that was two families of sines that happened to be handed the same
        // _Rate; it is now literally one constant, written into every bonfire
        // material's _FireHz and into every lit material's _FireRate, and read by
        // ONE function (GhvrFireFlicker) that both halves call.
        //
        // 4.6 Hz, and the number is the fix rather than a taste. The shipped
        // fire's surge ran at 0.63 and 1.03 Hz — measured off the material, see
        // EnvFlame's FIRE REAL block — which is the motion of a candle in a
        // draught and is most of why six fires read as six candle flames. Real
        // flame turbulence at this scale turns over three to eight times a
        // second; GhvrFireFlicker's four bands at this base are 4.6, 2.8, 8.0
        // and 1.1 Hz, i.e. the band plus the swell that stops it being buzz.
        public const float FireHz = 4.6f;

        // ...AND THE HALO'S SHARE OF IT, which is a different question and had
        // the wrong answer. Handed over by the fire lane, ModBuild 146, judged
        // and applied here.
        //
        // A fire's EnvGlow halo is the biggest, softest, most diffuse thing in
        // the fire group — a two-metre sphere of glowing air — and EnvGlow runs
        // the CANDLE waveform on it: four sines at 11.3, 6.1, 19.7 and 1.9 times
        // its _Rate. At 0.30 the fastest of those landed at 19.7*4.6*0.30 =
        // 4.33 Hz, at +-30 % of the halo's brightness, and uncorrelated with the
        // flame's own flicker because it is a different waveform family. So the
        // slowest-responding object in the picture was the third fastest thing
        // in it, twinkling against a flame that was doing something else.
        //
        // Physically the halo is the fire's light scattered by the air and the
        // smoke around it — an INTEGRAL over the whole flame, over a couple of
        // metres of path. Integrating a turbulent source over a large volume is
        // a low-pass filter: the fine structure cancels and what survives is the
        // slow swell of the fire as a whole. 0.18 puts the fastest term at
        // 2.60 Hz and the swell at 0.25 Hz, i.e. below the flame's own 4.6 Hz
        // band, which is the right ordering: the bigger and softer the thing,
        // the slower it may move.
        //
        // FireHz ITSELF IS NOT TOUCHED. The fire lane has just fixed the
        // "zappeln" as a spectral WEIGHTING fault; lowering the clock now would
        // compound with that fix and land back on the candle look that has been
        // rejected twice. This is the halo's share of the clock, and nothing else.
        private const float FireHaloRate = 0.18f;

        // Flicker phases and RATES are baked into the shaders, one per light
        // slot, and several places have to agree with them (the flame cards, the
        // candle halos, the puddle's reflection). Single source of truth.
        private static readonly float[] SlotPhase = { 0.0f, 2.1f, 4.4f };
        private static readonly float[] SlotRate = { 1.00f, 0.83f, 1.19f };

        // Deferred rig application: props are placed (and stacked via bounds)
        // BEFORE the candle/light positions are final, so materials register
        // here and the rig is written in one flush at the end of each room.
        private static readonly List<(Material m, Transform t, float tint)> Pending
            = new List<(Material, Transform, float)>();

        private static void Defer(Material m, Transform t, float tint) => Pending.Add((m, t, tint));

        // ====================================================== SHELF RIDERS ====
        // USER, hardware, ModBuild 144: "Die Kerzen und das Feuer, die auf dem
        // Bücherregal stehen, kippen nicht mit - das musst du beheben das ist ein
        // echter Bug. Sie müssen auf jeden Fall mitkippen."
        //
        // ONE RECORD, ONE WRITER. The hinge is DERIVED — from the placed shelf's
        // measured bounds, because a photoscan's base edge is not where anybody
        // would guess — so it exists exactly once, in BuildTippingShelf, and
        // everything that has to know it (the shelf's own material, the wax, the
        // flame, the halos, the two seated fires, and every lit surface in the
        // room, because the candle standing on the shelf lights all of them) is
        // written from this one record by WriteShelfTip. Nothing re-derives it,
        // nothing stores a second copy, and the shelf mesh no longer carries one
        // in its vertex lanes.
        private class ShelfTipRig
        {
            public Vector3 pivotW;   // the hinge, in ROOM space
            public Vector3 axisW;    // its axis, in ROOM space, unit
            public float maxAngle;   // radians
            public int card;         // which haunt card this event is
            public Vector4 env;      // reveal, hold, fade (the authored envelope)
            public float period, cards;
            public int litSlot;      // the baked light slot that stands on it
        }
        /// Null except while the cellar is being built — nothing in the forest
        /// stands on a bookshelf, so every forest material writes zeros and takes
        /// the untouched path.
        private static ShelfTipRig Tip;

        /// Per-material overrides of the four answers in _TipUse. A material that
        /// is not in here gets (0, litSlot, 0, 0): it does not move itself, its
        /// light follows the candle, it is not a flame.
        private static readonly Dictionary<Material, Vector4> TipUse
            = new Dictionary<Material, Vector4>();

        /// <summary>Declare what a material does with the shelf's pose.
        /// <paramref name="self"/> 1 = move my own geometry; <paramref name="lit"/>
        /// the baked light slot that rides (-1 = none); <paramref name="gutter"/>
        /// 1 = I am a candle flame and may be blown out; <paramref name="stiff"/>
        /// how much of the rotation a flame REFUSES (0 = rigid, 1 = stays
        /// upright).</summary>
        private static void RideShelf(Material m, float self, float lit, float gutter, float stiff)
            => TipUse[m] = new Vector4(self, lit, gutter, stiff);

        /// <summary>Write the shelf's pose into one material, in THAT material's
        /// object space.
        ///
        /// <para>This is the whole of "one source of truth for the pose": the
        /// hinge is a world point and the axis a world direction, and the only
        /// thing that differs between the shelf, the wax welded at the origin,
        /// the flame card under its own transform and the wall across the room is
        /// which object space they are expressed in. That is exactly the
        /// conversion ApplyRig already does for the three candle POSITIONS, with
        /// the same two calls, so a rider's hinge and the light it stands under
        /// cannot disagree about where the room is.</para>
        ///
        /// <para>The round trip is asserted rather than assumed: these transforms
        /// are rigid with uniform scale, and if one ever is not, a hinge that
        /// came back a centimetre out would put the candle inside the shelf —
        /// which is precisely the failure the shared pose exists to prevent, and
        /// it would be invisible in every frame except four.</para></summary>
        private static void WriteShelfTip(Material m, Transform xf)
        {
            if (m == null || !m.HasProperty("_TipPivot")) return;
            if (Tip == null)
            {
                // the forest, and anything built before the shelf exists
                m.SetVector("_TipPivot", Vector4.zero);
                m.SetVector("_TipAxis", Vector4.zero);
                m.SetVector("_TipSched", Vector4.zero);
                m.SetVector("_TipEnv", Vector4.zero);
                m.SetVector("_TipUse", new Vector4(0f, -1f, 0f, 0f));
                return;
            }
            var pO = xf.InverseTransformPoint(Tip.pivotW);
            var aO = xf.InverseTransformDirection(Tip.axisW).normalized;
            float back = Vector3.Distance(xf.TransformPoint(pO), Tip.pivotW);
            if (back > 1e-3f)
                throw new Exception($"Shelf rider '{m.name}' under transform '{xf.name}': the hinge "
                                    + $"does not survive the round trip into its object space "
                                    + $"({back * 1000f:F2} mm out). Every rider must be under a rigid, "
                                    + "uniformly scaled transform — otherwise the candle and the shelf "
                                    + "rotate about different points and the candle ends up inside its "
                                    + "own shelf.");
            m.SetVector("_TipPivot", new Vector4(pO.x, pO.y, pO.z, 1f));
            m.SetVector("_TipAxis", new Vector4(aO.x, aO.y, aO.z, Tip.maxAngle));
            m.SetVector("_TipSched", new Vector4(Tip.period, Tip.cards, Tip.card, 0f));
            m.SetVector("_TipEnv", Tip.env);
            m.SetVector("_TipUse",
                TipUse.TryGetValue(m, out var u) ? u : new Vector4(0f, Tip.litSlot, 0f, 0f));
        }

        private static void FlushRig(LightRig rig)
        {
            foreach (var (m, t, tint) in Pending)
                ApplyRig(m, rig, t, tint);
            Pending.Clear();
        }

        private static void ApplyRig(Material m, LightRig rig, Transform xf, float tintMul)
        {
            m.SetColor("_AmbUp", rig.ambUp);
            m.SetColor("_AmbDown", rig.ambDown);
            var dirObj = xf.InverseTransformDirection(rig.dirWorld.normalized);
            m.SetVector("_DirDir", dirObj);
            m.SetColor("_DirCol", rig.dirCol);
            // The rim gate ("only the moonlit SIDE catches the rim") was reading
            // EnvRoom's _RimDir DEFAULT of (0,1,0) — straight up — because nothing
            // ever set it, so every trunk got the same flat 0.55 gate all the way
            // round. Caught in the ModBuild 134 darkening pass: with the ambient
            // pulled out from under it, a rim that ignores the moon is the
            // difference between a volume and a glowing tube.
            m.SetVector("_RimDir", dirObj);
            if (m.HasProperty("_PtHard")) m.SetFloat("_PtHard", rig.ptHard);
            // ELEMENT ART — the room's frame, in this material's OWN object
            // space. Both rooms are authored around the origin, so the centre is
            // world zero; a prop with its own transform gets that point pulled
            // back into its space here, which is why a barrel three metres out
            // frosts on its outward side and warms on the side that faces the
            // table. Guarded by HasProperty: EnvGround and EnvRoomCutout go
            // through this same flush and belong to other lanes this round.
            float s = (xf.lossyScale.x + xf.lossyScale.y + xf.lossyScale.z) / 3f;
            if (m.HasProperty("_ElemCentre"))
            {
                var cObj = xf.InverseTransformPoint(Vector3.zero);
                m.SetVector("_ElemCentre", new Vector4(cObj.x, cObj.y, cObj.z, 0f));
                m.SetFloat("_ElemRad", rig.elemRad / Mathf.Max(s, 1e-4f));
                m.SetFloat("_ElemWarm", rig.elemWarm);
                // SURFACE GROWTH — this material's object units, in metres. It
                // rides here for the same reason _ElemRad does: ApplyRig is the
                // one place that knows every lit material AND its transform, and
                // a growth pattern that did not know the scale would be a 0.45 m
                // patch of frost on the floor and a 0.9 m one on a prop scaled 2.
                m.SetFloat("_ElemScl", s);
            }
            for (int i = 0; i < 3; i++)
            {
                string pn = "_L" + i + "Pos", cn = "_L" + i + "Col";
                if (i < rig.points.Length)
                {
                    var p = rig.points[i];
                    Vector3 lp = xf.InverseTransformPoint(p.pos);
                    m.SetVector(pn, new Vector4(lp.x, lp.y, lp.z, s / Mathf.Max(p.range, 0.01f)));
                    m.SetColor(cn, new Color(p.col.r, p.col.g, p.col.b, p.flicker));
                }
                else
                {
                    m.SetVector(pn, new Vector4(0, 0, 0, 1));
                    m.SetColor(cn, new Color(0, 0, 0, 0));
                }
            }
            // ---- FIRE REAL: the seats, in this material's object space -------
            // Same door, same conversion and the same reason as the three candle
            // positions above: several transforms may share one material, so a
            // seat is only meaningful once it has been pulled into the space the
            // fragment is shaded in. `s` is the material's scale, so 1/range
            // comes out in object units exactly as _L*Pos.w does.
            if (m.HasProperty("_FirePos0"))
            {
                for (int i = 0; i < 3; i++)
                {
                    string pn = "_FirePos" + i;
                    if (i < rig.fires.Length)
                    {
                        var f = rig.fires[i];
                        Vector3 fp = xf.InverseTransformPoint(f.pos);
                        m.SetVector(pn, new Vector4(fp.x, fp.y, fp.z,
                                                    s / Mathf.Max(f.range, 0.01f)));
                    }
                    else m.SetVector(pn, new Vector4(0, 0, 0, 1));
                }
                m.SetColor("_FireCol", rig.fireWash);
                m.SetFloat("_FireRate", rig.fireHz);
                // ...and which of them stands on the bookshelf that topples.
                var ride = Vector4.zero;
                for (int i = 0; i < 3 && i < rig.fires.Length; i++)
                    if (rig.fires[i].ridesShelf) ride[i] = 1f;
                m.SetVector("_FireRide", ride);
            }

            // ---- SHELF RIDERS: the pose, for every lit material in the room --
            // Not only for the things standing ON the shelf: the candle standing
            // on it lights the whole cellar, so every lit surface needs the
            // hinge in its own space in order to move that one light slot. See
            // WriteShelfTip and EnvShelfTip.cginc's channel block.
            WriteShelfTip(m, xf);

            var t = m.GetColor("_Tint");
            m.SetColor("_Tint", new Color(t.r * tintMul, t.g * tintMul, t.b * tintMul, t.a));
        }

        // ============================================================== materials
        private static Material NewRoomMat(string file, string shaderName)
        {
            var sh = Shader.Find(shaderName) ?? throw new Exception($"Shader '{shaderName}' missing");
            string path = MatDir + "/" + file;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh) { name = Path.GetFileNameWithoutExtension(file) };
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                // reset fully deterministic (materials are re-authored every build)
                m.shader = sh;
                m.SetColor("_Tint", Color.white);
            }
            return m;
        }

        /// <summary>The fire atlas, bound rather than defaulted.
        ///
        /// <para>A missing sprite here would be an INVISIBLE regression rather
        /// than a build error — the shader's "white" default would draw every
        /// card as a solid rectangle of flame colour, which at a glance in a dark
        /// preview looks like a bright fire. The same argument, and the same
        /// throw, as BuildHaunts makes about Env_Haunt.png.</para>
        ///
        /// <para>=== ModBuild 147: IT IS REAL FIRE ART NOW, NOT A PROCEDURAL ONE.
        ///
        /// USER, verbatim: "Ich hab dir in .debug/ressources zwei Feuer FX System
        /// abgelegt. Schau sie dir und importiere sie gegenfalls und ersetze damit
        /// dein eigens gebautes feuer - das sieht nicht wirklich gut aus." And,
        /// earlier and more strongly: "Bitte benutze irgendein Feuer FX das schon
        /// vorgefertigt ist als es selber zu bauen ... Ich will lieber das du es
        /// mit solchen fx Dingen umsetzt statt selber zu machen."</para>
        ///
        /// <para>WHAT MOVED AND WHAT DID NOT. Both packages he supplied are for a
        /// pipeline this project does not use — "Fire 001" (N2Studio) ships the
        /// Nova Shader tagged RenderPipeline = "UniversalPipeline", "Free Fire VFX
        /// - HDRP" (Vefects) is tagged "HDRenderPipeline", and this project is
        /// BUILT-IN. Neither package's shaders can run here at all. So the SHADER
        /// stays ours (which is also the whole answer to his second question — see
        /// the wind note below) and the ART becomes theirs, which is the half that
        /// was actually wrong.</para>
        ///
        /// <para>THE EVIDENCE THAT THE ART WAS THE WRONG HALF. Put the two atlases
        /// side by side. MakeFireAtlas's tongue cells are an ANALYTIC CONE with a
        /// sharp apex, filled with a coarse four-octave fBm that reads as curdled
        /// blobs; its bed is a rounded rectangle. A cone with a sharp point is the
        /// candle silhouette this feature has now been rejected for twice, and the
        /// file's own comments say so ("WIDE, and that is the lesson of both
        /// previous bakes: a tongue as narrow as a candle flame IS a candle
        /// flame"). The imported masks are real turbulent flame — filaments,
        /// wisps, a torn organic outline, no straight edge and no analytic curve
        /// anywhere. Measured, per cell, alpha-weighted drawn mass in cell units:
        ///   bed     0.445 x 0.182  ->  0.468 x 0.137
        ///   tongueA 0.187 x 0.359  ->  0.302 x 0.408   (the fat tongue, at last)
        ///   tongueB 0.191 x 0.410  ->  0.250 x 0.355
        ///   puff    0.364 x 0.433  ->  0.292 x 0.283
        /// FireMesh's ArtScale table below is derived from exactly those numbers,
        /// so the fire keeps the SIZE six rounds of tuning settled on and changes
        /// only its SHAPE.</para>
        ///
        /// <para>THE PROCEDURAL ATLAS COSTS THE BUNDLE NOTHING, and that was
        /// checked rather than assumed. BuildEnvironments.MakeFireAtlas still
        /// bakes Textures/Env_Fire.png, but nothing references it any more and
        /// the bake's own sweep takes it straight back out — the log line reads
        /// "pruned unreferenced Assets/Bundle/Environments/Textures/Env_Fire.png".
        /// So the swap is bundle-neutral WITHOUT touching BuildEnvironments.cs,
        /// which is another lane's file this round. Retiring the generator itself
        /// (the WritePng call and MakeFireAtlas, ~100 lines) is a tidy-up for
        /// whoever owns that file next; it is named in the report and deliberately
        /// not reached into here.</para>
        ///
        /// <para>PROVENANCE AND LICENCE: see Bundle/Environments/License.md. The
        /// shipped PNG is a re-authored composite — four greyscale masks cropped,
        /// re-profiled, knee-compressed and packed into this project's own 2x2
        /// layout — and no source file survives in it as delivered. It is derived
        /// reproducibly by Assets/Editor/fire_atlas_pipeline.py, which reads the
        /// two .unitypackage files directly, exactly as cobweb_pipeline.py and
        /// polyhaven_pipeline.py derive their own imports.</para></summary>
        private static Texture2D FireAtlas()
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ImpTex + "/fire_atlas_alb.png");
            if (t == null)
                throw new Exception("Imported/Textures/fire_atlas_alb.png is missing — the fires "
                                    + "would be drawn as solid rectangles and would look like a "
                                    + "fire in a dark preview, which is why this throws instead of "
                                    + "letting the shader's \"white\" default through. Re-derive "
                                    + "it: python3 Assets/Editor/fire_atlas_pipeline.py "
                                    + "<dir with the two .unitypackage files> "
                                    + "Assets/Bundle/Environments/Imported/Textures/"
                                    + "fire_atlas_alb.png");
            return t;
        }

        private static Texture2D Imp(string file)
        {
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ImpTex + "/" + file + ext);
                if (t != null) return t;
            }
            throw new Exception("Imported texture missing: " + file);
        }

        private static Mesh ImpMesh(string name)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(ImpModels + "/" + name + ".obj");
            var mesh = all.OfType<Mesh>().FirstOrDefault()
                       ?? throw new Exception("Imported mesh missing: " + name);
            return mesh;
        }

        // =============================================================== placing
        private static GameObject Place(Transform parent, string goName, Mesh mesh,
            Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // ====================================================== prop GROUNDING
        // User finding, ModBuild 132: "Manche Gegenstände im Keller schweben
        // herum, das soll nicht sein."
        //
        // ROOT CAUSE: the old drop used `Renderer.bounds`, which is the
        // axis-aligned box of the transformed LOCAL AABB — not the geometry.
        // Rotate a prop about any horizontal axis and that box sags below the
        // real mesh by up to (halfExtent * sin(tilt)); dropping to bounds.min.y
        // therefore parked the prop that far ABOVE its support. Stacking had the
        // mirror bug: `bounds.max.y` is the highest corner of the whole prop
        // (a bookshelf's top plank, a table's far corner), so anything stacked
        // on it started too high, and nothing checked that the stacked prop was
        // over its support at all.
        //
        // FIX: every height query runs on the real transformed VERTICES, and a
        // stacked prop samples its support's surface only UNDER ITS OWN
        // FOOTPRINT. Overhang is an error, not a silent float.
        private static readonly Dictionary<Mesh, Vector3[]> VertCache = new Dictionary<Mesh, Vector3[]>();

        private static Vector3[] Verts(Mesh m)
        {
            if (!VertCache.TryGetValue(m, out var v))
            {
                v = m.vertices;   // editor-side read works even for isReadable=false assets
                if (v == null || v.Length == 0)
                    throw new Exception($"Mesh '{m.name}' has no readable vertices — grounding cannot be exact.");
                VertCache[m] = v;
            }
            return v;
        }

        /// <summary>Every vertex of a placed object in ROOM space (the build-time
        /// world; the room root is identity).</summary>
        private static IEnumerable<Vector3> WorldVerts(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            var xf = go.transform;
            foreach (var v in Verts(mf.sharedMesh)) yield return xf.TransformPoint(v);
        }

        private struct Foot   // XZ footprint of a placed prop
        {
            public float x0, z0, x1, z1;
            public Vector2 Center => new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
        }

        private static Foot FootOf(GameObject go)
        {
            var f = new Foot { x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue };
            foreach (var p in WorldVerts(go))
            {
                if (p.x < f.x0) f.x0 = p.x;
                if (p.x > f.x1) f.x1 = p.x;
                if (p.z < f.z0) f.z0 = p.z;
                if (p.z > f.z1) f.z1 = p.z;
            }
            return f;
        }

        private static float TrueMinY(GameObject go)
        {
            float y = float.MaxValue;
            foreach (var p in WorldVerts(go)) if (p.y < y) y = p.y;
            return y;
        }

        /// <summary>Highest support vertex under a disc of XZ radius r — the real
        /// surface height a prop would rest on at (x,z).</summary>
        private static readonly Dictionary<Mesh, int[]> TriCache = new Dictionary<Mesh, int[]>();

        private static (Vector3[] v, int[] t) WorldMesh(GameObject go)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (!TriCache.TryGetValue(mesh, out var idx)) TriCache[mesh] = idx = mesh.triangles;
            var src = Verts(mesh);
            var xf = go.transform;
            var w = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) w[i] = xf.TransformPoint(src[i]);
            return (w, idx);
        }

        /// <summary>Cast a ray straight down at (x,z) onto a mesh and return the
        /// HIGHEST surface it hits. This has to be triangles, not vertices: the
        /// decimator collapses a flat shelf board or table top to two big
        /// triangles, so the whole interior of the surface a prop stands on
        /// contains no vertices at all.</summary>
        private static bool RayDown(Vector3[] w, int[] t, float x, float z, out float y)
        {
            y = float.MinValue; bool hit = false;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = w[t[i]], b = w[t[i + 1]], c = w[t[i + 2]];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (det > -1e-9f && det < 1e-9f) continue;             // edge-on
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                if (l1 < -1e-4f || l1 > 1.0001f) continue;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                if (l2 < -1e-4f || l2 > 1.0001f) continue;
                float l3 = 1f - l1 - l2;
                if (l3 < -1e-4f) continue;
                float yy = l1 * a.y + l2 * b.y + l3 * c.y;
                if (!hit || yy > y) { y = yy; hit = true; }
            }
            return hit;
        }

        private static float SurfaceYAt(GameObject support, float x, float z)
        {
            var (w, t) = WorldMesh(support);
            if (RayDown(w, t, x, z, out float y)) return y;
            var fb = FootOf(support);
            throw new Exception($"Nothing to stand on: a ray down at ({x:F2},{z:F2}) misses " +
                                $"'{support.name}' entirely (its footprint is " +
                                $"[{fb.x0:F2},{fb.z0:F2}..{fb.x1:F2},{fb.z1:F2}]).");
        }

        /// <summary>Support height under a prop's CONTACT SET (the vertices that
        /// actually touch down) plus an overhang check. Probing the footprint's
        /// AABB corners instead would flag every rotated or round prop, whose
        /// corners are empty air.</summary>
        private static float SupportUnder(GameObject support, GameObject prop, Foot f, out float cover)
        {
            // the prop's CONTACT SET: the vertices that actually touch down.
            // Probing the footprint's AABB corners instead would flag every
            // rotated or round prop, whose corners are empty air.
            float lowest = TrueMinY(prop);
            var contact = WorldVerts(prop).Where(p => p.y < lowest + 0.06f)
                                          .Select(p => new Vector2(p.x, p.z)).ToList();
            if (contact.Count == 0) contact.Add(f.Center);
            if (contact.Count > 40)                                  // subsample: 40 probes is plenty
            {
                int step = contact.Count / 40;
                contact = contact.Where((_, i) => i % step == 0).ToList();
            }

            var (w, t) = WorldMesh(support);
            float top = float.MinValue; int ok = 0;
            foreach (var c in contact)
                if (RayDown(w, t, c.x, c.y, out float y)) { ok++; if (y > top) top = y; }
            cover = ok / (float)contact.Count;
            if (top == float.MinValue) top = SurfaceYAt(support, f.Center.x, f.Center.y);
            return top;
        }

        // Room floor height at (x,z) — set per room before any prop is placed, so
        // props on an uneven floor/terrain rest on the ground actually under them.
        private static Func<float, float, float> _groundY = (x, z) => 0f;

        /// <summary>How far a prop must rise so the terrain no longer pokes through
        /// it. Every vertex asks "how much lift do I need here?" and we take a high
        /// quantile rather than the maximum: on uneven ground a strict maximum
        /// perches a wide prop (a 5 m rock set, a fallen log) on its single worst
        /// bump and floats everything else, while the quantile lets the outliers
        /// bury a centimetre — which is what rocks and logs do anyway.</summary>
        private static float GroundLift(GameObject go, float quantile = 0.995f)
        {
            var need = WorldVerts(go).Select(p => _groundY(p.x, p.z) - p.y).ToList();
            need.Sort();
            return need[Mathf.Clamp(Mathf.RoundToInt((need.Count - 1) * quantile), 0, need.Count - 1)];
        }

        /// <summary>Sit a placed object exactly on its support: floor when
        /// `support` is null, otherwise that prop's surface under this one's own
        /// footprint. Logs the correction and the error the old AABB drop made.</summary>
        private static void Rest(GameObject go, GameObject support, float sink, Vector3 want)
        {
            // Photoscan pivots are wherever the scanner happened to put them, so
            // `localPosition` alone says nothing about where the prop actually
            // STANDS. Re-centre its footprint on the asked-for spot first —
            // otherwise "the jug at the table's XZ" can be half a metre off the
            // table, which is the other half of the floating-props complaint.
            var f0 = FootOf(go);
            var c0 = f0.Center;
            go.transform.localPosition += new Vector3(want.x - c0.x, 0f, want.z - c0.y);

            var f = FootOf(go);
            float trueMin = TrueMinY(go);
            float aabbMin = go.GetComponent<Renderer>().bounds.min.y;
            float dy, cover = 1f;
            if (support == null) dy = GroundLift(go) - sink;
            else
            {
                float target = SupportUnder(support, go, f, out cover);
                dy = target - sink - trueMin;
                if (cover < 0.55f)
                    Debug.LogError($"[GloomhavenVR][Env] '{go.name}' overhangs its support " +
                                   $"'{support.name}' (only {cover * 100f:F0}% of its contact points are " +
                                   "supported) — move it onto the surface.");
            }
            go.transform.localPosition += new Vector3(0, dy, 0);
            if (support == null)
            {
                // shrink the footprint toward its centre: the contact pool belongs
                // under the prop's base, not under its widest overhang
                var fc = FootOf(go); var ctr = fc.Center;
                Contacts.Add((new Foot
                {
                    x0 = Mathf.Lerp(ctr.x, fc.x0, 0.55f), x1 = Mathf.Lerp(ctr.x, fc.x1, 0.55f),
                    z0 = Mathf.Lerp(ctr.y, fc.z0, 0.55f), z1 = Mathf.Lerp(ctr.y, fc.z1, 0.55f),
                }, 1f));
            }
            Grounded.Add($"{go.name}: on {(support == null ? "ground" : support.name)} " +
                         $"sink={sink:F3} dy={dy:+0.000;-0.000} " +
                         $"foot=[{f.x0:F2},{f.z0:F2}..{f.x1:F2},{f.z1:F2}] " +
                         $"recentre=({want.x - c0.x:+0.00;-0.00},{want.z - c0.y:+0.00;-0.00}) " +
                         $"aabbErr={(trueMin - aabbMin) * 1000f:F0}mm cover={cover * 100f:F0}%");
        }

        private static readonly List<string> Grounded = new List<string>();

        // Contact shading. These rooms have NO shadows at all (baked-light
        // materials, no scene lights), and without a dark pool where a prop meets
        // the floor even a perfectly grounded barrel reads as hovering — half of
        // "Gegenstände schweben herum" is missing contact, not missing contact.
        // Every ground-standing prop registers its footprint here and the floor
        // mesh's vertex colours are darkened underneath at the end of the room.
        private static readonly List<(Foot f, float strength)> Contacts
            = new List<(Foot, float)>();

        /// <summary>Darken a floor mesh under everything standing on it. `blend`
        /// is how black the deepest contact gets.</summary>
        private static void PaintContactAO(GameObject floor, float blend = 0.42f, float reach = 0.45f)
        {
            var mesh = floor.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices;
            var c = mesh.colors;
            if (c == null || c.Length != v.Length)
            {
                c = new Color[v.Length];
                for (int i = 0; i < c.Length; i++) c[i] = Color.white;
            }
            for (int i = 0; i < v.Length; i++)
            {
                float occ = 0f;
                foreach (var (f, s) in Contacts)
                {
                    // distance from the vertex to the footprint rectangle (0 inside)
                    float dx = Mathf.Max(f.x0 - v[i].x, v[i].x - f.x1, 0f);
                    float dz = Mathf.Max(f.z0 - v[i].z, v[i].z - f.z1, 0f);
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / reach;
                    occ = Mathf.Max(occ, s * Mathf.Exp(-d * d * 2.2f));
                }
                float k = Mathf.Lerp(1f, blend, Mathf.Clamp01(occ));
                c[i] = new Color(c[i].r * k, c[i].g * k, c[i].b * k, c[i].a);
            }
            mesh.colors = c;
            EditorUtility.SetDirty(mesh);
            floor.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_VCol", 1f);
            Debug.Log($"[GloomhavenVR][Env] Contact shading painted under {Contacts.Count} props.");
            Contacts.Clear();
        }

        /// <summary>Prove that the play-space disc is empty (see PLAY SPACE
        /// above). Walks the REAL vertices of every mesh under the room — not
        /// bounds boxes, which would both over- and under-report on the rotated
        /// props and the welded forest — and fails the build on the first
        /// intruder, naming it and the radius it reached. `exempt` is for the
        /// things that MUST be there: the floor/ground the board stands on, and
        /// the moonlight shafts, which are light, not matter.</summary>
        private static void AssertPlaySpaceClear(Transform room, string label, float diameter,
            params string[] exempt)
        {
            float r = diameter * 0.5f;
            var ex = new HashSet<string>(exempt);
            var bad = new List<string>();
            string worstName = null; float worst = float.MaxValue;
            foreach (var mf in room.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || ex.Contains(mf.gameObject.name)) continue;
                float near = float.MaxValue;
                foreach (var p in WorldVerts(mf.gameObject))
                {
                    // ignore anything overhead: crowns, canopy and beams pass over
                    // the disc by design — the constraint is on the space the
                    // board and the players' hands occupy.
                    if (p.y > 2.2f) continue;
                    float d = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    if (d < near) near = d;
                }
                if (near < worst) { worst = near; worstName = mf.gameObject.name; }
                // report EVERY intruder, not just the first: fixing them one build
                // at a time costs a full Unity batch run each
                if (near < r) bad.Add($"'{mf.gameObject.name}' reaches {near:F2} m");
            }
            if (bad.Count > 0)
                throw new Exception($"{label}: {bad.Count} object(s) intrude into the {diameter:F1} m PlaySpace " +
                                    $"(limit {r:F2} m from the centre): {string.Join(", ", bad)}. " +
                                    "Move them out, or lower the authored PlaySpace diameter.");
            Debug.Log($"[GloomhavenVR][Env] {label} PlaySpace {diameter:F2} m clear: nearest geometry is "
                      + $"'{worstName}' at {worst:F2} m (needs >= {r:F2} m).");
        }

        private static void ReportGrounding(string room)
        {
            Debug.Log($"[GloomhavenVR][Env] {room} prop grounding ({Grounded.Count} props):\n  "
                      + string.Join("\n  ", Grounded));
            Grounded.Clear();
        }

        /// <summary>Place an imported prop with its own lit material and sit it on
        /// its support (see prop GROUNDING above). The light rig is applied later
        /// via FlushRig (deferred, see Pending).</summary>
        private static GameObject Prop(Transform parent, string goName, string meshName,
            string texBase, Vector3 pos, float yaw, float scale,
            string matPrefix, float tintMul = 1f, float bump = 1f, Vector3? euler3 = null,
            bool cutout = false, Vector3? scale3 = null, float sink = 0.015f,
            GameObject support = null, Material shared = null, Quaternion? rot = null)
        {
            var mesh = ImpMesh(meshName);
            Material mat = shared ?? NewRoomMat($"{matPrefix}_{goName}.mat",
                cutout ? "GloomhavenVR/EnvRoomCutout" : "GloomhavenVR/EnvRoom");
            if (shared == null)
            {
                mat.SetTexture("_MainTex", Imp(texBase + "_alb"));
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(ImpTex + "/" + texBase + "_nrm.jpg");
                if (nrm != null) mat.SetTexture("_BumpMap", nrm);
                mat.SetFloat("_BumpScale", bump);
                if (cutout) mat.SetFloat("_Cutoff", 0.35f);
            }

            Vector3 sc = scale3 ?? Vector3.one * scale;
            var e = euler3 ?? new Vector3(0, yaw, 0);
            var go = Place(parent, goName, mesh, pos, e, sc, mat);
            // `rot` wins over the Euler triple: for a prop whose pose is DERIVED
            // (the axe's, from the direction its blade bites and the angle its
            // handle rises) a quaternion built from those two vectors is the
            // statement, and three Euler numbers are a transcription of it.
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            Rest(go, support, sink, pos);
            if (shared == null) Defer(mat, go.transform, tintMul);
            return go;
        }

        // ======================================================= procedural mesh
        /// <summary>Write a generated mesh to a stable asset path. `bounds`
        /// OVERRIDES the derived bounding box — mandatory for the meshes whose
        /// vertex shader moves them far from their authored position (the rat
        /// along its path, the drop down its fall): the derived box is a few
        /// centimetres wide and Unity would frustum-cull them the moment the
        /// object's own origin left the view.</summary>
        private static Mesh SaveMesh(string file, Mesh src, Bounds? bounds = null)
        {
            if (bounds.HasValue) src.bounds = bounds.Value;
            string path = MeshDir + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                src.name = Path.GetFileNameWithoutExtension(file);
                AssetDatabase.CreateAsset(src, path);
                return src;
            }
            existing.Clear();
            existing.vertices = src.vertices;
            existing.normals = src.normals;
            existing.tangents = src.tangents;
            existing.uv = src.uv;
            existing.colors = src.colors;
            existing.triangles = src.triangles;
            existing.RecalculateBounds();
            if (bounds.HasValue) existing.bounds = bounds.Value;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(src);
            return existing;
        }

        // seam-free noise (copies of EnvironmentsBuilder's — kept private there)
        private static float Hash3(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 974711);
                n *= 1274126177u; n ^= n >> 16; n *= 2246822519u; n ^= n >> 13;
                return (n & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Noise2(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(Hash3(x0, y0, 0, seed), Hash3(x0 + 1, y0, 0, seed), tx);
            float b = Mathf.Lerp(Hash3(x0, y0 + 1, 0, seed), Hash3(x0 + 1, y0 + 1, 0, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }

        private static float Fbm2(float x, float y, int oct, int seed)
        {
            float acc = 0, amp = 1, sum = 0;
            for (int o = 0; o < oct; o++)
            {
                acc += amp * Noise2(x, y, seed + o * 131);
                sum += amp; amp *= 0.55f; x *= 2.03f; y *= 2.03f;
            }
            return acc / sum;
        }

        /// <summary>XZ-plane grid facing +Y. Vertices in ROOM coordinates (pivot
        /// at origin) so object-space == room-space for the light rig.</summary>
        private static Mesh GridMeshXZ(float x0, float z0, float x1, float z1, int nx, int nz,
            Func<float, float, float> height, Func<float, float, Color> color, float uvScale,
            bool faceDown = false)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var cols = color != null ? new List<Color>() : null;
            var tri = new List<int>();
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = Mathf.Lerp(x0, x1, i / (float)nx);
                    float z = Mathf.Lerp(z0, z1, j / (float)nz);
                    v.Add(new Vector3(x, height?.Invoke(x, z) ?? 0f, z));
                    uv.Add(new Vector2(x / uvScale, z / uvScale));
                    cols?.Add(color(x, z));
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    if (faceDown) tri.AddRange(new[] { a, b, c, b, d, c });
                    else tri.AddRange(new[] { a, c, b, b, c, d }); // (b,d,c) faced DOWN — checkerboard bug, iteration 1
                }
            return FinishMesh(v, uv, tri, cols);
        }

        /// <summary>Polar ground disc (dense center, coarser rim), room coords.</summary>
        private static Mesh PolarGround(float radius, int rings, int segs,
            Func<float, float, float> height, Func<float, float, Color> color, float uvScale)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var cols = new List<Color>();
            var tri = new List<int>();
            v.Add(new Vector3(0, height(0, 0), 0)); uv.Add(Vector2.zero); cols.Add(color(0, 0));
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * Mathf.Pow(r / (float)rings, 1.45f); // denser center
                for (int s = 0; s < segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                    v.Add(new Vector3(x, height(x, z), z));
                    uv.Add(new Vector2(x / uvScale, z / uvScale));
                    cols.Add(color(x, z));
                }
            }
            for (int s = 0; s < segs; s++)
                tri.AddRange(new[] { 0, 1 + (s + 1) % segs, 1 + s });
            for (int r = 1; r < rings; r++)
            {
                int i0 = 1 + (r - 1) * segs, i1 = 1 + r * segs;
                for (int s = 0; s < segs; s++)
                {
                    int a = i0 + s, b = i0 + (s + 1) % segs, c = i1 + s, d = i1 + (s + 1) % segs;
                    tri.AddRange(new[] { a, d, c, a, b, d });
                }
            }
            return FinishMesh(v, uv, tri, cols);
        }

        /// <summary>Vertical wall strip along local +X (0..len), y 0..h, facing +Z,
        /// with optional rectangular holes (x0,y0,x1,y1) and gentle masonry bulge.</summary>
        private static Mesh WallMesh(float len, float h, float cell,
            Rect[] holes, float uvScale, int seed, float bulge = 0.035f, float uOff = 0f)
        {
            // uOff de-phases the tiling per wall: len/uvScale landed near an
            // integer, so adjacent walls met at the same texture column and the
            // corner read as a mirror (iteration-3 lesson).
            int nx = Mathf.CeilToInt(len / cell), ny = Mathf.CeilToInt(h / cell);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            int C(int i, int j) => j * (nx + 1) + i;
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = len * i / nx, y = h * j / ny;
                    // inward-only bulge, tapering to 0 at all edges & hole rims
                    float edge = Mathf.Min(
                        Mathf.InverseLerp(0f, 0.6f, Mathf.Min(x, len - x)),
                        Mathf.InverseLerp(0f, 0.5f, Mathf.Min(y, h - y)));
                    foreach (var ho in holes)
                    {
                        float dx = Mathf.Max(ho.xMin - x, x - ho.xMax, 0);
                        float dy = Mathf.Max(ho.yMin - y, y - ho.yMax, 0);
                        if (x >= ho.xMin - 0.4f && x <= ho.xMax + 0.4f && y >= ho.yMin - 0.4f && y <= ho.yMax + 0.4f)
                            edge = Mathf.Min(edge, Mathf.InverseLerp(0f, 0.4f, Mathf.Max(dx, dy)));
                    }
                    float z = -bulge * edge * Fbm2(x * 0.9f + 7f, y * 0.9f, 3, seed);
                    v.Add(new Vector3(x, y, z));
                    uv.Add(new Vector2((x + uOff) / uvScale, y / uvScale));
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float cx = len * (i + 0.5f) / nx, cy = h * (j + 0.5f) / ny;
                    bool inHole = holes.Any(ho => ho.Contains(new Vector2(cx, cy)));
                    if (inHole) continue;
                    tri.AddRange(new[] { C(i, j), C(i, j + 1), C(i + 1, j), C(i + 1, j), C(i, j + 1), C(i + 1, j + 1) });
                }
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>Axis-aligned box, per-face world-scaled UVs, centered at
        /// origin bottom (y 0..h).</summary>
        private static Mesh BoxMesh(float w, float h, float d, float uvScale)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Face(Vector3 o, Vector3 du, Vector3 dv)
            {
                int b = v.Count;
                v.Add(o); v.Add(o + du); v.Add(o + du + dv); v.Add(o + dv);
                float lu = du.magnitude / uvScale, lv = dv.magnitude / uvScale;
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(lu, 0));
                uv.Add(new Vector2(lu, lv)); uv.Add(new Vector2(0, lv));
                // outward faces: (o, o+du, o+du+dv) => normal = cross(du, dv)
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            float x = w / 2f, z = d / 2f;
            Face(new Vector3(-x, 0, -z), new Vector3(0, h, 0), new Vector3(w, 0, 0));   // front -Z? (normal -Z)
            Face(new Vector3(x, 0, z), new Vector3(0, h, 0), new Vector3(-w, 0, 0));    // back +Z
            Face(new Vector3(-x, 0, z), new Vector3(0, h, 0), new Vector3(0, 0, -d));   // left -X
            Face(new Vector3(x, 0, -z), new Vector3(0, h, 0), new Vector3(0, 0, d));    // right +X
            Face(new Vector3(-x, h, -z), new Vector3(0, 0, d), new Vector3(w, 0, 0));   // top
            Face(new Vector3(-x, 0, z), new Vector3(0, 0, -d), new Vector3(w, 0, 0));   // bottom
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>Lathe around local Y. profile = (radius, y) from bottom to top.</summary>
        private static Mesh LatheMesh(Vector2[] profile, int segs)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int p = 0; p < profile.Length; p++)
                for (int s = 0; s <= segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * profile[p].x, profile[p].y, Mathf.Sin(a) * profile[p].x));
                    uv.Add(new Vector2(s / (float)segs, p / (float)(profile.Length - 1)));
                }
            for (int p = 0; p < profile.Length - 1; p++)
                for (int s = 0; s < segs; s++)
                {
                    int a = p * (segs + 1) + s, b = a + 1, c = a + segs + 1, d = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                }
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>Two quads crossing at 90°, base at y=0, for flame cards.</summary>
        private static Mesh CrossQuadMesh(float w, float h)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(Vector3 right)
            {
                int b = v.Count;
                var x = right * (w / 2f);
                v.Add(-x); v.Add(x); v.Add(x + Vector3.up * h); v.Add(-x + Vector3.up * h);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Quad(Vector3.right);
            Quad(Vector3.forward);
            return FinishMesh(v, uv, tri, null);
        }

        private static Mesh FinishMesh(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Color> cols)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            if (cols != null) m.SetColors(cols);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------- mesh accumulator
        // The forest is thousands of trunks and foliage cards. Placing each as its
        // own GameObject would be thousands of draw calls with no way to fade them
        // by depth; instead everything of one kind is welded into ONE mesh whose
        // VERTEX COLOURS carry the distance fade (EnvRoom/_VCol, EnvRoomCutout/
        // _VCol). One draw call per layer, smooth falloff into darkness.
        private class Acc
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public int Count => V.Count;

            public void Vert(Vector3 p, Vector3 n, Vector2 uv, Color c)
            { V.Add(p); N.Add(n); UV.Add(uv); C.Add(c); }

            public void Quad(int b) { T.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetColors(C);
                m.SetTriangles(T, 0);
                m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Two-sided card (quad). `up` runs from the stem toward the tip
        /// of the sprig, `right` is the card's width axis.
        ///
        /// SURFACE GROWTH — vertex ALPHA is the card's FREEDOM: 0 along the stem
        /// edge, 1 along the tip edge. It is written here, once, for every card
        /// in both rooms, because "the attachment point never moves" is a
        /// property of the CARD and not of whatever is animating it — the wind
        /// (EnvRoomCutout/_ElemWind) and the Earth grow-in (_ElemGrow) both read
        /// it, and neither can pull a bough off its branch while it is 0 there.
        /// Unity's own terrain grass carries exactly this channel with exactly
        /// this comment ("1 on top vertices, 0 on bottom vertices",
        /// TerrainEngine.cginc); this is that, for cards that hang as well as
        /// stand.
        ///
        /// The rgb is untouched, and nothing read the alpha before, so authoring
        /// it changes no pixel of any existing frame. The stem edge really is
        /// the attachment: AddCrown places `c` at c0 + up*(len*0.55), so c - up
        /// lands on the whorl the bough grows out of.</summary>
        private static void AddCard(Acc a, Vector3 c, Vector3 right, Vector3 up, Vector3 nrm,
            Rect uvRect, Color col)
        {
            int b = a.Count;
            var stem = new Color(col.r, col.g, col.b, 0f);
            var tip = new Color(col.r, col.g, col.b, 1f);
            a.Vert(c - right - up, nrm, new Vector2(uvRect.xMin, uvRect.yMin), stem);
            a.Vert(c + right - up, nrm, new Vector2(uvRect.xMax, uvRect.yMin), stem);
            a.Vert(c + right + up, nrm, new Vector2(uvRect.xMax, uvRect.yMax), tip);
            a.Vert(c - right + up, nrm, new Vector2(uvRect.xMin, uvRect.yMax), tip);
            a.Quad(b);
        }

        /// <summary>Weld a finished mesh into an accumulator under a transform.
        /// This is how the window bars and each candle group become ONE object:
        /// the baked light rig is written in OBJECT space, so a material shared
        /// by several transforms lights every one of them as if it stood where
        /// the FIRST one does. (That is the bug the four bars had — bars 1..3
        /// were lit from bar 0's position — and it is the same class of silent
        /// default as the forest's unset _RimDir.)</summary>
        private static void MergeInto(Acc a, Mesh src, Vector3 pos, Quaternion rot,
            Vector3 scale, Color col)
        {
            var v = src.vertices; var n = src.normals; var uv = src.uv; var t = src.triangles;
            int b = a.Count;
            for (int i = 0; i < v.Length; i++)
                a.Vert(pos + rot * Vector3.Scale(v[i], scale),
                       (rot * (n != null && n.Length == v.Length ? n[i] : Vector3.up)).normalized,
                       uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero, col);
            foreach (var idx in t) a.T.Add(b + idx);
        }

        /// <summary>An axis-aligned quad, given its centre and two half-axes.
        /// uv spans the full 0..1 sprite. Used for the moonlight pools on the
        /// floor and for the dark mouths of the rat holes.</summary>
        private static void AddQuad(Acc a, Vector3 c, Vector3 halfU, Vector3 halfV, Color col)
        {
            Vector3 nrm = Vector3.Cross(halfV, halfU).normalized;
            int b = a.Count;
            a.Vert(c - halfU - halfV, nrm, new Vector2(0, 0), col);
            a.Vert(c + halfU - halfV, nrm, new Vector2(1, 0), col);
            a.Vert(c + halfU + halfV, nrm, new Vector2(1, 1), col);
            a.Vert(c - halfU + halfV, nrm, new Vector2(0, 1), col);
            a.Quad(b);
        }

        /// <summary>World-planar UVs for one face: project on the two axes the
        /// face's normal is LEAST aligned with, so no face smears, and take the
        /// coordinates from the ROOM position so two stones side by side never
        /// repeat the same texels. (Per-face 0..1 UVs, the obvious alternative,
        /// would stretch one whole block of the atlas across a 12 cm chip and
        /// across a 4 m skirting run alike — the chip would read as a boulder.)</summary>
        private static Vector2 PlanarUV(Vector3 p, Vector3 n, float uvScale)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(p.x / uvScale, p.z / uvScale);
            if (ax >= az) return new Vector2(p.z / uvScale, p.y / uvScale);
            return new Vector2(p.x / uvScale, p.y / uvScale);
        }

        /// <summary>One FLAT-SHADED quad from its four corners in loop order, with
        /// world-planar UVs. Its normal is cross(p1-p0, p2-p0) — the convention
        /// BoxMesh, RevealMesh, BuildShaft and WallMesh are all wound to — so a
        /// face built here matches everything it is welded next to.
        ///
        /// It deliberately does NOT route through Acc.Quad(), which emits
        /// (b, b+2, b+1) and therefore faces the OTHER way: that order exists for
        /// AddQuad's centre/half-axis form, where the stated normal is
        /// cross(halfV, halfU). Mixing the two conventions is exactly how the
        /// moonbeam hull came out inside-out in ModBuild 137, so each call site
        /// says which one it is using.</summary>
        private static void AddFaceUV(Acc a, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float uvScale, Color col)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            // A rubble run that has thinned to nothing pinches its own quads down
            // to a line. Emitting those would cost triangles and hand the mesh a
            // NaN normal, which the tangent solver then spreads to its neighbours.
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            int b = a.Count;
            a.Vert(p0, n, PlanarUV(p0, n, uvScale), col);
            a.Vert(p1, n, PlanarUV(p1, n, uvScale), col);
            a.Vert(p2, n, PlanarUV(p2, n, uvScale), col);
            a.Vert(p3, n, PlanarUV(p3, n, uvScale), col);
            a.T.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        /// <summary>HEWN — a chisel-cut block welded into `a`. It is a box whose
        /// eight corners are each jittered off the grid by up to `chip`, and whose
        /// far (+Z, "tip") end can be narrowed (`tipNarrow`, the fraction of the
        /// width it keeps there) and undercut (`tipRise`, the fraction of the
        /// height its SOLE climbs there, leaving the top face flat).
        ///
        /// The asymmetry matters: a symmetric taper shrinks a block about its
        /// centre, so the top slopes down as much as the bottom slopes up — and a
        /// corbel's top face is the one surface in the whole room that must stay
        /// flat, because a beam bears on it. Hence two separate knobs.
        ///
        /// Flat-shaded on purpose (4 verts per face): a smoothed block reads as a
        /// pillow, and what the user asked for is chisel work. 12 triangles.</summary>
        private static void AddHewnBlock(Acc a, Vector3 centre, Quaternion rot, Vector3 size,
            float tipNarrow, float tipRise, float chip, float uvScale, int seed, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 C(int ix, int iy, int iz)
            {
                float sx = ix == 0 ? -1f : 1f, sz = iz == 0 ? -1f : 1f;
                float w = iz == 1 ? tipNarrow : 1f;
                float y = iy == 1 ? h.y : -h.y;
                if (iz == 1 && iy == 0) y = -h.y + size.y * tipRise;
                var l = new Vector3(sx * h.x * w, y, sz * h.z)
                      + new Vector3(Hash3(ix, iy, iz, seed) - 0.5f,
                                    Hash3(ix, iy, iz, seed + 31) - 0.5f,
                                    Hash3(ix, iy, iz, seed + 67) - 0.5f) * (chip * 2f);
                return centre + rot * l;
            }
            Vector3 c000 = C(0, 0, 0), c001 = C(0, 0, 1), c010 = C(0, 1, 0), c011 = C(0, 1, 1),
                    c100 = C(1, 0, 0), c101 = C(1, 0, 1), c110 = C(1, 1, 0), c111 = C(1, 1, 1);
            AddFaceUV(a, c100, c110, c111, c101, uvScale, col);   // +X
            AddFaceUV(a, c000, c001, c011, c010, uvScale, col);   // -X
            AddFaceUV(a, c010, c011, c111, c110, uvScale, col);   // +Y
            AddFaceUV(a, c000, c100, c101, c001, uvScale, col);   // -Y
            AddFaceUV(a, c001, c101, c111, c011, uvScale, col);   // +Z, the tip
            AddFaceUV(a, c000, c010, c110, c100, uvScale, col);   // -Z, the buried end
        }

        private static Color Grey(float v) => new Color(v, v, v, 1f);

        /// <summary>How close an accumulated mesh comes to the room's vertical
        /// axis, counting only what is below `yMax` — the same rule
        /// AssertPlaySpaceClear applies (anything overhead passes by design). The
        /// assert reports the single nearest object in the whole room, which is a
        /// prop; this is how the build log can also state the clearance of the
        /// geometry THIS round added.</summary>
        private static float MinRadiusBelow(Acc a, float yMax)
        {
            float best = float.MaxValue;
            foreach (var p in a.V)
            {
                if (p.y > yMax) continue;
                float d = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>A tapered tube through a polyline of rings — the rat's body,
        /// head, tail, ears and legs are all this. `along` runs down the tube into
        /// uv.y, the ring angle gives the belly/back blend in uv.x (0 belly, 1
        /// back). `cap` closes both ends with a fan, which is what makes the
        /// result a SOLID rather than a piece of pipe.
        ///
        /// <para>RAT SOLID — USER FINDING, ModBuild 139: "Man kann durch die Ratte
        /// hindurchsehen und sieht die Beine." This routine is why, and it is the
        /// third time this project has shipped inward-wound geometry (the window
        /// bars, the moonbeam blades in 137). The proof, because "it looks fine in
        /// the scene view" is exactly what got the other two through:</para>
        ///
        /// <para>Unity's front face is the one whose vertices, taken in index
        /// order, satisfy <c>normal = cross(v1-v0, v2-v0)</c> — check it against
        /// the built-in Quad (verts (-.5,-.5,0),(.5,-.5,0),(-.5,.5,0),(.5,.5,0),
        /// triangles 0,2,1, normals (0,0,-1)): cross((0,1,0),(1,0,0)) = (0,0,-1).
        /// AddQuad below obeys that, which is why the rat holes are visible — and
        /// so does the moon-shaft hull that ModBuild 137 had to fix for the same
        /// reason (MoonHullMesh, "caps, so the hull is closed from every side"),
        /// which emits the winding below verbatim. AddTube was simply never
        /// brought along, and the rat is the only thing that uses it.
        /// The ring frame here is rt x uu = axis. Take the first quad of a ring
        /// pair: v0 = ring i at angle 0 = c + r*rt, v1 = ring i+1 at angle 0
        /// = v0 + L*axis, v2 = ring i at angle d = v0 + r*d*uu. The OLD winding
        /// emitted exactly that order, so its face normal was
        /// cross(L*axis, r*d*uu) = -rt*(L*r*d) — the negative of the `nrm` the
        /// very next line hands to the vertex. Every triangle of the rat faced
        /// INWARD. With Cull Back that culls the near surface and draws the far
        /// one, so you look straight into the animal and see its legs from the
        /// inside, lit by normals pointing away from you. Swapping v1 and v2
        /// (below) makes the face normal cross(r*d*uu, L*axis) = +rt.</para>
        ///
        /// <para>Closure is the other half. Every tube here was open at both ends;
        /// a correctly wound open tube still shows its interior through the hole,
        /// and the rump's was 4.8 cm across. `cap` fans each end onto its ring
        /// centre with the ring's axial normal, so the silhouette is closed from
        /// every direction. Interpenetrating closed solids (legs into body) are
        /// fine — opaque depth testing sorts them for free.</para></summary>
        private static void AddTube(Acc a, Vector3[] c, float[] r, Color[] col, float[] along, int segs,
            bool cap = true)
        {
            int n = c.Length, stride = segs + 1;
            int b0 = a.Count;
            Vector3 axis0 = Vector3.zero, rt0 = Vector3.zero, uu0 = Vector3.zero;
            Vector3 axis1 = Vector3.zero, rt1 = Vector3.zero, uu1 = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 axis = (i == 0 ? c[1] - c[0] : i == n - 1 ? c[n - 1] - c[n - 2] : c[i + 1] - c[i - 1]).normalized;
                Vector3 hint = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 rt = Vector3.Cross(hint, axis).normalized;
                Vector3 uu = Vector3.Cross(axis, rt).normalized;
                if (i == 0) { axis0 = axis; rt0 = rt; uu0 = uu; }
                if (i == n - 1) { axis1 = axis; rt1 = rt; uu1 = uu; }
                for (int s = 0; s <= segs; s++)
                {
                    float ang = s / (float)segs * Mathf.PI * 2f;
                    Vector3 nrm = rt * Mathf.Cos(ang) + uu * Mathf.Sin(ang);
                    a.Vert(c[i] + nrm * r[i], nrm,
                           new Vector2(0.5f + 0.5f * Mathf.Sin(ang), along[i]), col[i]);
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < segs; s++)
                {
                    int i0 = b0 + i * stride + s;
                    // (i0, i0+1, i0+stride) and its partner — OUTWARD, see above
                    a.T.AddRange(new[] { i0, i0 + 1, i0 + stride,
                                         i0 + 1, i0 + stride + 1, i0 + stride });
                }
            if (!cap) return;
            // the two end discs. `front` is the one the axis points out of, so it
            // fans forwards; the other is the mirror of it.
            void Cap(Vector3 ctr, Vector3 axis, Vector3 rt, Vector3 uu, float rad,
                     Color cc, float uvy, bool front)
            {
                Vector3 nrm = front ? axis : -axis;
                int cIdx = a.Count;
                a.Vert(ctr, nrm, new Vector2(0.5f, uvy), cc);
                for (int s = 0; s <= segs; s++)
                {
                    float ang = s / (float)segs * Mathf.PI * 2f;
                    Vector3 rad3 = rt * Mathf.Cos(ang) + uu * Mathf.Sin(ang);
                    a.Vert(ctr + rad3 * rad, nrm,
                           new Vector2(0.5f + 0.5f * Mathf.Sin(ang), uvy), cc);
                }
                for (int s = 0; s < segs; s++)
                {
                    int p0 = cIdx + 1 + s, p1 = cIdx + 2 + s;
                    // cross(p0-ctr, p1-ctr) = +axis for the ascending order, so
                    // the far cap takes it and the near cap takes it reversed
                    a.T.AddRange(front ? new[] { cIdx, p0, p1 } : new[] { cIdx, p1, p0 });
                }
            }
            Cap(c[0], axis0, rt0, uu0, r[0], col[0], along[0], false);
            Cap(c[n - 1], axis1, rt1, uu1, r[n - 1], col[n - 1], along[n - 1], true);
        }

        // ============================================================ ELEMENT ART
        // The four emitters the elements OWN, plus the two the cellar owns. Every
        // one of them is gated in its material (EnvParticleAdd/_ElemOwn): while
        // its element is down each quad collapses to a point in the vertex shader
        // and nothing is shaded. What that does NOT buy is the Shuriken
        // simulation or the draw call — the bundle ships no MonoBehaviours, so
        // nothing can enable or disable a particle system at runtime — and that
        // is why the counts below are small and why the bake log prints them.
        //
        // WHY THEY HANG UNDER RoomGeo AND NOT ON THE SHELL ROOT. The runtime
        // splits a spawned shell into a board-anchored ROOM branch and a
        // perceived-size-constant SKY branch BY NODE NAME, and the room list is a
        // closed contract owned by src/ (SkyAlternative.RoomBoundShellChildren =
        // RoomGeo, GroundFog, GroundFogFar, Fireflies): "any node a future content
        // round adds" rides the SKY. An ember ring parented to the shell root
        // would therefore be scaled like the star dome — 45 m away and growing as
        // the player zooms out. Under RoomGeo they are room geometry, which is
        // what they are. It also means no name has to be added on the src/ side.
        //
        // WHERE THEY MAY BE. Nothing here is allowed inside the play space: every
        // shape below is a DONUT or a box whose near edge clears the authored
        // PlaySpace radius, so an element blooms at the walls and the tree line
        // and the board keeps its own air. (AssertPlaySpaceClear cannot check
        // this for us — it walks MeshFilters, and an emitter has none.)
        //
        // ORIENTATION, which is a permanent VR ruling and not a preference:
        // nothing may visibly re-orient with the head. Every sprite used here is
        // radially symmetric (Env_Glow, Env_Spark) or symmetric about its long
        // axis and stretched along its own WORLD velocity (Env_Streak, Stretch
        // mode with cameraVelocityScale = 0) — the same two escape hatches the
        // fog, the motes and the shooting stars already use.
        //
        // SIMULATION SPACE matches the FX that ship today (World, default scaling
        // mode), deliberately: the ground fog and the fireflies are room-bound
        // and world-simulated, they have been judged on hardware in that state,
        // and an emitter that scaled differently from the fog beside it would be
        // a new question in a round that is not about that.

        /// <summary>An element emitter: world-simulated, shadowless, looping,
        /// prewarmed (a still preview frame has to show it populated).</summary>
        private static ParticleSystem ElemPS(Transform parent, string name, Vector3 pos,
            string matFile, int maxAlive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + matFile)
                               ?? throw new Exception("Element FX material missing: " + matFile);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = ps.main;
            m.loop = true;
            m.prewarm = true;
            m.playOnAwake = true;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = maxAlive;
            // ---- THE SEED IS PINNED, ModBuild 146 -------------------------
            // Every emitter in both rooms shipped with useAutoRandomSeed ON,
            // which means Unity draws a fresh seed for it at load. Two things
            // follow, and the second one is a rule violation rather than an
            // inconvenience:
            //
            //  * THE PREVIEW HARNESS IS NOT COMPARABLE ACROSS BAKES. A review
            //    set exists so that this build's frame can be diffed against the
            //    last one's; with an auto seed every emitter's population is a
            //    different sample of the same distribution, so a real change and
            //    a re-roll look identical. Two rounds of "the snow is too small"
            //    and "these dots look like sparks" were argued from frames that
            //    could not have been compared.
            //  * IT BREAKS "ALL PLAYERS SEE THE SAME ENVIRONMENT". Two peers in
            //    the same scenario currently get different spark patterns, snow
            //    and dust out of the same prefab. That is a determinism hole in
            //    a mod whose standing requirement is multiplayer parity.
            //
            // The seed is derived from the emitter's own NAME, so it is stable
            // across bakes, distinct per emitter (a shared seed would make the
            // three fires spark in lockstep), and needs no table to maintain.
            // WHAT THIS DOES NOT FIX, stated plainly: Shuriken starts its clock
            // when the system is enabled, so two clients who entered the
            // scenario at different moments still see the same pattern at
            // different PHASES. Closing that needs a script, and the bundle has
            // no MonoBehaviours; every shader-driven motion in these rooms rides
            // _Time.y + _GhvrTimeOfs precisely to avoid this, and the emitters
            // are the one thing that cannot.
            ps.useAutoRandomSeed = false;
            uint seed = 2166136261u;
            foreach (char ch in name) seed = (seed ^ ch) * 16777619u;
            ps.randomSeed = seed | 1u;
            return ps;
        }

        /// <summary>Fade in, hold, fade out — every element emitter uses the same
        /// envelope, so a particle never pops into or out of existence.</summary>
        private static void ElemFade(ParticleSystem ps, float inAt, float outAt)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inAt),
                    new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f),
                });
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>A ring emitter clear of the play space: inner edge at
        /// `inner` metres, outer at `outer`. Written as a donut rather than as a
        /// sphere-minus-hole because a donut's radiusThickness = 1 fills the
        /// whole tube, which is the only shape in Shuriken that can promise an
        /// empty middle.</summary>
        // ------------------------------------------------ THE DRAUGHT'S BREATH
        // USER, ModBuild 146: "Der Wind im Keller sieht eher aus wie eine
        // Klimaanlage statt wind das reinpustet (gruselig)."
        //
        // A machine moves a CONSTANT mass of air. A building does not: the flow
        // through an opening goes as sqrt of the pressure difference across the
        // envelope, and that difference is a few pascals of wind pressure and
        // stack effect that never hold still — so a draught through a cellar
        // window surges and dies on a scale of seconds and never quite stops.
        // That irregularity is most of what "gruselig" means here: a steady
        // stream is a fan, an irregular one is the building breathing.
        //
        // It is a SHIPPED SHURIKEN CURVE and not a script, because the bundle has
        // no MonoBehaviours and never will. Evaluated over the emitter's own loop
        // (10 s at the slab, 11 s at the window mouth), so the two are at
        // different periods and their surges walk past each other instead of
        // beating — one body of air with a source and a wake, not two pumps.
        //
        // The keys are DELIBERATELY NOT PERIODIC-LOOKING: three surges of
        // different heights at uneven spacing, with the deepest lull (0.30) just
        // before the strongest gust (2.05). Both ends are 0.55 so the loop does
        // not step.
        private const float ElemGustMean = 0.90f;   // measured mean of the curve below
        private static AnimationCurve ElemGustCurve()
        {
            var c = new AnimationCurve(
                new Keyframe(0.00f, 0.55f), new Keyframe(0.13f, 1.85f),
                new Keyframe(0.28f, 0.42f), new Keyframe(0.46f, 1.15f),
                new Keyframe(0.61f, 0.30f), new Keyframe(0.74f, 2.05f),
                new Keyframe(0.88f, 0.60f), new Keyframe(1.00f, 0.55f));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        private static void ElemRing(ParticleSystem ps, float inner, float outer)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Donut;
            sh.radius = (inner + outer) * 0.5f;
            sh.donutRadius = (outer - inner) * 0.5f;
            sh.radiusThickness = 1f;
            // THE RING IS LAID FLAT BY THE TRANSFORM, not by the shape module's
            // own rotation. Shuriken authors a donut in the XY plane, and the
            // first bake tried to lay it down with shape.rotation = (90,0,0):
            // the previews then showed embers only at the far right of the frame
            // and no snow or spores at all, i.e. a ring standing on edge in a
            // plane through the camera. The environments already had the right
            // answer — GroundFog and GroundFogFar are built with the EMITTER
            // rotated (-90,0,0) — so this uses the mechanism that has been on
            // hardware for six rounds instead of the one that reads better.
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        /// <summary>The wind the forest's Air blows along. Same sense as the
        /// cellar's authored draught (DraftDir): the moon stands in the
        /// north-east, so in both rooms the weather comes from the light. One
        /// constant, so anything a later round adds can lean the same way — which
        /// is the whole reason DraftDir exists on the cellar side.</summary>
        private static readonly Vector3 ForestWind = new Vector3(-0.822f, 0f, -0.570f);

        /// <summary>One component of a wind velocity as an ORDERED two-constant
        /// range. Both rooms' winds have negative components, so `w*lo, w*hi`
        /// comes out with min &gt; max — Shuriken's two-constant mode takes (min,
        /// max) and an inverted pair is a trap that costs a bake to notice.</summary>
        private static ParticleSystem.MinMaxCurve WindRange(float w, float lo, float hi)
        {
            float a = w * lo, b = w * hi;
            return new ParticleSystem.MinMaxCurve(Mathf.Min(a, b), Mathf.Max(a, b));
        }

        /// <summary>Build one room's element emitters. `cellar` picks the
        /// authored geometry (the two rooms are different sizes and have
        /// different draughts); everything else is shared on purpose, so Fire
        /// looks like Fire in both places.</summary>
        private static void AddElementFX(Transform root, bool cellar)
        {
            var wind = cellar ? DraftDir : ForestWind;
            // inner radius of every ring: outside the authored play space, with a
            // little margin so a particle's own size cannot reach in
            float playR = (cellar ? CellarPlaySpaceDia : ForestPlaySpaceDia) * 0.5f + 0.15f;
            int total = 0, systems = 0;

            // DENSITY IS THE WHOLE EFFECT, and the first two bakes both got it
            // wrong in the same way. The arithmetic that decides it:
            //   alive = rate x mean lifetime, spread over a RING, of which a
            //   60 deg view holds a sixth, of which the visible height band holds
            //   maybe half. Twenty particles in a nine-metre ring is therefore
            //   ONE particle in frame — which is what the previews showed, and it
            //   reads as a bug rather than as weather.
            // Two consequences are baked into every emitter below: lifetimes are
            // short (they must also reach their steady state inside the preview's
            // six-second fast-forward, or the review set lies about the density),
            // and the rings are as tight as the play space allows.
            void Note(ParticleSystem ps, string what)
            {
                total += ps.main.maxParticles; systems++;
                Debug.Log($"[GloomhavenVR][Env] Element FX {(cellar ? "Cellar" : "Forest")}/{ps.name}: "
                          + $"{what}, max alive {ps.main.maxParticles}, "
                          + $"rate {ps.emission.rateOverTime.constant:F1}/s.");
            }

            // ------------------------------------------------------------ FIRE
            // Embers, rising. In the cellar off the floor along the walls, in the
            // forest out of the ground between the trunks — the same event in two
            // places, which is what makes an element read as one thing.
            {
                // THE RING'S HEIGHT IS ITS TUBE RADIUS, not zero. A donut's cross
                // section is a circle as thick as the ring is wide, so a ring
                // authored at floor level spawns half of its particles UNDER the
                // floor, where they are depth-rejected and cost fill for nothing.
                // Both rooms therefore sit the tube ON the ground.
                var ps = ElemPS(root, "ElemEmbers", new Vector3(0f, cellar ? 0.80f : 1.50f, 0f),
                                "FX_ElemEmber.mat", cellar ? 20 : 26);
                var m = ps.main;
                m.duration = 12f;
                m.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 5.2f);
                m.startSpeed = 0f;
                // SIZES ARE MEASURED AGAINST THE FIREFLIES, which are the only
                // thing in either room a hardware round has already judged for
                // legibility at this kind of distance (0.032-0.070 m at ~8 m,
                // "dezent" but not gone). The first bake authored everything here
                // at half that and the previews showed nothing at all — an ember
                // ring four metres away has to be at least as big as a firefly
                // eight metres away to exist.
                m.startSize = new ParticleSystem.MinMaxCurve(0.030f, 0.075f);
                m.startColor = new Color(1f, 1f, 1f, 1f);
                var e = ps.emission; e.rateOverTime = cellar ? 4.6f : 6.2f;
                // The forest ring is 5.6-8.5 m and not 5.6-9.6: twenty embers
                // spread over the wider annulus were four embers per 60 deg of
                // view, i.e. nothing. Density is the whole effect — a ring you
                // can count the particles of is a bug report.
                ElemRing(ps, playR + 0.35f, cellar ? 4.9f : 8.5f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                // an ember rises, wanders and dies; the drift is the room's own
                // wind so Fire and Air agree about which way the air is going
                v.x = WindRange(wind.x, 0.10f, 0.26f);
                v.z = WindRange(wind.z, 0.10f, 0.26f);
                v.y = new ParticleSystem.MinMaxCurve(0.24f, 0.62f);
                var n = ps.noise; n.enabled = true; n.quality = ParticleSystemNoiseQuality.Low;
                n.strength = 0.09f; n.frequency = 0.5f; n.scrollSpeed = 0.35f;
                // they burn out rather than fade: the last third is the fade
                ElemFade(ps, 0.08f, 0.55f);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0.25f)));
                Note(ps, "embers rising at the periphery");
            }

            // ------------------------------------------------------------- ICE
            // Snow, but only in the FOREST: it falls out of a sky, and the cellar
            // has a ceiling. The cellar's Ice is the frost on the stone (EnvRoom),
            // the glazed puddle (EnvPuddle) and the motes going cold (FX_Dust) —
            // which is three surfaces the player already knows the resting state
            // of, and is worth more than a fourth emitter would be.
            //
            // REJECTED for the cellar: breath fog. It would have to come out of
            // the player's face, i.e. be head-anchored, and head-anchored FX are
            // permanently forbidden in this project.
            if (!cellar)
            {
                // 3.6 m and a 4.5-7 s life, not 5 m and 9-14 s: a flake that
                // takes twelve seconds to cross the frame is a flake the preview
                // never sees (six-second fast-forward) and the player waits for.
                // It fades out in mid-air on the way down, which is what a flake
                // does in a wood full of branches anyway.
                var ps = ElemPS(root, "ElemSnow", new Vector3(0f, 3.6f, 0f), "FX_ElemSnow.mat", 42);
                var m = ps.main;
                m.duration = 20f;
                m.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7f);
                m.startSpeed = 0f;
                // Bigger than the embers, because snow has to read AS SNOW at
                // eight metres and thirty flakes is not weather — the size is
                // doing the work the count cannot.
                m.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.105f);
                m.startColor = new Color(1f, 1f, 1f, 1f);
                var e = ps.emission; e.rateOverTime = 7.0f;
                // 5.3-8.0 m, not a 15 m annulus: the same flakes over a sixth of
                // the volume. Snow you can see is snow near you.
                ElemRing(ps, playR + 0.8f, 8.0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                // slow: 0.2-0.4 m/s is what a real flake does, and it is the one
                // thing that separates snow from ash at a glance
                v.y = new ParticleSystem.MinMaxCurve(-0.40f, -0.18f);
                v.x = WindRange(wind.x, 0.14f, 0.30f);
                v.z = WindRange(wind.z, 0.14f, 0.30f);
                var n = ps.noise; n.enabled = true; n.quality = ParticleSystemNoiseQuality.Low;
                n.strength = 0.10f; n.frequency = 0.18f; n.scrollSpeed = 0.10f;
                ElemFade(ps, 0.10f, 0.86f);
                Note(ps, "slow snow between the trunks");
            }

            // ------------------------------------------------------------- AIR
            // The air itself, made visible: needles and dust DRIVEN along the
            // room's own bearing, fast, at head height and above. Stretch mode
            // aligns each streak to its WORLD velocity, never to the head
            // (cameraVelocityScale pinned to 0) — the same escape hatch the
            // shooting stars use, and the reason this is legal in VR at all.
            {
                // A RING AROUND THE PLAYER, not a box upwind of him. The first
                // bake put the emitter on the windward side and let the streaks
                // cross the room: from the middle of the clearing they spawned
                // 40 deg off the view axis and then travelled AWAY BEHIND the
                // camera, so the forest's Air rendered a pixel-exact copy of the
                // still room. Wind is not a place, it is a direction — every
                // streak in the ring carries the SAME world velocity, so whichever
                // way the player turns he sees the air going one way.
                // THE CELLAR HALF WAS REJECTED, ModBuild 143: "aktuell diese
                // Pünktchen erinnern eher an weiße Funken, das ist nicht immersiv
                // oder realistisch." Everything below that reads `cellar ? ... `
                // has moved on that side and stands still on the forest side —
                // the wood's driven needles were not what he was looking at, and
                // a lane that changes both is a lane that cannot say which one
                // the next verdict is about.
                //
                // WHAT MAKES A DOT A SPARK, and therefore what had to change:
                // brightness concentrated in a small area, hard edges, and a
                // straight path. So on the cellar side: 34 instead of 20 (a
                // stream is a SHEET of fine matter, not a handful of objects you
                // can count), each of them dimmer (0.55 -> 0.30 alpha) and much
                // longer (velocityScale x3.6, lengthScale x1.6 — at 1.1 m/s a
                // mote is now a ~35 cm hair instead of an 8 cm dash), and the
                // noise strength triples so the paths CURL. Curl is what buys
                // tumbling: Stretch mode aligns each sprite to its own world
                // velocity, so a filament on a curling path turns as it goes,
                // and it does it without a billboard and without any camera term.
                // ============================================================
                // THE AIR CONDITIONER, ModBuild 146. USER, hardware: "Der Wind
                // im Keller sieht eher aus wie eine Klimaanlage statt wind das
                // reinpustet (gruselig)."
                //
                // IT WAS THIS EMITTER, and the paragraph above says why without
                // meaning to: "A RING AROUND THE PLAYER, not a box upwind of
                // him." That is the correct answer for a CLEARING, where the
                // wind has no source and the player stands in the middle of nine
                // metres of open ground. In a cellar it is the description of a
                // ceiling vent. Thirty-four streaks, born at a constant 7.6 per
                // second on a flat annulus 3.5-4.9 m out at 1.95 m — head height
                // — all carrying the same world velocity, arriving evenly from
                // every bearing at once, for ever. Every one of those five
                // properties is a property of forced ventilation and not one of
                // them is a property of a draught. Meanwhile the emitter that IS
                // the draught (AddCellarDraught's cone at the window) was the
                // smaller half of what was on screen at any moment.
                //
                // So in the CELLAR the ring becomes a SLAB ACROSS THE DRAUGHT,
                // seated upwind on the room's own bearing: the air still fills
                // the room rather than squirting out of a hole and stopping (the
                // thing the ring was right about), but it now enters from ONE
                // side, crosses the player and leaves — which is what a body of
                // air moving through a room looks like from inside it. The
                // FOREST side is untouched: nothing in the wood has moved and the
                // "wind is a direction, not a place" argument is still exactly
                // right there.
                //
                // The slab sits 2.6 m upwind of the room centre, is 5.4 m wide
                // ACROSS the draught (so it spans the whole crossing, corner to
                // corner) and 0.6 m thick ALONG it (a slab, not a volume — a
                // thick emitter puts half its motes downwind of the player at
                // birth, which is the "spawned behind the camera" failure the
                // ring was invented to avoid). Its centre lands at (2.31, 1.19),
                // its ends at (3.54,-1.21) and (1.08,3.59): inside the room at
                // both ends, so nothing is born inside masonry.
                // ============================================================
                var gustAt = cellar
                    ? -DraftDir.normalized * 2.6f + new Vector3(0f, 1.75f, 0f)
                    : new Vector3(0f, 2.20f, 0f);
                var ps = ElemPS(root, "ElemGust", gustAt,
                                cellar ? "FX_ElemDraught.mat" : "FX_ElemGust.mat", cellar ? 34 : 28);
                var m = ps.main;
                m.duration = 10f;
                // The forest gust is SHORT-LIVED on purpose: at 1.9-3.4 m/s a
                // six-second streak ends up twenty metres out in the black wood,
                // where it is neither visible nor doing anything. Three seconds
                // keeps the whole population inside the tree band.
                m.startLifetime = new ParticleSystem.MinMaxCurve(cellar ? 3.4f : 2.4f,
                                                                 cellar ? 5.6f : 3.8f);
                m.startSpeed = 0f;   // the wind is in velocityOverLifetime, below
                // SMALL and dim: this is dust and needles going past, and the
                // Stretch renderer multiplies whatever size is authored here by
                // the speed. The first bake's 0.055-0.13 at full alpha produced
                // three fat white comets per frame.
                m.startSize = new ParticleSystem.MinMaxCurve(cellar ? 0.014f : 0.026f,
                                                             cellar ? 0.034f : 0.055f);
                m.startColor = new Color(1f, 1f, 1f, cellar ? 0.20f : 0.62f);
                var e = ps.emission;
                if (cellar)
                {
                    // ...AND IT BREATHES. A constant rate is the other half of
                    // what reads as a machine: real air moving through a building
                    // is driven by a few pascals of pressure difference that a
                    // door, a gust outside or the fire itself keeps changing, so
                    // it surges and dies over some seconds. It never stops and it
                    // is never steady. This is a shipped Shuriken curve over the
                    // system's own 10 s loop and NOT a script — the bundle has no
                    // MonoBehaviours — so it is identical on every client that
                    // has been running as long, which is the same guarantee the
                    // rest of the room's animation gives.
                    e.rateOverTime = new ParticleSystem.MinMaxCurve(7.6f / ElemGustMean, ElemGustCurve());
                    var sh = ps.shape; sh.enabled = true;
                    sh.shapeType = ParticleSystemShapeType.Box;
                    sh.scale = new Vector3(5.4f, 2.6f, 0.6f);
                    sh.rotation = Vector3.zero;
                    // local +Z down the draught, so the slab's thin axis is the
                    // one the air travels along
                    ps.transform.localRotation = Quaternion.LookRotation(DraftDir, Vector3.up);
                }
                else
                {
                    e.rateOverTime = 8.5f;
                    ElemRing(ps, playR + 0.25f, 8.0f);
                }
                var vg = ps.velocityOverLifetime; vg.enabled = true;
                vg.space = ParticleSystemSimulationSpace.World;
                float lo = cellar ? 0.75f : 1.9f, hi = cellar ? 1.5f : 3.4f;
                vg.x = WindRange(wind.x, lo, hi);
                vg.z = WindRange(wind.z, lo, hi);
                // a real gust is not level: it lifts what it carries
                vg.y = new ParticleSystem.MinMaxCurve(-0.10f, 0.35f);
                var n = ps.noise; n.enabled = true; n.quality = ParticleSystemNoiseQuality.Low;
                n.strength = cellar ? 0.32f : 0.28f; n.frequency = cellar ? 0.42f : 0.7f;
                n.scrollSpeed = 0.6f;
                ElemFade(ps, 0.14f, 0.72f);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = cellar ? 0.200f : 0.055f;
                r.lengthScale = cellar ? 2.6f : 1.6f;
                r.cameraVelocityScale = 0f;   // no camera term may enter the stretch
                Note(ps, cellar ? "dust filaments CROSSING the room on the draught, born upwind of it" : "needles driven across the wood");
            }

            // ----------------------------------------------------------- EARTH
            // Two different events for one element, because the two rooms have
            // opposite geometry: in the wood the ground BREATHES upward (spores
            // off the litter), in a cellar the ceiling SHEDS (grit between the
            // planks). Both are slow, both are at the periphery, and neither is
            // a colour — Earth's colour channel is the green on the moss and the
            // roots (EnvRoom/_ElemMoss), which is a different surface again.
            {
                bool sift = cellar;
                var ps = ElemPS(root, sift ? "ElemSift" : "ElemSpores",
                                new Vector3(0f, sift ? 3.02f : 0.12f, 0f),
                                sift ? "FX_ElemSift.mat" : "FX_ElemSpore.mat", sift ? 22 : 26);
                var m = ps.main;
                m.duration = 15f;
                m.startLifetime = new ParticleSystem.MinMaxCurve(sift ? 2.8f : 4.5f,
                                                                 sift ? 4.6f : 7.5f);
                m.startSpeed = 0f;
                // The grit is the SMALLEST thing either room draws and it is in
                // the darkest air in the game; the first two bakes put it under a
                // pixel and the previews showed an unchanged room. Grit off a
                // ceiling plank is a few millimetres of stone dust catching a
                // candle — at 0.010-0.026 m it is still that, and it is visible.
                m.startSize = new ParticleSystem.MinMaxCurve(sift ? 0.010f : 0.026f,
                                                             sift ? 0.026f : 0.062f);
                m.startColor = new Color(1f, 1f, 1f, sift ? 0.95f : 0.95f);
                m.gravityModifier = sift ? 0.055f : 0f;
                var e = ps.emission; e.rateOverTime = sift ? 7.5f : 4.2f;
                ElemRing(ps, playR + 0.25f, cellar ? 4.9f : 7.6f);
                // the spores rise from just above the litter, not from inside it:
                // the donut's tube is 1.4 m thick, so half of a ring authored at
                // ground level spawns UNDER the forest floor and is never seen
                if (!sift) ps.transform.localPosition = new Vector3(0f, 1.45f, 0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                v.x = WindRange(wind.x, 0.04f, 0.10f);
                v.z = WindRange(wind.z, 0.04f, 0.10f);
                v.y = sift ? new ParticleSystem.MinMaxCurve(-0.05f, -0.01f)
                           : new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                var n = ps.noise; n.enabled = true; n.quality = ParticleSystemNoiseQuality.Low;
                n.strength = sift ? 0.05f : 0.14f; n.frequency = 0.22f; n.scrollSpeed = 0.12f;
                ElemFade(ps, sift ? 0.10f : 0.15f, sift ? 0.70f : 0.80f);
                Note(ps, sift ? "grit sifting off the ceiling planks" : "spores drifting up off the litter");
            }

            // THE STANDING COST, stated where it is paid rather than in a report
            // nobody keeps: this is what these emitters cost with all six elements
            // inert, because nothing in the bundle can switch a particle system
            // off. The DRAWN cost with an element down is zero (collapsed quads).
            Debug.Log($"[GloomhavenVR][Env] Element FX {(cellar ? "Cellar" : "Forest")}: {systems} emitters, "
                      + $"{total} particles max alive, ALWAYS simulating (no MonoBehaviours in the bundle). "
                      + "While an element is down its quads collapse in the vertex shader: no fill, "
                      + "but the simulation and one draw call per emitter remain.");
        }

        // ================================================================ CELLAR
        // ~10.5 x 9 m weathered stone cellar, beamed plank ceiling, barred night
        // window with a real reveal and a moonlight shaft, stair alcove rising
        // into darkness, barrels/crates/table/shelf props, three candle groups.
        //
        // USER VERDICT, ModBuild 134 — the room was REJECTED on atmosphere:
        //   "Die Beleuchtung ist noch nicht athmosphärisch genug - die Kerzen
        //    beleuchten hier viel zu viel. Eine flackernde Kerze sollte auch das
        //    Licht drumrum zum flackern bekommen und auch nicht den ganze Raum
        //    beleuchten. Die Gitterstäbe schweben vor der Wand. Gerne Mondschein
        //    durch das Fenster scheinen lassen. Und hier mehr athmosphärische
        //    Details einbauen! zB tropft Wasser von irgendwo runter in eine
        //    pütze, eine Ratte huscht durch den Raum..."
        //
        // THE LIGHT RECIPE, in the order that matters (all of it baked into the
        // materials — no scene lights exist, see EnvRoom.shader):
        //  1. RANGE + HARDNESS. Candle ranges went 4.6/4.0/4.6 m -> 2.55/2.15/
        //     2.45 m, and the falloff gained a near-field inverse-square divisor
        //     (_PtHard = 22). Together those two cut the light on the far wall by
        //     ~15x while leaving the table top where it was. THIS is what turns
        //     "amber room" into "three pools in the dark".
        //  2. FLICKER. The flicker amount is the alpha of the light colour and it
        //     went 0.30/0.35/0.35 -> 0.90/0.95/0.88, i.e. from +-10% (invisible on
        //     a wall) to +-31% (unmistakable). Each slot also runs at its own
        //     RATE (1.00 / 0.83 / 1.19), so the three pools never pulse together.
        //     Every consumer of a slot — the flame card, the halo, the puddle's
        //     reflection — is built with that slot's phase AND rate, so what you
        //     see burning and what you see lit are one flame.
        //  3. MOONLIGHT. dirWorld is now MoonDir itself (it used to be a
        //     hand-typed vector that disagreed with the sky), and a five-slat
        //     EnvShaft beam comes through the window along that same bearing:
        //     the slats ARE the bar shadows, so the pattern is exact and free.
        //     Cold (0.55,0.68,1.0) against the candles' amber — the two light
        //     sources must never be mistaken for each other.
        // Knobs a future round tunes, in order of effect: PLight.range, ptHard,
        // PLight colour scale, PLight flicker alpha, ambUp/ambDown, dirCol.
        private const float CW = 10.5f, CD = 9.0f, CH = 3.3f;   // room extents
        private const float WallCell = 0.16f;                   // wall mesh cell

        private static float CellarFloorY(float x, float z) =>
            0.012f * Fbm2(x * 0.8f, z * 0.8f, 3, 901) - 0.006f;

        /// <summary>Where the four ceiling beams run. ONE expression, read by the
        /// beams themselves, by the corbels under them, by the plank ceiling that
        /// sags between them and by the mortar cove that has to keep out of their
        /// way — so none of those four can drift apart.</summary>
        private static float CellarBeamZ(int i) => -CD / 2f + CD * (i + 1) / 5f;

        /// <summary>The plank ceiling's height. It is NAILED TO THE BEAMS, so it
        /// can only sag between them: the supports are the four beams and the two
        /// walls, the sag is a first-mode bulge across each bay, and the amplitude
        /// itself wanders along the room so no two bays sag alike.
        ///
        /// Pinned at every support for two reasons beyond the physical one. The
        /// ceiling edge stays EXACTLY at y = CH where it meets the wall tops, so
        /// the cove has a known line to bury itself in; and the beams' top faces
        /// (CH + 6 mm, i.e. bedded INTO the planks) can never be left standing
        /// proud of a ceiling that sagged out from under them.
        ///
        /// Amplitude is capped at 18 mm — the moon hull's SlideIntoRoom clamps to
        /// CH - 30 mm, so anything deeper than that would let the hull's rim poke
        /// through the planks and bite a hard-edged hole out of the beam.</summary>
        private static float CellarCeilY(float x, float z)
        {
            float hd = CD / 2f;
            float lo = -hd, hi = hd;
            for (int i = 0; i < 4; i++)
            {
                float b = CellarBeamZ(i);
                if (b <= z && b > lo) lo = b;
                if (b >= z && b < hi) hi = b;
            }
            float f = Mathf.Clamp01((z - lo) / Mathf.Max(hi - lo, 1e-3f));
            // ^1.3 rather than a plain sine: a sagging board is flatter at its
            // supports and deeper in the middle than a half-wave is.
            //
            // THE Max(0) IS LOAD-BEARING. Mathf.PI is 3.14159274f, which is
            // LARGER than pi, so Sin(1f * Mathf.PI) comes out at -8.7e-8 — and
            // Pow(negative, 1.3) is NaN. f is exactly 1 at every support, and the
            // ceiling grid samples exactly there, so without this clamp the whole
            // edge row of the plank plane (and the last station of every beam,
            // which uses the same shape) would be NaN: a mesh with no bounds that
            // Unity either drops or draws as a smear across the room.
            float bay = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(f * Mathf.PI)), 1.3f);
            float amp = 0.006f + 0.012f * Fbm2(x * 0.42f + 5f, lo * 0.9f, 3, 3907);
            return CH - bay * amp;
        }

        // The window moved WEST (was x 6.0 in wall-local units). Two reasons:
        // the moon shaft that now comes through it has a fixed bearing, and at
        // the old position its pool landed 2.3 m from the room centre — inside
        // the 6.5 m PlaySpace disc, i.e. across the board. From here the beam
        // lands at ~3.95 m, clear of it, and the cold light ends up on the
        // OPPOSITE side of the room from the warm candles.
        // BIGGER, ModBuild 146. USER: "Mach das Fenster etwas größer, dass man
        // eventuell auch den Mond dahinter sehen kann. Außerdem soll das Fenster
        // so erscheinen dass es in einer dicken Wand drin ist."
        //
        // The authored rect is NOT the opening — WallMesh keeps or drops whole
        // 0.16 m cells, so what is really cut is SnappedHole's answer and the
        // authored numbers only choose which cells. The cells are 10.5/66 =
        // 0.15909 m wide and 3.3/21 = 0.15714 m tall, so this rect is written to
        // sit comfortably between cell CENTRES rather than near them: it selects
        // columns 20..28 (was 21..27) and rows 14..18 (was 14..17), i.e.
        //
        //     x  3.1818 .. 4.6136  (1.4318 m, was 1.1136)   room x -2.068..-0.636
        //     y  2.2000 .. 2.9857  (0.7857 m, was 0.6286)
        //
        // 1.125 m2 of opening against 0.700 — 1.61x, which is "etwas größer" and
        // not a different window. THREE THINGS ABOUT IT ARE DELIBERATE:
        //
        //  * THE SILL DOES NOT MOVE. It is still exactly 2.200 m, and that is a
        //    cross-lane contract rather than taste: a cellar window is at OUTSIDE
        //    GROUND LEVEL, so the sill height IS the height of the ground on the
        //    other side of the wall, and HauntFigures.Events.cs walks a real game
        //    monster past the opening at room-local y = 2.20 (cellar card 0). The
        //    window grew UPWARD and SIDEWAYS only, so that lane's walk line is
        //    untouched by this change. Only the x-span it is framed by moved.
        //  * IT GREW SYMMETRICALLY IN X — one cell column each side — so the
        //    opening's CENTRE x is bit-identical to what it was (3.8977 in wall
        //    local, -1.3523 in the room). Everything derived from WindowCentre()
        //    — the moonbeam's axis and its floor pool, the draught's mouth, every
        //    flame's _AirGust, the haunt bust's winX — therefore keeps its x and
        //    moves only the 7.9 cm the centre rose in y.
        //  * THE HEAD ROSE, and that is the half of "den Mond dahinter sehen"
        //    that geometry can actually buy: the sightline to a moon 40 deg up is
        //    limited by the height of the OUTER head and by nothing else (see
        //    AssertMoonThroughWindow, which does that arithmetic and prints how
        //    far into the room the moon can be seen from). 2.9857 leaves two
        //    0.157 m courses of masonry between the opening and the ceiling,
        //    which is as far as it can go and still have a wall over the lintel.
        private static readonly Rect WindowHole = new Rect(3.20f, 2.20f, 1.46f, 0.79f);  // in N-wall local x/y
        private static readonly Rect StairHole = new Rect(6.2f, 0f, 1.6f, 2.35f);       // in W-wall local x/y

        // ------------------------------------------------- THE THICK WALL
        // "es soll erscheinen dass es in einer dicken Wand drin ist" — and the
        // honest answer to that is not one number, it is the SHAPE of the cut.
        //
        // 0.55 m of masonry, up from 0.34. A cellar's outer wall is the one that
        // carries the house AND holds back the ground, and 0.5-0.9 m of rubble
        // stone is what that really is; 0.34 was a partition. The cost is paid in
        // the one place it can be felt — the reveal eats more of the sightline
        // out (0.60 m of rise across the tunnel instead of 0.37), which is why
        // the head had to go up at the same time.
        private const float RevealDepth = 0.55f;   // wall thickness at the window
        // ...and the SPLAY, which is what actually says "thick" to an eye. A hole
        // bored square through half a metre of stone reads as a box; a real
        // embrasure has flared jambs and a cill that falls into the room, and
        // those two sloping surfaces are the entire visual difference between a
        // window in a wall and a window in a THICK wall. They are also the two
        // surfaces the moon can strike head-on, so they are what makes the
        // opening read as a source (see the reveal material's tintMul note).
        //
        // Both are TANGENTS, not angles, so the geometry is a multiply:
        //   jambs  0.185  -> 10.5 deg, 10.2 cm of inset per side over 0.55 m
        //   cill   0.260  -> 14.6 deg, the outer cill 14.3 cm above the inner one
        // The HEAD IS FLAT and stays flat. That is correct for a stone lintel,
        // and it is also the only face whose splay would cost anything: the
        // moon's sightline leaves through the top of the OUTER opening, so a head
        // that sloped down toward the outside would take the moon away again.
        // The jamb splay costs 20 cm of the opening's 1.43 m of horizontal
        // acceptance and the cill splay costs nothing at all, because a sightline
        // aimed at a moon 40 deg up leaves nowhere near the cill.
        private const float RevealJambSplay = 0.185f;
        private const float RevealCillFall = 0.260f;
        /// <summary>How far each jamb is pulled in at the OUTER face.</summary>
        private static float RevealJambInset => RevealDepth * RevealJambSplay;
        /// <summary>How far the OUTER cill stands above the inner one.</summary>
        private static float RevealCillRise => RevealDepth * RevealCillFall;

        // ------------------------------------------------- the cellar's ONE clock
        // The drip, its splash, and the rings in the puddle are three views of a
        // single event, so they share one period and one phase and all of them
        // read _Time (see EnvDrip.shader's header for why NOT Shuriken).
        private const float DripPeriod = 2.85f;    // seconds between drops
        private const float DripHang = 1.55f;      // how long a drop clings first
        private const float DripY0 = 3.252f;       // the plank it forms on
        private const float DripY1 = 0.008f;       // the water surface
        private static float DripFall => Mathf.Sqrt(2f * (DripY0 - DripY1) / 9.81f);
        private static readonly Vector3 PuddleAt = new Vector3(-3.60f, 0f, 2.35f);
        private const float PuddleR = 0.72f;

        // ------------------------------------------------- THE BOOKSHELF'S SPOT
        // ONE anchor, because SEVEN things stand on or beside this shelf and all
        // seven used to carry their own copy of its (x, z): the prop itself, the
        // candle group, the light slot that group drives, the fire seated on its
        // boards, that fire's near halo, its wall wash, and the hero cobweb in
        // the dihedral above it. Every one of those was a literal 4.7-ish/0.70,
        // and the round that had to MOVE the shelf (see BuildTippingShelf's
        // LANDING GATE — it now falls flat on its face and needed room along its
        // own wall to do it) is exactly the round in which seven copies of a fact
        // become seven chances to leave one behind.
        //
        // It moved from z = +0.70 to z = -3.15, i.e. 3.85 m south along the same
        // east wall, at the same distance from it and at the same yaw. WHAT THAT
        // COSTS AND BUYS, stated so the next round does not have to re-derive it:
        // the room's three candle pools were NE (table), E (shelf) and SW
        // (crate), with the shelf's and the table's within 2.5 m of each other
        // and the whole south-east quadrant unlit — this file's own rat block
        // calls it "the one quadrant no candle and no moonbeam reaches". The
        // shelf takes light slot 1 with it, so the pools are now NE, SE and SW: a
        // triangle instead of a cluster, the dead quadrant gets the one light it
        // was missing, and the deliberately black corners (the south-west, the
        // stair alcove) are untouched.
        private static readonly Vector3 CellarShelfAt = new Vector3(4.86f, 0f, -3.15f);
        private const float CellarShelfYaw = -90f;   // its back to the east wall, facing -X
        /// <summary>A point on the shelf, given as the offset from its anchor
        /// that the ModBuild 145 build had from ITS anchor. Everything that
        /// stands on the shelf is placed through this, so the arrangement on the
        /// top board is preserved exactly and cannot be half-moved.</summary>
        private static Vector3 OnShelf(float dx, float dz) =>
            new Vector3(CellarShelfAt.x + dx, 0f, CellarShelfAt.z + dz);
        // The draught: in at the window, out under the stair door. The flames
        // lean along it (EnvFlame/_GustDir) and the dust motes drift along it
        // (EnvironmentsBuilder), which is what makes it read as one draught
        // through the room instead of two unrelated wobbles.
        private static readonly Vector3 DraftDir = new Vector3(-0.890f, 0f, -0.456f);

        // The rat's route, lifted out of BuildCellarAtmosphere. The wall-base
        // rubble added this round has to leave the animal's two holes open, and
        // the only way that clearing cannot silently drift off the holes is for
        // the rubble and the holes to read the SAME four control points.
        private static readonly Vector3 RatW0 = new Vector3(-4.00f, 0.015f, 4.42f);
        private static readonly Vector3 RatW1 = new Vector3(-2.42f, 0.015f, 1.81f);
        private static readonly Vector3 RatW2 = new Vector3(-5.00f, 0.015f, -2.30f);
        private static readonly Vector3 RatW3 = new Vector3(-0.95f, 0.015f, -4.44f);

        /// <summary>The two holes, as rects in their own wall's local (u, y) —
        /// DERIVED from the route's own endpoints, which is the whole point: the
        /// mouth the rat comes out of and the Bezier point it comes out at are one
        /// fact, and a fact typed twice is a fact that drifts. `wall` is 0 for the
        /// north wall (RatW0) and 1 for the south (RatW3), matching CellarWalls().
        ///
        /// <para>0.32 x 0.10 m authored: WallMesh keeps or drops whole 0.16 m
        /// cells, so this is two cells wide and one tall whichever cell boundary
        /// the route happens to land on, and SnappedHole reports the opening that
        /// was really cut. The mouth inside it is fist-sized; the rest of the cut
        /// is the ring of broken stone AddRatHole sets it in.</para></summary>
        private static Rect RatHoleAuthored(int wall)
        {
            float u = wall == 0 ? RatW0.x + CW / 2f : CW / 2f - RatW3.x;
            return new Rect(u - 0.16f, 0f, 0.32f, 0.10f);
        }

        // ...and the SCHEDULE those four points are only the spine of. Every
        // number here is a range the per-slot hash picks out of; EnvCritter reads
        // them as material properties and AssertRatSchedule() proves the whole
        // range clears the play-space, the walls and the barrels before the bake
        // writes them. USER FINDING, ModBuild 139: "mach das laufen ein wenig
        // mehr random statt immer den selben weg."
        //
        // W2 WANDERS LESS THAN W1, AND NORTHWARD, and that asymmetry is the whole
        // tuning problem in one line: W1's influence peaks at u~0.33, over open
        // flagstones, while W2's peaks at u~0.66 — in among the three barrels.
        // Wandering W2 as freely as W1 walked the rat straight THROUGH Barrel2
        // (the one lying on its side, whose long footprint reaches out to
        // (-2.66,-3.51) — the build gate below caught it at -0.01 m). What the
        // clearance buys back is x, which is the axis pointing at the barrels;
        // z costs almost nothing there, and a +0.28 m northward bias walks the
        // late half of the route past the barrel rather than into it. Net: the
        // family keeps 1.0 m of lateral spread instead of 1.1 m, and every
        // barrel keeps more than a rat's width of daylight.
        private const float RatPeriod = 26f, RatRunTime = 4.6f, RatSkip = 0.15f;
        private const float RatDart = 0.055f, RatStride = 24f;
        private static readonly Vector4 RatTiming = new Vector4(0.05f, 0.55f, 0.72f, 0.62f);
        private static readonly Vector4 RatModes = new Vector4(0.42f, 0.30f, 0.45f, 0.26f);
        private static readonly Vector4 RatPeak = new Vector4(0.35f, 0.40f, 0f, 0f);
        private static readonly Vector4 RatWob1 = new Vector4(0.50f, 0.80f, -0.46f, 0f);
        private static readonly Vector4 RatWob2 = new Vector4(0.12f, 0.32f, -0.12f, 0.28f);

        // ============================================================ THE BURROW
        // USER FINDING, ModBuild 142: "Wenn sie verschwindet in einem loch geht
        // sie auch nicht durch das Loch sondern wird kleiner und verschwindet
        // dann." That was one line of EnvCritter — `wp = lerp(P, wp, vis)` — and
        // it predated the holes: when it was written the "hole" was a black
        // rectangle painted on the wall, and there was nothing to walk into.
        // ModBuild 140 gave both mouths a bent pocket 13-15 cm deep with a stone
        // ring, and this round the animal finally uses it.
        //
        // WHAT THE NUMBERS BELOW HAVE TO SATISFY, because "it goes in" is not a
        // feeling, it is an inequality: the animal is 44.5 cm from nose tip to
        // tail tip, its origin sits 25.4 cm ahead of that tail tip, and the
        // pocket is only 13-15 cm deep. So travelling to the CAP hides nothing —
        // three quarters of the animal would still be hanging out of the wall.
        // The burrow therefore continues past the pocket, into the wall and down
        // under its footing, and the travel is DERIVED per hole as
        //     gap + pocket depth + tail reach + clearance
        // where `gap` is the distance the route's endpoint stands in front of
        // the wall plane. RatBurrow() computes it, AssertRatSchedule prints it,
        // and the build fails if the last centimetre of tail is not past the cap
        // by the end of the travel.
        //
        // WHY IT MAY BE OUTSIDE THE ROOM AT ALL. Because everything a ray can
        // reach from inside the cellar is the mouth aperture, and the pocket is
        // BLIND: sleeve, floor and cap are one closed opaque sock (the winding
        // gate in AddRatHole proves every face of it points at the room). The
        // stair does the same thing already — its treads and its cap hang 2.15 m
        // outside the west wall, because the room is only ever looked at from
        // inside. Anything else here would need a second, invisible parking spot
        // for an animal that is standing in a hole, which is where it should be.
        private const float RatBurrowTime = 0.45f;    // seconds per burrow travel
        private const float RatBurrowClear = 0.06f;   // margin the tail tip clears the cap by
        private const float RatBurrowDrop = 0.45f;    // how far it has gone down at full travel
        // ...and the animal's own dimensions, read off RatMesh's control points
        // rather than typed twice: the tail's last ring plus its cap, the nose
        // tip, the widest body ring and the spine wave's amplitude.
        private const float RatTailReach = 0.254f;    // |z| of the rearmost point
        private const float RatNoseReach = 0.191f;    // +z of the foremost point
        private const float RatBodyR = 0.042f;        // widest ring
        private const float RatBodyTop = 0.096f;      // that ring's top, in rat-local y
        private const float RatSway = 0.016f;         // the spine wave
        private const float RatHoleBaseY = -0.012f;   // the mouth's foot, under the flagstones
        // How far a bore may lean off its wall's normal. 34 degrees is a real
        // limit and not a taste: the south route arrives at 60 degrees, and a
        // pocket sheared that far would (a) drift 13 cm sideways inside a 32 cm
        // cut and (b) turn one sleeve wall away from the only side the mouth can
        // be seen from. At 34 the sleeve stays inside the cut and the residual
        // 22 degrees is small enough for the animal to turn through as it enters.
        private const float RatBoreLeanMax = 34f;

        /// <summary>The direction the burrow behind hole `wall` runs: unit,
        /// horizontal, pointing INTO that wall.
        ///
        /// <para>DERIVED from the route's own tangent at that endpoint, for the
        /// same reason RatHoleAuthored derives the mouth's position from it: the
        /// direction the animal arrives from and the direction its hole bores in
        /// are one fact. A hole square to the wall while the route reaches it at
        /// 60 degrees is a hole the animal enters sideways, and no amount of
        /// heading blending in the shader hides that.</para>
        ///
        /// <para>The tangent is taken at the MEAN of the wander (the hash is
        /// symmetric about the bias, so the mean displacement is the bias), and
        /// the lean is clamped — see RatBoreLeanMax.</para></summary>
        private static Vector3 RatBore(int wall)
        {
            Vector3 d1 = new Vector3(RatWob1.z, 0f, RatWob1.w);
            Vector3 d2 = new Vector3(RatWob2.z, 0f, RatWob2.w);
            // hole 0 is where the animal comes OUT (tangent points into the
            // room, so the bore is against it); hole 1 is where it goes IN.
            Vector3 t = wall == 0 ? (RatW1 + d1) - RatW0 : RatW3 - (RatW2 + d2);
            t.y = 0f;
            Vector3 dir = (wall == 0 ? -t : t).normalized;
            Vector3 n = -CellarWalls()[wall].into;     // the wall's outward normal
            float lean = Vector3.SignedAngle(n, dir, Vector3.up);
            return Quaternion.AngleAxis(Mathf.Clamp(lean, -RatBoreLeanMax, RatBoreLeanMax),
                                        Vector3.up) * n;
        }

        /// <summary>What AddRatHole really built, in room coordinates, so the
        /// animal's burrow can be measured off the pocket instead of guessing at
        /// it. Filled by AddRatHole (which is called from BuildCellarRoom's HEWN
        /// block, i.e. long before the critter's material is written).</summary>
        private struct RatHoleGeo
        {
            public bool set;
            public Vector3 mouth;    // mouth centre at the animal's ride height
            public Vector3 bore;     // unit, into the wall
            public Vector3 along;    // the wall's own +u, for the pocket's sideways bend
            public float depth;      // pocket depth ALONG THE BORE, to the cap
            public float capBend;    // how far the cap's centre is off the mouth's, along `along`
            public float mouthW, mouthH;
        }
        private static readonly RatHoleGeo[] RatHoles = new RatHoleGeo[2];

        /// <summary>One hole's burrow, as the two vectors EnvCritter walks:
        /// P(b) = end + A.xyz*b + B.xyz*b^2 - up*A.w*b^4, b in 0..1.
        ///
        /// <para>A is the bore times the travel, B the pocket's own sideways
        /// crookedness (matched at the cap, where the builder knows it exactly),
        /// A.w the quartic drop and B.w the gap between the route's endpoint and
        /// the wall plane — which the shader needs to turn a vertex position into
        /// a depth into the pocket, and therefore into a light level.</para></summary>
        private static void RatBurrow(int wall, out Vector4 A, out Vector4 B,
                                      out float travel, out float gap, out float hides)
        {
            var g = RatHoles[wall];
            if (!g.set) throw new Exception($"RatBurrow({wall}): AddRatHole has not run yet — the "
                                            + "burrow is measured off the pocket, not authored.");
            Vector3 end = wall == 0 ? RatW0 : RatW3;
            Vector3 n = -CellarWalls()[wall].into;
            // bore-travel from the route's endpoint to the wall plane. The plane
            // distance is a normal projection; the travel is that over cos(lean).
            gap = Vector3.Dot(g.mouth - end, n) / Mathf.Max(Vector3.Dot(g.bore, n), 1e-3f);
            travel = gap + g.depth + RatTailReach + RatBurrowClear;
            // ...and the travel at which the last of the animal is behind the cap
            hides = gap + g.depth + RatTailReach;
            // the sideways bend, matched where the builder knows it: at the cap.
            float bCap = (gap + g.depth) / travel;
            Vector3 lat = g.along * (g.capBend / Mathf.Max(bCap * bCap, 1e-3f));
            A = new Vector4(g.bore.x * travel, g.bore.y * travel, g.bore.z * travel, RatBurrowDrop);
            B = new Vector4(lat.x, lat.y, lat.z, gap);
        }

        /// <summary>Strides walked over one burrow travel, in the same currency as
        /// the gait on the floor: RatStride is strides per spine route, so this is
        /// that rate over the mean of the two burrows. Typing a number here
        /// instead would be authoring a second gait for the last half metre.
        /// </summary>
        private static float RatBurrowStride()
        {
            float mean = 0f;
            for (int h = 0; h < 2; h++)
            {
                RatBurrow(h, out _, out _, out float travel, out _, out _);
                mean += travel * 0.5f;
            }
            return RatStride * mean / RatArc(Vector3.zero, Vector3.zero, 1f);
        }

        /// <summary>A clock offset (_GhvrTimeOfs) at which the animal is at run
        /// progress `q` of a crossing that uses hole `hole` at that end — the
        /// rat's answer to HauntPreviewClock, and for the same reason: the entry
        /// is 0.45 s out of a 26 s slot, so a preview that did not SOLVE the
        /// shipped schedule for it could only photograph it by faking one.
        ///
        /// <para>q &lt; 0 is the emergence (q = -1 is deep in the burrow, 0 the
        /// mouth), 0..1 the crossing, and q &gt; 1 the entry (2 = parked). `hole`
        /// selects the slot: 0 is the north mouth, 1 the south.</para></summary>
        public static float RatPreviewClock(int hole, float q)
        {
            for (int n = 0; n < 4000; n++)
            {
                if (RatH(n, 0) < RatSkip) continue;
                bool turn = RatH(n, 4) < RatModes.y;
                int rev = RatH(n, 3) < RatModes.x ? 1 : 0;
                int used = q <= 0f ? rev : (turn ? rev : 1 - rev);
                if (used != hole) continue;
                float peak = turn ? RatPeak.x + RatPeak.y * RatH(n, 5) : 1f;
                float runT = RatRunTime * (turn ? 2f * peak : 1f) * (RatTiming.z + RatTiming.w * RatH(n, 2));
                float t0 = n * RatPeriod + RatPeriod * (RatTiming.x + RatTiming.y * RatH(n, 1));
                if (q < 0f) return t0 + q * RatBurrowTime;
                if (q <= 1f) return t0 + q * runT;
                return t0 + runT + (q - 1f) * RatBurrowTime;
            }
            throw new Exception($"RatPreviewClock: no crossing in 4000 slots uses hole {hole} at q={q}.");
        }

        /// <summary>The schedule's hash, character for character the one in
        /// EnvCritter.shader. It is duplicated rather than derived because the
        /// GPU cannot report and the log cannot render: this is what lets the
        /// bake MEASURE the route family, the intervals and the speeds instead of
        /// describing them. If you edit one, edit the other — the closed-form
        /// check is that the printed play-space clearance still matches what the
        /// headset does, and nothing weaker.</summary>
        private static float RatH(float n, float k)
        {
            float x = Frac((n + 1f + k * 7.13f) * 0.7548776662f);
            x = Frac(x * (x + 31.70f));
            x = Frac(x * (x + 17.31f));
            return Frac(x * (x + 43.19f));
        }
        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>The opening WallMesh actually cut. It keeps or drops whole
        /// cells, so the hole is quantised to the 0.16 m grid and is NOT the
        /// authored rect — build a reveal or a set of bars against the authored
        /// rect and they miss the stone by up to half a cell. (Half of "die
        /// Gitterstäbe schweben vor der Wand" was this; the other half was the
        /// 5 cm the bars stood proud of the wall plane.)</summary>
        private static Rect SnappedHole(Rect ho, float len, float h, float cell)
        {
            int nx = Mathf.CeilToInt(len / cell), ny = Mathf.CeilToInt(h / cell);
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!ho.Contains(new Vector2(len * (i + 0.5f) / nx, h * (j + 0.5f) / ny))) continue;
                    i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i + 1);
                    j0 = Mathf.Min(j0, j); j1 = Mathf.Max(j1, j + 1);
                }
            if (i0 == int.MaxValue) throw new Exception($"SnappedHole: {ho} cuts no cell of a {len}x{h} wall.");
            return Rect.MinMaxRect(len * i0 / nx, h * j0 / ny, len * i1 / nx, h * j1 / ny);
        }

        /// <summary>Centre of the window opening, in room coordinates.</summary>
        private static Vector3 WindowCentre()
        {
            var wh = SnappedHole(WindowHole, CW, CH, WallCell);
            return new Vector3(-CW / 2f + (wh.xMin + wh.xMax) * 0.5f,
                               (wh.yMin + wh.yMax) * 0.5f, CD / 2f);
        }

        /// <summary>Where the moon shaft's axis strikes the floor. Derived, never
        /// typed: the shaft blades, the pools they make on the flagstones and the
        /// light the rat picks up as it crosses the beam all read this, so they
        /// cannot drift apart when the window or the moon moves.</summary>
        private static Vector3 MoonBeamHit()
        {
            var mid = WindowCentre();
            var dir = -MoonDir.normalized;
            return mid + dir * ((mid.y - 0.012f) / -dir.y);
        }

        // ==================================================== THE NIGHT OUTSIDE
        // USER, ModBuild 146: "Mach das Fenster etwas größer, dass man eventuell
        // auch den Mond dahinter sehen kann."
        //
        // NOTHING WAS BEHIND THAT WINDOW. The opening was cut, the reveal was
        // built, the bars were set in it, a shaft of moonlight came through it
        // and a monster now walks past it — and the thing all of that was
        // evidence FOR did not exist as a single triangle. What a player saw
        // through the barred slot was the camera's clear colour. So "den Mond
        // dahinter sehen" was not a tuning problem at all: there was no moon
        // behind the window to see, at any window size.
        //
        // THE MOON IS THE FOREST'S MOON, BY ASSET IDENTITY. The sky patch below
        // is drawn with a COPY of Swamp_StarDome.mat — the material BuildMaterials
        // has already filled in with the observer's latitude, the galactic basis,
        // the rotation rate, the extinction, the haze, Env_Moon.png, MoonDir,
        // _MoonExtent and _MoonDiscR. Not one of those constants is restated
        // here, and that is the whole point of copying the material instead of
        // configuring a second one: the cellar's moon cannot be a different size,
        // a different colour, a different phase or in a different place from the
        // one the wood shows, because it is the same material's values. (It also
        // means the shading lane's "Light lifts the MOON indoors" now has a moon
        // indoors to lift — EnvStars' GhvrMoonSize reads the element channel.)
        //
        // RENDER ORDER IS THE WHOLE OF THE COST ARGUMENT. EnvStars ships at
        // Queue Background+5, i.e. before the opaque room, which would make the
        // sky pay full fill for its entire screen footprint and then be painted
        // over by the north wall. The copy is re-queued to 2450 — after every
        // opaque surface, before the transparents — so the depth buffer the walls
        // just wrote rejects it everywhere except through the aperture. The sky
        // is shaded on the pixels you can actually see it on and nowhere else.
        //
        // REJECTED: a flat quad of "night colour" behind the window. It has no
        // parallax, so it slides with the head exactly like a poster; and it
        // would have needed the moon painted on it at a hand-typed place, which
        // is the class of duplicate this room has already been burned by twice
        // (the light rig's hand-typed moon bearing, _Ramp's hand-typed 0.34).
        private static void AddNightOutsideWindow(Transform root, Vector3 winMid,
            float wx0, float wy0, float wx1, float wy1, float hd)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Swamp_StarDome.mat");
            if (src == null)
                throw new Exception("Swamp_StarDome.mat is missing, so the cellar window has no sky "
                                    + "behind it. BuildMaterials() must run before BuildCellar() — see "
                                    + "EnvironmentsBuilder.Build().");

            float inset = RevealJambInset, rise = RevealCillRise;
            float ox0 = wx0 + inset, ox1 = wx1 - inset, oy0 = wy0 + rise, oy1 = wy1;
            float oz = hd + RevealDepth;                       // the outer face
            var moon = MoonDir.normalized;

            // ---- WHAT THE SKY HAS TO COVER, measured rather than chosen -------
            // Every direction any player anywhere in the play space can look
            // through this opening, plus the moon. A patch that missed one of
            // them would show the void through the window, which is the failure
            // this whole object exists to remove — so the extent is DERIVED from
            // that set and then re-asserted against it below.
            float azLo = float.MaxValue, azHi = float.MinValue;
            float elLo = float.MaxValue, elHi = float.MinValue;
            void Cover(Vector3 d)
            {
                d = d.normalized;
                float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float el = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                azLo = Mathf.Min(azLo, az); azHi = Mathf.Max(azHi, az);
                elLo = Mathf.Min(elLo, el); elHi = Mathf.Max(elHi, el);
            }
            var apertures = new[]
            {
                new Vector3(ox0, oy0, oz), new Vector3(ox1, oy0, oz),
                new Vector3(ox0, oy1, oz), new Vector3(ox1, oy1, oz),
            };
            // eyes: the play-space rim at 24 bearings plus its centre, at the two
            // ends of a standing player's eye height. A crouching player looks
            // UP more steeply and a tall one less, and both are in the range.
            float pr = CellarPlaySpaceDia * 0.5f;
            foreach (float h in new[] { 1.05f, 1.85f })
                for (int k = 0; k <= 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    var eye = k == 24 ? new Vector3(0f, h, 0f)
                                      : new Vector3(Mathf.Sin(a) * pr, h, Mathf.Cos(a) * pr);
                    foreach (var ap in apertures) Cover(ap - eye);
                }
            Cover(moon);
            // ...and a margin, because a patch that ends exactly where the last
            // sightline does has a seam on it.
            const float Margin = 9f;
            azLo -= Margin; azHi += Margin; elLo -= Margin; elHi += Margin;
            // The clamp is a GATE and not a convenience: if a future window ever
            // needs more sky than this, the patch has to grow deliberately (and
            // pay for it), not be quietly cropped into a void.
            if (azLo < -85f || azHi > 85f || elLo < -30f || elHi > 82f)
                throw new Exception($"The cellar window's sky patch would have to span az "
                                    + $"{azLo:F0}..{azHi:F0} deg, el {elLo:F0}..{elHi:F0} deg to cover "
                                    + "every sightline through the opening. That is past what a patch "
                                    + "can do — the outside needs a full dome, or the window has moved "
                                    + "somewhere a patch cannot serve.");

            // ---- the patch itself ---------------------------------------------
            // R = 26 m. Far enough that the parallax across the room is a couple
            // of degrees and near enough to stay well inside any camera's far
            // plane. The mesh is built in the PATCH's own space and the object is
            // placed at the window, so EnvStars' normalize(opos) is the direction
            // FROM THE WINDOW — which is where a player who is looking through it
            // is standing, to within a metre. (The wood's dome is centred on the
            // clearing and has the same property with a bigger error.)
            const float R = 26f;
            int na = Mathf.Max(8, Mathf.CeilToInt((azHi - azLo) / 7f));
            int ne = Mathf.Max(6, Mathf.CeilToInt((elHi - elLo) / 7f));
            var sky = new Acc();
            Vector3 Dir(float azDeg, float elDeg)
            {
                float ca = Mathf.Cos(elDeg * Mathf.Deg2Rad);
                return new Vector3(Mathf.Sin(azDeg * Mathf.Deg2Rad) * ca,
                                   Mathf.Sin(elDeg * Mathf.Deg2Rad),
                                   Mathf.Cos(azDeg * Mathf.Deg2Rad) * ca);
            }
            for (int j = 0; j < ne; j++)
                for (int i = 0; i < na; i++)
                {
                    float a0 = Mathf.Lerp(azLo, azHi, i / (float)na), a1 = Mathf.Lerp(azLo, azHi, (i + 1) / (float)na);
                    float e0 = Mathf.Lerp(elLo, elHi, j / (float)ne), e1 = Mathf.Lerp(elLo, elHi, (j + 1) / (float)ne);
                    var p00 = Dir(a0, e0) * R; var p10 = Dir(a1, e0) * R;
                    var p11 = Dir(a1, e1) * R; var p01 = Dir(a0, e1) * R;
                    int b = sky.Count;
                    // normals point back at the observer; EnvStars is Cull Off, so
                    // there is no winding hazard here to gate (the reveal, whose
                    // shader does cull, gets AssertRevealFacesIn instead).
                    sky.Vert(p00, -p00.normalized, new Vector2(0, 0), Color.white);
                    sky.Vert(p10, -p10.normalized, new Vector2(1, 0), Color.white);
                    sky.Vert(p11, -p11.normalized, new Vector2(1, 1), Color.white);
                    sky.Vert(p01, -p01.normalized, new Vector2(0, 1), Color.white);
                    sky.Quad(b);
                }
            var skyMesh = SaveMesh("Env_C_NightSky.asset", sky.Build("Env_C_NightSky"));
            var skyMat = NewRoomMat("C_NightSky.mat", "GloomhavenVR/EnvStars");
            skyMat.CopyPropertiesFromMaterial(src);            // the wood's sky, verbatim
            skyMat.renderQueue = 2450;                          // after the opaque room; see above
            Place(root, "NightSky", skyMesh, winMid, Vector3.zero, Vector3.one, skyMat);

            // ---- the ground it stands on --------------------------------------
            // A cellar window is at outside ground level, so the horizon is at
            // the OUTER CILL and the bottom half of the view through the opening
            // is earth, not sky. Without it the sky patch runs all the way down
            // and the creature that walks past (HauntFigures card 0) walks
            // through a starfield at ankle height.
            //
            // Pitch black, exactly like the stair shaft's end cap and for the
            // same reason: this is ground seen from inside an unlit cellar,
            // through a slot, at a grazing angle from below. Anything else on it
            // would be detail invented for a surface nobody can resolve — and a
            // black horizon is what actually cuts the sky off.
            float gy = oy0;
            var gnd = new Acc();
            {
                var c = new Vector3(0f, gy, oz + 17f);
                int b = gnd.Count;
                gnd.Vert(c + new Vector3(-22f, 0f, -17f), Vector3.up, new Vector2(0, 0), Color.white);
                gnd.Vert(c + new Vector3(22f, 0f, -17f), Vector3.up, new Vector2(1, 0), Color.white);
                gnd.Vert(c + new Vector3(22f, 0f, 17f), Vector3.up, new Vector2(1, 1), Color.white);
                gnd.Vert(c + new Vector3(-22f, 0f, 17f), Vector3.up, new Vector2(0, 1), Color.white);
                gnd.Quad(b);
            }
            var gndMesh = SaveMesh("Env_C_NightGround.asset", gnd.Build("Env_C_NightGround"));
            var gndMat = NewRoomMat("C_NightGround.mat", "GloomhavenVR/EnvRoom");
            gndMat.SetColor("_Tint", Color.black);
            var gndGo = Place(root, "NightGround", gndMesh, Vector3.zero, Vector3.zero, Vector3.one, gndMat);
            // WINDING GATE. This one DOES cull, and it is only ever seen from
            // above and from inside — so its single quad's normal must be +Y.
            // Same class of check as the reveal's, and the same reason: four
            // meshes in this project have shipped wound against the side they are
            // seen from.
            {
                var gv = Verts(gndMesh); var gt = gndMesh.triangles;
                for (int i = 0; i < gt.Length; i += 3)
                    if (Vector3.Cross(gv[gt[i + 1]] - gv[gt[i]], gv[gt[i + 2]] - gv[gt[i]]).y <= 0f)
                        throw new Exception("The night ground outside the cellar window is wound "
                                            + "downward: Cull Back would hide it and the sky patch "
                                            + "would run to the bottom of the opening.");
            }

            Debug.Log($"[GloomhavenVR][Env] Cellar NIGHT OUTSIDE: a {azHi - azLo:F0} x {elHi - elLo:F0} deg "
                      + $"patch of the WOOD'S OWN SKY (a copy of Swamp_StarDome.mat, so the moon's "
                      + $"bearing, size, phase and colour are that material's and not a second set of "
                      + $"numbers), radius {R:F0} m, centred on the window at ({winMid.x:F2},{winMid.y:F2},"
                      + $"{winMid.z:F2}), {sky.T.Count / 3} tris, drawn at queue {skyMat.renderQueue} so the "
                      + $"walls' own depth rejects every pixel of it except through the opening. "
                      + $"The moon bears az {Mathf.Atan2(moon.x, moon.z) * Mathf.Rad2Deg:F1} deg, "
                      + $"alt {Mathf.Asin(moon.y) * Mathf.Rad2Deg:F1} deg — inside the patch by "
                      + $"{Mathf.Min(Mathf.Atan2(moon.x, moon.z) * Mathf.Rad2Deg - azLo, azHi - Mathf.Atan2(moon.x, moon.z) * Mathf.Rad2Deg):F0} deg "
                      + $"of azimuth and {Mathf.Min(Mathf.Asin(moon.y) * Mathf.Rad2Deg - elLo, elHi - Mathf.Asin(moon.y) * Mathf.Rad2Deg):F0} deg of "
                      + $"altitude. Ground outside at y {gy:F3} (= the OUTER cill, i.e. the height "
                      + $"HauntFigures walks its creature at is the INNER cill {wy0:F3}).");
        }

        /// <summary>CAN THE MOON ACTUALLY BE SEEN THROUGH THIS WINDOW, AND FROM
        /// WHERE — as arithmetic, with a gate on the answer.
        ///
        /// <para>USER, ModBuild 146: "dass man eventuell auch den Mond dahinter
        /// sehen kann". That is a GEOMETRIC PROMISE and nothing in this file
        /// asserted it, so it gets a gate that fires, which is this room's house
        /// style for every other promise it makes (the play space, the rat's
        /// schedule, the shelf's landing).</para>
        ///
        /// <para><b>THE ARITHMETIC.</b> The moon is a direction, not a place:
        /// MoonDir is 40.0 deg above the horizon on a bearing 40.0 deg east of
        /// the window's own normal. A sightline to it climbs ky = dy/dz = 1.0953
        /// metres for every metre it travels north. It has to leave through BOTH
        /// rectangles — the inner opening at the wall's face and the smaller,
        /// higher outer opening at the far side of the embrasure — so the tunnel
        /// eats ky*RevealDepth of the opening's height and kx*RevealDepth of its
        /// width before the ray is out of the stone at all. What is left is the
        /// ACCEPTANCE: the set of points on the inner opening a moon-bearing ray
        /// may cross. Trace those back to eye height and you get the exact patch
        /// of floor a player has to be standing on.</para>
        ///
        /// <para><b>AND THE ANSWER IS NOT "FROM THE BOARD", AND CANNOT BE MADE
        /// TO BE.</b> The one number that decides how far back the moon can be
        /// seen from is the height of the OUTER head: reach = (head - eye)/ky -
        /// RevealDepth, and nothing else appears in it. Standing at the near rim
        /// of the play space, in the window's own direction, a player's eye is
        /// about 4.7 m from the outer face; a sightline at 40 deg would leave
        /// that wall at 5.36 m above the floor, and the cellar's ceiling is at
        /// 3.30 m. THE CEILING IS WHAT HIDES THE MOON FROM THE BOARD, not the
        /// window — no aperture in a wall 4.5 m away can undo a 3.3 m ceiling,
        /// and the only opening that could is one in the ceiling itself. Every
        /// number this method prints is there so that a future round reads that
        /// off the log instead of re-deriving it.</para>
        ///
        /// <para>What the window CAN do, and now does, is put the moon a couple
        /// of steps away: walk to the window and it is there, in a real sky, with
        /// the bars across it. "Eventuell" is exactly the right word for it and
        /// this method prints how eventuell, in metres.</para></summary>
        private static void AssertMoonThroughWindow(float wx0, float wy0, float wx1, float wy1, float hd)
        {
            var d = MoonDir.normalized;
            float kx = d.x / d.z, ky = d.y / d.z;
            float inset = RevealJambInset, rise = RevealCillRise, D = RevealDepth;
            float dx = kx * D, dy = ky * D;

            // the acceptance on the INNER opening: inside it, and inside the
            // outer opening once the ray has climbed and slid across the bore
            float axLo = Mathf.Max(wx0, wx0 + inset - dx), axHi = Mathf.Min(wx1, wx1 - inset - dx);
            float ayLo = Mathf.Max(wy0, wy0 + rise - dy), ayHi = Mathf.Min(wy1, wy1 - dy);
            if (axHi <= axLo || ayHi <= ayLo)
                throw new Exception($"No sightline to the moon passes through the cellar window at all: "
                                    + $"the {wx1 - wx0:F2} x {wy1 - wy0:F2} m opening loses {dx:F2} x {dy:F2} m "
                                    + $"of acceptance to a {D:F2} m reveal (plus {inset:F2} m of jamb splay "
                                    + $"and {rise:F2} m of cill rise), which closes it. Raise the head, "
                                    + "widen the opening, or thin the wall.");

            // trace the acceptance back to eye height. Further back = higher yc,
            // so the corner (axHi, ayHi) is the one nearest the room centre.
            Vector3 Eye(float xc, float yc, float h)
            {
                float t = (yc - h) / d.y;                       // metres along -MoonDir
                return new Vector3(xc - d.x * t, h, hd - d.z * t);
            }
            const float EyeStand = 1.60f;
            var near = Eye(axHi, ayHi, EyeStand);
            var far = Eye(axLo, ayLo, EyeStand);
            float nearR = new Vector2(near.x, near.z).magnitude;
            float farR = new Vector2(far.x, far.z).magnitude;
            float playR = CellarPlaySpaceDia * 0.5f;

            // The gate. It is NOT "the moon is visible from the play space" —
            // that is provably impossible in this room (see the doc comment) and
            // a gate nobody can satisfy is a gate somebody deletes. It is the two
            // things that ARE promises: a sightline exists at standing height,
            // and the place you have to stand to use it is INSIDE THE ROOM.
            if (near.z >= hd || near.z < -CD * 0.5f || Mathf.Abs(near.x) > CW * 0.5f)
                throw new Exception($"The moon can only be seen through the cellar window from "
                                    + $"({near.x:F2},{near.z:F2}), which is not inside the room "
                                    + $"({CW:F1} x {CD:F1} m). The opening admits the bearing but no "
                                    + "player can stand where it admits it from.");

            // ...and the number a future round will want: how high the OUTER head
            // would have to stand for a player anywhere in the play space to see
            // the moon. Scanned over the disc rather than solved at one point,
            // because the binding constraint is a pair (how far back, and how far
            // sideways) and the best compromise is not at either extreme: a
            // sightline has to reach the aperture in x AND clear the head in y,
            // and moving toward the window costs x while moving along the wall
            // costs y. The scan is 200x200 over the disc and reports the minimum.
            float needHead = float.PositiveInfinity; Vector2 needAt = Vector2.zero;
            float bestX = float.MaxValue; Vector2 bestXAt = Vector2.zero;
            for (int iz = 0; iz <= 200; iz++)
                for (int ix = 0; ix <= 200; ix++)
                {
                    float xe = -playR + 2f * playR * ix / 200f;
                    float ze = -playR + 2f * playR * iz / 200f;
                    if (xe * xe + ze * ze > playR * playR) continue;
                    // where this eye's moon-bearing sightline crosses the OUTER face
                    float run = hd + D - ze;
                    if (run <= 0f) continue;
                    float xc = xe + kx * run;
                    if (xc < bestX) { bestX = xc; bestXAt = new Vector2(xe, ze); }
                    if (xc < wx0 + inset || xc > wx1 - inset) continue;   // misses the opening
                    float h = EyeStand + ky * run;
                    if (h < needHead) { needHead = h; needAt = new Vector2(xe, ze); }
                }

            Debug.Log($"[GloomhavenVR][Env] Cellar MOON THROUGH THE WINDOW (user: \"dass man eventuell "
                      + $"auch den Mond dahinter sehen kann\"): the moon bears alt "
                      + $"{Mathf.Asin(d.y) * Mathf.Rad2Deg:F1} deg / az {Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg:F1} deg, "
                      + $"so a sightline climbs {ky:F3} m per metre north and slides {kx:F3} m east.\n"
                      + $"    A {D:F2} m reveal costs it {dy:F2} m of the opening's {wy1 - wy0:F2} m height "
                      + $"and {dx:F2} m of its {wx1 - wx0:F2} m width; the acceptance left on the inner "
                      + $"face is x {axLo:F3}..{axHi:F3} ({axHi - axLo:F2} m), y {ayLo:F3}..{ayHi:F3} "
                      + $"({ayHi - ayLo:F2} m).\n"
                      + $"    AT A STANDING EYE ({EyeStand:F2} m) the moon is visible from the strip "
                      + $"x {near.x:F2}..{far.x:F2}, z {near.z:F2}..{far.z:F2} — {nearR:F2} to {farR:F2} m "
                      + $"from the room centre, against a PlaySpace radius of {playR:F2} m. So it is "
                      + $"{nearR - playR:F2} m OUTSIDE the board's disc: two steps toward the window, "
                      + "not from the table.\n"
                      + "    AND IT CANNOT BE OTHERWISE, IN TWO INDEPENDENT WAYS.\n"
                      + (float.IsInfinity(needHead)
                         ? $"    (a) THE BEARING. From anywhere in the play space a moon-bearing "
                           + $"sightline crosses the outer wall plane at x >= {bestX:F2} (best at "
                           + $"({bestXAt.x:F2},{bestXAt.y:F2})), and this window's outer opening ENDS at "
                           + $"x = {wx1 - inset:F2}. It misses the hole by {bestX - (wx1 - inset):F2} m "
                           + "sideways, so no head height whatsoever would help: the moon is 40 deg east "
                           + "of north and the window is 1.35 m WEST of the room centre.\n"
                         : $"    (a) The best-placed player in the play space "
                           + $"({needAt.x:F2},{needAt.y:F2}) would need the outer head at "
                           + $"{needHead:F2} m.\n")
                      + $"    (b) THE CEILING. Even directly under the window's own bearing, a sightline "
                      + $"at {Mathf.Asin(d.y) * Mathf.Rad2Deg:F0} deg leaving the play-space rim reaches "
                      + $"{EyeStand + ky * (hd + D - playR):F2} m at the outer face, and this room's "
                      + $"ceiling is at {CH:F2} m with the head at {wy1:F3} m. The CEILING hides the moon "
                      + "from the board, not the window — the only opening that could show it from there "
                      + "is one in the ceiling.");
        }

        public static void BuildCellarRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);
            // SHELF RIDERS: the pose does not exist until the shelf is placed and
            // measured, and it must not leak from one bake of one room into the
            // next. Both are cleared here and again at the top of the forest.
            Tip = null; TipUse.Clear();

            // Light positions are patched in AFTER the props are stacked (the
            // candles sit ON the props — bounds-derived); see rig fixup below.
            // See the CELLAR header for what every number here does and why.
            var rig = new LightRig
            {
                // The ambient is the floor under everything: it is the only term
                // that reaches surfaces no candle and no moonbeam can, so it sets
                // how much of the room exists at all. Slightly LOWER and colder
                // than ModBuild 134's — with the candle pools now small, a warm
                // ambient was the only thing still making the whole room amber.
                ambUp = new Color(0.028f, 0.032f, 0.045f),
                ambDown = new Color(0.020f, 0.018f, 0.015f),
                // THE MOON, not a hand-typed lookalike. It used to be
                // (0.25,0.62,0.74) — 21 deg off the moon you can see through the
                // window, so the shaft and the shading disagreed about where the
                // light came from. Same class of silent mistake as the forest's
                // unset _RimDir; it is now the shared constant, by construction.
                dirWorld = MoonDir,
                // Raised in the second pass of this round: with the candles no
                // longer washing the walls, the moon is what gives the ROOM its
                // shape. It rakes in from the north-east, so the south and west
                // walls carry a cold wash and the two the moon cannot see stay
                // black — which is the geometry of the room, told in light.
                // Deliberately far bluer than it "should" be: the stone's albedo
                // is warm, so a neutral moon term comes out grey and the wash
                // reads as fog, not moonlight. The colour has to survive the
                // multiply.
                dirCol = new Color(0.048f, 0.070f, 0.128f),
                // 16, not 22: at 22 a candle standing ON the bookshelf could not
                // light the bookshelf. The pool has to have a soft outer half.
                ptHard = 16f,
                // ELEMENT ART: how far "the periphery" is. 5.0 m puts the far
                // wall at 1.0 on the ramp and the edge of the play space at
                // ~0.65, so frost owns the walls, touches the flagstones the
                // board stands on hardly at all, and never reaches the middle.
                elemRad = 5.0f,
                // 0.70: at 1.0 the first bake lit the south-west corner — the one
                // the cellar lane built as "a corner nobody ever repaired, no
                // candle reaches it" — well enough to read the barrels in it.
                // Fire may fill this room with firelight; it may not repeal the
                // room's own geometry of light.
                elemWarm = 0.70f,
                points = new[]
                {
                    new PLight(new Vector3(3.55f, 1.06f, 3.10f), 3.10f, new Color(1f, 0.60f, 0.30f) * 2.10f, 0.90f), // table candles
                    new PLight(OnShelf(-0.14f, 0f) + Vector3.up * 2.00f, 2.90f, new Color(1f, 0.56f, 0.26f) * 1.75f, 0.95f), // shelf candle (patched below)
                    new PLight(new Vector3(-1.55f, 1.30f, -3.95f), 3.00f, new Color(1f, 0.57f, 0.27f) * 1.90f, 0.88f), // crate candle
                },
            };

            Material SurfMat(string file, string texBase, float uvScale, Transform xf, float bump, float tintMul)
            {
                var m = NewRoomMat(file, "GloomhavenVR/EnvRoom");
                m.SetTexture("_MainTex", Imp(texBase + "_alb"));
                m.SetTexture("_BumpMap", Imp(texBase + "_nrm"));
                m.SetFloat("_BumpScale", bump);
                Defer(m, xf, tintMul);
                return m;
            }

            float hw = CW / 2f, hd = CD / 2f;

            // ---- floor (CLOSED, opaque — hard requirement) ----
            // Every prop grounds against THIS function (see prop GROUNDING): the
            // flagstones undulate by ±6 mm, so a prop dropped to a flat y=0 could
            // already read as floating on the high spots.
            _groundY = CellarFloorY;
            // 60x52 (not 30x26): the contact pools painted under the props at the
            // end of the room need vertices to live on — 0.35 m spacing smeared
            // them into the whole floor.
            var floorMesh = SaveMesh("Env_C_Floor.asset", GridMeshXZ(-hw, -hd, hw, hd, 60, 52,
                CellarFloorY, (x, z) => Color.white, 2.6f));
            var floorGo = Place(root, "Floor", floorMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            var floorMat = SurfMat("C_Floor.mat", "monastery_stone_floor", 2.6f, floorGo.transform, 1.0f, 1f);
            // ELEMENT ART / SURFACE GROWTH — EARTH on the flagstones.
            //
            // 1.0, where ModBuild 142 had 0.30, and it is NOT a threefold
            // increase: the number changed units. It used to be the OPACITY of a
            // green tint over the whole floor; it is now the fraction of the
            // frontier's travel this surface gets, i.e. a coverage. At 1.0 with
            // the periphery ramp (0.10 + 1.90*rim) the flagstones at the wall
            // are fully in reach of the frontier and the ones under the board are
            // beyond it at any strength — the floor the board stands on still
            // keeps exactly the colour it was tuned to, which was the reason for
            // the 0.30 and is preserved by the ramp instead.
            floorMat.SetFloat("_ElemMoss", 1.0f);
            floorGo.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // ---- walls (N has window + a rat hole, S has the other rat hole,
            //      W has stair doorway) ----
            // The rat holes are REALLY CUT now (ModBuild 140): they used to be a
            // black quad stuck on the wall, and a flat black patch 0.6 m from a VR
            // camera reads as a sticker. AddRatHole builds the mouth, the recess
            // behind the opening and the stone it took with it — see there.
            var wallN = SaveMesh("Env_C_WallN.asset", WallMesh(CW, CH, 0.16f, new[] { WindowHole, RatHoleAuthored(0) }, 3.3f, 911, uOff: 0.00f));
            var wallS = SaveMesh("Env_C_WallS.asset", WallMesh(CW, CH, 0.16f, new[] { RatHoleAuthored(1) }, 3.3f, 912, uOff: 1.31f));
            var wallE = SaveMesh("Env_C_WallE.asset", WallMesh(CD, CH, 0.16f, Array.Empty<Rect>(), 3.3f, 913, uOff: 2.17f));
            var wallW = SaveMesh("Env_C_WallW.asset", WallMesh(CD, CH, 0.16f, new[] { StairHole }, 3.3f, 914, uOff: 0.73f));
            GameObject wallGoN = null;
            void Wall(string n, Mesh mesh, Vector3 pos, float yaw)
            {
                var go = Place(root, n, mesh, pos, new Vector3(0, yaw, 0), Vector3.one, null);
                var m = SurfMat("C_" + n + ".mat", "medieval_blocks_05", 3.4f, go.transform, 1.15f, 1f);
                // SURFACE GROWTH — "Wände und Böden teilweise mit Moos bewachsen".
                // The WALLS are the half of that sentence ModBuild 142 could not
                // answer at all: _ElemMoss was 0 on every wall in the room, so
                // Earth had the flagstones and nothing else. A wall is where moss
                // most obviously belongs in a damp cellar, and the affinity puts
                // it at the FOOT of the run and in the mortar courses rather than
                // over the whole face (EnvRoom.shader, `place`).
                m.SetFloat("_ElemMoss", 1.0f);
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                if (n == "WallN") wallGoN = go;
            }
            Wall("WallN", wallN, new Vector3(-hw, 0, hd), 0);        // runs +X, faces -Z (into room)
            Wall("WallS", wallS, new Vector3(hw, 0, -hd), 180);
            Wall("WallE", wallE, new Vector3(hw, 0, hd), 90);        // runs -Z
            Wall("WallW", wallW, new Vector3(-hw, 0, -hd), 270);

            // ---- HEWN: the twelve edges of the box, broken ----
            // The HEWN section further down says WHY each piece exists; what is
            // chosen HERE is which run gets which clearing and which corner gets
            // which treatment. No two corners are alike, deliberately — a room
            // whose four corners are the same corner is still a rectangle, just a
            // lumpy one.
            var walls = CellarWalls();
            var stone = new Acc();
            var timber = new Acc();
            var hewn = new List<string>();
            {
                // Clear zones. The stair doorway is taken from SnappedHole, i.e.
                // the quantised rect the wall really cut — authoring against the
                // unsnapped rect is what floated the window bars in ModBuild 134 —
                // and the two rat holes are taken from the rat's own route.
                var door = SnappedHole(StairHole, CD, CH, WallCell);
                var skirtGate = new[]
                {
                    ClearOf((RatW0.x + hw, 0.30f)),      // N: the rat comes out here
                    ClearOf((hw - RatW3.x, 0.30f)),      // S: and goes in here
                    ClearOf(),                            // E: nothing to keep clear
                    ClearOf(((door.xMin + door.xMax) * 0.5f, door.width * 0.5f + 0.12f)),  // W: the stairs
                };
                for (int i = 0; i < 4; i++) hewn.Add(AddWallSkirt(stone, walls[i], skirtGate[i]));

                // The two rat holes, built into the openings WallMesh really cut
                // and welded into the same stonework mesh as the skirting — same
                // material, same object-space light rig, no extra draw call. They
                // go in AFTER the skirt so the loose blocks around each mouth lie
                // on top of the run rather than under it.
                for (int i = 0; i < 2; i++)
                    hewn.Add(AddRatHole(stone, walls[i], i,
                                        SnappedHole(RatHoleAuthored(i), CW, CH, WallCell),
                                        RatBore(i), 6203 + i * 197));

                // Four corners, four different lies:
                //   NE  a full quoin stack with the deepest step — the only corner
                //       a candle really reaches, so the only one that has to hold
                //       up at close range;
                //   NW  a cant the whole height, cut stone only at the bottom;
                //   SE  quoins to head height and a cant above them;
                //   SW  the dark one: a heavy heap and a wide cant, almost no
                //       dressed stone — a corner nobody ever repaired.
                var ne = new Vector3(hw, 0f, hd); var nw = new Vector3(-hw, 0f, hd);
                var se = new Vector3(hw, 0f, -hd); var sw = new Vector3(-hw, 0f, -hd);
                int quoins = 0, rubble = 0;
                quoins += AddCornerQuoins(stone, ne, Vector3.back, Vector3.left, 0.12f, 3.05f, 12, 0.045f, 0.105f, 7101);
                quoins += AddCornerQuoins(stone, nw, Vector3.back, Vector3.right, 0.10f, 1.35f, 4, 0.040f, 0.085f, 7213);
                AddCornerCant(stone, nw, Vector3.back, Vector3.right, 1.20f, CH - 0.02f, 0.07f, 0.19f, 7217);
                quoins += AddCornerQuoins(stone, se, Vector3.forward, Vector3.left, 0.14f, 2.05f, 7, 0.035f, 0.095f, 7331);
                AddCornerCant(stone, se, Vector3.forward, Vector3.left, 1.95f, CH - 0.02f, 0.06f, 0.16f, 7337);
                quoins += AddCornerQuoins(stone, sw, Vector3.forward, Vector3.right, 1.55f, 2.60f, 3, 0.030f, 0.070f, 7447);
                AddCornerCant(stone, sw, Vector3.forward, Vector3.right, 0.35f, CH - 0.02f, 0.05f, 0.22f, 7451);
                rubble += AddCornerRubble(stone, ne, Vector3.back, Vector3.left, 3, 0.30f, 7501);
                rubble += AddCornerRubble(stone, nw, Vector3.back, Vector3.right, 4, 0.36f, 7509);
                rubble += AddCornerRubble(stone, se, Vector3.forward, Vector3.left, 2, 0.26f, 7517);
                rubble += AddCornerRubble(stone, sw, Vector3.forward, Vector3.right, 7, 0.44f, 7523);
                hewn.Add($"corners: NE quoins to 3.05 m (step 4.5-10.5 cm); NW cant 1.20-3.28 m + 4 quoins; "
                       + $"SE quoins to 2.05 m + cant above; SW cant 0.35-3.28 m (5-22 cm) + 3 quoins; "
                       + $"{quoins} courses and {rubble} corner blocks");

                // The cove stops where a wall plate takes over (N, S) and where a
                // beam with its corbel comes into the wall (E, W) — the brief is
                // explicit that nothing added up here may touch those.
                var coveGate = new Func<float, float>[4];
                for (int i = 0; i < 4; i++)
                {
                    var zones = new List<(float, float)>();
                    foreach (var p in CellarPlates)
                        if (p.wall == i) zones.Add(((p.t0 + p.t1) * 0.5f, (p.t1 - p.t0) * 0.5f + 0.14f));
                    // a beam is <=0.33 m across and its corbel <=0.40 m; 0.35 m of
                    // half-width clears both with the cove's own 22 cm reach on top
                    for (int b = 0; b < 4; b++)
                    {
                        if (i == 2) zones.Add((hd - CellarBeamZ(b), 0.35f));
                        if (i == 3) zones.Add((CellarBeamZ(b) + hd, 0.35f));
                    }
                    coveGate[i] = ClearOf(zones.ToArray());
                }
                for (int i = 0; i < 4; i++) hewn.Add(AddCeilingCove(stone, walls[i], coveGate[i]));

                // ...and the plates themselves, bedded 12 mm INTO the planks so the
                // joint above them stays closed however the ceiling sags.
                foreach (var p in CellarPlates)
                {
                    var w = walls[p.wall];
                    const float dep = 0.15f, thick = 0.13f;
                    Vector3 c = w.p0 + w.along * ((p.t0 + p.t1) * 0.5f)
                              + w.into * ((dep - 0.02f) * 0.5f)
                              + Vector3.up * (CH + 0.012f - thick * 0.5f);
                    AddHewnBlock(timber, c, Quaternion.LookRotation(w.into, Vector3.up),
                                 new Vector3(p.t1 - p.t0, thick, dep + 0.02f),
                                 0.95f, 0.05f, 0.008f, 1.3f,
                                 7600 + p.wall * 31 + Mathf.RoundToInt(p.t0 * 10f), Grey(0.88f));
                }
                hewn.Add("wall plates: "
                       + string.Join(", ", CellarPlates.Select(p => $"{walls[p.wall].name} {p.t1 - p.t0:F2} m")));
            }

            // ---- ceiling: planks that SAG between the beams ----
            // 14x20, not 8x8: z=20 puts a grid line exactly on all four beams
            // (9.0/20 = 0.45 and the beams sit at multiples of 1.8), so the sag
            // really is pinned at its supports in the mesh and not merely in the
            // height function. It stays CLOSED and opaque — a hole in this plane
            // shows the void, which is a hard requirement.
            var ceilMesh = SaveMesh("Env_C_Ceil.asset",
                GridMeshXZ(-hw, -hd, hw, hd, 14, 20, CellarCeilY, null, 2.4f, faceDown: true));
            var ceilGo = Place(root, "Ceiling", ceilMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            ceilGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Ceiling.mat", "dark_wooden_planks", 2.4f, ceilGo.transform, 0.9f, 0.85f);

            // ---- the load-bearing stone, which is no longer eight cubes ----
            var corbels = CellarCorbels();
            foreach (var c in corbels) AddCorbel(stone, c);
            // ...and the beams they carry, derived FROM them (each beam reads the
            // bearing height of the two corbels under its own ends)
            for (int i = 0; i < 4; i++)
                hewn.Add(AddCellarBeam(timber, i, corbels[i * 2], corbels[i * 2 + 1]));

            // ONE mesh and ONE material each. Eight corbel transforms sharing a
            // material would all have been lit from the first one's position (see
            // MergeInto); welded at the identity transform, object space IS room
            // space and every baked light is exact. It also takes the ceiling from
            // 12 draw calls to 2, and EnvRoom shades per PIXEL out of i.opos, so
            // nothing is lost by the coarse station spacing.
            var stoneMesh = SaveMesh("Env_C_Stonework.asset", stone.Build("Env_C_Stonework"));
            var stoneGo = Place(root, "Stonework", stoneMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            var stoneMat = SurfMat("C_Stonework.mat", "medieval_blocks_05", 3.4f, stoneGo.transform, 1.15f, 0.86f);
            // the vertex colours are authored contact shading: dark down where the
            // rubble meets the flagstones and up inside the cove, bright on the
            // crests and on the quoin faces that catch the moon
            stoneMat.SetFloat("_VCol", 1f);
            // SURFACE GROWTH — the skirting, the quoins and the rubble heaps are
            // the wettest cut stone in the room (they stand IN the floor), so
            // they take moss in full, and their normal map is the deepest in the
            // room, which is what the affinity's `grain` term is for.
            stoneMat.SetFloat("_ElemMoss", 1.0f);
            stoneGo.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;

            var timberMesh = SaveMesh("Env_C_Timber.asset", timber.Build("Env_C_Timber"));
            var timberGo = Place(root, "CeilingTimber", timberMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            var timberMat = SurfMat("C_Timber.mat", "dark_wooden_planks", 1.3f, timberGo.transform, 0.9f, 0.9f);
            timberMat.SetFloat("_VCol", 1f);
            // ...and the ceiling timber takes it at a third: old damp wood does
            // go green, but three metres up and dry it goes last. The affinity's
            // `foot` term already puts it near zero up there; this is the second
            // guard, because a green CEILING is the one place moss would read as
            // a bug rather than as damp.
            timberMat.SetFloat("_ElemMoss", 0.35f);
            timberGo.GetComponent<MeshRenderer>().sharedMaterial = timberMat;

            Debug.Log("[GloomhavenVR][Env] Cellar HEWN — junction irregularity:\n  "
                      + string.Join("\n  ", hewn)
                      + $"\n  ceiling: 14x20 planks, sag <=18 mm, pinned at the walls and at the beams "
                      + $"z {CellarBeamZ(0):F2}/{CellarBeamZ(1):F2}/{CellarBeamZ(2):F2}/{CellarBeamZ(3):F2}"
                      + $"\n  stonework {stone.T.Count / 3} tris, nearest the room centre below 2.2 m: "
                      + $"{MinRadiusBelow(stone, 2.2f):F2} m (PlaySpace radius {CellarPlaySpaceDia * 0.5f:F2} m, "
                      + $"walls stand at {hd:F2}/{hw:F2} m); timber {timber.T.Count / 3} tris, all above 2.2 m");

            // ---- the window: reveal, sill, bars, and the moonlight through it ----
            // The opening WallMesh really cut, in ROOM coordinates. Everything
            // below is derived from it, so nothing can be half a cell out.
            var wh = SnappedHole(WindowHole, CW, CH, WallCell);
            float wx0 = -hw + wh.xMin, wx1 = -hw + wh.xMax;
            float wy0 = wh.yMin, wy1 = wh.yMax;
            var winMid = new Vector3((wx0 + wx1) * 0.5f, (wy0 + wy1) * 0.5f, hd);
            // THE NUMBERS THE OTHER LANES READ. HauntFigures.Events.cs quotes
            // this line verbatim in its card-0 doc comment (the creature that
            // walks past outside), so it prints the outer face and the outer
            // opening too — the outer opening is what actually frames that walk,
            // and since ModBuild 146 it is NOT the same rect as the inner one.
            Debug.Log($"[GloomhavenVR][Env] Cellar window opening (snapped): x {wx0:F3}..{wx1:F3}, "
                      + $"y {wy0:F3}..{wy1:F3}, at the wall's inner face z {hd:F2}; "
                      + $"reveal depth {RevealDepth:F2} m, so the outer face is z {hd + RevealDepth:F2} "
                      + $"and the SPLAYED outer opening is x {wx0 + RevealJambInset:F3}.."
                      + $"{wx1 - RevealJambInset:F3}, y {wy0 + RevealCillRise:F3}..{wy1:F3}. "
                      + $"The cill is unmoved at y {wy0:F3} = the outside ground level.");

            // THE PROMISE, AND ITS GATE. Both before anything is built off the
            // opening, so a window that cannot show the moon fails the bake
            // rather than shipping and being found on hardware.
            AssertMoonThroughWindow(wx0, wy0, wx1, wy1, hd);
            AddNightOutsideWindow(root, winMid, wx0, wy0, wx1, wy1, hd);

            // Reveal (jambs + head + cill). Without it the wall is a zero-
            // thickness plane, there is no "inside the opening" to put the bars
            // in, and anything placed near it necessarily floats in front of it.
            var revealRaw = RevealMesh(wx0, wy0, wx1, wy1, hd, RevealDepth,
                                       RevealJambInset, RevealCillRise);
            AssertRevealFacesIn(revealRaw, wx0, wy0, wx1, wy1, hd, RevealDepth);
            var revealMesh = SaveMesh("Env_C_Reveal.asset", revealRaw);
            var revealGo = Place(root, "WindowReveal", revealMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            // tintMul 0.9 -> 1.25 (ModBuild 136): the sill and the west jamb are
            // the only two surfaces in the room the moon strikes head-on, and
            // they are what makes the window read as a SOURCE rather than a
            // hole. Raising the albedo raises the lit faces and the unlit ones
            // by the same factor, so the reveal's own light/dark reading — which
            // the baked rig gets right for free — is preserved.
            revealGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Reveal.mat", "medieval_blocks_05", 3.4f, revealGo.transform, 1.0f, 1.25f);

            // Bars: FOUR uprights standing in the middle of the reveal (they used
            // to sit 5 cm proud of the wall plane, which is exactly what "die
            // Gitterstäbe schweben vor der Wand" was), their feet and heads
            // buried 3 cm into sill and head so they read as set in the stone.
            // Welded into ONE mesh in room coordinates: the light rig is baked in
            // OBJECT space, so four transforms sharing one material would all be
            // lit from the first bar's position.
            float barZ = hd + RevealDepth * 0.45f;
            // HOW MANY BARS IS NOT A TASTE DECISION, it is a pitch. A smith sets
            // the uprights close enough that nothing can get between them, and
            // the shipped window's 0.223 m is that pitch. ModBuild 146 widened
            // the opening from 1.11 m to 1.43 m; keeping four bars would have
            // opened the gaps to 0.286 m, i.e. the bars would have been
            // rearranged by the window instead of added to. So the COUNT follows
            // the width at a fixed pitch and the pitch is what stays put.
            //
            // 5 bars over 1.4318 m is 0.2386 m of pitch — the nearest whole
            // number of bars to the 0.223 that was tuned, and the bar shadow the
            // beam draws reads _BarX0/_BarPitch straight off these same two
            // lines, so the striation in the shaft cannot disagree with the iron
            // that casts it.
            int barCount = Mathf.Max(1, Mathf.RoundToInt((wx1 - wx0) / 0.2386f) - 1);
            float barGap = (wx1 - wx0) / (barCount + 1);    // n+1 slots of light, n bars
            var bars = new Acc();
            var barProfile = new[] { new Vector2(0.017f, 0f), new Vector2(0.019f, 0.35f), new Vector2(0.017f, 1f) };
            var barUnit = LatheMesh(barProfile, 6);
            for (int i = 0; i < barCount; i++)
                MergeInto(bars, barUnit,
                    new Vector3(wx0 + barGap * (i + 1), wy0 - 0.03f, barZ),
                    Quaternion.Euler(0f, 22f * i, 0f),   // hand-forged: none of them square
                    new Vector3(1f, (wy1 - wy0) + 0.06f, 1f), Color.white);
            var barMesh = SaveMesh("Env_C_Bars.asset", bars.Build("Env_C_Bars"));
            var barMat = NewRoomMat("C_Bars.mat", "GloomhavenVR/EnvRoom");
            barMat.SetColor("_Tint", new Color(0.14f, 0.13f, 0.12f));
            var barsGo = Place(root, "WindowBars", barMesh, Vector3.zero, Vector3.zero, Vector3.one, barMat);
            Defer(barMat, barsGo.transform, 1f);
            UnityEngine.Object.DestroyImmediate(barUnit);

            // ---- moonlight: ONE soft volume, not five slats ----
            // USER FINDING, ModBuild 135 (hardware): "die Mondstraheln sind
            // wirklich 5 Strahlen (sehen aus wie Laser) durch das Fenster.
            // Stattdessen soll es ein realistisches Licht sein was durch das
            // Fenster leicht hereinkommt vom Mond."
            //
            // ModBuild 135 built the beam out of five EnvShaft slats, one per
            // gap between the bars, so the bar shadows would be free geometry.
            // That is exactly why it read as five lasers: a slat is a flat
            // blade, a blade has an OUTLINE, and five outlines side by side in a
            // black room are five objects, not light. Dimming cannot fix an
            // outline.
            //
            // It is now a single analytic volume (EnvBeam.shader — read its
            // header for the density model). The mesh below is a bounding HULL
            // that is never seen: the shader integrates a smooth gaussian
            // density along each view ray, so the hull's own rim sits where the
            // density is already ~2%. Consequences that matter:
            //   * it has no faces and no silhouette at ANY angle, including the
            //     grazing ones where the old slabs betrayed themselves;
            //   * it brightens when you look along it and dims broadside, which
            //     is what air full of dust does and what a blade cannot do;
            //   * the bar shadows survive only as SOFT STRIPING that is gone
            //     within ~1.5 m (the user asked for restraint), computed from
            //     the real bar pitch traced back to the window plane;
            //   * the bright thing is now the POOL on the flagstones and the
            //     sill it grazes, not the beam. _Decay 0.95 takes the volume to
            //     40% by 1 m and 6% by the floor: light "leicht hereinkommend".
            // Knobs, in order of effect: _Tint.a (strength), _Decay (how fast it
            // dissolves), _W0/_WK (thickness and spread), _BarDepth, poolA.
            {
                var dir = -MoonDir.normalized;                       // light travels this way
                var across = Vector3.Cross(Vector3.up, new Vector3(MoonDir.x, 0f, MoonDir.z).normalized).normalized;
                Vector3 hit = MoonBeamHit();
                float beamLen = Vector3.Distance(winMid, hit);

                // ---- the hull, and the two traps in building it ----
                // It is drawn BACK-FACE ONLY, so any part of it that ends up
                // behind the north wall or under the flagstones fails the depth
                // test and takes its pixels' beam with it — a hard-edged bite
                // out of the light. It must therefore be clipped INTO the room.
                //
                // TRAP 1: clipping by moving vertices along the world axes (the
                // obvious clamp) pulls them TOWARD the beam axis, because the
                // beam runs at 54 deg to the wall's normal. The hull then stops
                // enclosing the density it is supposed to bound and cuts the
                // beam anyway. Clipping SLIDES each vertex along the beam
                // direction instead: that changes only how far down the beam the
                // vertex sits, never its distance from the axis.
                //
                // TRAP 2: sliding is only safe if the required radius does not
                // grow with distance, or a vertex slid forward lands inside the
                // envelope it was meant to enclose. Hence the hull is a
                // CYLINDER at the widest radius the beam ever needs, not a cone.
                // It costs a bigger screen footprint and nothing else — the hull
                // has no appearance of its own.
                // WK is deliberately SMALL. The moon is collimated (0.5 deg), so
                // a real shaft through a 1.1 m window is very nearly a prism —
                // but a perfect prism is what reads as a manufactured object, so
                // it widens by 4.5 cm per metre: 0.30 -> 0.48 m over the whole
                // fall, which is a suggestion of divergence and no more.
                // HULL 2.85 is not decoration: it is where the super-gaussian
                // cross-section reaches 1e-6 of its peak. At the 2.05 the first
                // pass used, the density at the hull's own rim was still 1.5%
                // of peak and the bounding mesh's silhouette was PLAINLY VISIBLE
                // as a hard arc — the exact failure this construction exists to
                // avoid, just moved from the blades to the hull.
                const float W0 = 0.24f, WK = 0.045f, HULL = 2.85f;
                float hullR = HULL * (W0 + WK * beamLen);
                Vector3 SlideIntoRoom(Vector3 p)
                {
                    for (int pass = 0; pass < 3; pass++)
                    {
                        float lo = 0f, hi = float.MaxValue;   // t along `dir`
                        void Need(float bound, float cur, float slope, bool upper)
                        {
                            if (Mathf.Abs(slope) < 1e-5f) return;
                            float t = (bound - cur) / slope;
                            bool violated = upper ? cur > bound : cur < bound;
                            if (!violated) return;
                            if (t > 0f) lo = Mathf.Max(lo, t); else hi = Mathf.Min(hi, t);
                        }
                        Need(hd - 0.035f, p.z, dir.z, true);                       // N wall
                        Need(-hd + 0.03f, p.z, dir.z, false);                      // S wall
                        Need(hw - 0.03f, p.x, dir.x, true);                        // E wall
                        Need(-hw + 0.03f, p.x, dir.x, false);                      // W wall
                        Need(CH - 0.03f, p.y, dir.y, true);                        // ceiling
                        Need(CellarFloorY(p.x, p.z) + 0.035f, p.y, dir.y, false);  // floor
                        float t2 = lo > 0f ? lo : (hi < 0f ? hi : 0f);
                        if (Mathf.Abs(t2) < 1e-5f) break;
                        p += dir * t2;
                    }
                    return p;
                }
                var hullMesh = SaveMesh("Env_C_MoonShaft.asset",
                    BeamHullMesh(winMid, dir, 0f, beamLen, hullR, hullR, 10, 24, SlideIntoRoom));

                var beamMat = NewRoomMat("C_MoonShaft.mat", "GloomhavenVR/EnvBeam");
                // COLD, the opposite temperature to the candles — the two light
                // sources in this room must never be mistaken for each other.
                // Strength 0.30 looks large next to the old slats' 0.105 only
                // because it is now divided by the path term and squashed by the
                // Reinhard knee; the peak on screen is LOWER than 135's.
                // ModBuild 137: the shader now INTEGRATES the density along the
                // view ray instead of sampling it once at the closest approach
                // (see EnvBeam.shader's 137 note). Broadside that integral is
                // sqrt(pi)-ish times the old point sample — 1.78 * w for this
                // super-gaussian — so the strength drops 0.055 -> 0.031 to land
                // the beam at the SAME broadside brightness the 136 previews
                // were judged at. What changes is only what the old model got
                // wrong: looking up the shaft is now ~5x broadside instead of
                // zero, and standing inside it counts only the half in front of
                // the head.
                beamMat.SetColor("_Tint", new Color(0.55f, 0.68f, 1.0f, 0.031f));
                beamMat.SetVector("_BeamOrg", winMid);
                beamMat.SetVector("_BeamDir", dir);
                beamMat.SetFloat("_Len", beamLen);
                beamMat.SetFloat("_W0", W0);
                beamMat.SetFloat("_WK", WK);
                beamMat.SetFloat("_RadPow", 1.35f);
                // DERIVED, not copied. This used to be the literal 0.34, and so
                // did _BarDepth below — two hand-made duplicates of RevealDepth
                // that would both have silently stayed at a 0.34 m wall the
                // moment the wall got thicker, so the beam would have emerged
                // from an embrasure 21 cm shallower than the one you can see.
                beamMat.SetFloat("_Ramp", RevealDepth);   // it emerges from the embrasure
                beamMat.SetFloat("_Decay", 1.30f);     // 26% left at 1 m, 7% at 2 m: "leicht hereinkommend"
                beamMat.SetFloat("_EndFade", 0.55f);
                // The integration interval IS the hull: one number, used twice,
                // so the density can never end inside its own bounding mesh.
                beamMat.SetFloat("_HullR", hullR);
                beamMat.SetFloat("_Steps", 24f);
                beamMat.SetFloat("_Knee", 1.10f);
                // THE ONLY LIT AIR IN THE ROOM, and therefore the only place the
                // draught can actually be SEEN. USER, ModBuild 146: "Der Wind im
                // Keller sieht eher aus wie eine Klimaanlage statt wind das
                // reinpustet." Half of the answer to that was to stop drawing the
                // wind as a machine (AddElementFX / AddCellarDraught); the other
                // half is to draw it where the light is. A particle in unlit air
                // is a bright dot with no reason to be bright — the file's own
                // note on the draught's 0.18 alpha says exactly that — whereas a
                // ripple in the shaft is a shadow of moving dust, which is what a
                // draught through a moonbeam really looks like and costs nothing.
                // 0.14 -> 0.22 and 0.13 -> 0.17: the striation is now the loudest
                // statement in the room that the air is moving, and the motes are
                // back to being the hint they were always meant to be.
                beamMat.SetFloat("_Shimmer", 0.22f);
                beamMat.SetFloat("_ShimmerSpeed", 0.17f);
                // ELEMENT ART — AIR. The shaft is the only LIT air in the cellar,
                // so it is where a draught coming in at the window can actually be
                // seen; under Air its mottling travels along the room's own
                // draught instead of merely shimmering faster (EnvBeam's element
                // block). Zero-length would be a hard off; this is the same vector
                // the flames lean along and the motes drift along.
                beamMat.SetVector("_DraftDir", DraftDir);
                // the bars' true shadow: pitch and first bar taken from the SAME
                // numbers the bars were built from, traced back to the wall plane
                beamMat.SetFloat("_WinZ", hd);
                beamMat.SetFloat("_BarX0", wx0 + barGap);
                beamMat.SetFloat("_BarPitch", barGap);
                beamMat.SetFloat("_BarDepth", RevealDepth);   // derived; see _Ramp above
                beamMat.SetFloat("_BarSig", 0.030f);
                beamMat.SetFloat("_BarBlur", 0.095f);
                beamMat.SetFloat("_BarFade", 0.85f);
                // HAUNT — the beam DIMS while something is leaning in at the
                // window. The two shaders never talk: both evaluate the same slot
                // schedule off the same shared clock, so the dim and the silhouette
                // are one event by construction. See EnvBeam's _HauntDepth block.
                beamMat.SetFloat("_HauntDepth", HauntBeamDepth);
                beamMat.SetFloat("_HauntPeriod", HauntPeriod);
                beamMat.SetFloat("_HauntCards", HauntCellarCards);
                beamMat.SetFloat("_HauntWatch", HauntCardWindow);
                beamMat.SetVector("_HauntEnv", HauntWindowEnv);
                Place(root, "MoonShaft", hullMesh, Vector3.zero, Vector3.zero, Vector3.one, beamMat);

                // ---- the pool, which is now the bright end of this ----
                // The footprint of the beam landing at the moon's altitude:
                // 2w across, 2w/sin(alt) along, so the ellipse is derived, not
                // drawn. But the important part is WHAT is in it.
                //
                // The first pass put a soft glow SPRITE there and it read as a
                // luminous blue disc hovering over a black floor — light with
                // nothing under it. A pool of moonlight is not a glow, it is
                // FLAGSTONES YOU CAN SUDDENLY SEE. So the pool is a patch of the
                // floor's own albedo, at the floor's own UVs, added back over
                // itself through a soft elliptical mask: the mortar lines, the
                // chips and the tool marks all come up cold inside the ellipse
                // and vanish outside it. (EnvParticleAdd multiplies by alpha AND
                // blends by it, so the authored mask is effectively squared —
                // hence the linear 1-f^2 mask, which lands as a smooth
                // zero-derivative falloff.)
                Vector3 alongDir = new Vector3(dir.x, 0f, dir.z).normalized;
                float wEnd = W0 + WK * beamLen;
                float sinAlt = Mathf.Max(MoonDir.normalized.y, 0.2f);
                float poolAcross = wEnd * 1.60f, poolAlong = wEnd * 1.60f / sinAlt;
                var poolMesh = SaveMesh("Env_C_MoonPool.asset",
                    MoonPoolMesh(hit, alongDir, across, poolAlong, poolAcross, 7, 26, 2.6f));
                var poolMat = NewRoomMat("C_MoonPool.mat", "GloomhavenVR/EnvParticleAdd");
                poolMat.SetTexture("_MainTex", Imp("monastery_stone_floor_alb"));
                poolMat.SetColor("_Tint", new Color(0.34f, 0.44f, 0.68f, 0.85f));
                Place(root, "MoonPool", poolMesh, Vector3.zero, Vector3.zero, Vector3.one, poolMat);

                // ...and a small, much fainter air-glow just above it, which is
                // the scattering the beam does in the last few centimetres. It
                // is the only part of the pool that is a sprite, and it is
                // deliberately smaller than the lit stone so it reads as a
                // brightening of the pool and never as a lamp on the floor.
                var haloMesh = new Acc();
                {
                    var c = hit; c.y = CellarFloorY(hit.x, hit.z) + 0.035f;
                    AddQuad(haloMesh, c, alongDir * (poolAlong * 0.80f),
                            across * (poolAcross * 0.80f), Color.white);
                }
                var haloMat = NewRoomMat("C_MoonPoolAir.mat", "GloomhavenVR/EnvParticleAdd");
                haloMat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Env_Glow.png"));
                haloMat.SetColor("_Tint", new Color(0.40f, 0.52f, 0.84f, 0.16f));
                Place(root, "MoonPoolAir", SaveMesh("Env_C_MoonPoolAir.asset", haloMesh.Build("Env_C_MoonPoolAir")),
                      Vector3.zero, Vector3.zero, Vector3.one, haloMat);

                // ---- and the sill it grazes ----
                // Which reveal faces the moon can see is not a choice: a face is
                // lit iff its normal opposes the light. With the moon bearing
                // down-and-west that is the SILL (+y) and the WEST jamb (+x) and
                // nothing else, and the baked rig already shades exactly those
                // two — so the grazing highlight is bought by RAISING THE
                // REVEAL'S ALBEDO (see the reveal material above), not by adding
                // geometry.
                //
                // REJECTED, and worth recording: a pair of additive glow quads
                // laid on the sill and the west jamb. They looked right head-on
                // and became a one-pixel-wide BRIGHT LINE the moment the view
                // dropped into their plane — the very "laser" failure this round
                // exists to remove, reintroduced 30 cm from the window. Any flat
                // additive card near a surface the player can get level with has
                // this problem; the fix is always to light the surface instead.

                Debug.Log($"[GloomhavenVR][Env] Moonlight: one analytic volume, axis {beamLen:F2} m, "
                          + $"w {W0:F2}->{wEnd:F2} m, lands at ({hit.x:F2},{hit.z:F2}) = "
                          + $"{new Vector2(hit.x, hit.z).magnitude:F2} m from centre "
                          + $"(PlaySpace radius {CellarPlaySpaceDia * 0.5f:F2} m); "
                          + $"bar pitch {barGap:F3} m from x {wx0 + barGap:F3}.");

                // the aperture itself glows cold, so the window reads as the
                // source and not as a hole with something bright behind it
                var winGlowMat = NewRoomMat("C_GlowMoon.mat", "GloomhavenVR/EnvGlow");
                winGlowMat.SetColor("_Tint", new Color(0.40f, 0.54f, 0.88f, 0.34f));
                // NOT a candle either, and this is the one that would have been
                // wrong to freeze: the aperture's glow IS moonlight, and this
                // whole round is about Light lifting the moon indoors rather than
                // the room. It must answer Light. (Which is exactly why the flag
                // is per-material and not GhvrIndoor() — all three of EnvGlow's
                // clients are indoors and they want three different answers.)
                winGlowMat.SetFloat("_ElemCandle", 0f);
                winGlowMat.SetFloat("_Falloff", 2.0f);
                // ...and it is the APERTURE'S OWN SIZE, not a pair of numbers that
                // happened to fit the old one. (0.52, 0.34) was 0.467 and 0.541
                // of the shipped opening's width and height; both factors are
                // kept exactly, so the glow is the same ellipsoid relative to the
                // hole it belongs to and the widened window does not end up with
                // a halo three quarters of its size. barGap already followed the
                // opening; this did not, and that asymmetry is the bug.
                var glowSphere = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
                if (glowSphere != null)
                    Place(root, "WindowGlow", glowSphere, winMid + new Vector3(0, 0, -0.06f),
                          Vector3.zero,
                          new Vector3((wx1 - wx0) * 0.467f, (wy1 - wy0) * 0.541f, 0.22f), winGlowMat);
            }

            // ---- stair alcove behind W doorway: steps up into darkness ----
            var stepMesh = SaveMesh("Env_C_Step.asset", BoxMesh(1.5f, 0.19f, 0.34f, 1.9f));
            for (int i = 0; i < 6; i++)
            {
                var s = Place(root, "Step" + i, stepMesh,
                    new Vector3(-hw - 0.17f - 0.30f * i, 0.19f * i, -hd + StairHole.xMin + StairHole.width / 2f),
                    new Vector3(0, 90, 0), Vector3.one, null);
                // the steps darken as they climb: by the top one they are barely
                // there (was 0.9 -> 0.4; the far end of the alcove has to be
                // unreadable, user finding ModBuild 133)
                s.GetComponent<MeshRenderer>().sharedMaterial =
                    SurfMat($"C_Step{i}.mat", "monastery_stone_floor", 1.9f, s.transform, 1.0f, Mathf.Lerp(0.85f, 0.12f, i / 5f));
            }
            // alcove shaft (walls + ceiling + pitch-black end cap)
            var shaftMesh = SaveMesh("Env_C_Shaft.asset", BuildShaft(2.2f, 2.6f, StairHole.width));
            var shaftGo = Place(root, "StairShaft", shaftMesh,
                new Vector3(-hw, 0, -hd + StairHole.xMin), Vector3.zero, Vector3.one, null);
            shaftGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Shaft.mat", "medieval_blocks_05", 3.4f, shaftGo.transform, 1.0f, 0.28f);
            var capMat = NewRoomMat("C_ShaftCap.mat", "GloomhavenVR/EnvRoom");
            capMat.SetColor("_Tint", Color.black);
            var capMesh = SaveMesh("Env_C_ShaftCap.asset", BoxMesh(StairHole.width, 2.6f, 0.05f, 1f));
            Place(root, "ShaftCap", capMesh,
                new Vector3(-hw - 2.15f, 0.6f, -hd + StairHole.xMin + StairHole.width / 2f),
                new Vector3(0, 90, 0), Vector3.one, capMat);

            // ---- props (crates were in the stair doorway in iteration 1 —
            // moved to the S wall). Every prop names the thing it stands ON;
            // `Rest` sits it there vertex-exactly and errors on overhang. ----
            Prop(root, "Barrel0", "wine_barrel_01", "wine_barrel_01", new Vector3(-3.7f, 0, -3.2f), 15, 1f, "C");
            Prop(root, "Barrel1", "wine_barrel_01", "wine_barrel_01", new Vector3(-4.25f, 0, -2.0f), 152, 1f, "C");
            Prop(root, "Barrel2", "wine_barrel_01", "wine_barrel_01", new Vector3(-2.85f, 0, -3.95f), 80, 0.92f, "C",
                euler3: new Vector3(0, 80, 90)); // on its side
            var crate0 = Prop(root, "Crate0", "wooden_crate_01", "wooden_crate_01", new Vector3(-1.55f, 0, -3.95f), 8, 1f, "C");
            var crate1 = Prop(root, "Crate1", "wooden_crate_01", "wooden_crate_01", new Vector3(-0.45f, 0, -4.05f), -12, 0.9f, "C");
            var crate2 = Prop(root, "Crate2", "wooden_crate_01", "wooden_crate_01",
                new Vector3(-1.52f, 0, -3.93f), 16, 0.78f, "C", sink: 0.004f, support: crate0);
            var table = Prop(root, "Table", "small_wooden_table_01", "small_wooden_table_01", new Vector3(3.6f, 0, 3.15f), -28, 1.1f, "C");
            // THE STOOL. USER, cellar 12: "Der kleine Hocker neben dem
            // Bücherregal von dem kleinen Tisch ist viel zu klein, als ob es ein
            // Hocker für eine Maus ist. Nicht immersiv."
            //
            // He is right and the number proves it: the Poly Haven scan is
            // authored at its own arbitrary scale and was placed at 1.0, which
            // put its seat well under the table's own apron. A stool is one of
            // the very few props in a room whose size everybody knows by heart,
            // because everybody has sat on one — so it is scaled to a MEASURED
            // seat height rather than to a number that looked right in the
            // editor. 0.45 m is the standard for a seat you sit on with your feet
            // on the floor (kitchen chairs are 0.44-0.47); the table beside it is
            // measured too, and the bake log prints both so the pair can be
            // checked without opening Unity.
            const float StoolSeatH = 0.45f;
            float stoolRaw = ImpMesh("wooden_stool_02").bounds.size.y;
            float stoolScale = StoolSeatH / Mathf.Max(stoolRaw, 1e-4f);
            // AND IT HAD TO MOVE. At its old (2.55, 2.30) the stool's axis was
            // 3.44 m from the room centre and it was the closest prop in the
            // room; scaling it up pushes its near edge inside the 3.25 m
            // PlaySpace radius, which AssertPlaySpaceClear would (correctly) fail
            // the build over. 25 cm further into the corner keeps it beside the
            // table, which is where the user is looking at it from.
            var stool = Prop(root, "Stool", "wooden_stool_02", "wooden_stool_02",
                             new Vector3(2.82f, 0, 2.62f), 40, stoolScale, "C");
            var shelf = BuildTippingShelf(root, CellarShelfAt, CellarShelfYaw);
            Prop(root, "Bucket", "wooden_bucket_01", "wooden_bucket_01", new Vector3(-4.0f, 0, -4.05f), 0, 1f, "C");
            Prop(root, "Jug0", "jug_01", "jug_01", new Vector3(3.52f, 0, 3.30f), 65, 1f, "C",
                sink: 0.001f, support: table);
            Prop(root, "Jug1", "jug_01", "jug_01", new Vector3(-0.45f, 0, -4.05f), 10, 0.9f, "C",
                sink: 0.001f, support: crate1);
            float tableTop = SurfaceYAt(table, 3.72f, 2.95f);
            {
                var sb = new Bounds(); bool first = true;
                foreach (var v in WorldVerts(stool))
                { if (first) { sb = new Bounds(v, Vector3.zero); first = false; } else sb.Encapsulate(v); }
                Debug.Log($"[GloomhavenVR][Env] Cellar STOOL rescaled (user: \"als ob es ein Hocker für "
                          + $"eine Maus ist\"): source mesh {stoolRaw:F3} m tall, placed at scale "
                          + $"{stoolScale:F2} (was 1.00), so the seat now sits at {sb.max.y:F3} m and the "
                          + $"stool is {sb.size.x:F2} x {sb.size.z:F2} m on the floor. The table top it "
                          + $"stands beside is at {tableTop:F3} m, i.e. the seat is "
                          + $"{100f * sb.max.y / Mathf.Max(tableTop, 1e-3f):F0} % of the table's height — a "
                          + "real stool and a real table are 0.45 and 0.75, i.e. 60 %.");
            }
            float crateTop = SurfaceYAt(crate2, -1.55f, -3.95f);
            var shelfSpot = OnShelf(-0.14f, 0f);       // where the candle stands on the top board
            float shelfTop = SurfaceYAt(shelf, shelfSpot.x, shelfSpot.z);

            // ---- candles: lathe wax + flame cards + warm glow, ON the props ----
            // Each GROUP drives one light slot, so everything that belongs to it —
            // its flames, its halo — is built with that slot's phase AND rate.
            // Before this round every flame in the room shared one material with
            // _Phase 0 while the three light slots ran on phases 0/2.1/4.4: the
            // flame you were looking at and the light it cast were two unrelated
            // animations, which is most of why the flicker read as "only the
            // flame cards move".
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            // SHELF RIDERS — `onShelf` is the whole of the ModBuild 144 bug
            // ("Die Kerzen und das Feuer, die auf dem Bücherregal stehen, kippen
            // nicht mit"). A candle group is FOUR things drawn by three shaders —
            // welded wax (EnvRoom), a flame card per candle (EnvFlame), a halo
            // (EnvGlow), and the baked light slot it drives — and every one of
            // them has to take the shelf's rotation. They take it from the one
            // record BuildTippingShelf published; nothing here knows a hinge.
            void CandleGroup(string n, int slot, Vector3 basePos,
                (float h, float dx, float dz)[] candles, float glowR, float glowA,
                bool onShelf = false)
            {
                // one welded mesh per group: a shared material across several
                // transforms would light all of them from the first one's spot
                var wax = NewRoomMat($"C_Wax{n}.mat", "GloomhavenVR/EnvRoom");
                wax.SetColor("_Tint", new Color(0.94f, 0.86f, 0.70f));
                var acc = new Acc();
                int ci = 0;
                foreach (var c in candles)
                {
                    var unit = CandleMesh(c.h, 0.016f, 400 + ci * 17);
                    MergeInto(acc, unit, basePos + new Vector3(c.dx, 0, c.dz),
                              Quaternion.identity, Vector3.one, Color.white);
                    UnityEngine.Object.DestroyImmediate(unit);

                    // the flame: its own material so it can carry its own phase
                    var fm = SaveMesh("Env_Flame.asset", CrossQuadMesh(0.045f, 0.085f));
                    var flame = NewRoomMat($"C_Flame{n}{ci}.mat", "GloomhavenVR/EnvFlame");
                    flame.SetTexture("_MainTex", Imp("candle_flame_alb"));
                    flame.SetColor("_Tint", new Color(1f, 0.82f, 0.55f, 1f));
                    flame.SetFloat("_Sway", 0.045f);
                    flame.SetFloat("_Flicker", 0.75f);
                    flame.SetFloat("_Phase", SlotPhase[slot] + 0.63f * ci);
                    flame.SetFloat("_Rate", SlotRate[slot]);
                    flame.SetFloat("_Gust", 0.055f);
                    flame.SetVector("_GustDir", DraftDir);
                    // ...and how hard AIR works this particular flame, from its
                    // own distance to the window (AirGustAt). The authored lean
                    // is unchanged — only the element's multiplier has a gradient
                    // now — so the room with nothing up is the room that was
                    // tuned. See the ModBuild 142 note on EnvFlame/_AirGust.
                    flame.SetFloat("_AirGust", AirGustAt(basePos + new Vector3(c.dx, c.h, c.dz)));
                    var flameGo = Place(root, $"Flame{n}{ci}", fm,
                          basePos + new Vector3(c.dx, c.h + 0.002f, c.dz),
                          Vector3.zero, Vector3.one, flame);
                    if (onShelf)
                    {
                        // it rides, it may be blown out, and it REFUSES 0.80 of
                        // the rotation: a flame goes up whatever the wax under it
                        // is doing, so the card is rotated rigidly (which welds
                        // the base to the wick) and then bent back about its own
                        // origin. 0.80 leaves the plume within twenty degrees of
                        // vertical at the shelf's full 88.
                        RideShelf(flame, self: 1f, lit: -1f, gutter: 1f, stiff: 0.80f);
                        WriteShelfTip(flame, flameGo.transform);
                    }
                    ci++;
                }
                var waxMesh = SaveMesh($"Env_C_Wax{n}.asset", acc.Build($"Env_C_Wax{n}"));
                var waxGo = Place(root, $"Candles{n}", waxMesh, Vector3.zero, Vector3.zero, Vector3.one, wax);
                // The wax is welded in ROOM space under the identity transform,
                // so its object space IS room space and the hinge arrives
                // unchanged — but it still goes through the one writer, because
                // "it happens to be the identity here" is not a thing to rely on.
                if (onShelf) RideShelf(wax, self: 1f, lit: slot, gutter: 0f, stiff: 0f);
                Defer(wax, waxGo.transform, 1f);

                if (glowMesh != null)
                {
                    // the halo breathes WITH the candle — it used to be a static
                    // sphere sitting inside a flickering pool of light
                    var g = NewRoomMat($"C_Glow{n}.mat", "GloomhavenVR/EnvGlow");
                    g.SetColor("_Tint", new Color(1f, 0.55f, 0.20f, glowA));
                    g.SetFloat("_Falloff", 2.2f);
                    // THE CANDLES ARE UNTOUCHABLE. User, twice: "anstatt die
                    // Kerzenscheine, die sollte identisch beiben" and "Auch bei
                    // Dunkelheit sollte es keinen Einfluss auf den Kerzenschein
                    // haben." The source GAIN was already the identity indoors
                    // (EnvElement's "AND THE CANDLES ARE UNTOUCHABLE INDOORS"),
                    // but EnvGlow's EDGE was not: Dark tightened this halo and
                    // Light opened it. This is the flag that closes it, and these
                    // three halos are the only materials in either room that
                    // carry it.
                    g.SetFloat("_ElemCandle", 1f);
                    g.SetFloat("_Flicker", 0.95f);
                    g.SetFloat("_Rate", SlotRate[slot]);
                    g.SetFloat("_Phase", SlotPhase[slot]);
                    var glowGo = Place(root, $"CandleGlow{n}", glowMesh,
                        basePos + new Vector3(candles[0].dx, candles[0].h + 0.05f, candles[0].dz),
                        Vector3.zero, Vector3.one * glowR, g);
                    if (onShelf)
                    {
                        // a halo IS the flame's light, so it travels with the
                        // flame AND dies with it — a pool of glow left hanging
                        // where a candle used to be is exactly the bug in a
                        // different shader.
                        RideShelf(g, self: 1f, lit: -1f, gutter: 1f, stiff: 0f);
                        WriteShelfTip(g, glowGo.transform);
                    }
                }
            }
            var candleTable = new Vector3(3.72f, tableTop, 2.95f);
            var candleShelf = new Vector3(shelfSpot.x, shelfTop, shelfSpot.z);
            var candleCrate = new Vector3(-1.55f, crateTop, -3.95f);
            CandleGroup("Table", 0, candleTable,
                new[] { (0.16f, 0f, 0f), (0.11f, 0.07f, 0.04f), (0.085f, -0.05f, 0.06f) }, 0.30f, 0.60f);
            // ...and THIS one is standing on the bookshelf that topples.
            CandleGroup("Shelf", 1, candleShelf, new[] { (0.12f, 0f, 0f) }, 0.24f, 0.50f,
                        onShelf: true);
            CandleGroup("Crate", 2, candleCrate, new[] { (0.14f, 0f, 0f), (0.09f, 0.06f, -0.05f) }, 0.27f, 0.55f);

            // rig fixup: light sources sit just above the tallest flame of each group
            rig.points[0].pos = candleTable + new Vector3(0, 0.22f, 0);
            rig.points[1].pos = candleShelf + new Vector3(0, 0.18f, 0);
            rig.points[2].pos = candleCrate + new Vector3(0, 0.20f, 0);

            BuildCellarAtmosphere(root, rig);
            // ELEMENT ART — the gated emitters (embers, the strengthened draught,
            // grit off the planks). See AddElementFX for why they hang here and
            // not on the shell root.
            AddElementFX(root, cellar: true);
            // REAL FIRE — the parts of the cellar that catch while Fire is up,
            // and the draught's own mouth at the window (user, ModBuild 142).
            // Both AFTER the props and the candles: every fire is seated on the
            // real surface it stands on, and the draught's mouth is derived from
            // the window opening that was really cut.
            AddCellarFire(root, rig, crateTop, shelfTop);
            AddCellarDraught(root);

            // ================================================ SURFACE GROWTH ==
            // The moss that is not there yet: cushions along the foot of every
            // wall run, folded flat until Earth brings them up. See the SURFACE
            // GROWTH section near the bottom of this file for the mechanism, and
            // for why every card is plumb.
            //
            // It is built HERE, before PaintContactAO, because that call clears
            // `Contacts` — the footprint list the growth asks "does a barrel
            // already stand here".
            {
                const float span = 0.30f;                  // the tallest card in this mesh
                var acc = new Acc();
                // (origin, along, length, inward) for the four runs, taken from
                // the Wall() calls above so the two can never disagree.
                var runs = new[]
                {
                    (o: new Vector3(-hw, 0, hd), a: Vector3.right, len: CW, inw: Vector3.back),
                    (o: new Vector3(hw, 0, -hd), a: Vector3.left, len: CW, inw: Vector3.forward),
                    (o: new Vector3(hw, 0, hd), a: Vector3.back, len: CD, inw: Vector3.left),
                    (o: new Vector3(-hw, 0, -hd), a: Vector3.forward, len: CD, inw: Vector3.right),
                };
                // THE RAT'S ROUTE IS NOT NEGOTIABLE. It runs the wall foot at
                // both hole mouths, and a moss cushion standing in it would have
                // the rat pass through a bush twice a minute. Its four waypoints
                // are the authored path (see the rat block), so the growth simply
                // keeps half a metre off the polyline.
                var rat = new[] { RatW0, RatW1, RatW2, RatW3 };
                bool NearRat(Vector3 p)
                {
                    for (int k = 0; k + 1 < rat.Length; k++)
                    {
                        Vector3 a = rat[k], b = rat[k + 1];
                        Vector3 ab = b - a; ab.y = 0f;
                        Vector3 ap = p - a; ap.y = 0f;
                        float u = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        if ((ap - ab * u).magnitude < 0.50f) return true;
                    }
                    return false;
                }
                int placed = 0, blocked = 0;
                for (int r = 0; r < runs.Length; r++)
                {
                    int n = Mathf.RoundToInt(runs[r].len / 0.40f);
                    for (int i = 0; i < n; i++)
                    {
                        int sd = 8300 + r * 131 + i;
                        float u = (i + 0.15f + 0.70f * Hash3(sd, 0, 0, 8311)) / n * runs[r].len;
                        float d = 0.10f + 0.28f * Hash3(sd, 1, 0, 8311);
                        var p = runs[r].o + runs[r].a * u + runs[r].inw * d;
                        // patchy, never a skirting board of moss all round the room
                        if (Hash3(sd, 2, 0, 8311) > 0.30f + 0.70f * Fbm2(p.x * 0.42f, p.z * 0.42f, 2, 8317))
                            continue;
                        if (NearRat(p) || GrowthBlocked(p.x, p.z, 0.10f)) { blocked++; continue; }
                        p.y = CellarFloorY(p.x, p.z);
                        float h = 0.11f + 0.18f * Hash3(sd, 3, 0, 8311);
                        AddGrowthClump(acc, p, h, 4, MossCards, sd, span, Grey(1f));
                        placed++;
                    }
                }

                // ---- MOSS REAL: the cushions that go UP THE WALL ------------
                // USER VERDICT, ModBuild 143 (cellar): "Bei Erde ist ähnlich wie
                // im Wald einfach grüne Flecken statt wirklich 'Moos' und
                // Bewachsung, nicht sehr glaubwürdig und immersiv."
                //
                // "Bewachsung" is the word that cannot be answered in a shader.
                // EnvGrowth's MOSS REAL block does everything a fragment can do
                // — its own micro-relief, its own normal, two greens, a lip with
                // an occlusion crease — and every one of those is still a
                // property of a FLAT wall. What a cushion has that none of them
                // gives it is a SILHOUETTE: an outline that stands off the stone
                // and breaks the wall's own straight edge.
                //
                // So the moss that grows in the mortar courses now grows OUT of
                // them, on the same folding cards the floor already uses: plumb,
                // 4-8 cm proud of the face, 8-24 cm tall, at 0.10-0.95 m — which
                // is exactly the band the shader's `foot` term (gone by 1.25 m)
                // puts the painted moss in, so the cards stand where the wall
                // behind them is greenest and the two read as one covering
                // rather than as a decal in front of a stain.
                //
                // THE WEST WALL IS SKIPPED, and that is not fastidiousness: it
                // carries the stair doorway (StairHole, 1.6 x 2.35 m), and a
                // plumb card placed on the face at 0.6 m inside that opening
                // would be a cushion of moss hanging in mid-air in a doorway.
                // Deriving the opening's u-range from the wall's own local frame
                // is possible and was rejected as three chances to be subtly
                // wrong for one wall's worth of moss.
                int onWall = 0;
                for (int r = 0; r < 3; r++)
                {
                    int n = Mathf.RoundToInt(runs[r].len / 0.34f);
                    for (int i = 0; i < n; i++)
                    {
                        int sd = 8600 + r * 149 + i;
                        float u = (i + 0.10f + 0.80f * Hash3(sd, 0, 0, 8611)) / n * runs[r].len;
                        // the same patch mask the floor cushions use, so the
                        // wall and the floor go green in the same places
                        var foot = runs[r].o + runs[r].a * u;
                        if (Hash3(sd, 1, 0, 8611) > 0.22f + 0.62f * Fbm2(foot.x * 0.42f, foot.z * 0.42f, 2, 8317))
                            continue;
                        // ...and the room's own footprint list, which is what
                        // keeps a cushion out of the back of the bookshelf and
                        // out of the barrels standing against the wall. A wall
                        // card is 8-24 cm tall at up to 0.95 m, so "there is a
                        // prop on this square metre" is exactly the right test
                        // even though the card is not on the floor.
                        if (NearRat(foot) || GrowthBlocked(foot.x, foot.z, 0.12f)) { blocked++; continue; }
                        // 4-8 cm proud of the face: enough that the card's own
                        // outline clears the stone at a grazing angle, little
                        // enough that it never reads as a leaf stuck on a wall.
                        float d = 0.04f + 0.04f * Hash3(sd, 2, 0, 8611);
                        // TWO OR THREE CARDS ON ONE SPOT, all parallel to the
                        // wall. NOT AddGrowthClump: its whole job is to fan the
                        // cards in yaw so a tuft holds up from every direction,
                        // and half of that fan would be inside the masonry.
                        int k = 2 + (Hash3(sd, 3, 0, 8611) < 0.45f ? 1 : 0);
                        for (int c = 0; c < k; c++)
                        {
                            float hh = 0.08f + 0.16f * Hash3(sd, 4 + c, 0, 8611);
                            float y0 = 0.10f + 0.85f * Hash3(sd, 7 + c, 0, 8611);
                            var rect = MossCards[(int)(Hash3(sd, 10 + c, 0, 8611) * MossCards.Length) % MossCards.Length];
                            float halfW = hh * 0.5f * (rect.width / Mathf.Max(rect.height, 1e-3f));
                            var at = foot + runs[r].a * ((Hash3(sd, 13 + c, 0, 8611) - 0.5f) * 0.22f)
                                          + runs[r].inw * (d + 0.012f * c);
                            at.y = CellarFloorY(at.x, at.z) + y0;
                            AddGrowthCard(acc, at, runs[r].a * halfW, hh, rect, Grey(1f), span);
                            onWall++;
                        }
                    }
                }
                var gMesh = SaveMesh("Env_C_Growth.asset", acc.Build("Env_C_Growth"));
                var gm = NewRoomMat("C_Growth.mat", "GloomhavenVR/EnvRoomCutout");
                gm.SetTexture("_MainTex", Imp("moss_01_alb"));
                gm.SetFloat("_Cutoff", 0.35f);
                gm.SetFloat("_VCol", 1f);
                // moss_01 is a daylight photoscan; in a cellar lit by three
                // candles it has to be as desaturated and as dark as the bark in
                // the wood was made in ModBuild 133, or the one green thing in
                // the room glows.
                gm.SetColor("_Tint", new Color(0.44f, 0.51f, 0.36f));
                gm.SetFloat("_ElemGrow", 1.0f);
                // ...and it frosts like everything else when Ice comes up. A
                // patch of moss with frost on it is the one place in this room
                // where two elements are visibly on the same square centimetre.
                gm.SetFloat("_ElemFrost", 0.9f);
                // amplitude 0 = no wind (a cellar has a draught, not a breeze;
                // the draught is on the candles and the cobwebs). w = 1 selects
                // vertex ALPHA as the fold weight, which is what _ElemGrow needs.
                gm.SetVector("_ElemWind", new Vector4(0f, 0f, 0f, 1f));
                gm.SetVector("_ElemWindDir", new Vector4(0f, 0f, 1f, span));
                var gGo = Place(root, "Growth", gMesh, Vector3.zero, Vector3.zero, Vector3.one, gm);
                Defer(gm, gGo.transform, 1f);
                // ---- THE FUNGI: SHELVES OUT OF THE WALL ---------------------
                // See the BRACKET FUNGI block for the whole argument (why the
                // cellar's growth cannot be moss at all, why the silhouette is
                // the thing a shading lane cannot supply, and why these are not
                // element-gated). This is only the placement.
                //
                // WHERE THEY GROW, and every line of it is the fungus's own
                // biology rather than composition:
                //   * 0.20-1.70 m, i.e. HIGHER than the moss cushions' 0.10-0.95.
                //     Moss is a plant and sits at the damp foot; a polypore
                //     fruits where its mycelium has eaten far enough up into the
                //     mortar and the wall plate, which in a cellar is chest
                //     height and above.
                //   * NORTH, SOUTH AND EAST only, the same three runs the wall
                //     cushions use and for the same reason: the west wall carries
                //     the 1.6 x 2.35 m stair doorway and a bracket at 0.6 m
                //     inside that opening is a fungus growing on air.
                //   * NOT in the rat's route and NOT where a prop stands, from
                //     the room's own two answers (NearRat, GrowthBlocked) — a rat
                //     running through a bracket twice a minute, or a shelf of
                //     fungus growing out of the back of a barrel.
                //   * IN THE DAMP HALF OF THE ROOM. The drip, the puddle and the
                //     window are all in the north-west, and that quarter is where
                //     the water is; the mask below leans the population that way
                //     instead of ringing the room evenly, because an even ring of
                //     fungus is decoration and a wet corner full of it is a
                //     cellar with a leak.
                var fung = new Acc();
                var fungAnchors = new List<(Vector3 at, Vector3 outw)>();
                int clumps = 0, caps = 0;
                for (int r = 0; r < 3; r++)
                {
                    int n = Mathf.RoundToInt(runs[r].len / 0.44f);
                    for (int i = 0; i < n; i++)
                    {
                        int sd = 8900 + r * 167 + i;
                        float u = (i + 0.12f + 0.76f * Hash3(sd, 0, 0, 8911)) / n * runs[r].len;
                        var foot = runs[r].o + runs[r].a * u;
                        // the damp lean: 1 at the puddle, ~0.25 at the far corner
                        float wet = Mathf.Clamp01(1.25f - Vector2.Distance(
                            new Vector2(foot.x, foot.z), new Vector2(PuddleAt.x, PuddleAt.z)) / 9.0f - 0.25f);
                        if (Hash3(sd, 1, 0, 8911) > 0.26f + 0.66f * wet) continue;
                        if (NearRat(foot) || GrowthBlocked(foot.x, foot.z, 0.16f)) { blocked++; continue; }
                        var at = foot + runs[r].inw * 0.012f;   // 12 mm proud, so nothing z-fights
                        at.y = CellarFloorY(at.x, at.z) + 0.20f + 1.50f * Hash3(sd, 2, 0, 8911);
                        int tiers = 1 + (Hash3(sd, 3, 0, 8911) < 0.62f ? 1 : 0)
                                      + (Hash3(sd, 4, 0, 8911) < 0.34f ? 1 : 0);
                        // 6-17 cm of reach. A polypore on masonry is a hand's
                        // width at most; the big ones live on timber.
                        float size = 0.060f + 0.110f * Hash3(sd, 5, 0, 8911);
                        AddFungusCluster(fung, at, runs[r].inw, runs[r].a, size, tiers, sd, Grey(1f));
                        fungAnchors.Add((at, runs[r].inw));
                        clumps++; caps += tiers;
                    }
                }
                if (fung.Count == 0)
                    throw new Exception("The cellar built no bracket fungi at all. The user has now "
                                        + "rejected this surface three times; a mask that happens to "
                                        + "refuse every candidate is not an acceptable outcome.");
                var fMesh = SaveMesh("Env_C_Fungus.asset", fung.Build("Env_C_Fungus"));
                AssertFungusFacesOut(fMesh, fungAnchors);
                var fm = NewRoomMat("C_Fungus.mat", "GloomhavenVR/EnvRoomCutout");
                fm.SetTexture("_MainTex", Imp("fungus_alb")
                    ?? throw new Exception("Imported/Textures/fungus_alb.png is missing — the cellar's "
                                           + "bracket fungi would be untextured white cards. See "
                                           + "License.md for the CC0 sources the atlas is keyed from."));
                fm.SetFloat("_Cutoff", FungusCutoff);
                fm.SetFloat("_VCol", 1f);
                // Pale and cold, and DELIBERATELY not the moss tint. A polypore in
                // the dark is bone/ochre with a paler growing margin — it is one
                // of the few things in a cellar that is lighter than the stone it
                // is on, which is exactly why it reads as an object stuck to the
                // wall rather than as a stain in it. (The shading lane's indoor
                // biology carries the rest; this is the albedo it works from.)
                fm.SetColor("_Tint", new Color(0.72f, 0.66f, 0.52f));
                // NOT element-gated: _ElemGrow 0, so no card ever folds. See the
                // BRACKET FUNGI block — this is a property of the room, like the
                // cobwebs, not a thing Earth switches on. Earth still owns the
                // frontier on the walls behind them and the moss cushions beside
                // them, so the two layers read as one damp wall getting damper.
                fm.SetFloat("_ElemGrow", 0f);
                // ...and it frosts, like every other surface in the room.
                fm.SetFloat("_ElemFrost", 0.9f);
                fm.SetVector("_ElemWind", new Vector4(0f, 0f, 0f, 1f));
                fm.SetVector("_ElemWindDir", new Vector4(0f, 0f, 1f, 1f));
                var fGo = Place(root, "Fungus", fMesh, Vector3.zero, Vector3.zero, Vector3.one, fm);
                Defer(fm, fGo.transform, 1f);
                Debug.Log($"[GloomhavenVR][Env] Cellar BRACKET FUNGI (user, third time: \"es sieht eher "
                          + "aus wie Schleim, es soll eher aussehen wie wuchende Pflanzen und Pilze die "
                          + $"an den Wänden wachsen\"): {clumps} clumps / {caps} fruiting bodies on the "
                          + $"N/S/E walls at 0.20-1.70 m, {fung.Count / 4} cards ({fung.Count} verts). "
                          + "Each bracket is TWO crossed cards on the axis that points out of the wall — "
                          + "a vertical profile and a horizontal plan — so the shelf silhouette exists "
                          + "from every direction except straight down the wall normal. NOT "
                          + "element-gated: fungus in a damp cellar is a property of the room, and moss "
                          + "is a plant that cannot grow in a room with no daylight.");

                Debug.Log($"[GloomhavenVR][Env] Cellar growth: {placed} moss clumps at the wall "
                          + $"foot plus {onWall} cushions ON the wall face (0.10-0.95 m, 4-8 cm "
                          + $"proud, N/S/E only) — {acc.Count / 4} cards, {acc.Count} verts; "
                          + $"{blocked} refused for a prop or the rat's route; card span {span:F2} m. "
                          + "With Earth down every card is folded onto its own base edge (zero area).");
            }

            PaintContactAO(floorGo, 0.40f, 0.30f);
            FlushRig(rig);
            // SURFACE GROWTH — the coverage table, printed after the rig so the
            // frame it is computed against is the one the shader will use.
            ReportGrowth("Cellar", "Floor", floorGo, "earth moss", 1.0f, 0.10f, 1.90f,
                         rig.elemRad, 0.35f, PlaceRoom(moss: true));
            ReportGrowth("Cellar", "Floor", floorGo, "ice frost", 1.0f, 0.15f, 1.15f,
                         rig.elemRad, 0.35f, PlaceRoom(moss: false));
            if (wallGoN != null)
            {
                ReportGrowth("Cellar", "WallN", wallGoN, "earth moss", 1.0f, 0.10f, 1.90f,
                             rig.elemRad, 0.45f, PlaceRoom(moss: true));
                ReportGrowth("Cellar", "WallN", wallGoN, "ice frost", 1.0f, 0.15f, 1.15f,
                             rig.elemRad, 0.45f, PlaceRoom(moss: false));
            }
            ReportGrounding("Cellar");
            // 'Floor' is what the board stands on; the moonlight and its pools on
            // the flagstones are light, not matter; 'Rat' is authored in its own
            // body space at the origin and put on its path by the vertex shader,
            // so its raw vertices say nothing about where it ever is.
            AssertPlaySpaceClear(root, "Cellar", CellarPlaySpaceDia,
                "Floor", "MoonShaft", "MoonPool", "MoonPoolAir", "WindowGlow", "Rat");
            Debug.Log("[GloomhavenVR][Env] Cellar room geometry assembled.");
        }

        // ------------------------------------------------- atmosphere geometry
        /// <summary>The window's embrasure: two jambs, a head and a sill, all
        /// facing INTO the opening, running from the wall plane (z = zWall)
        /// outward. The walls are zero-thickness planes, so without this there
        /// is no inside of the opening at all — and anything placed at the
        /// window necessarily hangs in front of the stone.</summary>
        /// <summary>The embrasure: jambs, head and cill cut through `d` metres of
        /// masonry, SPLAYED (ModBuild 146, user: "das Fenster soll so erscheinen
        /// dass es in einer dicken Wand drin ist").
        ///
        /// <para>The inner opening is the rect the wall mesh really cut. The
        /// OUTER opening is that rect pulled in by `inset` on each jamb and
        /// raised by `rise` at the cill, with the head left flat — so the four
        /// faces are trapezoids rather than rectangles, and the two sloping ones
        /// are precisely the reading. A square bore through half a metre of stone
        /// reads as a box with a hole in it whatever its depth; a flared jamb and
        /// a falling cill read as thickness from any angle, because their slope
        /// is visible as a slope and a depth is only visible as a parallax.</para>
        ///
        /// <para>THE WINDING IS THE TRAP THIS FILE HAS ALREADY PAID FOR FOUR
        /// TIMES (see PuddleMesh, which was inside out for ten builds). Every
        /// face here is seen from INSIDE the tunnel and from nowhere else, so
        /// every normal must point at the tunnel's own axis. The four quads below
        /// keep the exact cyclic order the unsplayed version used — (o, o+du,
        /// o+du+dv, o+dv) — so the splay cannot have flipped anything, and
        /// AssertRevealFacesIn() proves it against the built mesh rather than
        /// against this comment.</para></summary>
        private static Mesh RevealMesh(float x0, float y0, float x1, float y1, float zWall, float d,
                                       float inset, float rise)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            const float us = 3.4f;   // same world UV scale as the walls
            // Per-face UV axes. World-derived, so the reveal's stone continues the
            // wall's instead of restarting at a block edge — but a splayed face is
            // no longer axis-aligned, so the axes have to be named rather than
            // read off the face's own edge vectors (a single world-space formula
            // collapses the head's v to a constant and gives it a zero-area UV).
            // `flip` reverses the triangle order. THE TWO JAMBS NEED IT, AND THAT
            // IS A REAL BUG THIS ROUND'S GATE FOUND rather than a consequence of
            // the splay. Written out because the arithmetic is the evidence:
            //
            // the shipped (unsplayed) reveal built its west jamb as o=(x0,y0,zW),
            // du=(0,0,d), dv=(0,h,0), and its own comment said "normal =
            // cross(du,dv)". cross((0,0,d),(0,h,0)) = (-dh,0,0), i.e. -X — and the
            // bore is at +X from x0, so that face pointed INTO the masonry. The
            // east jamb came out +X for the same reason, with the bore at -X. This
            // project's own convention for "outward" is the one in
            // AssertClosedAndOutward — Cross(q-p, r-p), the same sign its signed
            // volume test needs — so both jambs have been wound against the only
            // side they can ever be seen from, since the reveal was written. Under
            // EnvRoom's Cull Back that means the two side faces of the embrasure
            // were not drawn at all: the ModBuild 136 note about the west jamb
            // being "one of the only two surfaces the moon strikes head-on" was
            // describing a face nobody could see. The head and the cill were
            // always right, which is why nothing looked obviously broken — a
            // window with a lit top and bottom and black sides reads as a dark
            // window. FIFTH mesh in this project wound against its own viewer.
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, bool jamb, float yEdge, float vSign,
                      bool flip)
            {
                int i0 = v.Count;
                foreach (var p in new[] { a, b, c, e })
                {
                    v.Add(p);
                    uv.Add(jamb
                        ? new Vector2((p.x + p.z) / us, p.y / us)
                        : new Vector2((p.x + zWall) / us, (yEdge + vSign * (p.z - zWall)) / us));
                }
                tri.AddRange(flip
                    ? new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 }
                    : new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            float zo = zWall + d;                 // the outer face
            float xa = x0 + inset, xb = x1 - inset, yc = y0 + rise;   // the outer opening
            // west jamb (seen from inside the tunnel, so its normal is +X) — FLIPPED
            Quad(new Vector3(x0, y0, zWall), new Vector3(xa, yc, zo),
                 new Vector3(xa, y1, zo), new Vector3(x0, y1, zWall), true, 0f, 0f, true);
            // east jamb (normal -X) — FLIPPED
            Quad(new Vector3(xb, yc, zo), new Vector3(x1, y0, zWall),
                 new Vector3(x1, y1, zWall), new Vector3(xb, y1, zo), true, 0f, 0f, true);
            // head — FLAT, normal down
            Quad(new Vector3(x0, y1, zWall), new Vector3(x1, y1, zWall),
                 new Vector3(xb, y1, zo), new Vector3(xa, y1, zo), false, y1, 1f, false);
            // cill — falls INTO the room, normal up
            Quad(new Vector3(xa, yc, zo), new Vector3(xb, yc, zo),
                 new Vector3(x1, y0, zWall), new Vector3(x0, y0, zWall), false, y0, -1f, false);
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>THE WINDING GATE for the embrasure. Every face of a reveal is
        /// seen from inside the tunnel and from nowhere else: the wall is opaque
        /// and the tunnel is the only place a camera can be that sees any of
        /// them. So the claim is exact — each triangle's normal must point TOWARD
        /// the tunnel's own axis — and it is checked against the built mesh.
        ///
        /// <para>This project has shipped FOUR meshes wound against the side they
        /// are seen from, one of which (the puddle) was invisible for ten builds
        /// behind Cull Back. A rewrite of a mesh's topology — which the splay is
        /// — is exactly the moment that happens, so the rewrite comes with the
        /// gate. Verified by REVERSING the triangle order in RevealMesh and
        /// re-baking: the build stops with all 8 triangles listed.</para></summary>
        private static void AssertRevealFacesIn(Mesh m, float x0, float y0, float x1, float y1,
                                                float zWall, float d)
        {
            var v = Verts(m); var t = m.triangles;
            var axis0 = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, zWall);
            var axis1 = axis0 + new Vector3(0f, 0f, d);
            int bad = 0; float worst = 1f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                var ctr = (a + b + c) / 3f;
                // the nearest point of the tunnel's axis segment, so the test is
                // about the face's own place in the bore and not about the ends
                float s = Mathf.Clamp01(Vector3.Dot(ctr - axis0, axis1 - axis0)
                                        / Mathf.Max((axis1 - axis0).sqrMagnitude, 1e-6f));
                var toAxis = (axis0 + (axis1 - axis0) * s) - ctr;
                if (toAxis.sqrMagnitude < 1e-8f) continue;
                float dp = Vector3.Dot(n, toAxis.normalized);
                worst = Mathf.Min(worst, dp);
                if (dp <= 0f) bad++;
            }
            if (bad > 0)
                throw new Exception($"Window reveal: {bad} of {t.Length / 3} triangles face AWAY from the "
                                    + "bore's axis, i.e. the embrasure is wound inside out and Cull Back "
                                    + "will hide it from the only place it can ever be seen from. "
                                    + "Reverse the quad order in RevealMesh (see its WINDING note).");
            Debug.Log($"[GloomhavenVR][Env] Cellar reveal winding OK: all {t.Length / 3} triangles face the "
                      + $"bore's axis, worst dot {worst:F3} (must be > 0).");
        }

        // ================================================== HEWN: breaking the box
        // USER FINDING, hardware 2026-08-14: "Ich mag auch den Keller im Groben
        // kannst du ihn so lassen. Mein Hauptproblem: Er ist noch zu eckig um
        // realistisch zu sein, die Stellen an denen Wände und Böden/Decke
        // aneinander Treffen sind perfekte 90 grad winkel, mach hier etwas
        // unregelmäßigkeit rein, damit es nie zu sehr wie ein Rechteck erscheint
        // in dem man ist. Zusätzlich auch die tragenden Elemende der Decke werden
        // von perfekten Würfeln getragen."
        //
        // The ROOM is right and stays as it is. What is wrong is that every one of
        // its EDGES is a mathematical line, and there are twelve of them:
        //   4 wall/floor lines   -> AddWallSkirt    (rubble, spilled mortar)
        //   4 wall/ceiling lines -> AddCeilingCove  (+ three wall-plate timbers)
        //   4 vertical corners   -> AddCornerQuoins / AddCornerCant / ...Rubble
        // plus the two things that are literally boxes: the corbels and the beams.
        //
        // WHY THE WALLS COULD NOT DO IT THEMSELVES. WallMesh already carries a
        // 3.5 cm masonry bulge, but its `edge` term tapers that bulge to ZERO at
        // every wall edge and every hole rim — so the one place the wall is
        // allowed to be irregular is precisely the place that is forced dead flat.
        // Lifting that taper was rejected: it opens gaps at the window and stair
        // rims and at the wall/wall joins, and WallMesh is shared. The junctions
        // instead get their OWN welded geometry, which overlaps both surfaces it
        // sits between and can therefore never open a seam.
        //
        // ALL OF IT IS ONE MESH per material, in room coordinates, at the identity
        // transform. The light rig is baked per MATERIAL in OBJECT space (see
        // MergeInto's header for the bug that taught us), so N transforms sharing
        // one material would all be lit from the first one's position; one welded
        // mesh at the identity makes object space == room space, which is exact —
        // and EnvRoom's point lights are evaluated per PIXEL from i.opos, so a
        // 26 cm station spacing costs nothing in the shading.
        //
        // Every displacement is a seeded Fbm2/Hash3. Nothing here uses Random.

        /// <summary>One wall of the cellar as a RUN: where its base line starts,
        /// which way it runs, and which way is INTO the room. (along, into, up) is
        /// a right-handed triple for all four walls — that is what lets every
        /// strip below use ONE winding order instead of four.</summary>
        private struct WallRun
        {
            public string name;
            public Vector3 p0;      // room-space start of the base line (y = 0)
            public Vector3 along;   // unit, along the wall
            public Vector3 into;    // unit, into the room
            public float len;
            public int seed;
        }

        /// <summary>The four runs. p0/along MUST agree with how BuildCellarRoom
        /// places the WallMesh planes (Wall("WallN", (-hw,0,hd), yaw 0) and the
        /// three that follow): a run transcribed backwards would heap its rubble
        /// at the wrong end of its wall and nothing would ever say so.</summary>
        private static WallRun[] CellarWalls()
        {
            float hw = CW / 2f, hd = CD / 2f;
            return new[]
            {
                new WallRun { name = "N", p0 = new Vector3(-hw, 0f, hd),  along = Vector3.right,   into = Vector3.back,    len = CW, seed = 5101 },
                new WallRun { name = "S", p0 = new Vector3(hw, 0f, -hd),  along = Vector3.left,    into = Vector3.forward, len = CW, seed = 5209 },
                new WallRun { name = "E", p0 = new Vector3(hw, 0f, hd),   along = Vector3.back,    into = Vector3.left,    len = CD, seed = 5317 },
                new WallRun { name = "W", p0 = new Vector3(-hw, 0f, -hd), along = Vector3.forward, into = Vector3.right,   len = CD, seed = 5431 },
            };
        }

        /// <summary>A gate over a wall run: 1 everywhere, fading to 0 within
        /// `half` of each listed wall-local position and back over a further
        /// 22 cm. This is how the rat holes, the stair doorway and the beam
        /// pockets stay open through geometry that otherwise runs the whole
        /// length of a wall.</summary>
        /// <summary>Stretch an fbm onto its authored band. Fbm2 is an average of
        /// value-noise octaves, so it lives around 0.5 and only rarely leaves
        /// [0.28, 0.80]: fed straight into Lerp(min, max, f) it delivers about
        /// HALF the amplitude the caller wrote down, which is how a "5-25 cm"
        /// skirting measures 10-17 cm and reads as a moulding. Remapped, the
        /// authored numbers are the numbers you get.</summary>
        private static float Band(float fbm, float min, float max) =>
            min + (max - min) * Mathf.Clamp01(Mathf.InverseLerp(0.28f, 0.80f, fbm));

        private static Func<float, float> ClearOf(params (float at, float half)[] zones)
        {
            return t =>
            {
                float k = 1f;
                foreach (var z in zones)
                    k = Mathf.Min(k, Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(z.half, z.half + 0.22f, Mathf.Abs(t - z.at))));
                return k;
            };
        }

        /// <summary>Broken masonry, spilled mortar and rubble along the foot of one
        /// wall — the wall/floor junction, which used to be a 10.5 m straight line
        /// at exactly 90 degrees.
        ///
        /// The cross-section is three rows: a TOE out on the flagstones, a CREST
        /// that carries 62% of the height at 48% of the depth (so the heap is
        /// convex — a straight two-row ramp reads as a skirting BOARD), and a TOP
        /// row driven 3 cm INTO the wall, which is what guarantees the run can
        /// never show a slot behind its own top edge no matter how the wall's own
        /// bulge moves under it.
        ///
        /// Height and depth are two independent fbm slices along the wall, and
        /// both are multiplied by an INTERRUPTION gate: where a third fbm falls
        /// below 0.40 the wall is swept bare and NO geometry is emitted at all.
        /// That is the whole point — a skirting of constant section would only
        /// have replaced one straight line with two.
        ///
        /// It sits on CellarFloorY, not on y = 0: the flagstones undulate +-6 mm
        /// and a run laid on a flat zero would float on the high spots exactly the
        /// way the props did before ModBuild 132.</summary>
        private static string AddWallSkirt(Acc a, WallRun w, Func<float, float> keepClear)
        {
            const float step = 0.26f;              // one station every 26 cm
            // The user's target is "reads from standing eye height", i.e. tens of
            // centimetres, not millimetres. These are the caps; Band() below is
            // what actually delivers them (measured peaks land at 0.20-0.28 m).
            // The depth cap is also the play-space budget: the north and south
            // walls are only 4.50 m out, so 0.36 m of rubble plus a half-metre
            // loose block still leaves >4.1 m against a 3.25 m requirement.
            const float minH = 0.06f, maxH = 0.30f;
            const float minD = 0.07f, maxD = 0.36f;
            int n = Mathf.Max(2, Mathf.RoundToInt(w.len / step));
            var toe = new Vector3[n + 1]; var crest = new Vector3[n + 1]; var top = new Vector3[n + 1];
            var gs = new float[n + 1];
            float peakH = 0f, peakD = 0f; int live = 0;
            for (int i = 0; i <= n; i++)
            {
                float t = w.len * i / n;
                float g = keepClear(t)
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.66f,
                              Fbm2(t * 0.62f + 3.7f, 0.5f, 3, w.seed)))
                        // a run that stopped dead at the corner would draw a new
                        // straight line there; it fades out and the corner's own
                        // heap takes over
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.14f, Mathf.Min(t, w.len - t)));
                float h = g * Band(Fbm2(t * 1.35f + 11f, 2.5f, 3, w.seed + 7), minH, maxH);
                float d = g * Band(Fbm2(t * 1.10f + 23f, 4.5f, 3, w.seed + 13), minD, maxD);
                gs[i] = g;
                if (g > 0.02f) live++;
                if (h > peakH) peakH = h;
                if (d > peakD) peakD = d;
                // The two BURIAL offsets — the top row's 3 cm into the wall and
                // the toe's 8 mm under the flagstones — fade out with the gate as
                // well. They must: held constant they keep the three rows apart
                // where the run has already gone to nothing, and every fade
                // boundary then ends in a 3 cm ribbon standing on edge, whose
                // normal points wherever the neighbouring station happens to be.
                // Faded, the rows collapse onto one line and AddFaceUV drops the
                // quads outright.
                float k = Mathf.Min(1f, g * 6f);
                Vector3 b = w.p0 + w.along * t;
                Vector3 pt = b + w.into * d, pc = b + w.into * (d * 0.48f), pp = b - w.into * (0.03f * k);
                float fy = CellarFloorY(b.x, b.z);
                toe[i] = new Vector3(pt.x, CellarFloorY(pt.x, pt.z) - 0.008f * k, pt.z);
                crest[i] = new Vector3(pc.x, fy + h * 0.62f, pc.z);
                top[i] = new Vector3(pp.x, fy + h, pp.z);
            }
            // WINDING: (along, into, up) is right-handed for all four runs, so
            // ordering each quad [lower-and-further-out, higher-and-further-in,
            // ... next station] gives cross(p1-p0,p2-p0) = +up/+into, i.e. a face
            // the player sees. Reverse it and the skirting is invisible from
            // inside the room and perfectly visible from outside it, which is the
            // ModBuild 137 failure with different coordinates.
            for (int i = 0; i < n; i++)
            {
                if (gs[i] < 0.02f && gs[i + 1] < 0.02f) continue;    // swept stretch
                AddFaceUV(a, toe[i], crest[i], crest[i + 1], toe[i + 1], 3.4f, Grey(0.60f));
                AddFaceUV(a, crest[i], top[i], top[i + 1], crest[i + 1], 3.4f, Grey(0.90f));
            }
            // Loose blocks fallen out of the courses. They are what gives the run a
            // SILHOUETTE — a smooth heap still reads as a moulding from three
            // metres — and they only appear where there is already a pile to lie
            // in, so the swept stretches stay swept.
            int stones = 0;
            for (int i = 1; i < n; i++)
            {
                if (gs[i] < 0.35f || Hash3(i, 0, 0, w.seed + 77) < 0.56f) continue;
                float t = w.len * i / n + (Hash3(i, 1, 0, w.seed + 77) - 0.5f) * step;
                float d = Band(Fbm2(t * 1.10f + 23f, 4.5f, 3, w.seed + 13), minD, maxD);
                Vector3 b = w.p0 + w.along * t + w.into * (d * 0.55f);
                float sw = 0.14f + 0.16f * Hash3(i, 2, 0, w.seed + 77);
                float sh = 0.09f + 0.09f * Hash3(i, 3, 0, w.seed + 77);
                float sd = 0.11f + 0.11f * Hash3(i, 4, 0, w.seed + 77);
                var rot = Quaternion.LookRotation(w.into, Vector3.up)
                        * Quaternion.Euler((Hash3(i, 5, 0, w.seed + 77) - 0.5f) * 26f,
                                           (Hash3(i, 6, 0, w.seed + 77) - 0.5f) * 60f,
                                           (Hash3(i, 7, 0, w.seed + 77) - 0.5f) * 22f);
                // sunk 12% of its own height into the heap: a block resting ON a
                // surface at exactly tangency is the floating-prop problem again
                AddHewnBlock(a, new Vector3(b.x, CellarFloorY(b.x, b.z) + sh * 0.38f, b.z),
                             rot, new Vector3(sw, sh, sd), 0.82f, 0.10f, 0.012f, 1.6f,
                             w.seed + 900 + i, Grey(0.86f));
                stones++;
            }
            return $"{w.name} skirt: h<={peakH * 100f:F0} cm, d<={peakD * 100f:F0} cm, "
                 + $"{live}/{n + 1} stations heaped, {stones} loose blocks";
        }

        /// <summary>The wall/ceiling junction: a crumbling mortar cove that drops a
        /// varying distance down the wall and reaches a varying distance out over
        /// the planks, INTERRUPTED so that stretches of the joint are simply gone
        /// and you see raw stone meet raw board.
        ///
        /// Its wall row is buried 3 cm behind the wall plane and its ceiling row
        /// 1 cm ABOVE CellarCeilY — i.e. it is bedded into both surfaces it joins,
        /// never butted against either, so neither the ceiling's new sag nor the
        /// wall's masonry bulge can open a crack along it.</summary>
        private static string AddCeilingCove(Acc a, WallRun w, Func<float, float> keepClear)
        {
            const float step = 0.30f;
            const float minDrop = 0.05f, maxDrop = 0.28f;
            const float minProj = 0.04f, maxProj = 0.22f;
            int n = Mathf.Max(2, Mathf.RoundToInt(w.len / step));
            var rw = new Vector3[n + 1]; var rm = new Vector3[n + 1]; var rc = new Vector3[n + 1];
            var gs = new float[n + 1];
            float peakDrop = 0f, peakProj = 0f; int live = 0;
            for (int i = 0; i <= n; i++)
            {
                float t = w.len * i / n;
                float g = keepClear(t)
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.66f,
                              Fbm2(t * 0.55f + 13f, 8.5f, 3, w.seed + 41)))
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.16f, Mathf.Min(t, w.len - t)));
                float drop = g * Band(Fbm2(t * 1.20f + 31f, 3.5f, 3, w.seed + 47), minDrop, maxDrop);
                float proj = g * Band(Fbm2(t * 0.95f + 47f, 7.5f, 3, w.seed + 53), minProj, maxProj);
                gs[i] = g;
                if (g > 0.02f) live++;
                if (drop > peakDrop) peakDrop = drop;
                if (proj > peakProj) peakProj = proj;
                // both burials fade with the gate, for the reason spelled out in
                // AddWallSkirt: constant offsets leave a standing ribbon wherever
                // the run tapers out, and its facing is then anybody's guess.
                // (CellarCeilY is exactly CH at a wall — the walls are supports —
                // so at g = 0 all three rows land on the same line and die.)
                float k = Mathf.Min(1f, g * 6f);
                Vector3 b = w.p0 + w.along * t;
                Vector3 pW = b - w.into * (0.03f * k), pM = b + w.into * (proj * 0.45f), pC = b + w.into * proj;
                rw[i] = new Vector3(pW.x, CH - drop, pW.z);
                rm[i] = new Vector3(pM.x, CH - drop * 0.35f, pM.z);
                rc[i] = new Vector3(pC.x, CellarCeilY(pC.x, pC.z) + 0.010f * k, pC.z);
            }
            // same right-handed ordering as the skirt, read the other way up: the
            // face comes out pointing DOWN and INTO the room, which is where the
            // player's eye is.
            for (int i = 0; i < n; i++)
            {
                if (gs[i] < 0.02f && gs[i + 1] < 0.02f) continue;
                AddFaceUV(a, rw[i], rm[i], rm[i + 1], rw[i + 1], 3.4f, Grey(0.70f));
                AddFaceUV(a, rm[i], rc[i], rc[i + 1], rm[i + 1], 3.4f, Grey(0.86f));
            }
            return $"{w.name} cove: drop<={peakDrop * 100f:F0} cm, reach<={peakProj * 100f:F0} cm, "
                 + $"{live}/{n + 1} stations";
        }

        /// <summary>QUOINS at one vertical corner: courses that alternate which of
        /// the two walls they stand proud of, so the corner LINE steps in and out
        /// instead of being a line. Two flat planes meeting at a perfect 90 degree
        /// edge is the single strongest "I am inside a box" cue there is, and it is
        /// the one the user named first.
        ///
        /// Each course gets its own height, length, projection and a few degrees of
        /// yaw/pitch/roll, and each block is a hewn block, so no two are alike and
        /// none of them is square.</summary>
        private static int AddCornerQuoins(Acc a, Vector3 corner, Vector3 inA, Vector3 inB,
            float y0, float y1, int courses, float projMin, float projMax, int seed)
        {
            float span = (y1 - y0) / courses;
            for (int k = 0; k < courses; k++)
            {
                bool even = (k & 1) == 0;
                Vector3 proj = even ? inA : inB;    // which wall this course stands out of
                Vector3 run = even ? inB : inA;     // and which way it runs from the corner
                float hgt = span * (0.72f + 0.36f * Hash3(k, 0, 0, seed));
                float yc = y0 + span * (k + 0.5f);
                float lng = 0.30f + 0.26f * Hash3(k, 1, 0, seed);
                float dep = projMin + (projMax - projMin) * Hash3(k, 2, 0, seed);
                // the block spans [-2 cm, dep] out of its wall and [-2 cm, lng]
                // along it: the 2 cm are buried, so no course can show an edge
                // where it meets the stone it sits against
                Vector3 c = corner + Vector3.up * yc
                          + proj * ((dep - 0.02f) * 0.5f)
                          + run * ((lng - 0.02f) * 0.5f);
                var rot = Quaternion.LookRotation(proj, Vector3.up)
                        * Quaternion.Euler((Hash3(k, 3, 0, seed) - 0.5f) * 3.5f,
                                           (Hash3(k, 4, 0, seed) - 0.5f) * 5.0f,
                                           (Hash3(k, 5, 0, seed) - 0.5f) * 3.0f);
                AddHewnBlock(a, c, rot, new Vector3(lng + 0.02f, hgt, dep + 0.02f),
                             0.93f, 0.03f, 0.009f, 1.7f, seed + k * 13,
                             Grey(0.86f + 0.12f * Hash3(k, 6, 0, seed)));
            }
            return courses;
        }

        /// <summary>A CANT across one vertical corner: an irregular chamfer that
        /// cuts the 90 degree dihedral off entirely, its width breathing up the
        /// height and tapering back into both walls at top and bottom (a chamfer
        /// that STARTED somewhere would just be two more straight lines).</summary>
        private static void AddCornerCant(Acc a, Vector3 corner, Vector3 inA, Vector3 inB,
            float y0, float y1, float wMin, float wMax, int seed)
        {
            // The four corners of a rectangular room ALTERNATE handedness: at two
            // of them cross(inA,inB) is +up and at the other two it is -up. Left
            // alone, half the cants would be wound inside out and would render
            // only from outside the room — the ModBuild 137 hull bug, once per
            // diagonal. Normalise first, then there is one winding order.
            if (Vector3.Dot(Vector3.Cross(inA, inB), Vector3.up) < 0f)
            { var tmp = inA; inA = inB; inB = tmp; }

            int n = Mathf.Max(3, Mathf.RoundToInt((y1 - y0) / 0.30f));
            var onA = new Vector3[n + 1]; var onB = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float y = Mathf.Lerp(y0, y1, i / (float)n);
                float wa = wMin + (wMax - wMin) * Fbm2(y * 1.15f + 3f, 1.5f, 3, seed);
                float wb = wMin + (wMax - wMin) * Fbm2(y * 1.15f + 9f, 6.5f, 3, seed + 5);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.35f, Mathf.Min(y - y0, y1 - y)));
                // a point ON wall A runs away from the corner along inB, and is
                // sunk 2 cm behind wall A's own plane
                onA[i] = corner + Vector3.up * y + inB * (wb * k) - inA * 0.02f;
                onB[i] = corner + Vector3.up * y + inA * (wa * k) - inB * 0.02f;
            }
            for (int i = 0; i < n; i++)
                AddFaceUV(a, onB[i], onA[i], onA[i + 1], onB[i + 1], 1.9f, Grey(0.90f));
        }

        /// <summary>A rat hole: the mouth, the recess behind it, and the stone it
        /// took with it. Welded into the room's one stonework mesh, in room
        /// coordinates, so it is lit by the baked rig exactly like the wall it is
        /// in and costs no extra draw call.
        ///
        /// <para>USER FINDING, ModBuild 140: "Das 'Loch' aus dem die Ratte kommt
        /// und hineingeht ist aktuell ein Viereckiges schwarzes Rechteck. Das ist
        /// nicht sehr immersiv." It was one <c>AddQuad</c>, 16.4 x 12.4 cm, tinted
        /// 0.012 grey and stuck flat on the wall plane. Three things were wrong
        /// with it and all three are geometry, not shading:</para>
        ///
        /// <para>1. IT WAS A RECTANGLE. The mouth here is a half-arch swept from
        /// jamb to jamb, widest where it meets the floor (which is where a rat
        /// actually wears one), ragged over the crown, and leaning — the wobble is
        /// an fbm of the sweep angle, weighted by sin so it dies at both feet and
        /// the mouth still meets the flagstones on a clean line. The two holes get
        /// different seeds, different leans and different proportions, so they are
        /// not the same hole twice.</para>
        ///
        /// <para>2. IT HAD NO DEPTH, and at 0.6 m from a VR camera a flat black
        /// patch is read as a sticker instantly — stereo gives it away before the
        /// shading does. So the wall is really CUT (see the <c>holes</c> array
        /// handed to WallMesh) and this builds a blind pocket behind the opening:
        /// the mouth loop extruded back, tapered to two thirds and BENT sideways,
        /// with a cap at the end. The bend is the point — head-on you cannot see
        /// the back of it, so the hole reads as going somewhere.
        /// The alternative was to leave the wall closed and fake the recess in
        /// front of it, which is cheaper by one Rect and is the same sticker with
        /// more triangles: with the wall intact there is nothing for the pocket to
        /// be recessed INTO, so it can only protrude.</para>
        ///
        /// <para>3. IT DID NOT BELONG TO ITS WALL. The mouth now carries a ring of
        /// stone out to the cut edge, standing 2 mm proud so no seam can open, a
        /// dropped lintel block above it, and a spill of broken stone on the floor
        /// to either side — placed to the SIDES, because the rat comes out through
        /// the middle. Darkness is a vertex-colour gradient into _VCol (the same
        /// mechanism as the skirting's contact shading), not a flat near-black
        /// tint: 0.72 grey out at the courses, 0.34 at the rim, then 0.22 and 0.07
        /// down the pocket. And the flagstones in front get a contact pool through
        /// the room's existing <c>Contacts</c> list — a thousand crossings' worth
        /// of polish, for no triangles at all.</para>
        ///
        /// <para>`cut` is the SNAPPED opening (what WallMesh really removed), in
        /// wall-local (u, y). Authoring against the unsnapped rect is what floated
        /// the window bars in ModBuild 134.</para>
        ///
        /// <para>MODBUILD 143. Two changes, both of them consequences of the
        /// animal finally going INTO this thing (see THE BURROW):</para>
        ///
        /// <para>a. THE POCKET IS BORED ALONG `bore`, not square to the wall. The
        /// bore is the route's own tangent at this endpoint (RatBore) and it
        /// leans up to 34 degrees, which is what lets the animal walk in along
        /// its own line of travel instead of skating sideways into the jamb. It
        /// also makes the recess read deeper head-on than a square bore of the
        /// same depth: you cannot see its cap from in front, which was the whole
        /// intent of the ModBuild 140 bend and is now geometry rather than a
        /// hashed nudge.</para>
        ///
        /// <para>b. THE MOUTH IS AT LEAST THE SIZE OF THE ANIMAL. The arch is
        /// pushed out of an ellipse sized from RatMesh's own widest ring, its
        /// ride height and the spine wave. It costs nothing when the hash already
        /// chewed a big enough hole (it did, both times: 15.9 and 16.2 cm wide),
        /// and it means "a rat fits through it" is a build gate rather than an
        /// observation about the two seeds that happen to ship.</para></summary>
        private static string AddRatHole(Acc a, WallRun w, int wall, Rect cut, Vector3 bore, int seed)
        {
            // wall-local (u, y, z) -> room. WallMesh's own local +Z runs AWAY from
            // the room (its bulge is negative), and `into` points the other way,
            // so the pocket lives at positive z and subtracts `into`.
            Vector3 P(float u, float y, float z) => w.p0 + w.along * u + Vector3.up * y - w.into * z;
            // ...and the same point taken down the BORE instead of straight back.
            // The two agree at z = 0, so the mouth loop, the ring and the loose
            // blocks are untouched by the shear: only what is behind the wall
            // plane leans. Depth in Q is measured ALONG the bore, so a pocket
            // 15 cm deep penetrates 15*cos(lean) cm of a 16 cm wall.
            Vector3 Q(float u, float y, float z) => w.p0 + w.along * u + Vector3.up * y + bore * z;

            const int M = 12;
            // 12 mm UNDER the wall's base line, the same trick the skirting's toe
            // uses: the flagstones undulate by +-6 mm, so a mouth that met them at
            // exactly y=0 would show daylight under one jamb and bury the other.
            const float yBase = RatHoleBaseY;
            // The animal's own cross-section, plus 2 mm of daylight and 10 mm for
            // the body bob. Everything here is a RatMesh number, so a future round
            // that fattens the rat widens its doors.
            float fitW = RatBodyR + RatSway + 0.002f;
            float fitH = RatBodyTop + RatW0.y - yBase + 0.010f;
            if (fitW > cut.width * 0.5f - 0.022f || fitH > cut.height - 0.020f)
                throw new Exception($"{w.name} rat hole: the {cut.width * 100f:F1} x {cut.height * 100f:F1} cm "
                                    + $"cut cannot hold a mouth the animal fits through ({fitW * 200f:F1} x "
                                    + $"{fitH * 100f:F1} cm plus a 2.2 cm ring). Widen RatHoleAuthored.");
            float cu = (cut.xMin + cut.xMax) * 0.5f;
            float aw = cut.width * 0.20f;          // half-width before the wobble
            float ah = cut.height * 0.78f;
            float lean = 0.86f + 0.30f * Hash3(0, 0, 0, seed);   // one jamb chewed further out
            float depth = 0.125f + 0.065f * Hash3(1, 0, 0, seed);
            // The bend is what stops the pocket reading as a shoebox: head-on you
            // must NOT see the back of it. So it gets a floor as well as a sign —
            // a hashed value that happens to come out near zero would quietly
            // give one of the two holes a straight bore and nothing would say so.
            float bend = (Hash3(2, 0, 0, seed) < 0.5f ? -1f : 1f)
                       * (0.40f + 0.60f * Hash3(3, 0, 0, seed)) * (aw * 0.95f);

            // ---- the mouth loop, and the cut edge it has to reach
            var arch = new Vector2[M + 1];
            var rim = new Vector2[M + 1];
            float wMax = 0f, hMax = 0f, jambL = 0f, jambR = 0f;
            for (int i = 0; i <= M; i++)
            {
                float th = Mathf.PI * i / M;                    // 0 = one jamb, PI = the other
                float c = Mathf.Cos(th), s = Mathf.Sin(th);
                float rag = (Fbm2(th * 2.7f + 1.3f, 0.5f, 3, seed) - 0.5f) * 2f;
                // gnawed, not drilled: ragged at the crown, clean at the feet, and
                // flared where it meets the floor
                float k = 1f + 0.26f * rag * s + 0.18f * (1f - s);
                float u = cu + aw * c * k * (c > 0f ? lean : 2f - lean);
                float y = yBase + ah * s * k;
                // never let the mouth eat its own frame: 22 mm of ring is the
                // minimum that still reads as stone and not as a chamfer
                u = Mathf.Clamp(u, cut.xMin + 0.022f, cut.xMax - 0.022f);
                y = Mathf.Clamp(y, yBase, cut.yMax - 0.020f);
                // ...and never let it be smaller than the animal. Pushed OUT of
                // the fit ellipse along its own ray, which is the one correction
                // that cannot make the mouth lopsided: a point already outside is
                // left exactly where the raggedness put it.
                float du = u - cu, dy = y - yBase;
                float el = (du / fitW) * (du / fitW) + (dy / fitH) * (dy / fitH);
                if (el < 1f && el > 1e-6f)
                {
                    float push = 1f / Mathf.Sqrt(el);
                    u = cu + du * push;
                    y = yBase + dy * push;
                }
                arch[i] = new Vector2(u, y);
                wMax = Mathf.Max(wMax, Mathf.Abs(u - cu) * 2f);
                hMax = Mathf.Max(hMax, y - yBase);
                if (u > cu) jambR = Mathf.Max(jambR, u - cu); else jambL = Mathf.Max(jambL, cu - u);

                // the cut edge along the same ray, pushed 12 mm past it so the
                // ring always overlaps the courses it is set into
                float t = float.MaxValue;
                if (c > 1e-4f) t = Mathf.Min(t, (cut.xMax + 0.012f - cu) / c);
                if (c < -1e-4f) t = Mathf.Min(t, (cut.xMin - 0.012f - cu) / c);
                if (s > 1e-4f) t = Mathf.Min(t, (cut.yMax + 0.012f - yBase) / s);
                rim[i] = new Vector2(cu + t * c, yBase + t * s);
            }

            // EVERY face below goes through this, and every face below states the
            // point it has to be visible FROM. Winding is the one mistake this
            // room keeps making — the moonbeam hull in ModBuild 137, the rat's own
            // body in 139 — and it is invisible in a diff and invisible in the
            // scene view from the wrong side. A recess is the worst case of all:
            // get it backwards and you see the OUTSIDE of the pocket, i.e. a
            // stone plug sitting in the hole, and it still looks like geometry.
            int bad = 0, faces = 0;
            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color col, Vector3 seenFrom)
            {
                int before = a.Count;
                AddFaceUV(a, p0, p1, p2, p3, 3.4f, col);
                if (a.Count == before) return;             // pinched to a line, dropped
                faces++;
                if (Vector3.Dot(a.N[a.Count - 1], seenFrom - (p0 + p1 + p2 + p3) * 0.25f) <= 0f) bad++;
            }
            // a standing player, 1.2 m out from the mouth — the only place this
            // hole is ever looked at from
            Vector3 eye = P(cu, 1.30f, -1.20f);

            // ---- the ring, 2 mm proud of the wall plane
            for (int i = 0; i < M; i++)
                Face(P(arch[i].x, arch[i].y, -0.002f), P(arch[i + 1].x, arch[i + 1].y, -0.002f),
                     P(rim[i + 1].x, rim[i + 1].y, -0.002f), P(rim[i].x, rim[i].y, -0.002f),
                     Grey(Mathf.Lerp(0.34f, 0.72f, 0.5f + 0.5f * Mathf.Cos(Mathf.PI * i / M))), eye);

            // ---- the pocket. Two rings deep, so the darkness is a gradient down
            // the recess and not one flat value on a single quad.
            Vector2 Shrink(Vector2 p, float f) =>
                new Vector2(cu + bend * (1f - f) + (p.x - cu) * f, yBase + (p.y - yBase) * f);
            float[] zs = { 0f, depth * 0.45f, depth };
            float[] fs = { 1f, 0.84f, 0.66f };
            float[] gs = { 0.24f, 0.13f, 0.07f };
            // A point ON the pocket's centreline at depth z. Every wall of the
            // recess has to face this, which is the exact statement of "the
            // inside of the pocket" and the thing the ModBuild 137 bug got
            // backwards.
            Vector3 Axis(int r) => Q(cu + bend * (1f - fs[r]) * 0.5f, yBase + ah * 0.42f * fs[r],
                                     (zs[r] + zs[r + 1]) * 0.5f);
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < M; i++)
                {
                    Vector2 a0 = Shrink(arch[i], fs[r]), a1 = Shrink(arch[i + 1], fs[r]);
                    Vector2 b0 = Shrink(arch[i], fs[r + 1]), b1 = Shrink(arch[i + 1], fs[r + 1]);
                    // (front_i, back_i, back_i+1, front_i+1) on a counter-clockwise
                    // loop gives cross(dz, tangent) = the INWARD normal, which is
                    // the only side of a pocket anybody can see
                    Face(Q(a0.x, a0.y, zs[r]), Q(b0.x, b0.y, zs[r + 1]),
                         Q(b1.x, b1.y, zs[r + 1]), Q(a1.x, a1.y, zs[r]), Grey(gs[r]), Axis(r));
                }
            // ...and the floor of it, which is the same extrusion of the one
            // segment that closes the loop along the flagstones.
            for (int r = 0; r < 2; r++)
            {
                Vector2 a0 = Shrink(arch[M], fs[r]), a1 = Shrink(arch[0], fs[r]);
                Vector2 b0 = Shrink(arch[M], fs[r + 1]), b1 = Shrink(arch[0], fs[r + 1]);
                Face(Q(a0.x, a0.y, zs[r]), Q(b0.x, b0.y, zs[r + 1]),
                     Q(b1.x, b1.y, zs[r + 1]), Q(a1.x, a1.y, zs[r]), Grey(gs[r] * 0.8f), Axis(r));
            }
            // the end of it, facing back out at the room
            var ctr = new Vector2(cu + bend * 0.34f, yBase + ah * 0.40f * fs[2]);
            for (int i = 0; i < M; i++)
            {
                Vector2 b0 = Shrink(arch[i], fs[2]), b1 = Shrink(arch[i + 1], fs[2]);
                int bi = a.Count;
                Vector3 q0 = Q(ctr.x, ctr.y, depth), q1 = Q(b1.x, b1.y, depth), q2 = Q(b0.x, b0.y, depth);
                Vector3 nn = Vector3.Cross(q1 - q0, q2 - q0);
                if (nn.sqrMagnitude < 1e-12f) continue;
                nn.Normalize();
                var cc = Grey(0.045f);
                a.Vert(q0, nn, PlanarUV(q0, nn, 3.4f), cc);
                a.Vert(q1, nn, PlanarUV(q1, nn, 3.4f), cc);
                a.Vert(q2, nn, PlanarUV(q2, nn, 3.4f), cc);
                a.T.AddRange(new[] { bi, bi + 1, bi + 2 });
                faces++;
                if (Vector3.Dot(nn, eye - (q0 + q1 + q2) / 3f) <= 0f) bad++;
            }
            if (bad > 0)
                throw new Exception($"{w.name} rat hole: {bad} of {faces} faces are wound away from the "
                                    + "only side they can be seen from. A backwards recess draws as a stone "
                                    + "plug sitting in the opening — see the winding note over AddTube.");

            // ---- the stone it took with it. The lintel block sits ON the ring,
            // the other three lie on the flagstones OFF TO THE SIDES: the rat's
            // Bezier leaves through the middle of this mouth, and a block in front
            // of it would be a block the animal walks through.
            int blocks = 0;
            for (int b = 0; b < 4; b++)
            {
                bool lintel = b == 0;
                float side = (b % 2 == 0) ? 1f : -1f;
                float du = side * (aw * 1.25f + 0.09f * Hash3(b, 1, 0, seed + 41));
                float sw = 0.045f + 0.055f * Hash3(b, 2, 0, seed + 41);
                float sh = 0.030f + 0.040f * Hash3(b, 3, 0, seed + 41);
                float sd = 0.040f + 0.050f * Hash3(b, 4, 0, seed + 41);
                Vector3 at = lintel
                    ? P(cu + (Hash3(b, 5, 0, seed + 41) - 0.5f) * aw, hMax + yBase + 0.030f, -0.018f)
                    : P(cu + du, 0f, -(0.055f + 0.11f * Hash3(b, 6, 0, seed + 41)));
                if (!lintel)
                    at = new Vector3(at.x, CellarFloorY(at.x, at.z) + sh * 0.34f, at.z);
                var rot = Quaternion.LookRotation(w.into, Vector3.up)
                        * Quaternion.Euler((Hash3(b, 7, 0, seed + 41) - 0.5f) * 40f,
                                           (Hash3(b, 8, 0, seed + 41) - 0.5f) * 80f,
                                           (Hash3(b, 9, 0, seed + 41) - 0.5f) * 34f);
                AddHewnBlock(a, at, rot, new Vector3(sw, sh, sd), 0.80f, 0.12f, 0.007f, 1.6f,
                             seed + 300 + b, Grey(lintel ? 0.66f : 0.80f));
                blocks++;
            }

            // ---- and the polish. No triangles: the flagstone mesh already
            // carries contact shading in its vertex colours (PaintContactAO), and
            // a run the animal has used a thousand times is exactly that.
            Vector3 m0 = P(cu - aw * 1.6f, 0f, -0.02f), m1 = P(cu + aw * 1.6f, 0f, -0.30f);
            Contacts.Add((new Foot
            {
                x0 = Mathf.Min(m0.x, m1.x), x1 = Mathf.Max(m0.x, m1.x),
                z0 = Mathf.Min(m0.z, m1.z), z1 = Mathf.Max(m0.z, m1.z),
            }, 0.80f));

            // ---- and what the animal needs to know about it. Measured here
            // rather than re-derived next to the material: cu, depth and bend are
            // all hashed off `seed`, and a second copy of them would be a second
            // hole that only agrees with this one by luck.
            RatHoles[wall] = new RatHoleGeo
            {
                set = true,
                mouth = P(cu, RatW0.y, 0f),
                bore = bore,
                along = w.along,
                depth = depth,
                capBend = bend * 0.34f,       // Shrink() at fs[2] = 0.66
                mouthW = wMax, mouthH = hMax,
            };

            float leanDeg = Vector3.Angle(bore, -w.into);
            return $"{w.name} rat hole: cut u {cut.xMin:F3}..{cut.xMax:F3} x y {cut.yMin:F3}..{cut.yMax:F3} "
                 + $"({Mathf.RoundToInt(cut.width / WallCell)} cells wide), mouth {wMax * 100f:F1} x "
                 + $"{hMax * 100f:F1} cm and NOT symmetric about it ({jambL * 100f:F1} cm one jamb, "
                 + $"{jambR * 100f:F1} the other) — the animal needs {fitW * 200f:F1} x {fitH * 100f:F1} cm "
                 + $"and gets it; ring >= 2.2 cm all round, pocket {depth * 100f:F0} cm deep along a bore "
                 + $"leaning {leanDeg:F0} deg off the wall (so {depth * Mathf.Cos(leanDeg * Mathf.Deg2Rad) * 100f:F0} cm "
                 + $"into a 16 cm wall), tapering to {fs[2]:F2} and bending {bend * 100f:+0.0;-0.0} cm, "
                 + $"{blocks} loose blocks, {faces} faces all facing the room (0 backwards), centre "
                 + $"{Mathf.Abs(cu - (w.name == "N" ? RatW0.x + CW / 2f : CW / 2f - RatW3.x)) * 100f:F1} cm "
                 + "off the route's own endpoint";
        }

        /// <summary>The heap where two skirtings meet. Both runs fade out over
        /// their last 14 cm, and this is what stands in the gap — which is also
        /// what a real cellar corner collects.</summary>
        private static int AddCornerRubble(Acc a, Vector3 corner, Vector3 inA, Vector3 inB,
            int count, float reach, int seed)
        {
            for (int k = 0; k < count; k++)
            {
                Vector3 p = corner + inA * (0.05f + reach * Hash3(k, 0, 0, seed))
                                   + inB * (0.05f + reach * Hash3(k, 1, 0, seed + 3));
                float sw = 0.13f + 0.17f * Hash3(k, 2, 0, seed);
                float sh = 0.08f + 0.13f * Hash3(k, 3, 0, seed);
                float sd = 0.11f + 0.14f * Hash3(k, 4, 0, seed);
                var rot = Quaternion.Euler((Hash3(k, 5, 0, seed) - 0.5f) * 30f,
                                           Hash3(k, 6, 0, seed) * 360f,
                                           (Hash3(k, 7, 0, seed) - 0.5f) * 26f);
                AddHewnBlock(a, new Vector3(p.x, CellarFloorY(p.x, p.z) + sh * 0.34f, p.z),
                             rot, new Vector3(sw, sh, sd), 0.80f, 0.12f, 0.013f, 1.6f,
                             seed + k * 17, Grey(0.84f));
            }
            return count;
        }

        /// <summary>One corbel's authored numbers. The BEAM reads `bear`, `z` and
        /// `dz` back out of here, so the stone and the timber cannot disagree about
        /// where they meet — which is the entire reason this is a struct and not
        /// eight literals typed twice.</summary>
        private struct Corbel
        {
            public int beam; public float sx;   // -1 west wall, +1 east wall
            public float z, dz;                 // its beam's centre line, and its own offset off it
            public float bear;                  // the height of its bearing face
            public float reach, wide, tall, setIn, yaw, roll;
            public int seed;
        }

        /// <summary>The eight brackets. Every one of them differs; in particular
        /// `bear` differs, which is what tilts the beam each PAIR carries.</summary>
        private static Corbel[] CellarCorbels()
        {
            var list = new List<Corbel>();
            for (int i = 0; i < 4; i++)
                for (int s = 0; s < 2; s++)
                    list.Add(new Corbel
                    {
                        beam = i,
                        sx = s == 0 ? -1f : 1f,
                        z = CellarBeamZ(i),
                        dz = (Hash3(i, s, 0, 6011) - 0.5f) * 0.09f,
                        // The bearing face. It used to be CH-0.50 + 0.24 = CH-0.26
                        // for all eight, to the millimetre. These were cut by hand,
                        // so it is now CH-0.26 -22/+14 mm and the beams sit crooked.
                        bear = CH - 0.26f + (Hash3(i, s, 1, 6011) - 0.62f) * 0.036f,
                        reach = 0.40f + 0.14f * Hash3(i, s, 2, 6011),   // the VISIBLE projection
                        wide = 0.30f + 0.10f * Hash3(i, s, 3, 6011),
                        tall = 0.26f + 0.10f * Hash3(i, s, 4, 6011),
                        setIn = 0.03f + 0.05f * Hash3(i, s, 5, 6011),   // how deep its root is buried
                        yaw = (Hash3(i, s, 6, 6011) - 0.5f) * 9f,
                        roll = (Hash3(i, s, 7, 6011) - 0.5f) * 5f,
                        seed = 6100 + i * 29 + s * 7,
                    });
            return list.ToArray();
        }

        /// <summary>A hewn stone bracket, in place of the BoxMesh(0.34, 0.24, 0.42)
        /// cube the user called out ("die tragenden Elemende der Decke werden von
        /// perfekten Würfeln getragen"). Two pieces: a tapered bracket whose sole
        /// sweeps up toward the tip and whose face narrows there, and a kicker
        /// stone tucked under its root — one piece reads as a bracket somebody
        /// modelled, two read as masonry.
        ///
        /// Its bearing face is authored 28 mm ABOVE `bear`, i.e. deliberately
        /// inside the beam, and that number is not a guess. The bracket carries up
        /// to 2.5 degrees of pitch over a half-metre reach, which walks its tip
        /// corner +-22 mm; at exact tangency the low case would drop the tip away
        /// from the timber and leave a slot you can see from across the room.
        /// Overlap is free (both surfaces are opaque and the join is hidden under
        /// the beam), gaps are not. Where the pad is wider than the beam the 28 mm
        /// simply shows as the step a bedded beam sits in. 24 triangles.</summary>
        private static void AddCorbel(Acc a, Corbel c)
        {
            float hw = CW / 2f;
            Vector3 into = new Vector3(-c.sx, 0f, 0f);          // into the room from its wall
            Vector3 wall = new Vector3(c.sx * hw, 0f, c.z + c.dz);
            // Euler is (pitch, yaw, roll) in the block's own frame, whose +Z is
            // `into`: pitch tips the bracket's nose down, roll leans its face.
            var rot = Quaternion.LookRotation(into, Vector3.up)
                    * Quaternion.Euler(c.roll * 0.55f, c.yaw, c.roll);

            float depth = c.reach + c.setIn;                    // spans [-setIn, reach]
            Vector3 body = wall + into * (depth * 0.5f - c.setIn)
                         + Vector3.up * (c.bear + 0.028f - c.tall * 0.5f);
            AddHewnBlock(a, body, rot, new Vector3(c.wide, c.tall, depth),
                         0.66f, 0.46f, 0.011f, 1.7f, c.seed, Grey(0.94f));

            float kd = 0.16f + 0.06f * Hash3(c.beam, 0, 1, c.seed);
            const float kh = 0.13f;
            Vector3 kick = wall + into * ((kd - 0.04f) * 0.5f)
                         + Vector3.up * (c.bear - c.tall - kh * 0.5f + 0.035f);
            AddHewnBlock(a, kick, rot, new Vector3(c.wide * 0.78f, kh, kd + 0.04f),
                         0.74f, 0.30f, 0.010f, 1.7f, c.seed + 5, Grey(0.86f));
        }

        /// <summary>One ceiling beam, swept from the WEST corbel's bearing face to
        /// the EAST one's. Hand-adzed timber is not a prism, so it gets, per beam
        /// and from its own seed: a downward BOW of 14-30 mm, a side-to-side
        /// WANDER of up to 34 mm, a cross-section that breathes +-10%, and a TWIST
        /// of up to 3 degrees over the length.
        ///
        /// TWO CONSTRAINTS SHAPE ALL OF THAT, and they are why the sag lives where
        /// it does. (a) The beam's ENDS are the corbels' bearing faces — read, not
        /// typed — and the bow is a sin() that vanishes at both ends, so the timber
        /// always lands ON the stone and the pair can never drift apart. Since the
        /// two ends differ in height the beam is also slightly out of level, which
        /// is free and completely correct. (b) The TOP face stays dead flat at
        /// CH + 6 mm, bedded into the planks: the ceiling is laid ON the beams, so
        /// the sag belongs on the soffit — which is also the only side of a beam
        /// anybody in this room will ever see. Sagging the whole section instead
        /// would open a 2 cm slot between beam and ceiling, 3.3 m up, at a grazing
        /// angle: exactly the kind of gap the eye finds instantly.
        ///
        /// The twist is applied to the SOLE only (the two bottom corners counter-
        /// rotate) for the same reason: rolling the whole section would tilt the
        /// top face out of the ceiling. 84 triangles per beam.</summary>
        private static string AddCellarBeam(Acc a, int i, Corbel west, Corbel east)
        {
            float hw = CW / 2f;
            const float overhang = 0.05f;      // the ends are buried behind the wall planes
            const int ns = 10;
            float sag = 0.014f + 0.016f * Hash3(i, 0, 0, 6203);
            float wob = 0.014f + 0.020f * Hash3(i, 1, 0, 6203);
            float twist = (Hash3(i, 2, 0, 6203) - 0.5f) * 0.055f;    // radians over the length
            float halfW0 = 0.145f + 0.020f * Hash3(i, 3, 0, 6203);
            const float topY = CH + 0.006f;

            var bl = new Vector3[ns + 1]; var br = new Vector3[ns + 1];
            var tl = new Vector3[ns + 1]; var tr = new Vector3[ns + 1];
            for (int k = 0; k <= ns; k++)
            {
                float x = Mathf.Lerp(-(hw + overhang), hw + overhang, k / (float)ns);
                float f = Mathf.Clamp01((x + hw) / CW);
                // Max(0) for the same reason CellarCeilY needs it: Mathf.PI is a
                // hair LARGER than pi, so Sin(1f * Mathf.PI) is -8.7e-8 and the
                // Pow below would return NaN at the beam's east end.
                float bow = Mathf.Max(0f, Mathf.Sin(f * Mathf.PI));
                float zc = west.z + wob * (Fbm2(f * 2.6f + 5f, i * 3.1f, 3, 6207) - 0.5f) * 2f * bow;
                float halfW = halfW0 * (1f + 0.10f * (Fbm2(f * 3.4f + 17f, i * 2.3f, 3, 6211) - 0.5f) * 2f);
                float under = Mathf.Lerp(west.bear, east.bear, f)
                            - sag * Mathf.Pow(bow, 1.15f)
                            - 0.010f * bow * Fbm2(f * 4.2f + 29f, i * 1.7f, 2, 6217);
                float dy = Mathf.Tan(twist * (f - 0.5f)) * halfW;
                bl[k] = new Vector3(x, under - dy, zc - halfW);
                br[k] = new Vector3(x, under + dy, zc + halfW);
                tl[k] = new Vector3(x, topY, zc - halfW);
                tr[k] = new Vector3(x, topY, zc + halfW);
            }
            for (int k = 0; k < ns; k++)
            {
                // vertex colour is grime, not light: the soffit is the smoke-black
                // face, the flanks stay lighter (which is what gives the beam an
                // edge to read against the planks), the top is never seen
                AddFaceUV(a, bl[k], bl[k + 1], br[k + 1], br[k], 1.3f, Grey(0.82f));   // soffit (-Y)
                AddFaceUV(a, tl[k], tr[k], tr[k + 1], tl[k + 1], 1.3f, Grey(0.70f));   // top (+Y), in the planks
                AddFaceUV(a, bl[k], tl[k], tl[k + 1], bl[k + 1], 1.3f, Grey(0.94f));   // -Z flank
                AddFaceUV(a, br[k], br[k + 1], tr[k + 1], tr[k], 1.3f, Grey(0.94f));   // +Z flank
            }
            // The ends are behind the wall planes and can never be seen, but an
            // open mesh is a trap for the next person who moves a wall.
            AddFaceUV(a, bl[0], br[0], tr[0], tl[0], 1.3f, Grey(0.78f));
            AddFaceUV(a, bl[ns], tl[ns], tr[ns], br[ns], 1.3f, Grey(0.78f));
            return $"beam{i}: bear W {west.bear:F3} / E {east.bear:F3} (out of level {(east.bear - west.bear) * 1000f:+0;-0} mm), "
                 + $"sag {sag * 1000f:F0} mm, wander {wob * 1000f:F0} mm, twist {twist * Mathf.Rad2Deg:F1} deg, "
                 + $"section {halfW0 * 2f:F3} m";
        }

        /// <summary>Wall plates: hewn timbers bedded in the wall/ceiling angle, on
        /// the two walls the beams do NOT run into (a plate on the east or west
        /// wall would have to pass through four beam ends, and the brief is
        /// explicit that nothing added here may intersect them).
        ///
        /// They deliberately do not run the full length and there are three of
        /// them, not four: a plate all the way round is just another continuous
        /// line at the same height, which is the thing being removed. Where a
        /// plate runs, the mortar cove is gated OFF — the plate is what is there
        /// instead. Wall index is into CellarWalls(): 0 N, 1 S, 2 E, 3 W.</summary>
        private static readonly (int wall, float t0, float t1)[] CellarPlates =
        {
            (0, 1.95f, 6.10f),    // N, the long one — above and clear of the window head
            (1, 2.30f, 4.05f),    // S, over the table end
            (1, 6.90f, 9.20f),    // S, a second and shorter one over the crates
        };

        /// <summary>The puddle: an irregular polar patch lying on (and following)
        /// the flagstones, its vertex ALPHA carrying the wet mask so the edge
        /// feathers into damp stone instead of ending in a rim.
        ///
        /// <para>THE WINDING WAS INSIDE OUT, AND THE PUDDLE HAS THEREFORE NEVER
        /// BEEN VISIBLE (found ModBuild 144, shipped since 134). The triangles
        /// were emitted (i0, j0, i1) / (i1, j0, j1), whose geometric normal —
        /// cross(b-a, c-a), which is Unity's front-face convention — points
        /// DOWN. EnvPuddle draws with Cull Back, so every one of its ~600
        /// triangles was discarded from any camera above the floor, and there is
        /// no camera below one. Both passes were dead.</para>
        ///
        /// <para>HOW IT SURVIVED THREE ROUNDS OF REVIEW, because that is the part
        /// worth recording: the moonbeam's pool (EnvGround) lands within 35 cm of
        /// PuddleAt, and a bright wet-looking patch of flagstone in exactly the
        /// right place is what everyone — including this lane, for an afternoon —
        /// took for the puddle. The ModBuild 143 note next to _DraftWave is the
        /// tell in hindsight: raising the wind ripple threefold "moved a measured
        /// maximum of 3/255 in that frame". It moved nothing at all; 3/255 was the
        /// drip. A preview that cannot see an effect and a preview that is looking
        /// at a different object produce the same report, and only a deliberate
        /// LOCATE pass (paint the additive pass magenta and count the pixels: 0
        /// with Cull Back, 134k with Cull Off) can tell them apart.</para>
        ///
        /// <para>Fixed by reversing the two triangles rather than by relaxing the
        /// cull: a horizontal surface should have a front and a back, and Cull Off
        /// would double the fill of both passes for a face nobody can ever see.
        /// </para></summary>
        private static Mesh PuddleMesh(Vector2 centre, float radius, int rings, int segs, int seed)
        {
            var a = new Acc();
            float Edge(float ang) => 0.74f + 0.42f * Fbm2(Mathf.Cos(ang) * 1.7f + 3f, Mathf.Sin(ang) * 1.7f, 2, seed);
            for (int r = 0; r <= rings; r++)
            {
                float f = Mathf.Pow(r / (float)rings, 0.92f);
                float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 0.55f, f));
                for (int s = 0; s < segs; s++)
                {
                    float ang = s / (float)segs * Mathf.PI * 2f;
                    float rr = radius * Edge(ang) * f;
                    float x = centre.x + Mathf.Cos(ang) * rr, z = centre.y + Mathf.Sin(ang) * rr;
                    a.Vert(new Vector3(x, CellarFloorY(x, z) + 0.007f, z), Vector3.up,
                           new Vector2(s / (float)segs, f), new Color(1, 1, 1, mask));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int i0 = r * segs + s, i1 = r * segs + (s + 1) % segs;
                    int j0 = i0 + segs, j1 = i1 + segs;
                    // UPWARD. cross(i1-i0, j0-i0) = +Y for the ascending ring
                    // order, which is Unity's front face — see the winding note
                    // above for what the other order cost.
                    a.T.AddRange(new[] { i0, i1, j0, i1, j1, j0 });
                }
            return a.Build("Env_C_Puddle");
        }

        /// <summary>The drop and its splash, as six cross-quads AT THE ORIGIN —
        /// EnvDrip puts every one of them where it belongs from _Time, so the
        /// mesh carries only the sprite quads and the per-element parameters
        /// (COLOR: r kind, g azimuth, b size/speed, a stretch flag).</summary>
        private static Mesh DripMesh()
        {
            var a = new Acc();
            void Cross(float hw2, float hh, Color col)
            {
                AddQuad(a, Vector3.zero, new Vector3(hw2, 0, 0), new Vector3(0, hh, 0), col);
                AddQuad(a, Vector3.zero, new Vector3(0, 0, hw2), new Vector3(0, hh, 0), col);
            }
            // 1.4 x 2.4 cm — larger than a real drop on purpose: at the 3-5 m the
            // puddle is normally seen from, a physically sized drop is under two
            // pixels and the drip simply does not exist.
            Cross(0.0070f, 0.0120f, new Color(0f, 0f, 0f, 1f));            // the drop
            for (int k = 0; k < 5; k++)                                    // the splash
                Cross(0.0060f, 0.0060f,
                      new Color(1f, (k + 0.35f) / 5f, Hash3(k, 7, 0, 6607), 0f));
            return a.Build("Env_C_Drip");
        }

        /// <summary>A rat, ~20 cm of body and 20 cm of tail, built nose-down-Z-
        /// forward at the origin with the gait weights in its vertex colours
        /// (r tail, g leg, b leg phase). EnvCritter walks it along its Bezier.
        ///
        /// <para>RAT SOLID, ModBuild 140. Two things made this animal
        /// see-through, and only one of them was the winding bug documented over
        /// AddTube. The other is here: every tube was OPEN. The rump ended in a
        /// 4.8 cm hole, each ear in a 2.1 cm one, each foot in a 1.1 cm one, and
        /// an open end shows the inside of the far wall of the tube from any
        /// angle that can see into it. AddTube caps both ends now, and the body
        /// gains a shrunken ring at each end so those caps are a 1.8 cm rump
        /// button and a 4 mm nose tip rather than two blunt plates — a capped
        /// cylinder read as a sawn-off pipe from behind, which is not what the
        /// fix is for.</para>
        ///
        /// <para>NOT DONE, deliberately: eyes. At the 3-5 m this animal is ever
        /// seen from in a room whose brightest light is a candle, two 3 mm
        /// spheres are below a pixel. The silhouette — rump, arched back, snout,
        /// ears, tail — is what identifies it, and that is what the closure work
        /// above was spent on.</para>
        ///
        /// <para>RAT SKIN, ModBuild 143: the colour ALPHA is now the BARE-SKIN
        /// weight. It was constant 1 and nothing read it, which made it the one
        /// free channel on this mesh (r, g and b are the gait's tail and leg
        /// weights and the leg phase, and the shader would moonwalk without any
        /// of them). 0 is fur; 1 is the naked, scaly, pinkish-grey of a rat's
        /// tail, feet, ear rims and nose. It is authored per RING because that is
        /// the granularity AddTube colours at, and per ring is exactly right for
        /// this: where the fur stops on a rat is a station along a limb, not a
        /// patch on a surface. EnvCritter's frag() reads it as the mask between
        /// the coat and the skin — see "the coat" there for why a tail at the
        /// same value as the body was half of "sieht aus hätte sie keine
        /// Textur".</para></summary>
        private static Mesh RatMesh()
        {
            var a = new Acc();
            var fur = new Color(0f, 0f, 0f, 0f);

            // body + head as one tube: rump -> shoulders -> muzzle. The first and
            // last rings are the ROUNDING (see the summary): small radii set back
            // from the ends so the caps read as curvature, not as a cut.
            var bc = new[]
            {
                new Vector3(0, 0.042f, -0.062f),
                new Vector3(0, 0.044f, -0.045f), new Vector3(0, 0.048f, -0.010f),
                new Vector3(0, 0.052f,  0.028f), new Vector3(0, 0.054f,  0.066f),
                new Vector3(0, 0.052f,  0.100f), new Vector3(0, 0.050f,  0.126f),
                new Vector3(0, 0.046f,  0.156f), new Vector3(0, 0.040f,  0.182f),
                new Vector3(0, 0.038f,  0.191f),
            };
            var br = new[] { 0.009f, 0.024f, 0.036f, 0.042f, 0.041f, 0.034f, 0.027f, 0.018f, 0.006f, 0.002f };
            // ...furred all the way to the muzzle, where the last two rings are
            // the bare nose. (Ring 7 is the snout at 1.8 cm, 8 and 9 the 6 mm and
            // 2 mm tip: the fur has to stop somewhere and it stops there.)
            var nose1 = new Color(0f, 0f, 0f, 0.45f);
            var nose2 = new Color(0f, 0f, 0f, 0.90f);
            var bcol = new[] { fur, fur, fur, fur, fur, fur, fur, fur, nose1, nose2 };
            var balong = new[] { 0f, 0.06f, 0.17f, 0.32f, 0.48f, 0.63f, 0.76f, 0.88f, 0.97f, 1f };
            AddTube(a, bc, br, bcol, balong, 8);

            // the tail: trails back, lifts, and tapers to a whip
            var tc = new[]
            {
                new Vector3(0, 0.044f, -0.048f), new Vector3(0, 0.048f, -0.090f),
                new Vector3(0, 0.052f, -0.135f), new Vector3(0, 0.048f, -0.180f),
                new Vector3(0, 0.038f, -0.220f), new Vector3(0, 0.026f, -0.252f),
            };
            var tr = new[] { 0.012f, 0.010f, 0.0082f, 0.0062f, 0.0040f, 0.0018f };
            var tcol = new Color[tc.Length];
            var talong = new float[tc.Length];
            for (int i = 0; i < tc.Length; i++)
            {
                float f = i / (float)(tc.Length - 1);
                // r = tail weight (the whip), a = bare skin: a rat's tail is
                // furred for the first centimetre or two out of the rump and
                // naked and scaly for the other 22 cm.
                tcol[i] = new Color(Mathf.SmoothStep(0f, 1f, f), 0f, 0f,
                                    Mathf.Lerp(0.30f, 1f, Mathf.Clamp01(f * 2.2f)));
                talong[i] = f;
            }
            AddTube(a, tc, tr, tcol, talong, 5);

            // four legs, two alternating phases (b = phase)
            void Leg(float x, float z, float phase)
            {
                var lc = new[]
                {
                    new Vector3(x, 0.046f, z),
                    new Vector3(x * 1.25f, 0.024f, z + 0.006f),
                    new Vector3(x * 1.35f, 0.004f, z + 0.014f),
                };
                var lr = new[] { 0.0105f, 0.0075f, 0.0055f };
                // g = leg weight, b = phase, a = bare skin: furred at the
                // shoulder, bare at the foot.
                var lcol = new[] { new Color(0f, 0.5f, phase, 0f), new Color(0f, 1f, phase, 0.45f),
                                   new Color(0f, 1f, phase, 0.80f) };
                AddTube(a, lc, lr, lcol, new[] { 0f, 0.5f, 1f }, 4);
            }
            Leg(0.026f, 0.098f, 0.0f); Leg(-0.026f, 0.098f, 0.5f);   // fore
            Leg(0.028f, -0.012f, 0.5f); Leg(-0.028f, -0.012f, 0.0f); // hind

            // Ears: SHORT FAT TUBES, not quads. A quad ear is a flat plate that
            // catches the light as a hard rectangle from one side and disappears
            // from the other — at 2 m it reads as a piece of geometry stuck to
            // the animal, which is worse than no ear at all.
            void Ear(float sx)
            {
                var ec = new[]
                {
                    new Vector3(sx * 0.019f, 0.068f, 0.114f),
                    new Vector3(sx * 0.028f, 0.076f, 0.113f),
                };
                // ...and thinly furred: an ear is skin with a fuzz on it, which is
                // why it catches the light differently from the head it sits on.
                var ear = new Color(0f, 0f, 0f, 0.60f);
                AddTube(a, ec, new[] { 0.0125f, 0.0105f }, new[] { ear, ear }, new[] { 0f, 1f }, 6);
            }
            Ear(1f); Ear(-1f);

            // Prove the claim the shader's Cull Back depends on, rather than
            // asserting it: every edge of a closed, consistently wound surface is
            // used by exactly two triangles in opposite directions. (Tubes here
            // interpenetrate rather than share vertices, so this is per-part
            // closure, which is what opaque depth testing needs.)
            AssertClosedAndOutward(a, "Rat");
            return a.Build("Env_C_Rat");
        }

        /// <summary>Fail the build if an accumulator's triangles are not a set of
        /// closed, outward-facing shells. Three checks, ALL of which the rat
        /// failed before ModBuild 140. That is not a guess: put the old winding
        /// and the open ends back and this bake stops with "82 unpaired edge(s),
        /// signed volume -770 cm^3, 282/282 triangles wound against their own
        /// normal" — 82 being exactly the nine open tube mouths, and the minus
        /// sign being the whole of the user's ModBuild 139 report:
        /// <list type="number">
        /// <item>EVERY directed edge (i,j) has exactly one partner (j,i). An open
        /// end leaves unpaired edges — that is the rump hole; a mirrored part
        /// leaves duplicated ones. Edges are keyed on WELDED POSITIONS, not on
        /// indices: a tube's seam and its cap rim are deliberately duplicated
        /// vertices (they carry different uv and normals) sitting on the same
        /// point, and an index-keyed test would call every seam a hole.</item>
        /// <item>The signed volume of the whole index set is POSITIVE. Flip the
        /// winding of a closed shell and this goes negative — it is the one test
        /// that tells inward from outward without a camera, and it is what
        /// "prove which way the faces point rather than assuming" means when you
        /// cannot render a frame.</item>
        /// <item>NO triangle disagrees with the normal its own vertices carry.
        /// The volume test is a sum and can average one flipped part away; this
        /// one is per-triangle and cannot.</item>
        /// </list></summary>
        private static void AssertClosedAndOutward(Acc a, string what)
        {
            var weld = new Dictionary<(long, long, long), int>();
            var id = new int[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                var p = a.V[i];
                var key = ((long)Mathf.RoundToInt(p.x * 1e5f),
                           (long)Mathf.RoundToInt(p.y * 1e5f),
                           (long)Mathf.RoundToInt(p.z * 1e5f));
                if (!weld.TryGetValue(key, out int w)) { w = weld.Count; weld[key] = w; }
                id[i] = w;
            }
            var edge = new Dictionary<long, int>();
            long Key(int i, int j) => (long)i * 1000003L + j;
            for (int k = 0; k < a.T.Count; k += 3)
                for (int e = 0; e < 3; e++)
                {
                    int i = id[a.T[k + e]], j = id[a.T[k + (e + 1) % 3]];
                    if (i == j) continue;                       // degenerate sliver
                    long back = Key(j, i);
                    if (edge.TryGetValue(back, out int cnt) && cnt > 0) edge[back] = cnt - 1;
                    else { edge.TryGetValue(Key(i, j), out int c2); edge[Key(i, j)] = c2 + 1; }
                }
            int open = edge.Values.Sum();
            double vol = 0;
            for (int k = 0; k < a.T.Count; k += 3)
            {
                Vector3 p = a.V[a.T[k]], q = a.V[a.T[k + 1]], r = a.V[a.T[k + 2]];
                vol += Vector3.Dot(p, Vector3.Cross(q, r)) / 6.0;
            }
            // dot(n, N) over the surface: how many triangles agree with the normal
            // they were authored with. A single disagreement is a winding slip in
            // one part, which the volume test can average away.
            int wrong = 0;
            for (int k = 0; k < a.T.Count; k += 3)
            {
                Vector3 p = a.V[a.T[k]], q = a.V[a.T[k + 1]], r = a.V[a.T[k + 2]];
                Vector3 fn = Vector3.Cross(q - p, r - p);
                Vector3 vn = a.N[a.T[k]] + a.N[a.T[k + 1]] + a.N[a.T[k + 2]];
                if (Vector3.Dot(fn, vn) < 0f) wrong++;
            }
            int tris = a.T.Count / 3;
            if (open != 0 || vol <= 0 || wrong > 0)
                throw new Exception($"{what} mesh is not a closed outward solid: {open} unpaired edge(s), "
                                    + $"signed volume {vol * 1e6:F0} cm^3, {wrong}/{tris} triangles wound "
                                    + "against their own normal. An inward-wound or open critter is "
                                    + "invisible from the side you look at it from (ModBuild 139).");
            Debug.Log($"[GloomhavenVR][Env] {what} mesh CLOSED and OUTWARD: {a.Count} verts, {tris} tris, "
                      + $"0 unpaired edges, signed volume +{vol * 1e6:F1} cm^3, "
                      + "0 triangles disagreeing with their vertex normals.");
        }

        /// <summary>One crossing's Bezier, with the two middle control points
        /// displaced. Identical to EnvCritter's Bez().</summary>
        private static Vector3 RatBez(float u, Vector3 d1, Vector3 d2)
        {
            float k = 1f - u;
            return k * k * k * RatW0 + 3f * k * k * u * (RatW1 + d1)
                 + 3f * k * u * u * (RatW2 + d2) + u * u * u * RatW3;
        }

        /// <summary>The wander of slot `n`, on the same hash channels the shader
        /// reads (7,8 for P1 and 9,10 for P2).</summary>
        private static void RatWander(int n, out Vector3 d1, out Vector3 d2)
        {
            d1 = new Vector3(RatWob1.z + RatWob1.x * (2f * RatH(n, 7) - 1f), 0f,
                             RatWob1.w + RatWob1.y * (2f * RatH(n, 8) - 1f));
            d2 = new Vector3(RatWob2.z + RatWob2.x * (2f * RatH(n, 9) - 1f), 0f,
                             RatWob2.w + RatWob2.y * (2f * RatH(n, 10) - 1f));
        }

        /// <summary>Everywhere the rat can EVER be at curve parameter u, as an
        /// axis-aligned rectangle in the floor plane.
        ///
        /// <para>This is not a bound, it is the exact set, and the reason is
        /// worth one line: the Bezier is affine in P1 and P2, the two
        /// coefficients 3k^2u and 3ku^2 are non-negative, and both wander
        /// rectangles are axis-aligned — so the reachable set is the (scaled)
        /// Minkowski sum of two axis-aligned rectangles, which is an
        /// axis-aligned rectangle. Distance from a point to it is then exact and
        /// costs four subtractions, which is what makes it affordable to test
        /// every barrel vertex against the whole family of routes.</para></summary>
        private static void RatBox(float u, out Vector3 ctr, out Vector2 half)
        {
            float k = 1f - u, c1 = 3f * k * k * u, c2 = 3f * k * u * u;
            ctr = RatBez(u, Vector3.zero, Vector3.zero)
                + new Vector3(c1 * RatWob1.z + c2 * RatWob2.z, 0f, c1 * RatWob1.w + c2 * RatWob2.w);
            half = new Vector2(c1 * RatWob1.x + c2 * RatWob2.x, c1 * RatWob1.y + c2 * RatWob2.y);
        }

        /// <summary>Arc length of one route up to curve parameter `to`. Static
        /// rather than a local of the assert because the burrow needs it too: the
        /// stride the animal walks down a hole is the stride it walks on the
        /// floor, scaled by how far the hole is against how far the route is, and
        /// a hand-typed strides-per-burrow would be a second gait.</summary>
        private static float RatArc(Vector3 d1, Vector3 d2, float to)
        {
            float len = 0f; var prev = RatBez(0f, d1, d2);
            for (int i = 1; i <= 200; i++)
            {
                var p = RatBez(to * i / 200f, d1, d2);
                len += Vector3.Distance(p, prev); prev = p;
            }
            return len;
        }

        private static float RatBoxDist(Vector2 p, Vector3 ctr, Vector2 half)
        {
            float dx = Mathf.Max(Mathf.Abs(p.x - ctr.x) - half.x, 0f);
            float dz = Mathf.Max(Mathf.Abs(p.y - ctr.z) - half.y, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Prove the whole route family is legal, and then PRINT it, so
        /// that a reader with the log and no Unity can check the rat: how many
        /// routes there are and how long they are, how the intervals and speeds
        /// spread, what fraction of crossings are reversed / turn back / stop to
        /// sniff, and that all of it is a pure function of the shared clock.
        ///
        /// <para>Three build gates, in the order they have historically been
        /// broken: the 6.5 m PLAY SPACE (the rat is exempt from
        /// AssertPlaySpaceClear — its mesh sits at the origin and the shader
        /// moves it, so no vertex walk can see where it goes); the WALLS; and the
        /// BARRELS, whose clearance is measured against their real vertices below
        /// rat height rather than against a guessed radius. The CRATES are
        /// deliberately not gated: the route ends underneath them, which is the
        /// authored ending ("...before it disappears under the crates").</para></summary>
        private static void AssertRatSchedule(Transform root, Vector3 beam, Vector3 beamDir)
        {
            const int US = 2000, SLOTS = 3000;
            float lim = CellarPlaySpaceDia * 0.5f;
            const float RatHalfWidth = 0.06f;   // body radius plus the spine wave

            // ---- gate 1+2: the reachable set against the disc and the walls
            float nearCentre = float.MaxValue, atU = 0f;
            float xLo = float.MaxValue, xHi = -float.MaxValue, zLo = float.MaxValue, zHi = -float.MaxValue;
            float spread = 0f;
            for (int i = 0; i <= US; i++)
            {
                float u = i / (float)US;
                RatBox(u, out var ctr, out var half);
                float d = RatBoxDist(Vector2.zero, ctr, half);
                if (d < nearCentre) { nearCentre = d; atU = u; }
                xLo = Mathf.Min(xLo, ctr.x - half.x); xHi = Mathf.Max(xHi, ctr.x + half.x);
                zLo = Mathf.Min(zLo, ctr.z - half.y); zHi = Mathf.Max(zHi, ctr.z + half.y);
                spread = Mathf.Max(spread, 2f * half.magnitude);
            }
            if (nearCentre - RatHalfWidth < lim)
                throw new Exception($"Rat route family reaches {nearCentre - RatHalfWidth:F2} m from the room "
                                    + $"centre at u={atU:F2} — inside the {CellarPlaySpaceDia:F1} m PlaySpace. "
                                    + "Shrink _Wob1/_Wob2 or move a control point outward.");
            float wallX = CW / 2f - Mathf.Max(Mathf.Abs(xLo), Mathf.Abs(xHi));
            float wallZ = CD / 2f - Mathf.Max(Mathf.Abs(zLo), Mathf.Abs(zHi));
            if (wallX < 0.10f)
                throw new Exception($"Rat route family comes within {wallX:F2} m of a side wall.");

            // ---- gate 3: the barrels, against their own geometry
            string worstProp = "-"; float worstGap = float.MaxValue;
            var propGaps = new List<string>();
            foreach (var nm in new[] { "Barrel0", "Barrel1", "Barrel2", "Bucket" })
            {
                var go = root.Find(nm)?.gameObject;
                if (go == null) continue;
                float gap = float.MaxValue, atV = 0f, atW = 0f, atRu = 0f;
                foreach (var p in WorldVerts(go))
                {
                    if (p.y > 0.25f) continue;          // only what the rat can hit
                    var p2 = new Vector2(p.x, p.z);
                    for (int i = 0; i <= 400; i++)
                    {
                        RatBox(i / 400f, out var ctr, out var half);
                        float d = RatBoxDist(p2, ctr, half);
                        if (d < gap) { gap = d; atV = p.x; atW = p.z; atRu = i / 400f; }
                    }
                }
                gap -= RatHalfWidth;
                propGaps.Add($"{nm} {gap:F2} m (its ({atV:F2},{atW:F2}) against u={atRu:F2})");
                if (gap < worstGap) { worstGap = gap; worstProp = nm; }
            }
            string propLine = string.Join(", ", propGaps);
            if (worstGap < 0.05f)
                throw new Exception($"Rat route family passes {worstGap:F2} m from '{worstProp}' — it would walk "
                                    + $"through it. Clearances: {propLine}. Restrain _Wob2 (it is the control "
                                    + "point whose influence peaks among the barrels), or bias it away.");

            // ---- the schedule itself, measured over SLOTS slots of the clock
            int runs = 0, revs = 0, turns = 0, sniffs = 0, stares = 0;
            var distinct = new HashSet<(int, int, int, int)>();   // routes to the nearest cm
            float lenLo = float.MaxValue, lenHi = 0f, spdLo = float.MaxValue, spdHi = 0f;
            float durLo = float.MaxValue, durHi = 0f, endLatest = 0f;
            float beamLo = float.MaxValue, beamHi = 0f, beamCross = 0f;
            float lastStart = float.NaN; var gaps = new List<float>();
            for (int n = 0; n < SLOTS; n++)
            {
                if (RatH(n, 0) < RatSkip) continue;
                RatWander(n, out var d1, out var d2);
                distinct.Add((Mathf.RoundToInt(d1.x * 100f), Mathf.RoundToInt(d1.z * 100f),
                              Mathf.RoundToInt(d2.x * 100f), Mathf.RoundToInt(d2.z * 100f)));
                bool turn = RatH(n, 4) < RatModes.y;
                float peak = turn ? RatPeak.x + RatPeak.y * RatH(n, 5) : 1f;
                float travel = turn ? 2f * peak : 1f;
                float runT = RatRunTime * travel * (RatTiming.z + RatTiming.w * RatH(n, 2));
                float start = RatPeriod * (RatTiming.x + RatTiming.y * RatH(n, 1));
                float t0 = n * RatPeriod + start;
                if (!float.IsNaN(lastStart)) gaps.Add(t0 - lastStart);
                lastStart = t0;
                // ...plus the burrow at BOTH ends: the animal is walking out of a
                // hole for RatBurrowTime before the run and into one for
                // RatBurrowTime after it, and both of those have to fit inside
                // the slot for the same reason the run does.
                endLatest = Mathf.Max(endLatest, start + runT + RatBurrowTime);
                if (start < RatBurrowTime)
                    throw new Exception($"Slot {n}'s crossing starts {start:F2} s in, which is less than the "
                                        + $"{RatBurrowTime:F2} s the animal spends coming out of the hole: the "
                                        + "emergence would reach back into the previous slot and two rats would "
                                        + "be out at once. Raise _Timing.x.");

                float len = RatArc(d1, d2, peak) * (turn ? 2f : 1f);
                lenLo = Mathf.Min(lenLo, len); lenHi = Mathf.Max(lenHi, len);
                spdLo = Mathf.Min(spdLo, len / runT); spdHi = Mathf.Max(spdHi, len / runT);
                durLo = Mathf.Min(durLo, runT); durHi = Mathf.Max(durHi, runT);

                float near = float.MaxValue;
                for (int i = 0; i <= 120; i++)
                {
                    var rel = RatBez(peak * i / 120f, d1, d2) - beam;
                    near = Mathf.Min(near, (rel - beamDir * Vector3.Dot(rel, beamDir)).magnitude);
                }
                beamLo = Mathf.Min(beamLo, near); beamHi = Mathf.Max(beamHi, near);
                if (near < 0.80f) beamCross++;      // 0.80 = the material's _ShaftR

                runs++;
                if (RatH(n, 3) < RatModes.x) revs++;
                if (turn) turns++;
                else if (RatH(n, 6) < RatModes.z) sniffs++;
                // HAUNT — the stare, measured on the same three gates the shader
                // applies: a straight crossing, channel 13 under RatStareChance,
                // and a haunt slot the schedule left QUIET (at the shipped dial),
                // which is what makes a stare and a drawn easter egg mutually
                // exclusive rather than merely unlikely to coincide.
                if (!turn && RatH(n, 13) < RatStareChance
                    && HauntH(Mathf.Floor(t0 / HauntPeriod), HcRate) >= HauntFreqDefault)
                    stares++;
            }
            // A run that overran its slot would be cut off mid-floor when sIn
            // wraps — the one way this scheme can produce a rat that vanishes in
            // the open, so it is a gate and not a note.
            if (endLatest > RatPeriod)
                throw new Exception($"A crossing can still be running {endLatest:F1} s into a {RatPeriod:F0} s "
                                    + "slot: it would be cut off in the open. Lower _Timing.x/.y or _RunTime.");
            gaps.Sort();

            // ---- gate 4: THE BURROW. "It goes in" as an inequality, per hole.
            // hides is the travel at which the tail TIP is level with the pocket's
            // cap; anything past that is the margin. If this were ever negative
            // the animal would come to a stop with its hindquarters hanging out of
            // the wall and stay there for twenty seconds — which is a far worse
            // bug than the shrink it replaces, and is why it is a throw.
            var burLines = new List<string>();
            for (int h = 0; h < 2; h++)
            {
                RatBurrow(h, out _, out var B, out float travel, out float gap, out float hides);
                var g = RatHoles[h];
                float margin = travel - hides;
                if (margin < 0.02f)
                    throw new Exception($"Hole {h}: the burrow is {travel * 100f:F1} cm long but the animal's "
                                        + $"tail tip is only clear of the pocket cap after {hides * 100f:F1} cm. "
                                        + "It would park with its back half sticking out of the wall. Raise "
                                        + "RatBurrowClear, or deepen the pocket.");
                // The route's endpoint must be IN FRONT of the wall, or `gap` is
                // negative and the animal starts the run already inside the stone.
                if (gap <= 0.01f)
                    throw new Exception($"Hole {h}: the route's endpoint is {gap * 100f:F1} cm from the wall "
                                        + "plane along the bore — move RatW0/RatW3 back into the room.");
                // ...and the drop must do nothing where it can be seen: b^6 of the
                // full drop at the moment the animal's NOSE reaches the cap, which
                // is the last instant at which any of it is deep in the pocket and
                // still lit. (At b^4 this came out at 13 mm and the gate below
                // stopped the bake — which is what it is for.)
                float bHide = hides / travel;
                float sagAtCap = RatBurrowDrop * Mathf.Pow((gap + g.depth) / travel, 6f);
                if (sagAtCap > 0.008f)
                    throw new Exception($"Hole {h}: the burrow has already dropped {sagAtCap * 1000f:F0} mm by "
                                        + "the pocket's cap — the animal would visibly sink through the pocket "
                                        + "floor. Lower RatBurrowDrop or lengthen the travel.");
                float vBur = travel / RatBurrowTime;
                burLines.Add(
                    $"hole {h} ({(h == 0 ? "N" : "S")}): bore "
                    + $"{Vector3.Angle(g.bore, -CellarWalls()[h].into):F0} deg "
                    + $"off the wall normal, mouth {g.mouthW * 100f:F1}x{g.mouthH * 100f:F1} cm, pocket "
                    + $"{g.depth * 100f:F1} cm; the route's end stands {gap * 100f:F1} cm out from the wall, "
                    + $"so the animal travels {travel * 100f:F1} cm in {RatBurrowTime:F2} s ({vBur:F2} m/s) and "
                    + $"is COMPLETELY hidden after {hides * 100f:F1} cm (b={bHide:F2}, {bHide * RatBurrowTime:F2} s "
                    + $"in), with {margin * 100f:F1} cm of margin; sideways bend "
                    + $"{new Vector3(B.x, B.y, B.z).magnitude * 100f:F1} cm, "
                    + $"sag at the cap {sagAtCap * 1000f:F1} mm");
            }

            float spineArc = RatArc(Vector3.zero, Vector3.zero, 1f);
            float burStride = RatBurrowStride();
            Debug.Log(
                $"[GloomhavenVR][Env] Cellar rat — a SCHEDULE, not a loop (user: \"mehr random statt immer "
                + $"den selben weg\"). Everything below is H(slot,k) of the SHARED clock alone "
                + $"(_Time.y+_GhvrTimeOfs, slot = floor(t/{RatPeriod:F0} s)): no Random, no per-client state, "
                + "no sin() in the hash, so two clients compute the same bits.\n"
                + $"  ROUTES: a continuum, not a list — P1 wanders +-{RatWob1.x:F2}/{RatWob1.y:F2} m about "
                + $"({RatWob1.z:F2},{RatWob1.w:F2}), P2 +-{RatWob2.x:F2}/{RatWob2.y:F2} m about "
                + $"({RatWob2.z:F2},{RatWob2.w:F2}); up to {spread:F2} m of lateral spread, arc length "
                + $"{lenLo:F2}..{lenHi:F2} m (the single old route was 9.90 m). {distinct.Count} of the "
                + $"{runs} crossings below take a route no other one takes, measured to the nearest cm.\n"
                + $"  MODES over {SLOTS} slots: {runs} crossings ({100f * (SLOTS - runs) / SLOTS:F0}% of slots "
                + $"quiet), {100f * revs / runs:F0}% out of the south hole instead of the north, "
                + $"{100f * turns / runs:F0}% turn back into the hole they came from at u="
                + $"{RatPeak.x:F2}..{RatPeak.x + RatPeak.y:F2}, {100f * sniffs / runs:F0}% of all crossings "
                + $"(= {100f * RatModes.z:F0}% of the straight ones) stop to sniff.\n"
                + $"  TIMING: gap between crossings {gaps[0]:F0}..{gaps[gaps.Count - 1]:F0} s "
                + $"(median {gaps[gaps.Count / 2]:F0} s); each lasts {durLo:F1}..{durHi:F1} s at "
                + $"{spdLo:F2}..{spdHi:F2} m/s; latest a run can still be going, burrow included, is "
                + $"{endLatest:F1} s of the {RatPeriod:F0} s slot.\n"
                + $"  THE BURROW (user: \"geht sie auch nicht durch das Loch sondern wird kleiner und "
                + $"verschwindet dann\"). THERE IS NO SCALE TERM LEFT: the animal is at 1:1 at every instant "
                + $"of every slot, including the {100f * (SLOTS - runs) / SLOTS:F0}% of slots in which it "
                + $"never comes out, and what hides it is stone. It walks {burLines[0]}; {burLines[1]}. "
                + $"Between crossings it stands at the far end of that travel — past the pocket's cap, "
                + $"inside the wall and {RatBurrowDrop * 100f:F0} cm under it — and the pocket is a blind "
                + $"sock (sleeve, floor and cap, every face of it proven to point at the room), so nothing "
                + $"in the room has a line to it. Down the hole it keeps walking: {burStride:F2} strides per "
                + $"travel, the same {RatStride / spineArc:F2} strides/m the floor gets.\n"
                + $"  CLEARANCE, over the WHOLE family and not just one route: play-space "
                + $"{nearCentre - RatHalfWidth:F2} m (needs >= {lim:F2}); side walls {wallX:F2} m "
                + $"(the {wallZ:F2} m in z is the two HOLES, which are in the wall on purpose); "
                + $"props {propLine} (the crates are exempt — it ends "
                + $"under them on purpose). Moon-beam axis {beamLo:F2}..{beamHi:F2} m, "
                + $"{100f * beamCross / runs:F0}% of crossings inside the 0.80 m the beam lights.\n"
                + $"  HAUNT — THE STARE: {stares} of the {runs} crossings ({100f * stares / runs:F0}%, about "
                + $"one in {runs / Mathf.Max(stares, 1)}) stop mid-floor and turn the head toward the ROOM "
                + $"CENTRE for ~1 s — never toward the camera, which would be the billboard behaviour this "
                + $"project has ruled out and would point somewhere else on every client. Gated on the haunt "
                + $"master AND on a QUIET haunt slot, so it can never land on top of one of the six drawn "
                + $"events; at the shipped dial ({HauntFreqDefault:F2}) that costs it about half its rolls. "
                + $"Every {RatPeriod:F0} s slot, so roughly one stare every "
                + $"{SLOTS * RatPeriod / Mathf.Max(stares, 1) / 60f:F0} min.");
        }

        // ==================================================== HAUNT SOLID: FORMS
        // HAUNT SOLID — the ruling this whole section was rewritten for. USER
        // VERDICT, hardware, ModBuild 143, said seven times: "mach keine 2D
        // Fratzen, das sieht man, dass es 2D ist ... lieber wirklich eine
        // Horrorgestalt die einfach da steht ... generell keine 2D Pappaufsteller
        // ... lieber einen 3D Kopf und Silhouette die durch Fenster schaut".
        //
        // The apparitions were quads carrying a baked likeness. That fixed the
        // EMOJI problem and left the CARDBOARD one: in stereo, at 8-16 m, the two
        // eyes disagree about a flat card's depth in a way they never disagree
        // about a solid, and the player walks around the room while the card does
        // not. So every apparition became a MESH, and the human forms were
        // IMPORTED rather than modelled (haunt_figures_pipeline.py — deleted this
        // round; git history has the search, the CC0 licence and the rejections).
        //
        // ...AND THEY ARE ALL GONE, ModBuild 146. USER VERDICT, hardware, and
        // it is final after three rounds of work on them:
        //     "mir gefallen die Figuren und animationen gar nicht"
        //     "Entferne die alten 3D assets komplett - sollen komplett raus die
        //      haben mir garnicht gefallen."
        //
        // Five imported CC0 forms (head, bust, figure, strider, hanged), the
        // pipeline that decimated and posed them (haunt_figures_pipeline.py), the
        // five .obj files under Imported/Models and every card that placed one are
        // deleted from this bundle. Nothing here re-poses a body any more.
        //
        // WHAT REPLACES THEM, where anything does: the game's OWN monsters, spawned
        // and animated by the game's own Animator at runtime
        // (src/GloomhavenVR/Core/HauntFigures.*.cs). That lane already drives four
        // of the ten — the thing past the cellar window, the thing across the stair
        // doorway, the watcher between the trunks and the thing that crosses the
        // gap — so those four cards remain in the catalogue as HKindNone
        // SCHEDULE PLACEHOLDERS: they carry the slot, the beat and the envelope the
        // runtime and the reacting shaders read, and they draw nothing themselves.
        //
        // WHAT IS SIMPLY GONE, with nothing to take its place, because no game
        // monster can express it: the head that came out from behind a trunk, the
        // 3.05 m hunched mass, the head lying on the flagstones, and the body
        // HANGED BY THE NECK — the last of which the user asked for by name and
        // there is no hang animation in the roster to rebuild it from.

        // ================================================================ HAUNTS
        // USER REQUEST, 2026-08-14: "'Grusel-Easter-Eggs' in den Umgebungen. Also
        // grusilige Animationen (ohne sound) die ab und zu auftreten ... (nur Wald
        // und Keller) ... nicht aufdringlich, eher im Hintergrund aber einen
        // ordnelichen Gruselfaktor auslösen ... sollen niemals den Spielfluss
        // stören ... sollen sie synchron von allen Spielern an den selben Stellen
        // sichtbar sein. Weiterhin sollen sie sich auch mit den aktuellen
        // Elementen nicht im weg stehen oder deswegen ihren gruselfaktor
        // verlieren."
        //
        // The DRAWING lives in EnvHaunt.shader and the SCHEDULE in EnvHaunt.cginc;
        // read those two first. This half does four things the GPU cannot:
        //   1. it PLACES and POSES the solids, in metres, against the room's real
        //      geometry, and bakes each one's own key light into its vertices;
        //   2. it GATES them — the play space, the walls, the trunks, the slot
        //      budget, the group partition, and now closed-and-outward on every
        //      solid — so the bake fails rather than the headset;
        //   3. it MEASURES the schedule over thousands of slots and prints it;
        //   4. it prints the GEOMETRY — every apparition's form, triangle count,
        //      real size in metres and placement — so the whole feature can be
        //      checked from the log by somebody with no Unity.
        //
        // ============================================================
        // WHY THE THREE INVISIBLE ONES WERE INVISIBLE — measured, not guessed.
        //
        // USER: "Dunkle Masse sehe ich gar nichts", "Auch hängender Körper sehe
        // ich nichts", "Gesicht am Boden sehe ich gar nicht." Three of ten, and
        // the three have one cause.
        //
        // It was NOT a missing atlas tile: Env_Haunt.png was measured cell by cell
        // and all ten tiles carry coverage (the Loom tile is the densest in the
        // sheet at 0.52). It was the VALUE PLAN. Those three cards were authored
        // as "a hole in the scene" — coverage with almost no light, on the theory
        // that you would see them as the thing they blot out. Their peak key
        // values came to 0.018 (Loom), ~0.05 (Hang) and ~0.02 (Floor) linear.
        // That works where there is something behind them to remove, which is
        // exactly the three the user DID see: the bust at the window is a hole in
        // the moonlight, the face at the shelf is against a candle-lit wall, and
        // the figure on the stair crosses a lit doorway. The other three stand
        // against a night forest whose blacks are 0.002-0.005 and against an
        // unlit cellar corner that is 0.000 — a hole cut in nothing is nothing.
        //
        // THE FIX IS NOT "TURN THEM UP", it is the reason they are solids: a real
        // body with a real key direction has an internal value gradient — a lit
        // shoulder, a dark flank, a jaw edge — and that gradient reads against a
        // black background at a fraction of the brightness a flat shape needs.
        // On top of that all three were MOVED to where a background exists: the
        // hanged body from 15 m (behind four bands of trunks) to 9.4 m in a gap,
        // the mass from a black quarter to the moonlit one, and the face on the
        // floor out of the dead corner to the edge of the moonbeam's own pool.
        // ============================================================
        //
        // The constants below are the C# MIRROR of EnvHaunt.cginc. They are
        // duplicated for exactly the reason the rat's are (see RatH): the GPU
        // cannot report and the log cannot render, so this is what lets the bake
        // measure the schedule rather than describe it. Edit one, edit the other.
        private const float HauntPeriod = 83f;      // the slot beat, seconds
        private const float HauntStartLo = 0.15f;   // earliest start, fraction of a slot
        private const float HauntStartSpan = 0.40f;
        private const float HauntDurLo = 0.85f;     // per-slot duration scale
        private const float HauntDurSpan = 0.30f;
        private const int HauntGroups = 3;
        // The shipped dial (Defaults.HauntFrequency). Mirrored here ONLY so the
        // bake log can state the interval a fresh install actually gets; the
        // runtime never reads this file.
        private const float HauntFreqDefault = 0.50f;
        // Hash channels — the same numbers EnvHaunt.cginc uses.
        private const int HcRate = 0, HcPick = 1, HcStart = 3, HcDur = 4;

        // EnvHaunt kinds. Mirrored in the shader's KIND_* defines.
        private const int HKindSolid = 0;   // a placed body or head
        private const int HKindDecal = 1;   // a flat mark lying IN a real surface
        private const int HKindCross = 2;   // a solid that travels across the event
        private const int HKindNone = 3;    // no geometry; a schedule placeholder
        private const int HKindProp = 4;    // always drawn; the clock only tips it
        private const int HKindEyes = 5;    // a solid with a blink

        // ---- the events OTHER shaders react to ------------------------------
        // Three shaders answer a haunt they do not draw (EnvBeam dims, the cobwebs
        // shiver, the rat looks up), and each of them needs the card index and the
        // exact envelope of the event it is watching. Those numbers therefore live
        // HERE, once, and both the catalogue below and the material setup read
        // them — because a beam that dimmed for 7.6 s while the silhouette lasted
        // 8.0 s would be a bug nobody could see the cause of.
        // STILL SIX after ModBuild 146, and the count is not a coincidence: the
        // never-the-same-event-twice guarantee partitions the catalogue into
        // GHVR_HAUNT_GROUPS = 3 equal groups, so a room's card count has to be a
        // multiple of three or one slot in six indexes past the end of the array
        // and draws nothing. The head on the flagstones was retired with the rest
        // of the imported figures and its slot went to a NEW, figure-free event —
        // the door of light at the top of the stair. The catalogue is now
        // Window / Hands / Door / Tremble / Stair / Shelf, and the two cards after
        // the replaced one each moved down... nothing: only the NAME at index 2
        // changed, so Tremble and Stair kept their indices and only the SHELF is
        // where it always was. (The FOREST is the room whose count really fell —
        // 6 to 3 — which is why Haunt.EventCount can no longer be one shared
        // constant.)
        private const int HauntCellarCards = 6;
        private const int HauntCardWindow = 0;    // the slot the thing outside the window uses
        private const int HauntCardTremble = 3;   // the invisible card the webs answer
        private static readonly Vector4 HauntWindowEnv = new Vector4(3.2f, 2.6f, 1.8f, 0f);
        private static readonly Vector4 HauntTrembleEnv = new Vector4(0f, 1.1f, 0.9f, 0f);
        // THE BOOKSHELF'S OWN CARD. User, cellar 11: "Wie wär es wenn das
        // Bücherregal umkippt, und sich dann nach ner Zeit wieder von selbst
        // aufstellt." It is card 5 — the slot the face at the shelf used to hold,
        // because it is the same piece of furniture and the same group.
        private const int HauntCardShelf = 5;
        // reveal / hold / fade of the shelf event, and the whole of it is HOLD:
        // there is nothing to fade in or out, the shelf is always there. 26 s is
        // what the tip curve is authored against, and the curve lives in
        // EnvShelfTip.cginc (NOT in EnvHaunt.shader, which is where it used to
        // be). Its shipped schedule, in phase and in seconds, from that file's
        // own GHVR_TIP_* constants after commit e0c50ce:
        //   0.000-0.180   4.68 s   the topple, on the pendulum's exact separatrix
        //   0.180-0.204   0.62 s   the landing: one ballistic rebound of 1.9 deg
        //   0.204-0.620  10.80 s   lying on its face
        //   0.620-1.000   9.88 s   the same arc, run backwards at 0.537x
        // (This comment said "about 4.7 s of falling, 10 s lying, 9.9 s up"
        // against the PRE-separatrix curve; the numbers above are read off the
        // constants rather than remembered.)
        private static readonly Vector4 HauntShelfEnv = new Vector4(0.001f, 26f, 0.001f, 0f);
        // How much of the moonbeam the thing at the window takes away. 0.55, not
        // 1.0: it is leaning IN at an opening it does not fill, so more than half
        // the aperture is still open. A beam that went out completely would read
        // as a light switch and would also make the room unusable for a second.
        private const float HauntBeamDepth = 0.55f;
        // Cobweb tremble, in metres. 10 mm on a web whose draught sway is 21-70 mm:
        // the tremble is SMALLER than the breathing it interrupts, and reads only
        // because it is fifty times faster.
        private const float HauntWebTremble = 0.010f;
        // Chance a straight crossing stops and looks at the room. With the rat's
        // 15 % quiet slots and 30 % turn-backs, 0.16 lands it at roughly one
        // crossing in nine — see the stare line in the rat's own bake report.
        private const float RatStareChance = 0.16f;

        /// <summary>The haunt schedule's hash, character for character
        /// GhvrHauntH() in EnvHaunt.cginc — and character for character the rat's,
        /// which is the point: it is a cascade of multiply/add/frac on values
        /// under 200, every one of them a correctly-rounded IEEE-754 single
        /// operation on every GPU this mod runs on. No sin(), so two clients do
        /// not compute a similar schedule, they compute the same bits.</summary>
        private static float HauntH(float n, float k)
        {
            float x = Frac((n + 1f + k * 7.13f) * 0.7548776662f);
            x = Frac(x * (x + 31.70f));
            x = Frac(x * (x + 17.31f));
            return Frac(x * (x + 43.19f));
        }

        /// <summary>Which event slot `n` belongs to. THE GROUP PARTITION: event k
        /// is in group (k mod 3) and slot n may only draw from group (n mod 3), so
        /// two consecutive slots are two different events BY CONSTRUCTION — no
        /// history, no re-roll, no residual chance of a repeat. See the "NEVER THE
        /// SAME EVENT TWICE RUNNING" block in EnvHaunt.cginc for why the obvious
        /// rule (re-roll on a collision) is not available to a shader.</summary>
        private static int HauntCardOfSlot(int n, int cards)
        {
            int grp = n % HauntGroups;
            int inGroup = Mathf.Max(cards / HauntGroups, 1);
            int j = Mathf.Min(Mathf.FloorToInt(HauntH(n, HcPick) * inGroup), inGroup - 1);
            return grp + HauntGroups * j;
        }

        /// <summary>A clock offset at which event `card` is exactly `at01` of the
        /// way through its run — the preview harness's way of PHOTOGRAPHING an
        /// apparition that is otherwise on screen for eight seconds out of every
        /// three minutes.
        ///
        /// <para>This is deliberately NOT a "force it visible" debug flag. A debug
        /// constant would be a second code path that could drift from the shipped
        /// one, and the previews would then be pictures of the debug path. This
        /// instead SOLVES the shipped schedule for a time at which the thing is
        /// really happening — the same arithmetic the GPU does, run backwards — so
        /// the frame is a photograph of the real thing at a real instant.
        /// The harness still has to publish _GhvrHaunt = (1, 1, 0, 0) so that the
        /// slot is not gated out, which is the shipped behaviour at dial 1.0.</para>
        ///
        /// <para>`durMul` is the per-slot duration jitter of the slot that is
        /// found, so `at01` really is a fraction of THAT run and not of the
        /// authored length.</para></summary>
        public static float HauntPreviewClock(int card, int cards, float reveal, float hold,
                                              float fade, float at01)
        {
            for (int n = 0; n < 4000; n++)
            {
                if (HauntCardOfSlot(n, cards) != card) continue;
                float start = HauntPeriod * (HauntStartLo + HauntStartSpan * HauntH(n, HcStart));
                float mul = HauntDurLo + HauntDurSpan * HauntH(n, HcDur);
                return n * HauntPeriod + start + (reveal + hold + fade) * mul * at01;
            }
            throw new Exception($"No slot in the first 4000 draws haunt card {card} of {cards}.");
        }

        /// <summary>One apparition: which form it is, where it stands, how it is
        /// lit, and how it moves. Everything here is METRES and ROOM SPACE.</summary>
        private struct HauntCard
        {
            public string name;      // for the log only
            public int kind;         // HKind*
            public Vector3 at;       // THE ANCHOR: what it collapses to when idle,
                                     // and the point the form's own origin lands on
            public Vector3 facing;   // unit, must point roughly at the room centre
            public float height;     // the form is scaled to exactly this, metres
            public float wide;       // lateral scale, relative to the height scale
            public float yaw;        // extra yaw about the anchor, degrees
            public Vector3 move;     // slide / travel direction, unit
            public float moveAmt;    // metres of it
            public Vector3 rotAxis;  // one-shot rotation axis (unit)
            public float rotAngle;   // radians of it, over the event
            public float pivotY;     // that rotation's pivot, metres above the anchor
            public float shape;      // damped sway amplitude (rad), or EYES blink lag
            public float reveal, hold, fade;  // the envelope, seconds
            public Color key;        // the KEY light's colour...
            public Vector3 keyDir;   // ...and the direction it comes FROM (unit)
            public float fillAmt;    // how much of the ROOM's fill light it takes
            public float opacity;    // base alpha
            public float rim;        // rim multiplier (the Dark-element compensation)
            public string why;       // one line of placement rationale, logged
        }

        /// <summary>The accumulator the haunt mesh is welded in. It carries the
        /// eight vertex channels EnvHaunt's appdata declares and not one more:
        /// POSITION, NORMAL, TANGENT, COLOR and UV0..UV3 is exactly the ceiling,
        /// and the round that used a ninth did not fail loudly — the streams
        /// aliased and every apparition in both rooms came out with a bright red
        /// outline.</summary>
        private class HauntAcc
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector4> Tan = new List<Vector4>();
            public readonly List<Vector4> UV0 = new List<Vector4>();
            public readonly List<Vector4> UV1 = new List<Vector4>();
            public readonly List<Vector4> UV2 = new List<Vector4>();
            public readonly List<Vector4> UV3 = new List<Vector4>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public Mesh Build(string name, Bounds bounds)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetTangents(Tan);
                m.SetUVs(0, UV0);
                m.SetUVs(1, UV1);
                m.SetUVs(2, UV2);
                m.SetUVs(3, UV3);
                m.SetColors(C);
                m.SetTriangles(T, 0);
                m.bounds = bounds;
                return m;
            }
        }


        /// <summary>Weld a finished piece into the room's haunt mesh, BAKING ITS
        /// LIGHTING as it goes.
        ///
        /// <para>Diffuse shading is view-independent, so N·L evaluated here, once,
        /// per vertex, is not an approximation of evaluating it per fragment — it
        /// is the same number. What the fragment keeps is what has to be live: the
        /// envelope, the element compensation and the rim amount. See the
        /// "WHY THE LIGHTING IS BAKED" block in EnvHaunt.shader.</para>
        ///
        /// <para>THE RIM IS BAKED AGAINST THE ROOM CENTRE, not the camera. A rim
        /// is a view-dependent quantity everywhere else in this bundle; here it
        /// may not be, because a camera term in this shader is one edit away from
        /// a billboard and the permanent VR ruling forbids anything that
        /// re-orients with the head. The room centre is within a metre or so of
        /// where the head really is and it is world-fixed.</para></summary>
        private static void HauntWeld(HauntAcc h, Acc piece, HauntCard c, int index, int kind,
                                      Color keyCol, Color fillCol, Vector3 move, float moveAmt,
                                      Vector3 rotAxis, float rotAngle, float pivotY,
                                      float alphaMul, float rimMul, float atlasTile)
        {
            var facing = c.facing.normalized;
            var keyDir = c.keyDir.sqrMagnitude > 1e-6f ? c.keyDir.normalized : Vector3.up;
            int b0 = h.V.Count;
            for (int i = 0; i < piece.Count; i++)
            {
                var n = piece.N[i].normalized;
                // A WRAPPED lambert, not a bare one. These things are nearly
                // black; a hard terminator on a near-black solid puts half of it
                // at exactly zero and the silhouette then reads as a flat shape
                // again, which is the whole failure this round is undoing. 0.25 of
                // wrap keeps the unlit side above the room's own floor value
                // without lighting it. 0.12 and not 0.25: at a quarter the dark
                // flank was still a fifth of the key, which on a body the size of
                // the Watcher is a large evenly-lit shape, i.e. the flat read
                // again.
                float ndl = Mathf.Clamp01((Vector3.Dot(n, keyDir) + 0.12f) / 1.12f);
                float hemi = 0.5f + 0.5f * n.y;
                var col = keyCol * ndl + fillCol * (c.fillAmt * hemi);
                // the rim, as seen from the middle of the room
                float rim = Mathf.Pow(1f - Mathf.Abs(Vector3.Dot(n, facing)), 2.2f) * c.rim * rimMul;
                h.V.Add(piece.V[i]);
                h.N.Add(n);
                h.Tan.Add(new Vector4(c.at.x, c.at.y, c.at.z, pivotY));
                h.UV0.Add(new Vector4(index, kind, rim, atlasTile));
                h.UV1.Add(new Vector4(c.reveal, c.hold, c.fade, c.shape));
                h.UV2.Add(new Vector4(move.x, move.y, move.z, moveAmt));
                h.UV3.Add(new Vector4(rotAxis.x, rotAxis.y, rotAxis.z, rotAngle));
                h.C.Add(new Color(col.r, col.g, col.b,
                                  c.opacity * alphaMul * piece.C[i].a));
            }
            foreach (var idx in piece.T) h.T.Add(b0 + idx);
        }

        /// <summary>The DECAL variant of HauntWeld: a flat mark lying in a real
        /// surface, whose value comes out of the atlas in the card's own uv rather
        /// than out of a baked normal. The atlas survived this round for exactly
        /// one card — the handprints — because a print IS two-dimensional, it lies
        /// IN the wall plane, and its parallax there is correct. "Keine 2D
        /// Pappaufsteller" is about things that stand up in the air.</summary>
        private static void HauntWeldDecal(HauntAcc h, Acc piece, HauntCard c, int index,
                                           float atlasTile)
        {
            int b0 = h.V.Count;
            for (int i = 0; i < piece.Count; i++)
            {
                h.V.Add(piece.V[i]);
                h.N.Add(piece.N[i]);
                h.Tan.Add(new Vector4(c.at.x, c.at.y, c.at.z, 0f));
                h.UV0.Add(new Vector4(index, HKindDecal, c.rim, atlasTile));
                h.UV1.Add(new Vector4(c.reveal, c.hold, c.fade, c.shape));
                // the DECAL kind reads mv.xy as the atlas's tile-space uv — the
                // same lane a solid uses for its slide direction, because a mark
                // on a wall does not slide anywhere
                h.UV2.Add(new Vector4(piece.UV[i].x, piece.UV[i].y, 0f, 0f));
                h.UV3.Add(new Vector4(0f, 1f, 0f, 0f));
                h.C.Add(new Color(c.key.r, c.key.g, c.key.b, c.opacity * piece.C[i].a));
            }
            foreach (var idx in piece.T) h.T.Add(b0 + idx);
        }

        /// <summary>A flat quad lying in a wall, in the wall's own plane, carrying
        /// atlas tile-space uv. The handprints are this and nothing else.</summary>
        private static void AddHauntMark(Acc a, Vector3 centre, Vector3 right, Vector3 up)
        {
            var nrm = Vector3.Cross(up, right).normalized;
            int b = a.Count;
            a.Vert(centre - right - up, nrm, new Vector2(-1, -1), Color.white);
            a.Vert(centre + right - up, nrm, new Vector2(1, -1), Color.white);
            a.Vert(centre + right + up, nrm, new Vector2(1, 1), Color.white);
            a.Vert(centre - right + up, nrm, new Vector2(-1, 1), Color.white);
            // Acc.Quad's (b, b+2, b+1) order and NOT the AddFaceUV order: with
            // the corners emitted in loop order the naive winding faces the other
            // way, and a mark on a wall drawn with Cull Back and the wrong
            // winding is simply not there.
            a.Quad(b);
        }

        /// <summary>An ellipsoid — the two eyeshines, and the only apparition in
        /// either room whose whole body is one primitive. LatheMesh's poles are
        /// degenerate rings rather than fans, which AssertClosedAndOutward accepts
        /// as closed because it skips zero-length edges.</summary>
        private static void AddHauntBlob(Acc a, Vector3 centre, float rx, float ry, int segs)
        {
            int rings = 7;
            var prof = new Vector2[rings];
            for (int i = 0; i < rings; i++)
            {
                float t = Mathf.PI * i / (rings - 1);
                prof[i] = new Vector2(Mathf.Sin(t) * rx, -Mathf.Cos(t) * ry);
            }
            var m = LatheMesh(prof, segs);
            MergeInto(a, m, centre, Quaternion.identity, Vector3.one, Color.white);
            UnityEngine.Object.DestroyImmediate(m);
        }

        /// <summary>Prove the catalogue is legal BEFORE it is baked, and print
        /// what every apparition actually is.
        ///
        /// <para>Six gates, in the order they would hurt:
        /// <list type="number">
        /// <item>THE PLAY SPACE, measured on every real VERTEX and at both ends of
        /// whatever travel the card has. "Niemals den Spielfluss stören" is the
        /// requirement; this is where it is enforced. (The room-wide sweep,
        /// AssertPlaySpaceClear, sees the rest pose only — a card that leaves the
        /// play space clear standing still and enters it half way through its
        /// crossing would pass it.)</item>
        /// <item>THE WALLS / THE GROUND: nothing may hang outside the room or below
        /// the floor.</item>
        /// <item>CLOSED AND OUTWARD, per solid, done by the caller as each piece is
        /// built.</item>
        /// <item>THE GROUP PARTITION: a positive multiple of three.</item>
        /// <item>THE SLOT BUDGET: the latest possible start plus the longest
        /// possible duration (including the Ice stretch) must finish inside the
        /// slot.</item>
        /// <item>THE FACING: every card must look roughly at the room centre —
        /// because a form placed with its back to the room is a form nobody will
        /// ever see the face of, and because the rim is baked against that
        /// direction.</item>
        /// </list></para></summary>
        private static void AssertHauntCards(string room, HauntCard[] cards, HauntAcc h,
                                             int[] firstVert, float playDia, Vector3 centre,
                                             float xLim, float zLim, float yLim, string[] shapes)
        {
            if (cards.Length == 0 || cards.Length % HauntGroups != 0)
                throw new Exception($"{room} haunts: {cards.Length} cards is not a positive multiple of "
                                    + $"{HauntGroups}. The 'never the same event twice running' guarantee is "
                                    + "the slot-mod-3 group partition, and it needs equal, non-empty groups.");

            float rLim = playDia * 0.5f;
            float nearest = float.MaxValue; string nearestName = "-";
            float longest = 0f;
            var lines = new List<string>();
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                int v0 = firstVert[i], v1 = i + 1 < cards.Length ? firstVert[i + 1] : h.V.Count;
                float near = float.MaxValue;
                var lo = Vector3.positiveInfinity;
                var hi = Vector3.negativeInfinity;
                for (int v = v0; v < v1; v++)
                {
                    // both ends of the travel, so a crossing is gated where it
                    // really goes and not where it is authored
                    var mv = new Vector3(h.UV2[v].x, h.UV2[v].y, h.UV2[v].z) * h.UV2[v].w;
                    foreach (var p in new[] { h.V[v], h.V[v] + mv, h.V[v] - mv * 0.5f })
                    {
                        near = Mathf.Min(near, new Vector2(p.x - centre.x, p.z - centre.z).magnitude);
                        lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                        if (Mathf.Abs(p.x) > xLim || Mathf.Abs(p.z) > zLim || p.y < -0.30f || p.y > yLim)
                            throw new Exception($"{room} haunt '{c.name}' has a vertex at {p} — outside the "
                                                + $"room box (|x|<={xLim:F2}, |z|<={zLim:F2}, -0.30<=y<={yLim:F2}).");
                    }
                }
                if (v1 > v0)
                {
                    if (near < rLim)
                        throw new Exception($"{room} haunt '{c.name}' reaches {near:F2} m of the room centre — "
                                            + $"inside the {playDia:F1} m PlaySpace. The easter eggs may never "
                                            + "be over the board (user: \"niemals den Spielfluss stören\").");
                    if (near < nearest) { nearest = near; nearestName = c.name; }
                }

                var toCentre = new Vector3(centre.x - c.at.x, 0f, centre.z - c.at.z).normalized;
                float dot = Vector3.Dot(c.facing.normalized, toCentre);
                if (dot < 0.55f)
                    throw new Exception($"{room} haunt '{c.name}' faces {c.facing} but the room centre is at "
                                        + $"{toCentre} (dot {dot:F2} < 0.55). It would stand with its back to "
                                        + "the room, and its baked rim would be on the side nobody sees.");

                float dur = c.reveal + c.hold + c.fade;
                if (c.kind != HKindProp) longest = Mathf.Max(longest, dur);
                var size = v1 > v0 ? hi - lo : Vector3.zero;
                lines.Add($"[{i}] grp{i % HauntGroups} {c.name} kind {c.kind} {shapes[i]} at "
                          + $"({c.at.x:F2},{c.at.y:F2},{c.at.z:F2}) — {v1 - v0} verts, "
                          + $"bbox {size.x:F2}x{size.y:F2}x{size.z:F2} m, "
                          + $"{c.reveal:F2}+{c.hold:F2}+{c.fade:F2} = {dur:F2} s"
                          + (c.fade <= 0f ? " (INSTANT vanish)" : "")
                          + (v1 > v0 ? $", {near:F2} m out" : "")
                          + $" — {c.why}");
            }

            // Ice stretches the duration by up to 35 % on top of the per-slot
            // jitter; the budget has to hold in that worst case or a frozen room
            // is where the scheme breaks.
            float worstEnd = HauntPeriod * (HauntStartLo + HauntStartSpan)
                             + longest * (HauntDurLo + HauntDurSpan) * 1.35f;
            if (worstEnd > HauntPeriod - 5f)
                throw new Exception($"{room} haunts: the latest event can still be running {worstEnd:F1} s into "
                                    + $"a {HauntPeriod:F0} s slot — it would be cut off in the open. Shorten the "
                                    + "longest event, or narrow the start window.");

            Debug.Log($"[GloomhavenVR][Env] {room} HAUNTS — {cards.Length} events, ONE mesh, ONE material, ONE "
                      + $"draw call: {h.V.Count} verts, {h.T.Count / 3} tris in total, of which five of six are "
                      + "collapsed onto their own anchor at any instant and rasterise nothing.\n  "
                      + string.Join("\n  ", lines)
                      + $"\n  CLEARANCE: nearest apparition vertex to the room centre is '{nearestName}' at "
                      + $"{nearest:F2} m (PlaySpace radius {rLim:F2} m). SLOT BUDGET: worst end "
                      + $"{worstEnd:F1} s of {HauntPeriod:F0} s.");
        }

        /// <summary>Measure the schedule and print it — the rat's report, for the
        /// haunts. Everything here is computed from HauntH() alone, which is the
        /// C# mirror of what the GPU runs, so these are MEASUREMENTS of the shipped
        /// schedule and not a description of it.
        ///
        /// <para>Two properties are ASSERTED rather than reported, because they are
        /// the two the user actually asked for:
        /// <list type="number">
        /// <item>NO REPEAT: no two consecutive slots pick the same event.</item>
        /// <item>THE NESTING: a player on a lower frequency dial sees a strict
        /// SUBSET of what a player on a higher one sees, at the same seconds and
        /// the same places. That is what makes a per-client dial compatible with
        /// "synchron von allen Spielern an den selben Stellen sichtbar".</item>
        /// </list></para></summary>
        private static void ReportHauntSchedule(string room, HauntCard[] cards)
        {
            const int SLOTS = 6000;
            int n = cards.Length;

            // ---- gate: no two consecutive slots are the same event
            for (int s = 1; s < SLOTS; s++)
                if (HauntCardOfSlot(s, n) == HauntCardOfSlot(s - 1, n))
                    throw new Exception($"{room} haunts: slots {s - 1} and {s} both pick event "
                                        + $"{HauntCardOfSlot(s, n)}. The group partition is broken.");

            // ---- gate: the dial is a MONOTONE SUBSET selector, checked as the
            // set inclusion it has to be and not as an algebraic identity. A
            // player at 0.25 must fire on a strict subset of the slots a player at
            // 0.50 fires on, who must fire on a subset of 1.00's — same slot, same
            // event, same second, because the SCHEDULE is the same and only the
            // gate differs. This is the whole reason the dial may not touch the
            // hash (EnvHaunt.cginc, "THE TWO THINGS THAT ARE NOT ALLOWED TO DEPEND
            // ON A LOCAL SETTING").
            var atLo = new HashSet<int>();
            var atMid = new HashSet<int>();
            var atHi = new HashSet<int>();
            for (int s = 0; s < SLOTS; s++)
            {
                float hh = HauntH(s, HcRate);
                if (hh < 0.25f) atLo.Add(s);
                if (hh < HauntFreqDefault) atMid.Add(s);
                if (hh < 1.00f) atHi.Add(s);
            }
            if (!atLo.IsProperSubsetOf(atMid) || !atMid.IsProperSubsetOf(atHi))
                throw new Exception($"{room} haunts: the frequency dial is not a monotone subset selector "
                                    + $"({atLo.Count} / {atMid.Count} / {atHi.Count} slots at 0.25 / "
                                    + $"{HauntFreqDefault:F2} / 1.00). Two players on different settings would "
                                    + "then see DIFFERENT events, not merely fewer of the same ones.");

            string Measure(float freq, out int fired)
            {
                var perCard = new int[n];
                var gaps = new List<float>();
                float lastStart = float.NaN, lastEnd = float.NaN;
                float minQuiet = float.MaxValue;
                fired = 0;
                for (int s = 0; s < SLOTS; s++)
                {
                    if (HauntH(s, HcRate) >= freq) continue;
                    int c = HauntCardOfSlot(s, n);
                    float start = s * HauntPeriod
                                  + HauntPeriod * (HauntStartLo + HauntStartSpan * HauntH(s, HcStart));
                    float dur = (cards[c].reveal + cards[c].hold + cards[c].fade)
                                * (HauntDurLo + HauntDurSpan * HauntH(s, HcDur));
                    if (!float.IsNaN(lastStart))
                    {
                        gaps.Add(start - lastStart);
                        minQuiet = Mathf.Min(minQuiet, start - lastEnd);
                    }
                    lastStart = start; lastEnd = start + dur;
                    perCard[c]++; fired++;
                }
                gaps.Sort();
                var mix = new List<string>();
                for (int i = 0; i < n; i++)
                    mix.Add($"{cards[i].name} {100f * perCard[i] / Mathf.Max(fired, 1):F1}%");
                return $"{fired} events in {SLOTS} slots ({100f * fired / SLOTS:F0}% of slots), "
                       + $"gap {gaps[0] / 60f:F1}..{gaps[gaps.Count - 1] / 60f:F1} min "
                       + $"(median {gaps[gaps.Count / 2] / 60f:F1} min), shortest QUIET stretch between "
                       + $"the end of one and the start of the next {minQuiet:F0} s; mix "
                       + string.Join(" / ", mix);
            }

            string mid = Measure(HauntFreqDefault, out int firedMid);
            string lo = Measure(0.25f, out int firedLo);
            string hi = Measure(1.00f, out int firedHi);
            // The elements bend the rate: Dark x1.60, Light x0.65 (EnvHaunt.cginc).
            Measure(Mathf.Clamp01(HauntFreqDefault * 1.60f), out int firedDark);
            Measure(Mathf.Clamp01(HauntFreqDefault * 0.65f), out int firedLight);

            Debug.Log(
                $"[GloomhavenVR][Env] {room} HAUNT SCHEDULE — a schedule, not a loop, and a PURE FUNCTION of "
                + $"the shared clock: slot = floor((_Time.y + _GhvrTimeOfs) / {HauntPeriod:F0} s), and every "
                + "decision in it is H(slot, k) — no Random, no per-client state, no per-instance seed, no "
                + "frame history, no head or camera input, and no sin() in the hash. Two clients compute the "
                + "same bits.\n"
                + $"  NO REPEAT, proven over {SLOTS} slots: event k is in group (k mod {HauntGroups}) and slot "
                + $"n may only draw from group (n mod {HauntGroups}), so consecutive slots are different events "
                + "by construction — there is no re-roll and therefore no residual chance.\n"
                + $"  AT THE SHIPPED DIAL ({HauntFreqDefault:F2}): {mid}\n"
                + $"  AT 0.25: {lo}\n"
                + $"  AT 1.00: {hi}\n"
                + $"  THE DIAL IS A SUBSET, NOT A RESHUFFLE: the schedule is fixed and the dial is the gate "
                + $"H(slot,{HcRate}) < freq, so the {firedLo} events a player at 0.25 sees are a strict subset "
                + $"of the {firedMid} at 0.50, which are a strict subset of the {firedHi} at 1.00 — same "
                + "seconds, same places. That is how a per-client dial coexists with \"synchron von allen "
                + "Spielern an den selben Stellen sichtbar\".\n"
                + $"  ELEMENTS bend the rate the same monotone way and stay client-identical (the game "
                + $"desync-checks the element board every round): full Dark {firedDark} events "
                + $"(x{(float)firedDark / firedMid:F2}), full Light {firedLight} (x"
                + $"{(float)firedLight / firedMid:F2}).");
        }

        /// <summary>Bake one room's catalogue: build every solid, prove it is a
        /// closed outward solid, bake its light, weld it, gate it, measure it,
        /// place it.
        ///
        /// <para>NO LIGHT RIG, deliberately. Every other object in these rooms is
        /// registered with Defer() and lit by the baked rig; an apparition is not.
        /// It takes no light from the candles and casts none — it is a shape in the
        /// dark, and giving it a candle's falloff would make it furniture. What it
        /// has instead is its OWN key, authored per card and baked per vertex, so
        /// the thing at the window is lit by the moon behind it and the face over
        /// the bookshelf by the candle under it.</para>
        ///
        /// <para>`extra` is now the ONLY way a card gets geometry — it used to be
        /// the hook for the pieces that were not a placed form, and since ModBuild
        /// 146 there are no placed forms. What is left in it: the handprints on the
        /// cellar's wet wall and the pair of eyeshines in the wood.</para></summary>
        private static GameObject BuildHaunts(Transform root, string room, string asset,
            HauntCard[] cards, Color fillLight, float playDia, float xLim, float zLim, float yLim,
            Action<HauntAcc, HauntCard[], int, Acc> extra = null)
        {
            ReportHauntSchedule(room, cards);

            var h = new HauntAcc();
            var firstVert = new int[cards.Length];
            var shapes = new string[cards.Length];
            var bounds = new Bounds(cards[0].at, Vector3.one * 0.01f);
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                firstVert[i] = h.V.Count;
                shapes[i] = "-";
                if (c.kind == HKindNone) { shapes[i] = "(no geometry)"; continue; }

                // EVERY remaining piece is BUILT HERE, in `extra`. Nothing is
                // imported and nothing is posed: the five human forms and the
                // pipeline that posed them were deleted in ModBuild 146 (see the
                // HAUNT SOLID: FORMS block).
                var piece = new Acc();
                extra?.Invoke(h, cards, i, piece);
                if (piece.Count == 0) continue;

                // CLOSED AND OUTWARD, per apparition. This project has shipped
                // inward-wound geometry three times (the window bars, the moonbeam
                // blades, the rat you could see the inside of), and an apparition
                // is the worst possible place for a fourth: it is dark, it is
                // brief, and "I could see through it" is indistinguishable from
                // "I did not see it". Decals are exempt — a mark on a wall is a
                // surface, not a solid, and it says so in its kind.
                if (c.kind != HKindDecal)
                    AssertClosedAndOutward(piece, $"{room} haunt '{c.name}'");

                int tris = piece.T.Count / 3;
                shapes[i] = $"built {tris} tris {c.height:F2} m tall";
                HauntWeld(h, piece, c, i, c.kind, c.key, fillLight, c.move.normalized, c.moveAmt,
                          c.rotAxis.normalized, c.rotAngle, c.pivotY, 1f, 1f, -1f);
            }
            // THE BOUNDS ARE EXPLICIT, and they have to be: a collapsed
            // apparition's vertices all sit on its anchor and a travelling one
            // leaves its authored box entirely, so Unity's own RecalculateBounds
            // would frustum-cull the one apparition that is currently happening.
            for (int v = 0; v < h.V.Count; v++)
            {
                var mv = new Vector3(h.UV2[v].x, h.UV2[v].y, h.UV2[v].z) * h.UV2[v].w;
                bounds.Encapsulate(h.V[v]);
                bounds.Encapsulate(h.V[v] + mv);
                bounds.Encapsulate(h.V[v] - mv);
            }
            // the rotations and the element drift move vertices too; a metre of
            // slack costs nothing and a frustum-culled apparition is a slot in
            // which nothing happens
            bounds.Expand(1.0f);

            AssertHauntCards(room, cards, h, firstVert, playDia, Vector3.zero, xLim, zLim, yLim, shapes);

            var mesh = SaveHauntMesh(asset + ".asset", h.Build(asset, bounds), bounds);
            var mat = NewRoomMat(room + "_Haunt.mat", "GloomhavenVR/EnvHaunt");
            mat.SetFloat("_Period", HauntPeriod);
            mat.SetFloat("_Cards", cards.Length);
            // TRANSPARENT render state, set from here because the shader's state
            // is material-controlled: the same program also draws the OPAQUE
            // tipping bookshelf, and one pass cannot carry two blend modes.
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.renderQueue = 3002;
            // THE FLAT-MARK ATLAS. Bound here rather than left to the shader's
            // "black" default on purpose: a missing atlas would otherwise be an
            // invisible feature rather than a build error.
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Bundle/Environments/Textures/Env_Haunt.png");
            if (atlas == null)
                throw new Exception("Env_Haunt.png is missing — the handprints have no likeness. "
                                    + "EnvironmentsBuilder.MakeHauntAtlas bakes it; GenerateTextures "
                                    + "must run before the rooms.");
            mat.SetTexture("_Atlas", atlas);
            // THE ROOM'S FILL LIGHT. One colour per room, because a room HAS one
            // ambient — the cellar's is the cold spill off the window and the
            // damp, the forest's is the sky between the trunks.
            mat.SetColor("_Fill", fillLight);
            // 0.08, not 0.30. The rim is COMPENSATION for a room that has gone
            // black (full Dark adds 0.55 on top), not a permanent outline: at 0.30
            // every apparition wore a bright even line all the way round itself,
            // which is the visual grammar of a sticker and was the loudest thing
            // in the frame.
            // 0.02, down from 0.08. The rim used to be measured against an atlas
            // whose key channel peaked at 1.0; it is now measured against a baked
            // key that peaks at 0.03-0.10, and at 0.08 the outline was BRIGHTER
            // than the thing it outlined — which is the visual grammar of a
            // sticker and is exactly what the 0.30 -> 0.08 pass was for the first
            // time round.
            mat.SetFloat("_Rim", 0.02f);
            mat.SetColor("_RimCold", new Color(0.42f, 0.56f, 0.78f, 1f));
            mat.SetColor("_RimWarm", new Color(0.95f, 0.48f, 0.16f, 1f));
            return Place(root, "Haunts", mesh, Vector3.zero, Vector3.zero, Vector3.one, mat);
        }

        /// <summary>THE BOOKSHELF THAT FALLS OVER.
        ///
        /// <para>USER, cellar 11: "Statt da auch ne Fratze zu machen: Wie wär es
        /// wenn das Bücherregal umkippt, und sich dann nach ner Zeit wieder von
        /// selbst aufstellt." So the bookshelf is no longer the thing an
        /// apparition hides behind — it IS the apparition, and card 5 of the
        /// cellar's catalogue is its fall.</para>
        ///
        /// <para>WHY IT IS NOT AN EnvRoom PROP ANY MORE. This mod ships
        /// script-free prefabs: the only thing that can move a vertex at runtime
        /// is a vertex shader, so whatever tips the shelf has to be the shader the
        /// shelf is drawn with. EnvRoom is shared by every prop in both rooms and
        /// has no business knowing the haunt schedule, so the shelf moves to
        /// EnvHaunt — which already owns that clock — on an OPAQUE material.
        /// One program, two render states, set from here because a Unity pass
        /// cannot carry two blend modes; see the KIND 4 block in EnvHaunt.shader.
        /// It keeps its albedo, its normal map and the room's three real point
        /// lights through the ordinary ApplyRig path. What it loses is the GROWN
        /// frost and moss patches (EnvGrowth), which need a second noise field
        /// this shader does not carry; the element GAINS it keeps.</para>
        ///
        /// <para>WHICH WAY IT GOES OVER IS NO LONGER AUTHORED AT ALL, ModBuild
        /// 146. It used to be a room diagonal chosen so the fallen top board
        /// cleared the play space — and a diagonal 36.9 deg off the carcass's own
        /// face is a hinge that is not a base edge, which is why the shelf came
        /// to rest leaning on one long edge (user: "bleibt dann unrealistisch auf
        /// einer Kante liegen"). The fall direction is now READ OFF THE PLACED
        /// PROP — it falls on its face, about the base edge it stands on — and
        /// the play space is cleared by where the shelf STANDS instead, which is
        /// a placement decision and not a physics one. See the block around
        /// tipDir for the full argument and for the two alternatives rejected.</para>
        ///
        /// <para>THE POSE IS A PURE FUNCTION OF THE CLOCK — GhvrShelfTip(phase) in
        /// the shader — so nothing integrates and there is no state to be left in.
        /// A player who joins mid-fall sees the same angle as everyone else, and an
        /// event cut short by the slot ending cannot leave the shelf on its
        /// face.</para>
        ///
        /// <para>SHELF RIDERS, ModBuild 145 — the KNOWN COST this method used to
        /// carry is paid. It read: "the room's second candle GROUP stands ON this
        /// shelf (CandleGroup 'Shelf', light slot 1) and does not fall with it,
        /// because its wax, its flame cards and its halo are three other
        /// shaders." The user found it on hardware and ruled: "Die Kerzen und das
        /// Feuer, die auf dem Bücherregal stehen, kippen nicht mit - das musst du
        /// beheben das ist ein echter Bug. Sie müssen auf jeden Fall mitkippen."
        ///
        /// This method now PUBLISHES the pose (the <c>Tip</c> record) instead of
        /// burying it in this mesh's vertex lanes, and every rider — the wax, the
        /// flame, the two halos, the two seated fires and the light slot that
        /// belongs to the candle — takes it from there through WriteShelfTip. The
        /// shelf itself is a rider like the rest: EnvHaunt reads the same five
        /// material vectors the candle does.</para></summary>
        private static GameObject BuildTippingShelf(Transform root, Vector3 pos, float yaw)
        {
            var src = ImpMesh("wooden_bookshelf_worn");
            var mat = NewRoomMat("C_Shelf.mat", "GloomhavenVR/EnvHaunt");
            mat.SetTexture("_MainTex", Imp("wooden_bookshelf_worn_alb"));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(
                ImpTex + "/wooden_bookshelf_worn_nrm.jpg");
            if (nrm != null) mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1f);
            // OPAQUE render state — the same program's other material is
            // transparent, so every one of these has to be said out loud.
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.renderQueue = 2000;
            mat.SetFloat("_Period", HauntPeriod);
            mat.SetFloat("_Cards", HauntCellarCards);

            var go = Place(root, "Shelf", src, pos, new Vector3(0, yaw, 0), Vector3.one, mat);
            Rest(go, null, 0.015f, pos);

            // measure the placed shelf, in ROOM space, and derive the hinge from
            // it — a photoscan's base edge is not where anybody would guess
            var b = new Bounds(); bool first = true;
            foreach (var v in WorldVerts(go))
            { if (first) { b = new Bounds(v, Vector3.zero); first = false; } else b.Encapsulate(v); }

            // axis x up = (-axis.z, 0, axis.x) is the direction the top of the
            // shelf travels, so the axis that tips it toward tipDir is
            // (tipDir.z, 0, -tipDir.x) — and the first version of this line had
            // the sign the other way round, which tipped the shelf INTO the wall
            // and below the floor. It did not fail anything: the preview simply
            // showed an empty corner for the middle two thirds of the event,
            // because the shelf really was inside the masonry. The guard at the
            // bottom of this method was checking the INTENDED direction, which is
            // why it passed. Both now come from the same vector.
            // ================================================================
            // WHY IT LANDED ON AN EDGE, and what the fix actually is.
            //
            // USER, ModBuild 146: "Mir gefällt wie das Regal fällt, aber es fällt
            // aktuell so schräg und bleibt dann unrealistisch auf einer Kante
            // liegen, es sollte realistisch Fallen."
            //
            // He is describing two separate faults and the first one is the big
            // one. It was NOT the curve (which commit e0c50ce had already made
            // the pendulum's exact separatrix) and it was NOT the 88 degrees on
            // its own. It was that THE HINGE WAS NOT A BASE EDGE OF THE SHELF.
            //
            //   The shelf stood against the east wall facing -X, and it was
            //   tipped toward (-0.8, 0, 0.6) — a room diagonal 36.9 deg off its
            //   own face — about the axis (0.60, 0, 0.80). Rotate a box 90 deg
            //   about a horizontal axis at angle t to its base edge and its
            //   landing face comes to rest tilted by exactly t: the up axis goes
            //   flat (that part is right, which is why it LOOKED nearly down),
            //   but the depth axis picks up a vertical component of cos(t) and
            //   the carcass ends up standing on one long edge at 36.9 deg. It was
            //   never lying down. It was leaning on itself.
            //
            //   Then the 88 degrees added a second, smaller fault of its own: two
            //   degrees short of flat, over a 2.06 m carcass, holds the far end —
            //   the TOP BOARD, the thing the eye follows all the way down — 7.2 cm
            //   off the flagstones. That is the "top board off the floor" in the
            //   p30 render, and it is a different defect from the roll.
            //
            // THE FIX IS TO STOP AUTHORING THE FALL DIRECTION AT ALL. A shelf
            // falls on its face, about the base edge it is standing on, and that
            // direction is not a taste decision — it is the shelf's own forward,
            // which is where the prop's placement yaw already put it. So tipDir
            // is READ OFF THE PLACED TRANSFORM and the hinge axis is the base
            // edge perpendicular to it, by construction. There is no longer a
            // vector here that can disagree with the carcass.
            //
            // WHAT THAT COST, AND WHY THE SHELF MOVED. Falling straight off the
            // east wall from the old (4.86, 0.70) put the fallen top board 2.94 m
            // from the room centre — inside the 3.25 m PlaySpace disc, which is
            // exactly why the diagonal had been chosen in the first place. The
            // answer is to slide the shelf ALONG ITS OWN WALL rather than to turn
            // it away from it: at z = -3.15 the whole fallen carcass, corners and
            // all, clears the disc (the gate below measures it, not just the top
            // board's centre). Everything that stands on the shelf is placed
            // relative to CellarShelfAt, so the candle, its light slot, the fire
            // and the halos all came with it.
            //
            // ALTERNATIVES REJECTED:
            //  * Turn the SHELF 36.9 deg to face the diagonal instead. It lands
            //    flat and needs no move along the wall — but a 0.58 x 1.37 m
            //    carcass yawed that far needs 0.64 m of clearance from the wall
            //    instead of 0.29, so it has to be dragged 0.32 m out into the
            //    room and stands there at an angle with a wedge of space behind
            //    it. A bookcase stands flat against a wall; building the room
            //    around the effect is the wrong way round.
            //  * Keep the diagonal and relax the PlaySpace guard. Refused: that
            //    guard is the "niemals den Spielfluss stören" line.
            //  * Land at 88 deg and let the curve's rebound hide the gap. The
            //    rebound is 1.9 deg of a body that has ALREADY reached the floor;
            //    it cannot fill a gap that exists at the end of the travel.
            // ================================================================
            //
            // The shelf falls along its own forward. Flattened and renormalised
            // because a hinge is horizontal whatever the prop's pitch is.
            var tipDir = new Vector3(go.transform.forward.x, 0f, go.transform.forward.z).normalized;
            var axisW = new Vector3(tipDir.z, 0f, -tipDir.x).normalized;
            // ...and the carcass's own half-extents in that frame, measured.
            float reach = 0f, halfWide = 0f;
            foreach (var v in WorldVerts(go))
            {
                var q = new Vector3(v.x - b.center.x, 0f, v.z - b.center.z);
                reach = Mathf.Max(reach, Vector3.Dot(q, tipDir));
                halfWide = Mathf.Max(halfWide, Mathf.Abs(Vector3.Dot(q, axisW)));
            }
            // A SHELF FALLS ON ITS FACE. If the direction it is being tipped in
            // is the LONG horizontal axis of the carcass then it is being pushed
            // over sideways, which is a different (and much less likely) event
            // and would land it on a side panel. This is the gate that keeps
            // "tipDir is the prop's forward" honest if a prop is ever placed at a
            // yaw that points its forward along the wall.
            if (reach >= halfWide)
                throw new Exception($"The tipping shelf is being tipped along its LONG axis "
                                    + $"(half-depth {reach:F2} m vs half-width {halfWide:F2} m): the "
                                    + "prop's forward does not point out of the wall it stands against. "
                                    + "Fix the placement yaw, not the fall.");
            var pivotW = new Vector3(b.center.x, b.min.y, b.center.z) + tipDir * reach;
            // 90 degrees, not 88. The old two-degree short-fall was there to stop
            // the topple "looking like a door closing", and that job now belongs
            // to the curve: EnvShelfTip lands on the separatrix and then plays one
            // ballistic rebound of 1.9 deg over 0.62 s (GHVR_TIP_BAMP), which is a
            // landing rather than a stop. Two degrees of permanent lean is not a
            // landing, it is a body that never arrived — and on a 2.06 m carcass
            // it is 7 cm of daylight under the top board.
            const float TipDeg = 90f;

            // ---- PUBLISH THE POSE. Everything that will stand on this shelf is
            // built after it (the candles at CandleGroup, the fires at
            // AddCellarFire), and every lit material in the room is written at
            // FlushRig — all of them read this record and none of them re-derives
            // any part of it. It is in ROOM space; WriteShelfTip is the only
            // thing that converts, and it converts per material.
            Tip = new ShelfTipRig
            {
                pivotW = pivotW,
                axisW = axisW,
                maxAngle = TipDeg * Mathf.Deg2Rad,
                card = HauntCardShelf,
                env = HauntShelfEnv,
                period = HauntPeriod,
                cards = HauntCellarCards,
                // the candle group that stands on this shelf drives light slot 1
                // (CandleGroup "Shelf"), so slot 1 is the one that travels
                litSlot = 1,
            };

            // SaveHauntMesh and NOT SaveMesh: the plain writer copies UV0 only,
            // and this mesh carries four UV sets. That mistake does not fail, it
            // draws the PREVIOUS bake's lanes with this bake's positions — see
            // SaveHauntMesh's own comment, which was written after it cost a
            // whole review round.
            var propMesh = HauntPropMesh(src, HauntCardShelf, HauntShelfEnv);
            var mesh = SaveHauntMesh("Env_C_ShelfTip.asset", propMesh, propMesh.bounds);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            // The shelf is a rider of its own pose — one call, so there is no
            // "the shelf's copy" of the hinge for a candle's copy to drift from.
            // It moves its own geometry (self = 1) and is lit by the candle
            // standing on it (the default lit slot, from the record).
            RideShelf(mat, self: 1f, lit: Tip.litSlot, gutter: 0f, stiff: 0f);
            Defer(mat, go.transform, 1f);

            // ---------------------------------------------- THE LANDING GATE
            // "bleibt dann unrealistisch auf einer Kante liegen" is a claim about
            // a body at rest, and a body at rest has a SUPPORT POLYGON, not a
            // support line. So the fallen pose is built here — every vertex,
            // rotated by the shipped final angle about the shipped hinge — and
            // three things are measured on it. All three FIRE on the ModBuild 145
            // geometry (tipDir (-0.8,0,0.6), 88 deg): its fallen carcass is
            // 0.86 m tall against a 0.58 m depth, its contact set is 0.02 m deep,
            // and its far corner reaches 2.90 m. Verified by putting those two
            // values back and re-baking.
            var fallen = new List<Vector3>();
            {
                // RODRIGUES, WRITTEN OUT, and not Quaternion.AngleAxis: this has
                // to be the SAME map the shader applies (GhvrTipRot), and the two
                // libraries' handedness conventions agreeing is a thing to check
                // rather than to assume. If this ever disagreed with the shader
                // the gate would be measuring a pose nobody ever sees.
                float ca = Mathf.Cos(TipDeg * Mathf.Deg2Rad), sa = Mathf.Sin(TipDeg * Mathf.Deg2Rad);
                foreach (var v in WorldVerts(go))
                {
                    var q = v - pivotW;
                    fallen.Add(pivotW + q * ca + Vector3.Cross(axisW, q) * sa
                               + axisW * (Vector3.Dot(axisW, q) * (1f - ca)));
                }
            }
            float fMinY = float.MaxValue, fMaxY = float.MinValue, fNear = float.MaxValue;
            foreach (var p in fallen)
            {
                fMinY = Mathf.Min(fMinY, p.y); fMaxY = Mathf.Max(fMaxY, p.y);
                if (p.y <= 2.2f) fNear = Mathf.Min(fNear, new Vector2(p.x, p.z).magnitude);
            }
            // 1. IT MUST LIE AS THICK AS IT IS DEEP. A carcass lying on its face
            //    stands exactly its own depth off the floor; one resting on an
            //    edge stands much taller, and the excess IS the tilt.
            float lieH = fMaxY - fMinY, depth = reach * 2f;
            if (lieH > depth * 1.06f + 0.03f)
                throw new Exception($"The fallen bookshelf is {lieH:F2} m tall but only {depth:F2} m "
                                    + $"deep: it has come to rest tilted by about "
                                    + $"{Mathf.Acos(Mathf.Clamp01(depth / Mathf.Max(lieH, 1e-3f))) * Mathf.Rad2Deg:F0} deg, "
                                    + "i.e. it is standing on an edge of its own carcass and not lying "
                                    + "down (user: \"bleibt dann unrealistisch auf einer Kante liegen\"). "
                                    + "The hinge axis must be parallel to a base edge and the travel must "
                                    + "reach flat.");
            // 2. IT MUST REST ON A POLYGON. The contact set is everything within
            //    2 cm of the lowest point; its extent ACROSS the hinge is what
            //    separates "lying on its face" from "balanced on the front edge".
            float cLo = float.MaxValue, cHi = float.MinValue;
            foreach (var p in fallen)
                if (p.y - fMinY < 0.02f)
                {
                    float s = Vector3.Dot(p - pivotW, tipDir);
                    cLo = Mathf.Min(cLo, s); cHi = Mathf.Max(cHi, s);
                }
            float support = cHi - cLo;
            if (support < 0.10f)
                throw new Exception($"The fallen bookshelf touches the floor over only {support:F3} m "
                                    + "measured along its fall: that is a line of contact, not a support "
                                    + "polygon, so it is propped rather than lying. See the LANDING GATE.");
            // 3. AND THE WHOLE OF IT, not just the top board's centre, must stay
            //    out of the play space. The shipped guard checked one point; a
            //    2.06 x 1.37 m slab has corners 0.69 m off that point, and both
            //    of the far ones were inside the disc.
            if (fNear < CellarPlaySpaceDia * 0.5f)
                throw new Exception($"The tipping bookshelf's fallen carcass reaches {fNear:F2} m from "
                                    + $"the room centre, inside the {CellarPlaySpaceDia * 0.5f:F2} m "
                                    + "PlaySpace. The easter eggs may never reach over the board (user: "
                                    + "\"niemals den Spielfluss stören\") — slide the shelf further "
                                    + "along its own wall.");

            var top = pivotW + tipDir * (b.size.y * Mathf.Sin(TipDeg * Mathf.Deg2Rad));
            Debug.Log($"[GloomhavenVR][Env] Cellar SHELF LANDING (user: \"es fällt aktuell so schräg und "
                      + $"bleibt dann unrealistisch auf einer Kante liegen\"): it now hinges on a REAL "
                      + $"base edge — tipDir {tipDir:F2} is the prop's own forward and the axis "
                      + $"{axisW:F2} is perpendicular to it, so nothing rolls. Fallen it is {lieH:F3} m "
                      + $"tall against its own {depth:F3} m of depth (was 0.86 against 0.58, a 37 deg "
                      + $"roll), rests on {support:F2} m of contact along the fall, and its nearest "
                      + $"corner is {fNear:F2} m from the room centre (PlaySpace "
                      + $"{CellarPlaySpaceDia * 0.5f:F2} m).");
            Debug.Log($"[GloomhavenVR][Env] Cellar SHELF is now a haunt (user: \"wie wär es wenn das "
                      + "Bücherregal umkippt, und sich dann nach ner Zeit wieder von selbst aufstellt\"). "
                      + $"Standing bounds {b.min:F2}..{b.max:F2} ({b.size.x:F2} x {b.size.y:F2} x "
                      + $"{b.size.z:F2} m, {src.triangles.Length / 3} tris). It hinges on its base edge at "
                      + $"({pivotW.x:F2},{pivotW.y:F2},{pivotW.z:F2}) about the horizontal axis "
                      + $"({axisW.x:F2},{axisW.y:F2},{axisW.z:F2}) and goes over {TipDeg:F0} deg toward "
                      + $"({tipDir.x:F2},0,{tipDir.z:F2}), which lands its top board at "
                      + $"({top.x:F2},{top.z:F2}) — {new Vector2(top.x, top.z).magnitude:F2} m from the room "
                      + $"centre, against a PlaySpace radius of {CellarPlaySpaceDia * 0.5f:F2} m. Event: "
                      + $"card {HauntCardShelf}, {HauntShelfEnv.y:F0} s, of which 4.68 s of topple "
                      + "(the pendulum separatrix), 0.62 s of rebound, 10.8 s lying there and 9.88 s "
                      + "getting back up (EnvShelfTip.cginc's own schedule, commit e0c50ce). "
                      + "The pose is GhvrShelfTip(phase) and nothing integrates, "
                      + "so an interrupted event cannot leave it lying on its face.");
            // (the top board's own landing point is still printed above; the GATE
            // on it is now the whole-carcass one in the LANDING GATE block, which
            // subsumes it — a slab whose nearest corner clears the disc has a
            // centre that clears it too.)
            return go;
        }

        /// <summary>Re-author an imported prop mesh with the eight channels
        /// EnvHaunt's appdata declares, so an ordinary photoscan can be drawn by
        /// the apparition shader. Only KIND 4 uses this, and it REINTERPRETS two
        /// lanes: TANGENT carries the real tangent (a prop has a normal map and
        /// never collapses, so it does not need an anchor) and UV0.zw carries the
        /// albedo uv.
        ///
        /// <para>UV2 AND UV3 ARE NOW ZERO, and that is the shelf-rider fix in the
        /// mesh. They used to carry the hinge and the axis+angle — a per-vertex
        /// copy of three constants, and worse, a SECOND place in the bundle where
        /// the pose was stored. The hinge is derived from the placed shelf's
        /// measured bounds, so a mesh baked in one build and a material written in
        /// another would have disagreed silently by however far the prop had
        /// moved. The pose lives on the material now, once, shared with every
        /// rider (WriteShelfTip). The lanes stay in the vertex layout because the
        /// apparition kinds use them and one mesh feeds one shader.</para></summary>
        private static Mesh HauntPropMesh(Mesh src, int card, Vector4 env)
        {
            var v = Verts(src);
            var n = src.normals;
            var t = src.tangents;
            var uv = src.uv;
            var m = new Mesh { name = "Env_C_ShelfTip" };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var tan = new List<Vector4>(v.Length);
            var uv0 = new List<Vector4>(v.Length);
            var uv1 = new List<Vector4>(v.Length);
            var uv2 = new List<Vector4>(v.Length);
            var uv3 = new List<Vector4>(v.Length);
            var col = new Color[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                tan.Add(t != null && t.Length == v.Length ? t[i] : new Vector4(1, 0, 0, 1));
                var q = uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero;
                uv0.Add(new Vector4(card, HKindProp, q.x, q.y));
                uv1.Add(env);
                uv2.Add(Vector4.zero);   // the hinge lives on the material now
                uv3.Add(Vector4.zero);   // ...and so do the axis and the angle
                col[i] = Color.white;
            }
            m.vertices = v;
            if (n != null && n.Length == v.Length) m.normals = n;
            m.SetTangents(tan);
            m.SetUVs(0, uv0);
            m.SetUVs(1, uv1);
            m.SetUVs(2, uv2);
            m.SetUVs(3, uv3);
            m.colors = col;
            m.triangles = src.triangles;
            if (m.normals == null || m.normals.Length != v.Length) m.RecalculateNormals();
            // THE BOUNDS HAVE TO COVER THE FALL. The shelf's own box is the box it
            // occupies STANDING; half way through the event it is lying two metres
            // from there, and a frustum-culled bookshelf is an event in which
            // nothing visibly happens.
            var bb = m.bounds;
            bb.Expand(2.0f * bb.size.y);
            m.bounds = bb;
            return m;
        }

        /// <summary>Write the haunt mesh to its asset — ALL OF IT.
        ///
        /// <para>This exists because SaveMesh() does not. When its target asset
        /// already exists it re-uses it (to keep the GUID stable) and copies
        /// vertices, normals, tangents, UV0, colors and triangles — and NOTHING
        /// ELSE. Every other mesh in this builder uses at most those channels, so
        /// nobody ever noticed; the haunt mesh carries four UV sets, and on a
        /// re-bake it therefore kept UV1..UV3 FROM THE PREVIOUS BAKE while the
        /// positions and colours came from the new one.</para>
        ///
        /// <para>THIS COST A WHOLE REVIEW ROUND, so it is written down. The
        /// symptom was not "the easter eggs are missing": it was that half of them
        /// drew the WRONG APPARITION and the other half drew nothing, in a way
        /// that looked exactly like a broken atlas lookup. A mesh writer that drops
        /// channels does not fail, it LIES.</para>
        ///
        /// <para>The channel count is asserted on the way out rather than trusted,
        /// because the next person to add a UV set to this mesh will not read
        /// this comment.</para></summary>
        private static Mesh SaveHauntMesh(string file, Mesh src, Bounds bounds)
        {
            string path = MeshDir + "/" + file;
            var dst = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (dst == null)
            {
                src.name = Path.GetFileNameWithoutExtension(file);
                src.bounds = bounds;
                AssetDatabase.CreateAsset(src, path);
                dst = src;
            }
            else
            {
                var v = new List<Vector3>(); src.GetVertices(v);
                var n = new List<Vector3>(); src.GetNormals(n);
                var t = new List<Vector4>(); src.GetTangents(t);
                var c = new List<Color>(); src.GetColors(c);
                var uvs = new List<Vector4>[4];
                for (int k = 0; k < 4; k++) { uvs[k] = new List<Vector4>(); src.GetUVs(k, uvs[k]); }
                dst.Clear();
                dst.indexFormat = src.indexFormat;
                dst.SetVertices(v);
                dst.SetNormals(n);
                dst.SetTangents(t);
                for (int k = 0; k < 4; k++) dst.SetUVs(k, uvs[k]);
                dst.SetColors(c);
                dst.SetTriangles(src.triangles, 0);
                dst.bounds = bounds;
                EditorUtility.SetDirty(dst);
                UnityEngine.Object.DestroyImmediate(src);
            }

            // Read the channels back off the ASSET, not off the array we just
            // handed it. Four UV sets of four components each is what EnvHaunt's
            // appdata declares, and a mesh one channel short draws the previous
            // bake's catalogue with this bake's coordinates.
            for (int k = 0; k < 4; k++)
            {
                var probe = new List<Vector4>();
                dst.GetUVs(k, probe);
                if (probe.Count != dst.vertexCount)
                    throw new Exception($"{file}: UV{k} has {probe.Count} entries for "
                                        + $"{dst.vertexCount} vertices. EnvHaunt reads all four UV sets; "
                                        + "a missing one is a silently WRONG apparition, not an absent one.");
            }
            return dst;
        }

        /// <summary>A cobweb SHEET: one span of silk strung across an opening or
        /// a corner, as a subdivided card that bellies out of its own plane.
        ///
        /// USER FINDING, ModBuild 135: "Im Keller die Spinnwebe sehen sehr
        /// low-poly aus". They were quarter fans (5 rings x 12 segments) carrying
        /// a PROCEDURAL orb web drawn in (angle, radius) space — nine perfectly
        /// even spokes, eleven perfectly even spirals, and a straight-edged
        /// polygon silhouette. Every part of that is regular, and regularity at
        /// low tessellation is exactly what "low-poly" means to the eye.
        ///
        /// What replaced it: the web is a 4k photoscanned CC0 ALPHA (see
        /// Environments/License.md) whose threads, tears and anchor strands are
        /// irregular because they were once real, and the geometry's only job is
        /// to hold it in a plausible place and let it move. The card is
        /// subdivided so the sheet can BELLY (out of plane, and drooping under
        /// its own weight), so its silhouette is a curve and not a rectangle,
        /// and so _Sway ripples it instead of translating it.
        ///
        /// Vertex RED is the freedom EnvRoomCutout's _Sway weights by — zero all
        /// round the rim, where the silk is anchored to stone, greatest in the
        /// middle of the free span.</summary>
        private static Mesh WebSheetMesh(Vector3 c, Vector3 halfU, Vector3 halfV,
            Rect uv, float belly, int nu, int nv, int seed)
        {
            var nrm = Vector3.Cross(halfV, halfU).normalized;
            var acc = new Acc();
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float fu = i / (float)nu, fv = j / (float)nv;
                    // the rim is pinned; the middle is free. sin*sin is the first
                    // mode of a stretched membrane, which is what silk is.
                    float bulge = Mathf.Sin(fu * Mathf.PI) * Mathf.Sin(fv * Mathf.PI);
                    float rough = 0.55f + 0.90f * Fbm2(fu * 2.6f, fv * 2.6f, 3, seed);
                    Vector3 p = c + halfU * (fu * 2f - 1f) + halfV * (fv * 2f - 1f)
                              + nrm * (belly * bulge * rough)
                              + Vector3.down * (belly * 0.45f * bulge * rough);
                    acc.Vert(p, nrm,
                             new Vector2(Mathf.Lerp(uv.xMin, uv.xMax, fu),
                                         Mathf.Lerp(uv.yMin, uv.yMax, fv)),
                             new Color(bulge, 0f, 0f, 1f));
                }
            int stride = nu + 1;
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int i0 = j * stride + i;
                    acc.T.AddRange(new[] { i0, i0 + stride, i0 + 1, i0 + 1, i0 + stride, i0 + stride + 1 });
                }
            return acc.Build("Env_C_WebSheet");
        }

        /// <summary>A single loose strand hanging off something: a narrow ribbon
        /// that follows a catenary from its anchor, twisting slightly so it is
        /// never edge-on for long. `col` picks one of the three threads in
        /// Env_Strand.png. Vertex RED grows toward the free end — the anchor
        /// cannot move, the tip swings most.</summary>
        private static Mesh StrandMesh(Vector3 anchor, Vector3 drop, Vector3 wide,
            int col, int cols, int segs, int seed)
        {
            var acc = new Acc();
            float u0 = col / (float)cols, u1 = (col + 1) / (float)cols;
            for (int i = 0; i <= segs; i++)
            {
                float f = i / (float)segs;
                // catenary-ish: it hangs straight down at first and drifts
                float sway = 0.35f * f * f + 0.10f * (Fbm2(f * 3.3f, seed * 0.01f, 2, seed) - 0.5f);
                Vector3 c = anchor + drop * f + wide * sway;
                // the ribbon narrows and turns as it falls
                float tw = Mathf.Lerp(1f, 0.55f, f);
                Vector3 right = (wide.normalized * Mathf.Cos(f * 1.9f + seed * 0.1f)
                                 + Vector3.Cross(drop.normalized, wide.normalized) * Mathf.Sin(f * 1.9f + seed * 0.1f))
                                * (wide.magnitude * 0.5f * tw);
                Vector3 n = Vector3.Cross(drop.normalized, right).normalized;
                var col2 = new Color(f * f, 0f, 0f, 1f);
                acc.Vert(c - right, n, new Vector2(u0, f), col2);
                acc.Vert(c + right, n, new Vector2(u1, f), col2);
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 2;
                acc.T.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            return acc.Build("Env_C_Strand");
        }

        private static Mesh BuildShaft(float depth, float h, float width)
        {
            // U-shaped alcove interior: two side walls + ceiling, opening toward +X? —
            // built in local coords: opening plane at x=0 (matches W wall), shaft
            // extends -X; z spans 0..width (aligned to StairHole along the wall run).
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float us)
            {
                int i0 = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2((b - a).magnitude / us, 0));
                uv.Add(new Vector2((b - a).magnitude / us, (d - a).magnitude / us)); uv.Add(new Vector2(0, (d - a).magnitude / us));
                tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
            // side wall at z=0 (faces +Z), from opening (x=0) into the hill (x=-depth)
            Quad(new Vector3(0, 0, 0), new Vector3(-depth, 0, 0), new Vector3(-depth, h, 0), new Vector3(0, h, 0), 3.4f);
            // side wall at z=width (faces -Z)
            Quad(new Vector3(-depth, 0, width), new Vector3(0, 0, width), new Vector3(0, h, width), new Vector3(-depth, h, width), 3.4f);
            // sloped ceiling following the stairs
            Quad(new Vector3(0, h, 0), new Vector3(-depth, h, 0), new Vector3(-depth, h, width), new Vector3(0, h, width), 3.4f);
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>The bounding hull of an analytic light volume (EnvBeam) — a
        /// closed truncated cone (r0 == r1 gives a cylinder, which is what the
        /// cellar uses) about the axis, every vertex pushed back inside the room
        /// by `clamp`.
        ///
        /// IT IS NEVER SEEN. The shader evaluates the density along the view ray
        /// analytically, so this surface only has to (a) cover the beam's screen
        /// footprint and (b) stay in front of every opaque thing that could
        /// depth-reject it. Hence the radius of several sigma — the caller sizes
        /// it where the super-gaussian is ~1e-6 of peak, so this mesh's rim is
        /// black before it ends — and hence `clamp`: the hull is drawn BACK FACE
        /// ONLY, so a rim that pokes through a wall or under the floor would
        /// fail ZTest and cut a hole in the beam.
        ///
        /// WINDING — THE ModBuild 137 BUG. Every triangle here is wound so that
        /// cross(p1-p0, p2-p0) points OUTWARD, i.e. along the vertex normals
        /// this mesh already stored. Until 137 the side quads and both caps were
        /// wound the other way round, so `Cull Front` in EnvBeam.shader kept the
        /// hull's NEAR faces instead of its far ones — and a near face is
        /// exactly what stops existing when the player walks into the volume.
        /// That, not the density model, is why the moonbeam vanished when
        /// entered ("verschwindet er plötzlich"): the hull was not drawn AT ALL
        /// from any camera inside it. The winding below is checked in the same
        /// way it was found — a debug pass that paints hull coverage must cover
        /// the whole frame from a camera standing on the axis. Never flip this
        /// without flipping the shader's Cull with it.
        ///
        /// Convention (right-handed basis ax x ay = dir): the side quad at
        /// (ring r, segment k) is emitted (i0, i0+1, i0+stride), which gives
        /// cross = +n, and the s0 cap is wound `flip` while the s1 cap is
        /// not — the mirror image of what 136 had.</summary>
        private static Mesh BeamHullMesh(Vector3 org, Vector3 dir, float s0, float s1,
            float r0, float r1, int rings, int segs, Func<Vector3, Vector3> clamp)
        {
            dir = dir.normalized;
            Vector3 ax = Vector3.Cross(dir, Vector3.up);
            if (ax.sqrMagnitude < 1e-4f) ax = Vector3.Cross(dir, Vector3.forward);
            ax.Normalize();
            Vector3 ay = Vector3.Cross(dir, ax).normalized;

            var a = new Acc();
            int stride = segs + 1;
            for (int r = 0; r <= rings; r++)
            {
                float f = r / (float)rings;
                float s = Mathf.Lerp(s0, s1, f), rad = Mathf.Lerp(r0, r1, f);
                for (int k = 0; k <= segs; k++)
                {
                    float ang = k / (float)segs * Mathf.PI * 2f;
                    Vector3 n = ax * Mathf.Cos(ang) + ay * Mathf.Sin(ang);
                    a.Vert(clamp(org + dir * s + n * rad), n, new Vector2(k / (float)segs, f), Color.white);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < segs; k++)
                {
                    int i0 = r * stride + k;
                    a.T.AddRange(new[] { i0, i0 + 1, i0 + stride, i0 + 1, i0 + stride + 1, i0 + stride });
                }
            // caps, so the hull is closed from every side (walking into the beam
            // must not reveal an open end)
            void Cap(int ringBase, Vector3 centre, Vector3 n, bool flip)
            {
                int c = a.Count;
                a.Vert(clamp(centre), n, new Vector2(0.5f, 0.5f), Color.white);
                for (int k = 0; k < segs; k++)
                {
                    if (flip) a.T.AddRange(new[] { c, ringBase + k + 1, ringBase + k });
                    else a.T.AddRange(new[] { c, ringBase + k, ringBase + k + 1 });
                }
            }
            Cap(0, org + dir * s0, -dir, true);
            Cap(rings * stride, org + dir * s1, dir, false);
            return a.Build("Env_BeamHull");
        }

        /// <summary>The lit patch of floor a beam lands on: an elliptical polar
        /// grid lying 1 cm over the flagstones, carrying THE FLOOR'S OWN UVs
        /// (uvScale must be the floor material's) and a soft radial mask in
        /// vertex alpha. Drawn additively with the floor albedo as its texture,
        /// so what brightens is the stone, not the air.</summary>
        private static Mesh MoonPoolMesh(Vector3 hit, Vector3 alongDir, Vector3 acrossDir,
            float halfAlong, float halfAcross, int rings, int segs, float uvScale)
        {
            var a = new Acc();
            int stride = segs + 1;
            for (int r = 0; r <= rings; r++)
            {
                float f = r / (float)rings;
                float m = Mathf.Clamp01(1f - f * f);
                for (int k = 0; k <= segs; k++)
                {
                    float ang = k / (float)segs * Mathf.PI * 2f;
                    Vector3 p = hit + alongDir * (halfAlong * f * Mathf.Cos(ang))
                                    + acrossDir * (halfAcross * f * Mathf.Sin(ang));
                    p.y = CellarFloorY(p.x, p.z) + 0.010f;
                    a.Vert(p, Vector3.up, new Vector2(p.x / uvScale, p.z / uvScale),
                           new Color(1f, 1f, 1f, m));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < segs; k++)
                {
                    int i0 = r * stride + k;
                    a.T.AddRange(new[] { i0, i0 + 1, i0 + stride, i0 + 1, i0 + stride + 1, i0 + stride });
                }
            return a.Build("Env_C_MoonPool");
        }

        private static Mesh CandleMesh(float h, float r, int seed)
        {
            float Lip(float a) => 1f + 0.16f * (Hash3((int)(a * 8), seed, 0, seed) - 0.5f);
            var prof = new List<Vector2>
            {
                new Vector2(r * 1.02f, 0f),
                new Vector2(r * 1.05f, h * 0.12f),
                new Vector2(r * 0.98f, h * 0.55f),
                new Vector2(r * 1.06f * Lip(1), h * 0.88f),   // melt lip
                new Vector2(r * 1.02f, h * 0.97f),
                new Vector2(r * 0.55f, h),                    // cratered top
                new Vector2(r * 0.10f, h * 0.965f),
            };
            return LatheMesh(prof.ToArray(), 10);
        }

        // ====================================================== REAL FIRE ========
        // USER VERDICT, hardware, ModBuild 142:
        //   "Das Feuer im Keller ist eher ein rötlicher Schein - ich möchte lieber
        //    das Teile des Kellers wirklich brennen."
        //
        // The cellar's Fire infusion was a warm rim on the stone (EnvRoom's
        // elemAdd), an ember ring out at the walls, and candle flames that flared.
        // Every one of those is light the COLOUR of fire with nothing burning in
        // it, which is exactly what "ein rötlicher Schein" means. So the room now
        // catches: while Fire is up, six places in the cellar are alight, with
        // real tongues of flame that surge and tear (EnvFlame's _Bonfire), sparks
        // coming off them, and a halo of firelight around each.
        //
        // WHAT BURNS, AND WHY THOSE THINGS. The room may not simply be set on fire
        // all over — a cellar full of flame is a lit room, and the whole tuning
        // history of this room is about darkness. The rule used was: it has to be
        // something that would REALLY catch, it has to be at the periphery, and
        // there has to be a reason in the frame for it to be burning.
        //   CRATE STACK, south wall (-1.55, -3.95). Dry pine boxes with a lit
        //     candle standing on the top one — the only ignition in the room that
        //     needs no explaining at all, because the player has been looking at
        //     the cause for six builds. Two fires: the crate top (the candle's
        //     own fire, grown) and a low one in the litter at its foot.
        //   BARREL GROUP, south-west (-4.25, -2.0 upright, -2.85, -3.95 on its
        //     side). A cask of spirits is the one thing in a cellar that burns
        //     BETTER than the wood around it. The upright one burns at the bung;
        //     the toppled one has spilled, so there is a wide, low, flat pool of
        //     burning oil on the flagstones beside it — a completely different
        //     silhouette from the crate fire, which is what stops five fires from
        //     reading as five copies.
        //   BOOKSHELF, east wall (4.72, 0.70). Paper and dry shelving, with the
        //     shelf candle on top of it. Fire on the top boards and flames coming
        //     out of the shelf below them.
        // REJECTED: the ceiling beams and the plank ceiling. They span the room,
        // including the part of it directly over the board, and nothing may burn
        // over the play space. Also rejected: the table, which is 3.6 m from the
        // centre and the one prop a player leans toward.
        //
        // THE FIRE LIGHT, and what could not be done this round. EnvRoom's baked
        // rig has exactly THREE point slots and all three are candles (_L0.._L2);
        // a fourth source cannot be added without that shader, which belongs to a
        // different lane this round. So a fire does NOT light the masonry through
        // the room's own lighting model. What it does instead is carry its own
        // EnvGlow volume — a real world-space sphere of firelight around the seat
        // of the fire and a second, larger and dimmer one pushed toward the wall
        // behind it, both on the fire's own flicker phase and rate. That reads as
        // the air and the wall near the fire glowing, and it is honest about being
        // additive light rather than a shaded surface. THE HOOK a later round
        // wants: a fourth EnvRoom point slot per fire, driven by e.fire, placed at
        // FireSeats below and flickering on the same rate. Nothing else about the
        // fires would change.
        //
        // ALL OF IT IS GATED ON FIRE. Every mesh here carries EnvFlame/_FireGate
        // or EnvGlow/_ElemGate, every emitter uses an element-owned material, and
        // all three collapse their geometry to a point in the vertex shader while
        // Fire is down. With the master at 0 or the room inert the cellar is the
        // room that was tuned over six rounds, instruction for instruction.

        /// <summary>A FIRE — a bed, the tongues that rise out of it, and the
        /// pieces that tear off and die. Crossed cards on a disc of `radius`,
        /// nothing taller than `height`.
        ///
        /// <para>USER VERDICT, hardware, ModBuild 144: "Das Feuer im Keller sieht
        /// eher aus wie viele Kerzenflammen statt wirklich ein bedrohliches
        /// Brennen der Möbel! Überarbeite das Feuer nochmal komplett." The two
        /// previous constructions both answered "make it a fire" with "make more
        /// candle flames", and the renders of the shipped build are exactly what
        /// he describes: tall amber spikes with black gaps between them, each one
        /// a smooth closed teardrop, standing in a row on a crate.</para>
        ///
        /// <para>THREE KINDS OF CARD, because a fire has three kinds of part, and
        /// the previous bake had one and a half of them:</para>
        ///
        /// <para>THE BED (38% of the cards, and the most important 38%). A fire's
        /// brightest and densest part is a low incandescent mass at the seat,
        /// WIDER THAN IT IS TALL, sitting ON the object. The previous version had
        /// bed cards but drew them with the candle sprite and clamped them to
        /// 2.8:1 to stop the teardrop smearing — so the bed was more teardrops.
        /// These are drawn with an authored BED cell (a wide, holed, cloudy mass:
        /// BuildEnvironments.MakeFireAtlas), they are crowded into the inner 85%
        /// of the seat where several always overlap, they carry over half of the
        /// fire's energy, and they neither surge nor wander — a bed of embers
        /// that slid about would read as a puddle of light. Additively they pile
        /// into one continuous body, and because the temperature ramp is measured
        /// up the WHOLE FIRE (EnvFlame/_FireH) rather than up each card, that body
        /// is the white-blue part.</para>
        ///
        /// <para>THE TONGUES (42%). They rise out of the bed — their bases sit
        /// INSIDE it, which is what stops the fire looking like flames standing on
        /// a lid — surge on their own phase, and the further out one stands the
        /// more it is torn outward and the sooner it dies back.</para>
        ///
        /// <para>THE PUFFS (20%), and this is what nothing in either previous
        /// build had. Pieces of a fire DETACH: they leave the flame body, rise,
        /// cool, redden and go out. A candle's flame never does, and its absence
        /// is one of the two or three strongest reasons an enlarged candle still
        /// reads as a candle. Each puff card is born part-way up the body, rises
        /// UV1.z metres over its own cycle, and is faded in fast and out slowly by
        /// the shader — a pure function of the clock with no birth event and no
        /// state, spread through their lives by UV1.w so a fire always has some at
        /// every age.</para>
        ///
        /// <para>Deterministic in `seed` (Hash3, no Random): every client builds
        /// the same fire, and the animation rides the shared clock, so two players
        /// in one scenario watch the same tongue leap at the same second.</para>
        ///
        /// <para>THE CHANNELS are EnvFlame's contract; see the FIRE REAL block
        /// there. COLOR = (phase, surge, how far out, energy share);
        /// UV1 = (atlas cell, kind, a puff's rise in metres, its place in its own
        /// cycle). Acc is not used because Acc has no UV1, and a fourth kind of
        /// per-card datum is exactly what the previous version ran out of room
        /// for when it needed to say "this one detaches".</para></summary>

        // ==================================================== ART COMPENSATION ==
        // ModBuild 147. THE SPRITE CHANGED; THE FIRE MAY NOT.
        //
        // The atlas under these cards is no longer procedural — it is real fire
        // art (see FireAtlas and Assets/Editor/fire_atlas_pipeline.py). A card's
        // quad is only a WINDOW: what the player sees is the sprite's own drawn
        // mass inside it, so swapping the sprite silently resizes and re-weights
        // every fire in both rooms even though not one vertex moved. Six rounds
        // of tuning went into those sizes and into the additive energy budget,
        // and none of that tuning is about the shape of the mask.
        //
        // So the swap is made SIZE-NEUTRAL by measurement rather than by eye.
        // fire_atlas_pipeline.py's `extent()` reports, per cell, the alpha-
        // weighted RMS extent x2 — a robust "how big is the drawn mass", in cell
        // units — and the same function was run over the atlas being replaced:
        //
        //   cell       OLD w x h        NEW w x h       w      h    mean alpha
        //   bed     0.445 x 0.182   0.404 x 0.139   0.908  0.764   0.192 -> 0.124  (x0.646)
        //   tongueA 0.187 x 0.359   0.301 x 0.408   1.610  1.137   0.098 -> 0.240  (x2.461)
        //   tongueB 0.191 x 0.410   0.250 x 0.355   1.309  0.866   0.125 -> 0.194  (x1.552)
        //   puff    0.364 x 0.433   0.289 x 0.280   0.794  0.647   0.294 -> 0.151  (x0.515)
        //
        // Drawn ENERGY is mean alpha times quad area, and quad area scales with
        // the width factor here (the heights below are 1.00 except the puff's),
        // so the energy factor is chosen to make (width factor) x (alpha ratio) x
        // (energy factor) land on 1.00. The three rows:
        //
        //   BED     w x1.10  h x1.00  E x1.41   0.646 x 1.10 x 1.41 = 1.00
        //           0.75 TALL IS THE POINT and is not corrected — a bed is the
        //           part of a fire that lies on something, and the procedural
        //           cell was a rounded rectangle standing a quarter too proud of
        //           its own seat. The 10 % of width IS corrected: the sideways
        //           feather the pipeline needs (see fire_atlas_pipeline.feather,
        //           and the render that forced it) costs the mass 9 % of its
        //           extent, and the bed is the one card whose whole job is to be
        //           wider than it is tall.
        //   TONGUE  w x0.80  h x1.00  E x0.62   mean 1.46 x 0.80 = 1.17 wide,
        //                                       mean 2.01 x 0.80 x 0.62 = 1.00
        //           NOT x0.68, which would hold the width exactly. The tongues
        //           are deliberately left 17 % FATTER than the ones that shipped,
        //           because "wider" is the one correction this file has written
        //           down twice and never actually got: the old drawn tongue was
        //           0.19 x 0.38 cell units, an aspect of 0.50, which is a candle
        //           flame's proportion whatever is painted inside it. The new one
        //           at x0.80 is 0.22 x 0.38 of a card, aspect 0.58, and the mask
        //           inside it is domed and torn instead of tapering to a point.
        //   PUFF    w x1.30  h x1.30  E x1.15   0.79/0.65 x 1.30 = 1.03 / 0.84;
        //                                       0.515 x 1.69 x 1.15 = 1.00
        //           The imported puff is the smallest of the four relative to its
        //           cell (it is feathered radially to nothing, so it has no edge
        //           of its own), and a detached piece that reads at four metres
        //           is most of what separates this fire from a candle. Scaled
        //           back up, isotropically, so its aspect is the art's.
        //
        // IF THE PIPELINE'S CROPS CHANGE, THIS TABLE IS STALE. That is why the
        // script prints `drawn mass w x h` for every cell on every run: re-derive
        // these six numbers from that line rather than nudging them.
        //
        // Indexed by GHVR_FKIND_* (0 bed, 1 tongue, 2 puff), which is the same
        // order EnvFire.cginc defines them in.
        private static readonly float[] ArtW = { 1.10f, 0.80f, 1.30f };
        private static readonly float[] ArtH = { 1.00f, 1.00f, 1.30f };
        private static readonly float[] ArtE = { 1.41f, 0.62f, 1.15f };

        private static Mesh FireMesh(string name, float radius, float height, int cards, int seed,
                                     float bedFrac = 0.38f)
        {
            var V = new List<Vector3>();
            var UV0 = new List<Vector2>();
            var UV1 = new List<Vector4>();
            var C = new List<Color>();
            var T = new List<int>();

            // `bedFrac` is 0 for a fire that has NO seat — the one climbing the
            // burning snag's bark two metres off the ground. The first bake gave
            // it the standard third of bed cards and the preview showed a wide
            // flat white slab hanging in mid-air across the trunk: a bed is the
            // part of a fire that lies ON something, and a fire licking up bark
            // is not lying on anything.
            int bed = bedFrac <= 0f ? 0 : Mathf.Max(4, Mathf.RoundToInt(cards * bedFrac));
            int puff = Mathf.Max(3, Mathf.RoundToInt(cards * 0.20f));

            for (int i = 0; i < cards; i++)
            {
                bool isBed = i < bed;
                bool isPuff = i >= cards - puff;
                float h0 = Hash3(i, 0, 0, seed), h1 = Hash3(i, 1, 0, seed);
                float h2 = Hash3(i, 2, 0, seed), h3 = Hash3(i, 3, 0, seed);
                float h4 = Hash3(i, 4, 0, seed), h5 = Hash3(i, 5, 0, seed);

                // sqrt-distributed radius: an even spread over a disc crowds the
                // RIM (there is more area out there), and a fire is densest at its
                // seat. The very first draft of this looked like a ring of flames.
                float rr = radius * Mathf.Sqrt(h0)
                           * (isBed ? 0.85f : (isPuff ? 0.55f : 0.95f));
                float ang = h1 * Mathf.PI * 2f;
                var at = new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                float outw = radius > 1e-4f ? Mathf.Clamp01(rr / radius) : 0f;

                float y0, th, tw, cell, kind, rise, cyc;
                Color col;
                if (isBed)
                {
                    // WIDER THAN TALL, and seated at y = 0 — ON the object. The
                    // aspect is the sprite's own (the bed cell is authored about
                    // 2:1), so the mass is not stretched; the fire gets its full
                    // width from several of these overlapping across the seat
                    // rather than from one enormous quad, which is also what makes
                    // the bright part uneven instead of a painted ellipse.
                    y0 = 0f;
                    th = height * (0.24f + 0.12f * h2);
                    // THE WIDTH COMES FROM THE FIRE'S RADIUS, not from the card's
                    // own height, and that is what the second bake fixed. Tied to
                    // the height, a bed card on the burning SPILL — half a metre
                    // of radius and a third of a metre tall — came out 20 cm wide
                    // on a 1.16 m pool, so thirteen of them were thirteen separate
                    // flamelets scattered on the flagstones instead of one sheet
                    // of burning spirits. The clamp stops the same number
                    // stretching the 2:1 sprite past 3.5:1, which is where the
                    // mottling starts reading as a horizontal smear.
                    // ...capped at 2.6:1. The second bake let it stretch to 3.5:1
                    // to cover the wide spill fire out of thirteen cards, and from
                    // a standing eye those came out as flat white PLATES lying on
                    // the flagstones — a 40 cm by 8 cm quad is a plate whatever is
                    // painted on it. A wide fire gets its coverage from MORE bed
                    // cards instead (see the counts at each site).
                    tw = Mathf.Clamp(radius * (0.85f + 0.45f * h3), th * 1.7f, th * 2.6f);
                    cell = 0f;
                    kind = 0f;                       // GHVR_FKIND_BED
                    rise = 0f; cyc = 0f;
                    col = new Color(h1,
                                    0.04f + 0.06f * h2,          // barely surges
                                    0f,                          // and never leans
                                    0.17f + 0.10f * h3);         // carries the mass
                }
                else if (isPuff)
                {
                    // it starts inside the upper body and leaves
                    y0 = height * (0.30f + 0.25f * h2);
                    th = height * (0.22f + 0.16f * h3);
                    tw = th * (0.85f + 0.45f * h4);
                    cell = 3f;
                    kind = 2f;                       // GHVR_FKIND_PUFF
                    rise = height * (0.55f + 0.40f * h5);
                    cyc = h4;                        // its place in its own cycle
                    col = new Color(h1, 0.25f + 0.25f * h2, outw,
                                    0.075f + 0.10f * h3);
                }
                else
                {
                    // A TONGUE'S BASE SITS INSIDE THE BED, not on top of it: the
                    // shipped fire's tongues all started at y = 0 alongside the
                    // bed cards, so the bed was a separate bright object under a
                    // row of flames rather than the thing they were coming out of.
                    //
                    // ...and on a fire with NO bed — the one climbing the burning
                    // snag's bark — they are SPREAD UP the burning face instead.
                    // With every base at the same y and nothing to hide it, the
                    // preview showed a glowing rectangle with a flat bottom edge
                    // nailed across the trunk. Fire on bark starts wherever the
                    // bark caught.
                    y0 = bed == 0 ? height * (0.30f * h4 - 0.04f) : -height * 0.06f;
                    th = height * Mathf.Lerp(1f, 0.45f, outw) * (0.62f + 0.30f * h2);
                    // WIDE, and that is the lesson of both previous bakes: a
                    // tongue as narrow as a candle flame IS a candle flame,
                    // however many of them there are.
                    tw = th * (0.58f + 0.34f * h3);
                    cell = 1f + Mathf.Floor(h5 * 2f);            // one of two shapes
                    kind = 1f;                       // GHVR_FKIND_TONGUE
                    rise = 0f; cyc = 0f;
                    // THE ENERGY SHARES ARE A THIRD OF THE FIRST BAKE'S, and the
                    // render is the argument. This pass is ADDITIVE and a fire is
                    // thirty overlapping cards: at 0.40-0.60 per bed card, eleven
                    // beds crowded into the same 30 cm summed to five, everything
                    // clipped to pure white, and the boundary of the clipped
                    // region traced the CARD EDGES — the crate fire came out with
                    // straight white slabs and a hard-edged white chevron in it,
                    // which is the one artefact that says "this is a stack of
                    // quads" out loud. The peak of a fire still clips; what it
                    // does not do any more is clip over its whole area.
                    col = new Color(h1, 0.55f + 0.45f * h2, outw,
                                    0.11f + 0.14f * (1f - outw) + 0.08f * h3);
                }

                // ---- the art compensation, applied once, in one place --------
                // See the ArtW/ArtH/ArtE block above. It is deliberately OUTSIDE
                // the three branches: every one of those numbers was tuned
                // against a sprite that no longer exists, and a reviewer has to
                // be able to see the whole correction without reading them.
                int akind = isBed ? 0 : (isPuff ? 2 : 1);
                tw *= ArtW[akind];
                th *= ArtH[akind];
                col.a *= ArtE[akind];

                // every card's cross is turned by its own angle: two quads at a
                // fixed 90 deg, repeated a dozen times, is a visible lattice from
                // the two axes that look down it.
                float yaw = h5 * Mathf.PI;
                var extra = new Vector4(cell, kind, rise, cyc);
                for (int q = 0; q < 2; q++)
                {
                    float qa = yaw + q * Mathf.PI * 0.5f;
                    var right = new Vector3(Mathf.Cos(qa), 0f, Mathf.Sin(qa)) * (tw * 0.5f);
                    var lo = at + Vector3.up * y0;
                    int b = V.Count;
                    V.Add(lo - right); UV0.Add(new Vector2(0, 0));
                    V.Add(lo + right); UV0.Add(new Vector2(1, 0));
                    V.Add(lo + right + Vector3.up * th); UV0.Add(new Vector2(1, 1));
                    V.Add(lo - right + Vector3.up * th); UV0.Add(new Vector2(0, 1));
                    for (int k = 0; k < 4; k++) { UV1.Add(extra); C.Add(col); }
                    T.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }

            var m = new Mesh { name = name };
            m.SetVertices(V);
            m.SetUVs(0, UV0);
            m.SetUVs(1, UV1);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>How hard the draught works a flame standing at `at`, as
        /// EnvFlame's _AirGust.
        ///
        /// <para>USER VERDICT, ModBuild 142: "Im Keller sollte es noch mehr wie
        /// ein Windzug wirken der insbesondere aus dem Fenster kommt." Air used to
        /// multiply every flame's lean by the same constant, which is a room that
        /// is windy everywhere — the one thing a draught with a source is not. The
        /// number falls off with the flame's distance from the WINDOW OPENING
        /// (derived, never typed: WindowCentre), so under Air the flames nearest
        /// the aperture are laid over hardest and the far corner barely stirs, and
        /// the gradient itself points at where the air is coming in.</para>
        ///
        /// <para>7.0 near / 1.2 far, against the 3.4 the shader still defaults to:
        /// the near end is stronger than the old uniform value and the far end
        /// much weaker, so the room's total agitation is about what it was and its
        /// DISTRIBUTION is the whole change.</para></summary>
        private static float AirGustAt(Vector3 at)
        {
            var w = WindowCentre();
            float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(w.x, w.z));
            return Mathf.Lerp(7.0f, 1.2f, Mathf.Clamp01(d / 9.0f));
        }

        /// <summary>Build one fire: the mesh, the material and the placement. The
        /// two rooms share it, because a burning bookshelf and a burning log are
        /// the same object with different numbers, and the round that gave the
        /// cellar a fire the user called "viele Kerzenflammen" is not a round to
        /// let a second room drift away from the fix.
        ///
        /// <para>Everything the FLAME's look is tuned by is here, in one place,
        /// so the two rooms cannot disagree about what fire looks like — and the
        /// one number the LIGHT also has to know (FireHz) is a constant rather
        /// than an argument, so it cannot be passed differently in two calls.
        /// That is the standing "a flame and the light it casts share a rate"
        /// rule, made unbreakable.</para></summary>
        private static GameObject BuildFireCards(Transform root, string room, string n,
            Vector3 seat, float radius, float height, int cards, int seed,
            float gust, float phaseOfs, float airGust, Vector3 wind, float bedFrac = 0.38f)
        {
            var mesh = SaveMesh($"Env_{room}_Fire{n}.asset",
                FireMesh($"Env_{room}_Fire{n}", radius, height, cards, seed, bedFrac),
                // The shader stretches a tongue and throws detached puffs most of
                // a fire-height above the seat, so the authored bounds would
                // frustum-cull the top of the fire the moment it surged past a
                // screen edge.
                new Bounds(new Vector3(0f, height * 1.15f, 0f),
                           new Vector3(radius * 3f + 0.5f, height * 3.4f, radius * 3f + 0.5f)));
            var m = NewRoomMat($"{room}_Fire{n}.mat", "GloomhavenVR/EnvFlame");
            // THE SPRITE IS NOT A CANDLE FLAME ANY MORE, and that is the first of
            // the three fixes this round: `candle_flame_alb` is one smooth closed
            // teardrop, and a smooth closed teardrop is a candle at every size.
            m.SetTexture("_MainTex", FireAtlas());
            // The tint is neutral: ALL of the colour comes from the three-stop
            // temperature ramp below, which is the thing that makes a fire read as
            // hot rather than as orange.
            m.SetColor("_Tint", new Color(1f, 0.94f, 0.86f, 1f));
            // WHITE-BLUE at the seat, orange through the body, dark red where the
            // tongues tear off. Both previous fires went amber -> amber, i.e. they
            // had no white in them anywhere; a fire with no white in it is a light
            // source painted the colour of fire, which is the note the ModBuild
            // 142 verdict already made about the round before it. The base stop is
            // deliberately over 1 in every channel: this is an ADDITIVE pass and
            // the seat of a fire is the one thing in a cellar that clips.
            // 1.52/1.30/1.06 and not the first bake's 1.70/1.52/1.34: the seat has
            // to be the hottest thing in the frame, but pushed to a NEUTRAL white
            // it clipped all three channels together and the fire came out pale
            // instead of hot. Keeping blue and green a step under red leaves the
            // clip yellow-white, which is the colour of something at 1300 K, and
            // the blue is still there in the unclipped fringe where it reads.
            m.SetColor("_BaseCol", new Color(1.78f, 1.32f, 0.86f, 1f));
            m.SetColor("_CoreCol", new Color(1.46f, 0.52f, 0.12f, 1f));
            m.SetColor("_TipCol", new Color(0.60f, 0.085f, 0.018f, 1f));
            m.SetFloat("_Bonfire", 1f);
            m.SetFloat("_FireGate", 1f);      // exists only under the infusion
            m.SetFloat("_FireH", height);     // the ramp is measured up the FIRE
            m.SetFloat("_Sway", 0.055f);
            m.SetFloat("_Flicker", 0.55f);
            // 0.48: a tongue that can grow to 1.5x its own length is visible from
            // four metres and still belongs to the mass it comes out of.
            m.SetFloat("_Lick", 0.48f);
            m.SetFloat("_Flare", 0.20f);
            // THE RATE, in Hz, and the same constant the wash on the stone is
            // written with (LightRig.fireHz). See FireHz for the measurement that
            // condemned the old 0.6-1.0 Hz sway.
            m.SetFloat("_FireHz", FireHz);
            // ...and how often a card tears off. 0.55 Hz per card, so with six to
            // eight puff cards a fire sheds something about every quarter second —
            // often enough to be a property of the fire, rare enough that each one
            // is a separate event to the eye.
            m.SetFloat("_PuffHz", 0.55f);
            m.SetFloat("_Phase", phaseOfs);
            // _Rate is the CANDLE family's rate and is only used by this material
            // for the fragment's UV wobble; the bonfire path runs on _FireHz.
            m.SetFloat("_Rate", 1f);
            m.SetFloat("_Gust", gust);
            // ...the room's OWN air. A fire leaning along the cellar's draught and
            // a fire leaning along the wood's wind is the same term with the two
            // rooms' two authored directions, and it is what ties a fire to the
            // rest of the room rather than leaving it a self-contained animation.
            m.SetVector("_GustDir", wind);
            m.SetFloat("_AirGust", airGust);
            return Place(root, $"Fire{n}", mesh, seat, Vector3.zero, Vector3.one, m);
        }

        /// <summary>The cellar's fires, the light they throw and their sparks.
        /// Called from BuildCellarRoom once the props are stacked, because every
        /// seat is derived from the real surface the fire stands on.</summary>
        private static void AddCellarFire(Transform root, LightRig rig,
                                          float crateTop, float shelfTop)
        {
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            // The barrel's top is measured off the placed prop rather than taken
            // from a second copy of its height: `Prop` grounds it against the
            // undulating flagstones, so a typed number would float or sink.
            var barrel = root.Find("Barrel1");
            float barrelTop = barrel != null
                ? SurfaceYAt(barrel.gameObject, -4.25f, -2.0f)
                : throw new Exception("Cellar fire: prop 'Barrel1' is missing — the cask cannot burn.");

            int tris = 0, fires = 0, particles = 0, emitters = 0;

            // Every fire's seat, so the log has one list to read.
            var seats = new List<(string n, Vector3 at, float r, float h, int cards)>();

            void Fire(string n, Vector3 seat, float radius, float height, int cards,
                      float phaseOfs, int seed, float gust, bool onShelf = false)
            {
                var go = BuildFireCards(root, "C", n, seat, radius, height, cards, seed,
                                        gust, phaseOfs, AirGustAt(seat), DraftDir);
                if (onShelf)
                {
                    // SHELF RIDERS — "Die Kerzen UND DAS FEUER, die auf dem
                    // Bücherregal stehen, kippen nicht mit". A burning shelf that
                    // topples is still burning, so these ride rigidly and do NOT
                    // gutter; the 0.35 stiffness is the small amount by which a
                    // bed of fire lying on a board that is turning under it goes
                    // on pointing up.
                    RideShelf(go.GetComponent<MeshRenderer>().sharedMaterial,
                              self: 1f, lit: -1f, gutter: 0f, stiff: 0.35f);
                    WriteShelfTip(go.GetComponent<MeshRenderer>().sharedMaterial, go.transform);
                }
                tris += cards * 4; fires++;
                seats.Add((n, seat, radius, height, cards));
            }

            // A fire's AIR GLOW: the near halo at the seat, and a bigger, dimmer
            // one pushed toward the wall behind it. One material for both — EnvGlow
            // reads no baked light rig, so unlike every EnvRoom material in this
            // room two transforms may share it without being lit from the first
            // one's position. The wall halo is a SPHERE and not a flat wash card:
            // a flat additive card near a surface the player can get level with
            // becomes a bright line the moment the view drops into its plane, and
            // this room has already paid for that lesson once (see the rejected
            // sill glows in the moonlight block).
            //
            // DIMMER THAN ModBuild 144's, and that is a consequence of the fire
            // wash finally existing: a halo used to be the ONLY light a fire
            // threw, so it was pushed until the wall behind the fire looked lit.
            // The wall is now genuinely lit, by a term that respects its normal
            // and its albedo, and a halo on top of that at the old strength is a
            // second, flatter copy of the same light. What is left is what a halo
            // honestly is: the AIR around a fire glowing.
            void Halo(string n, Vector3 at, float r, float alpha, Vector3 wallAt,
                      float wallR, float wallAlpha, float phaseOfs, bool onShelf = false)
            {
                if (glowMesh == null) return;
                var g = NewRoomMat($"C_FireGlow{n}.mat", "GloomhavenVR/EnvGlow");
                g.SetColor("_Tint", new Color(1f, 0.47f, 0.16f, alpha));
                // NOT a candle: a fire's halo answers Light and Dark in full.
                // Written out rather than left to the default so the distinction
                // is auditable from this file instead of from an absence.
                g.SetFloat("_ElemCandle", 0f);
                g.SetFloat("_Falloff", 1.85f);
                g.SetFloat("_Flicker", 0.85f);
                // the halo breathes at the fire's rate, not at a candle slot's
                g.SetFloat("_Rate", FireHz * FireHaloRate);
                g.SetFloat("_Phase", phaseOfs);
                g.SetFloat("_ElemGate", 1f);      // collapses while Fire is down
                var go = Place(root, $"FireGlow{n}", glowMesh, at, Vector3.zero,
                               Vector3.one * r, g);
                if (onShelf)
                {
                    RideShelf(g, self: 1f, lit: -1f, gutter: 0f, stiff: 0f);
                    WriteShelfTip(g, go.transform);
                }
                if (wallR > 0f)
                {
                    var w = NewRoomMat($"C_FireWash{n}.mat", "GloomhavenVR/EnvGlow");
                    w.SetColor("_Tint", new Color(1f, 0.40f, 0.13f, wallAlpha));
                    w.SetFloat("_ElemCandle", 0f);      // fire, not a candle
                    // softer edge than the near halo: this is the glow ON
                    // something, so it may not have a core of its own
                    w.SetFloat("_Falloff", 1.10f);
                    w.SetFloat("_Flicker", 0.85f);
                    w.SetFloat("_Rate", FireHz * FireHaloRate);
                    w.SetFloat("_Phase", phaseOfs);
                    w.SetFloat("_ElemGate", 1f);
                    // ...and this one deliberately does NOT ride the shelf, even
                    // for the fire that does: it is the glow ON THE WALL, and the
                    // wall does not fall over. What it gets wrong for twenty-six
                    // seconds of a haunt slot is that the wall is still glowing
                    // above a fire that has come down off it; what riding would
                    // get wrong is a sphere of light swinging through masonry.
                    Place(root, $"FireWash{n}", glowMesh, wallAt, Vector3.zero,
                          Vector3.one * wallR, w);
                }
            }

            // Sparks off a fire. FX_ElemEmber is already owned by Fire
            // (EnvParticleAdd's gate), so these cost one collapsed quad per
            // particle while the infusion is down — the simulation and the draw
            // call remain, because nothing in the bundle can switch a Shuriken
            // emitter off. The forest's flying sparks are what the user singled
            // out as liked; these are the same sprite and the same colour, coming
            // off something that is actually burning.
            //
            // THEY DO NOT RIDE THE SHELF, and nothing can make them: a Shuriken
            // system simulates in world space on the CPU and this bundle has no
            // scripts, so the sparks off the burning bookshelf go on rising from
            // where the shelf was standing. It is the one rider that could not be
            // taken along, it is stated in the bake log, and it is the least
            // visible of them — a spark is 2 cm and is already leaving.
            void Sparks(string n, Vector3 at, float spread, int maxAlive, float rate)
            {
                var ps = ElemPS(root, $"FireSparks{n}", at, "FX_ElemEmber.mat", maxAlive);
                var m = ps.main;
                m.duration = 9f;
                // short: an ember off a crate is out within a couple of metres,
                // and a long-lived one ends up at the ceiling where it reads as a
                // firefly indoors
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.15f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.042f);
                m.startColor = Color.white;
                m.gravityModifier = -0.045f;   // hot: they are carried UP
                var e = ps.emission; e.rateOverTime = rate;
                var sh = ps.shape; sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 26f;
                sh.radius = spread;
                sh.radiusThickness = 1f;
                // the cone's axis is its local +Z; stood on end it throws upward
                ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                // ...and the room's own draught takes them, so the sparks agree
                // with the flames about which way the air is going
                v.x = WindRange(DraftDir.x, 0.10f, 0.30f);
                v.z = WindRange(DraftDir.z, 0.10f, 0.30f);
                v.y = new ParticleSystem.MinMaxCurve(0.30f, 0.85f);
                var no = ps.noise; no.enabled = true; no.quality = ParticleSystemNoiseQuality.Low;
                no.strength = 0.14f; no.frequency = 0.8f; no.scrollSpeed = 0.5f;
                ElemFade(ps, 0.06f, 0.42f);   // they burn out, they do not fade away
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.55f, 0.7f), new Keyframe(1f, 0.15f)));
                particles += maxAlive; emitters++;
            }

            float hw = CW / 2f, hd = CD / 2f;

            // SCALE. USER, ModBuild 144: the fires read as candle flames, and one
            // of the reasons is that they were candle-sized. A burning crate's
            // flame is half a metre to three quarters ACROSS; the shipped crate
            // fire was 0.52 m across and 0.46 m tall, i.e. as tall as it was wide,
            // which is a flame rather than a fire. Every radius below is up and
            // every fire is now WIDER THAN IT IS TALL at its bed, which is the
            // silhouette the eye reads as "an object is alight" rather than as
            // "something is standing here burning".

            // ---- 1. the crate stack, lit by the candle standing on it --------
            var crateSeat = new Vector3(-1.55f, crateTop, -3.95f);
            Fire("CrateTop", crateSeat, 0.32f, 0.58f, 30, 0f, 7401, 0.050f);
            var litter = new Vector3(-0.95f, CellarFloorY(-0.95f, -3.75f), -3.75f);
            Fire("CrateFoot", litter, 0.38f, 0.30f, 36, 1.7f, 7402, 0.040f);
            // The wall halo is pushed hard against the masonry (0.22 m off it)
            // and held to 1.15 m: AssertPlaySpaceClear counts a glow sphere like
            // any other geometry, and an earlier bake failed on exactly this one —
            // a 1.45 m sphere 0.55 m off the south wall reaches 2.73 m from the
            // centre, i.e. into the board's air. That check is the reason the
            // wall halos are sized the way they are, and it is a good reason:
            // a sphere of firelight the player can put his head inside is not a
            // wall being lit.
            Halo("Crate", crateSeat + new Vector3(0f, 0.30f, 0f), 0.66f, 0.15f,
                 new Vector3(-1.35f, 1.55f, -hd + 0.22f), 1.15f, 0.028f, 0f);
            Sparks("Crate", crateSeat + new Vector3(0f, 0.34f, 0f), 0.26f, 18, 8.5f);

            // ---- 2. the casks: one burning at the bung, one spilled ----------
            var bung = new Vector3(-4.25f, barrelTop, -2.0f);
            // 0.28 and not 0.24: the bake log's own aspect check flagged this one
            // as the only fire in the room still taller than it is wide, and the
            // check is there because that silhouette is the candle silhouette.
            Fire("Barrel", bung, 0.28f, 0.48f, 26, 3.1f, 7403, 0.045f);
            // the spill: WIDE and LOW, so it reads as a burning floor rather than
            // as a third small bonfire. It is the one fire in the room whose
            // shape says what is on fire — and at 1.16 m across it is now the
            // widest thing alight in the cellar, which is what a pool of burning
            // spirits on flagstones is.
            var spill = new Vector3(-3.25f, CellarFloorY(-3.25f, -3.35f), -3.35f);
            Fire("Spill", spill, 0.58f, 0.32f, 52, 4.9f, 7404, 0.035f);
            Halo("Barrel", bung + new Vector3(0f, 0.26f, 0f), 0.58f, 0.14f,
                 new Vector3(-hw + 0.55f, 1.35f, -2.6f), 1.35f, 0.026f, 3.1f);
            Halo("Spill", spill + new Vector3(0f, 0.18f, 0f), 0.78f, 0.12f,
                 Vector3.zero, 0f, 0f, 4.9f);
            Sparks("Barrel", bung + new Vector3(0f, 0.28f, 0f), 0.20f, 14, 6.0f);
            Sparks("Spill", spill + new Vector3(0f, 0.14f, 0f), 0.50f, 16, 7.0f);

            // ---- 3. the bookshelf: paper and dry boards ----------------------
            // ...and this is the site that TOPPLES. Both fires and the near halo
            // ride the shelf; the wall wash and the sparks do not (see their
            // blocks for why, and the bake log states it).
            var shelfSeat = new Vector3(OnShelf(-0.14f, 0f).x, shelfTop, OnShelf(-0.14f, 0f).z);
            Fire("ShelfTop", shelfSeat, 0.30f, 0.52f, 28, 0f, 7405, 0.045f, onShelf: true);
            // out of the shelf itself, below the top boards — a bookshelf burns
            // from the inside out, and a fire that only sits on the lid of a
            // thing does not read as the thing being alight
            var shelfMid = new Vector3(4.60f, 1.34f, 0.70f);
            Fire("ShelfMid", shelfMid, 0.24f, 0.38f, 22, 2.3f, 7406, 0.045f, onShelf: true);
            Halo("Shelf", shelfSeat + new Vector3(-0.05f, 0.28f, 0f), 0.62f, 0.14f,
                 new Vector3(hw - 0.50f, 1.85f, 0.70f), 1.40f, 0.026f, 0f, onShelf: true);
            Sparks("Shelf", shelfSeat + new Vector3(0f, 0.30f, 0f), 0.24f, 16, 7.5f);

            // ================= THE LIGHT THE FIRE THROWS =======================
            // USER, ModBuild 144: "... und auch die Lichtverhältnisse entsprechend
            // anpassen." The half of this that was missing is the one that matters
            // most: it lights what it stands on. EnvRoom and EnvGround have
            // carried the receiving term since ModBuild 144 and nothing wrote it,
            // so a cellar could be alight in six places and its flagstones stayed
            // the colour of a cellar with three candles in it. The halos above
            // were standing in for it, and a halo is additive air — it cannot
            // brighten the top of a crate and leave its shaded side dark, which is
            // the single strongest cue that a thing is being LIT by something.
            //
            // THREE SEATS FOR SIX FIRES, and it is the right granularity rather
            // than a shortage: a crate top and the litter burning at its foot are
            // forty centimetres apart, and every surface more than a metre away is
            // lit by their sum. So the sites are the crate stack, the casks and
            // the bookshelf — which is also exactly the three the room reads as
            // "places that are on fire".
            //
            // The RANGE is a fire's, not a candle's: the candles were pulled down
            // to 2.6-3.1 m with _PtHard 16 on top, because "die Kerzen beleuchten
            // hier viel zu viel" (ModBuild 134). A fire is not a candle and the
            // wash deliberately does not take _PtHard (see EnvFire.cginc), so
            // these reach across their own end of the room and die before the
            // other one.
            rig.fires = new[]
            {
                // the crate stack: between the two fires, a little above the top one
                new FireSeat("Crates", new Vector3(-1.32f, crateTop + 0.22f, -3.86f), 2.6f, false),
                // the casks: between the bung and the spill
                new FireSeat("Casks", new Vector3(-3.74f, barrelTop * 0.55f + 0.18f, -2.72f),
                             2.8f, false),
                // the bookshelf, and this one MOVES: it is standing on the shelf
                // that topples, so its seat takes the same rigid transform the
                // flames on it take (EnvFire.cginc's _FireRide).
                new FireSeat("Shelf", new Vector3(OnShelf(-0.24f, 0f).x, shelfTop - 0.28f, OnShelf(-0.24f, 0f).z), 2.5f, true),
            };
            // THE WASH. Warm, and strong enough that the flagstones a fire stands
            // on and the wall a metre behind it are plainly lit by it — the term
            // is multiplied by the surface's own albedo and by N.L, so it lands as
            // light on stone rather than as an orange film over the picture. The
            // alpha is the flicker DEPTH: 0.45 means the pool swings +-45%, which
            // is what "unsteadily" means and is only possible because GhvrWave4 is
            // bounded.
            //
            // 1.05 AND 2.8-3.1 m RANGES, down from the first bake's 1.55 and
            // 3.3-3.7 m, and the render is the argument. At the first numbers the
            // three seats reached each other and the whole cellar came up to an
            // even orange — a lit room, which is the one thing six rounds of
            // tuning this cellar have been spent on not having. A fire lights
            // what it stands on; it does not light the room. What is here now
            // leaves the two corners furthest from anything burning as dark as
            // they are with the fire down.
            rig.fireWash = new Color(0.80f, 0.32f, 0.10f, 0.45f);
            rig.fireHz = FireHz;

            float playR = CellarPlaySpaceDia * 0.5f;
            var log = new System.Text.StringBuilder();
            log.Append($"[GloomhavenVR][Env] FIRE REAL (cellar, gated on the Fire infusion): "
                       + $"{fires} fires, {tris} tris, {emitters} spark emitters, "
                       + $"{particles} spark particles max alive. Turbulence {FireHz:F1} Hz "
                       + $"(was 0.63-1.03 Hz, which is why it read as candle flames).\n");
            foreach (var (n, at, r, h, c) in seats)
                log.Append($"    burns: {n,-9} seat ({at.x,6:F2},{at.y,5:F2},{at.z,6:F2})  "
                           + $"{r * 2f:F2} m across x {h:F2} m tall ({c} cards, "
                           + $"{(r * 2f > h ? "wider than tall" : "TALLER THAN WIDE — check it")}), "
                           + $"{new Vector2(at.x, at.z).magnitude:F2} m from the room centre "
                           + $"(PlaySpace radius {playR:F2} m), _AirGust {AirGustAt(at):F2}\n");
            foreach (var f in rig.fires)
                log.Append($"    lights: {f.name,-7} seat ({f.pos.x,6:F2},{f.pos.y,5:F2},"
                           + $"{f.pos.z,6:F2})  range {f.range:F2} m"
                           + (f.ridesShelf ? "  RIDES THE TIPPING SHELF" : "") + "\n");
            log.Append($"    wash: rgb ({rig.fireWash.r:F2},{rig.fireWash.g:F2},"
                       + $"{rig.fireWash.b:F2}) at +-{rig.fireWash.a * 100f:F0} % flicker, "
                       + $"{rig.fireHz:F1} Hz — THE SAME Hz every flame above burns at "
                       + "(one constant, one function: GhvrFireFlicker).\n");
            log.Append("    air: one EnvGlow halo at each seat plus a wall halo behind it. It is "
                       + "the air around the fire glowing and nothing else now — the surfaces are "
                       + "lit by the wash above, which is what a halo never could do.\n");
            log.Append("    cost when Fire is down: every flame card and every halo collapses to "
                       + "a point in the vertex shader (zero-area triangles, no fill); the fire "
                       + "wash is inside `if (e.fire > 0)` and its colour is black; the spark "
                       + "emitters keep simulating and keep one draw call each.");
            Debug.Log(log.ToString());
        }

        // ==================================================== THE WOOD ON FIRE ==
        // USER VERDICT, hardware, ModBuild 144: "Feuer im Wald ist noch nicht
        // implementiert, Teile der Bäume sollen brennen!"
        //
        // WHAT BURNS, AND WHY THOSE THINGS. Three rules decided it, and they are
        // the cellar's three:
        //   1. IT MUST BE SOMETHING THAT WOULD REALLY CATCH. A living fir does
        //      not go up like a torch; dead wood does. So: the deadfall log at
        //      the clearing edge, the dry-branch pile in the understorey, and a
        //      TRUNK — chosen from the ones the wood generator marked `dead`,
        //      i.e. a snag with a broken top and no crown, which is the one tree
        //      in a wet wood that burns standing.
        //   2. IT MUST BE PERIPHERAL. Everything below is 6.5 m or more from the
        //      centre against a 4.5 m PlaySpace radius, on the far side of the
        //      tree line, and NOTHING is over the board — the same line the
        //      easter eggs are held to ("niemals den Spielfluss stören").
        //   3. IT MAY NOT REPAINT A WOOD HE HAS APPROVED. Nothing here changes a
        //      material, a tint or a light that exists when Fire is down: the
        //      flames are new gated geometry that collapses to a point, the wash
        //      is inside `if (e.fire > 0)` with a black colour, and the halos
        //      carry _ElemGate. With Fire down this function has added nothing
        //      the renderer can see.
        //
        // AND THE THREE SILHOUETTES ARE DIFFERENT, which is the lesson from the
        // cellar's first bake (five fires that read as five copies): a trunk
        // burns as a TALL NARROW column hugging a vertical, a fallen log burns as
        // a LINE of low fires along its own axis, and brushwood burns as one wide
        // flat bed. Three shapes, three readings, one shader.
        //
        // THE SPARKS HE ALREADY LIKES STAY UNTOUCHED. The forest's flying embers
        // are AddElementFX's, they are the thing he singled out as good, and this
        // function does not go near them; what it adds is sparks coming OFF the
        // things that are now actually alight, from the same sprite and the same
        // gated material.
        private static void AddForestFire(Transform root, LightRig rig, List<Tree> trees)
        {
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            int tris = 0, fires = 0, particles = 0, emitters = 0;
            var seats = new List<(string n, Vector3 at, float r, float h, int cards)>();

            void Fire(string n, Vector3 seat, float radius, float height, int cards,
                      float phaseOfs, int seed, float gust, float bedFrac = 0.38f)
            {
                BuildFireCards(root, "S", n, seat, radius, height, cards, seed,
                               gust, phaseOfs, 2.2f, ForestWind, bedFrac);
                tris += cards * 4; fires++;
                seats.Add((n, seat, radius, height, cards));
            }

            void Halo(string n, Vector3 at, float r, float alpha, float phaseOfs)
            {
                if (glowMesh == null) return;
                var g = NewRoomMat($"S_FireGlow{n}.mat", "GloomhavenVR/EnvGlow");
                g.SetFloat("_ElemCandle", 0f);          // fire, not a candle
                g.SetColor("_Tint", new Color(1f, 0.45f, 0.15f, alpha));
                g.SetFloat("_Falloff", 1.70f);
                g.SetFloat("_Flicker", 0.85f);
                g.SetFloat("_Rate", FireHz * FireHaloRate);
                g.SetFloat("_Phase", phaseOfs);
                g.SetFloat("_ElemGate", 1f);
                Place(root, $"FireGlow{n}", glowMesh, at, Vector3.zero, Vector3.one * r, g);
            }

            void Sparks(string n, Vector3 at, float spread, int maxAlive, float rate)
            {
                var ps = ElemPS(root, $"FireSparks{n}", at, "FX_ElemEmber.mat", maxAlive);
                var m = ps.main;
                m.duration = 9f;
                // LONGER-LIVED than the cellar's, because out here they have
                // somewhere to go: an ember off a burning snag rides eight metres
                // up through the crowns instead of hitting a plank ceiling.
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 4.0f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.40f, 1.30f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.044f);
                m.startColor = Color.white;
                m.gravityModifier = -0.055f;
                var e = ps.emission; e.rateOverTime = rate;
                var sh = ps.shape; sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 24f;
                sh.radius = spread;
                sh.radiusThickness = 1f;
                ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                // the wood's own wind, so the embers off the fire and the leaves
                // in the air agree about which way the night is moving
                v.x = WindRange(ForestWind.x, 0.12f, 0.38f);
                v.z = WindRange(ForestWind.z, 0.12f, 0.38f);
                v.y = new ParticleSystem.MinMaxCurve(0.35f, 1.05f);
                var no = ps.noise; no.enabled = true; no.quality = ParticleSystemNoiseQuality.Low;
                no.strength = 0.16f; no.frequency = 0.7f; no.scrollSpeed = 0.5f;
                ElemFade(ps, 0.06f, 0.40f);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.55f, 0.7f), new Keyframe(1f, 0.15f)));
                particles += maxAlive; emitters++;
            }

            // ---- 1. THE SNAG. "Teile der Bäume sollen brennen" ---------------
            // CHOSEN, not typed. The trunk has to be one the generator really
            // placed — a hand-written position would be inside a tree in one bake
            // and in mid-air in the next, and this room's whole trunk field is
            // seeded. The rule: a DEAD one (broken top, no crown — the only kind
            // that burns standing in a wet wood), in the first band, at 6.5-9.5 m,
            // and as far from the moon's bearing as possible, so the burning tree
            // is the one light source on the side of the clearing the moon does
            // not reach. Deterministic and re-derived every bake.
            var moonHoriz = new Vector3(MoonDir.x, 0f, MoonDir.z).normalized;
            Tree snag = default; bool found = false; float bestDot = 2f;
            foreach (var t in trees)
            {
                if (!t.dead || t.band != 0) continue;
                float r = t.p.magnitude;
                if (r < 6.5f || r > 9.5f) continue;
                float d = Vector2.Dot(t.p.normalized, new Vector2(moonHoriz.x, moonHoriz.z));
                if (!found || d < bestDot) { snag = t; bestDot = d; found = true; }
            }
            if (!found)
                throw new Exception("Forest fire: no dead snag in the first trunk band between 6.5 "
                                    + "and 9.5 m. The burning tree is chosen from the trunks the "
                                    + "generator really placed (see the block above) — if the band "
                                    + "table or the 1-in-9 dead rule changed, this search has to be "
                                    + "widened rather than a position typed in.");
            // The fires hug the trunk: their radius comes from the trunk's own
            // radius at that height (HauntTrunkRadius, the same function the
            // apparition that hides behind a tree is sized by), so a thin snag
            // gets a thin fire and a thick one a broad one.
            var foot = TrunkAt(snag, 0.10f);
            var mid = TrunkAt(snag, 1.85f);
            float rFoot = HauntTrunkRadius(snag, 0.10f), rMid = HauntTrunkRadius(snag, 1.85f);
            // the root flare is burning widest — that is where the litter is
            Fire("Snag0", foot, rFoot * 2.1f, 1.05f, 42, 0f, 7501, 0.050f);
            // ...and it is climbing the bark, narrower, taller, and with NO BED:
            // see FireMesh's bedFrac. A bed is the part of a fire that lies on
            // something; two metres up a trunk there is nothing to lie on, and
            // the first bake's bed cards there read as a white slab nailed across
            // the tree.
            Fire("Snag1", mid, rMid * 1.5f, 0.95f, 24, 2.6f, 7502, 0.055f, bedFrac: 0f);
            Halo("Snag", foot + new Vector3(0f, 0.75f, 0f), 1.35f, 0.16f, 0f);
            Sparks("Snag", mid + new Vector3(0f, 0.45f, 0f), 0.28f, 22, 9.0f);

            // ---- 2. THE DEADFALL LOG at the clearing edge --------------------
            // Log1 is at (6.9, 5.0) yawed 128 deg, i.e. 8.5 m out, and it burns
            // ALONG ITS OWN AXIS: three low fires spaced down the log rather than
            // one on top of it. A line of fire is a completely different
            // silhouette from a cone of it, and it is the shape that says "this
            // long thing is what is alight".
            Vector3 logSeat;
            var logGo = root.Find("Log1");
            if (logGo == null)
                throw new Exception("Forest fire: prop 'Log1' is missing — the deadfall cannot burn.");
            {
                // THE LOG'S AXIS IS MEASURED, not taken from the yaw it was placed
                // with. `Rest` re-centres a prop on its own footprint and the
                // photoscan's long axis is not the mesh's local +Z, so the first
                // attempt at this — three points along Quaternion.Euler(0,128,0) —
                // put one of them inside the log's bounding box but off the log,
                // and the bake failed on "a ray down misses 'Log1' entirely". The
                // axis below is the direction of greatest spread of the placed
                // vertices in XZ, i.e. the log's own line, whatever it was rotated
                // by. Two dot products, no matrices: the covariance of a set of
                // points about their mean is diagonalised in closed form in 2D.
                var pts = WorldVerts(logGo.gameObject);
                var mean = Vector2.zero;
                foreach (var p in pts) mean += new Vector2(p.x, p.z);
                mean /= Mathf.Max(pts.Count(), 1);
                float sxx = 0f, szz = 0f, sxz = 0f;
                foreach (var p in pts)
                {
                    float dx = p.x - mean.x, dz = p.z - mean.y;
                    sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
                }
                float th = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
                var la = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                float half = 0f;
                foreach (var p in pts)
                    half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(
                        new Vector3(p.x - mean.x, 0f, p.z - mean.y), la)));
                var (lw, lt) = WorldMesh(logGo.gameObject);
                var logMid = new Vector3(mean.x, 0f, mean.y);
                for (int i = 0; i < 3; i++)
                {
                    // 0.62 of the half-length, not 1.0: the ends of a photoscanned
                    // log taper to nothing and a fire seated on the last ten
                    // centimetres of it hangs in the air.
                    float u = (i - 1) * half * 0.62f;
                    var at = logMid + la * u;
                    // ...and walk inward until the ray really lands on the log, so
                    // a re-scanned or re-scaled asset cannot fail the bake over a
                    // seat two centimetres past the bark.
                    float y = 0f; bool hit = false;
                    for (int k = 0; k < 10 && !hit; k++)
                    {
                        hit = RayDown(lw, lt, at.x, at.z, out y);
                        if (!hit) at = Vector3.Lerp(at, logMid, 0.18f);
                    }
                    if (!hit)
                        throw new Exception("Forest fire: no point on 'Log1' under the seat for "
                                            + $"Log{i} — the deadfall's measured axis "
                                            + $"({la.x:F2},{la.z:F2}) does not lie on the prop.");
                    at.y = y;
                    Fire($"Log{i}", at, 0.34f - 0.04f * Mathf.Abs(i - 1), 0.42f, 22,
                         1.3f * i, 7510 + i, 0.045f);
                    if (i == 1) Sparks("Log", at + new Vector3(0f, 0.24f, 0f), 0.30f, 16, 6.5f);
                }
                logMid.y = 0.42f;
                Halo("Log", logMid, 1.10f, 0.14f, 1.9f);
                logSeat = logMid;
            }

            // ---- 3. THE BRUSHWOOD in the understorey -------------------------
            // Branches1, the dry-branch pile at (-6.9, -5.0): one wide, flat,
            // low bed. It is the fire whose shape says "the ground itself is
            // catching", and it is on the opposite side of the clearing from the
            // log, so the wood is lit from two bearings and the trunks between
            // them stand up as volumes.
            var brush = new Vector3(-6.9f, ForestY(-6.9f, -5.0f) + 0.06f, -5.0f);
            Fire("Brush", brush, 0.62f, 0.40f, 50, 3.4f, 7520, 0.040f);
            Halo("Brush", brush + new Vector3(0f, 0.26f, 0f), 0.95f, 0.15f, 3.4f);
            Sparks("Brush", brush + new Vector3(0f, 0.18f, 0f), 0.52f, 18, 7.5f);

            // ================= THE LIGHT ======================================
            // Three sites, one per burning thing, and in the wood the wash lands
            // almost entirely on the GROUND — which is EnvGround, which has
            // carried the receiving term since ModBuild 144 with nothing writing
            // it. RANGES are longer than the cellar's because there is nothing
            // out here to stop the light: a fire in a wood lights the floor for
            // five or six metres and the trunks around it, and then the wood
            // swallows it.
            // A SEAT MAY NOT BE INSIDE THE THING IT IS LIGHTING. The snag's fire
            // is authored on the TRUNK'S AXIS, which is where the flames belong —
            // and where a point light is useless, because every triangle of the
            // trunk faces AWAY from its own centre line and N.L is negative on all
            // of them. The first bake's preview showed it exactly: a burning tree
            // whose bark was unlit blue-grey, with two red patches where the mesh
            // happened to curve back. So the SEAT (and only the seat) is pushed
            // out of the trunk toward the clearing by its own radius plus a
            // handspan, which is roughly where the luminous part of the flame
            // sheet really is.
            var toClearing = new Vector3(-foot.x, 0f, -foot.z).normalized;
            // rFoot + 0.55 and not + 0.20: at a fifth of a metre the seat was
            // barely in front of the bark it was meant to be lighting, so N.L on
            // the trunk's own face was still near zero and the burning tree stayed
            // blue-grey. Half a metre out is roughly where the luminous sheet of
            // a fire licking up a trunk actually stands.
            var snagSeat = foot + new Vector3(0f, 0.55f, 0f)
                           + toClearing * (rFoot + 0.55f);
            rig.fires = new[]
            {
                new FireSeat("Snag", snagSeat, 6.0f, false),
                new FireSeat("Log", logSeat + new Vector3(0f, -0.07f, 0f), 5.2f, false),
                new FireSeat("Brush", brush + new Vector3(0f, 0.22f, 0f), 5.0f, false),
            };
            // DIMMER THAN THE CELLAR'S, and that is the room's whole recipe
            // rather than a taste: the wood is tuned so that "hinter den Bäumen
            // außerhalb der Lichtung soll es so dunkel sein das man sich nicht
            // traut dahinter hinweg zu gehen", and a fire that lit the tree line
            // would undo the one thing ModBuild 133 and 134 were spent on. What
            // it does instead is light its OWN patch brightly and let the rest of
            // the wood stay black — which is also what a real fire in a wood does,
            // and it is why the burning snag reads as a place rather than as a
            // lamp. 0.55 flicker depth, deeper than the cellar's 0.45: out here
            // the fire is the only thing moving the light, so the swing is the
            // whole signal.
            rig.fireWash = new Color(0.92f, 0.35f, 0.11f, 0.55f);
            rig.fireHz = FireHz;

            float playR = ForestPlaySpaceDia * 0.5f;
            var log = new System.Text.StringBuilder();
            log.Append($"[GloomhavenVR][Env] FIRE REAL (forest, gated on the Fire infusion) — user: "
                       + $"\"Feuer im Wald ist noch nicht implementiert, Teile der Bäume sollen "
                       + $"brennen!\". {fires} fires, {tris} tris, {emitters} spark emitters, "
                       + $"{particles} spark particles max alive, turbulence {FireHz:F1} Hz.\n");
            log.Append($"    the SNAG that burns is chosen from the placed trunks, not typed: a dead "
                       + $"one (broken top, no crown) in band 0 at ({snag.p.x:F2},{snag.p.y:F2}), "
                       + $"{snag.p.magnitude:F2} m out, {snag.h:F1} m tall, base radius "
                       + $"{snag.rb:F2} m — the one furthest round from the moon's bearing "
                       + $"(dot {bestDot:F2}), so it lights the dark side of the clearing.\n");
            foreach (var (n, at, r, h, c) in seats)
                log.Append($"    burns: {n,-7} seat ({at.x,6:F2},{at.y,5:F2},{at.z,6:F2})  "
                           + $"{r * 2f:F2} m across x {h:F2} m tall ({c} cards), "
                           + $"{new Vector2(at.x, at.z).magnitude:F2} m from the clearing centre "
                           + $"(PlaySpace radius {playR:F2} m)\n");
            foreach (var f in rig.fires)
                log.Append($"    lights: {f.name,-6} seat ({f.pos.x,6:F2},{f.pos.y,5:F2},"
                           + $"{f.pos.z,6:F2})  range {f.range:F2} m\n");
            log.Append($"    wash: rgb ({rig.fireWash.r:F2},{rig.fireWash.g:F2},"
                       + $"{rig.fireWash.b:F2}) at +-{rig.fireWash.a * 100f:F0} % flicker, "
                       + $"{rig.fireHz:F1} Hz — the same Hz the flames burn at. Deliberately below "
                       + "the cellar's: the tree line has to stay unwalkable.\n");
            log.Append("    cost when Fire is down: every flame card and every halo collapses to a "
                       + "point in the vertex shader; the wash is black and inside `if (e.fire > 0)`; "
                       + "the spark emitters keep simulating and keep one draw call each.");
            Debug.Log(log.ToString());
        }

        /// <summary>The draught, made to come from somewhere.
        ///
        /// <para>USER VERDICT, ModBuild 142: "Im Keller sollte es noch mehr wie
        /// ein Windzug wirken der insbesondere aus dem Fenster kommt." The room
        /// already had a draught — DraftDir, in at the window and out under the
        /// stair door, which the flames lean along and the motes drift along —
        /// and Air already strengthened all of it. What it did not have was a
        /// SOURCE you could see: the Air emitter is a ring around the player, so
        /// every mote in the room set off in the same direction at the same
        /// moment from nowhere in particular. Air that starts everywhere is
        /// weather; air that starts at an opening is a draught.</para>
        ///
        /// <para>So this is a CONE at the window's own aperture, aimed along
        /// DraftDir, spreading as it comes in — the shape of air entering a room
        /// through a hole. The ring emitter stays: it is the same draught further
        /// along, and the two together are a stream that enters at a place and
        /// then fills the room.</para></summary>
        private static void AddCellarDraught(Transform root)
        {
            var mouth = WindowCentre();
            // Just INSIDE the reveal, not on the wall plane: particles born in
            // the opening itself are half behind the masonry, and the ones that
            // are not read as a sprite stuck to the stone.
            var at = mouth + DraftDir * 0.22f + new Vector3(0f, -0.06f, 0f);
            // 92, not 46, and every one of them dimmer and longer. See the
            // "WHAT MAKES A DOT A SPARK" note in AddElementFX: a draught is a
            // SHEET of fine matter entering at an opening, and a stream you can
            // count the members of reads as a handful of objects being thrown
            // through a window. Doubling the population while halving the alpha
            // costs the same fill and buys the one thing the count was short of.
            var ps = ElemPS(root, "ElemDraughtMouth", at, "FX_ElemDraught.mat", 92);
            var m = ps.main;
            m.duration = 11f;
            // long enough to cross the room's north-west corner (about 1.3 m/s
            // for four metres) and no longer: a mote that outlives the room ends
            // up inside the west wall, where it is fill for nothing.
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
            // THE SPEED IS THE EMITTER'S, not velocityOverLifetime's, and that is
            // the whole construction: with the speed on the shape, every particle
            // leaves along the CONE's own direction, so the stream fans out from
            // the aperture. A world-space velocity (which is what the ring
            // emitter uses, correctly, because a wind has no source) would make
            // them all travel parallel and the spreading would be gone.
            // SLOWER OUT OF THE APERTURE, and the shared push below raised to
            // match — the two together are the difference between a stream and a
            // firework. The first ModBuild 144 bake kept the old 0.65-1.55 m/s
            // cone speed and merely made the motes long, and long streaks leaving
            // one point at high speed read as a STARBURST: the preview showed a
            // spray of white needles radiating out of the window. Wind is
            // parallel. So the radial (cone) component comes down and the shared
            // (world) component goes up, and the fan survives as a spreading at
            // the mouth rather than as the motion.
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.00f);
            // FINER AND DIMMER. The renderer stretches these by their own speed
            // (see below), so the authored size is the filament's WIDTH, not its
            // length: 1.2-2.6 cm of dust hair is matter you can just see against a
            // moonbeam. 4.6 cm at 0.62 alpha was a bright bead. 0.18 alpha,
            // because these motes are drawn just as brightly OUTSIDE the beam as
            // inside it — nothing in a particle shader knows where the light is —
            // and a mote glowing in unlit air is the other half of what reads as a
            // spark. The visible stream is carried by the BEAM instead, where the
            // light actually falls (EnvBeam's striation); these are the hint that
            // there is something in the air at all.
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
            m.startColor = new Color(1f, 1f, 1f, 0.18f);
            // IT COMES IN BREATHS, ModBuild 146 (user: "sieht eher aus wie eine
            // Klimaanlage statt wind das reinpustet"). Same curve as the room's
            // crossing slab, over this emitter's own 11 s loop rather than the
            // slab's 10 s, so the two surge out of step and the room never looks
            // pumped. The MEAN is the 28 that was tuned — the mean has not
            // changed, only the fact that a draught is never at its mean.
            var e = ps.emission;
            e.rateOverTime = new ParticleSystem.MinMaxCurve(28f / ElemGustMean, ElemGustCurve());
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            // THE MOUTH IS THE SIZE OF THE OPENING — derived now, because the
            // opening changed size this round and a hand-typed 0.34 would have
            // gone on emitting a draught through the old window. 0.34 was 0.72 of
            // the equal-area radius of the shipped 1.114 x 0.629 m aperture; that
            // ratio is kept, so the mouth is the same fraction of whatever hole
            // it is coming through.
            var wA = SnappedHole(WindowHole, CW, CH, WallCell);
            sh.radius = 0.72f * Mathf.Sqrt(wA.width * wA.height / Mathf.PI);
            // 12 degrees is NOT taste: a free jet issuing from an orifice spreads
            // at a half-angle of about 11.8 deg, which is one of the few numbers
            // in fluid mechanics that is the same for every jet anybody has ever
            // measured. It is right and it stays.
            sh.angle = 12f;
            sh.radiusThickness = 1f;
            // Shuriken authors a cone about its own local +Z, so the emitter is
            // TURNED to face down the draught. (Same mechanism, opposite use, as
            // ElemRing's (-90,0,0): there the shape has to be laid flat, here it
            // has to be aimed.)
            ps.transform.localRotation = Quaternion.LookRotation(DraftDir, Vector3.up);
            // ...and a small shared push on top of the fan, so the stream bends
            // into the room's own bearing as it slows — a jet through a hole
            // spreads first and then goes with the room.
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            // 0.55-0.95, up from 0.18-0.42: this is the SHARED component, the one
            // every mote has in common, and it is what makes a hundred filaments
            // read as one body of air rather than as a hundred things thrown. It
            // now outweighs the cone's own radial speed within half a metre of
            // the aperture, which is exactly where a jet through a hole stops
            // spreading and starts going with the room.
            v.x = WindRange(DraftDir.x, 0.55f, 0.95f);
            v.z = WindRange(DraftDir.z, 0.55f, 0.95f);
            v.y = new ParticleSystem.MinMaxCurve(-0.14f, 0.10f);
            // TUMBLE, and the only legal way to get it. Nothing here may
            // re-orient with the head, so a rotationOverLifetime billboard is
            // out; what is left is to make the PATH turn, because Stretch mode
            // aligns each filament to its own world velocity and a filament on a
            // curling path turns with it. 0.34 of noise at 0.40 Hz over a 3 s
            // life is roughly a third of a turn per mote — visible tumbling, and
            // still recognisably one stream going one way. (It also breaks the
            // cone's radial fan, which was the other thing that read as thrown
            // objects rather than as air: real motes do not travel in rays.)
            var n = ps.noise; n.enabled = true; n.quality = ParticleSystemNoiseQuality.Low;
            n.strength = 0.34f; n.frequency = 0.40f; n.scrollSpeed = 0.55f;
            // ...AND IT DIES. The third thing that separated this from a duct was
            // that nothing ever slowed down: every mote kept the speed it was
            // born with for its whole life, which is a conveyor belt. A real jet
            // through a hole entrains the still air around it and its centreline
            // velocity falls off roughly as 1/x beyond a few mouth diameters — by
            // two metres from a 0.4 m window a 1 m/s jet is a drift. A speed
            // limit with damping is Shuriken's only script-free way to say that,
            // and it is the right shape: it does nothing at all to a mote that is
            // already slow (the shared push below the limit is untouched, so the
            // stream still goes with the room) and takes the fast ones down.
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true;
            lim.space = ParticleSystemSimulationSpace.World;
            lim.separateAxes = false;
            lim.limit = new ParticleSystem.MinMaxCurve(0.62f);
            lim.dampen = 0.26f;
            ElemFade(ps, 0.10f, 0.62f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            // EXTENT. At the mote's total speed (cone + shared push, ~1.0-1.8 m/s)
            // this draws a 24-45 cm filament instead of a 6-12 cm dash. Length is
            // the single strongest cue that separates carried matter from a spark,
            // and it is free — the same quad, stretched.
            r.velocityScale = 0.165f;
            r.lengthScale = 2.3f;
            r.cameraVelocityScale = 0f;   // no camera term may enter the stretch

            // How close the stream ever comes to the board, MEASURED rather than
            // asserted: the closest approach of the draught's own axis to the room
            // centre. AssertPlaySpaceClear cannot check an emitter (it walks
            // MeshFilters), so this is the only check there is.
            var a2 = new Vector2(at.x, at.z);
            var d2 = new Vector2(DraftDir.x, DraftDir.z).normalized;
            float tStar = -Vector2.Dot(a2, d2);
            float near = (a2 + d2 * Mathf.Max(tStar, 0f)).magnitude;
            Debug.Log($"[GloomhavenVR][Env] Cellar draught mouth: cone at the window "
                      + $"({at.x:F2},{at.y:F2},{at.z:F2}), aperture r {sh.radius:F2} m, spread "
                      + $"{sh.angle:F0} deg, {m.startSpeed.constantMin:F2}-{m.startSpeed.constantMax:F2} m/s "
                      + $"along DraftDir ({DraftDir.x:F3},{DraftDir.z:F3}) toward the stair door, damped "
                      + $"to {lim.limit.constant:F2} m/s at {lim.dampen:F2} so the jet spreads and dies; "
                      + $"emission BREATHES {28f * 0.30f / ElemGustMean:F0}-{28f * 2.05f / ElemGustMean:F0} "
                      + $"per second about the mean of 28 on an 11 s loop (ElemGustCurve); "
                      + $"{m.maxParticles} particles max alive, gated on Air. Closest approach of "
                      + $"the stream's axis to the room centre {near:F2} m (PlaySpace radius "
                      + $"{CellarPlaySpaceDia * 0.5f:F2} m).\n"
                      + $"    NOT SPARKS (ModBuild 144): Env_Wisp filaments, {m.startSize.constantMin * 100f:F1}-"
                      + $"{m.startSize.constantMax * 100f:F1} cm wide at {m.startColor.color.a:F2} alpha, "
                      + $"stretched x{r.velocityScale:F3} m per m/s and x{r.lengthScale:F1} => "
                      + $"{m.startSpeed.constantMin * r.velocityScale * r.lengthScale * 100f:F0}-"
                      + $"{m.startSpeed.constantMax * r.velocityScale * r.lengthScale * 100f:F0} cm long, "
                      + $"tumbling on {n.strength.constant:F2} of curl at {n.frequency:F2} Hz.");

            // ...and THE GRADIENT ITSELF, read back off the built room rather
            // than restated from the formula: every flame in the cellar, with its
            // distance to the aperture and the Air multiplier it was given. If a
            // later round moves the window or a candle, this list is where the
            // draught's source shows up as having moved with it.
            var g = new System.Text.StringBuilder();
            g.Append("[GloomhavenVR][Env] Cellar draught gradient (EnvFlame/_AirGust, "
                     + "3.4 was the old room-wide constant):\n");
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mm = mr.sharedMaterial;
                if (mm == null || mm.shader == null || mm.shader.name != "GloomhavenVR/EnvFlame") continue;
                var p = mr.transform.localPosition;
                float dd = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(mouth.x, mouth.z));
                g.Append($"    {mr.name,-14} ({p.x,6:F2},{p.z,6:F2})  {dd:F2} m from the window  "
                         + $"_AirGust {mm.GetFloat("_AirGust"):F2}\n");
            }
            Debug.Log(g.ToString().TrimEnd());
        }

        // ==================================================== CELLAR ATMOSPHERE
        // "Und hier mehr athmosphärische Details einbauen! zB tropft Wasser von
        //  irgendwo runter in eine pütze, eine Ratte huscht durch den Raum...
        //  Sowas." — user, ModBuild 134.
        //
        // Everything below is script-free: Shuriken lives in BuildEnvironments,
        // and every single motion HERE is a vertex shader reading _Time. Sparse
        // on purpose — five things that happen, not a haunted-house prop shop:
        //   1. a drip that forms on a plank, falls, splashes, and rings a puddle
        //      that mirrors the moonbeam it lands in (one shared clock, see the
        //      CELLAR header and EnvDrip.shader);
        //   2. a rat that comes out of a hole in the wall, runs the corner and
        //      goes into another one, roughly every half minute;
        //   3. a pair of eyes that watch from between the barrels, blink, and
        //      are not there when you look again;
        //   4. four cobwebs that breathe in the same draught the candles lean in;
        //   5. the two rat holes themselves, so the rat comes from somewhere.
        private static void BuildCellarAtmosphere(Transform root, LightRig rig)
        {
            float hw = CW / 2f, hd = CD / 2f;
            var moonObj = MoonDir.normalized;

            // ------------------------------------------------------- the puddle
            var puddleMesh = SaveMesh("Env_C_Puddle.asset",
                PuddleMesh(new Vector2(PuddleAt.x, PuddleAt.z), PuddleR, 10, 30, 5501));
            var pud = NewRoomMat("C_Puddle.mat", "GloomhavenVR/EnvPuddle");
            // 0.45 rather than 0.30: at 0.30 a wet patch on an already very dark
            // floor is indistinguishable from a hole in it. Water reads as water
            // through its REFLECTIONS, not through being darker than the stone.
            pud.SetColor("_Wet", new Color(0.46f, 0.49f, 0.56f, 1f));
            pud.SetVector("_Center", new Vector4(PuddleAt.x, 0f, PuddleAt.z, 0f));
            pud.SetFloat("_Radius", PuddleR);
            pud.SetFloat("_Period", DripPeriod);
            pud.SetFloat("_Phase", 0f);
            pud.SetFloat("_Impact", DripHang + DripFall);
            // ================================================================
            // THE PUDDLE WAS DRAWING A VERTICAL WALL OF WATER, ModBuild 146.
            //
            // USER, hardware: "Die Pfütze ist zu extrem bzw. reagiert zu extrem
            // den Wassertropfen." (The sound half of the same complaint is
            // already fixed, commit 4d57701. This is the water.)
            //
            // WHAT THESE NUMBERS PHYSICALLY ARE. EnvPuddle's RippleH returns a
            // surface height and RippleN differentiates it over 0.012 m to build
            // the normal, so the thing the eye actually sees is the SLOPE, and
            // for a sine train the slope is amplitude x wavenumber:
            //
            //     peak slope  =  _RingAmp * _RingFreq * 2*pi
            //
            // At the shipped 0.55 and 4.4 that is 15.2 — atan(15.2) = 86.2 deg.
            // The shader was tilting the water surface to within four degrees of
            // VERTICAL at the crest of every ring, at the instant of every drop.
            // That is not a strong ripple, it is a different object: the moon's
            // reflection and the candle's shard get swept across the whole patch
            // and back once every 2.85 s, which is exactly "reagiert zu extrem".
            //
            // WHAT IT SHOULD BE, reasoned from the event and not from a taste
            // factor. The drop falls DripY0-DripY1 = 3.24 m, so it arrives at
            // sqrt(2*9.81*3.24) = 8.0 m/s. A ceiling drip is 3-5 mm across, i.e.
            // 30-60 mg and about a millijoule. The crater it opens is a few
            // millimetres deep and about a centimetre across, and the ring that
            // leaves it carries roughly a millimetre of amplitude at 5 cm and a
            // few tenths of a millimetre by 30 cm — a capillary-gravity train
            // whose STEEPEST moment, the crown at the instant of impact, reaches
            // perhaps 30 deg of surface slope and is at single figures a
            // heartbeat later. Millimetres of height and tens of degrees of
            // slope: the slope is the whole of what is visible, which is why it
            // is the quantity these numbers are chosen against.
            //
            //   _RingFreq 4.4 KEPT. 0.227 m of wavelength puts about two crests
            //             inside the ring's own exp(-3|rel|) envelope, which is
            //             what one drop in a 0.72 m puddle really leaves behind
            //             it. Real ripples are finer than this — but a 2 cm wave
            //             on a 1.44 m puddle is a couple of pixels on a headset,
            //             so it would alias into a shimmer instead of reading as
            //             rings. The wavelength is a legibility decision and the
            //             amplitude then has to be the physical one.
            //   _RingAmp  0.55 -> 0.022, so the peak slope is 0.022*4.4*2*pi =
            //             0.61 => 31 deg AT THE FRONT AT THE MOMENT OF IMPACT,
            //             falling as (1-w)^2 through the cycle: 8 deg a third of
            //             the way through, gone by the next drop. That is the
            //             crown, and then a ripple.
            //   _RingCon  0.70 -> 1.00, and this one is a compromise rather than
            //             a measurement. `h` feeds TWO consumers whose
            //             sensitivities differ by about forty times: the normal
            //             (via its derivative, so multiplied by the wavenumber
            //             27.6) and the wet-darkening (directly, via
            //             _Wet*(1+_RingCon*h)). Scaling h to make the normal
            //             physical necessarily takes the darkening down with it,
            //             from a +-38 % band to about +-2 %, and _RingCon is
            //             capped at 1 so it cannot be bought back from out here.
            //             HANDED TO THE SHADER LANE: the darkening path wants its
            //             own scale, because its whole reason for existing is
            //             that "a ripple that can only be seen from the one angle
            //             that catches the moon is a ripple nobody sees" — which
            //             is still true and is now less well served.
            //   _Calm     0.06 -> 0.010. Same arithmetic, same fault: the resting
            //             wavelet's two terms carry wavenumbers 9.0 and 5.3, so
            //             0.06 was a standing slope of 0.43 => 23 deg on water
            //             that is supposed to be AT REST. 0.010 is 4.1 deg, which
            //             is a surface that is not quite still — a cellar has a
            //             draught — and is not a chop.
            // ================================================================
            pud.SetFloat("_RingFreq", 4.4f);
            pud.SetFloat("_RingAmp", 0.022f);
            pud.SetFloat("_RingCon", 1.00f);
            pud.SetFloat("_Calm", 0.010f);
            pud.SetColor("_SkyCol", new Color(0.16f, 0.20f, 0.31f, 1f));
            pud.SetVector("_MoonDir", moonObj);
            pud.SetColor("_MoonCol", new Color(0.78f, 0.92f, 1.25f, 1f));
            // 45, not 160: at 160 the mirror image of the moon is a point you
            // have to stand in exactly one place to see. A puddle is not a
            // mirror — it is a rippled one, and the blur is the point.
            pud.SetFloat("_MoonPow", 45f);
            // the nearest flame — far enough that the warm shard is a hint, which
            // is what a candle across a cellar actually looks like in water
            pud.SetVector("_CandPos", rig.points[2].pos);
            // alpha carries the FLICKER amount, exactly as ApplyRig writes it
            // into _L2Col — the reflection has to breathe with the flame it is a
            // reflection of, or the puddle is lit by a different candle
            pud.SetColor("_CandCol", new Color(1.0f, 0.52f, 0.20f, rig.points[2].flicker));
            pud.SetFloat("_CandPow", 26f);
            pud.SetFloat("_CandRate", SlotRate[2]);
            pud.SetFloat("_CandPhase", SlotPhase[2]);
            pud.SetFloat("_Fresnel", 0.50f);
            // ELEMENT ART — AIR: cat's paws running along the draught. The puddle
            // lies 0.9 m off the draught's own line (window -> stair door), i.e.
            // in it, so wind ripples here are physics rather than decoration —
            // and the moon's reflection breaking into travelling bands is the
            // cheapest legible statement in the room that the air is MOVING and
            // which way. 0.10 against the drip's own _RingAmp of 0.55: a draught
            // ruffles a puddle, it does not out-ring a falling drop.
            //
            // 0.18, and it is derived rather than eyeballed because THE PREVIEW
            // CANNOT SETTLE THIS ONE. The only element-review frame that contains
            // the puddle ('Puddle') also contains the moon pool, which is drawn
            // additively ON TOP of it, so the multiply pass's contribution there
            // is swamped: raising this number from 0.10 to 0.32 moved a measured
            // maximum of 3/255 in that frame, i.e. the frame is blind to it, not
            // the effect absent. So the value comes from the physics instead: the
            // wave's spatial frequency is 7.3 rad/m and the normal path takes
            // 0.30 of the amplitude, so 0.18 tilts the water by about 21 deg at
            // the crests — what a draught does to standing water — and swings the
            // wet darkening by ~13%. HARDWARE HAS TO JUDGE IT, from a pose where
            // the puddle is not under the moon pool.
            pud.SetVector("_DraftDir", DraftDir);
            // 0.18 -> 0.018, and the old comment's own arithmetic is what
            // condemns it. It reasoned "the wave's spatial frequency is 7.3 rad/m
            // and the normal path takes 0.30 of the amplitude, so 0.18 tilts the
            // water by about 21 deg" — but there is no 0.30 anywhere in RippleN.
            // The normal is a finite difference over 0.012 m, which for a
            // wavenumber of 7.3 recovers the full derivative, so 0.18 was tilting
            // the water by atan(0.18*7.3) = 53 deg. Cat's paws on standing water
            // are a ripple of a few degrees; 0.018 is 7.5 deg, and it keeps the
            // ratio the old comment was actually aiming at — a draught ruffles a
            // puddle (0.13 of slope) and does not out-ring a falling drop (0.61).
            // HARDWARE STILL HAS TO JUDGE IT, from a pose where the puddle is not
            // under the moon pool; that part of the old note is unchanged and
            // still true.
            pud.SetFloat("_DraftWave", 0.018f);
            // ELEMENT ART — ICE. The frozen puddle was rejected on hardware as
            // "eher wie Pfützen ... es sollte mehr wie Eis rüberkommen"
            // (ModBuild 143), and EnvPuddle's THE ICE IS A SOLID block carries
            // the rebuild. These three are what is left to tune from out here:
            //   _IceCol     the sheet's own colour. Pale and cold, and BRIGHTER
            //               than _Wet (0.46/0.49/0.56) rather than darker: the
            //               multiply pass is the only thing that can say "this
            //               patch of floor is now covered by something", and ice
            //               covers a flagstone lighter than water wets it.
            //   _IceBody    how strongly the sheet scatters. This is the term
            //               that exists OUTSIDE the Fresnel gate, i.e. the one
            //               that makes the ice visible from directly above where
            //               water is a black hole — the single change that stops
            //               it reading as a pale puddle. 0.12, and the first bake
            //               at 0.55 is why the number is stated so precisely: an
            //               unfresnelled term goes straight to the screen, so
            //               0.55 x this colour is 0.41 linear, which came out at
            //               175/255 in a room whose moon pool sits around 80 and
            //               whose floor sits around 10. It read as a plate of
            //               milk. 0.12 lands the sheet just under the pool it
            //               lies in, so the beam still owns the brightest thing
            //               in that corner and the ice is something ON the floor
            //               rather than a light in it.
            //   _IceRelief  the dome and the interlocking plates, in slope. 0.45
            //               tilts the sheet by up to ~22 deg at the ridges, which
            //               is enough to break the (already broadened, already
            //               dimmed) moon into scattered glints and not enough to
            //               make the surface read as crumpled foil.
            pud.SetColor("_IceCol", new Color(0.66f, 0.76f, 0.92f, 1f));
            pud.SetFloat("_IceBody", 0.12f);
            pud.SetFloat("_IceRelief", 0.45f);
            Place(root, "Puddle", puddleMesh, Vector3.zero, Vector3.zero, Vector3.one, pud);
            {
                // The numbers the "zu extrem" verdict is really about, printed as
                // ANGLES, because a slope means nothing to read and an angle is
                // immediately either water or not water.
                float ringSlope = pud.GetFloat("_RingAmp") * pud.GetFloat("_RingFreq") * 2f * Mathf.PI;
                float calmSlope = pud.GetFloat("_Calm") * 7.15f;      // the two calm terms' wavenumbers
                float windSlope = pud.GetFloat("_DraftWave") * 7.3f;  // WindH's own
                Debug.Log($"[GloomhavenVR][Env] Cellar puddle RIPPLE (user: \"Die Pfütze ist zu extrem "
                          + "bzw. reagiert zu extrem den Wassertropfen\"): a drop arriving at "
                          + $"{Mathf.Sqrt(2f * 9.81f * (DripY0 - DripY1)):F1} m/s tilts the water by at most "
                          + $"{Mathf.Atan(ringSlope) * Mathf.Rad2Deg:F0} deg at the ring front (was 86 deg — "
                          + $"the surface was being drawn very nearly vertical), falling as (1-w)^2 to "
                          + $"{Mathf.Atan(ringSlope * 0.44f) * Mathf.Rad2Deg:F0} deg a third of the way "
                          + $"through the {DripPeriod:F2} s cycle. At rest the water tilts "
                          + $"{Mathf.Atan(calmSlope) * Mathf.Rad2Deg:F0} deg (was 23), and a full Air "
                          + $"draught adds {Mathf.Atan(windSlope) * Mathf.Rad2Deg:F0} deg of cat's paws "
                          + "(was 53). The splash throws "
                          + $"{0.5f * 0.88f * 0.88f / 9.81f * 100f:F0} cm up over {2f * 0.88f / 9.81f:F2} s "
                          + "of flight, which is now also its lifetime.");
            }
            Debug.Log($"[GloomhavenVR][Env] Cellar puddle ICE: sheet grows from the rim inward, "
                      + $"front at uv.y = {0.98f:F2} (nothing) -> {0.98f - 1.16f:F2} (closed over the "
                      + "centre); at Waning 0.40 it is a frozen RING with open water inside it that "
                      + $"the drip still rings. Body {pud.GetFloat("_IceBody"):F2} (view-independent, "
                      + $"outside the Fresnel gate), relief {pud.GetFloat("_IceRelief"):F2}, moon lobe "
                      + "BROADENED x4.4 and dimmed 80% at full Ice. "
                      + "The puddle mesh's winding was inside out and Cull Back had been hiding "
                      + "the whole object since ModBuild 134 — see PuddleMesh. It draws now. "
                      + "The splash becomes a skitter (EnvDrip: up x0.10, out x1.90, life x0.34).");

            // --------------------------------------------------------- the drip
            var dripMesh = SaveMesh("Env_C_Drip.asset", DripMesh(),
                new Bounds(new Vector3(0f, (DripY0 + DripY1) * 0.5f, 0f),
                           new Vector3(0.9f, DripY0 - DripY1 + 0.4f, 0.9f)));
            var drip = NewRoomMat("C_Drip.mat", "GloomhavenVR/EnvDrip");
            drip.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Env_Spark.png"));
            drip.SetColor("_Tint", new Color(0.62f, 0.74f, 1.0f, 0.85f));
            drip.SetFloat("_Period", DripPeriod);
            drip.SetFloat("_Phase", 0f);
            drip.SetFloat("_Hang", DripHang);
            drip.SetFloat("_Y0", DripY0);
            drip.SetFloat("_Y1", DripY1);
            // THE SPLASH IS THE OTHER HALF OF "reagiert zu extrem", and it is
            // the half that can be settled entirely by ballistics, because all
            // three of these are stated in real units by the shader itself.
            //
            //   _SplashUp 1.10 m/s throws a droplet to v^2/2g = 6.2 cm and keeps
            //   it in the air for 2v/g = 0.224 s. _SplashLife was 0.40 s — nearly
            //   twice the flight time, so the second half of every droplet's life
            //   was spent BELOW the water it came out of. _SplashOut 0.55 m/s
            //   over that 0.40 s carried it 22 cm out, i.e. a third of the way
            //   across a 0.72 m puddle.
            //
            //   A 3-5 mm drop arriving at 8.0 m/s (see the ring block above)
            //   raises a crown of about one to two drop diameters and throws its
            //   secondaries a few centimetres. So: 4 cm of apex, which is
            //   _SplashUp = sqrt(2*9.81*0.04) = 0.88 m/s; the flight time that
            //   goes with it, 2*0.88/9.81 = 0.18 s, which is now the LIFETIME so
            //   a droplet dies as it lands instead of sinking through the
            //   surface; and 0.35 m/s outward, which over that flight is 6 cm of
            //   spread — a splash the size of the drop that made it.
            //
            //   _Stretch is unchanged: at the 1.77 m/s a droplet reaches by the
            //   end of its fall it draws the sprite 1.13x long, which is a hint
            //   of motion and not a streak.
            //   (The Ice branch's multipliers are relative to these three and
            //   follow them: the skitter is still a skitter.)
            drip.SetFloat("_SplashLife", 0.18f);
            drip.SetFloat("_SplashOut", 0.35f);
            drip.SetFloat("_SplashUp", 0.88f);
            drip.SetFloat("_Stretch", 0.075f);
            // ELEMENT ART — AIR: the drop is blown off plumb on the way down.
            // 0.85 m/s^2 is chosen against the fall itself, not picked: the drop
            // falls DripFall s, so at full Air it lands 0.5*0.85*DripFall^2 =
            // ~0.28 m downwind — plainly bent, and still well inside the 0.72 m
            // puddle it has to ring. A drop that missed its own puddle would
            // break the one event the drip and the ripples exist to tell together.
            drip.SetVector("_DraftDir", DraftDir);
            drip.SetFloat("_DraftPush", 0.85f);
            Place(root, "Drip", dripMesh, PuddleAt, Vector3.zero, Vector3.one, drip);
            Debug.Log($"[GloomhavenVR][Env] Cellar drip: period {DripPeriod:F2} s, hang {DripHang:F2} s, "
                      + $"fall {DripFall:F3} s => the puddle rings at t={DripHang + DripFall:F3} s of every cycle.");

            // ---------------------------------------------------------- the rat
            // ROUTE IS LIGHTING, not decoration. The first version ran it round
            // the south-east corner, which is the one quadrant no candle and no
            // moonbeam reaches: a preview of it is a black rectangle, and so
            // would the headset have been. It now comes out of a hole in the
            // north wall, crosses the MOONBEAM and its puddle (a cold silhouette
            // with rings under its feet), runs the dark west side, and ends up in
            // the crate candle's pool before it disappears under the crates.
            // Dark -> cold light -> dark -> warm light -> gone.
            // W1/W2 are TUNED so the curve passes within ~0.16 m of MoonBeamHit()
            // at u~0.25 (it really crosses the light, it does not merely go near
            // it) while its closest approach to the room centre stays at 3.41 m,
            // outside the 3.25 m PlaySpace radius. Move a control point and check
            // both of those again.
            // Since ModBuild 140 these four points are the SPINE of a family of
            // routes rather than the route (see RatWob1/RatWob2 next to them):
            // the wander is small enough that the story above still happens on
            // every crossing — the log prints what fraction of them are inside
            // the beam — and large enough that no two crossings are the same.
            // (the four points themselves live next to DraftDir now — the wall
            // rubble has to keep the holes at w0/w3 clear, see HEWN)
            Vector3 w0 = RatW0, w1 = RatW1, w2 = RatW2, w3 = RatW3;
            // ...and all of those claims are CHECKED — over the whole family of
            // routes the schedule can produce, not just the one drawn by the four
            // points above. See AssertRatSchedule: it is also where the bake log
            // gets its route/interval/speed measurements from.
            AssertRatSchedule(root, MoonBeamHit(), -MoonDir.normalized);

            var pathBox = new Bounds(w0, Vector3.zero);
            foreach (var p in new[] { w1, w2, w3 }) pathBox.Encapsulate(p);
            // The wander widens the swept volume as well as the route: the box
            // has to hold the FAMILY, or Unity frustum-culls the animal on the
            // runs that leave the old envelope.
            pathBox.Expand(new Vector3(0.7f + 2f * (RatWob1.x + RatWob2.x), 0.8f,
                                       0.7f + 2f * (RatWob1.y + RatWob2.y)));
            // ...and so does THE BURROW, which leaves the room entirely: the
            // animal travels half a metre into each wall and ends 45 cm under it.
            // Bounds that stopped at the wall plane would let Unity frustum-cull
            // the object at exactly the moment half of it is still in the mouth —
            // i.e. it would pop out of existence mid-entry, which is the ModBuild
            // 142 report with extra steps.
            {
                float reach = 0f;
                for (int h = 0; h < 2; h++)
                {
                    RatBurrow(h, out _, out _, out float travel, out _, out _);
                    reach = Mathf.Max(reach, travel);
                }
                pathBox.Expand(new Vector3(2f * reach, 2f * RatBurrowDrop, 2f * reach));
            }
            var ratMesh = SaveMesh("Env_C_Rat.asset", RatMesh(), pathBox);
            var ratMat = NewRoomMat("C_Rat.mat", "GloomhavenVR/EnvCritter");
            // RAT SKIN, ModBuild 143 — "Die Maus sieht aus hätte sie keine
            // Textur." It had none: one lerp between (0.36,0.31,0.28) and
            // (0.52,0.46,0.42), two greys 1.4 stops apart, which in the moon
            // shaft is a white blob and in candlelight is an orange one. A rat
            // is a THREE-value animal — near-black along the spine, mid brown on
            // the flank, and a belly pale enough to read as a different creature
            // from below — and the ratio between those is what survives being
            // lit by anything. The fur itself is procedural (EnvCritter's frag);
            // these are only its pigments.
            ratMat.SetColor("_Tint", new Color(0.310f, 0.265f, 0.235f));
            ratMat.SetColor("_BackTint", new Color(0.165f, 0.140f, 0.124f));
            ratMat.SetColor("_BellyTint", new Color(0.550f, 0.490f, 0.440f));
            // ...and the parts that are not fur at all: warmer and pinker than
            // the coat, because that is what makes a tail read as a tail — but
            // only just BRIGHTER than it. The first pass had this at
            // (0.44,0.32,0.30), 40% up on the flank and nearly three times the
            // back, and the preview of the entry was a pale rope hanging out of
            // a hole: in a room this dark the eye goes to the brightest thing in
            // frame, and that must not be 25 cm of tail.
            ratMat.SetColor("_SkinTint", new Color(0.345f, 0.258f, 0.240f));
            // ...and the fur itself: strand contrast, coarse mottle, the tail's
            // ring frequency and how deep the rings cut. The contrast is TUNED
            // AGAINST THE DARK, not against a lit turntable: at the shipped
            // (0.30, 0.20) the modulation came out at +-3 of 20 levels on a
            // candle-lit flank, i.e. under the 8-bit floor, and the preview was
            // as smooth as the flat tint it replaced. 0.85 is what makes a
            // strand legible at arm's length without turning the animal blotchy
            // in the moon shaft, which is the one place it is ever bright.
            ratMat.SetVector("_Fur", new Vector4(0.85f, 0.50f, 26f, 0.16f));
            ratMat.SetVector("_FurCell", new Vector4(215f, 48f, 62f, 19f));
            // the beam it runs through, as a real light on this one object
            ratMat.SetVector("_ShaftP", MoonBeamHit());
            ratMat.SetVector("_ShaftD", -MoonDir.normalized);
            // 0.80 m, not the beam's own 0.12 m slats: this is the light the rat
            // walks through, and the five slats plus their penumbra are that wide
            // taken together. A radius that matched one slat lit the animal for a
            // tenth of a second.
            ratMat.SetFloat("_ShaftR", 0.80f);
            ratMat.SetColor("_ShaftCol", new Color(0.55f, 0.70f, 1.05f));
            ratMat.SetVector("_W0", w0); ratMat.SetVector("_W1", w1);
            ratMat.SetVector("_W2", w2); ratMat.SetVector("_W3", w3);
            ratMat.SetFloat("_Period", RatPeriod);
            ratMat.SetFloat("_RunTime", RatRunTime);
            // Phase 0: slot 0 starts at t=0 of the shared clock. That is not a
            // detail — it is what lets a reviewer with the log and a stopwatch
            // predict which crossing happens when.
            ratMat.SetFloat("_Phase", 0f);
            ratMat.SetFloat("_Dart", RatDart);
            ratMat.SetFloat("_Stride", RatStride);
            ratMat.SetFloat("_Scale", 1f);
            // the schedule — proven legal by AssertRatSchedule above
            ratMat.SetFloat("_Skip", RatSkip);
            ratMat.SetVector("_Timing", RatTiming);
            ratMat.SetVector("_Modes", RatModes);
            ratMat.SetVector("_Peak", RatPeak);
            ratMat.SetVector("_Wob1", RatWob1);
            ratMat.SetVector("_Wob2", RatWob2);
            // HAUNT — the stare. The rat stops mid-crossing and turns its head
            // toward the ROOM CENTRE (never toward the camera; see EnvCritter's
            // _Stare block), and only in a haunt slot the schedule left quiet, so
            // it can never collide with one of the six drawn events.
            ratMat.SetFloat("_Stare", RatStareChance);
            ratMat.SetFloat("_HauntPeriod", HauntPeriod);
            ratMat.SetFloat("_HauntCards", HauntCellarCards);
            // THE BURROW — measured off the two pockets AddRatHole really built
            // (see RatBurrow), never typed. This is the whole of the ModBuild 142
            // fix on this side: the shader has no scale term left, so if these
            // four vectors were wrong the animal would walk into the wall in
            // plain sight rather than quietly fail to disappear. AssertRatSchedule
            // above has already proven each one long enough to swallow the tail.
            for (int h = 0; h < 2; h++)
            {
                RatBurrow(h, out var bA, out var bB, out _, out _, out _);
                ratMat.SetVector($"_Hole{h}A", bA);
                ratMat.SetVector($"_Hole{h}B", bB);
            }
            ratMat.SetFloat("_BurTime", RatBurrowTime);
            ratMat.SetFloat("_BurStride", RatBurrowStride());
            // How fast the light dies down the hole. The pocket's own stonework
            // goes 0.24 grey at the mouth to 0.07 at the cap, and the animal has
            // to be lit by the same nothing the stone next to it is.
            ratMat.SetFloat("_PocketD", 0.5f * (RatHoles[0].depth + RatHoles[1].depth));
            var ratGo = Place(root, "Rat", ratMesh, Vector3.zero, Vector3.zero, Vector3.one, ratMat);
            Defer(ratMat, ratGo.transform, 1f);

            // ...and the holes it uses are NOT here any more. Until ModBuild 140
            // they were two AddQuad rectangles on a near-black material, which is
            // what the user saw: "ein viereckiges schwarzes Rechteck ... nicht
            // sehr immersiv". They are now real openings cut out of the wall with
            // a recess behind them, built where the rest of the masonry is built
            // (AddRatHole, called from the HEWN block in BuildCellarRoom) so they
            // share the stonework's material, its vertex-colour darkening and its
            // draw call. The two dead assets go with them — a bundle that still
            // ships Env_C_RatHoles would be shipping the bug.
            foreach (var dead in new[] { MeshDir + "/Env_C_RatHoles.asset", MatDir + "/C_RatHole.mat" })
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dead) != null)
                {
                    AssetDatabase.DeleteAsset(dead);
                    Debug.Log($"[GloomhavenVR][Env] Removed the ModBuild 139 flat rat hole: {dead}");
                }

            // --------------------------------------------------------- the eyes
            // Between the barrels in the south-west, where no candle reaches.
            // ONE material for both eyes, deliberately: two would blink out of
            // step and a rat with independent eyelids is a horror of its own.
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            if (glowMesh != null)
            {
                var eye = NewRoomMat("C_Eyes.mat", "GloomhavenVR/EnvGlow");
                eye.SetColor("_Tint", new Color(1f, 0.72f, 0.34f, 0.80f));
                eye.SetFloat("_Falloff", 3.2f);
                eye.SetFloat("_Blink", 1f);
                eye.SetFloat("_BlinkPeriod", 4.3f);
                eye.SetFloat("_Away", 1f);
                eye.SetFloat("_AwayPeriod", 23f);
                // ELEMENT ART: half of Fire's warm push. Something watching from
                // between the barrels should catch the firelight — it is in the
                // room — but it must not become a pair of orange lamps.
                eye.SetFloat("_ElemWarm", 0.5f);
                var at = new Vector3(-4.86f, 0.115f, -3.55f);
                var side = new Vector3(0.028f, 0f, -0.010f);
                Place(root, "EyeL", glowMesh, at - side, Vector3.zero, Vector3.one * 0.021f, eye);
                Place(root, "EyeR", glowMesh, at + side, Vector3.zero, Vector3.one * 0.021f, eye);
            }

            // ------------------------------------------------------- the cobwebs
            // Anchored along the ceiling and down the wall; the free middle
            // billows on _Sway (EnvRoomCutout), phase-shifted per web but all in
            // the same DraftDir as the flames, so one draught moves the room.
            //
            // THE WEBS THEMSELVES are the CC0 photoscanned orb-web alpha now
            // (Imported/Textures/cobweb_alb.png — TextureCan others_0015, see
            // Environments/License.md), on a handful of well-placed sheets. The
            // ModBuild 135 webs were procedural: nine even spokes and eleven even
            // spirals on a five-ring quarter fan, i.e. regular threads on a
            // straight-edged polygon, which is what "sehr low-poly" was seeing.
            // No amount of extra geometry fixes regularity; a real web's alpha
            // does, and four irregular sheets cost 0.6k triangles between them.
            var webTex = Imp("cobweb_alb");
            var strandTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Env_Strand.png");
            Material WebMat(string n, Texture2D tex, float tint, float sway, float phase, Vector3 swayDir)
            {
                var m = NewRoomMat($"C_Web{n}.mat", "GloomhavenVR/EnvRoomCutout");
                m.SetTexture("_MainTex", tex);
                m.SetFloat("_BumpScale", 0f);
                // must equal the texture's mip-coverage threshold, or the web is
                // clipped out of existence as soon as it minifies (see WritePng
                // and EnvRoomBuilder.CoverageCutoff)
                m.SetFloat("_Cutoff", EnvironmentsBuilder.WebCutoff);
                // _VCol stays 0: this shader now APPLIES vertex colour (ModBuild
                // 136), and a web's vertex RED is a sway weight, not a tint.
                m.SetFloat("_VCol", 0f);
                m.SetColor("_Tint", new Color(0.80f, 0.78f, 0.73f) * tint);
                m.SetFloat("_Sway", sway);
                m.SetFloat("_SwayRate", 0.42f);
                m.SetFloat("_SwayPhase", phase);
                m.SetVector("_SwayDir", swayDir.normalized);
                // HAUNT — the tremble. Every web and every loose strand in the
                // cellar answers the same invisible card, so the whole room's silk
                // shivers at once: one web twitching is a draught, all of them
                // twitching together is something walking past behind them. The
                // forest's foliage uses this same shader and leaves _HauntTremble
                // at 0, which skips the schedule entirely (uniform branch).
                m.SetFloat("_HauntTremble", HauntWebTremble);
                m.SetFloat("_HauntPeriod", HauntPeriod);
                m.SetFloat("_HauntCards", HauntCellarCards);
                m.SetFloat("_HauntWatch", HauntCardTremble);
                m.SetVector("_HauntEnv", HauntTrembleEnv);
                return m;
            }
            void Sheet(string n, Vector3 c, Vector3 halfU, Vector3 halfV, Rect uv,
                       float belly, float sway, float phase, float tint)
            {
                var nrm = Vector3.Cross(halfV, halfU).normalized;
                var mesh = SaveMesh($"Env_C_Web{n}.asset",
                    WebSheetMesh(c, halfU, halfV, uv, belly, 7, 7, 700 + n.Length * 13));
                var m = WebMat(n, webTex, tint, sway, phase, nrm);
                var go = Place(root, "Web" + n, mesh, Vector3.zero, Vector3.zero, Vector3.one, m);
                Defer(m, go.transform, 1f);
            }
            void Strand(string n, Vector3 anchor, Vector3 drop, Vector3 wide,
                        int col, float sway, float phase, float tint)
            {
                var mesh = SaveMesh($"Env_C_Strand{n}.asset",
                    StrandMesh(anchor, drop, wide, col, 3, 9, 810 + n.Length * 7));
                var m = WebMat("Strand" + n, strandTex, tint, sway, phase, wide);
                var go = Place(root, "Strand" + n, mesh, Vector3.zero, Vector3.zero, Vector3.one, m);
                Defer(m, go.transform, 1f);
            }

            // ORIENTATION IS THE WHOLE PROBLEM with a flat web, and it has not
            // changed: a sheet whose plane contains the viewing direction is a
            // one-pixel sliver. Every sheet below therefore spans a corner or an
            // opening DIAGONALLY, which is both where a spider would actually
            // string it and the one orientation that faces the room.
            //
            // 1. THE HERO. Across the east wall / ceiling dihedral beside the
            //    shelf candle, at 45 deg: one edge lies on the ceiling, the other
            //    on the wall, and its face looks down into the room. It is the
            //    one web a candle reaches, so it is the one that has to hold up
            //    at 0.4 m.
            {
                const float wSpan = 0.62f;      // how far it reaches down each surface
                Sheet("Shelf",
                      new Vector3(hw - wSpan * 0.5f, CH - wSpan * 0.5f, CellarShelfAt.z + 0.60f),
                      new Vector3(0f, 0f, 0.46f),                       // along the corner
                      new Vector3(wSpan * 0.5f, -wSpan * 0.5f, 0f),     // ceiling -> wall
                      new Rect(0.04f, 0.06f, 0.92f, 0.88f), 0.055f, 0.021f, 1.9f, 1.0f);
            }
            // 2. THE SOUTH-WEST CORNER, the deep dark one. A big sheet cutting
            //    the vertical corner diagonally so its face is square to the
            //    middle of the room, plus a small one lying in the ceiling corner
            //    above it. Barely lit on purpose — shapes in the dark.
            Sheet("Corner",
                  new Vector3(-hw + 0.42f, CH - 0.52f, -hd + 0.42f),
                  new Vector3(0.44f, 0f, -0.44f),                       // across the corner
                  new Vector3(0f, -0.42f, 0f),                          // straight down
                  new Rect(0.02f, 0.10f, 0.96f, 0.86f), 0.075f, 0.030f, 3.7f, 0.92f);
            Sheet("CornerTop",
                  new Vector3(-hw + 0.30f, CH - 0.10f, -hd + 0.30f),
                  new Vector3(0.30f, 0f, -0.30f),
                  new Vector3(0.24f, -0.09f, 0.24f),                    // near-horizontal
                  new Rect(0.22f, 0.20f, 0.56f, 0.56f), 0.030f, 0.020f, 0.8f, 0.80f);
            // 3. IN THE WINDOW, inside the reveal, so the moonlight comes THROUGH
            //    it: a black lattice in the one bright thing in the room. Kept in
            //    the upper half of the opening — a web across the whole window
            //    would put a texture over the beam's source.
            {
                var wh = SnappedHole(WindowHole, CW, CH, WallCell);
                float wx0 = -hw + wh.xMin, wx1 = -hw + wh.xMax, wy1 = wh.yMax;
                Sheet("Window",
                      new Vector3((wx0 + wx1) * 0.5f, wy1 - 0.20f, hd + RevealDepth * 0.34f),
                      new Vector3((wx1 - wx0) * 0.48f, 0f, 0f),
                      new Vector3(0f, 0.19f, 0f),
                      new Rect(0.10f, 0.30f, 0.80f, 0.40f), 0.022f, 0.011f, 5.2f, 1.10f);
            }
            // 4. LOOSE STRANDS. What sells a web as silk rather than as a decal
            //    is the stuff that came adrift from it: three threads hanging off
            //    the beams and the shelf web, each with the dust it has caught,
            //    swinging on the same draught as the flames (DraftDir) and much
            //    more freely than the sheets — they are held at ONE end.
            Strand("A", new Vector3(hw - 0.30f, CH - 0.28f, 1.52f),
                   new Vector3(0.02f, -0.62f, 0.04f), DraftDir * 0.075f, 0, 0.055f, 0.7f, 0.95f);
            // B hangs off the beam ABOVE THE CRATE CANDLE. Its first home was
            // over the middle of the room, where nothing lights it: a thread
            // 2 cm wide in a black room is not a detail, it is nothing. A loose
            // strand is only worth building where something can catch it.
            // (Its drop stays short for another reason: below y = 2.20
            // AssertPlaySpaceClear counts geometry as intruding on the players.)
            Strand("B", new Vector3(-1.30f, CH - 0.30f, -2.62f),
                   new Vector3(-0.03f, -0.62f, 0.02f), DraftDir * 0.090f, 1, 0.070f, 2.6f, 0.95f);
            Strand("C", new Vector3(-hw + 0.72f, CH - 0.34f, -hd + 0.66f),
                   new Vector3(0.04f, -0.74f, 0.03f), DraftDir * 0.080f, 2, 0.062f, 4.4f, 0.75f);

            // ------------------------------------------------------- the haunts
            // HAUNT SOLID — the creepy easter eggs, as real geometry. Six events,
            // one mesh, one material, one draw call; see the HAUNTS block above,
            // EnvHaunt.shader and EnvHaunt.cginc.
            //
            // WHERE, AND WHY EACH ONE IS WHERE IT IS. The rule that decided all six
            // is the user's own: "eher im Hintergrund", "niemals den Spielfluss
            // stören". So every apparition stands against a piece of the room that
            // is ALREADY there and is already something the eye passes over — the
            // window, the wet wall by the puddle, the stair doorway, the shelf.
            // Nothing is in the open, nothing is over the board (the gate proves
            // it), and nothing needs a new prop to justify it. Two of the six are
            // not drawn here at all: the tremble happens in the cobwebs and the
            // window's occlusion happens in the moonbeam, which is what makes
            // those two believable rather than decorative.
            //
            // THE ORDER IS THE GROUP PARTITION (index mod 3), and it is chosen so
            // that no group is a single duration class.
            {
                var win = SnappedHole(WindowHole, CW, CH, WallCell);
                float winX = -hw + (win.xMin + win.xMax) * 0.5f;
                // THE OPENING'S OWN SIZE, because card 0 is FRAMED BY IT and the
                // gag depends on the ratio (see the card). ModBuild 146 made the
                // window 1.61x bigger; a bust that kept its authored 0.62 m would
                // have stopped being too big for the hole it looks through, which
                // is the entire joke.
                float winH = win.height, winMidY = (win.yMin + win.yMax) * 0.5f;
                // The W wall runs p0 = (-hw, 0, -hd) along +z (CellarWalls), so a
                // wall-local u is a z offset from the south-west corner. Reading
                // the SNAPPED rect and not the authored one matters for the same
                // reason the window bars read it: WallMesh keeps or drops whole
                // 0.16 m cells, so the doorway is up to half a cell from where
                // StairHole says it is, and a figure that crosses "the doorway"
                // 8 cm off it walks through the jamb instead.
                var stair = SnappedHole(StairHole, CD, CH, WallCell);
                float stairZ = -hd + (stair.xMin + stair.xMax) * 0.5f;

                // THE SHELF THE FACE HID BEHIND is gone from this catalogue: card
                // 5 is now the shelf ITSELF going over (user, cellar 11). What is
                // still measured off it is the bookshelf's real extents, because
                // the tip-over pivots on its own base edge and no one here knows
                // where a photoscan's base edge is without asking it.
                var shelfGo = root.Find("Shelf")?.gameObject;
                var shelfBounds = new Bounds(CellarShelfAt + Vector3.up * 1.02f, new Vector3(0.58f, 2.06f, 1.37f));
                if (shelfGo != null)
                {
                    bool first = true;
                    foreach (var p in WorldVerts(shelfGo))
                    {
                        if (first) { shelfBounds = new Bounds(p, Vector3.zero); first = false; }
                        else shelfBounds.Encapsulate(p);
                    }
                }

                // ============================================================
                // HAUNT FORCE ID TABLE — CELLAR. The C# lane that adds the
                // Advanced-menu buttons publishes _GhvrHauntForce = (id + 1,
                // clock, 0, 0); see the channel block in EnvHaunt.cginc. The id
                // IS the index in the array below, which is why this table lives
                // here and nowhere else — a copy kept anywhere else would drift
                // the first time somebody reorders a catalogue.
                //   0  Window   draws nothing here: one of the GAME'S OWN monsters
                //               walks past OUTSIDE the opening at sill height
                //               (HauntFigures card 0). The beam still dims for it
                //               (EnvBeam), and now it dims because something really
                //               is crossing in front of the window.
                //   1  Hands    handprints blooming on the wet wall by the puddle.
                //               The one flat card left in either room.
                //   2  Door     NEW: a dim warm rectangle opens at the top of the
                //               stair shaft, holds three seconds and closes. No
                //               figure, no face — light where there was none.
                //   3  Tremble  draws nothing: every COBWEB in the room shivers
                //               (EnvRoomCutout). A tester pressing this button has
                //               to be told to look at the webs, not at the room.
                //   4  Stair    draws nothing here: a game monster crosses the
                //               stair doorway inside the alcove (HauntFigures
                //               card 4, index unchanged).
                //   5  Shelf    THE BOOKSHELF TIPS OVER and stands itself back up.
                //               It is always on screen; this button only starts it.
                //
                // STILL SIX. The head that lay on the flagstones went with the rest
                // of the figures (ModBuild 146) and no game monster can lie on a
                // floor and look up — but the group partition needs a multiple of
                // three, so its slot got the door instead of being dropped.
                // ============================================================
                //
                // THE LIGHT COLOURS BELOW ARE THE ROOM'S OWN, and that is the whole
                // shading model: `key` is the colour of the light that reaches this
                // apparition and `keyDir` is where it comes FROM, and the builder
                // bakes key·N per vertex. An apparition lit by a colour the room
                // does not contain is a decal; one lit by the room's own light,
                // from the direction that light really arrives from, is IN it.
                var moon = EnvironmentsBuilder.MoonDir;
                var cards = new[]
                {
                    // [0] AT THE WINDOW — A SCHEDULE PLACEHOLDER. It used to be a
                    // head, a neck and two shoulders, built from the imported bust
                    // and deliberately too big for the opening. USER, ModBuild 146:
                    // "mir gefallen die Figuren und animationen gar nicht" and then
                    // "Entferne die alten 3D assets komplett". So the geometry is
                    // gone and this card now draws NOTHING.
                    //
                    // WHAT HAPPENS IN ITS SLOT INSTEAD: one of the game's own
                    // monsters walks past OUTSIDE the window at sill height, on the
                    // game's own Animator (HauntFigures.Events.cs, CellarWindow).
                    // The wall does all the revealing and all the hiding: the
                    // opening's 0.79 m shows the player its feet and shins and
                    // nothing else. That is a better version of this event than the
                    // bust ever was, and it needs no geometry from the bake.
                    //
                    // THE CARD MUST STAY, AND MUST STAY AT INDEX 0. Three things
                    // read this slot and none of them draws it: EnvBeam dims the
                    // moonbeam for card HauntCardWindow, the runtime spawns its
                    // figure on the same card, and the Advanced-menu force button
                    // is this index. A retired index would silence all three.
                    //
                    // THE ENVELOPE IS NOW THE FIGURE'S OWN — 0.35 / 4.80 / 0.35,
                    // copied from HauntFigures' CellarWindow, where it used to be
                    // the bust's 3.2 / 2.6 / 1.8. That is not tidiness: the beam
                    // dims for exactly this envelope, so a 7.6 s dim over a 5.5 s
                    // walk-past would leave the shaft dark for two seconds after
                    // the thing had gone.
                    new HauntCard
                    {
                        name = "Window", kind = HKindNone,
                        at = new Vector3(winX, winMidY, hd + RevealDepth + 0.10f),
                        facing = Vector3.back, height = 0.05f,
                        reveal = HauntWindowEnv.x, hold = HauntWindowEnv.y, fade = HauntWindowEnv.z,
                        key = Color.black, keyDir = moon, opacity = 0f,
                        why = "invisible here; a real game monster walks past outside it (HauntFigures card 0), and the beam dims for it",
                    },
                    // [1] THE HANDPRINTS, on the west wall beside the puddle — i.e.
                    // on the one piece of stone in the room the player has already
                    // been told is WET (the drip falls into that puddle every 2.85 s
                    // and the moonbeam lands in it). Prints bloom on wet stone; on
                    // dry stone they are a decal.
                    //
                    // THE ONE FLAT CARD LEFT IN EITHER ROOM, and it is flat because
                    // a handprint is flat. It lies IN the wall, in the wall's own
                    // plane, so it has the parallax a mark on a wall has. The
                    // ruling it has to answer — "generell keine 2D Pappaufsteller"
                    // — is about things that stand up in the air.
                    //
                    // LOW, not at hand height: 0.85 m, so they are where something
                    // on the floor could reach and not where a person standing
                    // would leave them.
                    new HauntCard
                    {
                        name = "Hands", kind = HKindDecal,
                        // z = PuddleAt.z - 1.05, NOT - 0.15. The puddle sits
                        // directly in front of the STAIR DOORWAY, so "beside the
                        // puddle" put the prints ON THE OPENING: the preview of
                        // the first two bakes is a picture of prints floating in a
                        // black rectangle, because there was no wall behind them
                        // to print on. A metre south of it there is stone.
                        at = new Vector3(-hw + 0.03f, 0.85f, PuddleAt.z - 1.05f),
                        facing = Vector3.right, height = 1.24f,
                        reveal = 2.8f, hold = 2.2f, fade = 3.0f,
                        key = new Color(0.105f, 0.090f, 0.070f), keyDir = Vector3.right,
                        fillAmt = 0.35f, opacity = 0.85f, rim = 0.0f,
                        why = "west wall by the drip's puddle at 0.85 m — the room's one wet stone",
                    },
                    // [2] SOMEBODY OPENED THE DOOR AT THE TOP OF THE STAIRS.
                    //
                    // THE HEAD ON THE FLOOR held this slot and is RETIRED with the
                    // rest of the imported figures (user: "Entferne die alten 3D
                    // assets komplett"). No monster in the game's roster can be
                    // posed lying on a floor looking up, so there was nothing to
                    // hand the slot to — and the slot CANNOT SIMPLY BE DROPPED:
                    // the never-the-same-event-twice guarantee is a partition of
                    // the catalogue into GHVR_HAUNT_GROUPS = 3 equal groups
                    // (EnvHaunt.cginc, `h.card = grp + GROUPS * j`), so a room with
                    // five cards has one group of two and one of one-and-a-half,
                    // and one slot in six would index a card that does not exist
                    // and draw nothing at all. Six or three; there is no five.
                    //
                    // So this slot gets a NEW event, and the constraint on it was
                    // strict: no body, no face, no figure of any kind, nothing that
                    // has to be posed or animated. What is left that is still
                    // frightening is LIGHT WHERE THERE WAS NONE.
                    //
                    // The stair alcove is the blackest thing in the room — a 2.2 m
                    // shaft behind the west doorway that ends in a pitch-black cap,
                    // with six steps climbing away into it and darkening from 0.85
                    // to 0.12 as they go (the user's own ModBuild 133 ruling: "the
                    // far end of the alcove has to be unreadable"). A dim warm
                    // rectangle fades up at the far end of it, holds, and goes out.
                    // Nobody comes down. Nothing crosses. A door at the top of your
                    // cellar stairs opened onto a lit room, stayed open for three
                    // seconds, and closed — and the only thing you can be sure of
                    // afterwards is that you were not the one who opened it.
                    //
                    // IT IS ONE FLAT BOX, 1.5 x 1.6 x 0.03 m, standing in front of
                    // the shaft's cap. The shaft's own opaque walls crop it to
                    // whatever the doorway shows, exactly as they crop the figure
                    // that used to walk across it, and its warm key is the ONLY
                    // warm light in this room that is not a candle — which is the
                    // whole of why it reads as somewhere else.
                    new HauntCard
                    {
                        name = "Door", kind = HKindSolid,
                        at = new Vector3(-hw - 2.05f, 0.90f, -hd + StairHole.xMin + StairHole.width * 0.5f),
                        facing = Vector3.right, height = 1.60f, wide = 1.0f, yaw = 0f,
                        reveal = 2.2f, hold = 3.0f, fade = 1.6f,
                        // warm, and DIM: it is a door onto a lit room seen from the
                        // bottom of an unlit stair, at 6 m, through a doorway. The
                        // candles in this room sit at 0.10-0.19 of key; this is
                        // under half of that, which is what "there is a light on up
                        // there" looks like from down here.
                        key = new Color(0.075f, 0.055f, 0.030f), keyDir = Vector3.right,
                        fillAmt = 0.10f, opacity = 0.92f, rim = 0.0f,
                        why = "a dim warm rectangle opens at the top of the stair shaft, holds 3 s and closes",
                    },
                    // [3] THE TREMBLE. Draws NOTHING. Its whole existence is to
                    // occupy a schedule slot that EnvRoomCutout watches: every
                    // cobweb in the room shivers for two seconds as if something
                    // large had just gone past behind them. Being a real card is
                    // what puts it under the same no-repeat and no-collision rules
                    // as the visible events — a tremble that could land on top of
                    // the thing at the window would read as one effect, not two.
                    new HauntCard
                    {
                        name = "Tremble", kind = HKindNone,
                        at = new Vector3(-4.40f, 2.40f, -4.00f),
                        facing = new Vector3(0.740f, 0f, 0.673f), height = 0.05f,
                        reveal = HauntTrembleEnv.x, hold = HauntTrembleEnv.y, fade = HauntTrembleEnv.z,
                        key = Color.black, keyDir = Vector3.up, opacity = 0f,
                        why = "invisible; the cobwebs shiver for it (EnvRoomCutout)",
                    },
                    // [4] THE STAIR DOORWAY. The fastest thing in the catalogue —
                    // seven tenths of a second, which is long enough to be a person
                    // and far too short to be examined. It is a REAL BODY MID-
                    // STRIDE now, not a shape sliding sideways: "Auch die
                    // Treppenerscheinung ist offensichtlich 2D" was a flat figure
                    // translating, and a walk cycle frozen at its widest is the one
                    // pose that cannot be mistaken for a slide.
                    //
                    // IT IS TOO TALL, AND ITS HEAD IS ABOVE THE OPENING. The
                    // doorway is 2.36 m; this figure is 2.55 m, so what crosses the
                    // lit rectangle is a body whose top you never see. The first
                    // bake made it a 1.8 m person, on the reasoning that a figure
                    // filling the door is a statue — true, and what it produced was
                    // a glowing chess pawn. A body you cannot measure is worse.
                    //
                    // AND IT TOO IS NOW A PLACEHOLDER (ModBuild 146). The imported
                    // strider is deleted with the rest of the figures; what crosses
                    // the doorway is a real game monster inside the alcove
                    // (HauntFigures.Events.cs, CellarStair — 2.6 m tall, scaled
                    // against the prefab's own CharacterManager.Height, visible for
                    // about nine tenths of a second across the 1.58 m the doorway is
                    // open). The card carries the slot and nothing else.
                    //
                    // THE ENVELOPE IS THE FIGURE'S OWN: 0.30 / 2.00 / 0.30 = 2.60 s,
                    // where the shader card used 0.18 / 0.34 / 0.18 = 0.70 s. A real
                    // body walking 4.2 m cannot be done in seven tenths of a second,
                    // so the slot has to be as long as the walk or the schedule cuts
                    // the monster off mid-stride.
                    new HauntCard
                    {
                        name = "Stair", kind = HKindNone,
                        at = new Vector3(-hw - 0.55f, 0f, stairZ),
                        facing = Vector3.right, height = 0.05f,
                        reveal = 0.30f, hold = 2.00f, fade = 0.30f,
                        key = Color.black, keyDir = Vector3.up, opacity = 0f,
                        why = "invisible here; a real game monster crosses the doorway inside the alcove (HauntFigures card 4 -> 3)",
                    },
                    // [5] THE BOOKSHELF ITSELF. User, cellar 11: "Statt da auch ne
                    // Fratze zu machen: Wie wär es wenn das Bücherregal umkippt,
                    // und sich dann nach ner Zeit wieder von selbst aufstellt."
                    //
                    // This card has NO apparition geometry: the geometry is the
                    // real bookshelf, drawn by the same shader on an opaque
                    // material (see BuildTippingShelf and the KIND 4 block in
                    // EnvHaunt.shader). The card exists so the shelf's fall is on
                    // the same schedule as everything else — same slot beat, same
                    // no-repeat partition, same Advanced-menu force button.
                    //
                    // WHICH WAY IT FALLS is the shelf's own forward, and the
                    // play space is cleared by where it STANDS (ModBuild 146 — it
                    // moved 3.85 m south along the same wall so that a fall
                    // straight off that wall lands the whole carcass, corners
                    // included, outside the 3.25 m disc). The old comment here
                    // described a diagonal fall, which is what made it land on an
                    // edge; see BuildTippingShelf.
                    new HauntCard
                    {
                        name = "Shelf", kind = HKindProp,
                        at = new Vector3(shelfBounds.center.x, 0f, shelfBounds.center.z),
                        facing = Vector3.left,       // = the placed prop's forward at yaw -90
                        height = shelfBounds.size.y,
                        reveal = HauntShelfEnv.x, hold = HauntShelfEnv.y, fade = HauntShelfEnv.z,
                        key = Color.white, keyDir = Vector3.up, opacity = 1f,
                        why = "the bookshelf goes over in 4.68 s, lands with one 0.62 s rebound, "
                              + "lies there 10.8 s, and stands itself up over 9.88 s",
                    },
                };
                // The two indices three other shaders were handed as constants.
                // Reordering this array is a completely reasonable thing to want to
                // do (the group partition is index-based), and it would silently
                // make the beam dim for the handprints — so it is a build error
                // rather than a comment.
                if (cards.Length != HauntCellarCards
                    || cards[HauntCardWindow].name != "Window"
                    || cards[HauntCardTremble].name != "Tremble"
                    || cards[HauntCardShelf].name != "Shelf")
                    throw new Exception("Cellar haunts were reordered: EnvBeam is told to dim for card "
                                        + $"{HauntCardWindow}, the cobwebs to shiver for card "
                                        + $"{HauntCardTremble} and the bookshelf to fall on card "
                                        + $"{HauntCardShelf}, but the catalogue now has "
                                        + $"'{cards[HauntCardWindow].name}', "
                                        + $"'{cards[HauntCardTremble].name}' and "
                                        + $"'{cards[HauntCardShelf].name}' there. Update the constants "
                                        + "with the order.");

                BuildHaunts(root, "Cellar", "Env_C_Haunt", cards,
                            new Color(0.10f, 0.13f, 0.21f, 1f),
                            // xLim reaches PAST the west wall on purpose: the stair
                            // alcove is a 2.2 m shaft behind it (BuildShaft) and the
                            // figure that crosses the doorway walks INSIDE that
                            // shaft, which is the only reason the jambs can be what
                            // it walks out of and into. A box that stopped at the
                            // wall would fail the build for geometry that is
                            // correctly hidden by the room.
                            CellarPlaySpaceDia, hw + 2.30f, hd + RevealDepth + 0.40f, CH,
                            (h, cs, i, piece) =>
                            {
                                var c = cs[i];
                                if (c.name == "Door")
                                {
                                    // ONE FLAT BOX standing in front of the stair
                                    // shaft's black cap. A closed solid, because
                                    // every non-decal kind has to pass
                                    // AssertClosedAndOutward — and a light in a
                                    // doorway is a thing you can be on the wrong
                                    // side of, so being a solid is correct rather
                                    // than a formality.
                                    // 0.03 along X — the slab FACES the room, so
                                    // its thin axis is the one the doorway is
                                    // looked through. (The first build had it
                                    // 1.50 m thick in x and the room-box gate
                                    // caught it 0.5 m outside the west wall.)
                                    var slab = BoxMesh(0.03f, 1.60f, 1.50f, 1f);
                                    MergeInto(piece, slab, c.at, Quaternion.identity,
                                              Vector3.one, Color.white);
                                    UnityEngine.Object.DestroyImmediate(slab);
                                }
                                else if (c.name == "Hands")
                                {
                                    // the wall the prints bloom on: a 1.24 m square
                                    // of the west wall, in the wall's own plane,
                                    // 3 cm proud of it so nothing z-fights
                                    AddHauntMark(piece, c.at,
                                                 Vector3.forward * (c.height * 0.5f),
                                                 Vector3.up * (c.height * 0.5f));
                                    HauntWeldDecal(h, piece, c, i, EnvironmentsBuilder.HTileHands);
                                    piece.V.Clear(); piece.N.Clear(); piece.UV.Clear();
                                    piece.C.Clear(); piece.T.Clear();
                                }
                            });

                // (THE SHADOW ON THE FLOOR is gone with the bust that cast it.
                // User, cellar 7: "gruselig wäre auch wenn sie beim Mondlicht einen
                // Schatten wirft wenn sie durchs Fenster schaut" — it was the bust's
                // OWN vertices projected onto the flagstones along MoonDir, hulled
                // and fanned, and with the bust deleted there is no occluder in the
                // opening to project. It cannot be rebuilt from the runtime figure
                // either: that creature walks past OUTSIDE the wall at ground level,
                // where the moonbeam has not yet been cut by the aperture, so what
                // it occludes is the beam itself and not a patch of floor. EnvBeam
                // still dims for the event, which is the honest half of the idea and
                // the half that was always free.)
            }
        }

        // ================================================================ FOREST
        // The prefab file is still Env_Swamp.prefab and the enum is still
        // SwampNight (the runtime lane owns those names) — the CONTENT is a night
        // forest. User ruling, ModBuild 132: "Der Boden im Moor gefällt mir nicht
        // - statt Moor mach eventuell doch lieber einen gruseligen Wald mit einer
        // kleinen Lichtung in der mitte wo das board ist. Achte sehr auf die
        // Athomsphäre ... im Wald soll man sich durchaus gruseln durch die Lichter
        // und Umgebung."
        //
        // WHAT MAKES IT A PLACE, in the order that matters:
        //  1. LIGHT. A cold moon at 40° pours through a TEAR in the canopy: five
        //     crossed-blade shafts (EnvShaft) rake into the clearing, trunks catch
        //     a cold rim (EnvRoom _RimCol) on the moonlit side only, and the gaps
        //     between them stay black. The only warm things in the whole scene are
        //     three small points deep in the wood — a will-o'-the-wisp over the
        //     hollow, a far lantern glow, and a pair of eyes that never move.
        //  2. DEPTH. Four concentric bands of trunks out to 28 m, each smaller,
        //     darker and hazier than the one in front, under a canopy shell that
        //     covers everything except the clearing and the moon tear: trees
        //     behind trees behind trees, dissolving into fog. Never a thin ring.
        //  3. GROUND. Real forest floor — needle/dirt and leaf-litter photoscans
        //     blended by vertex colour, roots, deadfall, moss — and a crooked
        //     trodden path that leaves the clearing and dies between the trunks.
        //  4. STORY, sparse and intentional: an axe left in a stump, a smashed
        //     crate spilled where the path bends, a dead tree leaning into its
        //     neighbour's crown, hanging moss.
        //
        // Trunks and foliage are GROWN here, not imported: Poly Haven's scanned
        // conifers are 0.5-1 GB multi-material photoscans whose alpha twig cards
        // do not survive decimation. Their TWIG ATLAS does ship (fir_tree_01,
        // CC0) — so every needle here is photoscanned pixels on procedural
        // geometry: photoreal at ~8 tris per bough instead of ~10k.
        private const float FR = 30f;        // ground disc radius — UNCHANGED: the
                                             // runtime sizes the room off the
                                             // prefab's authored extent
        private const float ClearR = 5.4f;   // open ground around the board

        // Sub-rects of Imported/Textures/fir_twig_alb.png — Poly Haven
        // fir_tree_01's twig atlas (CC0), whose alpha holds seven isolated fir
        // sprigs and one bare branch on clean transparency. Rects found by
        // connected-component analysis of that alpha channel.
        private static readonly Rect[] Sprigs =
        {
            new Rect(0.2988f, 0.2178f, 0.3525f, 0.3887f),
            new Rect(0.6279f, 0.1631f, 0.3359f, 0.3916f),
            new Rect(0.6455f, 0.6240f, 0.2988f, 0.3428f),
            new Rect(0.1826f, 0.6816f, 0.2529f, 0.2783f),
            new Rect(0.4873f, 0.5938f, 0.1504f, 0.1533f),
            new Rect(0.3799f, 0.6104f, 0.0859f, 0.1074f),
            new Rect(0.5322f, 0.7646f, 0.0635f, 0.1436f),
        };
        private static readonly Rect DeadTwig = new Rect(0.3223f, 0.0234f, 0.6543f, 0.2637f);

        // The crooked path: it leaves the clearing to the south-west (away from
        // the moon, so it walks INTO the dark) and bends out of sight at ~15 m.
        private static readonly Vector2[] PathPts =
        {
            new Vector2(0.6f, -1.2f), new Vector2(-0.9f, -3.4f), new Vector2(-2.6f, -5.2f),
            new Vector2(-3.4f, -7.6f), new Vector2(-2.7f, -10.3f), new Vector2(-3.9f, -12.8f),
            new Vector2(-6.4f, -14.6f), new Vector2(-9.2f, -15.6f),
        };

        /// <summary>Distance from (x,z) to the path polyline.</summary>
        private static float PathDist(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            for (int i = 0; i < PathPts.Length - 1; i++)
            {
                Vector2 a = PathPts[i], b = PathPts[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        /// <summary>How far along the path (0 at the clearing, 1 where it fades).</summary>
        private static float PathFade(float x, float z) =>
            Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(9f, 15f, new Vector2(x, z).magnitude));

        private static float ForestY(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.7f, 4.6f, r)); // dead-flat play space
            float h = lift * (0.44f * Fbm2(x * 0.13f + 19f, z * 0.13f, 3, 981)
                            + 0.15f * Fbm2(x * 0.52f, z * 0.52f, 3, 982) - 0.27f);
            // the path is trodden down into a shallow hollow
            float pd = PathDist(x, z) / 0.95f;
            h -= lift * 0.075f * Mathf.Exp(-pd * pd) * PathFade(x, z);
            // wooded bank closing the horizon — irregular, so the rim of the disc
            // never reads as a horizon line (permanent rule)
            // The rim used to rise 1.3-2.5 m, which silhouetted as a dead-flat
            // black band against the sky. In a forest the TREES close the horizon,
            // so the bank is only a low swell now.
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(19f, 28f, r))
                 * (0.9f + 3.4f * Fbm2(x * 0.145f + 7f, z * 0.145f, 3, 983)
                         * (0.45f + 0.9f * Fbm2(x * 0.055f, z * 0.055f + 21f, 2, 987)));
            return h;
        }

        // ------------------------------------------------------------- the trees
        private struct Tree
        {
            public Vector2 p; public float h, rb, lean, leanAz, sd; public int band; public bool dead;
        }

        private static List<Tree> ForestTrees()
        {
            // (count, rMin, rMax, hMin, hMax, baseRadiusMin, baseRadiusMax)
            var bands = new[]
            {
                (16, 6.2f, 10.0f, 11.5f, 16.5f, 0.23f, 0.40f),
                (22, 10.0f, 15.5f, 10.0f, 15.0f, 0.18f, 0.32f),
                (34, 15.5f, 21.5f, 10.0f, 15.0f, 0.15f, 0.26f),
                (34, 21.5f, 28.5f, 10.0f, 15.0f, 0.13f, 0.22f),
            };
            var list = new List<Tree>();
            for (int b = 0; b < bands.Length; b++)
            {
                var (n, r0, r1, h0, h1, b0, b1) = bands[b];
                for (int i = 0; i < n; i++)
                {
                    // stratified ring sampling: even coverage, no clumps, no gaps
                    float ang = (i + 0.15f + 0.7f * Hash3(i, b, 0, 5001)) / n * Mathf.PI * 2f;
                    float rad = Mathf.Lerp(r0, r1, Hash3(i, b, 1, 5001));
                    var p = new Vector2(Mathf.Sin(ang) * rad, Mathf.Cos(ang) * rad);
                    // never grow a trunk in the path (or the trail dead-ends in a tree)
                    for (int guard = 0; guard < 6 && PathDist(p.x, p.y) < 1.6f; guard++)
                        p += new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang)) * 0.9f;
                    list.Add(new Tree
                    {
                        p = p,
                        h = Mathf.Lerp(h0, h1, Hash3(i, b, 2, 5001)),
                        rb = Mathf.Lerp(b0, b1, Hash3(i, b, 3, 5001)),
                        lean = 0.012f + 0.045f * Hash3(i, b, 4, 5001),
                        leanAz = Hash3(i, b, 5, 5001) * Mathf.PI * 2f,
                        sd = Hash3(i, b, 6, 5001) * 40f,
                        band = b,
                        // one tree in nine is a dead snag: broken top, no crown
                        dead = Hash3(i, b, 7, 5001) > 0.89f,
                    });
                }
            }
            return list;
        }

        /// <summary>Trunk centre at height y above its base (lean + a slow wander,
        /// so no trunk in the wood is a straight pole).</summary>
        private static Vector3 TrunkAt(Tree t, float y)
        {
            float f = Mathf.Clamp01(y / t.h);
            Vector2 lean = new Vector2(Mathf.Sin(t.leanAz), Mathf.Cos(t.leanAz)) * (t.lean * t.h * f * f);
            Vector2 wander = new Vector2(Mathf.Sin(f * 3.1f + t.sd), Mathf.Cos(f * 2.4f + t.sd * 1.7f))
                             * (0.020f * t.h * f);
            return new Vector3(t.p.x + lean.x + wander.x,
                               ForestY(t.p.x, t.p.y) + y,
                               t.p.y + lean.y + wander.y);
        }

        /// <summary>A trunk's horizontal radius at height y — the SAME taper and
        /// the SAME per-tree noise AddTrunk builds the geometry from, minus the
        /// root flare (0.62 * exp(-y/0.30) is 3e-3 by one metre, and nothing here
        /// asks below that) and minus the per-segment lobes, which is deliberate:
        /// the haunt that hides behind a trunk needs the radius it can COUNT on,
        /// i.e. the smallest one, not the lobed maximum.
        ///
        /// <para>Derived rather than guessed because the whole "eine lächelnde
        /// fratze die hinter einem Baum hervorguckt" effect is a distance: the
        /// face has to start hidden behind THIS tree and travel exactly far enough
        /// to clear it. A typed-in 0.3 m would be right for one tree in the wood
        /// and wrong for the other 105.</para></summary>
        private static float HauntTrunkRadius(Tree t, float y)
        {
            float f = Mathf.Clamp01(y / t.h);
            float rad = Mathf.Lerp(t.rb, t.rb * 0.30f, Mathf.Pow(f, 1.9f));
            return rad * (1f + 0.13f * (Fbm2(f * 8f, t.sd * 3f, 3, 991) - 0.5f));
        }

        // (HauntPickTree is gone with the face that hid behind its answer. It
        // searched the wood for a trunk in a radius band at a wanted bearing and
        // at least a given thickness, so the forest could be re-seeded without
        // silently moving an apparition into the open. Nothing hides behind a tree
        // any more — ModBuild 146, "Entferne die alten 3D assets komplett".)

        private static void AddTrunk(Acc a, Tree t, int segs, int rings, float uvScale, Color tint)
        {
            for (int j = 0; j <= rings; j++)
            {
                float f = j / (float)rings;
                float y = t.h * f;
                Vector3 c = TrunkAt(t, y);
                if (j == 0) c.y -= 0.30f;                       // bury the foot: no gap on a slope
                // Taper. Pow(f,0.70) lost 64% of the diameter in the first
                // third of the height — every trunk read as a carrot. A conifer
                // is very nearly a cylinder low down and only narrows near the
                // crown, which is Pow(f, 1.9).
                float rad = Mathf.Lerp(t.rb, t.rb * 0.30f, Mathf.Pow(f, 1.9f));
                // Root buttress. It used to be 1 + 1.7*exp(-y/0.42) — a perfect
                // smooth cone, so every trunk read as a traffic cone. A real
                // conifer flares gently AND unevenly, so the flare is much weaker
                // and gets angular lobes that die out a third of a metre up.
                float flare = 0.62f * Mathf.Exp(-y / 0.30f);
                rad *= 1f + 0.13f * (Fbm2(f * 8f, t.sd * 3f, 3, 991) - 0.5f);
                float circ = 2f * Mathf.PI * rad;
                for (int s = 0; s <= segs; s++)
                {
                    float ang = s / (float)segs * Mathf.PI * 2f;
                    // cos/sin arguments so the lobes wrap seamlessly at the seam
                    float lobe = Fbm2(Mathf.Cos(ang) * 1.7f + t.sd, Mathf.Sin(ang) * 1.7f, 2, 992);
                    float rr = rad * (1f + flare * (0.45f + 1.35f * lobe));
                    var nrm = new Vector3(Mathf.Cos(ang), 0.10f + flare * 0.55f, Mathf.Sin(ang)).normalized;
                    a.Vert(c + new Vector3(nrm.x * rr, 0, nrm.z * rr), nrm,
                           new Vector2(s / (float)segs * circ / uvScale, y / uvScale), tint);
                }
            }
            int stride = segs + 1;
            int b0 = a.Count - (rings + 1) * stride;
            for (int j = 0; j < rings; j++)
                for (int s = 0; s < segs; s++)
                {
                    int i0 = b0 + j * stride + s;
                    a.T.AddRange(new[] { i0, i0 + stride, i0 + 1, i0 + 1, i0 + stride, i0 + stride + 1 });
                }
        }

        /// <summary>A conifer crown: whorls of drooping boughs, each bough two
        /// crossed cards so it holds up from every yaw (never camera-facing).</summary>
        private static void AddCrown(Acc a, Tree t, int whorls, int perWhorl, float crownFrac,
            float radScale, Color tint, bool crossed, bool dead = false)
        {
            float cb = t.h * crownFrac;                       // bare trunk below this
            for (int w = 0; w < whorls; w++)
            {
                float f = (w + 0.5f) / whorls;
                float y = Mathf.Lerp(cb, t.h * 0.99f, f);
                Vector3 c0 = TrunkAt(t, y);
                float rr = radScale * t.h * 0.20f * Mathf.Pow(1f - f, 0.62f) + 0.35f;
                int n = Mathf.Max(3, Mathf.RoundToInt(perWhorl * (1f - 0.45f * f)));
                for (int k = 0; k < n; k++)
                {
                    float ang = (k + Hash3(w, k, (int)t.sd, 5107) * 0.8f) / n * Mathf.PI * 2f + w * 0.7f;
                    var outDir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    float droop = 0.30f + 0.35f * Hash3(w, k, 1, 5107);
                    Vector3 up = (outDir - Vector3.up * droop).normalized;   // stem -> tip, drooping
                    float len = rr * (0.72f + 0.5f * Hash3(w, k, 2, 5107));
                    Vector3 c = c0 + up * (len * 0.55f);
                    var rect = dead ? DeadTwig
                             : Sprigs[(int)(Hash3(w, k, 3, 5107) * Sprigs.Length) % Sprigs.Length];
                    float halfW = len * 0.62f * (rect.width / Mathf.Max(rect.height, 1e-3f));
                    Vector3 right = Vector3.Cross(up, Vector3.up).normalized;
                    if (right.sqrMagnitude < 0.5f) right = Vector3.right;
                    // a crown normal (outward from the trunk axis) instead of the
                    // card's own facing: the boughs shade as one soft mass, not as
                    // a stack of flat plates
                    Vector3 nrm = (c - c0 + Vector3.up * 0.4f).normalized;
                    AddCard(a, c, right * halfW, up * (len * 0.55f), nrm, rect, tint);
                    if (crossed)
                    {
                        Vector3 r2 = Vector3.Cross(up, right).normalized;
                        AddCard(a, c, r2 * (halfW * 0.85f), up * (len * 0.52f), nrm, rect, tint);
                    }
                }
            }
        }

        // ------------------------------------------------------------ the canopy
        // A shell of foliage over the whole wood — this is what makes looking UP
        // frightening instead of empty, and what turns the far trunks into "trees
        // behind trees". It is missing over the clearing (that is the Lichtung)
        // and TORN open toward the moon, which is where the shafts come through.
        private static float MoonAzimuth()
        {
            var m = MoonDir;
            return Mathf.Atan2(m.x, m.z);
        }

        /// <summary>0 = open sky, 1 = solid canopy.</summary>
        private static float CanopyMask(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearR + 0.4f, ClearR + 5.5f, r));
            // the tear toward the moon: a wedge the moon and its shafts come through
            float az = Mathf.Atan2(x, z) - MoonAzimuth();
            while (az > Mathf.PI) az -= 2f * Mathf.PI;
            while (az < -Mathf.PI) az += 2f * Mathf.PI;
            float wedge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.52f, Mathf.Abs(az)))
                        + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15.5f, 19f, r));
            cover *= Mathf.Clamp01(wedge);
            // ragged, never a lid
            cover *= 0.45f + 0.75f * Fbm2(x * 0.20f + 61f, z * 0.20f, 3, 993);
            return Mathf.Clamp01(cover);
        }

        private static float CanopyY(float r) => 6.8f + 0.30f * (r - ClearR);

        // ==================================================== CANOPY SHADOW (bake)
        // USER FINDING, ModBuild 137 (hardware): "Die Lichstrahlen (die jetzt dem
        // Mond folgen) die durch die Bäume kommen im Waldgebiet clippen durch die
        // Bäume, ich würde hier gerne das die Bäume entsprechende Schatten
        // werfen."
        //
        // The room prefabs are SCRIPT-FREE and contain not one Unity Light — all
        // of the wood's lighting is baked per material by LightRig — so "cast
        // shadows" cannot mean shadow casting here. There is nothing to cast
        // FROM. It has to be DATA, baked at build time and read by the only two
        // shaders that carry the moon into the open air and onto the floor:
        // EnvShaft (the blades) and EnvGround (the floor's directional term).
        //
        // WHAT IS BAKED: an ORTHOGRAPHIC DEPTH MAP of the trees along the moon
        // bearing. A grid is laid on a plane facing the moon, and each texel
        // stores how far DOWN-LIGHT the nearest piece of tree is, in metres,
        // linearly encoded. A fragment projects itself onto the same plane and
        // asks "is anything in my texel nearer to the moon than I am?".
        //
        // WHY DEPTH AND NOT A BINARY MASK. This is the whole design, and it is
        // not an optimisation — a mask cannot work at all here. ModBuild 137
        // deliberately ran the shafts UP THROUGH THE TEAR in the canopy so the
        // light is seen entering where the moon is seen. A shaft's top and the
        // boughs around that tear project to THE SAME TEXELS: an orthographic
        // projection cannot tell a point above the canopy from a point below it,
        // because both sit on the same ray to the moon. A binary "is this texel
        // occluded" map would therefore black out the top of every shaft — the
        // one thing the previous round exists to show — while leaving the
        // clipping further down exactly as it is. With a depth the test becomes
        // the correct one: shadow only where the stored occluder is NEARER TO
        // THE MOON than the fragment (plus a bias), which is literally "the
        // segment from here to the moon is blocked".
        //
        // TWO MAPS SINCE ModBuild 142, and everything above is still true of both
        // — same box, same basis, same raster, same encoded depth range. What
        // differs is what is DONE with the answer: the trunks keep the crisp
        // 512-texel depth map and its all-or-nothing bite ramp, and the crowns get
        // a quarter-resolution AREA-COVERAGE map answered linearly and softly. The
        // argument, the mechanism it fixes and what the second texture costs are in
        // the SHAFT MASS block on the buffers below; read it before touching either
        // response, because the two are only correct as a pair.
        //
        // WHAT IS AN OCCLUDER: the trunks and every crown/canopy card, i.e. the
        // three accumulators the forest already has in hand at that point in the
        // build. Deliberately NOT the ground (nothing self-shadows, so the bias
        // can stay at a few centimetres and there is no acne to fight) and not
        // the props: the rocks and deadfall are under a metre tall, they cast a
        // 1.2 m smudge onto floor that is already at 1-5% brightness, and they
        // are placed AFTER the shafts in this room, so taking them would mean
        // re-ordering an approved room for an effect nobody can see.
        //
        // FOLIAGE IS ALPHA-TESTED, AND SO IS THE BAKE. This was got wrong once
        // and the log is what caught it: rasterising the crowns as solid quads
        // put 81.2% of the map in shadow and scored all three shafts at 0% clear,
        // i.e. it did not shadow the beams, it deleted them. A bough is a SPRITE
        // — a rect of Imported/Textures/fir_twig_alb.png whose alpha holds a fir
        // sprig on transparency, roughly half coverage — and a crown is a dozen
        // of them. Treated as solid cards, a crown is a disc and the wood is a
        // lid. So the raster interpolates each triangle's UV and only writes
        // depth where the ATLAS ALPHA is over the cutoff, which is the same
        // question EnvRoomCutout asks per fragment.
        //
        // The atlas is read by decoding the PNG into a scratch Texture2D
        // (ImageConversion.LoadImage) rather than by flipping isReadable on the
        // imported asset: isReadable keeps a CPU copy of a 1k BC7 texture alive
        // in the BUNDLE for the whole session, which is about a megabyte of
        // runtime memory bought for a build-time question.
        //
        // The three-corner stamp that closes pinholes in a solid card is
        // switched OFF for cutout sources for the same reason: a corner of a
        // sprig rect is nearly always transparent, so stamping it would print
        // exactly the sprig's empty margin into the map.
        //
        // The TRUNKS do not receive this. They are EnvRoom, which the cellar
        // shares, and a trunk's moonlit side is the contrast recipe ModBuild 134
        // spent a round building — it is not something to put a shadow term
        // under without a round of its own.
        //
        // MAXIMUM THROW, and why this room does not want a physically exact
        // shadow. The moon stands at 40 deg, so the ray from a point on the
        // clearing floor to the moon leaves obliquely and spends the next 20-30
        // metres inside the wood; and the shafts' own tops now stand at r ~20 m,
        // where CanopyMask's outer term (InverseLerp(15.5, 19, r)) has already
        // closed the tear again — which is exactly why the length solver runs
        // into its lenMax clamp for all three beams. An exact "is my whole path
        // to the moon clear" test therefore answers NO everywhere, and it is
        // right: this is a wood, and a wood at night has no moonlight on its
        // floor. The clearing, the tear and the three shafts are an AUTHORED
        // FICTION, and it is the fiction the user has approved twice.
        //
        // So an occluder only casts while it is within MaxThrow metres UP-LIGHT
        // of the fragment, releasing softly over the last Fall metres instead of
        // cutting. MaxThrow is measured ALONG THE BEAM, which is also along a
        // shaft's own axis, and that is what fixes the numbers:
        //   * a trunk at the clearing edge (r 6.2-10 m) shadows the floor for
        //     MaxThrow x cos(40) horizontally — the rake across the clearing
        //     that IS the effect the user asked for;
        //   * a shaft that passes through a trunk goes dark for MaxThrow of its
        //     own length below the crossing and then comes back, which reads as
        //     "the tree casts a shadow in the beam" rather than "the beam ends";
        //   * the canopy 20-30 m up-light — the roof over the whole wood — is
        //     past the throw and does not participate at all.
        // The two numbers are PER RECEIVER and live in Look, next to the bake
        // call: a floor and a column of lit mist are not asking the same
        // question, and one throw for both was measured and rejected.
        //
        // The room is world-fixed once placed and the trees never move, so what
        // is baked here is valid forever. Everything is expressed in the room
        // root's OWN frame — the frame 'Ground' and 'MoonShafts' are placed in —
        // for the same reason the light rig writes _DirDir and _L0Pos in object
        // space: it is the only frame the runtime's placement yaw and scale
        // cannot move under it.
        private sealed class CanopyShadowBake
        {
            // Square, and a power of two: TextureImporter.maxTextureSize only
            // takes values off the power-of-two ladder, and a cap that does not
            // match the image is a downscale nobody would notice until the
            // shadows went soft.
            //
            // With the receiver box the caller passes (see THE BOX in
            // BuildForestRoom) this comes to ~5.5 x 4.4 cm per texel, so a near
            // trunk is 9-15 texels across its flare and a far one 5-10. That
            // margin is the whole game: the penumbra has to be a few texels wide
            // to hide the grid, and if a trunk is only four texels across then
            // the filter that hides the grid also erases the shadow. The first
            // pass got exactly that wrong — 8.6 cm texels and a 0.26 m disc left
            // 5.1% of the clearing shaded on average but only 0.6% of it half
            // shaded, i.e. a faint wash where trunk shadows should be.
            //
            // 1024 would halve the texel again and quadruple the bundle cost for
            // detail that mostly lands on ground the vertex fade has already
            // taken to 2%. Bundle cost is reported by Report().
            public const int Res = 512;
            // Real depths are encoded into 0..Enc; 1.0 is the "nothing here"
            // sentinel, and reserving 2% is what keeps a genuine occluder at the
            // far plane from ever colliding with it.
            private const float Enc = 0.98f;

            public readonly Vector3 Origin, AxisU, AxisV, Travel;
            public readonly float ExtentU, ExtentV;
            /// <summary>Bias along the beam, in metres. Nothing self-shadows (the
            /// ground and the blades are receivers only), so this exists purely
            /// to absorb the 16-bit quantisation and the interpolation, and it
            /// can stay small — which is what keeps a trunk's shadow ATTACHED to
            /// its foot instead of peter-panning half a metre away from it.</summary>
            public float Bias = 0.06f;
            /// <summary>What one RECEIVER makes of the map. Penumbra is taste;
            /// everything else is measured, and all of it is PER RECEIVER because
            /// the floor and the open air are not asking the same question.
            ///
            /// MinVis — the MINIMUM VISIBILITY, and the safety rail of the whole
            /// feature. A fragment the map calls fully occluded keeps this much
            /// of its moon term and no less, so a beam can be cut to a hard dark
            /// band and still ARRIVE at the pool it lands in, and a patch of
            /// shadowed floor is still floor rather than a hole. It goes to the
            /// shaders as 1 - MinVis, which is what used to be called "strength"
            /// — the two are one number seen from opposite ends, and the code now
            /// names it from the end that has to be defended. It is the rail that
            /// lets Throw and the bite below be aggressive: the first pass had no
            /// rail and extinguished all three shafts (81% occluded, 0% clear).
            ///
            /// BiteLo/BiteHi — the coverage remap, and THE fix for ModBuild 139's
            /// "the beams are still smooth". A fir crown is an alpha-tested sieve
            /// (23.6% of the atlas is over the cutoff), so a beam crossing the
            /// crown mass collects a MOTTLE of 0.2-0.5 coverage over metres of its
            /// length; averaged linearly that is a uniform dimming and reads as
            /// "the beam got fainter", never as "a bough crosses the beam". The
            /// log said the lower runs were 42% occluded and the screenshot showed
            /// no band anywhere, and both were true at once. Coverage below BiteLo
            /// now counts for nothing and coverage above BiteHi for everything,
            /// which is also the physics (extinction is exponential in the needle
            /// mass crossed, not linear in a sub-texel average). The tap disc still
            /// averages BEFORE the remap, so a shadow's own edge keeps its
            /// penumbra; what the ramp deletes is the flat middle.
            ///
            /// MaxThrow/Fall are the authored fiction — see the MAXIMUM THROW
            /// block above the class.
            ///
            /// THE FLOOR wants a long throw: a trunk at the clearing edge is
            /// 6-12 m up-light of the middle of the clearing, and that rake
            /// across the floor is the effect the user asked for.
            ///
            /// THE BLADES want a shorter one, and this had to be measured to be
            /// believed. Air five metres up inside the wood is not like floor:
            /// the ray from it to the moon climbs 0.84 m per metre while the
            /// canopy only climbs 0.30, so at 6-9 m up-light it is still deep
            /// inside the crown mass at r 10-16. On the floor those crowns are
            /// 14 m away and the throw excludes them; from mid-air they are 3-8 m
            /// away and a floor-sized throw includes them. Measured with a 9 m
            /// throw and NO bite ramp the beams' lower runs came out 20-48% lit —
            /// the visible half of every shaft evaporating into a smooth
            /// grey. With the ramp that trade changes completely: a long throw is
            /// now what FINDS the boughs, and the ramp is what decides which of
            /// them are solid enough to draw.
            ///
            /// ...and ModBuild 142 confines the ramp to the TRUNKS, because on the
            /// needles it was the cause of the comb the user then photographed.
            /// The ramp is still exactly right for a silhouette and was exactly
            /// wrong for a sieve; see the SHAFT MASS block below.
            ///
            /// FolVis/FolReach/FolFall/FolOnset are the SAME four questions asked
            /// of the mass map, and they are separate numbers rather than a scale
            /// on the ones above because the whole point of ModBuild 142 is that a
            /// crown and a trunk may not share a response (see the SHAFT MASS
            /// block). There is no bite ramp among them: the mass answers LINEARLY
            /// in its coverage, which is what makes it a dimming and not a bar.
            /// FolOnset is the extra one — how many metres of depth the mass takes
            /// to reach full effect once it is up-light of the fragment. The trunk
            /// layer switches on the instant it is passed, because a trunk has a
            /// front surface; a crown does not, so it fades in, and that softness
            /// IN DEPTH is what stops a bough drawing a hard horizontal line across
            /// a beam the way its silhouette drew hard vertical ones.</summary>
            public struct Look
            {
                public float MinVis, Penumbra, MaxThrow, Fall, BiteLo, BiteHi;
                public float FolVis, FolReach, FolFall, FolOnset;
                /// <summary>What the shaders receive: a fully occluded fragment is
                /// multiplied by 1 - Strength, i.e. by MinVis.</summary>
                public float Strength => 1f - MinVis;
                /// <summary>The same, for a texel of crown at full coverage.</summary>
                public float FolStrength => 1f - FolVis;
                public Look(float minVis, float penumbra, float maxThrow, float fall,
                            float biteLo, float biteHi,
                            float folVis, float folReach, float folFall, float folOnset)
                {
                    MinVis = minVis; Penumbra = penumbra; MaxThrow = maxThrow; Fall = fall;
                    BiteLo = biteLo; BiteHi = biteHi;
                    FolVis = folVis; FolReach = folReach; FolFall = folFall; FolOnset = folOnset;
                }
            }

            /// <summary>One welded mesh to rasterise. Mask is the alpha-test
            /// coverage of the source's atlas, one bool per atlas texel, or null
            /// for solid geometry (the trunks). Fol says which of the TWO MAPS the
            /// source writes into — see the SHAFT MASS block below; it is set from
            /// the caller's intent (AddSolid vs AddCutout) and not inferred from
            /// Mask, because "is this a sieve" and "is this a diffuse mass" are two
            /// different questions that happen to have the same answer today.</summary>
            private struct Src
            {
                public Acc A; public bool[] Mask; public int MW, MH; public string Name;
                public bool Fol;
            }

            private readonly List<Src> _src = new List<Src>();
            // TWO LAYERS, and this is not an optimisation either — one layer
            // cannot answer the question the throw asks. A texel holds a whole
            // COLUMN of trunks on one ray to the moon, near and far bands
            // together, and behind them the floor. Keeping only the NEAREST
            // occluder stores the far band 25 m up-light, so the floor measures
            // its distance to THAT, finds it past the throw, and reports itself
            // lit — the trunk that is 4 m up-light of it never gets a vote. That
            // is exactly what the first measured bake did: 5.2% of the clearing
            // shaded on average but 0.9% of it half shaded, i.e. no trunk
            // shadows at all. (It was the CANOPY that filled the near layer then;
            // the canopy has since moved to the mass map below, and the argument
            // survives the move unchanged because a wood 28 m deep stacks trunks
            // on one bearing all by itself.)
            //
            //   _zN = the occluder NEAREST the moon. What a blade hanging in the
            //         air needs: the thing above it is the thing that shades it.
            //   _zF = the DEEPEST occluder. What the floor needs, and for the
            //         floor it is exactly right rather than an approximation —
            //         the floor is below everything, so the deepest occluder is
            //         always the nearest one up-light of it.
            // EnvGround therefore reads _zF alone. EnvShaft takes whichever of
            // the two casts (max of the two throw tests): _zN catches a far trunk
            // standing over the beam, _zF catches the trunk the beam runs
            // through. Only a third layer strictly between them is missed, and a
            // miss is a shadow that is not drawn, never one that is drawn wrongly.
            private readonly float[] _zN = new float[Res * Res];
            private readonly float[] _zF = new float[Res * Res];

            // ======================================== SHAFT MASS — THE SECOND MAP
            // USER FINDING, ModBuild 141 (hardware): "Die Schatten im Wald
            // funktionieren, allerdings da auch das Gestrüpp an den Bäumen Schatten
            // wirft sieht es etwas merkwürdig aus."
            //
            // He is exactly right about the cause, and the SHAPE of what he saw
            // names the mechanism: the beams were cut into hard VERTICAL STRIPES
            // running their whole length, a comb rather than a dapple.
            //
            // WHY A COMB, AND WHY ONLY ON THE BEAMS. A shaft runs ALONG THE LIGHT,
            // and the map's two axes are both PERPENDICULAR to the light. So (u,v)
            // is not merely slowly varying down a beam, it is EXACTLY CONSTANT —
            // moving one metre along dir changes u by dot(dir, AxisU) = 0 and v by
            // dot(dir, AxisV) = 0. AddShaft's two blades make that literal: blade
            // one is spanned by Cross(dir, up), which is AxisU, and blade two by
            // Cross(dir, that), which is AxisV. A vertical strip of blade one is
            // therefore ONE TEXEL COLUMN of the map, read over and over down its
            // whole length, with only the depth changing. The map is point-sampled,
            // so stepping one texel sideways swaps a whole tap and moves the seven-
            // tap average by 1/7; the bite ramp (span 0.38) multiplies that step by
            // 2.6 and turns it into a third of full shadow. One texel of sideways
            // motion = a third of full shadow, held down the entire beam. That is
            // the comb, and its teeth are 5.5 cm wide because the texels are.
            // The FLOOR never showed it because a floor moves in u AND v AND depth
            // at once, so the same grid lands as 2D dapple under albedo detail.
            //
            // The needle sieve is what FILLS those columns with a different value
            // each time: a fir sprig is 23.6% coverage of alpha-tested speckle at
            // 5.5 cm, which is noise at exactly the texel scale.
            //
            // THE FIX IS TO SPLIT THE OCCLUDERS BY SCALE, because a trunk and a
            // crown are not the same kind of thing and must not share a response:
            //   * A TRUNK is solid, half a metre thick and has a silhouette. It
            //     keeps this map, the bite ramp and the small tap disc, and it goes
            //     on casting the crisp dark bar the user asked for two rounds ago
            //     and likes. With the needles gone this map holds nothing BUT
            //     silhouettes, so the ramp now sharpens an edge instead of
            //     quantising noise.
            //   * A CROWN is a diffuse mass. It gets its own map, at a quarter of
            //     the resolution, holding AREA COVERAGE rather than a binary hit —
            //     which is the low-pass, done once at bake time where it is exact
            //     and free, rather than by taps at runtime where it never can be.
            //     It is read BILINEARLY and answered LINEARLY, with a long soft
            //     onset in depth, so it can only ever dim.
            //
            // THE MASS MAP, channel by channel. 128 x 128 over the same box: 22 x
            // 17.6 cm per texel, and each texel is the exact area average of the
            // 4 x 4 block of the 5.5 cm raster under it, so coverage arrives in
            // seventeenths and not as a hit/miss. Bilinear on top of that is a
            // ~44 cm reconstruction filter — half a metre, which is a bough. A
            // needle is 2 cm and is gone; a bough survives; a crown survives
            // completely. That IS the "represent the crown as a mass" instruction,
            // expressed as a filter width.
            //   R = the NEAREST foliage depth in the block, G = the DEEPEST,
            //   B = the coverage of the mass AT R, A = the coverage of the mass AT
            //       G — each counted over a FolWindow-metre slab of depth around
            //       its own extreme.
            // The two coverages are the reason A is used rather than left at 255,
            // and they are what stops the roof being charged to a bough: a texel
            // in the wood typically holds the canopy shell 25 m up-light AND a
            // crown 4 m up-light, and one shared coverage would bill the beam for
            // both while only the crown is inside the throw.
            //
            // WHAT IT COSTS: 128^2 x RGBA32 = 64 KiB in the bundle beside the
            // 1024 KiB of the trunk map, i.e. 6% more for the layer that carries
            // three quarters of the wood. Report() prints both.
            //
            // WHY NOT SQUEEZE IT INTO THE SPARE BITS OF THE FIRST MAP: there are
            // none. RGBA is already two 16-bit depths, and the trunk layer is the
            // one that may NOT lose precision — 8-bit over this room's 46 m span
            // is an 18 cm quantum, which is three times the bias and would detach
            // every trunk's shadow from its own foot (the note on Save()). The
            // mass layer is the one that can afford 8 bits, because its onset is
            // metres long and its own texels are 22 cm.
            public const int FolRes = 128;
            private const int FolDown = Res / FolRes;
            /// <summary>How deep a slab counts as "the mass at this extreme", in
            /// metres along the bearing. A fir crown is 2-4 m through, so 3 m is
            /// one crown's worth: wide enough that a whole bough counts against
            /// itself, narrow enough that the roof 20 m behind it does not.</summary>
            private const float FolWindow = 3.0f;
            // full-resolution scratch: the raster writes here, Bake() box-filters
            // it down to FolRes at the end and throws these away
            private readonly float[] _fN = new float[Res * Res];
            private readonly float[] _fF = new float[Res * Res];
            private readonly bool[] _fHit = new bool[Res * Res];
            // ...and the shipped grid, quantised to the same 8 bits the PNG will
            // carry, so Visible() below answers with the texture's numbers rather
            // than with the ones it wishes the texture had.
            private readonly float[] _mN = new float[FolRes * FolRes];
            private readonly float[] _mF = new float[FolRes * FolRes];
            private readonly float[] _mCn = new float[FolRes * FolRes];
            private readonly float[] _mCf = new float[FolRes * FolRes];

            private float _wNear, _wSpan = 1f;
            private int _tris, _outside, _filled, _cut, _folFilled;
            private float _folCovMean;
            private float _hitNear = float.MaxValue, _hitFar = float.MinValue;
            private float _clearShadow, _clearDeep;
            private string _path, _folPath;

            private float EncPerMetre => Enc / _wSpan;
            private float BiasEnc => Bias * EncPerMetre;

            /// <summary>The light-plane basis, derived from the ONE authored moon
            /// bearing. roiR/yLo/yHi describe the RECEIVER region the map has to
            /// cover — and only the receivers matter for sizing, because an
            /// occluder shadows a receiver only when it projects into the very
            /// same texel.</summary>
            public CanopyShadowBake(Vector3 moonDir, float roiR, float yLo, float yHi)
            {
                Vector3 L = moonDir.normalized;
                Travel = -L;                                       // the way the light travels
                AxisU = Vector3.Cross(Vector3.up, L).normalized;   // horizontal, across the bearing
                AxisV = Vector3.Cross(L, AxisU).normalized;        // the light plane's own "up"
                // The (u,v) footprint of a cylinder of radius roiR between yLo
                // and yHi. AxisU is horizontal, so u is simply +-roiR; AxisV is
                // tilted by the moon's altitude, so v takes both a vertical share
                // of the height and a horizontal share of the radius.
                float hV = new Vector2(AxisV.x, AxisV.z).magnitude;
                float vLo = Mathf.Min(AxisV.y * yLo, AxisV.y * yHi) - hV * roiR;
                float vHi = Mathf.Max(AxisV.y * yLo, AxisV.y * yHi) + hV * roiR;
                ExtentU = 2f * roiR;
                ExtentV = vHi - vLo;
                Origin = AxisV * ((vLo + vHi) * 0.5f);
                // 1.0 = 'no occluder' for the near layer, 0.0 for the far one.
                // Both sentinels are unreachable by real geometry because the
                // measured range is padded half a metre at each end.
                for (int i = 0; i < _zN.Length; i++) _zN[i] = 1f;
            }

            /// <summary>(u, v) across the map in 0..1, plus w = metres down-light
            /// from the light plane through Origin. w does not depend on the
            /// encoding range, which is what lets the range be MEASURED.</summary>
            public Vector3 Plane(Vector3 p)
            {
                Vector3 r = p - Origin;
                return new Vector3(Vector3.Dot(r, AxisU) / ExtentU + 0.5f,
                                   Vector3.Dot(r, AxisV) / ExtentV + 0.5f,
                                   Vector3.Dot(r, Travel));
            }

            private float Depth01(float w) => (w - _wNear) / _wSpan * Enc;

            /// <summary>Solid geometry: every triangle casts, into the CRISP map.
            /// The trunks, and since ModBuild 142 nothing else.</summary>
            public void AddSolid(Acc a, string name) =>
                _src.Add(new Src { A = a, Name = name });

            /// <summary>Alpha-tested geometry: a triangle casts only where its
            /// atlas alpha is over the cutoff. The crowns and the canopy shell.
            /// The atlas is decoded from its PNG into a scratch texture — never
            /// by making the imported asset readable, which would keep a CPU copy
            /// of it alive in the bundle for a build-time question.
            ///
            /// Since ModBuild 142 this writes into the MASS map, not the crisp
            /// one: the alpha test still runs at the full 5.5 cm raster (it is the
            /// only honest way to know what a sprig covers), but the result is
            /// AREA-AVERAGED down to 22 cm before anybody reads it, so the crown
            /// arrives as a mass and not as a stencil of individual needles.</summary>
            public void AddCutout(Acc a, string name, string pngPath, float cutoff)
            {
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tmp.LoadImage(File.ReadAllBytes(pngPath)))
                    throw new Exception("Canopy shadow: cannot decode atlas " + pngPath);
                int w = tmp.width, h = tmp.height;
                var raw = tmp.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tmp);
                byte cut = (byte)Mathf.Clamp(Mathf.RoundToInt(cutoff * 255f), 0, 255);
                var mask = new bool[w * h];
                int solid = 0;
                for (int i = 0; i < raw.Length; i++)
                    if (raw[i].a >= cut) { mask[i] = true; solid++; }
                _src.Add(new Src { A = a, Mask = mask, MW = w, MH = h, Name = name, Fol = true });
                Debug.Log($"[GloomhavenVR][Env] Canopy shadow: alpha atlas {Path.GetFileName(pngPath)} "
                          + $"{w}x{h}, {solid * 100f / mask.Length:F1}% of it is over cutoff {cutoff:F2} "
                          + $"— that is the fraction of every '{name}' card that can cast.");
            }

            /// <summary>Two passes: measure the depth range, then rasterise. The
            /// range is measured and not guessed because 16 bits spread over a
            /// corner-to-corner guess is the one thing that could put visible
            /// banding into a shadow edge.</summary>
            public void Bake(Func<float, float, float> groundY, float roiR)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                void Grow(Vector3 p)
                {
                    Vector3 c = Plane(p);
                    if (c.x < -0.02f || c.x > 1.02f || c.y < -0.02f || c.y > 1.02f) return;
                    if (c.z < lo) lo = c.z;
                    if (c.z > hi) hi = c.z;
                }
                foreach (var s in _src)
                    foreach (var v in s.A.V) Grow(v);
                // The receivers: the floor under the map. The blades need no pass
                // of their own — they hang between the floor and the canopy, so
                // they are inside a range that already holds both. (They are also
                // built AFTER this, from this.)
                for (int j = 0; j <= 32; j++)
                    for (int k = 0; k <= 32; k++)
                    {
                        float x = Mathf.Lerp(-roiR, roiR, j / 32f);
                        float z = Mathf.Lerp(-roiR, roiR, k / 32f);
                        if (x * x + z * z > roiR * roiR) continue;
                        Grow(new Vector3(x, groundY(x, z), z));
                    }
                if (hi <= lo) throw new Exception("Canopy shadow: nothing projects into the map.");
                _wNear = lo - 0.5f;                   // half a metre of headroom at each end
                _wSpan = (hi + 0.5f) - _wNear;
                foreach (var s in _src)
                {
                    var V = s.A.V; var UV = s.A.UV; var T = s.A.T;
                    for (int i = 0; i < T.Count; i += 3)
                        Tri(Plane(V[T[i]]), Plane(V[T[i + 1]]), Plane(V[T[i + 2]]),
                            UV[T[i]], UV[T[i + 1]], UV[T[i + 2]], s);
                }
                for (int i = 0; i < _zF.Length; i++) if (_zF[i] > 0f) _filled++;
                BuildMass();
            }

            /// <summary>Box-filter the 5.5 cm foliage raster down to the 22 cm mass
            /// grid, and quantise it to the eight bits the PNG will carry — the
            /// quantisation happens HERE and not in Save() so that Visible(), which
            /// places the shafts and writes the build log, is answering with the
            /// texture's own numbers rather than with the ones the bake wishes it
            /// had. Getting that wrong is how a search picks a beam the shader then
            /// draws differently.
            ///
            /// The two coverages are counted over a FolWindow slab around each
            /// extreme, so the crown 4 m up-light and the roof 25 m up-light are
            /// billed separately. A sub-texel that is thin enough in depth to lie
            /// in both slabs counts in both, which is correct: it really is part of
            /// both masses.</summary>
            private void BuildMass()
            {
                float win = FolWindow * EncPerMetre;
                double covSum = 0; int covN = 0;
                for (int y = 0; y < FolRes; y++)
                    for (int x = 0; x < FolRes; x++)
                    {
                        float near = 1f, far = 0f; int hits = 0;
                        for (int dy = 0; dy < FolDown; dy++)
                            for (int dx = 0; dx < FolDown; dx++)
                            {
                                int k = (y * FolDown + dy) * Res + (x * FolDown + dx);
                                if (!_fHit[k]) continue;
                                hits++;
                                if (_fN[k] < near) near = _fN[k];
                                if (_fF[k] > far) far = _fF[k];
                            }
                        int i = y * FolRes + x;
                        if (hits == 0) { _mN[i] = 1f; _mF[i] = 0f; _mCn[i] = 0f; _mCf[i] = 0f; continue; }
                        int cn = 0, cf = 0;
                        for (int dy = 0; dy < FolDown; dy++)
                            for (int dx = 0; dx < FolDown; dx++)
                            {
                                int k = (y * FolDown + dy) * Res + (x * FolDown + dx);
                                if (!_fHit[k]) continue;
                                if (_fN[k] <= near + win) cn++;
                                if (_fF[k] >= far - win) cf++;
                            }
                        int block = FolDown * FolDown;
                        _mN[i] = Q8(near); _mF[i] = Q8(far);
                        _mCn[i] = Q8(cn / (float)block); _mCf[i] = Q8(cf / (float)block);
                        _folFilled++;
                        covSum += Mathf.Max(_mCn[i], _mCf[i]); covN++;
                    }
                _folCovMean = covN > 0 ? (float)(covSum / covN) : 0f;
            }

            /// <summary>Round-trip through one byte, exactly as the PNG will.</summary>
            private static float Q8(float v) =>
                Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * 255f), 0, 255) / 255f;

            private void Tri(Vector3 a, Vector3 b, Vector3 c,
                Vector2 ua, Vector2 ub, Vector2 uc, Src s)
            {
                _tris++;
                // texel-CENTRE space: texel n covers u in [n/Res, (n+1)/Res), so
                // its centre sits at u*Res - 0.5 == n
                float ax = a.x * Res - 0.5f, ay = a.y * Res - 0.5f;
                float bx = b.x * Res - 0.5f, by = b.y * Res - 0.5f;
                float cx = c.x * Res - 0.5f, cy = c.y * Res - 0.5f;
                // A SOLID card turned edge-on to the moon can cover no texel
                // centre at all, which would punch a pinhole through a trunk.
                // Stamping the three corners as well costs three writes and
                // closes them, and it is also what carries the degenerate
                // triangles the edge-on test below drops.
                //
                // NOT for alpha-tested sources: the corner of a sprig rect is
                // nearly always the transparent margin around the sprig, so a
                // stamp there prints exactly the part of the card that is not
                // there. A cutout source is a sieve by design and does not want
                // its pinholes closed.
                if (s.Mask == null)
                { Stamp(ax, ay, a.z, s.Fol); Stamp(bx, by, b.z, s.Fol); Stamp(cx, cy, c.z, s.Fol); }
                int x0 = Mathf.CeilToInt(Mathf.Min(ax, Mathf.Min(bx, cx)));
                int x1 = Mathf.FloorToInt(Mathf.Max(ax, Mathf.Max(bx, cx)));
                int y0 = Mathf.CeilToInt(Mathf.Min(ay, Mathf.Min(by, cy)));
                int y1 = Mathf.FloorToInt(Mathf.Max(ay, Mathf.Max(by, cy)));
                if (x1 < 0 || y1 < 0 || x0 > Res - 1 || y0 > Res - 1) { _outside++; return; }
                if (x0 < 0) x0 = 0;
                if (y0 < 0) y0 = 0;
                if (x1 > Res - 1) x1 = Res - 1;
                if (y1 > Res - 1) y1 = Res - 1;
                float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (det > -1e-7f && det < 1e-7f) return;
                float inv = 1f / det;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) * inv;
                        if (l1 < -1e-4f || l1 > 1.0001f) continue;
                        float l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) * inv;
                        if (l2 < -1e-4f || l2 > 1.0001f) continue;
                        float l3 = 1f - l1 - l2;
                        if (l3 < -1e-4f) continue;
                        if (s.Mask != null)
                        {
                            // the same alpha test EnvRoomCutout runs per fragment,
                            // asked once per shadow texel
                            float mu = ua.x * l1 + ub.x * l2 + uc.x * l3;
                            float mv = ua.y * l1 + ub.y * l2 + uc.y * l3;
                            int mx = Mathf.Clamp((int)(mu * s.MW), 0, s.MW - 1);
                            int my = Mathf.Clamp((int)(mv * s.MH), 0, s.MH - 1);
                            if (!s.Mask[my * s.MW + mx]) { _cut++; continue; }
                        }
                        Put(x, y, l1 * a.z + l2 * b.z + l3 * c.z, s.Fol);
                    }
            }

            private void Stamp(float fx, float fy, float w, bool fol) =>
                Put(Mathf.RoundToInt(fx), Mathf.RoundToInt(fy), w, fol);

            /// <summary>One texel write, into whichever of the two maps this source
            /// belongs to. Both maps share the raster, the box and the encoded
            /// depth range — they differ only in what is DONE with the answer, and
            /// keeping the geometry side identical is what lets the two be argued
            /// against one another in the log.</summary>
            private void Put(int x, int y, float w, bool fol)
            {
                if (x < 0 || x >= Res || y < 0 || y >= Res) return;
                float z = Depth01(w);
                if (z < 0f || z > Enc) return;
                int k = y * Res + x;
                if (fol)
                {
                    if (!_fHit[k]) { _fHit[k] = true; _fN[k] = z; _fF[k] = z; }
                    else { if (z < _fN[k]) _fN[k] = z; if (z > _fF[k]) _fF[k] = z; }
                }
                else
                {
                    if (z < _zN[k]) _zN[k] = z;
                    if (z > _zF[k]) _zF[k] = z;
                }
                if (w < _hitNear) _hitNear = w;
                if (w > _hitFar) _hitFar = w;
            }

            /// <summary>Is this point inside the map at all (with a margin, in
            /// map fractions)? Outside it the shaders read "lit" — which is right
            /// for the far floor and WRONG for a shaft, so the shaft placement
            /// asserts on this instead of trusting the box arithmetic.</summary>
            public bool Inside(Vector3 p, float margin)
            {
                Vector3 c = Plane(p);
                return c.x > margin && c.x < 1f - margin
                    && c.y > margin && c.y < 1f - margin;
            }

            /// <summary>One tap, exactly as CsTap does it in the two shaders:
            /// point sample with CLAMP addressing, shadow only when the stored
            /// occluder is nearer the moon, and only within the throw. `useNear`
            /// picks EnvShaft's reading (max of both layers) over EnvGround's
            /// (the far layer alone, which is exact for a floor).</summary>
            private float Throw(float d, Look k) =>
                d <= 0f ? 0f
                        : Mathf.Clamp01((k.MaxThrow * EncPerMetre - d)
                                        / (Mathf.Max(k.Fall, 1e-3f) * EncPerMetre));

            private float Tap(float u, float v, float z, bool useNear, Look k)
            {
                int x = Mathf.Clamp((int)(u * Res), 0, Res - 1);
                int y = Mathf.Clamp((int)(v * Res), 0, Res - 1);
                int idx = y * Res + x;
                float f = _zF[idx];
                // f == 0 is the far layer's "nothing here". It has to be gated:
                // z - 0 is a SMALL depth for anything high in the room, so an
                // ungated sentinel would put a shadow on every shaft top.
                float sh = f > 0f ? Throw(z - f, k) : 0f;
                if (useNear) sh = Mathf.Max(sh, Throw(z - _zN[idx], k));
                return 1f - sh;
            }

            /// <summary>The mass map's throw, and the one place the two occluder
            /// classes really do behave differently in code rather than just in
            /// their numbers: a smoothstep ONSET over FolOnset metres before the
            /// linear release. A trunk has a front face and switches on the moment
            /// it is passed; a crown is a cloud of needles that thickens, so it
            /// arrives over a metre or so of depth. Without this a bough would
            /// simply have traded a hard vertical edge for a hard horizontal
            /// one.</summary>
            private float FolThrow(float d, Look k)
            {
                if (d <= 0f) return 0f;
                float t = Mathf.Clamp01(d / Mathf.Max(k.FolOnset * EncPerMetre, 1e-6f));
                t = t * t * (3f - 2f * t);
                return t * Mathf.Clamp01((k.FolReach * EncPerMetre - d)
                                         / Mathf.Max(k.FolFall * EncPerMetre, 1e-6f));
            }

            /// <summary>CsFol on the CPU: one BILINEAR fetch of the mass grid — no
            /// tap disc at all, because the low-pass that a tap disc is trying to
            /// approximate has already been done exactly, at bake time, by the box
            /// filter in BuildMass(). Bilinear on a DEPTH is forbidden in the crisp
            /// map (it invents an occluder halfway between a trunk and the sky) and
            /// is right here for the same reason it was wrong there: this map has
            /// no silhouettes in it. Between two crown texels the interpolated
            /// depth is a depth inside the crown, and at the mass's border the
            /// coverage falls to zero on the same slope, so the term dies rather
            /// than drifting.</summary>
            private float FolShadow(Vector2 uv, float z, Look k)
            {
                float fx = uv.x * FolRes - 0.5f, fy = uv.y * FolRes - 0.5f;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                float nr = 0f, fr = 0f, cn = 0f, cf = 0f;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int X = Mathf.Clamp(x0 + dx, 0, FolRes - 1);
                        int Y = Mathf.Clamp(y0 + dy, 0, FolRes - 1);
                        float wgt = (dx == 0 ? 1f - tx : tx) * (dy == 0 ? 1f - ty : ty);
                        int i = Y * FolRes + X;
                        nr += _mN[i] * wgt; fr += _mF[i] * wgt;
                        cn += _mCn[i] * wgt; cf += _mCf[i] * wgt;
                    }
                return Mathf.Max(cn * FolThrow(z - nr, k), cf * FolThrow(z - fr, k));
            }

            /// <summary>CsVisible on the CPU, seven taps and all — INCLUDING the
            /// bite remap and the visibility floor, so what this returns is the
            /// number the shader multiplies into its moon term and not a
            /// geometric proxy for it. That mattered: ModBuild 139 reported "80%
            /// lit" from the raw tap average while the shader, with its strength
            /// applied, was drawing something else, and a report you have to
            /// mentally re-scale is a report that gets misread. It is also what
            /// places the shafts, so the search, the shading and the build log
            /// can never disagree about where the light gets through.
            /// (EnvGround runs six taps rather than seven; the difference is
            /// under a percent and is not worth a second code path here.)</summary>
            public float Visible(Vector3 p, Look k, bool useNear) =>
                Visible(p, k, useNear, out _, out _);

            /// <summary>...and the same with the two occluder classes reported
            /// separately, which is what settles "how much of this beam's
            /// shadowing is the trunk and how much is the crown" in the build log
            /// instead of in an argument.</summary>
            public float Visible(Vector3 p, Look k, bool useNear,
                                 out float trunkTerm, out float folTerm)
            {
                Vector3 c = Plane(p);
                float z = Depth01(c.z) - BiasEnc;
                float fu = k.Penumbra / ExtentU, fv = k.Penumbra / ExtentV;
                float vis = Tap(c.x, c.y, z, useNear, k)
                          + Tap(c.x + 0.866f * fu, c.y + 0.500f * fv, z, useNear, k)
                          + Tap(c.x + 0.000f * fu, c.y + 1.000f * fv, z, useNear, k)
                          + Tap(c.x - 0.866f * fu, c.y + 0.500f * fv, z, useNear, k)
                          + Tap(c.x - 0.866f * fu, c.y - 0.500f * fv, z, useNear, k)
                          + Tap(c.x + 0.000f * fu, c.y - 1.000f * fv, z, useNear, k)
                          + Tap(c.x + 0.866f * fu, c.y - 0.500f * fv, z, useNear, k);
                vis /= 7f;
                float sh = Mathf.Clamp01(((1f - vis) - k.BiteLo)
                                         / Mathf.Max(k.BiteHi - k.BiteLo, 1e-3f));
                float shF = FolShadow(new Vector2(c.x, c.y), z, k);
                float q = Mathf.Max(Mathf.Abs(c.x - 0.5f), Mathf.Abs(c.y - 0.5f));
                float edge = Mathf.Clamp01((0.5f - q) * 40f);
                if (z < 0f || z > 1f) edge = 0f;
                trunkTerm = k.Strength * sh * edge;
                folTerm = k.FolStrength * shF * edge;
                // TWO OCCLUDERS ON ONE RAY MULTIPLY — that is what transmittances
                // do — but the product is floored at the TRUNK's own MinVis so the
                // safety rail stays exactly where it was declared. Without the
                // floor a crown standing over a trunk shadow could compound the
                // beam down past the 14% that guarantees it still arrives in its
                // pool, and the whole point of MinVis is that nothing may.
                return Mathf.Max(1f - k.Strength, (1f - trunkTerm) * (1f - folTerm));
            }

            /// <summary>Encode and write. R:G is the 16-bit linear depth of the
            /// NEAREST occluder (sentinel 65535, i.e. white) and B:A the DEEPEST
            /// (sentinel 0) — see the two-layer note on the buffers. 16 bits per
            /// layer rather than 8 because the depth span is the room's own
            /// extent along an oblique bearing — tens of metres — and 8 bits over
            /// that is a ~18 cm quantum, which forces a bias large enough to
            /// detach every trunk's shadow from its own foot.</summary>
            public void Save(string path, string massPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Color32/SetPixels32, never Color/SetPixels: the bytes ARE the
                // payload here, and a float round trip would put the outcome of
                // "does Unity round or truncate v*255" between the bake and the
                // shader. A one-step error in the HIGH byte is 256 steps of depth.
                var px = new Color32[Res * Res];
                for (int i = 0; i < px.Length; i++)
                {
                    int n = Mathf.Clamp(Mathf.RoundToInt(_zN[i] * 65535f), 0, 65535);
                    int f = Mathf.Clamp(Mathf.RoundToInt(_zF[i] * 65535f), 0, 65535);
                    px[i] = new Color32((byte)(n >> 8), (byte)(n & 255),
                                        (byte)(f >> 8), (byte)(f & 255));
                }
                var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path)
                         ?? throw new Exception("No importer for " + path);
                ti.textureType = TextureImporterType.Default;
                // DATA, not colour: an sRGB curve on the way in would bend the
                // depth and the comparison would be wrong everywhere at once.
                ti.sRGBTexture = false;
                // ALPHA IS THE LOW BYTE OF THE FAR LAYER, not transparency:
                // alphaIsTransparency would let Unity DILATE the colour into
                // texels it thinks are transparent, rewriting depths wholesale.
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                // NO MIPS: a mip is a box filter over depths, which would both
                // invent occluders halfway between a trunk and the sky beside it
                // and leak the border texels inward. POINT + CLAMP for the same
                // reason — the filtering that makes the edge soft is the
                // percentage-closer tap loop in the shaders, which compares
                // first and averages after.
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Point;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 1;
                ti.maxTextureSize = Res;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
                _path = path;

                // ---- and the mass map beside it (SHAFT MASS) ----
                // R = nearest foliage depth, G = deepest, B/A = the coverage of
                // the mass at each. Eight bits per channel is an 18 cm depth
                // quantum, which is a fifth of this map's own 22 cm texel and a
                // seventh of the shortest onset any receiver asks for — the
                // precision argument that forces 16 bits on the trunk layer simply
                // does not arise for a cloud.
                var mpx = new Color32[FolRes * FolRes];
                for (int i = 0; i < mpx.Length; i++)
                    mpx[i] = new Color32((byte)Mathf.RoundToInt(_mN[i] * 255f),
                                         (byte)Mathf.RoundToInt(_mF[i] * 255f),
                                         (byte)Mathf.RoundToInt(_mCn[i] * 255f),
                                         (byte)Mathf.RoundToInt(_mCf[i] * 255f));
                var mtex = new Texture2D(FolRes, FolRes, TextureFormat.RGBA32, false);
                mtex.SetPixels32(mpx);
                mtex.Apply();
                File.WriteAllBytes(massPath, mtex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(mtex);
                AssetDatabase.ImportAsset(massPath);
                var mi = (TextureImporter)AssetImporter.GetAtPath(massPath)
                         ?? throw new Exception("No importer for " + massPath);
                mi.textureType = TextureImporterType.Default;
                mi.sRGBTexture = false;
                // A IS A COVERAGE, not transparency — same trap as the depth map's
                // low byte, and the same answer: no dilation, no premultiply.
                mi.alphaSource = TextureImporterAlphaSource.FromInput;
                mi.alphaIsTransparency = false;
                mi.mipmapEnabled = false;
                // BILINEAR, and this is the one importer setting that differs from
                // the crisp map. There the filter would blend a trunk against the
                // sky beside it and invent an occluder; here there is nothing to
                // invent — every neighbour of a crown texel is either more crown
                // (so the blend is inside the mass) or empty (so the coverage
                // fades out with it). It is also the second half of the low-pass:
                // 22 cm texels reconstructed bilinearly is a ~44 cm kernel, which
                // is the size of a bough and ten times the size of a needle.
                mi.filterMode = FilterMode.Bilinear;
                mi.wrapMode = TextureWrapMode.Clamp;
                mi.anisoLevel = 1;
                mi.maxTextureSize = FolRes;
                mi.textureCompression = TextureImporterCompression.Uncompressed;
                mi.SaveAndReimport();
                _folPath = massPath;
            }

            /// <summary>Hand the basis to a material. Every number here is
            /// DERIVED from the one authored moon bearing and from the box the
            /// constructor measured — there is no second copy of anything.</summary>
            public void Apply(Material m, Look k)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(_path)
                          ?? throw new Exception("Canopy shadow map missing: " + _path);
                m.SetTexture("_CsMap", tex);
                m.SetVector("_CsOrg", new Vector4(Origin.x, Origin.y, Origin.z, Enc / _wSpan));
                m.SetVector("_CsU", new Vector4(AxisU.x, AxisU.y, AxisU.z, 1f / ExtentU));
                m.SetVector("_CsV", new Vector4(AxisV.x, AxisV.y, AxisV.z, 1f / ExtentV));
                m.SetVector("_CsDir", new Vector4(Travel.x, Travel.y, Travel.z, -_wNear));
                // The penumbra is asked for in METRES and converted here, so the
                // two shaders agree on a physical softness rather than on a texel
                // count that would change the day the resolution does.
                m.SetVector("_CsFlt", new Vector4(k.Penumbra / ExtentU, k.Penumbra / ExtentV,
                                                  BiasEnc, k.Strength));
                // x = how far down-light an occluder still casts, y = 1/release,
                // both in the map's own encoded depth units
                float throwEnc = k.MaxThrow * EncPerMetre;
                float fallEnc = Mathf.Max(k.Fall, 1e-3f) * EncPerMetre;
                // z/w are the BITE ramp: coverage below z is speckle and casts
                // nothing, coverage above z + 1/w is a solid occluder and casts
                // everything. See Look.BiteLo.
                m.SetVector("_CsThrow", new Vector4(throwEnc, 1f / fallEnc,
                                                    k.BiteLo, 1f / Mathf.Max(k.BiteHi - k.BiteLo, 1e-3f)));
                // ---- the mass layer (SHAFT MASS) ----
                var mass = AssetDatabase.LoadAssetAtPath<Texture2D>(_folPath)
                           ?? throw new Exception("Canopy mass map missing: " + _folPath);
                m.SetTexture("_CsFol", mass);
                m.SetVector("_CsFolP", new Vector4(
                    k.FolReach * EncPerMetre,
                    1f / Mathf.Max(k.FolFall * EncPerMetre, 1e-6f),
                    1f / Mathf.Max(k.FolOnset * EncPerMetre, 1e-6f),
                    k.FolStrength));
                Debug.Log($"[GloomhavenVR][Env] Canopy shadow -> {m.name}: TRUNKS — a fully occluded "
                          + $"fragment keeps {k.MinVis * 100f:F0}% of its moon (strength {k.Strength:F2}), "
                          + $"penumbra {k.Penumbra * 100f:F0} cm ({k.Penumbra / ExtentU * Res:F1} x "
                          + $"{k.Penumbra / ExtentV * Res:F1} texels), throw {k.MaxThrow:F1} m "
                          + $"releasing over {k.Fall:F1} m, bite ramp {k.BiteLo * 100f:F0}%..{k.BiteHi * 100f:F0}% "
                          + $"coverage; CROWNS — a texel of full needle mass takes {k.FolStrength * 100f:F0}% "
                          + $"off and no more (no bite ramp, LINEAR in coverage), reach {k.FolReach:F1} m "
                          + $"releasing over {k.FolFall:F1} m after a {k.FolOnset:F1} m onset, read "
                          + $"bilinearly off the {FolRes}x{FolRes} mass grid.");
            }

            /// <summary><paramref name="shade"/> is EnvGround's own arithmetic for
            /// a flat, up-facing patch of clearing floor: (x, z, moon visibility)
            /// -> luminance per unit albedo. It is what turns a shadow PERCENTAGE
            /// into the only number that decides whether the user can see
            /// anything — the RATIO between a lit and a shadowed patch in final
            /// shaded brightness. A floor whose moon is a tenth of its ambient
            /// plus point lights can be 100% shadowed and look identical.</summary>
            public void Report(Func<float, float, float> groundY, float clearR, Look floor,
                Func<float, float, float, float> shade)
            {
                // THE NUMBERS THAT PREDICT WHAT HE SEES STANDING AT THE BOARD:
                // how much of the clearing floor the wood now takes the moon off.
                // Sampled with the ground shader's own reading of the map, so
                // this is the shader's answer and not an estimate of it.
                //
                // TWO numbers, because the mean alone cannot tell a faint wash
                // over the whole clearing (which would read as "the floor got
                // darker" — a regression on a room tuned by hand) from a handful
                // of hard trunk shadows raking across it, which is the effect
                // that was asked for. The second is the fraction of the floor
                // that is at least HALF shadowed: how much of it is inside a
                // shadow you can point at.
                float sum = 0f; int n = 0, deep = 0;
                float litSum = 0f, shdSum = 0f;          // final brightness, lit vs as-shaded
                float moonSum = 0f;                      // ...and how much of it is MOON
                float worstRatio = 1f; float worstAt = 0f, worstAtZ = 0f;
                float deepLit = 0f, deepShd = 0f; int deepN = 0;
                float trunkShare = 0f, folShare = 0f;   // who is doing the shading
                for (int j = 0; j <= 48; j++)
                    for (int k = 0; k <= 48; k++)
                    {
                        float x = Mathf.Lerp(-clearR, clearR, j / 48f);
                        float z = Mathf.Lerp(-clearR, clearR, k / 48f);
                        if (x * x + z * z > clearR * clearR) continue;
                        float vis = Visible(new Vector3(x, groundY(x, z), z), floor,
                                            useNear: false, out float tT, out float fT);
                        trunkShare += tT; folShare += fT;
                        float sh = 1f - vis;
                        sum += sh; n++;
                        float lit = shade(x, z, 1f), got = shade(x, z, vis);
                        litSum += lit; shdSum += got; moonSum += lit - shade(x, z, 0f);
                        if (sh >= 0.5f)
                        {
                            deep++; deepLit += lit; deepShd += got;
                            deepN++;
                        }
                        float ratio = got > 1e-6f ? lit / got : 1f;
                        if (ratio > worstRatio) { worstRatio = ratio; worstAt = x; worstAtZ = z; }
                    }
                _clearShadow = n > 0 ? sum / n : 0f;
                _clearDeep = n > 0 ? deep / (float)n : 0f;
                Debug.Log("[GloomhavenVR][Env] Clearing floor CONTRAST (final shaded luminance per unit "
                          + "albedo, the ambient + three point lights + landing pool all included as the "
                          + "separate addends they are): THE MOON IS "
                          + $"{moonSum / Mathf.Max(litSum, 1e-6f) * 100f:F0}% of the lit floor's brightness "
                          + "— that is the ceiling on everything a shadow term can do here; "
                          + $"unshadowed mean {litSum / Mathf.Max(n, 1):F4}, "
                          + $"as shaded {shdSum / Mathf.Max(n, 1):F4} ({(1f - shdSum / Mathf.Max(litSum, 1e-6f)) * 100f:F1}% off "
                          + "the clearing's average brightness); over the "
                          + $"{_clearDeep * 100f:F1}% of it that is at least half shadowed a LIT patch reads "
                          + $"{(deepN > 0 ? deepLit / Mathf.Max(deepShd, 1e-6f) : 1f):F2}x a SHADOWED one, "
                          + $"and the deepest shadow in the clearing is {worstRatio:F2}x down at "
                          + $"({worstAt:F1},{worstAtZ:F1}). THAT ratio, not the shadow percentage, is what "
                          + "he can or cannot see. Of everything the floor loses, "
                          + $"{trunkShare * 100f / Mathf.Max(trunkShare + folShare, 1e-6f):F0}% comes from "
                          + "TRUNKS and the rest from the crown mass — the split matters because only the "
                          + "first is meant to be a shadow you can point at.");

                long bytes = new FileInfo(_path).Length;
                long mbytes = new FileInfo(_folPath).Length;
                // depths reported the way the shader sees them: metres down-light
                // of the near plane, which is where 0 sits
                float near = _hitNear == float.MaxValue ? 0f : _hitNear - _wNear;
                float far = _hitFar == float.MinValue ? 0f : _hitFar - _wNear;
                Debug.Log($"[GloomhavenVR][Env] Canopy shadow map: {Res}x{Res}, box "
                          + $"{ExtentU:F1} x {ExtentV:F1} m ({ExtentU / Res * 100f:F1} x "
                          + $"{ExtentV / Res * 100f:F1} cm per texel), {_tris} triangles rasterised "
                          + $"({_outside} projected clear of the map, {_cut} texel writes dropped by "
                          + $"the alpha test), occluder depth {near:F1}..{far:F1} m of a "
                          + $"{_wSpan:F1} m encoded range ({_wSpan / 65535f * 1000f:F2} mm per 16-bit "
                          + $"step, bias {Bias * 100f:F1} cm), {_filled * 100f / (Res * Res):F1}% of "
                          + "texels hold a TRUNK, "
                          + $"CLEARING FLOOR (r<{ClearR:F1} m) loses {_clearShadow * 100f:F1}% of its moon on average and {_clearDeep * 100f:F1}% of it is at least half shadowed, "
                          + $"{bytes / 1024} KiB on disk / {Res * Res * 4 / 1024} KiB as RGBA32 "
                          + $"in the bundle -> {_path}");
                Debug.Log($"[GloomhavenVR][Env] Canopy MASS map (SHAFT MASS): {FolRes}x{FolRes} over the "
                          + $"same box ({ExtentU / FolRes * 100f:F1} x {ExtentV / FolRes * 100f:F1} cm per "
                          + $"texel, each one the area average of {FolDown}x{FolDown} raster texels, so "
                          + $"coverage lands in {FolDown * FolDown}ths and not as a hit or a miss), "
                          + $"{_folFilled * 100f / (FolRes * FolRes):F1}% of texels hold needle mass and "
                          + $"where they do the mean coverage is {_folCovMean * 100f:F0}% — that is the "
                          + "number the crowns now dim WITH, linearly, instead of quantising through a "
                          + $"bite ramp. Depth 8-bit ({_wSpan / 255f * 100f:F1} cm per step, against a "
                          + $"{FolWindow:F1} m slab window), bilinear + clamp, {mbytes / 1024} KiB on disk / "
                          + $"{FolRes * FolRes * 4 / 1024} KiB as RGBA32 in the bundle "
                          + $"({FolRes * FolRes * 100f / (Res * Res):F0}% of the trunk map's) -> {_folPath}");
            }
        }

        // =========================================================== build forest
        public static void BuildForestRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);
            _groundY = ForestY;
            // nothing in a wood stands on a cellar bookshelf
            Tip = null; TipUse.Clear();

            float moonAz = MoonAzimuth();
            var moonHoriz = new Vector3(MoonDir.x, 0f, MoonDir.z).normalized;

            var rig = new LightRig
            {
                // User finding, ModBuild 133 (tested ON HARDWARE, so it outranks
                // the previews, which render brighter than the headset): "Hinter
                // den Bäumen außerhalb der Lichtung soll es so dunkel sein das man
                // sich nicht traut dahinter hinweg zu gehen."
                //
                // THE RECIPE, and what each number does:
                //  * ambUp 0.072 -> 0.024. This is THE number. Ambient is the only
                //    term that reaches surfaces the moon cannot, so it is exactly
                //    the "blue-grey haze floor" that stopped the wood going black.
                //    Everything not moonlit now sits at a third of what it was.
                //  * ambDown 0.017 -> 0.005: downward-facing surfaces (the
                //    undersides of the deadfall, the far ground) go to nothing.
                //  * dirCol 0.62 -> 0.70: the moon gets STRONGER while everything
                //    else falls away. Contrast is the tool, not brightness — the
                //    clearing must stay readable and the shafts must still land.
                //  * the far lantern's range 10 -> 6.5 and its colour halved: it
                //    was lighting a whole quadrant of the wood. It should be a
                //    point you notice, not a light source.
                // Beyond these, the wood's darkness is carried by the per-vertex
                // Depth() fade and GroundColor() below.
                ambUp = new Color(0.024f, 0.029f, 0.040f),
                ambDown = new Color(0.005f, 0.006f, 0.005f),
                dirWorld = MoonDir,
                // ELEMENT ART: the periphery is the TREE LINE, not the far edge
                // of the 30 m ground disc — out there nothing is lit enough for
                // an element to be seen doing anything. 12 m puts the first two
                // trunk bands (6.2-15 m) on the ramp and the clearing floor near
                // zero, which is the design's "effects live in the periphery so
                // the board stays readable", expressed as one number.
                elemRad = 12f,
                // 1.8, against the cellar's 0.70. The wood answers the moon with
                // dirCol 0.70 and its trunks stand 6-10 m out with a per-vertex
                // depth fade on top; at 1.0 Fire was a rumour on the near trunks
                // and the only thing that actually changed colour was the
                // fireflies. This puts a real ember rim on the faces that look
                // into the clearing and still leaves the far bands black.
                elemWarm = 1.8f,
                // The moon does most of the work: a flat ambient made every trunk
                // the same shade of blue-grey, which is exactly the "assembled
                // assets" look. High key on the moonlit side, near black behind.
                dirCol = new Color(0.70f, 0.79f, 0.94f),
                points = new[]
                {
                    // will-o'-the-wisp over the hollow by the path — cold green
                    new PLight(new Vector3(-4.6f, 0.85f, -6.2f), 5.5f, new Color(0.11f, 0.26f, 0.16f), 0.22f),
                    // a far lantern burning somewhere off among the trunks — the
                    // only warm light in the wood, and the reason to look that way
                    new PLight(new Vector3(9.2f, 1.35f, -7.4f), 6.5f, new Color(0.26f, 0.145f, 0.055f), 0.30f),
                    // the pool where the moon shafts land on the clearing floor —
                    // KEPT at full strength: this is the light the players read the
                    // board by, and it is the last thing that may be taken away.
                    // ModBuild 136 raised it ~13% as the counterweight to the
                    // ground's new _DirScale (see S_Ground below): the FLOOR gets
                    // darker, the place the shafts LAND does not.
                    new PLight(new Vector3(moonHoriz.x * 2.3f, 0.55f, moonHoriz.z * 2.3f), 6.0f,
                               new Color(0.17f, 0.20f, 0.29f), 0.0f),
                },
            };

            // ---------------------------------------------------- forest floor
            Color GroundColor(float x, float z)
            {
                float r = Mathf.Sqrt(x * x + z * z);
                // litter (leaf) vs bare needle/dirt floor
                float litter = Mathf.Clamp01(0.52f + 0.85f * (Fbm2(x * 0.19f + 41f, z * 0.19f, 3, 984) - 0.5f)
                                             + 0.22f * Mathf.InverseLerp(5f, 16f, r));
                // the trodden path scrubs the litter away
                float onPath = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.45f, 1.35f, PathDist(x, z)))
                               * PathFade(x, z);
                litter *= 1f - 0.92f * onPath;
                // Darkness: the clearing floor is the brightest thing down here,
                // everything under the canopy falls away into black.
                // ModBuild 134: the fall-off starts inside the tree ring (6.5 m,
                // was 8) and bottoms out at 2% (was 9%) by 16 m (was 22) — walk
                // past the first trunks and there is no ground left to see.
                float fade = Mathf.SmoothStep(1f, 0.015f, Mathf.InverseLerp(5.2f, 11.5f, r));
                float open = Mathf.Lerp(0.20f, 1f, Mathf.SmoothStep(1f, 0f,
                                        Mathf.InverseLerp(ClearR - 2.0f, ClearR + 2.5f, r)));
                // a slightly brighter pool where the shafts strike (0.55 -> 0.70
                // in ModBuild 136: the ground's overall moon response came down
                // by nearly a third, and this is the term that keeps the landing
                // zone — and with it the board's own surroundings — readable)
                var pl = new Vector2(moonHoriz.x * 2.3f, moonHoriz.z * 2.3f);
                float pool = 0.70f * Mathf.Exp(-(new Vector2(x, z) - pl).sqrMagnitude / 5.5f);
                float g = fade * open * (1f + pool);
                // trodden earth is a touch darker and greyer than the litter
                g *= 1f - 0.18f * onPath;
                return new Color(g, g, g * 0.98f, litter);
            }

            var groundMesh = SaveMesh("Env_S_Ground.asset",
                PolarGround(FR, 46, 100, ForestY, GroundColor, 3.2f));
            var g = Place(root, "Ground", groundMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            var gm = NewRoomMat("S_Ground.mat", "GloomhavenVR/EnvGround");
            gm.SetTexture("_MainTex", Imp("forest_ground_04_alb"));
            gm.SetTexture("_BumpMap", Imp("forest_ground_04_nrm"));
            gm.SetTexture("_MainTex2", Imp("forest_leaves_04_alb"));
            gm.SetTexture("_BumpMap2", Imp("forest_leaves_04_nrm"));
            gm.SetFloat("_BumpScale", 1.15f);
            // USER FINDING, ModBuild 135 (hardware): "Pass nochmal die
            // Lichtverhältnisse im Wald auf dem Boden an - der erscheint viel zu
            // hell bei den Lichtverältnissen. Er soll eher leicht angestrahlt
            // werden von Mond."
            //
            // WHY THE FLOOR, AND ONLY THE FLOOR. Every other surface in the wood
            // is a trunk, a bough or a prop: vertical or tilted, so the moon
            // rakes it and half of it stays dark. The ground is flat. Its normal
            // is up EVERYWHERE, so N.L against a 40 deg moon is 0.64 over the
            // whole disc — the one uniformly, fully lit surface in a room whose
            // entire recipe is contrast. That is what "viel zu hell" was.
            //
            // 0.38, i.e. the ground answers the moon with a bit over a third of
            // what everything else does. Turning the MOON down instead would
            // have cost the trunk rim, which is the one thing separating a trunk
            // from the black behind it (ModBuild 134's whole round). Numbers at
            // the clearing centre, per unit albedo: ambient 0.024 + moon 0.450 ->
            // ambient 0.024 + moon 0.171; times the vertex fade, the floor there
            // goes 0.61 -> 0.29, and out under the first trunks it goes to a
            // third of what it was. The LANDING POOL is held up separately (see
            // GroundColor's `pool` term and the third PLight): the board must
            // stay readable, and it now sits on the only lit patch of ground.
            gm.SetFloat("_DirScale", 0.38f);
            // ...and the other half of "viel zu hell": the floor was still
            // wearing its DAYLIGHT COLOUR. forest_ground_04 is a warm brown
            // photoscan, and a warm brown floor under a cold moon does not read
            // as dim, it reads as lit — by something else. The trunks were given
            // exactly this treatment in ModBuild 133 ("night bark is desaturated
            // and cold, not the warm pink of the daylight photoscan"); the
            // ground was simply forgotten. 0.72/0.74/0.82 takes another 25% off
            // and tilts what is left toward the moon's own colour.
            gm.SetColor("_Tint", new Color(0.72f, 0.74f, 0.82f));
            Defer(gm, g.transform, 1f);
            g.GetComponent<MeshRenderer>().sharedMaterial = gm;

            // ------------------------------------------------ trunks + crowns
            var trees = ForestTrees();
            var trunkA = new Acc();   // near bands: pine bark, full detail
            var trunkB = new Acc();   // far bands: a second species, coarser
            var canopy = new Acc();   // every crown + the canopy shell, one mesh
            // Depth fade baked per vertex: the wood must dissolve, never end.
            // ModBuild 134: 6.5..22 m -> 0.055 became 5.5..15 m -> 0.012. The
            // FIRST ring of trunks (6.2-10 m) is what the player sees against the
            // sky, and it now reads as a silhouette with a moon rim, not as a
            // described object; behind it there is effectively nothing left. This
            // one curve does more for "I would not walk back there" than the
            // ambient does, because it also darkens the moonlit side.
            //
            // ModBuild 136: `floorAt` is new, and it exists because THE CANOPY
            // ONLY STARTED OBEYING THIS CURVE THIS ROUND. EnvRoomCutout declared
            // _VCol and never applied it (ModBuild 135's own KNOWN DEBT), so
            // every foliage tint below was written and thrown away; only the
            // trunks (EnvRoom) were ever faded. With the shader fixed, the raw
            // curve would take the far canopy to 1% and the wood would lose its
            // roof in one build — a change the user never asked for on a room he
            // has approved. The TRUNKS therefore keep the tuned 0.010 floor
            // exactly as ModBuild 134 left it, and the FOLIAGE gets a floor of
            // 0.34: the crowns still recede, but they recede to a dark canopy
            // instead of to nothing.
            Color Depth(float r, float mul = 1f, float floorAt = 0.010f)
            {
                float f = Mathf.SmoothStep(1f, floorAt, Mathf.InverseLerp(3.0f, 10.5f, r)) * mul;
                return new Color(f, f, f, 1f);
            }

            foreach (var tt in trees)
            {
                var t = tt;
                if (t.dead) t.h *= 0.55f + 0.2f * Hash3((int)t.sd, 9, 0, 5001);  // snapped top
                float r = t.p.magnitude;
                var tint = Depth(r);
                if (r < 16f)
                {
                    float br = t.rb * 2.6f;
                    Contacts.Add((new Foot
                    {
                        x0 = t.p.x - br, x1 = t.p.x + br,
                        z0 = t.p.y - br, z1 = t.p.y + br,
                    }, 0.9f));
                }
                bool near = t.band <= 1;
                var acc = (t.band % 2 == 0) ? trunkA : trunkB;
                // The far bands (15.5-28.5 m) went from 8x6 to 7x4 rings in
                // ModBuild 134: the extra 6.6k triangles the fainter star cut
                // costs had to come from somewhere, and after this round's
                // darkening those trunks sit at 1-5% brightness — a 7-sided
                // silhouette out there is not resolvable at any distance.
                AddTrunk(acc, t, near ? 12 : 7, near ? 9 : 4, 1.6f, tint);
                if (t.dead)
                {
                    // a snag keeps a few bare branches and nothing else
                    AddCrown(canopy, t, whorls: 2, perWhorl: 3, crownFrac: 0.62f,
                             radScale: 0.5f, tint: Depth(r, 0.55f, 0.34f), crossed: false, dead: true);
                    continue;
                }
                AddCrown(canopy, t,
                    whorls: near ? 6 : 4,
                    perWhorl: near ? 7 : 5,
                    crownFrac: near ? 0.34f : 0.28f,      // bare trunk under the crown
                    radScale: near ? 1.0f : 0.85f,
                    tint: Depth(r, 0.92f, 0.34f),
                    crossed: near);
            }

            // canopy shell: fills the sky between and behind the crowns
            {
                int placed = 0;
                for (int i = 0; i < 4400; i++)
                {
                    float u = Hash3(i, 1, 0, 5209), w = Hash3(i, 2, 0, 5209);
                    float r = Mathf.Lerp(ClearR, 27f, Mathf.Sqrt(u));       // area-uniform
                    float ang = w * Mathf.PI * 2f;
                    float x = Mathf.Sin(ang) * r, z = Mathf.Cos(ang) * r;
                    if (Hash3(i, 3, 0, 5209) > CanopyMask(x, z)) continue;
                    float y = CanopyY(r) + 2.4f * (Hash3(i, 4, 0, 5209) - 0.5f)
                              + 1.8f * Fbm2(x * 0.25f, z * 0.25f, 2, 995);
                    var c = new Vector3(x, y, z);
                    var rect = Sprigs[(int)(Hash3(i, 5, 0, 5209) * Sprigs.Length) % Sprigs.Length];
                    float len = 0.7f + 1.1f * Hash3(i, 6, 0, 5209);
                    // boughs hang: mostly horizontal, tipped down and away
                    float ta = Hash3(i, 7, 0, 5209) * Mathf.PI * 2f;
                    Vector3 up = new Vector3(Mathf.Sin(ta), -0.35f - 0.4f * Hash3(i, 8, 0, 5209),
                                             Mathf.Cos(ta)).normalized;
                    Vector3 right = Vector3.Cross(up, Vector3.up).normalized;
                    float halfW = len * 0.6f * (rect.width / Mathf.Max(rect.height, 1e-3f));
                    // brighter near the tear (moonlit rim), dark deep in the mass.
                    // ModBuild 136: 1.15 -> 0.020 became 1.05 -> 0.30, for the
                    // same reason Depth() grew a floor — this tint had no effect
                    // at all until EnvRoomCutout was fixed this round, and the
                    // authored value would have blacked the roof out in one step.
                    float lit = Mathf.Lerp(1.05f, 0.30f, Mathf.InverseLerp(5.5f, 13f, r));
                    AddCard(canopy, c, right * halfW, up * (len * 0.55f),
                            (Vector3.down * 0.7f + up * 0.3f).normalized, rect,
                            new Color(lit, lit, lit, 1f));
                    placed++;
                }
                Debug.Log($"[GloomhavenVR][Env] Canopy shell: {placed} boughs.");
            }

            var barkA = NewRoomMat("S_TrunkA.mat", "GloomhavenVR/EnvRoom");
            barkA.SetTexture("_MainTex", Imp("pine_bark_alb"));
            barkA.SetTexture("_BumpMap", Imp("pine_bark_nrm"));
            barkA.SetFloat("_BumpScale", 1.35f);
            barkA.SetFloat("_VCol", 1f);
            var barkB = NewRoomMat("S_TrunkB.mat", "GloomhavenVR/EnvRoom");
            barkB.SetTexture("_MainTex", Imp("bark_brown_02_alb"));
            barkB.SetTexture("_BumpMap", Imp("bark_brown_02_nrm"));
            barkB.SetFloat("_BumpScale", 1.25f);
            barkB.SetFloat("_VCol", 1f);
            foreach (var m in new[] { barkA, barkB })
            {
                // the cold moon rim — the single most important lighting cue in
                // the whole room: it gives the trunks volume and separates them
                // from the black behind them.
                // ModBuild 134: the rim is the ONLY thing raised in this round —
                // with the ambient gone it is all that separates a trunk from the
                // black behind it, and a silhouette with a cold edge is much more
                // frightening than a described trunk.
                m.SetColor("_RimCol", new Color(0.21f, 0.27f, 0.40f));
                m.SetFloat("_RimPow", 4.2f);
                // night bark is desaturated and cold, not the warm pink of the
                // daylight photoscan
                m.SetColor("_Tint", new Color(0.52f, 0.53f, 0.58f));
            }
            var foliage = NewRoomMat("S_Foliage.mat", "GloomhavenVR/EnvRoomCutout");
            foliage.SetTexture("_MainTex", Imp("fir_twig_alb"));
            foliage.SetFloat("_BumpScale", 0f);
            foliage.SetFloat("_Cutoff", 0.42f);
            foliage.SetFloat("_VCol", 1f);
            // Needles at night are almost black. The bright fir green of the raw
            // photoscan under a lit ambient was the single most cartoon-looking
            // thing in the first pass. (ModBuild 134: darker again.)
            foliage.SetColor("_Tint", new Color(0.21f, 0.25f, 0.20f));

            void Weld(Acc acc, string asset, string node, Material mat)
            {
                var mesh = SaveMesh(asset, acc.Build(Path.GetFileNameWithoutExtension(asset)));
                var go = Place(root, node, mesh, Vector3.zero, Vector3.zero, Vector3.one, mat);
                Defer(mat, go.transform, 1f);
            }
            Weld(trunkA, "Env_S_TrunkA.asset", "TrunksNear", barkA);
            Weld(trunkB, "Env_S_TrunkB.asset", "TrunksFar", barkB);
            Weld(canopy, "Env_S_Canopy.asset", "Canopy", foliage);

            // ------------------------------------------------- CANOPY SHADOW
            // The wood is finished, so the thing that blocks the moon is finished
            // too — bake it now, before the shafts, because the shafts are placed
            // WITH it (see below). Acc keeps its vertex and index lists after
            // Build(), so this reads the very geometry that was just welded.
            //
            // THE BOX. Only the RECEIVERS set the size, and the receivers are the
            // floor and the blades. An occluder never needs to be inside the box
            // in world terms: it shadows a receiver only when it lands in the
            // receiver's OWN TEXEL, so a crown 28 m out that stands between the
            // moon and a beam is captured automatically — it shares that beam's
            // (u,v) by definition of being on its ray.
            //
            // Getting this wrong the first time cost a factor of four in texel
            // area. A SHAFT RUNS ALONG THE LIGHT, so in light space a whole 18 m
            // beam collapses to a PATCH, not to a 20 m radius: v is constant down
            // a beam (it moves 0.766 x -0.643 + -0.643 x -0.766 = 0 per metre),
            // and the three of them together only reach u +-5.6 m (side +-3.5 m
            // plus the widest blade half-width, 2.02 m) and v -6.9..-0.7 m. All
            // of that sits comfortably inside the box the FLOOR alone demands.
            //
            // So the box is the floor's: a disc of 14 m — past the 11.5 m where
            // GroundColor's fade bottoms out at 1.5% and past the 12 m where the
            // props are gone — with 3 m of height either side of it, which is far
            // more than ForestY moves inside that radius. 28.0 x 22.6 m, i.e.
            // ~5.5 x 4.4 cm per texel, so a near trunk is 9-15 texels across and
            // a shadow of it survives a filter wide enough to hide the grid.
            // Inside() below checks the shafts really do land in it rather than
            // trusting the arithmetic above.
            const float shadowRoi = 14f;
            var canopyShadow = new CanopyShadowBake(MoonDir, shadowRoi, yLo: -3f, yHi: 3f);
            canopyShadow.AddSolid(trunkA, "TrunksNear");
            canopyShadow.AddSolid(trunkB, "TrunksFar");
            // The crowns AND the canopy shell are one accumulator of fir_twig
            // sprite cards, so they cast through their own alpha. 0.50 rather
            // than the material's own _Cutoff of 0.42: a shadow drawn from the
            // sprig's SOLID CORE is slightly thinner than the sprig you can see,
            // which is the right way round — a needle mass that shadows more
            // than it covers is what turned the wood into a lid the first time.
            canopyShadow.AddCutout(canopy, "Canopy", ImpTex + "/fir_twig_alb.png", 0.50f);
            canopyShadow.Bake(ForestY, shadowRoi);
            // THE TWO READINGS OF THE ONE MAP. Penumbra is taste; everything
            // else is per receiver, because the floor and the open air are not
            // the same problem. See Look.
            //
            // SHAFT BITE, USER FINDING ModBuild 139 (hardware): "Die Mondstrahlen
            // in der Wald-Umgebung gehen immer noch durch die Bäume durch und
            // werfen auch keinen Schatten." The 139 numbers had already predicted
            // it and were read the wrong way round — a beam scored 98% clear over
            // its upper run and 42% occluded over its lower one produced a
            // perfectly smooth shaft, because 42% spread evenly over nine metres
            // of alpha-tested crown speckle is a DIMMING and not a shadow. Three
            // things are changed together, and none of them works alone:
            //   1. the candidate search wants a beam that is crossed, not a beam
            //      that is clear (see the block at the shaft loop),
            //   2. the BITE RAMP throws away the speckle and takes real crossings
            //      to full (Look.BiteLo),
            //   3. and only then can the throw go up, because the ramp is what
            //      stops a longer throw simply greying the whole beam out.
            //
            // FLOOR 9.0 m / 3.5 m, unchanged: full shadow for the first 5.5 m
            // along the beam (4.2 m of floor), gone by 9.0 (6.9 m of floor). A
            // trunk at the clearing edge therefore lays a shadow that reaches the
            // middle of the clearing and dies just past it, and nothing beyond
            // the second rank of trees can touch the floor at all. Its bite ramp
            // is 0.26..0.66: the crowns 20-30 m up-light dust the whole clearing
            // with 0.1-0.25 coverage and that is exactly the faint uniform wash
            // that must NOT be allowed to darken a hand-tuned floor, while a
            // trunk is a solid occluder and sails past 0.66.
            //
            // BLADES 5.0 m / 2.5 m — up from 4.0/2.0, and MEASURED down from 7.0.
            // The 4 m throw was chosen when a longer one was measured to take the
            // lower runs to 20-48% lit; that measurement was made WITHOUT the
            // bite ramp, and with the ramp the trade reverses, because a longer
            // throw now buys BARS instead of grey. A beam five metres up has its
            // crossing boughs 3-8 m up-light of it (its ray to the moon climbs
            // 0.84 m per metre travelled while the canopy climbs only 0.30, so
            // the two converge), and a 4 m throw finds almost none of them.
            //
            // 7.0 m was tried first and overshot in a way the profile in the log
            // makes unmistakable: two of the three beams came out
            // |9999...944411111111111111111136899|, i.e. their whole LOWER HALF
            // at the floor in one 5-8 m block. That is not a bough crossing a
            // beam, it is a beam that stops halfway down and reappears over its
            // pool — with a 7 m reach, everything up-light of the beam below the
            // canopy line is inside the throw at once and the crossings merge.
            // 5.0 m is the reach at which they separate again.
            //
            // MinVis 0.14 on the blades: a fully crossed stretch keeps a seventh
            // of its brightness. That is a hard dark bar and still not a hole —
            // what is left is the light the mist scatters sideways into the
            // shadowed stretch, which is real. On the FLOOR 0.25: a shadow in a
            // night wood is not a hole either, and under the trees the moon goes
            // 0.171 -> 0.043 per unit albedo against an ambient of 0.024.
            //
            // SHAFT MASS, USER FINDING ModBuild 141 (hardware): "da auch das
            // Gestrüpp an den Bäumen Schatten wirft sieht es etwas merkwürdig aus".
            // Everything above is now the TRUNK half of each Look and is unchanged
            // to the number, because the trunk half is the half he likes. The four
            // Fol* numbers are the crown half, and they are chosen to be the
            // opposite kind of thing in every respect that matters:
            //
            //   BLADES, FolVis 0.62 — a texel of solid needle mass takes 38% off
            //   and no more, against the trunk's 86%. That ratio IS the brief: a
            //   crisp dark bar where a trunk stands, a broad soft dapple where a
            //   crown does, and the two never confusable. Reach 7.0 m against the
            //   trunk's 5.0 and a 4.0 m release against 2.5: a diffuse mass may
            //   reach further precisely because it cannot draw an edge, and the
            //   long release is what makes the dapple BROAD instead of banded.
            //   Onset 1.2 m so a bough arrives over a metre of depth rather than
            //   switching on at a plane — the one thing that could have traded the
            //   comb's vertical teeth for horizontal ones.
            //
            //   FLOOR, FolVis 0.86 — a seventh of the blades' response. The floor
            //   was already refusing the crown wash on purpose (the bite ramp's
            //   whole reason for existing at 0.26 was to throw away the 0.1-0.25
            //   dusting from the roof 20-30 m up-light), and a hand-tuned floor may
            //   not be darkened by a round about beams. What it gains is the part
            //   that is worth having: a slow, half-metre-scale unevenness under the
            //   crowns instead of a mathematically flat moon. Reach 12 m with a
            //   5 m release, i.e. deliberately longer and softer than the trunks'
            //   9 m, because a crown's shadow on the ground has no edge to lose.
            //   Report() prints what it costs in the only currency that counts:
            //   the lit-to-shadowed brightness ratio over the clearing.
            var floorLook = new CanopyShadowBake.Look(
                minVis: 0.25f, penumbra: 0.18f, maxThrow: 9.0f, fall: 3.5f,
                biteLo: 0.26f, biteHi: 0.66f,
                folVis: 0.78f, folReach: 12.0f, folFall: 5.0f, folOnset: 2.0f);
            var beamLook = new CanopyShadowBake.Look(
                minVis: 0.14f, penumbra: 0.22f, maxThrow: 5.0f, fall: 2.5f,
                biteLo: 0.34f, biteHi: 0.72f,
                folVis: 0.62f, folReach: 7.0f, folFall: 4.0f, folOnset: 1.2f);
            canopyShadow.Save(Root + "/Textures/Env_S_CanopyShadow.png",
                              Root + "/Textures/Env_S_CanopyMass.png");
            // EnvGround's own arithmetic for a flat, up-facing patch of clearing
            // floor, per unit albedo — the ONLY way to answer "can he see the
            // shadow at all". The moon is one addend among four here: the
            // hemisphere ambient, the three point lights and the vertex tint are
            // all beside it, and the shadow reaches none of them. If the moon
            // were a small share of the total then removing three quarters of it
            // would be invisible BY CONSTRUCTION and no strength would help.
            // (The vertex tint and the albedo are common factors and cancel out
            // of the ratio, but they are carried anyway so the absolute numbers
            // in the log are the numbers the floor actually shows.)
            float FloorLum(float x, float z, float vis)
            {
                var N = Vector3.up;
                var p = new Vector3(x, ForestY(x, z), z);
                Color l = rig.ambUp;                                  // nw.y = 1
                float ndl = Mathf.Max(0f, Vector3.Dot(N, MoonDir.normalized));
                float dirScale = gm.GetFloat("_DirScale");
                l += rig.dirCol * (dirScale * ndl * vis);
                foreach (var pt in rig.points)                        // separate addends
                {
                    Vector3 lv = pt.pos - p;
                    float d2 = Mathf.Max(lv.sqrMagnitude, 1e-8f), d = Mathf.Sqrt(d2);
                    float q = d2 / (pt.range * pt.range);
                    float att = Mathf.Clamp01(1f - q); att *= att;    // _PtHard is 0 in the forest
                    l += pt.col * (att * Mathf.Max(0f, Vector3.Dot(N, lv / d)));
                }
                var alb = gm.GetColor("_Tint");
                var g = GroundColor(x, z);                            // the vertex fade and the pool
                return (l.r * alb.r * g.r + l.g * alb.g * g.g + l.b * alb.b * g.b) / 3f;
            }
            canopyShadow.Report(ForestY, ClearR, floorLook, FloorLum);
            // THE FLOOR. A pure multiply on the ground's DIRECTIONAL term only —
            // it can subtract moonlight under a tree and it can do nothing else.
            // The hand-tuned levels from ModBuild 135/136 survive untouched: the
            // hemisphere ambient is a separate addend, the three point lights are
            // separate addends, and the landing pool (both the `pool` term in
            // GroundColor and the third PLight) is a POINT light — so the patch
            // the board stands on cannot be darkened by this at all. Nothing in
            // the room gets brighter; the open floor is bit-for-bit what it was.
            //
            // MinVis 0.25, not 0: a shadow in a night wood is not a hole. The
            // moon is a 0.5 deg disc and the air between the crowns is full of
            // the mist the shafts are made of, so a trunk's shadow keeps a
            // quarter of its moonlight. Under the trees that is moon 0.171 ->
            // 0.043 per unit albedo against an ambient of 0.024, so a shadow
            // reads as a real drop without taking the floor to the flat black
            // the vertex fade already owns further out. What that comes to in
            // FINAL SHADED BRIGHTNESS — the only number that decides whether he
            // can see it — is measured and printed by Report() above, because
            // the moon is one of four addends on this floor and a percentage of
            // one addend says nothing on its own. First knob to turn on hardware.
            //
            // 0.18 m of penumbra. The PHYSICAL half-shadow of a trunk 10 m away
            // under a 0.5 deg moon is about 9 cm, and a filter that small would
            // draw the map's own grid on the floor as a staircase. 0.18 m is
            // three texels: the smallest disc that hides the quantisation while
            // staying well inside the 0.5-0.8 m trunk casting it — the first
            // pass had this at 0.26 m against 8.6 cm texels, which washed the
            // trunk shadows out to nothing.
            canopyShadow.Apply(gm, floorLook);

            // ------------------------------------------------- moonlight shafts
            // Blades through the tear in the canopy, along the real moon
            // bearing, landing in and around the clearing.
            //
            // USER FINDING, ModBuild 137: "Die Lichtstrahlen zwischen den Bäumen
            // in der Waldumgebung kommt nicht von der Richtung aus, aus dem der
            // Mond zu sehen ist. Sollte es aber."
            //
            // THE DIRECTION ITSELF IS NOT THE BUG, and that was checked before
            // anything was changed here: every shaft's axis is -MoonDir exactly,
            // so the three of them project to lines that meet at the projection
            // of +MoonDir — i.e. AT THE MOON — from any camera whatsoever
            // (parallel lines meet at their direction's vanishing point). The
            // 137 preview measurement puts all three intersections at the moon's
            // own pixel, 640 of 1280 across, to within a pixel. The real
            // disagreement is in the RUNTIME, not in this mesh: the sky branch
            // and the room branch are given DIFFERENT yaws when the environment
            // is placed (SkyAlternative.PlaceSky uses the player's head yaw,
            // TryPlaceRoom uses the board's yaw), so in the game the sky's moon
            // and this room's shafts stand at whatever angle those two happen to
            // differ by. That is a src/ fix and is reported as one.
            //
            // WHAT IS FIXED HERE is the other half of the same reading: the
            // shafts used to STOP about a metre and a half BELOW the canopy
            // (tops at y 7.8-8.8 m where the canopy shell is at 9.1-9.9 m), and
            // their top 18% faded out on top of that, so the light appeared to
            // begin in mid-air among the trunks with the moon far above it and
            // nothing joining the two. Each shaft now runs UP the moon bearing
            // until it is clear of the canopy — it is seen coming THROUGH the
            // tear the moon is seen through — and its fade-in is a fixed 1.1 m
            // rather than a fifth of its length, so it is at full strength where
            // it crosses that opening. No triangles are added: the same two
            // crossed blades, longer.
            {
                var sh = new Acc();
                var dir = -MoonDir.normalized;                       // light travels DOWN-sunward
                var across = Vector3.Cross(Vector3.up, moonHoriz).normalized;
                // Build each shaft from where it LANDS, not from where it enters.
                // Aiming down from a fixed canopy point sent every beam straight
                // through the play space, where it read as a pane of glass across
                // the whole view; now they strike the clearing floor around the
                // board and are seen from outside.
                // SHAFT BITE — CANOPY SHADOW, second use, and the objective of
                // this search is the thing ModBuild 139 got backwards.
                //
                // The landing point is CHOSEN with the map instead of taken from
                // the first roll of the dice. Every candidate re-rolls only the
                // two AUTHORED jitters (the +-0.4 m sideways nudge and the
                // 4.0-7.4 m reach along the bearing); the spacing, the widths,
                // the strengths and the derived length are untouched, so the
                // authored look of the three shafts is exactly the authored look
                // — the wood has been approved twice and this may not restyle it.
                //
                // WHAT CHANGED, AND WHY. The 139 objective was "the clearest
                // gap": it scored a candidate by how LIT its upper run was and
                // took the maximum. That is a search for a beam with nothing in
                // it, and it found one — the log reported the three upper runs
                // 98%, 95% and 89% clear, and a beam that is 98% clear over its
                // upper run has, by construction, almost nothing left to cast a
                // shadow into it. The user then saw exactly what those numbers
                // predicted: three perfectly smooth beams.
                //
                // A real shaft through a canopy is not a clear tube. It is open
                // at the TOP — that is what makes it read as light coming in
                // through the tear, and it is the one thing worth protecting —
                // and CROSSED further down, which is what makes it read as light
                // that trees stand in. So the score is now built from three
                // separate readings of the same beam:
                //   * HEAD (the top fifth) must be open   — a gate, not a term;
                //   * ARRIVAL (the bottom seventh) must still land in its pool
                //     — the other gate, and the reason the visibility floor
                //     exists at all;
                //   * the BODY between them is rewarded for being BROKEN: how
                //     deep its deepest bite goes, and how much of its length is
                //     inside a band you can point at.
                // Gates multiply and the reward adds, so no amount of dappling
                // can buy a beam that starts in a crown or dies before the pool.
                int moved = 0;
                // The beam, read at 48 stations along its own axis in the
                // SHADER's own visibility (Visible applies the bite ramp and the
                // floor), plus the two things a mean cannot say: the deepest
                // single point and the longest continuous dark run. A beam that
                // averages 60% lit with no band deeper than 15% still looks
                // smooth — the BAND is what he sees, not the average.
                const int BeamTaps = 48;
                const float BandVis = 0.55f;      // "inside a band you can point at"
                const int Rolls = 24;             // candidates per shaft, see the loop
                // ...and 9 LINES ACROSS, which ModBuild 142 had to add before the
                // search could see what it was choosing. A blade is up to 4 m wide
                // at the foot and a trunk's bar is 0.5-0.8 m of that, so a bar
                // almost never lands on the axis: measured on the spine alone, two
                // of the three shafts reported "0.0 m of band, deepest 62%" in a
                // wood where the very same log's TRUNK TOOTH said every one of them
                // carried a full-strength trunk edge somewhere across its width.
                // Both numbers were right. The axis was the wrong question.
                //
                // The offsets are FRACTIONS of the blade's own half-width, so they
                // follow the taper, and both blade planes are walked because they
                // span the map's two axes (see AddShaft). BARRED is therefore an
                // AREA of beam inside a bar, not a length of centre line, and
                // BANDLEN is the longest run down whichever line is most barred —
                // "the bar is four metres long" said about the line that has one.
                var acrossFrac = new[] { 0f, 0.45f, -0.45f, 0.85f, -0.85f };
                const int Across = 5;
                (float head, float arrive, float whole, float minVis, float barred,
                 float bandLen, string bar) Probe(Vector3 t0, float l, float wTop, float wBot)
                {
                    Vector3 e1 = Vector3.Cross(dir, Vector3.up).normalized;
                    Vector3 e2 = Vector3.Cross(dir, e1).normalized;
                    float head = 0f, arrive = 0f, whole = 0f, lo = 1f;
                    int nHead = 0, nArrive = 0, nBody = 0, nBarred = 0, bestRun = 0;
                    var runs = new int[Across * 2];
                    var bar = new System.Text.StringBuilder(BeamTaps);
                    for (int s = 0; s < BeamTaps; s++)
                    {
                        float u = Mathf.Lerp(0.02f, 0.98f, s / (BeamTaps - 1f));
                        Vector3 c = t0 + dir * (l * u);
                        float half = Mathf.Lerp(wTop, wBot, u);
                        for (int a = 0; a < Across * 2; a++)
                        {
                            Vector3 e = a < Across ? e1 : e2;
                            float vis = canopyShadow.Visible(
                                c + e * (acrossFrac[a % Across] * half), beamLook, useNear: true);
                            whole += vis;
                            // the printed profile stays the AXIS line, so it can be
                            // read against every previous round's log
                            if (a == 0)
                                bar.Append((char)('0' + Mathf.Clamp(Mathf.FloorToInt(vis * 9.99f), 0, 9)));
                            if (u < 0.20f) { head += vis; nHead++; }
                            else if (u > 0.86f) { arrive += vis; nArrive++; }
                            else
                            {
                                nBody++;
                                if (vis < lo) lo = vis;
                                if (vis < BandVis)
                                {
                                    nBarred++; runs[a]++;
                                    if (runs[a] > bestRun) bestRun = runs[a];
                                }
                                else runs[a] = 0;
                            }
                        }
                    }
                    return (head / Mathf.Max(nHead, 1), arrive / Mathf.Max(nArrive, 1),
                            whole / (BeamTaps * Across * 2), lo,
                            nBarred / (float)Mathf.Max(nBody, 1),
                            bestRun * l / BeamTaps, bar.ToString());
                }
                // SHAFT MASS — THE MEASUREMENT THAT WOULD HAVE CAUGHT ModBuild
                // 141 BEFORE HE DID. Probe above walks the beam's AXIS, one line
                // of samples, and the axis is the one direction in which the comb
                // is invisible: (u,v) is constant down a beam, so every station on
                // the axis reads the SAME texel and the profile comes out perfectly
                // smooth while the blade beside it is a picket fence. The log said
                // |999...711121111112479999| and meant it, and the screenshot still
                // showed stripes, and both were true.
                //
                // So the beam is also read ACROSS, which is the only direction the
                // striping lives in, and at a spacing (6 cm) close to the crisp
                // map's own texel (5.5 cm) so that a per-texel step cannot hide
                // between two samples. GRAIN is the mean absolute change in
                // visibility between neighbouring samples and TOOTH is the worst
                // one: with the old shared bite ramp a single texel of sideways
                // motion moved a seventh of the tap average through a 0.38-wide
                // ramp, i.e. about a third of full shadow, so TOOTH ran to tens of
                // points. A dapple is a few points; a comb is tens.
                //
                // Both blade axes are swept because they are the two axes of the
                // MAP: AddShaft spans blade one by Cross(dir, up) = AxisU and blade
                // two by the perpendicular = AxisV. Sweeping only one would answer
                // for half the geometry.
                //
                // THE TWO CLASSES ARE MEASURED SEPARATELY, and that is not
                // bookkeeping — a combined figure cannot tell the two things apart,
                // because the brief asks for a LARGE across-beam step (the crisp
                // edge of a trunk's bar) at the same time as it forbids one (the
                // comb). The first measured run made the point: a single combined
                // TOOTH of 74-85 points looked like the comb surviving and was in
                // fact the wanted bar, cast by a trunk the beam was standing
                // inside. So: CROWN GRAIN and CROWN TOOTH are the comb, and they
                // must be small; TRUNK TOOTH is the bar, and it should be large and
                // rare. The old build has no separated number to compare against
                // because it had no separated term — the before/after for the comb
                // itself is measured in the rendered PICTURES, across a beam.
                (float folGrain, float folTooth, float trunkTooth, float trunkPct) Grain(
                    Vector3 t0, float l, float wTop, float wBot)
                {
                    Vector3 e1 = Vector3.Cross(dir, Vector3.up).normalized;
                    Vector3 e2 = Vector3.Cross(dir, e1).normalized;
                    double gSum = 0; int gN = 0; float worstF = 0f, worstT = 0f;
                    double tSum = 0, fSum = 0;
                    foreach (float u in new[] { 0.30f, 0.45f, 0.60f, 0.75f, 0.88f })
                    {
                        Vector3 c = t0 + dir * (l * u);
                        float half = Mathf.Lerp(wTop, wBot, u);
                        int n = Mathf.Max(4, Mathf.RoundToInt(2f * half / 0.06f));
                        foreach (var e in new[] { e1, e2 })
                        {
                            float pT = 0f, pF = 0f;
                            for (int s = 0; s <= n; s++)
                            {
                                canopyShadow.Visible(
                                    c + e * Mathf.Lerp(-half, half, s / (float)n),
                                    beamLook, useNear: true, out float tT, out float fT);
                                tSum += tT; fSum += fT;
                                if (s > 0)
                                {
                                    float dF = Mathf.Abs(fT - pF), dT = Mathf.Abs(tT - pT);
                                    gSum += dF; gN++;
                                    if (dF > worstF) worstF = dF;
                                    if (dT > worstT) worstT = dT;
                                }
                                pT = tT; pF = fT;
                            }
                        }
                    }
                    return ((float)(gSum / Mathf.Max(gN, 1)), worstF, worstT,
                            (float)(tSum / System.Math.Max(tSum + fSum, 1e-6)));
                }

                /// The beam's core against the wood, cheaply enough to run inside
                /// the candidate loop: the smallest gap between the AXIS and any
                /// bark over the stretch that is actually drawn. Negative means the
                /// beam is standing inside a tree. See the placement gate below.
                float AxisClear(Vector3 t0, float l)
                {
                    float best = 99f;
                    for (int s = 0; s <= 32; s++)
                    {
                        Vector3 c = t0 + dir * (l * Mathf.Lerp(0.08f, 0.97f, s / 32f));
                        foreach (var t in trees)
                        {
                            // cheap reject first: nothing 2 m away in plan can be
                            // the nearest bark, and TrunkAt/HauntTrunkRadius are
                            // noise evaluations, not arithmetic
                            float dx = c.x - t.p.x, dz = c.z - t.p.y;
                            if (dx * dx + dz * dz > 4f) continue;
                            float y = c.y - ForestY(t.p.x, t.p.y);
                            if (y < 0f || y > t.h) continue;
                            Vector3 tc = TrunkAt(t, y);
                            float d = new Vector2(c.x - tc.x, c.z - tc.z).magnitude
                                      - HauntTrunkRadius(t, y);
                            if (d < best) best = d;
                        }
                    }
                    return best;
                }

                // ...and the OTHER half of the same user finding: "auch gibt es so
                // manchmal noch Strahlen die durch den Stamm gehen, auf jeden Fall
                // sieht es so aus." The hedge is fair and the question is a
                // GEOMETRIC one, so it is answered geometrically rather than by
                // looking at a picture. A blade is a flat quad up to 2 m out from
                // its axis; where that quad and a trunk's cylinder intersect, the
                // half of the blade on the camera's side of the trunk passes the
                // ZTest and is drawn additively OVER the bark. That is not a depth
                // bug — lit mist between an eye and a trunk really does veil it —
                // but it reads as a beam boring through wood, and the fix for a
                // thing that is right and looks wrong is placement.
                //
                // Two numbers, because they mean different things: the AXIS
                // clearance (does the beam's core run into a tree?) and how much of
                // the blade's own span is inside bark (does the picture show it?).
                float BladeInBark(Vector3 t0, float l, float wTop, float wBot)
                {
                    int inN = 0, allN = 0;
                    Vector3 e1 = Vector3.Cross(dir, Vector3.up).normalized;
                    Vector3 e2 = Vector3.Cross(dir, e1).normalized;
                    for (int s = 0; s <= 60; s++)
                    {
                        float u = s / 60f;
                        Vector3 c = t0 + dir * (l * u);
                        float half = Mathf.Lerp(wTop, wBot, u);
                        foreach (var e in new[] { e1, e2 })
                            for (int k = -4; k <= 4; k++)
                            {
                                Vector3 p = c + e * (half * k / 4f);
                                allN++;
                                foreach (var t in trees)
                                {
                                    float dx = p.x - t.p.x, dz = p.z - t.p.y;
                                    if (dx * dx + dz * dz > 4f) continue;
                                    float y = p.y - ForestY(t.p.x, t.p.y);
                                    if (y < 0f || y > t.h) continue;
                                    Vector3 tc = TrunkAt(t, y);
                                    if (new Vector2(p.x - tc.x, p.z - tc.z).magnitude
                                        < HauntTrunkRadius(t, y)) { inN++; break; }
                                }
                            }
                    }
                    return inN / (float)Mathf.Max(allN, 1);
                }

                for (int i = 0; i < 3; i++)
                {
                    Vector3 hit = Vector3.zero, top = Vector3.zero;
                    float len = 0f, best = -1f;
                    int chosen = 0;
                    (float head, float arrive, float whole, float minVis, float barred,
                     float bandLen, string bar) pick = default;
                    // width first: the blade taper is a function of the shaft index
                    // alone, and Probe now needs it to walk the beam as a surface
                    float w0 = 0.42f + 0.30f * Hash3(i, 4, 0, 5311);
                    float w1 = w0 * 2.8f;
                    // 24 rolls, up from 8 (and 5 before that). The objective is a
                    // conjunction of four gates now, and a conjunction is satisfied
                    // by a smaller share of the rolls with every gate added. The
                    // measured run at 8 is what forced it: with the crowns no
                    // longer able to bite, TWO of the three shafts came out with no
                    // band anywhere (deepest 63% and 68% visibility, 0.0 m of body
                    // under the band threshold) — the search had simply not been
                    // offered a candidate with a trunk up-light of it. A candidate
                    // is 48 CPU taps; two dozen of them per shaft is nothing at
                    // build time and it is the difference between a shaft with a
                    // shadow in it and a shaft without one.
                    float clr = 0f;
                    for (int c = 0; c < Rolls; c++)
                    {
                        // c == 0 IS the previously authored roll, bit for bit
                        float side = (i - 1.0f) * 3.1f + 0.8f * (Hash3(i, 0, c, 5311) - 0.5f);
                        Vector3 h = moonHoriz * (4.0f + 3.4f * Hash3(i, 1, c, 5311)) + across * side;
                        h.y = ForestY(h.x, h.z) - 0.15f;
                        // The length is DERIVED: run up the bearing until the top
                        // stands `clear` metres over the canopy at the radius it
                        // reaches. Both sides of that condition move with the
                        // length (the canopy rises 0.30 m per metre of radius,
                        // the shaft 0.84), so it is solved by iteration — six
                        // passes is far more than the two it needs. The old fixed
                        // 11.5-14 m is the floor, and 19 m the ceiling: past ~19 m
                        // the canopy closes again (CanopyMask's outer term) and a
                        // shaft that ends up there would be roofed over instead
                        // of open to the sky.
                        // `clear` — how far the top must stand over the canopy —
                        // is now re-rolled per candidate as well (c == 0 is still
                        // the authored value, bit for bit). It slides the whole
                        // beam up to a metre along its OWN axis without moving
                        // where it lands or how wide it is, which is the one
                        // extra degree of freedom that costs the authored look
                        // nothing: shaft 2's landing jitter alone could not find
                        // anything to cross it in eight rolls.
                        float clear = 1.4f + 0.9f * Hash3(i, 3, c, 5311);
                        float lenMax = 17.6f + 2.0f * Hash3(i, 6, 0, 5311);
                        float l = 11.5f + 2.5f * Hash3(i, 3, 0, 5311);
                        for (int it = 0; it < 6; it++)
                        {
                            Vector3 t0 = h - dir * l;
                            float rTop = new Vector2(t0.x, t0.z).magnitude;
                            l = Mathf.Clamp((CanopyY(rTop) + clear - h.y) / MoonDir.normalized.y,
                                            11.5f, lenMax);
                        }
                        Vector3 tp = h - dir * l;                   // back up along the beam
                        var m = Probe(tp, l, w0, w1);
                        // THE TWO GATES. Neither is a preference: a beam whose
                        // head is in a crown does not read as light entering the
                        // tear (that is what ModBuild 137 spent a round fixing),
                        // and a beam that does not arrive is a beam the user
                        // never sees land. Below the lower knee the gate is zero,
                        // so no amount of dappling can buy such a candidate.
                        float headGate = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(0.55f, 0.85f, m.head));
                        float footGate = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(0.30f, 0.65f, m.arrive));
                        // ...and a third, on the WHOLE beam: three quarters of a
                        // shaft in shadow is not a dappled shaft, it is a shaft
                        // that has been deleted, which is the failure the first
                        // pass shipped.
                        float aliveGate = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(0.32f, 0.55f, m.whole));
                        // THE REWARD: how deep the deepest bite goes, normalised
                        // against the floor so a perfect bite scores 1, plus how
                        // much of the body is inside a band — capped, because
                        // past about a third of the run more darkness stops
                        // adding anything a viewer can read as "a bough".
                        float bite = Mathf.Clamp01((1f - m.minVis) / Mathf.Max(beamLook.Strength, 1e-3f));
                        // 0.10 rather than 0.30, and the number changed meaning as
                        // well as value: barred is now an AREA of blade inside a bar
                        // rather than a length of centre line. A trunk's bar is
                        // 0.5-0.8 m of a 2-4 m width and runs 4-5 m of an 18 m
                        // length, so a strong single crossing is about a tenth of
                        // the surface. The old normaliser was calibrated against a
                        // spine that needle speckle could fill from end to end.
                        float dapple = Mathf.Clamp01(m.barred / 0.10f);
                        // THE EMBEDDING GATE, and the answer to the second half of
                        // USER FINDING 141: "auch gibt es so manchmal noch Strahlen
                        // die durch den Stamm gehen, auf jeden Fall sieht es so
                        // aus." He is right about what he sees, and it is neither a
                        // depth-order bug nor, in general, a placement mistake.
                        //
                        // THE GEOMETRY SETTLES IT, and it settles it the opposite
                        // way round from the obvious guess. A shaft is PARALLEL TO
                        // THE LIGHT. So anything that shadows a point of the beam
                        // lies on that point's own ray to the moon — which is the
                        // beam's own axis, further up. A trunk can therefore bar
                        // this beam IF AND ONLY IF the beam passes through that same
                        // trunk higher up: "a tree stands up-light of the beam" and
                        // "the beam goes through that tree" are the same sentence.
                        // (Numerically: 5 m up-light along the beam is 3.83 m
                        // horizontally and 3.21 m higher at the moon's 40 deg — and
                        // 5 m further up the beam is at exactly that point.) The
                        // first attempt at this round gated crossings OUT and got
                        // precisely what the arithmetic promises: three beams with
                        // no band anywhere, deepest 63-78% visibility, 0.0 m of bar
                        // between them. The crossing is not the defect. It is the
                        // mechanism.
                        //
                        // What is left, and what this gate is for, is the ONE case
                        // that really does look wrong: a beam whose AXIS is buried
                        // deep in the timber, so that the lit half of the blade —
                        // the half on the camera's side of the trunk, which passes
                        // the ZTest and is additively drawn over the bark, as lit
                        // mist in front of a tree genuinely is — appears to sprout
                        // from INSIDE the wood rather than to graze past it.
                        // ModBuild 141 shipped one at 0.21 m inside bark. So the
                        // gate does not ask for clearance, it only refuses
                        // EMBEDDING: full anywhere from the bark surface outwards,
                        // and zero by 0.35 m in. A tangent beam — core just outside
                        // the trunk, blade span crossing it — keeps its bar, and is
                        // the reading the room wants: the tree stands IN the light,
                        // its face is lit by it, and its shadow falls down the beam.
                        float cl = AxisClear(tp, l);
                        float clearGate = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(-0.35f, 0.0f, cl));
                        float score = headGate * footGate * aliveGate * clearGate
                                      * (0.55f * bite + 0.45f * dapple);
                        if (score > best + 1e-4f)
                        { best = score; chosen = c; hit = h; top = tp; len = l; pick = m; clr = cl; }
                        // The authored roll already does the job — take it and
                        // stop, so the room stays the room the user approved
                        // whenever it can.
                        if (c == 0 && score >= 0.62f) break;
                    }
                    if (chosen != 0) moved++;
                    // The map is sized off the FLOOR (see THE BOX above) and the
                    // blades are only argued to fall inside it. Check, because a
                    // blade that projects off the edge reads as fully lit and the
                    // whole feature would silently do nothing for it — which is
                    // the exact class of failure the first pass shipped.
                    foreach (var probe in new[] { top, hit,
                                                  top + across * w0, top - across * w0,
                                                  hit + across * w1, hit - across * w1,
                                                  hit + Vector3.Cross(dir, across) * w1,
                                                  hit - Vector3.Cross(dir, across) * w1 })
                        if (!canopyShadow.Inside(probe, 0.03f))
                            throw new Exception($"Moon shaft {i}: a blade corner at "
                                + $"({probe.x:F2},{probe.y:F2},{probe.z:F2}) falls outside the canopy "
                                + "shadow map — widen shadowRoi or the map's height range.");
                    float amp = 0.6f + 0.4f * Hash3(i, 5, 0, 5311);
                    // fade lengths in METRES, carried per vertex (see AddShaft):
                    // 1.1 m in at the top so the beam is already bright where it
                    // crosses the canopy, 5.5 m out at the bottom so it still
                    // dies in the air over its pool instead of ending on it.
                    AddShaft(sh, top, dir, len, w0, w1, amp, across, 1.1f / len, 5.5f / len);
                    var gr = Grain(top, len, w0, w1);
                    float bark = BladeInBark(top, len, w0, w1);
                    Debug.Log($"[GloomhavenVR][Env] Moon shaft {i}: lands ({hit.x:F2},{hit.z:F2}) "
                              + $"r {new Vector2(hit.x, hit.z).magnitude:F1} m, length {len:F1} m, top "
                              + $"y {top.y:F1} m at r {new Vector2(top.x, top.z).magnitude:F1} m "
                              + $"(canopy there {CanopyY(new Vector2(top.x, top.z).magnitude):F1} m), "
                              + $"candidate {chosen} of {Rolls} scoring {best:F2} — head {pick.head * 100f:F0}% lit, "
                              + $"arrival {pick.arrive * 100f:F0}%, whole beam {pick.whole * 100f:F0}%, i.e. "
                              + $"{(1f - pick.whole) * 100f:F0}% OF THE BEAM SHADOWED; DEEPEST BAND down to "
                              + $"{pick.minVis * 100f:F0}% visibility, longest continuous band under "
                              + $"{BandVis * 100f:F0}% is {pick.bandLen:F1} m of {len:F1} ({pick.barred * 100f:F0}% "
                              + $"of the body). Profile top->foot |{pick.bar}|. ACROSS THE BLADES (the axis "
                              + "the profile above cannot see, and the only one the comb lived in): CROWN "
                              + $"GRAIN {gr.folGrain * 100f:F1} points of visibility per 6 cm, worst CROWN "
                              + $"TOOTH {gr.folTooth * 100f:F1} — that pair IS the comb, and single digits "
                              + $"is a dapple; worst TRUNK TOOTH {gr.trunkTooth * 100f:F1} points, which is "
                              + $"the wanted bar edge and should be large. {gr.trunkPct * 100f:F0}% of this "
                              + $"beam's shadowing is TRUNK and {(1f - gr.trunkPct) * 100f:F0}% crown mass. "
                              + $"Beam AXIS clears the nearest bark by {clr:F2} m and {bark * 100f:F1}% of "
                              + "the blade span is inside bark — the fraction that can be drawn over a "
                              + "trunk from some yaw and read as a beam going through it.");
                }
                Debug.Log($"[GloomhavenVR][Env] Moon shafts: {moved} of 3 moved off the authored roll — the "
                          + "search now wants a beam that is OPEN AT THE TOP and CROSSED further down, not "
                          + "the clearest gap it can find (which is what ModBuild 139 asked for and got).");
                var shaftMat = NewRoomMat("S_Shaft.mat", "GloomhavenVR/EnvShaft");
                // a touch stronger than ModBuild 133 (alpha 0.30): with the wood
                // around them darker the blades are now the brightest thing in the
                // room, which is exactly what should draw the eye to the clearing
                shaftMat.SetColor("_Tint", new Color(0.56f, 0.66f, 0.92f, 0.34f));
                shaftMat.SetFloat("_Softness", 6.5f);
                shaftMat.SetFloat("_Shimmer", 0.30f);
                shaftMat.SetFloat("_ShimmerSpeed", 0.20f);
                // CANOPY SHADOW on the blades — the answer to the user finding.
                // Multiplied into the existing across/along/shimmer/facing
                // product, so the pass stays additive and order-independent and
                // no other term is disturbed.
                //
                // beamLook, declared with the bake: a 7 m throw so the boughs the
                // beam is actually passing through are found, a bite ramp so only
                // the solid ones are drawn, and a 12% visibility floor so that
                // when one does bite it is unmistakable and the beam still
                // arrives. The penumbra is 0.22 m against the floor's 0.18
                // because a blade's shadow edge hangs in mid-air, where there is
                // no albedo detail to hide a hard one — a beam of lit mist that
                // goes to nothing behind a bough looks CUT. And 12%, not 0: what
                // is left over is the light the mist scatters sideways into the
                // shadowed stretch, which is real. A shaft does not have a black
                // bite taken out of it, it goes dim and comes back.
                canopyShadow.Apply(shaftMat, beamLook);
                var shMesh = SaveMesh("Env_S_Shafts.asset", sh.Build("Env_S_Shafts"));
                Place(root, "MoonShafts", shMesh, Vector3.zero, Vector3.zero, Vector3.one, shaftMat);
            }

            // ---------------------------------------------------- ground props
            // ModBuild 134: props fade out with the same urgency the trunks do —
            // a lit fern at 12 m is a described object where there should be
            // nothing but a suggestion.
            float Fade(Vector3 pos) =>
                Mathf.SmoothStep(1f, 0.04f, Mathf.InverseLerp(5.5f, 12f, new Vector2(pos.x, pos.z).magnitude));
            // `bed` is this prop's BEARING REACH in metres — how far up from its
            // lowest touching piece the parts that are meant to be lying in the
            // ground still extend — and passing it is the statement "this
            // photoscan carries a skirt of the ground it was scanned on, so bury
            // the skirt". See the BEDDING A PHOTOSCAN IN block at the bottom of
            // this file. It is small for a log (its bearing line is a line) and
            // large for a rock SET, whose dozen boulders bed independently over
            // the relief the set spans. 0 = off, which is right for the alpha-card
            // understory (a fern is not standing on anything), for the leaning
            // tree (it touches at one end and leans away) and for anything
            // resting on another prop rather than on the floor.
            GameObject SProp(string n, string mesh, string tex, Vector3 pos, float yaw, float scale,
                Vector3? e3 = null, Vector3? s3 = null, bool cutout = false, float bump = 1f,
                float sink = 0.05f, float tintExtra = 1f, GameObject support = null,
                Quaternion? rot = null, float bed = 0f, float bedBand = 0.05f, float bedQ = 0.90f)
            {
                var go = Prop(root, n, mesh, tex, pos, yaw, scale, "S",
                    tintMul: Fade(pos) * tintExtra, euler3: e3, scale3: s3, cutout: cutout,
                    bump: bump, sink: sink, support: support, rot: rot);
                if (bed > 0f) Bed(go, bed, bedBand, bedQ);
                return go;
            }

            // deadfall: one log across the path, one at the clearing edge
            // both were pulled outward for the 9.0 m PlaySpace: a 3 m log lying
            // across the path reached 4.28 m from the centre at its near end
            SProp("Log0", "dead_tree_trunk", "dead_tree_trunk", new Vector3(-3.1f, 0, -6.1f), 62, 1.0f, sink: 0.10f, bed: 0.14f);
            SProp("Log1", "dead_tree_trunk", "dead_tree_trunk", new Vector3(6.9f, 0, 5.0f), 128, 1.15f, sink: 0.12f, bed: 0.14f);
            // THE leaning dead tree — caught in its neighbour's crown and never
            // fell. Rest() grounds it vertex-exactly despite the 62° tilt.
            // No bedding: it TOUCHES the ground at one end and leans away from it,
            // so it has no bearing surface to bury and a drop would simply sink
            // the whole tree.
            SProp("LeanTree", "dead_tree_trunk_02", "dead_tree_trunk_02", new Vector3(-6.4f, 0, 5.9f),
                0, 1.35f, e3: new Vector3(0f, 24f, 62f), sink: 0.05f);
            // Stumps; the axe is left in the near one. THIS IS ONE OF THE TWO
            // THINGS THE USER PHOTOGRAPHED: tree_stump_01 is 1.5 x 1.7 m across
            // and 0.59 m tall, which is a stump plus most of a square metre of
            // the forest floor it was scanned on, and Rest() was floating that
            // apron a hand's width over this room's floor — where it read as a
            // pale dome with a razor-edged plate running out of it. `bed` buries
            // it. bedQ 0.99 rather than the default 0.90 because a stump's apron
            // is ONE surface and any part of it left standing is the whole
            // artefact back again; a rock set, by contrast, wants the default,
            // where a couple of boulders may keep their feet in the air rather
            // than drown the other twenty-four. Bedding happens before the axe is
            // placed, so the axe still lands on the stump's real surface.
            var stump0 = SProp("Stump0", "tree_stump_01", "tree_stump_01", new Vector3(4.4f, 0, -4.1f), 60, 1.05f, bed: 0.18f, bedBand: 0.18f, bedQ: 0.99f);
            SProp("Stump1", "tree_stump_02", "tree_stump_02", new Vector3(-7.2f, 0, -2.1f), 200, 1.0f, bed: 0.18f, bedBand: 0.18f, bedQ: 0.99f);
            // THE AXE. User finding, ModBuild 134: "Die Axt schwebt falsch rum
            // auf dem Stamm." Both halves of that were true, and both came from
            // guessing a pose in Euler angles instead of deriving it.
            //
            // wooden_axe_02's own axes (measured from the OBJ): the haft runs
            // along local Y with the head at +Y (y 0.30..0.42) and the butt at
            // y = -0.28; the blade widens along +Z and its cutting EDGE is the
            // line at z = 0.185. Those two facts are all a stuck axe needs:
            //   * the haft (local -Y) must rise out of the block at ~34 deg,
            //   * the edge (local +Z) must point INTO the wood — and because the
            //     two are perpendicular in the mesh, "handle up at 34 deg" fixes
            //     the edge at 34 deg past vertical automatically, both leaning
            //     the same way. That is what an axe left in a chopping block
            //     looks like, and it is not expressible as three round numbers.
            // The old pose had the head 2 cm ABOVE the stump (sink -0.02, i.e. a
            // deliberate lift); now it bites 4.5 cm INTO it, and Rest() measures
            // that against the stump's real triangles under the blade.
            {
                const float rise = 34f * Mathf.Deg2Rad;
                var h = new Vector3(Mathf.Sin(40f * Mathf.Deg2Rad), 0f, Mathf.Cos(40f * Mathf.Deg2Rad));
                var handle = h * Mathf.Cos(rise) + Vector3.up * Mathf.Sin(rise);   // head -> butt
                var edge = h * Mathf.Sin(rise) - Vector3.up * Mathf.Cos(rise);     // eye -> cutting edge
                // Rest() re-centres a prop's FOOTPRINT on the asked-for spot, and
                // this prop's footprint is dominated by the haft sticking out over
                // the edge — so the position is chosen so the HEAD, not the
                // silhouette, lands on the middle of the stump top (4.40,-4.10):
                // the head sits ~0.29 m back along the lean bearing from the
                // footprint centre. Aiming at the centre put the blade out on the
                // stump's falling rim, where it read as hanging past the back.
                var head = new Vector3(4.40f, 0f, -4.10f);
                SProp("Axe", "wooden_axe_02", "wooden_axe_02", head + h * 0.29f, 0, 1.0f,
                    rot: Quaternion.LookRotation(edge, -handle), sink: 0.060f, support: stump0);
            }
            // Roots breaking the floor, at the path rim.
            //
            // root_cluster_02 IS GONE, and this is the second half of the ModBuild
            // 139 "etwas undefiniertes" finding — the pale angular shard, as
            // against the stumps' hovering apron above. It is not a fixable
            // placement: the asset is a 2.4 x 2.7 m photoscanned patch of forest
            // floor that is 0.17 m THICK, i.e. a ground DECAL delivered as
            // geometry, and this room's terrain has more relief than that across
            // the patch. Sunk far enough to hide its apron (it was at sink 0.14 of
            // its own 0.16 m) all that surfaces is a scatter of disconnected
            // decimated top facets — a hard-edged tan plate with a stretched
            // texture and no readable shape, which is exactly what was
            // photographed. Raised far enough to read as roots, the apron floats
            // and is worse. There is no height at which it works, so it goes: the
            // two single_root props below already do the job at the path rim, and
            // every tree here has a modelled flare at its foot.
            // was (-1.9,-3.1): that reached 3.6 m into the 9.0 m PlaySpace disc
            SProp("Root2", "single_root", "single_root", new Vector3(-3.6f, 0, -4.5f), 300, 1.0f, sink: 0.06f, bed: 0.08f);
            SProp("Root3", "single_root", "single_root", new Vector3(2.6f, 0, 4.9f), 130, 0.9f, sink: 0.06f, bed: 0.08f);
            // mossy rock outcrops. The sets are 8 m wide in their own frame — a
            // dozen separate boulders — so the bedding is per SHELL (see
            // ShellOf): one drop measured off the deepest boulder would leave the
            // other eleven standing on air, which is what the ModBuild 139
            // previews show them doing.
            SProp("Rocks0", "rock_moss_set_01", "rock_moss_set_01", new Vector3(-6.4f, 0, 3.4f), 30, 0.42f, sink: 0.12f, bed: 0.30f);
            SProp("Rocks1", "rock_moss_set_02", "rock_moss_set_02", new Vector3(7.4f, 0, -1.4f), 245, 0.5f, sink: 0.12f, bed: 0.30f);
            SProp("Rocks2", "rock_moss_set_02", "rock_moss_set_02", new Vector3(-3.1f, 0, 7.2f), 95, 0.38f, sink: 0.10f, bed: 0.30f);
            // deadfall branches
            SProp("Branches0", "dry_branches_medium_01", "dry_branches_medium_01", new Vector3(1.4f, 0, -5.9f), 80, 1.0f, bed: 0.10f);
            SProp("Branches1", "dry_branches_medium_01", "dry_branches_medium_01", new Vector3(-6.9f, 0, -5.0f), 250, 0.9f, bed: 0.10f);
            // the story beat at the bend of the path: something was dropped here
            SProp("Crate", "wooden_crate_01", "wooden_crate_01", new Vector3(-4.1f, 0, -7.0f), 24, 0.95f,
                e3: new Vector3(-14f, 24f, 78f), sink: 0.06f, tintExtra: 0.85f);
            // understory
            SProp("Fern0", "fern_02", "fern_02", new Vector3(5.9f, 0, 4.3f), 0, 1.8f, cutout: true, sink: 0.05f, tintExtra: 0.72f);
            SProp("Fern1", "fern_02", "fern_02", new Vector3(-5.9f, 0, 4.8f), 200, 1.6f, cutout: true, sink: 0.05f, tintExtra: 0.72f);
            SProp("Fern2", "fern_02", "fern_02", new Vector3(-2.4f, 0, -6.1f), 95, 1.5f, cutout: true, sink: 0.05f, tintExtra: 0.72f);
            SProp("Grass0", "grass_medium_02", "grass_medium_02", new Vector3(2.8f, 0, 5.6f), 0, 1.9f, cutout: true, sink: 0.05f);
            SProp("Grass1", "grass_medium_02", "grass_medium_02", new Vector3(-6.4f, 0, -3.4f), 260, 2.0f, cutout: true, sink: 0.05f);
            SProp("Shrub0", "shrub_03", "shrub_03", new Vector3(6.4f, 0, 1.1f), 20, 2.1f, cutout: true, sink: 0.08f);
            SProp("Shrub1", "shrub_03", "shrub_03", new Vector3(-7.2f, 0, 0.4f), 160, 1.9f, cutout: true, sink: 0.08f);
            SProp("Shrub2", "shrub_03", "shrub_03", new Vector3(0.9f, 0, 6.8f), 300, 2.2f, cutout: true, sink: 0.08f);
            // moss on the ground and draped over the deadfall
            SProp("Moss0", "moss_01", "moss_01", new Vector3(-2.4f, 0, -4.4f), 20, 1.5f, cutout: true, sink: 0.02f);
            SProp("Moss1", "moss_01", "moss_01", new Vector3(5.9f, 0, 4.1f), 200, 1.3f, cutout: true, sink: 0.02f);
            SProp("Moss2", "moss_01", "moss_01", new Vector3(-6.1f, 0, 3.2f), 110, 1.6f, cutout: true, sink: 0.02f);
            SProp("Moss3", "moss_01", "moss_01", new Vector3(3.4f, 0, -4.8f), 260, 1.2f, cutout: true, sink: 0.02f);

            // ------------------------------------------------- lights in the dark
            // Small additive spheres, world-anchored, no billboarding (EnvGlow
            // falls off toward its own silhouette so it reads as a halo from any
            // direction and in stereo).
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            void Glow(string n, Vector3 p, float r, Color c, float falloff, float elemWarm = 0.35f)
            {
                var m = NewRoomMat($"S_Glow{n}.mat", "GloomhavenVR/EnvGlow");
                m.SetColor("_Tint", c);
                m.SetFloat("_Falloff", falloff);
                // ELEMENT ART: these halos still answer the Light/Dark split in
                // full (they are SOURCES — see EnvGlow), but they only take a
                // THIRD of Fire's warm push. A will-o'-the-wisp that turns
                // ember-orange is not a will-o'-the-wisp any more, and the cold
                // marsh green out among the trunks is the one colour in this wood
                // that is doing narrative work.
                m.SetFloat("_ElemWarm", elemWarm);
                Place(root, "Wisp" + n, glowMesh, p, Vector3.zero, Vector3.one * r, m);
            }
            // ModBuild 134: the halos keep their brightness while everything
            // around them loses two thirds of its own. They are the "occasional
            // wisp, glint of eyes" the user asked to be the ONLY thing readable
            // out there, so they are left alone deliberately — the contrast they
            // gain is the point.
            Glow("Wisp", new Vector3(-4.6f, 0.95f, -6.2f), 0.55f, new Color(0.42f, 1f, 0.60f, 0.16f), 2.4f);
            // the lantern is the only FIRE in the wood, so it takes Fire in full
            Glow("Lantern", new Vector3(9.2f, 1.45f, -7.4f), 0.75f, new Color(1f, 0.60f, 0.24f, 0.17f), 2.2f, 1f);
            Glow("Far", new Vector3(-11.5f, 1.1f, 8.2f), 0.7f, new Color(0.55f, 0.95f, 0.70f, 0.10f), 2.6f);
            // eyes: two tiny cold points at head height, deep between the trunks,
            // 12 cm apart. They never move — that is the point.
            var eyeDir = new Vector3(Mathf.Sin(2.35f), 0f, Mathf.Cos(2.35f));
            var eyeAt = eyeDir * 12.5f + Vector3.up * 1.55f;
            var eyeSide = Vector3.Cross(Vector3.up, eyeDir).normalized * 0.06f;
            Glow("EyeL", eyeAt - eyeSide, 0.045f, new Color(1f, 0.88f, 0.45f, 0.85f), 3.2f);
            Glow("EyeR", eyeAt + eyeSide, 0.045f, new Color(1f, 0.88f, 0.45f, 0.85f), 3.2f);

            // ------------------------------------------------------- the haunts
            // HAUNT SOLID — the creepy easter eggs, as real geometry. See the
            // HAUNTS block, EnvHaunt.shader and EnvHaunt.cginc.
            //
            // WHERE. Every one of the six is between 7.4 m and 16.5 m out — past
            // the 4.5 m play radius by a wide margin, past the understory, and
            // among the trunks where the eye already has nothing to hold on to.
            //
            // THE BEARINGS MOVED THIS ROUND, and the reason is the user's "sehe
            // ich gar nichts". The old rule was "keep them AWAY from the moon,
            // because the moonward wedge is the one direction with light in it and
            // an apparition there would be an exhibit". That rule produced two
            // apparitions nobody could see: a black body against black trees at
            // 15 m and a black mass against a black quarter at 7.6 m. What was
            // missed is that a SOLID does not need a lit background, it needs a
            // lit SIDE — one raked shoulder and a dark flank is a figure, at a
            // twentieth of the brightness a flat shape needs. So the two that
            // failed now stand where the moon can graze them, and they are still
            // not in the moonward wedge itself: the moon is at 40 deg and they sit
            // at 96 and 138, i.e. lit across the shoulder, not from in front.
            //
            // NOTHING HERE IS A BILLBOARD and nothing re-orients with the head:
            // the forms face the CLEARING (the board), which is world-fixed and is
            // roughly where the player is anyway. See the VR SAFETY block in
            // EnvHaunt.shader.
            {
                // (THE FACE'S TRUNK SEARCH is gone with the face. It picked a
                // tree by radius so the wood could be re-seeded without silently
                // moving the head into the open, and measured the travel off that
                // tree's own bark. There is nothing left to hide behind it.)

                Vector3 OnGround(float bearingDeg, float r, float up)
                {
                    float x = Mathf.Sin(bearingDeg * Mathf.Deg2Rad) * r;
                    float z = Mathf.Cos(bearingDeg * Mathf.Deg2Rad) * r;
                    return new Vector3(x, ForestY(x, z) + up, z);
                }
                Vector3 ToClearing(Vector3 p) => new Vector3(-p.x, 0f, -p.z).normalized;

                var watcherAt = OnGround(162f, 16.5f, 0f);
                var crossAt = OnGround(190f, 13.0f, 0f);
                // 0.75 m and 9.0 m on bearing 288: the old placement was measured
                // against nothing and the preview showed the result —
                // env_swamp_HauntEyes was a picture of two BOULDERS, with the
                // apparition entirely behind them. Half occluded is the goal;
                // entirely occluded is a slot in which nothing happens.
                var eyesAt = OnGround(288f, 8.2f, 0.98f);

                // ============================================================
                // HAUNT FORCE ID TABLE — FOREST. Same channel and same rules as
                // the cellar's (see EnvHaunt.cginc, _GhvrHauntForce); the id is
                // the index in the array below.
                //   0  Eyes     two eyeshines open low in the understory and blink
                //               out of step with each other. The only thing this
                //               room still DRAWS.
                //   1  Watcher  draws nothing here: one of the GAME'S OWN monsters
                //               stands between the trunks and does nothing at all
                //               (HauntFigures card 2, renumbered 2 -> 1).
                //   2  Cross    draws nothing here: a game monster walks across a
                //               gap (HauntFigures card 3, renumbered 3 -> 2).
                //
                // THREE, not six, and the wood lost more than the cellar did. The
                // head behind the trunk, the 3.05 m hunched mass and the BODY
                // HANGED BY THE NECK were all imported figures and all three are
                // retired outright — there is no monster in the game's roster that
                // can be posed leaning out from behind a tree, hunched over, or
                // hanging from a rope. The hanged body is the loss worth naming:
                // the user asked for it specifically ("dass man jemanden/eine
                // Silhouette erkennt von jemandem der sich erhängt hat an einem
                // Baum") and it cannot be rebuilt from an Animator that has no
                // hang animation in it.
                // ============================================================
                //
                // THE LIGHT IS THE MOON, on MoonDir, and every key colour below is
                // cold and dim because that is the only light this wood has. The
                // builder bakes key·N per vertex, so what these forms contribute is
                // one cold raked side and a dark flank — see the HAUNTS block.
                var moon = EnvironmentsBuilder.MoonDir;
                var cards = new[]
                {
                    // (THE FACE that came out from behind a trunk was card 0
                    // and is RETIRED. "eine lächelnde fratze die hinter einem Baum
                    // hervorguckt" was the user's own idea and it went through a 2D
                    // version, a baked-atlas version and an imported-head version
                    // before this one: "mir gefallen die Figuren und animationen
                    // gar nicht". Nothing in the game's roster can lean a head out
                    // from behind a trunk, so this event is gone rather than
                    // hollowed out.)
                    // [0] THE EYES. The wood already has a pair that never move
                    // (Glow EyeL/EyeR, bearing 135 deg) and that is the point of
                    // THOSE. These are the opposite: they open low in the
                    // undergrowth, hold, blink and are gone.
                    //
                    // NOTHING ABOUT THE PAIR MATCHES: they are two ellipsoids of
                    // different size at different heights, and `shape` lags the
                    // second blink behind the first by 0.09 of the event, so they
                    // do not close together. Two eyes that blink together belong to
                    // a face; two that do not belong to something that is not built
                    // like one.
                    new HauntCard
                    {
                        name = "Eyes", kind = HKindEyes,
                        at = eyesAt, facing = ToClearing(eyesAt), height = 0.20f,
                        // 0.98 m and 8.2 m, not 0.75 m and 9.0: at 0.75 the pair sat
                        // behind the boulder group on that bearing and the preview
                        // was a picture of a rock.
                        shape = 0.09f,
                        // 5.4 s and not 3.0. THREE SECONDS IS TOO SHORT TO
                        // PHOTOGRAPH and, it turns out, too short to see: the
                        // preview solves the shipped schedule for a given fraction
                        // of the run, and on a three-second event the frames at
                        // 0.55 and 0.88 came back empty while the one at 0.25
                        // showed both beads perfectly. Whatever the residual offset
                        // is (the editor's own _Time.y rides on top of the
                        // harness's offset and it is not zero), an event shorter
                        // than that slack is one nobody can aim a camera at — and
                        // an eyeshine that opens and is gone inside three seconds
                        // is also the easiest thing in this catalogue to miss.
                        reveal = 1.6f, hold = 3.0f, fade = 0.8f,
                        // the eyes stay eight times brighter than everything else here,
                        // and that is not an oversight: an eyeshine IS a bright
                        // thing, and at 9 m a 26 mm bead covers about three
                        // pixels. Dim it to a body's value and there is nothing
                        // left to see at all.
                        key = new Color(0.170f, 0.165f, 0.087f), keyDir = moon,
                        fillAmt = 0.50f, opacity = 0.93f, rim = 0.0f,
                        why = "0.75 m off the ground at 9 m, in the understory; two blinks, out of step",
                    },
                    // [1] THE WATCHER — A SCHEDULE PLACEHOLDER. "Auch die
                    // Beobachter Idee ist gut aber auch ein 2D Pappaufsteller,
                    // lieber wirklich eine Horrorgestalt die einfach da steht." He
                    // got one, built from the imported figure, and then ruled on
                    // the whole family: "mir gefallen die Figuren und animationen
                    // gar nicht". The figure is deleted; the EVENT survives,
                    // because the game itself has monsters that stand still and
                    // this is the one apparition whose entire content is that it
                    // does nothing (HauntFigures.Events.cs, ForestWatcher).
                    //
                    // The envelope is unchanged, 3.5 / 5.0 / 0 — a slow arrival
                    // nobody catches, five seconds of stillness, and gone between
                    // two frames. It already matches the runtime event exactly.
                    new HauntCard
                    {
                        name = "Watcher", kind = HKindNone,
                        at = watcherAt, facing = ToClearing(watcherAt), height = 0.05f,
                        reveal = 3.5f, hold = 5.0f, fade = 0f,
                        key = Color.black, keyDir = moon, opacity = 0f,
                        why = "invisible here; a real game monster stands between the trunks (HauntFigures card 2 -> 1)",
                    },
                    // [2] SOMETHING CROSSES — A SCHEDULE PLACEHOLDER. "Genauso
                    // das 'etwas huscht herbei'". The frozen mid-stride body is
                    // deleted with the rest; what crosses the gap now is a game
                    // monster with the game's own walk on it, which is strictly
                    // better for this one event — a real walk cycle is the thing a
                    // frozen pose was standing in for.
                    //
                    // THE ENVELOPE GREW A LOT: 1.00 / 2.40 / 0.80 = 4.2 s, where
                    // the frozen figure crossed in 0.34 s. A pose sliding sideways
                    // can be done in a third of a second; a body actually walking
                    // four metres cannot, and a slot shorter than the walk would cut
                    // it off in the open. This was the fastest event in either room
                    // and it no longer is.
                    new HauntCard
                    {
                        name = "Cross", kind = HKindNone,
                        at = crossAt, facing = ToClearing(crossAt), height = 0.05f,
                        reveal = 1.00f, hold = 2.40f, fade = 0.80f,
                        key = Color.black, keyDir = moon, opacity = 0f,
                        why = "invisible here; a real game monster walks across the gap (HauntFigures card 3 -> 2)",
                    },
                    // (THE MASS was card 4 and THE HANGED BODY was card 5. Both
                    // are RETIRED with the rest of the imported figures, and both
                    // are losses with nothing to replace them:
                    //
                    //  * the MASS was a 3.05 m hunched body standing at 10.5 m with
                    //    its back turned, its whole horror being that its
                    //    proportions were a person's and its size was not. A game
                    //    monster is the size the game made it, so the one thing
                    //    this event was about cannot be expressed by one.
                    //  * the HANGED BODY is the one the user described in the most
                    //    detail and asked for by name: "auch die Idee hier ist gut,
                    //    dass man jemanden/eine Silhouette erkennt von jemandem der
                    //    sich erhängt hat an einem Baum." It hung by the neck from
                    //    a branch 2.86 m up, on a real rope, turning slowly. There
                    //    is no hang animation anywhere in the game's roster and
                    //    nothing to pose against a rope, so this is simply gone.
                    //    It is the single biggest thing lost to the figure removal
                    //    and the user needs to be told rather than to notice.)
                };
                // (THE TRUNK-CLEARANCE GATE is gone with the bodies it protected.
                // It measured every apparition's distance to the nearest trunk axis
                // and failed the build at under 0.55 m, because a 1.05 m-wide
                // shoulder standing inside a tree is a slot in which nothing
                // happens. Nothing in this catalogue has a body any more: the two
                // placeholders draw nothing at all, and where the game's own
                // monsters really stand is decided at RUNTIME by HauntFigures
                // against the live scene — which is the right place for it and not
                // something a bake can gate. The eyeshines are 3 cm beads that are
                // meant to be half-occluded.)

                BuildHaunts(root, "Forest", "Env_S_Haunt", cards,
                            new Color(0.045f, 0.058f, 0.090f, 1f),
                            ForestPlaySpaceDia, FR, FR, 12f,
                            (h, cs, i, piece) =>
                            {
                                var c = cs[i];
                                if (c.name == "Eyes")
                                {
                                    // two eyeshines, and NOTHING about the pair
                                    // matches: 26 mm against 19 mm, 6 cm apart, one
                                    // 4 cm higher than the other, and the second one
                                    // blinks late. The sign of the pivot lane is
                                    // what tells the shader WHICH eye a vertex
                                    // belongs to — it is free here, because an
                                    // eyeshine does not rotate about anything.
                                    var left = new Acc();
                                    var right = new Acc();
                                    var side = Vector3.Cross(Vector3.up, c.facing.normalized).normalized;
                                    // 38 mm and 28 mm, up from 26 and 19: a 26 mm
                                    // bead at 9 m subtends 0.17 deg, which is three
                                    // pixels on the headset and nothing at all in a
                                    // preview. An eyeshine can be large — it is a
                                    // reflection off a tapetum, not an eyeball.
                                    AddHauntBlob(left, c.at - side * 0.075f, 0.038f, 0.029f, 10);
                                    AddHauntBlob(right, c.at + side * 0.065f + Vector3.up * 0.048f,
                                                 0.028f, 0.021f, 10);
                                    AssertClosedAndOutward(left, "Forest haunt 'Eyes' (left)");
                                    AssertClosedAndOutward(right, "Forest haunt 'Eyes' (right)");
                                    HauntWeld(h, left, c, i, HKindEyes, c.key, Color.black,
                                              Vector3.up, 0f, Vector3.up, 0f, -1f, 1f, 0f, -1f);
                                    HauntWeld(h, right, c, i, HKindEyes, c.key * 0.82f, Color.black,
                                              Vector3.up, 0f, Vector3.up, 0f, +1f, 1f, 0f, -1f);
                                }
                            });
            }

            // ELEMENT ART — Earth is a PIGMENT, not a light: the green goes on
            // the things in this wood that could plausibly be damp and growing,
            // and on nothing else. The mossy rock sets and the roots carry it in
            // full, the deadfall and the stumps a little (old wood goes green
            // before a live trunk does), the standing trunks a third of it at
            // most. A barrel that turns green is a bug; a root that does not is
            // a missed element.
            //
            // The KNOWN GAP this comment used to record is PAID: the moss cards,
            // the ferns, the grass, the canopy and the FLOOR all answer the
            // elements now (EnvRoomCutout and EnvGround grew the growth block
            // this round). The susceptibilities below are coverage fractions and
            // no longer tint opacities — see SURFACE GROWTH — so the numbers
            // moved even where the intent did not.
            Material MatOf(string node)
            {
                var t = root.Find(node);
                var mr = t == null ? null : t.GetComponent<MeshRenderer>();
                return mr == null ? null : mr.sharedMaterial;
            }
            void ElemMoss(string node, float amount)
            {
                var mat = MatOf(node);
                if (mat != null && mat.HasProperty("_ElemMoss")) mat.SetFloat("_ElemMoss", amount);
            }
            foreach (var n in new[] { "Rocks0", "Rocks1", "Rocks2", "Root2", "Root3" })
                ElemMoss(n, 1.4f);
            foreach (var n in new[] { "Log0", "Log1", "LeanTree", "Stump0", "Stump1", "Branches0", "Branches1" })
                ElemMoss(n, 1.1f);
            // The TRUNKS, and this is the sentence "auch im Wald das die Stämme
            // teilweise ... bewachsen" in one number. 1.6 rather than the rocks'
            // 1.4 because the periphery ramp is weakest exactly where the trunks
            // are: the first band stands at 6-10 m of a 12 m element radius, so
            // its rim value is only ~0.37 and the coverage ramp (0.10 + 1.90*rim)
            // gives it 0.8 of the frontier's travel. The affinity's `foot` term
            // is what keeps it TEILWEISE — it dies out 1.25 m up, so what grows
            // is the base and the lower bark and never the crown.
            foreach (var n in new[] { "TrunksNear", "TrunksFar" })
                ElemMoss(n, 1.6f);

            // ================================================ SURFACE GROWTH ==
            // THE FLOOR. "Frost auf dem Boden" and "der Boden mit Moos bzw. Gras
            // bewachsen" are both about this one surface, which until now was the
            // only major surface in either room outside the element channel.
            // Frost takes it readily (0.9: a clearing floor under an open sky is
            // where frost forms first); moss a little less, because the floor's
            // own damp map — the mud/litter blend in its vertex alpha — is doing
            // most of the choosing (EnvGround.shader, `place`).
            gm.SetFloat("_ElemFrost", 0.9f);
            gm.SetFloat("_ElemMoss", 1.0f);

            // THE FOLIAGE. Air is the reason this block exists: "Bei der Luft
            // bzw Wind möchte ich das die Blätter der Bäume wackeln!"
            //
            // The wind blows ACROSS the moon bearing rather than along it, and
            // that is a looking decision: the moonbeams and the shafts run along
            // that bearing, so leaves crossing them are seen against the one lit
            // thing in the wood. Along it they would move up and down a beam and
            // read as nothing.
            var windDir = new Vector3(-moonHoriz.z, 0f, moonHoriz.x).normalized;
            void ElemFoliage(string node, float frost, float moss, float windAmp)
            {
                var t = root.Find(node);
                var mr = t == null ? null : t.GetComponent<MeshRenderer>();
                var mat = mr == null ? null : mr.sharedMaterial;
                var mf = t == null ? null : t.GetComponent<MeshFilter>();
                if (mat == null || mf == null || mf.sharedMesh == null) return;
                if (mat.HasProperty("_ElemFrost")) mat.SetFloat("_ElemFrost", frost);
                if (mat.HasProperty("_ElemMoss")) mat.SetFloat("_ElemMoss", moss);
                if (windAmp > 0f)
                    ElemWind(mat, t, mf.sharedMesh, windAmp, windDir, vertexAlpha: false, what: node);
            }
            // The canopy is ONE welded mesh at the identity transform, and its
            // cards carry the freedom in vertex ALPHA (AddCard) — so it is the
            // one material that takes the alpha path. 4.5 cm: see ElemWind for
            // the argument against the canopy shadow map's 5.5 cm texel.
            {
                var cf = root.Find("Canopy");
                var cm = cf == null ? null : cf.GetComponent<MeshFilter>();
                if (cm != null && cm.sharedMesh != null)
                {
                    ElemWind(foliage, cf, cm.sharedMesh, 0.045f, windDir,
                             vertexAlpha: true, what: "Canopy");
                    // needles frost at the tips; they do not grow moss on
                    // themselves — the moss in this wood is on what the needles
                    // fall ONTO.
                    foliage.SetFloat("_ElemFrost", 0.85f);
                }
            }
            // The understory is NOT in the canopy shadow bake (only the trunks
            // and the crowns are), so it gets a real breeze rather than a
            // budgeted one: 8 cm at the tip of a metre-tall fern is a light wind,
            // and its roots are pinned by the height weight.
            foreach (var n in new[] { "Fern0", "Fern1", "Fern2" }) ElemFoliage(n, 0.9f, 0.9f, 0.080f);
            foreach (var n in new[] { "Grass0", "Grass1" }) ElemFoliage(n, 0.9f, 0.7f, 0.090f);
            foreach (var n in new[] { "Shrub0", "Shrub1", "Shrub2" }) ElemFoliage(n, 0.9f, 0.8f, 0.065f);
            // the hanging/ground moss cards are already moss: they frost, and
            // they barely move (a moss cushion is not a frond)
            foreach (var n in new[] { "Moss0", "Moss1", "Moss2", "Moss3" }) ElemFoliage(n, 1.0f, 0f, 0.020f);

            // THE GRASS AND MOSS THAT ARE NOT THERE YET. A ring of cards outside
            // the play space, folded flat until Earth brings them up. Grass on
            // the open ground and moss at the damp edges, in two meshes because
            // they are two atlases — two draw calls, ~4k vertices, and not one
            // fragment until the element rises.
            {
                const float span = 0.65f;
                var grass = new Acc();
                var mossA = new Acc();
                int nG = 0, nM = 0, refused = 0;
                // 1500, where ModBuild 143 drew 900. USER VERDICT: "auch gerne
                // noch mehr Gräser auf dem Boden (die eventuell auch mit dem
                // Wind agieren falls das auch aktiv ist)". Both halves are
                // answered here: the count, and the wind — the growth cards go
                // through the SAME _ElemWind path the ferns do (see GrowthMat
                // below), so a tuft that has come up leans in the same gust as
                // the fern beside it, and the parenthesis "falls das auch aktiv
                // ist" is now always true, because the breeze is permanent.
                for (int i = 0; i < 1500; i++)
                {
                    float u = Hash3(i, 0, 0, 8401), w = Hash3(i, 1, 0, 8401);
                    // area-uniform over an annulus that starts OUTSIDE the 9 m
                    // play space (4.5 m) with a hand's margin, and stops where
                    // GroundColor's own fade has taken the floor to a few percent
                    float r = Mathf.Sqrt(Mathf.Lerp(4.9f * 4.9f, 11.5f * 11.5f, u));
                    float ang = w * Mathf.PI * 2f;
                    float x = Mathf.Sin(ang) * r, z = Mathf.Cos(ang) * r;
                    // the trodden path is trodden: nothing grows in it
                    if (PathDist(x, z) < 0.95f) { refused++; continue; }
                    if (GrowthBlocked(x, z, 0.05f)) { refused++; continue; }
                    // patchy: a meadow is patches, not a lawn. The floor is 0.20
                    // -> 0.30 with the count: more grass has to mean more grass
                    // in the thin places too, or 1500 samples through the same
                    // mask just thickens the tufts that were already there and
                    // the clearing still reads as bare between them.
                    float dens = 0.30f + 0.80f * Fbm2(x * 0.33f + 17f, z * 0.33f, 2, 8407);
                    if (Hash3(i, 2, 0, 8401) > dens) continue;
                    var p = new Vector3(x, ForestY(x, z), z);
                    // The same darkness fade every other prop in this room wears,
                    // but it does NOT bottom out as low: at the props' 0.04 floor
                    // the first render of these cards was a scatter of black
                    // scratches you could only find in a diff. 0.22, i.e. the
                    // grass at the tree line recedes without disappearing.
                    float lit = Mathf.SmoothStep(1f, 0.22f, Mathf.InverseLerp(5.5f, 12f, r));
                    var tint = Grey(lit * (0.86f + 0.30f * Hash3(i, 3, 0, 8401)));
                    // moss at the damp edge of the clearing and in under the
                    // trees, grass where the moon still reaches
                    bool isMoss = Hash3(i, 4, 0, 8401) < Mathf.InverseLerp(5.5f, 10.5f, r) * 0.75f;
                    if (isMoss)
                    {
                        AddGrowthClump(mossA, p, 0.11f + 0.18f * Hash3(i, 5, 0, 8401), 4,
                                       MossCards, 8500 + i, span, tint);
                        nM++;
                    }
                    else
                    {
                        // 0.30-0.64 m: a tuft you can see over the litter from
                        // standing height, which a 0.26 m one at eight metres is
                        // not. Four cards, not three — three plumb cards leave a
                        // yaw from which a tuft is two crossed lines.
                        AddGrowthClump(grass, p, 0.30f + 0.34f * Hash3(i, 5, 0, 8401), 4,
                                       GrassCards, 8500 + i, span, tint);
                        nG++;
                    }
                }
                Material GrowthMat(string file, string tex, Color tint, float frost, float wind)
                {
                    var m = NewRoomMat(file, "GloomhavenVR/EnvRoomCutout");
                    m.SetTexture("_MainTex", Imp(tex));
                    m.SetFloat("_Cutoff", 0.35f);
                    m.SetFloat("_VCol", 1f);
                    m.SetColor("_Tint", tint);
                    m.SetFloat("_ElemGrow", 1.0f);
                    m.SetFloat("_ElemFrost", frost);
                    // w = 1: the fold weight and the wind weight are both the
                    // vertex alpha AddGrowthCard wrote (height / span).
                    m.SetVector("_ElemWind", new Vector4(wind, 0f, 0f, 1f));
                    m.SetVector("_ElemWindDir", new Vector4(windDir.x, windDir.y, windDir.z, span));
                    return m;
                }
                void PlaceGrowth(string node, string asset, Acc acc, Material m)
                {
                    var mesh = SaveMesh(asset, acc.Build(Path.GetFileNameWithoutExtension(asset)));
                    var go = Place(root, node, mesh, Vector3.zero, Vector3.zero, Vector3.one, m);
                    Defer(m, go.transform, 1f);
                }
                // night grass is grey-green, not the meadow green of the daylight
                // photoscan — the same correction the bark and the needles got
                // 0.45/0.52/0.38 against the needles' 0.21/0.25/0.20: grass
                // catches the moon where a fir needle absorbs it, and this is
                // the one new thing in the room that has to be FOUND rather than
                // merely not look wrong.
                // 8.5 cm, up from 7.0: the tufts stand 30-64 cm and the ferns
                // beside them get 8.0 at a metre, so at 7.0 the grass was the
                // one thing in the clearing that moved LESS than the plant next
                // to it. The moss cushions stay at 2.5 cm — a cushion is not a
                // frond, and a moss that swayed would undo the whole point of
                // the round.
                PlaceGrowth("GrowthGrass", "Env_S_GrowthGrass.asset", grass,
                            GrowthMat("S_GrowthGrass.mat", "grass_medium_02_alb",
                                      new Color(0.36f, 0.42f, 0.30f), 0.9f, 0.085f));
                PlaceGrowth("GrowthMoss", "Env_S_GrowthMoss.asset", mossA,
                            GrowthMat("S_GrowthMoss.mat", "moss_01_alb",
                                      new Color(0.29f, 0.35f, 0.24f), 1.0f, 0.025f));
                Debug.Log($"[GloomhavenVR][Env] Forest growth: {nG} grass clumps "
                          + $"({grass.Count / 4} cards) and {nM} moss clumps ({mossA.Count / 4} cards), "
                          + $"{grass.Count + mossA.Count} verts total, {refused} refused for the path "
                          + $"or a prop; ring 4.90-11.50 m, card span {span:F2} m. "
                          + "With Earth down every card has zero area and costs no fragment, "
                          + "and the wind weight is folded by the SAME grow factor, so a card "
                          + "that has not come up gets an offset of exactly zero.");
            }

            // ELEMENT ART — the gated emitters (embers, snow, the gust, spores).
            AddElementFX(root, cellar: false);
            // FIRE REAL — the parts of the wood that catch while Fire is up.
            AddForestFire(root, rig, trees);

            PaintContactAO(g, 0.45f, 0.55f);
            FlushRig(rig);
            // SURFACE GROWTH — the coverage table. The forest floor's `place` is
            // EnvGround's, which needs two terms this side cannot read back: the
            // mud/litter blend (it IS in the mesh, so it is used) and the canopy
            // visibility (baked into a texture, so it is ESTIMATED from the
            // radius here and labelled as such).
            Func<Vector3, Vector3, float> PlaceGround(bool moss) => (p, nn) =>
            {
                float r = new Vector2(p.x, p.z).magnitude;
                float vis = Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(ClearR, ClearR + 4f, r));
                float rim = GrowRim(r, rig.elemRad);
                float blend = 0.55f;      // GroundColor's litter mean over the disc
                return moss ? Mathf.Clamp01(0.55f * blend + 0.25f * (1f - vis) + 0.20f * rim)
                            : Mathf.Clamp01(0.50f * vis + 0.30f * (1f - blend) + 0.20f * rim);
            };
            ReportGrowth("Forest", "Ground", g, "earth moss (canopy visibility estimated)",
                         1.0f, 0.16f, 1.60f, rig.elemRad, 0.40f, PlaceGround(moss: true));
            ReportGrowth("Forest", "Ground", g, "ice frost (canopy visibility estimated)",
                         0.9f, 0.34f, 1.05f, rig.elemRad, 0.40f, PlaceGround(moss: false));
            {
                var tn = root.Find("TrunksNear");
                if (tn != null)
                    ReportGrowth("Forest", "TrunksNear", tn.gameObject, "earth moss", 1.6f,
                                 0.10f, 1.90f, rig.elemRad, 0.50f, PlaceRoom(moss: true));
            }
            ReportGrounding("Forest");
            // 'Ground' is the floor the board stands on and 'MoonShafts' are
            // light, not matter — everything else must stay outside the clearing.
            AssertPlaySpaceClear(root, "Forest", ForestPlaySpaceDia, "Ground", "MoonShafts");
            Debug.Log("[GloomhavenVR][Env] Night-forest room geometry assembled.");
        }

        // ================================================== SURFACE GROWTH ===
        // The BAKE half of the frost/moss/wind round. The per-pixel mechanism and
        // the whole argument live in the bundle's EnvGrowth.cginc; what is here is
        //   * WHICH surfaces grow and how strongly (the susceptibilities),
        //   * the GRASS AND MOSS THAT ARE NOT THERE YET — real cards, folded flat
        //     onto their own base edge until Earth brings them up,
        //   * the wind's amplitudes, chosen against the canopy shadow map,
        //   * and the numbers a reader needs to check all of that without opening
        //     Unity (ReportGrowth).
        //
        // USER FINDING, ModBuild 142 (hardware): "Bei Erde möchte das Wände und
        // Böden teilweise mit Moos bewachsen - auch im Wald das die Stämme
        // teilweise und der Boden mit Moos bzw. Gras bewachsen wird."
        //
        // The shader can turn a wall green where moss would be. It cannot make
        // grass STAND UP out of a floor, and "der Boden mit Moos bzw. Gras
        // bewachsen" is a request for something with a silhouette. So the two
        // rooms get a growth mesh each: cards that exist in the asset from the
        // first build and have ZERO AREA until the element reaches them.
        //
        // WHY NOT NEW GEOMETRY ON DEMAND: there is no runtime code in these
        // rooms at all (script-free prefabs, permanent ruling), so anything that
        // appears has to be geometry that was always there. A folded card costs
        // its vertices and not one fragment, which is the cheapest form of
        // "not there yet" this engine has.
        //
        // WHY THE CARDS MUST BE PLUMB: the fold is `p.y -= span * alpha * (1-g)`,
        // i.e. straight down. A leaning card folded straight down lands as a
        // horizontal SLIVER with real area, and a sliver rasterises fragments —
        // the zero state would leak a few hundred lit pixels per room. Vertical
        // cards fold onto their own base edge exactly, so the quad's area is
        // exactly zero and the rasteriser produces nothing. The lean a tuft of
        // grass needs comes from the yaw and height spread instead.

        // Sub-rects of Imported/Textures/*_alb.png, found the way the fir twig
        // atlas's Sprigs were: connected-component analysis of the alpha channel.
        // Both are Poly Haven CARD plants rather than solid photoscans, so their
        // textures are already atlases of isolated cutouts on clean transparency
        // — which is exactly what a growth card wants and is the reason these two
        // assets were chosen over inventing a sprite sheet.
        //
        // Each grass rect is a PAIR of blade clusters, not one blade: a single
        // cluster is 4 cm wide on a 40 cm card and reads as a scratch at six
        // metres, which is where most of these are.
        private static readonly Rect[] GrassCards =
        {
            new Rect(0.0312f, 0.0273f, 0.2734f, 0.9492f),
            new Rect(0.4512f, 0.1055f, 0.3848f, 0.8203f),
            new Rect(0.6279f, 0.1055f, 0.3516f, 0.8711f),
        };
        // moss_01's nine cushions, the five that read as a patch of moss rather
        // than as a torn leaf.
        private static readonly Rect[] MossCards =
        {
            new Rect(0.7051f, 0.1152f, 0.1641f, 0.2402f),
            new Rect(0.2637f, 0.1094f, 0.1641f, 0.2383f),
            new Rect(0.0566f, 0.1621f, 0.1504f, 0.1934f),
            new Rect(0.8281f, 0.6582f, 0.1094f, 0.1699f),
            new Rect(0.6309f, 0.5371f, 0.1543f, 0.3027f),
        };

        // ---------------------------------------- the C# mirror of EnvGrowth
        // Character for character the shader's, and it exists for one reason:
        // "teilweise" is a number and the bake has to be able to PRINT it. The
        // same reasoning, and the same standing risk, as the haunt schedule's
        // mirror further up — if one side is edited the other must be.
        private const float GrowFull = 0.60f;      // GHVR_GROW_FULL
        private const float GrowEdge = 0.06f;      // GHVR_GROW_EDGE

        private static float GFrac(float v) => v - Mathf.Floor(v);

        private static float GrowHash(Vector3 p)
        {
            p = new Vector3(GFrac(p.x * 0.1031f), GFrac(p.y * 0.1031f), GFrac(p.z * 0.1031f));
            float d = p.x * (p.z + 31.32f) + p.y * (p.y + 31.32f) + p.z * (p.x + 31.32f);
            p = new Vector3(p.x + d, p.y + d, p.z + d);
            return GFrac((p.x + p.y) * p.z);
        }

        private static float GrowNoise(Vector3 p)
        {
            var i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            var f = p - i;
            f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y),
                            f.z * f.z * (3f - 2f * f.z));
            float a = GrowHash(i), b = GrowHash(i + new Vector3(1, 0, 0));
            float c = GrowHash(i + new Vector3(0, 1, 0)), d = GrowHash(i + new Vector3(1, 1, 0));
            float e = GrowHash(i + new Vector3(0, 0, 1)), g = GrowHash(i + new Vector3(1, 0, 1));
            float h = GrowHash(i + new Vector3(0, 1, 1)), k = GrowHash(i + new Vector3(1, 1, 1));
            float lo = Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(e, g, f.x), f.z);
            float hi = Mathf.Lerp(Mathf.Lerp(c, d, f.x), Mathf.Lerp(h, k, f.x), f.z);
            return Mathf.Lerp(lo, hi, f.y);
        }

        private static float GrowField(Vector3 p) => Mathf.Clamp01((GrowNoise(p) - 0.5f) * 2.4f + 0.5f);

        private static float GrowAff(float field, float grain, float place) =>
            Mathf.Clamp01(0.46f * field + 0.18f * grain + 0.36f * place);

        private static float GrowAt(float a, float cover)
        {
            cover = Mathf.Clamp01(cover);
            cover = cover * (2f - cover);          // THE EASE, see GhvrGrow
            float T = Mathf.Lerp(1f + GrowEdge, GrowFull, cover);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((a - (T - GrowEdge)) / (2f * GrowEdge)));
        }

        /// GhvrRim, in C#.
        private static float GrowRim(float r, float rad) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Clamp01(r / Mathf.Max(rad, 0.01f)) - 0.18f) / 0.82f));

        /// <summary>How much of one placed surface a given element actually
        /// covers, at Strong and at the Waning plateau — the "teilweise" the user
        /// asked for, as a measured fraction rather than an intention.
        ///
        /// It samples the object's REAL vertices in room space. The shader
        /// samples the noise in each object's OWN rotated axes (see GhvrGrowQ),
        /// so a given wall's pattern is not the one computed here — but the
        /// distribution of the field is rotation-invariant, so the FRACTION is
        /// exactly comparable, which is all this table claims. `grain` is the one
        /// term that cannot be read back at bake time (it lives in the normal
        /// map), so it is passed as a constant and named in the log; since grain
        /// only ever ADDS affinity, a 0 here is a floor on the real coverage.
        /// </summary>
        private static void ReportGrowth(string room, string what, GameObject go,
            string element, float susceptibility, float rampLo, float rampHi, float elemRad,
            float grain, Func<Vector3, Vector3, float> place)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mesh = mf.sharedMesh;
            var xf = go.transform;
            var v = Verts(mesh);
            var nrm = mesh.normals;
            int step = Mathf.Max(1, v.Length / 6000);
            float freq = 3.0f;                              // _ElemGrowFreq default
            double sS = 0, sW = 0, aSum = 0, aSq = 0, tSum = 0, tCov = 0; int n = 0;
            for (int i = 0; i < v.Length; i += step)
            {
                Vector3 p = xf.TransformPoint(v[i]);
                Vector3 nn = nrm != null && nrm.Length == v.Length
                    ? xf.TransformDirection(nrm[i]).normalized : Vector3.up;
                float rr = GrowRim(new Vector2(p.x, p.z).magnitude, elemRad);
                float field = GrowField(p * freq);
                float a = GrowAff(field, grain, place(p, nn));
                float cover = susceptibility * (rampLo + rampHi * rr);
                float mS = GrowAt(a, cover);
                sS += mS;
                sW += GrowAt(a, cover * 0.40f);
                // MOSS REAL — the CUSHION DEPTH, mirroring GhvrMossThick. It is
                // the number the "grüne Flecken" verdict is really about: a
                // coverage says how much of the wall is green, a thickness says
                // whether any of it is a plant. The micro-relief is taken at its
                // mean (0), which is what the 0.86 stands for; on the real
                // surface it spreads each sample by +-14%.
                float thick = mS * mS * Mathf.Clamp01(0.26f + 0.52f * field + 0.34f * grain) * 0.86f;
                tSum += thick;
                tCov += thick;              // divided by the COVERAGE below
                aSum += a; aSq += a * (double)a; n++;
            }
            if (n == 0) return;
            double mean = aSum / n;
            double sd = System.Math.Sqrt(System.Math.Max(aSq / n - mean * mean, 0));
            Debug.Log($"[GloomhavenVR][Env] {room} growth / {element} on '{what}': "
                      + $"coverage {sS / n * 100.0:F1}% at Strong, {sW / n * 100.0:F1}% at Waning "
                      + $"({n} surface samples; affinity mean {mean:F3} sd {sd:F3}, "
                      + $"grain taken at {grain:F2}; susceptibility {susceptibility:F2}, "
                      + $"ramp {rampLo:F2}+{rampHi:F2}*rim); cushion depth {tSum / n:F3} mean, "
                      + $"{(sS > 0 ? tCov / System.Math.Max(sS, 1e-6) : 0.0):F3} over the covered part "
                      + "(0 = a film, 1 = a cushion).");
        }

        /// The `place` recipe of EnvRoom.shader, mirrored — frost first on what
        /// sees the sky and what the moon never touches, moss first at the foot.
        private static Func<Vector3, Vector3, float> PlaceRoom(bool moss)
        {
            var moon = MoonDir.normalized;
            return (p, nw) =>
            {
                float foot = Mathf.Clamp01(1f - p.y * 0.80f);
                float sky = Mathf.Clamp01(nw.y);
                float shade = 1f - Mathf.Clamp01(Vector3.Dot(nw, moon) * 0.5f + 0.5f);
                return moss ? Mathf.Clamp01(0.52f * foot + 0.28f * shade + 0.20f * sky)
                            : Mathf.Clamp01(0.42f * sky + 0.34f * shade + 0.24f * foot);
            };
        }

        // ------------------------------------------------------------ the wind
        /// <summary>Switch the wind on for one foliage material.
        ///
        /// THE AMPLITUDE IS THE WHOLE DECISION, and it is settled against the
        /// canopy shadow map rather than by eye. That map is baked from the
        /// STATIC canopy at 5.5 x 4.4 cm per texel; the crowns reach the floor
        /// through a 4x4-box-filtered coverage at a seventh strength (CsFol) and
        /// reach the moon shafts through a 0.22 m penumbra. EnvGrowth's wave is
        /// analytically bounded to +-1, so the tip displacement is exactly `amp`
        /// (x1.06 with the flutter): at 4.5 cm a bough tip moves under ONE texel
        /// of the map it cast, and a fifth of the blades' penumbra. Nothing in
        /// either receiver can resolve that, so the shadow and the leaves cannot
        /// visibly disagree — and the number did not have to be found by
        /// rendering, which a still preview could not have done anyway.
        ///
        /// The understory is not in that bake at all (only the trunks and the
        /// canopy are), so it gets a real breeze instead of a budgeted one.
        ///
        /// WHAT CHANGED WITH THE ModBuild 143 VERDICT ("so wie du es gemacht
        /// hast sollte der Normalzustand sein und immer sichtbar"): `ampMetres`
        /// is no longer the amplitude AT FULL AIR, it is the amplitude ALWAYS —
        /// the numbers below are unchanged and what changed is that they now
        /// apply with no element up. Air multiplies them by up to 1.85 and the
        /// bound by up to 2.04 (GhvrWind's THE STORM), so the canopy's worst
        /// case goes from 4.8 cm to 9.2 cm: 1.7 texels of the map, 42% of the
        /// penumbra the only ANIMATED layer of that map is read through, and the
        /// layer with the hard edges — the trunks — is not animated at all.
        /// Both bounds are printed below so the argument can be checked from the
        /// log rather than taken on trust.</summary>
        private static void ElemWind(Material m, Transform xf, Mesh mesh, float ampMetres,
            Vector3 windWorld, bool vertexAlpha, string what)
        {
            float s = (xf.lossyScale.x + xf.lossyScale.y + xf.lossyScale.z) / 3f;
            var b = mesh.bounds;
            float span = Mathf.Max(b.size.y, 1e-3f);
            var dir = xf.InverseTransformDirection(windWorld.normalized).normalized;
            m.SetVector("_ElemWind", new Vector4(ampMetres / Mathf.Max(s, 1e-4f),
                                                 b.min.y, 1f / span, vertexAlpha ? 1f : 0f));
            var wd = m.GetVector("_ElemWindDir");
            m.SetVector("_ElemWindDir", new Vector4(dir.x, dir.y, dir.z, wd.w));
            Debug.Log($"[GloomhavenVR][Env] Wind on '{what}': tip {ampMetres * 100f:F1} cm "
                      + $"ALWAYS (bound 1.06x = {ampMetres * 106f:F1} cm), and "
                      + $"{ampMetres * 185f:F1} cm at full Air (bound 2.04x = "
                      + $"{ampMetres * 204f:F1} cm); weight from "
                      + (vertexAlpha ? "vertex alpha (0 at the stem edge)"
                                     : $"height over y={b.min.y:F2} across {span:F2} object units")
                      + $", object scale {s:F2}, {mesh.vertexCount} verts pay it every frame.");
        }

        // ------------------------------------------------------- the cards
        /// <summary>One plumb growth card standing on `basePt`. Vertex ALPHA is
        /// the height above ITS OWN base divided by the mesh-wide `span`, which is
        /// what lets one shader constant fold cards of a dozen different heights
        /// each exactly onto its own base edge (EnvRoomCutout/_ElemGrow).
        ///
        /// THE CARD'S HEIGHT IS QUANTISED TO THE VERTEX COLOUR, and that is not
        /// tidiness — it is the zero state. Unity's default vertex layout stores
        /// the colour channel as four UNORM BYTES, so an alpha of h/span comes
        /// back out of the shader rounded to the nearest 1/255. Fold by that
        /// rounded value and the card lands up to span/510 — 0.6 mm — off its own
        /// base edge, which at two metres is about half a pixel of sliver: the
        /// FIRST bake of this feature leaked 22 to 32 lit pixels per cellar frame
        /// with every element down, and it took an image diff against the
        /// previous build to see them. Deriving the height FROM the quantised
        /// alpha instead makes the two exact reciprocals of each other, so the
        /// quad's top edge folds onto its bottom edge to within the rounding of
        /// one float add (~60 nm) — far below the rasteriser's own sub-pixel
        /// grid, i.e. no coverage at all, ever.</summary>
        private static void AddGrowthCard(Acc a, Vector3 basePt, Vector3 right, float h,
            Rect uv, Color tint, float span)
        {
            float w = Mathf.Round(h / Mathf.Max(span, 1e-4f) * 255f) / 255f;
            h = span * w;
            var top = new Vector3(0f, h, 0f);
            var c0 = new Color(tint.r, tint.g, tint.b, 0f);
            var c1 = new Color(tint.r, tint.g, tint.b, w);
            var nrm = Vector3.Cross(Vector3.up, right).normalized;
            int b = a.Count;
            a.Vert(basePt - right, nrm, new Vector2(uv.xMin, uv.yMin), c0);
            a.Vert(basePt + right, nrm, new Vector2(uv.xMax, uv.yMin), c0);
            a.Vert(basePt + right + top, nrm, new Vector2(uv.xMax, uv.yMax), c1);
            a.Vert(basePt - right + top, nrm, new Vector2(uv.xMin, uv.yMax), c1);
            a.Quad(b);
        }

        /// <summary>A clump: `n` cards on one spot, fanned in yaw so it holds up
        /// from every direction (never camera-facing — permanent ruling).</summary>
        private static void AddGrowthClump(Acc a, Vector3 basePt, float h, int n,
            Rect[] atlas, int seed, float span, Color tint)
        {
            for (int k = 0; k < n; k++)
            {
                var rect = atlas[(int)(Hash3(seed, k, 1, 8101) * atlas.Length) % atlas.Length];
                float hh = h * (0.70f + 0.60f * Hash3(seed, k, 2, 8101));
                float halfW = hh * 0.5f * (rect.width / Mathf.Max(rect.height, 1e-3f));
                float ang = (k / (float)n + 0.42f * Hash3(seed, k, 3, 8101)) * Mathf.PI;
                var right = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * halfW;
                // the cards of one clump do not share a point: a tuft is a
                // handful of blades a few centimetres apart, and stacking them on
                // one axis is what makes a fan read as a paper windmill
                var off = new Vector3((Hash3(seed, k, 4, 8101) - 0.5f) * h * 0.35f, 0f,
                                      (Hash3(seed, k, 5, 8101) - 0.5f) * h * 0.35f);
                AddGrowthCard(a, basePt + off, right, hh, rect, tint, span);
            }
        }

        // ==================================================== BRACKET FUNGI ====
        // USER VERDICT, ModBuild 146, and it is the THIRD time he has rejected
        // this surface: "Das Moos gefällt mir immer noch nicht insbesondere nicht
        // im Keller - es sieht eher aus wie Schleim, es soll eher aussehen wie
        // wuchende Pflanzen und Pilze die an den Wänden wachsen."
        //
        // WHY IT KEEPS READING AS SLIME, and why the answer is geometry.
        // A shading lane can give a wall a green colour, a micro-relief, a wet
        // sheen, a lip and an occlusion crease, and it has — all of them are
        // properties of a FLAT WALL. "Schleim" is precisely what a flat wall with
        // a green property on it looks like, because slime is the one growth that
        // has no shape of its own. Two rounds have now been spent making the film
        // better and the verdict has not moved. What is missing is a SILHOUETTE,
        // and the growth cards this room already has cannot supply the right one:
        // AddGrowthCard builds `top = (0, h, 0)`, i.e. every card in both rooms is
        // PLUMB. A plumb card on a wall is a tuft of something standing up
        // against the stone. It is not what grows on a cellar wall.
        //
        // WHAT DOES: BRACKET FUNGI. A polypore is a HORIZONTAL SHELF cantilevered
        // out of a vertical surface — the one silhouette that breaks a wall's
        // plane at right angles, that casts a shadow downward onto the wall it
        // grows from, and that no amount of shading can fake, because it is
        // literally somewhere the wall is not. That shape does not exist anywhere
        // in this bundle today, and its absence is most of why the cellar reads
        // as a painted-on film.
        //
        // AND THE BIOLOGY SETTLES THE ARGUMENT RATHER THAN JUST DECORATING IT.
        // MOSS IS A PLANT AND PLANTS NEED LIGHT. A cellar lit by three candles
        // and a shaft of moonlight cannot grow moss on its walls at all; what
        // grows in a dark, cold, permanently damp masonry cellar is FUNGUS —
        // saprotrophic, needs no light, feeds on the timber and the mortar, and
        // fruits as brackets on anything vertical. So "es sieht eher aus wie
        // Schleim" and "insbesondere nicht im Keller" are the same observation:
        // the cellar had the WOOD'S growth on its walls. The wood keeps its moss
        // (it has a sky), the cellar gets fungi, and the shading lane's separate
        // indoor/outdoor biology lands on exactly the same split from the other
        // side.
        //
        // IT IS NOT ELEMENT-GATED, and that is a decision with a precedent. The
        // existing wall cushions carry _ElemGrow = 1, so they fold flat onto
        // their own base edges and DO NOT EXIST unless Earth is up — which means
        // the cellar the user has been looking at has had only the film on it,
        // almost always. USER RULING, ModBuild 143, on exactly this question for
        // the wind: "so wie du es gemacht hast sollte der Normalzustand sein und
        // immer sichtbar." Fungus in a damp cellar is a property of the room, not
        // of an infusion — like the cobwebs, the rat and the drip. Earth still
        // does its thing on the same walls (the shader's frontier, and the moss
        // cushions, both unchanged); the fungi are simply always there.
        //
        // WHY CARDS AND NOT MODELLED CAPS. A modelled bracket is ~120 tris for a
        // shape whose whole content is its outline, and this room already pays
        // for 10 106 tris of bookshelf. Two crossed alpha-tested cards give the
        // same outline from every direction a player can be in — see
        // AddFungusBracket for the geometric argument about which two.

        /// <summary>The sub-rects of fungus_alb.png. Filled in from the atlas the
        /// texture pipeline packs (see License.md for the CC0 sources), U running
        /// AWAY FROM THE WALL and V running up, so one rect serves both of a
        /// bracket's cards without being re-authored per orientation.
        ///
        /// <para>THAT IS A PROPERTY OF BRACKETS AND NOT A SHORTCUT: a polypore's
        /// side elevation and its plan are the same shape — a half disc attached
        /// along its straight edge — so the profile cutout is also a correct plan
        /// cutout, which is why two cards can share one rect and why the atlas
        /// only has to be keyed once.</para></summary>
        /// <remarks>Eight cutouts, keyed from four CC0 1.0 photographs (see
        /// License.md's "Wall fungus" section for the Commons files, the authors
        /// and the licence line verified against the Commons API). Every rect is
        /// the cutout's exact alpha bounding box, so a card built on one has no
        /// transparent margin to waste fill on, and every one is pre-oriented:
        /// the rect's LEFT edge is the straight-shaved attachment edge, so +U is
        /// always "away from the wall" and no card ever has to be mirrored.
        /// Mirroring in U would push the bracket INTO the masonry.</remarks>
        private static readonly Rect[] FungusCards =
        {
            new Rect(0.345703f, 0.090820f, 0.255859f, 0.275391f),  // 0 tinder hoof, side profile — the hero
            new Rect(0.009766f, 0.214844f, 0.316406f, 0.419922f),  // 1 porling, a deep wedge
            new Rect(0.465820f, 0.693359f, 0.415039f, 0.296875f),  // 2 tiered cluster, upper
            new Rect(0.465820f, 0.385742f, 0.414062f, 0.288086f),  // 3 tiered cluster, lower
            new Rect(0.009766f, 0.654297f, 0.436523f, 0.335938f),  // 4 fat round shelf
            new Rect(0.621094f, 0.253906f, 0.369141f, 0.112305f),  // 5 long thin ledge
            new Rect(0.009766f, 0.088867f, 0.226562f, 0.106445f),  // 6 small, far
            new Rect(0.621094f, 0.138672f, 0.178711f, 0.095703f),  // 7 small, crusty scatter
        };

        /// <summary>ONE BRACKET: two alpha-tested cards crossed on the axis that
        /// points out of the wall.
        ///
        /// <para><b>WHICH TWO CARDS, AND WHY THOSE.</b> The one direction a
        /// bracket must never lose is OUT OF THE WALL, so both cards contain it
        /// and they are perpendicular to each other about it:
        /// <list type="bullet">
        /// <item>the PROFILE card stands vertically (its plane holds `out` and
        /// up), so it is face-on to anyone standing along the wall and it draws
        /// the shelf's side elevation — the wedge, the droop and the pale
        /// margin;</item>
        /// <item>the PLAN card lies horizontally (its plane holds `out` and the
        /// wall's tangent), so it is face-on to anyone below or above it and it
        /// draws the shelf's footprint — the half-disc reaching into the
        /// room.</item>
        /// </list>
        /// Between them there is no viewing direction except straight down the
        /// wall normal from which both are edge-on, and that is the one direction
        /// in which a real bracket also presents almost nothing: its front rim.
        /// A third card in the wall's own plane was tried on paper and rejected —
        /// it is the plumb card again, it re-introduces exactly the flat-decal
        /// reading this whole block exists to remove, and it costs a third of the
        /// fill.</para>
        ///
        /// <para>THE PROFILE CARD IS YAWED off the wall normal by `skew`. Not for
        /// variety: a card exactly perpendicular to a flat wall is edge-on to
        /// every player standing in front of that wall, which in a 10.5 x 9.0 m
        /// room is most of them. Twenty-odd degrees costs nothing of the profile
        /// and buys the card a real frontal area from the middle of the floor.
        /// </para>
        ///
        /// <para>AND IT DROOPS. A bracket grows outward and its own weight bends
        /// it: the free edge sits below the attached one. That single degree of
        /// asymmetry is what stops a row of them reading as shelves screwed to
        /// the wall.</para></summary>
        private static void AddFungusBracket(Acc a, Vector3 attach, Vector3 outw, Vector3 tangent,
            float reach, float thick, float wide, float droop, float skew, Rect uv, Color tint)
        {
            var up = Vector3.up;
            // the profile card's own outward, yawed about the vertical
            float cs = Mathf.Cos(skew), sn = Mathf.Sin(skew);
            var oSkew = (outw * cs + tangent * sn).normalized;
            var free = (oSkew - up * droop).normalized * reach;   // the free edge, drooping

            void Card(Vector3 o, Vector3 du, Vector3 dv, Vector3 n)
            {
                int b = a.Count;
                // uv.xMin is the ATTACHED edge and uv.xMax the free rim: the atlas
                // is packed with U running away from the wall (see FungusCards),
                // so a card that is built attached-edge-first is textured right by
                // construction and never needs a flip.
                a.Vert(o - dv * 0.5f, n, new Vector2(uv.xMin, uv.yMin), tint);
                a.Vert(o + du - dv * 0.5f, n, new Vector2(uv.xMax, uv.yMin), tint);
                a.Vert(o + du + dv * 0.5f, n, new Vector2(uv.xMax, uv.yMax), tint);
                a.Vert(o + dv * 0.5f, n, new Vector2(uv.xMin, uv.yMax), tint);
                a.Quad(b);
            }
            // 1. the PROFILE, vertical: du runs out of the wall, dv is its own
            //    thickness. Normal along the wall, on the side the skew turned it
            //    toward, so the baked rig lights the face a player actually sees.
            var pn = Vector3.Cross(free, up).normalized;
            if (Vector3.Dot(pn, tangent) < 0f) pn = -pn;
            Card(attach + up * (thick * 0.5f), free, -up * thick, pn);
            // 2. the PLAN, horizontal: du runs out of the wall, dv is the shelf's
            //    width along the wall. Normal UP — a bracket is lit from above by
            //    whatever the room has, and a plan card wound the other way is a
            //    shelf lit by the floor.
            var pf = (outw - up * droop).normalized * reach;
            Card(attach, pf, tangent * wide, up);
        }

        /// <summary>A tier of brackets on one spot: the way polypores actually
        /// grow, each fruiting body a little above and a little smaller than the
        /// one below, all of them out of the same crack. `n` of them.</summary>
        private static void AddFungusCluster(Acc a, Vector3 attach, Vector3 outw, Vector3 tangent,
            float size, int n, int seed, Color tint)
        {
            for (int k = 0; k < n; k++)
            {
                // each tier smaller than the one under it, with a little jitter
                float f = (1f - 0.22f * k) * (0.80f + 0.40f * Hash3(seed, k, 1, 8701));
                float reach = size * f;
                var rect = FungusCards[(int)(Hash3(seed, k, 2, 8701) * FungusCards.Length) % FungusCards.Length];
                var at = attach
                         + Vector3.up * (k * size * (0.42f + 0.30f * Hash3(seed, k, 3, 8701)))
                         + tangent * ((Hash3(seed, k, 4, 8701) - 0.5f) * size * 0.9f);
                AddFungusBracket(a, at, outw, tangent,
                    reach,
                    thick: reach * (0.26f + 0.14f * Hash3(seed, k, 5, 8701)),
                    wide: reach * (1.25f + 0.55f * Hash3(seed, k, 6, 8701)),
                    droop: 0.14f + 0.20f * Hash3(seed, k, 7, 8701),
                    skew: (Hash3(seed, k, 8, 8701) - 0.5f) * 0.95f,       // +-27 deg
                    uv: rect, tint: tint);
            }
        }

        /// <summary>THE FUNGUS GATES. Two, and they check the two different ways a
        /// wall-mounted card can be built so that nobody ever sees it.
        ///
        /// <para><b>G1 — the winding agrees with the normals.</b> This project has
        /// shipped FOUR meshes wound against the side they are seen from; the
        /// puddle was invisible for ten builds behind Cull Back. EnvRoomCutout is
        /// Cull Off, so a reversed fungus card would not vanish — it would be LIT
        /// FROM BEHIND, which is the same fault wearing a different coat and is
        /// harder to spot in a dark room. So the check is that every triangle's
        /// geometric normal (from its winding) agrees with the normal the builder
        /// stored. PROVEN TO FIRE by reversing Acc.Quad's index order for this
        /// mesh and re-baking: the build stops and names every triangle.</para>
        ///
        /// <para><b>G2 — nothing grows into the masonry.</b> Each card is built
        /// from an attachment point and an outward direction, and if that
        /// direction is ever the wrong way round the whole clump is inside the
        /// wall: invisible, and invisible in a way that looks exactly like "the
        /// fungus did not get built". Every vertex must be on or outside the face
        /// it grows from. PROVEN TO FIRE by negating `outw` at the call site.
        /// </para></summary>
        private static void AssertFungusFacesOut(Mesh m, IList<(Vector3 at, Vector3 outw)> anchors)
        {
            var v = Verts(m); var nrm = m.normals; var t = m.triangles;
            int wound = 0; float worstDot = 1f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var g = Vector3.Cross(b - a, c - a);
                if (g.sqrMagnitude < 1e-14f) continue;
                float dp = Vector3.Dot(g.normalized, nrm[t[i]]);
                worstDot = Mathf.Min(worstDot, dp);
                if (dp <= 0f) wound++;
            }
            if (wound > 0)
                throw new Exception($"Bracket fungi: {wound} of {t.Length / 3} triangles are wound "
                                    + "against the normal the builder stored, so they are lit from "
                                    + "behind and come out black on a black wall. See G1 in "
                                    + "AssertFungusFacesOut.");
            float deepest = 0f; int buried = 0;
            foreach (var p in v)
            {
                // the anchor this vertex belongs to is the nearest one; a card is
                // at most `reach` from its own attachment, and the clumps are
                // metres apart, so nearest-anchor is exact here
                float best = float.MaxValue; Vector3 bo = Vector3.zero, ba = Vector3.zero;
                foreach (var (at, outw) in anchors)
                {
                    float d2 = (p - at).sqrMagnitude;
                    if (d2 < best) { best = d2; bo = outw; ba = at; }
                }
                float into = Vector3.Dot(p - ba, bo);
                if (into < -0.006f) { buried++; deepest = Mathf.Min(deepest, into); }
            }
            if (buried > 0)
                throw new Exception($"Bracket fungi: {buried} vertices are behind the wall face they "
                                    + $"grow from (deepest {deepest * 100f:F1} cm in). The outward "
                                    + "direction of a clump is reversed — every one of those cards is "
                                    + "inside the masonry. See G2 in AssertFungusFacesOut.");
            Debug.Log($"[GloomhavenVR][Env] Bracket fungi winding OK: {t.Length / 3} triangles, every one "
                      + $"wound with its own normal (worst dot {worstDot:F3}), and no vertex is more "
                      + "than 6 mm behind the face it grows from.");
        }

        /// <summary>Does a prop already stand here? `Contacts` is the footprint
        /// list the contact shading is painted from, so the growth gets the
        /// room's own answer for free — as long as it is built BEFORE
        /// PaintContactAO clears it, which is why both callers do.</summary>
        private static bool GrowthBlocked(float x, float z, float pad)
        {
            foreach (var (f, _) in Contacts)
                if (x > f.x0 - pad && x < f.x1 + pad && z > f.z0 - pad && z < f.z1 + pad)
                    return true;
            return false;
        }

        // ============================================ BEDDING A PHOTOSCAN IN
        // USER FINDING, ModBuild 139 (hardware): "In der Waldumgebung gibt es
        // zwei stellen wo etwas undefiniertes aus dem Boden clipped."
        //
        // ROOT CAUSE, and it is not that the grounding is wrong — it is that the
        // grounding is RIGHT about the wrong thing. Rest() drops a ground-standing
        // prop with GroundLift(), which lifts it until the 99.5th percentile of
        // its vertices clears the terrain, i.e. until essentially NOTHING of it
        // is buried. For a barrel, a stool or a crate that is exactly correct and
        // it is what the ModBuild 132 "Gegenstände schweben herum" round was for.
        //
        // It is the opposite of what these props want, because a Poly Haven
        // photoscan of a stump, a log or a boulder is not a model of the object:
        // it is the object WITH A SKIRT OF THE GROUND IT STOOD ON, a nearly flat
        // apron of scanned forest floor 1.5-2.7 m across welded into the same
        // mesh. tree_stump_01 is 1.5 x 1.7 m across and only 0.59 m tall for
        // exactly this reason. Lift that apron until its lowest corner clears the
        // terrain and the REST of it hangs up to the terrain's own relief above
        // the floor — ForestY's 0.52-frequency term alone gives 10-15 cm over a
        // two-metre footprint — where it reads as a pale, hard-edged plate
        // clipping up out of the litter, with a razor silhouette and a texture
        // that matches nothing around it. Which is precisely what was
        // photographed.
        //
        // The `sink` argument was the hand-applied counterweight and it could not
        // work: sink is ONE CONSTANT per prop, while the error is the terrain
        // relief under that prop's own footprint, which nobody measured. So
        // measure it. Bed() asks the prop's own bearing surface how far it stands
        // over the ground it is supposed to be lying in, and drops it by that —
        // never up, so it can only ever bury a skirt and never re-float a prop
        // Rest() has already seated.
        private static readonly Dictionary<Mesh, int[]> ShellCache = new Dictionary<Mesh, int[]>();

        /// <summary>Per-vertex connected-shell id. A decimated photoscan is a heap
        /// of disconnected islands — rock_moss_set_01 is 71 of them, one per
        /// boulder over an 8 x 7 m spread — and a bedding rule that used the
        /// MESH's single lowest vertex would measure the one boulder standing in
        /// the deepest dip and leave the other seventy in the air. Each shell
        /// therefore gets its own base.</summary>
        private static int[] ShellOf(Mesh m)
        {
            if (ShellCache.TryGetValue(m, out var cached)) return cached;
            var v = Verts(m);
            if (!TriCache.TryGetValue(m, out var t)) TriCache[m] = t = m.triangles;
            var parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
                return a;
            }
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }
            for (int i = 0; i + 2 < t.Length; i += 3)
            { Union(t[i], t[i + 1]); Union(t[i + 1], t[i + 2]); }
            var id = new int[v.Length];
            var map = new Dictionary<int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                int r = Find(i);
                if (!map.TryGetValue(r, out int k)) map[r] = k = map.Count;
                id[i] = k;
            }
            ShellCache[m] = id;
            return id;
        }

        /// <summary>Sink a placed prop until its BEARING SURFACE is in the ground
        /// rather than on it.
        ///
        /// One number per SHELL — how far that island's own lowest point stands
        /// over the terrain under it — and then only the shells that are trying
        /// to touch down at all: `reach` is how far up from the closest one the
        /// bearing surface is allowed to spread, which for a scanned apron is its
        /// own relief and for a set of boulders is how uneven the ground under
        /// the set is. Everything above that is the prop's BODY (the stump's
        /// crown, the top facets the decimator left as separate islands) and must
        /// not vote, which is the whole of the first attempt's failure: measured
        /// per vertex against each island's own base, a chip of geometry sitting
        /// on top of a stump reported itself half a metre in the air and dragged
        /// the whole stump 47 cm underground.
        ///
        /// `band` is the other half of the same question and they are genuinely
        /// two: `reach` is how far apart the FEET are (a rock set's boulders bed
        /// independently over the relief the set spans), `band` is how thick ONE
        /// foot is. A stump's apron is a single shell 1.7 m across, so its base
        /// alone says nothing — all of its own relief is INSIDE that one shell,
        /// and only a band over it measures the far side that was left hanging.
        ///
        /// `quantile` is how much of that bearing surface has to end up buried.
        /// Not 1.0 by default: one stray vertex of a photoscan is worth less than
        /// the shape of the thing — but a stump's apron is ONE surface and any
        /// part of it left standing is the whole artefact back again, so that
        /// prop asks for 0.99.</summary>
        private static void Bed(GameObject go, float reach, float band = 0.05f,
            float quantile = 0.90f)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var id = ShellOf(mesh);
            var src = Verts(mesh);
            var xf = go.transform;
            int shells = 0;
            for (int i = 0; i < src.Length; i++) if (id[i] + 1 > shells) shells = id[i] + 1;
            var w = new Vector3[src.Length];
            var clr = new float[src.Length];
            var shellY = new float[shells];
            var baseClear = new float[shells];
            var count = new int[shells];
            for (int s = 0; s < shells; s++) { shellY[s] = float.MaxValue; baseClear[s] = float.MaxValue; }
            for (int i = 0; i < src.Length; i++)
            {
                w[i] = xf.TransformPoint(src[i]);
                clr[i] = w[i].y - _groundY(w[i].x, w[i].z);
                count[id[i]]++;
                if (w[i].y < shellY[id[i]]) shellY[id[i]] = w[i].y;
                if (clr[i] < baseClear[id[i]]) baseClear[id[i]] = clr[i];
            }
            // A decimated photoscan is mostly slivers; a shell of three vertices
            // is noise and not a piece of the thing that stands on the ground.
            float nearest = float.MaxValue;
            int kept = 0;
            for (int s = 0; s < shells; s++)
                if (count[s] >= 8) { kept++; nearest = Mathf.Min(nearest, baseClear[s]); }
            if (kept == 0) return;
            // The bearing surface: every vertex in the low `band` of a shell that
            // is itself trying to touch down.
            var bearing = new List<float>();
            for (int i = 0; i < w.Length; i++)
                if (count[id[i]] >= 8 && baseClear[id[i]] <= nearest + reach
                    && w[i].y <= shellY[id[i]] + band)
                    bearing.Add(clr[i]);
            if (bearing.Count == 0) return;
            bearing.Sort();
            float drop = bearing[Mathf.Clamp(Mathf.RoundToInt((bearing.Count - 1) * quantile),
                                             0, bearing.Count - 1)];
            // Never lift. Rest() has already guaranteed the prop is not below its
            // support; this pass exists only to take a hovering skirt down.
            if (drop <= 0.005f)
            {
                Grounded.Add($"{go.name}: bedding not needed — its bearing surface "
                             + $"({bearing.Count} vertices of {kept} shell(s)) already sits within "
                             + $"{drop * 100f:+0.0;-0.0} cm of the floor.");
                return;
            }
            go.transform.localPosition += new Vector3(0f, -drop, 0f);
            Grounded.Add($"{go.name}: BEDDED {drop * 100f:F1} cm — that much of its scanned ground "
                         + $"skirt was standing proud of the forest floor ({bearing.Count} bearing "
                         + $"vertices over {kept} shell(s), reach {reach * 100f:F0} cm, band "
                         + $"{band * 100f:F0} cm, q {quantile:F2}).");
        }

        /// <summary>One shaft of moonlight: two crossed tapered blades, world-fixed
        /// (never camera-facing). uv = (across 0..1, along 0..1) for EnvShaft.
        ///
        /// VERTEX COLOUR IS DATA, not a tint (ModBuild 137): r = the fade-in
        /// length and g = the fade-out length, both in v units, so shafts of
        /// different lengths can share one material and still fade over the same
        /// number of METRES. It used to be white and multiplied into the tint,
        /// which is why nothing else has to change. Alpha is still the per-shaft
        /// strength.</summary>
        private static void AddShaft(Acc a, Vector3 top, Vector3 dir, float len,
            float w0, float w1, float amp, Vector3 across, float fadeIn, float fadeOut)
        {
            dir = dir.normalized;
            Vector3 r1 = Vector3.Cross(dir, Vector3.up).normalized;
            if (r1.sqrMagnitude < 0.5f) r1 = across.normalized;
            Vector3 r2 = Vector3.Cross(dir, r1).normalized;
            var col = new Color(Mathf.Clamp(fadeIn, 0.01f, 0.5f),
                                Mathf.Clamp(fadeOut, 0.01f, 0.9f), 1f, amp);
            void Blade(Vector3 right)
            {
                Vector3 bot = top + dir * len;
                // the blade's OWN normal (perpendicular to its plane) — EnvShaft
                // fades the blade out as it turns edge-on, which is what stops a
                // wide beam reading as a pane of glass
                Vector3 nrm = Vector3.Cross(dir, right).normalized;
                int b = a.Count;
                a.Vert(top - right * w0, nrm, new Vector2(0f, 0f), col);
                a.Vert(top + right * w0, nrm, new Vector2(1f, 0f), col);
                a.Vert(bot + right * w1, nrm, new Vector2(1f, 1f), col);
                a.Vert(bot - right * w1, nrm, new Vector2(0f, 1f), col);
                a.Quad(b);
            }
            Blade(r1);
            Blade(r2);
        }
    }
}
