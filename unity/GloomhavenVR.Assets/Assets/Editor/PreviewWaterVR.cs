// GloomhavenVR companion project — the WaterVR CONTACT SHEET and the two MEASUREMENTS.
//
// Batch: xvfb-run -a Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.WaterVRPreview.RenderAll -logFile water-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device), and the device must
//   support shader model 4.6 or the tessellated SubShader cannot be exercised at all — RenderAll
//   says so in the log and in the frame names rather than quietly rendering the fallback.
//   Output goes to $WATERVR_PREVIEW_OUT, or ./water-previews when that is unset.
//
// ============================================================================
//  WHY THIS EXISTS, AND WHAT IT CAN AND CANNOT SETTLE
// ============================================================================
//  EnvironmentsPreview renders the mod's OWN Env_*.prefab shells. The water film is not one of
//  those: it is the GAME's TERRAIN_Water_Plane, placed by Apparance inside a running scenario, and
//  there is no game install on the build machine. So the shipped surface cannot be rendered here.
//
//  WHAT CAN. The shader is a pure function of its properties, and every property the driver writes
//  is known: the hardware WATER SURFACE census read them off TERRAIN_GEN_WaterPlane_Crypt_Mat. So
//  this stages the same MATERIAL on the same ARRANGEMENT of geometry and renders several candidate
//  settings at the same instants of the shader clock.
//
// ============================================================================
//  AND SINCE MODBUILD 166 IT MEASURES INSTEAD OF ONLY PHOTOGRAPHING
// ============================================================================
//  Three hardware rounds have now been spent on "the water moves too much", and each of them
//  shipped a still frame as evidence. A still frame cannot show a rate and it cannot show a
//  direction, so each round's claim that the surface was calmer was an argument. This harness now
//  produces two NUMBERS per column, both written into the log and into water-measurements.txt:
//
//    1. NET TRANSLATION. One frame is cross-correlated against a later one over a range of pixel
//       offsets and the offset of the best match is reported, in pixels and in centimetres. For a
//       pattern that does not travel it is (0, 0). For the ModBuild 165 reference column it is not,
//       and that difference IS the ruling — "es fließt jetzt einmal in die eine Richtung, stoppt
//       kurz und fließt dann wieder in die andere ... Ich will außerdem so gut wie KEIN fließen".
//
//    2. CHANGE RATE. The fraction of pixels whose luminance changes by more than a small threshold,
//       and the mean absolute luminance change, over one second and over four and a half. This is
//       the number "viel zu hektisch" is about.
//
//  BOTH ARE MEASURED FROM A PURPOSE-BUILT STATION, not from a pretty one. `plan` is an
//  ORTHOGRAPHIC camera directly above the pool looking straight down, so an offset in pixels is a
//  displacement in world units exactly rather than approximately, with no perspective to unpick.
//
//  AND THE BASIN IS HIDDEN FOR THE TRANSLATION MEASUREMENT. The bed is a static checker seen
//  through a 45 %-opaque film, so it is the strongest signal in the frame and it never moves: a
//  correlation computed over it would report (0, 0) for any water whatsoever, which is a measurement
//  that cannot fail and therefore says nothing. With the bed hidden the only structure in the frame
//  is the water's own. The CHANGE RATE is measured with the basin in place, because that is the
//  image a player actually sees and the fraction of it that changes is the thing being judged.
//
// ============================================================================
//  THE MESH IS THE ONE THE GAME SUPPLIES, AND THAT IS THE CHANGE THAT MATTERS
// ============================================================================
//  Through ModBuild 164 this harness staged its own 8x8 SUBDIVIDED grids, "because that is what the
//  driver produces". The driver produced nothing of the kind: the mesh swap it relied on could never
//  run, because the game imports its meshes without Read/Write and there is no index buffer to
//  subdivide. A whole hardware round was lost to that, and it was invisible HERE because the
//  preview had quietly staged the successful outcome of the step that was failing.
//
//  So the staged film is the mesh the census actually measured:
//
//      FILM MESH: 'TERRAIN_Water_Plane' 33 verts / 0 tris,
//                 local bounds centre (0,0.02,0) size (1.73,0,1.998)
//
//  — a hexagon 1.73 m across the flats and 1.998 m point to point, carrying exactly 33 vertices.
//  The TOPOLOGY behind those 33 is not knowable from the log (the `0 tris` is Mesh.triangles
//  refusing on a non-readable mesh, not a count), so HexFilm below builds the COARSEST plausible
//  reading: one centre vertex and 32 around the rim, fanned into 32 triangles. If the game's mesh
//  is a grid instead it is finer than this, so every judgement about whether the relief READS is a
//  lower bound. Staging a finer guess would repeat exactly the mistake this file is correcting.
//
// ============================================================================
//  WHAT IT CANNOT
// ============================================================================
//  The bump texture is NOT the game's. 'WaterBump' lives in the game's own bundles and is not on
//  this machine, so a stand-in is synthesised below and every frame is named `_synthbump` to say
//  so. The MOTION, the wave scale, the contrast, the calmness, the relief and the view-independence
//  are all the shipped ones; the GRAIN of the fine ripple is not.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class WaterVRPreview
    {
        private const string ShaderPath = "Assets/Bundle/Environments/WaterVR.shader";

        /// <summary>The FROZEN ModBuild 165 water, for the reference column. It is not in the
        /// bundle and nothing in src/ names it — see the file's own header for why a copy is the
        /// only honest way to keep a reference of a build whose motion terms were DELETED rather
        /// than zeroed.</summary>
        private const string Ref165ShaderPath = "Assets/Editor/WaterVRRef165.shader";

        private const int W = 1280, H = 720;

        // ---- the authored material, read off the hardware WATER SURFACE census of
        //      TERRAIN_GEN_WaterPlane_Crypt_Mat on VFX/Water_Shd_Trans. These are the same numbers
        //      src/GloomhavenVR/Core/WaterOwnSurface.cs carries as its measured fallbacks; they are
        //      repeated here rather than shared because this project does not reference the mod's
        //      assembly. If they ever disagree, WaterOwnSurface is the authority.
        private static readonly Vector4 Tilings = new Vector4(0.14f, 6.00f, -0.12f, -0.20f);
        private static readonly Color AuthoredTint = new Color(0.195f, 0.311f, 0.131f, 0.737f);

        // The tileset's authored SCROLL speeds. The shipped shader no longer reads them — nothing
        // on it scrolls — and they survive here only to drive the ModBuild 165 reference column,
        // which is the build that did.
        private static readonly Vector4 Ref165SpeedA = new Vector4(1.00f, 1.00f, 0.60f, 0f);
        private static readonly Vector4 Ref165SpeedB = new Vector4(0.50f, 1.00f, 1.00f, 0f);
        private static readonly Vector4 Ref165NoiseSpeed = new Vector4(1.00f, 1.00f, 1.00f, 0f);

        // [Water] Opacity's shipped default caps the alpha, exactly as the driver does.
        private const float OpacityCap = 0.45f;

        // WaterOwnSurface's own constants, mirrored for the reason above.
        private const float AnisoTame = 0.70f;
        private const float SwellWavelength = 2.4f;
        private const float SwellPeriod = 1.1f;
        private const float SwellCalmDepth = 0.75f;
        private const float MaxSwellAmplitude = 0.06f;
        private const float TessellationFactor = 4f;
        private static readonly float[] RippleFadeRatios = { 1.618034f, 2.618034f, 4.236068f };
        private static readonly float[] SwellRatios =
            { 1f, 0.618034f, 0.414214f, 0.267949f, 0.732051f, 0.828427f };
        private static readonly float[] SwellDirections = { 17f, 103f, 61f, 148f, 47f, 164f };

        // ModBuild 165's own constants, frozen with its shader.
        private const float Ref165SwellDrift = 0.02f;
        private const float Ref165SwayPeriodA = 13f;
        private const float Ref165SwayPeriodB = 17f;
        private const float Ref165DriftShare = 0.25f;

        // The film's measured footprint and the lattice the game lays it out on. A pointy-top hex
        // 1.73 m across the flats tiles at 1.73 in x, 0.75 x 1.998 in z, with alternate rows offset
        // half a width — which is exactly how these 17 quads sit in the report's room and is what
        // makes "does the pattern repeat tile for tile" a question this sheet can answer.
        private const float HexFlats = 1.73f;
        private const float HexPoints = 1.998f;
        private const int HexRimVerts = 32;   // + 1 centre = the 33 the census counted

        // The live _Smoothness on hardware: the game authors 0.754 and the driver's reflection cap
        // brings it down to this, which is the value the glint exponent must be judged at.
        private const float Smoothness = 0.219f;

        private static readonly Vector4 LightLocal =
            new Vector4(0.34f, 0.22f, 0.91f, 0f).normalized;

        /// <summary>One column of the contact sheet: everything the dials resolve to, named so a
        /// frame says which settings produced it.</summary>
        private struct Candidate
        {
            public string Name;
            public float WaveScale;      // [Water] WaveScale
            public float RippleSpeed;    // [Water] RippleSpeed
            public float Shimmer;        // [Water] Shimmer
            public float SwellHeight;    // [Water] SwellHeight, a fraction of the quad's width
            public float WaveShade;      // the shader's own body-shading depth
            public float RippleStrength; // WaterOwnSurface.NormalStrength's output

            /// <summary>Force the LOD 100 SubShader, i.e. NO tessellation — the geometry A/B.
            /// </summary>
            public bool NoTessellation;

            /// <summary>THE REFERENCE COLUMN. Draws on the frozen ModBuild 165 shader with ModBuild
            /// 165's own dials, so the sheet compares the shipped surface against the surface the
            /// verdict was actually written about rather than against this build's constants fed
            /// different numbers.</summary>
            public bool Ref165;
        }

        /// <summary>
        /// The sheet. The ModBuild 165 reference first, then the shipped ModBuild 166 defaults and
        /// two neighbours, then the two A/B controls. A candidate is only judged against what the
        /// user was actually looking at when he wrote the report.
        /// </summary>
        private static readonly Candidate[] Candidates =
        {
            // THE REFERENCE. ModBuild 165's shipped dials on ModBuild 165's shader: the swell at
            // 3.6 cm bobbing once every 9 s with a 2.4 mm/s trace drift, and the ripple as a
            // one-way drift plus a reversing sway. Every property of the look the user was judging
            // when he wrote "Immer noch viel zu hektisch und es fließt jetzt einmal in die eine
            // Richtung, stoppt kurz und fließt dann wieder in die andere".
            new Candidate
            {
                Name = "ref165", WaveScale = 1f, RippleSpeed = 0.12f,
                Shimmer = 0.05f, SwellHeight = 0.014f, WaveShade = 0.35f, RippleStrength = 0.5f,
                Ref165 = true,
            },
            // THE SHIPPED DEFAULTS.
            new Candidate
            {
                Name = "puddle", WaveScale = 1f, RippleSpeed = 0.035f, Shimmer = 0.03f,
                SwellHeight = 0.005f, WaveShade = 0.35f, RippleStrength = 0.5f,
            },
            new Candidate
            {
                // WAVE SCALE STAYS AT 1 ON BOTH NEIGHBOURS. It is the one dial that moves the
                // LATTICE MISMATCH — the preview log prints it per candidate — and 1.0 is the
                // setting chosen for scoring best against the film lattice. A neighbour column that
                // also changed the scale would be asking the user to judge calmness and tile
                // repetition in the same frame.
                Name = "stiller", WaveScale = 1f, RippleSpeed = 0.02f, Shimmer = 0.02f,
                SwellHeight = 0.003f, WaveShade = 0.30f, RippleStrength = 0.35f,
            },
            new Candidate
            {
                Name = "livelier", WaveScale = 1f, RippleSpeed = 0.06f, Shimmer = 0.05f,
                SwellHeight = 0.009f, WaveShade = 0.40f, RippleStrength = 0.6f,
            },
            // A/B ONE — THE RELIEF. The shipped column with the geometry switched off. The
            // difference between this and 'puddle' is exactly what the displaced surface
            // contributes, which is the question "wirklich 3D wellen" asks.
            new Candidate
            {
                Name = "puddleflat", WaveScale = 1f, RippleSpeed = 0.035f, Shimmer = 0.03f,
                SwellHeight = 0f, WaveShade = 0.35f, RippleStrength = 0.5f,
            },
            // A/B TWO — THE TESSELLATOR. The shipped column, same amplitude, forced onto the
            // LOD 100 SubShader. If this frame equals 'puddle' then the hull/domain stages did
            // NOTHING and the round is ModBuild 164 again; if it differs, the GPU added geometry
            // and the proof is a photograph rather than a claim.
            new Candidate
            {
                Name = "puddlenotess", WaveScale = 1f, RippleSpeed = 0.035f, Shimmer = 0.03f,
                SwellHeight = 0.005f, WaveShade = 0.35f, RippleStrength = 0.5f,
                NoTessellation = true,
            },
        };

        /// <summary>The two instants of the visual sheet. FOUR AND A HALF SECONDS APART, which is
        /// the interval ModBuild 165's change-rate number was quoted over, so the pair of frames a
        /// reader compares by eye is the pair the measurement is computed from.</summary>
        private static readonly float[] Clocks = { 0f, 4.5f };

        /// <summary>THE TEMPORAL STRIP: one column at six evenly spaced instants over ten seconds.
        /// This is the frame set that answers "zu hektisch", because a rate cannot be read off a
        /// single still however carefully it is composed — the two-frame sheet above shows that the
        /// surface is different, and only a strip shows how fast it got there.</summary>
        private static readonly float[] StripClocks = { 0f, 2f, 4f, 6f, 8f, 10f };

        /// <summary>The stations. All of them are set back for the real film size: seventeen hexes
        /// 1.73 m across is a pool 8.6 m wide and 6 m deep, where ModBuild 164's harness staged
        /// 1 m quads.
        ///
        /// <para>BRIM puts the eye 7 cm above the undisturbed water and looks along it. At that
        /// height a crest stands against the stone beyond the pool — a crest BREAKING THE HORIZON
        /// is a silhouette, and a silhouette is the only proof of relief a still frame can
        /// carry.</para>
        ///
        /// <para>GRAZE is a player leaning down over the pool. STAND is the ordinary standing view,
        /// which is where the verdict "das sieht ruhig aus" is actually formed. STEEP is the
        /// view-independence control: same clock, different camera, and any difference between it
        /// and the others is a term that would differ between the two MultiPass eyes.</para>
        ///
        /// <para>WIDE is high and back, looking down the whole pool, so five hexes across and five
        /// rows deep are in one frame. If the field repeated tile for tile it would be unmissable
        /// here and nowhere else.</para></summary>
        private static readonly (string Name, Vector3 Pos, Vector3 Look)[] Stations =
        {
            // THE FIRST FOUR STAND AT THE POOL'S NEAR EDGE, not back from it. The first render of
            // this sheet put them 5 m away from a pool 8.6 m wide, and every column came back
            // near-featureless for the same reason a lake looks flat from a hill: at that distance
            // one pixel averages half a metre of water and a small crest has nothing left to be.
            // The nearest hex now fills the lower third of the frame, which is where the verdict is
            // actually formed — the player is at the table, not across the room.
            ("brim", new Vector3(0f, 0.07f, -4.20f), new Vector3(0f, 0.045f, 2.00f)),
            ("graze", new Vector3(0f, 0.35f, -4.40f), new Vector3(0f, 0f, -0.60f)),
            ("stand", new Vector3(0f, 1.60f, -5.00f), new Vector3(0f, 0f, -0.50f)),
            ("steep", new Vector3(1.60f, 2.20f, -3.40f), new Vector3(0f, 0f, -0.80f)),
            // ...and WIDE keeps its distance on purpose: it is the frame where seeing all nineteen
            // tiles at once is the whole point.
            ("wide", new Vector3(0f, 5.00f, -6.60f), new Vector3(0f, 0f, 0.20f)),
        };

        // ============================================ THE MEASUREMENT STATION ==
        // ORTHOGRAPHIC, DIRECTLY OVERHEAD. An offset in pixels is then a displacement in world
        // units exactly — no perspective, no depth, no station-dependent scale — which is what lets
        // the translation measurement be quoted in centimetres rather than in pixels of an
        // unspecified frame.
        private const float PlanHeight = 6f;
        private const float PlanHalfHeightWU = 1.5f;   // vertical half-extent: 3 m of water
        private const int PlanSearchPx = 40;           // +/- 16.7 cm at this scale

        /// <summary>World units per pixel at the plan station: the vertical extent is
        /// 2 * <see cref="PlanHalfHeightWU"/> over H pixels. The horizontal is the same number,
        /// because an orthographic camera's pixels are square.</summary>
        private static float PlanWUPerPixel => 2f * PlanHalfHeightWU / H;

        /// <summary>How much a pixel's luminance must change to count as CHANGED, on the 0..1
        /// gamma-encoded scale the PNGs carry. 1.5/255 is a little over half of one 8-bit step:
        /// small enough that a real change is caught, large enough that the render target's own
        /// quantisation is not. The same number is applied to every column, which is what makes the
        /// comparison between them mean something even though the absolute figure depends on it.
        /// </summary>
        private const float ChangeThreshold = 1.5f / 255f;

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
            // graphics device got a usable program out of it. Checking it here turns the single most
            // likely way this round is lost — a compile error nobody read out of a batch log — into
            // an exit code.
            if (!shader.isSupported)
                throw new Exception(
                    $"'{shader.name}' loaded but is NOT SUPPORTED on this device: it failed to "
                    + "compile. The shader errors are earlier in this log.");

            var ref165 = AssetDatabase.LoadAssetAtPath<Shader>(Ref165ShaderPath);
            if (ref165 == null || !ref165.isSupported)
                throw new Exception(
                    $"{Ref165ShaderPath} did not load or did not compile. Without it the sheet has "
                    + "no ModBuild 165 column, and a measurement with nothing to compare it "
                    + "against is not a measurement.");

            bool canTessellate = SystemInfo.graphicsShaderLevel >= 46;
            Debug.Log(
                "[GloomhavenVR][WaterPreview] device: " + SystemInfo.graphicsDeviceType
                + " '" + SystemInfo.graphicsDeviceName + "' shaderLevel "
                + SystemInfo.graphicsShaderLevel
                + (canTessellate
                    ? " — the TESSELLATED SubShader (LOD 300) is reachable, so the *_notess A/B is "
                      + "a real comparison"
                    : " — BELOW 46: THIS DEVICE CANNOT RUN THE TESSELLATED SubShader. Every frame "
                      + "in this sheet is the LOD 100 fallback and the flat-vs-tessellated A/B is "
                      + "MEANINGLESS. Do not read relief off these frames."));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject basin = BuildBasin();
            Material water = new Material(shader) { name = "WaterVRPreview" };
            Material old = new Material(ref165) { name = "WaterVRRef165Preview" };
            Texture2D bump = SynthBump();
            Mesh film = HexFilm();
            Debug.Log(
                $"[GloomhavenVR][WaterPreview] staged film mesh '{film.name}': {film.vertexCount} "
                + $"verts / {film.triangles.Length / 3} tris, local bounds size {film.bounds.size} "
                + "(the census measured 33 verts over (1.73, 0, 1.998); the padded bounds are the "
                + "driver's own Renderer.localBounds pad reproduced on the mesh, because this "
                + "harness has no driver to write it)");
            List<MeshRenderer> field = BuildWaterField(water, film);

            var camGo = new GameObject("WaterPreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 100f;

            // ARGBHalf and a manual gamma encode: an 8-bit LINEAR target quantises at 1/255 linear,
            // which after the encode is a first step of 18/255, and every soft gradient arrives as
            // contour bands that are not on the headset.
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf,
                                       RenderTextureReadWrite.Linear);
            var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);

            float[] Grab()
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                var px = tex.GetPixels();
                var lum = new float[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i].gamma;
                    c.a = 1f;
                    px[i] = c;
                    lum[i] = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                }
                tex.SetPixels(px);
                tex.Apply();
                return lum;
            }

            void Aim(Vector3 pos, Vector3 look)
            {
                cam.orthographic = false;
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            }

            void AimPlan()
            {
                cam.orthographic = true;
                cam.orthographicSize = PlanHalfHeightWU;
                cam.transform.position = new Vector3(0f, PlanHeight, 0f);
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }

            float[] Shoot(string name, Vector3 pos, Vector3 look)
            {
                Aim(pos, look);
                float[] lum = Grab();
                string png = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Debug.Log($"[GloomhavenVR][WaterPreview] wrote {Path.GetFullPath(png)}");
                return lum;
            }

            float[] ShootPlan(string name)
            {
                AimPlan();
                float[] lum = Grab();
                string png = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Debug.Log($"[GloomhavenVR][WaterPreview] wrote {Path.GetFullPath(png)}");
                return lum;
            }

            // ================================================== ONE CLOCK FOR EVERY COLUMN ==
            // The shader reads `_Time.y + _GhvrTimeOfs`, so a sheet meant to compare candidates
            // depends on _Time.y being the SAME for every frame in the batch. MEASURED, not assumed:
            // the first frame is rendered a second time as the very LAST frame of the run, under the
            // same name + "_ctrl", and the two came back byte-identical — so _Time.y does not
            // advance across Camera.Render calls in batch mode and the offset alone IS the instant.
            //
            // The control frame stays in the sheet. A future Unity that ran the clock would make
            // every comparison here meaningless, and that is a thing to find out from a diff rather
            // than from an argument.
            void SetClock(float t) => Shader.SetGlobalFloat("_GhvrTimeOfs", t);

            var report = new StringBuilder();
            report.AppendLine("WATERVR MEASUREMENTS — ModBuild 166");
            report.AppendLine();
            report.AppendLine(
                "NET TRANSLATION is the offset of the best cross-correlation match between the "
                + "frame at t=0 and a later one, from the ORTHOGRAPHIC overhead station with the "
                + "basin bed HIDDEN (a static checker under the film would pin any correlation to "
                + "zero and the measurement could not fail). "
                + PlanWUPerPixel.ToString("0.####", CultureInfo.InvariantCulture)
                + " world units per pixel; searched to +/-" + PlanSearchPx + " px.");
            report.AppendLine(
                "CHANGE RATE is the share of pixels whose gamma-encoded luminance moves by more "
                + "than " + (ChangeThreshold * 255f).ToString("0.#", CultureInfo.InvariantCulture)
                + "/255, and the mean absolute change, from the same station WITH the basin in "
                + "place — that is the image a player sees.");
            report.AppendLine();

            foreach (Candidate c in Candidates)
            {
                Material m = c.Ref165 ? old : water;
                Shader sh = c.Ref165 ? ref165 : shader;
                ApplyCandidate(m, bump, c);
                foreach (MeshRenderer r in field)
                    r.sharedMaterial = m;
                // FORCING THE SUBSHADER is how the geometry A/B is made provable: LOD 200 excludes
                // the tessellated SubShader (LOD 300) and leaves only the plain one (LOD 100).
                sh.maximumLOD = c.NoTessellation ? 200 : 600;

                foreach (float t in Clocks)
                {
                    SetClock(t);
                    string stamp = $"t{Mathf.RoundToInt(t * 100f):D3}";
                    Shoot($"watervr_synthbump_{c.Name}_graze_{stamp}",
                          Stations[1].Pos, Stations[1].Look);
                    Shoot($"watervr_synthbump_{c.Name}_brim_{stamp}",
                          Stations[0].Pos, Stations[0].Look);
                }
                // ...and the wide shot, which is the frame that answers "bei jedem tile identisch".
                SetClock(Clocks[0]);
                Shoot($"watervr_synthbump_{c.Name}_wide_t000", Stations[4].Pos, Stations[4].Look);
                Shoot($"watervr_synthbump_{c.Name}_stand_t000", Stations[2].Pos, Stations[2].Look);

                Measure(c, basin, report, SetClock, ShootPlan);

                sh.maximumLOD = 600;
            }

            // ---- THE TEMPORAL STRIP. One column, six instants over ten seconds, at the station
            //      where the verdict is formed. This is the deliverable that answers "zu hektisch"
            //      by eye, next to the numbers that answer it by measurement.
            Candidate rec = Candidates[1];
            foreach (MeshRenderer r in field)
                r.sharedMaterial = water;
            ApplyCandidate(water, bump, rec);
            shader.maximumLOD = 600;
            foreach (float t in StripClocks)
            {
                SetClock(t);
                Shoot($"watervr_strip_{rec.Name}_graze_t{Mathf.RoundToInt(t * 100f):D4}",
                      Stations[1].Pos, Stations[1].Look);
            }
            // ...and the same six on the REFERENCE, because a strip of one column shows a rate and
            // a pair of strips shows which rate was rejected.
            Candidate refc = Candidates[0];
            ApplyCandidate(old, bump, refc);
            foreach (MeshRenderer r in field)
                r.sharedMaterial = old;
            ref165.maximumLOD = 600;
            foreach (float t in StripClocks)
            {
                SetClock(t);
                Shoot($"watervr_strip_{refc.Name}_graze_t{Mathf.RoundToInt(t * 100f):D4}",
                      Stations[1].Pos, Stations[1].Look);
            }

            // ---- the recommended column from the view-independence control station.
            foreach (MeshRenderer r in field)
                r.sharedMaterial = water;
            ApplyCandidate(water, bump, rec);
            SetClock(Clocks[0]);
            Shoot($"watervr_synthbump_{rec.Name}_steep_t000", Stations[3].Pos, Stations[3].Look);

            // ---- the FALLBACK ripple path, which is what is on screen when the game material
            //      carries no readable normal map. It has to move too; a still film there would be
            //      ModBuild 162's flat sheet arrived at silently.
            water.SetFloat("_ProcNormal", 1f);
            SetClock(Clocks[0]);
            Shoot($"watervr_procfallback_{rec.Name}_graze_t000", Stations[1].Pos, Stations[1].Look);
            water.SetFloat("_ProcNormal", 0f);

            // ---- THE CLOCK CONTROL. Same candidate, same station, same instant as the very first
            //      frame of the sheet; `compare` it against that frame.
            ApplyCandidate(old, bump, Candidates[0]);
            foreach (MeshRenderer r in field)
                r.sharedMaterial = old;
            ref165.maximumLOD = 600;
            SetClock(Clocks[0]);
            Shoot($"watervr_synthbump_{Candidates[0].Name}_graze_t000_ctrl",
                  Stations[1].Pos, Stations[1].Look);

            string txt = Path.Combine(outDir, "water-measurements.txt");
            File.WriteAllText(txt, report.ToString());
            Debug.Log("[GloomhavenVR][WaterPreview] MEASUREMENTS\n" + report);
            Debug.Log($"[GloomhavenVR][WaterPreview] wrote {Path.GetFullPath(txt)}");

            Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
            shader.maximumLOD = 600;
            ref165.maximumLOD = 600;
            cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
        }

        /// <summary>
        /// THE TWO NUMBERS, for one column.
        ///
        /// <para>Both come off the orthographic overhead station, and the basin is HIDDEN for the
        /// translation and SHOWN for the change rate — see the file header for why that split is
        /// load-bearing rather than tidy.</para>
        /// </summary>
        private static void Measure(
            Candidate c, GameObject basin, StringBuilder report,
            Action<float> setClock, Func<string, float[]> shootPlan)
        {
            // --- NET TRANSLATION, water alone.
            basin.SetActive(false);
            setClock(0f);
            float[] p0 = shootPlan($"watervr_plan_{c.Name}_t000");
            setClock(4.5f);
            float[] p45 = shootPlan($"watervr_plan_{c.Name}_t450");
            setClock(10f);
            float[] p10 = shootPlan($"watervr_plan_{c.Name}_t1000");
            basin.SetActive(true);

            (int dx, int dy, float score) a = BestOffset(p0, p45);
            (int dx, int dy, float score) b = BestOffset(p0, p10);

            // --- CHANGE RATE, the image as the player sees it.
            setClock(0f);
            float[] b0 = shootPlan($"watervr_planbasin_{c.Name}_t000");
            setClock(1f);
            float[] b1 = shootPlan($"watervr_planbasin_{c.Name}_t100");
            setClock(4.5f);
            float[] b45 = shootPlan($"watervr_planbasin_{c.Name}_t450");

            (float frac, float mean) r1 = ChangeRate(b0, b1);
            (float frac, float mean) r45 = ChangeRate(b0, b45);

            float wu = PlanWUPerPixel;
            var ci = CultureInfo.InvariantCulture;
            report.AppendLine("COLUMN '" + c.Name + "'"
                              + (c.Ref165 ? "  [the ModBuild 165 REFERENCE]" : string.Empty));
            report.AppendLine(
                "  NET TRANSLATION  0 -> 4.5 s : (" + a.dx + ", " + a.dy + ") px = ("
                + (a.dx * wu * 100f).ToString("0.##", ci) + ", "
                + (a.dy * wu * 100f).ToString("0.##", ci) + ") cm   [peak |r| "
                + a.score.ToString("0.###", ci) + "]");
            report.AppendLine(
                "  NET TRANSLATION  0 -> 10  s : (" + b.dx + ", " + b.dy + ") px = ("
                + (b.dx * wu * 100f).ToString("0.##", ci) + ", "
                + (b.dy * wu * 100f).ToString("0.##", ci) + ") cm   [peak |r| "
                + b.score.ToString("0.###", ci) + "]");
            report.AppendLine(
                "  CHANGE RATE      over 1.0 s : "
                + (r1.frac * 100f).ToString("0.00", ci) + " % of pixels, mean |dL| "
                + (r1.mean * 255f).ToString("0.###", ci) + "/255");
            report.AppendLine(
                "  CHANGE RATE      over 4.5 s : "
                + (r45.frac * 100f).ToString("0.00", ci) + " % of pixels, mean |dL| "
                + (r45.mean * 255f).ToString("0.###", ci) + "/255");
            report.AppendLine();
        }

        /// <summary>
        /// The pixel offset at which <paramref name="b"/> best matches <paramref name="a"/>.
        ///
        /// <para>ZERO-MEAN NORMALISED CROSS-CORRELATION over the overlap, and the score is the
        /// MAGNITUDE of it: a surface whose shading dips below its own mean between the two instants
        /// is not a surface that has moved, and a search for the most POSITIVE correlation would
        /// walk away from the true answer to avoid the sign. Ties go to the smaller offset, because
        /// a pattern that has moved a whole wavelength is the pattern that has not moved and the
        /// honest reading of that ambiguity is the smallest displacement consistent with it.</para>
        /// </summary>
        private static (int dx, int dy, float score) BestOffset(float[] a, float[] b)
        {
            // TWO STAGES, because the honest search is too big to run flat. Eighty-one offsets
            // squared over a 1280x720 frame is ten thousand million multiplications per pair and
            // there are eighteen pairs in a sheet. So the peak is FOUND on a quarter-scale copy —
            // which cannot lie about where it is, only about where it is to the nearest four
            // pixels — and then REFINED at full resolution in a small window around it. The number
            // reported is the full-resolution one.
            const int Coarse = 4;
            int cw = W / Coarse, ch = H / Coarse;
            float[] da = Downsample(a, Coarse), db = Downsample(b, Coarse);
            var rough = Search(da, db, cw, ch, 0, 0, PlanSearchPx / Coarse);
            return Search(a, b, W, H, rough.dx * Coarse, rough.dy * Coarse, Coarse + 2);
        }

        private static float[] Downsample(float[] src, int factor)
        {
            int w = W / factor, h = H / factor;
            var dst = new float[w * h];
            float inv = 1f / (factor * factor);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int j = 0; j < factor; j++)
                        for (int i = 0; i < factor; i++)
                            sum += src[(y * factor + j) * W + (x * factor + i)];
                    dst[y * w + x] = sum * inv;
                }
            return dst;
        }

        private static (int dx, int dy, float score) Search(
            float[] a, float[] b, int w, int h, int cx, int cy, int radius)
        {
            int bestX = cx, bestY = cy, bestDist = int.MaxValue;
            float bestScore = float.NegativeInfinity;
            for (int dx = cx - radius; dx <= cx + radius; dx++)
                for (int dy = cy - radius; dy <= cy + radius; dy++)
                {
                    int x0 = Mathf.Max(0, -dx), x1 = Mathf.Min(w, w - dx);
                    int y0 = Mathf.Max(0, -dy), y1 = Mathf.Min(h, h - dy);
                    int count = (x1 - x0) * (y1 - y0);
                    if (count <= 0)
                        continue;

                    double sa = 0, sb = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            sa += a[y * w + x];
                            sb += b[(y + dy) * w + (x + dx)];
                        }
                    double ma = sa / count, mb = sb / count;

                    double num = 0, va = 0, vb = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            double u = a[y * w + x] - ma;
                            double v = b[(y + dy) * w + (x + dx)] - mb;
                            num += u * v;
                            va += u * u;
                            vb += v * v;
                        }
                    double denom = Math.Sqrt(va * vb);
                    float score = denom > 1e-12 ? (float)Math.Abs(num / denom) : 0f;
                    int dist = dx * dx + dy * dy;
                    if (score > bestScore + 1e-5f
                        || (score > bestScore - 1e-5f && dist < bestDist))
                    {
                        bestScore = score;
                        bestX = dx;
                        bestY = dy;
                        bestDist = dist;
                    }
                }
            return (bestX, bestY, bestScore);
        }

        /// <summary>The share of pixels that changed by more than <see cref="ChangeThreshold"/>,
        /// and the mean absolute change over every pixel.</summary>
        private static (float frac, float mean) ChangeRate(float[] a, float[] b)
        {
            int changed = 0;
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float d = Mathf.Abs(a[i] - b[i]);
                sum += d;
                if (d > ChangeThreshold)
                    changed++;
            }
            return (changed / (float)a.Length, (float)(sum / a.Length));
        }

        /// <summary>
        /// Write ONE candidate onto the film material, doing exactly the arithmetic
        /// <c>WaterOwnSurface</c> and the driver do at runtime — the tame, the weights, the swell
        /// and the period resolution — so a frame here is a frame of the shipped resolution and not
        /// of a set of numbers chosen for a picture.
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
            m.SetFloat("_TessFactor", TessellationFactor);

            m.SetFloat("_Shimmer", c.Shimmer);
            m.SetFloat("_WaveShade", c.WaveShade);
            m.SetFloat("_NormalStrength", c.RippleStrength);

            Vector4 tiling = Tame(Tilings, c.WaveScale);
            Vector4 weights = Weights(tiling);
            m.SetVector("_NormalTilings", tiling);
            m.SetVector("_LayerWeights", weights);

            float speedDial = Mathf.Max(c.RippleSpeed, 0f);
            float width = Mathf.Max(HexFlats, HexPoints);
            float amp = Mathf.Clamp(width * c.SwellHeight, 0f, MaxSwellAmplitude);
            float swellWave = SwellWavelength * c.WaveScale;
            float swellPeriod = SwellPeriod * Mathf.Sqrt(Mathf.Max(c.WaveScale, 0.01f))
                                / Mathf.Max(speedDial, 0.01f);

            m.SetFloat("_SwellAmp", amp);
            m.SetFloat("_SwellWave", swellWave);
            m.SetFloat("_SwellPeriod", swellPeriod);
            m.SetFloat("_SwellCalm", SwellCalmDepth);

            string motion;
            if (c.Ref165)
            {
                // THE FROZEN COLUMN. ModBuild 165's own scroll rates, its trace drift and its
                // drift-plus-sway ripple, written onto the frozen shader — so the reference is that
                // build's SURFACE and not this build's constants fed different numbers.
                Vector4 rateA = Ref165Resolve(Ref165SpeedA, speedDial);
                Vector4 rateB = Ref165Resolve(Ref165SpeedB, speedDial);
                m.SetVector("_WaterUVAnimSpeedA", rateA);
                m.SetVector("_WaterUVAnimSpeedB", rateB);
                m.SetVector("_RippleSway",
                            new Vector4(Ref165SwayPeriodA, Ref165SwayPeriodB, Ref165DriftShare, 0f));
                m.SetFloat("_SwellSpeed", Ref165SwellDrift * c.WaveScale * speedDial);
                motion =
                    $"ripple rate A {rateA.x:0.####},{rateA.y:0.####} world units/s peak of which "
                    + $"{Ref165DriftShare * 100f:0}% is a one-way DRIFT and the rest a SWAY over "
                    + $"{Ref165SwayPeriodA:0.#}/{Ref165SwayPeriodB:0.#} s, swell trace drift "
                    + $"{Ref165SwellDrift * c.WaveScale * speedDial:0.####} m/s";
            }
            else
            {
                // THE SHIPPED COLUMNS. Nothing to write but PERIODS: there is no rate property on
                // this shader and no coordinate for one to move.
                var fade = new Vector4(swellPeriod * RippleFadeRatios[0],
                                       swellPeriod * RippleFadeRatios[1],
                                       swellPeriod * RippleFadeRatios[2], 0f);
                m.SetVector("_RippleFade", fade);
                motion = $"NO DRIFT AND NO SCROLL — ripple crossfades on {fade.x:0.#}/{fade.y:0.#}/"
                         + $"{fade.z:0.#} s, bloom on {swellPeriod * 3.1f:0}/"
                         + $"{swellPeriod * 4.7f:0}/{swellPeriod * 7.3f:0} s";
            }

            Debug.Log(
                $"[GloomhavenVR][WaterPreview] candidate '{c.Name}'"
                + (c.Ref165 ? " [FROZEN ModBuild 165 shader]" : " [shipped shader]")
                + (c.NoTessellation ? " [LOD 100, NO TESSELLATION]" : " [LOD 300, tessellated x"
                                                                      + TessellationFactor + "]")
                + $": tilings {tiling} (= one repeat every {Wave(tiling.x)} x {Wave(tiling.y)} m "
                + $"and {Wave(tiling.z)} x {Wave(tiling.w)} m), weights {weights.x:0.###}/"
                + $"{weights.y:0.###}, {motion}, swell {amp:0.####} m peak, longest component "
                + $"{swellWave:0.##} m bobbing once every {swellPeriod:0.#} s, calm "
                + $"{SwellCalmDepth:0.##}, lattice mismatch {LatticeMismatch(swellWave):0.###} "
                + $"cycles, shimmer {c.Shimmer:0.##}, exponent {Mathf.Lerp(1f, 8f, Smoothness):0.##}");
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

        /// <summary>ModBuild 165's deleted WaterOwnSurface.ScrollRate, kept here for the reference
        /// column alone. Nothing in the shipped path resolves a rate any more.</summary>
        private static Vector4 Ref165Resolve(Vector4 speed, float dial)
        {
            float layer = speed.z > 0f ? speed.z : 1f;
            float clock = Ref165NoiseSpeed.x > 0f ? Ref165NoiseSpeed.x : 1f;
            float k = layer * clock * Mathf.Max(dial, 0f);
            return new Vector4(speed.x * k, speed.y * k, 0f, 0f);
        }

        /// <summary>WaterOwnSurface.LatticeMismatch, mirrored — printed per candidate so the log
        /// says how far each setting is from drawing the same figure on every tile.</summary>
        private static float LatticeMismatch(float wavelength)
        {
            Vector2[] lattice = { new Vector2(HexFlats, 0f), new Vector2(0f, HexPoints) };
            float worst = 1f;
            foreach (Vector2 v in lattice)
                for (int i = 0; i < SwellRatios.Length; i++)
                {
                    float rad = SwellDirections[i] * Mathf.Deg2Rad;
                    float cycles = Vector2.Dot(new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)), v)
                                   / (wavelength * SwellRatios[i]);
                    worst = Mathf.Min(worst, Mathf.Abs(cycles - Mathf.Round(cycles)));
                }
            return worst;
        }

        /// <summary>
        /// The basin: a flat unlit checker bed under the film, and a low rim of the same stone
        /// BEYOND the pool so the water has something to be a silhouette against.
        ///
        /// <para>IT IS A CHECKER ON PURPOSE. The film is transparent, and two of the failures worth
        /// catching are about what is UNDER it: an additive blend would make the water LIGHTER than
        /// the stone (the photographed defect), and a film that hid the floor entirely would mean
        /// the alpha cap never landed. A flat colour would hide both.</para>
        ///
        /// <para>THE BED IS NINE CENTIMETRES DOWN, because that is what the hardware FLOOR CENSUS
        /// measured: the film at y[0.0..0.0] with TERRAIN_Crypt_Water_02_Base at y[-0.34..-0.09]
        /// directly beneath it. THIS DEPTH IS PART OF THE TEST. An earlier draft left the bed 3 cm
        /// down and the first swell render came back with a scatter of small holes in the pool — the
        /// troughs reaching BELOW the bed, where ZTest LEqual rejects them. That is exactly the
        /// collision WaterOwnSurface.MaxSwellAmplitude is set against.</para>
        ///
        /// <para>IT IS RETURNED AS ONE PARENT so the translation measurement can switch it off:
        /// a static checker under a 45 %-opaque film dominates any cross-correlation and would pin
        /// the answer at (0, 0) whatever the water did.</para>
        /// </summary>
        private static GameObject BuildBasin()
        {
            var root = new GameObject("Basin");

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    // A GENTLER CHECKER THAN THE FIRST DRAFT'S 0.34/0.22. The film is 45% opaque,
                    // so a high-contrast bed reads THROUGH it and swamps the water's own shading,
                    // which varies by a few percent — the first render of this sheet was a
                    // photograph of a chessboard with a green filter on it. The pattern still has
                    // to be legible, because seeing the floor through the water is what proves the
                    // blend is not additive.
                    bool a = ((x / 8) + (y / 8)) % 2 == 0;
                    float v = a ? 0.30f : 0.245f;
                    tex.SetPixel(x, y, new Color(v, v * 0.96f, v * 0.90f, 1f));
                }
            tex.Apply();

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = tex;
            mat.mainTextureScale = new Vector2(16f, 16f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "BasinBed";
            floor.transform.SetParent(root.transform, false);
            floor.transform.position = new Vector3(0f, -0.09f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(16f, 16f, 1f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // THE FAR WALL IS THE HORIZON, and it is built on the SAME Unlit/Texture shader as the
            // bed rather than on Unlit/Color. Unlit/Color did not resolve in batch mode — the first
            // two renders of this sheet had no wall at all, so the brim station was a silhouette
            // shot against an empty black background and could not have shown a crest breaking a
            // skyline however long anyone looked at it. Shader.Find returning null gives a material
            // that renders NOTHING and says nothing about it, which is the same class of silent
            // failure this whole round is about.
            var wallTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            wallTex.SetPixel(0, 0, new Color(0.26f, 0.26f, 0.29f, 1f));
            wallTex.Apply();
            var wallMat = new Material(Shader.Find("Unlit/Texture"));
            if (wallMat.shader == null)
                throw new Exception("Unlit/Texture did not resolve — the sheet has no horizon.");
            wallMat.mainTexture = wallTex;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wall.name = "FarWall";
            wall.transform.SetParent(root.transform, false);
            wall.transform.position = new Vector3(0f, 1.2f, 6.0f);
            // NO ROTATION. Unity's Quad primitive has its normal along -Z, so it already faces the
            // cameras, which all stand at negative z. The Euler(0,180,0) that used to be here turned
            // it AWAY from them and it was backface-culled in every frame of every sheet this file
            // has ever produced — which is the second silent-nothing of this round, in the very
            // instrument built to catch the first.
            wall.transform.localScale = new Vector3(22f, 2.4f, 1f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            return root;
        }

        /// <summary>
        /// NINETEEN separate hexes on the lattice the game lays them out on. The report's room holds
        /// 17 TERRAIN_Water_Plane instances; this stages a closed 3-4-5-4-3 patch instead, because
        /// the ROW PARITY is what makes hexes meet edge to edge and a 2-4-5-4-2 patch has two
        /// half-offset rows in a row. The first render of this sheet showed exactly that as ragged
        /// gaps between the top rows, which would have been read as something wrong with the film.
        ///
        /// <para>THE LATTICE IS THE MEASUREMENT, NOT A CONVENIENCE. A pointy-top hexagon 1.73 m
        /// across the flats and 1.998 m point to point tiles at 1.73 in x and 0.75 x 1.998 = 1.4985
        /// in z, with alternate rows offset half a width. Those two numbers are exactly what
        /// WaterOwnSurface.LatticeMismatch is computed against, so the WIDE frame of this sheet is a
        /// direct test of the claim that the field cannot repeat tile for tile — with the hexes
        /// meeting edge to edge, a per-tile figure has nowhere to hide.</para>
        /// </summary>
        private static List<MeshRenderer> BuildWaterField(Material water, Mesh film)
        {
            var root = new GameObject("WaterFilm");
            var renderers = new List<MeshRenderer>();
            int[] counts = { 3, 4, 5, 4, 3 };   // 19, and every row's parity alternates
            const float stepX = HexFlats;
            const float stepZ = HexPoints * 0.75f;
            for (int row = 0; row < counts.Length; row++)
            {
                int n = counts[row];
                float z = (row - 2) * stepZ;
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) * 0.5f) * stepX;
                    var q = new GameObject($"TERRAIN_Water_Plane_{row}_{i}");
                    q.transform.SetParent(root.transform, false);
                    q.transform.position = new Vector3(x, 0f, z);
                    q.AddComponent<MeshFilter>().sharedMesh = film;
                    var mr = q.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = water;
                    renderers.Add(mr);
                }
            }
            return renderers;
        }

        /// <summary>
        /// The film mesh the census measured: a pointy-top hexagon 1.73 m across the flats and
        /// 1.998 m point to point, with EXACTLY 33 vertices — one centre and 32 around the rim,
        /// fanned into 32 triangles.
        ///
        /// <para>THE TOPOLOGY IS A CHOICE AND IT IS THE PESSIMISTIC ONE. The hardware line reads
        /// "33 verts / 0 tris", and the 0 is <c>Mesh.triangles</c> refusing on a non-readable mesh
        /// rather than a count — so how those 33 vertices are connected cannot be known from here. A
        /// fan is the COARSEST arrangement of 33 vertices over this outline (one triangle from the
        /// centre to each rim segment, i.e. spokes about a metre long), so a relief that reads in
        /// these frames reads at least as well on a mesh that turns out to be a grid. The mistake
        /// this replaces went the other way: ModBuild 164's harness staged an 8x8 grid because that
        /// is what the driver was supposed to produce, and it was never produced.</para>
        ///
        /// <para>THE BOUNDS ARE PADDED by the swell's CEILING, exactly as the driver pads
        /// Renderer.localBounds at runtime. Unity culls against those bounds and cannot see a domain
        /// program, so an unpadded film loses its crests as the camera closes — one eye first, under
        /// MultiPass. There is no driver in this harness to write the pad, so the mesh carries
        /// it.</para>
        /// </summary>
        private static Mesh HexFilm()
        {
            float rx = HexFlats * 0.5f;      // across the flats  -> x
            float rz = HexPoints * 0.5f;     // point to point    -> z

            // The six corners of a pointy-top hexagon, in order.
            var corners = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (90f + 60f * i);
                corners[i] = new Vector2(Mathf.Cos(a) * rx / Mathf.Cos(Mathf.Deg2Rad * 30f),
                                         Mathf.Sin(a) * rz);
            }

            // 32 rim vertices. EVERY CORNER IS ONE OF THEM, and the remaining 26 are shared out
            // over the six edges (5,4,4,5,4,4). Spreading 32 points evenly by arc length instead
            // MISSES the corners — the first render of this sheet came back with hexes whose
            // outline was visibly scalloped, which is a staging artefact that would have been read
            // as something the water was doing.
            var perEdge = new[] { 5, 4, 4, 5, 4, 4 };   // + 6 corners = 32
            var rim = new List<Vector2>(HexRimVerts);
            for (int e = 0; e < 6; e++)
            {
                rim.Add(corners[e]);
                for (int k = 1; k <= perEdge[e]; k++)
                {
                    rim.Add(Vector2.Lerp(corners[e], corners[(e + 1) % 6],
                                         k / (float)(perEdge[e] + 1)));
                }
            }

            var verts = new List<Vector3>(HexRimVerts + 1) { Vector3.zero };
            var uvs = new List<Vector2>(HexRimVerts + 1) { new Vector2(0.5f, 0.5f) };
            foreach (Vector2 p in rim)
            {
                verts.Add(new Vector3(p.x, 0f, p.y));
                uvs.Add(new Vector2(p.x / (2f * rx) + 0.5f, p.y / (2f * rz) + 0.5f));
            }

            var tris = new List<int>(HexRimVerts * 3);
            for (int i = 0; i < HexRimVerts; i++)
            {
                tris.Add(0);
                tris.Add(1 + i);
                tris.Add(1 + (i + 1) % HexRimVerts);
            }

            var m = new Mesh { name = "TERRAIN_Water_Plane_preview" };
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
        /// <para>Encoded the way a plain RGB normal map is — x in R, y in G, z in B, ALPHA 1 — so
        /// the shader's RG-or-AG unpack (<c>r * a</c>) recovers x under the same expression it uses
        /// for a DXT5nm import. Getting that wrong here would make the preview disagree with the
        /// headset for a reason that has nothing to do with the water.</para>
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
            // A tileable height field: three crossing sine trains at integer periods (so the texture
            // wraps without a seam) at incommensurate directions, differentiated analytically.
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
