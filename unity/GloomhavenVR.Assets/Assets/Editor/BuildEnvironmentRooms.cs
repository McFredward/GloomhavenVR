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
        private const string GenTexDir = Root + "/Textures";

        // Moon bearing — MUST match EnvironmentsBuilder.MoonDir (star-dome shader)
        // so the swamp's directional light and water glints come from the moon.
        private static readonly Vector3 MoonDir = new Vector3(0.596f, 0.374f, 0.710f).normalized;

        // ================================================================ imports
        // Per-asset import caps — THE bundle-size knob. Values are chosen for
        // "reads painterly-real at arm's length in VR" vs the ≤ ~25 MB bundle
        // growth budget; big close surfaces get 2k, props 512–1k, normals half.
        private static readonly Dictionary<string, int> AlbSize = new Dictionary<string, int>
        {
            // surfaces
            ["medieval_blocks_05"] = 2048,
            ["monastery_stone_floor"] = 2048,
            ["dark_wooden_planks"] = 1024, ["brown_mud_leaves_01"] = 2048,
            ["forest_leaves_04"] = 1024,
            // hero props
            ["wine_barrel_01"] = 1024, ["dead_quiver_trunk"] = 1024,
            ["dead_tree_trunk"] = 1024, ["dead_tree_trunk_02"] = 1024,
            ["rock_moss_set_01"] = 1024, ["tree_stump_01"] = 1024,
            ["wooden_crate_01"] = 1024, ["small_wooden_table_01"] = 1024,
            ["wooden_bookshelf_worn"] = 1024,
            // foliage cards are small + night-dark: 512 reads fine
            ["grass_medium_02"] = 512, ["fern_02"] = 512,
            // minor props
            ["wooden_stool_02"] = 512, ["wooden_bucket_01"] = 512,
            ["jug_01"] = 512, ["root_cluster_01"] = 512,
            ["namaqualand_boulder_05"] = 512, ["dry_branches_medium_01"] = 512,
            ["candle_flame"] = 256,
        };
        // Normal maps: 1k only where candlelight rakes a big close surface;
        // swamp props live ≥3 m away in moonlight — 256 is invisible there.
        private static readonly Dictionary<string, int> NrmSize = new Dictionary<string, int>
        {
            ["medieval_blocks_05"] = 1024, ["monastery_stone_floor"] = 1024,
            ["dark_wooden_planks"] = 512, ["brown_mud_leaves_01"] = 1024,
            ["forest_leaves_04"] = 256,
            ["wine_barrel_01"] = 512, ["dead_quiver_trunk"] = 256,
            ["dead_tree_trunk"] = 256, ["dead_tree_trunk_02"] = 256,
            ["rock_moss_set_01"] = 256, ["tree_stump_01"] = 256,
            ["wooden_crate_01"] = 512, ["small_wooden_table_01"] = 512,
            ["wooden_bookshelf_worn"] = 512,
            ["wooden_stool_02"] = 256, ["wooden_bucket_01"] = 256,
            ["jug_01"] = 256, ["root_cluster_01"] = 256,
            ["namaqualand_boulder_05"] = 256, ["dry_branches_medium_01"] = 256,
        };
        // BC7 for the two surfaces the player studies up close (candle-raked
        // wall + floor); BC1/BC3 elsewhere — the night mud ground survives BC1.
        private static readonly HashSet<string> HqAlbedo = new HashSet<string>
        {
            "medieval_blocks_05", "monastery_stone_floor",
        };

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
        }

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
            m.SetVector("_DirDir", xf.InverseTransformDirection(rig.dirWorld.normalized));
            m.SetColor("_DirCol", rig.dirCol);
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

        /// <summary>Place an imported prop with its own lit material; drops it so
        /// bounds.min.y lands on pos.y (photoscan pivots are arbitrary). The
        /// light rig is applied later via FlushRig (deferred, see Pending).</summary>
        private static GameObject Prop(Transform parent, string goName, string meshName,
            string texBase, Vector3 pos, float yaw, float scale,
            string matPrefix, float tintMul = 1f, float bump = 1f, Vector3? euler3 = null,
            bool cutout = false, Vector3? scale3 = null, float sink = 0.015f)
        {
            var mesh = ImpMesh(meshName);
            var mat = NewRoomMat($"{matPrefix}_{goName}.mat",
                cutout ? "GloomhavenVR/EnvRoomCutout" : "GloomhavenVR/EnvRoom");
            mat.SetTexture("_MainTex", Imp(texBase + "_alb"));
            var nrmPath = ImpTex + "/" + texBase + "_nrm.jpg";
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
            if (nrm != null) mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", bump);
            if (cutout) mat.SetFloat("_Cutoff", 0.35f);

            Vector3 sc = scale3 ?? Vector3.one * scale;
            var e = euler3 ?? new Vector3(0, yaw, 0);
            var go = Place(parent, goName, mesh, pos, e, sc, mat);
            // drop to ground: rotate/scale-aware via renderer bounds
            var b = go.GetComponent<Renderer>().bounds; // world == room space at build time
            go.transform.localPosition += new Vector3(0, pos.y - b.min.y - sink, 0);
            Defer(mat, go.transform, tintMul);
            return go;
        }

        private static float TopOf(GameObject go) => go.GetComponent<Renderer>().bounds.max.y;

        // ======================================================= procedural mesh
        private static Mesh SaveMesh(string file, Mesh src)
        {
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

        /// <summary>Flat polar disc with rim alpha fade (water). Pivot center.</summary>
        private static Mesh WaterDisc(float radius, int rings, int segs)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var cols = new List<Color>(); var tri = new List<int>();
            v.Add(Vector3.zero); uv.Add(Vector2.zero); cols.Add(new Color(1, 1, 1, 1));
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * r / rings;
                float alpha = r >= rings ? 0f : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, r / (float)rings));
                for (int s = 0; s < segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr));
                    uv.Add(new Vector2(v[v.Count - 1].x, v[v.Count - 1].z));
                    cols.Add(new Color(1, 1, 1, alpha));
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

        // ================================================================ CELLAR
        // ~10.5 x 9 m weathered stone cellar, beamed plank ceiling, barred night
        // window (StarDome visible through it), stair alcove rising into darkness,
        // barrels/crates/table/shelf props, three candle groups = the light rig.
        private const float CW = 10.5f, CD = 9.0f, CH = 3.3f;   // room extents
        private static readonly Rect WindowHole = new Rect(6.0f, 2.15f, 1.15f, 0.7f);  // in N-wall local x/y
        private static readonly Rect StairHole = new Rect(6.2f, 0f, 1.6f, 2.35f);      // in W-wall local x/y

        public static void BuildCellarRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);

            // Light positions are patched in AFTER the props are stacked (the
            // candles sit ON the props — bounds-derived); see rig fixup below.
            var rig = new LightRig
            {
                // iteration 4: cooler + more contrast — 3 was uniformly amber
                // (candle pool covered the whole room); sky ambient cooled, floor
                // bounce warmed (lifts beam sides/undersides out of pure black),
                // window moonlight strengthened for a cool counter-tone.
                ambUp = new Color(0.058f, 0.064f, 0.086f),
                ambDown = new Color(0.048f, 0.040f, 0.032f),
                dirWorld = new Vector3(0.25f, 0.62f, 0.74f), // in through the N window
                dirCol = new Color(0.060f, 0.075f, 0.110f),
                points = new[]
                {
                    new PLight(new Vector3(3.55f, 1.06f, 3.10f), 6.0f, new Color(1f, 0.62f, 0.33f) * 1.35f, 0.30f), // table candles
                    new PLight(new Vector3(4.72f, 2.00f, 0.70f), 5.5f, new Color(1f, 0.58f, 0.28f) * 1.0f, 0.35f), // shelf candle
                    new PLight(new Vector3(-1.55f, 1.30f, -3.95f), 6.5f, new Color(1f, 0.58f, 0.28f) * 1.15f, 0.35f), // crate candle
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
            var floorMesh = SaveMesh("Env_C_Floor.asset", GridMeshXZ(-hw, -hd, hw, hd, 30, 26,
                (x, z) => 0.012f * Fbm2(x * 0.8f, z * 0.8f, 3, 901) - 0.006f, null, 2.6f));
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

            // ---- window bars ----
            var barMesh = SaveMesh("Env_C_Bar.asset", LatheMesh(new[]
            { new Vector2(0.021f, 0f), new Vector2(0.021f, 0.8f) }, 8));
            var barMat = NewRoomMat("C_Bars.mat", "GloomhavenVR/EnvRoom");
            barMat.SetColor("_Tint", new Color(0.16f, 0.15f, 0.14f));
            for (int i = 0; i < 4; i++)
            {
                float wx = -hw + WindowHole.xMin + WindowHole.width * (i + 0.5f) / 4f;
                var bar = Place(root, "WindowBar" + i, barMesh,
                    new Vector3(wx, WindowHole.yMin - 0.05f, hd - 0.05f), Vector3.zero, Vector3.one, barMat);
                if (i == 0) Defer(barMat, bar.transform, 1f);
            }

            // ---- stair alcove behind W doorway: steps up into darkness ----
            var stepMesh = SaveMesh("Env_C_Step.asset", BoxMesh(1.5f, 0.19f, 0.34f, 1.9f));
            for (int i = 0; i < 6; i++)
            {
                var s = Place(root, "Step" + i, stepMesh,
                    new Vector3(-hw - 0.17f - 0.30f * i, 0.19f * i, -hd + StairHole.xMin + StairHole.width / 2f),
                    new Vector3(0, 90, 0), Vector3.one, null);
                s.GetComponent<MeshRenderer>().sharedMaterial =
                    SurfMat($"C_Step{i}.mat", "monastery_stone_floor", 1.9f, s.transform, 1.0f, Mathf.Lerp(0.9f, 0.4f, i / 5f));
            }
            // alcove shaft (walls + ceiling + pitch-black end cap)
            var shaftMesh = SaveMesh("Env_C_Shaft.asset", BuildShaft(2.2f, 2.6f, StairHole.width));
            var shaftGo = Place(root, "StairShaft", shaftMesh,
                new Vector3(-hw, 0, -hd + StairHole.xMin), Vector3.zero, Vector3.one, null);
            shaftGo.GetComponent<MeshRenderer>().sharedMaterial =
                SurfMat("C_Shaft.mat", "medieval_blocks_05", 3.4f, shaftGo.transform, 1.0f, 0.6f);
            var capMat = NewRoomMat("C_ShaftCap.mat", "GloomhavenVR/EnvRoom");
            capMat.SetColor("_Tint", Color.black);
            var capMesh = SaveMesh("Env_C_ShaftCap.asset", BoxMesh(StairHole.width, 2.6f, 0.05f, 1f));
            Place(root, "ShaftCap", capMesh,
                new Vector3(-hw - 2.15f, 0.6f, -hd + StairHole.xMin + StairHole.width / 2f),
                new Vector3(0, 90, 0), Vector3.one, capMat);

            // ---- props (crates were in the stair doorway in iteration 1 —
            // moved to the S wall; everything stacked via real bounds now) ----
            Prop(root, "Barrel0", "wine_barrel_01", "wine_barrel_01", new Vector3(-3.7f, 0, -3.2f), 15, 1f, "C");
            Prop(root, "Barrel1", "wine_barrel_01", "wine_barrel_01", new Vector3(-4.25f, 0, -2.0f), 152, 1f, "C");
            Prop(root, "Barrel2", "wine_barrel_01", "wine_barrel_01", new Vector3(-2.85f, 0, -3.95f), 80, 0.92f, "C",
                euler3: new Vector3(0, 80, 90)); // on its side
            var crate0 = Prop(root, "Crate0", "wooden_crate_01", "wooden_crate_01", new Vector3(-1.55f, 0, -3.95f), 8, 1f, "C");
            var crate1 = Prop(root, "Crate1", "wooden_crate_01", "wooden_crate_01", new Vector3(-0.45f, 0, -4.05f), -12, 0.9f, "C");
            var crate2 = Prop(root, "Crate2", "wooden_crate_01", "wooden_crate_01",
                new Vector3(-1.55f, TopOf(crate0), -3.95f), 38, 0.82f, "C", sink: 0.005f);
            var table = Prop(root, "Table", "small_wooden_table_01", "small_wooden_table_01", new Vector3(3.6f, 0, 3.15f), -28, 1.1f, "C");
            Prop(root, "Stool", "wooden_stool_02", "wooden_stool_02", new Vector3(2.55f, 0, 2.3f), 40, 1f, "C");
            var shelf = Prop(root, "Shelf", "wooden_bookshelf_worn", "wooden_bookshelf_worn", new Vector3(4.86f, 0, 0.7f), -90, 1f, "C");
            Prop(root, "Bucket", "wooden_bucket_01", "wooden_bucket_01", new Vector3(-4.0f, 0, -4.05f), 0, 1f, "C");
            float tableTop = TopOf(table), crateTop = TopOf(crate2), shelfTop = TopOf(shelf);
            Prop(root, "Jug0", "jug_01", "jug_01", new Vector3(3.32f, tableTop, 3.42f), 65, 1f, "C", sink: 0.002f);
            Prop(root, "Jug1", "jug_01", "jug_01", new Vector3(-0.45f, TopOf(crate1), -4.05f), 10, 0.9f, "C", sink: 0.002f);

            // ---- candles: lathe wax + flame cards + warm glow, ON the props ----
            var flameTexMat = FlameMat();
            var glowMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/FX_GlowWarm.mat");
            var glowMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/Env_GlowSphere.asset");
            void CandleGroup(string n, Vector3 basePos, (float h, float dx, float dz)[] candles, float glowR)
            {
                var wax = NewRoomMat($"C_Wax{n}.mat", "GloomhavenVR/EnvRoom");
                wax.SetColor("_Tint", new Color(0.94f, 0.86f, 0.70f));
                bool rigged = false;
                int ci = 0;
                foreach (var c in candles)
                {
                    var mesh = SaveMesh($"Env_C_Candle{n}{ci}.asset", CandleMesh(c.h, 0.016f, 400 + ci * 17));
                    var go = Place(root, $"Candle{n}{ci}", mesh, basePos + new Vector3(c.dx, 0, c.dz), Vector3.zero, Vector3.one, wax);
                    if (!rigged) { Defer(wax, go.transform, 1f); rigged = true; }
                    var fm = SaveMesh($"Env_Flame.asset", CrossQuadMesh(0.045f, 0.085f));
                    Place(root, $"Flame{n}{ci}", fm, basePos + new Vector3(c.dx, c.h + 0.002f, c.dz), Vector3.zero, Vector3.one, flameTexMat);
                    ci++;
                }
                if (glowMat != null && glowMesh != null)
                {
                    Place(root, $"CandleGlow{n}", glowMesh,
                        basePos + new Vector3(candles[0].dx, candles[0].h + 0.05f, candles[0].dz),
                        Vector3.zero, Vector3.one * glowR, glowMat);
                }
            }
            var candleTable = new Vector3(3.72f, tableTop, 2.95f);
            var candleShelf = new Vector3(4.72f, shelfTop, 0.70f);
            var candleCrate = new Vector3(-1.55f, crateTop, -3.95f);
            CandleGroup("Table", candleTable,
                new[] { (0.16f, 0f, 0f), (0.11f, 0.07f, 0.04f), (0.085f, -0.05f, 0.06f) }, 0.30f);
            CandleGroup("Shelf", candleShelf, new[] { (0.12f, 0f, 0f) }, 0.24f);
            CandleGroup("Crate", candleCrate, new[] { (0.14f, 0f, 0f), (0.09f, 0.06f, -0.05f) }, 0.27f);

            // rig fixup: light sources sit just above the tallest flame of each group
            rig.points[0].pos = candleTable + new Vector3(0, 0.22f, 0);
            rig.points[1].pos = candleShelf + new Vector3(0, 0.18f, 0);
            rig.points[2].pos = candleCrate + new Vector3(0, 0.20f, 0);
            FlushRig(rig);

            Debug.Log("[GloomhavenVR][Env] Cellar room geometry assembled.");
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

        private static Material FlameMat()
        {
            var m = NewRoomMat("C_Flame.mat", "GloomhavenVR/EnvFlame");
            m.SetTexture("_MainTex", Imp("candle_flame_alb"));
            m.SetColor("_Tint", new Color(1f, 0.82f, 0.55f, 1f));
            m.SetFloat("_Sway", 0.045f);
            m.SetFloat("_Flicker", 0.4f);
            return m;
        }

        // ================================================================= SWAMP
        // ~11 m clearing: mud/leaf-litter heightfield fading into darkness at
        // ~20 m, three moonlit ponds, a ring of dead trees / stumps / fallen
        // logs / mossy rocks, reeds & ferns at the pond rims, two leaning
        // standing stones as the ruined remnant. Existing shell FX (fog,
        // fireflies, shooting stars, star dome) complete the night.
        private static readonly (Vector3 c, float r, float depth)[] Ponds =
        {
            (new Vector3(4.6f, 0, 3.9f), 2.3f, 0.45f),
            (new Vector3(-5.6f, 0, -4.4f), 1.8f, 0.42f),
            (new Vector3(-0.4f, 0, -7.8f), 1.5f, 0.38f),
        };

        private const float WaterY = -0.09f; // pond water level (room space)

        private static float SwampHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 4.5f, r)); // flat play space
            // noise is flattened near ponds so the water always sits in a real pit
            // (iteration-2 lesson: hummocks poked through the discs -> floating
            // grey slabs at grazing view angles)
            float pondMask = 0f;
            foreach (var p in Ponds)
            {
                float d2 = (x - p.c.x) * (x - p.c.x) + (z - p.c.z) * (z - p.c.z);
                pondMask = Mathf.Max(pondMask, Mathf.Exp(-d2 / (p.r * p.r * 1.2f)));
            }
            float h = lift * (1f - pondMask) *
                      (0.55f * Fbm2(x * 0.10f + 31f, z * 0.10f, 3, 921)
                       + 0.16f * Fbm2(x * 0.45f, z * 0.45f, 3, 922)
                       - 0.32f);
            foreach (var p in Ponds)
            {
                float d2 = (x - p.c.x) * (x - p.c.x) + (z - p.c.z) * (z - p.c.z);
                h -= p.depth * Mathf.Exp(-d2 / (p.r * p.r * 0.7f));
            }
            // low IRREGULAR berm toward the rim: hides the disc edge as a wavy
            // distant treeline silhouette (iteration 3's tall uniform berm cut
            // the sky as a dead-flat black band — the waviness is the point)
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 30f, r))
                 * (1.1f + 0.9f * Fbm2(x * 0.09f + 11f, z * 0.09f, 3, 951));
            return h;
        }

        private static float PondBlend(float x, float z)
        {
            float b = 0;
            foreach (var p in Ponds)
            {
                float d2 = (x - p.c.x) * (x - p.c.x) + (z - p.c.z) * (z - p.c.z);
                b = Mathf.Max(b, Mathf.Exp(-d2 / (p.r * p.r * 1.6f)));
            }
            return b;
        }

        public static void BuildSwampRoom(Transform shellRoot)
        {
            var root = new GameObject("RoomGeo").transform;
            root.SetParent(shellRoot, false);

            var rig = new LightRig
            {
                // iterations 2+3: raised twice — 1 was silhouettes on black,
                // 2 still crushed the mid-ground. VR headsets render darker
                // than these gamma-encoded previews; err brighter.
                ambUp = new Color(0.130f, 0.158f, 0.210f),
                ambDown = new Color(0.032f, 0.040f, 0.034f),
                dirWorld = MoonDir,
                dirCol = new Color(0.38f, 0.45f, 0.57f),
                points = new[]
                {
                    // faint bioluminescent lift over the big pond (fireflies hover there)
                    new PLight(new Vector3(4.6f, 0.5f, 3.9f), 6.0f, new Color(0.10f, 0.20f, 0.13f), 0.15f),
                },
            };

            // ---- ground: closed 30 m disc, mud→leaves blend, radial fade ----
            Color GroundColor(float x, float z)
            {
                float r = Mathf.Sqrt(x * x + z * z);
                float mud = Mathf.Clamp01(0.35f + 0.9f * PondBlend(x, z)
                            + 0.55f * (Fbm2(x * 0.16f + 57f, z * 0.16f, 3, 931) - 0.5f)
                            - 0.25f * Mathf.InverseLerp(4f, 12f, r));
                float fade = Mathf.SmoothStep(1f, 0.05f, Mathf.InverseLerp(12f, 26f, r));
                return new Color(fade, fade, fade, 1f - mud); // a: 0=mud tex, 1=leaves tex
            }
            var groundMesh = SaveMesh("Env_S_Ground.asset", PolarGround(30f, 44, 96, SwampHeight, GroundColor, 3.6f));
            var g = Place(root, "Ground", groundMesh, Vector3.zero, Vector3.zero, Vector3.one, null);
            var gm = NewRoomMat("S_Ground.mat", "GloomhavenVR/EnvGround");
            gm.SetTexture("_MainTex", Imp("brown_mud_leaves_01_alb"));
            gm.SetTexture("_BumpMap", Imp("brown_mud_leaves_01_nrm"));
            gm.SetTexture("_MainTex2", Imp("forest_leaves_04_alb"));
            gm.SetTexture("_BumpMap2", Imp("forest_leaves_04_nrm"));
            gm.SetFloat("_BumpScale", 1.0f);
            Defer(gm, g.transform, 1f);
            g.GetComponent<MeshRenderer>().sharedMaterial = gm;

            // ---- ponds ----
            var rippleTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GenTexDir + "/Env_Ripple.png");
            var waterMat = NewRoomMat("S_Water.mat", "GloomhavenVR/EnvWater");
            if (rippleTex != null) waterMat.SetTexture("_RippleTex", rippleTex);
            waterMat.SetVector("_MoonDir", MoonDir);
            waterMat.SetColor("_MoonCol", new Color(0.30f, 0.31f, 0.27f));
            waterMat.SetColor("_SkyCol", new Color(0.035f, 0.050f, 0.075f));
            waterMat.SetFloat("_GlintPow", 260f);
            waterMat.SetFloat("_RippleScale", 0.25f);
            int pi = 0;
            foreach (var p in Ponds)
            {
                // disc stays INSIDE the flattened pit (0.85 r vs pit reach ~1.3 r)
                var wm = SaveMesh($"Env_S_Water{pi}.asset", WaterDisc(p.r * 0.85f, 6, 40));
                Place(root, "Pond" + pi, wm, new Vector3(p.c.x, WaterY, p.c.z), Vector3.zero, Vector3.one, waterMat);
                pi++;
            }

            // ---- tree ring / deadfall / rocks (fade darker with distance) ----
            float Fade(Vector3 pos) => Mathf.SmoothStep(1f, 0.15f, Mathf.InverseLerp(9f, 22f, new Vector2(pos.x, pos.z).magnitude));
            GameObject SProp(string n, string mesh, string tex, Vector3 pos, float yaw, float scale,
                Vector3? e3 = null, Vector3? s3 = null, bool cutout = false, float bump = 1f, float sink = 0.05f,
                float tintExtra = 1f)
            {
                pos.y = SwampHeight(pos.x, pos.z);
                return Prop(root, n, mesh, tex, pos, yaw, scale, "S",
                    tintMul: Fade(pos) * tintExtra, euler3: e3, scale3: s3, cutout: cutout, bump: bump, sink: sink);
            }

            // standing dead trees — quiver trunks widened non-uniformly so they
            // read as gnarled swamp snags, not telephone poles (iteration-1
            // lesson); tinted down + BROWNED: the pale quiver bark (and a sawn
            // pale ring in the scan) glowed ghost-white under the moon
            // (iteration-3/4 lessons — a colored tint mutes the ring too)
            var treeTint = new Color(0.62f, 0.55f, 0.44f);
            void Tint(GameObject go, Color c) =>
                go.GetComponent<Renderer>().sharedMaterial.SetColor("_Tint", c);
            Tint(SProp("Tree0", "dead_quiver_trunk", "dead_quiver_trunk", new Vector3(7.4f, 0, 1.6f), 15, 1f,
                s3: new Vector3(1.9f, 1.35f, 1.7f), tintExtra: 0.55f), treeTint);
            Tint(SProp("Tree1", "dead_quiver_trunk", "dead_quiver_trunk", new Vector3(-6.9f, 0, 5.3f), 150, 1f,
                s3: new Vector3(1.6f, 1.1f, 1.8f), e3: new Vector3(3f, 150f, -4f), tintExtra: 0.55f), treeTint);
            Tint(SProp("Tree2", "dead_quiver_trunk", "dead_quiver_trunk", new Vector3(2.3f, 0, -8.8f), 250, 1f,
                s3: new Vector3(2.1f, 1.55f, 1.9f), e3: new Vector3(4f, 250f, -3f), tintExtra: 0.55f), treeTint);
            Tint(SProp("Tree3", "dead_quiver_trunk", "dead_quiver_trunk", new Vector3(-8.9f, 0, -2.0f), 78, 1f,
                s3: new Vector3(1.7f, 1.25f, 1.5f), e3: new Vector3(-3f, 78f, 6f), tintExtra: 0.55f), treeTint);
            // fallen logs
            SProp("Log0", "dead_tree_trunk", "dead_tree_trunk", new Vector3(7.9f, 0, -4.6f), -25, 1.0f);
            SProp("Log1", "dead_tree_trunk_02", "dead_tree_trunk_02", new Vector3(-3.6f, 0, 8.1f), 105, 1.0f);
            // stump + roots
            SProp("Stump0", "tree_stump_01", "tree_stump_01", new Vector3(3.4f, 0, 6.1f), 60, 1.1f);
            SProp("Roots0", "root_cluster_01", "root_cluster_01", new Vector3(6.2f, 0, 2.7f), 190, 0.85f);
            // mossy rocks (iteration 3: 1.0 scale made them read as black boulders
            // dominating the clearing — the set is several metres wide)
            SProp("Rocks0", "rock_moss_set_01", "rock_moss_set_01", new Vector3(1.7f, 0, 7.4f), 30, 0.62f);
            SProp("Rocks1", "rock_moss_set_01", "rock_moss_set_01", new Vector3(-7.9f, 0, 0.9f), 245, 0.5f);
            // ruined remnant: pair of leaning standing stones
            SProp("Menhir0", "namaqualand_boulder_05", "namaqualand_boulder_05", new Vector3(6.6f, 0, 5.7f), 20, 1f,
                e3: new Vector3(0, 20f, 7f), s3: new Vector3(0.55f, 1.9f, 0.7f), sink: 0.25f, tintExtra: 0.7f);
            SProp("Menhir1", "namaqualand_boulder_05", "namaqualand_boulder_05", new Vector3(7.6f, 0, 4.6f), 130, 1f,
                e3: new Vector3(-6f, 130f, 0), s3: new Vector3(0.5f, 1.4f, 0.6f), sink: 0.3f, tintExtra: 0.7f);
            // deadfall branches
            SProp("Branches0", "dry_branches_medium_01", "dry_branches_medium_01", new Vector3(-2.2f, 0, -6.9f), 80, 1.0f);
            // reeds & ferns at pond rims (tri budget: grass is ~7.8k/patch)
            SProp("Grass0", "grass_medium_02", "grass_medium_02", new Vector3(3.1f, 0, 4.6f), 0, 1.25f, cutout: true, sink: 0.03f);
            SProp("Grass1", "grass_medium_02", "grass_medium_02", new Vector3(-4.4f, 0, -5.6f), 260, 1.3f, cutout: true, sink: 0.03f);
            SProp("Fern0", "fern_02", "fern_02", new Vector3(-6.3f, 0, 4.3f), 0, 1.1f, cutout: true, sink: 0.03f);
            SProp("Fern1", "fern_02", "fern_02", new Vector3(4.6f, 0, 6.5f), 200, 1.0f, cutout: true, sink: 0.03f);

            FlushRig(rig);
            Debug.Log("[GloomhavenVR][Env] Swamp room geometry assembled.");
        }

        // ============================================================== textures
        /// <summary>Ripple normal for EnvWater — small tiling value-noise normal
        /// map (generated, deterministic).</summary>
        public static void GenerateRippleTexture()
        {
            const int n = 256;
            string path = GenTexDir + "/Env_Ripple.png";
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (float)n, fy = y / (float)n;
                    // tileable: sample noise on a torus via 4-corner blend
                    float H(float ox, float oy) =>
                        Fbm2((fx + ox) * 6f, (fy + oy) * 6f, 4, 941);
                    float bx = fx, by = fy;
                    float h00 = H(0, 0), h10 = H(-1, 0), h01 = H(0, -1), h11 = H(-1, -1);
                    float h = Mathf.Lerp(Mathf.Lerp(h00, h10, bx), Mathf.Lerp(h01, h11, bx), by);
                    px[y * n + x] = new Color(h, h, h, 1);
                }
            // heights -> normals
            var nrm = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float hl = px[y * n + (x + n - 1) % n].r, hr = px[y * n + (x + 1) % n].r;
                    float hd = px[((y + n - 1) % n) * n + x].r, hu = px[((y + 1) % n) * n + x].r;
                    var v = new Vector3((hl - hr) * 2.2f, (hd - hu) * 2.2f, 1f).normalized;
                    nrm[y * n + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1);
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels(nrm);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.NormalMap;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 256;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }
    }
}
