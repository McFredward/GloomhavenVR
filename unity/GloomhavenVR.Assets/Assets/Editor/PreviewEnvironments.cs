// GloomhavenVR companion project — ambient-environment preview renderer.
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device); on a
//   headless box wrap in `xvfb-run -a`.
//
// For each Env_*.prefab (FX-only shells): loads it into an empty temp scene,
// fast-forwards every particle system 6 s (so fog banks, fireflies and — with
// luck — a shooting star are populated), then renders 5 views from the seated player position
// (0, 1.4, 0): N/E/S/W at 60° FOV horizontal plus one 30°-up view. 1280x720 PNGs go
// to $ENV_PREVIEW_OUT (or ./env-previews under the project when unset).
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class EnvironmentsPreview
    {
        private const string Root = "Assets/Bundle/Environments";
        private const int W = 1280, H = 720;

        // NOTE: declared BEFORE Views — C# initialises static fields in textual
        // order, so a Views table that referenced Eye from above would capture
        // (0,0,0) and render every view from inside the floor.
        private static readonly Vector3 Eye = new Vector3(0f, 1.4f, 0f); // seated player head

        // The mandatory self-review set. 4 yaws at seated eye height, the most
        // detailed corner of each room, two upward views (canopy / zenith), a
        // LOW pass that is the only way to catch a prop hovering a centimetre
        // over the floor, a shot from the clearing edge looking back across the
        // play space, and a sky-only frame with the room hidden.
        //
        // The moon bears 40.0 deg azimuth at 40.0 deg altitude
        // (EnvironmentsBuilder.MoonDir) — the Moon/MoonWide/TreeLine views are
        // aimed off that constant, so they follow it if it ever moves.
        private static readonly float MoonAz =
            Mathf.Atan2(EnvironmentsBuilder.MoonDir.x, EnvironmentsBuilder.MoonDir.z) * Mathf.Rad2Deg;
        private static readonly float MoonAlt =
            Mathf.Asin(EnvironmentsBuilder.MoonDir.y) * Mathf.Rad2Deg;

        private static readonly (string name, Vector3 pos, Vector3 euler, bool skyOnly, float fov)[] Views =
        {
            ("N", Eye, new Vector3(0, 0, 0), false, 60f),
            ("E", Eye, new Vector3(0, 90, 0), false, 60f),
            ("S", Eye, new Vector3(0, 180, 0), false, 60f),
            ("W", Eye, new Vector3(0, 270, 0), false, 60f),
            ("Corner", Eye, new Vector3(8, 48, 0), false, 60f),   // cellar: candle table NE; forest: toward the moon
            ("Up", Eye, new Vector3(-30, 45, 0), false, 60f),
            ("Canopy", Eye, new Vector3(-58, 20, 0), false, 60f),
            ("Zenith", Eye, new Vector3(-88, 0, 0), false, 60f),
            // low camera: floaters are invisible from 1.4 m and obvious from 0.45 m
            ("LowS", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 195, 0), false, 60f),
            ("LowN", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 25, 0), false, 60f),
            ("LowW", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 285, 0), false, 60f),
            // from the edge of the clearing, looking back over the play space
            ("Edge", new Vector3(3.6f, 1.4f, -3.6f), new Vector3(2, 315, 0), false, 60f),
            ("SkyOnly", Eye, new Vector3(-34, 40, 0), true, 60f),
            // ---- ModBuild 134 review set ----
            // sky-only wide, straight at the Milky Way's own half of the dome
            ("SkyBand", Eye, new Vector3(-42, 200, 0), true, 75f),
            // the moon, close: does the disc OCCLUDE the stars behind it?
            ("Moon", Eye, new Vector3(-MoonAlt, MoonAz, 0), false, 14f),
            ("MoonWide", Eye, new Vector3(-MoonAlt, MoonAz, 0), false, 45f),
            // ...and the same close-up with the room hidden, which is the only
            // frame in which the occlusion can actually be judged: from inside
            // the clearing the crowns cover most of the moon.
            ("MoonSky", Eye, new Vector3(-MoonAlt, MoonAz, 0), true, 11f),
            // straight INTO the tree line on the side AWAY from the moon — this
            // is the frame the "so dark you would not dare walk there" ruling is
            // judged on, and the one that shows a lifted horizon band if any is left
            ("TreeLine", Eye, new Vector3(-2, MoonAz + 180f, 0), false, 60f),
            ("TreeLineLow", new Vector3(0f, 0.9f, 0f), new Vector3(3, MoonAz + 140f, 0), false, 60f),
            // cellar: the two corners furthest from any candle (SW and the stair
            // alcove behind the W doorway)
            ("DarkCornerSW", Eye, new Vector3(6, 232, 0), false, 60f),
            ("DarkCornerNW", Eye, new Vector3(4, 300, 0), false, 60f),
        };

        [MenuItem("GloomhavenVR/Render Environment Previews")]
        public static void RenderFromMenu() => Render();

        /// <summary>Batch entry — exits the editor 0/1.</summary>
        public static void RenderAll()
        {
            try
            {
                Render();
                Debug.Log("[GloomhavenVR][EnvPreview] RenderAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][EnvPreview] RenderAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Render()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new Exception("No graphics device — run WITHOUT -nographics (use xvfb-run on headless).");

            string outDir = Environment.GetEnvironmentVariable("ENV_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "env-previews";
            Directory.CreateDirectory(outDir);

            foreach (var env in new[] { "Env_Cellar", "Env_Swamp" })
            {
                string prefabPath = $"{Root}/{env}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[GloomhavenVR][EnvPreview] {prefabPath} missing — skipped.");
                    continue;
                }

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                // ENV_PREVIEW_DEBUG=1: log every mesh renderer — separates
                // "geometry missing/culled" from "material/lighting wrong".
                // (The old bright-material override died with the EnvLit shader.)
                if (Environment.GetEnvironmentVariable("ENV_PREVIEW_DEBUG") == "1")
                {
                    foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        Debug.Log($"[EnvPreview][DBG] {mr.transform.root.name}/{mr.name}: active={mr.gameObject.activeInHierarchy} enabled={mr.enabled} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "NULL")} bounds={mr.bounds.center:F1}/{mr.bounds.size:F1} lossyScale={mr.transform.lossyScale:F2} mat={(mr.sharedMaterial ? mr.sharedMaterial.name : "NULL")}");
                    }
                }

                // Fast-forward the particle systems so the still frame shows them alive.
                foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                        ps.Simulate(6f, true, true);

                var camGo = new GameObject("PreviewCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                var roomGeo = inst.transform.Find("RoomGeo");

                // Project is LINEAR color space: take the readback as raw linear and
                // gamma-encode manually, otherwise the PNG comes out ~2.2x too dark
                // (iteration-2 lesson — mid-tones crushed to black).
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);
                foreach (var (name, pos, euler, skyOnly, fov) in Views)
                {
                    if (roomGeo != null) roomGeo.gameObject.SetActive(!skyOnly);
                    cam.fieldOfView = fov;
                    cam.transform.position = pos;
                    cam.transform.rotation = Quaternion.Euler(euler);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    var px = tex.GetPixels();
                    for (int i = 0; i < px.Length; i++) { var c = px[i].gamma; c.a = 1f; px[i] = c; }
                    tex.SetPixels(px);
                    tex.Apply();
                    string png = Path.Combine(outDir, $"{env.ToLowerInvariant()}_{name}.png");
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    Debug.Log($"[GloomhavenVR][EnvPreview] wrote {Path.GetFullPath(png)}");
                }
                RenderTexture.active = null;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }
    }
}
