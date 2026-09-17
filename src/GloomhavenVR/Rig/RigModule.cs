using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR camera rig: rig-OWNED head camera anchored at the game camera's vantage
/// (game cameras never render stereo — docs/CAMERA-POLICY.md §3), diorama/table
/// scale, recenter (Phase 1, feat/xr-bootstrap) + Demeo-style world grab, snap turn,
/// height/recenter comfort and the <see cref="ComfortSettings"/> API (feat/comfort). Those
/// entries are surfaced in VR by <see cref="WorldUI.VROptionsTab"/> — curated Komfort rows plus
/// the generic config browser it builds from <see cref="WorldUI.ConfigCatalog"/>.
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
            VRSession.Harmony?.PatchAll(typeof(ScenarioRetryStart_Patch));
            VRSession.Harmony?.PatchAll(typeof(ScenarioRetryDestination_Patch));
        }

        _driverGo = new GameObject("GloomhavenVR.RigDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        if (vr)
            _driverGo.AddComponent<VRRigDriver>();

        // Comfort stack (each component self-gates on rig presence / config).
        //
        // THIS ORDER IS NOT LOAD-BEARING — stated explicitly because it looks like it is.
        // AddComponent order is what decides Unity's Update order between components on the SAME
        // GameObject, so a reader is right to ask; the answer is that nothing here depends on it,
        // and nothing here may be "hardened" with a [DefaultExecutionOrder] attribute. Freezing an
        // order the code does not depend on is a behaviour change wearing a tidy-up costume, and it
        // invites a later edit to start depending on it. Why it does not matter:
        //   • Only WorldGrab, SnapTurn and Flight write the rig in Update, and in the common case
        //     they are mutually exclusive — SnapTurn and Flight both ignore a hand that is
        //     world-grabbing, and they read ORTHOGONAL stick axes (x turns, y flies).
        //   • When they do both run in a frame, neither integrates a private copy of the rig pose:
        //     each re-derives its correction from the rig's LIVE transform every frame (WorldGrab
        //     from the hand-vs-anchor delta, SnapTurn from the live HMD pivot, Flight from the live
        //     head/aim direction and the live rig scale), so a sibling's write in the same frame is
        //     absorbed, not lost. Flight adds a pure translation, which commutes with nothing it
        //     needs to: a turn in the same frame pivots around the head's CURRENT position, i.e.
        //     wherever flight has just put it.
        //   • VRRigDriver.TickWorldTilt then reconstructs the rig in LateUpdate, after every
        //     Update-phase writer, whatever order they ran in.
        //   • Comfort writes no transform at all (it latches the recenter chord and requests a
        //     recenter that VRRigDriver consumes), and ComfortGizmos is OnGUI-only.
        _driverGo.AddComponent<WorldGrab>();
        _driverGo.AddComponent<SnapTurn>();
        _driverGo.AddComponent<Flight>();
        _driverGo.AddComponent<Comfort>();
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
