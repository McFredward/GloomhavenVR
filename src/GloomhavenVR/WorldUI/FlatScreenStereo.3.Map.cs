using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreenStereo
{
    // ---- map base capture: probe + engage (class doc MAP ALBEDO RENDER) ---------------------

    /// <summary>
    /// Throttled ASYNC non-black probe of the base RT. Runs only while a 3D background
    /// camera is actively rendering (so the base RT SHOULD hold scene content) and the
    /// albedo render is not yet engaged; a stall-free downsample-then-readback that,
    /// after several consecutive all-black results, engages the mod albedo render of
    /// the campaign map.
    /// </summary>
    private void TickBlackProbe(bool anyMirrorRendering)
    {
        // The detection gate ([WorldUI] ScreenLeftMirrorFallback) is GONE — user ruling
        // 2026-08-13: its OFF left the campaign map a BLACK SCREEN by design, which is not a
        // setting but a way to lose the campaign. The probe always runs.
        if (_mapBaseCapture)
            return;
        if (!anyMirrorRendering || _leftRt == null || _probePending)
            return;
        if (_blackProbeFrame != int.MinValue
            && Time.frameCount - _blackProbeFrame < BlackProbeIntervalFrames)
            return;
        _blackProbeFrame = Time.frameCount;

        if (_probeRt == null)
        {
            _probeRt = new RenderTexture(BlackProbeSize, BlackProbeSize, 0)
            {
                name = "GloomhavenVR.FlatScreenRT.BlackProbe",
                antiAliasing = 1,
            };
            if (!_probeRt.Create())
            {
                ReleaseProbeRt();
                return;
            }
        }

        // Bilinear downsample to NxN, then read those few texels back off-thread.
        Graphics.Blit(_leftRt, _probeRt);
        _probePending = true;
        _probeReqGen = _probeGen;
        AsyncGPUReadback.Request(_probeRt, 0, TextureFormat.RGBA32, OnBlackProbe);
    }

    private void OnBlackProbe(AsyncGPUReadbackRequest req)
    {
        _probePending = false;
        // Ignore results from a torn-down / superseded activation.
        if (!_active || _mapBaseCapture || _probeReqGen != _probeGen || req.hasError)
            return;

        var data = req.GetData<Color32>();
        int maxChannel = 0;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            int m = c.r;
            if (c.g > m) m = c.g;
            if (c.b > m) m = c.b;
            if (m > maxChannel)
                maxChannel = m;
        }

        if (maxChannel <= BlackChannelThreshold)
        {
            _blackConsecutive++;
            _nonBlackMapConsecutive = 0;
            if (_blackConsecutive >= BlackConsecutiveToEngage)
                EngageMapAlbedo(maxChannel);
        }
        else
        {
            _blackConsecutive = 0;
            TickNonBlackMapDetect(maxChannel);
        }
    }

    /// <summary>Consecutive NON-black probes while a campaign map is provably showing (second engage path).</summary>
    private int _nonBlackMapConsecutive;

    /// <summary>
    /// Machine-independent map engage (remote log 2026-08-01). On the reporting user's machine the
    /// CampaignMap scene's base RT does NOT read black: the raw game MapCamera writes visible content
    /// into it (brown parchment murk — how the deferred render resolves into an off-screen RT is
    /// quality-preset/GPU dependent; on the original test machine it resolves to pure black). The
    /// all-black detection above therefore never fires on his machine and the map render never engages
    /// — his log shows the CampaignMap scene with ZERO further MAP lines, and he sees the raw un-posed
    /// map camera (brown, inside the parchment). The map being SHOWN is decidable POSITIVELY instead:
    /// a MapChoreographer whose worldMap or cityMap is active in the hierarchy. When that holds while
    /// the probes keep reading non-black, engage after the same consecutive count. On a machine where
    /// the map RT reads all-black (the original behavior) the black path engages first and this
    /// counter never reaches the threshold — that path is unchanged.
    /// </summary>
    private void TickNonBlackMapDetect(int maxChannel)
    {
        global::MapChoreographer choreo = Object.FindObjectOfType<global::MapChoreographer>();
        GameObject? world = choreo != null ? choreo.worldMap : null;
        GameObject? city = choreo != null ? choreo.cityMap : null;
        GameObject? shown = world != null && world.activeInHierarchy ? world
                          : city != null && city.activeInHierarchy ? city : null;
        if (shown == null)
        {
            _nonBlackMapConsecutive = 0;
            return;
        }
        _nonBlackMapConsecutive++;
        if (_nonBlackMapConsecutive < BlackConsecutiveToEngage)
            return;
        EngageMapCore();
        VRLog.Info("WorldUI", $"MAP RENDER detection (NON-black): the campaign map is showing " +
                              $"(MapChoreographer map '{shown.name}' active) but the base RenderTexture reads " +
                              $"NON-black (max channel {maxChannel}/255 over {BlackProbeSize}x{BlackProbeSize}) — " +
                              "on this machine the raw map camera resolves visible content into the RT " +
                              "(quality/GPU dependent), which the all-black detection can never catch. " +
                              "Engaging the mod forward render of the REAL MapChoreographer map mesh (MapUnlit) " +
                              "all the same. Re-arms on the next scene change.");
    }

    // ---- fast positive map-open engage (user report: brown flash before the real map) --------

    /// <summary>Cached MapChoreographer for the fast positive engage. Unity fake-null after a scene
    /// change revives the throttled re-find automatically.</summary>
    private global::MapChoreographer? _fastMapChoreo;
    private int _fastMapFindFrame = int.MinValue;
    /// <summary>Frames between FindObjectOfType attempts while no choreographer is cached (near-free
    /// when the scene has none — the scan is type-indexed).</summary>
    private const int FastMapFindIntervalFrames = 10;
    /// <summary><c>Time.realtimeSinceStartup</c> at engage — for the MAP REVEAL latency readout.</summary>
    private float _mapEngageTime;

    /// <summary>
    /// ROOT CAUSE (user report 2026-08, "zuerst eine Sekunde ... die braune Farbe sichtbar"): map
    /// engagement used to be PROBE-driven only — an async base-RT readback every
    /// <see cref="BlackProbeIntervalFrames"/> (30) frames needing <see cref="BlackConsecutiveToEngage"/>
    /// (3) consecutive results, so the raw game map camera's brown murk (its dead deferred render
    /// resolving into the shared base RT) was SHOWN for ~90+ frames (~1.25 s at 72 Hz) before the mod
    /// render replaced it. But the probes only ever decided WHEN to engage — WHETHER is positively
    /// decidable the moment the map opens: a MapChoreographer whose worldMap or cityMap is
    /// activeInHierarchy, the exact criterion the non-black detect path already trusts as proof (see
    /// <see cref="TickNonBlackMapDetect"/>). So engage at the map-open EVENT: the choreographer lookup
    /// is throttled, but once cached the active-in-hierarchy check runs per tick, so engagement lands
    /// on the very frame the map GO activates. Both probe paths remain as fallback (each checks
    /// <c>_mapBaseCapture</c> first, so they are no-ops once this fired). Paired with the reveal gate
    /// in <c>OnPreRenderCamera</c> (hold the quad BLACK until the mod camera has produced its first
    /// frame at a valid driven pose), the brown pre-map content is never shown at all.
    /// </summary>
    private void TickFastMapEngage(Camera? mapSource)
    {
        // [WorldUI] MapAlbedoRender + ScreenLeftMirrorFallback are GONE (user ruling
        // 2026-08-13): both OFF states ended in a black campaign map. The rescue is
        // unconditional now.
        if (_mapBaseCapture || mapSource == null || _leftRt == null || _introGuard)
            return;
        if (_fastMapChoreo == null)
        {
            if (_fastMapFindFrame != int.MinValue
                && Time.frameCount - _fastMapFindFrame < FastMapFindIntervalFrames)
                return;
            _fastMapFindFrame = Time.frameCount;
            _fastMapChoreo = Object.FindObjectOfType<global::MapChoreographer>();
            if (_fastMapChoreo == null)
                return;
        }
        GameObject? world = _fastMapChoreo.worldMap;
        GameObject? city = _fastMapChoreo.cityMap;
        GameObject? shown = world != null && world.activeInHierarchy ? world
                          : city != null && city.activeInHierarchy ? city : null;
        if (shown == null)
            return;
        EngageMapCore();
        VRLog.Info("WorldUI", $"MAP RENDER detection (FAST positive): campaign map '{shown.name}' became active " +
                              "in the hierarchy — engaging the mod forward render at the map-open event itself " +
                              "(no probe wait; the black/non-black probe paths remain as fallback). The screen " +
                              "quad holds BLACK until the first mod-rendered frame at a valid pose, so the " +
                              "game's raw brown map render is never shown.");
    }

    // ---- map reveal gate (never show the game's brown pre-map render) ------------------------

    /// <summary>True once the mod map camera has rendered at least one frame into the private map RT
    /// for THIS engagement (stamped in <c>OnPostRenderCamera</c>). Until then — and until the driven
    /// pose is valid — the screen quad holds BLACK instead of the base RT's brown murk.</summary>
    private bool _mapFirstFrameRendered;
    /// <summary>One-shot guard for the MAP REVEAL latency log (per engagement).</summary>
    private bool _mapRevealLogged;
    /// <summary>Upper bound on the post-engage black hold (~1 s at 72 Hz). If the driven pose never
    /// validates or the private RT never appears, the quad falls back to the pre-gate behaviour
    /// (map/base RT as available) instead of staying black — never worse than the old brown delay.</summary>
    private const int MapRevealMaxHoldFrames = 72;
    /// <summary>True when the post-engage black-hold window has run out (fail-open).</summary>
    private bool MapRevealHoldElapsed =>
        _mapEngageFrame != int.MinValue && Time.frameCount - _mapEngageFrame > MapRevealMaxHoldFrames;

    /// <summary>
    /// The base RT reads black while a 3D background camera renders — engage MAP ALBEDO
    /// RENDER: the mod's own forward camera renders the worldMap parchment (unlit, from
    /// its albedo texture) into the base RT, and both eyes are driven from it (mono).
    /// Sticky for the scene; re-arms on the next stack release.
    /// </summary>
    private void EngageMapAlbedo(int maxChannel)
    {
        if (_mapBaseCapture)
            return;
        EngageMapCore();
        VRLog.Info("WorldUI", $"MAP RENDER detection: the screen's base RenderTexture reads BLACK " +
                              $"(max channel {maxChannel}/255 over {BlackProbeSize}x{BlackProbeSize}) while a 3D " +
                              "background camera renders — the campaign map's deferred parchment shader will not " +
                              "light into any RenderTexture we own. Engaging the mod forward render of the REAL " +
                              "MapChoreographer.worldMap mesh (MapUnlit) into the base RT. Re-arms on the next scene change.");
    }

    /// <summary>
    /// Shared engage state for BOTH detection paths (all-black base RT on the original machine,
    /// NON-black-with-live-MapChoreographer on machines whose map camera resolves visible content —
    /// see <see cref="TickNonBlackMapDetect"/>). Guarded by the callers' <c>_mapBaseCapture</c> check
    /// (the non-black path only runs while it is false, see <see cref="TickBlackProbe"/>).
    /// </summary>
    private void EngageMapCore()
    {
        _mapBaseCapture = true;
        _mapEngageFrame = Time.frameCount; // start the fast per-frame base-RT probe window
        _mapEngageTime = Time.realtimeSinceStartup;
        // Reveal gate: hold the quad black until the mod camera renders its first frame at a valid
        // pose (see the map-reveal-gate block) — the MAP REVEAL line logs the measured latency.
        _mapFirstFrameRendered = false;
        _mapRevealLogged = false;
        _mapMirrorLogged = false;
        _capLogged = false;
        _capMapValid = false;
        _albedoMaterialsLogged = false;
        _albedoWarned = false;
        _nonBlackMapConsecutive = 0;
        // Fresh acquisition-failure dump budget per engagement (see LogMapAcquisitionCandidates).
        _albedoFailTicks = 0;
        _albedoFailDumpLogged = false;
        // Fresh diagnostic budget per map ENTRY: the savegame session that shipped no icons at
        // all had spent all five MAP ICONS lines within seconds of the FIRST map load — every
        // later minute on the map was diagnostically blind.
        _mapIconsLogCount = 0;
        _iconFallbackLogged = false;
        // Force a fresh decal scan on the FIRST icon draw of this map entry (identity-guarded
        // sentinel, see the _iconCacheFrame doc block) so the per-entry MAP ICONS diagnostics
        // above always describe a scan from THIS visit, never a set cached on a previous one.
        _iconCacheFrame = int.MinValue;
        // Fresh MAP ICON GEOMETRY dump budget per map entry (scan-event-throttled per-icon lines).
        _iconGeomDumps = 0;
        _iconGeomLastDrawn = -1;
    }

    // ---- map render: forward camera over the REAL parchment mesh (class doc MAP RENDER) --------

    /// <summary>
    /// Configure + enable the mod FORWARD camera so it renders the REAL parchment mesh into the base RT
    /// this frame; disable it whenever the map capture is not engaged / not ready. The camera clones the
    /// game map camera (<paramref name="mapSource"/>) — world pose, and (in
    /// <see cref="OnPreCullCamera"/>/<see cref="OnPreRenderCamera"/>) the game camera's RENDER-TIME
    /// view+projection captured from its own onPreRender — so the mesh, drawn at its true GPU vertex
    /// positions, projects to exactly the same screen coordinates as the game's own map and pans/zooms
    /// 1:1 with it (the glass-RT markers + laser align automatically). Its cullingMask = the game map
    /// camera's mask (which includes the parchment layer); every OTHER object on those layers uses a
    /// deferred material with no forward pass, so it stays invisible in our forward camera — only the
    /// parchment (whose materials we override with MapUnlit for exactly our render) draws. Clears black,
    /// sits at a depth just above the game map camera so it OWNS the base RT's final content.
    /// </summary>
    /// <summary>Intersect the ray through camera viewport (u,v) with the horizontal plane y=planeY. False if parallel/behind.</summary>
    private static bool TryMapPlaneHit(Camera cam, float u, float v, float planeY, out Vector3 hit)
    {
        hit = Vector3.zero;
        Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));
        if (Mathf.Abs(ray.direction.y) < 1e-5f)
            return false;
        float t = (planeY - ray.origin.y) / ray.direction.y;
        if (t <= 0f || t > 1e5f)
            return false;
        hit = ray.origin + t * ray.direction;
        return true;
    }

    /// <summary>VR map controls: right-stick Y zooms (FOV). Pan (trigger-drag) is wired separately.</summary>
    private void TickMapInput()
    {
        if (!_mapBaseCapture)
            return;
        var rh = GloomhavenVR.Hands.VRHands.Right;
        if (rh == null || !rh.HasPose)
            return;
        if (_mapFov <= 0f)
            _mapFov = MapDefaultFov;
        float zy = rh.Thumbstick.y;
        if (Mathf.Abs(zy) > MapStickDeadzone)
        {
            // Stick UP (y>0) → zoom IN → narrower FOV; DOWN → zoom OUT → wider FOV (matches flat's wheel).
            _mapFov = Mathf.Clamp(_mapFov - zy * MapZoomSpeed * Time.deltaTime, MapMinFov, MapMaxFov);
        }
    }

    // ---- map PAN (laser + trigger grab-drag) ------------------------------------------------
    //
    // FlatScreen owns the trigger gesture (it already distinguishes a still click from a moving
    // drag via the press latch). On the campaign map a moving held trigger PANS the parchment
    // instead of dragging a uGUI widget: FlatScreen calls BeginMapPan(downPixel) when the latch
    // opens into a drag, UpdateMapPan(pixel) each tick, and EndMapPan() on release. A still
    // trigger keeps the latch closed → the existing click path selects the location under it.

    /// <summary>True while the campaign map is being rendered by our forward camera AND we have a valid
    /// driven pose — the window in which pan/zoom apply. FlatScreen gates the pan gesture on this. Also
    /// false while a scenario/story/encounter overlay is up (#2) so the map can't be panned behind it.</summary>
    internal bool MapActive => _mapBaseCapture && _mapDrivenValid && !IsScenarioOverlayActive();

    /// <summary>
    /// #2: is a full-screen scenario intro / story dialogue / city-or-road ENCOUNTER overlay up? While one
    /// is, the map must not stay visible behind it (screen should be filled) nor be pannable. Polls the
    /// game's own overlay owners: UIEventPanel (road/city event card) and StoryController (scenario/level
    /// story box, e.g. the "Schwarzgrab" card). Cheap; guarded so it never throws if a singleton is absent.
    /// </summary>
    private static bool IsScenarioOverlayActive()
    {
        try
        {
            if (global::Singleton<global::UIEventPanel>.IsInitialized
                && global::Singleton<global::UIEventPanel>.Instance.IsOpen)
                return true;
            if (global::Singleton<global::StoryController>.IsInitialized
                && global::Singleton<global::StoryController>.Instance.IsVisible)
                return true;
        }
        catch { /* singleton not ready / type shape changed — treat as no overlay */ }
        return false;
    }

    /// <summary>Start a grab-pan: remember the world point (on the focal plane y=0) under the press pixel.</summary>
    internal void BeginMapPan(Vector2 downPixel)
    {
        if (!MapActive || !TryMapPixelToPlane(downPixel, out Vector3 grab))
        {
            _mapPanning = false;
            return;
        }
        _mapPanGrabWorld = grab;
        _mapPanning = true;
        if (!_mapPanLogged)
        {
            _mapPanLogged = true;
            VRLog.Info("WorldUI", $"MAP PAN begin: grab world={grab} at pixel=({downPixel.x:F0},{downPixel.y:F0}).");
        }
    }

    /// <summary>Continue a grab-pan: move the focal point so the grabbed world point stays under the pointer.
    /// The whole camera rig translates rigidly with the focal point (horizontal, plane y=0 fixed), so the
    /// world point under a fixed pixel moves 1:1 with the focal point → this converges in a single step.</summary>
    internal void UpdateMapPan(Vector2 pixel)
    {
        if (!_mapPanning || !MapActive)
            return;
        CameraController? cc = CameraController.s_CameraController;
        if (cc == null)
            return;
        if (!TryMapPixelToPlane(pixel, out Vector3 curHit))
            return;
        Vector3 delta = _mapPanGrabWorld - curHit; // move focal so grab returns under the pointer
        delta.y = 0f;
        Vector3 nf = cc.m_TargetFocalPoint + delta;
        nf.y = 0f;
        // Clamp to the map's focal bounds (encapsulates the party/scenario/village positions) so the
        // parchment can't be dragged off into the void. Bounds may be degenerate early — skip then.
        Bounds fb = cc.m_FocalBounds;
        if (fb.size.x > 0.01f && fb.size.z > 0.01f)
        {
            nf.x = Mathf.Clamp(nf.x, fb.min.x, fb.max.x);
            nf.z = Mathf.Clamp(nf.z, fb.min.z, fb.max.z);
        }
        cc.m_TargetFocalPoint = nf;
        cc.m_FocalPoint = nf; // keep the game's lerp target and current in sync (LateUpdate is skipped in VR)
    }

    /// <summary>End the pan gesture.</summary>
    internal void EndMapPan()
    {
        _mapPanning = false;
    }

    /// <summary>Reconstruct the ray for an RT pixel through our DRIVEN map-camera pose (pos/look/fov + RT
    /// aspect — identical to what the game's m_Camera projects markers/clicks with) and intersect the
    /// focal plane y=0. This is a manual ScreenPointToRay: it depends only on the cached driven pose, so
    /// it is immune to when the real camera transform actually gets driven during the render loop.</summary>
    private bool TryMapPixelToPlane(Vector2 pixel, out Vector3 world)
    {
        world = Vector3.zero;
        if (!_mapDrivenValid || _leftRt == null)
            return false;
        float w = _leftRt.width, h = _leftRt.height;
        if (w < 1f || h < 1f)
            return false;
        Vector3 fwd = _mapDrivenLook - _mapDrivenPos;
        if (fwd.sqrMagnitude < 1e-6f)
            return false;
        fwd.Normalize();
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        float aspect = w / h;
        float tanV = Mathf.Tan(_mapDrivenFov * 0.5f * Mathf.Deg2Rad);
        float nx = pixel.x / w * 2f - 1f;   // pixel is bottom-origin (local.y up), matching Unity screen coords
        float ny = pixel.y / h * 2f - 1f;
        Vector3 dir = (fwd + right * (nx * tanV * aspect) + up * (ny * tanV)).normalized;
        if (Mathf.Abs(dir.y) < 1e-5f)
            return false;
        float t = (0f - _mapDrivenPos.y) / dir.y; // intersect plane y = 0 (focal plane)
        if (t <= 0f)
            return false;
        world = _mapDrivenPos + dir * t;
        return true;
    }

    /// <summary>
    /// Replicate the game's map-camera pose (CameraController.RefreshFocusPosition) so our forward camera
    /// frames the map exactly like flat (the mod prefix-skips CameraController.LateUpdate in VR, so the
    /// game never positions its own map camera). pos = focal + horizontal radius offset, y from zoom;
    /// look at focal + up*focusHeight. Returns false if the captured radius/height are degenerate.
    /// </summary>
    private bool TryComputeGameMapPose(out Vector3 pos, out Vector3 lookTarget, out float fov)
    {
        pos = Vector3.zero; lookTarget = Vector3.zero; fov = 50f;
        CameraController? cc = CameraController.s_CameraController;
        if (cc == null || cc.m_Camera == null)
            return false;
        Vector3 focal = cc.m_TargetFocalPoint; focal.y = 0f; // PAN center (party token / clicked location)
        float radius = cc.m_CameraDefaultRadius;
        float baseH = cc.m_InitialCameraHeight;               // authored map-camera height (fixed ~80° pitch with radius)
        Vector3 diff = cc.m_CameraToFocalTargetDiff;          // horizontal offset, |diff| == radius
        // ZOOM = CAMERA FOV, exactly like flat. The camera keeps the scene pose (fixed radius + height);
        // only FOV changes, so the DEFAULT (world 60° / city 80°) frames the map 1:1 with the flat game.
        float defFov = MapDefaultFov;
        if (_mapFov <= 0f)
            _mapFov = defFov;
        fov = _mapFov;
        if (!_mapPoseLogged)
        {
            _mapPoseLogged = true;
            VRLog.Info("WorldUI", $"MAP POSE (CameraController): map={(_activeMapIsCity ? "CITY" : "WORLD")} focal(target)={cc.m_TargetFocalPoint} FocusPoint={cc.FocusPoint} " +
                                  $"radius={radius:F2} baseHeight={baseH:F2} zoomExtra={cc.m_ZoomOutExtraHeight:F2} " +
                                  $"gameZoom(target)={cc.Zoom:F1} defFOV={cc.m_DefaultFOV:F1} minFOV={cc.m_MinimumFOV:F1} " +
                                  $"diff={diff} |diff|={diff.magnitude:F2} modFov={fov:F1} defaultFov={defFov:F0} focusPtH={cc.m_FocusPointHeight:F2} " +
                                  $"gameCamPos={cc.m_Camera.transform.position}.");
        }
        if (radius < 0.5f || baseH < 0.5f || diff.sqrMagnitude < 0.01f)
            return false; // degenerate captured values → caller uses the top-down fallback
        // Height ramp: flat holds y = m_InitialCameraHeight until you zoom OUT past the default, then adds
        // up to ZoomOutExtraHeight (pull back-and-up). Replicate proportionally in FOV so zooming out past
        // flat's range keeps pulling the camera up instead of only widening the lens.
        float extraH = 0f;
        if (_mapFov > defFov && MapMaxFov > defFov)
            extraH = (_mapFov - defFov) / (MapMaxFov - defFov) * Mathf.Max(0f, cc.m_ZoomOutExtraHeight);
        Vector3 hdiff = new Vector3(diff.x, 0f, diff.z); // fixed radius (flat pose — no dolly)
        pos = focal + hdiff; pos.y = baseH + extraH;
        lookTarget = focal + Vector3.up * cc.m_FocusPointHeight;
        // Cache for driving the real game map camera (so its marker projection + click raycasts align).
        _mapDrivenPos = pos; _mapDrivenLook = lookTarget; _mapDrivenFov = fov; _mapDrivenValid = true;
        return true;
    }

    private void ReconcileAlbedoCamera(Camera? mapSource)
    {
        if (!_mapBaseCapture || mapSource == null || !EnsureAlbedoReady())
        {
            if (_mapAlbedoCam != null && _mapAlbedoCam.enabled)
                _mapAlbedoCam.enabled = false;
            return;
        }

        Camera cam = _mapAlbedoCam!;
        TickMapInput(); // right-stick zoom (updates _mapFov before the pose is computed)
        _mapSourceCam = mapSource; // OnPreRenderCamera captures its render-time matrices for our camera
        // Copy the game map camera's live world pose (the captured matrices override this before culling).
        _mapAlbedoTransform!.SetPositionAndRotation(mapSource.transform.position, mapSource.transform.rotation);
        cam.clearFlags = CameraClearFlags.SolidColor;
        // DIAGNOSTIC (MapDiagClearColor): clear to bright MAGENTA instead of black. This is the
        // unambiguous test of whether our forward render is the effective last writer of the shared
        // base RT: if the map area shows magenta (with our mesh on top), our render LANDS and wins →
        // the bug is the UV feeding MapUnlit. If it stays brown, our output does NOT survive (the
        // deferred MapCamera co-writing _leftRt wins) → the fix is a private RT. Set false to restore.
        cam.backgroundColor = MapDiagClearColor ? new Color(1f, 0f, 1f, 1f) : Color.black;
        // See the SAME layers the game map camera sees (includes the parchment layer). Other objects on
        // those layers render nothing in a forward camera (their deferred materials have no forward pass);
        // only the parchment draws, because we override ITS materials with the forward MapUnlit shader.
        cam.cullingMask = mapSource.cullingMask;
        LogMapSceneRenderers(mapSource.cullingMask);
        // Just above the game map camera so Unity composites us LAST into the base RT (we overwrite its
        // black deferred render); still below the head camera, so the screen quad samples this frame's result.
        cam.depth = mapSource.depth + 0.1f;
        cam.rect = mapSource.rect;
        cam.allowHDR = mapSource.allowHDR;
        cam.allowMSAA = mapSource.allowMSAA;
        cam.useOcclusionCulling = mapSource.useOcclusionCulling;
        // Forward — MapUnlit is an unlit forward shader; no deferred resolve needed (and the game's
        // deferred parchment shader would never light into an off-screen RT we own).
        cam.renderingPath = RenderingPath.Forward;
        if (MapDiagTopDown && _worldMapRenderer != null)
        {
            // Frame the map ourselves with an ORTHOGRAPHIC top-down camera (the game's own MapCamera pose
            // re-renders the mesh flat). To MATCH the game's live pan/zoom/rotation (so the game's on-map
            // markers + click hit-testing, which use MapCamera world↔screen, line up), fit our ortho to the
            // exact map-plane region the game camera currently sees: intersect the game camera's viewport
            // rays with the parchment plane, then center + size + orient the ortho to that footprint.
            Bounds wb = _worldMapRenderer.bounds;
            float aspect = (_leftRt != null && _leftRt.height > 0) ? (float)_leftRt.width / _leftRt.height : 1.7778f;
            float planeY = wb.center.y;
            bool matched = false;
            if (MapMatchGameFraming
                && TryComputeGameMapPose(out Vector3 gPos, out Vector3 gLook, out float gFov))
            {
                // Replicate the game's map-camera pose (CameraController.RefreshFocusPosition): a PERSPECTIVE
                // camera above the party-token focal point at the captured radius/height, fixed ~80° pitch,
                // FOV = the game's live zoom. Matches flat's pan/zoom/rotation 1:1 — and since the game's
                // marker projection + click raycasts run through this same pose (patched to our camera),
                // they line up. We drive OUR camera (not the game MapCamera, which the VR rig anchors to).
                cam.orthographic = false;
                cam.fieldOfView = gFov;
                cam.aspect = aspect;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = Mathf.Max(500f, gPos.y * 2f + 200f);
                _mapAlbedoTransform!.SetPositionAndRotation(gPos, Quaternion.LookRotation((gLook - gPos).normalized, Vector3.up));
                cam.ResetWorldToCameraMatrix();
                cam.ResetProjectionMatrix();
                matched = true;
            }
            if (!matched)
            {
                // Fallback: frame the whole mesh bounds top-down (whole map visible).
                Vector3 wc = wb.center;
                float orthoSize = Mathf.Max(wb.size.z * 0.5f, (wb.size.x * 0.5f) / aspect) * 1.02f;
                float height = wb.size.y * 0.5f + Mathf.Max(50f, orthoSize);
                _mapAlbedoTransform!.SetPositionAndRotation(new Vector3(wc.x, wc.y + height, wc.z),
                                                            Quaternion.Euler(90f, 0f, 0f));
                cam.orthographic = true;
                cam.orthographicSize = orthoSize;
                cam.aspect = aspect;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = height + wb.size.y + 10f;
                cam.ResetWorldToCameraMatrix();
                cam.ResetProjectionMatrix();
            }
            _capMapValid = false; // OnPreCull/OnPreRender must NOT overwrite with the game's grazing matrices
        }
        else
        {
            cam.nearClipPlane = mapSource.nearClipPlane;
            cam.farClipPlane = mapSource.farClipPlane;
            cam.orthographic = mapSource.orthographic;
            cam.orthographicSize = mapSource.orthographicSize;
            cam.fieldOfView = mapSource.fieldOfView;
            // Seed the matrices from the game camera; OnPreCull/OnPreRender re-apply the captured
            // render-time matrices (the map CameraController sets them at render time; Tick values stale).
            cam.projectionMatrix = mapSource.projectionMatrix;
            cam.worldToCameraMatrix = mapSource.worldToCameraMatrix;
        }
        // Render into our PRIVATE map RT (nothing else writes it), NOT the shared base RT the deferred
        // MapCamera co-writes — so our forward render is the sole, surviving content.
        EnsureMapRt();
        RenderTexture mapTarget = _mapRt != null ? _mapRt : _leftRt!;
        if (cam.targetTexture != mapTarget)
            cam.targetTexture = mapTarget;
        if (!cam.enabled)
            cam.enabled = true;
        if (!_mapTargetLogged)
        {
            _mapTargetLogged = true;
            VRLog.Info("WorldUI", $"MAP RENDER camera target = '{(cam.targetTexture != null ? cam.targetTexture.name : "null")}' " +
                                  $"(private _mapRt={(_mapRt != null ? _mapRt.name : "null")}, base _leftRt={(_leftRt != null ? _leftRt.name : "null")}, " +
                                  $"targetIsPrivate={cam.targetTexture == _mapRt}, clearOnlyDiag={MapDiagClearOnly}, clear={cam.backgroundColor})");
        }

        if (!_mapMirrorLogged)
        {
            _mapMirrorLogged = true;
            int n = _worldMapOverrideMats != null ? _worldMapOverrideMats.Length : 0;
            VRLog.Info("WorldUI", $"MAP RENDER (real mesh + MapUnlit): shader loaded={(MapUnlitShader() != null)}, " +
                                  $"{n} submesh materials, active={(_activeMapIsCity ? "CITY" : "WORLD")} renderer " +
                                  $"'{(_worldMapRenderer != null ? _worldMapRenderer.name : "?")}', " +
                                  $"localBounds min=({_mapLocalMin.x:F2},{_mapLocalMin.y:F2},{_mapLocalMin.z:F2}) " +
                                  $"size=({_mapLocalSize.x:F2},{_mapLocalSize.y:F2},{_mapLocalSize.z:F2}) planeAxes=XZ; " +
                                  $"quadrant UV offsets NW={MapQuadrantUvOffset[0]} NE={MapQuadrantUvOffset[1]} " +
                                  $"SW={MapQuadrantUvOffset[2]} SE={MapQuadrantUvOffset[3]} (scale (2,2)); " +
                                  $"forward camera clones '{mapSource.name}' (depth {cam.depth:F1}, mask 0x{cam.cullingMask:X8}, " +
                                  "render-time matrices applied in OnPreCull/OnPreRender).");
        }
    }

    // ---- MAP RENDER: the REAL parchment mesh, forward, textured with GloomhavenVR/MapUnlit ----
    // The definitive fix (class doc MAP RENDER): a mod-owned FORWARD camera (_mapAlbedoCam) renders
    // the ACTUAL campaign-map mesh (MapChoreographer.worldMap / cityMap) into the flat-screen base RT,
    // using the game map camera's captured render-time view+projection so it tracks pan/zoom 1:1. The
    // mesh is drawn via a temporary MATERIAL OVERRIDE (one GloomhavenVR/MapUnlit material per submesh,
    // scoped to exactly our render in onPreRender/onPostRender — the game's own mesh/materials are
    // never mutated, so it is rendering-only and multiplayer-safe). MapUnlit computes the UV on the
    // GPU from OBJECT-space vertex position (the mesh is isReadable=false with no usable UVs) and
    // remaps the mesh-wide 0..1 to each quadrant's own texture via per-material _UvScale/_UvOffset.

    /// <summary>Gathered GH_CampaignMap quadrant textures: [0]=01(NW) [1]=02(NE) [2]=03(SW) [3]=04(SE).</summary>
    private Texture[]? _mapQuadTextures;
    private bool _mapTexLogged;

    // ---- HARD-CODED per-quadrant UV offset table (class doc MAP RENDER). MapUnlit computes a
    // mesh-wide UV = (objPos.xz - localMin.xz)/localSize.xz in 0..1, then remaps it to this submesh's
    // own quadrant texture via uv*_UvScale + _UvOffset with _UvScale = (2,2). The offset selects the
    // quarter, indexed by the quadrant number parsed from the material name (0=01=NW, 1=02=NE,
    // 2=03=SW, 3=04=SE). Assumes local +X = East (u right) and local +Z = North (v up), texture
    // painted North-up/East-right. If a hardware screenshot shows the map mirrored/rotated, flip the
    // matching offset row here and rebuild the bundle-independent material set (logged on engage).
    //   SW (03) samples local uv [0,0.5]x[0,0.5]   → offset ( 0, 0)
    //   NW (01) samples local uv [0,0.5]x[0.5,1]   → offset ( 0,-1)
    //   SE (04) samples local uv [0.5,1]x[0,0.5]   → offset (-1, 0)
    //   NE (02) samples local uv [0.5,1]x[0.5,1]   → offset (-1,-1)
    private static readonly Vector2[] MapQuadrantUvOffset =
    {
        new Vector2( 0f, -1f), // 0 = 01 = NW
        new Vector2(-1f, -1f), // 1 = 02 = NE
        new Vector2( 0f,  0f), // 2 = 03 = SW
        new Vector2(-1f,  0f), // 3 = 04 = SE
    };

    // ==== MAP BISECTION SWITCHES ================================================================
    // The five `false` consts below (MapUvDebug, MapDiagClearColor, MapDiagPerSubmeshChannel,
    // MapDiagClearOnly, MapIconsSolidTest) are COMPILE-TIME bisection switches left over from the
    // ~30-commit campaign-map hunt: each one is what disambiguated a single hypothesis on hardware.
    // They are deliberately `const` so a disabled branch contributes NOTHING to the shipped DLL —
    // which is also why three of them raise CS0162 and carry a site-scoped
    // `#pragma warning disable CS0162` at their use site. MapDiagClearColor and
    // MapDiagPerSubmeshChannel raise no warning purely because they are consumed inside TERNARIES
    // rather than statements; that is an accident of expression form, not a difference in kind.
    // The suppressions are site-scoped ON PURPOSE: a file-wide disable in a 3 700-line file would
    // hide a genuine unreachable-code bug added later.
    //
    // Usage: flip ONE to true, rebuild the DLL (no AssetBundle rebuild needed), take one hardware
    // screenshot, flip it back. Do not convert them to properties (a property is not a compile-time
    // constant, so the branch would start shipping) or to config entries (runtime-flippable = a
    // behaviour change, and the map's config surface is being made honest, not re-armed).
    //
    // NOT diagnostics, do not confuse them with these: MapDiagTopDown, MapMatchGameFraming,
    // MapDriveGameCamera and MapDrawIcons below are all `true` and SELECT SHIPPING BEHAVIOUR —
    // their disabled arms are the previous, disproven implementations. Flipping one of those is a
    // behaviour change (Tier 3). `if (!MapDiagTopDown && <non-const>)` escapes CS0162 only because
    // `false && x` is not a constant expression — again an accident of form.
    // ============================================================================================

    // ---- UV DIAGNOSTIC (pure DLL, no bundle rebuild) ----
    // When true, every MapUnlit submesh samples a GENERATED "UV read-out" texture instead of its real
    // albedo, with _UvScale=(1,1) _UvOffset=(0,0) so the RAW mesh-wide UV field is shown. The texture
    // encodes the coordinate directly: R = u, G = v (continuous ramp), with a coarse 8×8 dark checker to
    // count tiles, a thick white 0..1 border, a solid MAGENTA block at the (0,0) origin corner and a
    // solid CYAN block at the (1,1) corner. wrapMode=Repeat, so any UV outside 0..1 shows a hard
    // red/green wrap discontinuity — that pins where the 0..1 seams fall per quadrant. From one hardware
    // screenshot this fixes orientation (which screen direction is +u / +v), the covered range, and the
    // per-submesh seam layout, from which the exact _UvScale/_UvOffset (or a needed flip) is computed.
    // Set to false to restore the real map textures.
    private const bool MapUvDebug = false;
    // DIAGNOSTIC: clear the map forward camera to bright magenta (see ReconcileAlbedoCamera).
    private const bool MapDiagClearColor = false;
    // OFFLINE-PROVEN: the mesh's UV0 + the GH_CampaignMap_0N textures rasterize to the full correct map
    // (scratchpad render). So UV0 is the albedo UV and the content is right — any runtime flatness is a
    // framing/plumbing bug, tracked via the MAP RENDER NDC-extent diagnostic.
    // DIAGNOSTIC: assign each submesh a UV channel so one screenshot A/B-tests channels. TexCoord0 and
    // object-space projection both came back FLAT, so this now splits the visible mesh between the two
    // remaining candidates — TexCoord1 (submesh 0,2) and TexCoord2 (submesh 1,3), with the REAL texture:
    // wherever real map art appears, that channel is the albedo UV. (Pipeline confirmed working d92b15c4a.)
    private const bool MapDiagPerSubmeshChannel = false;
    // DIAGNOSTIC: skip the mesh override so only the camera clear renders (proved the pipeline works).
    private const bool MapDiagClearOnly = false;
    // The captured game MapCamera pose is degenerate for re-rendering (grazes the y≈0 190×237 plane →
    // flat). When true, frame the map ourselves with an orthographic top-down camera fitted to the mesh
    // bounds (proven-correct content) — validates the pipeline + makes the whole map visible; game
    // pan/zoom matching comes after. See ReconcileAlbedoCamera.
    private const bool MapDiagTopDown = true;
    // When true, the top-down ortho tracks the game camera's live map-plane footprint (pan/zoom/rotation)
    // so the game's on-map markers + click hit-testing line up; false = static whole-map top-down.
    private const bool MapMatchGameFraming = true;
    private Texture2D? _uvDebugTex;
    // Which mesh UV channel the MapUnlit GPU shader samples the albedo from (0=TexCoord0 default,
    // 1/2 fallback). The mesh carries TexCoord0/1/2 (dim2) — TexCoord0 is the standard albedo channel.
    // HARD-CODED deliberately (the old MapUvChannel config key, deleted, could hold a stale value)
    // and drives MapUnlit's _UvChannel, so trying another channel is a one-line change, no bundle rebuild.
    private const float MapUnlitUvChannel = 0f;
    /// <summary>One-shot guard for the MAP MESH layout / material-ST dump.</summary>
    private bool _meshLayoutLogged;

    /// <summary>One-shot guard: the MAP RENDER engaged line (per engagement).</summary>
    private bool _mapMirrorLogged;
    /// <summary>The parchment mesh's LOCAL bounds min/size (meshFilter.sharedMesh.bounds) — the MapUnlit UV base.</summary>
    private Vector3 _mapLocalMin;
    private Vector3 _mapLocalSize = Vector3.one;
    /// <summary>The game map camera we mirror; used to capture its render-time view/projection in OnPreRenderCamera.</summary>
    private Camera? _mapSourceCam;
    private Matrix4x4 _capMapView;
    private Matrix4x4 _capMapProj;
    private bool _capMapValid;
    private bool _capLogged;
    /// <summary>One-shot guard for the MAP POSE (CameraController fields) log.</summary>
    private bool _mapPoseLogged;
    // ---- map camera pose driving (match flat: pan via focal point, zoom via FOV) ----
    /// <summary>When true, drive the real game map camera (CameraController.m_Camera) to our computed
    /// pose so the game's marker projection + click raycasts (which use that camera) line up with our
    /// render. The VR rig only reads m_Camera as a build-time anchor (never follows it), so this is safe.</summary>
    private const bool MapDriveGameCamera = true;
    // FOV-based zoom, exactly like flat (the game's zoom IS camera FOV; MapChoreographer.SetMapConfig
    // pushes MapConfig.DefaultFOV → ResetZoomTo → m_Camera.fieldOfView). Values extracted offline from
    // the Campaign WorldMapConfig / CityMapConfig assets. The camera stays at the scene pose (fixed
    // radius, height = m_InitialCameraHeight) and only FOV changes — so the DEFAULT frames 1:1 with flat.
    private const float MapWorldDefaultFov = 60f; // WorldMapConfig.DefaultFOV (resting zoom at open)
    private const float MapWorldMinFov = 50f;     // WorldMapConfig.MinimumFOV (most zoomed IN)
    private const float MapCityDefaultFov = 80f;  // CityMapConfig.DefaultFOV
    private const float MapCityMinFov = 70f;      // CityMapConfig.MinimumFOV
    // The user wants to zoom OUT further than flat allows (flat MaxFOV is only 70/80). Extend the
    // zoom-out clamp well past flat, and add camera height (ZoomOutExtraHeight) as FOV widens so it
    // pulls back-and-up like flat's zoom-out, not just fisheyes in place.
    private const float MapWorldMaxFov = 105f;
    private const float MapCityMaxFov = 110f;
    private const float MapZoomSpeed = 40f;       // deg/s at full stick (flat ZoomWheelSpeed = 40)
    private const float MapStickDeadzone = 0.15f;
    /// <summary>Mod-managed map zoom AS CAMERA FOV (the game's zoom LateUpdate is prefix-skipped in VR).
    /// 0 = uninitialised → set to the active map's default FOV on the next pose. Reset on world↔city switch.</summary>
    private float _mapFov;
    private float MapDefaultFov => _activeMapIsCity ? MapCityDefaultFov : MapWorldDefaultFov;
    private float MapMinFov => _activeMapIsCity ? MapCityMinFov : MapWorldMinFov;
    private float MapMaxFov => _activeMapIsCity ? MapCityMaxFov : MapWorldMaxFov;
    private Vector3 _mapDrivenPos, _mapDrivenLook;
    private float _mapDrivenFov = 50f;
    private bool _mapDrivenValid;
    // ---- map PAN (laser + trigger grab-drag) ----
    /// <summary>True while a trigger-drag pan gesture is in progress (FlatScreen drives Begin/Update/End).</summary>
    private bool _mapPanning;
    /// <summary>World point on the map's focal plane (y=0) that was under the pointer when the pan grab began.
    /// Each tick the focal point is moved so THIS same world point stays pinned under the (moving) pointer.</summary>
    private Vector3 _mapPanGrabWorld;
    private bool _mapPanLogged;
    // ---- map location icons (deferred Decalicious decals → forward quads in our camera) ----
    /// <summary>Draw each MapLocation's decal icon as a textured quad flat on the map plane, ONLY in our
    /// forward map camera (the game's decals are deferred and never light into our RT).</summary>
    private const bool MapDrawIcons = true;
    /// <summary>DIAGNOSTIC: draw solid magenta quads instead of the icon texture to prove geometry renders.</summary>
    private const bool MapIconsSolidTest = false;
    private Mesh? _iconQuad;
    private Material? _iconMat;
    private MaterialPropertyBlock? _iconMpb;
    /// <summary>Command buffer that draws the location icons AFTER the wind, on a freshly CLEARED depth
    /// buffer — so the wind/cloud particles (which write depth) can never occlude the icons. Render-queue
    /// ordering alone was not enough: the cloud particles have ZWrite on and, drifting above the icon
    /// plane, depth-rejected the icons wherever a cloud passed (the "wind streak through the icon").</summary>
    private CommandBuffer? _iconCmd;
    private Camera? _iconCmdCam;
    // ---- wind/cloud particle dimming (drifting streaks over the map) ----
    // The map's Wind/Clouds ambiance particles render as thick, over-prominent translucent streaks in our
    // forward capture (the game's map camera post-processes/masks them; ours doesn't). We KEEP the wind but
    // scale down its emitted alpha so it's subtle (user: keep it, just not thick streaks). Reversible.
    /// <summary>Particle systems we dimmed + their ORIGINAL startColor (index-aligned), for exact restore.</summary>
    private readonly System.Collections.Generic.List<ParticleSystem> _tunedWindParticles = new();
    private readonly System.Collections.Generic.List<ParticleSystem.MinMaxGradient> _tunedWindOriginal = new();
    /// <summary>The map root we last scanned for wind particles (re-scan on world↔city switch).</summary>
    private GameObject? _windScannedRoot;
    private int _windScanAttempts;
    private int _windNextScanFrame;
    private const int WindScanMaxAttempts = 20;   // retry window for late-spawning particles
    private const int WindScanRetryFrames = 15;   // ~ every 15 frames until found (or attempts exhausted)
    private static readonly int IconMainTex = Shader.PropertyToID("_MainTex");
    private static readonly int IconColor = Shader.PropertyToID("_Color");
    // Decal type is in an unreferenced assembly (ThreeEyedGames Decalicious) — reach it via reflection.
    private static System.Type? _decalType;
    private static System.Reflection.PropertyInfo? _decalCurMatProp;
    private bool _decalTypeMissing;
    private int _mapIconsLogCount;
    /// <summary>One warn line per map entry when the scene-wide decal fallback engaged.</summary>
    private bool _iconFallbackLogged;
    /// <summary>Per-map-entry budget for the MAP ICON GEOMETRY dump (re-armed in <see cref="EngageMapCore"/>).</summary>
    private const int IconGeomDumpMax = 4;
    /// <summary>MAP ICON GEOMETRY dumps emitted this map entry.</summary>
    private int _iconGeomDumps;
    /// <summary>Drawn-icon count at the last geometry dump — a CHANGED count (icons materialize over the
    /// first scans; decal materials load async) re-dumps, an unchanged one stays silent.</summary>
    private int _iconGeomLastDrawn = -1;
    /// <summary>Periodic-sample counter + last frame for the MAP RENDER NDC/geometry diagnostic.</summary>
    private int _ndcLogCount;
    private int _ndcLastLogFrame = int.MinValue;

    // ---- GloomhavenVR/MapUnlit shader (bundled — Shader.Find can't see bundle shaders, so probed
    // across the loaded AssetBundles, mirroring PlayTray.BoardLitShader). ----
    // The asset path lives in Core.BundleShaders now — one table, checked against the real
    // unity/.../Assets/Bundle/**.shader files by the BundledShaderVectors wire test, so a renamed
    // or re-cased asset fails a gate instead of failing silently at runtime.
    private static Shader? _mapUnlitShader;

    /// <summary>
    /// Load the bundled <c>GloomhavenVR/MapUnlit</c> shader. <c>Shader.Find</c> does NOT see shaders
    /// that live only inside an AssetBundle (no bundle PREFAB material references MapUnlit, unlike
    /// BoardLit), so probe every loaded AssetBundle for the shader asset (same pattern as
    /// <see cref="Cards.PlayTray.OverlayShader"/>). Cached; logged once each way. Null only when the
    /// bundle lacks it (then the map render is skipped and the base RT is left as-is).
    /// </summary>
    private static Shader? MapUnlitShader()
    {
        // The find-then-probe-the-bundles logic that used to be inlined here now lives in
        // Core.BundleShaders (which also owns the asset path). It ALSO drops the one-shot
        // `_mapUnlitShaderProbed` latch this method used to carry: a single miss at startup latched
        // the null for the life of the process, so a bundle that finished loading a frame later
        // could never be picked up. BundleShaders caches successes only.
        _mapUnlitShader ??= BundleShaders.Resolve(
            "GloomhavenVR/MapUnlit", "WorldUI",
            "MAP RENDER: the real campaign-map mesh is drawn forward, textured, tracking the game "
            + "pan/zoom.",
            "MAP RENDER: the campaign map cannot be textured; the base RT is left as-is (map black).");
        return _mapUnlitShader;
    }

    /// <summary>
    /// Read the four <c>GH_CampaignMap_0N</c> quadrant textures off the worldMap renderer's materials
    /// (by the number in the material name), indexed by quadrant [0]=01(NW)..[3]=04(SE). Each quadrant's
    /// 4096² albedo becomes the <c>_MainTex</c> of that submesh's MapUnlit override material.
    /// </summary>
    private void GatherMapTextures()
    {
        if (_mapQuadTextures != null || _worldMapRenderer == null)
            return;
        var quads = new Texture[4];
        Material[] mats = _worldMapRenderer.sharedMaterials;
        var sb = new System.Text.StringBuilder();
        foreach (Material m in mats)
        {
            if (m == null)
                continue;
            // The quadrant index lives in the MATERIAL name's "0N" suffix (world: GH_CampaignMap_0N_MAT,
            // city: GH_City*_0N). The albedo is on _Alb for the "_New" modded materials, else _MainTex.
            int idx = QuadrantIndexFromName(m.name);
            if (idx < 0 || idx >= 4 || quads[idx] != null)
                continue;
            Texture? tex = (m.HasProperty("_Alb") ? m.GetTexture("_Alb") : null)
                           ?? (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null)
                           ?? m.mainTexture;
            if (tex == null)
                continue;
            quads[idx] = tex;
            sb.Append($"\n  quad[{idx}] (0{idx + 1}) = '{tex.name}' {tex.width}x{tex.height} from '{m.name}'");
        }
        _mapQuadTextures = quads;
        if (!_mapTexLogged)
        {
            _mapTexLogged = true;
            int have = 0;
            for (int i = 0; i < 4; i++) if (quads[i] != null) have++;
            VRLog.Info("WorldUI", $"MAP MIRROR textures ({(_activeMapIsCity ? "CITY" : "WORLD")} map): {have}/4 " +
                                  "quadrant textures gathered off " +
                                  $"'{(_worldMapRenderer != null ? _worldMapRenderer.name : "?")}' — mapped onto four " +
                                  "world-space quads (01=NW, 02=NE, 03=SW, 04=SE) at the map mesh's world position." + sb);
        }
    }

    /// <summary>
    /// Ensure the worldMap renderer, override materials and mod camera all exist for the albedo
    /// render (built lazily once the map capture is engaged; retried each tick until the scene's
    /// worldMap is available). Returns false (with a one-shot WARN) if the map is not renderable.
    /// </summary>
    private bool EnsureAlbedoReady()
    {
        if (_root == null || _leftRt == null)
            return false;

        // ISSUE 3: detect a world↔city switch every tick (it does NOT change scene) and invalidate the
        // cached renderer/textures so we re-find + re-gather off the now-active map object.
        DetectActiveMap();

        if (_worldMapRenderer == null && !FindWorldMapRenderer())
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP RENDER: no MapChoreographer.worldMap MeshRenderer with a " +
                                      "GH_CampaignMap material found in the scene — the base RT is left as-is " +
                                      "(the map stays black). Retrying each tick while the map is showing.");
            }
            else if (!_albedoFailDumpLogged && ++_albedoFailTicks >= AlbedoFailTicksForDump)
            {
                // The retry loop above is real (this method runs every tick via ReconcileAlbedoCamera)
                // but it retried SILENTLY forever: a log that ends on the one-shot warning cannot say
                // WHY nothing was ever found. Name the discrepancy once per engagement.
                _albedoFailDumpLogged = true;
                LogMapAcquisitionCandidates();
            }
            return false;
        }

        // Gather the 4 quadrant albedo textures off the renderer, then build ONE GloomhavenVR/MapUnlit
        // override material per submesh (mesh.bounds gives the LOCAL X–Z plane extent even though the mesh
        // is isReadable=false). The materials are swapped onto the real parchment renderer only for our
        // forward camera's render (ApplyWorldMapOverride/RestoreWorldMapOverride).
        GatherMapTextures();
        if (!BuildOverrideMaterials())
            return false;
        EnsureAlbedoCamera();
        return _mapAlbedoCam != null;
    }

    /// <summary>
    /// Resolve the ACTIVE campaign map GameObject (ISSUE 3): MapChoreographer toggles worldMap/cityMap
    /// via SetActive when the player opens the city vs world map. Prefer whichever is activeInHierarchy;
    /// fall back to UIGuildmasterHUD.CurrentMode, then to worldMap. Returns null if MapChoreographer is
    /// absent. If the active map changed since the last gather, invalidate the cached renderer/textures
    /// so the next EnsureAlbedoReady re-finds and re-gathers (a world↔city switch does NOT change scene,
    /// so ReleaseAlbedo alone never fires).
    /// </summary>
    private void DetectActiveMap()
    {
        global::MapChoreographer choreo = Object.FindObjectOfType<global::MapChoreographer>();
        if (choreo == null)
            return;
        GameObject? world = choreo.worldMap; // publicized private serialized field
        GameObject? city = choreo.cityMap;   // publicized private serialized field

        GameObject? active = null;
        bool isCity = false;
        if (city != null && city.activeInHierarchy) { active = city; isCity = true; }
        else if (world != null && world.activeInHierarchy) { active = world; isCity = false; }
        else
        {
            // Neither is active in the hierarchy yet (mid-transition) — fall back to the HUD mode.
            bool hudCity = global::Singleton<global::UIGuildmasterHUD>.IsInitialized
                           && global::Singleton<global::UIGuildmasterHUD>.Instance.CurrentMode == global::EGuildmasterMode.City;
            if (hudCity && city != null)
            { active = city; isCity = true; }
            else if (world != null)
            { active = world; isCity = false; }
            else if (city != null)
            { active = city; isCity = true; }
        }
        if (active == null)
            return;

        if (!ReferenceEquals(active, _activeMapGo))
        {
            // Switched (world↔city, or first resolve): drop the stale renderer + textures so we re-gather.
            _activeMapGo = active;
            _activeMapIsCity = isCity;
            _mapFov = 0f; // re-init zoom to the NEW map's default FOV (world 60 / city 80)
            _worldMapRenderer = null;
            _mapQuadTextures = null;
            _mapTexLogged = false;
            VRLog.Info("WorldUI", $"MAP SELECT (ISSUE 3): active map = {(isCity ? "CITY" : "WORLD")} " +
                                  $"('{active.name}') — renderer + quadrant textures will be re-gathered.");
        }
    }

    /// <summary>Reflect the decompiled campaign-map parchment renderer off the ACTIVE map GO (ISSUE 3).</summary>
    private bool FindWorldMapRenderer()
    {
        DetectActiveMap();
        GameObject? mapGo = _activeMapGo;
        if (mapGo == null)
            return false;

        // Find the renderer carrying the quadrant materials (name has a numeric 0N suffix, any prefix —
        // GH_CampaignMap_0N_MAT for the world map, GH_City*_0N for the city map). Fall back to the GO's
        // own MeshRenderer (the decompiled MapChoreographer uses worldMap.GetComponent<MeshRenderer>()).
        MeshRenderer[] rends = mapGo.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        MeshRenderer? found = null;
        for (int i = 0; i < rends.Length && found == null; i++)
        {
            Material[] mats = rends[i].sharedMaterials;
            for (int j = 0; j < mats.Length; j++)
            {
                if (mats[j] != null && QuadrantIndexFromName(mats[j].name) >= 0)
                {
                    found = rends[i];
                    break;
                }
            }
        }
        if (found == null)
            found = mapGo.GetComponent<MeshRenderer>();
        if (found == null)
            return false;

        _worldMapRenderer = found;
        return true;
    }

    /// <summary>Failed acquisition ticks since engage (drives the one-shot what-IS-there dump).</summary>
    private int _albedoFailTicks;
    /// <summary>One-shot guard (per engagement) for <see cref="LogMapAcquisitionCandidates"/>.</summary>
    private bool _albedoFailDumpLogged;
    /// <summary>Engaged-but-failing ticks before the acquisition-candidates dump (~3–4 s at 72–90 Hz).</summary>
    private const int AlbedoFailTicksForDump = 240;

    /// <summary>
    /// Decisive acquisition-failure diagnostic: the per-tick retry has been failing for
    /// <see cref="AlbedoFailTicksForDump"/> ticks after engage — log ONCE what map-like renderers and
    /// material names DO exist (≤10), so a failing log names the actual discrepancy (renamed
    /// quality-variant materials, a missing MapChoreographer, an inactive map GO) instead of only
    /// repeating "not found".
    /// </summary>
    private void LogMapAcquisitionCandidates()
    {
        var sb = new StringBuilder();
        global::MapChoreographer choreo = Object.FindObjectOfType<global::MapChoreographer>();
        if (choreo == null)
            sb.Append("MapChoreographer=NONE in the loaded scene(s)");
        else
            sb.Append($"MapChoreographer='{choreo.name}' worldMap={DescribeMapGo(choreo.worldMap)} " +
                      $"cityMap={DescribeMapGo(choreo.cityMap)}");
        int listed = 0, scanned = 0;
        foreach (MeshRenderer r in Object.FindObjectsOfType<MeshRenderer>(includeInactive: true))
        {
            scanned++;
            if (listed >= 10)
                continue; // keep counting scanned
            Material[] mats = r.sharedMaterials;
            bool mapLike = NameIsMapLike(r.name);
            for (int i = 0; i < mats.Length && !mapLike; i++)
                mapLike = mats[i] != null && NameIsMapLike(mats[i].name);
            if (!mapLike)
                continue;
            listed++;
            sb.Append($"\n  '{r.name}' active={r.gameObject.activeInHierarchy} mats=[");
            for (int i = 0; i < mats.Length; i++)
                sb.Append(i > 0 ? ", " : "").Append(mats[i] != null ? mats[i].name : "<null>");
            sb.Append(']');
        }
        VRLog.Warn("WorldUI", $"MAP RENDER acquisition still failing after {AlbedoFailTicksForDump} engaged " +
                              $"ticks — what IS there: {sb}\n  ({listed} map-like MeshRenderer(s) listed, " +
                              $"{scanned} scanned; 'map-like' = GO or material name contains " +
                              "map/campaign/city/parchment).");
    }

    private static string DescribeMapGo(GameObject? go) =>
        go == null ? "null" : $"'{go.name}' active={go.activeInHierarchy}";

    private static bool NameIsMapLike(string n) =>
        !string.IsNullOrEmpty(n)
        && (n.IndexOf("map", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("campaign", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("city", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("parchment", System.StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>Parse the quadrant index (0..3) from a material/texture name's "0N" suffix (N=1..4), or -1.</summary>
    private static int QuadrantIndexFromName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return -1;
        for (int k = 0; k + 1 < n.Length; k++)
        {
            if (n[k] == '0' && n[k + 1] >= '1' && n[k + 1] <= '4')
                return n[k + 1] - '1';
        }
        return -1;
    }

    /// <summary>
    /// Build (once, cached) the UV read-out texture used by the <see cref="MapUvDebug"/> diagnostic.
    /// Encodes the sampled UV directly as colour so a hardware screenshot reveals the object-space→UV
    /// mapping: R = u, G = v, dark 8×8 checker to count tiles, white 0..1 border, MAGENTA block at the
    /// (0,0) origin corner, CYAN block at the (1,1) corner. wrapMode = Repeat so out-of-range UV wraps
    /// with a hard red/green discontinuity that pins the 0..1 seams.
    /// </summary>
    private Texture2D UvDebugTexture()
    {
        if (_uvDebugTex != null)
            return _uvDebugTex;

        const int N = 256;
        const int cell = N / 8;   // 8×8 checker
        const int mark = 40;      // corner marker size (px)
        const int border = 6;     // white border thickness (px)
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                byte r = (byte)(x * 255 / (N - 1)); // u ramp
                byte g = (byte)(y * 255 / (N - 1)); // v ramp
                byte b = 40;
                // Coarse checker (darken alternate cells) to make the tile grid legible.
                if ((((x / cell) + (y / cell)) & 1) == 1)
                {
                    r = (byte)(r * 55 / 100);
                    g = (byte)(g * 55 / 100);
                    b = 22;
                }
                // 0..1 border (white), origin marker (magenta), far corner marker (cyan).
                if (x < border || x >= N - border || y < border || y >= N - border)
                    px[y * N + x] = new Color32(255, 255, 255, 255);
                else if (x < mark && y < mark)
                    px[y * N + x] = new Color32(255, 0, 255, 255);       // (0,0) origin
                else if (x >= N - mark && y >= N - mark)
                    px[y * N + x] = new Color32(0, 255, 255, 255);       // (1,1) corner
                else
                    px[y * N + x] = new Color32(r, g, b, 255);
            }
        }
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false, false)
        {
            name = "GloomhavenVR.UvDebug",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
        };
        t.SetPixels32(px);
        t.Apply(false, false);
        _uvDebugTex = t;
        return t;
    }

    /// <summary>One-shot guard for the MAP SCENE renderers enumeration.</summary>
    private bool _mapSceneRenderersLogged;

    /// <summary>
    /// Enumerate the 3D map-decoration renderers (location decals, city model, indicators, paths) under
    /// MapChoreographer's scenario/village parents so we can see WHY they don't appear in our forward
    /// render (they draw via the deferred map camera). Logs type/layer/shader/albedo-property grouped.
    /// </summary>
    private void LogMapSceneRenderers(int cullingMask)
    {
        if (_mapSceneRenderersLogged)
            return;
        _mapSceneRenderersLogged = true;
        var choreo = Object.FindObjectOfType<MapChoreographer>();
        if (choreo == null)
        {
            VRLog.Info("WorldUI", "MAP SCENE renderers: no MapChoreographer found.");
            return;
        }
        var roots = new List<Transform>();
        if (choreo.m_ScenariosParent != null) roots.Add(choreo.m_ScenariosParent.transform);
        if (choreo.m_VillagesParent != null) roots.Add(choreo.m_VillagesParent.transform);
        if (_activeMapGo != null && _activeMapGo.transform.parent != null) roots.Add(_activeMapGo.transform.parent);
        var sb = new StringBuilder();
        int total = 0;
        foreach (Transform root in roots)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (r == _worldMapRenderer) continue;
                total++;
                bool inMask = (cullingMask & (1 << r.gameObject.layer)) != 0;
                sb.Append($"\n  {r.name} [{r.GetType().Name}] L{r.gameObject.layer} mask={inMask} enabled={r.enabled} " +
                          $"worldPos={r.transform.position} boundsSize={r.bounds.size}");
                Material[] mats = r.sharedMaterials;
                sb.Append($" sharedMats={mats.Length}");
                for (int i = 0; i < mats.Length; i++)
                {
                    Material? m = mats[i];
                    if (m == null) { sb.Append($" [{i}]=null"); continue; }
                    string sh = m.shader != null ? m.shader.name : "<noshader>";
                    Texture? mt = m.mainTexture;
                    string alb = "none";
                    if (m.HasProperty("_Alb") && m.GetTexture("_Alb") != null) alb = "_Alb:" + m.GetTexture("_Alb")!.name;
                    else if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) alb = "_MainTex:" + m.GetTexture("_MainTex")!.name;
                    else if (mt != null) alb = "main:" + mt.name;
                    sb.Append($" [{i}]shader='{sh}' alb={alb} color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")}");
                }
                // MeshFilter mesh (for the icon geometry)
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    sb.Append($" mesh='{mf.sharedMesh.name}' verts={mf.sharedMesh.vertexCount}");
            }
        }
        VRLog.Info("WorldUI", $"MAP SCENE renderers [{total} total] (forwardMask=0x{cullingMask:X8}):" + sb.ToString());
    }

    /// <summary>
    /// Draw each active MapLocation's decal icon (a deferred Decalicious decal — invisible in our forward
    /// render) as a textured quad flat on the map plane, queued ONLY for our forward map camera. The icon
    /// texture + tint come from the decal's CurrentMaterial (_MainTex/_Color); the footprint from its
    /// renderer bounds. Uses Graphics.DrawMesh(..., camera) so nothing else in the scene is affected.
    /// </summary>
    /// <summary>
    /// Make the map's Wind/Clouds ambiance particles SUBTLE (not disabled — the user wants the wind kept)
    /// by scaling down their emitted alpha. They render as thick, over-prominent translucent streaks in
    /// our forward capture (the game's map camera post-processes/masks them; ours does not), and where a
    /// streak crosses a location icon it draws a see-through smear across it. The particle GameObjects are
    /// NOT children of the map root (a previous root-scoped scan found 0), so we search the whole scene by
    /// ParticleSystemRenderer material name (Map_Ambiance_*Wind*/*Cloud*) — the reliable identifier from
    /// the offline asset dump. We scale each system's MainModule startColor alpha (reversible: the original
    /// is stored and restored on disengage / map switch), which fades both new and (over their lifetime)
    /// existing particles. Waves and sparkles are left alone.
    /// </summary>
    private void TuneMapWindParticles()
    {
        // Committed once we've dimmed ≥1 system for the current map. The particle GameObjects can spawn a
        // few frames AFTER the map is selected, so if a scan finds none we RETRY (throttled, bounded) rather
        // than give up — otherwise a single early empty scan would miss the wind for the whole session.
        bool mapChanged = !ReferenceEquals(_windScannedRoot, _activeMapGo);
        if (mapChanged)
        {
            RestoreWindParticles();          // restore the previous map's particles
            _windScannedRoot = _activeMapGo;
            _windScanAttempts = 0;
            _windNextScanFrame = 0;
        }
        bool committed = _tunedWindParticles.Count > 0;
        if (committed || _windScanAttempts >= WindScanMaxAttempts || Time.frameCount < _windNextScanFrame)
            return;
        _windScanAttempts++;
        _windNextScanFrame = Time.frameCount + WindScanRetryFrames;
        {
            float opacity = Mathf.Clamp01(WorldUIConfig.MapWindOpacity.Value);
            int n = 0, scanned = 0;
            var sampleNames = new System.Text.StringBuilder();
            foreach (ParticleSystemRenderer r in Object.FindObjectsOfType<ParticleSystemRenderer>())
            {
                scanned++;
                Material? m = r.sharedMaterial;
                string mn = m != null ? m.name : "";
                string gn = r.gameObject.name;
                if (sampleNames.Length < 400)
                    sampleNames.Append($"[go='{gn}' mat='{mn}'] ");
                // Match on EITHER the material name OR the GameObject name (Wind*/Cloud*) — robust to
                // material instancing / per-map renaming (a prior root-scoped, GO-name-only scan found 0).
                bool isWind = mn.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || mn.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || gn.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || gn.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isWind)
                    continue;
                var ps = r.GetComponent<ParticleSystem>();
                if (ps == null)
                    continue;
                var main = ps.main;
                _tunedWindParticles.Add(ps);
                _tunedWindOriginal.Add(main.startColor);           // struct copy — the exact original
                main.startColor = ScaleGradientAlpha(main.startColor, opacity);
                n++;
            }
            VRLog.Info("WorldUI", $"MAP WIND: dimmed {n}/{scanned} Wind/Clouds ambiance particle system(s) to " +
                                  $"{opacity:F2} alpha on the {(_activeMapIsCity ? "CITY" : "WORLD")} map " +
                                  "(subtle wind, kept — not disabled)." +
                                  (n == 0 ? $" NO MATCH — scene particle systems: {sampleNames}" : ""));
        }
    }

    /// <summary>Restore the original startColor of every particle system we dimmed (disengage / map switch).</summary>
    private void RestoreWindParticles()
    {
        for (int i = 0; i < _tunedWindParticles.Count; i++)
        {
            ParticleSystem ps = _tunedWindParticles[i];
            if (ps == null)
                continue;
            var main = ps.main;
            main.startColor = _tunedWindOriginal[i];
        }
        _tunedWindParticles.Clear();
        _tunedWindOriginal.Clear();
        _windScannedRoot = null;
    }

    /// <summary>Return a copy of a particle startColor gradient with its alpha scaled by <paramref name="f"/>,
    /// preserving whatever mode it uses (constant / two colours / gradient / two gradients).</summary>
    private static ParticleSystem.MinMaxGradient ScaleGradientAlpha(ParticleSystem.MinMaxGradient g, float f)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
            {
                Color c = g.color; c.a *= f;
                return new ParticleSystem.MinMaxGradient(c);
            }
            case ParticleSystemGradientMode.TwoColors:
            {
                Color a = g.colorMin; a.a *= f;
                Color b = g.colorMax; b.a *= f;
                return new ParticleSystem.MinMaxGradient(a, b) { mode = ParticleSystemGradientMode.TwoColors };
            }
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(ScaleGradient(g.gradient, f));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(ScaleGradient(g.gradientMin, f), ScaleGradient(g.gradientMax, f))
                    { mode = ParticleSystemGradientMode.TwoGradients };
            default:
                return g;
        }
    }

    private static Gradient ScaleGradient(Gradient src, float f)
    {
        var ak = src.alphaKeys;
        for (int i = 0; i < ak.Length; i++)
            ak[i].alpha *= f;
        var dst = new Gradient();
        dst.SetKeys(src.colorKeys, ak);
        dst.mode = src.mode;
        return dst;
    }

    // ---- [Optimize] MapIconCache (2026-07 perf pass) -----------------------------------------
    // DrawMapIcons is called from Camera.onPreCull, i.e. ONCE PER RENDERING CAMERA PER FRAME —
    // and with the stereo flat screen up that is two or three cameras. As written it did, per
    // call: a full-scene FindObjectOfType<MapChoreographer>(), a `new[]` roots array, an
    // ALLOCATING GetComponentsInChildren per root, a GetComponent<Renderer>() per decal, a fresh
    // MaterialPropertyBlock per decal, another allocating GetComponentsInChildren for the party
    // token, and a multi-hundred-character interpolated string per decal that is only ever
    // printed for the first five calls of the session. On the campaign map — exactly the screen
    // the flat-screen stereo path exists for — that is the single biggest per-frame allocator in
    // the mod.
    //
    // The set of decals under the scenario/village roots only changes when the map state changes,
    // never between two frames of a pan; their POSES are still read fresh every frame from the
    // live renderer bounds, so panning and zooming are pixel-identical. So the SET is cached and
    // re-scanned on an interval, the renderers are cached alongside their decals, and the
    // property blocks come from a pool that hands out the SAME distinct instance per draw slot
    // every frame (which preserves the original "one MPB per draw" semantics exactly — the
    // original comment shows that was a deliberate debugging choice, so it is not weakened).
    private MapChoreographer? _iconChoreo;

    /// <summary>
    /// Frame of the last decal/renderer re-scan; <c>int.MinValue</c> is the "never scanned"
    /// sentinel and MUST be tested by identity, never fed into the age subtraction.
    ///
    /// ROOT CAUSE of the recurring "Icons (z.B. Gloomhaven oder Quest) fehlen auf der Karte"
    /// report (both occurrences, sessions after the 2026-07-27 MapIconCache perf pass): the
    /// rescan gate was the bare subtraction
    /// <c>Time.frameCount - _iconCacheFrame &gt;= IconCacheIntervalFrames</c> with this field
    /// initialized to <c>int.MinValue</c>. In C#'s default UNCHECKED arithmetic,
    /// <c>frameCount - int.MinValue</c> wraps: for any frameCount &gt;= 0 the result is
    /// <c>frameCount - 2147483648</c>, a large NEGATIVE number, which FAILS the "interval
    /// elapsed" test — so with the cache on (the default) the very first call skipped the
    /// scan, and because this field is only ever stamped INSIDE the rescan block, no later
    /// call could ever rescan either. The decal and party-token lists stayed empty for the
    /// whole session, and the 2026-07-31 scene-wide fallback sweep (added for the FIRST
    /// occurrence, on the hypothesis that the choreographer parents held no decals) sat
    /// inside the same dead block and never ran once. Evidence (2026-08-04 Player.log, the
    /// session WITH the diagnostics): every "MAP ICONS [1..5]" line reads
    /// <c>decals=0 noMat=0 noTex=0 drawn=0 partyTokenRenderers=0</c> while the SAME lines'
    /// ZERO-DECAL STATE — queried directly, outside the rescan block — reads
    /// <c>scenariosParent='Scenarios' active=True children=48, villagesParent='Villages'
    /// active=True children=5, scene-wide ACTIVE Decal components=41</c>: the icons existed,
    /// active, right under the parents the scan targets, and no MAP ICONS FALLBACK warn ever
    /// appeared. The savegame-dependence was illusory — saves whose map visits predated the
    /// perf pass simply still had the per-frame (cache-off) scan. Same overflow class as the
    /// <see cref="Hands.Interact.UiScrollFocus"/> hover-stamp latch, opposite polarity: there
    /// the wrapped negative PASSED a "recent" test, here it FAILS an "elapsed" test. The gate
    /// now tests the sentinel by identity before any subtraction.
    /// </summary>
    private int _iconCacheFrame = int.MinValue;
    private readonly List<Component> _iconDecals = new(64);
    private readonly List<Renderer?> _iconDecalRenderers = new(64);
    private readonly List<Renderer> _iconTokenRenderers = new(8);
    private readonly List<MaterialPropertyBlock> _iconMpbPool = new(64);

    /// <summary>Frames between two decal/renderer re-scans while <c>MapIconCache</c> is on. ~0.2 s
    /// at 72 Hz: far below the time any map state change takes to become visible, and 15x less
    /// scanning than the per-frame original.</summary>
    private const int IconCacheIntervalFrames = 15;

    /// <summary>Per-draw property block from the pool — same instance for the same draw slot every
    /// frame, so nothing is shared BETWEEN draws (the property values are re-set below anyway).</summary>
    private MaterialPropertyBlock RentIconMpb(int slot)
    {
        while (_iconMpbPool.Count <= slot)
            _iconMpbPool.Add(new MaterialPropertyBlock());
        return _iconMpbPool[slot];
    }

    private void DrawMapIcons(Camera mapCam)
    {
        if (!MapDrawIcons || mapCam == null || _worldMapRenderer == null)
            return;
        TuneMapWindParticles(); // keep the wind but make it subtle (no thick see-through streaks over icons)

        bool cacheOn = Core.PerfConfig.MapIconCacheOn;
        // A full-scene type scan per camera per frame; with the cache on it only re-runs when the
        // cached choreographer died or the rescan interval elapsed.
        if (!cacheOn || _iconChoreo == null)
            _iconChoreo = Object.FindObjectOfType<MapChoreographer>();
        MapChoreographer? choreo = _iconChoreo;
        if (choreo == null)
            return;
        if (_iconQuad == null)
        {
            _iconQuad = new Mesh { name = "GloomhavenVR.MapIconQuad" };
            _iconQuad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            _iconQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _iconQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white }; // Sprites/Default multiplies by vertex color
            _iconQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _iconQuad.RecalculateBounds();
        }
        if (_iconMat == null)
        {
            // Straight-alpha unlit transparent; fall back to Sprites/Default (premultiplied, still works).
            Shader? sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (sh == null) { VRLog.Warn("WorldUI", "MAP ICONS: no unlit/sprite shader found — icons skipped."); _decalTypeMissing = true; return; }
            _iconMat = new Material(sh) { name = "GloomhavenVR.MapIconMat" };
            // Draw AFTER the wind/cloud particles. In flat the wind is world-space particles on
            // the Default sorting layer (queue ~3000) and the location icons are UI-canvas markers
            // painted LAST, on top — the wind never obscures them. Our map mesh writes depth but the
            // particles and our icons do NOT, so overlap is decided purely by render-queue draw ORDER,
            // not depth. Forcing the icons to the Overlay queue (4000 > particles' 3000) reproduces the
            // flat layering: icons always on top, the wind drifts subtly underneath (no thick bands).
            _iconMat.renderQueue = 4000;
            VRLog.Info("WorldUI", $"MAP ICONS: icon material shader = '{sh.name}' (renderQueue={_iconMat.renderQueue}, drawn over the wind particles).");
        }
        _iconMpb ??= new MaterialPropertyBlock();
        // Draw the icons through a command buffer at AfterForwardAlpha (after the wind) that first CLEARS the
        // depth buffer, so a cloud particle's depth can never reject an icon. Rebuilt every frame (icons move
        // with pan/zoom). Attached to the current map camera; re-attached if the camera instance changes.
        if (_iconCmd == null)
            _iconCmd = new CommandBuffer { name = "GloomhavenVR.MapIcons" };
        if (!ReferenceEquals(_iconCmdCam, mapCam))
        {
            if (_iconCmdCam != null)
                _iconCmdCam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _iconCmd);
            mapCam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _iconCmd);
            _iconCmdCam = mapCam;
        }
        _iconCmd.Clear();
        _iconCmd.ClearRenderTarget(true, false, Color.clear); // clear DEPTH only → icons ignore the wind's depth
        if (_decalTypeMissing) return;
        if (_decalType == null)
        {
            _decalType = HarmonyLib.AccessTools.TypeByName("Decal");
            if (_decalType == null) { _decalTypeMissing = true; VRLog.Warn("WorldUI", "MAP ICONS: Decal type not found — icons skipped."); return; }
            _decalCurMatProp = _decalType.GetProperty("CurrentMaterial");
        }
        // Sit clearly ABOVE the parchment (its mesh is ~0.13 thick) so we never z-fight the animated
        // foliage — that intersection caused transparent 'wind' bands sweeping through the icons.
        float planeY = _worldMapRenderer.bounds.max.y + 0.10f;

        // Re-scan the decal / party-token component sets. With the cache OFF this is the original
        // per-call behaviour (two allocating GetComponentsInChildren per camera per frame); with it
        // ON the same scan runs once per IconCacheIntervalFrames and the results are reused.
        // The sentinel is compared by IDENTITY before the subtraction — see the _iconCacheFrame
        // doc block: the bare subtraction wraps negative for a virgin stamp and killed every
        // rescan (and with it every icon) for the whole session.
        bool rescan = !cacheOn
                      || _iconCacheFrame == int.MinValue
                      || Time.frameCount - _iconCacheFrame >= IconCacheIntervalFrames
                      || _iconDecals.Count != _iconDecalRenderers.Count;
        if (!rescan)
        {
            // A destroyed decal (map state changed inside the interval) forces an early re-scan
            // rather than a frame of stale draws.
            for (int i = 0; i < _iconDecals.Count; i++)
            {
                if (_iconDecals[i] == null)
                {
                    rescan = true;
                    break;
                }
            }
        }
        if (rescan)
        {
            _iconCacheFrame = Time.frameCount;
            _iconDecals.Clear();
            _iconDecalRenderers.Clear();
            CollectIconDecals(choreo.m_ScenariosParent);
            CollectIconDecals(choreo.m_VillagesParent);

            // FALLBACK (kept as a safety net; its founding hypothesis is DISPROVEN): the
            // "zero decals for the whole map visit" sessions were NOT a save that parents its
            // MapLocations elsewhere — the 2026-08-04 log showed 41 active decals right under
            // 'Scenarios'/'Villages' while this whole rescan block was dead code behind the
            // wrapped-sentinel gate (see the _iconCacheFrame doc block). Now that the block
            // runs, the parent scan is expected to find them; this sweep stays for the case it
            // was written for — a game update or save state genuinely reparenting the icons —
            // and names where they actually live. Runs only while the parent scan finds
            // nothing, at rescan cadence.
            if (_iconDecals.Count == 0 && _decalType != null)
            {
                UnityEngine.Object[] all = Object.FindObjectsOfType(_decalType);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is not Component c || !c.gameObject.activeInHierarchy)
                        continue;
                    _iconDecals.Add(c);
                    _iconDecalRenderers.Add(c.GetComponent<Renderer>());
                }
                if (!_iconFallbackLogged && _iconDecals.Count > 0)
                {
                    _iconFallbackLogged = true;
                    Component first = _iconDecals[0];
                    var path = new System.Text.StringBuilder(first.name);
                    Transform? p = first.transform.parent;
                    for (int depth = 0; p != null && depth < 5; depth++, p = p.parent)
                        path.Insert(0, p.name + "/");
                    VRLog.Warn("WorldUI", $"MAP ICONS FALLBACK: the choreographer parents "
                        + $"(scenarios='{(choreo.m_ScenariosParent != null ? choreo.m_ScenariosParent.name : "null")}', "
                        + $"villages='{(choreo.m_VillagesParent != null ? choreo.m_VillagesParent.name : "null")}') "
                        + $"held 0 active decals, but a scene-wide sweep found {_iconDecals.Count} — "
                        + $"drawing those instead. First lives at '{path}'. This path names where "
                        + "this save/game version really parents its map icons.");
                }
            }

            _iconTokenRenderers.Clear();
            PartyToken? token = choreo.m_PartyToken; // publicized serialized field
            if (token != null)
                token.GetComponentsInChildren(includeInactive: false, _iconTokenRenderers);
        }

        // The MAP ICONS diagnostic is emitted at most five times per session, so its counters and
        // its (very long) detail string must not be built on every one of the thousands of calls
        // in between — the gate now wraps the string work instead of only the log call.
        bool wantDiag = _mapIconsLogCount < 5;
        int nDecals = 0, nNoMat = 0, nNoTex = 0, nDrawn = 0;
        string firstDetail = "";
        // Per-icon GEOMETRY dump, throttled to SCAN events only (rescan is true at most once per
        // IconCacheIntervalFrames) and budgeted per map entry: one line per drawn icon naming the
        // location, texture size, decal scale and the computed quad size — so the next hardware
        // test can name any icon that still looks wrong. Emitted only when the drawn count CHANGED
        // (decal materials load async, so icons materialize over the first scans).
        StringBuilder? geomSb = (rescan && _iconGeomDumps < IconGeomDumpMax) ? new StringBuilder() : null;

        for (int i = 0; i < _iconDecals.Count; i++)
        {
            Component d = _iconDecals[i];
            if (d == null)
                continue;
            // The scan collects with includeInactive:false, so the ORIGINAL per-call version could
            // never draw a decal that had since been deactivated. A cached set can — for up to one
            // rescan interval — so the active test is re-applied here per draw. Cheap, and it keeps
            // the caching strictly a work-removal change rather than a visible one.
            if (!d.gameObject.activeInHierarchy)
                continue;
            nDecals++;
            Material? cm = _decalCurMatProp?.GetValue(d) as Material;
            if (cm == null) { nNoMat++; continue; }
            Texture? tex = cm.HasProperty(IconMainTex) ? cm.GetTexture(IconMainTex) : cm.mainTexture;
            if (tex == null)
            {
                nNoTex++;
                if (wantDiag && firstDetail == "")
                    firstDetail = $"noTex mat.shader='{(cm.shader != null ? cm.shader.name : "?")}' main='{(cm.mainTexture != null ? cm.mainTexture.name : "null")}'";
                continue;
            }
            Renderer? rend = _iconDecalRenderers[i];
            if (rend == null) continue;
            // ROOT CAUSE (user report 2026-08, "vereinzelt Icons gequetscht/gestaucht"): the quad
            // footprint used to come from rend.bounds — the decal cube's WORLD-AXIS-ALIGNED bounding
            // box — and the quad was then rotated by the decal's yaw ON TOP of that. The game's map
            // decals carry a 90-degree yaw (hardware log 2026-08-04: decalEuler=(0.00, 90.00, 0.00)
            // on the drawn icons), and Decalicious draws each decal as a UNIT CUBE transformed by the
            // decal's own transform (decompiled ThreeEyedGames.DecaliciousRenderer:
            // DrawMesh(_cubeMesh, current.transform.localToWorldMatrix, ...)), so the AUTHORED
            // footprint is lossyScale.x (texture u) by lossyScale.z (texture v). Under a 90-degree
            // yaw the world AABB reports those extents TRANSPOSED (world x extent = local z, world z
            // extent = local x); scaling the quad by the AABB and then rotating it AGAIN by the yaw
            // re-transposes them — every NON-SQUARE icon rendered with width and height swapped
            // (squashed on one axis, stretched on the other), while square footprints came out
            // pixel-identical: exactly the "most icons fine, some squashed" report. Non-square
            // footprints are authored per icon type — GlobalSettings.AdventureLocationMaterialSettings
            // .OverrideLocationScale is a full Vector3 applied to the decal's parent (decompiled
            // MapLocation, "MeshParent.transform.localScale = ...OverrideLocationScale"). The quad TRS
            // now comes straight from the decal transform — the same matrix the game itself draws the
            // decal cube with — so every icon renders at its authored aspect and rotation. (The decal
            // materials' _MainTex_ST is scale (1,1) offset (0,0) in the hardware log — no atlas trim,
            // full-rect UVs on the quad are correct.)
            Transform dt = d.transform;
            Vector3 dScale = dt.lossyScale;
            Vector3 dPos = dt.position;
            var pos = new Vector3(dPos.x, planeY, dPos.z);
            var scale = new Vector3(Mathf.Max(Mathf.Abs(dScale.x), 0.01f), 1f, Mathf.Max(Mathf.Abs(dScale.z), 0.01f));
            // Orient the quad to the decal's yaw so the icon matches the game (our camera has a 90-degree yaw).
            var rot = Quaternion.Euler(0f, dt.eulerAngles.y, 0f);
            // One property block PER DRAW, as before — pooled instead of newly allocated, so the
            // "no shared MPB between draws" property the original comment was protecting is kept.
            MaterialPropertyBlock mpb = cacheOn ? RentIconMpb(nDrawn) : new MaterialPropertyBlock();
#pragma warning disable CS0162 // MapIconsSolidTest is a const bisection switch — see MAP BISECTION SWITCHES
            if (MapIconsSolidTest)
            {
                mpb.SetTexture(IconMainTex, Texture2D.whiteTexture);
                mpb.SetColor(IconColor, new Color(1f, 0f, 1f, 1f));
            }
#pragma warning restore CS0162
            else
            {
                mpb.SetTexture(IconMainTex, tex);
                mpb.SetColor(IconColor, Color.white);
            }
            _iconCmd.DrawMesh(_iconQuad, Matrix4x4.TRS(pos, rot, scale), _iconMat, 0, 0, mpb);
            nDrawn++;
            if (geomSb != null)
            {
                // The MapLocation GO sits two levels up (decal -> MeshParent -> location).
                Transform? locT = dt.parent != null ? dt.parent.parent : null;
                geomSb.Append($"\n  '{(locT != null ? locT.name : d.name)}/{d.name}' tex='{tex.name}' " +
                              $"{tex.width}x{tex.height} decalScale=({dScale.x:F2},{dScale.y:F2},{dScale.z:F2}) " +
                              $"yaw={dt.eulerAngles.y:F0} quad={scale.x:F2}x{scale.z:F2} pos=({pos.x:F1},{pos.z:F1})");
            }
            if (wantDiag && (firstDetail == "" || !firstDetail.StartsWith("drawn")))
            {
                Vector2 stS = cm.HasProperty(IconMainTex) ? cm.GetTextureScale(IconMainTex) : Vector2.one;
                Vector2 stO = cm.HasProperty(IconMainTex) ? cm.GetTextureOffset(IconMainTex) : Vector2.zero;
                Color col = cm.HasProperty(IconColor) ? cm.GetColor(IconColor) : Color.white;
                firstDetail = $"drawn tex='{tex.name}' {tex.width}x{tex.height} shader='{(cm.shader != null ? cm.shader.name : "?")}' ST(scale {stS.x:F3},{stS.y:F3} off {stO.x:F3},{stO.y:F3}) color={col} pos={pos} scale={scale} decalEuler={d.transform.eulerAngles} active={(_activeMapIsCity ? "CITY" : "WORLD")}";
            }
        }

        // #3: the party / current-location marker (MapChoreographer.m_PartyToken) is a world-space 3D
        // mesh rendered in the normal forward pass — so our AfterForwardAlpha icons (drawn on cleared
        // depth) land ON TOP of it and it looks like it's "behind" the quest/city icons. Re-draw its
        // renderers into the SAME command buffer AFTER the icons so the party marker is always frontmost.
        int nToken = 0;
        for (int i = 0; i < _iconTokenRenderers.Count; i++)
        {
            Renderer tr = _iconTokenRenderers[i];
            // activeInHierarchy for the same reason as the decals above: the collection filtered
            // inactive renderers out, and with a cached set that filter has to be re-applied.
            if (tr == null || !tr.enabled || !tr.gameObject.activeInHierarchy || tr.sharedMaterial == null)
                continue;
            Material[] mats = tr.sharedMaterials;
            for (int sm = 0; sm < mats.Length; sm++)
            {
                if (mats[sm] == null) continue;
                _iconCmd.DrawRenderer(tr, mats[sm], sm, -1); // -1 = the material's own valid passes
                nToken++;
            }
        }
        if (geomSb != null && nDrawn > 0 && nDrawn != _iconGeomLastDrawn)
        {
            _iconGeomLastDrawn = nDrawn;
            _iconGeomDumps++;
            VRLog.Info("WorldUI", $"MAP ICON GEOMETRY [{_iconGeomDumps}/{IconGeomDumpMax}] ({nDrawn} drawn, " +
                                  $"{(_activeMapIsCity ? "CITY" : "WORLD")} map): quad footprint = decal lossyScale.xz " +
                                  "at the decal's own yaw (one line per icon — names any icon that still looks wrong):"
                                  + geomSb);
        }
        if (wantDiag)
        {
            _mapIconsLogCount++;
            // decals=0 is the whole story of the "icons gone with this savegame" report, so THE
            // ZERO CASE must name its own cause: the parents' live state plus a scene-wide count.
            string zeroState = "";
            if (nDecals == 0 && _decalType != null)
            {
                GameObject? sp = choreo.m_ScenariosParent;
                GameObject? vp = choreo.m_VillagesParent;
                int sceneWide = Object.FindObjectsOfType(_decalType).Length;
                zeroState = " | ZERO-DECAL STATE: scenariosParent="
                    + (sp != null ? $"'{sp.name}' active={sp.activeInHierarchy} children={sp.transform.childCount}" : "null")
                    + ", villagesParent="
                    + (vp != null ? $"'{vp.name}' active={vp.activeInHierarchy} children={vp.transform.childCount}" : "null")
                    + $", scene-wide ACTIVE Decal components={sceneWide}";
            }
            VRLog.Info("WorldUI", $"MAP ICONS [{_mapIconsLogCount}]: decals={nDecals} noMat={nNoMat} noTex={nNoTex} drawn={nDrawn} partyTokenRenderers={nToken} iconMat='{(_iconMat != null ? _iconMat.shader.name : "null")}' planeY={planeY:F2} — first: {firstDetail}{zeroState}");
        }
    }

    /// <summary>
    /// Collect one root's Decal components plus each one's Renderer into the cached parallel
    /// lists. The per-decal <c>GetComponent&lt;Renderer&gt;()</c> used to run every frame for every
    /// decal; a decal never changes its renderer, so it belongs in the scan, not the draw loop.
    /// </summary>
    private void CollectIconDecals(GameObject? rootGo)
    {
        if (rootGo == null || _decalType == null)
            return;
        Component[] found = rootGo.GetComponentsInChildren(_decalType, includeInactive: false);
        for (int i = 0; i < found.Length; i++)
        {
            _iconDecals.Add(found[i]);
            _iconDecalRenderers.Add(found[i] != null ? found[i].GetComponent<Renderer>() : null);
        }
    }

    /// <summary>
    /// Build one <c>GloomhavenVR/MapUnlit</c> override material per map submesh: <c>_MainTex</c> = that
    /// submesh's quadrant albedo (the texture gathered by <see cref="GatherMapTextures"/> for the 0N
    /// number parsed from the material name, else the material's own <c>_Alb</c> ?? <c>_MainTex</c> ??
    /// <c>mainTexture</c>), <c>_UvChannel</c> = <see cref="MapUnlitUvChannel"/> (TexCoord0 — the mesh
    /// carries a real one and the shader samples it on the GPU), <c>_UvScale</c> = (1,1) and
    /// <c>_UvOffset</c> = (0,0), because each quadrant submesh's own UV already runs 0..1 across its own
    /// texture. Cached; rebuilt when the ACTIVE RENDERER changes — a length-only check kept the world
    /// textures on the city mesh, since both maps have four submeshes. Returns false (one-shot WARN) if
    /// the bundle shader is missing or the renderer has no mesh.
    /// (This doc block had been stranded above <see cref="UvDebugTexture"/> and still described the
    /// disproven object-space-position UV with a (2,2) quadrant scale.)
    /// </summary>
    private bool BuildOverrideMaterials()
    {
        if (_worldMapRenderer == null)
            return false;

        Material[] orig = _worldMapRenderer.sharedMaterials;
        // Cached — but rebuild when the ACTIVE RENDERER changes (world↔city): both maps have 4 submeshes,
        // so a length-only check kept the world textures/UVs on the city mesh (city showed world content).
        if (_worldMapOverrideMats != null && ReferenceEquals(_overrideMatsRenderer, _worldMapRenderer)
            && _worldMapOverrideMats.Length == orig.Length)
            return true;
        DestroyOverrideMaterials();
        _overrideMatsRenderer = _worldMapRenderer;

        Shader? sh = MapUnlitShader();
        if (sh == null)
            return false; // MapUnlitShader already logged the miss once

        // LOCAL bounds of the parchment mesh — valid even though the mesh is isReadable=false. The plane
        // is local X–Z (Y is the thin/normal axis); the shader maps (objPos.xz - min.xz)/size.xz → 0..1.
        MeshFilter? mf = _worldMapRenderer.GetComponent<MeshFilter>();
        Mesh? mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null)
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP RENDER: the map renderer has no MeshFilter/sharedMesh — cannot size " +
                                      "the MapUnlit UV; the base RT is left as-is (map black).");
            }
            return false;
        }
        // GROUND-TRUTH probe (pure DLL): isReadable=false blocks CPU *data* access but NOT the vertex
        // layout metadata — HasVertexAttribute/GetVertexAttributeDimension read the descriptor only. If
        // the mesh carries a real TexCoord0, the whole object-space-position UV guessing is unnecessary:
        // a shader sampling v.texcoord0 is pixel-perfect and tracks zoom for free. Log the full layout
        // once, plus the original material's texture-ST (tiling/offset) which is what the game's Amplify
        // shader would use to turn position/uv into a texture coordinate.
        if (!_meshLayoutLogged)
        {
            _meshLayoutLogged = true;
            var sb = new StringBuilder();
            sb.Append($"MAP MESH layout: '{mesh.name}' verts={mesh.vertexCount} subMeshes={mesh.subMeshCount} isReadable={mesh.isReadable} bounds min({mesh.bounds.min.x:F2},{mesh.bounds.min.y:F2},{mesh.bounds.min.z:F2}) size({mesh.bounds.size.x:F2},{mesh.bounds.size.y:F2},{mesh.bounds.size.z:F2}); attributes:");
            foreach (VertexAttribute va in new[]
            {
                VertexAttribute.Position, VertexAttribute.Normal, VertexAttribute.Tangent, VertexAttribute.Color,
                VertexAttribute.TexCoord0, VertexAttribute.TexCoord1, VertexAttribute.TexCoord2, VertexAttribute.TexCoord3,
            })
            {
                bool has = mesh.HasVertexAttribute(va);
                sb.Append($" {va}={(has ? "dim" + mesh.GetVertexAttributeDimension(va) : "-")}");
            }
            // Dump the original submesh materials' texture-ST so we can see the game's UV tiling/offset.
            for (int mi = 0; mi < orig.Length; mi++)
            {
                Material om = orig[mi];
                if (om == null) continue;
                Vector2 albS = om.HasProperty("_Alb") ? om.GetTextureScale("_Alb") : Vector2.zero;
                Vector2 albO = om.HasProperty("_Alb") ? om.GetTextureOffset("_Alb") : Vector2.zero;
                Vector2 mtS = om.HasProperty("_MainTex") ? om.GetTextureScale("_MainTex") : Vector2.zero;
                Vector2 mtO = om.HasProperty("_MainTex") ? om.GetTextureOffset("_MainTex") : Vector2.zero;
                sb.Append($"\n  mat[{mi}] '{om.name}' shader '{(om.shader != null ? om.shader.name : "<null>")}' _Alb_ST(scale {albS.x:F3},{albS.y:F3} off {albO.x:F3},{albO.y:F3}) _MainTex_ST(scale {mtS.x:F3},{mtS.y:F3} off {mtO.x:F3},{mtO.y:F3})");
            }
            VRLog.Info("WorldUI", sb.ToString());
        }

        Bounds lb = mesh.bounds;
        _mapLocalMin = lb.min;
        _mapLocalSize = new Vector3(
            Mathf.Abs(lb.size.x) > 1e-5f ? lb.size.x : 1f,
            Mathf.Abs(lb.size.y) > 1e-5f ? lb.size.y : 1f,
            Mathf.Abs(lb.size.z) > 1e-5f ? lb.size.z : 1f);

        var overrides = new Material[orig.Length];
        int withTex = 0;
        string facts = "";
        for (int i = 0; i < orig.Length; i++)
        {
            Material o = orig[i];
            Texture? tex = null;
            string prop = "NONE";
            int q = -1;
            if (o != null)
            {
                q = QuadrantIndexFromName(o.name);
                if (o.HasProperty("_Alb") && (tex = o.GetTexture("_Alb")) != null)
                    prop = "_Alb";
                else if (o.HasProperty("_MainTex") && (tex = o.GetTexture("_MainTex")) != null)
                    prop = "_MainTex";
                else if ((tex = o.mainTexture) != null)
                    prop = "mainTexture";
            }
            // Prefer the quadrant texture gathered by GatherMapTextures (indexed by 0N number), falling
            // back to this submesh material's own albedo.
            if (q >= 0 && q < 4 && _mapQuadTextures != null && _mapQuadTextures[q] != null)
                tex = _mapQuadTextures[q];

            // UV DIAGNOSTIC: swap the real albedo for the generated UV read-out texture (see MapUvDebug).
#pragma warning disable CS0162 // MapUvDebug is a const bisection switch — see MAP BISECTION SWITCHES
            if (MapUvDebug)
            {
                tex = UvDebugTexture();
                prop = "UV-DEBUG";
            }
#pragma warning restore CS0162

            var m = new Material(sh) { name = "GloomhavenVR.MapUnlit." + i };
            // Sample the mesh's OWN UV (the mesh carries real TexCoord0/1/2 — verified 20f8f79c0).
            // Each quadrant submesh's UV already runs 0..1 over its own texture, so scale=1/offset=0.
            Vector2 uvScale = Vector2.one;
            Vector2 uvOffset = Vector2.zero;
            // A/B the two remaining UV channels across submeshes: even index → ch1, odd → ch2.
            float chan = MapDiagPerSubmeshChannel ? ((i % 2 == 0) ? 1f : 2f) : MapUnlitUvChannel;
            m.SetFloat("_UvChannel", chan);
            m.SetVector("_UvScale", new Vector4(uvScale.x, uvScale.y, 0f, 0f));
            m.SetVector("_UvOffset", new Vector4(uvOffset.x, uvOffset.y, 0f, 0f));
            m.SetFloat("_Bright", 1f);
            if (tex != null)
            {
                m.SetTexture("_MainTex", tex);
                withTex++;
            }
            overrides[i] = m;
            if (!_albedoMaterialsLogged)
                facts += $"\n  submesh[{i}] '{(o != null ? o.name : "<null>")}' quadrant {(q >= 0 ? (q + 1).ToString("00") : "??")}"
                         + $": albedo from {prop}"
                         + (tex != null ? $" = '{tex.name}' {tex.width}x{tex.height}" : " (none)")
                         + $", UvChannel={chan:F0} uvScale({uvScale.x:F0},{uvScale.y:F0}) uvOffset({uvOffset.x:F0},{uvOffset.y:F0})";
        }
        _worldMapOverrideMats = overrides;
        _worldMapOriginalMats = orig;

        if (!_albedoMaterialsLogged)
        {
            _albedoMaterialsLogged = true;
            VRLog.Info("WorldUI", $"MAP RENDER materials ({orig.Length} submesh(es), {withTex} with a texture, " +
                                  $"localBounds min({_mapLocalMin.x:F2},{_mapLocalMin.y:F2},{_mapLocalMin.z:F2}) " +
                                  $"size({_mapLocalSize.x:F2},{_mapLocalSize.y:F2},{_mapLocalSize.z:F2})):{facts}");
        }

        if (withTex == 0)
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP RENDER: no worldMap submesh material exposed an albedo texture " +
                                      "(_Alb / _MainTex / mainTexture all null) — the base RT is left as-is (map black).");
            }
            // Drop the useless overrides so a later rebuild (materials populated) can retry.
            DestroyOverrideMaterials();
            return false;
        }
        return true;
    }

    /// <summary>Create the mod-owned albedo camera (bare, disabled; flipped on by ReconcileAlbedoCamera).</summary>
    private void EnsureAlbedoCamera()
    {
        if (_mapAlbedoCam != null || _root == null)
            return;
        var go = new GameObject("GloomhavenVR.MapAlbedoCamera");
        go.transform.SetParent(_root.transform, worldPositionStays: false);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;                            // ReconcileAlbedoCamera flips it on
        cam.stereoTargetEye = StereoTargetEyeMask.None; // VRCameraPolicy-invisible by construction
        EnsureMapRt();
        cam.targetTexture = _mapRt != null ? _mapRt : _leftRt;
        XRDevice.DisableAutoXRCameraTracking(cam, true);
        _mapAlbedoCam = cam;
        _mapAlbedoGo = go;
        _mapAlbedoTransform = go.transform;
    }

    /// <summary>(Re)allocate the private map RT to match the base RT's size (the mod map camera's sole target).</summary>
    private void EnsureMapRt()
    {
        if (_leftRt == null)
            return;
        if (_mapRt != null && (_mapRt.width != _leftRt.width || _mapRt.height != _leftRt.height))
            ReleaseMapRt();
        if (_mapRt == null)
        {
            _mapRt = CreateColorRt(_leftRt.width, _leftRt.height, 24, "GloomhavenVR.MapRT");
            bool ok = _mapRt.Create();
            VRLog.Info("WorldUI", $"MAP RENDER private RT allocated: created={ok} IsCreated={_mapRt.IsCreated()} " +
                                  $"native={_mapRt.GetNativeTexturePtr()} {DescribeRt(_mapRt)} (base {DescribeRt(_leftRt)})");
        }
    }

    private void ReleaseMapRt()
    {
        if (_mapRt == null)
            return;
        if (_mapAlbedoCam != null && _mapAlbedoCam.targetTexture == _mapRt)
            _mapAlbedoCam.targetTexture = null;
        _mapRt.Release();
        Object.Destroy(_mapRt);
        _mapRt = null;
    }

    /// <summary>
    /// Swap the MapUnlit override materials onto the REAL parchment renderer just before our forward
    /// camera renders it (scoped to that render only — <see cref="RestoreWorldMapOverride"/> puts the
    /// game's own materials back in the same frame, so the game state is untouched; multiplayer-safe).
    /// </summary>
    private void ApplyWorldMapOverride()
    {
#pragma warning disable CS0162 // MapDiagClearOnly is a const bisection switch — see MAP BISECTION SWITCHES
        if (MapDiagClearOnly)
            return; // diagnostic: leave the game's deferred material on → nothing draws → magenta clear only
#pragma warning restore CS0162
        if (_overrideApplied || _worldMapRenderer == null || _worldMapOverrideMats == null)
            return;
        // Re-capture the live originals each time so the restore always puts back exactly what the
        // game currently has (a submesh count change invalidates our override — skip + rebuild).
        Material[] current = _worldMapRenderer.sharedMaterials;
        if (current.Length != _worldMapOverrideMats.Length)
        {
            DestroyOverrideMaterials(); // stale — EnsureAlbedoReady rebuilds next tick
            return;
        }
        _worldMapOriginalMats = current;
        _worldMapRenderer.sharedMaterials = _worldMapOverrideMats;
        _overrideApplied = true;
    }

    /// <summary>Restore the worldMap renderer's original materials right after our forward camera renders (game state untouched).</summary>
    private void RestoreWorldMapOverride()
    {
        if (!_overrideApplied)
            return;
        _overrideApplied = false;
        if (_worldMapRenderer != null && _worldMapOriginalMats != null)
            _worldMapRenderer.sharedMaterials = _worldMapOriginalMats;
    }

    private void DestroyOverrideMaterials()
    {
        if (_worldMapOverrideMats != null)
        {
            for (int i = 0; i < _worldMapOverrideMats.Length; i++)
                if (_worldMapOverrideMats[i] != null)
                    Object.Destroy(_worldMapOverrideMats[i]);
            _worldMapOverrideMats = null;
        }
        _worldMapOriginalMats = null;
    }

    /// <summary>Tear down the whole albedo render (restore game materials, destroy overrides + mod camera).</summary>
    private void ReleaseAlbedo()
    {
        RestoreWorldMapOverride(); // never leave the override materials/mesh on the game renderer
        DestroyOverrideMaterials();
        _mapQuadTextures = null;
        _mapTexLogged = false;
        _mapMonoLogged = false;
        _mapMirrorLogged = false;
        _activeMapGo = null;
        _worldMapRenderer = null;
        _albedoMaterialsLogged = false;
        _albedoWarned = false;
        _overrideApplied = false;
        if (_iconCmd != null && _iconCmdCam != null)
            _iconCmdCam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _iconCmd);
        _iconCmdCam = null; // camera is being torn down; re-attach on next engage
        if (_mapAlbedoGo != null)
        {
            Object.Destroy(_mapAlbedoGo);
            _mapAlbedoGo = null;
            _mapAlbedoCam = null;
            _mapAlbedoTransform = null;
        }
        ReleaseMapRt();
        _mapTargetLogged = false;
        _mapPoseLogged = false;
        _mapDrivenValid = false;
        _mapFirstFrameRendered = false;
        _mapRevealLogged = false;
        _fastMapChoreo = null; // scene is going away — re-find on the next map open
        _mapFov = 0f;
        _mapPanning = false;
        _mapPanLogged = false;
        RestoreWindParticles(); // leaving the map — give the game its wind/cloud particles back
        _mapSceneRenderersLogged = false;
        _mapIconsLogCount = 0;
        _ndcLogCount = 0;
        _ndcLastLogFrame = int.MinValue;
        _capLogged = false;
    }

    // ---- map albedo render: once-per-second base-RT probe (MAP ALBEDO probe line) ------------

    /// <summary>
    /// Throttled async probe of the CENTER region of the base RT while the albedo render is engaged —
    /// a single decisive line reporting whether the parchment landed bright (mean &gt; 50) or the base
    /// RT is still failing (~0 black / ~12 dark murk).
    /// </summary>
    private void TickAlbedoProbe()
    {
        if (!_mapBaseCapture || !_active || _leftRt == null || _albedoProbePending)
            return;
        // Fast window: probe EVERY frame for the first ~FastProbeFrames after the map engages (to
        // catch the exact frame the detailed deferred render dies), then throttle to once/second.
        int interval = (_mapEngageFrame != int.MinValue
                        && Time.frameCount - _mapEngageFrame < FastProbeFrames)
            ? 1
            : AlbedoProbeIntervalFrames;
        if (_albedoProbeFrame != int.MinValue
            && Time.frameCount - _albedoProbeFrame < interval)
            return;
        _albedoProbeFrame = Time.frameCount;

        if (_albedoProbeRt == null)
        {
            _albedoProbeRt = new RenderTexture(BlackProbeSize, BlackProbeSize, 0)
            {
                name = "GloomhavenVR.FlatScreenRT.AlbedoProbe",
                antiAliasing = 1,
            };
            if (!_albedoProbeRt.Create())
            {
                ReleaseAlbedoProbeRt();
                return;
            }
        }

        float scale = AlbedoProbeRegion;
        float offset = (1f - AlbedoProbeRegion) * 0.5f;
        // Probe what the quad actually shows in map mode: the private map RT (our forward render).
        RenderTexture probeSrc = (_mapBaseCapture && _mapRt != null) ? _mapRt : _leftRt;
        Graphics.Blit(probeSrc, _albedoProbeRt, new Vector2(scale, scale), new Vector2(offset, offset));
        _albedoProbePending = true;
        _albedoProbeReqGen = _probeGen;
        AsyncGPUReadback.Request(_albedoProbeRt, 0, TextureFormat.RGBA32, OnAlbedoProbe);
    }

    private void OnAlbedoProbe(AsyncGPUReadbackRequest req)
    {
        _albedoProbePending = false;
        if (!_active || !_mapBaseCapture || _albedoProbeReqGen != _probeGen || req.hasError)
            return;

        var data = req.GetData<Color32>();
        int n = Mathf.Max(1, data.Length);
        long sum = 0, rSum = 0, gSum = 0, bSum = 0;
        int max = 0;
        int lumMax = 0, lumMin = 255;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            int m = c.r;
            if (c.g > m) m = c.g;
            if (c.b > m) m = c.b;
            if (m > max) max = m;
            // Spread = per-texel luminance range over the 8x8 downsample: the DETAIL signal.
            // A detailed map has bright and dark texels (high spread); a flat colour ~0.
            int lum = (c.r * 77 + c.g * 150 + c.b * 29) >> 8;
            if (lum > lumMax) lumMax = lum;
            if (lum < lumMin) lumMin = lum;
            sum += m; rSum += c.r; gSum += c.g; bSum += c.b;
        }
        int mean = (int)(sum / n);
        int rMean = (int)(rSum / n), gMean = (int)(gSum / n), bMean = (int)(bSum / n);
        int spread = Mathf.Max(0, lumMax - lumMin);
        int rel = _mapEngageFrame != int.MinValue ? Time.frameCount - _mapEngageFrame : Time.frameCount;
        VRLog.Info("WorldUI",
            $"MAP CAP probe f{rel}: mean {mean}/255 (r{rMean} g{gMean} b{bMean}), max {max}, spread {spread} — " +
            "spread>25 ⇒ DETAILED map present; spread<8 ⇒ flat/blacked.");
    }

}
