// GloomhavenVR companion project — MOON PHASE preview (the eclipse and the swell).
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.MoonPhasePreview.RenderAll -logFile moon.log
//   (WITHOUT -nographics; on a headless box wrap in `xvfb-run -a`.)
//
// WHY THIS IS NOT A ROW IN EnvironmentsPreview.ElementMoods. That series holds
// the shader clock at ONE instant (3.7 s) and varies the element channel, which
// is the right shape for everything the elements did until now: a frost, an
// ember glow and a fog bank look the same at any second. The eclipse is the
// first element effect whose whole content is a POSITION THAT MOVES — a shadow
// crossing a disc over 30 s — so it has to be photographed the other way round:
// one mood, many clocks. A flipbook of the transit is the only frame set in
// which "this reads as an occlusion and not as a dimmer" can be judged at all.
//
// The camera is aimed off EnvironmentsBuilder.MoonDir, never off a typed
// bearing, exactly as EnvironmentsPreview's Moon views are: move the moon and
// these frames follow it.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class MoonPhasePreview
    {
        private const string Root = "Assets/Bundle/Environments";
        private const int W = 1280, H = 720;

        private static readonly Vector3 Eye = new Vector3(0f, 1.4f, 0f);
        private static readonly float MoonAz =
            Mathf.Atan2(EnvironmentsBuilder.MoonDir.x, EnvironmentsBuilder.MoonDir.z) * Mathf.Rad2Deg;
        private static readonly float MoonAlt =
            Mathf.Asin(EnvironmentsBuilder.MoonDir.y) * Mathf.Rad2Deg;

        // Four framings, and each of them answers a different question.
        //   Disc   the moon alone, room hidden, 9 deg: the terminator's SHAPE.
        //   Shafts the moon through the canopy tear WITH its own shafts under it
        //          (the same aim as EnvironmentsPreview's ShaftMoon). This is the
        //          cause-and-effect frame: the thing being covered and the light
        //          it casts, in one picture.
        //   Sill   up into the window aperture from under it — the ONE frame in
        //          which a cellar player sees any sky at all. It is here to
        //          record a NEGATIVE result and keep it recorded: the moon is
        //          not in that cone (nor in env_cellar_MoonWide, where the
        //          ceiling takes it), so the cellar's entire share of this
        //          feature has to arrive through GhvrMoonLight() and nothing
        //          that happens to the sprite can substitute for it.
        //   Beam   the moonbeam from the middle of the cellar. A CONTROL: until
        //          the room shaders call GhvrMoonLight() it must not move, and
        //          once they do it must fall with the disc.
        private static readonly (string name, string env, Vector3 pos, Vector3 euler, bool skyOnly, float fov)[] Shots =
        {
            ("Disc",   "Env_Swamp",  Eye, new Vector3(-MoonAlt, MoonAz, 0), true,  9f),
            ("Shafts", "Env_Swamp",  Eye, new Vector3(-26, MoonAz, 0), false, 78f),
            ("Sill",   "Env_Cellar", new Vector3(-2.00f, 2.00f, 3.30f), new Vector3(-21, 28, 0), false, 45f),
            ("Beam",   "Env_Cellar", new Vector3(1.60f, 1.50f, 0.60f), new Vector3(-2, 315, 0), false, 70f),
        };

        // Eight steps of 3.75 s over the 30 s transit (GHVR_ECL_PERIOD). Even
        // spacing on purpose: the frames are read as a flipbook, and an uneven
        // series cannot show that the shadow moves at a CONSTANT rate, which is
        // half of what makes it read as a body passing rather than a fade.
        private const int PhaseSteps = 8;

        //   _GhvrElemA = (Fire, Ice, Air, Earth), _GhvrElemB = (Light, Dark, Master, Peak)
        private static readonly (string tag, Vector4 a, Vector4 b, bool series)[] Moods =
        {
            ("rest",   Vector4.zero, Vector4.zero,                    false), // the shipped sky
            ("darkS",  Vector4.zero, new Vector4(0, 1f, 1, 1),        true),
            ("darkW",  Vector4.zero, new Vector4(0, 0.40f, 1, 0.40f), true),
            ("lightS", Vector4.zero, new Vector4(1f, 0, 1, 1),        false),
            ("lightW", Vector4.zero, new Vector4(0.40f, 0, 1, 0.40f), false),
            ("split",  Vector4.zero, new Vector4(1f, 1f, 1, 1),       true),
            // the acceptance test, at the same instants: all six up, master 0.
            ("moff",   new Vector4(1, 1, 1, 1), new Vector4(1, 1, 0, 1), false),
        };

        [MenuItem("GloomhavenVR/Render Moon Phase Previews")]
        public static void RenderFromMenu() => Render();

        /// <summary>Batch entry — exits the editor 0/1.</summary>
        public static void RenderAll()
        {
            try
            {
                Render();
                Debug.Log("[GloomhavenVR][MoonPreview] RenderAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][MoonPreview] RenderAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Render()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new Exception("No graphics device — run WITHOUT -nographics (use xvfb-run on headless).");

            string outDir = Environment.GetEnvironmentVariable("ENV_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "moon-previews";
            Directory.CreateDirectory(outDir);

            foreach (var env in new[] { "Env_Swamp", "Env_Cellar" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{env}.prefab");
                if (prefab == null) { Debug.LogWarning($"[MoonPreview] {env} missing — skipped."); continue; }

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var roomGeo = inst.transform.Find("RoomGeo");
                void FastForward(Transform under)
                {
                    foreach (var ps in under.GetComponentsInChildren<ParticleSystem>(true))
                        if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                            ps.Simulate(6f, true, true);
                }
                FastForward(inst.transform);

                var camGo = new GameObject("MoonCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                // ARGBHalf + manual gamma, for the reason EnvironmentsPreview
                // states at length: an 8-bit LINEAR target contours a night sky.
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);
                bool roomHidden = false;

                void Shoot(string name, Vector3 pos, Vector3 euler, bool skyOnly, float fov, string suffix)
                {
                    // hiding the room restarts its emitters empty — see the same
                    // note in EnvironmentsPreview; refill whenever it comes back
                    if (roomGeo != null)
                    {
                        roomGeo.gameObject.SetActive(!skyOnly);
                        if (roomHidden && !skyOnly) FastForward(roomGeo);
                        roomHidden = skyOnly;
                    }
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
                    string png = Path.Combine(outDir, $"moon_{name}{suffix}.png");
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    Debug.Log($"[GloomhavenVR][MoonPreview] wrote {Path.GetFullPath(png)}");
                }

                Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
                Shader.SetGlobalVector("_GhvrHauntForce", Vector4.zero);
                foreach (var (name, senv, pos, euler, skyOnly, fov) in Shots)
                {
                    if (senv != env) continue;
                    foreach (var (tag, ea, eb, series) in Moods)
                    {
                        Shader.SetGlobalVector("_GhvrElemA", ea);
                        Shader.SetGlobalVector("_GhvrElemB", eb);
                        int steps = series ? PhaseSteps : 1;
                        for (int k = 0; k < steps; k++)
                        {
                            float ofs = k * (EnvironmentsBuilder.EclipsePeriod / PhaseSteps);
                            Shader.SetGlobalFloat("_GhvrTimeOfs", ofs);
                            Shoot(name, pos, euler, skyOnly, fov,
                                  series ? $"_{tag}_k{k}" : $"_{tag}");
                        }
                    }
                }
                // ...and the SAME frame twice at the same offset, first and last,
                // which is this harness's own control: the editor's _Time.y is
                // whatever the editor says it is, so a phase series only means
                // anything if two shots at one offset come out byte-identical.
                Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                Shader.SetGlobalVector("_GhvrElemB", new Vector4(0, 1f, 1, 1));
                Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                foreach (var (name, senv, pos, euler, skyOnly, fov) in Shots)
                    if (senv == env) Shoot(name, pos, euler, skyOnly, fov, "_darkS_k0_again");

                Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
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
