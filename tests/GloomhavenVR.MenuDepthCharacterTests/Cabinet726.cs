using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Native ground roots/body dimensions, original model membership and XR eyes are
// explicit inputs. The actual automatic direct Canvas/head path and shipping
// supersampled Display path render production Source/Projection on the GPU.
public static class MenuDepthCharacterCabinet726
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    public static IEnumerator CommonCabinet726(string output) => Run(output, false);
    public static IEnumerator DesktopCabinet726(string output) => Run(output, true);
    public static int RearThrow726(string output)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var scene = new Scene(output, 0))
        { scene.SetActor(new Vector3(.23f,-.676f,3.036f),1.233132f,false); scene.ThrowingRear(); }
        return _checks;
    }
    private static IEnumerator Run(string output, bool desktopOnly)
    {
        _checks = 0; VRSession.IsRunning = true;
        // Native corpus examples, deliberately different original camera-fit
        // translations. Their dimensions are authored inputs, never fitted output.
        float[] heights = { 1.233132f, 2.257989f, 1.233132f };
        Vector3[] placements = { new Vector3(.23f, -.676f, 3.036f), new Vector3(.21f, -1.17f, 3.94f), new Vector3(-.28f, -.76f, 3.885f) };
        Vector3[] poses = { new Vector3(0, 0, -1.7f), new Vector3(0, 0, 1.7f),
            new Vector3(.28f, .13f, -1.45f), new Vector3(-.26f, -.10f, 1.55f) };
        foreach (int path in desktopOnly ? new[] { 2 } : new[] { 0, 1 })
        {
            using (var scene = new Scene(output, path))
            {
                var frontalHeights = new float[heights.Length];
                for (int model = 0; model < heights.Length; model++)
                {
                    scene.SetActor(placements[model], heights[model], model == 2);
                    for (int pose = 0; pose < poses.Length; pose++)
                    {
                        yield return null; // Actual Canvas/head lifecycle gets a new Unity frame.
                        bool rear = poses[pose].z > 0;
                        for (int eye = 0; eye < 2; eye++)
                        {
                            Vector3 position = poses[pose] + Vector3.right * ((eye == 0 ? -.032f : .032f) * (rear ? -1 : 1));
                            Texture2D pixels = scene.Capture(position, eye != 0, model + "-" + pose + "-" + eye);
                            Sample actual = Measure(pixels, rear);
                            Vector3 native = placements[model] + new Vector3(0, heights[model] * .5f, rear ? .18f : -.18f);
                            Vector3 world = scene.World(native, placements[model]);
                            Vector3 expected = scene.Head.WorldToViewportPoint(world);
                            Vector2 error = new Vector2(actual.Center.x - expected.x, actual.Center.y - expected.y);
                            Check(Vector2.Scale(error, new Vector2(pixels.width, pixels.height)).magnitude < 2f,
                                "actual front and rear body pixels retain the fixed common main-actor axis across tracked stereo and opposite oblique eyes");
                            Vector3 top = scene.Head.WorldToViewportPoint(scene.World(placements[model]
                                + new Vector3(0, heights[model], rear ? .18f : -.18f), placements[model]));
                            Vector3 bottom = scene.Head.WorldToViewportPoint(scene.World(placements[model]
                                + new Vector3(0, 0, rear ? .18f : -.18f), placements[model]));
                            Check(Mathf.Abs(actual.Height - Mathf.Abs(top.y - bottom.y) * pixels.height) < 2.1f,
                                "actual body pixels preserve distinct native short and tall heights under one common unit scale");
                            Check(actual.OtherPixels < 5,
                                "rear window views render the actual native body back instead of a front image or mirrored front geometry");
                            if (pose == 0 && eye == 0) frontalHeights[model] = actual.Height;
                            scene.Restored(); Object.DestroyImmediate(pixels);
                        }
                    }
                }
                Check(Mathf.Abs(frontalHeights[1] / frontalHeights[0] - heights[1] / heights[0]) < .035f,
                    "rendered native tall-to-short body ratio survives character changes without individual fitting");
                Check(Mathf.Abs(frontalHeights[2] - frontalHeights[0]) < 1.1f,
                    "asymmetric equipment and companions never shrink or shift the same main body");
            }
        }
        yield return _checks;
    }

    private readonly struct Sample
    {
        internal readonly Vector2 Center;
        internal readonly float Height;
        internal readonly int OtherPixels;
        internal Sample(Vector2 center, float height, int other) { Center = center; Height = height; OtherPixels = other; }
    }
    private static Sample Measure(Texture2D image, bool rear)
    {
        Color32[] pixels = image.GetPixels32(); double weight = 0, x = 0, y = 0; int low = image.height, high = 0, other = 0;
        for (int iy = 0; iy < image.height; iy++) for (int ix = 0; ix < image.width; ix++)
        {
            Color32 p = pixels[iy * image.width + ix]; int own = rear ? p.g : p.r, opposite = rear ? p.r : p.g;
            if (opposite > 100 && opposite > own * 2 && opposite > p.b * 2) other++;
            if (own < 100 || own < opposite * 2 || own < p.b * 2) continue;
            weight++; x += ix + .5; y += iy + .5; low = Math.Min(low, iy); high = Math.Max(high, iy);
        }
        Check(weight > 100, "actual native front or rear body surface is visible in the final physical head raster");
        return new Sample(new Vector2((float)(x / weight / image.width), (float)(y / weight / image.height)), high - low + 1, other);
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Camera Head;
        private readonly Camera _nativeCamera, _ui;
        private readonly Character3DDisplayManager _manager;
        private readonly Transform _holder;
        private readonly RectTransform _root, _frame;
        private readonly Transform _screen;
        private readonly RawImage _image;
        private readonly MenuDepthCharacterSource _source;
        private readonly MenuDepthDisplay? _display;
        private readonly RenderTexture _native, _final, _capture;
        private readonly RenderTexture? _desktopRight;
        private readonly Material? _desktopMaterial;
        private readonly Material _front, _back, _side, _extra;
        private readonly Mesh _bodyMesh;
        private readonly string _output, _path;
        private readonly Camera.CameraCallback _selectEye;
        private Transform? _actor, _companion;
        private bool _right;
        private readonly bool _supersampled, _desktop;
        private bool _invariant = true;
        private Vector3 _actorPosition;
        private readonly Vector3 _holderPosition;
        private int _nativeDraws;
        internal Scene(string output, int path)
        {
            bool supersampled = path == 1; _desktop = path == 2;
            _output = output; _supersampled = supersampled; _path = _desktop ? "desktop" : supersampled ? "supersampled" : "direct";
            _native = Target(512, 512); _final = Target(720, 540); _capture = Target(1024, 768);
            _nativeCamera = new GameObject("original native character camera").AddComponent<Camera>();
            _nativeCamera.enabled = true; _nativeCamera.targetTexture = _native; _nativeCamera.cullingMask = 1 << 8;
            _nativeCamera.fieldOfView = 30; _nativeCamera.nearClipPlane = .1f; _nativeCamera.farClipPlane = 10;
            _nativeCamera.allowHDR = false; _nativeCamera.allowMSAA = false; _nativeCamera.backgroundColor = Color.clear;
            _nativeCamera.clearFlags = CameraClearFlags.SolidColor; _nativeCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _holder = new GameObject("fixed original camera-child holder").transform; _holder.SetParent(_nativeCamera.transform, false);
            _holder.localPosition = _holderPosition = new Vector3(.4f, -.45f, 2.56f);
            _manager = _nativeCamera.gameObject.AddComponent<Character3DDisplayManager>(); _manager.Bind(_holder);
            _front = Material(Color.red); _back = Material(Color.green); _side = Material(Color.gray); _extra = Material(Color.cyan);
            _bodyMesh = new Mesh { name = "closed native body with distinct actual front and rear surfaces" };
            var window = new GameObject("fixed rotated physical cabinet", typeof(RectTransform), typeof(Canvas)); window.layer = 5;
            _root = (RectTransform)window.transform; _root.sizeDelta = new Vector2(1200, 900); _root.localScale = Vector3.one * .001f;
            _root.SetPositionAndRotation(new Vector3(.8f, .2f, 2f), Quaternion.Euler(-7, 29, 4));
            window.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; _frame = _root; _screen = _root;
            _image = Image(_root, 5, _native); _source = new MenuDepthCharacterSource(_image, _nativeCamera, _manager, _holder, m => m.Hidden);
            MenuDepthSources.Register(_source);
            Head = new GameObject("actual final tracked perspective head").AddComponent<Camera>();
            Head.enabled = true; Head.targetTexture = _final; Head.fieldOfView = 55; Head.aspect = 4f / 3;
            Head.nearClipPlane = .01f; Head.farClipPlane = 20; Head.cullingMask = 1 << (path != 0 ? 6 : 5);
            Head.backgroundColor = Color.black; Head.clearFlags = CameraClearFlags.SolidColor; Head.allowHDR = false; Head.allowMSAA = false;
            Head.stereoTargetEye = StereoTargetEyeMask.None; VRRigDriver.HeadCamera = Head;
            _ui = new GameObject("unchanged orthographic supersample camera").AddComponent<Camera>(); _ui.enabled = false;
            _ui.transform.SetPositionAndRotation(_root.position - _root.forward * 5, _root.rotation);
            _ui.orthographic = true; _ui.orthographicSize = .45f; _ui.aspect = 4f / 3; _ui.targetTexture = _capture;
            _ui.nearClipPlane = .1f; _ui.farClipPlane = 10; _ui.cullingMask = 1 << 5;
            _ui.backgroundColor = Color.clear; _ui.clearFlags = CameraClearFlags.SolidColor; _ui.allowHDR = false; _ui.allowMSAA = false;
            if (path != 0)
            {
                var physical = new GameObject("actual supersampled display", typeof(RectTransform), typeof(Canvas)); physical.layer = 6;
                var rect = (RectTransform)physical.transform; rect.sizeDelta = _root.sizeDelta; rect.localScale = _root.localScale;
                rect.SetPositionAndRotation(_root.position, _root.rotation); physical.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                if (supersampled) _display = new MenuDepthDisplay(Image(rect, 6, _capture), _capture, _capture);
                else
                {
                    _frame = rect; _desktopRight = Target(1024, 768);
                    _screen = new GameObject("native desktop unit-quad glass frame").transform;
                    _screen.SetPositionAndRotation(rect.position,rect.rotation); _screen.localScale = new Vector3(1.2f,.9f,1f);
                    _desktopMaterial = MenuDepthDisplay.CreateDisplayMaterial(_capture, _desktopRight);
                    Image(rect, 6, _capture).material = _desktopMaterial;
                    _ui.transform.SetPositionAndRotation(new Vector3(50,30,90), Quaternion.identity);
                    Canvas canvas = window.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = _ui; canvas.planeDistance = 7;
                }
            }
            // Explicit active-XR-eye observation only. Both shipping UI eye
            // captures and this final shader selection still render real pixels.
            _selectEye = camera => { if (camera == Head && (_supersampled || _desktop)) Shader.SetGlobalFloat("_GvrMenuDepthEye", _right ? 1 : 0); };
            Camera.onPreRender += _selectEye;
            CharacterNativeImageEffect effect = _nativeCamera.gameObject.AddComponent<CharacterNativeImageEffect>();
            effect.BeforeBlit = () =>
            {
                _nativeDraws++;
                _invariant &= _nativeCamera.transform.position == Vector3.zero && _nativeCamera.transform.rotation == Quaternion.identity
                    && _holder.position == _holderPosition && _actor != null && _actor.position == _actorPosition;
            };
        }
        private static RenderTexture Target(int width, int height) { var target = new RenderTexture(width, height, 24); target.Create(); return target; }
        private static Material Material(Color color) { var material = new Material(Shader.Find("Unlit/Color")); material.color = color; return material; }
        private static RawImage Image(RectTransform root, int layer, Texture texture)
        {
            var go = new GameObject("original preview pixels", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)); go.layer = layer;
            go.transform.SetParent(root, false); var image = go.GetComponent<RawImage>(); image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.sizeDelta = Vector2.zero; image.texture = texture; return image;
        }
        internal void SetActor(Vector3 origin, float height, bool extras)
        {
            if (_actor != null) Object.DestroyImmediate(_actor.gameObject);
            if (_companion != null) Object.DestroyImmediate(_companion.gameObject);
            _actor = new GameObject("original main actor ground root").transform; _actor.SetParent(_holder, false); _actor.position = _actorPosition = origin;
            var body = new GameObject("original complete body", typeof(MeshFilter), typeof(MeshRenderer)); body.layer = 8; body.transform.SetParent(_actor, false);
            var vertices = new Vector3[24]; var colors = new[] { _front, _back, _side, _side, _side, _side };
            Vector3[] p = { new Vector3(-.225f,0,-.18f), new Vector3(-.225f,height,-.18f), new Vector3(.225f,height,-.18f), new Vector3(.225f,0,-.18f),
                new Vector3(-.225f,0,.18f), new Vector3(-.225f,height,.18f), new Vector3(.225f,height,.18f), new Vector3(.225f,0,.18f) };
            int[][] faces = { new[] { 0,1,2,3 }, new[] { 7,6,5,4 }, new[] { 4,5,1,0 }, new[] { 3,2,6,7 }, new[] { 1,5,6,2 }, new[] { 4,0,3,7 } };
            _bodyMesh.Clear(); for (int face = 0; face < 6; face++) for (int corner = 0; corner < 4; corner++) vertices[face * 4 + corner] = p[faces[face][corner]];
            _bodyMesh.vertices = vertices; _bodyMesh.subMeshCount = 6;
            for (int face = 0; face < 6; face++) { int start = face * 4; _bodyMesh.SetTriangles(new[] { start,start+1,start+2,start,start+2,start+3 }, face); }
            _bodyMesh.RecalculateNormals(); _bodyMesh.RecalculateBounds(); body.GetComponent<MeshFilter>().sharedMesh = _bodyMesh;
            body.GetComponent<MeshRenderer>().sharedMaterials = colors;
            _companion = new GameObject("native optional companion root").transform; _companion.SetParent(_holder, false);
            _companion.position = origin + new Vector3(.65f, .001f, .55f); _companion.gameObject.SetActive(extras);
            var companion = GameObject.CreatePrimitive(PrimitiveType.Cube); companion.layer = 8; companion.transform.SetParent(_companion, false);
            companion.transform.localPosition = new Vector3(0, .3f, 0); companion.transform.localScale = new Vector3(.25f,.6f,.3f);
            companion.GetComponent<MeshRenderer>().sharedMaterial = _extra;
            if (extras)
            {
                var equipment = GameObject.CreatePrimitive(PrimitiveType.Cube); equipment.layer = 8; equipment.transform.SetParent(_actor, false);
                equipment.transform.localPosition = new Vector3(-.7f,.6f,.05f); equipment.transform.localScale = new Vector3(.8f,.06f,.06f);
                equipment.GetComponent<MeshRenderer>().sharedMaterial = _extra;
            }
            _manager.ReplaceOriginal(_actor, _companion);
        }
        internal Vector3 World(Vector3 native, Vector3 origin)
        {
            float units = Math.Max(2.4f / (1.2f * .82f), 2.4f / (.9f * .75f));
            Vector3 point = (native - origin - Vector3.up * 1.2f) / units;
            return _frame.position + _frame.right * point.x + _frame.up * point.y + _frame.forward * point.z;
        }
        internal Texture2D Capture(Vector3 localEye, bool right, string label)
        {
            _right = right; bool rear = localEye.z > 0;
            Vector3 position = _frame.position + _frame.right * localEye.x + _frame.up * localEye.y + _frame.forward * localEye.z;
            Head.transform.SetPositionAndRotation(position, _frame.rotation * Quaternion.Euler(0, rear ? 180 : 0, 0));
            if (_display != null)
            {
                Vector3 other = position + Head.transform.right * (right ? -.064f : .064f);
                _display.Render(_ui, _root, _root.rect, _capture, _capture,
                    new MenuDepthEye(false, right ? other : position, Head), new MenuDepthEye(true, right ? position : other, Head));
            }
            if (_desktop)
            {
                Canvas.ForceUpdateCanvases();
                Vector3 other = position + Head.transform.right * (right ? -.064f : .064f);
                CaptureDesktop(right ? other : position, false); CaptureDesktop(right ? position : other, true);
            }
            Canvas.ForceUpdateCanvases(); Head.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = _final;
            var pixels = new Texture2D(_final.width, _final.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0); pixels.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(_output, _path + "-" + label + ".png"), pixels.EncodeToPNG()); return pixels;
        }
        private void CaptureDesktop(Vector3 physicalEye, bool right)
        {
            Check(MenuDepthScreenProjection.TryMap(_screen, _ui, _image.rectTransform, physicalEye, out Vector3 nativeEye),
                "the native desktop mapper admits both physical window sides without changing its fixed metric");
            Vector2 pixels = MenuDepthScreenProjection.PixelFootprint(_screen, _ui, _image.rectTransform, new MenuDepthEye(right, physicalEye, Head));
            RenderTexture target = right ? _desktopRight! : _capture; _ui.targetTexture = target;
            MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(right, nativeEye, Head, true, pixels));
            try
            {
                Canvas.ForceUpdateCanvases();
                File.AppendAllText(Path.Combine(_output, "desktop-geometry.txt"), "eye=" + nativeEye + "; physical=" + physicalEye + "; sourceReady=" + _source.IsReady
                    + "; sourceTexture=" + _image.texture.name + "; cull=" + _image.canvasRenderer.cull + "; graphic=" + _image.rectTransform.rect
                    + "; graphicWorld=" + _image.rectTransform.localToWorldMatrix + "; camera=" + _ui.transform.position + "; target=" + target.width + "," + target.height + "\n");
                SaveTarget((RenderTexture)_image.texture, "desktop-original-eye-" + right); _ui.Render(); SaveTarget(target, "desktop-ui-eye-" + right);
            }
            finally { MenuDepthSources.AfterCapture(); _ui.targetTexture = _capture; }
        }
        private void SaveTarget(RenderTexture target, string label)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0); pixels.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(_output,label + ".png"),pixels.EncodeToPNG()); Object.DestroyImmediate(pixels);
        }
        internal void ThrowingRear()
        {
            Vector3 eye = _frame.position + _frame.forward * 1.7f;
            Matrix4x4 originalView = _nativeCamera.worldToCameraMatrix, originalProjection = _nativeCamera.projectionMatrix;
            bool threw = false;
            try { _source.BeforeCapture(new MenuDepthEye(true,eye,Head)); }
            catch (InvalidOperationException error) { threw = error.Message.Contains("injected rear native render failure"); }
            finally { _source.AfterCapture(); }
            Check(threw,"the injected rear draw failure reaches the actual reflected native camera lease");
            Check(!GL.invertCulling && _nativeCamera.targetTexture == _native && _image.texture == _native,
                "a throwing rear draw restores global winding and both original native texture identities");
            Check(_nativeCamera.worldToCameraMatrix == originalView && _nativeCamera.projectionMatrix == originalProjection
                && Mathf.Abs(_nativeCamera.nearClipPlane-.1f)<.0001f && Mathf.Abs(_nativeCamera.farClipPlane-10f)<.0001f,
                "a throwing rear draw restores the original automatic camera matrices and clip fields");
        }
        internal void Restored()
        {
            Check(_invariant && _nativeDraws > 0, "actual camera-hosted effects retain the original main actor holder and camera transforms during front and rear renders");
            Check(_image.texture == _native && _nativeCamera.targetTexture == _native && !GL.invertCulling,
                "front and rear native leases restore original textures and global culling state");
        }
        public void Dispose()
        {
            GL.invertCulling = false; Camera.onPreRender -= _selectEye; VRRigDriver.HeadCamera = null;
            MenuDepthSources.AfterCapture(); MenuDepthSources.Unregister(_source); _source.Dispose(); _display?.Dispose();
            if (_desktopMaterial != null) MenuDepthDisplay.ReleaseDisplayMaterial(_desktopMaterial); MenuDepthDisplay.Shutdown();
            if (_desktopRight != null) { _desktopRight.Release(); Object.DestroyImmediate(_desktopRight); }
            foreach (Material material in new[] { _front,_back,_side,_extra }) Object.DestroyImmediate(material); Object.DestroyImmediate(_bodyMesh);
            foreach (RenderTexture target in new[] { _native,_final,_capture }) { target.Release(); Object.DestroyImmediate(target); }
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
        }
    }
}
