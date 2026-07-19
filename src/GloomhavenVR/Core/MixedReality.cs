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
/// - HeadCamera: MR owns its clear WHILE ON. <c>VRRigDriver.Tick()</c> calls
///   <see cref="Tick"/> AFTER its own TickHeadClearColor, so MR's key color wins the frame
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

    /// <summary>Cycle presets for the settings UI (green → magenta → blue → green …).</summary>
    private static readonly (string Name, Color Color)[] Presets =
    {
        ("Green", new Color(0f, 1f, 0f, 1f)),
        ("Magenta", new Color(1f, 0f, 1f, 1f)),
        ("Blue", new Color(0f, 0f, 1f, 1f)),
    };

    // Recorded originals for full restore.
    private static readonly Dictionary<Camera, (CameraClearFlags Flags, Color Bg)> CamOriginals = new();
    private static readonly List<Camera> Scratch = new(8);
    private static Material? _savedSkybox;
    private static bool _skyboxSaved;

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

    /// <summary>Material/shader/name fragments that mark a renderer as sky/background.</summary>
    private static readonly string[] SkyNameHints =
    {
        "skydome", "skybox", "sky", "backdrop", "dome", "horizon",
        "cloud", "vista", "firmament", "background",
    };

    private static bool _active;        // MR currently applied to the scene
    private static bool _loggedActive;  // change-dedup for the on/off log
    private static Color _loggedColor;

    // ---- menu-unclip: keep floated menus visible through the void backdrop (WorldUI item 5a) ----
    // When MR is OFF the scenario void/backdrop is the level's opaque environment MESH — the
    // hardware log names it exactly: 'GH_SkySphere' (layer Default/0, size (224.94,71.18,224.94),
    // shader 'AMP_SkyShader'), the largest renderer that ENCLOSES the head. A floated world-space
    // menu is transparent UI (writes no depth) that ZTests LEqual against the depth buffer, so a
    // movable menu dragged out to the edge of the shell sits BEHIND the backdrop mesh in depth and
    // is CLIPPED there.
    //
    // Robust lever (works regardless of the shader's own state): while a menu floats, force the
    // backdrop material into the BACKGROUND render queue (1000) so it always composites BEHIND all
    // Geometry-queue diorama geometry, and — where the shader exposes it — disable depth WRITE so
    // the shell contributes no depth. A depthless menu (Transparent queue) then always passes ZTest
    // where only the backdrop is behind it. The diorama look is unchanged: nearer diorama geometry
    // (Geometry queue 2000) still renders AFTER the Background shell and writes/tests its own depth,
    // so it occludes both the shell and the menu exactly as before. Only relevant while MR is OFF
    // (MR hides the backdrop mesh outright via HideSkyGeometry). Fully reversible (renderQueue and
    // ZWrite restored). We do NOT depend on the strict IsSkyRenderer gate matching: the biggest
    // enclosing renderer (the candidate the diagnostic already computes) is always targeted too.
    private static readonly List<(Material Mat, int OrigRenderQueue, bool HadZWrite, int OrigZWrite)> MenuUnclipMats = new(4);
    private static bool _menuUnclipWanted;   // a floated menu exists (set by WorldUI.ModalFallback)
    private static bool _menuUnclipActive;
    private static int _menuUnclipScanNextFrame;
    private static bool _menuUnclipDiagLogged;

    /// <summary>
    /// WorldUI item 5a: a floated (movable) menu is open, so the void backdrop must not clip it.
    /// Idempotent per-frame switch — ModalFallback calls it with whether any modal window floats.
    /// No effect while MR is ON (the backdrop mesh is already hidden).
    /// </summary>
    internal static void KeepMenusUnclipped(bool wanted) => _menuUnclipWanted = wanted;

    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("mixedreality");
        Enabled = _file.Bind("MixedReality", "Enabled", false,
            "Mixed-reality (chroma-key passthrough) mode. When ON the sky/background of the " +
            "whole game turns the flat solid KeyColor and every skybox is disabled, so Virtual " +
            "Desktop (or any compositor) can chroma-key that color and show the diorama/table " +
            "floating over your real room. The 3D geometry keeps rendering — only the sky becomes " +
            "the flat key color. Restored fully when turned off.");
        KeyColor = _file.Bind("MixedReality", "KeyColor", new Color(0f, 1f, 0f, 1f),
            "The solid chroma-key color the sky/background clears to in mixed-reality mode " +
            "(default pure green RGBA 0,1,0,1). The in-VR settings panel cycles the presets " +
            "green / magenta / blue; any RGBA is accepted here.");
        HideSkyMeshes = _file.Bind("MixedReality", "HideSkyMeshes", true,
            "Also disable the sky/background GEOMETRY while MR is on. The scenario backdrop is " +
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
            // MR OFF: the void backdrop is drawn as opaque mesh — keep floated menus from being
            // clipped by it (WorldUI item 5a). Only runs while a menu actually floats.
            TickMenuUnclip();
            return;
        }

        // MR ON: the backdrop mesh is hidden outright below, so nothing can clip the menus —
        // drop any depth-write override we applied while MR was off.
        if (_menuUnclipActive)
            RestoreMenuUnclip();

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

    // ---- menu-unclip (WorldUI item 5a) ---------------------------------------------------------

    /// <summary>
    /// While a floated menu exists and MR is OFF, push the void backdrop material(s) into the
    /// Background render queue (and disable depth WRITE where the shader exposes it) so the
    /// depthless menu canvas is never ZTest-clipped by the enclosing shell.
    ///
    /// Targeting is robust: any renderer the sky heuristic matches is unclipped, AND — crucially —
    /// so is the single LARGEST renderer that encloses the head (the exact candidate the diagnostic
    /// dump computes; the hardware log named it 'GH_SkySphere' / 'AMP_SkyShader'). That fallback
    /// means we no longer depend on the strict <see cref="IsSkyRenderer"/> gate matching, nor on the
    /// shader exposing any particular property. Throttled like the sky sweep (the backdrop can
    /// generate late). Reversible; when nothing was targeted, the enclosing candidates are dumped once.
    /// </summary>
    private static void TickMenuUnclip()
    {
        if (!_menuUnclipWanted || !VRSession.IsRunning)
        {
            if (_menuUnclipActive)
                RestoreMenuUnclip();
            return;
        }
        if (Time.frameCount < _menuUnclipScanNextFrame)
            return;
        _menuUnclipScanNextFrame = Time.frameCount + SkyScanIntervalFrames;

        Camera? head = VRCameraPolicy.AllowedHead;
        if (head == null)
            return;
        Vector3 headPos = head.transform.position;
        float sizeFloor = Mathf.Max(SkyMinEnclosingSize, head.farClipPlane * SkyEnclosingFarFraction);

        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();

        // The void backdrop is the biggest renderer enclosing the head. Track it as we scan so it
        // is ALWAYS unclipped, even if the strict sky heuristic misses (e.g. a squashed dome that
        // fails the per-axis extent floor). This is the same selection the diagnostic dump reports.
        Renderer? largestEnclosing = null;
        float largestVolume = 0f;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5) // never our own mod visuals / UI hosts
                continue;

            if (r.bounds.Contains(headPos))
            {
                float vol = BoundsVolume(r.bounds);
                if (vol > largestVolume)
                {
                    largestVolume = vol;
                    largestEnclosing = r;
                }
            }

            if (IsSkyRenderer(r, headPos, sizeFloor))
                UnclipBackdrop(r);
        }

        // Fallback: force-unclip the biggest enclosing shell (GH_SkySphere) regardless of the sky
        // gate. Materials already recorded above are skipped, so this never double-processes.
        if (largestEnclosing != null)
            UnclipBackdrop(largestEnclosing);

        _menuUnclipActive = MenuUnclipMats.Count > 0;
        // Nothing targeted (no enclosing renderer at all): dump the candidates ONCE so a hardware
        // run can name the real void shell.
        if (MenuUnclipMats.Count == 0 && !_menuUnclipDiagLogged)
        {
            _menuUnclipDiagLogged = true;
            LogSkyCandidates(all, headPos);
        }
    }

    /// <summary>
    /// Push every shared material of <paramref name="r"/> into the Background render queue (1000)
    /// and, where the shader exposes ZWrite, disable depth-write — so the huge backdrop always
    /// composites BEHIND the world and never depth-occludes a floated menu. Records the original
    /// renderQueue / ZWrite for full restore; skips materials already recorded. Shared (not
    /// instanced): the enclosing shell material is unique, keeping the change reversible with no
    /// instance leaks. Logs the shader's render state once per material so the next hardware log
    /// confirms exactly what fired.
    /// </summary>
    private static void UnclipBackdrop(Renderer r)
    {
        int layer = r.gameObject.layer;
        Material[] mats = r.sharedMaterials;
        for (int m = 0; m < mats.Length; m++)
        {
            Material mat = mats[m];
            if (mat == null || IsUnclipRecorded(mat))
                continue;

            int origQueue = mat.renderQueue;
            bool hadZWrite = mat.HasProperty("_ZWrite");
            int origZWrite = hadZWrite ? mat.GetInt("_ZWrite") : 0;
            MenuUnclipMats.Add((mat, origQueue, hadZWrite, origZWrite));

            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background; // 1000
            if (hadZWrite)
                mat.SetInt("_ZWrite", 0);

            string shader = mat.shader != null ? mat.shader.name : "<none>";
            VRLog.Info("Core", $"MR menu-unclip: targeted backdrop '{r.gameObject.name}' " +
                               $"(layer {LayerName(layer)}) material '{mat.name}' shader '{shader}' — " +
                               $"renderQueue {origQueue}->1000 (Background); " +
                               $"ZWrite {(hadZWrite ? origZWrite + "->0" : "property ABSENT (queue-only)")}. " +
                               "Floated menus now composite over the void shell (rendering otherwise " +
                               "unchanged; fully reversible).");
        }
    }

    private static bool IsUnclipRecorded(Material mat)
    {
        for (int i = 0; i < MenuUnclipMats.Count; i++)
        {
            if (ReferenceEquals(MenuUnclipMats[i].Mat, mat))
                return true;
        }
        return false;
    }

    /// <summary>Restore every backdrop material's renderQueue + depth-write (menu closed / MR on / scene change).</summary>
    private static void RestoreMenuUnclip()
    {
        for (int i = 0; i < MenuUnclipMats.Count; i++)
        {
            Material mat = MenuUnclipMats[i].Mat;
            if (mat == null) // Unity fake-null: destroyed by a scene unload
                continue;
            mat.renderQueue = MenuUnclipMats[i].OrigRenderQueue;
            if (MenuUnclipMats[i].HadZWrite)
                mat.SetInt("_ZWrite", MenuUnclipMats[i].OrigZWrite);
        }
        MenuUnclipMats.Clear();
        _menuUnclipActive = false;
        _menuUnclipScanNextFrame = 0;
        _menuUnclipDiagLogged = false;
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
        RestoreMenuUnclip(); // item 5a: put backdrop depth-write back (harmless if it was off)

        bool wasActive = _active;
        _active = false;
        if (wasActive && _loggedActive)
        {
            _loggedActive = false;
            VRLog.Info("Core", $"Mixed reality OFF — skybox, {restored} camera clear(s) and " +
                               $"{skyRestored} sky renderer(s) restored to vanilla.");
        }
    }

    /// <summary>Drop bookkeeping for cameras destroyed by scene unloads (defensive).</summary>
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

        // Backdrop materials may be destroyed by the unload — drop stale records and re-scan.
        for (int i = MenuUnclipMats.Count - 1; i >= 0; i--)
        {
            if (MenuUnclipMats[i].Mat == null)
                MenuUnclipMats.RemoveAt(i);
        }
        _menuUnclipActive = MenuUnclipMats.Count > 0;
        _menuUnclipScanNextFrame = 0;
        _menuUnclipDiagLogged = false;
    }
}
