using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR camera rig: head-tracked camera over the game's scenario camera, diorama/table
/// scale, recenter (Phase 1, feat/xr-bootstrap) + Demeo-style world grab, snap turn,
/// height/recenter comfort and the <see cref="ComfortSettings"/> API (Phase 4,
/// feat/comfort — the in-VR settings *panel* is deferred to the P3c panel framework).
///
/// Key seams: <see cref="CameraController_LateUpdate_Patch"/> (prefix-skip) +
/// <see cref="VRRigDriver"/> (rig lifecycle) + <see cref="WorldGrab"/>/<see cref="SnapTurn"/>/
/// <see cref="Comfort"/> (rig manipulation — always the rig, never game objects).
///
/// Also installs in dev mode without an HMD ([Dev] Enabled): the comfort components run
/// against a hidden proxy rig ([Dev] SimulateHands) so grab/turn logic is exercisable
/// flat; the Harmony camera patches and the real rig driver stay VR-only.
/// </summary>
internal sealed class RigModule : IVRModule
{
    public string Name => "Rig";

    private GameObject? _driverGo;

    public void Init()
    {
        bool vr = VRSession.IsRunning;
        bool dev = Plugin.DevMode.Value;
        if (!vr && !dev)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — rig not installed.");
            return;
        }

        ComfortSettings.Bind();

        if (vr)
        {
            // The patches are no-ops (prefix returns true) whenever VRSession.IsRunning is
            // false, so applying them here is safe even if VR later shuts down.
            VRSession.Harmony?.PatchAll(typeof(CameraController_LateUpdate_Patch));
            VRSession.Harmony?.PatchAll(typeof(CameraController_RefreshFocusPosition_Patch));
        }

        _driverGo = new GameObject("GloomhavenVR.RigDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        if (vr)
            _driverGo.AddComponent<VRRigDriver>();

        // Phase-4 comfort stack (each self-gates on rig presence / config).
        _driverGo.AddComponent<WorldGrab>();
        _driverGo.AddComponent<SnapTurn>();
        _driverGo.AddComponent<Comfort>();
        _driverGo.AddComponent<ComfortVignette>();
        _driverGo.AddComponent<ComfortGizmos>();

        VRLog.Info(Name, vr
            ? "Rig driver + comfort stack installed — waiting for a scenario camera."
            : "Comfort stack installed in dev mode (world grab math runs on the dev rig proxy).");
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
        RigTarget.DestroyProxy();
        ComfortSettings.Unbind();
    }
}
