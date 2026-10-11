using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Build612: the Frame610 trace still brackets two discarded native camera passes in addition
/// to the two eye passes. A zero culling mask avoids scenery but still enters culling, clears,
/// command buffers and image effects. The optional stronger policy disables those exact sink
/// cameras. Native projection objects remain alive; only their automatic render is stopped.
///
/// Actual Unity2021.3.5 proves that disabling a MainCamera makes Camera.main null, even though
/// ScreenPointToRay/WorldToScreenPoint on that same Camera remain valid. Before suspending any
/// camera, replace the shipped game's managed main-camera reads with an identity-preserving
/// resolver. Do not patch Unity's native getter, retag the head, or change gameplay controllers.
/// Visible flat-screen captures and independently consumed preview RTs are never discarded.
/// </summary>
internal static class NativeCameraRenderBudget
{
    private static readonly HashSet<Camera> Suspended = new();
    private static readonly List<Camera> Dead = new(4);
    private static Camera? _main;
    private static Camera[] _projectionBuffer = new Camera[16];
    private static bool _bridgeAttempted;
    private static bool _bridgeReady;
    private static bool _faultNoted;

    internal static Camera? Main
    {
        get
        {
            if (VRSession.IsRunning)
            {
                if (IsNativeMain(_main) && Suspended.Contains(_main!)) return _main;
                // A native scene may retag another already-suspended camera between polls.
                foreach (Camera camera in Suspended)
                    if (IsNativeMain(camera)) { _main = camera; return camera; }
            }
            return Camera.main;
        }
    }
    private static bool IsNativeMain(Camera? camera) => camera != null
        && camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera");

    internal static int GetProjectionCamerasNonAlloc(out Camera[] cameras)
    {
        int active = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] rendering);
        int capacity = active + (VRSession.IsRunning ? Suspended.Count : 0);
        if (_projectionBuffer.Length < capacity) _projectionBuffer = new Camera[Mathf.NextPowerOfTwo(capacity)];
        Array.Copy(rendering, _projectionBuffer, active);
        int count = active;
        if (VRSession.IsRunning)
            foreach (Camera camera in Suspended)
                if (camera != null && camera.gameObject.activeInHierarchy && !camera.enabled)
                    _projectionBuffer[count++] = camera;
        cameras = _projectionBuffer;
        return count;
    }
    private static Camera[] ProjectionCameras
    {
        get
        {
            if (!VRSession.IsRunning || Suspended.Count == 0) return Camera.allCameras;
            int count = GetProjectionCamerasNonAlloc(out Camera[] originals);
            var result = new Camera[count]; Array.Copy(originals, result, count); return result;
        }
    }

    internal static bool Owns(Camera? camera) => camera != null && Suspended.Contains(camera);

    internal static void Apply(IReadOnlyList<Camera> discarded, bool requested)
    {
        if (!requested || !VRSession.IsRunning || Rig.VRRigDriver.HeadCamera == null)
        {
            Restore();
            return;
        }
        if (!EnsureBridge()) return; // A missing managed read must never create an input deadlock.
        if (Camera.main != null) _main = Camera.main;
        Dead.Clear();
        foreach (Camera camera in Suspended)
            if (camera == null || !camera.gameObject.activeInHierarchy || WorldUI.NativeVideoWindow.OwnsRenderCamera(camera) || !Contains(discarded, camera)) Dead.Add(camera!);
        foreach (Camera camera in Dead)
        {
            if (camera != null && !camera.enabled) camera.enabled = true;
            Suspended.Remove(camera!);
        }
        int added = 0;
        for (int i = 0; i < discarded.Count; i++)
        {
            Camera camera = discarded[i];
            if (camera == null || camera == Rig.VRRigDriver.HeadCamera || WorldUI.NativeVideoWindow.OwnsRenderCamera(camera) || !camera.gameObject.activeInHierarchy)
                continue;
            if (!Suspended.Contains(camera))
            {
                if (!camera.enabled) continue; // A native disabled camera is never ours to enable.
                Suspended.Add(camera);
                added++;
            }
            // Native scene writers may enable the same discarded camera again.
            if (camera.enabled) camera.enabled = false;
        }
        if (added != 0)
            VRLog.Note("WorldUI", "NATIVE CAMERA SUSPEND: " + added + " discarded camera(s) disabled; "
                + Suspended.Count + " owned, native projection retained, headset/captured previews unchanged.");
    }

    private static bool Contains(IReadOnlyList<Camera> cameras, Camera camera)
    {
        for (int i = 0; i < cameras.Count; i++) if (cameras[i] == camera) return true;
        return false;
    }

    internal static void Restore()
    {
        int restored = 0;
        foreach (Camera camera in Suspended)
            if (camera != null && !camera.enabled) { camera.enabled = true; restored++; }
        Suspended.Clear(); Dead.Clear(); _main = null;
        if (restored != 0)
            VRLog.Note("WorldUI", "NATIVE CAMERA SUSPEND: restored " + restored + " camera(s) for capture, setting change or VR teardown.");
    }

    internal static void Shutdown()
    {
        Restore();
        _bridgeAttempted = _bridgeReady = _faultNoted = false;
    }

    internal static void Prepare() { if (PerfConfig.UnusedCamerasSuspended) EnsureBridge(); }

    private static bool EnsureBridge()
    {
        if (_bridgeAttempted) return _bridgeReady;
        if (VRSession.Harmony == null) return false;
        _bridgeAttempted = true;
        try
        {
            // Optional startup preparation may precede the first native scene using FirstPass
            // or ThirdParty. Load the already-installed managed dependencies before lookup.
            foreach (string assembly in new[] { "GH.Runtime", "GH.Runtime.FirstPass", "ThirdParty" })
                Assembly.Load(assembly);
            var getter = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.main));
            var replacement = AccessTools.PropertyGetter(typeof(NativeCameraRenderBudget), nameof(Main));
            if (getter == null || replacement == null) throw new MissingMethodException("camera projection getter");
            // These are the shipped GH.Runtime/FirstPass/ThirdParty types with managed
            // Camera.main reads, including native UI, fog/water and preview helpers.
            // Scan only these methods, never FFSNet/ScenarioRuleLibrary/Unity externals.
            string[] types = { "MF", "CameraController", "HoverRegisterer", "OffsetTowardsCamera",
                "Assets.Script.AdventureMap.MapLocationSelector", "UnityEngine.UI.UITooltip",
                "SpawnAtCurrentCameraPos_SMB", "RFX4_RealtimeReflection", "RFX4_DistortionAndBloom",
                "BFX_DemoTest", "LevelEditorController",
                "UnityStandardAssets.Water.WaterBase", "AraSamples.ObjectDragger",
                "UnityStandardAssets.Utility.DynamicShadowSettings", "UnityStandardAssets.Utility.DragRigidbody",
                "AsmodeeNet.Foundation.Preferences", "RenderHeads.Media.AVProMovieCapture.Utils",
                "RenderHeads.Media.AVProMovieCapture.CaptureFromCamera", "TileAnimation", "GraphProgress.Toucher",
                "VolumetricFogAndMist.VolumetricFog", "DynamicFogAndMist.DynamicFogManager",
                "ThreeEyedGames.DecaliciousExample.PlayerController", "ThreeEyedGames.DecaliciousExample.ShootDecal",
                "ThreeEyedGames.DecaliciousExample.PickupWeapon", "AutomaticLOD", "BeautifyEffect.FreeCameraMove",
                "AsmodeeNet.UserInterface.Focusable" };
            int patched = 0;
            foreach (string name in types)
            {
                Type type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    bool reads = false;
                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                        if (instruction.Calls(getter)) { reads = true; break; }
                    if (!reads) continue;
                    VRSession.Harmony.Patch(method, transpiler: new HarmonyMethod(
                        AccessTools.Method(typeof(NativeCameraRenderBudget), nameof(ReplaceMain))));
                    patched++;
                }
            }
            if (patched == 0) throw new MissingMethodException("native main-camera readers");
            // These two original consumers enumerate cameras for input/projection binding,
            // not rendering. Keep suspended originals visible to that same native discovery.
            var allGetter = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.allCameras));
            int enumerators = 0;
            foreach (string name in new[] { "CanvasManager", "InputManager" })
            {
                Type type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    bool reads = false;
                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                        if (instruction.Calls(allGetter)) { reads = true; break; }
                    if (reads)
                    {
                        VRSession.Harmony.Patch(method, transpiler: new HarmonyMethod(
                            AccessTools.Method(typeof(NativeCameraRenderBudget), nameof(ReplaceProjectionEnumeration))));
                        enumerators++;
                    }
                }
            }
            if (enumerators < 2) throw new MissingMethodException("native projection camera enumeration");
            _bridgeReady = true;
            VRLog.Note("WorldUI", "NATIVE CAMERA PROJECTION: " + patched
                + " original managed readers retain native Camera identity while discarded draws are suspended.");
        }
        catch (Exception error)
        {
            if (!_faultNoted)
            {
                _faultNoted = true;
                VRLog.Warn("WorldUI", "NATIVE CAMERA SUSPEND unavailable (" + error.GetType().Name + ": "
                    + error.Message + "); existing discarded-draw suppression remains active.");
            }
        }
        return _bridgeReady;
    }

    private static IEnumerable<CodeInstruction> ReplaceProjectionEnumeration(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.allCameras));
        MethodInfo replacement = AccessTools.PropertyGetter(typeof(NativeCameraRenderBudget), nameof(ProjectionCameras));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceMain(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.main));
        MethodInfo replacement = AccessTools.PropertyGetter(typeof(NativeCameraRenderBudget), nameof(Main));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
            yield return instruction;
        }
    }
}
