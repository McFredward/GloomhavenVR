// GloomhavenVR companion project — THIN NIGHT CLOUD preview + measurement station.
//
// Batch: CLOUDS_PREVIEW_OUT=<dir> xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity \
//          -batchmode -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
//          -executeMethod GloomhavenVR.CloudsPreview.RenderAll -logFile cloud-preview.log
//   IMPORTANT: WITHOUT -nographics (rendering needs a graphics device); no -quit
//   (RenderAll exits itself). Run EnvironmentsBuilder.BuildAll FIRST — this
//   station renders the COMMITTED prefab and bakes nothing.
//
// A NEW FILE ON PURPOSE. PreviewEnvironments.cs is the shared station and another
// lane is working in it this round; this one is separate so the two cannot
// collide. It deliberately copies that station's hard-won conventions rather than
// inventing its own — the ARGBHalf linear target, the manual gamma encode, the
// _GhvrTimeOfs clock, the control frame — and the reasons are quoted where they
// are not obvious.
//
// WHAT THIS PRODUCES, and why each piece exists (the user asked for renders:
// "Mach dir renders davon, um dich selber davon zu überzeugen"):
//
//   1. cloud_seat_*.png / cloud_noclouds_*.png — the BEFORE/AFTER pair, from the
//      player's own seat at the rig's real scale. Identical camera, identical
//      clock, one node toggled. Anything that differs between them is the clouds.
//
//   2. cloud_cycle_*.png — a time series across a FULL DRIFT CYCLE. The field is
//      exactly periodic with period EnvironmentsBuilder.CloudPeriod (both winds
//      are whole texture turns per period), so [0, period) is not a sample of the
//      behaviour, it is all of it — which is the only way a finite set of frames
//      can settle a claim about all time. A wrap control frame at t = period is
//      rendered last and diffed against t = 0; if the loop does not close, that
//      diff is where it shows.
//
//   3. THE OCCLUSION SERIES — the number, not a picture. See MeasureMoon below.
//      This is the deliverable for "den Mond nie voll verdecken".
//
//   4. cloud_stereo_[LR].png — a parallel 63 mm pair, no toe-in, WITH a control
//      pair of the same view with the clouds off. The absolute L-R difference of
//      a sky at 45 m is not zero (real parallax), so the meaningful quantity is
//      whether the clouds RAISE it above the shipped sky's own.
//
//   5. THE FILL FRACTION — what share of the seated view the layer actually
//      rasterises and how much of that survives the clip(). That is the input to
//      the cost estimate, measured rather than guessed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR
{
    public static class CloudsPreview
    {
        private const string Root = "Assets/Bundle/Environments";
        private const int W = 1280, H = 720;
        // The occlusion measurement gets its own small square target: the moon is
        // 2.8 deg across and the frame is 11 deg, so 512 px puts ~130 px across the
        // disc — far more than the claim needs, and small enough that 240 pairs of
        // readbacks finish in minutes.
        private const int MW = 512, MH = 512;

        // ---- the seat, derived exactly as PreviewEnvironments derives it, never
        // typed. These mirror SkyAlternative's normalisation: the prefab is scaled
        // so PlaySpace's authored diameter becomes 4.5x the board's world extent,
        // and the head sits (0.75 + overBoard) board-widths above the floor.
        private const float PlaySpaceToBoardRatio = 4.5f;
        private const float FloatGapToBoardRatio = 0.75f;
        private const float StandOverBoard = 0.65f;
        private const float SeatOverBoard = 0.40f;
        private static float HeadY(float playDia, float overBoard) =>
            (FloatGapToBoardRatio + overBoard) * (playDia / PlaySpaceToBoardRatio);
        private static float SeatForest => HeadY(EnvRoomBuilder.ForestPlaySpaceDia, SeatOverBoard);   // 2.30 m
        private static float HeadForest => HeadY(EnvRoomBuilder.ForestPlaySpaceDia, StandOverBoard);  // 2.80 m

        // The moon's bearing is READ from the builder, never typed — the whole
        // point of the clear-patch construction is that one constant aims the
        // sprite, the shafts and the patch, so a preview that typed its own copy
        // could agree with a broken build.
        private static readonly float MoonAz =
            Mathf.Atan2(EnvironmentsBuilder.MoonDir.x, EnvironmentsBuilder.MoonDir.z) * Mathf.Rad2Deg;
        private static readonly float MoonAlt =
            Mathf.Asin(EnvironmentsBuilder.MoonDir.y) * Mathf.Rad2Deg;

        private const float DomeRadius = 45f;      // StarDome's authored scale
        private const float HalfIpd = 0.0315f;     // 63 mm, the usual adult IPD

        [MenuItem("GloomhavenVR/Render Cloud Previews")]
        public static void RenderFromMenu() => Render();

        public static void RenderAll()
        {
            try
            {
                Render();
                Debug.Log("[GloomhavenVR][CloudPreview] RenderAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][CloudPreview] RenderAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        // ------------------------------------------------------------- state
        private static GameObject _inst;
        private static GameObject _cloudBand;
        private static Renderer _domeRen, _starsRen;
        private static GameObject _roomGeo;
        private static Camera _cam;
        private static string _out;

        private static void Render()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new Exception("No graphics device — run WITHOUT -nographics (use xvfb-run on headless).");

            _out = Environment.GetEnvironmentVariable("CLOUDS_PREVIEW_OUT");
            if (string.IsNullOrEmpty(_out)) _out = "cloud-previews";
            Directory.CreateDirectory(_out);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Env_Swamp.prefab")
                         ?? throw new Exception("Env_Swamp.prefab missing — run EnvironmentsBuilder.BuildAll first.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            var dome = _inst.transform.Find("StarDome") ?? throw new Exception("StarDome missing.");
            _domeRen = dome.GetComponent<Renderer>();
            _starsRen = dome.Find("StarField")?.GetComponent<Renderer>();
            _cloudBand = dome.Find("CloudBand")?.gameObject
                         ?? throw new Exception("CloudBand missing — the bake did not run, or the node was renamed.");
            _roomGeo = _inst.transform.Find("RoomGeo")?.gameObject;

            // The shipped zero state, explicitly, so these frames are comparable
            // with every previous round's: no haunt, no element, and the clock
            // owned by us alone.
            Shader.SetGlobalFloat("_GhvrIndoor", 0f);                 // the forest, not the cellar
            Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
            Shader.SetGlobalVector("_GhvrHauntForce", Vector4.zero);
            Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
            Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
            SetClock(0f);
            FastForward(_inst.transform);

            var camGo = new GameObject("CloudPreviewCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 300f;

            try
            {
                BeforeAfter();
                DriftSeries();
                Stereo();
                MeasureMoon();
                MeasureSkyMax();
                MeasureShellCoverage();
                MeasureFill();
                Cellar();
            }
            finally
            {
                RenderTexture.active = null;
                _cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(_inst);
                Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                Shader.SetGlobalFloat("_GhvrIndoor", 0f);
            }
        }

        // ============================================================== clocks
        // The shader reads `_Time.y + _GhvrTimeOfs`, and PreviewWaterVR MEASURED
        // (rather than assumed) that _Time.y does not advance across Camera.Render
        // calls in batch mode — so the offset alone IS the instant. Every series
        // here re-renders its first frame as its LAST frame under a "_ctrl" name
        // for exactly that reason: if a future Unity runs the clock, the diff says
        // so instead of the conclusions quietly rotting.
        private static void SetClock(float t) => Shader.SetGlobalFloat("_GhvrTimeOfs", t);

        private static void FastForward(Transform under)
        {
            foreach (var ps in under.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                    ps.Simulate(6f, true, true);
        }

        // ============================================================ plumbing
        private static void Aim(Vector3 pos, Vector3 euler, float fov)
        {
            _cam.transform.position = pos;
            _cam.transform.rotation = Quaternion.Euler(euler);
            _cam.fieldOfView = fov;
        }

        /// <summary>Look straight at the moon FROM WHERE THE CAMERA IS. Not at the
        /// moon's bearing — at the point on the dome where the sprite is painted,
        /// which from an off-centre eye is a slightly different direction. Getting
        /// this wrong would put the disc off centre and quietly bias the occlusion
        /// measurement toward whichever pixels happened to land in the crop.</summary>
        private static void AimAtMoon(Vector3 pos, float fov)
        {
            Vector3 moonPoint = EnvironmentsBuilder.MoonDir * DomeRadius;
            _cam.transform.position = pos;
            _cam.transform.rotation = Quaternion.LookRotation(moonPoint - pos, Vector3.up);
            _cam.fieldOfView = fov;
        }

        /// <summary>Render and read back RAW LINEAR floats. The PNG path applies
        /// `.gamma`; this one must not, because what is being measured is drawn
        /// energy and not what a picture looks like (PreviewEnvironments' own
        /// ReadWindow makes the same split for the same reason).</summary>
        private static Color[] Grab(int w, int h, Color clear)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false);
            var prevClear = _cam.backgroundColor;
            _cam.backgroundColor = clear;
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            var px = tex.GetPixels();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            _cam.backgroundColor = prevClear;
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            return px;
        }

        private static void Shoot(string name)
        {
            // ARGBHalf, not ARGB32: an 8-bit LINEAR target quantises at 1/255
            // linear, which after the gamma encode is a first step of 18/255 — so
            // every soft gradient in a night sky arrives as hard contour bands that
            // do not exist on the headset. A thin cloud over a near-black sky is
            // precisely that case (ModBuild 136's lesson, inherited).
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            var px = tex.GetPixels();
            // Project is LINEAR colour space: gamma-encode by hand or the PNG comes
            // out ~2.2x too dark. Nothing else is applied — no exposure, no tonemap.
            for (int i = 0; i < px.Length; i++) { var c = px[i].gamma; c.a = 1f; px[i] = c; }
            tex.SetPixels(px); tex.Apply();
            string path = Path.Combine(_out, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            _cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"[GloomhavenVR][CloudPreview] wrote {Path.GetFullPath(path)}");
        }

        private static void Clouds(bool on) => _cloudBand.SetActive(on);

        /// <summary>Leave ONLY the cloud shell drawing. Renderers are toggled, not
        /// GameObjects, so nothing under them is deactivated and no ParticleSystem
        /// restarts empty — the trap that cost PreviewEnvironments two rounds of
        /// "the sparks are frozen".</summary>
        private static void IsolateClouds(bool on)
        {
            if (_domeRen != null) _domeRen.enabled = !on;
            if (_starsRen != null) _starsRen.enabled = !on;
            if (_roomGeo != null) _roomGeo.SetActive(!on);
        }

        // ====================================================== 1. before/after
        private static readonly (string name, Vector3 pos, Vector3 euler, float fov, bool skyOnly)[] Views =
        {
            // The seat, looking at the moon's bearing a little above the horizon —
            // the shipped README framing, so this is directly comparable with
            // docs/img/env-forest.jpg.
            ("seat",   new Vector3(0f, 0f, 0f), new Vector3(-14f, 0f, 0f), 74f, false),
            // Standing, looking up through the canopy tear at the moon.
            ("canopy", new Vector3(0f, 0f, 0f), new Vector3(-MoonAlt + 4f, 0f, 0f), 26f, false),
            // The dome alone, wide: this is where a cloud LAYER has to read as a
            // layer rather than as a texture, and where "niemals dicht" is judged.
            ("sky",    new Vector3(0f, 0f, 0f), new Vector3(-34f, 0f, 0f), 75f, true),
        };

        private static void Place(int i, float height)
        {
            var v = Views[i];
            Aim(v.pos + new Vector3(0f, height, 0f), v.euler + new Vector3(0f, MoonAz, 0f), v.fov);
            IsolateClouds(false);
            if (_roomGeo != null) _roomGeo.SetActive(!v.skyOnly);
        }

        private static void BeforeAfter()
        {
            for (int i = 0; i < Views.Length; i++)
            {
                float hgt = i == 1 ? HeadForest : SeatForest;
                // A non-round clock on purpose: at t = 0 the drift offsets are all
                // exactly zero and the two layers sit in phase, which is the one
                // instant of the cycle that is not representative of it.
                SetClock(413.7f);
                Place(i, hgt);
                Clouds(true); Shoot($"cloud_after_{Views[i].name}");
                Clouds(false); Shoot($"cloud_before_{Views[i].name}");
                Clouds(true);
            }
            if (_roomGeo != null) _roomGeo.SetActive(true);
        }

        // ========================================================= 2. the cycle
        private static void DriftSeries()
        {
            float P = EnvironmentsBuilder.CloudPeriod;
            const int N = 16;
            Place(2, SeatForest);      // the wide sky-only view
            Clouds(true);
            for (int i = 0; i < N; i++)
            {
                float t = P * i / N;
                SetClock(t);
                Shoot($"cloud_cycle_t{Mathf.RoundToInt(t):D5}");
            }
            LoopCheck();
            if (_roomGeo != null) _roomGeo.SetActive(true);
        }

        /// <summary>THE LOOP CLOSES — checked, not asserted, and the first version
        /// of this check was WRONG in a way worth recording. It rendered the whole
        /// sky at t=0 and t=CloudPeriod and diffed the two pictures; 93 % of the
        /// pixels differed and it looked like the loop was broken. It was not. The
        /// STAR DOME turns at 2*pi/2880 rad/s, so at t = 1440 s the entire
        /// celestial sphere has rotated 180 degrees — the diff was measuring the
        /// stars, and the cloud layer was invisible inside it. That is the "one
        /// step too early / measure the right layer" trap exactly, so the check now
        /// ISOLATES the cloud shell (dome, stars and room renderers off) and
        /// compares RAW LINEAR floats, where the only thing that can differ is the
        /// clouds.
        ///
        /// It also renders a HALF-PERIOD frame and diffs that too. A comparison
        /// that can only come out equal proves nothing: the half-period diff is the
        /// positive control that says this instrument can see a difference at
        /// all.</summary>
        private static void LoopCheck()
        {
            float P = EnvironmentsBuilder.CloudPeriod;
            Aim(new Vector3(0f, SeatForest, 0f), new Vector3(-34f, MoonAz, 0f), 75f);
            IsolateClouds(true);
            Clouds(true);
            SetClock(0f); var a0 = Grab(W, H, Color.black);
            SetClock(P); var aP = Grab(W, H, Color.black);
            SetClock(P * 0.5f); var aH = Grab(W, H, Color.black);
            float Max(Color[] x, Color[] y)
            {
                float m = 0f;
                for (int i = 0; i < x.Length; i++)
                {
                    m = Mathf.Max(m, Mathf.Abs(x[i].r - y[i].r));
                    m = Mathf.Max(m, Mathf.Abs(x[i].g - y[i].g));
                    m = Mathf.Max(m, Mathf.Abs(x[i].b - y[i].b));
                }
                return m;
            }
            float wrap = Max(a0, aP), half = Max(a0, aH);
            // The cloud shell alone, at t = 0 and at t = P, as pictures too — so the
            // claim is legible without reading a log.
            SetClock(0f); Shoot("cloud_loop_t0_isolated");
            SetClock(P); Shoot("cloud_loop_tP_isolated");
            IsolateClouds(false);
            Debug.Log($"[GloomhavenVR][CloudPreview] LOOP: isolated cloud shell, max |t=0 minus t={P:F0}s| over the "
                      + $"whole frame in LINEAR radiance = {wrap:E3}. Positive control, same instrument, same "
                      + $"frame, half a period apart: {half:E3} ({half / Mathf.Max(wrap, 1e-12f):E1}x larger). "
                      + "The field is exactly periodic, so the drift series above is not a sample of the "
                      + "behaviour — it is all of it.");
        }

        // ========================================================= 4. the stereo
        private static void Stereo()
        {
            SetClock(413.7f);
            var v = Views[1];
            Vector3 basePos = new Vector3(0f, HeadForest, 0f);
            Quaternion rot = Quaternion.Euler(v.euler + new Vector3(0f, MoonAz, 0f));
            // PARALLEL, no toe-in: two eyes at +-31.5 mm along the head's own right
            // axis, identical orientation. Toe-in would introduce a keystone
            // difference of its own and confound the thing being looked for.
            Vector3 right = rot * Vector3.right;
            IsolateClouds(false);
            if (_roomGeo != null) _roomGeo.SetActive(true);

            Color[] Eye(float s, bool clouds)
            {
                Clouds(clouds);
                Aim(basePos + right * s, v.euler + new Vector3(0f, MoonAz, 0f), v.fov);
                return Grab(W, H, new Color(0.01f, 0.01f, 0.015f));
            }

            var l = Eye(-HalfIpd, true); var r = Eye(+HalfIpd, true);
            var l0 = Eye(-HalfIpd, false); var r0 = Eye(+HalfIpd, false);
            Clouds(true);
            Aim(basePos - right * HalfIpd, v.euler + new Vector3(0f, MoonAz, 0f), v.fov);
            Shoot("cloud_stereo_L");
            Aim(basePos + right * HalfIpd, v.euler + new Vector3(0f, MoonAz, 0f), v.fov);
            Shoot("cloud_stereo_R");

            double Mean(Color[] a, Color[] b)
            {
                double s = 0;
                for (int i = 0; i < a.Length; i++)
                    s += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                return s / (a.Length * 3);
            }
            double withC = Mean(l, r), without = Mean(l0, r0);
            Debug.Log($"[GloomhavenVR][CloudPreview] STEREO (parallel, IPD {HalfIpd * 2f * 1000f:F0} mm, "
                      + $"{v.fov:F0} deg fov): mean |L-R| in LINEAR radiance = {withC:E3} with clouds vs "
                      + $"{without:E3} on the SHIPPED sky alone, ratio {withC / Mathf.Max((float)without, 1e-12f):F2}x. "
                      + "Neither is zero and neither should be: a dome at 45 m has real parallax at 63 mm, and the "
                      + "shipped sky's own figure is the baseline this project has already accepted. What would "
                      + "indicate rivalry is the clouds RAISING it, which is what this ratio measures.");
        }

        // ================================================ 3. THE MOON, MEASURED
        // The claim is "der Mond wird nie voll verdeckt", and it is a claim about
        // every instant. Two independent things settle it here:
        //
        //   * the ARGUMENT (BuildEnvironments.AssertCloudsClearTheMoon): the
        //     shader's opacity is bounded above by a time-independent function of
        //     direction that is flat at CloudAlpha*CloudMoonMin over the whole moon
        //     sprite. That is a supremum over ALL time and needs no frames.
        //   * this MEASUREMENT, which is here because an argument about a shader is
        //     an argument about what I think the shader says. It reads the shipped
        //     program's actual output through the actual rasteriser.
        //
        // HOW THE ALPHA IS RECOVERED, exactly. EnvCloud composites premultiplied:
        //     out = L + (1 - a) * background.
        // Render the isolated cloud shell twice, over a BLACK clear and a WHITE
        // clear, and read back raw linear:
        //     black -> L,        white -> L + (1 - a),
        //     so  a = 1 - (white - black),  channel by channel, with no reliance on
        // what L is. Clipped fragments leave the background untouched, so they come
        // back as a = 0, which is correct.
        private static void MeasureMoon()
        {
            // Step 1: WHERE the moon is on this frame, found from the picture and
            // not from a formula. Render the sky with the clouds off and take the
            // disc as the pixels far brighter than the halo around them.
            AimAtMoon(new Vector3(0f, SeatForest, 0f), 11f);
            IsolateClouds(false);
            if (_roomGeo != null) _roomGeo.SetActive(false);
            Clouds(false);
            SetClock(0f);
            var clean = Grab(MW, MH, Color.black);
            float peak = clean.Max(c => c.g);
            float thresh = peak * 0.5f;
            var disc = new List<int>();
            for (int i = 0; i < clean.Length; i++) if (clean[i].g >= thresh) disc.Add(i);
            if (disc.Count < 200)
                throw new Exception($"MeasureMoon: only {disc.Count} px above half-peak — the moon is not in frame.");
            // Report the found disc as an ANGLE so the threshold is checkable
            // against the builder's own MoonDiscRad rather than taken on trust.
            float pxRad = Mathf.Sqrt(disc.Count / Mathf.PI);
            float halfFovTan = Mathf.Tan(11f * 0.5f * Mathf.Deg2Rad);
            float foundDeg = Mathf.Atan(pxRad / (MH * 0.5f) * halfFovTan) * Mathf.Rad2Deg;
            Debug.Log($"[GloomhavenVR][CloudPreview] moon disc found: {disc.Count} px of {MW}x{MH} at 11 deg fov "
                      + $"=> equivalent angular radius {foundDeg:F2} deg (builder says the sprite's disc is "
                      + "~1.39 deg; the found figure includes the sprite's bright inner halo, which is the "
                      + "conservative direction for this measurement).");

            // Step 2: sweep the WHOLE cycle. 240 steps of 6 s: the finest noise
            // octave subtends 0.32 deg at this elevation and the field drifts
            // 0.055 deg/s, so a feature crosses a given point in ~6 s. Sampling at
            // the feature's own crossing time is the condition for not stepping
            // over a peak — a coarser series could miss the worst instant, which is
            // the only instant this measurement is about.
            IsolateClouds(true);
            Clouds(true);
            float P = EnvironmentsBuilder.CloudPeriod;
            const int N = 240;
            float worstMax = 0f, worstMean = 0f; float worstT = -1f;
            var rows = new List<string>();
            for (int i = 0; i < N; i++)
            {
                float t = P * i / N;
                SetClock(t);
                var b = Grab(MW, MH, Color.black);
                var w = Grab(MW, MH, Color.white);
                float mx = 0f; double sum = 0;
                foreach (int p in disc)
                {
                    float a = 1f - (w[p].g - b[p].g);
                    a = Mathf.Clamp01(a);
                    if (a > mx) mx = a;
                    sum += a;
                }
                float mean = (float)(sum / disc.Count);
                rows.Add($"{t:F1}\t{mx:F5}\t{mean:F5}\t{1f - mx:F5}\t{1f - mean:F5}");
                if (mx > worstMax) { worstMax = mx; worstT = t; }
                if (mean > worstMean) worstMean = mean;
            }
            string tsv = Path.Combine(_out, "cloud_moon_occlusion.tsv");
            File.WriteAllLines(tsv, new[]
            {
                "# EnvCloud opacity over the moon disc, across one FULL drift cycle.",
                $"# period={P:F0}s, {N} samples ({P / N:F1}s apart), disc={disc.Count}px.",
                "# alpha recovered as 1 - (white_clear - black_clear) on the isolated shell.",
                "t_s\talpha_max\talpha_mean\ttransmit_min\ttransmit_mean",
            }.Concat(rows));

            Debug.Log($"[GloomhavenVR][CloudPreview] MOON OCCLUSION, measured over the whole {P:F0} s cycle "
                      + $"({N} samples): WORST single pixel on the disc reached alpha {worstMax:F4} (at t={worstT:F0} s), "
                      + $"i.e. the moon was NEVER less than {(1f - worstMax) * 100f:F2}% transmitted at any point, at "
                      + $"any instant. Worst DISC-MEAN opacity {worstMean:F4} => the disc as a whole never dropped "
                      + $"below {(1f - worstMean) * 100f:F2}% of its clear brightness. The builder's STRUCTURAL bound "
                      + $"is {EnvironmentsBuilder.CloudMaxOverMoon:F3} (CloudAlpha x CloudMoonMin), read from the "
                      + "builder rather than retyped here: the measurement must sit UNDER it, and if it ever sits "
                      + $"above it then the bound is wrong and not the picture. Table: {Path.GetFullPath(tsv)}");
        }

        // ============================================ "niemals dicht", measured
        private static void MeasureSkyMax()
        {
            Aim(new Vector3(0f, SeatForest, 0f), new Vector3(-40f, MoonAz, 0f), 90f);
            IsolateClouds(true);
            Clouds(true);
            float P = EnvironmentsBuilder.CloudPeriod;
            const int N = 60;
            float worst = 0f; double worstCoverage = 0;
            for (int i = 0; i < N; i++)
            {
                SetClock(P * i / N);
                var b = Grab(MW, MH, Color.black);
                var w = Grab(MW, MH, Color.white);
                float mx = 0f; int lit = 0;
                for (int p = 0; p < b.Length; p++)
                {
                    float a = Mathf.Clamp01(1f - (w[p].g - b[p].g));
                    if (a > mx) mx = a;
                    if (a > 0.02f) lit++;
                }
                if (mx > worst) worst = mx;
                worstCoverage = Math.Max(worstCoverage, lit / (double)b.Length);
            }
            Debug.Log($"[GloomhavenVR][CloudPreview] NEVER DENSE, measured: over {N} instants spanning the whole "
                      + $"cycle, in a 90 deg view of the sky, the single most opaque pixel anywhere reached alpha "
                      + $"{worst:F4}. The shader's structural ceiling is CloudAlpha = {EnvironmentsBuilder.CloudMaxAlpha:F2} "
                      + "and the measured peak is "
                      + $"under it because the noise field's own maximum does not drive the coverage term to 1 "
                      + "(the bake log prints that distribution). At most "
                      + $"{worstCoverage * 100f:F1}% of the sky carried more than 2% opacity at any instant.");
        }

        // ==================================================== THE COST'S INPUT
        // AND THE THING I NEARLY GOT WRONG. The first version of this measured how
        // much of the frame the clouds visibly CHANGED — 1.5 % from the seat, with
        // the canopy over it — and that number is worthless as a cost input,
        // because the cloud shell is in the BACKGROUND queue and the trees are in
        // Geometry. The shell rasterises FIRST. The canopy hides its result; it
        // does not save it a single fragment. What the GPU is billed for is the
        // shell's SCREEN COVERAGE, which is a property of the frustum alone, and
        // that is what is measured here — by swapping the band's material for a
        // flat white unlit one, so every fragment the shell issues shows up
        // whatever the cloud maths would have done with it.
        private static void MeasureShellCoverage()
        {
            // THE PROBE IS THE SHIPPED MATERIAL WITH ITS DIALS FORCED, not a
            // borrowed `Unlit/Color`. The first attempt used one and it reported
            // 0.0 % coverage in every view — an opaque white cap that drew nothing,
            // silently, with no error in the log. Rather than debug a probe, this
            // clones the real material and drives it to a=1 everywhere
            // (coverage threshold below the field's floor, no elevation fade, no
            // moon taper) with the in-scatter zeroed. Over a WHITE clear, every
            // fragment the shell actually issues comes back BLACK — because
            // premultiplied blending with a=1 replaces the background — and every
            // pixel the shell never touched stays white. It is the shipped vertex
            // stage on the shipped mesh, so it cannot disagree with the thing it
            // is measuring about where the geometry lands.
            var mr = _cloudBand.GetComponent<MeshRenderer>();
            var real = mr.sharedMaterial;
            var white = new Material(real);
            white.SetFloat("_CloudAlpha", 1f);
            white.SetFloat("_CloudCut", -1f);        // coverage saturates everywhere
            white.SetFloat("_CloudSharp", 12f);
            white.SetFloat("_CloudMoonMin", 1f);     // no taper
            white.SetFloat("_CloudElevLo", -1f);     // no elevation fade
            white.SetFloat("_CloudElevHi", -0.99f);
            white.SetFloat("_CloudScatBase", 0f);    // ...and no light of its own
            white.SetFloat("_CloudScatFwd", 0f);
            var shots = new (string name, Vector3 euler, float fov, float h)[]
            {
                ("seat 74deg, looking at the moon's bearing", new Vector3(-14f, MoonAz, 0f), 74f, SeatForest),
                ("standing, up at the canopy tear 26deg", new Vector3(-MoonAlt + 4f, MoonAz, 0f), 26f, HeadForest),
                ("open sky 75deg", new Vector3(-34f, MoonAz, 0f), 75f, SeatForest),
                ("straight up 90deg (worst case)", new Vector3(-89f, MoonAz, 0f), 90f, HeadForest),
                ("level at the horizon 90deg (best case)", new Vector3(0f, MoonAz, 0f), 90f, SeatForest),
            };
            IsolateClouds(true);
            Clouds(true);
            foreach (var s in shots)
            {
                Aim(new Vector3(0f, s.h, 0f), s.euler, s.fov);
                mr.sharedMaterial = white;
                var raster = Grab(W, H, Color.white);
                int rasterPx = raster.Count(c => c.g < 0.5f);
                mr.sharedMaterial = real;
                SetClock(413.7f);
                var b = Grab(W, H, Color.black);
                var w = Grab(W, H, Color.white);
                int kept = 0;
                for (int i = 0; i < b.Length; i++)
                    if (1f - (w[i].g - b[i].g) > 0.5f / 255f) kept++;
                Debug.Log($"[GloomhavenVR][CloudPreview] SHELL COVERAGE, {s.name}: rasterises "
                          + $"{100f * rasterPx / raster.Length:F1}% of the frame; of the whole frame "
                          + $"{100f * kept / b.Length:F1}% survives the clip() and reaches the blend, i.e. "
                          + $"{(rasterPx > 0 ? 100f * kept / rasterPx : 0f):F1}% of what the shell covers. "
                          + "The first number is what the fragment program is billed for and it does NOT "
                          + "shrink when trees are in the way (Background queue draws before Geometry); the "
                          + "second is what the frame-buffer blend is billed for.");
            }
            mr.sharedMaterial = real;
            UnityEngine.Object.DestroyImmediate(white);
            IsolateClouds(false);
        }

        // ========================================= THE OTHER ROOM IS UNTOUCHED
        // The clouds ship FOREST-ONLY. This method exists to PROVE that rather
        // than to assert it: it loads Env_Cellar and looks for a CloudBand node,
        // and the expected, passing outcome is that there is none. It also still
        // renders the two cellar frames, because "the cellar is unchanged" is a
        // claim about pixels and those frames are the ones a later round would
        // diff against if it ever turns the band on there.
        //
        // HOW THIS ENDED UP HERE: the band shipped in BOTH rooms for one bake, on
        // the argument that it is the same sky. The check below reported the band
        // changing 0.000 % of the cellar's frame — and the frames turned out not
        // to contain the window at all, so the number was empty. That is the
        // "measure the picture, not the state" trap with the camera pointed at a
        // wall, and the fix was not a better probe: the user asked for clouds in
        // the WOOD, and an unverified full-screen fragment program in a room he
        // did not ask about is a bill, not a feature.
        private static void Cellar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Env_Cellar.prefab");
            if (prefab == null) { Debug.LogWarning("[GloomhavenVR][CloudPreview] Env_Cellar.prefab missing."); return; }
            UnityEngine.Object.DestroyImmediate(_inst);
            _inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Shader.SetGlobalFloat("_GhvrIndoor", 1f);       // the cellar's own ambient rules
            var dome = _inst.transform.Find("StarDome");
            _domeRen = dome.GetComponent<Renderer>();
            _starsRen = dome.Find("StarField")?.GetComponent<Renderer>();
            _cloudBand = dome.Find("CloudBand")?.gameObject;
            _roomGeo = _inst.transform.Find("RoomGeo")?.gameObject;
            FastForward(_inst.transform);
            SetClock(413.7f);
            if (_cloudBand == null)
            {
                // THE EXPECTED OUTCOME. Shoot the two frames anyway so the
                // untouched cellar sky is on record at this build.
                float h0 = HeadY(EnvRoomBuilder.CellarPlaySpaceDia, StandOverBoard);
                Aim(new Vector3(0f, h0, 0f), new Vector3(6f, 48f, 0f), 70f);
                Shoot("cloud_cellar_unchanged_window");
                Aim(new Vector3(0f, h0, 0f), new Vector3(-22f, 48f, 0f), 55f);
                Shoot("cloud_cellar_unchanged_windowup");
                Debug.Log("[GloomhavenVR][CloudPreview] CELLAR: no CloudBand node — CORRECT. The clouds are "
                          + "forest-only, so the cellar's sky is bit-identical to the build before this feature "
                          + "existed and pays nothing for it. Frames on record for a later round to diff.");
                Shader.SetGlobalFloat("_GhvrIndoor", 0f);
                return;
            }
            // PreviewEnvironments' own cellar framing (its 'ReadmeC' station):
            // standing head height for that room, turned toward the window wall.
            float head = HeadY(EnvRoomBuilder.CellarPlaySpaceDia, StandOverBoard);
            foreach (var (nm, euler, fov) in new (string, Vector3, float)[]
                     { ("window", new Vector3(6f, 48f, 0f), 70f), ("windowup", new Vector3(-22f, 48f, 0f), 55f) })
            {
                Aim(new Vector3(0f, head, 0f), euler, fov);
                Clouds(true); var a = Grab(W, H, Color.black); Shoot($"cloud_cellar_after_{nm}");
                Clouds(false); var b = Grab(W, H, Color.black); Shoot($"cloud_cellar_before_{nm}");
                Clouds(true);
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 1e-5f)
                        changed++;
                Debug.Log($"[GloomhavenVR][CloudPreview] CELLAR {nm} (head {head:F2} m, fov {fov:F0}): the cloud band "
                          + $"changed {100f * changed / a.Length:F3}% of the frame ({changed} px). If that is ~0 the "
                          + "band is invisible indoors and is paying its shader cost for nothing there.");
            }
            Shader.SetGlobalFloat("_GhvrIndoor", 0f);
        }

        private static void MeasureFill()
        {
            SetClock(413.7f);
            // The seated view, room and all — i.e. what the player actually looks
            // at, with the canopy and the trunks covering most of the sky.
            Place(0, SeatForest);
            IsolateClouds(false);
            Clouds(true);
            var withC = Grab(W, H, Color.black);
            Clouds(false);
            var without = Grab(W, H, Color.black);
            int touched = 0;
            for (int i = 0; i < withC.Length; i++)
                if (Mathf.Abs(withC[i].r - without[i].r) + Mathf.Abs(withC[i].g - without[i].g)
                    + Mathf.Abs(withC[i].b - without[i].b) > 1e-5f) touched++;
            Clouds(true);

            // ...and the same for the wide sky-only frame, which is the worst case
            // the layer can ever be asked for.
            Place(2, SeatForest);
            Clouds(true);
            var skyWith = Grab(W, H, Color.black);
            Clouds(false);
            var skyWithout = Grab(W, H, Color.black);
            int skyTouched = 0;
            for (int i = 0; i < skyWith.Length; i++)
                if (Mathf.Abs(skyWith[i].r - skyWithout[i].r) + Mathf.Abs(skyWith[i].g - skyWithout[i].g)
                    + Mathf.Abs(skyWith[i].b - skyWithout[i].b) > 1e-5f) skyTouched++;
            Clouds(true);
            if (_roomGeo != null) _roomGeo.SetActive(true);

            Debug.Log($"[GloomhavenVR][CloudPreview] FILL: from the seat, with the room standing, the cloud layer "
                      + $"CHANGED {100f * touched / withC.Length:F2}% of the frame ({touched} of {withC.Length} px). "
                      + $"In the unobstructed 75 deg sky view it changed {100f * skyTouched / skyWith.Length:F2}%. "
                      + "That is the fraction that survives the clip(); the shell RASTERISES more than that and "
                      + "discards the rest, so the blend cost scales with the first number and the shader cost with "
                      + "the shell's screen coverage. Both are in FOREST-CLOUDS.md's cost arithmetic.");
        }
    }
}
