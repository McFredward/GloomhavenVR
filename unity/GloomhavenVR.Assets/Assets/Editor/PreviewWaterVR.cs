// GloomhavenVR companion project — the WaterVR CONTACT SHEET.
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
//  ARRANGEMENT of geometry — SEVENTEEN separate 1 m quads, which is exactly how
//  many TERRAIN_Water_Plane instances the report's room holds and exactly how the
//  game places them — and renders SEVERAL CANDIDATE SETTINGS at the SAME two
//  instants of the shader clock, from a camera down at the water's own level.
//  That answers, without a headset:
//    * does the shader COMPILE and DRAW (a pink quad or an empty frame is the
//      single most likely way a round like this is lost),
//    * is the surface SLOW — the two instants are one second apart, so how far
//      the pattern travels between two frames IS the speed, in quad widths,
//    * does it have RELIEF — the grazing station puts the eye 15 cm above the
//      water so a crest breaks the far rim's line, which is the one thing a
//      top-down frame can never show and the one thing the last round got wrong,
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
//  below and every frame is named `_synthbump` to say so. The MOTION, the wave
//  scale, the contrast, the calmness, the relief and the view-independence are
//  all the shipped ones; the GRAIN of the fine ripple is not, and no judgement
//  about the pattern's own texture may be read off these frames.
//
//  AND THE MESH IS AUTHORED HERE, which is a second honest difference. On
//  hardware the film's mesh is the game's own, subdivided by
//  Core/WaterSwellMesh.cs; the game's mesh is not on this machine either, so
//  the quads below are built as grids directly. The SUBDIVISION DENSITY is the
//  same 8x8 the driver's own ceiling produces for a 1 m quad, so the sampling of
//  the swell is the shipped one even though the source quad is not.
using System;
using System.Collections.Generic;
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

        // WaterOwnSurface's own constants, mirrored for the reason above.
        private const float AnisoTame = 0.70f;
        private const float SwellWavelength = 1.1f;
        private const float SwellSpeed = 0.20f;
        private const float MaxSwellAmplitude = 0.06f;
        private const float QuadWidth = 1.0f;

        // The live _Smoothness on hardware: the game authors 0.754 and the driver's reflection
        // cap brings it down to this, which is the value the glint exponent must be judged at.
        private const float Smoothness = 0.219f;

        private static readonly Vector4 LightLocal =
            new Vector4(0.34f, 0.22f, 0.91f, 0f).normalized;

        /// <summary>One column of the contact sheet: everything the four dials and the two
        /// constants resolve to, named so a frame says which settings produced it.</summary>
        private struct Candidate
        {
            public string Name;
            public float WaveScale;      // [Water] WaveScale
            public float RippleSpeed;    // [Water] RippleSpeed
            public float Shimmer;        // [Water] Shimmer
            public float SwellHeight;    // [Water] SwellHeight, a fraction of the quad's width
            public float WaveShade;      // the shader's own body-shading depth
            public float RippleStrength; // WaterOwnSurface.NormalStrength's output
            /// <summary>True for the ModBuild 163 reference column: the authored tilings
            /// untamed, the two layers at equal weight, and the drift rates converted so that
            /// the surface moves at the speed that build ACTUALLY produced on screen.</summary>
            public bool AsReported;
        }

        /// <summary>
        /// The sheet. One recommended default, one calmer, one livelier — and the shipped
        /// ModBuild 163 look as a reference column, because a candidate is only judged against
        /// what the user was actually looking at when he wrote "extrem schnelle (und viele)
        /// hektische weiße Streifen".
        /// </summary>
        private static readonly Candidate[] Candidates =
        {
            new Candidate
            {
                Name = "asreported163", WaveScale = 1f, RippleSpeed = 1f, Shimmer = 0.35f,
                SwellHeight = 0f, WaveShade = 0.18f, RippleStrength = 1.2f, AsReported = true,
            },
            new Candidate
            {
                Name = "calm", WaveScale = 1f, RippleSpeed = 0.5f, Shimmer = 0.10f,
                SwellHeight = 0.045f, WaveShade = 0.35f, RippleStrength = 0.5f,
            },
            new Candidate
            {
                Name = "calmer", WaveScale = 1.6f, RippleSpeed = 0.3f, Shimmer = 0.05f,
                SwellHeight = 0.025f, WaveShade = 0.26f, RippleStrength = 0.35f,
            },
            new Candidate
            {
                Name = "livelier", WaveScale = 0.7f, RippleSpeed = 0.8f, Shimmer = 0.18f,
                SwellHeight = 0.05f, WaveShade = 0.42f, RippleStrength = 0.7f,
            },
            // THE A/B FOR THE SWELL ITSELF: the recommended column with the geometry switched
            // off. Everything else is identical, so the difference between this frame and 'calm'
            // is exactly what the displaced mesh contributes — which is the question "wirklich 3D
            // wellen" asks, and the one a single frame of a single setting cannot answer.
            new Candidate
            {
                Name = "calmflat", WaveScale = 1f, RippleSpeed = 0.5f, Shimmer = 0.10f,
                SwellHeight = 0f, WaveShade = 0.35f, RippleStrength = 0.5f,
            },
        };

        /// <summary>The two instants, ONE SECOND APART on purpose: at a drift measured in world
        /// units per second, the distance the pattern moves between two frames is the speed in
        /// metres, and every quad below is exactly one metre across to read it off against.</summary>
        private static readonly float[] Clocks = { 0f, 1f };

        /// <summary>The stations. BRIM and GRAZE are the measurement; the other two are controls.
        ///
        /// <para>BRIM puts the eye 6 cm above the undisturbed water and looks along it. At that
        /// height a 4.5 cm crest is most of the eye's own elevation, so the near waves stand
        /// against the stone beyond the pool and against the far wall — a crest BREAKING THE
        /// HORIZON is a silhouette, and a silhouette is the only proof of relief a still frame can
        /// carry. It is not a seat anybody sits in; it is the shot that cannot be argued with.</para>
        ///
        /// <para>GRAZE is a player leaning right down over the pool, 45 cm above it. STAND is the
        /// ordinary standing view at 1.6 m, which is where the verdict "das sieht ruhig aus" or
        /// "das tickt aus" is actually formed. STEEP is the view-independence control: same clock,
        /// different camera, and any difference between it and the others is a term that would
        /// differ between the two MultiPass eyes.</para></summary>
        private static readonly (string Name, Vector3 Pos, Vector3 Look)[] Stations =
        {
            ("brim", new Vector3(0f, 0.07f, -2.60f), new Vector3(0f, 0.035f, 0.80f)),
            ("graze", new Vector3(0f, 0.45f, -3.20f), new Vector3(0f, 0f, 0.40f)),
            ("stand", new Vector3(0f, 1.60f, -3.40f), new Vector3(0f, 0f, 0.30f)),
            ("steep", new Vector3(0f, 2.60f, -1.10f), new Vector3(0f, 0f, 0.20f)),
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

            BuildBasin();
            Material water = new Material(shader) { name = "WaterVRPreview" };
            Texture2D bump = SynthBump();
            BuildWaterField(water);

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

            // ================================================== ONE CLOCK FOR EVERY COLUMN ==
            // The shader reads `_Time.y + _GhvrTimeOfs`, so a sheet meant to compare candidates
            // depends on _Time.y being the SAME for every frame in the batch. MEASURED, not
            // assumed: the first frame is rendered a second time as the very LAST frame of the
            // run, under the same name + "_ctrl", and the two came back byte-identical (mean
            // absolute difference 0.0 over 1280x720x3) — so _Time.y does not advance across
            // Camera.Render calls in batch mode and the offset alone IS the instant.
            //
            // The control frame stays in the sheet. A future Unity that ran the clock would make
            // every comparison here meaningless, and that is a thing to find out from a diff
            // rather than from an argument: an earlier draft of this file "corrected" for a
            // drifting clock by subtracting Time.realtimeSinceStartup, and the control frame is
            // what proved the correction was itself the drift (1.19 mean, 36 peak).
            void SetClock(float t) => Shader.SetGlobalFloat("_GhvrTimeOfs", t);

            foreach (Candidate c in Candidates)
            {
                ApplyCandidate(water, bump, c);
                foreach (float t in Clocks)
                {
                    SetClock(t);
                    Shoot($"watervr_synthbump_{c.Name}_graze_t{Mathf.RoundToInt(t * 100f):D3}",
                          Stations[1].Pos, Stations[1].Look);
                }
                // ...and the silhouette shot, at the first instant only: it answers a question
                // about SHAPE, and the same shape one second later answers it twice.
                SetClock(Clocks[0]);
                Shoot($"watervr_synthbump_{c.Name}_brim_t000", Stations[0].Pos, Stations[0].Look);
                Shoot($"watervr_synthbump_{c.Name}_stand_t000", Stations[2].Pos, Stations[2].Look);
            }

            // ---- the recommended column from the other two stations. SEAT is what the player
            //      actually sits at; STEEP is the view-independence control, and it is a
            //      MEASUREMENT: the same water at the same instant from somewhere else.
            Candidate rec = Candidates[1];
            ApplyCandidate(water, bump, rec);
            SetClock(Clocks[0]);
            Shoot($"watervr_synthbump_{rec.Name}_stand_t000", Stations[2].Pos, Stations[2].Look);
            SetClock(Clocks[0]);
            Shoot($"watervr_synthbump_{rec.Name}_steep_t000", Stations[3].Pos, Stations[3].Look);

            // ---- the FALLBACK path, which is what is on screen when the game material carries
            //      no readable normal map. It has to move too; a still film there would be
            //      ModBuild 162's flat sheet arrived at silently.
            water.SetFloat("_ProcNormal", 1f);
            SetClock(Clocks[0]);
            Shoot($"watervr_procfallback_{rec.Name}_graze_t000", Stations[1].Pos, Stations[1].Look);
            water.SetFloat("_ProcNormal", 0f);

            // ---- THE CLOCK CONTROL. Same candidate, same station, same instant as the very
            //      first frame of the sheet; `compare` it against that frame.
            ApplyCandidate(water, bump, Candidates[0]);
            SetClock(Clocks[0]);
            Shoot($"watervr_synthbump_{Candidates[0].Name}_graze_t000_ctrl",
                  Stations[1].Pos, Stations[1].Look);

            Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
            cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
        }

        /// <summary>
        /// Write ONE candidate onto the film material, doing exactly the arithmetic
        /// <c>WaterOwnSurface</c> does at runtime — the tame, the weights, the drift resolution
        /// and the swell — so a frame here is a frame of the shipped resolution and not of a set
        /// of numbers chosen for a picture.
        /// </summary>
        private static void ApplyCandidate(Material m, Texture bump, Candidate c)
        {
            m.SetColor("_Color", new Color(AuthoredTint.r, AuthoredTint.g, AuthoredTint.b,
                                           Mathf.Min(AuthoredTint.a, OpacityCap)));
            m.SetTexture("_MainTex", null);
            m.SetTexture("_Normal_Map", bump);
            m.SetFloat("_ProcNormal", 0f);
            m.SetVector("_LightDir", LightLocal);
            m.SetFloat("_Smoothness", Smoothness);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_ZTest", 4f);
            m.SetFloat("_SrcBlend", 5f);      // SrcAlpha
            m.SetFloat("_DstBlend", 10f);     // OneMinusSrcAlpha
            m.renderQueue = 2900;             // the authored queue

            m.SetFloat("_Shimmer", c.Shimmer);
            m.SetFloat("_WaveShade", c.WaveShade);
            m.SetFloat("_NormalStrength", c.RippleStrength);

            Vector4 tiling = c.AsReported ? Tilings : Tame(Tilings, c.WaveScale);
            Vector4 weights = c.AsReported
                ? new Vector4(1f, 1f, 0f, 0f)   // ModBuild 163 added both layers at full amplitude
                : Weights(tiling);
            m.SetVector("_NormalTilings", tiling);
            m.SetVector("_LayerWeights", weights);

            Vector4 rateA = Resolve(SpeedA, c.RippleSpeed);
            Vector4 rateB = Resolve(SpeedB, c.RippleSpeed);
            if (c.AsReported)
            {
                // ModBuild 163 added the drift AFTER the tiling, so its rate meant texture
                // repeats per second and the speed on screen was rate/tiling. Dividing by the
                // tiling reproduces THAT surface exactly under the shipped shader, which is what
                // makes this column a fair reference instead of a straw man.
                rateA = new Vector4(rateA.x / Tilings.x, rateA.y / Tilings.y, 0f, 0f);
                rateB = new Vector4(rateB.x / Tilings.z, rateB.y / Tilings.w, 0f, 0f);
            }
            m.SetVector("_WaterUVAnimSpeedA", rateA);
            m.SetVector("_WaterUVAnimSpeedB", rateB);

            float amp = Mathf.Clamp(QuadWidth * c.SwellHeight, 0f, MaxSwellAmplitude);
            m.SetFloat("_SwellAmp", amp);
            m.SetFloat("_SwellWave", SwellWavelength * c.WaveScale);
            m.SetFloat("_SwellSpeed", SwellSpeed * c.WaveScale * c.RippleSpeed);

            Debug.Log(
                $"[GloomhavenVR][WaterPreview] candidate '{c.Name}': tilings {tiling} "
                + $"(= one repeat every {Wave(tiling.x)} x {Wave(tiling.y)} m and "
                + $"{Wave(tiling.z)} x {Wave(tiling.w)} m), weights {weights.x:0.###}/"
                + $"{weights.y:0.###}, drift A {rateA.x:0.###},{rateA.y:0.###} and B "
                + $"{rateB.x:0.###},{rateB.y:0.###} world units/s, swell {amp:0.###} m at "
                + $"{SwellWavelength * c.WaveScale:0.##} m / "
                + $"{SwellSpeed * c.WaveScale * c.RippleSpeed:0.###} m/s, shimmer {c.Shimmer:0.##} "
                + $"exponent {Mathf.Lerp(1f, 8f, Smoothness):0.##}");
        }

        private static string Wave(float tiling)
        {
            float a = Mathf.Abs(tiling);
            return a > 1e-4f ? (1f / a).ToString("0.##") : "inf";
        }

        /// <summary>WaterOwnSurface.TameTilings, mirrored.</summary>
        private static Vector4 Tame(Vector4 authored, float waveScale)
        {
            Vector2 a = TameLayer(new Vector2(authored.x, authored.y), waveScale);
            Vector2 b = TameLayer(new Vector2(authored.z, authored.w), waveScale);
            return new Vector4(a.x, a.y, b.x, b.y);
        }

        private static Vector2 TameLayer(Vector2 t, float waveScale)
        {
            float ax = Mathf.Abs(t.x), ay = Mathf.Abs(t.y);
            if (ax <= 0f || ay <= 0f)
                return t / waveScale;
            float gm = Mathf.Sqrt(ax * ay);
            float p = 1f - AnisoTame;
            return new Vector2(gm * Mathf.Pow(ax / gm, p) * Mathf.Sign(t.x),
                               gm * Mathf.Pow(ay / gm, p) * Mathf.Sign(t.y)) / waveScale;
        }

        /// <summary>WaterOwnSurface.LayerWeights, mirrored.</summary>
        private static Vector4 Weights(Vector4 resolved)
        {
            float fa = Mathf.Sqrt(Mathf.Abs(resolved.x * resolved.y));
            float fb = Mathf.Sqrt(Mathf.Abs(resolved.z * resolved.w));
            if (fa <= 0f || fb <= 0f)
                return new Vector4(0.5f, 0.5f, 0f, 0f);
            float sum = fa + fb;
            return new Vector4(fb / sum, fa / sum, 0f, 0f);
        }

        /// <summary>WaterOwnSurface.ScrollRate, mirrored: .xy is the per-axis rate, .z a
        /// per-layer multiplier, _WaterNoiseSpeed.x the shared clock scale, and the dial on
        /// top.</summary>
        private static Vector4 Resolve(Vector4 speed, float dial)
        {
            float layer = speed.z > 0f ? speed.z : 1f;
            float clock = NoiseSpeed.x > 0f ? NoiseSpeed.x : 1f;
            float k = layer * clock * Mathf.Max(dial, 0f);
            return new Vector4(speed.x * k, speed.y * k, 0f, 0f);
        }

        /// <summary>
        /// The basin: a flat unlit checker bed under the film, and a low rim of the same stone
        /// BEYOND the pool so the water has something to be a silhouette against.
        ///
        /// <para>IT IS A CHECKER ON PURPOSE. The film is transparent, and two of the failures
        /// worth catching are about what is UNDER it: an additive blend would make the water
        /// LIGHTER than the stone (the photographed defect), and a film that hid the floor
        /// entirely would mean the alpha cap never landed. A flat colour would hide both; a
        /// pattern you can read through the water shows them at a glance.</para>
        ///
        /// <para>THE FAR WALL IS THE HORIZON. From the grazing station the pool's far edge cuts
        /// across the stone behind it, so a crest that rises above that line is visible AS a
        /// silhouette — which is the only way a still frame can show relief, and the thing the
        /// last round's top-down render could not have shown however long anyone looked at it.</para>
        /// </summary>
        private static void BuildBasin()
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

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(9f, 9f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "BasinBed";
            // NINE CENTIMETRES under the film, because that is what the hardware FLOOR CENSUS
            // measured: the film at y[0.0..0.0] with TERRAIN_Crypt_Water_02_Base at
            // y[-0.34..-0.09] directly beneath it. THIS DEPTH IS PART OF THE TEST. An earlier
            // draft left the bed 3 cm down, and the first swell render came back with a scatter of
            // small holes in the pool — the troughs (3.5 cm at the time) reaching BELOW the bed, where ZTest
            // LEqual rejects them. That is exactly the collision WaterOwnSurface.MaxSwellAmplitude
            // is set against, and with the bed at the measured depth it cannot happen: 6 cm of
            // ceiling under 9 cm of clearance.
            floor.transform.position = new Vector3(0f, -0.09f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(9f, 9f, 1f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wall.name = "FarWall";
            wall.transform.position = new Vector3(0f, 1.2f, 4.6f);
            wall.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            wall.transform.localScale = new Vector3(14f, 2.4f, 1f);
            var wallMat = new Material(Shader.Find("Unlit/Color"));
            wallMat.color = new Color(0.10f, 0.10f, 0.12f, 1f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
        }

        /// <summary>
        /// SEVENTEEN separate 1 m quads on a hex lattice, which is what the game places: the
        /// report's room holds 17 TERRAIN_Water_Plane instances, each with its own transform and
        /// its own UV running 0..1 across itself. One big quad would stretch the ripple over five
        /// metres and every judgement about its scale would be wrong by a factor of five.
        ///
        /// <para>THE ROWS ARE OFFSET HALF A STEP, like brickwork and like the hex rows the game
        /// lays these out in, so that no two quads share a UV origin. The shipped shader keys both
        /// the ripple and the swell on WORLD position, so per-quad repetition cannot arise either
        /// way — but a preview on a lattice where every quad sat at the same offset would hide a
        /// whole class of repetition bug if that ever stopped being true. The rows are a full metre
        /// apart rather than a hex row's 0.866: square quads at 0.866 OVERLAP, and two transparent
        /// films over each other are twice the opacity, which would be read off these frames as a
        /// look rather than as a staging mistake.</para>
        ///
        /// <para>EACH QUAD IS A GRID, not two triangles. The swell displaces vertices, and four
        /// corners cannot carry a wave; on hardware the driver subdivides the game's own mesh to
        /// an edge of about 12 cm, so these are built at the 8x8 that produces. Their bounds are
        /// padded by the swell's ceiling for the same reason the driver pads: Unity culls against
        /// the mesh's bounds and a crest outside them vanishes — one eye first, under
        /// MultiPass.</para>
        /// </summary>
        private static void BuildWaterField(Material water)
        {
            Mesh grid = GridQuad(8);
            var root = new GameObject("WaterFilm");
            int[] counts = { 2, 4, 5, 4, 2 };   // 17
            for (int row = 0; row < counts.Length; row++)
            {
                int n = counts[row];
                float z = (row - 2) * 1.0f;
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) * 0.5f) * 1.0f;
                    var q = new GameObject($"TERRAIN_Water_Plane_{row}_{i}");
                    q.transform.SetParent(root.transform, false);
                    q.transform.position = new Vector3(x, 0f, z);
                    q.AddComponent<MeshFilter>().sharedMesh = grid;
                    q.AddComponent<MeshRenderer>().sharedMaterial = water;
                }
            }
        }

        /// <summary>A 1 m x 1 m horizontal grid, UV 0..1 across it, <paramref name="cells"/> per
        /// side, with the swell's ceiling padded into its bounds.</summary>
        private static Mesh GridQuad(int cells)
        {
            var verts = new List<Vector3>((cells + 1) * (cells + 1));
            var uvs = new List<Vector2>((cells + 1) * (cells + 1));
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                {
                    float u = i / (float)cells, v = j / (float)cells;
                    verts.Add(new Vector3(u - 0.5f, 0f, v - 0.5f));
                    uvs.Add(new Vector2(u, v));
                }
            var tris = new List<int>(cells * cells * 6);
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int v0 = j * (cells + 1) + i;
                    int v1 = v0 + 1, v2 = v0 + cells + 1, v3 = v2 + 1;
                    tris.Add(v0); tris.Add(v2); tris.Add(v1);
                    tris.Add(v1); tris.Add(v2); tris.Add(v3);
                }
            var m = new Mesh { name = "WaterPreviewGrid" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            Bounds padded = m.bounds;
            padded.Expand(MaxSwellAmplitude * 2f);
            m.bounds = padded;
            return m;
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
