using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Hands { }
namespace GloomhavenVR.Hands.Interact { }
namespace TMPro { }
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const int GameUiLayerMask = 1 << 5; }
    internal static class PerfFrameSplit { }
    internal static class VRSession { internal static bool IsRunning; internal static HarmonyLib.Harmony? Harmony; }
    internal static class PerfConfig { internal static bool UnusedCamerasSuspended; }
    internal static class VRLog
    {
        internal static void Info(string category, string message) { }
        internal static void Warn(string category, string message) => Debug.LogWarning(message);
        internal static void Note(string category, string message) { }
    }
    internal static class VRCameraPolicy
    {
        private static readonly Camera[] Cameras = new Camera[32];
        internal static int GetAllCamerasNonAlloc(out Camera[] cameras)
        { cameras = Cameras; return Camera.GetAllCameras(Cameras); }
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal sealed class Setting { internal bool Value; }
        internal static readonly Setting DesktopMirrorLeftEye = new Setting();
    }
    internal sealed partial class FlatScreen
    {
        private bool _visible;
        private bool _mirrorLogged;
        private void CaptureStack() { }
        private static readonly HashSet<Camera> CapturedSet = new HashSet<Camera>();
        private static readonly HashSet<Camera> Captured = new HashSet<Camera>();
        internal static bool IsCaptured(Camera camera) => Captured.Contains(camera);
        internal void Tick(bool visible, bool mirror)
        {
            _visible = visible;
            WorldUIConfig.DesktopMirrorLeftEye.Value = mirror;
            TickDesktopMirrorMode();
            TickDesktopCameraScrub();
        }
        internal void End() { RestoreDesktopMirrorMode(); ReleaseDesktopScrub("fixture teardown"); }
        internal RenderTexture? Sink => _scrubRt;
        internal void Pre(Camera camera) => OnScrubPreCull(camera);
        internal void Post(Camera camera) => OnScrubPostRender(camera);
        internal void Handoff(Camera camera, RenderTexture capture)
        {
            Captured.Add(camera);
            TickDesktopCameraScrub();
            camera.targetTexture = capture;
        }
        internal void ReleaseForCapture() => ReleaseDesktopScrub("flat screen capture begins");
        internal void Capture(Camera camera, RenderTexture target)
        { Captured.Add(camera); camera.targetTexture = target; }
        internal void ForgetCapture(Camera camera) { Captured.Remove(camera); camera.targetTexture = null; }
    }
}
