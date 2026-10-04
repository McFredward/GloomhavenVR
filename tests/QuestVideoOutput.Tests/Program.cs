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
        internal static void Capture(Camera camera, RenderTexture target, bool visible = true, bool ui = false)
        {
            var owner = new FlatScreen { _visible = visible, _rt = ui ? null : target, _uiRt = ui ? target : null, _splitRouting = ui };
            CapturedSet[camera] = new CapturedCamera { Owner = owner, IsUi = ui };
        }
        internal static void ClearFixture() => CapturedSet.Clear();
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
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "GloomhavenVR.FlatScreenRT" };
        target.Create(); camera.targetTexture = target;
        FlatScreen.Capture(camera, target);
        var texture = Decoded();
        using (var output = new QuestCameraVideoOutput(null!, "fixture"))
        {
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            camera.Render();
            Color bottomLeft = Pixel(target, 8, 8), topRight = Pixel(target, 56, 56);
            Close(bottomLeft.r, 1, "native decoded red pixels reach captured target");
            Close(bottomLeft.g, 1, "native decoder vertical orientation retained");
            Close(topRight.r, 0, "native decoder horizontal orientation retained");
            Close(topRight.g, 0, "native decoder vertical orientation retained");
            Close(bottomLeft.b, .25f, "native decoded color preserved");
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 1, "near plane uses post-render camera output");
            for (int n = 0; n < 100; n++) output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 1, "camera output does not grow each frame");
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.FitInside, 1, 1, 1); camera.Render();
            Close(Pixel(target, 32, 4).b, 0, "native fit-inside letterbox bars preserved");
            Close(Pixel(target, 8, 32).r, 1, "native fit-inside picture preserved");
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, .5f, 1, 1); camera.Render();
            Close(Pixel(target, 8, 8).r, .5f, "native camera alpha preserved");
            Close(Pixel(target, 8, 8).b, .625f, "native camera alpha composites existing background");
            var foreground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            foreground.transform.position = new Vector3(0, 0, 2);
            foreground.transform.localScale = new Vector3(1, 1, 1);
            var foregroundMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.magenta };
            foreground.GetComponent<Renderer>().sharedMaterial = foregroundMaterial;
            camera.cullingMask = -1;
            output.Apply(camera, texture, VideoRenderMode.CameraFarPlane, VideoAspectRatio.Stretch, 1, 1, 1); camera.Render();
            Color center = Pixel(target, 32, 32);
            Close(center.r, 1, "far plane retains native foreground depth");
            Close(center.b, 1, "far plane retains native foreground depth");
            Close(center.g, 0, "far plane retains native foreground depth");
            Close(Pixel(target, 4, 4).b, .25f, "far plane fills unoccluded capture background");
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1); camera.Render();
            Close(Pixel(target, 32, 32).b, .25f, "near plane overlays native foreground");
            var alternate = new RenderTexture(96, 48, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "GloomhavenVR.FlatScreenRT" };
            alternate.Create(); camera.targetTexture = alternate;
            alternate.name = "future-mod-capture-name";
            FlatScreen.Capture(camera, alternate, ui: true);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.FitInside, 1, 1, 1); camera.Render();
            Close(Pixel(alternate, 4, 4).b, .25f, "same native camera follows changed capture target");
            camera.targetTexture = target; UnityEngine.Object.DestroyImmediate(alternate);
            FlatScreen.Capture(camera, target);
            QuestStandalonePlatform.Enabled = false;
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "desktop movie output remains untouched");
            QuestStandalonePlatform.Enabled = true;
            var foreign = new RenderTexture(64, 64, 24) { name = "GloomhavenVR.FlatScreenRT" }; foreign.Create();
            camera.targetTexture = foreign;
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "foreign native outputs remain untouched");
            camera.targetTexture = target; UnityEngine.Object.DestroyImmediate(foreign);
            FlatScreen.Capture(camera, target, visible: false);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "hidden capture remains untouched");
            FlatScreen.Capture(camera, target);
            output.Apply(camera, texture, VideoRenderMode.CameraNearPlane, VideoAspectRatio.Stretch, 1, 1, 1);
            output.Dispose(); Check(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length == 0, "output disposal removes owned buffer");
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
