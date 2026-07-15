using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR camera rig: head-tracked camera over the game's scenario camera, diorama/table
/// scale, recenter. Phase 1 (feat/xr-bootstrap); world grab/comfort follow in Phase 4.
/// Key seams: <see cref="CameraController_LateUpdate_Patch"/> (prefix-skip) +
/// <see cref="VRRigDriver"/> (rig lifecycle).
/// </summary>
internal sealed class RigModule : IVRModule
{
    public string Name => "Rig";

    private GameObject? _driverGo;

    public void Init()
    {
        if (!VRSession.IsRunning)
        {
            VRLog.Debug(Name, "VR not running — rig not installed.");
            return;
        }

        // The patches are no-ops (prefix returns true) whenever VRSession.IsRunning is
        // false, so applying them here is safe even if VR later shuts down.
        VRSession.Harmony?.PatchAll(typeof(CameraController_LateUpdate_Patch));
        VRSession.Harmony?.PatchAll(typeof(CameraController_RefreshFocusPosition_Patch));

        _driverGo = new GameObject("GloomhavenVR.RigDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<VRRigDriver>();

        VRLog.Info(Name, "Rig driver installed — waiting for a scenario camera.");
    }

    public void Shutdown()
    {
        // VRRigDriver.OnDestroy restores the game camera. Harmony patches are removed
        // collectively by Plugin.OnDestroy (UnpatchSelf).
        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }
    }
}
