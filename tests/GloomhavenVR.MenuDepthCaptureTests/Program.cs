using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthProgram
{
    private static int _checks;
    private static string _output = string.Empty;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Exception(label); }
    private static bool Near(float a, float b, float epsilon = .001f) => Mathf.Abs(a - b) <= epsilon;
    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    { for (int i = 0; i < 16; i++) if (!Near(a[i], b[i], .0001f)) return false; return true; }
    private static Texture2D Read(RenderTexture target, string name)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG()); return image;
    }

    public static int Run(string output)
    {
        _checks = 0; _output = output;
        Check(MenuDepthDisplay.DepthShader != null, "actual depth graphic shader is embedded and supported");
        Projection();
        using (var scene = new Scene())
        {
            scene.Capture();
            Check(scene.Entry.MenuDepthDisplay != null && PanelSupersample.Completed > 0,
                "registered native content reaches actual per-eye capture coordinator before eye draw");
            Check(scene.Source.Begins == 2 && scene.Source.Ends == 2,
                "each eye prepares and restores the same native source exactly once");
            Check(scene.Image.rectTransform.localPosition.z == 0,
                "native image transforms remain on the original flat interaction plane");
            Check(scene.Entry.Cam.targetTexture == scene.Entry.Rt && scene.Entry.Cam.orthographic
                && Same(scene.OriginalView, scene.Entry.Cam.worldToCameraMatrix)
                && Same(scene.OriginalProjection, scene.Entry.Cam.projectionMatrix),
                "private camera retains original target and automatic mono matrices after both eyes");
            using (TextureOwner left = scene.Eye(false, "left"))
            using (TextureOwner right = scene.Eye(true, "right"))
            {
                Check(PrintedControlsVisible(left.Image, false) && PrintedControlsVisible(right.Image, true),
                    "coplanar native text and controls remain at independently projected physical positions for both eyes");
                Check(Difference(left.Image, right.Image, new RectInt(50, 40, 220, 115)) > 100,
                    "actual depth geometry produces distinct recorded eye perspectives");
            }
            scene.Panel.rotation = Quaternion.Euler(0, 23, 0); scene.Panel.position = new Vector3(.3f, .1f, 3.3f);
            scene.Panel.localScale *= 1.25f;
            MenuDepthFixture.Left = new Vector3(.55f, .1f, .2f);
            MenuDepthFixture.Right = new Vector3(.85f, .1f, .2f);
            scene.Capture();
            Check(scene.Source.Begins == 4 && scene.Source.Ends == 4,
                "rotated resized moved window uses its current world frame for each eye");
            using (TextureOwner side = scene.Eye(false, "moved-window-left"))
                Check(side.Image.GetPixel(160, 90).maxColorComponent > .1f,
                    "side head position and moved window retain visible original content");
            Check(scene.Entry.Cam.targetTexture == scene.Entry.Rt && scene.Entry.Cam.orthographic,
                "a second moved-window capture restores its native mono camera again");
            scene.Source.ThrowRight = true;
            RenderTexture previousRight = scene.SeenRight!;
            scene.Capture();
            Check(scene.Entry.MenuDepthDisplay == null && PanelSupersample.ExtraBytes == 0,
                "failed second-eye preparation releases only owned depth targets and restores mono presentation");
            Check(!previousRight.IsCreated(), "failed second-eye preparation actually releases its right render surface");
            Check(scene.Source.Ends == scene.Source.Begins,
                "throwing native source is restored including its partially begun right-eye lease");
            Check(scene.Entry.Cam.orthographic && scene.Entry.Cam.targetTexture == scene.Entry.Rt,
                "throwing native source cannot retain borrowed capture projection or target");
            scene.Source.ThrowRight = false;
            scene.Capture();
            Check(scene.Entry.MenuDepthDisplay != null, "a later valid native source can recover after the failed pair");
            scene.Source.Ready = false;
            scene.Capture();
            Check(scene.Entry.MenuDepthDisplay == null && PanelSupersample.ExtraBytes == 0,
                "leaving the spatial subview releases its extra eye pair");
            scene.Source.Ready = true;
            long headBytes = MenuDepthDisplay.HeadCost(scene.Head, scene.Entry.Rt);
            PanelSupersample.ExhaustBudget(); scene.Capture();
            Check(scene.Entry.MenuDepthDisplay != null && PanelSupersample.ExtraBytes == headBytes,
                "ordinary mono budget exhaustion cannot downsample or refuse an independently admitted head raster");
        }
        using (var scene = new Scene())
        {
            scene.Source.Ready = false;
            scene.Capture();
            Check(scene.Entry.MenuDepthDisplay == null && scene.Source.Begins == 0,
                "ordinary mono window does not prepare or allocate spatial content");
        }
        RegistryFailure();
        AlphaDepth();
        MaskedContent();
        MixedCaptureOrder();
        DirectAndNested();
        Check(PanelSupersample.ExtraBytes == 0, "all owned eye resources are released after window teardown");
        return _checks;
    }

    private static void Projection()
    {
        var go = new GameObject("actual projection plane", typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(4, 2);
        rect.position = new Vector3(1, .5f, 4); rect.rotation = Quaternion.Euler(0, 31, 0);
        rect.localScale = new Vector3(1.2f, 1.2f, 1.2f);
        Vector3 eye = new(.3f, .2f, .1f);
        Check(MenuDepthProjection.TryBuild(rect, eye, out Matrix4x4 view, out Matrix4x4 projection),
            "live world rectangle admits a front eye");
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = (projection * view).MultiplyPoint(corners[i]);
            Check(Near(p.x, i < 2 ? -1 : 1) && Near(p.y, i == 0 || i == 3 ? -1 : 1),
                "window corners remain exactly pinned in the off-axis capture");
        }
        Check(!MenuDepthProjection.TryBuild(rect, rect.position + rect.forward, out _, out _),
            "eye behind the physical window refuses invalid projection");
        Check(!MenuDepthProjection.TryBuild(rect, new Vector3(float.NaN, 0, 0), out _, out _),
            "invalid tracked position refuses rather than submitting a NaN matrix");
        Vector3 projected = MenuDepthProjection.ProjectToPlane(new Vector3(0, 0, .5f), new Vector3(-.2f, 0, -2));
        Check(Near(projected.x, -.04f) && Near(projected.y, 0) && Near(projected.z, .5f),
            "shared analytic portal projection preserves depth and moves artwork for the actual local eye");
        Object.DestroyImmediate(go);
    }

    private static void RegistryFailure()
    {
        var root = new GameObject("source scope", typeof(RectTransform));
        var first = new TestSource(MakeImage(root.transform, "first", new Vector2(20, 20), Vector2.zero, Color.white, 0));
        var failing = new TestSource(MakeImage(root.transform, "failing", new Vector2(20, 20), Vector2.zero, Color.white, 0)) { ThrowRight = true };
        MenuDepthSources.Register(first); MenuDepthSources.Register(failing);
        try
        {
            try { MenuDepthSources.BeforeCapture(root.transform, new MenuDepthEye(true, Vector3.zero, null!)); }
            catch (InvalidOperationException) { }
            Check(first.Begins == 1 && first.Ends == 1 && failing.Begins == 1 && failing.Ends == 1,
                "source scope restores every actually begun source when later preparation throws");
            MenuDepthSources.AfterCapture();
            Check(first.Ends == 1 && failing.Ends == 1, "source cleanup is idempotent after a failed begin");
        }
        finally { MenuDepthSources.Unregister(first); MenuDepthSources.Unregister(failing); Object.DestroyImmediate(root); }
    }

    private static void DirectAndNested()
    {
        using (var scene = new Scene())
        {
            scene.Capture();
            var aux = new GameObject("nested mono camera").AddComponent<Camera>(); aux.enabled = false; aux.cullingMask = 0;
            aux.targetTexture = new RenderTexture(16, 16, 0); aux.targetTexture.Create();
            Camera.CameraCallback interleave = camera =>
            {
                if (camera != scene.Head || MenuDepthFixture.CurrentEye != Camera.MonoOrStereoscopicEye.Right) return;
                MenuDepthFixture.CurrentEye = Camera.MonoOrStereoscopicEye.Mono;
                aux.Render(); MenuDepthFixture.CurrentEye = Camera.MonoOrStereoscopicEye.Right;
            };
            Camera.onPreRender += interleave;
            try
            {
                using (TextureOwner right = scene.Eye(true, "nested-right"))
                using (TextureOwner left = scene.Eye(false, "nested-left"))
                    Check(Difference(left.Image, right.Image, new RectInt(50, 40, 220, 115)) > 100,
                        "nested mono render restores the enclosing right eye shader selection");
            }
            finally
            {
                Camera.onPreRender -= interleave;
                aux.targetTexture.Release(); Object.DestroyImmediate(aux.targetTexture); Object.DestroyImmediate(aux.gameObject);
            }
            scene.Source.Ready = false; scene.Capture();
            scene.Source.Ready = true; scene.Image.gameObject.layer = 27;
            int began = scene.Source.Begins;
            scene.Head.Render();
            Check(scene.Source.Begins == began + 1 && scene.Source.Ends == scene.Source.Begins,
                "direct converted UI retains its source lease when supersampling is unavailable");
        }
    }

    private static void MaskedContent()
    {
        foreach (bool stencil in new[] { false, true })
        {
            using (var scene = new Scene())
            {
                scene.AddMask(stencil);
                scene.Capture();
                using (TextureOwner eye = scene.Eye(false, stencil ? "stencil-aperture" : "rect-aperture"))
                {
                    Check(eye.Image.GetPixel(110, 90).maxColorComponent < .03f,
                        "original UI aperture clips displaced art outside its native mask");
                    Check(eye.Image.GetPixel(160, 90).r > .7f,
                        "native mask keeps the original artwork visible inside its aperture");
                }
                if (!stencil)
                {
                    scene.UseDirect();
                    Vector3 position = new(.8f, 0, 0);
                    Check(MenuDepthProjection.TryBuild(scene.Panel, position, out Matrix4x4 view, out Matrix4x4 projection),
                        "direct off-axis mask fixture has a valid actual eye view");
                    scene.Head.transform.position = position;
                    scene.Head.orthographic = false; scene.Head.worldToCameraMatrix = view; scene.Head.projectionMatrix = projection;
                    using (TextureOwner direct = scene.Eye(false, "direct-rect-aperture"))
                        Check(direct.Image.GetPixel(127, 90).r > .7f,
                            "direct RectMask2D aperture stays on its original plane while art is deeper");
                }
            }
        }
    }

    private static void AlphaDepth()
    {
        using (var scene = new Scene())
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = x >= 10 && x < 22 && y >= 10 && y < 22
                    ? new Color32(0, 0, 255, 0) : new Color32(0, 0, 255, 255);
            texture.SetPixels32(pixels); texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
            Image front = MakeImage(scene.Panel, "native cutout in front", new Vector2(200, 150), Vector2.zero, Color.white, 10);
            front.sprite = sprite; front.material = scene.Image.material;
            front.transform.SetSiblingIndex(0); scene.Image.transform.SetSiblingIndex(1);
            var source = new TestSource(front); MenuDepthSources.Register(source);
            try
            {
                scene.Capture();
                using (TextureOwner eye = scene.Eye(false, "alpha-depth"))
                {
                    Check(eye.Image.GetPixel(160, 90).r > .7f,
                        "zero-alpha native sprite pixels leave depth open for farther artwork");
                    Check(eye.Image.GetPixel(122, 90).b > .7f,
                        "opaque nearer native sprite pixels occlude later farther UI triangles");
                }
            }
            finally { MenuDepthSources.Unregister(source); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }
    }

    private static void MixedCaptureOrder()
    {
        using (var scene = new Scene())
        {
            var direct = new GameObject("separate direct window", typeof(RectTransform), typeof(Canvas)); direct.layer = 27;
            direct.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            direct.transform.position = new Vector3(-3, 0, 3);
            var source = new TestSource(MakeImage(direct.transform, "direct content", new Vector2(20, 20), Vector2.zero, Color.white, 0));
            MenuDepthSources.Register(source);
            Action<Camera> offscreen = camera => PanelSupersample.Capture(camera);
            MenuDepthSources.BeforeHeadCapture += offscreen;
            try
            {
                scene.Head.Render();
                Check(scene.Entry.MenuDepthDisplay != null && scene.Entry.MenuDepthFailures == 0
                    && scene.Source.Begins == 2 && scene.Source.Ends == 2 && source.Begins == 1 && source.Ends == 1,
                    "mixed direct and supersampled windows finish offscreen captures before direct source leases");
            }
            finally
            {
                MenuDepthSources.BeforeHeadCapture -= offscreen;
                MenuDepthSources.Unregister(source); Object.DestroyImmediate(direct);
            }
            scene.Source.OnBegin = () => PanelSupersample.Clear();
            int submitted = 0;
            Camera.CameraCallback countDraws = camera => { if (camera == scene.Entry.Cam && MenuDepthDisplay.IsCapturing(camera)) submitted++; };
            Camera.onPreCull += countDraws;
            try { scene.Capture(); }
            finally { Camera.onPreCull -= countDraws; }
            Check(scene.Source.Ends == scene.Source.Begins && PanelSupersample.ExtraBytes == 0,
                "native owner teardown during source preparation restores its begun lease and releases the pair");
            Check(submitted == 0, "closed native owner cannot submit another eye draw after its source lease ends");
        }
    }

    private static bool PrintedControlsVisible(Texture2D image, bool right)
    {
        using (var reference = new CameraOwner())
        {
            reference.Camera.transform.position = right ? MenuDepthFixture.Right : MenuDepthFixture.Left;
            reference.Camera.fieldOfView = 2f * Mathf.Atan(.9f / 3f) * Mathf.Rad2Deg;
            reference.Camera.aspect = 16f / 9f;
            Vector3 pixel = reference.Camera.WorldToViewportPoint(new Vector3(-215*.005f,-146*.005f,3));
            int x = Mathf.RoundToInt(pixel.x*image.width), y = Mathf.RoundToInt(pixel.y*image.height);
            if (x < 0 || y < 0 || x >= image.width || y >= image.height) return false;
            Color color = image.GetPixel(x,y);
            return color.b > .65f && color.b > color.r*2f;
        }
    }
    private sealed class CameraOwner : IDisposable
    {
        internal readonly Camera Camera;
        internal CameraOwner() { Camera = new GameObject("independent physical landmark camera").AddComponent<Camera>(); Camera.enabled=false; }
        public void Dispose() => Object.DestroyImmediate(Camera.gameObject);
    }
    private static int Difference(Texture2D left, Texture2D right, RectInt rect)
    {
        int count = 0;
        for (int y = rect.yMin; y < rect.yMax; y++) for (int x = rect.xMin; x < rect.xMax; x++)
        {
            Color a = left.GetPixel(x, y), b = right.GetPixel(x, y);
            if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > .025f) count++;
        }
        return count;
    }

    private sealed class TextureOwner : IDisposable
    {
        internal readonly Texture2D Image;
        internal TextureOwner(Texture2D image) => Image = image;
        public void Dispose() => Object.DestroyImmediate(Image);
    }
    private sealed class TestSource : IMenuDepthSource
    {
        public Graphic Graphic { get; }
        internal bool Ready = true, ThrowRight;
        internal int Begins, Ends;
        internal Action? OnBegin;
        internal TestSource(Graphic graphic) => Graphic = graphic;
        public bool IsReady => Ready;
        public void BeforeCapture(MenuDepthEye eye)
        {
            Begins++; if (ThrowRight && eye.Right) throw new InvalidOperationException("explicit source refusal");
            OnBegin?.Invoke();
            GeometryInput? geometry = Graphic.GetComponent<GeometryInput>();
            if (geometry != null)
            {
                geometry.Projected = eye.ProjectToGraphicPlane;
                geometry.LocalEye = Graphic.transform.InverseTransformPoint(eye.WorldPosition);
                Graphic.SetVerticesDirty();
            }
        }
        public void AfterCapture()
        {
            Ends++;
            GeometryInput? geometry = Graphic.GetComponent<GeometryInput>();
            if (geometry != null) { geometry.Projected = false; Graphic.SetVerticesDirty(); }
        }
    }
    private sealed class GeometryInput : BaseMeshEffect
    {
        internal float Depth;
        internal Vector3 LocalEye;
        internal bool Projected;
        public override void ModifyMesh(VertexHelper vertices)
        {
            UIVertex vertex = default;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i); vertex.position.z += Depth;
                if (Projected) vertex.position = MenuDepthProjection.ProjectToPlane(vertex.position, LocalEye);
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
    private static Image MakeImage(Transform parent, string name, Vector2 size, Vector2 position, Color color, float z)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
        image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition3D = new Vector3(position.x, position.y, 0);
        if (z != 0) { go.AddComponent<GeometryInput>().Depth = z; image.SetVerticesDirty(); }
        return image;
    }
    private sealed class Scene : IDisposable
    {
        internal readonly RectTransform Panel;
        internal readonly Camera Head;
        internal readonly PanelSupersample.Entry Entry;
        internal readonly TestSource Source;
        internal readonly Image Image;
        internal readonly Matrix4x4 OriginalView, OriginalProjection;
        private readonly RenderTexture _result;
        private readonly Material _depthMaterial;
        internal RenderTexture? SeenRight;
        internal Scene()
        {
            PanelSupersample.Clear(); MenuDepthFixture.Left = new Vector3(-.15f, 0, 0); MenuDepthFixture.Right = new Vector3(.15f, 0, 0);
            var go = new GameObject("original world-space window", typeof(RectTransform), typeof(Canvas)); go.layer = 8;
            Panel = go.GetComponent<RectTransform>(); Panel.sizeDelta = new Vector2(640, 360); Panel.position = new Vector3(0, 0, 3);
            Panel.localScale = Vector3.one * .005f; go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            MakeImage(Panel, "original stable controls", new Vector2(180, 48), new Vector2(-215, -146), new Color(.1f, .4f, .9f, 1), 0);
            var label = new GameObject("actual native text", typeof(RectTransform)); label.layer = 8; label.transform.SetParent(Panel, false);
            var text = label.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = "Depth 123"; text.fontSize = 22; text.color = Color.white; text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(180, 48); text.rectTransform.anchoredPosition = new Vector2(-215, -146);
            Image = MakeImage(Panel, "original depth artwork", new Vector2(220, 170), Vector2.zero, new Color(.9f, .15f, .07f, 1), 45);
            _depthMaterial = new Material(MenuDepthDisplay.DepthShader!); Image.material = _depthMaterial;
            MakeImage(Panel, "near original artwork detail", new Vector2(50, 100), new Vector2(50, 15), Color.green, 0);
            Source = new TestSource(Image); MenuDepthSources.Register(Source); MenuDepthSources.ExcludedDirectLayers = 1 << 8;
            var capture = new GameObject("private native UI capture").AddComponent<Camera>(); capture.enabled = false;
            capture.transform.SetParent(Panel, false); capture.transform.localPosition = new Vector3(0, 0, -600);
            capture.orthographic = true; capture.orthographicSize = .9f; capture.aspect = 16f / 9f;
            capture.nearClipPlane = .1f; capture.farClipPlane = 10; capture.cullingMask = 1 << 8;
            capture.clearFlags = CameraClearFlags.SolidColor; capture.backgroundColor = Color.clear;
            var rt = new RenderTexture(640, 360, 24); rt.Create(); capture.targetTexture = rt;
            var mip = new RenderTexture(640, 360, 0) { useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear }; mip.Create();
            var display = new GameObject("actual window display", typeof(RectTransform), typeof(Canvas)); display.layer = 27;
            display.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var displayRect = display.GetComponent<RectTransform>(); displayRect.sizeDelta = Panel.rect.size;
            displayRect.SetPositionAndRotation(Panel.position, Panel.rotation); displayRect.localScale = Panel.lossyScale;
            var raw = display.AddComponent<RawImage>(); raw.texture = mip; raw.raycastTarget = false;
            Head = new GameObject("observed head camera").AddComponent<Camera>(); Head.enabled = false;
            Head.orthographic = false; Head.fieldOfView = 2f * Mathf.Atan(.9f / 3f) * Mathf.Rad2Deg; Head.aspect = 16f / 9f; Head.cullingMask = 1 << 27;
            Head.clearFlags = CameraClearFlags.SolidColor; Head.backgroundColor = Color.black;
            _result = new RenderTexture(320, 180, 24); _result.Create(); Head.targetTexture = _result; VRRigDriver.HeadCamera = Head;
            Entry = new PanelSupersample.Entry { Panel = new ConvertedPanel { HostRect = Panel }, Cam = capture,
                CamGo = capture.gameObject, DisplayGo = display, Rt = rt, MipRt = mip, DisplayImage = raw,
                Frame = Panel.rect, RtW = rt.width, RtH = rt.height, VramBytes = (long)rt.width * rt.height * 40 / 3 };
            PanelSupersample.Add(Entry); PanelSupersample.Synchronize(Entry);
            Canvas.ForceUpdateCanvases(); capture.Render(); Graphics.Blit(rt, mip); mip.GenerateMips();
            OriginalView = capture.worldToCameraMatrix; OriginalProjection = capture.projectionMatrix;
        }
        internal void Capture()
        {
            PrepareEye(false);
            PanelSupersample.Capture(Head);
            if (Entry.MenuDepthDisplay != null)
                SeenRight = (RenderTexture)typeof(MenuDepthDisplay).GetField("_headRight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Entry.MenuDepthDisplay)!;
        }
        internal TextureOwner Eye(bool right, string name)
        {
            PrepareEye(right);
            Canvas.ForceUpdateCanvases(); Head.Render();
            return new TextureOwner(Read(_result, name));
        }
        internal void PrepareEye(bool right)
        {
            MenuDepthFixture.CurrentEye = right ? Camera.MonoOrStereoscopicEye.Right : Camera.MonoOrStereoscopicEye.Left;
            Head.transform.position = right ? MenuDepthFixture.Right : MenuDepthFixture.Left;
            Head.ResetWorldToCameraMatrix();
        }
        internal void AddMask(bool stencil)
        {
            var mask = new GameObject("original clipping aperture", typeof(RectTransform)); mask.layer = 8;
            mask.transform.SetParent(Panel, false); mask.GetComponent<RectTransform>().sizeDelta = new Vector2(140, 120);
            if (stencil) { mask.AddComponent<Image>(); mask.AddComponent<Mask>().showMaskGraphic = false; }
            else mask.AddComponent<RectMask2D>();
            Image.transform.SetParent(mask.transform, false); Image.rectTransform.anchoredPosition = Vector2.zero;
            Image.SetAllDirty(); Canvas.ForceUpdateCanvases();
        }
        internal void UseDirect()
        {
            PanelSupersample.Clear(); Entry.DisplayGo.SetActive(false);
            foreach (Transform child in Panel.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 27;
            MenuDepthSources.ExcludedDirectLayers = 1 << 8;
        }
        public void Dispose()
        {
            MenuDepthSources.Unregister(Source); PanelSupersample.Clear(); VRRigDriver.HeadCamera = null;
            // Deliberate release mutants are cleaned only after the real assertion.
            if (SeenRight != null) { SeenRight.Release(); Object.DestroyImmediate(SeenRight); }
            Entry.Rt.Release(); Entry.MipRt.Release(); _result.Release();
            Object.DestroyImmediate(Entry.Rt); Object.DestroyImmediate(Entry.MipRt); Object.DestroyImmediate(_result);
            Object.DestroyImmediate(Entry.DisplayGo); Object.DestroyImmediate(Panel.gameObject);
            Object.DestroyImmediate(Head.gameObject); Object.DestroyImmediate(_depthMaterial);
        }
    }
}
