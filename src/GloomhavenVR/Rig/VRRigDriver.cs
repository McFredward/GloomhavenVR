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
    /// <summary>Fallback diorama scale when auto-detection has no tile size yet.</summary>
    private const float FallbackWorldScale = 12f;

    /// <summary>Real-world size a hex tile should read as on the "table" (meters).</summary>
    private const float TargetHexSizeMeters = 0.15f;

    /// <summary>Real-world eye offset from the board focus at recenter (meters).</summary>
    private const float RecenterEyeHeight = 0.7f;
    private const float RecenterEyeBack = 0.7f;

    private GameObject? _rigRoot;
    private Camera? _camera;
    private TrackedPoseDriver? _poseDriver;
    private bool _pendingRecenter;

    // Original camera state for restoration on teardown.
    private Transform? _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private float _originalFov;
    private float _originalNearClip;

    private void Update()
    {
        CameraController controller = CameraController.s_CameraController;
        bool gameCameraAlive = controller != null && controller.m_Camera != null;

        if (_rigRoot == null && gameCameraAlive && VRSession.IsRunning)
        {
            BuildRig(controller!);
        }
        else if (_rigRoot != null && (!gameCameraAlive || !VRSession.IsRunning))
        {
            TearDownRig();
        }

        // Recenter once tracking delivers the first real pose (localPosition leaves zero).
        if (_pendingRecenter && _camera != null && _camera.transform.localPosition.sqrMagnitude > 1e-6f)
        {
            Recenter();
            _pendingRecenter = false;
        }
    }

    private void OnDestroy() => TearDownRig();

    private void BuildRig(CameraController controller)
    {
        Camera cam = controller.m_Camera;
        _camera = cam;

        _originalParent = cam.transform.parent;
        _originalLocalPos = cam.transform.localPosition;
        _originalLocalRot = cam.transform.localRotation;
        _originalFov = cam.fieldOfView;
        _originalNearClip = cam.nearClipPlane;

        // Freeze the orbit-camera code paths not covered by the LateUpdate prefix-skip
        // (SmartFocus/ZoomTo coroutines check this flag too).
        controller.m_IsCameraCodeControlDisabled = true;

        float scale = ResolveWorldScale();

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

        _pendingRecenter = true;

        VRLog.Info("Rig", $"VR rig built at focus {controller.FocusPoint}, world scale {scale:F1} " +
                          $"(config {Plugin.WorldScale.Value:F1}, tile size {UnityGameEditorRuntime.s_TileSize.x:F2}).");
    }

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up at a comfortable
    /// table-edge spot: eyes ~0.7 m (real) above the orbit focus plane and ~0.7 m back.
    /// P1: called automatically on the first tracked pose; controller binding follows
    /// with input work in Phase 2/4.
    /// </summary>
    internal void Recenter()
    {
        if (_rigRoot == null || _camera == null)
            return;

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        float scale = _rigRoot.transform.localScale.x;
        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + _rigRoot.transform.rotation * (Vector3.back * (RecenterEyeBack * scale))
                                   + Vector3.up * (RecenterEyeHeight * scale);
        Vector3 headOffsetWorld = _rigRoot.transform.rotation * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;

        VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position}.");
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
            VRLog.Info("Rig", "VR rig torn down — game camera restored.");
        }

        _pendingRecenter = false;
    }
}
