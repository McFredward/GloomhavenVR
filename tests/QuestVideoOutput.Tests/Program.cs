using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Quest;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform
    {
        internal static bool Enabled = true;
        internal static bool IsFlatScreenVideoTarget(Camera camera) => Enabled && FlatScreen.OwnsVideoCapture(camera);
        internal static bool DebugLogging => false;
        internal static Camera? HeadCamera = null;
        internal static event Action? FlatScreenVideoSampling;
        internal static event Action? FlatScreenVideoSampled;
        internal static void ObserveFlatScreenVideoSample() { if (Enabled) FlatScreenVideoSampled?.Invoke(); }
        internal static void PrepareFlatScreenVideoSample()
        { if (Enabled) FlatScreenVideoSampling?.Invoke(); }
        internal static void PrepareFlatScreenVideoSample(Camera rendering)
        { if (rendering == HeadCamera) PrepareFlatScreenVideoSample(); }
        internal static Material? FlatScreenVideoConsumer(Camera camera) => Enabled ? FlatScreen.VideoConsumer(camera) : null;
        internal static Camera? FlatScreenVideoFinalCamera(Camera camera) => Enabled ? FlatScreen.FinalVideoCamera(camera) : null;
        internal static Texture? FlatScreenVideoGlassCapture(Camera camera) => Enabled ? FlatScreen.VideoGlassCapture(camera) : null;
    }
}
namespace GloomhavenVR.WorldUI
{
    // Producer/record seams only. The production ownership method and production
    // TargetFor routing execute unchanged with real Unity camera/RT identities.
    internal sealed partial class FlatScreen
    {
        internal sealed class CapturedCamera
        {
            internal FlatScreen Owner = null!;
            internal bool IsUi;
        }
        private static readonly System.Collections.Generic.Dictionary<Camera, CapturedCamera> CapturedSet = new();
        private bool _visible, _splitRouting;
        private RenderTexture? _rt, _uiRt;
        private Material? _screenMaterial, _glassMaterial;
        internal static void Capture(Camera camera, RenderTexture target, bool visible = true, bool ui = false)
        {
            var owner = new FlatScreen { _visible = visible, _rt = ui ? null : target, _uiRt = ui ? target : null, _splitRouting = ui };
            foreach (var existing in CapturedSet.Values)
                if (existing.Owner._rt == target && !ui && existing.Owner._visible == visible) { owner = existing.Owner; break; }
            CapturedSet[camera] = new CapturedCamera { Owner = owner, IsUi = ui };
        }
        internal static void ClearFixture() => CapturedSet.Clear();
        internal static void Consumer(Camera camera, Material material)
        { var owner = CapturedSet[camera].Owner; owner._screenMaterial = material; owner._glassMaterial = material; }
    }
}
public static class InteractionProgram
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Close(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .018f, message + " actual=" + actual + " expected=" + expected);
    static Color Pixel(RenderTexture target, int x, int y)
    {
        var previous = RenderTexture.active;
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = target; copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); copy.Apply(); return copy.GetPixel(x, y); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
    static Texture2D Decoded()
    {
        var texture = new Texture2D(16, 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[128];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 16; x++)
            pixels[y * 16 + x] = new Color(x < 8 ? 1 : 0, y < 4 ? 1 : 0, .25f, 1);
        texture.SetPixels(pixels); texture.Apply(); return texture;
    }
    public static int Run()
    {
        checks = 0;
        var go = new GameObject("native-captured-camera");
        var camera = go.AddComponent<Camera>(); camera.enabled = true; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.blue; camera.cullingMask = 0; camera.orthographic = true;
        camera.nearClipPlane = .1f; camera.farClipPlane = 20; camera.orthographicSize = 1;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "GloomhavenVR.FlatScreenRT", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
        target.Create(); camera.targetTexture = target;
        FlatScreen.Capture(camera, target);
        var texture = Decoded();
        using (var output = new QuestCameraVideoOutput(null!, "fixture"))
        {
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            camera.Render(); output.CompleteCapture();
            Color bottomLeft = Pixel(target, 8, 8), topRight = Pixel(target, 56, 56);
            Close(bottomLeft.r, 1, "native decoded red pixels reach captured target");
            Close(bottomLeft.g, 1, "native decoder vertical orientation retained");
            Close(topRight.r, 0, "native decoder horizontal orientation retained");
            Close(topRight.g, 0, "native decoder vertical orientation retained");
            Close(bottomLeft.b, .25f, "native decoded color preserved");
            var view = new GameObject("head-camera-consumer").AddComponent<Camera>();
            view.orthographic = true; view.orthographicSize = 1; view.cullingMask = 1 << 27;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.black;
            view.targetTexture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            view.targetTexture.Create();
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.layer = 27; screen.transform.position = new Vector3(0, 0, 2); screen.transform.localScale = new Vector3(2, 2, 1);
            var displayMaterial = new Material(Shader.Find("Hidden/BlitCopy")) { mainTexture = target };
            FlatScreen.Consumer(camera, displayMaterial);
            screen.GetComponent<Renderer>().sharedMaterial = displayMaterial;
            view.Render();
            Color displayed = Pixel(view.targetTexture, 8, 8);
            Debug.Log("Video fixture consumer shader=" + displayMaterial.shader.name + " pixel=" + displayed);
            Close(displayed.b, .25f, "native decoded pixels reach actual world-quad display shader");
            screen.transform.localScale = new Vector3(.5f, .5f, 1);
            view.Render();
            Close(Pixel(view.targetTexture, 29, 29).b, .25f, "completed movie pixels reach minified world-quad mip consumer");
            using (var finalOutput = new QuestCameraVideoOutput(null!, "late-native-Intro"))
            {
                finalOutput.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, .5f, 1, 1);
                var late = new GameObject("later-native-stack-camera").AddComponent<Camera>();
                late.targetTexture = target; late.clearFlags = CameraClearFlags.SolidColor;
                late.backgroundColor = Color.blue; late.cullingMask = 0; late.depth = camera.depth + 1;
                late.Render();
                Close(Pixel(target, 8, 8).b, 1, "fixture establishes a native write after original camera output");
                finalOutput.CompleteCapture();
                Close(Pixel(target, 8, 8).r, .5f, "completed near output follows native stack without changing alpha");
                Close(Pixel(target, 8, 8).b, .625f, "completed near output retains native destination alpha blend");
                finalOutput.CompleteCapture();
                Close(Pixel(target, 8, 8).r, .5f, "repeated eye consumers never blend movie alpha twice");
                view.Render();
                Close(Pixel(view.targetTexture, 29, 29).b, .625f, "completed output regenerates mips before minified head consumption");
                var shiftedLeft = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var shiftedRight = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                shiftedLeft.Create(); shiftedRight.Create();
                var ui = new GameObject("native-glass-camera").AddComponent<Camera>();
                var glass = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                glass.Create(); ui.targetTexture = glass; ui.cullingMask = 0;
                ui.clearFlags = CameraClearFlags.SolidColor; ui.backgroundColor = new Color(0, 1, 0, .5f); ui.Render();
                finalOutput.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, .5f, 1, 1);
                late.Render();
                QuestStandalonePlatform.FlatScreenVideoSampling += finalOutput.CompleteCapture;
                bool rightEye = false;
                Camera.CameraCallback consume = rendering =>
                {
                    if (rendering != view) return;
                    SharedStereoFixture.Sample(target, shiftedLeft, shiftedRight, .02f);
                    displayMaterial.mainTexture = rightEye ? shiftedRight : shiftedLeft;
                };
                Camera.onPreRender += consume;
                view.Render();
                Close(Pixel(shiftedLeft, 8, 8).b, .625f, "current completed video reaches left shifted eye");
                Close(Pixel(view.targetTexture, 29, 29).b, .625f, "actual head callback preserves its render context");
                rightEye = true; view.Render();
                Close(Pixel(shiftedRight, 56, 56).b, .625f, "current completed video reaches right shifted eye");
                Close(Pixel(shiftedLeft, 8, 8).r, .5f, "stereo consumers retain native alpha once");
                Close(Pixel(glass, 8, 8).g, 1, "completed movie never changes independent UI capture");
                Close(Pixel(glass, 8, 8).a, .5f, "native glass alpha remains independent from background movie");
                Camera.onPreRender -= consume;
                QuestStandalonePlatform.FlatScreenVideoSampling -= finalOutput.CompleteCapture;
                displayMaterial.mainTexture = target;
                UnityEngine.Object.DestroyImmediate(ui.gameObject); UnityEngine.Object.DestroyImmediate(glass);
                UnityEngine.Object.DestroyImmediate(shiftedLeft); UnityEngine.Object.DestroyImmediate(shiftedRight);
                UnityEngine.Object.DestroyImmediate(late.gameObject);
            }
            string? evidence = null;
            Application.LogCallback captureLog = (message, stack, kind) =>
            { if (message.Contains("stage=fixture-forced-sync")) evidence = message; };
            Application.logMessageReceived += captureLog;
            output.Probe(texture, "fixture-forced-sync", false);
            Application.logMessageReceived -= captureLog;
            Check(evidence != null && evidence.Contains("readbackMode=bounded-sync")
                && evidence.Contains("samples=128") && evidence.Contains("rgbMin=0")
                && evidence.Contains("rgbMax=255") && evidence.Contains("readbackElapsedMs="),
                "unsupported async GPU path still records real bounded pixels and timing");
            UnityEngine.Object.DestroyImmediate(displayMaterial); UnityEngine.Object.DestroyImmediate(screen);
            UnityEngine.Object.DestroyImmediate(view.targetTexture); UnityEngine.Object.DestroyImmediate(view.gameObject);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "camera event never duplicates completed movie draw");
            for (int n = 0; n < 100; n++) output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "camera output does not grow each frame");
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.FitInside, 1, 1, 1); camera.Render(); output.CompleteCapture();
            Close(Pixel(target, 32, 4).b, 0, "native fit-inside letterbox bars preserved");
            Close(Pixel(target, 8, 32).r, 1, "native fit-inside picture preserved");
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, .5f, 1, 1); camera.Render(); output.CompleteCapture();
            Close(Pixel(target, 8, 8).r, .5f, "native camera alpha preserved");
            Close(Pixel(target, 8, 8).b, .625f, "native camera alpha composites existing background");
            var foreground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            foreground.transform.position = new Vector3(0, 0, 2);
            foreground.transform.localScale = new Vector3(1, 1, 1);
            var foregroundMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.magenta };
            foreground.GetComponent<Renderer>().sharedMaterial = foregroundMaterial;
            camera.cullingMask = -1;
            output.Apply(camera, texture, VideoRenderMode.CameraFarPlane, VideoAspectRatio.Stretch, 1, 1, 1); camera.Render();
            Debug.Log("Video fixture foreground before final draw=" + Pixel(target, 32, 32));
            Close(Pixel(target, 32, 32).b, 1, "native foreground is present before output adaptation");
            output.CompleteCapture();
            Color center = Pixel(target, 32, 32);
            Close(center.r, 1, "far plane retains native foreground depth");
            Close(center.b, 1, "far plane retains native foreground depth");
            Close(center.g, 0, "far plane retains native foreground depth");
            Close(Pixel(target, 4, 4).b, .25f, "far plane fills unoccluded capture background");
            var finalCamera = new GameObject("later-native-foreground-camera").AddComponent<Camera>();
            finalCamera.orthographic = true; finalCamera.orthographicSize = 1; finalCamera.nearClipPlane = .1f;
            finalCamera.farClipPlane = 20; finalCamera.depth = camera.depth + 1; finalCamera.cullingMask = 1 << 28;
            finalCamera.targetTexture = target; finalCamera.clearFlags = CameraClearFlags.Depth;
            FlatScreen.Capture(finalCamera, target);
            var laterForeground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            laterForeground.layer = 28; laterForeground.transform.position = new Vector3(.65f, 0, 2);
            laterForeground.transform.localScale = new Vector3(.3f, .3f, 1);
            var laterMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1, 1, 0, 1) };
            laterForeground.GetComponent<Renderer>().sharedMaterial = laterMaterial;
            camera.cullingMask = 1;
            output.Apply(camera, texture, VideoRenderMode.CameraFarPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(QuestStandalonePlatform.FlatScreenVideoFinalCamera(camera) == finalCamera,
                "snapshot follows last actual camera sharing the current owned target");
            Graphics.Blit(Texture2D.grayTexture, target);
            output.CompleteCapture();
            Close(Pixel(target, 4, 4).b, .5f, "head before producer never samples an unrendered retained target");
            camera.Render(); finalCamera.Render();
            Graphics.Blit(Texture2D.grayTexture, target);
            output.CompleteCapture();
            Close(Pixel(target, 53, 32).g, 1, "completed snapshot retains later native foreground camera contribution");
            Close(Pixel(target, 53, 32).b, 0, "completed snapshot retains later native foreground camera color");
            Close(Pixel(target, 32, 32).b, 1, "completed snapshot retains earlier native foreground too");
            UnityEngine.Object.DestroyImmediate(finalCamera.gameObject); UnityEngine.Object.DestroyImmediate(laterForeground);
            UnityEngine.Object.DestroyImmediate(laterMaterial);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1); camera.Render(); output.CompleteCapture();
            Close(Pixel(target, 32, 32).b, .25f, "near plane overlays native foreground");
            var alternate = new RenderTexture(96, 48, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "GloomhavenVR.FlatScreenRT" };
            alternate.Create(); camera.targetTexture = alternate;
            alternate.name = "future-mod-capture-name";
            FlatScreen.Capture(camera, alternate, ui: true);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.FitInside, 1, 1, 1); camera.Render(); output.CompleteCapture();
            Close(Pixel(alternate, 4, 4).b, .25f, "same native camera follows changed capture target");
            camera.targetTexture = target; UnityEngine.Object.DestroyImmediate(alternate);
            FlatScreen.Capture(camera, target);
            QuestStandalonePlatform.Enabled = false;
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            camera.Render(); output.CompleteCapture();
            Close(Pixel(target, 4, 4).b, 1, "desktop movie output remains untouched");
            QuestStandalonePlatform.Enabled = true;
            var foreign = new RenderTexture(64, 64, 24) { name = "GloomhavenVR.FlatScreenRT" }; foreign.Create();
            camera.targetTexture = foreign;
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            camera.Render(); output.CompleteCapture();
            Close(Pixel(foreign, 4, 4).b, 1, "foreign native outputs remain untouched");
            camera.targetTexture = target; UnityEngine.Object.DestroyImmediate(foreign);
            FlatScreen.Capture(camera, target, visible: false);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            camera.Render(); output.CompleteCapture();
            Close(Pixel(target, 4, 4).b, 1, "hidden capture remains untouched");
            FlatScreen.Capture(camera, target);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            output.Dispose(); camera.Render();
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            output.CompleteCapture();
            Close(Pixel(target, 4, 4).b, 1, "disposed output never writes another capture");
            UnityEngine.Object.DestroyImmediate(foreground); UnityEngine.Object.DestroyImmediate(foregroundMaterial);
        }
        foreach (VideoAspectRatio aspect in Enum.GetValues(typeof(VideoAspectRatio)))
        {
            var mapping = QuestCameraVideoOutput.UvTransform(aspect, 200, 100, 400, 400, 1, 1);
            Check(!float.IsNaN(mapping.x) && mapping.x > 0 && mapping.y > 0, "every native aspect mode supported");
            Close(mapping.z, (1 - mapping.x) * .5f, "native aspect remains horizontally centred");
            Close(mapping.w, (1 - mapping.y) * .5f, "native aspect remains vertically centred");
        }
        var anamorphic = QuestCameraVideoOutput.UvTransform(VideoAspectRatio.FitHorizontally, 200, 100, 400, 400, 2, 1);
        Close(anamorphic.y, 4, "native nonsquare video pixels preserved");
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(target);
        FlatScreen.ClearFixture();
        return checks;
    }
}
