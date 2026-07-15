using GloomhavenVR.Core;
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
/// in favor of the game-shipped <see cref="TrackedPoseDriver"/>
/// (UnityEngine.SpatialTracking — avoids InputSystem-version questions in P1;
/// InputSystem-based tracking incl. controllers is the Phase 2 follow-up).
/// The flat UICamera keeps rendering untouched (2D UI stays on the desktop mirror;
/// full UI work is Phase 3c).
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

    private void Awake() => Instance = this;

    private void Update()
    {
        CameraController controller = CameraController.s_CameraController;
        bool scenarioCameraAlive = controller != null && controller.m_Camera != null;

        // P5 (MISSION A.7): outside a scenario the rig falls back to the menu camera
        // (Camera.main) so the HMD view is head-tracked in the main menu / guildmaster
        // map and the WorldUI flat screen + hands have a tracked anchor.
        RigKind desired =
            !VRSession.IsRunning ? RigKind.None :
            scenarioCameraAlive ? RigKind.Scenario :
            Plugin.MenuRig.Value ? RigKind.Menu :
            RigKind.None;

        // Tear down on kind change or when the owned camera died (menu scene swap).
        if (_rigRoot != null && (desired != _kind || _camera == null))
            TearDownRig();

        if (_rigRoot == null)
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
    }

    private void OnDestroy()
    {
        TearDownRig();
        if (Instance == this)
            Instance = null;
    }

    private void BuildRig(CameraController controller)
    {
        Camera cam = controller.m_Camera;
        _camera = cam;

        _originalParent = cam.transform.parent;
        _originalLocalPos = cam.transform.localPosition;
        _originalLocalRot = cam.transform.localRotation;
        _originalFov = cam.fieldOfView;
        _originalNearClip = cam.nearClipPlane;

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

        // We drive the pose via TrackedPoseDriver — switch off the implicit XR camera
        // tracking the display subsystem would otherwise apply on top.
        XRDevice.DisableAutoXRCameraTracking(cam, true);

        _poseDriver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        _poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        RigRoot = _rigRoot.transform;
        HeadCamera = cam;
        BaseWorldScale = baseScale;
        _kind = RigKind.Scenario;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"VR rig built at focus {controller.FocusPoint}, world scale {scale:F1} " +
                          $"(base {baseScale:F1}, config {Plugin.WorldScale.Value:F1}, " +
                          $"tile size {UnityGameEditorRuntime.s_TileSize.x:F2}).");
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
        _camera = cam;

        _originalParent = cam.transform.parent;
        _originalLocalPos = cam.transform.localPosition;
        _originalLocalRot = cam.transform.localRotation;
        _originalFov = cam.fieldOfView;
        _originalNearClip = cam.nearClipPlane;

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

        XRDevice.DisableAutoXRCameraTracking(cam, true);
        _poseDriver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        _poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        RigRoot = _rigRoot.transform;
        HeadCamera = cam;
        BaseWorldScale = 1f;
        _kind = RigKind.Menu;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"Menu rig built around camera '{cam.name}' (1:1 scale, head-tracked menu view).");
    }

    /// <summary>
    /// The camera to head-track outside scenarios: Camera.main (tag MainCamera), else
    /// the first enabled non-UICamera camera rendering to the backbuffer. Null when the
    /// menu scene has no world camera (the rig then waits; the flat screen is hidden
    /// anyway because it needs a world camera too).
    /// </summary>
    private static Camera? ResolveMenuCamera()
    {
        Camera? cam = Camera.main;
        if (cam != null)
            return cam;

        Camera[] all = Camera.allCameras; // only on the (cheap) no-rig path
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].enabled && all[i].targetTexture == null && !all[i].CompareTag("UICamera"))
                return all[i];
        }
        return null;
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

    /// <summary>Menu recenter: put the head back at the menu camera's authored vantage (1:1).</summary>
    private void RecenterMenu()
    {
        if (_rigRoot == null || _camera == null)
            return;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        // Offset with the NEW yaw applied (rig scale is 1 in the menu).
        Vector3 headOffsetWorld = _menuAnchorYaw * _camera.transform.localPosition;
        _rigRoot.transform.position = _menuAnchorPos - headOffsetWorld;
        VRLog.Info("Rig", "Menu rig recentered at the menu camera vantage.");
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

    private void TearDownRig()
    {
        bool wasMenu = _kind == RigKind.Menu;
        _kind = RigKind.None;
        RigRoot = null;
        HeadCamera = null;
        BaseWorldScale = 0f;

        if (_poseDriver != null)
        {
            Destroy(_poseDriver);
            _poseDriver = null;
        }

        if (_camera != null)
        {
            XRDevice.DisableAutoXRCameraTracking(_camera, false);
            _camera.transform.SetParent(_originalParent, worldPositionStays: true);
            _camera.transform.localPosition = _originalLocalPos;
            _camera.transform.localRotation = _originalLocalRot;
            _camera.fieldOfView = _originalFov;
            _camera.nearClipPlane = _originalNearClip;
            _camera = null;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller != null)
            controller.m_IsCameraCodeControlDisabled = false;

        if (_rigRoot != null)
        {
            Destroy(_rigRoot);
            _rigRoot = null;
            VRLog.Info("Rig", wasMenu
                ? "Menu rig torn down — menu camera restored."
                : "VR rig torn down — game camera restored.");
        }

        _pendingRecenter = false;
    }
}
