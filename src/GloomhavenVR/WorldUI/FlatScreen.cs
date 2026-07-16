using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Floating 2D screen (ROADMAP P3c #6): a world-space quad showing the flat game's
/// full desktop composite for everything that is not physicalized — main menu,
/// guildmaster map, merchant, level-up, and any unconverted window.
///
/// Rendering (STACK CAPTURE, hardware test #5): while the screen is visible EVERY
/// game camera that renders to the backbuffer is retargeted onto one shared
/// RenderTexture shown on the quad (UUVR screen-mirror pattern; UI-ARCH §2.1
/// consequence (a)). Test #5 proved a UICamera-only redirect loses the menu's
/// ambient slideshow/video: 'Main Camera' (clear=Depth, mask 0x20) and the
/// late-created 'MainMenuVideo' camera kept rendering to the backbuffer, which our
/// end-of-frame Blit overwrites — their content reached neither the quad nor the
/// desktop (black background). Unity orders cameras by depth per render target, so
/// the captured cameras compose inside the RT exactly like they did on the
/// backbuffer; only the FIRST camera of the stack gets an opaque SolidColor clear
/// forced (fresh RTs have undefined color; see field comment), all others keep
/// their own clear flags (Depth etc.) so compositing matches the game's intent.
/// The per-tick capture sweep also catches cameras created later (MainMenuVideo
/// appears seconds after the menu scene). Cameras already targeting another RT
/// ('GUI 3D Camera' → character assembly RT) and our own rig head camera are left
/// alone. Everything (targetTexture, clear flags) is restored on hide, scene
/// change, VR off and hot reload. Screen-Space-Camera canvases follow their
/// worldCamera into the RT automatically — verified via ilspycmd (GH.Runtime.dll,
/// CanvasManager): <c>private void OnSceneLoaded(Scene scene, LoadSceneMode mode)</c>
/// re-binds <c>persistentUICanvas.worldCamera</c> / <c>tooltipCanvas.worldCamera</c>
/// to the first camera tagged "UICamera" (PATCH-TARGETS §1.7 ✅).
///
/// Coordination with <see cref="Core.VRCameraPolicy"/>: complementary, no fight —
/// the policy owns exactly <c>stereoTargetEye</c>/XR tracking, this class owns
/// exactly <c>targetTexture</c>/clear flags of captured cameras. A camera can be
/// stereo-None AND render into our RT.
///
/// DESKTOP MIRROR (menu-blackscreen fix): while the RT redirect is active nothing
/// would reach the desktop backbuffer (the XR mirror shows an HMD eye, which shows
/// the quad at best). <see cref="OnEndOfFrame"/> — driven by the WorldUI driver's
/// WaitForEndOfFrame loop, i.e. after Unity's own XR mirror-view blit — blits the RT
/// to the backbuffer every frame, so the monitor always shows the full 2D UI and
/// stays mouse-operable as a fallback.
///
/// PRE-MENU GATE (menu-blackscreen fix): the game boots scene 0 (Bootstrap) → scene
/// "Intro" (IntroPlayer: VideoPlayer + logos) → "Gloomhaven_unified" (main menu);
/// all Single-mode loads (verified via decompiled GH.Runtime Bootstrap.ShowSplash).
/// The screen stays fully hands-off in scene 0 / "Intro" — the intro renders
/// vanilla — and engages on the first real menu scene. While gated, the HMD shows
/// the menu rig's grey void plus a small "starting…" indicator.
///
/// XR EXCLUSION: owned centrally by <see cref="Core.VRCameraPolicy"/> (pumped by the
/// rig driver) — in VR only the rig head camera renders stereo; the UICamera and any
/// other camera are forced to StereoTargetEyeMask.None there, and restored on VR-off.
/// The per-UICamera guard this class used to run was removed (one owner, one policy;
/// docs/CAMERA-POLICY.md §1).
///
/// Pointer: the primary hand ray (Phase-2 <see cref="RayInteractor"/> pick pose) is
/// intersected with the screen plane in code (no physics collider — keeps clear of
/// Phase-3a's selection ray masks), hit UV × RT resolution → the P2
/// <see cref="VirtualMouse"/> bridge (<c>WarpTo</c>; trigger press/release →
/// <c>Press</c>/<c>Release</c> so drags work). Everything downstream (InControl
/// module, ClickTracker, IsPointerOverUI, tooltips) works untouched.
///
/// Show policy: auto-appears in <see cref="VRMode.Menu2D"/> (config), hides in
/// scenario modes; in <see cref="VRMode.ModalUI"/> it appears when no converted
/// dialog owns the modal (fallback for unconverted windows).
/// </summary>
internal sealed class FlatScreen
{
    private const float ScreenAspect = 16f / 9f;

    /// <summary>Intro scene name (decompiled GH.Runtime Bootstrap.ShowSplash: "Intro").</summary>
    private const string IntroSceneName = "Intro";

    private GameObject? _quad;
    private Renderer? _quadRenderer;
    private Transform? _reticle;
    private RenderTexture? _rt;
    private bool _visible;
    private bool _pressing;
    private bool _mirrorLogged;

    /// <summary>One captured backbuffer camera + everything needed to restore it.</summary>
    private sealed class CapturedCamera
    {
        public Camera Camera = null!;
        public CameraClearFlags OriginalClearFlags;
        public Color OriginalBackground;
    }

    // Captured camera stack (I1, hardware test #5). The list is reused across
    // frames; a CapturedCamera record only allocates when a NEW camera is first
    // captured (rare event — scene load / MainMenuVideo appearing).
    private readonly System.Collections.Generic.List<CapturedCamera> _captured = new(8);

    /// <summary>The stack's base (lowest-depth) camera — the only one whose clear we force.</summary>
    private CapturedCamera? _base;

    /// <summary>Cameras currently captured into the RT (camera-inventory diagnostics).</summary>
    private static readonly System.Collections.Generic.HashSet<Camera> CapturedSet = new();

    /// <summary>True while the FlatScreen has redirected this camera into its RT.</summary>
    internal static bool IsCaptured(Camera cam) => CapturedSet.Contains(cam);

    // Base-clear rationale (hardware test #4, P3b): the game's menu cameras ship
    // clearFlags=Depth — rendering that into a fresh RT leaves the COLOR buffer
    // (incl. alpha ≈ 0) undefined, so an alpha-blended quad shows nothing in the
    // HMD while the desktop Blit (which ignores alpha) looks perfect. The stack's
    // lowest-depth camera therefore gets an OPAQUE SolidColor clear forced (also
    // fixes RT garbage); every other camera keeps its own clear flags.
    private static readonly Color OpaqueBlack = new(0f, 0f, 0f, 1f);

    // Placement anchors: re-place instantly when the head camera or the rig moves
    // (menu rig rebuild / recenter), instead of waiting for the lazy 45° follow.
    private Camera? _placedHead;
    private Vector3 _placedRigPos;

    // Lazy follow (P3a): only glide the screen back in front after the gaze has
    // been >45° off it for >1 s (prevents chasing quick glances).
    private const float FollowAngleDegrees = 45f;
    private const float FollowDwellSeconds = 1f;
    private const float FollowSettledDegrees = 5f;
    private float _offGazeSince = -1f;
    private bool _gliding;

    // Pre-menu "starting…" indicator (HMD-side sign of life while the intro plays flat).
    private GameObject? _indicator;

    public FlatScreen()
    {
        // P5 (MISSION A.2): scene loads re-wire the game's UI cameras (CanvasManager.
        // OnSceneLoaded re-binds worldCamera) — release the whole captured stack on
        // the bus event and re-capture next Tick instead of waiting for old cameras
        // to die.
        Core.Events.VREvents.SceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Core.Events.SceneLoadedEvent e) => ReleaseStack();

    public void Tick()
    {
        bool preMenu = IsPreMenuScene();

        TickStartingIndicator(preMenu);

        // The quad can be destroyed behind our back (a Single-mode scene load before
        // it was made persistent, an explorer tool, …) — treat it as hidden and let
        // Show() rebuild everything instead of ticking a dead screen forever. This
        // was the P3c-era root cause of the black menu: the quad died with the
        // "Intro" scene unload while the UICamera stayed redirected into the RT.
        if (_visible && _quad == null)
        {
            VRLog.Warn("WorldUI", "FlatScreen quad was destroyed externally — rebuilding.");
            _visible = false;
        }

        bool want = !preMenu && WantVisible();
        if (want && !_visible)
            Show();
        else if (!want && _visible)
            Hide();

        if (!_visible)
            return;

        CaptureStack();

        FollowHead();
        TickPointer();
    }

    /// <summary>
    /// Redirect every backbuffer game camera into our RT (I1, hardware test #5) and
    /// keep the stack's lowest-depth camera on an OPAQUE SolidColor clear (P3b — see
    /// field comment). Cheap per-tick re-assert: game code may rewrite targetTexture
    /// or clearFlags at any time, and new cameras (MainMenuVideo) appear mid-scene.
    /// Excluded: our rig head camera and cameras already targeting a different RT
    /// ('GUI 3D Camera' → character-assembly RT, per the test-#5 camera inventory).
    /// Originals are recorded on first capture and restored by <see cref="ReleaseStack"/>.
    /// No per-frame allocations: shared scan buffer + reused list; records allocate
    /// only when a new camera is first captured.
    /// </summary>
    private void CaptureStack()
    {
        if (_rt == null)
            return;

        Camera? head = Rig.VRRigDriver.HeadCamera;

        // 1. Capture new backbuffer cameras / re-assert the redirect on known ones.
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || (head != null && cam == head))
                continue;
            if (CapturedSet.Contains(cam))
            {
                if (cam.targetTexture != _rt) // game code rewrote it — re-assert
                    cam.targetTexture = _rt;
                continue;
            }
            if (cam.targetTexture != null)
                continue; // renders to its own RT (e.g. 'GUI 3D Camera') — not ours

            var record = new CapturedCamera
            {
                Camera = cam,
                OriginalClearFlags = cam.clearFlags,
                OriginalBackground = cam.backgroundColor,
            };
            _captured.Add(record);
            CapturedSet.Add(cam);
            cam.targetTexture = _rt;
            VRLog.Info("WorldUI", $"FlatScreen stack capture: '{cam.name}' → RenderTexture " +
                                  $"(depth {cam.depth:F1}, clear {record.OriginalClearFlags} kept unless base).");
        }

        // 2. Compact dead entries (scene unloads destroy cameras behind our back).
        for (int i = _captured.Count - 1; i >= 0; i--)
        {
            if (_captured[i].Camera == null)
            {
                if (_captured[i] == _base)
                    _base = null;
                CapturedSet.Remove(_captured[i].Camera);
                _captured.RemoveAt(i);
            }
        }

        // 3. The stack's FIRST camera gets the forced opaque clear. Unity renders
        //    cameras targeting the same RT in ascending depth order, so pick the
        //    lowest depth. Depth ties exist (test-#5 inventory: 'Main Camera' and
        //    'UI Camera' both at depth 1.0) — the UICamera-tagged camera always
        //    composites LAST per game intent (CanvasManager binds all overlay
        //    canvases to it), so on a tie it must NOT be the base or its forced
        //    clear would erase the other cameras' output.
        CapturedCamera? newBase = null;
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (newBase == null
                || c.Camera.depth < newBase.Camera.depth
                || (c.Camera.depth == newBase.Camera.depth && newBase.Camera.CompareTag("UICamera")))
            {
                newBase = c;
            }
        }

        if (newBase != _base)
        {
            // The previous base (still captured) returns to its own clear flags.
            if (_base != null && _base.Camera != null)
            {
                _base.Camera.clearFlags = _base.OriginalClearFlags;
                _base.Camera.backgroundColor = _base.OriginalBackground;
            }
            _base = newBase;
            if (_base != null)
                VRLog.Info("WorldUI", $"FlatScreen stack base: '{_base.Camera.name}' (depth {_base.Camera.depth:F1}) " +
                                      $"clears the RT (clear {_base.OriginalClearFlags} → SolidColor opaque black).");
        }

        // Re-assert the base clear every tick (game code may rewrite it).
        if (_base != null && _base.Camera != null)
        {
            if (_base.Camera.clearFlags != CameraClearFlags.SolidColor)
                _base.Camera.clearFlags = CameraClearFlags.SolidColor;
            if (_base.Camera.backgroundColor != OpaqueBlack)
                _base.Camera.backgroundColor = OpaqueBlack;
        }
    }

    /// <summary>Undo everything <see cref="CaptureStack"/> did and drop all references.</summary>
    private void ReleaseStack()
    {
        for (int i = 0; i < _captured.Count; i++)
        {
            Camera cam = _captured[i].Camera;
            if (cam == null)
                continue;
            if (cam.targetTexture == _rt)
                cam.targetTexture = null;
            // Only the base had its clear forced — leave the others' flags alone
            // (they may have been legitimately changed by game code meanwhile).
            if (_captured[i] == _base)
            {
                cam.clearFlags = _captured[i].OriginalClearFlags;
                cam.backgroundColor = _captured[i].OriginalBackground;
            }
        }
        if (_captured.Count > 0)
            VRLog.Info("WorldUI", $"FlatScreen stack released — {_captured.Count} camera(s) restored to the backbuffer.");
        _captured.Clear();
        CapturedSet.Clear();
        _base = null;
    }

    /// <summary>
    /// End-of-frame hook (WorldUI driver coroutine, after Unity's XR mirror blit):
    /// while the UICamera is redirected into our RT, copy the RT to the desktop
    /// backbuffer so the monitor never goes black and stays mouse-operable.
    /// </summary>
    public void OnEndOfFrame()
    {
        if (!_visible || _rt == null || !_rt.IsCreated())
            return;

        if (!_mirrorLogged)
        {
            _mirrorLogged = true;
            VRLog.Info("WorldUI", $"Desktop mirror active — FlatScreen RT ({_rt.width}x{_rt.height}) " +
                                  "blits to the backbuffer at end of frame.");
        }
        Graphics.Blit(_rt, (RenderTexture?)null);
    }

    public void Shutdown()
    {
        Core.Events.VREvents.SceneLoaded -= OnSceneLoaded;
        Hide();
        DestroyIndicator();
    }

    // ---- policy ------------------------------------------------------------------------

    /// <summary>
    /// True while the game is still in its boot flow: scene 0 (Bootstrap) or the
    /// "Intro" scene (video + logos). Verified against decompiled GH.Runtime
    /// Bootstrap.ShowSplash: Intro and the menu scene both load Single, so nothing
    /// non-persistent survives into the menu. The FlatScreen must not touch the
    /// UICamera's targetTexture here — the intro renders vanilla on the desktop.
    /// </summary>
    private static bool IsPreMenuScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.buildIndex == 0 || scene.name == IntroSceneName;
    }

    private bool WantVisible()
    {
        if (!WorldUIConfig.FlatScreen.Value || !WorldUIConfig.ConversionActive)
            return false;

        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D)
            return WorldUIConfig.FlatScreenAutoShow.Value;

        // Fallback for unconverted modal windows (merchant, level-up, ESC menu...):
        // the confirmation-box surface owns plain dialogs; everything else 2D.
        if (mode == VRMode.ModalUI)
            return !WorldUIConfig.Dialogs.Value || !IsConfirmationBoxOpen();

        return false;
    }

    private static bool IsConfirmationBoxOpen() =>
        Singleton<UIConfirmationBoxManager>.IsInitialized
        && Singleton<UIConfirmationBoxManager>.Instance.IsOpen;

    // ---- pre-menu indicator -----------------------------------------------------------------

    /// <summary>
    /// While the intro plays flat (pre-menu gate) the HMD would show only the menu
    /// rig's grey void — float a small "starting…" label in front of the head so the
    /// player knows the mod is alive and the menu is coming.
    /// </summary>
    private void TickStartingIndicator(bool preMenu)
    {
        bool want = preMenu && VRSession.IsRunning
                    && WorldUIConfig.FlatScreen.Value && WorldUIConfig.Master.Value;
        if (!want)
        {
            if (_indicator != null)
            {
                DestroyIndicator();
                VRLog.Info("WorldUI", "Starting indicator removed (menu scene reached).");
            }
            return;
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        if (_indicator == null)
        {
            _indicator = new GameObject("GloomhavenVR.StartingIndicator");
            Object.DontDestroyOnLoad(_indicator);
            var text = _indicator.AddComponent<TextMeshPro>();
            text.text = "GloomhavenVR\n<size=60%>starting… (intro plays on the desktop)</size>";
            text.fontSize = 1f; // 3D TMP: ~0.1 m line height, comfortable at 1.5 m
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            var rect = (RectTransform)_indicator.transform;
            rect.sizeDelta = new Vector2(3f, 1f);
            VRLayers.Apply(_indicator); // head camera masks include the mod layer (CAMERA-POLICY §2)
            VRLog.Info("WorldUI", "Starting indicator shown (pre-menu scene, FlatScreen gated).");
        }

        Transform h = head.transform;
        Vector3 fwd = h.forward;
        _indicator.transform.SetPositionAndRotation(
            h.position + fwd * 1.5f,
            Quaternion.LookRotation(fwd, Vector3.up)); // TMP front faces -Z → toward the head
    }

    private void DestroyIndicator()
    {
        if (_indicator != null)
        {
            Object.Destroy(_indicator);
            _indicator = null;
        }
    }

    // ---- lifecycle ----------------------------------------------------------------------

    private void Show()
    {
        if (_rt == null)
        {
            _rt = new RenderTexture(Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24)
            {
                name = "GloomhavenVR.FlatScreenRT",
                antiAliasing = 1,
            };
            _rt.Create();
        }

        if (_quad == null)
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "GloomhavenVR.FlatScreen";
            // Menu scenes load Single — the quad must survive them (root cause of the
            // black HMD menu: quad died with the scene, UICamera stayed on the RT).
            Object.DontDestroyOnLoad(_quad);
            Object.Destroy(_quad.GetComponent<Collider>());
            _quadRenderer = _quad.GetComponent<Renderer>();
            // Shader choice (P3b, hardware test #4): the quad must render its texture
            // ALPHA-IGNORING — the RT's alpha channel is whatever the UI left behind.
            //
            // 1. "Hidden/BlitCopy": Unity's always-included internal blit shader —
            //    plain opaque copy of _MainTex (Blend Off, ignores alpha). Verified
            //    shipped: the GAME itself calls Shader.Find("Hidden/BlitCopy")
            //    (decompiled GH.Runtime.FirstPass, RenderHeads.Media.AVProMovieCapture/
            //    CaptureFromCamera360.cs:446), and Graphics.Blit depends on it, so it
            //    cannot be stripped. Its pass states ZTest Always / ZWrite Off — fine
            //    for a menu screen that must never be occluded.
            // 2. Fallback "Sprites/Default" (verified shipped: decompiled ThirdParty
            //    GraphProgress/VertexView calls Shader.Find on it): alpha-blended, but
            //    with the RT now cleared to OPAQUE black (RetargetUiCamera) dst alpha
            //    ≈ 1, so it renders near-opaque instead of invisible.
            // (An opaque keyword/MaterialPropertyBlock variant of Sprites/Default does
            // not exist — the shader has no such keyword — hence the BlitCopy pick.)
            Shader? shader = Shader.Find("Hidden/BlitCopy")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("UI/Default");
            _quadRenderer.sharedMaterial = new Material(shader) { mainTexture = _rt };

            GameObject reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleGo.name = "Reticle";
            Object.Destroy(reticleGo.GetComponent<Collider>());
            reticleGo.transform.SetParent(_quad.transform, worldPositionStays: false);
            reticleGo.GetComponent<Renderer>().sharedMaterial =
                WorldUIAssets.CreateFlatMaterial(new Color(1f, 0.9f, 0.3f, 0.9f));
            _reticle = reticleGo.transform;
            _reticle.gameObject.SetActive(false);

            // Screen + reticle live on the dedicated mod layer — only the rig head
            // camera renders it (its mask ORs the mod bit, never 0; CAMERA-POLICY §2).
            VRLayers.Apply(_quad);
        }

        _quad.SetActive(true);
        CaptureStack();

        // P5 (MISSION A.5): the ModalUI-constrained laser may point at the screen.
        RayInteractor.RegisterUiTarget(_quad.transform);

        PlaceScreen(instant: true);
        _visible = true;
        VRLog.Info("WorldUI", "FlatScreen shown (backbuffer camera stack → RenderTexture; " +
                              "desktop mirror engages at end of frame).");
    }

    private void Hide()
    {
        if (_pressing)
        {
            VirtualMouse.Release();
            _pressing = false;
        }
        ReleaseStack();

        if (_quad != null)
        {
            RayInteractor.UnregisterUiTarget(_quad.transform);
            _quad.SetActive(false);
        }
        if (_rt != null)
        {
            _rt.Release();
            Object.Destroy(_rt);
            _rt = null;
            if (_quad != null)
            {
                Object.Destroy(_quad);
                _quad = null;
                _quadRenderer = null;
                _reticle = null;
            }
        }
        if (_visible)
            VRLog.Info("WorldUI", "FlatScreen hidden — captured cameras restored to the backbuffer.");
        _visible = false;
        _mirrorLogged = false;
        _placedHead = null;
        _offGazeSince = -1f;
        _gliding = false;
    }

    // ---- placement -----------------------------------------------------------------------

    private void PlaceScreen(bool instant)
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 target = h.position + fwd * (1.6f * scale);
        // Quad primitive faces -Z (visible from -forward side): +Z away from viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float width = WorldUIConfig.FlatScreenWidth.Value * scale;
        Vector3 size = new(width, width / ScreenAspect, 1f);

        Transform t = _quad.transform;
        if (instant)
        {
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            t.position = Vector3.Lerp(t.position, target, Time.deltaTime * 3f);
            t.rotation = Quaternion.Slerp(t.rotation, rot, Time.deltaTime * 3f);
        }
        t.localScale = size;

        if (instant)
        {
            _placedHead = head;
            Transform? rig = Rig.VRRigDriver.RigRoot;
            _placedRigPos = rig != null ? rig.position : Vector3.zero;
            LogPlacement(head);
        }
    }

    /// <summary>
    /// One diagnostic line per (re)placement: quad pose vs head pose, culling mask,
    /// layer and shader — makes "quad exists but camera can't see it" visible in the
    /// BepInEx log without an HMD report.
    /// </summary>
    private void LogPlacement(Camera head)
    {
        if (_quad == null || _quadRenderer == null)
            return;
        Shader? shader = _quadRenderer.sharedMaterial != null ? _quadRenderer.sharedMaterial.shader : null;
        VRLog.Info("WorldUI",
            $"FlatScreen quad placed: pos={_quad.transform.position}, size={_quad.transform.localScale}, " +
            $"layer={_quad.layer}, shader='{(shader != null ? shader.name : "NULL")}', " +
            $"RT={( _rt != null ? $"{_rt.width}x{_rt.height}" : "NULL")} | " +
            $"head '{head.name}' pos={head.transform.position}, fwd={head.transform.forward}, " +
            $"mask=0x{head.cullingMask:X8}, clear={head.clearFlags}, stereo={head.stereoTargetEye}.");
    }

    private void FollowHead()
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // Rig rebuild / recenter / head-camera swap: snap the screen back in front of
        // the (new) vantage instead of lazily drifting after it. Placement always uses
        // the TRACKED head's actual world forward (PlaceScreen) — never an assumed
        // axis: hardware test #4 placed the quad "forward" of a vantage the player
        // wasn't facing.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        Vector3 rigPos = rig != null ? rig.position : Vector3.zero;
        if (head != _placedHead || (rigPos - _placedRigPos).sqrMagnitude > 1e-4f)
        {
            PlaceScreen(instant: true);
            _offGazeSince = -1f;
            _gliding = false;
            return;
        }

        // Lazy follow (P3a): after the gaze has been >45° off the screen for >1 s,
        // glide it back in front of the current gaze until it settles (<5°).
        Vector3 toScreen = _quad.transform.position - head.transform.position;
        float angle = Vector3.Angle(head.transform.forward, toScreen);
        if (angle > FollowAngleDegrees)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_gliding && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _gliding = true;
                VRLog.Info("WorldUI", $"FlatScreen lazy follow: gaze {angle:F0}° off for " +
                                      $">{FollowDwellSeconds:F0}s — gliding back in front (head fwd {head.transform.forward}).");
            }
        }
        else
        {
            _offGazeSince = -1f;
        }

        if (_gliding)
        {
            PlaceScreen(instant: false);
            if (angle < FollowSettledDegrees)
            {
                _gliding = false;
                _offGazeSince = -1f;
            }
        }
    }

    // ---- pointer ---------------------------------------------------------------------------

    private void TickPointer()
    {
        if (_quad == null || _rt == null)
            return;

        VRHand? hand = VRHands.Primary;
        IPickProvider? pick = VRHands.PrimaryPick;
        if (pick == null || hand == null || !pick.TryGetPick(out PickPose pose))
        {
            HideReticle();
            return;
        }

        // Ray ∩ screen plane (quad faces -Z; plane normal = -forward toward viewer).
        Transform t = _quad.transform;
        Vector3 normal = -t.forward;
        float denom = Vector3.Dot(pose.Direction, normal);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            HideReticle();
            return;
        }
        float dist = Vector3.Dot(t.position - pose.Origin, normal) / denom;
        if (dist < 0f)
        {
            HideReticle();
            return;
        }

        Vector3 hit = pose.Origin + pose.Direction * dist;
        Vector3 local = t.InverseTransformPoint(hit); // quad local: x/y in [-0.5, 0.5]
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
        {
            HideReticle();
            return;
        }

        // UV → virtual mouse pixels.
        var pixel = new Vector2((local.x + 0.5f) * _rt.width, (local.y + 0.5f) * _rt.height);
        VirtualMouse.WarpTo(pixel);

        if (_reticle != null)
        {
            if (!_reticle.gameObject.activeSelf)
                _reticle.gameObject.SetActive(true);
            _reticle.localPosition = new Vector3(local.x, local.y, -0.005f);
            _reticle.localScale = new Vector3(0.008f, 0.008f * ScreenAspect, 0.008f);
        }

        // Trigger = left mouse button (press/release so drags work). The release
        // condition is the trigger STATE, not the TriggerUp edge: a hands rebuild
        // mid-press (HandsDriver re-creates VRHand instances on rig changes) would
        // swallow the edge forever and leave uGUI in drag state — hover would die
        // globally (I3 hardening; VirtualMouse has a second, time-based watchdog).
        if (hand.TriggerDown && !_pressing)
        {
            _pressing = true;
            VirtualMouse.Press();
        }
        else if (_pressing && !hand.TriggerPressed)
        {
            _pressing = false;
            if (!hand.TriggerUp)
                VRLog.Warn("WorldUI", "FlatScreen pointer: trigger release edge was missed " +
                                      "(hands rebuilt mid-press?) — forced VirtualMouse release.");
            VirtualMouse.Release();
        }
    }

    private void HideReticle()
    {
        if (_reticle != null && _reticle.gameObject.activeSelf)
            _reticle.gameObject.SetActive(false);
        if (_pressing)
        {
            _pressing = false;
            VirtualMouse.Release();
        }
    }
}
