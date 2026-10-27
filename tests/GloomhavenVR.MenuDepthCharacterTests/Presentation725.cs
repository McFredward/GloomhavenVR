using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Authored geometry, native manager/model identity, physical eyes and effective
// downstream samples are explicit inputs. Every camera/image effect/UI draw and
// Source/Projection/lease/pool decision executes the production implementation.
public static class MenuDepthCharacterPresentation725
{
    private static int _checks;
    private static void Check(bool value, string text) { _checks++; if (!value) throw new Exception(text); }
    private static readonly Vector3 Front = new Vector3(.4f, -.1f, 2.3f), Back = new Vector3(1.2f, .5f, 2.9f);
    // Includes the authored skinned envelope and both complete marker quads.
    private static readonly Bounds Envelope = new Bounds(new Vector3(.595f, -.1f, 2.6f), new Vector3(1.29f, 1.8f, .6f));

    public static int Presentation(string output) => Run(output, false);
    public static int NativeLdrEffect(string output) => Run(output, true);
    private static int Run(string output, bool effect)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var scene = new Scene(output, effect))
        {
            // These are absolute authored presentation requirements, independent
            // of the old mono framing and independent of production matrices.
            Vector3 front = scene.Map(Front), back = scene.Map(Back);
            Check(front.z < -.02f * scene.Aperture && front.z >= -.036f * scene.Aperture,
                "the front of the authored figure protrudes only slightly through the glass");
            Check(back.z > 0 && back.z < .11f * scene.Aperture,
                "the complete authored figure stays in a shallow cabinet around the pane");
            Check(Mathf.Abs(scene.Map(Envelope.center).x) < .0001f && Mathf.Abs(scene.Map(Envelope.center).y) < .0001f,
                "the completed model and companion envelope is centered on the pane");
            float bodyHeight = 1.8f / scene.Scale.x;
            Check(Mathf.Abs(bodyHeight / scene.Height - .75f) < .001f,
                "authored body height uses an appropriate three-quarter window fit");
            var motions = new[] { Vector3.zero, new Vector3(.17f, .10f, .2f), new Vector3(-.18f, -.08f, -.25f) };
            foreach (Vector3 motion in motions)
            {
                var raster = new Vector2[2, 2];
                for (int eye = 0; eye < 2; eye++)
                {
                    Vector3 localEye = new Vector3((eye == 0 ? -.032f : .032f) + motion.x, motion.y, -1.2f + motion.z);
                    Texture2D image = scene.Capture(localEye, eye != 0, new Vector2(2048, 1536), 140,
                        "anchor-" + motion + "-" + eye);
                    for (int marker = 0; marker < 2; marker++)
                    {
                        Vector2 observed = Centroid(image, marker), expected = scene.Project(localEye, marker == 0 ? front : back);
                        raster[eye, marker] = observed;
                        float error = Vector2.Distance(Vector2.Scale(observed - expected, new Vector2(image.width, image.height)), Vector2.zero);
                        scene.Log("anchor," + localEye + "," + marker + "," + observed + "," + expected + "," + error);
                        Check(error < 1.5f, "actual rendered landmarks occupy the independently specified shallow centered cabinet");
                    }
                    Check(scene.Invariant, "original camera holder lights skinned model and animation stay fixed during every native eye draw");
                    scene.Restored(); Object.DestroyImmediate(image);
                }
                for (int marker = 0; marker < 2; marker++)
                {
                    Vector3 point = scene.Reconstruct(new Vector3(-.032f + motion.x, motion.y, -1.2f + motion.z), raster[0, marker], raster[1, marker]);
                    Check(Vector3.Distance(point, marker == 0 ? front : back) < .025f,
                        "actual stereo rays reconstruct the fixed absolute near-pane model depth across head movement");
                }
            }
            // Real near/mid/far head rasters; frequencies are explicit scene
            // content matched to each final raster's Nyquist limit. The near
            // case deliberately exceeds the old1024 native target's capacity.
            foreach (float distance in new[] { 2.4f, 1.2f, .55f })
            {
                scene.Source.MaintainResources(Time.unscaledTime + 3f);
                float pixels = scene.Height * scene.Head.pixelHeight / (2 * distance * Mathf.Tan(30 * Mathf.Deg2Rad));
                int frequency = distance < 1 ? 400 : distance < 2 ? 160 : 80;
                Texture2D image = scene.Capture(new Vector3(0, 0, -distance), false,
                    new Vector2(pixels * scene.Width / scene.Height, pixels), frequency, "frequency-" + distance);
                double correlation = scene.FrequencyCorrelation(image, distance, frequency);
                scene.Log("frequency," + distance + "," + frequency + "," + scene.LastTarget.width + "," + pixels + "," + correlation);
                Check(correlation > .86, "near mid and far actual head rasters preserve the independently authored spatial frequency");
                Check(scene.LastTarget.width <= 2048 && scene.LastTarget.height <= 2048,
                    "per-eye allocation stays within the fixed spatial sample cap");
                if (distance < 1) Check(scene.LastTarget.width == 2048,
                    "close-up native1024 preview acquires enough effective samples for the physical aperture");
                Object.DestroyImmediate(image);
            }
            RenderTexture held = scene.LastTarget;
            for (int i = 0; i < 6; i++)
            {
                scene.CaptureTarget(new Vector3(0, 0, -.7f), false, new Vector2(1530 + i * 5, 1000));
                Check(ReferenceEquals(held, scene.LastTarget), "head-footprint jitter reuses the current owned eye allocation");
            }
            scene.Source.MaintainResources(Time.unscaledTime + 3f);
            Check(!held.IsCreated(), "an unused preview releases its owned eye resource through bounded maintenance");
            scene.CaptureTarget(new Vector3(0, 0, -1.2f), false, new Vector2(100000, 100000));
            Check(scene.LastTarget.width == 2048, "unbounded close-up demand remains hard-capped");
            scene.CaptureTarget(new Vector3(0, 0, -1.2f), true, new Vector2(100000, 100000));
            scene.CheckPool();
            scene.CheckStableCalibration();
            Check(!effect || scene.Effect!.Draws >= 6, "native LDR source effect actually executes for the adjusted presentation");
        }
        return _checks;
    }
    private static Vector2 Centroid(Texture2D image, int channel)
    {
        Color32[] pixels = image.GetPixels32(); double weight = 0, x = 0, y = 0;
        for (int iy = 0; iy < image.height; iy++) for (int ix = 0; ix < image.width; ix++)
        {
            Color32 p = pixels[iy * image.width + ix];
            int own = channel == 0 ? p.r : p.g, other = channel == 0 ? Math.Max(p.g, p.b) : Math.Max(p.r, p.b);
            if (own < 100 || own < other * 2) continue;
            weight += own; x += (ix + .5) * own; y += (iy + .5) * own;
        }
        Check(weight > 255 * 10, "native near-pane landmark remains visible in the final actual head raster");
        return new Vector2((float)(x / weight / image.width), (float)(y / weight / image.height));
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Camera NativeCamera, Head;
        internal readonly MenuDepthCharacterSource Source;
        internal readonly CharacterNativeImageEffect? Effect;
        internal readonly float Width, Height, Aperture;
        internal readonly Vector3 Scale;
        internal bool Invariant = true;
        internal RenderTexture LastTarget = null!;
        private readonly RenderTexture _native, _final;
        private readonly RawImage _image;
        private readonly RectTransform _root;
        private readonly Character3DDisplayManager _manager;
        private readonly Transform _holder, _body, _bone, _light;
        private readonly Material _pattern, _red, _green;
        private readonly Mesh _skin;
        private readonly StreamWriter _log;
        private readonly string _output;
        private readonly List<MenuDepthCharacterSource> _extra = new List<MenuDepthCharacterSource>();
        private Vector3 _sampledBone;
        private Vector3 _center => new Vector3(Envelope.center.x, Envelope.center.y, Envelope.min.z + .035f * Aperture * Scale.z);
        internal Scene(string output, bool effect)
        {
            _output = output; _log = new StreamWriter(Path.Combine(output, "presentation-and-samples.csv"));
            _native = new RenderTexture(1024, 1024, 24); _native.Create(); _final = new RenderTexture(2048, 1536, 24); _final.Create();
            NativeCamera = new GameObject("native1024 LDR preview camera").AddComponent<Camera>();
            NativeCamera.targetTexture = _native; NativeCamera.allowHDR = false; NativeCamera.allowMSAA = false;
            NativeCamera.clearFlags = CameraClearFlags.SolidColor; NativeCamera.backgroundColor = Color.clear;
            NativeCamera.fieldOfView = 30; NativeCamera.aspect = 1; NativeCamera.nearClipPlane = .1f; NativeCamera.farClipPlane = 10;
            NativeCamera.cullingMask = 1 << 8; NativeCamera.stereoTargetEye = StereoTargetEyeMask.None;
            if (effect) NativeCamera.rect = new Rect(.1f, .05f, .8f, .9f);
            _holder = new GameObject("native authored camera-child holder").transform; _holder.SetParent(NativeCamera.transform, false);
            _holder.localPosition = new Vector3(.4f, -.45f, 2.56f); _holder.localRotation = Quaternion.Euler(0, 164, 0);
            _manager = NativeCamera.gameObject.AddComponent<Character3DDisplayManager>(); _manager.Bind(_holder);
            _pattern = new Material(Shader.Find("Hidden/GloomhavenVR/MenuDepthCharacterFrequency"));
            _red = new Material(Shader.Find("Unlit/Color")); _red.color = Color.red;
            _green = new Material(Shader.Find("Unlit/Color")); _green.color = Color.green;
            _body = new GameObject("actual skinned native body").transform; _body.SetParent(_holder, false);
            _body.position = new Vector3(.4f, -.1f, 2.56f); _body.rotation = Quaternion.identity; _body.gameObject.layer = 8;
            _bone = new GameObject("authored native animation bone").transform; _bone.SetParent(_body, false);
            _skin = new Mesh { name = "authored skinned local envelope" };
            _skin.vertices = new[] { new Vector3(-.45f,-.9f,0), new Vector3(-.45f,.9f,0), new Vector3(.45f,.9f,0), new Vector3(.45f,-.9f,0) };
            _skin.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
            _skin.triangles = new[] { 0,1,2,0,2,3 }; _skin.RecalculateNormals(); _skin.RecalculateBounds();
            _skin.bindposes = new[] { Matrix4x4.identity }; var weights = new BoneWeight[4];
            for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            _skin.boneWeights = weights;
            SkinnedMeshRenderer skinned = _body.gameObject.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = _skin; skinned.bones = new[] { _bone }; skinned.rootBone = _bone; skinned.sharedMaterial = _pattern;
            skinned.localBounds = new Bounds(Vector3.zero, new Vector3(.9f, 1.8f, .08f));
            var equipment = GameObject.CreatePrimitive(PrimitiveType.Quad); equipment.name = "rigid equipment on animated bone"; equipment.layer = 8;
            equipment.transform.SetParent(_bone, false); equipment.transform.localPosition = new Vector3(0, .7f, 0);
            equipment.transform.localScale = Vector3.one * .1f; equipment.GetComponent<MeshRenderer>().sharedMaterial = _pattern;
            Transform frontMarker = Marker("front figure detail", Front, _red), backMarker = Marker("back companion detail", Back, _green);
            _manager.ReplaceOriginal(_body, frontMarker, backMarker);
            var unusedSkin = GameObject.CreatePrimitive(PrimitiveType.Cube); unusedSkin.name = "inactive cached wardrobe variant";
            unusedSkin.layer = 8; unusedSkin.transform.SetParent(_body, false); unusedSkin.transform.localScale = Vector3.one * 100;
            unusedSkin.SetActive(false);
            var other = GameObject.CreatePrimitive(PrimitiveType.Cube); other.name = "unrelated non-model holder content";
            other.transform.SetParent(_holder, false); other.transform.localScale = Vector3.one * 100;
            // Layer0 is outside both native preview and final UI culling masks.
            // Completed native membership excludes it from authored calibration.
            _light = new GameObject("native child light").transform; _light.SetParent(_holder, false); _light.localPosition = new Vector3(0, 1, -.4f);
            _light.gameObject.AddComponent<Light>().type = LightType.Point;
            var window = new GameObject("rotated shared physical window", typeof(RectTransform), typeof(Canvas)); window.layer = 5;
            _root = (RectTransform)window.transform; _root.sizeDelta = new Vector2(1200, 900);
            _root.position = new Vector3(1, .4f, 2); _root.rotation = Quaternion.Euler(-12, 31, 7); _root.localScale = new Vector3(.0013f, .00085f, .001f);
            window.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var image = new GameObject("native RawImage consumer", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            image.layer = 5; image.transform.SetParent(_root, false); _image = image.GetComponent<RawImage>();
            _image.rectTransform.sizeDelta = new Vector2(1200, 900); _image.texture = _native;
            if (effect) _image.uvRect = new Rect(.1f, .15f, .7f, .7f);
            Canvas.ForceUpdateCanvases();
            Width = 1.2f * 1.3f; Height = .9f * .85f; Aperture = Mathf.Min(Width, Height);
            float n = Mathf.Max(Envelope.size.x / (.82f * Width), Envelope.size.y / (.75f * Height));
            Scale = new Vector3(n, n, Mathf.Max(n, Envelope.size.z / (.14f * Aperture)));
            Head = new GameObject("actual physical perspective head").AddComponent<Camera>(); Head.enabled = false;
            Head.targetTexture = _final; Head.fieldOfView = 60; Head.aspect = 4f / 3; Head.nearClipPlane = .01f; Head.farClipPlane = 20;
            Head.allowHDR = false; Head.allowMSAA = false; Head.cullingMask = 1 << 5; Head.backgroundColor = Color.black;
            Head.clearFlags = CameraClearFlags.SolidColor; Head.stereoTargetEye = StereoTargetEyeMask.None;
            if (effect)
            {
                Effect = NativeCamera.gameObject.AddComponent<CharacterNativeImageEffect>();
                Vector3 holder = _holder.position, body = _body.position, light = _light.position;
                Effect.BeforeBlit = () => Invariant &= NativeCamera.transform.position == Vector3.zero && _holder.position == holder
                    && _body.position == body && _light.position == light && _bone.localPosition == _sampledBone;
            }
            Source = new MenuDepthCharacterSource(_image, NativeCamera, _manager, _holder, m => m.Hidden);
            MenuDepthSources.Register(Source);
            // Seed the completed original at a deliberately different animation
            // phase. Rigid equipment must use authored bind-pose membership,
            // while the original real render still sees that animated pose.
            _bone.localPosition = _sampledBone = new Vector3(.9f, 0, 0);
            CaptureTarget(new Vector3(0, 0, -1.2f), false, new Vector2(1024, 1024));
            _bone.localPosition = _sampledBone = Vector3.zero;
        }
        private Transform Marker(string name, Vector3 point, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad); marker.name = name; marker.layer = 8;
            marker.transform.SetParent(_holder, false); marker.transform.position = point; marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * .08f; marker.GetComponent<MeshRenderer>().sharedMaterial = material;
            return marker.transform;
        }
        internal Vector3 Map(Vector3 native) { Vector3 d = native - _center; return new Vector3(d.x / Scale.x, d.y / Scale.y, d.z / Scale.z); }
        private Vector3 World(Vector3 local) => _root.position + _root.right * local.x + _root.up * local.y + _root.forward * local.z;
        internal Vector2 Project(Vector3 eye, Vector3 point)
        {
            Vector3 delta = point - eye; float f = 1 / Mathf.Tan(30 * Mathf.Deg2Rad);
            return new Vector2(.5f + delta.x / delta.z * f / Head.aspect * .5f, .5f + delta.y / delta.z * f * .5f);
        }
        internal Vector3 Reconstruct(Vector3 leftEye, Vector2 left, Vector2 right)
        {
            float f = 1 / Mathf.Tan(30 * Mathf.Deg2Rad), dx = (left.x - right.x) * 2 * Head.aspect / f;
            float distance = .064f / dx;
            return leftEye + new Vector3((left.x - .5f) * 2 * Head.aspect / f, (left.y - .5f) * 2 / f, 1) * distance;
        }
        internal void CaptureTarget(Vector3 localEye, bool right, Vector2 footprint)
        {
            MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(right, World(localEye), Head, false, footprint));
            try { LastTarget = (RenderTexture)_image.texture; Check(LastTarget.IsCreated(), "actual owned eye target is created before sampling"); }
            finally { MenuDepthSources.AfterCapture(); }
        }
        internal Texture2D Capture(Vector3 localEye, bool right, Vector2 footprint, int frequency, string name)
        {
            _pattern.SetFloat("_Frequency", frequency);
            Head.transform.SetPositionAndRotation(World(localEye), _root.rotation);
            MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(right, World(localEye), Head, false, footprint));
            try
            {
                LastTarget = (RenderTexture)_image.texture; Head.Render();
                RenderTexture old = RenderTexture.active; RenderTexture.active = _final;
                var pixels = new Texture2D(_final.width, _final.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply(); RenderTexture.active = old;
                File.WriteAllBytes(Path.Combine(_output, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + ".png"), pixels.EncodeToPNG()); return pixels;
            }
            finally { MenuDepthSources.AfterCapture(); }
        }
        internal double FrequencyCorrelation(Texture2D image, float distance, int frequency)
        {
            Color32[] pixels = image.GetPixels32(); double a = 0, b = 0, aa = 0, bb = 0, ab = 0; int count = 0;
            Vector3 body = Map(new Vector3(.4f, -.1f, 2.56f));
            float physicalHalfHeight = .9f / Scale.y, physicalHalfWidth = .45f / Scale.x;
            float f = 1 / Mathf.Tan(30 * Mathf.Deg2Rad);
            for (int y = 0; y < image.height; y++) for (int x = image.width / 3; x < image.width * 2 / 3; x++)
            {
                float px = ((x + .5f) / image.width - .5f) * 2 * Head.aspect / f * (distance + body.z);
                float py = ((y + .5f) / image.height - .5f) * 2 / f * (distance + body.z);
                if (Mathf.Abs(px - body.x) > physicalHalfWidth * .7f || Mathf.Abs(py - body.y) > physicalHalfHeight * .75f) continue;
                Color32 color = pixels[y * image.width + x];
                if (Math.Abs(color.r - color.g) > 8 || Math.Abs(color.g - color.b) > 8) continue;
                double uv = (py - body.y) / (physicalHalfHeight * 2) + .5;
                double expected = Math.Sin(2 * Math.PI * frequency * uv), observed = color.r / 255.0;
                a += expected; b += observed; aa += expected * expected; bb += observed * observed; ab += expected * observed; count++;
            }
            Check(count > 1000, "independent ray intersection samples the actual visible authored skinned pattern");
            return (ab - a * b / count) / Math.Sqrt((aa - a * a / count) * (bb - b * b / count));
        }
        internal void Restored()
        {
            Check(ReferenceEquals(_image.texture, _native) && ReferenceEquals(NativeCamera.targetTexture, _native),
                "adjusted high-resolution capture restores both original native and UI texture targets");
            Check(NativeCamera.transform.position == Vector3.zero && NativeCamera.transform.rotation == Quaternion.identity,
                "adjusted cabinet rendering never moves the actual camera-child scene");
        }
        internal void CheckPool()
        {
            long actual = LastTarget.width * (long)LastTarget.height * 16;
            for (int i = 0; i < 2; i++)
            {
                var image = new GameObject("additional known preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                image.transform.SetParent(_root, false); var graphic = image.GetComponent<RawImage>(); graphic.rectTransform.sizeDelta = new Vector2(1200,900); graphic.texture = _native;
                var source = new MenuDepthCharacterSource(graphic, NativeCamera, _manager, _holder, m => m.Hidden); _extra.Add(source);
                for (int eye = 0; eye < 2; eye++)
                {
                    source.BeforeCapture(new MenuDepthEye(eye != 0, World(new Vector3(0, 0, -1.2f)), Head, false, new Vector2(10000,10000)));
                    if (eye == 0) { var target = (RenderTexture)graphic.texture; actual += target.width * (long)target.height * 16; }
                    source.AfterCapture();
                }
            }
            Check(actual <= 96L * 1024 * 1024, "simultaneous actual eye pairs stay inside the shared96MiB budget");
        }
        internal void CheckStableCalibration()
        {
            // This explicit maintenance clock input releases idle resources;
            // subsequent allocations and real GPU resources remain production.
            Source.MaintainResources(Time.unscaledTime + 3f);
            CaptureTarget(new Vector3(0,0,-1.2f), false, new Vector2(512,512)); RenderTexture small = LastTarget;
            _bone.localPosition = _sampledBone = new Vector3(0, .04f, 0);
            CaptureTarget(new Vector3(.1f,.1f,-1.1f), false, new Vector2(512,512));
            Check(ReferenceEquals(small, LastTarget), "native bone animation and head movement never recalibrate or resize a stable preview");
            _bone.localPosition = _sampledBone = Vector3.zero;
            _manager.ReplaceOriginal(_body);
            CaptureTarget(new Vector3(0,0,-1.2f), false, new Vector2(512,512));
            Check(LastTarget.IsCreated(), "a newly completed native model identity receives its own stable calibration");
        }
        internal void Log(string message) { _log.WriteLine(message); _log.Flush(); }
        public void Dispose()
        {
            MenuDepthSources.AfterCapture(); MenuDepthSources.Unregister(Source); Source.Dispose();
            foreach (MenuDepthCharacterSource source in _extra) source.Dispose();
            _log.Dispose(); _native.Release(); _final.Release(); Object.DestroyImmediate(_native); Object.DestroyImmediate(_final);
            Object.DestroyImmediate(_pattern); Object.DestroyImmediate(_red); Object.DestroyImmediate(_green); Object.DestroyImmediate(_skin);
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
        }
    }
}
