using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

// These ports describe a pre-menu scene with a VR head but no scenario or options.
// Native decoder readiness/pixels and XR eye observation remain supplied inputs.
// Actual Show/Tick/capture/split/stereo lifecycle, native admission, shader and GPU
// draw are source-bound by the runner. Unsupported gameplay/map paths fail closed.
public static class StereoEyeFixture { public static Camera.MonoOrStereoscopicEye ActiveEye; }
public static class MovieDecoderFixture { public static Texture? Texture; public static bool Prepared; }
public static class MovieSourceFixture
{
    // The private text placeholder reads back an empty URL; cause is unknown.
    // Attempt the real setter, then transport supplied decoder metadata. Production parsing,
    // finite admission, selection and all camera/lifecycle policies remain real.
    private static readonly Dictionary<VideoPlayer, string> Urls = new Dictionary<VideoPlayer, string>();
    public static string GetUrl(VideoPlayer player) => Urls.TryGetValue(player, out string value) ? value : player.url;
    public static void SetUrl(VideoPlayer player, string value) { player.url = value; Urls[player] = value; }
}
public sealed class IntroPlayer { }
public sealed class UIManager { public static UIManager? Instance => null; }
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform
    {
        internal static bool Enabled => false;
        internal static bool SharedStereoEyeRouting => false;
        internal static string ResourceDirectory => string.Empty;
        internal static bool IsPreparationScene(string name) => false;
        internal static Shader? SelectFlatScreenShader(Shader? shader) => shader;
        internal static Shader? SelectFlatScreenGlassShader(Shader? shader) => shader;
        internal static void SetFlatScreenMono(Material? material, Texture? texture) { }
        internal static void SetFlatScreenEyes(Material material, Texture? left, Texture? right) { }
        internal static void ObserveFlatScreenVideoSample() { }
    }
    internal static class QuestMovieAssets
    {
        internal static Shader Shader(string name) => throw new InvalidOperationException("Android shaders have a separate GPU fixture.");
    }

    internal static class StereoModeConfig
    {
        internal enum Mode { MultiPass, SinglePassInstanced }
        internal const Mode Current = Mode.MultiPass;
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static bool WantsDebug => true;
        internal static readonly List<string> Messages = new List<string>();
        internal static void Note(string scope, string text) => Messages.Add(text);
        internal static void Info(string scope, string text) => Messages.Add(text);
        internal static void Warn(string scope, string text) => Messages.Add(text);
    }
    internal static class VRLayers
    {
        internal const int ModLayerMask = 1 << 27, GameUiLayerMask = 1 << 5;
        internal static void Apply(GameObject go) => go.layer = 27;
    }
    internal static class VRCameraPolicy
    {
        private static readonly Camera[] Cameras = new Camera[32];
        internal static int GetAllCamerasNonAlloc(out Camera[] cameras)
        { cameras = Cameras; return Camera.GetAllCameras(cameras); }
        internal static void ExcludeStereo(Camera camera, string reason) => camera.stereoTargetEye = StereoTargetEyeMask.None;
    }
    internal static class ScenarioCameraCullBoundary
    {
        // The shipped Harmony final-cull seam is covered by desktop-render-runtime.
        // This fixture exercises the real capture owner's documented built-in fallback.
        internal static void Install() => throw new NotSupportedException("Explicit built-in Camera.Render fallback boundary");
        internal static void Subscribe(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull subscription");
        internal static void Unsubscribe(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull subscription");
    }
    internal enum VRMode { Menu2D, ModalUI }
    internal static class VRModeStateMachine { internal static VRMode CurrentMode => VRMode.Menu2D; }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Hands.Interact { internal static class RayInteractor { internal static void RegisterUiTarget(Transform target) { } } }
namespace GloomhavenVR.Cards.Patches { internal static class HandSuppression { internal static bool Active => false; internal static bool BurnActive => false; } }
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver { internal static bool Active => false; } }
namespace GloomhavenVR.WorldUI
{
    internal static class PanelLayout { internal const float WorldScale = 1; }
    internal static class WorldUIConfig
    {
        internal static ConfigFile FileHandle = null!;
        internal static ConfigEntry<bool> ShowIntro = null!, Dialogs = null!;
        internal static ConfigEntry<float> ScreenDistance = null!, ScreenWidth = null!;
        internal const bool ScreenLayerSplit = true;
        internal static bool ConversionActive = true;
        internal static void Initialize(string path)
        {
            FileHandle = new ConfigFile(path, false);
            ShowIntro = FileHandle.Bind("WorldUI", "ShowIntro", true);
            Dialogs = FileHandle.Bind("WorldUI", "Dialogs", true);
            ScreenDistance = FileHandle.Bind("WorldUI", "ScreenDistance", 1.6f);
            ScreenWidth = FileHandle.Bind("WorldUI", "ScreenWidth", 2.2f);
        }
    }
    internal static class NativeVideoReview { internal static void CancelForNativePlayback() { } }
    internal static class NativeVideoWindow { internal static bool Visible => false; internal static void Tick() { } }
    internal static class LoadingIndicator { internal static bool FlatScreenSuppressed => false; }
    internal static class ModalFallback { internal static bool ScreenWanted => throw new Exception("Unexpected modal"); internal static bool WindowModalActive => throw new Exception("Unexpected modal"); }
    internal static class CardLossModalGuard { internal static bool OwnsAllLocks(UIManager? manager, bool active) => throw new Exception("Unexpected gameplay"); }
    internal sealed partial class FlatScreen
    {
        private static FlatScreen? _coverageOwner;
        private const string IntroSceneName = "Intro";
        private const float BackplaneGapMeters = .06f, NoUiFallbackSeconds = 3;
        private GameObject? _quad, _backQuad;
        private Renderer? _quadRenderer, _backRenderer;
        private Material? _screenMaterial, _glassMaterial;
        private RenderTexture? _rt, _uiRt;
        private bool _splitRouting, _splitFailed, _splitNoUi, _visible;
        private float _noUiSince = -1;
        private string? _stereoGateReason;
        private int _screenMaterialQueueDefault;
        private bool _rescueShow, _manualShow, _cardLossFallbackNoted;
        private static bool MapFallbackActive => false;
        internal static bool ScreenVisible;
        internal FlatScreen() { _coverageOwner = this; }
        private void TickDesktopMirrorMode() { }
        private void TickDesktopCameraScrub() { }
        private void ReleaseDesktopScrub(string reason) { }
        private void TickStartingIndicator(bool preMenu) { }
        private void TickManualChord() { }
        private void UpdateScreenTakeover() { }
        private void FollowHead() { }
        private void TickPointer() { }
        private void TickBackdropDepth() { if (_screenMaterial != null) _screenMaterial.renderQueue = 1000; }
        private bool IsConfirmationBoxOpen() => throw new Exception("Unexpected gameplay dialog");
        private void Hide() { ReleaseStack(); _stereo.Deactivate("startup fixture hide"); _visible = false; }
        private void PlaceScreen(bool instant)
        {
            if (_quad != null) { _quad.transform.position = new Vector3(0, 0, 3); _quad.transform.localScale = new Vector3(3.2f, 1.8f, 1); }
            if (_backQuad != null) { _backQuad.transform.position = new Vector3(0, 0, 3.06f); _backQuad.transform.localScale = new Vector3(3.264f, 1.836f, 1); }
        }
        internal void Census() => CaptureStack();
        internal bool Routed => _visible && _splitRouting && _stereo.StereoMovieActive;
        internal RenderTexture? Glass => _uiRt;
        internal Material? MovieMaterial => _screenMaterial;
        internal void LoseMovieSurface() => _stereo.LoseSurface();
        internal void End()
        {
            ReleaseStack(); _stereo.Deactivate("fixture teardown"); TeardownSplit("fixture teardown");
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); _rt = null; }
            if (_quad != null) Object.DestroyImmediate(_quad);
            if (_screenMaterial != null) Object.DestroyImmediate(_screenMaterial);
            _coverageOwner = null;
        }
    }
    internal sealed partial class FlatScreenStereo
    {
        private const float DefaultIpdMeters = .063f, MinConvergenceMeters = .25f, VideoOverscan = 1.03f, MaxVideoDisparityMeters = .055f;
        private const int VideoRecheckIntervalFrames = 15, VideoSweepIntervalFrames = 30;
        private static ConfigEntry<bool>? s_stereoScreen, s_videoDepthLayer;
        private static ConfigEntry<float>? s_depthStrength, s_parallaxScale, s_videoDepth;
        private bool _active, _hooked, _introGuard, _videoSuspended, _videoShift, _shiftRtFailed;
        private RenderTexture? _rtRight, _rtLeftShifted, _leftRt;
        private Material? _quadMaterial;
        private readonly Camera.CameraCallback _preCullHook, _preRenderHook, _postRenderHook;
        private float _ipdMeters = DefaultIpdMeters, _sepScene, _convScene, _videoShiftUv;
        private int _eyeObsCount, _shiftBlitFrame = -1, _videoRecheckFrame, _videoSweepFrame;
        private bool _videoRecheckDue, _mapBaseCapture, _mapMonoLogged, _probePending, _albedoProbePending;
        private int _probeGen, _blackConsecutive, _nonBlackMapConsecutive;
        private GameObject? _root;
        private sealed class MirrorEntry
        {
            internal Camera Source = null!, Mirror = null!;
            internal bool Synced, SourceOn, VideoActive;
        }
        private readonly List<MirrorEntry> _mirrors = new List<MirrorEntry>();
        internal bool Active => _active;
        internal bool Suspended => _videoSuspended;
        internal void LoseSurface() { if (_rtRight != null) _rtRight.Release(); }
        private void OnPreCullCamera(Camera camera) { }
        private void OnPostRenderCamera(Camera camera) { }
        private void SampleIpd() { } // XR input: explicit stable 63 mm, never affects baked views.
        private void TickMapRoomHandover() { }
        private void TickFastMapEngage(Camera? camera) { if (camera != null) throw new Exception("Unexpected map source"); }
        private void ReconcileAlbedoCamera(Camera? camera) { if (camera != null) throw new Exception("Unexpected map source"); }
        private void TickBlackProbe(bool rendering) { if (rendering) throw new Exception("Unexpected 3D mirror"); }
        private void TickAlbedoProbe() { }
        private void SweepForCameraPlaneVideos() => throw new Exception("Unexpected 3D mirror census");
        internal void SyncCamera(Camera camera) => throw new Exception("Only the original orthographic studio camera belongs to this startup fixture");
        private void DestroyMirrorAt(int index) => throw new Exception("Unexpected 3D mirror");
        private void ReleaseAlbedo() { }
        internal void ReleaseMirrors() { if (_mirrors.Count != 0) throw new Exception("Unexpected 3D mirror"); }
        private void ReleaseProbeRt() { }
        private void ReleaseAlbedoProbeRt() { }
        private void ReleaseMapRt() { }
    }
}
