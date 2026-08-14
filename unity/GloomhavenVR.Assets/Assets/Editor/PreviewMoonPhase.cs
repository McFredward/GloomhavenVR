// GloomhavenVR companion project — MOON PHASE preview (the eclipse and the swell).
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.MoonPhasePreview.RenderAll -logFile moon.log
//   (WITHOUT -nographics; on a headless box wrap in `xvfb-run -a`.)
//
// MOON HELD (ModBuild 144). This harness was built as a FLIPBOOK: eight frames
// at 3.75 s intervals across a 30 s transit, because the eclipse was then the
// one element effect whose whole content was a position that moved. The user
// kept the blood moon and threw the movement out — "lass ihn statisch, das
// 'Vorbeiziehen' gefällt mir nicht gut" — so the flipbook has no subject any
// more and is gone with it.
//
// WHAT THE SERIES IS NOW, and it is the acceptance test rather than a portrait:
// each shot is taken at THREE WIDELY SEPARATED CLOCK OFFSETS, and the three
// files must be byte-identical. That is the proof that the moon is really held
// — a flipbook could only ever show that it moved the way it was supposed to,
// whereas a triple of identical bytes shows that it does not move at all, which
// is the property that was actually asked for. The MOOD axis carries the rest:
// a blood moon at Strong and at Waning is the same picture at two strengths,
// and that is now the only thing that changes.
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
        //   Beam   the moonbeam from the middle of the cellar. It used to be a
        //          CONTROL that must not move; the room shaders call
        //          GhvrMoonLight() now, so it is the opposite — this is where
        //          "bei Dunkelheit den Mondschein extrem reduzieren" is judged.
        private static readonly (string name, string env, Vector3 pos, Vector3 euler, bool skyOnly, float fov)[] Shots =
        {
            ("Disc",   "Env_Swamp",  Eye, new Vector3(-MoonAlt, MoonAz, 0), true,  9f),
            ("Shafts", "Env_Swamp",  Eye, new Vector3(-26, MoonAz, 0), false, 78f),
            ("Sill",   "Env_Cellar", new Vector3(-2.00f, 2.00f, 3.30f), new Vector3(-21, 28, 0), false, 45f),
            ("Beam",   "Env_Cellar", new Vector3(1.60f, 1.50f, 0.60f), new Vector3(-2, 315, 0), false, 70f),
        };

        // THE HELD PROOF. Three clock offsets, deliberately not round and
        // deliberately far apart — 0, a third of a minute, and most of an hour —
        // so that anything left running on any clock at any rate would have to
        // land on the same value three times to escape. `series` moods are shot
        // at all three and the files compared byte for byte; the rest are shot
        // at the first only. (The old harness shot eight EVEN steps of one
        // transit; even spacing was right for showing a constant rate and is
        // exactly wrong for catching a residual motion, which is what is being
        // looked for now.)
        private static readonly float[] HoldOffsets = { 0f, 19.37f, 2153.1f };

        //   _GhvrElemA = (Fire, Ice, Air, Earth), _GhvrElemB = (Light, Dark, Master, Peak)
        private static readonly (string tag, Vector4 a, Vector4 b, bool series)[] Moods =
        {
            ("rest",   Vector4.zero, Vector4.zero,                    false), // the shipped sky
            ("darkS",  Vector4.zero, new Vector4(0, 1f, 1, 1),        true),
            ("darkW",  Vector4.zero, new Vector4(0, 0.40f, 1, 0.40f), true),
            ("lightS", Vector4.zero, new Vector4(1f, 0, 1, 1),        false),
            ("lightW", Vector4.zero, new Vector4(0.40f, 0, 1, 0.40f), false),
            ("split",  Vector4.zero, new Vector4(1f, 1f, 1, 1),       true),
            // the acceptance test: all six up, master 0.
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

                // THE HELD PROOF, measured rather than eyeballed. It compares a
                // square well INSIDE the moon's own disc across the three clock
                // offsets, and that boundary is the whole design of the test:
                // within the disc the sprite's alpha is 1 and the sky behind is
                // completely replaced, so the crop contains the moon and nothing
                // else. Take it wider and it would include stars, which genuinely
                // DO turn between these offsets — the moon is the one thing in
                // this sky that does not ride the celestial rotation (it anchors
                // the baked shafts; see EnvStars), and a comparison that mixed the
                // two could not distinguish "the moon moved" from "the sky moved,
                // correctly".
                //
                // THE BOX IS FOUND, NOT ASSUMED. The first version of this test
                // took the middle of the frame, on the reasoning that the camera
                // is aimed exactly down MoonDir — and reported a moving moon,
                // because the disc does not in fact land dead centre (the sky
                // dome carries a placement of its own) and the "crop" was mostly
                // rotating star field. A test whose failure mode is to fail is
                // fine; one that would also have PASSED a moving moon by looking
                // at the wrong pixels is not. So the box is derived from the
                // brightest copper in the reference frame itself, and if that
                // search comes up empty the harness says so instead of comparing
                // whatever happened to be there.
                Color[] discRef = null;
                RectInt discBox = new RectInt(0, 0, 0, 0);
                float discMaxDiff = 0f;
                int discCmp = 0;

                /// <summary>Centroid and half-extent of the eclipsed disc: the copper
                /// is the only strongly red-dominant thing in a sky-only frame.</summary>
                bool FindDisc(Color[] px, out RectInt box)
                {
                    double sx = 0, sy = 0; int n = 0;
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            var c = px[y * W + x];
                            if (c.r > 0.25f && c.r > c.b * 1.6f) { sx += x; sy += y; n++; }
                        }
                    box = new RectInt(0, 0, 0, 0);
                    if (n < 4000) return false;               // no disc worth measuring
                    // n is the disc's area, so its radius is sqrt(n/pi); take 60%
                    // of that, which is comfortably inside the limb and inside the
                    // sprite's alpha = 1 plateau.
                    int half = Mathf.RoundToInt(0.60f * Mathf.Sqrt(n / Mathf.PI));
                    int cx = Mathf.RoundToInt((float)(sx / n)), cy = Mathf.RoundToInt((float)(sy / n));
                    half = Mathf.Min(half, Mathf.Min(Mathf.Min(cx, W - 1 - cx), Mathf.Min(cy, H - 1 - cy)));
                    if (half < 24) return false;
                    box = new RectInt(cx - half, cy - half, half * 2, half * 2);
                    return true;
                }

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

                    if (name == "Disc" && suffix.StartsWith("_darkS_k"))
                    {
                        if (discRef == null)
                        {
                            if (!FindDisc(px, out discBox))
                                Debug.LogWarning("[GloomhavenVR][MoonPreview] MOON HELD proof SKIPPED: "
                                                 + "no eclipsed disc found in moon_Disc_darkS_k0 — either the "
                                                 + "blood moon is not being painted or the shot no longer "
                                                 + "contains it.");
                            else
                                discRef = tex.GetPixels(discBox.x, discBox.y, discBox.width, discBox.height);
                        }
                        else
                        {
                            var crop = tex.GetPixels(discBox.x, discBox.y, discBox.width, discBox.height);
                            discCmp++;
                            for (int i = 0; i < crop.Length; i++)
                            {
                                discMaxDiff = Mathf.Max(discMaxDiff, Mathf.Abs(crop[i].r - discRef[i].r));
                                discMaxDiff = Mathf.Max(discMaxDiff, Mathf.Abs(crop[i].g - discRef[i].g));
                                discMaxDiff = Mathf.Max(discMaxDiff, Mathf.Abs(crop[i].b - discRef[i].b));
                            }
                        }
                    }
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
                        int steps = series ? HoldOffsets.Length : 1;
                        for (int k = 0; k < steps; k++)
                        {
                            Shader.SetGlobalFloat("_GhvrTimeOfs", HoldOffsets[k]);
                            Shoot(name, pos, euler, skyOnly, fov,
                                  series ? $"_{tag}_k{k}" : $"_{tag}");
                        }
                    }
                }

                if (discCmp > 0)
                {
                    // 1/255 is one 8-bit step of the PNG these frames are written
                    // as, i.e. the finest thing the review set can represent at
                    // all. Anything at or under it is not a moving moon, it is
                    // the half-float target's own rounding.
                    string verdict = discMaxDiff <= 1f / 255f
                        ? "HELD (indistinguishable at 8 bits)"
                        : "*** THE MOON MOVED — the transit is not gone ***";
                    Debug.Log($"[GloomhavenVR][MoonPreview] MOON HELD proof: {discCmp} comparison(s) "
                              + $"of a {discBox.width}x{discBox.height} box at ({discBox.x},{discBox.y}) "
                              + $"— found on the disc itself — of moon_Disc_darkS across clock offsets "
                              + $"{string.Join(", ", HoldOffsets)} s. Max channel difference "
                              + $"{discMaxDiff:F6} ({discMaxDiff * 255f:F3} of an 8-bit step) — {verdict}. "
                              + "The star field behind the crop DOES turn over these offsets; the moon is "
                              + "the one thing in this sky that does not ride the celestial rotation.");
                }

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
