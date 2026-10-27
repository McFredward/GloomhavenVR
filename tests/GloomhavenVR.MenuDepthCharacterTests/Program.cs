using System;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MenuDepthCharacterProgram
{
    private static void ActivateCharacters()
    {
        VRSession.IsRunning = true;
        // Exact surrounding Driver registration input. The supplied callback
        // executes production's queued native-character discovery, not a model.
        MenuDepthSources.SetDiscoveryPump(MenuDepthCharacters.FlushForCapture);
    }
    private static void ShutdownCharacters()
    { MenuDepthSources.SetDiscoveryPump(null); MenuDepthCharacters.Shutdown(); }
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    private static bool Near(float a, float b, float tolerance = .0001f) => Math.Abs(a - b) <= tolerance;
    private static bool MatrixNear(Matrix4x4 a, Matrix4x4 b)
    { for (int i = 0; i < 16; i++) if (!Near(a[i], b[i])) return false; return true; }
    private static Texture2D Read(RenderTexture target, string path)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var result = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); result.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, result.EncodeToPNG()); return result;
    }
    private static float Centroid(Texture2D image, int channel)
    {
        Color32[] pixels = image.GetPixels32(); float weight = 0, moment = 0;
        for (int y = 0; y < image.height; y++) for (int x = 0; x < image.width; x++)
        {
            Color32 p = pixels[y * image.width + x];
            bool hit = channel == 0 ? p.r > 80 && p.r > p.g * 2 && p.r > p.b * 2
                : p.g > 80 && p.g > p.r * 2 && p.g > p.b * 2;
            if (hit) { weight++; moment += x; }
        }
        Check(weight > 30, "original native model and companion remain visible in the eye raster");
        return moment / weight;
    }
    private static float Difference(Texture2D first, Texture2D second)
    {
        Color32[] a = first.GetPixels32(), b = second.GetPixels32(); float total = 0;
        for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i].r - b[i].r)
            + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
        return total / (a.Length * 4f);
    }
    private static Color Peak(Texture2D image)
    {
        Color result = Color.clear;
        foreach (Color color in image.GetPixels())
            result = new Color(Mathf.Max(result.r, color.r), Mathf.Max(result.g, color.g),
                Mathf.Max(result.b, color.b), Mathf.Max(result.a, color.a));
        return result;
    }

    public static int Run(string output)
    {
        _assertions = 0; ActivateCharacters();
        using (var scene = new Scene())
        {
            scene.Source.Render();
            Texture2D nativeScene = Read(scene.Native, Path.Combine(output, "native-original.png"));
            Object.DestroyImmediate(nativeScene);
            Texture2D original = scene.CaptureMono(Path.Combine(output, "native-original-ui.png"));
            MenuDepthCharacters.Tick();
            Check(MenuDepthSources.HasContent(scene.Root), "actual original camera/RT identity discovers the live character preview");
            Vector3 center = scene.NeutralEye(scene.Image);
            Texture2D neutral = scene.Capture(center, false, Path.Combine(output, "neutral-original-fit.png"));
            Check(Centroid(neutral, 0) > 10 && Centroid(neutral, 1) < neutral.width - 10,
                "the completed native model and companion fit the adjusted cabinet presentation");
            Object.DestroyImmediate(original); Object.DestroyImmediate(neutral);
            scene.CheckRestored();

            Texture2D left = scene.Capture(center - scene.Image.rectTransform.right * .032f, false, Path.Combine(output, "eye-left.png"));
            Texture2D right = scene.Capture(center + scene.Image.rectTransform.right * .032f, true, Path.Combine(output, "eye-right.png"));
            Check(Centroid(right, 1) - Centroid(left, 1) > .2f,
                "each recorded eye renders the native model from its own physical window-relative viewpoint");
            // Build726 preserves native body sizes instead of expanding this
            // small fixture group to fill the pane. Total changed-pixel area is
            // no longer a meaningful stereo threshold: measure the independent
            //64mm pinhole disparity of its companion at native depth+.6 instead.
            float units = Mathf.Max(2.4f / (1.2f * .82f), 2.4f / (.9f * .75f));
            float depth = .6f / units;
            float expectedDisparity = .064f * depth / (scene.NeutralDistance(scene.Image) + depth) * 256f / 1.2f;
            Check(Mathf.Abs(Centroid(right, 1) - Centroid(left, 1) - expectedDisparity) < .35f,
                "the native64mm eye baseline produces the independently specified physical companion disparity");
            Object.DestroyImmediate(left); Object.DestroyImmediate(right);

            Texture2D side = scene.Capture(center + scene.Image.rectTransform.right * .18f, false, Path.Combine(output, "head-side.png"));
            Texture2D baseView = scene.Capture(center, false, Path.Combine(output, "head-centre.png"));
            Check(Centroid(side, 1) - Centroid(baseView, 1) > .5f,
                "sideways head movement reveals a new native model view instead of only a fixed stereo pair");
            Object.DestroyImmediate(side); Object.DestroyImmediate(baseView);
            Check(scene.Effect.Draws >= 6, "the actual original camera-hosted image effect participates in native and eye renders");
            scene.CheckRestored();

            Matrix4x4 oldView = scene.Source.worldToCameraMatrix;
            scene.Source.transform.position += new Vector3(.2f, 0, 0);
            Check(!MatrixNear(oldView, scene.Source.worldToCameraMatrix),
                "restoring an automatic camera keeps subsequent native pose changes automatic");
            scene.Source.transform.position -= new Vector3(.2f, 0, 0);
            Matrix4x4 oldProjection = scene.Source.projectionMatrix;
            float oldFov = scene.Source.fieldOfView; scene.Source.fieldOfView = oldFov + 7;
            Check(!MatrixNear(oldProjection, scene.Source.projectionMatrix),
                "restoring an automatic camera keeps subsequent native FOV changes automatic");
            scene.Source.fieldOfView = oldFov;

            Matrix4x4 customView = scene.Source.worldToCameraMatrix; customView.m03 += .03f;
            Matrix4x4 customProjection = scene.Source.projectionMatrix; customProjection.m02 += .02f;
            Matrix4x4 customCulling = customProjection * customView; customCulling.m03 += .01f;
            scene.Source.worldToCameraMatrix = customView; scene.Source.projectionMatrix = customProjection; scene.Source.cullingMatrix = customCulling;
            Texture2D custom = scene.Capture(scene.NeutralEye(scene.Image), false, Path.Combine(output, "native-custom-matrices.png"));
            Object.DestroyImmediate(custom);
            Check(MatrixNear(customView, scene.Source.worldToCameraMatrix) && MatrixNear(customProjection, scene.Source.projectionMatrix)
                && MatrixNear(customCulling, scene.Source.cullingMatrix), "all custom original camera matrices return exactly after eye rendering");
            scene.Source.ResetWorldToCameraMatrix(); scene.Source.ResetProjectionMatrix(); scene.Source.ResetCullingMatrix();

            Texture2D cropOriginal = scene.Capture(center, false, Path.Combine(output, "uncropped-cabinet-ui.png"));
            scene.Image.uvRect = new Rect(.15f, .1f, .7f, .8f);
            Texture2D cropEye = scene.Capture(center, false, Path.Combine(output, "eye-cropped-ui.png"));
            Check(Difference(cropOriginal, cropEye) < .8f,
                "native RawImage UV cropping preserves the fitted model and companion");
            Object.DestroyImmediate(cropOriginal); Object.DestroyImmediate(cropEye);
            scene.Image.uvRect = new Rect(0, 0, 1, 1);

            RectTransform second = scene.SecondPreview();
            ShutdownCharacters(); ActivateCharacters(); MenuDepthCharacters.Tick();
            Check(MenuDepthSources.HasContent(second), "a second original RawImage sharing the native render target gets its own source");
            Vector3 firstEye = scene.NeutralEye(scene.Image) + scene.Image.rectTransform.right * .13f;
            Vector3 secondEye = second.TransformPoint(new Vector3(second.rect.center.x, second.rect.center.y))
                + second.right * .13f - second.forward * scene.NeutralDistance(scene.SecondImage!);
            Texture2D first = scene.Capture(firstEye, false, Path.Combine(output, "window-first.png"));
            Texture2D secondView = scene.Capture(secondEye, false, Path.Combine(output, "window-rotated.png"), second);
            Check(Difference(first, secondView) < .8f,
                "moved rotated windows derive the same local view independently of their different world poses");
            Object.DestroyImmediate(first); Object.DestroyImmediate(secondView);
            scene.CheckRestored();

            scene.Root.localScale *= 1.5f;
            Vector3 scaledEye = scene.NeutralEye(scene.Image);
            Texture2D scaled = scene.Capture(scaledEye, false, Path.Combine(output, "shared-resize.png"));
            Object.DestroyImmediate(scaled);
            Check(scene.Root.localScale.x == .0015f, "character eye rendering never rewrites the shared window pose or manual size");

            scene.Image.color = new Color(1, 1, 1, .55f);
            CanvasGroup group = scene.Root.gameObject.AddComponent<CanvasGroup>(); group.alpha = .5f;
            var maskGo = new GameObject("original preview clipper", typeof(RectTransform), typeof(RectMask2D));
            maskGo.layer = 5; maskGo.transform.SetParent(scene.Root, false);
            RectTransform mask = (RectTransform)maskGo.transform; mask.sizeDelta = new Vector2(900, 650);
            scene.Image.rectTransform.SetParent(mask, false);
            scene.Source.Render();
            Texture2D nativeUi = scene.CaptureMono(Path.Combine(output, "native-alpha-mask.png"));
            Texture2D eyeUi = scene.Capture(scene.NeutralEye(scene.Image), false, Path.Combine(output, "eye-alpha-mask.png"));
            Check((Peak(nativeUi) - Peak(eyeUi)).maxColorComponent < .01f && Peak(eyeUi).a > .25f && Peak(eyeUi).a < .30f,
                "original native UI tint ancestor fade and RectMask clipping survive character depth");
            Check(eyeUi.GetPixel(2, 2).a < .01f, "original clipper still excludes the outer preview pixels");
            Object.DestroyImmediate(nativeUi); Object.DestroyImmediate(eyeUi);

            scene.Manager.Hidden = true;
            Check(!MenuDepthSources.HasContent(scene.Root), "original native hide permission suppresses depth without calling native continuation");
            scene.Manager.Hidden = false;
            Check(MenuDepthSources.HasContent(scene.Root), "original native show restores existing depth readiness");
            RenderTexture foreign = new RenderTexture(64, 64, 24); foreign.Create();
            scene.Image.texture = foreign;
            MenuDepthCharacters.Tick();
            Check(!MenuDepthSources.HasContent(scene.Root), "pooled preview source replacement retires its exact old registration immediately");
            Check(ReferenceEquals(scene.Image.texture, foreign), "retiring a stale preview preserves a newer native texture");
            foreign.Release(); Object.DestroyImmediate(foreign);
            ShutdownCharacters();
            Check(!MenuDepthSources.HasContent(second), "shutdown releases all per-window character sources");
            Check(scene.Native.IsCreated(), "shutdown preserves the original game-owned render target");
        }
        return _assertions;
    }

    public static int ThrowingRender(string output)
    {
        _assertions = 0; ActivateCharacters();
        using (var scene = new Scene())
        {
            MenuDepthCharacters.Tick();
            bool threw = false;
            try { MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, scene.NeutralEye(scene.Image), scene.UiCamera)); }
            catch (InvalidOperationException) { threw = true; }
            finally { MenuDepthSources.AfterCapture(); }
            Check(threw, "injected native render failure reached the actual camera lease");
            scene.CheckRestored();
            Check(ReferenceEquals(scene.Image.texture, scene.Native), "throwing source render cannot leave an eye texture on native UI");
        }
        return _assertions;
    }

    public static int Lifecycle(string output)
    {
        _assertions = 0; ActivateCharacters();
        var harmony = new Harmony("GloomhavenVR.MenuDepthCharacter.Lifecycle." + typeof(MenuDepthCharacterProgram).Assembly.GetName().Name);
        harmony.PatchAll(typeof(MenuDepthCharacterDisplayObserver));
        try
        {
            using (var scene = new Scene())
            {
                scene.Manager.Hidden = true; scene.Source.gameObject.SetActive(false); scene.Image.gameObject.SetActive(false);
                MenuDepthCharacters.Tick();
                Check(!MenuDepthSources.HasContent(scene.Root), "inactive native previews never advertise render readiness");
                scene.Source.gameObject.SetActive(true); scene.Manager.Hidden = false; scene.Image.gameObject.SetActive(true);
                Check(MenuDepthSources.HasContent(scene.Root),
                    "pooled inactive original consumer and disabled native manager are bound before first visibility");
                Texture2D first = scene.Capture(scene.NeutralEye(scene.Image) - scene.Root.right * .032f,
                    false, Path.Combine(output, "first-visible-eye.png"));
                Check(Centroid(first, 1) > 0, "the first visible capture already renders the actual native model eye");
                Object.DestroyImmediate(first);
                RenderTexture firstOwnedEye = scene.LastEye!;

                scene.Manager.Hidden = true;
                Check(!MenuDepthSources.HasContent(scene.Root), "native Hide immediately suppresses a retained structural binding");
                scene.Source.gameObject.SetActive(false); scene.Image.gameObject.SetActive(false); MenuDepthCharacters.Tick();
                scene.Source.gameObject.SetActive(true); scene.Manager.Hidden = false; scene.Image.gameObject.SetActive(true);
                Check(MenuDepthSources.HasContent(scene.Root) && firstOwnedEye.IsCreated(),
                    "native HideAll and window reopening retain depth before the next bounded census");

                MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, scene.NeutralEye(scene.Image), scene.UiCamera));
                RenderTexture lost = (RenderTexture)scene.Image.texture; MenuDepthSources.AfterCapture(); lost.Release();
                MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, scene.NeutralEye(scene.Image), scene.UiCamera));
                RenderTexture restored = (RenderTexture)scene.Image.texture;
                Check(restored.IsCreated() && !ReferenceEquals(restored, lost),
                    "a released owned eye resource is replaced before the next native eye capture");
                MenuDepthSources.AfterCapture(); scene.CheckRestored();
                Check(scene.Native.IsCreated(), "owned eye recovery preserves the original game-owned target");

                RectTransform second = scene.SecondPreview();
                Check(!MenuDepthSources.HasContent(second), "newly created consumers wait for an actual native display observation");
                scene.Manager.Display(second, ECharacter.Brute);
                Check(scene.Manager.DisplayCalls == 1, "the original display request still runs exactly once before observation");
                Check(MenuDepthSources.HasContent(second),
                    "the actual native Display postfix binds a new window before its first visible render");
                Texture2D dynamicEye = scene.Capture(scene.NeutralEye(scene.SecondImage!), true,
                    Path.Combine(output, "first-dynamic-window-eye.png"), second);
                Check(Centroid(dynamicEye, 1) > 0, "a newly created native display request renders its actual first model eye");
                Object.DestroyImmediate(dynamicEye);
                scene.Manager.Display(second, ECharacter.Brute, "skin", "PowerUp");
                Check(scene.Manager.DisplayCalls == 2 && MenuDepthSources.HasContent(second),
                    "repeated native skin and animation requests preserve the same structural depth consumers");
                ShutdownCharacters();
                scene.Manager.Display(second, ECharacter.Brute);
                Check(scene.Manager.DisplayCalls == 3 && !MenuDepthSources.HasContent(second),
                    "a disabled menu-depth feature never rebinds on later native Display requests");
            }
        }
        finally { MenuDepthSources.AfterCapture(); harmony.UnpatchSelf(); }
        return _assertions;
    }

    public static int NativeCloseDuringRender(string output)
    {
        _assertions = 0; ActivateCharacters();
        using (var scene = new Scene())
        {
            MenuDepthCharacters.Tick();
            RenderTexture lease = null!; bool aliveInsideOriginal = false;
            scene.Effect.BeforeBlit = () =>
            {
                lease = scene.Source.targetTexture;
                ShutdownCharacters();
                aliveInsideOriginal = lease.IsCreated();
            };
            bool rejected = false;
            try { MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, scene.NeutralEye(scene.Image), scene.UiCamera)); }
            catch (InvalidOperationException) { rejected = true; }
            finally { MenuDepthSources.AfterCapture(); }
            Check(rejected && aliveInsideOriginal,
                "shutdown during the actual native image effect defers owned-target destruction until the original draw returns");
            Check(lease != null && !lease.IsCreated(), "deferred character teardown releases its eye resource after native draw completion");
            scene.CheckRestored();
            Check(scene.Native.IsCreated() && !MenuDepthSources.HasContent(scene.Root),
                "native-close teardown preserves the original game target and retires depth readiness");
        }
        return _assertions;
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Camera Source, UiCamera;
        internal readonly Character3DDisplayManager Manager;
        internal readonly CharacterNativeImageEffect Effect;
        internal readonly RenderTexture Native, Ui;
        internal readonly RectTransform Root;
        internal readonly RawImage Image;
        internal RawImage? SecondImage;
        internal RenderTexture? LastEye;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly float _near, _far;
        private readonly int _sourceLayer = 8;
        private readonly Material _red, _green;

        internal Scene()
        {
            // Exact source-profile shape: serialized GUI3D uses a square target,
            // FOV30, near.1/far10, transparent clear and no HDR/MSAA. The fixture
            // uses quarter resolution and test geometry instead of game assets.
            Native = new RenderTexture(256, 256, 24); Native.Create();
            Ui = new RenderTexture(256, 192, 24); Ui.Create();
            var sourceGo = new GameObject("original GUI character camera");
            Source = sourceGo.AddComponent<Camera>(); Source.transform.position = new Vector3(20, 30, 95);
            Source.stereoTargetEye = StereoTargetEyeMask.None; Source.clearFlags = CameraClearFlags.SolidColor;
            Source.backgroundColor = Color.clear; Source.cullingMask = 1 << _sourceLayer;
            Source.fieldOfView = 30; Source.aspect = 1; Source.nearClipPlane = .1f; Source.farClipPlane = 10;
            Source.allowHDR = false; Source.allowMSAA = false;
            Source.targetTexture = Native;
            Manager = sourceGo.AddComponent<Character3DDisplayManager>();
            var holder = new GameObject("original native model holder");
            holder.transform.position = Source.transform.position + new Vector3(.4f, -.45f, 2.56f);
            Manager.Bind(holder.transform);
            Effect = sourceGo.AddComponent<CharacterNativeImageEffect>();
            _red = new Material(Shader.Find("Unlit/Color")); _red.color = Color.red;
            _green = new Material(Shader.Find("Unlit/Color")); _green.color = Color.green;
            Model(holder.transform, "native animated main model", new Vector3(-.35f, .4f, 0), new Vector3(.3f, .7f, .3f), _red);
            Model(holder.transform, "authored companion", new Vector3(.2f, .4f, .6f), new Vector3(.3f, .45f, .25f), _green);
            var root = new GameObject("actual original native world-space window", typeof(RectTransform), typeof(Canvas));
            Root = (RectTransform)root.transform; Root.sizeDelta = new Vector2(1200, 900); Root.localScale = Vector3.one * .001f;
            root.layer = 5; Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            Image = CreateImage(Root, 5);
            var uiGo = new GameObject("private original window capture"); UiCamera = uiGo.AddComponent<Camera>();
            UiCamera.enabled = false; UiCamera.stereoTargetEye = StereoTargetEyeMask.None; UiCamera.clearFlags = CameraClearFlags.SolidColor;
            UiCamera.backgroundColor = Color.clear; UiCamera.cullingMask = 1 << 5; UiCamera.targetTexture = Ui;
            _position = Source.transform.position; _rotation = Source.transform.rotation;
            _near = Source.nearClipPlane; _far = Source.farClipPlane;
        }
        private RawImage CreateImage(Transform parent, int layer)
        {
            var go = new GameObject("original character preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.layer = layer; go.transform.SetParent(parent, false); var image = go.GetComponent<RawImage>();
            image.rectTransform.sizeDelta = new Vector2(1200, 900); image.texture = Native; return image;
        }
        private void Model(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = _sourceLayer;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        internal float NeutralDistance(RawImage image)
        {
            float width = image.rectTransform.rect.width * image.rectTransform.TransformVector(Vector3.right).magnitude;
            float height = image.rectTransform.rect.height * image.rectTransform.TransformVector(Vector3.up).magnitude;
            float sx = image.uvRect.width, sy = image.uvRect.height;
            return .5f * Mathf.Sqrt(width * height * Source.projectionMatrix.m00 * Source.projectionMatrix.m11 / (sx * sy));
        }
        internal Vector3 NeutralEye(RawImage image)
        {
            RectTransform rect = image.rectTransform;
            float width = rect.rect.width * rect.TransformVector(Vector3.right).magnitude;
            float height = rect.rect.height * rect.TransformVector(Vector3.up).magnitude;
            return rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.center.y))
                - rect.forward * NeutralDistance(image)
                - rect.right * ((image.uvRect.center.x - .5f) * width / image.uvRect.width)
                - rect.up * ((image.uvRect.center.y - .5f) * height / image.uvRect.height);
        }
        internal Texture2D Capture(Vector3 eye, bool right, string path, RectTransform? root = null)
        {
            RectTransform target = root ?? Root;
            MenuDepthSources.BeforeCapture(target, new MenuDepthEye(right, eye, UiCamera));
            try { LastEye = (RenderTexture)(root == null ? Image.texture : SecondImage!.texture); return Draw(target, eye, path); }
            finally { MenuDepthSources.AfterCapture(); MenuDepthSources.AfterCapture(); }
        }
        internal Texture2D CaptureMono(string path) => Draw(Root, NeutralEye(Image), path);
        private Texture2D Draw(RectTransform root, Vector3 eye, string path)
        {
            Check(MenuDepthProjection.TryBuild(root, eye, out Matrix4x4 view, out Matrix4x4 projection), "actual physical capture rectangle admits the supplied eye");
            UiCamera.cullingMask = 1 << root.gameObject.layer;
            UiCamera.transform.position = eye; UiCamera.transform.rotation = root.rotation;
            UiCamera.worldToCameraMatrix = view; UiCamera.projectionMatrix = projection; UiCamera.cullingMatrix = projection * view;
            Canvas.ForceUpdateCanvases(); UiCamera.Render(); return Read(Ui, path);
        }
        internal RectTransform SecondPreview()
        {
            var go = new GameObject("second native window", typeof(RectTransform), typeof(Canvas)); go.layer = 6;
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(1200, 900); rect.localScale = Vector3.one * .001f;
            rect.position = new Vector3(.4f, .2f, 1); rect.rotation = Quaternion.Euler(0, 35, 0);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; SecondImage = CreateImage(rect, 6); return rect;
        }
        internal void CheckRestored()
        {
            Check(ReferenceEquals(Source.targetTexture, Native), "original camera target is restored after the eye render");
            Check(Source.transform.position == _position && Source.transform.rotation == _rotation, "original camera position and facing are restored after the eye render");
            Check(Near(Source.nearClipPlane, _near) && Near(Source.farClipPlane, _far) && !Source.orthographic,
                "original camera projection mode and clipping fields are restored after the eye render");
            Check(ReferenceEquals(Image.texture, Native), "original RawImage texture is restored after private UI capture");
        }
        public void Dispose()
        {
            ShutdownCharacters(); MenuDepthSources.AfterCapture();
            Native.Release(); Ui.Release(); Object.DestroyImmediate(Native); Object.DestroyImmediate(Ui);
            Object.DestroyImmediate(_red); Object.DestroyImmediate(_green);
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
        }
    }
}
