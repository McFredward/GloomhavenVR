// GloomhavenVR companion project — offscreen check for GloomhavenVR/WaterVR.
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.WaterVRPreview.RenderAll -logFile water-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device); on a
//   headless box wrap in `xvfb-run -a`. Output goes to $WATERVR_PREVIEW_OUT, or
//   ./water-previews when that is unset.
//
// ============================================================================
//  WHY THIS EXISTS, AND WHAT IT CAN AND CANNOT SETTLE
// ============================================================================
//  EnvironmentsPreview renders the mod's OWN Env_*.prefab shells. The water film
//  is not one of those: it is the GAME's TERRAIN_Water_Plane, placed by
//  Apparance inside a running scenario, and there is no game install on the
//  build machine (checked — no bundle, no StreamingAssets, nothing but the
//  managed DLLs). So the shipped surface cannot be rendered here.
//
//  WHAT CAN. The shader is a pure function of its properties, and every property
//  the driver writes is known: the hardware WATER SURFACE census read them off
//  TERRAIN_GEN_WaterPlane_Crypt_Mat. So this stages the same MATERIAL on the same
//  ARRANGEMENT of geometry — a grid of 1 m quads with UVs running 0..1 across
//  each, which is exactly how the game places its hex water — and renders it at
//  two instants of the shader clock. That answers, without a headset:
//    * does the shader COMPILE and DRAW (a pink quad or an empty frame is the
//      single most likely way a round like this is lost),
//    * does the surface MOVE, and by how much between two known times,
//    * does the glint travel with the WAVES rather than sit still,
//    * is the film translucent over the floor rather than a lighter sheet on
//      top of it (the ADDITIVE trap that would re-create the photographed
//      defect out of the mod's own shader),
//    * and — the one that no argument can replace — is the frame IDENTICAL when
//      only the CAMERA moves. Two frames are taken from two different positions
//      at the same clock; a view-dependent term would make the surface differ
//      between them, which is the same difference the two eyes see under
//      MultiPass.
//
//  WHAT IT CANNOT. The bump texture is NOT the game's. 'WaterBump' lives in the
//  game's own bundles and is not on this machine, so a stand-in is synthesised
//  below and every frame is named `_synthbump` to say so. The MOTION, the
//  tilings, the speeds, the tint, the blend and the view-independence are all
//  the shipped ones; the grain of the ripple is not, and no judgement about how
//  the pattern itself looks may be read off these frames.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class WaterVRPreview
    {
        private const string ShaderPath = "Assets/Bundle/Environments/WaterVR.shader";
        private const int W = 1280, H = 720;

        // ---- the authored material, read off the hardware WATER SURFACE census of
        //      TERRAIN_GEN_WaterPlane_Crypt_Mat on VFX/Water_Shd_Trans. These are the same
        //      numbers src/GloomhavenVR/Core/WaterOwnSurface.cs carries as its measured
        //      fallbacks; they are repeated here rather than shared because this project does
        //      not reference the mod's assembly, and check-mirrors.sh is not watching a Unity
        //      editor script. If they ever disagree, WaterOwnSurface is the authority.
        private static readonly Vector4 Tilings = new Vector4(0.14f, 6.00f, -0.12f, -0.20f);
        private static readonly Vector4 SpeedA = new Vector4(1.00f, 1.00f, 0.60f, 0f);
        private static readonly Vector4 SpeedB = new Vector4(0.50f, 1.00f, 1.00f, 0f);
        private static readonly Vector4 NoiseSpeed = new Vector4(1.00f, 1.00f, 1.00f, 0f);
        private static readonly Color AuthoredTint = new Color(0.195f, 0.311f, 0.131f, 0.737f);

        // [Water] Opacity's shipped default caps the alpha, exactly as the driver does.
        private const float OpacityCap = 0.45f;

        // WaterOwnSurface.NormalStrength(5,5,0,0) and the fixed light constant, mirrored for the
        // same reason as above.
        private const float RippleStrength = 1.2f;
        private static readonly Vector4 LightLocal =
            new Vector4(0.34f, 0.22f, 0.91f, 0f).normalized;

        private const float Smoothness = 0.754f;
        private const float Shimmer = 0.35f;

        /// <summary>The two instants. Half a second apart, because that is long enough for the
        /// slower layer to move a visible fraction of its own wavelength and short enough that a
        /// reader can still see it is the SAME water.</summary>
        private static readonly float[] Clocks = { 0f, 0.5f };

        /// <summary>Two camera stations at the same clock, and this pair is the measurement, not
        /// the scenery. A grazing seat across a VR table and a steep one over it: any
        /// view-dependent term in the shader would make the SAME water look different between
        /// them, which is exactly the difference the two eyes see under MultiPass.</summary>
        private static readonly (string Name, Vector3 Pos, Vector3 Look)[] Stations =
        {
            ("graze", new Vector3(0f, 0.80f, -3.20f), new Vector3(0f, 0f, 0.2f)),
            ("steep", new Vector3(0f, 3.20f, -1.10f), new Vector3(0f, 0f, 0.2f)),
        };

        public static void RenderAll()
        {
            try
            {
                Render();
                Debug.Log("[GloomhavenVR][WaterPreview] RenderAll OK");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][WaterPreview] RenderAll FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Render()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new Exception(
                    "No graphics device — run WITHOUT -nographics (use xvfb-run on headless).");

            string outDir = Environment.GetEnvironmentVariable("WATERVR_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "water-previews";
            Directory.CreateDirectory(outDir);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new Exception($"{ShaderPath} did not load as a Shader.");
            // A shader that fails to compile still LOADS; `isSupported` is what says whether the
            // graphics device got a usable program out of it. Checking it here turns the single
            // most likely way this round is lost — a compile error nobody read out of a 7000-line
            // batch log — into an exit code.
            if (!shader.isSupported)
                throw new Exception(
                    $"'{shader.name}' loaded but is NOT SUPPORTED on this device: it failed to "
                    + "compile. The shader errors are earlier in this log.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildFloor();
            Material water = BuildWaterMaterial(shader);
            BuildWaterGrid(water);

            var camGo = new GameObject("WaterPreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 100f;

            // ARGBHalf and a manual gamma encode, for the reason EnvironmentsPreview gives: an
            // 8-bit LINEAR target quantises at 1/255 linear, which after the encode is a first
            // step of 18/255, and every soft gradient arrives as contour bands that are not on
            // the headset.
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf,
                                       RenderTextureReadWrite.Linear);
            var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);

            void Shoot(string name, Vector3 pos, Vector3 look)
            {
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                var px = tex.GetPixels();
                for (int i = 0; i < px.Length; i++) { var c = px[i].gamma; c.a = 1f; px[i] = c; }
                tex.SetPixels(px);
                tex.Apply();
                string png = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Debug.Log($"[GloomhavenVR][WaterPreview] wrote {Path.GetFullPath(png)}");
            }

            // ---- the shipped path: the tileset's own two scrolling layers, two instants,
            //      two camera stations.
            water.SetFloat("_ProcNormal", 0f);
            foreach (float t in Clocks)
            {
                Shader.SetGlobalFloat("_GhvrTimeOfs", t);
                foreach (var (name, pos, look) in Stations)
                    Shoot($"watervr_synthbump_{name}_t{Mathf.RoundToInt(t * 100f):D3}", pos, look);
            }

            // ---- and the FALLBACK path, which is what is on screen when the game material
            //      carries no readable normal map. It has to move too; a still film there would
            //      be ModBuild 162's flat sheet arrived at silently.
            water.SetFloat("_ProcNormal", 1f);
            foreach (float t in Clocks)
            {
                Shader.SetGlobalFloat("_GhvrTimeOfs", t);
                Shoot($"watervr_procfallback_graze_t{Mathf.RoundToInt(t * 100f):D3}",
                      Stations[0].Pos, Stations[0].Look);
            }
            Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);

            cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
        }

        /// <summary>
        /// The basin bed under the film: a flat unlit checker in two stone tones.
        ///
        /// <para>IT IS A CHECKER ON PURPOSE. The film is transparent, and the two failures worth
        /// catching here are both about what is UNDER it: an additive blend would make the water
        /// LIGHTER than the stone (the photographed defect), and a film that hid the floor
        /// entirely would mean the alpha cap never landed. A flat colour would hide both; a
        /// pattern you can read through the water shows them at a glance.</para>
        /// </summary>
        private static void BuildFloor()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool a = ((x / 8) + (y / 8)) % 2 == 0;
                    float v = a ? 0.34f : 0.22f;
                    tex.SetPixel(x, y, new Color(v, v * 0.96f, v * 0.90f, 1f));
                }
            tex.Apply();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "BasinBed";
            floor.transform.position = new Vector3(0f, -0.03f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(20f, 20f, 1f);
            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(20f, 20f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// The film's material, carrying exactly what
        /// <c>WaterTerrainVR.OwnFilmOne</c>/<c>AnimateFilmOne</c> write at runtime.
        /// </summary>
        private static Material BuildWaterMaterial(Shader shader)
        {
            var m = new Material(shader) { name = "WaterVRPreview" };
            m.SetColor("_Color", new Color(AuthoredTint.r, AuthoredTint.g, AuthoredTint.b,
                                           Mathf.Min(AuthoredTint.a, OpacityCap)));
            m.SetTexture("_MainTex", null);
            m.SetTexture("_Normal_Map", SynthBump());
            m.SetVector("_NormalTilings", Tilings);
            // The same resolution WaterOwnSurface.ScrollRate performs: .xy is the per-axis rate,
            // .z a per-layer multiplier, _WaterNoiseSpeed.x the shared clock scale.
            m.SetVector("_WaterUVAnimSpeedA", Resolve(SpeedA));
            m.SetVector("_WaterUVAnimSpeedB", Resolve(SpeedB));
            m.SetFloat("_NormalStrength", RippleStrength);
            m.SetFloat("_ProcNormal", 0f);
            m.SetVector("_LightDir", LightLocal);
            m.SetFloat("_Shimmer", Shimmer);
            m.SetFloat("_Smoothness", Smoothness);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_ZTest", 4f);
            m.SetFloat("_SrcBlend", 5f);      // SrcAlpha
            m.SetFloat("_DstBlend", 10f);     // OneMinusSrcAlpha
            m.renderQueue = 2900;             // the authored queue
            return m;
        }

        private static Vector4 Resolve(Vector4 speed)
        {
            float layer = speed.z > 0f ? speed.z : 1f;
            float clock = NoiseSpeed.x > 0f ? NoiseSpeed.x : 1f;
            return new Vector4(speed.x * layer * clock, speed.y * layer * clock, 0f, 0f);
        }

        /// <summary>
        /// A 5x5 grid of 1 m quads with UVs running 0..1 across EACH, which is how the game
        /// places its hex water (17 separate TERRAIN_Water_Plane instances in the report's room)
        /// and therefore the arrangement the authored tilings were chosen against. One big quad
        /// would stretch the ripple over five metres and every judgement about its scale would be
        /// wrong by a factor of five.
        /// </summary>
        private static void BuildWaterGrid(Material water)
        {
            var root = new GameObject("WaterFilm");
            for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = $"TERRAIN_Water_Plane_{x}_{z}";
                    q.transform.SetParent(root.transform, false);
                    q.transform.position = new Vector3(x, 0f, z);
                    q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    q.GetComponent<MeshRenderer>().sharedMaterial = water;
                }
        }

        /// <summary>
        /// A STAND-IN for the game's 'WaterBump' 512x512, which is not on this machine.
        ///
        /// <para>Encoded the way a plain RGB normal map is — x in R, y in G, z in B, ALPHA 1 —
        /// so the shader's RG-or-AG unpack (<c>r * a</c>) recovers x under the same expression it
        /// uses for a DXT5nm import. Getting that wrong here would make the preview disagree with
        /// the headset for a reason that has nothing to do with the water.</para>
        /// </summary>
        private static Texture2D SynthBump()
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            {
                name = "WaterBumpStandIn",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            // A tileable height field: three crossing sine trains at integer periods (so the
            // texture wraps without a seam) at incommensurate directions, differentiated
            // analytically.
            float H0(float u, float v) =>
                Mathf.Sin(2f * Mathf.PI * (3f * u + 1f * v))
                + 0.7f * Mathf.Sin(2f * Mathf.PI * (-1f * u + 4f * v) + 1.7f)
                + 0.4f * Mathf.Sin(2f * Mathf.PI * (5f * u - 3f * v) + 3.1f);

            var px = new Color32[N * N];
            const float d = 1f / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x * d, v = y * d;
                    float h = H0(u, v);
                    float gx = (H0(u + d, v) - h) / d;
                    float gy = (H0(u, v + d) - h) / d;
                    var n = new Vector3(-gx * 0.012f, -gy * 0.012f, 1f).normalized;
                    px[y * N + x] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }
    }
}
