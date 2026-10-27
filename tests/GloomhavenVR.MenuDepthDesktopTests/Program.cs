using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MenuDepthDesktopProgram
{
    private static int _count;
    private static void Check(bool value, string message) { _count++; if (!value) throw new Exception(message); }
    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    { for (int i = 0; i < 16; i++) if (Math.Abs(a[i] - b[i]) > .0001f) return false; return true; }
    private static Texture2D Read(RenderTexture rt, string path)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); RenderTexture.active = previous;
        File.WriteAllBytes(path, image.EncodeToPNG()); return image;
    }
    private static float X(Texture2D image, int channel)
    {
        Color32[] pixels = image.GetPixels32(); float total = 0, moment = 0;
        for (int y = 0; y < image.height; y++) for (int x = 0; x < image.width; x++)
        {
            Color32 p = pixels[y * image.width + x];
            bool match = channel == 0 ? p.r > 100 && p.r > p.g * 2 && p.r > p.b * 2
                : channel == 1 ? p.g > 100 && p.g > p.r * 2 && p.g > p.b * 2
                : p.b > 100 && p.b > p.r * 2 && p.b > p.g * 2;
            if (match) { total++; moment += x; }
        }
        Check(total > 12, "actual native UI graphic is visible in the captured stack"); return moment / total;
    }

    public static IEnumerator Run(string output, Action<int> complete)
    {
        _count = 0; VRSession.IsRunning = true; DesktopEyeInput.Available = true;
        using (var scene = new Scene())
        {
            scene.Flat.Begin();
            Canvas.ForceUpdateCanvases();
            Check(scene.Flat.Captured == 2, "actual original capture census adopts both screen-space UI cameras while excluding the foreign preview target");
            Check(scene.OriginalA.targetTexture == scene.Ui && scene.OriginalB.targetTexture == scene.Ui,
                "actual captured native UI cameras route to the owned glass target");
            scene.OriginalA.Render(); scene.OriginalB.Render();
            Texture2D mono = Read(scene.Ui, Path.Combine(output, "original-native-mono.png"));
            float monoBlue = X(mono, 2), monoRed = X(mono, 0);
            Color nativeFade = mono.GetPixel(115, 110); Object.DestroyImmediate(mono);
            scene.Eyes(Vector3.zero); scene.Render(false);
            Check(scene.Flat.Right != null && scene.Flat.Right.IsCreated(), "native screen-space menu receives two prepared glass captures before the first actual head draw");
            Texture2D left = Read(scene.Ui, Path.Combine(output, "native-ui-left.png"));
            Texture2D right = Read(scene.Flat.Right!, Path.Combine(output, "native-ui-right.png"));
            Check(left.GetPixel(220, 105).g > .8f && right.GetPixel(220, 105).g > .8f,
                "both original native UI cameras populate the borrowed eye target without ordinary-route overwrite");
            Check(X(right, 0) - X(left, 0) > .15f,
                "original orthographic UI stack carries physical-eye-dependent graphic relief");
            Check(Math.Abs(X(left, 2) - monoBlue) < .12f && Math.Abs(X(right, 2) - monoBlue) < .12f,
                "native planar text and controls keep their original sampling in both eye captures");
            Check(Math.Abs(X(left, 1) - X(right, 1)) > .1f,
                "the second original UI camera receives its own graphic-plane eye mapping");
            Check(Math.Abs(left.GetPixel(115, 110).r - nativeFade.r) < .01f
                && Math.Abs(right.GetPixel(115, 110).a - nativeFade.a) < .01f,
                "native ancestor fade and original graphic alpha retain their sampled appearance");
            Color masked = right.GetPixel(60, 40), excluded = right.GetPixel(32, 40);
            Check(masked.r > .8f && masked.b > .8f && excluded.r < .05f && excluded.b < .05f,
                "original native RectMask2D still clips its graphic in the eye-specific UI stack");
            Texture2D headLeft = Read(scene.HeadTarget, Path.Combine(output, "head-left.png"));
            scene.Render(true);
            Texture2D headRight = Read(scene.HeadTarget, Path.Combine(output, "head-right.png"));
            Check(Math.Abs(X(headLeft, 0) - X(headRight, 0)) + Math.Abs(X(headLeft, 1) - X(headRight, 1)) > .08f,
                "actual head rendering selects the correct original glass eye before draw");
            Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(headLeft); Object.DestroyImmediate(headRight);
            scene.CheckOriginalState();
            Check(scene.Glass.mainTexture == scene.Ui, "head post-render returns the ordinary left glass binding for native continuation");
            Check(scene.Clicks == 0, "capturing spatial menus never invokes the original button callback");
            scene.Button.onClick.Invoke(); Check(scene.Clicks == 1, "explicit original UI callback remains usable after spatial capture");

            int frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
            scene.Eyes(new Vector3(.25f, .08f, 0)); scene.Render(false);
            Texture2D side = Read(scene.Ui, Path.Combine(output, "head-side-ui.png"));
            float sideRed = X(side, 0); Object.DestroyImmediate(side);
            frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
            scene.Eyes(Vector3.zero); scene.Render(false);
            Texture2D centre = Read(scene.Ui, Path.Combine(output, "head-centre-ui.png"));
            Check(Math.Abs(sideRed - X(centre, 0)) > .8f,
                "head translation updates native-screen graphic perspective on a later actual frame");
            Object.DestroyImmediate(centre);

            frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
            scene.Screen.rotation = Quaternion.Euler(0, 27, 0); scene.Screen.position = new Vector3(.3f, .15f, 2.2f);
            scene.Eyes(new Vector3(.15f, 0, 0)); scene.Render(false);
            Texture2D moved = Read(scene.Ui, Path.Combine(output, "moved-rotated-window.png"));
            Check(X(moved, 0) > 0, "moved and rotated physical window maps original screen-space art to a valid native eye");
            Object.DestroyImmediate(moved); scene.CheckOriginalState();

            frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
            scene.Screen.rotation = Quaternion.identity; scene.Screen.position = new Vector3(0, 0, 2);
            var fault = new DesktopFaultSource(scene.SpatialGraphic); MenuDepthSources.Register(fault);
            scene.Eyes(Vector3.zero); scene.Render(false);
            Check(scene.Flat.Right == null && fault.Restores == 2,
                "a partial right-eye source failure releases both source leases and the unpresentable eye pair");
            scene.CheckOriginalState();
            Texture2D recovered = Read(scene.Ui, Path.Combine(output, "recovered-native-mono.png"));
            Check(Math.Abs(X(recovered, 0) - monoRed) < .2f,
                "a partial eye failure recaptures original mono UI after restoring projected graphics");
            Object.DestroyImmediate(recovered); MenuDepthSources.Unregister(fault);
            frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
            DesktopEyeInput.Available = false; scene.Render(false);
            Check(scene.Flat.Right == null, "unavailable eye observations release the owned right target and retain original native UI");
            Check(scene.Glass.mainTexture == scene.Ui, "unavailable eye observations keep the original mono glass binding");
            scene.CheckOriginalState();
            scene.Flat.End();
            Check(scene.OriginalA.targetTexture == null && scene.OriginalB.targetTexture == null,
                "release restores both original native UI cameras to the backbuffer");
            Check(scene.ModelCamera.targetTexture == scene.ModelTarget, "release never redirects or destroys the separately owned native preview target");
            File.WriteAllLines(Path.Combine(output, "lifecycle.log"), VRLog.Messages);
        }
        complete(_count);
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Camera OriginalA, OriginalB, Head, ModelCamera;
        internal readonly RenderTexture Ui, Background, HeadTarget, ModelTarget;
        internal readonly Transform Screen;
        internal readonly Material Glass;
        internal readonly FlatScreen Flat;
        internal readonly Button Button;
        internal Graphic SpatialGraphic => _red;
        internal int Clicks;
        private readonly RectTransform _canvasA, _canvasB;
        private readonly Image _red, _green;
        private readonly MenuDepthGraphic _redDepth, _greenDepth;
        private readonly Matrix4x4 _projectionA, _projectionB;
        private readonly Vector3 _positionA, _positionB;
        private readonly Texture2D _art;
        private readonly Sprite _sprite;
        private readonly Material _modelMaterial;

        internal Scene()
        {
            VRLog.Messages.Clear();
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
            Ui = new RenderTexture(320, 240, 24); Ui.Create();
            Background = new RenderTexture(320, 240, 24); Background.Create();
            HeadTarget = new RenderTexture(320, 240, 24); HeadTarget.Create();
            ModelTarget = new RenderTexture(128, 128, 24); ModelTarget.Create();
            OriginalA = CreateCamera("original main UI camera", 5, -2); OriginalA.orthographic = true; OriginalA.orthographicSize = 5;
            OriginalA.transform.position = new Vector3(50, 30, 90); OriginalA.nearClipPlane = .01f; OriginalA.farClipPlane = 100;
            OriginalB = CreateCamera("original overlay UI camera", 6, -1); OriginalB.orthographic = true; OriginalB.orthographicSize = 7;
            OriginalB.transform.position = new Vector3(-40, 20, 70); OriginalB.nearClipPlane = .01f; OriginalB.farClipPlane = 100;
            OriginalB.clearFlags = CameraClearFlags.Depth;
            _canvasA = CreateCanvas(OriginalA, 5, 12); _canvasB = CreateCanvas(OriginalB, 6, 25);
            _canvasA.gameObject.AddComponent<CanvasGroup>().alpha = .8f;
            _art = new Texture2D(2, 2, TextureFormat.RGBA32, false); _art.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); _art.Apply();
            _sprite = Sprite.Create(_art, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
            _red = Image(_canvasA, 5, new Vector2(-45, -10), new Vector2(85, 85), Color.red);
            _redDepth = _red.gameObject.AddComponent<MenuDepthGraphic>(); _redDepth.Configure(_red, null, .22f);
            _green = Image(_canvasB, 6, new Vector2(60, -15), new Vector2(50, 55), Color.green);
            _greenDepth = _green.gameObject.AddComponent<MenuDepthGraphic>(); _greenDepth.Configure(_green, null, .18f);
            var clipGo = new GameObject("original native image clipper", typeof(RectTransform), typeof(RectMask2D));
            clipGo.layer = 6; clipGo.transform.SetParent(_canvasB, false);
            var clip = (RectTransform)clipGo.transform; clip.sizeDelta = new Vector2(40, 20); clip.anchoredPosition = new Vector2(-100, -80);
            Image(clip, 6, Vector2.zero, new Vector2(60, 40), Color.magenta);
            var label = new GameObject("actual original planar text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.layer = 6; label.transform.SetParent(_canvasB, false);
            var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = "NATIVE"; text.color = Color.blue; text.fontSize = 15; text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.sizeDelta = new Vector2(100, 25); text.rectTransform.anchoredPosition = new Vector2(0, 78);
            var button = Image(_canvasA, 5, new Vector2(0, -85), new Vector2(65, 15), new Color(.2f, .2f, .2f));
            Button = button.gameObject.AddComponent<Button>(); Button.onClick.AddListener(() => Clicks++);
            Head = CreateCamera("actual head draw", 27, 100); Head.enabled = false; Head.orthographic = true;
            Head.orthographicSize = 1; Head.aspect = 4f / 3f; Head.targetTexture = HeadTarget;
            Head.transform.position = Vector3.zero; VRRigDriver.HeadCamera = Head;
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name = "actual physical desktop glass";
            Screen = screen.transform; screen.layer = 27; Screen.position = new Vector3(0, 0, 2);
            Screen.localScale = new Vector3(1.2f, .9f, 1); Glass = new Material(Shader.Find("Sprites/Default"));
            Glass.mainTexture = Ui; screen.GetComponent<MeshRenderer>().sharedMaterial = Glass;
            Flat = new FlatScreen(Screen, Background, Ui, Glass);
            ModelCamera = CreateCamera("original GUI3D preview camera", 8, -5); ModelCamera.targetTexture = ModelTarget;
            ModelCamera.fieldOfView = 30; ModelCamera.transform.position = new Vector3(100, 100, 100);
            var manager = ModelCamera.gameObject.AddComponent<Character3DDisplayManager>();
            var holder = new GameObject("original model holder"); holder.transform.position = ModelCamera.transform.position + new Vector3(.4f, -.45f, 2.56f);
            manager.Bind(holder.transform);
            _modelMaterial = new Material(Shader.Find("Unlit/Color")); _modelMaterial.color = Color.yellow;
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube); model.layer = 8; model.transform.SetParent(holder.transform, false);
            model.transform.localPosition = new Vector3(-.3f, .4f, 0); model.transform.localScale = new Vector3(.3f, .7f, .25f);
            model.GetComponent<MeshRenderer>().sharedMaterial = _modelMaterial;
            manager.BindOriginal(model.transform);
            var preview = new GameObject("actual original creator RawImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            preview.layer = 5; preview.transform.SetParent(_canvasA, false);
            RawImage raw = preview.GetComponent<RawImage>(); raw.texture = ModelTarget; raw.rectTransform.sizeDelta = new Vector2(70, 70);
            raw.rectTransform.anchoredPosition = new Vector2(75, 45);
            Canvas.ForceUpdateCanvases(); ModelCamera.Render(); MenuDepthCharacters.Tick();
            _projectionA = OriginalA.projectionMatrix; _projectionB = OriginalB.projectionMatrix;
            _positionA = OriginalA.transform.position; _positionB = OriginalB.transform.position;
        }
        private static Camera CreateCamera(string name, int layer, float depth)
        {
            var camera = new GameObject(name).AddComponent<Camera>(); camera.depth = depth; camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.stereoTargetEye = StereoTargetEyeMask.None; return camera;
        }
        private static RectTransform CreateCanvas(Camera camera, int layer, float distance)
        {
            var go = new GameObject("original screen-space canvas", typeof(RectTransform), typeof(Canvas)); go.layer = layer;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = distance;
            return (RectTransform)go.transform;
        }
        private Image Image(Transform parent, int layer, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject("original native graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = layer; go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.sprite = _sprite;
            image.color = color; image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition = position; return image;
        }
        internal void Eyes(Vector3 localOffset)
        {
            Vector3 centre = Screen.position - Screen.forward * 2f + Screen.right * localOffset.x + Screen.up * localOffset.y + Screen.forward * localOffset.z;
            Vector3 left = centre - Screen.right * .032f, right = centre + Screen.right * .032f;
            Head.SetStereoViewMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(left, Screen.rotation, Vector3.one).inverse);
            Head.SetStereoViewMatrix(Camera.StereoscopicEye.Right, Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(right, Screen.rotation, Vector3.one).inverse);
        }
        internal void NativePixels725(int factor)
        {
            Ui.Release(); Ui.width *= factor; Ui.height *= factor; Ui.Create();
            foreach (RectTransform root in new[] { _canvasA, _canvasB })
                foreach (RectTransform item in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (item == root) continue;
                    item.anchoredPosition *= factor; item.sizeDelta *= factor;
                    if (item.GetComponent<Text>() is Text label) label.fontSize *= factor;
                }
        }
        internal void Render(bool right) { DesktopEyeInput.Right = right; Head.Render(); }
        internal void CheckOriginalState()
        {
            Check(OriginalA.targetTexture == Ui && OriginalB.targetTexture == Ui, "each borrowed original UI camera restores its captured target after both eyes");
            Check(OriginalA.transform.position == _positionA && OriginalB.transform.position == _positionB
                && Same(OriginalA.projectionMatrix, _projectionA) && Same(OriginalB.projectionMatrix, _projectionB),
                "native screen-space UI camera pose and orthographic projection never change for the spatial pass");
        }
        public void Dispose()
        {
            Flat.End(); MenuDepthCharacters.Shutdown(); MenuDepthSources.Unregister(_redDepth); MenuDepthSources.Unregister(_greenDepth);
            VRRigDriver.HeadCamera = null; MenuDepthSources.AfterCapture();
            foreach (RenderTexture rt in new[] { Ui, Background, HeadTarget, ModelTarget }) { rt.Release(); Object.DestroyImmediate(rt); }
            Object.DestroyImmediate(Glass); Object.DestroyImmediate(_modelMaterial); Object.DestroyImmediate(_sprite); Object.DestroyImmediate(_art);
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
        }
    }
}
