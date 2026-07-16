using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Menu-blackscreen diagnostics: one camera-disposition inventory per scene load
/// while outside a scenario (Menu2D). Logs every active camera's render destination
/// so an HMD/desktop-black report can be triaged from the BepInEx log alone:
///
/// <code>
/// [WorldUI] Camera inventory after scene 'Gloomhaven_unified' (2 active):
/// [WorldUI]   'Camera' tag=MainCamera enabled=True depth=-1.0 clear=SolidColor
///             mask=0x00000301 stereo=Both target=backbuffer [VR head]
/// [WorldUI]   'UICamera' tag=UICamera enabled=True depth=10.0 clear=Depth
///             mask=0x00000020 stereo=None target=GloomhavenVR.FlatScreenRT
/// </code>
///
/// The log runs two frames after the scene-loaded event so the menu rig rebuild and
/// the FlatScreen retarget (both next-Update work) are reflected in the snapshot.
/// Armed on attach for the boot scene too (the plugin loads before any scene event).
/// </summary>
internal static class CameraInventory
{
    private static int _pendingFrames = -1;
    private static string _sceneName = "";
    private static bool _attached;

    internal static void Attach()
    {
        if (_attached)
            return;
        _attached = true;
        VREvents.SceneLoaded += OnSceneLoaded;
        Arm(SceneManager.GetActiveScene().name);
    }

    internal static void Detach()
    {
        if (!_attached)
            return;
        _attached = false;
        VREvents.SceneLoaded -= OnSceneLoaded;
        _pendingFrames = -1;
    }

    private static void OnSceneLoaded(SceneLoadedEvent e) => Arm(e.Scene.name);

    private static void Arm(string sceneName)
    {
        _sceneName = sceneName;
        _pendingFrames = 2;
    }

    /// <summary>Per-frame pump (WorldUI driver Update).</summary>
    internal static void Tick()
    {
        if (_pendingFrames < 0)
            return;
        if (_pendingFrames-- > 0)
            return;

        // Scenario scenes have their own (working) camera story — the inventory is a
        // menu/boot diagnostic. Requirement: once per scene load while in Menu2D.
        if (VRModeStateMachine.CurrentMode != VRMode.Menu2D)
            return;

        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] all);
        Camera? head = Rig.VRRigDriver.HeadCamera;
        int captured = 0;
        VRLog.Info("WorldUI", $"Camera inventory after scene '{_sceneName}' ({count} active):");
        for (int i = 0; i < count; i++)
        {
            Camera cam = all[i];
            if (cam == null)
                continue;
            string target = cam.targetTexture != null ? cam.targetTexture.name : "backbuffer";
            bool inStack = FlatScreen.IsCaptured(cam);
            if (inStack)
                captured++;
            Rect r = cam.rect;
            VRLog.Info("WorldUI",
                $"  '{cam.name}' tag={cam.tag} enabled={cam.enabled} depth={cam.depth:F1} " +
                $"clear={cam.clearFlags} rect=({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}) " +
                $"mask=0x{cam.cullingMask:X8} stereo={cam.stereoTargetEye} " +
                $"target={target}{(cam == head ? " [VR head]" : "")}{(inStack ? " [RT stack]" : "")}");
        }
        VRLog.Info("WorldUI", $"  FlatScreen stack: {captured} camera(s) captured into the RT " +
                              "(every backbuffer game camera belongs there while the screen shows).");

        // Applied policy summary (CAMERA-POLICY): which layer the mod owns and how
        // many foreign cameras are stereo-excluded right now.
        VRLog.Info("WorldUI", $"  Policy: mod layer={VRLayers.ModLayer} (mask 0x{VRLayers.ModLayerMask:X8}), " +
                              $"{VRCameraPolicy.Describe()}.");

        // The hardware-test-#3 failure signature: VR runs but no live rig head camera.
        if (VRSession.IsRunning && (head == null || !head.isActiveAndEnabled))
            VRLog.Warn("WorldUI", "No live [VR head] camera in the inventory while VR is running — " +
                                  "the rig driver should rebuild within a frame; if this repeats, it can't.");
    }
}
