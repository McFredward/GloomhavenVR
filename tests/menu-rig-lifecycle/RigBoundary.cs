using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SpatialTracking;

// External game/XR services only. Production lifecycle, menu construction,
// rebind, camera creation, clip/clear maintenance, recenter and teardown bodies
// are extracted unchanged and compiled beside this boundary.
internal sealed class CameraController
{
    internal static CameraController s_CameraController = new CameraController();
    internal Camera m_Camera = null!;
    internal bool m_IsCameraCodeControlDisabled = false;
}
namespace UnityEngine.SpatialTracking
{
    // No HMD is attached to this Editor fixture. Preserve the real production
    // component calls and identity; pose samples are explicit physical inputs.
    internal sealed class TrackedPoseDriver : MonoBehaviour
    {
        internal enum DeviceType { GenericXRDevice }
        internal enum TrackedPose { Center }
        internal enum TrackingType { RotationAndPosition }
        internal enum UpdateType { UpdateAndBeforeRender }
        internal TrackingType trackingType;
        internal UpdateType updateType;
        internal void SetPoseSource(DeviceType device, TrackedPose pose) { }
    }
}
namespace GloomhavenVR.Core.Events
{
    internal static class VREvents
    {
        internal static event Action<int> SceneLoaded { add { } remove { } }
    }
}
namespace GloomhavenVR.Core
{
    internal static class Plugin
    {
        internal static Color VoidColor => Color.black;
        internal static bool ForwardRendering => true;
    }
    internal static class VRLayers { internal const int ModLayerMask = 1 << 30; }
    internal static class PerfConfig { internal static bool DepthPrepassOn => false; }
    internal static class LightStabiliser
    {
        internal static int Releases;
        internal static void Release(string reason) { Releases++; }
    }
    internal static class SkyAlternative
    {
        internal static float MinFarWorldUnits(float scale) => 0;
        internal static void RestoreAll() { }
    }
    // No private Default geometry is active in this rig-lifetime scope. The
    // actual shell budget/consumer/first-draw pixels run in default-sky-runtime.
    internal static class NativeDefaultSky { internal static float MinFarWorldUnits() => 0; }
    internal static class RenderQuality { internal static void RequestEyeTargetDiagnostics(string reason) { } }
    internal static class HeadRenderCounters { internal static void Stop() { } }
    internal static class NativeCameraRenderBudget
    {
        internal static Camera? MainOverride;
        internal static bool UseOverride;
        internal static readonly HashSet<Camera> Suspended = new HashSet<Camera>();
        internal static Camera? Main => UseOverride ? MainOverride : Camera.main;
        internal static bool Owns(Camera? camera) => camera != null && Suspended.Contains(camera);
        internal static int GetProjectionCamerasNonAlloc(out Camera[] cameras)
        {
            var all = new List<Camera>(Camera.allCameras);
            foreach (var camera in Suspended)
                if (camera != null && camera.gameObject.activeInHierarchy && !camera.enabled) all.Add(camera);
            cameras = all.ToArray(); return cameras.Length;
        }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static int StandDowns;
        internal static void StandDown(string reason) { StandDowns++; }
    }
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver : MonoBehaviour
    {
        private enum RigKind { None, Scenario, Menu, Map }
        private GameObject? _rigRoot, _cameraGo;
        private Camera? _camera, _anchor;
        private TrackedPoseDriver? _poseDriver;
        private RigKind _kind, _priorKind;
        private string? _pendingRebuildRequest;
        private string _rebuildTrigger = "fixture initial", _sceneRecheckName = "fixture scene";
        private float _buildScale = 1, _baseFarClip = 100;
        private Vector3 _menuAnchorPos;
        private Quaternion _menuAnchorYaw;
        private bool _pendingRecenter, _frozeGameCameraControl;
        private const float BaseNearMeters = .05f, MinNearClip = .01f, MaxNearClip = .5f, MaxFarNearRatio = 50000;
        internal static Transform? RigRoot;
        internal static Camera? HeadCamera;
        internal static VRRigDriver? Instance;
        internal static float BaseWorldScale;
        internal static int RigPoseVersion;
        internal static bool ScenarioArrivalPending;
        // Reset-only fields of the unchanged production teardown.
#pragma warning disable CS0414
        private bool _tiltActive, _prevHeadYawValid, _burstActive, _ringSettled, _ringPlaced;
        private Quaternion WorldTiltRotation;
        private string? _axisSnapReason;
        private string _lastChangeTrigger = "none";
        private float _lastTiltTarget, _tiltApplied, _tiltTweenFrom, _tiltAimYawDeg, _burstGrabDegrees;
#pragma warning restore CS0414
        internal Camera? Anchor => _anchor;
        internal Component? PoseDriver => _poseDriver;
        internal bool PendingRecenter { get => _pendingRecenter; set => _pendingRecenter = value; }
        internal int Kind => (int)_kind;
        internal int PriorKind => (int)_priorKind;
        internal void Step(int kind = 2, bool scene = false) => UpdateRigLifetime((RigKind)kind, CameraController.s_CameraController, scene);
        internal void Maintain() { TickHeadClearColor(); TickClipPlanes(); }
        internal void ManualRecenter() => RecenterMenu();
        internal void Shutdown() => OnDestroy();
        internal void Rebuild() { _pendingRebuildRequest = "explicit fixture rebuild"; Step(); }
        private void OnSceneLoaded(int unused) { }
        private static int ComposeHeadMask(int source) => source | VRLayers.ModLayerMask;
        // Scenario/map builders are boundaries: the fixture proves lifetime
        // routing and old-head revocation, not those independent seat algorithms.
        private void BuildRig(CameraController controller) => Context(RigKind.Scenario);
        private void BuildMapRig() => Context(RigKind.Map);
        private void Context(RigKind kind)
        {
            _kind = kind; _anchor = CameraController.s_CameraController.m_Camera;
            _rigRoot = new GameObject("fixture context root");
            CreateHeadCamera(_anchor, 1, true);
            RigRoot = _rigRoot.transform; HeadCamera = _camera; BaseWorldScale = 1; RigPoseVersion++;
        }
    }
}
