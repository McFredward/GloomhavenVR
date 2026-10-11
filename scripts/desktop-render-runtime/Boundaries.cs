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
        internal static int FirstCaptureReports;
        internal static bool WantsDebug = true;
        internal static void Info(string category, string message)
        {
            if (message.StartsWith("Stereo policy: ")) StereoCorrections++;
            if (message.StartsWith("FlatScreen first-render discovery: ")) FirstCaptureReports++;
        }
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
        private RenderTexture? _rt, _uiRt;
        private bool _splitRouting;
        private bool _splitNoUi, _splitFailed = false;
        private string? _stereoGateReason;
        private Renderer? _backRenderer = null;
        private bool SplitActive => _uiRt != null;
        private static bool IsPreMenuScene() => false;
        // Explicit construction boundaries: tests execute real native capture and
        // clear policy, and separately observe its calls into the stereo owner.
        private int _fixtureCaptureScans;
        private void TickSplitLifecycle() => _fixtureCaptureScans++;
        private void SetSplitRouting(bool routing, string reason) => _splitRouting = routing;
        private void TickNoUiWatchdog() { }
        internal void BeginEmptyCapture(RenderTexture target, RenderTexture? glass = null)
        { _rt = target; _uiRt = glass; _splitRouting = glass != null; SetCaptureRenderGuard(true); }
        internal void Census() => CaptureStack();
        internal int MemberCount => _captured.Count;
        internal int CaptureScans => _fixtureCaptureScans;
        internal int StereoSyncCount => _stereo.SyncCount;
        internal void EnableStereoFixture() => _stereo.Active = true;
        internal void Tick(bool visible, bool mirror)
        {
            _visible = visible;
            WorldUIConfig.DesktopMirrorLeftEye.Value = mirror;
            TickDesktopMirrorMode();
            TickDesktopCameraScrub();
        }
        internal void End() { ReleaseStack(); GloomhavenVR.Core.VRCameraPolicy.RestoreAll(); RestoreDesktopMirrorMode(); ReleaseDesktopScrub("fixture teardown"); }
        internal RenderTexture? Sink => _scrubRt;
        internal void Pre(Camera camera) => OnScrubPreCull(camera);
        internal void Post(Camera camera) => OnScrubPostRender(camera);
        internal void Handoff(Camera camera, RenderTexture capture)
        {
            RegisterFixtureCapture(camera);
            TickDesktopCameraScrub();
            camera.targetTexture = capture;
        }
        internal void ReleaseForCapture() => ReleaseDesktopScrub("flat screen capture begins");
        internal void Capture(Camera camera, RenderTexture target)
        { RegisterFixtureCapture(camera); _rt = target; _uiRt = null; _splitRouting = false; camera.targetTexture = target; SetCaptureRenderGuard(true); }
        private void RegisterFixtureCapture(Camera camera)
        {
            if (CapturedSet.ContainsKey(camera)) return;
            var record = new CapturedCamera { Camera = camera, OriginalClearFlags = camera.clearFlags,
                OriginalBackground = camera.backgroundColor, WasEnabled = camera.isActiveAndEnabled, IsUi = false };
            _captured.Add(record); CapturedSet.Add(camera, record);
        }
        internal void ForgetCapture(Camera camera)
        { if (CapturedSet.TryGetValue(camera, out CapturedCamera record)) _captured.Remove(record); CapturedSet.Remove(camera); camera.targetTexture = null; }
        internal void InterruptCapture(Camera camera) => OnCapturePreCull(camera);
        internal void RestoreInterruptedCapture(Camera camera) => OnCapturePostRender(camera);
    }
    internal sealed class FlatScreenStereo
    {
        internal bool Active, Suspended = false;
        internal int SyncCount;
        internal void Tick(RenderTexture rt, Renderer? renderer, bool preMenu) { }
        internal void BeginStackSync() => SyncCount = 0;
        internal void SyncCamera(Camera camera) => SyncCount++;
        internal void EndStackSync() { }
        internal void Deactivate(string reason) => Active = false;
        internal void ReleaseMirrors() => SyncCount = 0;
    }
}
