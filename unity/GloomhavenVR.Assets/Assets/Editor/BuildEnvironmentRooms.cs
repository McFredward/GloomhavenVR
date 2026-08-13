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
        //   props line the walls; the closest (the stool) stands at 3.44 m. NOT
        //   the room's own 9.0 m: that would put the board's diorama scale on a
        //   circle that the table, stool and crates all stand inside of.
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
            new Dictionary<string, float> { ["cobweb"] = EnvironmentsBuilder.WebCutoff };

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
        }

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
            float s = (xf.lossyScale.x + xf.lossyScale.y + xf.lossyScale.z) / 3f;
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
        /// of the sprig, `right` is the card's width axis.</summary>
        private static void AddCard(Acc a, Vector3 c, Vector3 right, Vector3 up, Vector3 nrm,
            Rect uvRect, Color col)
        {
            int b = a.Count;
            a.Vert(c - right - up, nrm, new Vector2(uvRect.xMin, uvRect.yMin), col);
            a.Vert(c + right - up, nrm, new Vector2(uvRect.xMax, uvRect.yMin), col);
            a.Vert(c + right + up, nrm, new Vector2(uvRect.xMax, uvRect.yMax), col);
            a.Vert(c - right + up, nrm, new Vector2(uvRect.xMin, uvRect.yMax), col);
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

        /// <summary>A tapered tube through a polyline of rings — the rat's body,
        /// head, tail and legs are all this. `uvx` runs along the tube, the ring
        /// angle gives the belly/back blend in uv.x (0 belly, 1 back).</summary>
        private static void AddTube(Acc a, Vector3[] c, float[] r, Color[] col, float[] along, int segs)
        {
            int n = c.Length, stride = segs + 1;
            int b0 = a.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 axis = (i == 0 ? c[1] - c[0] : i == n - 1 ? c[n - 1] - c[n - 2] : c[i + 1] - c[i - 1]).normalized;
                Vector3 hint = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 rt = Vector3.Cross(hint, axis).normalized;
                Vector3 uu = Vector3.Cross(axis, rt).normalized;
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
                    a.T.AddRange(new[] { i0, i0 + stride, i0 + 1, i0 + 1, i0 + stride, i0 + stride + 1 });
                }
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

        // The window moved WEST (was x 6.0 in wall-local units). Two reasons:
        // the moon shaft that now comes through it has a fixed bearing, and at
        // the old position its pool landed 2.3 m from the room centre — inside
        // the 6.5 m PlaySpace disc, i.e. across the board. From here the beam
        // lands at ~3.95 m, clear of it, and the cold light ends up on the
        // OPPOSITE side of the room from the warm candles.
        private static readonly Rect WindowHole = new Rect(3.32f, 2.15f, 1.15f, 0.7f);  // in N-wall local x/y
        private static readonly Rect StairHole = new Rect(6.2f, 0f, 1.6f, 2.35f);       // in W-wall local x/y
        private const float RevealDepth = 0.34f;   // wall thickness at the window

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
        // The draught: in at the window, out under the stair door. The flames
        // lean along it (EnvFlame/_GustDir) and the dust motes drift along it
        // (EnvironmentsBuilder), which is what makes it read as one draught
        // through the room instead of two unrelated wobbles.
        private static readonly Vector3 DraftDir = new Vector3(-0.890f, 0f, -0.456f);

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

        public static void BuildCellarRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);

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
                points = new[]
                {
                    new PLight(new Vector3(3.55f, 1.06f, 3.10f), 3.10f, new Color(1f, 0.60f, 0.30f) * 2.10f, 0.90f), // table candles
                    new PLight(new Vector3(4.72f, 2.00f, 0.70f), 2.90f, new Color(1f, 0.56f, 0.26f) * 1.75f, 0.95f), // shelf candle
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
            floorGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Floor.mat", "monastery_stone_floor", 2.6f, floorGo.transform, 1.0f, 1f);

            // ---- walls (N has window, W has stair doorway) ----
            var wallN = SaveMesh("Env_C_WallN.asset", WallMesh(CW, CH, 0.16f, new[] { WindowHole }, 3.3f, 911, uOff: 0.00f));
            var wallS = SaveMesh("Env_C_WallS.asset", WallMesh(CW, CH, 0.16f, Array.Empty<Rect>(), 3.3f, 912, uOff: 1.31f));
            var wallE = SaveMesh("Env_C_WallE.asset", WallMesh(CD, CH, 0.16f, Array.Empty<Rect>(), 3.3f, 913, uOff: 2.17f));
            var wallW = SaveMesh("Env_C_WallW.asset", WallMesh(CD, CH, 0.16f, new[] { StairHole }, 3.3f, 914, uOff: 0.73f));
            void Wall(string n, Mesh mesh, Vector3 pos, float yaw)
            {
                var go = Place(root, n, mesh, pos, new Vector3(0, yaw, 0), Vector3.one, null);
                go.GetComponent<MeshRenderer>().sharedMaterial =
                    SurfMat("C_" + n + ".mat", "medieval_blocks_05", 3.4f, go.transform, 1.15f, 1f);
            }
            Wall("WallN", wallN, new Vector3(-hw, 0, hd), 0);        // runs +X, faces -Z (into room)
            Wall("WallS", wallS, new Vector3(hw, 0, -hd), 180);
            Wall("WallE", wallE, new Vector3(hw, 0, hd), 90);        // runs -Z
            Wall("WallW", wallW, new Vector3(-hw, 0, -hd), 270);

            // ---- ceiling: planks + beams + corbels ----
            var ceilMesh = SaveMesh("Env_C_Ceil.asset", GridMeshXZ(-hw, -hd, hw, hd, 8, 8, (x, z) => CH, null, 2.4f, faceDown: true));
            var ceilGo = Place(root, "Ceiling", ceilMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            ceilGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Ceiling.mat", "dark_wooden_planks", 2.4f, ceilGo.transform, 0.9f, 0.85f);

            var beamMesh = SaveMesh("Env_C_Beam.asset", BoxMesh(0.30f, 0.26f, CW, 1.3f));
            var corbelMesh = SaveMesh("Env_C_Corbel.asset", BoxMesh(0.34f, 0.24f, 0.42f, 1.7f));
            for (int i = 0; i < 4; i++)
            {
                float z = -hd + CD * (i + 1) / 5f;
                var b = Place(root, "Beam" + i, beamMesh, new Vector3(0, CH - 0.26f, z), new Vector3(0, 90, 0), Vector3.one, null);
                b.GetComponent<MeshRenderer>().sharedMaterial =
                    SurfMat($"C_Beam{i}.mat", "dark_wooden_planks", 1.3f, b.transform, 0.9f, 0.9f);
                foreach (var sx in new[] { -1f, 1f })
                {
                    var c = Place(root, $"Corbel{i}{(sx < 0 ? "W" : "E")}", corbelMesh,
                        new Vector3(sx * (hw - 0.17f), CH - 0.50f, z), Vector3.zero, Vector3.one, null);
                    c.GetComponent<MeshRenderer>().sharedMaterial =
                        SurfMat($"C_Corbel{i}{(sx < 0 ? "W" : "E")}.mat", "medieval_blocks_05", 1.7f, c.transform, 1.0f, 0.9f);
                }
            }

            // ---- the window: reveal, sill, bars, and the moonlight through it ----
            // The opening WallMesh really cut, in ROOM coordinates. Everything
            // below is derived from it, so nothing can be half a cell out.
            var wh = SnappedHole(WindowHole, CW, CH, WallCell);
            float wx0 = -hw + wh.xMin, wx1 = -hw + wh.xMax;
            float wy0 = wh.yMin, wy1 = wh.yMax;
            var winMid = new Vector3((wx0 + wx1) * 0.5f, (wy0 + wy1) * 0.5f, hd);
            Debug.Log($"[GloomhavenVR][Env] Cellar window opening (snapped): x {wx0:F3}..{wx1:F3}, "
                      + $"y {wy0:F3}..{wy1:F3}, reveal depth {RevealDepth:F2} m.");

            // Reveal (jambs + head + sill). Without it the wall is a zero-
            // thickness plane, there is no "inside the opening" to put the bars
            // in, and anything placed near it necessarily floats in front of it.
            var revealMesh = SaveMesh("Env_C_Reveal.asset", RevealMesh(wx0, wy0, wx1, wy1, hd, RevealDepth));
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
            float barGap = (wx1 - wx0) / 5f;             // 5 slots of light, 4 bars
            var bars = new Acc();
            var barProfile = new[] { new Vector2(0.017f, 0f), new Vector2(0.019f, 0.35f), new Vector2(0.017f, 1f) };
            var barUnit = LatheMesh(barProfile, 6);
            for (int i = 0; i < 4; i++)
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
                beamMat.SetColor("_Tint", new Color(0.55f, 0.68f, 1.0f, 0.055f));
                beamMat.SetVector("_BeamOrg", winMid);
                beamMat.SetVector("_BeamDir", dir);
                beamMat.SetFloat("_Len", beamLen);
                beamMat.SetFloat("_W0", W0);
                beamMat.SetFloat("_WK", WK);
                beamMat.SetFloat("_RadPow", 1.35f);
                beamMat.SetFloat("_Ramp", 0.34f);      // = RevealDepth: it emerges from the embrasure
                beamMat.SetFloat("_Decay", 1.30f);     // 26% left at 1 m, 7% at 2 m: "leicht hereinkommend"
                beamMat.SetFloat("_EndFade", 0.55f);
                beamMat.SetFloat("_MinSin", 0.45f);    // looking along it is 2.2x broadside, not 3.1x
                beamMat.SetFloat("_Knee", 1.10f);
                beamMat.SetFloat("_Shimmer", 0.14f);
                beamMat.SetFloat("_ShimmerSpeed", 0.13f);
                // the bars' true shadow: pitch and first bar taken from the SAME
                // numbers the bars were built from, traced back to the wall plane
                beamMat.SetFloat("_WinZ", hd);
                beamMat.SetFloat("_BarX0", wx0 + barGap);
                beamMat.SetFloat("_BarPitch", barGap);
                beamMat.SetFloat("_BarDepth", 0.34f);
                beamMat.SetFloat("_BarSig", 0.030f);
                beamMat.SetFloat("_BarBlur", 0.095f);
                beamMat.SetFloat("_BarFade", 0.85f);
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
                winGlowMat.SetFloat("_Falloff", 2.0f);
                var glowSphere = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
                if (glowSphere != null)
                    Place(root, "WindowGlow", glowSphere, winMid + new Vector3(0, 0, -0.06f),
                          Vector3.zero, new Vector3(0.52f, 0.34f, 0.22f), winGlowMat);
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
            Prop(root, "Stool", "wooden_stool_02", "wooden_stool_02", new Vector3(2.55f, 0, 2.3f), 40, 1f, "C");
            var shelf = Prop(root, "Shelf", "wooden_bookshelf_worn", "wooden_bookshelf_worn", new Vector3(4.86f, 0, 0.7f), -90, 1f, "C");
            Prop(root, "Bucket", "wooden_bucket_01", "wooden_bucket_01", new Vector3(-4.0f, 0, -4.05f), 0, 1f, "C");
            Prop(root, "Jug0", "jug_01", "jug_01", new Vector3(3.52f, 0, 3.30f), 65, 1f, "C",
                sink: 0.001f, support: table);
            Prop(root, "Jug1", "jug_01", "jug_01", new Vector3(-0.45f, 0, -4.05f), 10, 0.9f, "C",
                sink: 0.001f, support: crate1);
            float tableTop = SurfaceYAt(table, 3.72f, 2.95f);
            float crateTop = SurfaceYAt(crate2, -1.55f, -3.95f);
            float shelfTop = SurfaceYAt(shelf, 4.72f, 0.70f);

            // ---- candles: lathe wax + flame cards + warm glow, ON the props ----
            // Each GROUP drives one light slot, so everything that belongs to it —
            // its flames, its halo — is built with that slot's phase AND rate.
            // Before this round every flame in the room shared one material with
            // _Phase 0 while the three light slots ran on phases 0/2.1/4.4: the
            // flame you were looking at and the light it cast were two unrelated
            // animations, which is most of why the flicker read as "only the
            // flame cards move".
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            void CandleGroup(string n, int slot, Vector3 basePos,
                (float h, float dx, float dz)[] candles, float glowR, float glowA)
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
                    Place(root, $"Flame{n}{ci}", fm, basePos + new Vector3(c.dx, c.h + 0.002f, c.dz),
                          Vector3.zero, Vector3.one, flame);
                    ci++;
                }
                var waxMesh = SaveMesh($"Env_C_Wax{n}.asset", acc.Build($"Env_C_Wax{n}"));
                var waxGo = Place(root, $"Candles{n}", waxMesh, Vector3.zero, Vector3.zero, Vector3.one, wax);
                Defer(wax, waxGo.transform, 1f);

                if (glowMesh != null)
                {
                    // the halo breathes WITH the candle — it used to be a static
                    // sphere sitting inside a flickering pool of light
                    var g = NewRoomMat($"C_Glow{n}.mat", "GloomhavenVR/EnvGlow");
                    g.SetColor("_Tint", new Color(1f, 0.55f, 0.20f, glowA));
                    g.SetFloat("_Falloff", 2.2f);
                    g.SetFloat("_Flicker", 0.95f);
                    g.SetFloat("_Rate", SlotRate[slot]);
                    g.SetFloat("_Phase", SlotPhase[slot]);
                    Place(root, $"CandleGlow{n}", glowMesh,
                        basePos + new Vector3(candles[0].dx, candles[0].h + 0.05f, candles[0].dz),
                        Vector3.zero, Vector3.one * glowR, g);
                }
            }
            var candleTable = new Vector3(3.72f, tableTop, 2.95f);
            var candleShelf = new Vector3(4.72f, shelfTop, 0.70f);
            var candleCrate = new Vector3(-1.55f, crateTop, -3.95f);
            CandleGroup("Table", 0, candleTable,
                new[] { (0.16f, 0f, 0f), (0.11f, 0.07f, 0.04f), (0.085f, -0.05f, 0.06f) }, 0.30f, 0.60f);
            CandleGroup("Shelf", 1, candleShelf, new[] { (0.12f, 0f, 0f) }, 0.24f, 0.50f);
            CandleGroup("Crate", 2, candleCrate, new[] { (0.14f, 0f, 0f), (0.09f, 0.06f, -0.05f) }, 0.27f, 0.55f);

            // rig fixup: light sources sit just above the tallest flame of each group
            rig.points[0].pos = candleTable + new Vector3(0, 0.22f, 0);
            rig.points[1].pos = candleShelf + new Vector3(0, 0.18f, 0);
            rig.points[2].pos = candleCrate + new Vector3(0, 0.20f, 0);

            BuildCellarAtmosphere(root, rig);

            PaintContactAO(floorGo, 0.40f, 0.30f);
            FlushRig(rig);
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
        private static Mesh RevealMesh(float x0, float y0, float x1, float y1, float zWall, float d)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            const float us = 3.4f;   // same world UV scale as the walls
            void Face(Vector3 o, Vector3 du, Vector3 dv)
            {
                int b = v.Count;
                v.Add(o); v.Add(o + du); v.Add(o + du + dv); v.Add(o + dv);
                float lu = du.magnitude / us, lv = dv.magnitude / us;
                // offset the UV origin by the world position so the reveal's
                // stone continues the wall's instead of restarting at a block edge
                float ou = (o.x + o.z) / us, ov = o.y / us;
                uv.Add(new Vector2(ou, ov)); uv.Add(new Vector2(ou + lu, ov));
                uv.Add(new Vector2(ou + lu, ov + lv)); uv.Add(new Vector2(ou, ov + lv));
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });  // normal = cross(du,dv)
            }
            float h = y1 - y0, w = x1 - x0;
            // jambs (normals +X and -X, into the opening)
            Face(new Vector3(x0, y0, zWall), new Vector3(0, 0, d), new Vector3(0, h, 0));
            Face(new Vector3(x1, y0, zWall + d), new Vector3(0, 0, -d), new Vector3(0, h, 0));
            // head (normal down) and sill (normal up)
            Face(new Vector3(x0, y1, zWall), new Vector3(w, 0, 0), new Vector3(0, 0, d));
            Face(new Vector3(x0, y0, zWall + d), new Vector3(w, 0, 0), new Vector3(0, 0, -d));
            return FinishMesh(v, uv, tri, null);
        }

        /// <summary>The puddle: an irregular polar patch lying on (and following)
        /// the flagstones, its vertex ALPHA carrying the wet mask so the edge
        /// feathers into damp stone instead of ending in a rim.</summary>
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
                    a.T.AddRange(new[] { i0, j0, i1, i1, j0, j1 });
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
        /// (r tail, g leg, b leg phase). EnvCritter walks it along its Bezier.</summary>
        private static Mesh RatMesh()
        {
            var a = new Acc();
            var fur = new Color(0f, 0f, 0f, 1f);

            // body + head as one tube: rump -> shoulders -> muzzle
            var bc = new[]
            {
                new Vector3(0, 0.044f, -0.045f), new Vector3(0, 0.048f, -0.010f),
                new Vector3(0, 0.052f,  0.028f), new Vector3(0, 0.054f,  0.066f),
                new Vector3(0, 0.052f,  0.100f), new Vector3(0, 0.050f,  0.126f),
                new Vector3(0, 0.046f,  0.156f), new Vector3(0, 0.040f,  0.182f),
            };
            var br = new[] { 0.024f, 0.036f, 0.042f, 0.041f, 0.034f, 0.027f, 0.018f, 0.006f };
            var bcol = new[] { fur, fur, fur, fur, fur, fur, fur, fur };
            var balong = new[] { 0f, 0.12f, 0.28f, 0.45f, 0.62f, 0.75f, 0.88f, 1f };
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
                tcol[i] = new Color(Mathf.SmoothStep(0f, 1f, f), 0f, 0f, 1f);   // r = tail weight
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
                var lcol = new[] { new Color(0f, 0.5f, phase, 1f), new Color(0f, 1f, phase, 1f), new Color(0f, 1f, phase, 1f) };
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
                AddTube(a, ec, new[] { 0.0125f, 0.0105f }, new[] { fur, fur }, new[] { 0f, 1f }, 6);
            }
            Ear(1f); Ear(-1f);
            return a.Build("Env_C_Rat");
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
        /// fail ZTest and cut a hole in the beam.</summary>
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
                    a.T.AddRange(new[] { i0, i0 + stride, i0 + 1, i0 + 1, i0 + stride, i0 + stride + 1 });
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
            Cap(0, org + dir * s0, -dir, false);
            Cap(rings * stride, org + dir * s1, dir, true);
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
            pud.SetFloat("_RingFreq", 4.4f);
            pud.SetFloat("_RingAmp", 0.55f);
            pud.SetFloat("_RingCon", 0.70f);
            pud.SetFloat("_Calm", 0.06f);
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
            Place(root, "Puddle", puddleMesh, Vector3.zero, Vector3.zero, Vector3.one, pud);

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
            drip.SetFloat("_SplashLife", 0.40f);
            drip.SetFloat("_SplashOut", 0.55f);
            drip.SetFloat("_SplashUp", 1.10f);
            drip.SetFloat("_Stretch", 0.075f);
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
            var w0 = new Vector3(-4.00f, 0.015f, 4.42f);
            var w1 = new Vector3(-2.42f, 0.015f, 1.81f);
            var w2 = new Vector3(-5.00f, 0.015f, -2.30f);
            var w3 = new Vector3(-0.95f, 0.015f, -4.44f);
            // ...and both of those claims are CHECKED, because they are the two
            // things a future edit to the route would silently break.
            {
                Vector3 Bez(float u)
                {
                    float k = 1f - u;
                    return k * k * k * w0 + 3f * k * k * u * w1 + 3f * k * u * u * w2 + u * u * u * w3;
                }
                var beam = MoonBeamHit();
                var bd = -MoonDir.normalized;
                float nearCentre = float.MaxValue, nearBeam = float.MaxValue;
                for (int i = 0; i <= 240; i++)
                {
                    var p = Bez(i / 240f);
                    nearCentre = Mathf.Min(nearCentre, new Vector2(p.x, p.z).magnitude);
                    var rel = p - beam;
                    nearBeam = Mathf.Min(nearBeam, (rel - bd * Vector3.Dot(rel, bd)).magnitude);
                }
                if (nearCentre < CellarPlaySpaceDia * 0.5f)
                    throw new Exception($"Rat path reaches {nearCentre:F2} m from the room centre — inside the "
                                        + $"{CellarPlaySpaceDia:F1} m PlaySpace. Move a control point outward.");
                Debug.Log($"[GloomhavenVR][Env] Rat path: nearest the room centre {nearCentre:F2} m "
                          + $"(needs >= {CellarPlaySpaceDia * 0.5f:F2}), nearest the moon beam axis {nearBeam:F2} m "
                          + "(it has to cross it, not pass by).");
            }

            var pathBox = new Bounds(w0, Vector3.zero);
            foreach (var p in new[] { w1, w2, w3 }) pathBox.Encapsulate(p);
            pathBox.Expand(new Vector3(0.7f, 0.8f, 0.7f));
            var ratMesh = SaveMesh("Env_C_Rat.asset", RatMesh(), pathBox);
            var ratMat = NewRoomMat("C_Rat.mat", "GloomhavenVR/EnvCritter");
            ratMat.SetColor("_Tint", new Color(0.36f, 0.310f, 0.280f));
            ratMat.SetColor("_BellyTint", new Color(0.52f, 0.46f, 0.42f));
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
            ratMat.SetFloat("_Period", 31f);
            ratMat.SetFloat("_RunTime", 4.6f);
            // Phase 0: the run starts at t=0 of the shader clock. That is not a
            // detail — it is what lets a preview time series (and a reviewer with
            // a stopwatch) see the whole run at known offsets.
            ratMat.SetFloat("_Phase", 0f);
            ratMat.SetFloat("_Dart", 0.062f);
            ratMat.SetFloat("_Stride", 24f);
            ratMat.SetFloat("_Scale", 1f);
            var ratGo = Place(root, "Rat", ratMesh, Vector3.zero, Vector3.zero, Vector3.one, ratMat);
            Defer(ratMat, ratGo.transform, 1f);

            // ...and the holes it uses. Nothing bigger than a fist, black inside.
            // (Quads face INTO the room: AddQuad's facing is cross(halfV,halfU).)
            var holes = new Acc();
            AddQuad(holes, new Vector3(w0.x, 0.062f, hd - 0.022f),
                    new Vector3(0.082f, 0, 0), new Vector3(0, 0.062f, 0), Color.white);
            AddQuad(holes, new Vector3(w3.x, 0.058f, -hd + 0.022f),
                    new Vector3(-0.078f, 0, 0), new Vector3(0, 0.058f, 0), Color.white);
            var holeMat = NewRoomMat("C_RatHole.mat", "GloomhavenVR/EnvRoom");
            holeMat.SetColor("_Tint", new Color(0.012f, 0.011f, 0.010f));
            Place(root, "RatHoles", SaveMesh("Env_C_RatHoles.asset", holes.Build("Env_C_RatHoles")),
                  Vector3.zero, Vector3.zero, Vector3.one, holeMat);

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
                      new Vector3(hw - wSpan * 0.5f, CH - wSpan * 0.5f, 1.30f),
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

        // =========================================================== build forest
        public static void BuildForestRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);
            _groundY = ForestY;

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

            // ------------------------------------------------- moonlight shafts
            // Five blades through the tear in the canopy, along the real moon
            // bearing, landing in and around the clearing.
            {
                var sh = new Acc();
                var dir = -MoonDir.normalized;                       // light travels DOWN-sunward
                var across = Vector3.Cross(Vector3.up, moonHoriz).normalized;
                // Build each shaft from where it LANDS, not from where it enters.
                // Aiming down from a fixed canopy point sent every beam straight
                // through the play space, where it read as a pane of glass across
                // the whole view; now they strike the clearing floor around the
                // board and are seen from outside.
                for (int i = 0; i < 3; i++)
                {
                    float side = (i - 1.0f) * 3.1f + 0.8f * (Hash3(i, 0, 0, 5311) - 0.5f);
                    Vector3 hit = moonHoriz * (4.0f + 3.4f * Hash3(i, 1, 0, 5311)) + across * side;
                    hit.y = ForestY(hit.x, hit.z) - 0.15f;
                    float len = 11.5f + 2.5f * Hash3(i, 3, 0, 5311);
                    Vector3 top = hit - dir * len;                   // back up along the beam
                    float w0 = 0.42f + 0.30f * Hash3(i, 4, 0, 5311);
                    float w1 = w0 * 2.8f;
                    float amp = 0.6f + 0.4f * Hash3(i, 5, 0, 5311);
                    AddShaft(sh, top, dir, len, w0, w1, amp, across);
                }
                var shaftMat = NewRoomMat("S_Shaft.mat", "GloomhavenVR/EnvShaft");
                // a touch stronger than ModBuild 133 (alpha 0.30): with the wood
                // around them darker the blades are now the brightest thing in the
                // room, which is exactly what should draw the eye to the clearing
                shaftMat.SetColor("_Tint", new Color(0.56f, 0.66f, 0.92f, 0.34f));
                shaftMat.SetFloat("_Softness", 6.5f);
                shaftMat.SetFloat("_Shimmer", 0.30f);
                shaftMat.SetFloat("_ShimmerSpeed", 0.20f);
                var shMesh = SaveMesh("Env_S_Shafts.asset", sh.Build("Env_S_Shafts"));
                Place(root, "MoonShafts", shMesh, Vector3.zero, Vector3.zero, Vector3.one, shaftMat);
            }

            // ---------------------------------------------------- ground props
            // ModBuild 134: props fade out with the same urgency the trunks do —
            // a lit fern at 12 m is a described object where there should be
            // nothing but a suggestion.
            float Fade(Vector3 pos) =>
                Mathf.SmoothStep(1f, 0.04f, Mathf.InverseLerp(5.5f, 12f, new Vector2(pos.x, pos.z).magnitude));
            GameObject SProp(string n, string mesh, string tex, Vector3 pos, float yaw, float scale,
                Vector3? e3 = null, Vector3? s3 = null, bool cutout = false, float bump = 1f,
                float sink = 0.05f, float tintExtra = 1f, GameObject support = null,
                Quaternion? rot = null)
            {
                return Prop(root, n, mesh, tex, pos, yaw, scale, "S",
                    tintMul: Fade(pos) * tintExtra, euler3: e3, scale3: s3, cutout: cutout,
                    bump: bump, sink: sink, support: support, rot: rot);
            }

            // deadfall: one log across the path, one at the clearing edge
            // both were pulled outward for the 9.0 m PlaySpace: a 3 m log lying
            // across the path reached 4.28 m from the centre at its near end
            SProp("Log0", "dead_tree_trunk", "dead_tree_trunk", new Vector3(-3.1f, 0, -6.1f), 62, 1.0f, sink: 0.10f);
            SProp("Log1", "dead_tree_trunk", "dead_tree_trunk", new Vector3(6.9f, 0, 5.0f), 128, 1.15f, sink: 0.12f);
            // THE leaning dead tree — caught in its neighbour's crown and never
            // fell. Rest() grounds it vertex-exactly despite the 62° tilt.
            SProp("LeanTree", "dead_tree_trunk_02", "dead_tree_trunk_02", new Vector3(-6.4f, 0, 5.9f),
                0, 1.35f, e3: new Vector3(0f, 24f, 62f), sink: 0.05f);
            // stumps; the axe is left in the near one
            var stump0 = SProp("Stump0", "tree_stump_01", "tree_stump_01", new Vector3(4.4f, 0, -4.1f), 60, 1.05f);
            SProp("Stump1", "tree_stump_02", "tree_stump_02", new Vector3(-7.2f, 0, -2.1f), 200, 1.0f);
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
            // roots breaking the floor, mostly at the trunk feet and the path rim
            SProp("Roots0", "root_cluster_02", "root_cluster_02", new Vector3(5.4f, 0, 2.3f), 190, 0.95f, sink: 0.14f);
            SProp("Roots1", "root_cluster_02", "root_cluster_02", new Vector3(-5.7f, 0, -3.1f), 55, 0.85f, sink: 0.16f);
            // was (-1.9,-3.1): that reached 3.6 m into the 9.0 m PlaySpace disc
            SProp("Root2", "single_root", "single_root", new Vector3(-3.6f, 0, -4.5f), 300, 1.0f, sink: 0.12f);
            SProp("Root3", "single_root", "single_root", new Vector3(2.6f, 0, 4.9f), 130, 0.9f, sink: 0.12f);
            // mossy rock outcrops
            SProp("Rocks0", "rock_moss_set_01", "rock_moss_set_01", new Vector3(-6.4f, 0, 3.4f), 30, 0.42f, sink: 0.12f);
            SProp("Rocks1", "rock_moss_set_02", "rock_moss_set_02", new Vector3(7.4f, 0, -1.4f), 245, 0.5f, sink: 0.12f);
            SProp("Rocks2", "rock_moss_set_02", "rock_moss_set_02", new Vector3(-3.1f, 0, 7.2f), 95, 0.38f, sink: 0.10f);
            // deadfall branches
            SProp("Branches0", "dry_branches_medium_01", "dry_branches_medium_01", new Vector3(1.4f, 0, -5.9f), 80, 1.0f);
            SProp("Branches1", "dry_branches_medium_01", "dry_branches_medium_01", new Vector3(-6.9f, 0, -5.0f), 250, 0.9f);
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
            void Glow(string n, Vector3 p, float r, Color c, float falloff)
            {
                var m = NewRoomMat($"S_Glow{n}.mat", "GloomhavenVR/EnvGlow");
                m.SetColor("_Tint", c);
                m.SetFloat("_Falloff", falloff);
                Place(root, "Wisp" + n, glowMesh, p, Vector3.zero, Vector3.one * r, m);
            }
            // ModBuild 134: the halos keep their brightness while everything
            // around them loses two thirds of its own. They are the "occasional
            // wisp, glint of eyes" the user asked to be the ONLY thing readable
            // out there, so they are left alone deliberately — the contrast they
            // gain is the point.
            Glow("Wisp", new Vector3(-4.6f, 0.95f, -6.2f), 0.55f, new Color(0.42f, 1f, 0.60f, 0.16f), 2.4f);
            Glow("Lantern", new Vector3(9.2f, 1.45f, -7.4f), 0.75f, new Color(1f, 0.60f, 0.24f, 0.17f), 2.2f);
            Glow("Far", new Vector3(-11.5f, 1.1f, 8.2f), 0.7f, new Color(0.55f, 0.95f, 0.70f, 0.10f), 2.6f);
            // eyes: two tiny cold points at head height, deep between the trunks,
            // 12 cm apart. They never move — that is the point.
            var eyeDir = new Vector3(Mathf.Sin(2.35f), 0f, Mathf.Cos(2.35f));
            var eyeAt = eyeDir * 12.5f + Vector3.up * 1.55f;
            var eyeSide = Vector3.Cross(Vector3.up, eyeDir).normalized * 0.06f;
            Glow("EyeL", eyeAt - eyeSide, 0.045f, new Color(1f, 0.88f, 0.45f, 0.85f), 3.2f);
            Glow("EyeR", eyeAt + eyeSide, 0.045f, new Color(1f, 0.88f, 0.45f, 0.85f), 3.2f);

            PaintContactAO(g, 0.45f, 0.55f);
            FlushRig(rig);
            ReportGrounding("Forest");
            // 'Ground' is the floor the board stands on and 'MoonShafts' are
            // light, not matter — everything else must stay outside the clearing.
            AssertPlaySpaceClear(root, "Forest", ForestPlaySpaceDia, "Ground", "MoonShafts");
            Debug.Log("[GloomhavenVR][Env] Night-forest room geometry assembled.");
        }

        /// <summary>One shaft of moonlight: two crossed tapered blades, world-fixed
        /// (never camera-facing). uv = (across 0..1, along 0..1) for EnvShaft.</summary>
        private static void AddShaft(Acc a, Vector3 top, Vector3 dir, float len,
            float w0, float w1, float amp, Vector3 across)
        {
            dir = dir.normalized;
            Vector3 r1 = Vector3.Cross(dir, Vector3.up).normalized;
            if (r1.sqrMagnitude < 0.5f) r1 = across.normalized;
            Vector3 r2 = Vector3.Cross(dir, r1).normalized;
            var col = new Color(1f, 1f, 1f, amp);
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
