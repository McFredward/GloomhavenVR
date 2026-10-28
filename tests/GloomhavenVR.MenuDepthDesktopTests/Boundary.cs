using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Inputs: already-visible FlatScreen resources/mode, native scene geometry and XR
// observations. Real source capture discovery/routing/clear policies, .7 guard,
// .8 menu coordinator, source registry/mapper/graphic/character draw run verbatim.
public static class DesktopEyeInput
{
    public static bool Available = true, Right;
    public static Camera.MonoOrStereoscopicEye Read(Camera camera) => Right
        ? Camera.MonoOrStereoscopicEye.Right : Camera.MonoOrStereoscopicEye.Left;
}
public sealed class ItemCardUI : MonoBehaviour { }
public sealed class FullAbilityCard : MonoBehaviour { }
public sealed class Character3DDisplayManager : MonoBehaviour
{
    private Transform character3DHolder = null!;
    private bool isHidden;
    private object? character3D;
    public void Bind(Transform holder) { character3DHolder = holder; isHidden = false; }
    public Transform Holder => character3DHolder;
    public bool Hidden => isHidden;
    public bool HasOriginal => character3D != null;
    public void BindOriginal(Component model) => character3D = new DesktopOriginal725(model);
}
// Native presentation identity shape: Character3D.models -> CharacterManager.
// No gameplay controller is run or supplied by this fixture boundary.
internal sealed class DesktopOriginal725
{
    private readonly List<DesktopModel725> models;
    internal DesktopOriginal725(Component model) => models = new List<DesktopModel725> { new DesktopModel725(model) };
}
internal sealed class DesktopModel725
{
    public Component CharacterManager { get; }
    internal DesktopModel725(Component model) => CharacterManager = model;
}
internal sealed class DesktopFaultSource : IMenuDepthSource
{
    public Graphic Graphic { get; }
    public bool IsReady => true;
    internal int Restores;
    internal DesktopFaultSource(Graphic graphic) => Graphic = graphic;
    public void BeforeCapture(MenuDepthEye eye)
    { if (eye.Right) throw new InvalidOperationException("injected right-eye native content failure"); }
    public void AfterCapture() => Restores++;
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static bool WantsDebug => true;
        internal static readonly List<string> Messages = new List<string>();
        internal static void Info(string scope, string text) => Messages.Add(text);
        internal static void Note(string scope, string text) => Messages.Add(text);
        internal static void Warn(string scope, string text) => Messages.Add(text);
    }
    internal static class VRLayers { internal const int ModLayerMask = 1 << 27, GameUiLayerMask = 1 << 5; }
    internal static class VRCameraPolicy
    {
        private static readonly Camera[] Cameras = new Camera[32];
        internal static int GetAllCamerasNonAlloc(out Camera[] cameras) { cameras = Cameras; return Camera.GetAllCameras(cameras); }
        internal static void ExcludeStereo(Camera camera, string reason) => camera.stereoTargetEye = StereoTargetEyeMask.None;
    }
    internal static class ScenarioCameraCullBoundary
    {
        internal static void Install() => throw new NotSupportedException("Explicit built-in Camera.Render fallback");
        internal static void Subscribe(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull installation");
        internal static void Unsubscribe(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull installation");
        internal static void SubscribeUnavailable(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull installation");
        internal static void UnsubscribeUnavailable(Camera.CameraCallback callback) => throw new Exception("Unexpected final-cull installation");
    }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal sealed class Setting { internal bool Value = true; }
        internal static readonly Setting CharacterPreviewDepth = new();
    }
    internal static class NativeMovieCameraPlane
    {
        internal static void RestoreAll() { }
        // The source calls this during every stack census. This fixture has no
        // VideoPlayer; it supplies no movie plane or movie capture ownership.
        internal static void RefreshQuestMovie() { }
        internal static void BeforeRender(Camera camera) => throw new Exception("Unexpected movie");
        internal static void AfterRender(Camera camera) { }
    }
    internal sealed class FlatScreenStereo
    {
        internal bool Active => false;
        internal bool Suspended => false;
        internal bool StereoMovieActive => false;
        internal bool NeedsMovieCaptureSync(Camera camera) => false;
        internal bool OwnsMoviePlane(Camera camera) => false;
        internal void Tick(RenderTexture? rt, Renderer? renderer, bool preMenu) { }
        internal void Deactivate(string reason) { }
        internal void BeginStackSync() => throw new Exception("Unexpected background stereo");
        internal void EndStackSync() => throw new Exception("Unexpected background stereo");
        internal void SyncCamera(Camera camera) => throw new Exception("Unexpected background stereo");
        internal void ReleaseMirrors() { }
    }
    internal sealed partial class FlatScreen
    {
        private RenderTexture? _rt, _uiRt;
        private GameObject? _quad;
        private Renderer? _backRenderer;
        private Material? _glassMaterial;
        private bool _visible, _splitRouting, _splitNoUi, _splitFailed;
        private string? _stereoGateReason;
        private bool SplitActive => _uiRt != null;
        private static bool IsPreMenuScene() => false;
        private void TickSplitLifecycle() { } // Existing split is supplied before capture ownership begins.
        private void TickNoUiWatchdog() { } // Both original UI cameras are live in this fixture.
        // Quest's bounded producer receipt observes its Android menu handoff.
        // It does not provide a UI draw, projection, raster, texture or readiness
        // decision to this ordinary desktop/head-pixel coordinator proof.
        private void ObserveQuestMenuProducer(Camera camera) { }
        private void ReleaseQuestMenuReceipt() { }
        internal FlatScreen(Transform screen, RenderTexture background, RenderTexture ui, Material material)
        { _quad = screen.gameObject; _rt = background; _uiRt = ui; _glassMaterial = material; _visible = _splitRouting = true; }
        internal void Begin() => CaptureStack();
        internal void Census() => CaptureStack();
        internal void End() => ReleaseStack();
        internal int Captured => _captured.Count;
        internal RenderTexture? Right => _menuDepthRight;
        internal RenderTexture Left => _menuDepthCarrierLeft ?? _uiRt!;
        internal Rect SampleFrame => _menuDepthFrameUv;
        internal void HideForFixture() { _visible = false; ReleaseStack(); }
    }
}
