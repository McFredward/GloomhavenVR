// GloomhavenVR companion project — ambient environment assembler ("VRChat-world" style
// non-interactive surroundings for the play table).
//
// Menu:  GloomhavenVR > Build Environments
// Batch: Unity -batchmode -nographics -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll -logFile env-build.log
//        (do NOT pass -quit; BuildAll exits itself. Does NOT build the game bundle.)
//
// Produces, deterministically (re-run => identical output):
//   Assets/Bundle/Environments/Env_Cellar.prefab  — cozy nerd DnD dungeon cellar
//   Assets/Bundle/Environments/Env_Swamp.prefab   — night swamp: star dome, shooting
//                                                   stars, ground fog, fireflies, moon
// plus all procedural textures/meshes/materials they reference.
//
// Hard VR rules honoured throughout (user requirement):
//  - everything is WORLD-anchored: real geometry, world/local-simulated Shuriken
//    particles on static anchors — nothing camera-attached, no screen-space effects.
//  - no MonoBehaviours in the bundle: all animation is Shuriken or shader-_Time-driven.
//  - all materials use bundled GloomhavenVR/Env* shaders (never builtin Standard —
//    the pink-material trap, TOOLCHAIN.md §4.1). Imported FBX materials are REMAPPED
//    by name onto the bundled palette materials.
//  - no colliders: the environments must never intercept game/mod raycasts.
//  - budget: each environment well under ~150k triangles, particle systems few and
//    small (see the alive-count log at the end of each Build*).
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
        private const string DungeonDir = Root + "/Models/Dungeon";
        private const string NatureDir = Root + "/Models/Nature";

        private const float FloorY = -0.02f;   // cellar walk surface (below real 0 => no z-fight)
        private const float WaterY = -0.06f;   // swamp water surface

        // Moon bearing (shared by moon quad, water glint and the swamp light rig).
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
            ImportModels();
            AssetDatabase.SaveAssets();
            BuildCellar();
            BuildSwamp();
            AssetDatabase.SaveAssets();
        }

        // ================================================================ textures
        // All procedural, seeded => deterministic. No external downloads.
        private static void GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);

            WritePng(TexDir + "/Env_Spark.png", MakeSpark(64), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Flame.png", MakeFlame(128), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(256), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Noise.png", MakeNoise(256), sRGB: false, clamp: false);
            WritePng(TexDir + "/Env_Stars.png", MakeStars(2048, 1024), sRGB: false, clamp: false, clampV: true);
            AssetDatabase.Refresh();
        }

        private static Color[] MakeSpark(int n)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r2 = (dx * dx + dy * dy) * 4f; // 0..1 at edge
                    float a = Mathf.Exp(-r2 * 9f) + 0.35f * Mathf.Exp(-r2 * 2.5f);
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
                }
            return px;
        }

        private static Color[] MakeFlame(int n)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n; // v=0 bottom
                    float w = 0.30f * (1.05f - v * 0.72f);        // narrows toward tip
                    float dx = (u - 0.5f) / Mathf.Max(w, 1e-3f);
                    float core = Mathf.Exp(-dx * dx * 2.2f);
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.22f, v));
                    float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.97f, v));
                    float a = Mathf.Clamp01(core * rise * fall);
                    // hot core brighter in the middle-bottom
                    float hot = Mathf.Clamp01(core * (1f - v) * 1.4f);
                    px[y * n + x] = new Color(1f, Mathf.Lerp(0.75f, 1f, hot), Mathf.Lerp(0.45f, 0.9f, hot), a);
                }
            return px;
        }

        private static Color[] MakeFogPuff(int n)
        {
            var rnd = new System.Random(1101);
            float[,] noise = ValueNoiseGrid(n, new[] { 4, 8, 16 }, rnd, tile: false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 1 at edge
                    float blob = Mathf.Exp(-r * r * 3.0f);
                    float edge = Mathf.Pow(Mathf.Clamp01(1.12f - r * 1.12f), 1.4f);
                    float a = Mathf.Clamp01(blob * (0.5f + 0.5f * noise[x, y]) * edge);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            return px;
        }

        private static Color[] MakeMoon(int n)
        {
            var rnd = new System.Random(2202);
            // a few soft crater spots
            var craters = new List<Vector3>(); // x,y,radius (uv units)
            for (int i = 0; i < 7; i++)
                craters.Add(new Vector3(
                    0.5f + ((float)rnd.NextDouble() - 0.5f) * 0.42f,
                    0.5f + ((float)rnd.NextDouble() - 0.5f) * 0.42f,
                    0.02f + (float)rnd.NextDouble() * 0.045f));
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = 0f, halo = 0f;
                    if (r < 0.30f)
                    {
                        core = 1f - 0.22f * Mathf.Pow(r / 0.30f, 2f); // limb darkening
                        foreach (var c in craters)
                        {
                            float cd = Mathf.Sqrt((u - c.x) * (u - c.x) + (v - c.y) * (v - c.y));
                            core -= 0.16f * Mathf.Exp(-(cd * cd) / (c.z * c.z));
                        }
                        core = Mathf.Clamp01(core);
                    }
                    else halo = 0.40f * Mathf.Exp(-(r - 0.30f) * 7.5f);
                    float a = Mathf.Clamp01(core + halo);
                    px[y * n + x] = new Color(1f, 0.97f, 0.90f, a);
                }
            return px;
        }

        private static Color[] MakeNoise(int n)
        {
            var rnd = new System.Random(3303);
            float[,] g = ValueNoiseGrid(n, new[] { 4, 8, 16, 32 }, rnd, tile: true);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = g[x, y];
                    px[y * n + x] = new Color(v, v, v, 1f);
                }
            return px;
        }

        private static Color[] MakeStars(int w, int h)
        {
            var rnd = new System.Random(4404);
            var px = new Color[w * h]; // starts black (0,0,0,0)
            const int starCount = 1500;
            for (int i = 0; i < starCount; i++)
            {
                // uniform on the sphere -> equirect
                float su = (float)rnd.NextDouble();
                float sy = (float)rnd.NextDouble() * 2f - 1f;         // dir.y uniform
                float vAng = Mathf.Asin(sy);                          // -pi/2..pi/2
                float cx = su * w;
                float cy = (vAng / Mathf.PI + 0.5f) * h;
                bool bright = i < 40;
                float sigma = bright ? 1.6f + (float)rnd.NextDouble() * 1.2f
                                     : 0.7f + (float)rnd.NextDouble() * 0.9f;
                float amp = bright ? 0.85f + (float)rnd.NextDouble() * 0.15f
                                   : 0.30f + (float)rnd.NextDouble() * 0.55f;
                float phase = (float)rnd.NextDouble();
                int rad = Mathf.CeilToInt(sigma * 3f);
                for (int oy = -rad; oy <= rad; oy++)
                {
                    int yy = (int)cy + oy;
                    if (yy < 0 || yy >= h) continue;
                    for (int ox = -rad; ox <= rad; ox++)
                    {
                        int xx = ((int)cx + ox + w) % w; // wrap U
                        float d2 = (ox * ox + oy * oy) / (sigma * sigma);
                        float val = amp * Mathf.Exp(-d2 * 0.7f);
                        int idx = yy * w + xx;
                        if (val > px[idx].r)
                            px[idx] = new Color(val, phase, 0f, 1f);
                    }
                }
            }
            return px;
        }

        /// <summary>Multi-octave value noise, optionally tileable, normalized 0..1.</summary>
        private static float[,] ValueNoiseGrid(int n, int[] octaves, System.Random rnd, bool tile)
        {
            var acc = new float[n, n];
            float ampSum = 0f, amp = 1f;
            foreach (int cells in octaves)
            {
                var grid = new float[cells, cells];
                for (int gy = 0; gy < cells; gy++)
                    for (int gx = 0; gx < cells; gx++)
                        grid[gx, gy] = (float)rnd.NextDouble();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float fx = (float)x / n * cells, fy = (float)y / n * cells;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                        int x1 = tile ? (x0 + 1) % cells : Mathf.Min(x0 + 1, cells - 1);
                        int y1 = tile ? (y0 + 1) % cells : Mathf.Min(y0 + 1, cells - 1);
                        float v = Mathf.Lerp(
                            Mathf.Lerp(grid[x0, y0], grid[x1, y0], tx),
                            Mathf.Lerp(grid[x0, y1], grid[x1, y1], tx), ty);
                        acc[x, y] += v * amp;
                    }
                ampSum += amp; amp *= 0.55f;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    acc[x, y] /= ampSum;
            return acc;
        }

        private static void WritePng(string path, Color[] px, bool sRGB, bool clamp, bool clampV = false)
        {
            int n2 = px.Length;
            int w = (int)Mathf.Sqrt(n2), h = w;
            if (w * h != n2) { w = 2048; h = 1024; } // the only non-square texture (stars)
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = sRGB;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapModeU = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.wrapModeV = (clamp || clampV) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }

        // ================================================================== meshes
        private static void GenerateMeshes()
        {
            Directory.CreateDirectory(MeshDir);
            SaveMesh(MeshDir + "/Env_Box.asset", BuildBox());
            SaveMesh(MeshDir + "/Env_Quad.asset", BuildQuad());
            SaveMesh(MeshDir + "/Env_Disc.asset", BuildDisc(48));
            SaveMesh(MeshDir + "/Env_Dome.asset", BuildSphere(32, 16, inward: true));
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

        private static Mesh BuildBox()
        {
            var m = new Mesh();
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Face(Vector3 c, Vector3 r, Vector3 u, Vector3 n)
            {
                int b = v.Count;
                v.Add(c - r - u); v.Add(c + r - u); v.Add(c + r + u); v.Add(c - r + u);
                for (int i = 0; i < 4; i++) nrm.Add(n);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            const float s = 0.5f;
            Face(new Vector3(0, 0, -s), Vector3.right * s, Vector3.up * s, Vector3.back);
            Face(new Vector3(0, 0, s), Vector3.left * s, Vector3.up * s, Vector3.forward);
            Face(new Vector3(-s, 0, 0), Vector3.back * s, Vector3.up * s, Vector3.left);
            Face(new Vector3(s, 0, 0), Vector3.forward * s, Vector3.up * s, Vector3.right);
            Face(new Vector3(0, s, 0), Vector3.right * s, Vector3.forward * s, Vector3.up);
            Face(new Vector3(0, -s, 0), Vector3.right * s, Vector3.back * s, Vector3.down);
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            return m;
        }

        private static Mesh BuildQuad()
        {
            var m = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) },
                normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 } // faces -Z
            };
            return m;
        }

        private static Mesh BuildDisc(int seg)
        {
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
                uv.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            var t = new List<int>();
            for (int i = 1; i <= seg; i++) t.AddRange(new[] { 0, i + 1, i });
            var m = new Mesh();
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetNormals(v.Select(_ => Vector3.up).ToList());
            m.SetTriangles(t, 0);
            return m;
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
        // Two light rigs, baked per material (EnvLit is scene-light independent).
        private struct Rig { public Color ambient, key, fill; public Vector3 keyDir, fillDir; }

        private static readonly Rig CellarRig = new Rig
        {
            ambient = new Color(0.17f, 0.13f, 0.10f),
            key = new Color(0.95f, 0.72f, 0.45f), keyDir = new Vector3(0.25f, 1f, 0.15f),
            fill = new Color(0.10f, 0.11f, 0.17f), fillDir = new Vector3(-0.5f, 0.25f, -0.4f),
        };

        private static readonly Rig SwampRig = new Rig
        {
            ambient = new Color(0.055f, 0.075f, 0.115f),
            key = new Color(0.38f, 0.48f, 0.68f), keyDir = MoonDir,
            fill = new Color(0.03f, 0.05f, 0.08f), fillDir = new Vector3(-0.5f, 0.2f, -0.6f),
        };

        private struct MatDef { public Color albedo; public Color emission; public MatDef(Color a, Color e = default) { albedo = a; emission = e; } }

        // Dungeon-pack material names (parsed from the FBX set) -> cellar palette.
        private static readonly Dictionary<string, MatDef> CellarPalette = new Dictionary<string, MatDef>
        {
            { "Rock",       new MatDef(new Color(0.46f, 0.44f, 0.42f)) },
            { "RockLight",  new MatDef(new Color(0.58f, 0.55f, 0.50f)) },
            { "Wood",       new MatDef(new Color(0.42f, 0.26f, 0.15f)) },
            { "DarkSteel",  new MatDef(new Color(0.16f, 0.16f, 0.18f)) },
            { "Steel",      new MatDef(new Color(0.50f, 0.50f, 0.55f)) },
            { "Gold",       new MatDef(new Color(0.85f, 0.66f, 0.28f)) },
            { "Black",      new MatDef(new Color(0.09f, 0.09f, 0.10f)) },
            { "Candle",     new MatDef(new Color(0.93f, 0.87f, 0.72f), new Color(0.30f, 0.19f, 0.07f)) },
            { "Fire",       new MatDef(new Color(1.0f, 0.55f, 0.15f),  new Color(1.1f, 0.55f, 0.18f)) },
            { "Bones",      new MatDef(new Color(0.78f, 0.74f, 0.64f)) },
            { "Cover",      new MatDef(new Color(0.42f, 0.16f, 0.12f)) },
            { "Ink",        new MatDef(new Color(0.10f, 0.10f, 0.11f)) },
            { "Paper",      new MatDef(new Color(0.82f, 0.77f, 0.66f)) },
            { "Red",        new MatDef(new Color(0.45f, 0.12f, 0.10f)) },
            { "Blue",       new MatDef(new Color(0.25f, 0.45f, 0.85f), new Color(0.05f, 0.12f, 0.35f)) },
            { "Cork",       new MatDef(new Color(0.55f, 0.42f, 0.28f)) },
            { "Glass",      new MatDef(new Color(0.65f, 0.75f, 0.80f)) },
            { "Liquid",     new MatDef(new Color(0.65f, 0.18f, 0.50f), new Color(0.20f, 0.04f, 0.15f)) },
            { "Yellow",     new MatDef(new Color(0.85f, 0.70f, 0.20f), new Color(0.25f, 0.18f, 0.03f)) },
            { "Grey",       new MatDef(new Color(0.40f, 0.40f, 0.42f)) },
            { "Ice",        new MatDef(new Color(0.70f, 0.85f, 0.95f), new Color(0.08f, 0.15f, 0.20f)) },
        };

        // Nature-pack material names -> moonlit swamp palette.
        private static readonly Dictionary<string, MatDef> SwampPalette = new Dictionary<string, MatDef>
        {
            { "Green",           new MatDef(new Color(0.14f, 0.24f, 0.15f)) },
            { "DarkGreen",       new MatDef(new Color(0.07f, 0.13f, 0.09f)) },
            { "Wood",            new MatDef(new Color(0.20f, 0.14f, 0.11f)) },
            { "Rock",            new MatDef(new Color(0.24f, 0.26f, 0.31f)) },
            { "White",           new MatDef(new Color(0.60f, 0.63f, 0.68f)) },
            { "Black",           new MatDef(new Color(0.06f, 0.06f, 0.07f)) },
            { "Mushroom_Top",    new MatDef(new Color(0.10f, 0.30f, 0.34f), new Color(0.10f, 0.42f, 0.45f)) },
            { "Mushroom_Bottom", new MatDef(new Color(0.45f, 0.42f, 0.36f), new Color(0.06f, 0.05f, 0.03f)) },
            { "LightWood",       new MatDef(new Color(0.34f, 0.27f, 0.20f)) },
            { "Pink",            new MatDef(new Color(0.50f, 0.22f, 0.36f), new Color(0.15f, 0.04f, 0.10f)) },
            { "Berry",           new MatDef(new Color(0.38f, 0.10f, 0.14f)) },
        };

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

        private static Material EnvLitMat(string path, MatDef def, Rig rig)
        {
            var m = LoadOrNewMat(path, "GloomhavenVR/EnvLit");
            m.SetColor("_Color", def.albedo);
            m.SetColor("_Emission", def.emission);
            m.SetColor("_AmbientCol", rig.ambient);
            m.SetColor("_KeyCol", rig.key);
            m.SetVector("_KeyDir", rig.keyDir);
            m.SetColor("_FillCol", rig.fill);
            m.SetVector("_FillDir", rig.fillDir);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static string PaletteMatPath(string prefix, string name) => $"{MatDir}/{prefix}_{name}.mat";

        private static void BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);

            foreach (var kv in CellarPalette) EnvLitMat(PaletteMatPath("Cellar", kv.Key), kv.Value, CellarRig);
            foreach (var kv in SwampPalette) EnvLitMat(PaletteMatPath("Swamp", kv.Key), kv.Value, SwampRig);

            // Bespoke solids
            EnvLitMat(MatDir + "/Cellar_Ceiling.mat", new MatDef(new Color(0.05f, 0.045f, 0.04f)), CellarRig);
            EnvLitMat(MatDir + "/Swamp_Mud.mat", new MatDef(new Color(0.10f, 0.085f, 0.07f)), SwampRig);

            // FX materials
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + n);

            var fog = LoadOrNewMat(MatDir + "/FX_Fog.mat", "GloomhavenVR/EnvParticleAlpha");
            fog.SetTexture("_MainTex", T("Env_FogPuff.png"));
            fog.SetColor("_Tint", Color.white);

            var dust = LoadOrNewMat(MatDir + "/FX_Dust.mat", "GloomhavenVR/EnvParticleAlpha");
            dust.SetTexture("_MainTex", T("Env_Spark.png"));
            dust.SetColor("_Tint", Color.white);

            var flame = LoadOrNewMat(MatDir + "/FX_Flame.mat", "GloomhavenVR/EnvParticleAdd");
            flame.SetTexture("_MainTex", T("Env_Flame.png"));
            flame.SetColor("_Tint", Color.white);

            var firefly = LoadOrNewMat(MatDir + "/FX_Firefly.mat", "GloomhavenVR/EnvParticleAdd");
            firefly.SetTexture("_MainTex", T("Env_Spark.png"));
            firefly.SetColor("_Tint", Color.white);

            var streak = LoadOrNewMat(MatDir + "/FX_StarStreak.mat", "GloomhavenVR/EnvParticleAdd");
            streak.SetTexture("_MainTex", T("Env_Spark.png"));
            streak.SetColor("_Tint", Color.white);

            var moon = LoadOrNewMat(MatDir + "/FX_Moon.mat", "GloomhavenVR/EnvParticleAdd");
            moon.SetTexture("_MainTex", T("Env_Moon.png"));
            moon.SetColor("_Tint", new Color(1f, 0.98f, 0.92f, 1f));

            var glowWarm = LoadOrNewMat(MatDir + "/FX_GlowWarm.mat", "GloomhavenVR/EnvGlow");
            glowWarm.SetColor("_Tint", new Color(1f, 0.55f, 0.20f, 0.35f));
            glowWarm.SetFloat("_Falloff", 2.6f);

            var glowWindow = LoadOrNewMat(MatDir + "/FX_GlowWindow.mat", "GloomhavenVR/EnvGlow");
            glowWindow.SetColor("_Tint", new Color(0.40f, 0.60f, 1.0f, 0.28f));
            glowWindow.SetFloat("_Falloff", 2.0f);

            var stars = LoadOrNewMat(MatDir + "/Swamp_StarDome.mat", "GloomhavenVR/EnvStars");
            stars.SetTexture("_MainTex", T("Env_Stars.png"));

            var water = LoadOrNewMat(MatDir + "/Swamp_Water.mat", "GloomhavenVR/EnvWater");
            water.SetTexture("_NoiseTex", T("Env_Noise.png"));
            water.SetVector("_GlintDir", MoonDir);

            AssetDatabase.SaveAssets();
        }

        // ================================================================= imports
        private static IEnumerable<string> AllModelPaths()
        {
            foreach (var dir in new[] { DungeonDir, NatureDir })
                foreach (var f in Directory.GetFiles(dir, "*.fbx").OrderBy(p => p, StringComparer.Ordinal))
                    yield return f.Replace('\\', '/');
        }

        private static void ImportModels()
        {
            foreach (var path in AllModelPaths())
            {
                bool dungeon = path.Contains("/Dungeon/");
                var palette = dungeon ? CellarPalette : SwampPalette;
                string prefix = dungeon ? "Cellar" : "Swamp";

                var imp = (ModelImporter)AssetImporter.GetAtPath(path)
                          ?? throw new FileNotFoundException("No importer for " + path);
                imp.useFileScale = true;
                imp.globalScale = 1f;
                imp.bakeAxisConversion = true;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.addCollider = false;
                imp.isReadable = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.SaveAndReimport();

                // Source material names: embedded sub-assets (first run) + already
                // remapped identifiers (re-runs) — union keeps this idempotent.
                var names = new HashSet<string>(
                    AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name));
                foreach (var kv in imp.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material))
                        names.Add(kv.Key.name);

                foreach (var srcName in names.OrderBy(n => n, StringComparer.Ordinal))
                {
                    string key = System.Text.RegularExpressions.Regex.Replace(srcName, @"\.\d+$", "");
                    Material target;
                    if (palette.ContainsKey(key))
                        target = AssetDatabase.LoadAssetAtPath<Material>(PaletteMatPath(prefix, key));
                    else
                    {
                        Debug.LogWarning($"[GloomhavenVR][Env] {Path.GetFileName(path)}: material '{srcName}' not in palette — grey fallback.");
                        target = AssetDatabase.LoadAssetAtPath<Material>(PaletteMatPath(prefix, dungeon ? "Grey" : "Rock"));
                    }
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), srcName), target);
                }
                imp.SaveAndReimport();
            }
        }

        // ============================================================ scene helpers
        private enum Snap { None, Bottom }

        private static GameObject ModelGO(string pack, string name)
        {
            string path = (pack == "D" ? DungeonDir : NatureDir) + "/" + name + ".fbx";
            return AssetDatabase.LoadAssetAtPath<GameObject>(path)
                   ?? throw new Exception("Model not found: " + path);
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static GameObject Place(Transform parent, string pack, string name,
            Vector3 pos, float rotY = 0f, float scale = 1f, Snap snap = Snap.Bottom)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(ModelGO(pack, name));
            inst.transform.SetParent(parent, false);
            inst.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.localPosition = pos;
            if (snap == Snap.Bottom)
            {
                var b = RendererBounds(inst); // parent sits at origin => world == local
                inst.transform.localPosition += new Vector3(0, pos.y - b.min.y, 0);
            }
            return inst;
        }

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

        private static Material Mat(string file) =>
            AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + file)
            ?? throw new Exception("Material not found: " + file);

        private static void Glow(Transform parent, Vector3 pos, float radius, string matFile)
        {
            Solid(parent, "Glow", "Env_GlowSphere.asset", Mat(matFile), pos, Vector3.zero, Vector3.one * radius);
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

        private static void LogModelBounds()
        {
            foreach (var path in AllModelPaths())
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
                var b = RendererBounds(inst);
                long tris = 0;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                Debug.Log($"[GloomhavenVR][Env] MODEL {Path.GetFileNameWithoutExtension(path)}: size={b.size:F2} min={b.min:F2} max={b.max:F2} tris={tris}");
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }

        // ================================================================== CELLAR
        private static void BuildCellar()
        {
            LogModelBounds();

            var root = new GameObject("Env_Cellar");
            try
            {
                var t = root.transform;
                const float half = 4f; // room is 8x8 m

                // ---- measured module sizes drive the grid (unit-safe) ----
                var floorB = MeasureModel("D", "ModularFloor");
                var wallB = MeasureModel("D", "ModularStoneWall");
                float fw = Mathf.Max(floorB.size.x, 0.25f);
                float fd = Mathf.Max(floorB.size.z, 0.25f);
                float ww = Mathf.Max(wallB.size.x, wallB.size.z);
                float wh = wallB.size.y;
                Debug.Log($"[GloomhavenVR][Env] Cellar grid: floor {fw:F2}x{fd:F2}, wall w={ww:F2} h={wh:F2}");

                // ---- floor ----
                int nx = Mathf.CeilToInt(2 * half / fw), nz = Mathf.CeilToInt(2 * half / fd);
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float px = -((nx - 1) * fw) / 2f + ix * fw;
                        float pz = -((nz - 1) * fd) / 2f + iz * fd;
                        Place(t, "D", "ModularFloor", new Vector3(px, FloorY, pz));
                    }

                // ---- walls (entrance replaces the middle segment of the +Z side) ----
                int segs = Mathf.Max(1, Mathf.RoundToInt(2 * half / ww));
                int rows = wh < 2.6f ? 2 : 1;
                int entranceSeg = segs / 2;
                foreach (var (side, rotY) in new[] { ("N", 180f), ("S", 0f), ("E", 270f), ("W", 90f) })
                {
                    for (int i = 0; i < segs; i++)
                    {
                        float along = -((segs - 1) * ww) / 2f + i * ww;
                        Vector3 pos = side switch
                        {
                            "N" => new Vector3(along, FloorY, half),
                            "S" => new Vector3(along, FloorY, -half),
                            "E" => new Vector3(half, FloorY, along),
                            _ => new Vector3(-half, FloorY, along),
                        };
                        bool isEntrance = side == "N" && i == entranceSeg;
                        for (int row = 0; row < rows; row++)
                        {
                            float y = FloorY + row * wh;
                            if (isEntrance && row == 0)
                                Place(t, "D", "Entrance", new Vector3(pos.x, y, pos.z), rotY);
                            else
                                Place(t, "D", "ModularStoneWall", new Vector3(pos.x, y, pos.z), rotY);
                        }
                        Place(t, "D", "ModularStoneWall_top", new Vector3(pos.x, FloorY + rows * wh, pos.z), rotY);
                    }
                }
                float wallTopY = FloorY + rows * wh;

                // ---- corner columns ----
                foreach (var c in new[] { new Vector2(3.45f, 3.45f), new Vector2(-3.45f, 3.45f), new Vector2(3.45f, -3.45f), new Vector2(-3.45f, -3.45f) })
                    Place(t, "D", "Column", new Vector3(c.x, FloorY, c.y));

                // ---- stairs beyond the entrance, shrouded in darkness ----
                Place(t, "D", "Stairs", new Vector3(0, FloorY, 5.4f), 180f);
                Solid(t, "StairFloor", "Env_Box.asset", Mat("Cellar_Rock.mat"),
                    new Vector3(0, FloorY - 0.10f, 5.5f), Vector3.zero, new Vector3(3.4f, 0.2f, 3.4f));
                // shroud: three dark slabs + roof enclosing the stairwell so the view
                // up the stairs fades into black instead of showing void.
                var shroudMat = Mat("Cellar_Ceiling.mat");
                Solid(t, "ShroudBack", "Env_Box.asset", shroudMat, new Vector3(0, 2.0f, 7.3f), Vector3.zero, new Vector3(4.2f, 5.4f, 0.2f));
                Solid(t, "ShroudL", "Env_Box.asset", shroudMat, new Vector3(-2.0f, 2.0f, 5.9f), Vector3.zero, new Vector3(0.2f, 5.4f, 3.0f));
                Solid(t, "ShroudR", "Env_Box.asset", shroudMat, new Vector3(2.0f, 2.0f, 5.9f), Vector3.zero, new Vector3(0.2f, 5.4f, 3.0f));
                Solid(t, "ShroudTop", "Env_Box.asset", shroudMat, new Vector3(0, 4.6f, 5.9f), Vector3.zero, new Vector3(4.2f, 0.2f, 3.2f));

                // ---- ceiling: open beams + darkness above ----
                float beamY = Mathf.Clamp(wallTopY - 0.15f, 3.0f, 3.9f);
                var beamMat = Mat("Cellar_Wood.mat");
                for (float bx = -3.2f; bx <= 3.21f; bx += 1.6f)
                    Solid(t, "Beam", "Env_Box.asset", beamMat, new Vector3(bx, beamY, 0), Vector3.zero, new Vector3(0.22f, 0.30f, 8.3f));
                Solid(t, "CeilingDark", "Env_Box.asset", Mat("Cellar_Ceiling.mat"),
                    new Vector3(0, beamY + 0.65f, 0), Vector3.zero, new Vector3(8.6f, 0.06f, 8.6f));

                // ---- carpet under the (real) table ----
                Place(t, "D", "Carpet", new Vector3(0, FloorY + 0.012f, 0), 90f, 1.4f, Snap.None);

                // ---- prop dressing (explicit layout; y = ground unless noted) ----
                float g = FloorY;
                // SW: barrel corner with book & potions
                var barrel1 = Place(t, "D", "Barrel", new Vector3(-3.05f, g, -2.75f), 10f);
                var barrel2 = Place(t, "D", "Barrel", new Vector3(-2.35f, g, -3.05f), 55f);
                Place(t, "D", "Barrel", new Vector3(-2.90f, g, -2.05f), 90f);
                float b1Top = RendererBounds(barrel1).max.y;
                float b2Top = RendererBounds(barrel2).max.y;
                Place(t, "D", "Book_Open", new Vector3(-3.05f, b1Top, -2.75f), 25f);
                Place(t, "D", "Potion2", new Vector3(-2.45f, b2Top, -3.10f), 0f, 1f);
                Place(t, "D", "Potion4", new Vector3(-2.28f, b2Top, -2.98f), 40f, 1f);
                Place(t, "D", "Potion6", new Vector3(-2.36f, b2Top, -3.22f), 75f, 1f);

                // E: chests with book stack and a candle
                var chest = Place(t, "D", "Chest", new Vector3(3.05f, g, -2.55f), -75f);
                Place(t, "D", "Chest_gold", new Vector3(3.20f, g, -1.55f), -95f);
                float chestTop = RendererBounds(chest).max.y;
                Place(t, "D", "Book2", new Vector3(3.05f, chestTop, -2.60f), 30f);
                Place(t, "D", "Book3", new Vector3(3.03f, chestTop + 0.06f, -2.58f), 65f);
                var candle1 = Place(t, "D", "Candle", new Vector3(3.15f, g, -3.35f), 0f);

                // NE/NW: bones, rubble
                Place(t, "D", "Bones", new Vector3(2.75f, g, 3.05f), 70f);
                Place(t, "D", "Bones2", new Vector3(-3.00f, g, 2.75f), -30f);
                Place(t, "D", "Rock1", new Vector3(-3.35f, g, 3.15f), 15f);
                Place(t, "D", "Rock3", new Vector3(3.40f, g, 2.60f), 120f);
                Place(t, "D", "WallRocks", new Vector3(-1.35f, g, -3.85f), 0f);

                // candelabra flanking the play space (outside the 1.5 m free radius)
                var cand1 = Place(t, "D", "Candelabrum_tall", new Vector3(-1.95f, g, -1.30f), 20f);
                var cand2 = Place(t, "D", "Candelabrum_tall", new Vector3(1.95f, g, 1.05f), -140f);
                var cand3 = Place(t, "D", "Candelabrum", new Vector3(-1.75f, g, 1.80f), 0f);

                // window (south wall) with cold moonlight glow
                Place(t, "D", "Window", new Vector3(1.4f, 2.1f, -3.97f), 0f, 1f, Snap.None);
                Glow(t, new Vector3(1.4f, 2.1f, -4.1f), 0.5f, "FX_GlowWindow.mat");

                // wall torches, E and W walls
                var torchDefs = new[]
                {
                    (pos: new Vector3(-3.92f, 1.75f, 1.7f), rot: 90f),
                    (pos: new Vector3(-3.92f, 1.75f, -1.7f), rot: 90f),
                    (pos: new Vector3(3.92f, 1.75f, 1.7f), rot: 270f),
                    (pos: new Vector3(3.92f, 1.75f, -1.7f), rot: 270f),
                };
                foreach (var td in torchDefs)
                {
                    var torch = Place(t, "D", "Torch_wall", td.pos, td.rot, 1f, Snap.None);
                    var tb = RendererBounds(torch);
                    var flamePos = new Vector3(tb.center.x, tb.max.y - 0.02f, tb.center.z);
                    FlamePS(t, flamePos, big: true);
                    Glow(t, flamePos + Vector3.up * 0.06f, 0.30f, "FX_GlowWarm.mat");
                }

                // candle flames on the candelabra
                foreach (var cnd in new[] { cand1, cand2, cand3 })
                {
                    var cb = RendererBounds(cnd);
                    var fp = new Vector3(cb.center.x, cb.max.y - 0.01f, cb.center.z);
                    FlamePS(t, fp, big: false);
                    Glow(t, fp + Vector3.up * 0.05f, 0.18f, "FX_GlowWarm.mat");
                }
                var c1b = RendererBounds(candle1);
                FlamePS(t, new Vector3(c1b.center.x, c1b.max.y - 0.01f, c1b.center.z), big: false);

                // drifting dust motes in the candlelight (world-space, big room volume)
                var dust = NewPS(t, "DustMotes", new Vector3(0, 1.8f, 0), Vector3.zero, Mat("FX_Dust.mat"));
                var dm = dust.main;
                dm.simulationSpace = ParticleSystemSimulationSpace.World;
                dm.duration = 30f;
                dm.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
                dm.startSpeed = 0f;
                dm.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
                dm.startColor = new Color(1f, 0.88f, 0.65f, 0.40f);
                dm.maxParticles = 30;
                var de = dust.emission; de.rateOverTime = 1.6f;
                var dsh = dust.shape; dsh.enabled = true; dsh.shapeType = ParticleSystemShapeType.Box; dsh.scale = new Vector3(6.5f, 3.2f, 6.5f);
                var dn = dust.noise; dn.enabled = true; dn.strength = 0.03f; dn.frequency = 0.25f; dn.scrollSpeed = 0.1f;
                var dcol = dust.colorOverLifetime; dcol.enabled = true;
                dcol.color = new ParticleSystem.MinMaxGradient(Grad((0f, new Color(1, 1, 1, 0f)), (0.15f, new Color(1, 1, 1, 1f)), (0.85f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));

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

        private static Bounds MeasureModel(string pack, string name)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(ModelGO(pack, name));
            var b = RendererBounds(inst);
            UnityEngine.Object.DestroyImmediate(inst);
            return b;
        }

        private static void FlamePS(Transform parent, Vector3 pos, bool big)
        {
            var ps = NewPS(parent, big ? "TorchFlame" : "CandleFlame", pos, new Vector3(-90, 0, 0), Mat("FX_Flame.mat"));
            var m = ps.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local; // static anchor => still world-fixed
            m.duration = 5f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            m.startSize = big ? new ParticleSystem.MinMaxCurve(0.10f, 0.17f) : new ParticleSystem.MinMaxCurve(0.045f, 0.075f);
            m.startColor = Color.white;
            m.maxParticles = big ? 14 : 8;
            var e = ps.emission; e.rateOverTime = big ? 16f : 9f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 7f; sh.radius = big ? 0.03f : 0.012f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(
                (0f, new Color(1f, 0.92f, 0.55f, 0f)),
                (0.12f, new Color(1f, 0.85f, 0.45f, 0.95f)),
                (0.55f, new Color(1f, 0.50f, 0.12f, 0.75f)),
                (1f, new Color(0.55f, 0.12f, 0.03f, 0f))));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard; // stays world-up: no head-roll flame tilt
        }

        // =================================================================== SWAMP
        private static void BuildSwamp()
        {
            var root = new GameObject("Env_Swamp");
            try
            {
                var t = root.transform;

                // ---- sky: star dome (shader-twinkled), moon, water ----
                Solid(t, "StarDome", "Env_Dome.asset", Mat("Swamp_StarDome.mat"),
                    Vector3.zero, Vector3.zero, Vector3.one * 45f);
                var moonPos = MoonDir * 38f;
                var moon = Solid(t, "Moon", "Env_Quad.asset", Mat("FX_Moon.mat"),
                    moonPos, Vector3.zero, Vector3.one * 6.5f);
                moon.transform.rotation = Quaternion.LookRotation(-moonPos.normalized); // quad faces -Z => face origin
                Solid(t, "Water", "Env_Disc.asset", Mat("Swamp_Water.mat"),
                    new Vector3(0, WaterY, 0), Vector3.zero, new Vector3(28f, 1f, 28f));

                // ---- player islet: gentle mud mound peeking out of the water ----
                Solid(t, "Islet", "Env_GlowSphere.asset", Mat("Swamp_Mud.mat"),
                    new Vector3(0, WaterY - 0.26f, 0), Vector3.zero, new Vector3(2.7f, 0.35f, 2.7f));
                // grass fringe on the islet rim (outside the 1.5 m free radius)
                var rimRnd = new System.Random(7707);
                for (int i = 0; i < 11; i++)
                {
                    float a = i / 11f * Mathf.PI * 2f + 0.2f;
                    float rr = 1.75f + (float)rimRnd.NextDouble() * 0.45f;
                    string grass = i % 3 == 0 ? "Grass_2" : (i % 3 == 1 ? "Grass" : "Grass_Short");
                    Place(t, "N", grass, new Vector3(Mathf.Cos(a) * rr, WaterY + 0.01f, Mathf.Sin(a) * rr),
                        (float)rimRnd.NextDouble() * 360f, 0.8f + (float)rimRnd.NextDouble() * 0.5f);
                }

                // ---- willow ring + dead trees (silhouettes against the sky) ----
                var trees = new (string model, float x, float z, float rot, float s)[]
                {
                    ("Willow_1", 6.5f, 3.5f, 15f, 1.20f),
                    ("Willow_2", -5.5f, 5.8f, 160f, 1.00f),
                    ("Willow_3", -7.5f, -3.0f, 75f, 1.30f),
                    ("Willow_1", 4.5f, -6.5f, 230f, 0.95f),
                    ("Willow_2", 8.5f, -1.0f, 310f, 1.10f),
                    ("CommonTree_Dead_1", 2.8f, 6.8f, 40f, 1.10f),
                    ("CommonTree_Dead_2", -3.5f, -6.0f, 200f, 1.00f),
                    ("BirchTree_Dead_1", -6.8f, 1.5f, 120f, 1.05f),
                    ("Willow_Dead_1", 7.0f, -5.0f, 20f, 1.15f),
                };
                // bottom-snapped 0.30 m BELOW the water line (roots submerged)
                foreach (var tr in trees)
                    Place(t, "N", tr.model, new Vector3(tr.x, WaterY - 0.30f, tr.z), tr.rot, tr.s);

                // ---- reed islands: moss rocks + grass + plants ----
                var rocks = new (string model, float x, float z, float rot, float s, float sink)[]
                {
                    ("Rock_Moss_1", 3.2f, 1.6f, 30f, 1.0f, 0.22f),
                    ("Rock_Moss_2", -2.9f, 3.3f, 100f, 1.1f, 0.25f),
                    ("Rock_Moss_3", 4.6f, -2.6f, 210f, 1.3f, 0.30f),
                    ("Rock_Moss_1", -4.3f, -2.1f, 280f, 1.6f, 0.35f),
                    ("Rock_Moss_2", 1.8f, 4.8f, 55f, 0.9f, 0.20f),
                };
                foreach (var rk in rocks)
                    Place(t, "N", rk.model, new Vector3(rk.x, WaterY - rk.sink, rk.z), rk.rot, rk.s);

                var reeds = new (string model, float x, float z, float rot, float s)[]
                {
                    ("Grass_2", 3.5f, 2.2f, 0f, 1.3f),
                    ("Grass", 3.0f, 1.1f, 80f, 1.2f),
                    ("Plant_1", 2.6f, 1.9f, 150f, 1.1f),
                    ("Grass_2", -3.3f, 3.8f, 210f, 1.4f),
                    ("Grass", -2.5f, 2.9f, 20f, 1.1f),
                    ("Grass_2", 5.0f, -3.1f, 120f, 1.5f),
                    ("Plant_1", 4.2f, -2.2f, 260f, 1.2f),
                    ("Grass", -4.7f, -2.7f, 300f, 1.3f),
                    ("Grass_2", -4.0f, -1.6f, 45f, 1.2f),
                    ("Bush_1", -5.2f, 4.6f, 90f, 1.0f),
                    ("Bush_1", 6.0f, 1.8f, 200f, 1.2f),
                    ("Grass_2", 1.9f, 5.3f, 330f, 1.2f),
                };
                foreach (var rd in reeds)
                    Place(t, "N", rd.model, new Vector3(rd.x, WaterY + 0.02f, rd.z), rd.rot, rd.s);

                // ---- mossy logs & stump with glowing mushrooms (near the player) ----
                Place(t, "N", "WoodLog_Moss", new Vector3(2.3f, WaterY - 0.03f, -2.6f), 25f, 1.2f);
                Place(t, "N", "TreeStump_Moss", new Vector3(-2.5f, WaterY - 0.02f, -2.3f), 0f, 1.1f);
                Place(t, "N", "WoodLog_Moss", new Vector3(-4.8f, WaterY - 0.05f, 3.9f), 120f, 1.4f);

                // ---- lilypads ----
                var lilies = new (float x, float z, float rot, float s)[]
                {
                    (2.4f, 3.6f, 10f, 1.0f), (3.6f, 4.2f, 90f, 1.3f), (5.2f, 0.6f, 200f, 0.9f),
                    (-2.2f, 4.6f, 45f, 1.1f), (-3.8f, 0.9f, 300f, 1.2f), (-5.6f, -3.8f, 150f, 1.0f),
                    (0.8f, -4.4f, 250f, 1.2f), (-1.6f, -3.9f, 70f, 0.9f), (4.0f, -4.6f, 330f, 1.1f),
                };
                foreach (var l in lilies)
                    Place(t, "N", "Lilypad", new Vector3(l.x, WaterY + 0.005f, l.z), l.rot, l.s, Snap.None);

                // ---- ground fog: big slow WORLD-SPACE puffs standing over the water ----
                var fog = NewPS(t, "GroundFog", new Vector3(0, 0.45f, 0), new Vector3(-90, 0, 0), Mat("FX_Fog.mat"));
                var fm = fog.main;
                fm.simulationSpace = ParticleSystemSimulationSpace.World;
                fm.duration = 40f;
                fm.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
                fm.startSpeed = 0f;
                fm.startSize = new ParticleSystem.MinMaxCurve(4.5f, 8.5f);
                fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                fm.startColor = new Color(0.62f, 0.72f, 0.85f, 0.13f);
                fm.maxParticles = 44;
                var fe = fog.emission; fe.rateOverTime = 2.2f;
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
                fr.maxParticleSize = 2.5f; // don't clamp big close puffs
                fr.sortMode = ParticleSystemSortMode.Distance;

                // ---- fireflies: two swarms near the logs/reeds ----
                FireflyPS(t, new Vector3(2.6f, 0.55f, -2.4f));
                FireflyPS(t, new Vector3(-3.6f, 0.6f, 3.4f));

                // ---- shooting stars: infrequent streaks across the sky ----
                var meteor = NewPS(t, "ShootingStars", new Vector3(0, 30f, 0), new Vector3(115f, 30f, 0f), Mat("FX_StarStreak.mat"));
                var mm = meteor.main;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                mm.duration = 20f;
                mm.startLifetime = 1.1f;
                mm.startSpeed = new ParticleSystem.MinMaxCurve(30f, 44f);
                mm.startSize = 0.35f;
                mm.startColor = new Color(0.92f, 0.96f, 1f, 1f);
                mm.maxParticles = 4;
                var me = meteor.emission; me.rateOverTime = 0.22f; // ~one every 4.5 s
                var msh = meteor.shape; msh.enabled = true; msh.shapeType = ParticleSystemShapeType.Box;
                msh.scale = new Vector3(36f, 36f, 0.1f); // spawn plane ⟂ travel direction
                var mcol = meteor.colorOverLifetime; mcol.enabled = true;
                mcol.color = new ParticleSystem.MinMaxGradient(Grad(
                    (0f, new Color(1, 1, 1, 0f)), (0.1f, new Color(1, 1, 1, 1f)),
                    (0.7f, new Color(1, 1, 1, 0.8f)), (1f, new Color(1, 1, 1, 0f))));
                var mr = meteor.GetComponent<ParticleSystemRenderer>();
                mr.renderMode = ParticleSystemRenderMode.Stretch;
                mr.velocityScale = 0.12f;
                mr.lengthScale = 1f;

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
            m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            m.startColor = new Color(0.72f, 1f, 0.35f, 1f);
            m.maxParticles = 24;
            var e = ps.emission; e.rateOverTime = 2.4f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.4f;
            var n = ps.noise; n.enabled = true; n.strength = 0.35f; n.frequency = 0.35f; n.scrollSpeed = 0.15f;
            // blink: size pulses over lifetime
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.1f), new Keyframe(0.12f, 1f), new Keyframe(0.28f, 0.15f),
                new Keyframe(0.45f, 1f), new Keyframe(0.62f, 0.1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(
                (0f, new Color(1, 1, 1, 0f)), (0.1f, new Color(1, 1, 1, 1f)),
                (0.9f, new Color(1, 1, 1, 1f)), (1f, new Color(1, 1, 1, 0f))));
        }
    }
}
