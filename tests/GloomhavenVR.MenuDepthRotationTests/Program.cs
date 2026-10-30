using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
    private static bool Yaw(float actual, float expected) => Mathf.Abs(Mathf.DeltaAngle(actual, expected)) < .02f;

    public static IEnumerator Run(string output, Action<int> complete)
    {
        _checks = 0;
        foreach (int mode in new[] { 0, 1, 2 })
        using (var scene = new Scene(mode, output))
        {
            yield return null; Canvas.ForceUpdateCanvases();
            scene.Bind();
            yield return null; Canvas.ForceUpdateCanvases();
            scene.Interact();
            scene.RenderTurntable();
        }
        complete(_checks);
    }

    private sealed class Scene : IDisposable
    {
        private readonly List<Object> _objects = new();
        private readonly string _output;
        private readonly string _name;
        private readonly Canvas _canvas;
        private readonly GraphicRaycaster _raycaster;
        private readonly Camera? _eventCamera;
        private readonly RawImage _portrait;
        private readonly RectTransform _control;
        private readonly Character3DDisplayManager _manager;
        private readonly UIWindow _window;
        private readonly NativeWindowDragCanary _windowDrag;
        private readonly UguiPointer _left = new(-111), _right = new(-112), _mouse = new(-1);
        private object _identity = new();
        private bool _ready = true;
        private int _nativeClicks, _backgroundClicks;
        private readonly Vector2 _nativeSize;
        private readonly Vector3 _nativePosition, _nativeScale;
        private readonly Transform _nativeParent;

        internal Scene(int mode, string output)
        {
            _name = mode == 0 ? "world" : mode == 1 ? "native-camera" : "native-overlay"; _output = output;
            WorldUIConfig.CharacterPreviewDepth.Value = true;
            GloomhavenVR.Core.VRSession.IsRunning = true;
            var eventSystem = new GameObject("actual EventSystem", typeof(EventSystem)); _objects.Add(eventSystem);
            GameObject root = Go("original window", null, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.AddComponent(mode == 1 ? typeof(UICharacterCreatorWindow) : typeof(UIAdventurePartyAssemblyCharacterDisplay));
            _window = root.AddComponent<UIWindow>();
            _windowDrag = root.AddComponent<NativeWindowDragCanary>();
            _canvas = root.GetComponent<Canvas>(); _raycaster = root.GetComponent<GraphicRaycaster>();
            _canvas.renderMode = mode == 0 ? RenderMode.WorldSpace : mode == 1 ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            RectTransform rootRect = (RectTransform)root.transform; rootRect.sizeDelta = new Vector2(1200, 900);
            if (mode != 2)
            {
                _eventCamera = Go("actual UI event camera", null, typeof(Camera)).GetComponent<Camera>();
                _eventCamera.enabled = false; _eventCamera.cullingMask = 1 << 5;
                var target = new RenderTexture(1200, 900, 24); target.Create(); _objects.Add(target);
                _eventCamera.targetTexture = target; _canvas.worldCamera = _eventCamera;
                if (mode == 0)
                {
                    rootRect.localScale = Vector3.one * .002f;
                    rootRect.SetPositionAndRotation(new Vector3(.2f, .1f, .5f), Quaternion.Euler(0, 23, 0));
                    _eventCamera.transform.SetPositionAndRotation(new Vector3(.4f, .2f, -3f),
                        Quaternion.LookRotation(rootRect.position - new Vector3(.4f, .2f, -3f)));
                }
                else
                {
                    _eventCamera.orthographic = true; _eventCamera.orthographicSize = 450;
                    _eventCamera.transform.position = new Vector3(20, -10, -7); _canvas.planeDistance = 7;
                }
            }
            var background = Go("original native window hit target", rootRect, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var backRect = (RectTransform)background.transform; backRect.sizeDelta = new Vector2(1200, 900);
            background.GetComponent<Image>().color = Color.gray;
            var backgroundButton = root.AddComponent<Button>(); backgroundButton.targetGraphic = background.GetComponent<Image>();
            backgroundButton.onClick.AddListener(() => _backgroundClicks++);
            _portrait = Go("original native character RawImage", rootRect, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            _portrait.rectTransform.sizeDelta = new Vector2(400, 700); _portrait.rectTransform.anchoredPosition = new Vector2(-100, 0);
            _portrait.raycastTarget = false;
            _control = (RectTransform)Go("original native adjacent control", rootRect, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).transform;
            _control.sizeDelta = new Vector2(160, 70); _control.anchoredPosition = new Vector2(90, 220);
            _control.GetComponent<Button>().onClick.AddListener(() => _nativeClicks++);
            _manager = Go("original manager lifetime input", null, typeof(Character3DDisplayManager)).GetComponent<Character3DDisplayManager>();
            _nativeSize = _portrait.rectTransform.sizeDelta; _nativeParent = _portrait.transform.parent;
            _nativePosition = _portrait.transform.localPosition; _nativeScale = _portrait.transform.localScale;
        }

        private GameObject Go(string name, Transform? parent, params Type[] components)
        {
            var go = new GameObject(name, components); go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false); else _objects.Add(go);
            return go;
        }

        internal void Bind() => MenuDepthCharacterRotation.Bind(_portrait, _manager, () => _ready ? _identity : null);
        private float Current => MenuDepthCharacterRotation.GetYaw(_portrait, _identity);
        private Vector2 Point(float x, float y = 0f)
            => RectTransformUtility.WorldToScreenPoint(_eventCamera, _portrait.rectTransform.TransformPoint(new Vector3(x, y)));
        private RaycastResult Pick(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            var data = new PointerEventData(EventSystem.current) { position = point, pointerId = -111 };
            _raycaster.Raycast(data, hits);
            Check(hits.Count > 0, "the original enabled native raycaster has a visible hit");
            return hits[0];
        }
        private void Press(UguiPointer pointer, float x = 0f)
        {
            Vector2 point = Point(x); RaycastResult hit = Pick(point);
            Check(ReferenceEquals(hit.gameObject, _portrait.gameObject),
                "original native portrait becomes the exact enabled uGUI hit");
            pointer.Hover(hit.gameObject, hit); pointer.Press(point);
        }

        internal void Interact()
        {
            Press(_left);
            Check(_portrait.raycastTarget, "the bound native portrait leases its original raycast flag");
            Check(_window.Downs == 1, "portrait input preserves the original native window pointer-focus callback once");
            GameObject selected = ExecuteEvents.GetEventHandler<ISelectHandler>(_portrait.gameObject);
            Check(ReferenceEquals(selected, _window.gameObject), "the new portrait target leaves the original native selection handler unchanged");
            ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.selectHandler);
            Check(_window.Selects == 1, "native selection still reaches its original window callback");
            _left.Drag(Point(100));
            Check(Yaw(Current, 90), "the first actual laser drag turns the portrait by its physical width");
            _left.Release(Point(100)); float stopped = Current;
            _left.Drag(Point(200));
            Check(Yaw(Current, stopped), "a released pointer cannot continue rotating the original figure");
            Check(_backgroundClicks == 0, "portrait press and drag never click the native window behind it");
            _window.DisabledClickToFocus = true; int focus = _window.Downs;
            Press(_left); _left.Release(Point(0));
            Check(_window.Downs == focus, "the original native click-to-focus disablement remains authoritative");
            _window.DisabledClickToFocus = false;

            Press(_left); stopped = Current;
            _left.Drag(Point(0, 120));
            Check(Yaw(Current, stopped), "vertical motion alone does not change the original figure yaw");
            if (_canvas.renderMode == RenderMode.WorldSpace)
            {
                Vector3 eyePosition = _eventCamera!.transform.position; Quaternion eyeRotation = _eventCamera.transform.rotation;
                _eventCamera.transform.position += new Vector3(.3f, -.1f, .2f);
                _eventCamera.transform.LookAt(_portrait.transform.position);
                _left.Drag(Point(0, 120));
                Check(Yaw(Current, stopped), "moving the physical head cannot turn a stationary captured portrait point");
                _eventCamera.transform.SetPositionAndRotation(eyePosition, eyeRotation);
            }
            _left.Cancel();

            Press(_left); Press(_right);
            _right.Drag(Point(-150));
            Check(Yaw(Current, 90), "a second held pointer cannot change the established figure turn");
            _right.Release(Point(-150));
            _left.Drag(Point(100));
            Check(Yaw(Current, 180), "the first captured pointer remains authoritative after another pointer releases");
            _left.Cancel(); stopped = Current; _left.Drag(Point(-150));
            Check(Yaw(Current, stopped), "tracking-style cancellation stops held rotation without a native click");

            var hand = new GloomhavenVR.Hands.RotationHandInput();
            GloomhavenVR.Hands.VRHands.Primary = hand;
            Press(_left); stopped = Current;
            var laserLifetime = new RotationLaserLifetime(_left, hand, _canvas);
            hand.Ray.Active = false; laserLifetime.TickTrackingGate(); _left.Drag(Point(150));
            Check(Yaw(Current, stopped), "production laser tracking loss releases native portrait capture");
            hand.Ray.Active = true; Press(_left); stopped = Current;
            laserLifetime = new RotationLaserLifetime(_left, hand, _canvas);
            GloomhavenVR.Hands.VRHands.Primary = new GloomhavenVR.Hands.RotationHandInput();
            laserLifetime.TickTrackingGate(); _left.Drag(Point(-150));
            Check(Yaw(Current, stopped), "production dominant-hand mode changes cancel the existing original turn");

            Press(_mouse); _mouse.Drag(Point(-50)); _mouse.Release(Point(-50));
            Check(Yaw(Current, 135), "the same original event target also supports a native mouse drag");
            Press(_left); stopped = Current; _raycaster.enabled = false; _left.Drag(Point(100));
            Check(Yaw(Current, stopped), "locking the original raycaster revokes its active turn without changing yaw");
            _left.Cancel(); _raycaster.enabled = true;

            Vector2 controlPoint = RectTransformUtility.WorldToScreenPoint(_eventCamera, _control.position);
            RaycastResult control = Pick(controlPoint);
            Check(ReferenceEquals(control.gameObject, _control.gameObject), "the original native control wins over the portrait beneath it");
            _left.Hover(control.gameObject, control); _left.Press(controlPoint); _left.Release(controlPoint);
            Check(_nativeClicks == 1 && Yaw(Current, stopped), "the original adjacent control keeps its callback and never turns the portrait");

            Press(_left); _identity = new object();
            Check(Yaw(Current, 0), "a replacement native character starts at its own original facing");
            _left.Drag(Point(100));
            Check(Yaw(Current, 0), "a held old character drag cannot rotate a replacement native model"); _left.Cancel();

            Press(_left); _left.Drag(Point(50)); _portrait.gameObject.SetActive(false);
            Check(!_portrait.raycastTarget, "closing the native portrait restores its original hit behavior immediately");
            _portrait.gameObject.SetActive(true); Canvas.ForceUpdateCanvases();
            stopped = Current; _left.Drag(Point(100));
            Check(Yaw(Current, stopped), "reopening a pooled portrait does not resurrect an old captured press"); _left.Cancel();

            Press(_left); _left.Drag(Point(50)); _ready = false; _left.Drag(Point(100));
            Check(!_portrait.raycastTarget, "native source loss restores the original portrait raycast flag during capture");
            _ready = true; _ = Current; _left.Cancel();
            Press(_left); _left.Drag(Point(50)); WorldUIConfig.CharacterPreviewDepth.Value = false;
            Check(Yaw(Current, 0) && !_portrait.raycastTarget, "the independent depth-off switch removes turn capture and restores native input");
            _left.Cancel(); WorldUIConfig.CharacterPreviewDepth.Value = true; Bind();
            Press(_left); _left.Drag(Point(50));
            _portrait.gameObject.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
            stopped = Current; _left.Drag(Point(100));
            Check(Yaw(Current, stopped), "focus loss cancels the current turn capture"); _left.Cancel();

            MenuDepthCharacterRotation.Unbind(_portrait);
            Check(!_portrait.raycastTarget, "turntable retirement restores the exact native raycast flag");
            _portrait.raycastTarget = true; Bind(); MenuDepthCharacterRotation.Unbind(_portrait);
            Check(_portrait.raycastTarget, "an originally interactive native portrait restores its original true flag too");
            _portrait.raycastTarget = false; Bind();
            var automatic = Go("automatic presentation outside interactive character menus", null, typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            automatic.raycastTarget = false;
            MenuDepthCharacterRotation.Bind(automatic, _manager, () => _identity);
            Check(!automatic.raycastTarget && automatic.GetComponent<MenuDepthCharacterRotationReceiver>() == null,
                "automatic unlock and retirement portraits keep their original noninteractive presentation");
            Check(_portrait.rectTransform.sizeDelta == _nativeSize && ReferenceEquals(_portrait.transform.parent, _nativeParent)
                && _portrait.transform.localPosition == _nativePosition && _portrait.transform.localScale == _nativeScale,
                "turntable input preserves original window size parent position and scale");
            Check(_backgroundClicks == 0, "all portrait gestures leave original native window actions untouched");

            var flat = new RotationFlatDispatch(); stopped = Current;
            flat.Begin(Point(0)); flat.Move(Point(100));
            Check(Yaw(Current, stopped + 90), "the shipped native-screen drag dispatcher turns the same original portrait");
            flat.PoseLost(); stopped = Current; flat.Move(Point(-100));
            Check(Yaw(Current, stopped), "native screen pose loss ends the same original turn capture");
            Check(_windowDrag.Drags == 0, "the native window ancestor never steals the figure drag or changes its placement");
        }

        private void Turn(float start, float end)
        { Press(_left, start); _left.Drag(Point(end)); _left.Release(Point(end)); }

        internal void RenderTurntable()
        {
            // Input meshes are asymmetric so an unchanged camera/pose cannot pass
            // the back-view picture. The production matrix is the only turn.
            Vector3 pivot = new(13, -4, 7);
            var root = Go("fixed native preview model", null); root.layer = 8; root.transform.position = pivot;
            ModelPart(root.transform, "native grey body", new Vector3(0, 1, 0), new Vector3(.8f, 2, .5f), Color.gray);
            ModelPart(root.transform, "original front red panel", new Vector3(0, 1, -.27f), new Vector3(.65f, 1.6f, .03f), Color.red);
            ModelPart(root.transform, "original back blue panel", new Vector3(0, 1, .27f), new Vector3(.65f, 1.6f, .03f), Color.blue);
            ModelPart(root.transform, "original right green equipment", new Vector3(.55f, 1.3f, 0), new Vector3(.25f, .3f, .3f), Color.green);
            var camera = Go("actual native model camera", null, typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = false; camera.fieldOfView = 35;
            camera.cullingMask = 1 << 8; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.transform.position = pivot + new Vector3(0, 1, -5); camera.transform.LookAt(pivot + Vector3.up);
            var target = new RenderTexture(512, 512, 24); target.Create(); _objects.Add(target); camera.targetTexture = target;
            Transform[] native = root.GetComponentsInChildren<Transform>(); var positions = new Vector3[native.Length]; var rotations = new Quaternion[native.Length];
            for (int i = 0; i < native.Length; i++) { positions[i] = native[i].position; rotations[i] = native[i].rotation; }
            _identity = new object(); _ = Current;
            Texture2D front = Render(camera, target, pivot, "front");
            Check(Count(front, 0) > 1000 && Count(front, 2) < 100, "actual original viewport first shows the native front marker");
            Turn(0, 200);
            Check(Yaw(Current, 180), "a half portrait-width gesture turns the same original figure through half a turn");
            Texture2D rear = Render(camera, target, pivot, "rear");
            Check(Count(rear, 2) > 1000 && Count(rear, 0) < 100,
                "actual viewport reveals the original back marker after half a turn");
            Turn(100, 0); Texture2D side = Render(camera, target, pivot, "side");
            Check(Count(side, 1) > 100, "sideways native equipment appears while inspecting the original side");
            for (int i = 0; i < native.Length; i++)
                Check(native[i].position == positions[i] && native[i].rotation == rotations[i],
                    "rendered inspection never writes original native model or gameplay transforms");
            Object.DestroyImmediate(front); Object.DestroyImmediate(rear); Object.DestroyImmediate(side);
        }

        private void ModelPart(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = 8;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var material = new Material(Shader.Find("Unlit/Color")); material.color = color; _objects.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private Texture2D Render(Camera camera, RenderTexture target, Vector3 pivot, string pose)
        {
            Matrix4x4 rotation = MenuDepthCharacterRotation.NativeRotation(Current, pivot, Vector3.up);
            Check(Vector3.Distance(rotation.MultiplyPoint3x4(pivot), pivot) < .0001f,
                "the actual native main ground remains fixed throughout a full turn");
            Matrix4x4 original = camera.worldToCameraMatrix;
            try { camera.worldToCameraMatrix = original * rotation; camera.Render(); }
            finally { camera.worldToCameraMatrix = original; }
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(_output, _name + "-" + pose + ".png"), texture.EncodeToPNG());
            return texture;
        }

        private static int Count(Texture2D image, int channel)
        {
            int count = 0;
            foreach (Color32 color in image.GetPixels32())
                if (channel == 0 ? color.r > 150 && color.g < 80 && color.b < 80
                    : channel == 1 ? color.g > 150 && color.r < 80 && color.b < 80
                    : color.b > 150 && color.r < 80 && color.g < 80) count++;
            return count;
        }

        public void Dispose()
        {
            _left.Cancel(); _right.Cancel(); _mouse.Cancel(); MenuDepthCharacterRotation.Shutdown();
            foreach (Object item in _objects)
            {
                if (item == null) continue;
                if (item is RenderTexture target) target.Release();
                Object.DestroyImmediate(item);
            }
            _objects.Clear();
        }
    }
}
