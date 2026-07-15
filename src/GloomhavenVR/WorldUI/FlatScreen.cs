using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Floating 2D screen (ROADMAP P3c #6): a world-space quad showing the UICamera's
/// output for everything that is not physicalized — main menu, guildmaster map,
/// merchant, level-up, and any unconverted window.
///
/// Rendering: while the screen is visible the "UICamera"-tagged camera is retargeted
/// onto a RenderTexture shown on the quad (UUVR screen-mirror pattern; UI-ARCH §2.1
/// consequence (a)). The camera reference is re-resolved after every scene load —
/// this coordinates with the game's own re-wiring, verified via ilspycmd
/// (GH.Runtime.dll, CanvasManager): <c>private void OnSceneLoaded(Scene scene,
/// LoadSceneMode mode)</c> re-binds <c>persistentUICanvas.worldCamera</c> /
/// <c>tooltipCanvas.worldCamera</c> to the first camera tagged "UICamera" (IL 70 B,
/// PATCH-TARGETS §1.7 ✅) — Screen-Space-Camera canvases follow their worldCamera
/// into our RenderTexture automatically. <c>targetTexture</c> is restored on hide,
/// shutdown and camera change.
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
/// XR EXCLUSION: with the XR display running, any camera without a targetTexture
/// renders to the HMD (stereoTargetEye defaults to Both). Outside scenarios the
/// UICamera is therefore forced to <see cref="StereoTargetEyeMask.None"/> (renders
/// to the main display only) + <see cref="XRDevice.DisableAutoXRCameraTracking"/>,
/// restored when leaving Menu2D / on hide / shutdown / config-off.
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
    private Camera? _uiCamera;
    private bool _visible;
    private bool _pressing;
    private bool _mirrorLogged;

    // XR exclusion bookkeeping (restore-on-release).
    private Camera? _guardedCamera;
    private StereoTargetEyeMask _guardedOriginalEye;
    private int _resolveCooldown;

    // Placement anchors: re-place instantly when the head camera or the rig moves
    // (menu rig rebuild / recenter), instead of waiting for the lazy 45° follow.
    private Camera? _placedHead;
    private Vector3 _placedRigPos;

    // Pre-menu "starting…" indicator (HMD-side sign of life while the intro plays flat).
    private GameObject? _indicator;

    public FlatScreen()
    {
        // P5 (MISSION A.2): scene loads re-wire the game's UI cameras (CanvasManager.
        // OnSceneLoaded re-binds worldCamera) — drop our reference on the bus event and
        // re-resolve/re-target next Tick instead of waiting for the old camera to die.
        Core.Events.VREvents.SceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Core.Events.SceneLoadedEvent e)
    {
        if (_uiCamera != null && _uiCamera.targetTexture == _rt)
            _uiCamera.targetTexture = null;
        _uiCamera = null;
        _resolveCooldown = 0;
    }

    public void Tick()
    {
        bool preMenu = IsPreMenuScene();

        TickUiCameraGuard(preMenu);
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

        // Camera may die/change with scene loads.
        if (_uiCamera == null)
        {
            ResolveUiCamera();
            if (_uiCamera == null)
                return;
        }
        if (_uiCamera.targetTexture != _rt)
        {
            _uiCamera.targetTexture = _rt;
            VRLog.Info("WorldUI", $"FlatScreen: UICamera '{_uiCamera.name}' → RenderTexture.");
        }

        FollowHead();
        TickPointer();
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
        ReleaseUiCameraGuard();
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

    // ---- UICamera XR exclusion ------------------------------------------------------------

    /// <summary>
    /// While VR runs and no scenario is up, the UICamera must not render into the HMD:
    /// with the XR display active a camera without targetTexture renders to both eyes
    /// by default, which (a) splatters screen-space UI across the stereo view and
    /// (b) steals the desktop backbuffer (the window falls back to the XR mirror).
    /// StereoTargetEyeMask.None = "render to the main (desktop) display only".
    /// Fully vanilla when [WorldUI] FlatScreen or Master is off.
    /// </summary>
    private void TickUiCameraGuard(bool preMenu)
    {
        bool guardWanted = VRSession.IsRunning
                           && WorldUIConfig.FlatScreen.Value
                           && WorldUIConfig.Master.Value
                           && (preMenu || VRModeStateMachine.CurrentMode == VRMode.Menu2D);

        if (!guardWanted)
        {
            ReleaseUiCameraGuard();
            return;
        }

        if (_uiCamera == null)
        {
            // Throttled re-resolve (Camera.allCameras allocates) — a scene load resets
            // the cooldown so the guard reacquires promptly after CanvasManager rewires.
            if (_resolveCooldown-- > 0)
                return;
            _resolveCooldown = 30;
            ResolveUiCamera();
            if (_uiCamera == null)
                return;
        }

        if (_guardedCamera == _uiCamera)
            return;

        ReleaseUiCameraGuard();
        _guardedCamera = _uiCamera;
        _guardedOriginalEye = _uiCamera.stereoTargetEye;
        _uiCamera.stereoTargetEye = StereoTargetEyeMask.None;
        XRDevice.DisableAutoXRCameraTracking(_uiCamera, true);
        VRLog.Info("WorldUI", $"UICamera '{_uiCamera.name}' excluded from XR rendering " +
                              $"(stereoTargetEye {_guardedOriginalEye} → None, auto XR tracking off) — " +
                              "the desktop keeps its 2D UI.");
    }

    private void ReleaseUiCameraGuard()
    {
        Camera? cam = _guardedCamera;
        _guardedCamera = null;
        if (cam == null) // also true when the camera died with a scene unload
            return;
        cam.stereoTargetEye = _guardedOriginalEye;
        XRDevice.DisableAutoXRCameraTracking(cam, false);
        VRLog.Info("WorldUI", $"UICamera '{cam.name}' restored to vanilla XR behavior " +
                              $"(stereoTargetEye {_guardedOriginalEye}).");
    }

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
            VRLog.Info("WorldUI", "Starting indicator shown (pre-menu scene, FlatScreen gated).");
        }

        Transform h = head.transform;
        Vector3 fwd = h.forward;
        _indicator.transform.SetPositionAndRotation(
            h.position + fwd * 1.5f,
            Quaternion.LookRotation(fwd, Vector3.up)); // TMP front faces -Z → toward the head
        EnsureLayerVisible(_indicator, head);
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
        ResolveUiCamera();

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
            // Shader choice verified against the shipped game: the game itself calls
            // Shader.Find("Sprites/Default") (decompiled ThirdParty GraphProgress/
            // VertexView) and the P4 comfort vignette renders with it. "Unlit/Texture"
            // is referenced by nothing the game ships and may be stripped.
            Shader? shader = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Texture")
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
        }

        _quad.SetActive(true);
        if (_uiCamera != null)
        {
            _uiCamera.targetTexture = _rt;
            VRLog.Info("WorldUI", $"FlatScreen: UICamera '{_uiCamera.name}' → RenderTexture.");
        }

        // P5 (MISSION A.5): the ModalUI-constrained laser may point at the screen.
        RayInteractor.RegisterUiTarget(_quad.transform);

        PlaceScreen(instant: true);
        _visible = true;
        VRLog.Info("WorldUI", "FlatScreen shown (UICamera → RenderTexture; desktop mirror engages at end of frame).");
    }

    private void Hide()
    {
        if (_pressing)
        {
            VirtualMouse.Release();
            _pressing = false;
        }
        if (_uiCamera != null && _uiCamera.targetTexture == _rt)
            _uiCamera.targetTexture = null;
        _uiCamera = null;

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
            VRLog.Info("WorldUI", "FlatScreen hidden — UICamera restored to the backbuffer.");
        _visible = false;
        _mirrorLogged = false;
        _placedHead = null;
    }

    private void ResolveUiCamera()
    {
        // Prefer the scenario UIManager's serialized camera; otherwise scan by tag
        // (only on show/scene change — Camera.allCameras allocates).
        UIManager manager = UIManager.Instance;
        if (manager != null && manager.UICamera != null)
        {
            _uiCamera = manager.UICamera;
            return;
        }
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].CompareTag("UICamera"))
            {
                _uiCamera = all[i];
                return;
            }
        }
        _uiCamera = null;
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

        EnsureLayerVisible(_quad, head);

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
            $"head '{head.name}' pos={head.transform.position}, mask=0x{head.cullingMask:X8}, " +
            $"clear={head.clearFlags}, stereo={head.stereoTargetEye}.");
    }

    /// <summary>
    /// Make sure <paramref name="go"/> (and children) sit on a layer the camera
    /// actually renders — prefer keeping the current layer, else Default (0), else
    /// the lowest bit set in the camera's culling mask.
    /// </summary>
    private static void EnsureLayerVisible(GameObject go, Camera camera)
    {
        int mask = camera.cullingMask;
        if ((mask & (1 << go.layer)) != 0)
            return;

        int layer = (mask & 1) != 0 ? 0 : -1;
        if (layer < 0)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    layer = i;
                    break;
                }
            }
        }
        if (layer < 0)
            return; // camera renders nothing — logged via LogPlacement's mask

        VRLog.Info("WorldUI", $"'{go.name}' layer {go.layer} not in camera '{camera.name}' mask " +
                              $"0x{mask:X8} — moving to layer {layer}.");
        SetLayerRecursive(go.transform, layer);
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    private void FollowHead()
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // Rig rebuild / recenter / head-camera swap: snap the screen back in front of
        // the (new) vantage instead of lazily drifting after it.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        Vector3 rigPos = rig != null ? rig.position : Vector3.zero;
        if (head != _placedHead || (rigPos - _placedRigPos).sqrMagnitude > 1e-4f)
        {
            PlaceScreen(instant: true);
            return;
        }

        // Lazy follow: re-center only when the screen drifts far out of view.
        Vector3 toScreen = _quad.transform.position - head.transform.position;
        float angle = Vector3.Angle(head.transform.forward, toScreen);
        if (angle > 45f)
            PlaceScreen(instant: false);
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

        // Trigger = left mouse button (press/release so drags work).
        if (hand.TriggerDown && !_pressing)
        {
            _pressing = true;
            VirtualMouse.Press();
        }
        else if (_pressing && hand.TriggerUp)
        {
            _pressing = false;
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
