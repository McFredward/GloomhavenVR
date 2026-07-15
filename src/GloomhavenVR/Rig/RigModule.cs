using GloomhavenVR.Core;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR camera rig: head-tracked camera, diorama/table scale, world grab/rotate/zoom,
/// comfort options. Phase 1 (feat/xr-bootstrap) + Phase 4 (feat/comfort).
/// Key seams: CameraController.LateUpdate prefix-skip, VROrigin + TrackedPoseDriver.
/// </summary>
internal sealed class RigModule : IVRModule
{
    public string Name => "Rig";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phase 1/4 implement camera rig & comfort).");

    public void Shutdown()
    {
        // Stub — nothing to undo yet.
    }
}
