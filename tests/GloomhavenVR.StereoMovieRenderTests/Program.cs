using System;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

public static class StereoVideoRenderProgram
{
    private static int _count;
    private static void Check(bool condition, string label) { _count++; if (!condition) throw new Exception(label); }
    private static Texture2D Read(RenderTexture source, string path)
    {
        RenderTexture old = RenderTexture.active;
        RenderTexture.active = source;
        var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(path, result.EncodeToPNG());
        return result;
    }
    public static int Run(string output)
    {
        _count = 0;
        Texture2D? packed = null, pixels = null;
        RenderTexture? target = null;
        try
        {
            packed = new Texture2D(3840, 1080, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var colors = new Color32[3840 * 1080];
            for (int y = 0; y < 1080; y++) for (int x = 0; x < 3840; x++)
            {
                // Deliberate unsaturated values detect brightness/tint changes that a
                // saturated eye-color witness misses. Each eye has its own circle.
                int localX = x % 1920;
                int dx = localX - 960, dy = y - 540;
                bool circle = dx * dx + dy * dy < 160 * 160;
                colors[y * 3840 + x] = localX < 200 && y < 200 ? new Color32(140, 16, 160, 255)
                    : localX > 1720 && y > 880 ? new Color32(160, 160, 160, 255)
                    : y >= 960 ? new Color32(8, 120, 140, 255) : y < 120 ? new Color32(160, 100, 4, 255)
                    : circle ? new Color32(20, 30, 200, 255)
                    : x < 1920 ? new Color32(96, 16, 24, 255) : new Color32(16, 112, 32, 255);
            }
            packed.SetPixels32(colors); packed.Apply();
            foreach (var dimensions in new[] { new Vector2Int(320, 180), new Vector2Int(320, 240), new Vector2Int(420, 180) })
            {
                target = new RenderTexture(dimensions.x, dimensions.y, 0); target.Create();
                for (int eye = 0; eye < 2; eye++)
                {
                    FlatScreenStereo.Pixels(packed, target, eye);
                    pixels = Read(target, Path.Combine(output, dimensions.x + "x" + dimensions.y + "-eye" + eye + ".png"));
                    Color sample = pixels.GetPixel(dimensions.x / 2 + 60, dimensions.y / 2);
                    Color expected = eye == 0 ? (Color)new Color32(96, 16, 24, 255) : (Color)new Color32(16, 112, 32, 255);
                    Check(Math.Abs(sample.r - expected.r) < .02f && Math.Abs(sample.g - expected.g) < .02f
                        && Math.Abs(sample.b - expected.b) < .02f, "decoded eye raster retains original unsaturated RGB without tint or brightness change");
                    int minX = dimensions.x, maxX = -1, minY = dimensions.y, maxY = -1;
                    for (int y = 0; y < dimensions.y; y++) for (int x = 0; x < dimensions.x; x++)
                        if (pixels.GetPixel(x, y).b > .7f && pixels.GetPixel(x, y).r < .1f) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                    Check(maxX >= minX && Math.Abs((maxX - minX) - (maxY - minY)) <= 2,
                        "per-eye movie frame retains round source geometry across desktop aspect ratios");
                    Rect frame = FlatScreenStereo.MovieFrameRect(dimensions.x, dimensions.y, 16f / 9f);
                    Color top = pixels.GetPixel(dimensions.x / 2, (int)(dimensions.y * .5f + frame.height * .45f));
                    Color bottom = pixels.GetPixel(dimensions.x / 2, (int)(dimensions.y * .5f - frame.height * .45f));
                    Check(top.g > .44f && top.b > .5f && top.r < .06f && bottom.r > .6f && bottom.b < .04f,
                        "asymmetric top and bottom marks keep original vertical orientation");
                    Color corner = pixels.GetPixel((int)(frame.x + frame.width * .05f), (int)(frame.y + frame.height * .05f));
                    Check(corner.r > .5f && corner.b > .6f && corner.g < .08f,
                        "asymmetric corner marker keeps original placement inside per-eye frame");
                    if (dimensions.y == 240) Check(pixels.GetPixel(160, 10).maxColorComponent < .01f, "tall screen letterboxes rather than stretching recorded eye");
                    if (dimensions.x == 420) Check(pixels.GetPixel(10, 90).maxColorComponent < .01f, "wide screen pillarboxes rather than stretching recorded eye");
                    Object.DestroyImmediate(pixels); pixels = null;
                }
                target.Release(); Object.DestroyImmediate(target); target = null;
            }
            EyeSelection(packed, output);
            PlaneLease(output, packed);
            File.WriteAllText(Path.Combine(output, "assertions.txt"), _count + " assertions\n");
            return _count;
        }
        finally
        {
            NativeMovieCameraPlane.RestoreAll();
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (packed != null) Object.DestroyImmediate(packed);
        }
    }
    private static void EyeSelection(Texture2D packed, string output)
    {
        var camera = new GameObject("head eye observation").AddComponent<Camera>(); camera.enabled = false;
        var player = new GameObject("recorded-eye source").AddComponent<VideoPlayer>();
        var left = new RenderTexture(320, 180, 0); left.Create();
        var right = new RenderTexture(320, 180, 0); right.Create();
        var material = new Material(Shader.Find("Unlit/Texture"));
        var compositor = new FlatScreenStereo(); compositor.Initialize(left, right);
        NativeVideoMovies.CameraPlaneSource = player; MovieDecoderFixture.Texture = packed;
        compositor.SourceChanged();
        try
        {
            foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right,
                         Camera.MonoOrStereoscopicEye.Mono, (Camera.MonoOrStereoscopicEye)987 })
            {
                StereoEyeFixture.ActiveEye = eye;
                compositor.Draw(camera, material);
                RenderTexture selected = (RenderTexture)material.mainTexture;
                var pixels = Read(selected, Path.Combine(output, "actual-eye-" + eye + ".png"));
                Color sample = pixels.GetPixel(220, 90); Object.DestroyImmediate(pixels);
                bool isRight = eye == Camera.MonoOrStereoscopicEye.Right;
                Check(isRight ? sample.g > .42f && sample.r < .08f : sample.r > .36f && sample.g < .08f,
                    isRight ? "actual right eye uses its recorded right raster" : "actual left/mono/unknown eye uses recorded left raster");
            }
        }
        finally
        {
            MovieDecoderFixture.Texture = null; NativeVideoMovies.CameraPlaneSource = null;
            left.Release(); right.Release(); Object.DestroyImmediate(left); Object.DestroyImmediate(right);
            Object.DestroyImmediate(material); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(player.gameObject);
        }
    }
    private static void PlaneLease(string output, Texture2D packed)
    {
        var root = new GameObject("original UI camera");
        var camera = root.AddComponent<Camera>(); camera.enabled = false;
        var player = root.AddComponent<VideoPlayer>(); player.targetCamera = camera;
        player.renderMode = VideoRenderMode.CameraNearPlane;
        player.targetCameraAlpha = .73f;
        NativeVideoMovies.CameraPlaneSource = player;
        var target = new RenderTexture(128, 128, 24); target.Create(); camera.targetTexture = target;
        var canvasRoot = new GameObject("Original native UI", typeof(Canvas));
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera; canvas.planeDistance = 1;
        var imageRoot = new GameObject("Live original control", typeof(RectTransform)); imageRoot.transform.SetParent(canvasRoot.transform, false);
        var image = imageRoot.AddComponent<RawImage>(); image.color = new Color(.2f, .4f, .7f, 1);
        image.rectTransform.sizeDelta = new Vector2(60, 60);
        var foreign = new GameObject("foreign camera").AddComponent<Camera>(); foreign.enabled = false;
        try
        {
            NativeMovieCameraPlane.BeforeRender(camera);
            Check(player.targetCameraAlpha == 0f && player.renderMode == VideoRenderMode.CameraNearPlane
                && player.targetCamera == camera && player.targetTexture == null,
                "camera-plane lease hides packed plane without decoder mode or target mutation");
            NativeMovieCameraPlane.BeforeRender(foreign); NativeMovieCameraPlane.AfterRender(foreign);
            Check(player.targetCameraAlpha == 0f, "nested foreign camera cannot restore enclosing packed plane early");
            var left = new RenderTexture(320, 180, 0); left.Create();
            var right = new RenderTexture(320, 180, 0); right.Create();
            var background = new RenderTexture(320, 180, 0); background.Create();
            RenderTexture old = RenderTexture.active; RenderTexture.active = background;
            GL.Clear(true, true, new Color(20f / 255f, 28f / 255f, 36f / 255f)); RenderTexture.active = old;
            var material = new Material(Shader.Find("Unlit/Texture"));
            var compositor = new FlatScreenStereo(); compositor.Initialize(left, right, background);
            MovieDecoderFixture.Texture = packed; compositor.SourceChanged();
            foreach (float alpha in new[] { .73f, .41f })
            {
                NativeMovieCameraPlane.RestoreAll(); player.targetCameraAlpha = alpha;
                NativeMovieCameraPlane.BeforeRender(camera);
                NativeMovieCameraPlane.BeforeRender(camera);
                Check(Math.Abs(NativeMovieCameraPlane.PresentationAlpha(player) - alpha) < .001f,
                    "nested captured camera retains genuine bottom lease alpha for replacement");
                NativeVideoMovies.CameraPlaneSource = null; compositor.SourceChanged();
                NativeVideoMovies.CameraPlaneSource = player; compositor.SourceChanged();
                StereoEyeFixture.ActiveEye = Camera.MonoOrStereoscopicEye.Left; compositor.Draw(camera, material);
                var replacement = Read(left, Path.Combine(output, "native-alpha-" + alpha + ".png"));
                Color actual = replacement.GetPixel(220, 90); Object.DestroyImmediate(replacement);
                float expectedRed = (96f * alpha + 20f * (1f - alpha)) / 255f;
                Check(Math.Abs(actual.r - expectedRed) < .02f,
                    "borrowed native alpha affects replacement RGB over retained background");
                camera.Render();
                var liveUi = Read(target, Path.Combine(output, "live-ui-alpha-" + alpha + ".png"));
                Color unchangedUi = liveUi.GetPixel(64, 64); Object.DestroyImmediate(liveUi);
                Check(Math.Abs(unchangedUi.r - .2f) < .02f && Math.Abs(unchangedUi.g - .4f) < .02f && Math.Abs(unchangedUi.b - .7f) < .02f,
                    "native nonunit movie alpha leaves live original UI RGB untouched");
                NativeMovieCameraPlane.AfterRender(camera); NativeMovieCameraPlane.AfterRender(camera);
            }
            NativeMovieCameraPlane.RestoreAll(); player.targetCameraAlpha = .73f; NativeMovieCameraPlane.BeforeRender(camera);
            left.Release(); right.Release(); background.Release();
            Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(background); Object.DestroyImmediate(material);
            MovieDecoderFixture.Texture = null;
            camera.Render();
            var pixels = Read(target, Path.Combine(output, "live-original-ui.png"));
            Color sample = pixels.GetPixel(64, 64); Object.DestroyImmediate(pixels);
            Check(Math.Abs(sample.r - .2f) < .02f && Math.Abs(sample.g - .4f) < .02f && Math.Abs(sample.b - .7f) < .02f,
                "real native UI camera still renders original live control while packed plane is hidden");
            NativeMovieCameraPlane.AfterRender(camera);
            Check(Math.Abs(player.targetCameraAlpha - .73f) < .0001f, "post-render restores exact original native plane alpha");
            NativeMovieCameraPlane.BeforeRender(camera); player.targetCameraAlpha = .41f;
            NativeMovieCameraPlane.AfterRender(camera);
            Check(Math.Abs(player.targetCameraAlpha - .41f) < .0001f, "foreign native alpha write survives lease release");
            player.targetCameraAlpha = .62f;
            NativeMovieCameraPlane.BeforeRender(camera); NativeMovieCameraPlane.RestoreAll();
            Check(Math.Abs(player.targetCameraAlpha - .62f) < .0001f, "shutdown restores interrupted camera-plane lease");
        }
        finally
        {
            NativeVideoMovies.CameraPlaneSource = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(root);
            Object.DestroyImmediate(canvasRoot); Object.DestroyImmediate(foreign.gameObject);
        }
    }
}
