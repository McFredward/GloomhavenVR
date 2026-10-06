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
    internal static partial class QuestStandalonePlatform
    {
        internal static bool Enabled = true;
        internal static bool SharedStereoEyeRouting => Enabled;
        // Production material selection/binding methods are compiled by the runner.
        internal static bool IsFlatScreenVideoTarget(Camera camera) => Enabled && FlatScreen.OwnsVideoCapture(camera);
        internal static bool DebugLogging => false;
    }
    internal static class VRLog { internal static void Info(string area, string message) => Debug.Log(message); }
    internal static partial class QuestStandalonePlatform
    {
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
    static void GlassCapturePixels()
    {
        var ui = new GameObject("native-UI-camera").AddComponent<Camera>();
        ui.orthographic = true; ui.orthographicSize = 32; ui.nearClipPlane = .1f; ui.farClipPlane = 200;
        ui.cullingMask = 1 << 5; ui.clearFlags = CameraClearFlags.SolidColor; ui.backgroundColor = Color.clear;
        ui.targetTexture = new RenderTexture(128, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Check(ui.targetTexture.Create(), "native Canvas producer capture created");
        var canvas = new GameObject("original-menu-Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.gameObject.layer = 5; canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = ui; canvas.planeDistance = 2;
        var widget = new GameObject("native-widget", typeof(RectTransform), typeof(CanvasRenderer)); widget.layer = 5;
        widget.transform.SetParent(canvas.transform, false);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-32,-16,0),new Vector3(32,-16,0),new Vector3(32,16,0),new Vector3(-32,16,0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = Enumerable.Repeat(Color.red, 4).ToArray(); mesh.triangles = new[] {0,1,2,0,2,3};
        var producer = widget.GetComponent<CanvasRenderer>(); producer.SetMesh(mesh); producer.SetColor(Color.white);
        var uiMaterial = new Material(Shader.Find("UI/Default"));
        producer.materialCount = 1; producer.SetMaterial(uiMaterial, 0); producer.SetTexture(Texture2D.whiteTexture);
        var head = new GameObject("owned-head-camera").AddComponent<Camera>();
        head.orthographic = true; head.orthographicSize = 1; head.nearClipPlane = .1f; head.farClipPlane = 20;
        head.cullingMask = 1 << 27; head.clearFlags = CameraClearFlags.SolidColor; head.backgroundColor = Color.blue;
        head.targetTexture = new RenderTexture(128,64,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); head.targetTexture.Create();
        var glass = GameObject.CreatePrimitive(PrimitiveType.Quad); glass.layer = 27;
        glass.transform.position = new Vector3(0,0,2); glass.transform.localScale = new Vector3(4,2,1);
        Shader desktop = Shader.Find("Sprites/Default");
        QuestStandalonePlatform.Enabled = false;
        Check(QuestStandalonePlatform.SelectFlatScreenGlassShader(desktop) == desktop, "desktop glass shader choice remains unchanged");
        QuestStandalonePlatform.Enabled = true;
        var material = new Material(QuestStandalonePlatform.SelectFlatScreenGlassShader(desktop)) { mainTexture = ui.targetTexture };
        glass.GetComponent<Renderer>().sharedMaterial = material;
        try
        {
            Canvas.ForceUpdateCanvases(); ui.Render(); head.Render();
            Debug.Log("Glass fixture actual API=" + SystemInfo.graphicsDeviceType + " producer=" + Pixel(ui.targetTexture,64,32)
                + " head=" + Pixel(head.targetTexture,64,32));
            Close(Pixel(ui.targetTexture,64,32).r, 1, "native Canvas producer emits current UI pixels");
            Close(Pixel(head.targetTexture,64,32).r, 1, "opaque native UI capture reaches owned head camera");
            Close(Pixel(head.targetTexture,4,4).b, 1, "untouched glass pixels retain native background transparency");
            // Real UI/Default blending premultiplies the stored RGB. Its alpha
            // uses the authored UI blend factors and remains untouched here.
            mesh.colors = Enumerable.Repeat(new Color(1,0,0,.5f),4).ToArray(); producer.SetMesh(mesh);
            Canvas.ForceUpdateCanvases(); ui.Render(); head.Render();
            Color captured = Pixel(ui.targetTexture,64,32), sampled = Pixel(head.targetTexture,64,32);
            Close(captured.r, .5f, "native producer already alpha-composes widget RGB");
            Close(sampled.r, captured.r, "glass consumes native captured RGB without multiplying alpha twice");
            Close(sampled.b, 1-captured.a, "glass retains captured alpha for background composition");
            var rgbMaterial = new Material(Shader.Find("Hidden/GloomhavenVR/FixtureUiRgb"));
            producer.SetMaterial(rgbMaterial, 0); Canvas.ForceUpdateCanvases(); ui.Render(); head.Render();
            Color rgbOnly = Pixel(ui.targetTexture,64,32);
            Close(rgbOnly.a, 0, "original RGB-only UI pass leaves capture alpha untouched");
            Close(Pixel(head.targetTexture,64,32).r, rgbOnly.r, "RGB-only native UI content is never erased by capture alpha");
            var old = new Material(desktop) { mainTexture = ui.targetTexture };
            glass.GetComponent<Renderer>().sharedMaterial = old; head.Render();
            Close(Pixel(head.targetTexture,64,32).r, 0, "historical sprite consumer reproduces blank RGB-only UI");
            UnityEngine.Object.DestroyImmediate(old);
            UnityEngine.Object.DestroyImmediate(rgbMaterial);
            glass.GetComponent<Renderer>().sharedMaterial = material;
            var hand = GameObject.CreatePrimitive(PrimitiveType.Quad); hand.layer = 27;
            hand.transform.position = new Vector3(0,0,1); hand.transform.localScale = new Vector3(.25f,.25f,1);
            var handMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
            hand.GetComponent<Renderer>().sharedMaterial = handMaterial; head.Render();
            Close(Pixel(head.targetTexture,64,32).g, 1, "Quest glass retains native closer-hand depth occlusion");
            Close(Pixel(head.targetTexture,64,32).r, 0, "Quest glass never paints UI over closer native hand geometry");
            UnityEngine.Object.DestroyImmediate(hand); UnityEngine.Object.DestroyImmediate(handMaterial);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(uiMaterial);
            UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(glass);
            UnityEngine.Object.DestroyImmediate(canvas.gameObject); UnityEngine.Object.DestroyImmediate(ui.targetTexture);
            UnityEngine.Object.DestroyImmediate(head.targetTexture); UnityEngine.Object.DestroyImmediate(head.gameObject);
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
        }
    }
    static void ScreenStereoPixels()
    {
        var camera = new GameObject("real-world-screen-camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1;
        camera.nearClipPlane = .1f; camera.farClipPlane = 10;
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.transform.position = new Vector3(0, 0, 2);
        screen.transform.localScale = new Vector3(2, 2, 1);
        var left = Decoded();
        var rightPixels = new Texture2D(16, 8, TextureFormat.RGBA32, false, true);
        rightPixels.SetPixels(Enumerable.Repeat(new Color(.125f, .5f, .875f, 0), 128).ToArray()); rightPixels.Apply();
        var leftCapture = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); leftCapture.Create(); Graphics.Blit(left, leftCapture);
        var right = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); right.Create(); Graphics.Blit(rightPixels, right);
        Shader originalShader = Shader.Find("Hidden/BlitCopy");
        QuestStandalonePlatform.Enabled = false;
        Check(QuestStandalonePlatform.SelectFlatScreenShader(originalShader) == originalShader,
            "desktop world screen shader remains unchanged");
        QuestStandalonePlatform.Enabled = true;
        var material = new Material(QuestStandalonePlatform.SelectFlatScreenShader(originalShader)) { enableInstancing = true };
        SharedEyeBindingFixture.Bind(material, false, leftCapture, right, suspended: false, shifted: true);
        Check(material.mainTexture == leftCapture && material.GetTexture("_RightTex") == right,
            "actual shared shifted-eye policy binds two current capture identities");
        var eyes = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { dimension = TextureDimension.Tex2DArray, volumeDepth = 2, antiAliasing = 1 };
        Check(eyes.Create(), "real array eye target created");
        var planar = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); planar.Create();
        // An actual XR provider supplies this built-in constant buffer. The
        // fixture sets its real layout explicitly; individual global matrices
        // do not populate UnityStereoGlobals on a non-XR editor camera.
        var stereoData = new float[272];
        Matrix4x4 stereoProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        Matrix4x4 stereoView = camera.worldToCameraMatrix;
        Matrix4x4[] matrices = { stereoProjection, stereoView, stereoView.inverse,
            stereoProjection * stereoView, stereoProjection, stereoProjection.inverse, stereoView, stereoView.inverse };
        for (int kind = 0; kind < matrices.Length; kind++) for (int eye = 0; eye < 2; eye++)
            for (int column = 0; column < 4; column++) for (int row = 0; row < 4; row++)
                stereoData[(kind * 2 + eye) * 16 + column * 4 + row] = matrices[kind][row, column];
        stereoData[264] = stereoData[265] = stereoData[268] = stereoData[269] = 1;
        using var stereoGlobals = new ComputeBuffer(272, 4, ComputeBufferType.Constant);
        stereoGlobals.SetData(stereoData);
        void RenderEyes()
        {
            using var draw = new CommandBuffer();
            draw.SetRenderTarget(new RenderTargetIdentifier(eyes, 0, CubemapFace.Unknown, -1));
            draw.ClearRenderTarget(true, true, Color.gray);
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 vp = projection * view;
            draw.SetViewProjectionMatrices(view, projection);
            draw.SetGlobalMatrixArray("unity_StereoMatrixVP", new[] { vp, vp });
            draw.SetGlobalMatrixArray("unity_StereoMatrixP", new[] { projection, projection });
            draw.SetGlobalMatrixArray("unity_StereoMatrixV", new[] { view, view });
            draw.SetGlobalMatrixArray("unity_StereoWorldToCamera", new[] { view, view });
            draw.SetGlobalConstantBuffer(stereoGlobals, Shader.PropertyToID("UnityStereoGlobals"), 0, 1088);
            draw.EnableShaderKeyword("STEREO_INSTANCING_ON");
            draw.SetSinglePassStereo(SinglePassStereoMode.Instancing);
            Mesh mesh = screen.GetComponent<MeshFilter>().sharedMesh;
            draw.DrawMeshInstanced(mesh, 0, material, 0,
                new[] { screen.transform.localToWorldMatrix, screen.transform.localToWorldMatrix }, 2);
            draw.SetSinglePassStereo(SinglePassStereoMode.None);
            draw.DisableShaderKeyword("STEREO_INSTANCING_ON");
            Graphics.ExecuteCommandBuffer(draw);
        }
        RenderEyes();
        Graphics.CopyTexture(eyes, 0, 0, planar, 0, 0);
        Color leftPixel = Pixel(planar, 8, 8);
        Debug.Log("World screen fixture instanced left pixel=" + leftPixel + " shader=" + material.shader.name);
        Close(leftPixel.b, .25f, "actual instanced left eye renders owned Tex2D screen pixels");
        Close(leftPixel.r, 1, "world screen geometry projection and orientation retained");
        Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Color rightPixel = Pixel(planar, 8, 8);
        Debug.Log("World screen fixture instanced right pixel=" + rightPixel);
        Close(rightPixel.b, .875f, "actual instanced right eye samples its distinct owned capture");
        Close(rightPixel.g, .5f, "actual right eye keeps intended native disparity source");
        Close(rightPixel.a, 1, "opaque background ignores captured zero alpha");
        SharedEyeBindingFixture.Bind(material, true, leftCapture, right, suspended: true, shifted: true);
        RenderEyes(); Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, .25f, "same persistent screen clears stale right capture on Intro suspension");
        Check(material.mainTexture == leftCapture && material.GetTexture("_RightTex") == leftCapture,
            "actual shared Intro suspended policy binds the same texture for both eyes");
        Check(material.GetFloat("_StereoCapture") == 0, "suspended screen resets stereo routing state");
        SharedEyeBindingFixture.Bind(material, false, leftCapture, right, suspended: false, shifted: false);
        RenderEyes(); Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, .875f, "same persistent material resumes native right capture");
        SharedEyeBindingFixture.Bind(material, false, leftCapture, right, suspended: false, shifted: false, map: true);
        RenderEyes(); Graphics.CopyTexture(eyes, 0, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, .875f, "same material map handover shows current revealed map capture to left eye");
        Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, .875f, "same material map handover clears prior stereo capture for right eye");
        SharedEyeBindingFixture.Bind(material, false, leftCapture, right, suspended: false, shifted: false, map: true, revealed: false);
        RenderEyes(); Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, 0, "same material unrevealed map retains native black reveal guard for both eyes");
        SharedEyeBindingFixture.Bind(material, false, leftCapture, right, suspended: false, shifted: false);
        SharedEyeBindingFixture.Deactivate(material, leftCapture);
        RenderEyes(); Graphics.CopyTexture(eyes, 1, 0, planar, 0, 0);
        Close(Pixel(planar, 8, 8).b, .25f, "shared stereo deactivation clears stale right-eye pixels on retained material");
        Check(material.GetFloat("_StereoCapture") == 0, "shared deactivation resets stereo state before capture release");
        QuestStandalonePlatform.Enabled = false;
        QuestStandalonePlatform.SetFlatScreenEyes(material, right, right);
        Check(material.mainTexture == leftCapture, "desktop never changes material stereo bindings");
        QuestStandalonePlatform.Enabled = true;
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(left); UnityEngine.Object.DestroyImmediate(right); UnityEngine.Object.DestroyImmediate(rightPixels); UnityEngine.Object.DestroyImmediate(leftCapture);
        UnityEngine.Object.DestroyImmediate(eyes); UnityEngine.Object.DestroyImmediate(planar);
        UnityEngine.Object.DestroyImmediate(screen); UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }
    static int VulkanCameraPlanePixels()
    {
        // A real Vulkan camera/RT checks the different clip-space convention.
        // Synthetic desktop XR constant buffers are intentionally not involved.
        Check(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, "requested real Vulkan fixture is active");
        var camera = new GameObject("vulkan-original-video-camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1; camera.cullingMask = 0;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.blue;
        camera.nearClipPlane = .1f; camera.farClipPlane = 20;
        var texture = Decoded();
        int mipErrors = 0;
        Application.LogCallback watch = (message, stack, type) =>
        { if (message.Contains("RenderTexture.GenerateMips failed")) mipErrors++; };
        Application.logMessageReceived += watch;
        try
        {
            foreach (bool automatic in new[] { true, false })
            {
                var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { useMipMap = true, autoGenerateMips = automatic, filterMode = FilterMode.Trilinear };
                Check(target.Create(), "actual Vulkan mipmapped movie target created");
                camera.targetTexture = target; FlatScreen.Capture(camera, target);
                using (var output = new QuestCameraVideoOutput(null!, "Vulkan-orientation"))
                foreach (var mode in new[] { VideoRenderMode.CameraNearPlane, VideoRenderMode.CameraFarPlane })
                {
                    output.Apply(camera, texture, mode, VideoAspectRatio.Stretch, 1, 1, 1);
                    camera.Render(); output.CompleteCapture();
                    Close(Pixel(target, 8, 8).g, 1, "camera-plane samples retain Vulkan camera orientation");
                    Close(Pixel(target, 56, 56).g, 0, "camera-plane samples retain Vulkan top orientation");
                    Close(Pixel(target, 8, 8).r, 1, "Vulkan correction preserves horizontal movie orientation");
                    Close(Pixel(target, 56, 56).r, 0, "Vulkan correction preserves opposite horizontal orientation");
                    output.Apply(camera, texture, mode, VideoAspectRatio.FitInside, 1, 1, 1);
                    camera.Render(); output.CompleteCapture();
                    Close(Pixel(target, 32, 4).b, 0, "projection correction preserves native letterbox bars");
                    Close(Pixel(target, 8, 24).g, 1, "aspect and projection corrections compose around image centre");
                    // Actual completed mip pixels, not a descriptor assertion.
                    var mip = new RenderTexture(16, 16, 0, target.format, RenderTextureReadWrite.Linear); mip.Create();
                    Graphics.CopyTexture(target, 0, 2, mip, 0, 0);
                    Close(Pixel(mip, 2, 6).g, 1, "completed movie mip pixels remain current for world consumers");
                    UnityEngine.Object.DestroyImmediate(mip);
                }
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
            }
            Check(mipErrors == 0, "automatic mip chains never invoke rejected explicit generation");
        }
        finally
        {
            Application.logMessageReceived -= watch;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(camera.gameObject);
            FlatScreen.ClearFixture();
        }
        GlassCapturePixels();
        return checks;
    }
    public static int Run()
    {
        checks = 0;
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan) return VulkanCameraPlanePixels();
        ScreenStereoPixels();
        Debug.Log("Video fixture actual API=" + SystemInfo.graphicsDeviceType
            + " graphicsUVStartsAtTop=" + SystemInfo.graphicsUVStartsAtTop);
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
            var displayMaterial = new Material(QuestStandalonePlatform.SelectFlatScreenShader(Shader.Find("Hidden/BlitCopy"))) { mainTexture = target };
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
