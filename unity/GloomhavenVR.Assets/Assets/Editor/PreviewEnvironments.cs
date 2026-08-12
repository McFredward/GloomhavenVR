// GloomhavenVR companion project — ambient-environment preview renderer.
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device); on a
//   headless box wrap in `xvfb-run -a`.
//
// For each Env_*.prefab: loads it into an empty temp scene, fast-forwards every
// particle system 6 s (so fog banks, flames, fireflies and — with luck — a shooting
// star are populated), then renders 5 views from the seated player position
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

        private static readonly (string name, Vector3 euler)[] Views =
        {
            ("N", new Vector3(0, 0, 0)),
            ("E", new Vector3(0, 90, 0)),
            ("S", new Vector3(0, 180, 0)),
            ("W", new Vector3(0, 270, 0)),
            ("Up", new Vector3(-30, 45, 0)),
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
                cam.transform.position = new Vector3(0f, 1.4f, 0f); // seated player head

                var rt = new RenderTexture(W, H, 24);
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                foreach (var (name, euler) in Views)
                {
                    cam.transform.rotation = Quaternion.Euler(euler);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
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
