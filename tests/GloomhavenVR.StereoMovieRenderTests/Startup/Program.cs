using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

public static class StereoVideoRenderProgram
{
    private const string Canonical = "Embedded/TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best.mov";
    private static int _count;
    private static void Check(bool value, string label) { _count++; if (!value) throw new Exception(label); }
    private static Texture2D Pixels(RenderTexture rt, string path)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply(); RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG()); return texture;
    }
    private static Texture2D Packed()
    {
        var texture = new Texture2D(3840, 1080, TextureFormat.RGBA32, false);
        var pixels = new Color32[3840 * 1080];
        for (int y = 0; y < 1080; y++) for (int x = 0; x < 3840; x++)
            pixels[y * 3840 + x] = y < 120 ? new Color32(20, 20, 120, 255)
                : x < 1920 ? new Color32(96, 16, 24, 255) : new Color32(16, 112, 32, 255);
        texture.SetPixels32(pixels); texture.Apply(); return texture;
    }
    public static int Run(string output) => Exercise(output, true, false, "deactivate");
    public static int Unprepared(string output) => Exercise(output, false, false, "deactivate");
    public static int Late(string output) => Exercise(output, true, true, "deactivate");
    public static int Error(string output) => Exercise(output, true, false, "error");
    public static int DestroyPlayer(string output) => Exercise(output, true, false, "destroy-player");
    public static int DestroyCamera(string output) => Exercise(output, true, false, "destroy-camera");
    public static int SourceChanged(string output) => Exercise(output, true, false, "source");
    public static int CameraChanged(string output) => Exercise(output, true, false, "camera");
    public static int SurfaceLost(string output) => Exercise(output, true, false, "surface");
    public static int VrStopped(string output) => Exercise(output, true, false, "vr");
    public static int IntroDisabled(string output) => Refused(output, "intro");
    public static int NonVr(string output) => Refused(output, "vr");
    public static int ForeignTarget(string output) => Refused(output, "foreign");
    public static int NoHead(string output) => Refused(output, "head");
    public static int MissingOriginal(string output) => Refused(output, "missing-original");
    public static int ThrowingReadiness(string output) => Refused(output, "throwing-readiness");

    private static int Exercise(string output, bool prepared, bool late, string ending)
    {
        _count = 0;
        Initialize(output);
        var head = new GameObject("XR head observation").AddComponent<Camera>();
        head.orthographic = true; head.orthographicSize = .9f; head.aspect = 16f / 9f; head.cullingMask = VRLayers.ModLayerMask;
        head.clearFlags = CameraClearFlags.SolidColor; head.backgroundColor = Color.black; VRRigDriver.HeadCamera = head;
        var result = new RenderTexture(320, 180, 24); result.Create(); head.targetTexture = result;
        var camera = NativeCamera();
        var player = new GameObject("actual IntroPlayer source admission").AddComponent<VideoPlayer>();
        Configure(player, camera); player.targetCameraAlpha = .73f;
        File.WriteAllText(Path.Combine(output, "admission-input.txt"), "native-url=" + player.url + "\nprovided-url=" + MovieSourceFixture.GetUrl(player) + "\nsource=" + player.source
            + "\nactive=" + player.isActiveAndEnabled + "\nkey=" + NativeVideoMovies.KeyFromUrl(MovieSourceFixture.GetUrl(player))
            + "\ncanonical=" + NativeVideoMovies.CanonicalKey(NativeVideoMovies.KeyFromUrl(MovieSourceFixture.GetUrl(player)))
            + "\nmono=" + File.Exists(MovieSourceFixture.GetUrl(player)) + "\nstereo=" + File.Exists(MovieSourceFixture.GetUrl(player).Replace(".mov", ".sbs-v1.mov")) + "\n");
        var window = new FlatScreen();
        Texture2D packed = Packed(); MovieDecoderFixture.Texture = packed; MovieDecoderFixture.Prepared = prepared;
        var ui = new GameObject("Original studio UI", typeof(Canvas));
        var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        ui.layer = 5;
        var control = new GameObject("Live original logo/control", typeof(RectTransform)); control.layer = 5; control.transform.SetParent(ui.transform, false);
        var image = control.AddComponent<RawImage>(); image.color = new Color(.2f, .4f, .7f, 1); image.rectTransform.sizeDelta = new Vector2(120, 80);
        try
        {
            if (late)
            {
                window.Tick();
                Check(!window.Routed, "pre-menu capture without a registered movie keeps its normal mono intro guard");
                NativeVideoMovies.SetNativeUrl(player, MovieSourceFixture.GetUrl(player), Canonical.Substring(0, Canonical.Length - 4));
                NativeVideoMovies.RegisterCameraPlane(player);
                camera.Render();
                Check(window.Routed && camera.targetTexture == window.Glass,
                    "known startup camera reconciles late source admission before its first actual render");
            }
            else
            {
                // Exact production prefix executes before the native Play boundary.
                // Decoder readiness/pixels are supplied; native Harmony/Play/timer
                // ordering is separately exercised by native-video's real patch.
                NativeStartupMoviePatch.Prefix(player);
                Check(window.Routed && FlatScreen.OwnsStartupMovie(player),
                    "automatic studio prefix prepares the actual capture and stereo lifecycle without options or a scenario board");
                Check(player.targetCameraAlpha == 0,
                    "studio packed plane is reserved before original Play rather than first hidden at late pre-cull");
            }
            Check(MovieSourceFixture.GetUrl(player).EndsWith(".sbs-v1.mov") && NativeVideoMovies.CameraPlaneSource == player,
                "native source admission selects the exact registered studio revision before presentation");
            Check(player.renderMode == VideoRenderMode.CameraNearPlane && player.targetCamera == camera && player.targetTexture == null,
                "startup keeps original native decoder render mode and target binding");
            var other = new GameObject("another player reusing the same camera").AddComponent<VideoPlayer>(); Configure(other, camera);
            Check(!FlatScreen.OwnsStartupMovie(other), "persistent reservation proves exact decoder identity, not only its reused camera");
            Object.DestroyImmediate(other.gameObject);
            camera.Render();
            usingPixels(window.Glass!, Path.Combine(output, "original-live-ui.png"), texture =>
            {
                Color p = texture.GetPixel(texture.width / 2, texture.height / 2);
                Check(Math.Abs(p.b - .7f) < .03f, "original startup UI camera keeps its live logos while its packed plane is hidden");
            });
            foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right })
            {
                StereoEyeFixture.ActiveEye = eye; head.Render();
                usingPixels(result, Path.Combine(output, "automatic-" + eye + ".png"), texture =>
                {
                    Color p = texture.GetPixel(220, 90);
                    if (!prepared) Check(p.maxColorComponent < .02f, "unprepared automatic studio holds neutral black before its first decoded eye");
                    else if (eye == Camera.MonoOrStereoscopicEye.Right) Check(p.g > .28f && p.r < .08f,
                        "automatic startup right head render shows only its recorded right eye");
                    else Check(p.r > .24f && p.g < .08f,
                        "automatic startup left head render shows only its recorded left eye");
                    Color controlPixel = texture.GetPixel(160, 90);
                    Check(Math.Abs(controlPixel.b - .7f) < .04f, "automatic startup preserves identical live original UI over both recorded eyes");
                });
            }
            if (!late)
            {
                Check(player.targetCameraAlpha == 0 && Math.Abs(NativeMovieCameraPlane.PresentationAlpha(player) - .73f) < .001f,
                    "persistent startup plane stays hidden between renders while replacement retains original alpha");
                player.targetCameraAlpha = .41f; window.Census();
                Check(player.targetCameraAlpha == 0 && Math.Abs(NativeMovieCameraPlane.PresentationAlpha(player) - .41f) < .001f,
                    "later native alpha write updates replacement appearance without reopening packed plane");
                if (ending == "error")
                {
                    MethodInfo callback = typeof(VideoPlayer).GetMethod("InvokeErrorReceivedCallback_Internal", BindingFlags.Static | BindingFlags.NonPublic)!;
                    Check(callback != null, "actual Unity decoder error callback delivery seam is present");
                    callback!.Invoke(null, new object[] { player, "injected decoder failure" });
                }
                else if (ending == "destroy-player") Object.DestroyImmediate(player);
                else if (ending == "destroy-camera") Object.DestroyImmediate(camera);
                else if (ending == "source") MovieSourceFixture.SetUrl(player, Path.Combine(Path.GetDirectoryName(typeof(StereoVideoRenderProgram).Assembly.Location)!, "Movies", Canonical));
                else if (ending == "camera") player.targetCamera = head;
                else if (ending == "surface") window.LoseMovieSurface();
                else if (ending == "vr") VRSession.IsRunning = false;
                else player.gameObject.SetActive(false); // Native four-second continuation's exact first action.
                if (ending != "error") window.Census();
                if (player != null) Check(Math.Abs(player.targetCameraAlpha - .41f) < .001f,
                    "studio reservation restores current original alpha on native continuation or ownership loss: " + ending);
                Check(!ReservationActive(),
                    "studio continuation and failed ownership release the persistent reservation: " + ending);
            }
            File.WriteAllLines(Path.Combine(output, "lifecycle.log"), VRLog.Messages);
            return _count;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(output, "lifecycle.log"), VRLog.Messages);
            NativeMovieCameraPlane.RestoreAll(); window.End();
            MovieDecoderFixture.Texture = null; VRRigDriver.HeadCamera = null;
            result.Release(); Object.DestroyImmediate(result); Object.DestroyImmediate(packed);
            Object.DestroyImmediate(ui); if (player != null) Object.DestroyImmediate(player.gameObject);
            if (camera != null) Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(head.gameObject);
            UnloadFixtureBundle();
        }
    }
    private static int Refused(string output, string reason)
    {
        _count = 0; Initialize(output);
        var head = new GameObject("head input").AddComponent<Camera>(); VRRigDriver.HeadCamera = head;
        var camera = NativeCamera(); var player = new GameObject("startup fallback source").AddComponent<VideoPlayer>(); Configure(player, camera);
        var window = new FlatScreen(); RenderTexture? foreign = null;
        if (reason == "intro") WorldUIConfig.ShowIntro.Value = false;
        if (reason == "vr") VRSession.IsRunning = false;
        if (reason == "head") VRRigDriver.HeadCamera = null;
        if (reason == "foreign") { foreign = new RenderTexture(80, 80, 0); foreign.Create(); camera.targetTexture = foreign; }
        if (reason == "missing-original")
        {
            WorldUIConfig.ShowIntro.Value = false;
            File.Delete(Path.Combine(Path.GetDirectoryName(typeof(StereoVideoRenderProgram).Assembly.Location)!, "Movies", Canonical));
        }
        try
        {
            try { NativeStartupMoviePatch.Prefix(player); }
            catch { Check(false, "throwing startup readiness cannot escape the prefix and skip native Play or continuation"); }
            Check(!NativeVideoMovies.IsStereoUrl(MovieSourceFixture.GetUrl(player)) && player.targetCameraAlpha == 1,
                "unowned startup cannot hide a native plane or expose a packed source: " + reason);
            Check(!ReservationActive(), "startup fallback owns no persistent reservation: " + reason);
            if (foreign != null) Check(camera.targetTexture == foreign, "foreign startup camera target remains independent");
            return _count;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(output, "lifecycle.log"), VRLog.Messages);
            window.End(); NativeMovieCameraPlane.RestoreAll(); VRRigDriver.HeadCamera = null;
            if (foreign != null) { foreign.Release(); Object.DestroyImmediate(foreign); }
            Object.DestroyImmediate(player.gameObject); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(head.gameObject);
            UnloadFixtureBundle();
        }
    }
    private static Camera NativeCamera()
    {
        var camera = new GameObject("Main Camera: serialized studio UI").AddComponent<Camera>(); camera.orthographic = true;
        camera.orthographicSize = 5; camera.depth = -1; camera.cullingMask = 1 << 5;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; return camera;
    }
    private static void Configure(VideoPlayer player, Camera camera)
    { player.playOnAwake = false; player.renderMode = VideoRenderMode.CameraNearPlane; player.targetCamera = camera; player.source = VideoSource.Url; MovieSourceFixture.SetUrl(player, Path.Combine(Path.GetDirectoryName(typeof(StereoVideoRenderProgram).Assembly.Location)!, "Movies", Canonical)); }
    private static void Initialize(string output)
    {
        VRSession.IsRunning = true; WorldUIConfig.ConversionActive = true; VRLog.Messages.Clear();
        typeof(BepInEx.Paths).GetProperty("BepInExConfigPath")!.SetValue(null, Path.Combine(output, "private-BepInEx.cfg"), null);
        WorldUIConfig.Initialize(Path.Combine(output, "private-fixture.cfg"));
        Scene scene = SceneManager.GetSceneByName("Intro"); if (!scene.IsValid()) scene = SceneManager.CreateScene("Intro"); SceneManager.SetActiveScene(scene);
        string movies = Path.Combine(Path.GetDirectoryName(typeof(StereoVideoRenderProgram).Assembly.Location)!, "Movies");
        string mono = Path.Combine(movies, Canonical), stereo = Path.Combine(movies, NativeVideoMovies.StereoKey(Canonical));
        Directory.CreateDirectory(Path.GetDirectoryName(mono)!); File.WriteAllText(mono, "Explicit decoder-pixel boundary; no native movie decoder invoked."); File.WriteAllText(stereo, "Explicit decoder-pixel boundary; no native movie decoder invoked.");
    }
    private static void usingPixels(RenderTexture rt, string path, Action<Texture2D> inspect)
    { Texture2D texture = Pixels(rt, path); try { inspect(texture); } finally { Object.DestroyImmediate(texture); } }
    private static bool ReservationActive() => !ReferenceEquals(typeof(NativeMovieCameraPlane)
        .GetField("_startupPlayer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), null);
    private static void UnloadFixtureBundle()
    {
        var bundle = (AssetBundle?)typeof(StereoVideoSurface).GetField("_bundle", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        if (bundle != null) bundle.Unload(true);
    }
}
