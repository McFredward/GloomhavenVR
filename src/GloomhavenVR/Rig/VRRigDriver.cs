using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>
/// Persistent driver that owns the VR camera rig. Each frame it watches for the
/// game's scenario camera (<c>CameraController.s_CameraController.m_Camera</c>,
/// created per scenario scene) and builds/tears down the rig accordingly:
///
/// <code>
/// GloomhavenVR.VRRig (rig root: at orbit focus, yaw-aligned, scaled by WorldScale)
/// └── [game scenario camera] + TrackedPoseDriver (head pose, center eye)
/// </code>
///
/// Diorama scale (ARCHITECTURE §3): the rig root is scaled by WorldScale (game units
/// per real meter) so head motion maps 1 m → WorldScale units and the board reads
/// as a table. Camera strategy: the game camera itself becomes the head camera —
/// stereo rendering starts automatically once the XR display runs; implicit head
/// pose driving is disabled (<see cref="XRDevice.DisableAutoXRCameraTracking"/>)
/// in favor of the game-shipped <see cref="TrackedPoseDriver"/>.
///
/// RIG LIFETIME (hardware test #3 root cause #1): menu scene swaps can DISABLE the
/// camera the menu rig was built around without destroying it (Gloomhaven_unified →
/// MainMenu left 'Camera' fake-alive but deactivated — a destroyed-only check never
/// fired, the HMD froze grey). The health check in <see cref="Update"/> therefore
/// tears down and rebuilds when the head camera is destroyed OR disabled on ANY
/// frame, when the rig root is destroyed externally, and — on scene load — when a
/// better menu camera appeared (tag MainCamera &gt; Camera.main &gt; highest-depth
/// enabled backbuffer camera that isn't the UICamera). Every teardown/rebuild is
/// logged with its trigger reason. Hands re-home automatically: HandsDriver polls
/// <see cref="RigRoot"/> every frame and rebuilds under the new root the frame it
/// changes (event-free but per-frame — never scene-driven).
///
/// CAMERA OWNERSHIP (root causes #2/#3): this driver is the pump for
/// <see cref="VRCameraPolicy"/> (only the rig head renders stereo — swept on scene
/// load, rig rebuild and periodically) and owns the head culling-mask policy: the
/// head camera renders the tracked game camera's mask OR'd with
/// <see cref="VRLayers.ModLayerMask"/>, never 0 (a zero source mask — MainMenu's
/// 'Camera' shipped 0x00000000 — falls back to Default | mod layer). Re-asserted
/// every frame; original mask/stereo restored on teardown.
/// </summary>
internal sealed class VRRigDriver : MonoBehaviour
{
    /// <summary>
    /// Tracking-space root of the VR rig while it exists, else null. XR device poses
    /// (head, hands) are local to this transform; its lossyScale is the diorama scale.
    /// Phase-2 consumers (Hands) parent their tracked objects under this.
    /// </summary>
    internal static Transform? RigRoot { get; private set; }

    /// <summary>The head-tracked camera while the rig exists (the game's scenario camera).</summary>
    internal static Camera? HeadCamera { get; private set; }

    /// <summary>
    /// Base (unmultiplied) diorama scale resolved at rig build, 0 while no rig. Phase-4
    /// comfort clamps pinch-scale relative to this ([Comfort] ScaleMin/ScaleMax).
    /// </summary>
    internal static float BaseWorldScale { get; private set; }

    /// <summary>The live driver instance (for <see cref="RequestRecenter"/>), if any.</summary>
    internal static VRRigDriver? Instance { get; private set; }

    /// <summary>Fallback diorama scale when auto-detection has no tile size yet.</summary>
    private const float FallbackWorldScale = 12f;

    /// <summary>Real-world size a hex tile should read as on the "table" (meters).</summary>
    private const float TargetHexSizeMeters = 0.15f;

    /// <summary>Stereo-policy sweep cadence (frames) between the event-driven sweeps.</summary>
    private const int SweepIntervalFrames = 30;

    /// <summary>What the current rig is built around (P5: menu rig added, MISSION A.7).</summary>
    private enum RigKind
    {
        None,
        Scenario,
        Menu
    }

    private GameObject? _rigRoot;
    private RigKind _kind;
    private Camera? _camera;
    private TrackedPoseDriver? _poseDriver;
    private bool _pendingRecenter;
    private int _sweepCountdown;
    private bool _sceneRecheck;
    private string _sceneRecheckName = "";
    private string _rebuildTrigger = "initial";

    // Menu rig anchor: where the menu camera stood when we took it over — recenter
    // puts the player's head back there (real 1:1 scale, no table math).
    private Vector3 _menuAnchorPos;
    private Quaternion _menuAnchorYaw;

    // Original camera state for restoration on teardown.
    private Transform? _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private float _originalFov;
    private float _originalNearClip;
    private CameraClearFlags _originalClearFlags;
    private Color _originalBackground;
    private int _originalCullingMask;
    private StereoTargetEyeMask _originalStereo;

    /// <summary>
    /// Menu rig clear color (menu-blackscreen fix): NOT black, so an HMD report can
    /// distinguish "camera renders, content missing" (grey void) from "camera dead /
    /// not rendering" (pitch black).
    /// </summary>
    private static readonly Color MenuVoidColor = new(0.12f, 0.13f, 0.15f, 1f);

    private void Awake()
    {
        Instance = this;
        VREvents.SceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(SceneLoadedEvent e)
    {
        _sceneRecheck = true;
        _sceneRecheckName = e.Scene.name;
    }

    private void Update()
    {
        CameraController controller = CameraController.s_CameraController;
        bool scenarioCameraAlive = controller != null && controller.m_Camera != null;

        // P5 (MISSION A.7): outside a scenario the rig falls back to the menu camera
        // so the HMD view is head-tracked in the main menu / guildmaster map and the
        // WorldUI flat screen + hands have a tracked anchor.
        RigKind desired =
            !VRSession.IsRunning ? RigKind.None :
            scenarioCameraAlive ? RigKind.Scenario :
            Plugin.MenuRig.Value ? RigKind.Menu :
            RigKind.None;

        bool sceneRecheck = _sceneRecheck;
        _sceneRecheck = false;

        // Health check — tear down (and rebuild below) the frame anything breaks.
        // Order matters: kind change > camera destroyed > camera disabled > root
        // destroyed > a better camera appeared with a scene load.
        string? teardownReason = null;
        if (_kind != RigKind.None)
        {
            if (desired != _kind)
                teardownReason = $"rig kind change {_kind} → {desired}";
            else if (_camera == null)
                teardownReason = "head camera destroyed";
            else if (!_camera.isActiveAndEnabled)
                teardownReason = $"head camera '{_camera.name}' disabled/deactivated";
            else if (_rigRoot == null)
                teardownReason = "rig root destroyed externally";
            else if (sceneRecheck && _kind == RigKind.Menu)
            {
                Camera? best = ResolveMenuCamera();
                if (best != null && best != _camera)
                    teardownReason = $"scene '{_sceneRecheckName}' brought a better menu camera '{best.name}'";
            }
        }

        if (teardownReason != null)
        {
            TearDownRig(teardownReason);
            _rebuildTrigger = teardownReason;
        }

        if (_kind == RigKind.None)
        {
            if (desired == RigKind.Scenario)
                BuildRig(controller!);
            else if (desired == RigKind.Menu)
                BuildMenuRig();
        }

        // Recenter once tracking delivers the first real pose (localPosition leaves zero).
        if (_pendingRecenter && _camera != null && _camera.transform.localPosition.sqrMagnitude > 1e-6f)
        {
            Recenter();
            _pendingRecenter = false;
        }

        TickHeadCullingMask();
        TickCameraPolicy(sceneRecheck);
    }

    private void OnDestroy()
    {
        VREvents.SceneLoaded -= OnSceneLoaded;
        TearDownRig("rig driver destroyed (shutdown/hot reload)");
        VRCameraPolicy.RestoreAll();
        if (Instance == this)
            Instance = null;
    }

    // ---- camera ownership policies (docs/CAMERA-POLICY.md) --------------------------------

    /// <summary>
    /// Head culling mask policy: tracked game camera's mask OR the mod layer bit,
    /// never 0. Cheap per-frame re-assert — game code and CanvasConversion may
    /// rewrite the mask; the mod bit (and non-zero-ness) must survive.
    /// </summary>
    private static int ComposeHeadMask(int sourceMask) =>
        (sourceMask == 0 ? 1 : sourceMask) | VRLayers.ModLayerMask;

    private void TickHeadCullingMask()
    {
        if (_kind == RigKind.None || _camera == null)
            return;
        int mask = _camera.cullingMask;
        int wanted = ComposeHeadMask(mask);
        if (mask != wanted)
            _camera.cullingMask = wanted;
    }

    /// <summary>
    /// Stereo-exclusion pump: sweep immediately on scene loads (new foreign cameras,
    /// e.g. MainMenu's stereo=Both 'Main Camera'), otherwise on a frame cadence that
    /// also catches cameras created mid-scene. Rig rebuilds sweep inside Build*.
    /// </summary>
    private void TickCameraPolicy(bool sceneLoaded)
    {
        if (!VRSession.IsRunning)
            return;
        if (sceneLoaded)
        {
            VRCameraPolicy.PruneDead();
            VRCameraPolicy.Sweep("scene load");
            _sweepCountdown = SweepIntervalFrames;
            return;
        }
        if (--_sweepCountdown > 0)
            return;
        _sweepCountdown = SweepIntervalFrames;
        VRCameraPolicy.Sweep("periodic");
    }

    /// <summary>Common head-camera takeover: snapshot originals, apply mask/stereo/tracking policy.</summary>
    private void AdoptHeadCamera(Camera cam)
    {
        _camera = cam;

        _originalParent = cam.transform.parent;
        _originalLocalPos = cam.transform.localPosition;
        _originalLocalRot = cam.transform.localRotation;
        _originalFov = cam.fieldOfView;
        _originalNearClip = cam.nearClipPlane;
        _originalClearFlags = cam.clearFlags;
        _originalBackground = cam.backgroundColor;
        _originalCullingMask = cam.cullingMask;

        // Stereo: this camera is the ONE stereo renderer. Reclaim returns the stereo
        // mask from before any policy sweep touched it (for teardown restore).
        _originalStereo = VRCameraPolicy.Reclaim(cam);
        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        VRCameraPolicy.AllowedHead = cam;

        // Culling: never 0, always includes the mod layer (hands/lasers/screen).
        cam.cullingMask = ComposeHeadMask(_originalCullingMask);

        // We drive the pose via TrackedPoseDriver — switch off the implicit XR camera
        // tracking the display subsystem would otherwise apply on top.
        XRDevice.DisableAutoXRCameraTracking(cam, true);
    }

    private void AttachPoseDriver(Camera cam)
    {
        _poseDriver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        _poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }

    // ---- build ---------------------------------------------------------------------------

    private void BuildRig(CameraController controller)
    {
        Camera cam = controller.m_Camera;
        AdoptHeadCamera(cam);

        // Belt & braces on top of the LateUpdate/RefreshFocusPosition prefix-skips.
        // NOT durable on its own: MoveToLook and scripted flows re-toggle this flag
        // (PATCH-TARGETS.md §1.3) — the Harmony skips are the real ownership switch.
        controller.m_IsCameraCodeControlDisabled = true;

        float baseScale = ResolveWorldScale();
        // Re-apply the pinch-scale the player last reached ([Comfort] SavedScaleMultiplier).
        float scale = baseScale * ComfortSettings.ClampedSavedMultiplier;

        _rigRoot = new GameObject("GloomhavenVR.VRRig");
        // Rig at the orbit focus, yaw taken from the current camera so the board is
        // oriented the way the player last saw it flat.
        _rigRoot.transform.position = controller.FocusPoint;
        _rigRoot.transform.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
        _rigRoot.transform.localScale = Vector3.one * scale;

        cam.transform.SetParent(_rigRoot.transform, worldPositionStays: false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        // Near plane in world units so ~5 real cm in front of the eyes still renders.
        cam.nearClipPlane = 0.05f * scale;

        AttachPoseDriver(cam);

        RigRoot = _rigRoot.transform;
        HeadCamera = cam;
        BaseWorldScale = baseScale;
        _kind = RigKind.Scenario;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"VR rig built at focus {controller.FocusPoint}, world scale {scale:F1} " +
                          $"(base {baseScale:F1}, config {Plugin.WorldScale.Value:F1}, " +
                          $"tile size {UnityGameEditorRuntime.s_TileSize.x:F2}; " +
                          $"mask 0x{_originalCullingMask:X8} → 0x{cam.cullingMask:X8}) — trigger: {_rebuildTrigger}.");
        VRCameraPolicy.Sweep("scenario rig built");
    }

    /// <summary>
    /// P5 (MISSION A.7): head-track the MENU camera at real 1:1 scale so Menu2D is not
    /// a frozen viewpoint — the WorldUI flat screen (and the hands driving its pointer)
    /// anchor to a tracked camera in the main menu / guildmaster screens. Torn down as
    /// soon as a scenario camera appears (the scenario rig takes over).
    /// </summary>
    private void BuildMenuRig()
    {
        Camera? cam = ResolveMenuCamera();
        if (cam == null)
            return;
        AdoptHeadCamera(cam);

        // Menu-blackscreen fix: menu scenes may give the head camera nothing to render
        // (UI lives on the separate UICamera). A black clear then looks identical to a
        // dead camera. Force a dark-grey solid clear so "renders but empty" is visibly
        // distinct — but keep a Skybox untouched (it IS visible content). Restored on
        // teardown along with the rest of the camera state.
        if (cam.clearFlags != CameraClearFlags.Skybox)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MenuVoidColor;
        }

        // Anchor: the camera's authored vantage — recenter puts the head back here.
        _menuAnchorPos = cam.transform.position;
        _menuAnchorYaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);

        _rigRoot = new GameObject("GloomhavenVR.VRRig");
        _rigRoot.transform.position = _menuAnchorPos;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        _rigRoot.transform.localScale = Vector3.one;

        cam.transform.SetParent(_rigRoot.transform, worldPositionStays: false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.05f;

        AttachPoseDriver(cam);

        RigRoot = _rigRoot.transform;
        HeadCamera = cam;
        BaseWorldScale = 1f;
        _kind = RigKind.Menu;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"Menu rig built around camera '{cam.name}' (1:1 scale, head-tracked menu view; " +
                          $"clear {_originalClearFlags} → {cam.clearFlags} '{cam.backgroundColor}', " +
                          $"mask 0x{_originalCullingMask:X8} → 0x{cam.cullingMask:X8}, " +
                          $"stereo {_originalStereo} → Both) — trigger: {_rebuildTrigger}.");
        VRCameraPolicy.Sweep("menu rig built");
    }

    /// <summary>
    /// The camera to head-track outside scenarios, best first: Camera.main (tag
    /// MainCamera) → highest-depth enabled backbuffer camera that isn't the UICamera.
    /// Null when the menu scene has no world camera (the rig then waits; the flat
    /// screen is hidden anyway because it needs a world camera too).
    /// </summary>
    private static Camera? ResolveMenuCamera()
    {
        Camera? cam = Camera.main;
        if (cam != null)
            return cam;

        // Cold path only (no-rig frames / scene-load recheck) — shared non-alloc buffer.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] all);
        Camera? best = null;
        for (int i = 0; i < count; i++)
        {
            Camera candidate = all[i];
            if (candidate == null || !candidate.enabled || candidate.targetTexture != null
                || candidate.CompareTag("UICamera"))
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }
        return best;
    }

    /// <summary>Recenter the live rig, if any (Phase-4 comfort entry point — chord/panel/dev key).</summary>
    internal static void RequestRecenter() => Instance?.Recenter();

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up at the configured
    /// table-edge spot: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real)
    /// above the orbit focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/>
    /// back (standing/seated presets + [Comfort] TableHeightOffset). Called automatically
    /// on the first tracked pose; Phase 4 binds it to the B+Y hold chord (see
    /// <see cref="Comfort"/>).
    /// </summary>
    internal void Recenter()
    {
        if (_rigRoot == null || _camera == null)
            return;

        if (_kind == RigKind.Menu)
        {
            RecenterMenu();
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        float scale = _rigRoot.transform.localScale.x;
        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + _rigRoot.transform.rotation * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                                   + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);
        Vector3 headOffsetWorld = _rigRoot.transform.rotation * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);

        VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position} " +
                          $"(seated {(ComfortSettings.IsBound && ComfortSettings.SeatedMode.Value ? "yes" : "no")}).");
    }

    /// <summary>
    /// Menu recenter: put the head back at the menu camera's authored vantage (1:1).
    /// Sign convention (verified against hardware test #3 logs): rig = anchor − yaw·headLocal
    /// puts head world = rig + yaw·headLocal = anchor exactly. With floor-origin
    /// tracking headLocal.y ≈ eye height, so the rig root legitimately sits ~1.1–1.7 m
    /// BELOW the anchor (the test's rig y=−1.09 with anchor y=0 was correct; the odd
    /// "head at −1.09" later was the disabled camera's pose driver going stale — fixed
    /// by the Update health check, not by this math).
    /// </summary>
    private void RecenterMenu()
    {
        if (_rigRoot == null || _camera == null)
            return;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        // Offset with the NEW yaw applied (rig scale is 1 in the menu).
        Vector3 headOffsetWorld = _menuAnchorYaw * _camera.transform.localPosition;
        _rigRoot.transform.position = _menuAnchorPos - headOffsetWorld;
        VRLog.Info("Rig", $"Menu rig recentered at the menu camera vantage (anchor {_menuAnchorPos}, " +
                          $"head local {_camera.transform.localPosition}, rig root {_rigRoot.transform.position}).");
    }

    /// <summary>
    /// WorldScale config wins when &gt; 0; otherwise derive from the runtime hex tile
    /// size (<c>UnityGameEditorRuntime.s_TileSize</c>, BOARD-INPUT §2: x = hex width in
    /// world units) so one hex reads as ~15 cm on the table. Falls back to 12× when the
    /// tile size isn't initialized yet (outside a scenario).
    /// </summary>
    private static float ResolveWorldScale()
    {
        float configured = Plugin.WorldScale.Value;
        if (configured > 0f)
            return Mathf.Clamp(configured, 1f, 100f);

        float tileSize = UnityGameEditorRuntime.s_TileSize.x;
        if (tileSize <= 0.001f)
            return FallbackWorldScale;

        return Mathf.Clamp(tileSize / TargetHexSizeMeters, 1f, 100f);
    }

    private void TearDownRig(string reason)
    {
        bool hadRig = _kind != RigKind.None;
        bool wasMenu = _kind == RigKind.Menu;
        _kind = RigKind.None;
        RigRoot = null;
        HeadCamera = null;
        BaseWorldScale = 0f;
        VRCameraPolicy.AllowedHead = null;

        if (_poseDriver != null)
        {
            Destroy(_poseDriver);
            _poseDriver = null;
        }

        if (_camera != null) // Unity-alive: restore even when merely disabled
        {
            XRDevice.DisableAutoXRCameraTracking(_camera, false);
            _camera.transform.SetParent(_originalParent, worldPositionStays: true);
            _camera.transform.localPosition = _originalLocalPos;
            _camera.transform.localRotation = _originalLocalRot;
            _camera.fieldOfView = _originalFov;
            _camera.nearClipPlane = _originalNearClip;
            _camera.clearFlags = _originalClearFlags;
            _camera.backgroundColor = _originalBackground;
            _camera.cullingMask = _originalCullingMask;
            _camera.stereoTargetEye = _originalStereo;
            _camera = null;
        }
        _camera = null; // clear the fake-null reference too

        CameraController controller = CameraController.s_CameraController;
        if (controller != null)
            controller.m_IsCameraCodeControlDisabled = false;

        if (_rigRoot != null)
        {
            Destroy(_rigRoot);
            _rigRoot = null;
        }
        _rigRoot = null;

        if (hadRig)
        {
            VRLog.Info("Rig", wasMenu
                ? $"Menu rig torn down ({reason}) — menu camera restored."
                : $"VR rig torn down ({reason}) — game camera restored.");
        }

        _pendingRecenter = false;
    }
}
