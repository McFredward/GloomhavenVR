using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Native camera->holder->models/lights hierarchy and scene geometry are explicit
// inputs. Pixel predictions use independent pinhole rays and fixed metric scene
// points, never the production View/Projection matrices as their expected result.
public static class MenuDepthCharacterWindowProof
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private const float Pane = 2.56f * .85f;
    private static readonly float NativeSize = 2 * Pane * Mathf.Tan(15 * Mathf.Deg2Rad);
    // Keep every complete marker patch inside the authored crop for all poses;
    // a clipped patch's centroid is not its projected geometric centre.
    private static readonly Vector3[] Points = { new Vector3(.1f, -.25f, 2.56f), new Vector3(.05f, .25f, 2.56f), new Vector3(.3f, .1f, 3f) };
    private static readonly Vector3 EyeProbe = new Vector3(-.35f, .15f, 2.75f);

    private static Texture2D Read(RenderTexture target, string file)
    {
        RenderTexture old = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = old;
        File.WriteAllBytes(file, pixels.EncodeToPNG()); return pixels;
    }
    private static Vector2 Centroid(Texture2D image, int channel)
    {
        Color32[] pixels = image.GetPixels32(); double total = 0, xMoment = 0, yMoment = 0;
        for (int y = 0; y < image.height; y++) for (int x = 0; x < image.width; x++)
        {
            Color32 p = pixels[y * image.width + x];
            float own = channel == 0 ? p.r : channel == 1 ? p.g : p.b;
            float other = channel == 0 ? Math.Max(p.g, p.b) : channel == 1 ? Math.Max(p.r, p.b) : Math.Max(p.r, p.g);
            if (own < 80 || own < other * 2) continue;
            total += own; xMoment += (x + .5) * own; yMoment += (y + .5) * own;
        }
        Check(total > 255 * 10, "native scene landmark remains visible in the actual eye raster");
        return new Vector2((float)(xMoment / total / image.width), (float)(yMoment / total / image.height));
    }

    public static int GlassCabinet(string output)
        => RunCabinet(output, false);
    public static int TargetViewport(string output)
        => RunCabinet(output, true);
    private static int RunCabinet(string output, bool ignoredViewports)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var scene = new Scene(output, ignoredViewports))
        {
            var motions = new[] { Vector3.zero, new Vector3(.22f, 0, 0), new Vector3(0, .13f, 0), new Vector3(0, 0, .32f), new Vector3(-.16f, -.08f, -.2f) };
            foreach (Consumer consumer in scene.Consumers)
            {
                Vector3[] fixedLocal = new Vector3[Points.Length];
                for (int i = 0; i < Points.Length; i++) fixedLocal[i] = consumer.SceneToPhysical(Points[i]);
                for (int motion = 0; motion < motions.Length; motion++)
                {
                    scene.SampleNativeAnimation(motion * .17f);
                    Vector3 center = consumer.Neutral + motions[motion];
                    Vector3[] eyes = { center - Vector3.right * .032f, center + Vector3.right * .032f };
                    var observed = new Vector2[2, Points.Length];
                    for (int eye = 0; eye < 2; eye++)
                    {
                        Texture2D pixels = scene.Capture(consumer, eyes[eye], eye != 0, motion);
                        for (int i = 0; i < Points.Length; i++)
                        {
                            observed[eye, i] = Centroid(pixels, i);
                            Vector2 expected = consumer.Project(eyes[eye], fixedLocal[i]);
                            float error = Vector2.Distance(Vector2.Scale(observed[eye, i] - expected, new Vector2(pixels.width, pixels.height)), Vector2.zero);
                            scene.Log(consumer.Name + "," + motion + "," + eye + "," + i + "," + observed[eye, i].x + "," + observed[eye, i].y + "," + expected.x + "," + expected.y + "," + error);
                            Check(error < 1.05f, "actual eye raster projects fixed cabinet landmarks from independently authored window-relative 3D");
                        }
                        Vector2 probe = consumer.Project(eyes[eye], consumer.SceneToPhysical(EyeProbe));
                        Color eyeUniform = pixels.GetPixel((int)(probe.x * pixels.width), (int)(probe.y * pixels.height));
                        Vector3 expectedEye = consumer.NativeEye(eyes[eye]);
                        scene.Log("shader-eye," + consumer.Name + "," + motion + "," + eye + "," + eyeUniform + ",expected-eye," + expectedEye);
                        Check(Mathf.Abs(eyeUniform.r - (expectedEye.x * .1f + .5f)) < .012f
                            && Mathf.Abs(eyeUniform.g - (expectedEye.y * .1f + .5f)) < .012f
                            && Mathf.Abs(eyeUniform.b - (expectedEye.z * .1f + .5f)) < .012f,
                            "actual GPU world-space camera uniform follows the borrowed eye while the native scene stays fixed");
                        Object.DestroyImmediate(pixels);
                        Check(scene.InvariantDuringRender, "native camera holder lights and models stay fixed during original render callbacks");
                    }
                    for (int i = 0; i < Points.Length; i++)
                    {
                        Vector3 reconstructed = consumer.Reconstruct(eyes[0], observed[0, i], observed[1, i]);
                        scene.Log("reconstructed," + consumer.Name + "," + motion + "," + i + "," + reconstructed + ",expected," + fixedLocal[i]);
                        Check(Vector3.Distance(reconstructed, fixedLocal[i]) < .045f,
                            "rendered stereo rays reconstruct the same fixed window-relative cabinet scene across head motion");
                    }
                    scene.CheckNativeAnimation();
                }
            }
            Check(scene.Effect.Draws == scene.Consumers.Count * motions.Length * 2,
                "every eye renders through the actual original camera-hosted effect exactly once");
            scene.CheckRestored();
        }
        return _checks;
    }

    private sealed class Consumer
    {
        internal readonly string Name;
        internal readonly RectTransform Root;
        internal readonly RawImage Image;
        internal readonly Transform Frame;
        internal readonly Camera UiCamera;
        internal readonly bool Desktop;
        internal readonly float Width, Height;
        private readonly Rect _nativeViewport, _uiViewport;
        internal Consumer(string name, RectTransform root, RawImage image, Transform frame, Camera camera, bool desktop, Rect nativeViewport)
        {
            Name = name; Root = root; Image = image; Frame = frame; UiCamera = camera; Desktop = desktop;
            _nativeViewport = nativeViewport; _uiViewport = desktop ? camera.rect : new Rect(0, 0, 1, 1);
            Width = desktop ? frame.TransformVector(Vector3.right).magnitude : root.rect.width * root.TransformVector(Vector3.right).magnitude;
            Height = desktop ? frame.TransformVector(Vector3.up).magnitude : root.rect.height * root.TransformVector(Vector3.up).magnitude;
        }
        private Vector3 Scale
        {
            get
            {
                float x = NativeSize * Image.uvRect.width / _nativeViewport.width / (Width * _uiViewport.width);
                float y = NativeSize * Image.uvRect.height / _nativeViewport.height / (Height * _uiViewport.height);
                return new Vector3(x, y, Mathf.Sqrt(x * y));
            }
        }
        private Vector3 Offset => new Vector3((_uiViewport.center.x - .5f) * Width, (_uiViewport.center.y - .5f) * Height, 0);
        private Vector3 CropCenter => new Vector3(((Image.uvRect.center.x - _nativeViewport.x) / _nativeViewport.width - .5f) * NativeSize,
            ((Image.uvRect.center.y - _nativeViewport.y) / _nativeViewport.height - .5f) * NativeSize, Pane);
        internal Vector3 SceneToPhysical(Vector3 scene) { Vector3 delta = scene - CropCenter, scale = Scale; return Offset + new Vector3(delta.x / scale.x, delta.y / scale.y, delta.z / scale.z); }
        internal Vector3 NativeEye(Vector3 physicalEye) => CropCenter + Vector3.Scale(physicalEye - Offset, Scale);
        internal Vector3 Neutral => SceneToPhysical(Vector3.zero);
        internal Vector3 World(Vector3 local) => Frame.position + Frame.right * local.x + Frame.up * local.y + Frame.forward * local.z;
        internal Vector2 Project(Vector3 eye, Vector3 fixedPoint)
        {
            float t = -eye.z / (fixedPoint.z - eye.z);
            Vector3 glass = eye + (fixedPoint - eye) * t;
            return new Vector2(glass.x / Width + .5f, glass.y / Height + .5f);
        }
        internal Vector3 Reconstruct(Vector3 leftEye, Vector2 left, Vector2 right)
        {
            double disparity = (right.x - left.x) * Width;
            double z = disparity * -leftEye.z / (.064 - disparity);
            double t = (z - leftEye.z) / -leftEye.z;
            return new Vector3((float)(leftEye.x + ((left.x - .5) * Width - leftEye.x) * t),
                (float)(leftEye.y + ((left.y - .5) * Height - leftEye.y) * t), (float)z);
        }
    }

    private sealed class Scene : IDisposable
    {
        internal readonly List<Consumer> Consumers = new List<Consumer>();
        internal readonly Camera Source;
        internal readonly Transform Holder, Light;
        internal readonly CharacterNativeImageEffect Effect;
        internal readonly RenderTexture Native, Ui;
        internal bool InvariantDuringRender = true;
        private readonly List<Transform> _markers = new List<Transform>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly StreamWriter _log;
        private readonly string _output;
        private readonly Vector3 _holderPosition;
        private readonly Animation _animation;
        private readonly AnimationClip _clip;
        private readonly Transform _animatedBody;
        private Vector3 _sampledBody;
        private float _sampledTime;
        private readonly bool _ignoredViewports;

        internal Scene(string output, bool ignoredViewports)
        {
            _ignoredViewports = ignoredViewports;
            _output = output; _log = new StreamWriter(Path.Combine(output, "independent-landmarks.csv"));
            // Four times the earlier raster resolution is needed for metric
            // reconstruction: a subpixel stereo disparity at shallow depth can
            // otherwise exceed45mm despite an accurate subpixel image projection.
            Native = new RenderTexture(3072, 3072, 24); Native.Create(); Ui = new RenderTexture(3072, 2304, 24); Ui.Create();
            Source = new GameObject("native GUI3D camera with real child scene").AddComponent<Camera>();
            Source.stereoTargetEye = StereoTargetEyeMask.None; Source.backgroundColor = Color.clear; Source.clearFlags = CameraClearFlags.SolidColor;
            Source.fieldOfView = 30; Source.aspect = 1; Source.nearClipPlane = .1f; Source.farClipPlane = 10;
            Source.cullingMask = 1 << 8; Source.targetTexture = Native; Source.allowHDR = false; Source.allowMSAA = false;
            if (ignoredViewports) Source.rect = new Rect(.1f, .05f, .8f, .9f);
            Holder = new GameObject("native serialized holder child").transform; Holder.SetParent(Source.transform, false);
            Holder.localPosition = new Vector3(.4f, -.45f, 2.56f); Holder.localRotation = Quaternion.Euler(0, 164, 0);
            _holderPosition = Holder.position;
            var manager = Source.gameObject.AddComponent<Character3DDisplayManager>(); manager.Bind(Holder);
            Light = new GameObject("native authored light under holder").transform; Light.SetParent(Holder, false); Light.localPosition = new Vector3(.3f, 1, -.5f);
            Light.gameObject.AddComponent<Light>().type = LightType.Point;
            for (int i = 0; i < Points.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Quad); marker.layer = 8; marker.transform.SetParent(Holder, false);
                marker.transform.position = Points[i]; marker.transform.rotation = Quaternion.identity; marker.transform.localScale = Vector3.one * .08f;
                var material = new Material(Shader.Find("Unlit/Color")); material.color = i == 0 ? Color.red : i == 1 ? Color.green : Color.blue;
                _materials.Add(material); marker.GetComponent<MeshRenderer>().sharedMaterial = material; _markers.Add(marker.transform);
            }
            var eyeProbe = GameObject.CreatePrimitive(PrimitiveType.Quad); eyeProbe.layer = 8; eyeProbe.transform.SetParent(Holder, false);
            eyeProbe.transform.position = EyeProbe; eyeProbe.transform.rotation = Quaternion.identity; eyeProbe.transform.localScale = Vector3.one * .1f;
            var eyeMaterial = new Material(Shader.Find("Hidden/GloomhavenVR/MenuDepthCharacterEyeProbe"));
            _materials.Add(eyeMaterial); eyeProbe.GetComponent<MeshRenderer>().sharedMaterial = eyeMaterial;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.layer = 8; body.transform.SetParent(Holder, false);
            _animatedBody = body.transform; _animatedBody.localPosition = new Vector3(.25f, .25f, .2f); _animatedBody.localScale = new Vector3(.035f, .1f, .035f);
            var bodyMaterial = new Material(Shader.Find("Unlit/Color")); bodyMaterial.color = Color.yellow;
            _materials.Add(bodyMaterial); body.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            _animation = body.AddComponent<Animation>(); _clip = new AnimationClip { legacy = true };
            _clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, .25f, 1, .3f));
            _animation.AddClip(_clip, "NativeIdle"); _animation["NativeIdle"].enabled = true; _animation["NativeIdle"].weight = 1;
            SampleNativeAnimation(0);
            Vector3 lightPosition = Light.position;
            Effect = Source.gameObject.AddComponent<CharacterNativeImageEffect>();
            Effect.BeforeBlit = () =>
            {
                InvariantDuringRender &= Source.transform.position == Vector3.zero && Source.transform.rotation == Quaternion.identity
                    && Holder.position == _holderPosition && Light.position == lightPosition;
                InvariantDuringRender &= Vector3.Distance(_animatedBody.position, _sampledBody) < .00001f;
                for (int i = 0; i < Points.Length; i++) InvariantDuringRender &= Vector3.Distance(_markers[i].position, Points[i]) < .00001f;
            };
            AddConsumer("converted", false, Vector3.zero, 0, 1, new Rect(0, 0, 1, 1));
            AddConsumer("rotated-scaled-cropped", false, new Vector3(1, .4f, 2), 31, 1.3f, new Rect(.1f, .15f, .75f, .7f));
            AddConsumer("native-screen-cropped", true, new Vector3(-.7f, .3f, 1.3f), -23, .85f, new Rect(.1f, .15f, .75f, .7f));
            Canvas.ForceUpdateCanvases(); MenuDepthCharacters.Tick();
        }
        private void AddConsumer(string name, bool desktop, Vector3 position, float yaw, float size, Rect crop)
        {
            var camera = new GameObject(name + " original UI camera").AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << (5 + Consumers.Count); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear; camera.stereoTargetEye = StereoTargetEyeMask.None; camera.targetTexture = Ui;
            camera.allowHDR = false; camera.allowMSAA = false;
            var rootGo = new GameObject(name, typeof(RectTransform), typeof(Canvas)); rootGo.layer = 5 + Consumers.Count;
            var root = (RectTransform)rootGo.transform; var canvas = rootGo.GetComponent<Canvas>(); Transform frame = root;
            if (desktop)
            {
                camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 4f / 3f; camera.transform.position = new Vector3(50, 30, 90);
                if (_ignoredViewports) camera.rect = new Rect(.08f, .12f, .83f, .76f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 7;
                frame = new GameObject(name + " physical glass").transform; frame.position = position; frame.rotation = Quaternion.Euler(0, yaw, 0);
                frame.localScale = new Vector3(1.2f * size, .9f * size, 1);
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace; root.sizeDelta = new Vector2(1200, 900); root.localScale = Vector3.one * .001f * size;
                root.position = position; root.rotation = Quaternion.Euler(0, yaw, 0);
            }
            var imageGo = new GameObject(name + " actual preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageGo.layer = rootGo.layer; imageGo.transform.SetParent(root, false); var image = imageGo.GetComponent<RawImage>();
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one; image.rectTransform.sizeDelta = Vector2.zero;
            image.texture = Native; image.uvRect = crop; Canvas.ForceUpdateCanvases();
            Consumers.Add(new Consumer(name, root, image, frame, camera, desktop, Source.rect));
        }
        internal Texture2D Capture(Consumer consumer, Vector3 localEye, bool right, int motion)
        {
            Vector3 worldEye = consumer.World(localEye), nativeEye = worldEye;
            if (consumer.Desktop)
                Check(MenuDepthScreenProjection.TryMap(consumer.Frame, consumer.UiCamera, consumer.Image.rectTransform, worldEye, out nativeEye),
                    "actual native desktop mapper admits the physical eye for its original cropped preview");
            MenuDepthSources.BeforeCapture(consumer.Root, new MenuDepthEye(right, nativeEye, consumer.UiCamera));
            try
            {
                if (!consumer.Desktop)
                {
                    Check(MenuDepthProjection.TryBuild(consumer.Root, worldEye, out Matrix4x4 view, out Matrix4x4 projection), "actual private window camera admits the independent eye");
                    consumer.UiCamera.worldToCameraMatrix = view; consumer.UiCamera.projectionMatrix = projection;
                    consumer.UiCamera.cullingMatrix = projection * view;
                }
                Canvas.ForceUpdateCanvases(); consumer.UiCamera.Render();
                return Read(Ui, Path.Combine(_output, consumer.Name + "-" + motion + "-" + (right ? "right" : "left") + ".png"));
            }
            finally { MenuDepthSources.AfterCapture(); }
        }
        internal void Log(string text) { _log.WriteLine(text); _log.Flush(); }
        internal void SampleNativeAnimation(float time)
        {
            _animation["NativeIdle"].time = time; _animation.Sample(); _sampledTime = _animation["NativeIdle"].time;
            _sampledBody = _animatedBody.position;
        }
        internal void CheckNativeAnimation() => Check(Mathf.Abs(_animation["NativeIdle"].time - _sampledTime) < .0001f
            && Vector3.Distance(_animatedBody.position, _sampledBody) < .00001f,
            "both eye captures preserve the one actual native Animation.Sample pose and clock");
        internal void CheckRestored()
        {
            Check(Source.transform.position == Vector3.zero && Source.transform.rotation == Quaternion.identity && Holder.position == _holderPosition,
                "original camera and its real descendant model scene retain their authored world pose");
            Check(Source.targetTexture == Native && Native.IsCreated(), "eye capture retains the original native render target");
            foreach (Consumer consumer in Consumers) Check(consumer.Image.texture == Native, "each original RawImage returns to its original shared texture");
        }
        public void Dispose()
        {
            MenuDepthSources.AfterCapture(); MenuDepthCharacters.Shutdown(); _log.Dispose();
            foreach (Material material in _materials) Object.DestroyImmediate(material);
            Object.DestroyImmediate(_clip);
            Native.Release(); Ui.Release(); Object.DestroyImmediate(Native); Object.DestroyImmediate(Ui);
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true)) Object.DestroyImmediate(go);
        }
    }
}
