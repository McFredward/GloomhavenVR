using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Hands { }
namespace GloomhavenVR.Hands.Interact { }
namespace TMPro { }
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const int GameUiLayerMask = 1 << 5; internal const int ModLayerMask = 1 << 27; }
    internal static class PerfFrameSplit { }
    internal static class VRSession { internal static bool IsRunning; internal static HarmonyLib.Harmony? Harmony; }
    internal static class PerfConfig { internal static bool UnusedCamerasSuspended; }
    internal static class VRLog
    {
        internal static int StereoCorrections;
        internal static void Info(string category, string message) { if (message.StartsWith("Stereo policy: ")) StereoCorrections++; }
        internal static void Warn(string category, string message) => Debug.LogWarning(message);
        internal static void Note(string category, string message) { }
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
        private sealed class CapturedCamera { internal bool IsUi; }
        private static readonly Dictionary<Camera, CapturedCamera> CapturedSet = new Dictionary<Camera, CapturedCamera>();
        private static readonly HashSet<Camera> Captured = new HashSet<Camera>();
        private RenderTexture? _rt, _uiRt;
        private bool _splitRouting;
        private RenderTexture? TargetFor(CapturedCamera c) => c.IsUi && _splitRouting && _uiRt != null ? _uiRt : _rt;
        internal static bool IsCaptured(Camera camera) => Captured.Contains(camera);
        internal void Tick(bool visible, bool mirror)
        {
            _visible = visible;
            WorldUIConfig.DesktopMirrorLeftEye.Value = mirror;
            TickDesktopMirrorMode();
            TickDesktopCameraScrub();
        }
        internal void End() { SetCaptureRenderGuard(false); CapturedSet.Clear(); Captured.Clear(); GloomhavenVR.Core.VRCameraPolicy.RestoreAll(); RestoreDesktopMirrorMode(); ReleaseDesktopScrub("fixture teardown"); }
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
        { Captured.Add(camera); CapturedSet[camera] = new CapturedCamera { IsUi = false }; _rt = target; _uiRt = null; _splitRouting = false; camera.targetTexture = target; SetCaptureRenderGuard(true); }
        internal void ForgetCapture(Camera camera) { Captured.Remove(camera); CapturedSet.Remove(camera); camera.targetTexture = null; }
        internal void InterruptCapture(Camera camera) => OnCapturePreCull(camera);
        internal void RestoreInterruptedCapture(Camera camera) => OnCapturePostRender(camera);
    }
}
