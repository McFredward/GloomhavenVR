using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MenuDepthRotationProgram
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }

    public static IEnumerator Run(string output, Action<int> complete)
    {
        _checks = 0;
        foreach (bool creator in new[] { false, true })
        using (var scene = new Scene(creator, output))
        {
            yield return null; Canvas.ForceUpdateCanvases();
            scene.Enroll();
            yield return null; Canvas.ForceUpdateCanvases();
            scene.Exercise();
        }
        complete(_checks);
    }

    private sealed class Scene : IDisposable
    {
        private readonly List<Object> _objects = new();
        private readonly string _name, _output;
        private readonly Camera _native, _events;
        private readonly Character3DDisplayManager _manager;
        private readonly RotationNativeImageEffect _effect;
        private readonly RawImage _image;
        private readonly GraphicRaycaster _raycaster;
        private readonly RectTransform _root;
        private readonly Transform _main;
        private readonly UguiPointer _pointer = new(-111);
        private readonly RenderTexture _nativeTarget;
        private readonly Dictionary<Transform, Matrix4x4> _nativeTransforms = new();
        private MenuDepthCharacterSource _source = null!;

        internal Scene(bool creator, string output)
        {
            _name = creator ? "creator" : "party"; _output = output;
            VRSession.IsRunning = true; WorldUIConfig.CharacterPreviewDepth.Value = true;
            Go("actual Unity EventSystem", null, typeof(EventSystem));
            _native = Go("native GUI character camera", null, typeof(Camera)).GetComponent<Camera>();
            _native.transform.SetPositionAndRotation(new Vector3(13, -4, 7), Quaternion.Euler(0, 23, 0));
            _native.fieldOfView = 30; _native.aspect = 1; _native.nearClipPlane = .1f; _native.farClipPlane = 10;
            _native.cullingMask = 1 << 8; _native.clearFlags = CameraClearFlags.SolidColor;
            _native.backgroundColor = Color.clear; _native.allowHDR = false; _native.allowMSAA = false;
            _nativeTarget = new RenderTexture(512, 512, 24); _nativeTarget.Create(); _objects.Add(_nativeTarget);
            _native.targetTexture = _nativeTarget;
            _manager = _native.gameObject.AddComponent<Character3DDisplayManager>();
            Transform holder = Go("original camera-child preview holder", _native.transform).transform;
            holder.localPosition = new Vector3(.4f, -1.2f, 3.5f);
            _main = Go("original native main actor ground", holder).transform;
            Part("body", new Vector3(0, 1, 0), new Vector3(.8f, 2, .5f), Color.gray);
            Part("front red", new Vector3(0, 1, -.27f), new Vector3(.65f, 1.6f, .03f), Color.red);
            Part("back blue", new Vector3(0, 1, .27f), new Vector3(.65f, 1.6f, .03f), Color.blue);
            Part("right green equipment", new Vector3(.55f, 1.3f, 0), new Vector3(.25f, .3f, .3f), Color.green);
            _manager.Supply(holder, _main);
            _effect = _native.gameObject.AddComponent<RotationNativeImageEffect>(); _effect.Ground = _main;

            var root = Go("original interactive " + _name + " window", null, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.AddComponent(creator ? typeof(UICharacterCreatorWindow) : typeof(UIAdventurePartyAssemblyCharacterDisplay));
            root.AddComponent<UIWindow>(); _root = (RectTransform)root.transform;
            _root.sizeDelta = new Vector2(1200, 900); _root.localScale = Vector3.one * .001f;
            _root.SetPositionAndRotation(new Vector3(.3f, .2f, .5f), Quaternion.Euler(0, -17, 0));
            _image = Go("original portrait RawImage", _root, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            _image.rectTransform.sizeDelta = new Vector2(1200, 900); _image.texture = _nativeTarget;
            _image.raycastTarget = false;
            _events = Go("actual portrait event camera", null, typeof(Camera)).GetComponent<Camera>();
            _events.enabled = false; _events.cullingMask = 1 << 5; _events.fieldOfView = 35;
            var eventTarget = new RenderTexture(1200, 900, 24); eventTarget.Create(); _objects.Add(eventTarget);
            _events.targetTexture = eventTarget;
            _events.transform.SetPositionAndRotation(_root.position - _root.forward * 2f, _root.rotation);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _events;
            _raycaster = root.GetComponent<GraphicRaycaster>();
            foreach (Transform native in _native.GetComponentsInChildren<Transform>()) _nativeTransforms.Add(native, native.localToWorldMatrix);
        }

        private GameObject Go(string name, Transform? parent, params Type[] types)
        {
            var go = new GameObject(name, types); go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false); else _objects.Add(go);
            return go;
        }
        private void Part(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = 8;
            go.transform.SetParent(_main, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var material = new Material(Shader.Find("Unlit/Color")) { color = color }; _objects.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        internal void Enroll()
        {
            MenuDepthCharacters.Tick();
            Check(MenuDepthSources.Registered.Count == 1, "real Characters enrollment registers exactly its native camera target consumer");
            _source = (MenuDepthCharacterSource)MenuDepthSources.Registered[0];
            Check(ReferenceEquals(_source.Graphic, _image) && ReferenceEquals(_source.ReadyIdentity, _manager.Identity),
                "real Characters enrollment uses actual Source readiness and original native identity");
            Check(_image.raycastTarget && _image.GetComponent<MenuDepthCharacterRotationReceiver>() != null,
                "real Characters enrollment attaches the original portrait rotation receiver");
        }
        private Vector2 Point(float x) => RectTransformUtility.WorldToScreenPoint(_events,
            _image.rectTransform.TransformPoint(new Vector3(x, 0)));
        private void Press()
        {
            Vector2 start = Point(0); var hits = new List<RaycastResult>();
            _raycaster.Raycast(new PointerEventData(EventSystem.current) { position = start }, hits);
            Check(hits.Count > 0 && ReferenceEquals(hits[0].gameObject, _image.gameObject),
                "the real enrolled portrait receives the actual original uGUI raycast");
            _pointer.Hover(hits[0].gameObject, hits[0]); _pointer.Press(start);
        }
        private void Turn(float x) { Press(); _pointer.Drag(Point(x)); _pointer.Release(Point(x)); }
        private Texture2D Capture(string pose, bool headRaster = false)
        {
            Vector2Int pixels = MenuDepthHeadRaster.Size(_events);
            using var head = headRaster ? MenuDepthHeadRaster.Begin(pixels) : default(MenuDepthHeadRaster.Lease);
            var eye = new MenuDepthEye(false, _events.transform.position, headRaster ? _events : null!);
            _source.BeforeCapture(eye);
            Texture2D result;
            try
            {
                Check(_image.texture is RenderTexture && !ReferenceEquals(_image.texture, _nativeTarget),
                    "actual Source capture lends its own rendered colour target to the original portrait");
                RenderTexture target = (RenderTexture)_image.texture;
                if(headRaster)
                    Check(MenuDepthHeadRaster.Active && eye.HasWorldToHeadClip && target.width==pixels.x && target.height==pixels.y
                        && MenuDepthHeadRaster.Matches(target,_nativeTarget.descriptor,pixels),
                        "enrolled Source uses the real event-camera head raster and normalized head resources");
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target; result = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                    result.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); result.Apply();
                }
                finally { RenderTexture.active = previous; }
                File.WriteAllBytes(Path.Combine(_output, _name + "-enrolled-" + pose + ".png"), result.EncodeToPNG());
            }
            finally { _source.AfterCapture(); }
            Check(ReferenceEquals(_image.texture, _nativeTarget), "actual Source capture restores the original native portrait target");
            foreach (var pair in _nativeTransforms)
                Check(MatrixNear(pair.Key.localToWorldMatrix, pair.Value), "integrated preview turn leaves every native camera holder and model transform unchanged");
            return result;
        }
        internal void Exercise()
        {
            Texture2D front = Capture("front"); Vector3 ground = _effect.ObservedGround;
            Check(Count(front, 0) > 1000 && Count(front, 2) < 100, "enrolled Source initially renders the original front marker");
            Texture2D headFront=Capture("head-front",true);Vector3 headGround=_effect.ObservedGround;
            Check(Count(headFront,0)>1000 && Count(headFront,2)<100,
                "enrolled Source renders the native front through the actual event-camera head frustum");
            Turn(600);
            Check(Mathf.Abs(Mathf.DeltaAngle(MenuDepthCharacterRotation.GetYaw(_image, _manager.Identity), 180)) < .02f,
                "actual portrait-width pointer input changes the enrolled preview by half a turn");
            Texture2D rear = Capture("rear");
            Check(Count(rear, 2) > 1000 && Count(rear, 0) < 100,
                "real Characters pointer yawprovider and Source render reveal the original back marker");
            Check(Vector3.Distance(ground, _effect.ObservedGround) < .0001f,
                "integrated Source rotation keeps the actual main ground fixed in the measured native camera view");
            Texture2D headRear=Capture("head-rear",true);
            Check(Count(headRear,2)>1000 && Count(headRear,0)<100,
                "real enrolled uGUI drag reveals the native rear through the actual head raster");
            Check(Vector3.Distance(headGround,_effect.ObservedGround)<.0001f,
                "head-raster rotation keeps the same actual native main ground in the measured source view");
            Turn(-300); Texture2D side = Capture("side");
            Check(Count(side, 1) > 100, "the integrated Source renders original side equipment after the same pointer gesture");
            Check(Vector3.Distance(ground, _effect.ObservedGround) < .0001f,
                "the integrated quarter turn retains the same measured native ground");
            Check(_effect.Draws >= 3, "all enrolled rotation pictures execute actual native-style image effect callbacks");
            Object.DestroyImmediate(front); Object.DestroyImmediate(rear); Object.DestroyImmediate(side);
            Object.DestroyImmediate(headFront);Object.DestroyImmediate(headRear);

            _manager.Replace(_main); Texture2D replaced = Capture("replacement");
            Check(Count(replaced, 0) > 1000 && Count(replaced, 2) < 100,
                "actual Source ready-identity replacement resets the integrated turn before its next rendered frame");
            Object.DestroyImmediate(replaced);
            Turn(300); Press(); _manager.Hidden = true; _pointer.Drag(Point(100));
            Check(_source.ReadyIdentity == null, "native hide revokes actual integrated Source readiness");
            Check(!_image.raycastTarget,
                "integrated native readiness loss restores original portrait input immediately");
            _pointer.Release(Point(100));
            _manager.Hidden = false;
            WorldUIConfig.CharacterPreviewDepth.Value = false; MenuDepthCharacters.Tick();
            Check(MenuDepthSources.Registered.Count == 0 && !_image.raycastTarget
                && _image.GetComponent<MenuDepthCharacterRotationReceiver>().enabled == false,
                "actual Characters depth-off shutdown retires source and input before deferred destruction");
            Check(_nativeTarget.IsCreated() && ReferenceEquals(_native.targetTexture, _nativeTarget),
                "integrated depth-off preserves native rendering and its original game-owned target");
        }
        private static bool MatrixNear(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .0001f) return false; return true; }
        private static int Count(Texture2D image, int channel)
        {
            int count = 0;
            foreach (Color32 p in image.GetPixels32())
                if (channel == 0 ? p.r > 150 && p.g < 80 && p.b < 80 : channel == 1 ? p.g > 150 && p.r < 80 && p.b < 80
                    : p.b > 150 && p.r < 80 && p.g < 80) count++;
            return count;
        }
        public void Dispose()
        {
            _pointer.Cancel(); MenuDepthCharacters.Shutdown();
            foreach (Object item in _objects)
            { if (item == null) continue; if (item is RenderTexture target) target.Release(); Object.DestroyImmediate(item); }
            _objects.Clear();
        }
    }
}
