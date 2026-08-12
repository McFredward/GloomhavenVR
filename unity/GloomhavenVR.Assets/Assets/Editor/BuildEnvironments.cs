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
//   Assets/Bundle/Environments/Env_Swamp.prefab  — night-sky FX shell: star dome
//       (procedural star texture, EnvStars shader with baked moon + twinkle),
//       shooting-star streaks, two firefly swarms, ground-fog donut.
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
        private static void GenerateTextures()
        {
            Directory.CreateDirectory(TexDir);

            WritePng(TexDir + "/Env_Spark.png", MakeSpark(64), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_FogPuff.png", MakeFogPuff(256), sRGB: true, clamp: true);
            WritePng(TexDir + "/Env_Moon.png", MakeMoon(256), sRGB: true, clamp: true);
            // stars: NO mips + uncompressed, or the pinpricks smear into bokeh blobs
            WritePng(TexDir + "/Env_Stars.png", MakeStars(2048, 1024), sRGB: false, clamp: false, clampV: true,
                mips: false, compress: false);
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
                    // force alpha to 0 at the sprite border — residual 2% halo showed
                    // as a hard square against the night sky (iteration-4 lesson)
                    float edge = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.40f, 0.5f, r));
                    float a = Mathf.Clamp01(core + halo) * edge;
                    px[y * n + x] = new Color(1f, 0.97f, 0.90f, a);
                }
            return px;
        }

        private static Color[] MakeStars(int w, int h)
        {
            var rnd = new System.Random(4404);
            var px = new Color[w * h]; // starts black (0,0,0,0)
            const int starCount = 2200;
            for (int i = 0; i < starCount; i++)
            {
                // uniform on the sphere -> equirect
                float su = (float)rnd.NextDouble();
                float sy = (float)rnd.NextDouble() * 2f - 1f;         // dir.y uniform
                float vAng = Mathf.Asin(sy);                          // -pi/2..pi/2
                float cx = su * w;
                float cy = (vAng / Mathf.PI + 0.5f) * h;
                // mostly faint pinpricks, a sparse bright layer (sharp = tiny sigma;
                // the dome magnifies the texture ~4x on screen, fat gaussians read as
                // bokeh blobs — iteration-1 lesson)
                bool bright = i < 90;
                float sigma = bright ? 0.9f + (float)rnd.NextDouble() * 0.5f
                                     : 0.45f + (float)rnd.NextDouble() * 0.35f;
                float amp = bright ? 0.75f + (float)rnd.NextDouble() * 0.25f
                                   : 0.12f + (float)rnd.NextDouble() * 0.40f;
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
                        float val = amp * Mathf.Exp(-d2 * 1.6f);
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

        private static void WritePng(string path, Color[] px, bool sRGB, bool clamp, bool clampV = false,
            bool mips = true, bool compress = true)
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
            ti.mipmapEnabled = mips;
            ti.wrapModeU = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.wrapModeV = (clamp || clampV) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = compress ? TextureImporterCompression.Compressed
                                             : TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        // ================================================================== meshes
        private static void GenerateMeshes()
        {
            Directory.CreateDirectory(MeshDir);
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
            firefly.SetTexture("_MainTex", T("Env_Spark.png"));
            firefly.SetColor("_Tint", Color.white);

            var streak = LoadOrNewMat(MatDir + "/FX_StarStreak.mat", "GloomhavenVR/EnvParticleAdd");
            streak.SetTexture("_MainTex", T("Env_Spark.png"));
            streak.SetColor("_Tint", Color.white);

            // warm torch halo — used by the cellar shell's inactive GlowTemplate
            var glowWarm = LoadOrNewMat(MatDir + "/FX_GlowWarm.mat", "GloomhavenVR/EnvGlow");
            glowWarm.SetColor("_Tint", new Color(1f, 0.55f, 0.20f, 0.65f));
            glowWarm.SetFloat("_Falloff", 2.2f);

            // the moon is baked into the star-dome shader (a separate blended quad
            // left a visible seam against the sky gradient)
            var stars = LoadOrNewMat(MatDir + "/Swamp_StarDome.mat", "GloomhavenVR/EnvStars");
            stars.SetTexture("_MainTex", T("Env_Stars.png"));
            stars.SetColor("_TopCol", new Color(0.006f, 0.010f, 0.022f));
            stars.SetColor("_HorizonCol", new Color(0.030f, 0.048f, 0.080f));
            stars.SetFloat("_StarBoost", 1.8f);
            stars.SetTexture("_MoonTex", T("Env_Moon.png"));
            stars.SetVector("_MoonDir", MoonDir);
            stars.SetColor("_MoonCol", new Color(1f, 0.98f, 0.92f));
            stars.SetFloat("_MoonExtent", 0.075f);

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
                dm.startColor = new Color(1f, 0.88f, 0.65f, 0.40f);
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
                fm.startSize = new ParticleSystem.MinMaxCurve(6f, 11f);
                fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                fm.startColor = new Color(0.50f, 0.60f, 0.78f, 0.07f); // dim: mist, not snowdrifts
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
                // Stretch aligns the quad to the particle's WORLD velocity — the
                // head only picks the (invisible, sprite is radially symmetric)
                // roll around that axis. cameraVelocityScale pinned to 0 so no
                // camera-motion term can ever creep into the stretch.
                mr.renderMode = ParticleSystemRenderMode.Stretch;
                mr.velocityScale = 0.12f;
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
            m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
            m.startColor = new Color(0.72f, 1f, 0.35f, 1f);
            m.maxParticles = 34;
            var e = ps.emission; e.rateOverTime = 3.4f;
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
