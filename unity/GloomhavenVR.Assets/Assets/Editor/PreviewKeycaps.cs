// GloomhavenVR companion project — control-board KEYCAP renderer.
//
// Batch:
//   KEYCAP_PREVIEW_OUT=/somewhere xvfb-run -a \
//     Unity -batchmode -projectPath <this> -buildTarget Win64 \
//           -executeMethod GloomhavenVR.KeycapPreview.RenderAll -logFile keycap-preview.log
//   (needs a graphics device — do NOT pass -nographics. The method exits by itself; no -quit.)
//
// WHY THIS EXISTS. The keycaps are built PROCEDURALLY at runtime, in C#, inside the plugin
// (Cards/CardMesh.BuildBeveledKeycap + Cards/PlayTray.NewKeycapMaterial). There is no prefab, no
// mesh asset and no material asset for any preview station to open, so the ONLY way to see a
// keycap before it is on the rig is to rebuild it here from the same numbers.
//
// THAT MAKES THIS FILE A COPY, AND A COPY IS A HAZARD. The editor project cannot reference the
// plugin assembly, so BuildBeveledKeycap, BuildRoundCap, CapCellMath.Cell, the state palette, the
// bevel/wall lerps, the seat floor and PressDepth01 are all PORTED here by hand. Every one of them
// is a place where the picture can be right about something the game does not do. Two defences:
//
//   1. THE MESH IS CHECKED AGAINST THE SHIPPED RUNTIME DIAGNOSTIC'S OWN INVARIANT, not against
//      this file's expectations. Cards/PlayTray.7.Nested.LogCapDiagnostics asserts that a closed
//      beveled keycap is 40 verts / 20 tris / submesh index counts [6, 24, 30]. Those five numbers
//      are hard-coded below and printed with the ACTUAL counts. A self-test that builds its own
//      input proves nothing; this one is answerable by a hardware log line that already exists.
//   2. Every ported constant carries the file and symbol it came from, so a drift is a grep away
//      rather than an archaeology exercise.
//
// WHAT THIS RENDERER CANNOT SEE — stated so nobody trusts it further than it goes.
//   • THE SHADER IS THE PROJECT'S, NOT THE BUNDLE'S, and that is deliberate. A Win64 asset bundle
//     opened in a Linux editor has no compatible shader variant, HasProperty is false for
//     everything and the object draws MAGENTA (PreviewBoard.cs documents the trap at length). So
//     Assets/Bundle/Table/BoardLit.shader is loaded through AssetDatabase and compiles for this
//     editor's own API. Only the rig can prove the D3D11 variants are good.
//   • THE COLOUR SPACE IS EMULATED, AND IF IT WERE NOT, EVERY BRIGHTNESS HERE WOULD BE A LIE.
//     This Unity project renders LINEAR (ProjectSettings m_ActiveColorSpace: 1). THE RIG DOES
//     NOT: the mod's own source states it in three independent places, each written off a rig
//     observation — WorldUI/FlatScreenStereo.2.Compositor.cs ("the rig renders in Gamma
//     colorspace, so sRGB read/write yields sRGB=False regardless"),
//     WorldUI/FlatScreen.4.Lifecycle.cs and WorldUI/PanelSupersample.2.Capture.cs.
//     That is not a subtlety. BoardLit's fragment is `tex2D(_MainTex, uv) * _Color * shade`, a
//     PRODUCT OF TWO COLOURS, and a product is exactly where the two pipelines diverge: in gamma
//     the shader multiplies the sRGB-encoded values, in linear it multiplies their decoded
//     versions and re-encodes. Measured on the shipped numbers, the naive linear render made the
//     steel cap read 11x darker than the reference cap where the rig gives 3x. A station that
//     had shipped that would have reported a defect three times the size of the real one.
//     SO THE GAMMA PIPELINE IS REPRODUCED, in the only three places it differs:
//       1. every texture is loaded from its PNG BYTES into a Texture2D created with
//          `linear: true` (see LoadRaw) — so the sampler returns the RAW sRGB values a gamma
//          project's sampler returns, and the mips are built in the same raw space. This also
//          side-steps `isReadable: 0` and touches no importer setting and no .meta file;
//       2. every Color written to a material is pre-multiplied through `.gamma`, because a
//          linear project converts Color-typed shader properties on upload — `c.gamma.linear`
//          is `c`, so the shader receives the raw authored number;
//       3. every RenderTexture is RenderTextureReadWrite.Linear, i.e. sRGB=FALSE, so the shader's
//          output is stored raw instead of being encoded on write.
//     None of that is trusted: Calibrate() renders a flat patch whose expected framebuffer value
//     is arithmetic (a known texel times a known colour times a known shade of exactly 1.0) and
//     prints measured against expected. If that line does not match, the colour in every other
//     picture is void and the log says so.
//   • NO SCENE LIGHT IS CREATED, on purpose. BoardLit bakes two studio directions
//     (key normalize(0.35, 0.85, -0.45), fill normalize(-0.55, 0.35, 0.30)) plus an ambient floor
//     and never reads a scene light. Adding one would light nothing and prove nothing — but it
//     would make the picture a lie about a surface the player sees in an unlit diorama.
//   • THE ENGRAVING IS A STAND-IN. TextMeshPro is not in this project's Packages/manifest.json, so
//     the shipped SDF material cannot be instantiated here. See the Engraving section.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class KeycapPreview
    {
        // ---- ASSET PATHS (the mod's own, verbatim) --------------------------------------------
        private const string ShaderPath = "Assets/Bundle/Table/BoardLit.shader";
        private static string AtlasAlbedo(string style) => $"Assets/Bundle/Table/Keycap{style}_albedo.png";
        private static string AtlasNormal(string style) => $"Assets/Bundle/Table/Keycap{style}_normal.png";

        // THE "BEFORE" TEXTURES. KeycapGrain_albedo/normal are what EVERY cap on EVERY board wore
        // before the per-style atlases existed (Cards/PlayTray.6.Build.EnsureGrainLoaded +
        // NewKeycapMaterial's style==null path). They are NOT touched by this round — verified
        // read-only with
        //   git status --porcelain unity/GloomhavenVR.Assets/Assets/Bundle/Table/KeycapGrain_albedo.png
        // which came back EMPTY (clean, tracked, unmodified), so the working-tree copy IS the
        // shipped one and the reference column is honest. The three new atlases are untracked (??)
        // in the same listing, which is the expected shape of this round.
        private const string GrainAlbedo = "Assets/Bundle/Table/KeycapGrain_albedo.png";
        private const string GrainNormal = "Assets/Bundle/Table/KeycapGrain_normal.png";

        // ---- CAP GEOMETRY --------------------------------------------------------------------
        // Cards/PlayTray.6.Build.cs: SquareCapThickness / SquareCapBevel.
        private const float SquareCapThickness = 0.036f;
        private const float SquareCapBevel = 0.007f;
        private const float DiscThickness = 0.012f;
        // Cards/PlayTray.7.Nested.cs: CapRestZ / CapTravel.
        private const float CapRestZ = -0.004f;
        private const float CapTravel = 0.004f;
        // Cards/CardMesh.cs: RoundCapSegments.
        private const int RoundCapSegments = 64;

        // Fitted cap sizes: Cards/BoardAnchors.FitCapSize applied to the tuned 0.063 x 0.065 m
        // against each board's MEASURED seat recess, i.e. .planning/BOARD-REBUILD-HANDOVER.md's
        // "seat floor" / "rest pad" table minus 2 x 4 mm of margin. Re-derived here so the
        // arithmetic is visible rather than trusted:
        //   Oak    74.6 x 64.3 -> 66.6 x 56.3, width capped by the tuned 63.0 -> 63.0 x 56.3
        //   Steel  81.0 x 70.1 -> 73.0 x 62.1, width capped by the tuned 63.0 -> 63.0 x 62.1
        //   Bronze 61.2 x 51.9 -> 53.2 x 43.9 (both under the tuned size, so the recess wins)
        //   rest pads 81.6 / 81.7 / 68.1 -> 73.6 / 73.7 / 60.1 mm diameter
        private static readonly (string Style, float CapW, float CapH, float DiscD)[] Styles =
        {
            ("Oak",    0.0630f, 0.0563f, 0.0736f),
            ("Steel",  0.0630f, 0.0621f, 0.0737f),
            ("Bronze", 0.0532f, 0.0439f, 0.0601f),
        };

        // ---- ROLE -> ATLAS CELL (Cards/CapSymbols.CapRole; mirrored from cap_atlas.py CELLS) ---
        private const int CellPlain = 0, CellConfirm = 1, CellUndo = 2, CellSkip = 3,
                          CellItemUse = 4, CellShortRest = 5, CellLongRest = 6,
                          CellFixedPinned = 7, CellFixedFollow = 8;

        // The control set, in the order the contact sheet wants its rows. Round = the rest discs
        // only: Defaults.Cards RestButtonShape_{Oak,Steel,Bronze} are all Round and
        // GenericButtonShape_{...} are all Square, so this is the shipped default shape per board,
        // not a choice made here.
        private static readonly (string Name, int Cell, bool Round)[] Controls =
        {
            ("Confirm",     CellConfirm,     false),
            ("Undo",        CellUndo,        false),
            ("Skip",        CellSkip,        false),
            ("ItemUse",     CellItemUse,     false),
            ("ShortRest",   CellShortRest,   true),
            ("LongRest",    CellLongRest,    true),
            ("Fixed",       CellFixedPinned, false),
            ("FixedFollow", CellFixedFollow, false),
        };

        // ---- THE STATE PALETTE (Cards/PlayTray.7.Nested.cs, verbatim) --------------------------
        private static readonly Color IdleColor = new(0.60f, 0.51f, 0.35f);
        private const float WallTintFactor = 0.50f;
        private static readonly Color WallWarm = new(0.17f, 0.11f, 0.06f);
        private const float WallWarmLerp = 0.42f;
        private static readonly Color BevelHighlight = new(0.66f, 0.53f, 0.32f);
        private const float BevelLerp = 0.48f;

        // WorldUI/ButtonTuning.cs: CapWellColor / CapSeatContrast / SeatedCapColor. The face colour
        // the game actually writes is SeatedCapColor(state * tint), so this station must apply it
        // too or it renders a colour the game never produces.
        private static readonly Color CapWellColor = new(0.15f, 0.12f, 0.08f);
        private const float CapSeatContrast = 1.35f;

        // THE CAP-FACE TINT THE USER'S CONFIG SHIPS AT. Defaults.WorldUI.cs
        // BoardCapTintR/G/B = DashCapTintR/G/B = RestCapTintR/G/B = 0.5, i.e. every board keycap's
        // FACE colour is multiplied by a 0.5 grey before the bevel and wall are derived from it
        // (Cards/PlayTray.7.Nested.StateColor -> BevelTint/WallTint; the same three lines in
        // Net/RemoteBoardFurniture.cs around _capTint for a peer's mirror). Both variants are
        // rendered: the plain name is the shipped look, "_untinted" is the authored palette at 1.0.
        private static readonly Color ShippedCapTint = new(0.5f, 0.5f, 0.5f, 1f);

        private static Color WallTint(Color top)
        {
            var dark = new Color(top.r * WallTintFactor, top.g * WallTintFactor, top.b * WallTintFactor, top.a);
            Color w = Color.Lerp(dark, WallWarm, WallWarmLerp);
            w.a = top.a;
            return w;
        }

        private static Color BevelTint(Color top)
        {
            Color b = Color.Lerp(top, BevelHighlight, BevelLerp);
            b.a = top.a;
            return b;
        }

        private static Color SeatedCapColor(Color face) => new(
            Mathf.Max(face.r, CapWellColor.r * CapSeatContrast),
            Mathf.Max(face.g, CapWellColor.g * CapSeatContrast),
            Mathf.Max(face.b, CapWellColor.b * CapSeatContrast),
            face.a);

        private static bool SeatFloorEngages(Color face) =>
            face.r < CapWellColor.r * CapSeatContrast
            || face.g < CapWellColor.g * CapSeatContrast
            || face.b < CapWellColor.b * CapSeatContrast;

        // ---- THE PRESS STROKE (WorldUI/ButtonTuning.cs, verbatim) ------------------------------
        private const float PressAttackSeconds = 0.035f;
        private const float PressHoldSeconds = 0.030f;
        private const float PressReleaseSeconds = 0.120f;
        private const float PressReboundFraction = 0.10f;
        private const float PressReboundSplit = 0.62f;
        private const float PressStrokeSeconds = PressAttackSeconds + PressHoldSeconds + PressReleaseSeconds;

        private static float PressDepth01(float seconds)
        {
            if (seconds <= 0f)
                return 0f;
            if (seconds < PressAttackSeconds)
            {
                float t = seconds / PressAttackSeconds;
                return 1f - (1f - t) * (1f - t);
            }
            float held = seconds - PressAttackSeconds;
            if (held < PressHoldSeconds)
                return 1f;
            float rel = (held - PressHoldSeconds) / PressReleaseSeconds;
            if (rel >= 1f)
                return 0f;
            if (rel < PressReboundSplit)
                return 1f - (1f + PressReboundFraction)
                            * Mathf.SmoothStep(0f, 1f, rel / PressReboundSplit);
            return -PressReboundFraction
                   * (1f - Mathf.SmoothStep(0f, 1f, (rel - PressReboundSplit) / (1f - PressReboundSplit)));
        }

        // ---- THE CELL ARITHMETIC (Cards/CapCellMath.Cell, verbatim) ----------------------------
        private const int GridCols = 4;
        private const int GridRows = 4;
        private const int CellCount = GridCols * GridRows;
        private const int InsetTexels = 2;

        private static void Cell(int atlasWidth, int atlasHeight, int cellIndex,
                                 out Vector2 scale, out Vector2 offset)
        {
            if (cellIndex < 0 || cellIndex >= CellCount)
                cellIndex = 0;
            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                scale = Vector2.one;
                offset = Vector2.zero;
                return;
            }
            int col = cellIndex % GridCols;
            int row = cellIndex / GridCols;
            int rowFromBottom = GridRows - 1 - row;
            float cw = atlasWidth / (float)GridCols;
            float ch = atlasHeight / (float)GridRows;
            float insetU = InsetTexels / (float)atlasWidth;
            float insetV = InsetTexels / (float)atlasHeight;
            scale = new Vector2(cw / atlasWidth - 2f * insetU, ch / atlasHeight - 2f * insetV);
            offset = new Vector2(col * cw / atlasWidth + insetU,
                                 rowFromBottom * ch / atlasHeight + insetV);
        }

        // ---- CAMERA / FRAMING ------------------------------------------------------------------
        private static readonly Color ClearGrey = new(0.18f, 0.18f, 0.18f, 1f);
        private const float BigFov = 24f;          // vertical fov of the judged shots
        private const float BigFill = 0.72f;       // fraction of the half-frame the cap half-width fills
        private const int BigPx = 420;
        private const float RakeDegrees = 35f;     // "35 degrees above the face normal"

        // THE TWO ZOOM EXTREMES, argued in unity/board-prep/buttons/cap_check.py's header: a Quest 3
        // over Virtual Desktop renders ~20 px/degree, and a 40 mm cap subtends 4.6 deg (~96 px) at
        // 0.5 m and 1.5 deg (~32 px) at 1.5 m. This station holds the 20 px/deg and the target
        // PIXEL width fixed and solves for the distance, because the cap here is at its BOARD-LOCAL
        // size while the shipped board carries a world scale of 0.42..0.88 — so the honest statement
        // is the equivalence, and it is logged per shot.
        private const float PxPerDegree = 20f;
        private const int NearPx = 96;
        private const int FarPx = 32;

        private static int _exit;
        private static readonly List<string> Written = new();

        // ---- THE GAMMA-PIPELINE EMULATION (see the header) --------------------------------------
        private static readonly Dictionary<string, Texture2D> RawCache = new();

        /// <summary>
        /// A texture loaded from its PNG BYTES into a LINEAR-flagged Texture2D, so the sampler hands
        /// the shader the raw sRGB numbers a gamma-space project's sampler hands it — which is what
        /// the rig runs. It also bypasses the importer entirely: the atlases import with
        /// <c>isReadable: 0</c>, and this copy is readable, which is what lets Calibrate() know the
        /// exact texel value it is checking the render against. Nothing on disk is touched.
        /// </summary>
        private static Texture2D LoadRaw(string assetPath)
        {
            if (RawCache.TryGetValue(assetPath, out Texture2D cached)) return cached;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string full = Path.Combine(projectRoot, assetPath);
            if (!File.Exists(full))
            {
                RawCache[assetPath] = null;
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: true)
            { name = Path.GetFileNameWithoutExtension(assetPath) };
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(full), markNonReadable: false))
            {
                Err($"could not decode {assetPath}");
                _exit = 1;
                Object.DestroyImmediate(tex);
                RawCache[assetPath] = null;
                return null;
            }
            // CLAMP, as Cards/CapSymbols sets on the bundle-loaded atlas. The cell rect never leaves
            // 0..1 so it cannot matter through the intended path; it matters if anything ever drives
            // a cap's UV out of range, where a wrap would draw a DIFFERENT ROLE's symbol.
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            RawCache[assetPath] = tex;
            return tex;
        }

        /// <summary>A colour as the RIG's shader would receive it. A linear project converts
        /// Color-typed shader properties on upload, so pre-applying <c>.gamma</c> makes
        /// <c>c.gamma.linear == c</c> arrive at the shader — the raw authored number.</summary>
        private static Color Rig(Color c)
        {
            Color g = c.gamma;
            g.a = c.a;
            return g;
        }

        public static void RenderAll()
        {
            try
            {
                string outDir = System.Environment.GetEnvironmentVariable("KEYCAP_PREVIEW_OUT");
                if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("keycap-preview");
                Directory.CreateDirectory(outDir);
                Log($"out dir: {outDir}");

                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new System.Exception("No graphics device — run under xvfb-run WITHOUT -nographics.");
                Log($"graphics device: {SystemInfo.graphicsDeviceType}, colour space {QualitySettings.activeColorSpace}");

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                if (shader == null)
                    throw new FileNotFoundException($"No shader at {ShaderPath}");
                Log($"shader '{shader.name}' isSupported={shader.isSupported}, "
                    + $"{ShaderUtil.GetPropertyCount(shader)} properties (0 would mean no variant for this API).");
                if (!shader.isSupported)
                {
                    Err("BoardLit does not compile for this editor's graphics API — every picture below is magenta.");
                    _exit = 1;
                }

                MeshInvariant();
                Calibrate(shader);

                var grainA = LoadRaw(GrainAlbedo);
                var grainN = LoadRaw(GrainNormal);
                if (grainA == null) { Err($"BEFORE texture missing: {GrainAlbedo}"); _exit = 1; }
                else Log($"BEFORE textures: {grainA.name} {grainA.width}x{grainA.height}"
                         + $" + {(grainN ? $"{grainN.name} {grainN.width}x{grainN.height}" : "NO normal")}");

                var camGo = new GameObject("KeycapPreviewCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Rig(ClearGrey);   // same convention as every material colour
                cam.nearClipPlane = 0.005f;
                cam.farClipPlane = 20f;
                // No Light object anywhere in this scene, and none is wanted: BoardLit ignores them.

                foreach (var (style, capW, capH, discD) in Styles)
                {
                    var atlasA = LoadRaw(AtlasAlbedo(style));
                    var atlasN = LoadRaw(AtlasNormal(style));
                    if (atlasA == null)
                    {
                        Err($"{style}: atlas {AtlasAlbedo(style)} NOT FOUND — the mod would fall back to "
                            + "KeycapGrain and a centred label. Skipping this style's AFTER column.");
                        _exit = 1;
                        continue;
                    }
                    Log($"{style} atlas: {atlasA.width}x{atlasA.height} albedo (cell {atlasA.width / GridCols}"
                        + $"x{atlasA.height / GridRows} texels), normal "
                        + (atlasN ? $"{atlasN.width}x{atlasN.height} (cell {atlasN.width / GridCols}"
                                    + $"x{atlasN.height / GridRows}) — the ST comes from the ALBEDO's size for BOTH "
                                    + "maps, exactly as PlayTray.NewKeycapMaterial writes it"
                                  : "MISSING"));

                    foreach (var (name, cell, round) in Controls)
                    {
                        float w = round ? discD : capW;
                        float h = round ? discD : capH;
                        float thick = round ? DiscThickness : SquareCapThickness;

                        // AFTER, at the shipped 0.5 face tint, and the untinted control beside it.
                        ShootCap(cam, outDir, $"{style}_{name}_after", shader, atlasA, atlasN, cell,
                                 IdleColor * ShippedCapTint, w, h, thick, round, true);
                        ShootCap(cam, outDir, $"{style}_{name}_after_untinted", shader, atlasA, atlasN, cell,
                                 IdleColor, w, h, thick, round, false);
                        // BEFORE: the same mesh, the same state colours, the shared grain pair and
                        // NO texture transform — one surface for all three boards and no symbol.
                        ShootCap(cam, outDir, $"{style}_{name}_before", shader, grainA, grainN, -1,
                                 IdleColor * ShippedCapTint, w, h, thick, round, false);

                        ShootZoom(cam, outDir, $"{style}_{name}", shader, atlasA, atlasN, cell,
                                  IdleColor * ShippedCapTint, w, h, thick, round);
                    }
                }

                NormalMapCheck(cam, shader);
                PressFilmstrip(cam, outDir, shader);
                Engraving(cam, outDir, shader);

                Object.DestroyImmediate(camGo);
                Log($"wrote {Written.Count} PNG(s). exit {_exit}");
            }
            catch (System.Exception e)
            {
                Err($"FAILED: {e}");
                _exit = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(_exit);
        }

        // =========================================================================================
        // THE MESH — ported from Cards/CardMesh.cs, then checked against the SHIPPED diagnostic
        // =========================================================================================

        /// <summary>
        /// The five numbers Cards/PlayTray.7.Nested.LogCapDiagnostics asserts on the rig:
        /// 40 verts, 20 tris, submesh index counts [6, 24, 30]. Printed with the actual counts, and
        /// a loud FAIL if they disagree — but the run continues, because a wrong mesh is a finding
        /// and the picture of it is the evidence.
        /// </summary>
        private static void MeshInvariant()
        {
            Mesh m = BuildBeveledKeycap(0.063f, 0.0563f, SquareCapThickness, SquareCapBevel);
            int vtx = m.vertexCount;
            int tris = 0;
            for (int i = 0; i < m.subMeshCount; i++) tris += (int)(m.GetIndexCount(i) / 3);
            int s0 = m.subMeshCount > 0 ? (int)m.GetIndexCount(0) : -1;
            int s1 = m.subMeshCount > 1 ? (int)m.GetIndexCount(1) : -1;
            int s2 = m.subMeshCount > 2 ? (int)m.GetIndexCount(2) : -1;
            bool ok = vtx == 40 && tris == 20 && s0 == 6 && s1 == 24 && s2 == 30;
            Log($"MESH INVARIANT (the numbers LogCapDiagnostics asserts on the rig): verts {vtx} "
                + $"(expect 40), tris {tris} (expect 20), submesh indices top/bevel/wall {s0}/{s1}/{s2} "
                + $"(expect 6/24/30), submeshCount {m.subMeshCount} (expect 3), bounds {m.bounds.size}. "
                + (ok ? "MATCH — this station's port is the shipped mesh."
                      : "*** FAIL — THE PORTED MESH IS NOT THE SHIPPED ONE. Every picture below is of "
                        + "something the game does not build. ***"));
            if (!ok) _exit = 1;

            Mesh r = BuildRoundCap(0.0736f, DiscThickness, RoundCapSegments);
            int rTris = (int)(r.GetIndexCount(0) / 3);
            int expectV = RoundCapSegments * 4 + 2, expectT = RoundCapSegments * 4;
            Log($"ROUND CAP at {RoundCapSegments} segments: verts {r.vertexCount} (expect {expectV}), "
                + $"tris {rTris} (expect {expectT}: {RoundCapSegments} front fan + {RoundCapSegments} back fan "
                + $"+ {RoundCapSegments * 2} wall), submeshes {r.subMeshCount} (expect 1 — the disc carries ONE "
                + "material, so the ROLE cell goes on the whole disc), bounds " + r.bounds.size);
            if (r.vertexCount != expectV || rTris != expectT || r.subMeshCount != 1)
            {
                Err("*** ROUND CAP PORT DISAGREES with BuildRoundCap's own construction. ***");
                _exit = 1;
            }
            Object.DestroyImmediate(m);
            Object.DestroyImmediate(r);
        }

        /// <summary>
        /// THE POSITIVE CONTROL FOR THE COLOUR PIPELINE. Everything the header claims about
        /// reproducing the rig's gamma space is a claim about a chain of three independent
        /// mechanisms, and a chain that is wrong in one link looks exactly like a chain that is
        /// right — a plausible picture, slightly the wrong brightness, with nothing to compare it
        /// against. So this renders a case whose answer is ARITHMETIC.
        ///
        /// <para>A flat quad faces the camera down -Z. Its normal is (0,0,-1); BoardLit's fill
        /// direction has a NEGATIVE dot with it, so the fill term is clamped away, and with
        /// _LightBoost forced to 0 the key term vanishes too. `shade` is therefore exactly
        /// _Ambient = 1.0, and the fragment reduces to `tex * _Color`. Both factors are known
        /// exactly — the texel comes off the readable raw copy, the colour is authored here — so
        /// the framebuffer value is predictable to the byte. Measured against expected, out loud.
        /// If they disagree, every colour in every other picture this run writes is void.</para>
        /// </summary>
        private static void Calibrate(Shader sh)
        {
            var tex = LoadRaw(AtlasAlbedo("Oak"));
            if (tex == null) { Err("calibration: no oak atlas."); _exit = 1; return; }

            // A texel well inside the PLAIN cell (cell 0 is the top-left of the image, and the
            // texture's own row 0 is the image's TOP row for GetPixel? No — GetPixel counts from
            // the BOTTOM, which is exactly why the cell arithmetic flips the row. Sample by UV
            // through the same rect the material uses, so the two cannot disagree.)
            Cell(tex.width, tex.height, CellPlain, out Vector2 sc, out Vector2 of);
            Color texel = tex.GetPixel((int)((of.x + sc.x * 0.5f) * tex.width),
                                       (int)((of.y + sc.y * 0.5f) * tex.height));
            var colour = new Color(0.5f, 0.5f, 0.5f, 1f);

            // TWO WAYS THIS MEASUREMENT LIED BEFORE IT WAS BELIEVED, both found by running it:
            //  • the quad was 0.4 m in a frame 0.268 m tall, so it filled the picture and the
            //    "background" sample was another piece of QUAD. It is 0.15 m now and the corner is
            //    really the clear colour.
            //  • the cell was MINIFIED, so the render sampled a deep mip — an AVERAGE over the
            //    cell — while the expectation was one texel, and the two disagreed by 11/255 with
            //    nothing wrong. Point filtering plus a magnifying framing pins the render to mip 0
            //    and to exactly the texel the expectation names.
            FilterMode was = tex.filterMode;
            tex.filterMode = FilterMode.Point;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<MeshCollider>());
            quad.transform.localScale = new Vector3(0.15f, 0.15f, 1f);
            var m = MakeMat(sh, colour, tex, null, CellPlain);
            m.SetFloat("_Ambient", 1f);
            m.SetFloat("_LightBoost", 0f);
            m.SetFloat("_NormalStrength", 0f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = m;

            var go = new GameObject("CalCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Rig(ClearGrey);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            cam.transform.position = new Vector3(0f, 0f, -0.5f);
            cam.transform.LookAt(Vector3.zero);
            const int R = 512;
            var rt = new RenderTexture(R, R, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var read = new Texture2D(R, R, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, R, R), 0, 0);
            read.Apply();
            Color got = read.GetPixel(R / 2, R / 2);
            Color bg = read.GetPixel(2, 2);
            RenderTexture.active = null;
            cam.targetTexture = null;

            Color expect = new(texel.r * colour.r, texel.g * colour.g, texel.b * colour.b, 1f);
            float err = Mathf.Max(Mathf.Abs(got.r - expect.r),
                       Mathf.Max(Mathf.Abs(got.g - expect.g), Mathf.Abs(got.b - expect.b)));
            float bgErr = Mathf.Abs(bg.r - ClearGrey.r);
            Log($"COLOUR CALIBRATION (the rig runs GAMMA; this project is {QualitySettings.activeColorSpace}): "
                + $"flat quad, shade forced to exactly 1.0, texel {C(texel)} x colour {C(colour)} "
                + $"=> expected {C(expect)}, MEASURED {C(got)}, worst channel error {err:F4} "
                + $"({err * 255f:F1} of 255). Background: expected {ClearGrey.r:F3}, measured {bg.r:F3}.");
            if (err > 0.008f || bgErr > 0.008f)
            {
                Err("*** THE COLOUR PIPELINE DOES NOT REPRODUCE THE RIG. Every colour and every "
                    + "brightness in this run is void — read the geometry and the symbols only. ***");
                _exit = 1;
            }
            else
            {
                Log("   -> the gamma chain is proven end to end (raw texture load, .gamma colour "
                    + "pre-multiply, sRGB=False target). Brightness comparisons below are the rig's.");
            }

            tex.filterMode = was;
            Object.DestroyImmediate(read);
            rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(m);
            Object.DestroyImmediate(quad);
        }

        /// <summary>
        /// DOES THE KEYCAP NORMAL MAP DO ANYTHING AT ALL? The question is not rhetorical.
        /// BoardLit builds its shading normal in TANGENT space —
        /// <c>N = normalize(wt*nt.x + wb*nt.y + wn*nt.z)</c> — and
        /// <c>CardMesh.BuildBeveledKeycap</c> never writes tangents (no <c>RecalculateTangents</c>,
        /// no tangent stream). With no tangent the first two terms are whatever Unity binds for a
        /// missing vertex attribute, which is not a defensible input to a lighting equation.
        ///
        /// <para>So: render the same cap at _NormalStrength 1 and at 0 and diff them. If the two
        /// are identical, the atlas normal maps are bundle weight that never reaches a pixel on the
        /// square caps, and that is a finding about a shipped asset rather than about this
        /// station. Reported as a measurement, with the mesh's actual tangent count beside it.</para>
        /// </summary>
        private static void NormalMapCheck(Camera cam, Shader sh)
        {
            var (style, capW, capH, _) = Styles[0];
            var alb = LoadRaw(AtlasAlbedo(style));
            var nrm = LoadRaw(AtlasNormal(style));
            if (alb == null || nrm == null) { Err("normal-map A/B: oak atlas pair missing."); return; }

            var go = BuildCap(sh, alb, nrm, CellConfirm, IdleColor * ShippedCapTint, capW, capH,
                              SquareCapThickness, false, out Mesh mesh, out Material[] mats);
            int tangents = mesh.tangents != null ? mesh.tangents.Length : 0;

            cam.fieldOfView = BigFov;
            float d = FitDistance(Mathf.Max(capW, capH), BigFov, BigFill);
            var look = new Vector3(0f, 0f, -SquareCapThickness * 0.5f);
            Texture2D Grab()
            {
                cam.transform.position = look + RakeEye(d);
                cam.transform.LookAt(look);
                var rt = new RenderTexture(300, 300, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(300, 300, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, 300, 300), 0, 0); t.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                rt.Release(); Object.DestroyImmediate(rt);
                return t;
            }
            Texture2D on = Grab();
            foreach (var m in mats) m.SetFloat("_NormalStrength", 0f);
            Texture2D off = Grab();
            foreach (var m in mats) m.SetFloat("_NormalStrength", 1f);

            Color[] a = on.GetPixels(), b = off.GetPixels();
            double sum = 0; float max = 0f; int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (Mathf.Max(a[i].r, Mathf.Max(a[i].g, a[i].b)) <= 0.20f) continue;   // clear is 0.18
                float dch = Mathf.Max(Mathf.Abs(a[i].r - b[i].r),
                            Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                sum += dch; max = Mathf.Max(max, dch); n++;
            }
            Object.DestroyImmediate(on); Object.DestroyImmediate(off);
            Log($"NORMAL MAP A/B on a square {style} cap: the mesh carries {tangents} tangents "
                + $"(BuildBeveledKeycap writes none, so 0 is expected). _NormalStrength 1 vs 0 over "
                + $"{n} cap pixels: mean |diff| {(n > 0 ? sum / n : 0):F5}, max {max:F5}. "
                + (max < 0.004f
                   ? "*** THE NORMAL MAP MOVES NO PIXEL — with no tangent frame BoardLit's "
                     + "N collapses to the vertex normal, so Keycap*_normal.png is bundle weight that "
                     + "never reaches a pixel on the SQUARE caps. The round discs have no tangents "
                     + "either. Fixing it is one RecalculateTangents call in CardMesh. ***"
                   : "*** THE NORMAL MAP MOVES THE PICTURE ANYWAY, on a mesh with NO TANGENT STREAM. "
                     + "BoardLit builds N in tangent space, so the shading normal here is being built "
                     + "from whatever this graphics API binds for a missing TANGENT attribute — a "
                     + "DEFAULT, not an authored basis. The bump is therefore applied along an "
                     + "undefined direction, and nothing here can promise the rig's D3D11 binds the "
                     + "same default as this editor's OpenGL. One RecalculateTangents in "
                     + "CardMesh.BuildBeveledKeycap/BuildRoundCap would make it defined. ***"));
            Cleanup(go, mesh, mats);
        }

        /// <summary>Ported verbatim from <c>Cards.CardMesh.BuildBeveledKeycap</c>.</summary>
        private static Mesh BuildBeveledKeycap(float width, float height, float thickness, float bevel)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(Mathf.Min(hw, hh) * 0.9f, thickness * 0.9f));
            float iw = hw - bevel, ih = hh - bevel;
            float zTop = -thickness;
            float zBev = -thickness + bevel;
            float zBack = 0f;

            var verts = new List<Vector3>(24);
            var norms = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var top = new List<int>(6);
            var ring = new List<int>(24);
            var walls = new List<int>(30);

            Vector2 Uv(Vector3 p) => new(p.x / width + 0.5f, p.y / height + 0.5f);

            void AddQuad(List<int> sm, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int b0 = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
                uvs.Add(Uv(a)); uvs.Add(Uv(b)); uvs.Add(Uv(c)); uvs.Add(Uv(d));
                Vector3 rh = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(rh, n) > 0f)
                {
                    sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                    sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
                }
                else
                {
                    sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                    sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
                }
            }

            AddQuad(top, new(-iw, ih, zTop), new(iw, ih, zTop), new(iw, -ih, zTop), new(-iw, -ih, zTop),
                    Vector3.back);

            const float s = 0.70710678f;
            AddQuad(ring, new(-iw, ih, zTop), new(iw, ih, zTop), new(hw, hh, zBev), new(-hw, hh, zBev),
                    new Vector3(0f, s, -s));
            AddQuad(ring, new(iw, ih, zTop), new(iw, -ih, zTop), new(hw, -hh, zBev), new(hw, hh, zBev),
                    new Vector3(s, 0f, -s));
            AddQuad(ring, new(iw, -ih, zTop), new(-iw, -ih, zTop), new(-hw, -hh, zBev), new(hw, -hh, zBev),
                    new Vector3(0f, -s, -s));
            AddQuad(ring, new(-iw, -ih, zTop), new(-iw, ih, zTop), new(-hw, hh, zBev), new(-hw, -hh, zBev),
                    new Vector3(-s, 0f, -s));

            AddQuad(walls, new(-hw, hh, zBev), new(hw, hh, zBev), new(hw, hh, zBack), new(-hw, hh, zBack),
                    Vector3.up);
            AddQuad(walls, new(hw, hh, zBev), new(hw, -hh, zBev), new(hw, -hh, zBack), new(hw, hh, zBack),
                    Vector3.right);
            AddQuad(walls, new(hw, -hh, zBev), new(-hw, -hh, zBev), new(-hw, -hh, zBack), new(hw, -hh, zBack),
                    Vector3.down);
            AddQuad(walls, new(-hw, -hh, zBev), new(-hw, hh, zBev), new(-hw, hh, zBack), new(-hw, -hh, zBack),
                    Vector3.left);
            AddQuad(walls, new(-hw, hh, zBack), new(hw, hh, zBack), new(hw, -hh, zBack), new(-hw, -hh, zBack),
                    Vector3.forward);

            var mesh = new Mesh { name = "GloomhavenVR.BeveledKeycap" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(ring, 1);
            mesh.SetTriangles(walls, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Ported verbatim from <c>Cards.CardMesh.BuildRoundCap</c> (the body
        /// <c>GetRoundCap</c> caches at 64 segments).</summary>
        private static Mesh BuildRoundCap(float diameter, float thickness, int segments)
        {
            segments = Mathf.Clamp(segments, 12, 128);
            int seg = segments;
            float r = diameter * 0.5f;
            float h = Mathf.Max(0.0005f, thickness * 0.5f);
            float zFront = -h;
            float zBack = h;

            var verts = new List<Vector3>(seg * 4 + 2);
            var norms = new List<Vector3>(seg * 4 + 2);
            var uvs = new List<Vector2>(seg * 4 + 2);
            var tris = new List<int>(seg * 12);

            Vector2 Uv(float x, float y) => new(x / diameter + 0.5f, y / diameter + 0.5f);

            var ring = new Vector2[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                ring[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }

            int frontBase = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
                norms.Add(Vector3.back);
                uvs.Add(Uv(ring[i].x, ring[i].y));
            }
            int frontCenter = verts.Count;
            verts.Add(new Vector3(0f, 0f, zFront)); norms.Add(Vector3.back); uvs.Add(new Vector2(0.5f, 0.5f));

            int backBase = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
                norms.Add(Vector3.forward);
                uvs.Add(Uv(ring[i].x, ring[i].y));
            }
            int backCenter = verts.Count;
            verts.Add(new Vector3(0f, 0f, zBack)); norms.Add(Vector3.forward); uvs.Add(new Vector2(0.5f, 0.5f));

            int wallBase = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                Vector3 outward = new Vector3(ring[i].x, ring[i].y, 0f).normalized;
                verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
                verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
                norms.Add(outward); norms.Add(outward);
                uvs.Add(Uv(ring[i].x, ring[i].y)); uvs.Add(Uv(ring[i].x, ring[i].y));
            }

            for (int i = 0; i < seg; i++)
            {
                int next = (i + 1) % seg;
                tris.Add(frontCenter); tris.Add(frontBase + next); tris.Add(frontBase + i);
            }
            for (int i = 0; i < seg; i++)
            {
                int next = (i + 1) % seg;
                tris.Add(backCenter); tris.Add(backBase + i); tris.Add(backBase + next);
            }
            for (int i = 0; i < seg; i++)
            {
                int next = (i + 1) % seg;
                int a = wallBase + i * 2;
                int b = wallBase + i * 2 + 1;
                int c = wallBase + next * 2;
                int d = wallBase + next * 2 + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(c); tris.Add(d); tris.Add(b);
            }

            var mesh = new Mesh { name = "GloomhavenVR.RoundCap" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // =========================================================================================
        // MATERIALS — Cards/PlayTray.6.Build.NewKeycapMaterial, reproduced
        // =========================================================================================

        /// <summary>
        /// One keycap material. <paramref name="cell"/> &lt; 0 writes NO texture transform, which is
        /// the shipped BEFORE path (style == null in NewKeycapMaterial). Otherwise the ST is
        /// computed from the ALBEDO's own pixel size and written to BOTH _MainTex and _BumpMap —
        /// that is what the mod does, and it is why a 1024 albedo and a 512 normal share one rect.
        /// The float defaults below are BoardLit's own declared Properties defaults, restated so a
        /// picture is never at the mercy of a Material constructor's memory of them.
        /// </summary>
        private static Material MakeMat(Shader sh, Color col, Texture2D alb, Texture2D nrm, int cell)
        {
            var m = new Material(sh);
            m.SetColor("_Color", Rig(col));      // see Rig(): arrives at the shader as `col` itself
            if (alb != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alb);
            if (nrm != null && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", nrm);
            if (cell >= 0 && alb != null)
            {
                Cell(alb.width, alb.height, cell, out Vector2 scale, out Vector2 offset);
                if (m.HasProperty("_MainTex"))
                {
                    m.SetTextureScale("_MainTex", scale);
                    m.SetTextureOffset("_MainTex", offset);
                }
                if (nrm != null && m.HasProperty("_BumpMap"))
                {
                    m.SetTextureScale("_BumpMap", scale);
                    m.SetTextureOffset("_BumpMap", offset);
                }
            }
            m.SetFloat("_Ambient", 0.5f);
            m.SetFloat("_LightBoost", 0.85f);
            m.SetFloat("_NormalStrength", 1.0f);
            m.SetFloat("_SpecStrength", 0f);
            m.SetFloat("_Cull", 2f);
            return m;
        }

        /// <summary>
        /// A cap body: the mesh, and the one-or-three materials it carries. Square caps take
        /// [0] the ROLE cell on the top plateau, [1] and [2] the PLAIN cell on the bevel ring and
        /// on the walls+back — the exact three lines PlayTray and RemoteBoardFurniture both write.
        /// Round discs are ONE submesh and therefore ONE material, which carries the role.
        /// </summary>
        private static GameObject BuildCap(Shader sh, Texture2D alb, Texture2D nrm, int cell,
                                           Color face, float w, float h, float thick, bool round,
                                           out Mesh mesh, out Material[] mats)
        {
            Color top = SeatedCapColor(face);
            var go = new GameObject("Cap");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            if (round)
            {
                mesh = BuildRoundCap(w, thick, RoundCapSegments);
                mats = new[] { MakeMat(sh, top, alb, nrm, cell) };
            }
            else
            {
                mesh = BuildBeveledKeycap(w, h, thick, SquareCapBevel);
                mats = new[]
                {
                    MakeMat(sh, top, alb, nrm, cell),
                    MakeMat(sh, BevelTint(top), alb, nrm, cell < 0 ? -1 : CellPlain),
                    MakeMat(sh, WallTint(top), alb, nrm, cell < 0 ? -1 : CellPlain),
                };
            }
            mf.sharedMesh = mesh;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static void Cleanup(GameObject go, Mesh mesh, Material[] mats)
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);
            foreach (var m in mats) Object.DestroyImmediate(m);
        }

        // =========================================================================================
        // SHOTS
        // =========================================================================================

        private static void ShootCap(Camera cam, string outDir, string stem, Shader sh,
                                     Texture2D alb, Texture2D nrm, int cell, Color face,
                                     float w, float h, float thick, bool round, bool describe)
        {
            var go = BuildCap(sh, alb, nrm, cell, face, w, h, thick, round, out Mesh mesh, out Material[] mats);
            // The disc is centred on z = 0; the square cap spans z = -thickness..0. Aim at the
            // drawn body's own centre either way so the framing is comparable between shapes.
            var look = new Vector3(0f, 0f, round ? 0f : -thick * 0.5f);
            float d = FitDistance(Mathf.Max(w, h), BigFov, BigFill);
            cam.fieldOfView = BigFov;
            Shot(cam, outDir, stem + "_front.png", look + new Vector3(0f, 0f, -d), look, BigPx, BigPx, 8);
            Shot(cam, outDir, stem + "_rake.png", look + RakeEye(d), look, BigPx, BigPx, 8);
            if (describe)
            {
                Color top = SeatedCapColor(face);
                Log($"{stem}: cap {w * 1000f:F1} x {h * 1000f:F1} x {thick * 1000f:F1} mm "
                    + $"({(round ? "ROUND disc, 1 submesh" : "SQUARE, 3 submeshes")}), cell {cell}, "
                    + $"face {C(top)} bevel {C(BevelTint(top))} wall {C(WallTint(top))}"
                    + (SeatFloorEngages(face) ? " [SEAT FLOOR LIFTED the face — it would have been darker than its own well]" : "")
                    + $", camera {d * 1000f:F0} mm at fov {BigFov} deg.");
                if (cell >= 0 && alb != null)
                {
                    Cell(alb.width, alb.height, cell, out Vector2 s, out Vector2 o);
                    Log($"   cell {cell} ST: scale ({s.x:F6}, {s.y:F6}) offset ({o.x:F6}, {o.y:F6}) "
                        + $"-> u {o.x:F4}..{o.x + s.x:F4}, v {o.y:F4}..{o.y + s.y:F4} of a "
                        + $"{GridCols}x{GridRows} grid with {InsetTexels} texels of inset.");
                }
            }
            Cleanup(go, mesh, mats);
        }

        /// <summary>
        /// The two apparent sizes, rendered AT that size — a 32 px cap is genuinely rasterised into
        /// a 48 px image, never rendered large and shrunk (which would substitute a downsample
        /// filter's opinion for the rasteriser's). The contact sheet upscales these with POINT
        /// filtering, so the reader is looking at the pixels that actually exist.
        /// </summary>
        private static void ShootZoom(Camera cam, string outDir, string stem, Shader sh,
                                      Texture2D alb, Texture2D nrm, int cell, Color face,
                                      float w, float h, float thick, bool round)
        {
            var go = BuildCap(sh, alb, nrm, cell, face, w, h, thick, round, out Mesh mesh, out Material[] mats);
            var look = new Vector3(0f, 0f, round ? 0f : -thick * 0.5f);
            foreach (var (tag, targetPx, aa) in new[]
                     { ("near96", NearPx, 4), ("far32", FarPx, 4), ("far32_aa1", FarPx, 1) })
            {
                int img = targetPx + targetPx / 2;                  // a quarter of the target as margin each side
                cam.fieldOfView = img / PxPerDegree;                // hold 20 px/degree, the rig's number
                float capAngle = targetPx / PxPerDegree;            // degrees the cap must subtend
                float d = (w * 0.5f) / Mathf.Tan(capAngle * 0.5f * Mathf.Deg2Rad);
                Shot(cam, outDir, $"{stem}_zoom_{tag}.png", look + RakeEye(d), look, img, img, aa);
                if (tag == "near96")
                    Log($"{stem} ZOOM: {targetPx} px across at {PxPerDegree} px/deg = {capAngle:F2} deg, "
                        + $"which puts a {w * 1000f:F1} mm board-local cap at {d:F3} m — i.e. the shipped board "
                        + $"at world scale {0.5f / d:F3} viewed from 0.5 m, inside the 0.42..0.88 the "
                        + "footprint table gives. MSAA 4 (what the rig runs); the far shot is written twice, "
                        + "aa4 and aa1, so the aliasing is not hidden by the sample count.");
            }
            Cleanup(go, mesh, mats);
        }

        /// <summary>
        /// The press stroke, 9 phases evenly across PressStrokeSeconds. The cap body sits at local
        /// z = CapRestZ and travels +Z (away from the viewer, into the board) by
        /// CapTravel * PressDepth01(t) — the same line BoardButton runs
        /// (pos.z = CapRestZ + Travel * depth01).
        ///
        /// <para>A FIXED PLATE IS DRAWN AT z = 0 and it is not decoration: 4 mm of travel on a
        /// 63 mm cap is a couple of pixels of silhouette change, and without a stationary surface
        /// behind it the filmstrip is nine identical pictures. The plate is the board face the cap
        /// is seated in, tinted with ButtonTuning.CapWellColor — the colour the mod actually paints
        /// the recess with.</para>
        /// </summary>
        private static void PressFilmstrip(Camera cam, string outDir, Shader sh)
        {
            var (style, capW, capH, _) = Styles[0];
            var alb = LoadRaw(AtlasAlbedo(style));
            var nrm = LoadRaw(AtlasNormal(style));
            if (alb == null) { Err("press filmstrip: no oak atlas."); _exit = 1; return; }

            var go = BuildCap(sh, alb, nrm, CellConfirm, IdleColor * ShippedCapTint, capW, capH,
                              SquareCapThickness, false, out Mesh mesh, out Material[] mats);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);   // Unity's Quad faces -Z
            Object.DestroyImmediate(plate.GetComponent<MeshCollider>());
            plate.transform.localScale = new Vector3(capW * 2.6f, capH * 2.6f, 1f);
            plate.transform.position = Vector3.zero;
            var plateMat = MakeMat(sh, CapWellColor, alb, nrm, CellPlain);
            plate.GetComponent<MeshRenderer>().sharedMaterial = plateMat;

            cam.fieldOfView = BigFov;
            float d = FitDistance(Mathf.Max(capW, capH) * 1.5f, BigFov, BigFill);
            var look = new Vector3(0f, 0f, -SquareCapThickness * 0.5f);
            Log($"PRESS STROKE — {PressStrokeSeconds * 1000f:F0} ms total "
                + $"(attack {PressAttackSeconds * 1000f:F0} + hold {PressHoldSeconds * 1000f:F0} + release "
                + $"{PressReleaseSeconds * 1000f:F0}), travel {CapTravel * 1000f:F1} mm, rest z {CapRestZ * 1000f:F1} mm, "
                + $"rebound {PressReboundFraction:F2} of travel at split {PressReboundSplit:F2}. "
                + "frame / t(ms) / PressDepth01 / cap local z (mm):");
            for (int i = 0; i < 9; i++)
            {
                float t = PressStrokeSeconds * i / 8f;
                float depth = PressDepth01(t);
                float z = CapRestZ + CapTravel * depth;
                go.transform.position = new Vector3(0f, 0f, z);
                Shot(cam, outDir, $"press_{i}.png", look + RakeEye(d), look, 360, 360, 8);
                Log($"   press frame {i}: t {t * 1000f:F1} ms, depth {depth:F4}, cap z {z * 1000f:F3} mm"
                    + (depth < 0f ? "  <- REBOUND, proud of rest" : ""));
            }
            Object.DestroyImmediate(plate);
            Object.DestroyImmediate(plateMat);
            Cleanup(go, mesh, mats);
        }

        // =========================================================================================
        // THE ENGRAVING — A STAND-IN, and it is labelled one in every filename it writes
        // =========================================================================================
        //
        // WHAT IS REAL HERE AND WHAT IS NOT.
        //   REAL: the three LAYERS and their order, the three COLOURS per style, the underlay's
        //         DOWN-AND-LEFT direction, the 0.8 mm proud seating, the caption fit box, and the
        //         DE/EN string pairs. Those are read straight out of Cards/BoardEngraving.cs.
        //   NOT REAL: the glyph shapes and their antialiasing. TextMeshPro is not in this project's
        //         Packages/manifest.json, so the shipped SDF material — and the game's MarcellusSC
        //         face that NativeButtonSkin.ApplyFont harvests off a live widget — cannot be
        //         instantiated. This uses Unity's legacy TextMesh with the built-in font, which is a
        //         bitmap atlas quad-per-glyph, not a distance field.
        //   APPROXIMATED: TMP's _OutlineWidth and _UnderlayOffsetX/Y are in SDF units — fractions of
        //         the font asset's distance-field spread, a quantity that does not exist without a
        //         font asset. They are converted here through one stated assumption
        //         (SdfSpreadEm below) so the offsets are in the right RATIO to each other and to the
        //         glyph. A reader may judge LAYOUT, COLOUR and DEPTH from these pictures. Nobody may
        //         judge stroke crispness, hinting or SDF aliasing from them.

        /// <summary>A TMP font asset's distance-field spread as a fraction of the em, used ONLY to
        /// turn BoardEngraving's SDF-unit offsets into metres for the stand-in. Assumed, not
        /// measured — there is no font asset in this project to measure.</summary>
        private const float SdfSpreadEm = 0.10f;

        private const float ProudLocalZ = -0.0008f;                 // BoardEngraving.ProudLocalZ
        private const float KeylineWidth = 0.09f;                   // BoardEngraving.KeylineWidth
        private const float LipOffsetX = -0.28f, LipOffsetY = -0.28f;
        private const float LipAlpha = 0.85f;
        private static readonly Vector2 RestCaptionBox = new(0.105f, 0.017f);   // BoardEngraving

        private static readonly Color[] GrooveFloor =
        {
            new(0.289f, 0.186f, 0.086f, 1f),
            new(0.124f, 0.121f, 0.124f, 1f),
            new(0.209f, 0.198f, 0.118f, 1f),
        };
        private static readonly Color[] LitLip =
        {
            new(0.867f, 0.557f, 0.258f, 1f),
            new(0.372f, 0.363f, 0.372f, 1f),
            new(0.627f, 0.593f, 0.354f, 1f),
        };
        private static readonly Color[] Keyline =
        {
            new(0.141f, 0.091f, 0.042f, 1f),
            new(0.061f, 0.059f, 0.061f, 1f),
            new(0.102f, 0.097f, 0.058f, 1f),
        };

        private static readonly (string Lang, string[] Lines)[] Captions =
        {
            ("de", new[] { "KURZE RAST", "LANGE RAST", "RUNDE 12" }),
            ("en", new[] { "SHORT REST", "LONG REST", "ROUND 12" }),
        };

        private static void Engraving(Camera cam, string outDir, Shader sh)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
            {
                Err("engraving stand-in: no built-in font in this editor — skipped.");
                _exit = 1;
                return;
            }
            Log("ENGRAVING STAND-IN: the glyph shapes and the antialiasing below are NOT the shipped "
                + "ones — the shipped board labels are TextMeshPro SDF text in the game's MarcellusSC "
                + "face, and TextMeshPro is not in this project's package manifest — so these pictures "
                + "are evidence for LAYOUT, COLOUR and DEPTH BEHAVIOUR only, never for glyph rendering. "
                + $"Every file they produce is named standin_*. (Font used: {font.name}; TMP's SDF-unit "
                + $"outline/underlay offsets converted at an ASSUMED spread of {SdfSpreadEm:F2} em.)");

            for (int si = 0; si < Styles.Length; si++)
            {
                var (style, _, _, _) = Styles[si];
                var alb = LoadRaw(AtlasAlbedo(style));
                var nrm = LoadRaw(AtlasNormal(style));
                if (alb == null) continue;

                foreach (var (lang, lines) in Captions)
                {
                    var root = new GameObject($"Engrave_{style}_{lang}");

                    // The board-surface stand-in: the style's own PLAIN cell (index 0) on a quad,
                    // through the same BoardLit the board uses. It is a stand-in for the BOARD
                    // atlas, which is a different texture — but it is the same material family and
                    // the same style, which is what the colour judgement needs.
                    var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(plate.GetComponent<MeshCollider>());
                    plate.transform.SetParent(root.transform, false);
                    plate.transform.localScale = new Vector3(0.150f, 0.090f, 1f);
                    var plateMat = MakeMat(sh, Color.white, alb, nrm, CellPlain);
                    // THE PLATE'S BUMP IS TURNED OFF, and this is a correction rather than a
                    // simplification. This quad is 150 mm wide wearing a cell authored for a ~60 mm
                    // cap, i.e. the normal map is stretched about 2.5x past its intended texel
                    // density. At that scale its slopes drive BoardLit's `shade` above 1.0 and the
                    // plate CLIPS: measured, 30 % of the oak plate's pixels had a blown channel and
                    // its mean luminance came out 0.71 where albedo x shade is 0.50. A caption is
                    // judged against the surface it is cut into, so a surface 40 % too bright makes
                    // this picture argue the opposite of the truth about legibility. Flat plate =
                    // albedo x shade, which is the honest mid-tone of that material.
                    plateMat.SetFloat("_NormalStrength", 0f);
                    plate.GetComponent<MeshRenderer>().sharedMaterial = plateMat;

                    var junk = new List<GameObject>();
                    float em = RestCaptionBox.y;    // the caption's own cap height is the em stand-in
                    for (int li = 0; li < lines.Length; li++)
                    {
                        float y = 0.026f - li * 0.026f;
                        junk.Add(BuildEngravedLine(root.transform, font, lines[li],
                                                   new Vector3(0f, y, 0f), si, em));
                    }

                    cam.fieldOfView = BigFov;
                    float d = FitDistance(0.150f, BigFov, 0.86f);
                    var look = Vector3.zero;
                    // Two renders: the first one forces the legacy TextMesh components to build
                    // their meshes (they generate lazily), the second is the picture. Bounds are
                    // read between the two and each line is scaled into RestCaptionBox — the same
                    // shrink-to-fit Core.TmpFit.Fit performs on the shipped labels.
                    cam.transform.position = look + RakeEye(d);
                    cam.transform.LookAt(look);
                    var warm = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = warm; cam.Render(); cam.targetTexture = null;
                    warm.Release(); Object.DestroyImmediate(warm);
                    foreach (var g in junk) FitLine(g, RestCaptionBox);

                    Shot(cam, outDir, $"standin_engraving_{style}_{lang}_rake.png",
                         look + RakeEye(d), look, 900, 540, 8);

                    Log($"   standin_engraving_{style}_{lang}: groove floor {C(GrooveFloor[si])}, "
                        + $"lit lip {C(LitLip[si])} at alpha {LipAlpha:F2} offset ({LipOffsetX:F2}, {LipOffsetY:F2}) "
                        + $"SDF units = ({LipOffsetX * SdfSpreadEm * em * 1000f:F2}, "
                        + $"{LipOffsetY * SdfSpreadEm * em * 1000f:F2}) mm here (DOWN and LEFT), keyline "
                        + $"{C(Keyline[si])} at width {KeylineWidth:F2} SDF units = "
                        + $"{KeylineWidth * SdfSpreadEm * em * 1000f:F2} mm radius, seated "
                        + $"{-ProudLocalZ * 1000f:F1} mm proud.");

                    Object.DestroyImmediate(plateMat);
                    Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>One engraved line as the three layers BoardEngraving.Restyle writes: a KEYLINE
        /// (8 copies on a small circle, standing in for the SDF outline), an UNDERLAY offset DOWN
        /// and LEFT at the lit-lip colour and its alpha, and the FACE at the groove-floor colour.
        /// Draw order is by distance — the layers are separated in z so the transparent sort puts
        /// the face in front, the underlay behind it and the keyline furthest back.</summary>
        private static GameObject BuildEngravedLine(Transform parent, Font font, string text,
                                                    Vector3 localPos, int styleRow, float em)
        {
            var holder = new GameObject("Line_" + text);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(localPos.x, localPos.y, ProudLocalZ);

            float keyR = KeylineWidth * SdfSpreadEm * em;
            float lipX = LipOffsetX * SdfSpreadEm * em;
            float lipY = LipOffsetY * SdfSpreadEm * em;

            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.PI * 2f * i / 8f;
                MakeTextLayer(holder.transform, font, text, Keyline[styleRow],
                              new Vector3(Mathf.Cos(a) * keyR, Mathf.Sin(a) * keyR, 0.00006f));
            }
            Color lip = LitLip[styleRow]; lip.a = LipAlpha;
            MakeTextLayer(holder.transform, font, text, lip, new Vector3(lipX, lipY, 0.00003f));
            MakeTextLayer(holder.transform, font, text, GrooveFloor[styleRow], Vector3.zero);
            return holder;
        }

        private static void MakeTextLayer(Transform parent, Font font, string text, Color col, Vector3 off)
        {
            var go = new GameObject("L");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = off;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = 0.01f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            tm.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            font.RequestCharactersInTexture(text, tm.fontSize, FontStyle.Normal);
        }

        /// <summary>Shrink a built line into its fit box, the way Core.TmpFit.Fit does on the rig.
        /// Measured off the RENDERED bounds rather than off a font metric, because a legacy TextMesh
        /// has no metric this project can trust.</summary>
        private static void FitLine(GameObject holder, Vector2 box)
        {
            Bounds b = new(holder.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in holder.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any || b.size.x <= 0f || b.size.y <= 0f)
            {
                Err($"engraving stand-in: '{holder.name}' has NO rendered bounds — the legacy TextMesh "
                    + "did not generate a mesh in batch mode. The picture will be blank; do not read it "
                    + "as a layout result.");
                _exit = 1;
                return;
            }
            float s = Mathf.Min(box.x / b.size.x, box.y / b.size.y);
            holder.transform.localScale = new Vector3(s, s, 1f);
        }

        // =========================================================================================
        // PLUMBING
        // =========================================================================================

        /// <summary>Distance at which an object <paramref name="widthMeters"/> across fills
        /// <paramref name="fill"/> of the frame at <paramref name="fovDeg"/> (square frames only —
        /// every shot here is square or wider than tall with the object centred).</summary>
        private static float FitDistance(float widthMeters, float fovDeg, float fill) =>
            (widthMeters * 0.5f / fill) / Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);

        /// <summary>The rake eye offset: <see cref="RakeDegrees"/> above the cap's face normal
        /// (which is local -Z), i.e. what the player sees down the board's shipped tilt.</summary>
        private static Vector3 RakeEye(float d)
        {
            float r = RakeDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(r) * d, -Mathf.Cos(r) * d);
        }

        private static void Shot(Camera cam, string outDir, string file, Vector3 eye, Vector3 look,
                                 int w, int h, int aa)
        {
            cam.transform.position = eye;
            cam.transform.LookAt(look);
            // RenderTextureReadWrite.Linear = sRGB FALSE on the target, so the shader's output is
            // STORED RAW instead of being encoded on write. That is the rig's framebuffer (see the
            // header): on a Gamma-space project every RT comes back sRGB=False whatever the
            // ReadWrite mode says, which is the observation the mod's own comments record.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.Linear) { antiAliasing = aa };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            string path = Path.Combine(outDir, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Written.Add(file);
        }

        private static string C(Color c) =>
            "(" + c.r.ToString("F3", CultureInfo.InvariantCulture)
                + ", " + c.g.ToString("F3", CultureInfo.InvariantCulture)
                + ", " + c.b.ToString("F3", CultureInfo.InvariantCulture) + ")";

        private static void Log(string s) => Debug.Log("[KeycapPreview] " + s);
        private static void Err(string s) => Debug.LogError("[KeycapPreview] " + s);
    }
}
