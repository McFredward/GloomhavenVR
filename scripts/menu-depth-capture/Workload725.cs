using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthProgram
{
    // The editor supplies tick boundaries; the production visibility policy,
    // coordinator, native Camera.Render callbacks and final glass draw all run.
    private static void Workload725(string output)
    {
        using (var scene = new Scene())
        {
            scene.Entry.Panel.HostCanvas = scene.Panel.GetComponent<Canvas>();
            scene.Entry.Panel.HostGo = scene.Panel.gameObject;
            scene.Panel.SetPositionAndRotation(new Vector3(.3f, .1f, 3), Quaternion.Euler(0, 19, 0));
            scene.Panel.localScale *= 1.2f;
            scene.Head.orthographic = false; scene.Head.fieldOfView = 63; scene.Head.transform.rotation = scene.Panel.rotation;
            var raw = new GameObject("725 sampled white aperture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            raw.gameObject.layer = 8; raw.transform.SetParent(scene.Panel, false);
            raw.rectTransform.sizeDelta = new Vector2(130, 70); raw.texture = Texture2D.whiteTexture;
            var sampled = new Sampled725(raw); MenuDepthSources.Register(sampled);
            int draws = 0;
            Camera.CameraCallback count = camera => { if (camera == scene.Entry.Cam) draws++; };
            Camera.onPreCull += count;
            try
            {
                Canvas.ForceUpdateCanvases(); scene.Capture();
                PanelSupersample.Visibility(scene.Entry);
                float[] distances = { 4f, 2f, 1f, .5f };
                var watch = Stopwatch.StartNew();
                int startDraws = draws, startSources = sampled.Begins;
                for (int tick = 0; tick < 60; tick++)
                {
                    float distance = distances[tick % 4];
                    Vector3 center = scene.Panel.position - scene.Panel.forward * distance;
                    MenuDepthFixture.Left = center - scene.Panel.right * .032f;
                    MenuDepthFixture.Right = center + scene.Panel.right * .032f;
                    scene.Head.transform.position = MenuDepthFixture.Left;
                    PanelSupersample.Visibility(scene.Entry);
                    if (scene.Entry.Cam.enabled) scene.Entry.Cam.Render(); // actual automatic camera eligibility
                    int prior = draws; scene.Capture();
                    Check(draws - prior == 2 && draws - startDraws == (tick + 1) * 2,
                        "a successful spatial window has exactly two UI camera draws per tick and no automatic third draw");
                    int both = draws; PanelSupersample.CaptureAgain(scene.Head);
                    scene.Head.Render(); // Complete that cached observer draw; no private UI recapture is allowed.
                    Check(draws == both, "a second head coordinator callback in the same frame cannot render the eye pair again");
                    Check(sampled.Begins - startSources == (tick + 1) * 2 && sampled.Ends == sampled.Begins,
                        "a moving window prepares one sampled source exactly once for each eye and restores every lease");
                    Check(sampled.Last == new Vector2(scene.Head.targetTexture.width * 2, scene.Head.targetTexture.height * 2),
                        "head-raster source preparation receives the complete actual eye pixel domain without the old native-photo ceiling at fixed two-times density");
                    if (tick < 4)
                    {
                        watch.Stop(); // Pixel readback/evidence IO is not capture-loop timing.
                        using (TextureOwner image = scene.Eye(false, "725-aperture-" + tick))
                        {
                            Vector3[] corners = new Vector3[4]; raw.rectTransform.GetWorldCorners(corners);
                            Vector3 a = scene.Head.WorldToViewportPoint(corners[0]), b = scene.Head.WorldToViewportPoint(corners[2]);
                            float width = Math.Abs(b.x - a.x) * image.Image.width;
                            float height = Math.Abs(b.y - a.y) * image.Image.height;
                            Check(sampled.Left == sampled.Last && width > 0 && height > 0,
                                "both head-raster eyes retain one fixed pixel domain at near mid and far poses");
                            int minimum = image.Image.width, maximum = -1;
                            int xmin = Math.Max(0, (int)(Math.Min(a.x, b.x) * image.Image.width) - 2);
                            int xmax = Math.Min(image.Image.width - 1, (int)(Math.Max(a.x, b.x) * image.Image.width) + 2);
                            int y = (int)((a.y + b.y) * .5f * image.Image.height);
                            for (int x = xmin; x <= xmax; x++)
                            {
                                Color pixel = image.Image.GetPixel(x, y);
                                if (pixel.r < .94f || pixel.g < .94f || pixel.b < .94f) continue;
                                minimum = Math.Min(minimum, x); maximum = Math.Max(maximum, x);
                            }
                            Check(maximum >= minimum && Math.Abs(maximum - minimum + 1 - width) < 2.5f,
                                "the final head raster actually displays the physical sampled aperture at its predicted pixel width");
                        }
                        watch.Start();
                    }
                }
                Check(!scene.Entry.Cam.enabled, "a successful spatial window hands automatic mono capture ownership to the eye pair");
                watch.Stop();
                File.WriteAllText(Path.Combine(output, "725-capture-workload.txt"), "60 explicit moving ticks; actualUIrenders="
                    + (draws - startDraws) + "; sampledPrepares=" + (sampled.Begins - startSources) + "; elapsedMs=" + watch.Elapsed.TotalMilliseconds + "\n");

                var clip = new GameObject("725 actual native clip", typeof(RectTransform), typeof(RectMask2D));
                clip.layer = 8; clip.transform.SetParent(scene.Panel, false); clip.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
                raw.transform.SetParent(clip.transform, false); raw.rectTransform.anchoredPosition = new Vector2(200, 0);
                Canvas.ForceUpdateCanvases(); int masked = sampled.Begins; scene.Capture();
                Check(raw.canvasRenderer.cull && sampled.Begins == masked,
                    "native RectMask2D-clipped sampled content performs no eye-resource work");
                raw.transform.SetParent(scene.Panel, false); raw.rectTransform.anchoredPosition = Vector2.zero;
                Object.DestroyImmediate(clip); Canvas.ForceUpdateCanvases();

                scene.Entry.Panel.RenderHidden = true; PanelSupersample.Visibility(scene.Entry);
                int hiddenDraws = draws, hiddenSources = sampled.Begins; scene.Capture();
                Check(draws == hiddenDraws && sampled.Begins == hiddenSources && scene.Entry.MenuDepthDisplay == null,
                    "a hidden physical display performs no UI or native-content capture");
                scene.Entry.Panel.RenderHidden = false; PanelSupersample.Visibility(scene.Entry);
                Check(scene.Entry.Cam.enabled, "a reopened native window restores first-render mono eligibility before its fresh eye pair");
                scene.Capture(); PanelSupersample.Visibility(scene.Entry);
                Check(!scene.Entry.Cam.enabled && scene.Entry.MenuDepthDisplay != null, "reopening a spatial window recovers stereo ownership without a stale target");
                sampled.Ready = false; scene.Source.Ready = false;
                scene.Image.color = Color.blue;
                Vector3 monoCenter = scene.Panel.position - scene.Panel.forward*4f;
                MenuDepthFixture.Left = monoCenter-scene.Panel.right*.032f;
                MenuDepthFixture.Right = monoCenter+scene.Panel.right*.032f;
                int empty = draws; scene.Capture();
                Check(draws == empty + 1 && scene.Entry.MenuDepthDisplay == null,
                    "leaving spatial content refills a suppressed automatic mono capture immediately");
                using (TextureOwner native = new TextureOwner(Read(scene.Entry.Rt, "725-fresh-mono-fallback")))
                    Check(native.Image.GetPixel(225, 180).b > .7f && native.Image.GetPixel(225, 180).r < .1f,
                        "the mono fallback actually contains newly changed native artwork instead of the previous spatial eye");
                using(TextureOwner monoMip = new TextureOwner(Read(scene.Entry.MipRt,"725-restored-mono-mip"))) { }
                File.WriteAllText(Path.Combine(output,"725-restored-photo-state.txt"),
                    "canvas="+scene.Entry.DisplayGo.GetComponent<Canvas>().enabled+";graphic="+scene.Entry.DisplayImage.enabled
                    +";cull="+scene.Entry.DisplayImage.canvasRenderer.cull+";texture="+scene.Entry.DisplayImage.texture.name
                    +";material="+scene.Entry.DisplayImage.material.shader.name+";rendererMaterial="+scene.Entry.DisplayImage.canvasRenderer.GetMaterial(0).shader.name+"\n");
                using (TextureOwner monoHead = scene.Eye(false,"725-restored-mono-head"))
                {
                    Color pixel = PhysicalPixel(scene,monoHead.Image,new Vector3(-90,0,0));
                    Check(pixel.b>.7f && pixel.r<.1f,
                        "retired spatial presentation returns original mono photograph to final head pixels");
                }
                PanelSupersample.Visibility(scene.Entry);
                Check(scene.Entry.Cam.enabled, "an ordinary window regains its original automatic mono capture policy");
                sampled.Ready = true; scene.Source.Ready = true; scene.Capture(); PanelSupersample.Visibility(scene.Entry);
                scene.Source.ThrowRight = true; int failed = draws; scene.Capture();
                Check(draws == failed + 2 && scene.Entry.MenuDepthDisplay == null && sampled.Ends == sampled.Begins,
                    "a right preparation failure draws the completed left and fresh mono fallback while restoring all begun leases");
            }
            finally { Camera.onPreCull -= count; MenuDepthSources.Unregister(sampled); }
        }
        Sampling725(output);
        Discovery725();
    }

    private static void Discovery725()
    {
        var head = new GameObject("725 first empty head").AddComponent<Camera>(); head.enabled = false;
        head.orthographic = true; head.orthographicSize = 1; head.transform.position = new Vector3(0, 0, -2); head.cullingMask = 1 << 8;
        var target = new RenderTexture(32, 32, 24); target.Create(); head.targetTexture = target;
        var root = new GameObject("725 late source", typeof(RectTransform), typeof(Canvas)); root.layer = 8;
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var image = new GameObject("late native Graphic", typeof(RectTransform)).AddComponent<Image>(); image.transform.SetParent(root.transform, false);
        image.gameObject.layer = 8; image.rectTransform.sizeDelta = Vector2.one;
        var source = new Sampled725(image); bool pending = true; int pumps = 0;
        int previousExcluded = MenuDepthSources.ExcludedDirectLayers; MenuDepthSources.ExcludedDirectLayers = 0;
        Camera? previous = GloomhavenVR.Rig.VRRigDriver.HeadCamera; GloomhavenVR.Rig.VRRigDriver.HeadCamera = head;
        Action pump = () =>
        {
            pumps++;
            if (!pending) return;
            pending = false;
            MenuDepthSources.HasContent(root.transform); // intentional recursive query must not recursively pump
            MenuDepthSources.Register(source);
        };
        try
        {
            Canvas.ForceUpdateCanvases(); MenuDepthSources.SetDiscoveryPump(pump); head.Render();
            Check(source.Begins == 1 && source.Ends == 1 && pumps == 1,
                "an initially empty registry flushes late native readiness before its first actual head draw without recursive pumping");
            MenuDepthSources.Unregister(source); pending = true;
            Check(MenuDepthSources.HasContent(root.transform), "a late content query flushes ready native identities before returning an empty answer");
            MenuDepthSources.Unregister(source); pending = true;
            var graphics = new System.Collections.Generic.List<Graphic>(); MenuDepthSources.CollectReadyGraphics(graphics);
            Check(graphics.Count == 1 && graphics[0] == image, "native screen collection includes a ready source arriving after the last driver tick");
            MenuDepthSources.Unregister(source); pending = true;
            var eye = new MenuDepthEye(false, head.transform.position, head);
            MenuDepthSources.BeforeCaptureWhere(eye, graphic => graphic == image, graphic => eye);
            MenuDepthSources.AfterCapture();
            Check(source.Begins == 2 && source.Ends == 2, "native camera selection flushes pending readiness before borrowing its first source");
            int leasedPumps = pumps;
            MenuDepthSources.BeforeCapture(root.transform, eye);
            MenuDepthSources.HasContent(root.transform);
            Check(pumps == leasedPumps + 1, "an already borrowed source scope cannot recursively run discovery work");
            MenuDepthSources.AfterCapture();
        }
        finally
        {
            MenuDepthSources.AfterCapture(); MenuDepthSources.Unregister(source); MenuDepthSources.SetDiscoveryPump(null);
            MenuDepthSources.ExcludedDirectLayers = previousExcluded;
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = previous; Object.DestroyImmediate(root); head.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(head.gameObject);
        }
    }

    private static void Sampling725(string output)
    {
        var head = new GameObject("725 near-aperture head").AddComponent<Camera>(); head.enabled = false;
        head.stereoTargetEye = StereoTargetEyeMask.None; head.fieldOfView = 70; head.aspect = 4f / 3f;
        head.nearClipPlane = .03f; head.farClipPlane = 20; head.cullingMask = 1 << 29;
        head.clearFlags = CameraClearFlags.SolidColor; head.backgroundColor = Color.black;
        var target = new RenderTexture(640, 480, 24); target.Create(); head.targetTexture = target;
        var aperture = GameObject.CreatePrimitive(PrimitiveType.Quad); aperture.layer = 29;
        var material = new Material(Shader.Find("Unlit/Color")); material.color = Color.white;
        aperture.GetComponent<MeshRenderer>().sharedMaterial = material;
        try
        {
            Check(MenuDepthProjection.LimitPixels(new Vector2(800,600),new Vector2(130,70)) == new Vector2(130,70),
                "legacy photographed sampling demand stops at the actual intermediate UI raster");
            foreach (bool near in new[] { true, false })
            {
                aperture.transform.SetPositionAndRotation(near ? new Vector3(.1f, 0, .15f) : new Vector3(1.1f, 0, 1f),
                    Quaternion.Euler(0, near ? 80 : 35, 0));
                aperture.transform.localScale = new Vector3(1, .5f, 1);
                Vector3 bl = aperture.transform.TransformPoint(new Vector3(-.5f, -.5f));
                Vector3 tl = aperture.transform.TransformPoint(new Vector3(-.5f, .5f));
                Vector3 tr = aperture.transform.TransformPoint(new Vector3(.5f, .5f));
                Vector3 br = aperture.transform.TransformPoint(new Vector3(.5f, -.5f));
                Vector2 pixels = MenuDepthProjection.HeadPixels(new MenuDepthEye(false, Vector3.zero, head), bl, tl, tr, br);
                head.Render();
                using (TextureOwner raster = new TextureOwner(Read(target, near ? "725-near-clipped" : "725-partly-offscreen")))
                {
                    int visible = 0; foreach (Color32 p in raster.Image.GetPixels32()) if (p.r > 230) visible++;
                    Check(visible > 1000 && pixels.x > 0 && pixels.y > 0 && pixels.x <= target.width * 2 && pixels.y <= target.height * 2,
                        "a visibly near-clipped angled pane receives bounded visible pixel demand instead of native-size fallback");
                }
            }
            MethodInfo? allocated = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            var counter = allocated != null ? (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocated) : null;
            long calibrationBefore = counter?.Invoke() ?? -1;
            var calibration = new byte[16 * 1024]; calibration[0] = 1;
            long calibrationAfter = counter?.Invoke() ?? -1; GC.KeepAlive(calibration);
            long calibrationDelta = calibrationAfter - calibrationBefore;
            if (calibrationBefore < 0 || calibrationAfter < 0 || calibrationDelta < calibration.Length) counter = null;
            Vector3 p0 = new(-.5f, -.5f, 1), p1 = new(-.5f, .5f, 1), p2 = new(.5f, .5f, 1), p3 = new(.5f, -.5f, 1);
            var eye = new MenuDepthEye(false, Vector3.zero, head);
            Check(MenuDepthProjection.HeadPixels(eye, -p0, -p1, -p2, -p3) == Vector2.zero,
                "an aperture entirely behind the viewer never requests a maximum sampled target");
            Check(MenuDepthProjection.HeadPixels(eye, new Vector3(float.NaN, 0, 1), p1, p2, p3) == Vector2.zero
                && MenuDepthProjection.LimitPixels(new Vector2(-1, -1), Vector2.one) == Vector2.zero,
                "invalid or negative aperture demand remains unknown-safe instead of requesting resources");
            Matrix4x4 original = head.projectionMatrix; head.projectionMatrix = Matrix4x4.zero;
            Check(MenuDepthProjection.HeadPixels(eye, p0, p1, p2, p3) == Vector2.zero,
                "a degenerate head projection cannot manufacture a full-raster demand");
            head.projectionMatrix = original;
            for (int i = 0; i < 64; i++) MenuDepthProjection.HeadPixels(eye, p0, p1, p2, p3);
            var watch = new Stopwatch(); long before = counter?.Invoke() ?? 0; float checksum = 0;
            watch.Start(); for (int i = 0; i < 10000; i++) checksum += MenuDepthProjection.HeadPixels(eye, p0, p1, p2, p3).x; watch.Stop();
            long bytes = counter != null ? counter() - before : -1;
            File.WriteAllText(Path.Combine(output, "725-sampling-cost.txt"), "10000 sampled aperture projections; elapsedMs="
                + watch.Elapsed.TotalMilliseconds + "; threadAllocatedBytes=" + (bytes >= 0 ? bytes.ToString() : "unavailable")
                + "; counterCalibration16KiBDelta=" + calibrationDelta + "; checksum=" + checksum + "\n");
        }
        finally
        {
            head.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(material);
            Object.DestroyImmediate(aperture); Object.DestroyImmediate(head.gameObject);
        }
    }

    private sealed class Sampled725 : IMenuDepthSampledSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => Ready;
        internal bool Ready = true;
        internal int Begins, Ends;
        internal Vector2 Last, Left;
        internal Sampled725(Graphic graphic) => Graphic = graphic;
        public void BeforeCapture(MenuDepthEye eye) { Begins++; Last = eye.PixelFootprint; if (!eye.Right) Left = Last; }
        public void AfterCapture() => Ends++;
    }
}
