using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Mixed-reality / chroma-key mode (hardware test #22 item 7). When ON, the whole
/// SKY/BACKGROUND turns a flat, solid KEY COLOR (default green) so Virtual Desktop can
/// chroma-key it and composite the game over the real room — the diorama/table geometry
/// keeps rendering, only the sky/background becomes the flat key color the compositor
/// punches out.
///
/// HOW THE SKYBOX IS DISABLED WHILE THE DIORAMA STAYS VISIBLE
/// ----------------------------------------------------------
/// In VR the ONLY camera that reaches the HMD is the rig's own HeadCamera
/// (<see cref="VRCameraPolicy.AllowedHead"/>) — every game camera is forced to
/// StereoTargetEyeMask.None by <see cref="VRCameraPolicy"/>. So the passthrough result is
/// decided entirely by the HeadCamera: force its clear to SolidColor(key) and the sky is
/// gone while all the opaque diorama geometry in its culling mask still renders. We also
/// null <see cref="RenderSettings.skybox"/> (re-asserted every tick — StaticAmbience can
/// re-set it) so ambient/reflection sky contributions vanish too, and sweep every OTHER
/// live camera whose clear is still <see cref="CameraClearFlags.Skybox"/> over to
/// SolidColor(key) so the desktop mirror keys identically and nothing sky-clears anywhere.
///
/// PRECEDENCE (documented, keyed off the MR flag)
/// - HeadCamera: MR owns its clear WHILE ON. <see cref="Tick"/> is the LAST entry
///   (<c>"Rig.MixedReality"</c>) of <c>VRRigDriver</c>'s per-frame tail-step array, i.e. it runs
///   AFTER that array's own TickHeadClearColor step, so MR's key color wins the frame
///   (TickHeadClearColor only pins backgroundColor when clearFlags==SolidColor, then MR
///   overrides it). When MR turns OFF the head clear is restored and TickHeadClearColor
///   resumes its VoidColor management.
/// - Game cameras: MR only ever touches cameras whose clear is STILL Skybox. FlatScreen's
///   TickStackClears drives its RT-captured cameras to SolidColor/Depth for compositing —
///   those are already non-Skybox, so MR never writes the same camera in the same frame;
///   FlatScreen keeps authority over its captured cameras (they feed the flat window, not
///   passthrough).
/// - <see cref="VRCameraPolicy"/> is untouched (stereoTargetEye only) — it is precisely
///   what guarantees only the HeadCamera reaches the HMD, which is why keying the
///   HeadCamera is sufficient for passthrough.
///
/// SKY/BACKGROUND GEOMETRY (hardware test #23 item 2)
/// -------------------------------------------------
/// Keying the HeadCamera clear + nulling the skybox is NOT enough on its own: the scenario
/// background is drawn as OPAQUE MESH GEOMETRY, not a skybox. The scenario environment is
/// procedurally generated (Apparance), the HeadCamera renders the scenario camera's full
/// culling mask (0xFFFFFFFF — verified in the test-#23 rig-build log), and a
/// backdrop/skydome mesh in that mask draws OVER the SolidColor key. No code sets the
/// scenario cameras' clearFlags/backgroundColor and there is no per-camera Skybox
/// component (verified: <c>StaticAmbience.Apply</c> is the ONLY writer of
/// <c>RenderSettings.skybox</c>), so no clear-flag/skybox patch can remove it. We therefore
/// SWEEP the live renderers and disable the ones that draw the surrounding sky: any renderer
/// whose material/shader/name reads sky-ish, OR whose world bounds SURROUND the head with a
/// large extent on all three axes (a dome/sphere/backdrop box — never a flat floor tile or a
/// table prop). Each disabled renderer is recorded and re-enabled on restore; the diorama
/// tiles/props (which never enclose the head) stay visible. Every hidden renderer is logged
/// by name/layer/bounds/shader, and when the sweep hides nothing the biggest enclosing
/// candidates are dumped, so the next hardware run can name the real sky source.
///
/// Idempotent; originals (per camera + the skybox material + disabled sky renderers) are
/// recorded on first force and RESTORED fully on MR-off, VR-stop, or hot reload
/// (<see cref="RestoreAll"/>).
/// </summary>
internal static class MixedReality
{
    private static ConfigFile? _file;

    /// <summary>MR mode master (settings-panel toggle binds this).</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>Chroma key color the sky/background clears to (default solid green).</summary>
    internal static ConfigEntry<Color> KeyColor = null!;

    /// <summary>Sweep + disable the sky/background GEOMETRY (item 2). Safety valve — default on.</summary>
    internal static ConfigEntry<bool> HideSkyMeshes = null!;

    /// <summary>Give ALL "unseen" fog-of-war geometry (the face-down preview tile stacks AND the
    /// unseen-area hexes inside revealed tiles) an opaque dark UNDERLAY while MR is on, so the
    /// passthrough/key can no longer show through it (user rulings 2026-08-04 + 2026-08-05).
    /// Safety valve like <see cref="HideSkyMeshes"/> — default on. The key keeps its original
    /// name from the preview-stack-only round; its scope has grown, its cfg identity has not.</summary>
    internal static ConfigEntry<bool> OpaquePreviewTiles = null!;

    /// <summary>XZ widening of each piece's GROOVE FILL copy (round 7 semantics — the round-4
    /// "skirt on the underlay itself" is gone: round 5 proved the animation is UV-scroll, which
    /// cannot leave the mesh silhouette, and a DISPLACED primary copy stops sitting coplanar
    /// behind the beveled groove faces, which round 7's screenshot exposed as green channels.
    /// The primary underlay is exact 1:1 again; this factor widens only the lowered fill copy so
    /// neighboring fills overlap under the groove line). Tunable live (a change rebuilds).</summary>
    internal static ConfigEntry<float> UnseenSkirtScale = null!;

    /// <summary>World-units drop of each piece's groove-fill copy below its authored pose
    /// (round 7): the fill's top surfaces must sit BELOW the beveled V-channel floors between
    /// neighboring hexes so a ray through a groove lands on dark. Tunable live. Too small = deep
    /// grooves still glow; too large = the fill peeks out below the outer rim pieces.</summary>
    internal static ConfigEntry<float> UnseenFillDrop = null!;

    /// <summary>
    /// Key-colour presets offered by the settings UI.
    ///
    /// <para>Green, magenta and blue are the chroma keys a compositor expects — saturated colours
    /// no game pixel is likely to share. BLACK is not a chroma key at all and is here for the other
    /// use: a headset whose passthrough composites on black, and players who simply want the void
    /// dark rather than lurid. It is last because picking it turns the chroma workflow off in
    /// everything but name.</para>
    /// </summary>
    private static readonly (string Name, Color Color)[] Presets =
    {
        ("Green", new Color(0f, 1f, 0f, 1f)),
        ("Magenta", new Color(1f, 0f, 1f, 1f)),
        ("Blue", new Color(0f, 0f, 1f, 1f)),
        ("Black", new Color(0f, 0f, 0f, 1f)),
    };

    /// <summary>Preset names, in offer order — the dropdown's option list.</summary>
    internal static string[] KeyColorNames
    {
        get
        {
            var names = new string[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
                names[i] = Presets[i].Name;
            return names;
        }
    }

    /// <summary>Index of the current key colour among the presets, or -1 for a custom one.</summary>
    internal static int KeyColorIndex
    {
        get
        {
            Color c = KeyColor.Value;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Approximately(Presets[i].Color, c))
                    return i;
            }
            return -1;
        }
    }

    /// <summary>Pick a preset by index — what a dropdown needs, where cycling needed no index.</summary>
    internal static void SetKeyColor(int index)
    {
        Bind();
        if (index < 0 || index >= Presets.Length)
            return;
        KeyColor.Value = Presets[index].Color; // BepInEx persists on set
    }

    // Recorded originals for full restore.
    private static readonly Dictionary<Camera, (CameraClearFlags Flags, Color Bg)> CamOriginals = new();
    private static readonly List<Camera> Scratch = new(8);
    private static Material? _savedSkybox;
    private static bool _skyboxSaved;

    // "Unseen" fog-of-war geometry backed by an opaque dark UNDERLAY while MR is on (user rulings
    // 2026-08-04 "die Stapel noch nicht entdeckter Räume sollen in MR nicht transparent sein" +
    // 2026-08-05 "die Kacheln, die das noch nicht entdeckte Gebiet markieren, sehen in MR aus wie
    // grünes Glas — gleicher Look, gleiche Animation, aber keine Transparenz"). Each entry is one
    // matched source renderer plus the mod-owned underlay CHILD cloned from its mesh; the child
    // dies with its source (Apparance regenerates tile content constantly — the ModBuild-57 run
    // accumulated 226 overrides in one session), so teardown is structural, not bookkept.
    // See ForceUnseenOpaque for the full derivation.
    private sealed class UnseenUnderlay
    {
        public Renderer Source = null!;
        public int SourceId;          // GetInstanceID at build time (fast dedup-set removal)
        public Renderer Plate = null!; // the underlay's own MeshRenderer, child of Source

        /// <summary>Round 7: the piece's GROOVE FILL — a second same-mesh copy, mildly XZ-widened
        /// and dropped by <see cref="UnseenFillDrop"/>, whose top surfaces sit UNDER the beveled
        /// V-channels between neighboring hexes so a ray through a groove lands on dark instead
        /// of the key. A child of the source like the plate (structural lifecycle); its union
        /// follows the hex silhouettes — the round-5/6 rectangular base quads it replaces were
        /// visible as an alien slab at the region rim (user ruling: removed).</summary>
        public Renderer? Fill;
    }

    private static readonly List<UnseenUnderlay> UnseenUnderlays = new(64);

    /// <summary>Instance ids of every source renderer that already carries an underlay — the
    /// sweep's dedup test (a linear list scan went quadratic against the regen churn above).</summary>
    private static readonly HashSet<int> UnseenSources = new(64);

    private static Material? _unseenDarkMat;  // opaque dark plate — key-color-safe, retinted live
    private static Material? _unseenSkipMat;  // fully invisible — fills a source's opaque slots
    private static Color _unseenDarkColor;
    private static int _previewScanNextFrame; // throttle (same cadence as the sky sweep)
    private static int _loggedPreviewCount = -1;
    private static bool _previewDiagLogged;
    private static int _unseenVerboseLogs;    // per-renderer build log cap (see the churn note)

    // Border-census scratch/state (CensusUnseenBorder): reused lists, change-gate hash, rate floor.
    private static readonly List<Renderer> CensusScratch = new(32);
    private static readonly List<Bounds> CensusBoundsScratch = new(64);
    private static int _censusLastHash;
    private static float _censusNextAllowed;

    /// <summary>The skirt scale the live underlays were built with — a config change rebuilds
    /// them (restore + immediate resweep) so tuning needs no MR toggle, let alone a rebuild.</summary>
    private static float _appliedSkirtScale = -1f;

    /// <summary>The fill drop the live underlays were built with — tracked beside
    /// <see cref="_appliedSkirtScale"/> so a config change rebuilds live (round 7).</summary>
    private static float _appliedFillDrop = -1f;

    /// <summary>Shader names whose float/vector properties were already dumped this session
    /// (<see cref="DumpUnseenShaderProperties"/> — the margin-derivation instrument).</summary>
    private static readonly HashSet<string> DumpedUnseenShaders = new(4);

    /// <summary>Lowest renderQueue observed on any matched family MATERIAL this session (round
    /// 6): the base quads/underlays must composite BEFORE the family draws, against a depth
    /// buffer that does not yet contain the family surfaces. All evidence says the family sits
    /// in the transparent range (&gt;2500 — the ModBuild-57 IsTranslucent match could only have
    /// passed on the queue, the materials expose no _DstBlend), so the default 2500 already
    /// precedes it; but if a family material ever shows up AT or BELOW 2500 with its pass-0
    /// hardcoded ZWrite On (ShaderOcclusionPatcher README: Amp_Basic_Unseen 0/0 zWrite On), a
    /// later-drawn backing would fail LEqual behind it from above — so the shared materials'
    /// queue adapts to observedMin−1 the moment the observation says so. int.MaxValue = none
    /// observed yet.</summary>
    private static int _familyMinQueue = int.MaxValue;

    // Sky/background geometry hidden while MR is on (item 2). The scenario backdrop/skydome is
    // opaque mesh geometry, not the skybox — disabled here, re-enabled on restore.
    private static readonly List<Renderer> HiddenSky = new(8);
    private static readonly List<Renderer> RendererScratch = new(8);
    private static int _skyScanNextFrame;   // throttle the (allocating) renderer sweep
    private static int _loggedSkyCount = -1; // change-dedup for the hidden-count log
    private static bool _skyDiagLogged;      // one-shot candidate dump when nothing matched

    /// <summary>Sweep the scene renderers this often (frames) — the backdrop can generate late.</summary>
    private const int SkyScanIntervalFrames = 60;

    /// <summary>Absolute floor for the enclosing-dome extent test (world units), all three axes.</summary>
    private const float SkyMinEnclosingSize = 10f;

    /// <summary>Enclosing extent must also clear this fraction of the head far plane (scale-aware).</summary>
    private const float SkyEnclosingFarFraction = 0.1f;

    /// <summary>Name fragment that marks the game's fog-of-war family. The game uses it
    /// consistently across the whole kit: the hex shader `Amp_Basic_Unseen`
    /// (tools/ShaderOcclusionPatcher/README.md: "X-ray floor tiles"), the dedicated ground-plane
    /// shader `UnseenGroundPlane_Shd` (Player.log addressables list), and the Apparance object
    /// names ('EN_Unseen_…'). The ModBuild-57 log proves the shader half (every preview renderer
    /// it caught ran Amp_Basic_Unseen); hardware round 2 ("die Animation drumrum ist immer noch
    /// transparent") forced the round-3 widening from shader-name-only to GO/material/shader name
    /// (<see cref="HasUnseenName"/>) so the animated pieces of the kit match too.</summary>
    private const string UnseenShaderHint = "Unseen";

    /// <summary>Cap on per-renderer "underlay built" log lines per MR session. Apparance
    /// regenerates tile content constantly (226 rebuilds of the same two names in the ModBuild-57
    /// log); after the cap the change-gated count line still tracks the total.</summary>
    private const int UnseenVerboseLogCap = 12;

    /// <summary>Border margin (world units) around the matched unseen AABBs inside which the
    /// census (<see cref="CensusUnseenBorder"/>) looks for uncovered translucent renderers — the
    /// "Animation drumrum" plays at/just beyond the unseen region's edge.</summary>
    private const float CensusBorderWu = 1f;

    /// <summary>Census candidates named in full; beyond this only the count is reported.</summary>
    private const int CensusMaxListed = 20;

    /// <summary>Rate floor between census logs even when the candidate set keeps changing —
    /// Apparance regen would otherwise re-print it every sweep.</summary>
    private const float CensusMinIntervalSeconds = 30f;

    /// <summary>The dark the unseen geometry blends against in MR — outside MR the same geometry
    /// blends against the unrendered near-black void behind doors, so a dark neutral IS the
    /// authored background. Mirrors WorldUI.MrBacking's plate neutral family (keep in sync).</summary>
    private static readonly Color UnseenDark = new(0.12f, 0.11f, 0.10f, 1f);

    /// <summary>Key-avoidance lift (same rule and values as WorldUI.MrBacking): when the live key
    /// colour comes within keying distance of the dark neutral (only the BLACK preset does), the
    /// underlay brightens so no compositor threshold can key the backing away.</summary>
    private static readonly Color UnseenLift = new(0.34f, 0.30f, 0.25f, 1f);

    /// <summary>Per-channel distance below which the underlay counts as key-colored.</summary>
    private const float UnseenKeyDistance = 0.25f;

    /// <summary>Material/shader/name fragments that mark a renderer as sky/background.</summary>
    private static readonly string[] SkyNameHints =
    {
        "skydome", "skybox", "sky", "backdrop", "dome", "horizon",
        "cloud", "vista", "firmament", "background",
    };

    private static bool _active;        // MR currently applied to the scene
    private static bool _loggedActive;  // change-dedup for the on/off log
    private static Color _loggedColor;

    // ---- menu-visibility: the sky now STAYS, the MENU renders ON TOP -----------------------
    // A floated (MR-off) menu is uGUI that ZTests LEqual against the depth buffer, so the enclosing
    // scenario backdrop ('GH_SkySphere', shader 'AMP_SkyShader' — a squashed dome that writes depth
    // and exposes no '_ZWrite' to toggle) can occlude a movable menu dragged toward the shell edge.
    //
    // The previous lever here DISABLED that backdrop renderer outright while a menu floated. The user
    // found the vanishing sky very distracting, so that lever is GONE: the sky always stays rendered.
    // The occlusion is instead fixed where it belongs — the floated MODAL's uGUI graphics are switched
    // to ZTest Always (WorldUI.CanvasConversion 'renderOnTop'), so nothing can occlude them and the sky
    // is untouched. <see cref="KeepMenusUnclipped"/> is now a no-op kept only for its call sites.
    //
    // NOTE: the MR-ON path (<see cref="HideSkyGeometry"/> / <see cref="HideSkyMeshes"/>) is unrelated
    // and fully intact — MR still hides the backdrop mesh so the chroma key shows through.

    /// <summary>
    /// True while the MR readability treatment is wanted (WorldUI.MrBacking): the SAME want
    /// condition <see cref="Tick"/> keys the whole mode off, so backings appear/vanish with the
    /// one existing MR switch (user ruling: no second toggle). Reads the config WITHOUT forcing
    /// a Bind — before the rig has ticked once there is no VR session, hence no MR, hence false;
    /// binding stays owned by the rig path.
    /// </summary>
    internal static bool BackingsWanted =>
        _file != null && Enabled.Value && VRSession.IsRunning;

    /// <summary>
    /// No-op (kept for its ModalFallback call sites). The floated-menu-vs-sky occlusion is now fixed
    /// by rendering the MODAL on top (WorldUI.CanvasConversion 'renderOnTop'); the sky is never
    /// disabled for a menu any more, so there is nothing to do here.
    ///
    /// <para>KEEP — DO NOT DELETE (refactor Batch D, verified at HEAD: both call sites exist,
    /// <c>ModalFallback.cs</c> around :800 and :1135, and pass real arguments). An empty method
    /// looks like the obvious cleanup, but the comment block above it is the ONLY record of why
    /// the sky is no longer disabled for a floated menu — and that comment states that
    /// re-implementing this body IS the regression (b84817d: the user found the vanishing sky
    /// very distracting). Removing the method deletes the question along with the answer.</para>
    /// </summary>
    internal static void KeepMenusUnclipped(bool wanted) { }

    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("mixedreality");
        Enabled = _file.Bind("MixedReality", "Enabled", Defaults.MixedReality_Enabled,
            "Mixed-reality (chroma-key passthrough) mode. When ON the sky/background of the " +
            "whole game turns the flat solid KeyColor and every skybox is disabled, so Virtual " +
            "Desktop (or any compositor) can chroma-key that color and show the diorama/table " +
            "floating over your real room. The 3D geometry keeps rendering — only the sky becomes " +
            "the flat key color. While ON, the mod's floating UI (menus, captions, name tags) " +
            "additionally gets opaque backing plates so text stays readable over the passthrough " +
            "room. Restored fully (plates included) when turned off.");
        KeyColor = _file.Bind("MixedReality", "KeyColor", Defaults.KeyColor,
            "The solid chroma-key color the sky/background clears to in mixed-reality mode " +
            "(default pure green RGBA 0,1,0,1). The in-VR settings panel cycles the presets " +
            "green / magenta / blue; any RGBA is accepted here.");
        OpaquePreviewTiles = _file.Bind("MixedReality", "OpaquePreviewTiles", Defaults.OpaquePreviewTiles,
            "PART OF MIXED REALITY, not a choice beside it (like HideSkyMeshes; not offered in the " +
            "VR menu). ALL of the game's translucent 'unseen' fog-of-war geometry — the face-down " +
            "tile STACKS of not-yet-discovered rooms AND the unseen-area hexes that mark the " +
            "undiscovered area behind doors — blends with whatever is behind it; over the game's " +
            "dark void that reads fine, but in MR the chroma key / passthrough room shows through " +
            "and it all looks like green glass. While MR is on, the sweep finds those renderers " +
            "(the 'Unseen' shader family, plus anything translucent under a tile's active " +
            "'Preview' subtree) and slips an OPAQUE dark backing mesh UNDER each one — the " +
            "authored translucent material keeps rendering exactly as designed, look and " +
            "animation untouched, it just blends against dark instead of against your room. The " +
            "backings are destroyed when MR turns off — normal mode is never touched. Turn OFF " +
            "only if a run shows it darkening wanted geometry — the log names what it backed.");
        UnseenSkirtScale = _file.Bind("MixedReality", "UnseenSkirtScale", Defaults.UnseenSkirtScale,
            "Widening of each unseen piece's GROOVE-FILL copy relative to its geometry (1 = " +
            "exact silhouette). Every fog-of-war piece gets TWO dark backings in MR: an exact " +
            "copy directly behind its surfaces, and a lowered fill copy that plugs the beveled " +
            "channels BETWEEN neighboring hexes — this factor widens only that fill so " +
            "neighboring fills overlap under the groove line. Applies while MR is on, live " +
            "(backings rebuild on change). Raise if grooves between hexes still glow; lower if " +
            "dark peeks out past the outermost hex edges. Clamped to 1..2.");
        UnseenFillDrop = _file.Bind("MixedReality", "UnseenFillDrop", Defaults.UnseenFillDrop,
            "How far (world units) each unseen piece's groove-fill copy sits BELOW its authored " +
            "pose in MR. The fill's surfaces must lie under the beveled V-channels between " +
            "neighboring hexes so looking into a groove lands on dark instead of the " +
            "passthrough room. Applies while MR is on, live (backings rebuild on change). Raise " +
            "if deep grooves still glow green; lower if dark peeks out below the outer rim " +
            "pieces. Clamped to 0..2.");
        HideSkyMeshes = _file.Bind("MixedReality", "HideSkyMeshes", Defaults.HideSkyMeshes,
            "PART OF MIXED REALITY, not a choice beside it — turning MR on does this, and the key "
            + "is kept only as an escape hatch for a run where it hides wanted geometry. It is not "
            + "offered in the VR menu, because half of MR is not a thing to switch off: keying the "
            + "camera clear without it leaves the backdrop drawn over the key colour, so MR simply "
            + "would not work. Disables the sky/background GEOMETRY while MR is on. The scenario backdrop is " +
            "an opaque mesh (not the skybox), so keying the camera clear alone leaves it drawn " +
            "over the key color; MR sweeps the renderers and disables the ones that draw the " +
            "surrounding sky (sky-ish name/material, or bounds enclosing the head on all axes — " +
            "never the diorama tiles/props), restoring them when MR turns off. Turn OFF only if " +
            "a run shows it hiding wanted geometry — the log names every renderer it disabled.");
    }

    /// <summary>Name of the current key color if it matches a preset, else "Custom".</summary>
    internal static string KeyColorName
    {
        get
        {
            Color c = KeyColor.Value;
            foreach ((string name, Color color) in Presets)
            {
                if (Approximately(color, c))
                    return name;
            }
            return "Custom";
        }
    }

    /// <summary>Advance the key color to the next preset (settings-panel swatch cycle).</summary>
    internal static void CycleKeyColor()
    {
        int index = 0;
        Color c = KeyColor.Value;
        for (int i = 0; i < Presets.Length; i++)
        {
            if (Approximately(Presets[i].Color, c))
            {
                index = i + 1;
                break;
            }
        }
        KeyColor.Value = Presets[index % Presets.Length].Color; // BepInEx persists on set
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f &&
        Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    /// <summary>
    /// Per-frame driver (called from <c>VRRigDriver.Update</c> AFTER TickHeadClearColor and
    /// the camera-policy sweep). Applies the key-color clears while MR is wanted, restores
    /// fully otherwise.
    /// </summary>
    internal static void Tick()
    {
        Bind();
        bool want = Enabled.Value && VRSession.IsRunning;
        if (!want)
        {
            if (_active)
                RestoreAll();
            // MR OFF: the sky STAYS. It is made a pure NON-OCCLUDING backdrop by SkyBackdrop
            // (ZWrite-off, or — when the shader hard-codes ZWrite On — the sky is left drawing
            // its own colour at Background and a real mod-layer depth-reset renderer at queue 1001
            // overwrites depth to ~far after it, an ordinary tiled-GPU-safe draw with NO renderer
            // suppression and NO mid-pass depth clear) so floated menus, the moved board and the
            // laser in front of it are never clipped — the fix lives on the SPHERE side, not on
            // the menus (WorldUI.CanvasConversion no longer forces menus on top).
            SkyBackdrop.Tick(mrHidingSky: false);
            return;
        }

        // MR ON: SkyBackdrop stands down so MR's HideSkyMeshes owns the sphere for the chroma key
        // (restores the renderer/material first, so HideSkyGeometry disables a clean renderer).
        SkyBackdrop.Tick(mrHidingSky: true);

        Color key = KeyColor.Value;
        key.a = 1f; // the sky clear must be fully opaque for a clean chroma key

        // 1) Kill the global skybox (ambient/reflection contributions + any Skybox clear).
        if (!_skyboxSaved)
        {
            _savedSkybox = RenderSettings.skybox;
            _skyboxSaved = true;
        }
        if (RenderSettings.skybox != null)
            RenderSettings.skybox = null;

        // 2) HeadCamera — the only camera that reaches the HMD (VRCameraPolicy). MR owns its
        //    clear while ON; runs after TickHeadClearColor so the key color wins the frame.
        Camera? head = VRCameraPolicy.AllowedHead;
        if (head != null)
        {
            Record(head);
            ForceSolid(head, key);
        }

        // 3) Every OTHER live camera still sky-clearing → SolidColor(key) (desktop mirror +
        //    any secondary camera). FlatScreen-managed cameras are already non-Skybox, so we
        //    never collide with its RT clear policy.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam == head || cam.clearFlags != CameraClearFlags.Skybox)
                continue;
            Record(cam);
            ForceSolid(cam, key);
        }

        // 4) Hide the sky/background GEOMETRY (item 2). The scenario backdrop is an opaque mesh
        //    the HeadCamera renders (mask 0xFFFFFFFF) — a SolidColor clear draws BEHIND it, so
        //    the key color never shows until the mesh itself is disabled.
        HideSkyGeometry();

        // 5) Back ALL translucent "unseen" fog-of-war geometry — the preview tile stacks AND the
        //    unseen-area hexes inside revealed tiles — with opaque dark underlays (user rulings
        //    2026-08-04 + 2026-08-05). Their translucent materials blend with whatever is behind
        //    them — over the key colour that mix lands inside the compositor's similarity window,
        //    so the real room shows through. Mod-owned child meshes only, the authored materials
        //    are never touched, everything destroyed on MR off; normal mode is bit-identical
        //    because none of this runs while MR is off.
        ForceUnseenOpaque();

        _active = true;
        if (!_loggedActive || _loggedColor != key)
        {
            _loggedActive = true;
            _loggedColor = key;
            VRLog.Info("Core", $"Mixed reality ON — skybox disabled, sky/background keyed to " +
                               $"{KeyColorName} (RGBA {key.r:0.##},{key.g:0.##},{key.b:0.##},{key.a:0.##}); " +
                               $"diorama geometry stays visible.");
        }
    }

    // ---- sky/background geometry (item 2) -------------------------------------------------------

    /// <summary>
    /// Throttled sweep of the live renderers: disable the ones that draw the surrounding
    /// sky/background (sky-ish name/material/shader, OR world bounds that enclose the head with
    /// a large extent on all three axes — a dome/backdrop, never a floor tile or a table prop).
    /// Recorded for restore; each hidden renderer is logged. When nothing matched, the biggest
    /// enclosing candidates are dumped once so the next hardware run can name the sky source.
    /// </summary>
    private static void HideSkyGeometry()
    {
        if (!HideSkyMeshes.Value)
        {
            // Live safety valve: restore anything we already hid when the toggle flips off.
            if (HiddenSky.Count > 0)
                RestoreSky();
            return;
        }
        if (Time.frameCount < _skyScanNextFrame)
            return;
        _skyScanNextFrame = Time.frameCount + SkyScanIntervalFrames;

        Camera? head = VRCameraPolicy.AllowedHead;
        if (head == null)
            return;
        Vector3 headPos = head.transform.position;
        float sizeFloor = Mathf.Max(SkyMinEnclosingSize, head.farClipPlane * SkyEnclosingFarFraction);

        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>(); // active renderers only
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5) // never our own mod visuals / UI hosts
                continue;
            if (IsHiddenSky(r) || !IsSkyRenderer(r, headPos, sizeFloor))
                continue;

            HiddenSky.Add(r);
            r.enabled = false;
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR: disabled sky/background renderer '{r.gameObject.name}' " +
                               $"(layer {LayerName(layer)}, bounds size {b.size} @ {b.center}, " +
                               $"shader '{ShaderName(r)}') — drew over the key color as geometry.");
        }

        if (HiddenSky.Count != _loggedSkyCount)
        {
            _loggedSkyCount = HiddenSky.Count;
            VRLog.Info("Core", $"MR: {HiddenSky.Count} sky/background renderer(s) hidden — " +
                               $"the {KeyColorName} key now shows behind the diorama.");
        }

        if (HiddenSky.Count == 0 && !_skyDiagLogged)
        {
            _skyDiagLogged = true;
            LogSkyCandidates(all, headPos);
        }
    }

    private static bool IsSkyRenderer(Renderer r, Vector3 headPos, float sizeFloor)
    {
        if (NameLooksLikeSky(r))
            return true;
        // Enclosing-dome signal: the world bounds SURROUND the head with a large extent on ALL
        // THREE axes. A flat floor/tile has one thin axis; a table prop does not contain the
        // head — only a skydome/sphere/backdrop box passes.
        Bounds b = r.bounds;
        if (!b.Contains(headPos))
            return false;
        Vector3 s = b.size;
        return s.x > sizeFloor && s.y > sizeFloor && s.z > sizeFloor;
    }

    private static bool NameLooksLikeSky(Renderer r)
    {
        if (NameHasHint(r.gameObject.name))
            return true;
        Material? m = r.sharedMaterial;
        if (m == null)
            return false;
        return NameHasHint(m.name) || (m.shader != null && NameHasHint(m.shader.name));
    }

    private static bool NameHasHint(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        for (int i = 0; i < SkyNameHints.Length; i++)
        {
            if (s!.IndexOf(SkyNameHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsHiddenSky(Renderer r)
    {
        for (int i = 0; i < HiddenSky.Count; i++)
        {
            if (ReferenceEquals(HiddenSky[i], r))
                return true;
        }
        return false;
    }

    /// <summary>
    /// One-shot diagnostic: the sky is generated geometry with an unknown name, so when the
    /// heuristics hide nothing, dump the largest renderers that enclose the head — the tester
    /// reads the real sky source's name/layer straight off this list.
    /// </summary>
    private static void LogSkyCandidates(Renderer[] all, Vector3 headPos)
    {
        RendererScratch.Clear();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5)
                continue;
            if (r.bounds.Contains(headPos))
                RendererScratch.Add(r);
        }
        // Largest-first (bounds volume): a simple selection is fine for a one-shot dump.
        RendererScratch.Sort((a, b) => BoundsVolume(b.bounds).CompareTo(BoundsVolume(a.bounds)));
        int count = Mathf.Min(8, RendererScratch.Count);
        VRLog.Info("Core", $"MR: no sky renderer matched the heuristics — {RendererScratch.Count} " +
                           $"renderer(s) enclose the head; largest {count} candidate(s) (name the sky source here):");
        for (int i = 0; i < count; i++)
        {
            Renderer r = RendererScratch[i];
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR:   candidate '{r.gameObject.name}' " +
                               $"(layer {LayerName(r.gameObject.layer)}, size {b.size}, " +
                               $"shader '{ShaderName(r)}').");
        }
        RendererScratch.Clear();
    }

    private static float BoundsVolume(Bounds b) => b.size.x * b.size.y * b.size.z;

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? layer.ToString() : $"{name}/{layer}";
    }

    private static string ShaderName(Renderer r)
    {
        Material? m = r.sharedMaterial;
        return m != null && m.shader != null ? m.shader.name : "<none>";
    }

    // ---- "unseen" fog-of-war geometry (MR opaque underlays) -------------------------------------

    /// <summary>
    /// Throttled sweep: find ALL translucent "unseen" fog-of-war renderers — the face-down TILE
    /// STACKS of not-yet-revealed rooms and the unseen-AREA hexes inside revealed tiles — and slip
    /// an OPAQUE DARK UNDERLAY under each while MR is on.
    ///
    /// HOW THE GEOMETRY IS IDENTIFIED (read from the ModBuild-57 log + decompiled source): the
    /// game draws both classes with the <c>Amp_Basic_Unseen</c> shader family
    /// (<see cref="UnseenShaderHint"/>). The previous round matched only "renderer under an ACTIVE
    /// 'Preview' ancestor" (<c>ProceduralMapTile.ShowContent</c>, decompiled
    /// ProceduralMapTile.cs:148) — correct for the stand-in stacks, but the unseen-area hexes
    /// behind doors are part of a REVEALED tile's generated content (log: the glassy hexes sit in
    /// MAPTILE 'E', vis=All, while the Preview-vis tiles report 0 renderers — their content is not
    /// even generated), so the sweep never saw them and they stayed green glass
    /// (.planning/debug/keine_ausblendung.png, bottom). The match is now the UNION of both
    /// signals: shader-family membership OR an active 'Preview' ancestor — one mechanism for the
    /// whole see-through class, and the Preview signal keeps covering any translucent stack
    /// renderer that might not run the family shader.
    ///
    /// ROUND 3 (hardware 2026-08-05: the hexes read right, but "die ANIMATION DRUMRUM ist immer
    /// noch transparent"). All 226 family hexes carried full-slot underlays, so the still-open
    /// border animation is either a DIFFERENT renderer both round-2 signals miss, or an animated
    /// pass overhanging its own static underlay silhouette. Three changes: (a) the family signal
    /// widened from shader-name-only to GO/material/shader name — the game names the whole kit
    /// 'Unseen', including the dedicated animated ground-plane shader 'UnseenGroundPlane_Shd'
    /// the Player.log addressables list ships and round 2 could miss (its material can fail the
    /// blend probe; family slots therefore now earn the dark plate on the NAME too, see
    /// <see cref="BuildUnseenUnderlay"/>); (b) family PARTICLES/trails are recognised but never
    /// material-touched (standing instruction) — they compose additively/blended over the
    /// now-dark region wherever a backed mesh is behind them; (c) the census
    /// (<see cref="CensusUnseenBorder"/>) prints every uncovered translucent renderer near the
    /// region so the next hardware log names the animation definitively instead of the mod
    /// guessing a fourth time.
    ///
    /// ROUND 4 (hardware 2026-08-05 #2, ModBuild 59): the census ANSWERED — all 19 candidates
    /// near the region were ambient FX (torch sparks, character idle-FX systems with sloppy
    /// AABBs, the waypoint path), none the border animation, and round 3's name-widening matched
    /// nothing new (count stayed 226). By elimination the still-transparent "Animation drumrum"
    /// is the matched family's OWN animated pass reaching past its rest-pose mesh — beyond the
    /// 1:1 underlay's static silhouette. Fix: THE SKIRT — every underlay is scaled up about its
    /// bounds center (<see cref="UnseenSkirtScale"/>; the default is INFERRED, not asset-derived
    /// — see <see cref="DumpUnseenShaderProperties"/>, the instrument that lets the next log
    /// replace the guess) so the moving fringe always lands on dark. The census stays as the
    /// regression instrument: a future transparent border WITH an empty census means the margin
    /// is short, not a renderer missed — its empty-set message says exactly that.
    ///
    /// ROUND 5 (hardware 2026-08-05 #3, ModBuild 61): the shader-property dump ANSWERED the
    /// margin question — 'Unseen_Floor_Hex_Mat' animates by UV-SCROLL (_UV_Offset/_UVTiling/
    /// _WorldSpace_tiling, no displacement property), so the pattern can never leave its mesh
    /// silhouette and the round-4 skirt was aimed at a failure mode that does not exist
    /// (harmless, kept — it still widens the backing under each piece). The census again listed
    /// only ambient FX, and no 'UnseenGroundPlane_Shd' material was ever family-matched (the
    /// dump would have fired for it), so by elimination the remaining "green glass BETWEEN the
    /// hexagons" is family geometry scrolling its pattern over the GROUT GAPS between pieces,
    /// where no piece — and therefore no per-piece underlay — has anything dark behind the
    /// blend. Fix at the time: per-piece rectangular REGION BASE quads under each piece's AABB —
    /// REMOVED again in round 7 (user ruling, see below); the round-7 groove FILL is their
    /// hex-silhouette successor.
    ///
    /// ROUND 6 (hardware 2026-08-05 #4, ModBuild 62): the base quads sealed the region from
    /// BELOW but not from ABOVE ("falsch rum — es soll von BEIDEN Seiten dicht sein"). Two
    /// candidate mechanisms were closed blind (twin back-to-back quads against cull-back
    /// shaders; the adaptive backing queue in <see cref="EnsureUnseenMaterials"/> against a
    /// family depth-write at/below the backings' queue) plus the render-state dump to settle
    /// them. The ModBuild-63 log then settled BOTH as non-causes: the dark material really is
    /// 'Sprites/Default' (Cull Off) and the family queue really is 3000 (all pass state
    /// hardcoded; RenderType tag 'Overlay') — the backings draw first and cull nothing. The
    /// adaptive queue + dump stay (cheap, and they are the proof for the next anomaly); the
    /// quads themselves are gone (round 7).
    ///
    /// ROUND 7 (hardware 2026-08-06, ModBuild 63, screenshot mixed_reality_transparenz.png):
    /// two user rulings. (1) The rectangular base plate is VISIBLE at the region rim as an
    /// alien dark slab — removed entirely, twins included. (2) The hex TOPS read correctly
    /// dark, but the beveled V-CHANNELS between neighboring hexes glow bright green. Root
    /// cause READ FROM THE CODE against the screenshot: round 4 did not ADD a skirt copy, it
    /// SCALED THE ONLY dark copy — and a copy displaced about the mesh center no longer sits
    /// coplanar behind the piece's beveled groove faces, so a grazing ray slips through the
    /// parallax gap between the translucent bevel and its shifted backing, into the channel,
    /// onto the key (round 5 had already proven the scale bought nothing: UV-scroll cannot
    /// leave the silhouette). Fix: the PRIMARY underlay is exact 1:1 again — every surface
    /// backed coplanar from every direction — and each piece additionally gets the GROOVE
    /// FILL, a second same-mesh copy, XZ-widened by <see cref="UnseenSkirtScale"/> and dropped
    /// <see cref="UnseenFillDrop"/> wu straight down, whose hex-shaped top surfaces lie under
    /// the groove floors; neighboring fills overlap under the groove line, so the union
    /// follows the hex silhouettes everywhere — nothing rectangular from any angle, nothing
    /// past the outer hex edges except the long-accepted thin rim.
    ///
    /// WHY AN UNDERLAY AND NOT FORCED-OPAQUE MATERIAL COPIES (the previous mechanism, replaced
    /// here): forcing Blend One/Zero on a copy rewires the shader's own output — the animated
    /// alpha pattern that gives the unseen hexes their pulsing look suddenly reads as
    /// full-intensity texture, a different look from the authored one. The underlay changes
    /// NOTHING about the authored rendering: the original renderer keeps its original materials,
    /// passes and animation, and merely blends against a mod-owned opaque dark mesh (same mesh,
    /// same transform, drawn at the end of the opaque range) instead of against the chroma key.
    /// Outside MR the same geometry blends against the unrendered near-black void, so dark IS the
    /// authored background — same look, same animation, no see-through. The underlay is a CHILD
    /// of its source renderer: Apparance's constant tile regeneration (226 rebuilds in the
    /// ModBuild-57 session) destroys and re-creates sources at will, and a child dies with its
    /// parent — teardown is structural. Per-slot: translucent slots get the dark plate, opaque
    /// slots get a draws-nothing filler (their own submesh already occludes; a coplanar dark copy
    /// would z-fight it). Renderers only — Lights are never touched (standing MR constraint), no
    /// game material is ever written, no <c>_Cull</c> is changed anywhere.
    ///
    /// Throttled on the sky sweep's cadence (the cheap prune/enabled-sync in
    /// <see cref="SyncUnseenUnderlays"/> runs every tick); config safety valve:
    /// <see cref="OpaquePreviewTiles"/> (default on, name kept from the preview-only round),
    /// restoring live when flipped off.
    /// </summary>
    private static void ForceUnseenOpaque()
    {
        if (!OpaquePreviewTiles.Value)
        {
            if (UnseenUnderlays.Count > 0)
                RestoreUnseenUnderlays();
            return;
        }

        EnsureUnseenMaterials();
        SyncUnseenUnderlays();

        // Live fill tuning (rounds 4+7): a changed [MixedReality] UnseenSkirtScale or
        // UnseenFillDrop tears every underlay down and falls through to an immediate resweep,
        // so a hardware round can dial the groove fill in without a rebuild or an MR toggle.
        // Restore resets the scan throttle.
        float skirt = Mathf.Clamp(UnseenSkirtScale.Value, 1f, 2f);
        float drop = Mathf.Clamp(UnseenFillDrop.Value, 0f, 2f);
        if (_appliedSkirtScale > 0f && UnseenUnderlays.Count > 0
            && (!Mathf.Approximately(skirt, _appliedSkirtScale)
                || !Mathf.Approximately(drop, _appliedFillDrop)))
        {
            VRLog.Info("Core", $"MR: unseen fill tuning changed (scale {_appliedSkirtScale:0.###} → " +
                               $"{skirt:0.###}, drop {_appliedFillDrop:0.###} → {drop:0.###} wu) — " +
                               "rebuilding every underlay + fill.");
            RestoreUnseenUnderlays();
        }
        _appliedSkirtScale = skirt;
        _appliedFillDrop = drop;

        if (Time.frameCount < _previewScanNextFrame)
            return;
        _previewScanNextFrame = Time.frameCount + SkyScanIntervalFrames;

        int unseenRenderers = 0;
        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>(); // active renderers only
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5) // never our own visuals / UI hosts
                continue;
            // Never match a mod-owned object — an underlay under a 'Preview' node would otherwise
            // match the Preview signal and grow an underlay of its own.
            if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;
            // ROUND 3 family signal (hardware round 2: "die Animation drumrum ist immer noch
            // transparent"): the game names its whole fog-of-war kit 'Unseen' — the object names
            // ('EN_Unseen_…'), the hex shader ('Amp_Basic_Unseen') AND a dedicated ground-plane
            // shader the Player.log addressables list ships as 'UnseenGroundPlane_Shd'. Round 2
            // read only the SHADER name, so an unseen-family mesh running a differently-named
            // animated shader — or the ground plane if its material fails the blend probe below —
            // stayed uncovered. Any of GO name / material name / shader name now counts.
            bool family = HasUnseenName(r.gameObject.name);
            bool anyTranslucent = false;
            for (int mIdx = 0; mIdx < mats.Length; mIdx++)
            {
                Material? m = mats[mIdx];
                if (m == null)
                    continue;
                if (IsUnseenFamilyMaterial(m))
                {
                    family = true;
                    if (m!.renderQueue < _familyMinQueue)
                        _familyMinQueue = m.renderQueue; // round 6: EnsureUnseenMaterials adapts
                }
                if (IsTranslucent(m))
                    anyTranslucent = true;
            }
            if (!family && !(anyTranslucent && UnderPreviewNode(r.transform)))
                continue;
            unseenRenderers++;
            if (UnseenSources.Contains(r.GetInstanceID()))
                continue;

            // Only a mesh can carry a same-mesh underlay. A family PARTICLE/TRAIL system is left
            // authored (standing instruction: never touch particle materials) — the census below
            // names it so the region backing can be verified/extended against the next log.
            if (r is MeshRenderer mesh)
                BuildUnseenUnderlay(mesh, mats);
        }

        if (UnseenUnderlays.Count != _loggedPreviewCount)
        {
            _loggedPreviewCount = UnseenUnderlays.Count;
            VRLog.Info("Core", $"MR: {UnseenUnderlays.Count} unseen-geometry renderer(s) carry an " +
                               "opaque dark underlay + region base quad (authored materials " +
                               "untouched; everything destroyed when MR turns off).");
        }

        // One-shot diagnostic for the next hardware run: unseen renderers exist but NONE was
        // translucent by the material test — then the see-through look has another mechanism
        // (per-vertex alpha, a dither keyword, …) and this dump names the shaders to chase.
        if (unseenRenderers > 0 && UnseenUnderlays.Count == 0 && !_previewDiagLogged)
        {
            _previewDiagLogged = true;
            VRLog.Info("Core", $"MR: {unseenRenderers} unseen-geometry renderer(s) found but none " +
                               "matched the translucency test — dumping their material state:");
            int dumped = 0;
            for (int i = 0; i < all.Length && dumped < 8; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || !UnderPreviewNode(r.transform))
                    continue;
                Material? m = r.sharedMaterial;
                VRLog.Info("Core", $"MR:   unseen '{r.gameObject.name}' shader " +
                                   $"'{ShaderName(r)}' queue {(m != null ? m.renderQueue : -1)}.");
                dumped++;
            }
        }

        CensusUnseenBorder(all);
    }

    /// <summary>
    /// THE identification instrument for the remaining "Animation drumrum" transparency (hardware
    /// round 2): every translucent/additive/particle renderer whose AABB intersects the unseen
    /// region (union of the matched sources' AABBs, ±<see cref="CensusBorderWu"/> wu) and carries
    /// NO dark backing is listed by name, kind, shader, queue, slot count and bounds. The border
    /// animation the user still sees through MUST be in this list — or the list is empty, and the
    /// transparency then comes from the matched family itself (a vertex-animated pass overhanging
    /// its static underlay silhouette), which is the other hypothesis this census exists to tell
    /// apart. Change-gated on the candidate set (plus a rate floor) so a steady scene logs once
    /// per MR session; reset with the underlays.
    /// </summary>
    private static void CensusUnseenBorder(Renderer[] all)
    {
        if (UnseenUnderlays.Count == 0)
            return;

        // Union AABB of the matched unseen sources (coarse gate), plus the per-source list for
        // the fine test — both expanded by the border margin.
        CensusBoundsScratch.Clear();
        Bounds union = default;
        bool first = true;
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            Renderer src = UnseenUnderlays[i].Source;
            if (src == null)
                continue;
            Bounds b = src.bounds;
            b.Expand(CensusBorderWu * 2f);
            CensusBoundsScratch.Add(b);
            if (first)
            {
                union = b;
                first = false;
            }
            else
            {
                union.Encapsulate(b);
            }
        }
        if (first)
            return;

        CensusScratch.Clear();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5)
                continue;
            if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;
            if (UnseenSources.Contains(r.GetInstanceID()))
                continue; // already carries a dark backing
            Bounds rb = r.bounds;
            if (!union.Intersects(rb))
                continue;

            // "Could show the passthrough through itself": particles/trails/lines always qualify
            // (their material state is opaque to the blend probe), meshes qualify when any slot
            // is translucent or family-named.
            bool interesting = r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer;
            if (!interesting)
            {
                Material[] mats = r.sharedMaterials;
                if (mats != null)
                {
                    for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                    {
                        if (IsTranslucent(mats[mIdx]) || IsUnseenFamilyMaterial(mats[mIdx]))
                        {
                            interesting = true;
                            break;
                        }
                    }
                }
            }
            if (!interesting)
                continue;

            for (int bIdx = 0; bIdx < CensusBoundsScratch.Count; bIdx++)
            {
                if (CensusBoundsScratch[bIdx].Intersects(rb))
                {
                    CensusScratch.Add(r);
                    break;
                }
            }
        }

        int hash = 17;
        for (int i = 0; i < CensusScratch.Count; i++)
            hash = hash * 31 + CensusScratch[i].GetInstanceID();
        float now = Time.unscaledTime;
        if (hash == _censusLastHash || now < _censusNextAllowed)
        {
            CensusScratch.Clear();
            return;
        }
        _censusLastHash = hash;
        _censusNextAllowed = now + CensusMinIntervalSeconds;

        if (CensusScratch.Count == 0)
        {
            VRLog.Info("Core", "MR: UNSEEN-BORDER CENSUS — no uncovered translucent/additive " +
                               "renderer intersects the unseen region (±1 wu). If an animation " +
                               "still reads transparent there, it comes from the MATCHED family " +
                               "itself — with the skirt applied (round 4) that means the margin " +
                               "is SHORT, not a renderer missed: raise [MixedReality] " +
                               $"UnseenSkirtScale (currently {_appliedSkirtScale:0.###}).");
            return;
        }

        VRLog.Info("Core", $"MR: UNSEEN-BORDER CENSUS — {CensusScratch.Count} translucent/additive " +
                           $"renderer(s) intersect the unseen region (±{CensusBorderWu:0.#} wu, " +
                           $"union center {union.center}, size {union.size}) and carry NO dark " +
                           "backing; the still-transparent border animation must be among these:");
        int listed = Mathf.Min(CensusScratch.Count, CensusMaxListed);
        for (int i = 0; i < listed; i++)
        {
            Renderer r = CensusScratch[i];
            string kind = r switch
            {
                ParticleSystemRenderer => "particles",
                TrailRenderer => "trail",
                LineRenderer => "line",
                SkinnedMeshRenderer => "skinned",
                MeshRenderer => "mesh",
                _ => r.GetType().Name,
            };
            Material? m = r.sharedMaterial;
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR:   census '{r.gameObject.name}' [{kind}] shader " +
                               $"'{ShaderName(r)}' queue {(m != null ? m.renderQueue : -1)} " +
                               $"slot(s) {(r.sharedMaterials != null ? r.sharedMaterials.Length : 0)} " +
                               $"size {b.size} @ {b.center}.");
        }
        if (CensusScratch.Count > listed)
            VRLog.Info("Core", $"MR:   census … +{CensusScratch.Count - listed} more.");
        CensusScratch.Clear();
    }

    /// <summary>
    /// Build the opaque dark underlay for one matched source renderer: a mod-owned CHILD sharing
    /// the source's mesh and full transform, dark plate material on the translucent slots,
    /// draws-nothing filler on the rest (see <see cref="ForceUnseenOpaque"/> for why). Skipped
    /// silently when the source has no MeshFilter mesh to clone.
    /// </summary>
    private static void BuildUnseenUnderlay(MeshRenderer source, Material[] mats)
    {
        MeshFilter? filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return;

        // Per-slot dark rule (round 3): a slot earns the dark plate when the blend probe reads it
        // translucent OR when it is family-named — the family IS the see-through class by the
        // game's own naming, and a family shader that hardcodes its blend in the pass (no
        // '_DstBlend' property, queue ≤ 2500) is invisible to the probe. A dark plate under a
        // slot that turns out genuinely opaque is covered by that slot's own later draw (family
        // materials render at ≥ our 2500) — harmless; a SKIPPED see-through slot is the reported
        // bug. Slots that are neither stay on the draws-nothing filler (their opaque submesh
        // already occludes; a coplanar dark copy would z-fight it).
        var plateMats = new Material[mats.Length];
        int backed = 0;
        for (int i = 0; i < mats.Length; i++)
        {
            bool dark = IsTranslucent(mats[i]) || IsUnseenFamilyMaterial(mats[i]);
            plateMats[i] = dark ? _unseenDarkMat! : _unseenSkipMat!;
            if (dark)
                backed++;
        }
        if (backed == 0)
            return; // GO-name family with all-opaque, non-family slots: nothing to back

        // THE PRIMARY UNDERLAY — exact 1:1 again (round 7). Round 4 scaled THIS copy as the
        // "skirt"; round 5 proved the animation is UV-scroll (cannot leave the silhouette, so
        // the margin bought nothing), and the round-7 screenshot exposed what it cost: a copy
        // displaced about the mesh center no longer sits coplanar behind the beveled groove
        // faces, so a grazing ray slips through the parallax gap between the translucent bevel
        // and its shifted backing, into the V-channel, onto the key — the glowing green grooves.
        // Coplanar means every surface pixel of the piece is backed from EVERY view direction.
        var go = new GameObject("GloomhavenVR.MrUnseenUnderlay");
        go.transform.SetParent(source.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = source.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var plate = go.AddComponent<MeshRenderer>();
        plate.sharedMaterials = plateMats;
        plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        plate.receiveShadows = false;
        plate.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        plate.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // THE GROOVE FILL (round 7, replaces the round-5/6 rectangular base quads the user
        // rejected on sight — "diese viereckige Platte unten, entferne die wieder"). The beveled
        // V-channels BETWEEN neighboring hexes have no geometry of their own: the coplanar
        // underlay backs the pieces' surfaces, but a ray INTO a channel passes between pieces and
        // lands on the key. The fill is a second same-mesh copy, XZ-widened by the (tunable)
        // skirt factor about the mesh-local bounds center and DROPPED straight down in WORLD
        // space by UnseenFillDrop — its hex-shaped top surfaces lie under the groove floors, and
        // neighboring fills overlap under the groove line, so the union follows the hex
        // silhouettes everywhere: nothing can read as a rectangle from any angle, and nothing
        // reaches past the outer hex edges except the accepted thin rim. ZTest LEqual, no depth
        // write — revealed geometry occludes the fill exactly like the plate.
        float skirt = _appliedSkirtScale > 0f ? _appliedSkirtScale : 1f;
        float drop = _appliedFillDrop >= 0f ? _appliedFillDrop : Defaults.UnseenFillDrop;
        Vector3 meshCenter = filter.sharedMesh.bounds.center;
        var fillGo = new GameObject("GloomhavenVR.MrUnseenFill");
        fillGo.transform.SetParent(source.transform, worldPositionStays: false);
        fillGo.transform.localRotation = Quaternion.identity;
        fillGo.transform.localScale = new Vector3(skirt, 1f, skirt);
        fillGo.transform.localPosition = new Vector3(
            meshCenter.x * (1f - skirt), 0f, meshCenter.z * (1f - skirt)); // XZ center fixed
        fillGo.transform.position += Vector3.down * drop; // WORLD drop, whatever the parent pose
        fillGo.layer = source.gameObject.layer;
        fillGo.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var fill = fillGo.AddComponent<MeshRenderer>();
        fill.sharedMaterials = plateMats;
        fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fill.receiveShadows = false;
        fill.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        fill.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        int id = source.GetInstanceID();
        UnseenUnderlays.Add(new UnseenUnderlay
        {
            Source = source,
            SourceId = id,
            Plate = plate,
            Fill = fill,
        });
        UnseenSources.Add(id);
        for (int i = 0; i < mats.Length; i++)
        {
            if (IsUnseenFamilyMaterial(mats[i]))
                DumpUnseenShaderProperties(mats[i]!);
        }
        if (_unseenVerboseLogs < UnseenVerboseLogCap)
        {
            _unseenVerboseLogs++;
            VRLog.Info("Core", $"MR: unseen geometry '{source.gameObject.name}' backed by an " +
                               $"opaque dark underlay ({backed} of {mats.Length} slot(s), shader " +
                               $"'{ShaderName(source)}') — the fog-of-war look stays authored, " +
                               "the passthrough room can no longer show through it." +
                               (_unseenVerboseLogs == UnseenVerboseLogCap
                                   ? " (Further builds counted, not listed — tile regen churn.)"
                                   : string.Empty));
        }
    }

    /// <summary>
    /// Margin-derivation instrument (once per shader name per session): the skirt's default
    /// factor is INFERRED — the game bundles are not readable offline (ressources/ carries only
    /// Managed DLLs, tools/ShaderDisasm has no Unseen entry), so the authored wave amplitude of
    /// the family shaders is unknown. This dump prints every float/range/vector/color property
    /// of a matched family material with its LIVE value into the hardware log; if an
    /// amplitude/displacement property shows up there, the next round replaces the guessed
    /// <see cref="UnseenSkirtScale"/> default with a value read from the asset.
    /// </summary>
    private static void DumpUnseenShaderProperties(Material m)
    {
        if (m.shader == null || DumpedUnseenShaders.Count >= 4 || !DumpedUnseenShaders.Add(m.shader.name))
            return;

        var sb = new System.Text.StringBuilder(256);
        int count = m.shader.GetPropertyCount();
        int listed = 0;
        for (int i = 0; i < count && listed < 24; i++)
        {
            string name = m.shader.GetPropertyName(i);
            UnityEngine.Rendering.ShaderPropertyType type = m.shader.GetPropertyType(i);
            if (!m.HasProperty(name))
                continue;
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    sb.Append(' ').Append(name).Append('=').Append(m.GetFloat(name).ToString("0.###"));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    Vector4 v = m.GetVector(name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append($"({v.x:0.###},{v.y:0.###},{v.z:0.###},{v.w:0.###})");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Color c = m.GetColor(name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append($"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})");
                    break;
                default:
                    continue; // textures/ints carry no wave amplitude
            }
            listed++;
        }

        // Round 6: the render-state half — the from-above/from-below asymmetry hypotheses hinge
        // on the family's queue and depth/blend state, so pin them. A state name that is not a
        // material property is hardcoded in the compiled pass (the serialized pass state for
        // Amp_Basic_Unseen — zTest LEqual, zWrite On/Off across its two FORWARD passes — is in
        // tools/ShaderOcclusionPatcher/README.md, read from the shipped bundles).
        var state = new System.Text.StringBuilder(96);
        state.Append(" queue=").Append(m.renderQueue)
             .Append(" renderTypeTag='").Append(m.GetTag("RenderType", false, "<none>")).Append('\'');
        string[] stateProps = { "_ZWrite", "_ZTest", "_SrcBlend", "_DstBlend", "_Cull" };
        for (int i = 0; i < stateProps.Length; i++)
        {
            state.Append(' ').Append(stateProps[i]).Append('=');
            if (m.HasProperty(stateProps[i]))
                state.Append(m.GetFloat(stateProps[i]).ToString("0.#"));
            else
                state.Append("hardcoded");
        }

        VRLog.Info("Core", $"MR: UNSEEN-SHADER PROPERTIES '{m.shader.name}' (material '{m.name}', " +
                           $"{count} propert(ies)):{state} |{sb} — this line pins the family's " +
                           "queue/depth/blend behaviour AND names the animation mechanism " +
                           "(round 5 read UV-scroll off it).");
    }

    /// <summary>
    /// Per-tick bookkeeping for the underlays: drop entries whose source died (the underlay child
    /// died with it — Apparance regen; the next throttled sweep re-backs the replacements) and
    /// mirror the source's <c>enabled</c> flag onto the plate, so anything that fades or disables
    /// an unseen renderer (reveal transitions, the mod's own visibility systems) never leaves a
    /// bare dark slab behind.
    /// </summary>
    private static void SyncUnseenUnderlays()
    {
        for (int i = UnseenUnderlays.Count - 1; i >= 0; i--)
        {
            UnseenUnderlay e = UnseenUnderlays[i];
            if (e.Source == null || e.Plate == null)
            {
                if (e.Plate != null) // source renderer died alone (component removal) — clean up
                    UnityEngine.Object.Destroy(e.Plate.gameObject);
                // The base quad lives under the scene-root holder, NOT under the source — it
                // never dies structurally with the piece and must go explicitly (Apparance regen
                // would otherwise strand a dark quad under a piece that no longer exists).
                if (e.Fill != null) // like the plate: source died alone — clean up the children
                    UnityEngine.Object.Destroy(e.Fill.gameObject);
                UnseenSources.Remove(e.SourceId);
                UnseenUnderlays.RemoveAt(i);
                continue;
            }
            if (e.Plate.enabled != e.Source.enabled)
                e.Plate.enabled = e.Source.enabled;
            // The fill is a child of the source like the plate, so hierarchy deactivation
            // (ProceduralMapTile.ShowContent on reveal) covers it for free — only the renderer
            // flag needs mirroring.
            if (e.Fill != null && e.Fill.enabled != e.Source.enabled)
                e.Fill.enabled = e.Source.enabled;
        }
    }

    /// <summary>
    /// The two shared underlay materials, created lazily and retinted live: the DARK plate uses a
    /// game-shipped unlit shader at the END of the opaque range (queue 2500 — after all real
    /// opaque/cutout geometry, before every translucent pass), ZTest LEqual, so nearer geometry
    /// still occludes it while it paints solid dark exactly where the unseen mesh is about to
    /// blend. The SKIP filler is the same shader fully transparent — it exists only to keep the
    /// underlay's material array aligned with the source's submesh slots. Key-colour safety as in
    /// WorldUI.MrBacking: when the live key moves within keying distance of the dark neutral, the
    /// plate lifts to a brighter warm gray so it can never be keyed away.
    /// </summary>
    private static void EnsureUnseenMaterials()
    {
        Color key = KeyColor.Value;
        bool nearKey = Mathf.Abs(key.r - UnseenDark.r) < UnseenKeyDistance
                       && Mathf.Abs(key.g - UnseenDark.g) < UnseenKeyDistance
                       && Mathf.Abs(key.b - UnseenDark.b) < UnseenKeyDistance;
        Color wanted = nearKey ? UnseenLift : UnseenDark;

        // Round 6: the backings must draw BEFORE the family, against a depth buffer that does
        // not yet contain the family surfaces (pass 0 of Amp_Basic_Unseen hardcodes ZWrite On —
        // ShaderOcclusionPatcher README). All evidence puts the family in the transparent range
        // (>2500), where the default 2500 already precedes it; the adaptive branch exists for
        // the one unconfirmed case (family AT/below 2500) so the fix cannot be outrun by a
        // material this code has not seen yet. Revealed floor stays safe in both branches: it
        // draws at the geometry queue (~2000) with depth, and a backing drawn later either
        // fails LEqual below it or — where the floor is behind — is painted over by nothing,
        // because the backing writes no depth and the floor already won the pixel.
        int wantedQueue = _familyMinQueue <= 2500 ? Mathf.Max(2000, _familyMinQueue - 1) : 2500;

        if (_unseenDarkMat == null)
        {
            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Legacy Shaders/Diffuse")
                            ?? Shader.Find("Hidden/InternalErrorShader");
            _unseenDarkMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenDark",
                color = wanted,
                renderQueue = wantedQueue,
            };
            _unseenSkipMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenSkip",
                color = new Color(0f, 0f, 0f, 0f), // alpha 0: rasterized to nothing, writes nothing
                renderQueue = wantedQueue,
            };
            _unseenDarkColor = wanted;
            // One-shot state line (round 6): the next hardware log must show WHICH shader the
            // dark material actually got — the from-above/from-below asymmetry hypotheses hinge
            // on its cull/depth state, and Shader.Find fallbacks are invisible without this.
            VRLog.Info("Core", $"MR: unseen dark material created — shader '{shader.name}', " +
                               $"queue {wantedQueue} (per-piece coplanar underlay + groove fill; " +
                               "ModBuild-63 log confirmed Sprites/Default + family queue 3000).");
            return;
        }
        if (_unseenDarkMat.renderQueue != wantedQueue)
        {
            _unseenDarkMat.renderQueue = wantedQueue;
            if (_unseenSkipMat != null)
                _unseenSkipMat.renderQueue = wantedQueue;
            VRLog.Info("Core", $"MR: unseen backings re-queued to {wantedQueue} — a family " +
                               $"material was observed at queue {_familyMinQueue}, and the " +
                               "backings must composite before the family's depth-writing pass.");
        }
        if (_unseenDarkColor != wanted)
        {
            _unseenDarkColor = wanted;
            _unseenDarkMat.color = wanted;
            VRLog.Info("Core", $"MR: key color moved near the unseen-underlay neutral — underlay " +
                               $"re-tinted to RGBA {wanted.r:0.##},{wanted.g:0.##},{wanted.b:0.##},1 " +
                               "so it can never be chroma-keyed away.");
        }
    }

    /// <summary>True when an ACTIVE ancestor named 'Preview' sits above <paramref name="t"/> —
    /// the node <c>ProceduralMapTile.ShowContent</c> toggles for a hidden room's stand-in stack.
    /// One of the two match signals of <see cref="ForceUnseenOpaque"/> (the other is the 'Unseen'
    /// shader family); kept so a translucent stack renderer outside the family stays covered.
    /// Depth-capped: the preview content is generated a handful of levels under the tile.</summary>
    private static bool UnderPreviewNode(Transform t)
    {
        Transform? p = t;
        for (int depth = 0; p != null && depth < 12; depth++)
        {
            if (p.name == "Preview")
                return true;
            p = p.parent;
        }
        return false;
    }

    /// <summary>True when <paramref name="s"/> carries the game's fog-of-war naming fragment
    /// (<see cref="UnseenShaderHint"/>) — applied to GO names ('EN_Unseen_…'), material names and
    /// shader names ('Amp_Basic_Unseen', 'UnseenGroundPlane_Shd') alike since round 3.</summary>
    private static bool HasUnseenName(string? s) =>
        !string.IsNullOrEmpty(s)
        && s!.IndexOf(UnseenShaderHint, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Family test for one material: its own name or its shader's name reads 'Unseen'.</summary>
    private static bool IsUnseenFamilyMaterial(Material? m) =>
        m != null && (HasUnseenName(m.name) || (m.shader != null && HasUnseenName(m.shader.name)));

    /// <summary>Translucency test: a transparent-range render queue, or an active alpha blend
    /// (DstBlend != Zero). Cutout (AlphaTest ≤ 2500, DstBlend 0) counts as opaque — it does not
    /// let the key colour through per-pixel, so it needs no forcing.</summary>
    private static bool IsTranslucent(Material? m)
    {
        if (m == null)
            return false;
        if (m.renderQueue > 2500)
            return true;
        return m.HasProperty("_DstBlend") && m.GetInt("_DstBlend") != 0;
    }

    /// <summary>Destroy every underlay child and the two shared materials — MR off / VR stop /
    /// hot reload / the safety valve flipping off. The sources' own materials were never touched,
    /// so there is nothing to reassign; underlays whose source a scene unload already destroyed
    /// died with it (Unity fake-null) and are simply dropped.</summary>
    private static void RestoreUnseenUnderlays()
    {
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            Renderer plate = UnseenUnderlays[i].Plate;
            if (plate != null)
                UnityEngine.Object.Destroy(plate.gameObject);
            Renderer? fill = UnseenUnderlays[i].Fill;
            if (fill != null)
                UnityEngine.Object.Destroy(fill.gameObject);
        }
        UnseenUnderlays.Clear();
        UnseenSources.Clear();
        if (_unseenDarkMat != null)
        {
            UnityEngine.Object.Destroy(_unseenDarkMat);
            _unseenDarkMat = null;
        }
        if (_unseenSkipMat != null)
        {
            UnityEngine.Object.Destroy(_unseenSkipMat);
            _unseenSkipMat = null;
        }
        _previewScanNextFrame = 0;
        _loggedPreviewCount = -1;
        _previewDiagLogged = false;
        _unseenVerboseLogs = 0;
        _censusLastHash = 0;
        _censusNextAllowed = 0f;
        _familyMinQueue = int.MaxValue; // re-observe per session (round 6 adaptive queue)
    }

    private static void RestoreSky()
    {
        for (int i = 0; i < HiddenSky.Count; i++)
        {
            Renderer r = HiddenSky[i];
            if (r != null) // Unity fake-null: destroyed by a scene unload
                r.enabled = true;
        }
        HiddenSky.Clear();
        _skyScanNextFrame = 0;
        _loggedSkyCount = -1;
        _skyDiagLogged = false;
    }

    private static void Record(Camera cam)
    {
        if (!CamOriginals.ContainsKey(cam))
            CamOriginals.Add(cam, (cam.clearFlags, cam.backgroundColor));
    }

    private static void ForceSolid(Camera cam, Color key)
    {
        if (cam.clearFlags != CameraClearFlags.SolidColor)
            cam.clearFlags = CameraClearFlags.SolidColor;
        if (cam.backgroundColor != key)
            cam.backgroundColor = key;
    }

    /// <summary>Restore every recorded camera + the skybox material (MR off / VR stop / hot reload).</summary>
    internal static void RestoreAll()
    {
        int restored = 0;
        foreach (KeyValuePair<Camera, (CameraClearFlags Flags, Color Bg)> pair in CamOriginals)
        {
            Camera cam = pair.Key;
            if (cam == null) // Unity fake-null: destroyed by a scene unload
                continue;
            cam.clearFlags = pair.Value.Flags;
            cam.backgroundColor = pair.Value.Bg;
            restored++;
        }
        CamOriginals.Clear();

        if (_skyboxSaved)
        {
            RenderSettings.skybox = _savedSkybox;
            _savedSkybox = null;
            _skyboxSaved = false;
        }

        int skyRestored = HiddenSky.Count;
        RestoreSky();

        int unseenRestored = UnseenUnderlays.Count;
        RestoreUnseenUnderlays();

        bool wasActive = _active;
        _active = false;
        if (wasActive && _loggedActive)
        {
            _loggedActive = false;
            VRLog.Info("Core", $"Mixed reality OFF — skybox and {restored} camera clear(s) " +
                               $"restored, {skyRestored} sky renderer(s) re-enabled and " +
                               $"{unseenRestored} unseen-geometry underlay(s) destroyed " +
                               "(authored materials were never touched) — back to vanilla.");
        }
    }

    /// <summary>Drop bookkeeping for cameras destroyed by scene unloads (defensive).
    ///
    /// <para>Same name as <c>VRCameraPolicy.PruneDead</c> and called from the same scene-load
    /// path, but a DIFFERENT map (clear-flags/background colour, not stereo eye masks) plus the
    /// hidden-sky renderer list and three scan/log latches this one alone owns. Not a duplicate;
    /// do not merge (REVIEW-Hands-Board-Core §2 item 9).</para></summary>
    internal static void PruneDead()
    {
        Scratch.Clear();
        foreach (KeyValuePair<Camera, (CameraClearFlags Flags, Color Bg)> pair in CamOriginals)
        {
            if (pair.Key == null)
                Scratch.Add(pair.Key!);
        }
        for (int i = 0; i < Scratch.Count; i++)
            CamOriginals.Remove(Scratch[i]);
        Scratch.Clear();

        // Drop sky renderers destroyed by the unload; re-scan the new scene from scratch.
        for (int i = HiddenSky.Count - 1; i >= 0; i--)
        {
            if (HiddenSky[i] == null)
                HiddenSky.RemoveAt(i);
        }
        _skyScanNextFrame = 0;
        _skyDiagLogged = false;
        _loggedSkyCount = -1;

        // Same for the unseen underlays: a source the unload destroyed took its underlay child
        // with it — drop the entry (the shared dark/skip materials are mod-owned and survive),
        // and let the next MR tick re-sweep the new scene.
        for (int i = UnseenUnderlays.Count - 1; i >= 0; i--)
        {
            if (UnseenUnderlays[i].Source == null)
            {
                UnseenSources.Remove(UnseenUnderlays[i].SourceId);
                UnseenUnderlays.RemoveAt(i);
            }
        }
        _previewScanNextFrame = 0;
        _previewDiagLogged = false;
        _loggedPreviewCount = -1;
    }
}
