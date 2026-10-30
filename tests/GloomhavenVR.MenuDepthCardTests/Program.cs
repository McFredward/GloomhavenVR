using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static void Field(object owner, string name, object value) => AccessTools.Field(owner.GetType(), name).SetValue(owner, value);
    private static readonly List<GameObject> Owned = new();
    private static string _output = "";
    private static GameObject Go(string name, Transform? parent = null)
    { var go = new GameObject(name, typeof(RectTransform)); if (parent != null) go.transform.SetParent(parent, false); Owned.Add(go); return go; }

    public static IEnumerator Run(string output) => RunCore(output, false);
    public static IEnumerator RunNativeUiFreeze(string output) => RunCore(output, true);
    public static IEnumerator RunNativeHover(string output) => RunCore(output, false, true);
    public static IEnumerator RunHeadText736(string output) => RunCore(output, false, headTextOnly: true);
    public static IEnumerator RunLevelUpHover738(string output) => RunCore(output, false, levelUpHoverOnly: true);
    public static IEnumerator RunMoreCards747(string output) => RunCore(output, false, moreCardsOnly: true);

    private static IEnumerator RunCore(string output, bool nativeFreezeOnly, bool nativeHoverOnly = false, bool headTextOnly = false, bool levelUpHoverOnly = false, bool moreCardsOnly = false)
    {
        _checks = 0; _output = output; VRSession.IsRunning = true;
        var native = new Harmony("MenuDepthCards.NativePorts." + typeof(MenuDepthCardProgram).Assembly.GetName().Name);
        var observer = new Harmony(native.Id + ".observer");
        native.CreateClassProcessor(typeof(NativePorts)).Patch();
        if (nativeHoverOnly) native.CreateClassProcessor(typeof(NativeHoverPorts)).Patch();
        if (levelUpHoverOnly || moreCardsOnly) native.CreateClassProcessor(typeof(LevelUpHoverPorts738)).Patch();
        if (moreCardsOnly)
        {
            native.CreateClassProcessor(typeof(MoreCardsPorts747)).Patch();
            native.CreateClassProcessor(typeof(MoreCardsFitPort747)).Patch();
            native.CreateClassProcessor(typeof(MoreCardsInputPort747)).Patch();
            observer.CreateClassProcessor(typeof(NativeTooltipHide747)).Patch();
        }
        observer.CreateClassProcessor(typeof(MenuDepthLifecycleObserver)).Patch();
        observer.CreateClassProcessor(typeof(MenuDepthLevelUpHoverObserver)).Patch();
        Type? furtherObserver = typeof(MenuDepthLevelUpHoverObserver).Assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthFurtherAbilityHoverObserver");
        if (furtherObserver != null) observer.CreateClassProcessor(furtherObserver).Patch();
        if (headTextOnly) observer.CreateClassProcessor(typeof(HeadMeshObserver736)).Patch();
        try
        {
            if (moreCardsOnly)
            {
                using (var scene = new Scene(1, headRaster: true))
                {
                    IEnumerator proof = scene.ProveMoreCards747();
                    while (proof.MoveNext()) yield return proof.Current;
                }
            }
            else if (levelUpHoverOnly)
            {
                using (var scene = new Scene(1, headRaster: true))
                {
                    IEnumerator proof = scene.ProveLevelUpHover738();
                    while (proof.MoveNext()) yield return proof.Current;
                }
            }
            else if (headTextOnly)
            {
                using (var scene = new Scene(1, headRaster: true))
                {
                    IEnumerator proof = scene.ProveHeadText736();
                    while (proof.MoveNext()) yield return proof.Current;
                }
            }
            else if (nativeFreezeOnly)
            {
                IEnumerator freeze = ProveNativeUiFreeze728(output);
                while (freeze.MoveNext()) yield return freeze.Current;
            }
            else for (int path = 0; path < 3; path++)
                using (var scene = new Scene(path))
                {
                    IEnumerator proof = nativeHoverOnly ? scene.ProveNativeHover729() : scene.Prove();
                    while (proof.MoveNext()) yield return proof.Current;
                }
        }
        finally
        {
            MenuDepthDriver.Shutdown(); MenuDepthDisplay.Shutdown(); VRRigDriver.HeadCamera = null;
            _hoverScene = null;
            observer.UnpatchAll(observer.Id); native.UnpatchAll(native.Id);
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
        }
        yield return _checks;
    }

    [HarmonyPatch]
    private static class NativePorts
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in new[] { typeof(AbilityCardUI), typeof(FullAbilityCard), typeof(MiniAbilityCard), typeof(ItemCardUI), typeof(FullAbilityCardAction), typeof(MonsterRoundCardUI), typeof(MonsterBaseUI), typeof(UIPerkAttackModifier), typeof(UIScenarioAttackModifier), typeof(ExtendedButton) })
                foreach (string name in new[] { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy" })
                {
                    MethodInfo method = AccessTools.DeclaredMethod(type, name);
                    if (method != null) yield return method;
                }
        }
        // Native identity/serialized widgets are real. Starting a campaign/card
        // controller, loading artwork or global gameplay services is not this test.
        [HarmonyPrefix] private static bool Before() => false;
    }

    private sealed class Card
    {
        internal readonly MonoBehaviour Owner;
        internal readonly RectTransform Rect;
        internal readonly TextMeshProUGUI Title;
        internal readonly List<Image> Backgrounds = new();
        internal readonly Button Button;
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal readonly Vector2 Size, Minimum, Maximum, Pivot;
        internal readonly NativePoseWitness Witness;
        internal int Clicks;
        internal Card(Type type, RectTransform parent, int index)
        {
            GameObject go = Go(type.Name + " actual native widget " + index, parent); go.SetActive(false);
            Rect = (RectTransform)go.transform; Rect.sizeDelta = new Vector2(294, 450);
            Rect.localPosition = Vector3.zero; Rect.localScale = Vector3.one;
            Owner = (MonoBehaviour)go.AddComponent(type);
            if (type == typeof(FullAbilityCard) && index >= 3)
            {
                // Both real action-only full prefab variants omit header/default
                // backing fields. Their actual button target Graphics form the art.
                for (int half = 0; half < 2; half++)
                {
                    var actionGo = Go("original action half " + half, Rect);
                    var action = actionGo.AddComponent<FullAbilityCardAction>();
                    var art = Image((RectTransform)actionGo.transform, "original action button art", new Vector2(294, 225), new Vector2(0, half == 0 ? 112.5f : -112.5f));
                    var actionButton = art.gameObject.AddComponent<Button>(); actionButton.targetGraphic = art;
                    var defaultArt = Image((RectTransform)actionGo.transform, "original default button art", new Vector2(35, 35), new Vector2(115, half == 0 ? 200 : -200));
                    var defaultButton = defaultArt.gameObject.AddComponent<Button>(); defaultButton.targetGraphic = defaultArt;
                    Field(action, "actionButton", actionButton); Field(action, "defaultActionButton", defaultButton);
                    Field(Owner, half == 0 ? "topActionButton" : "bottomActionButton", action);
                    Backgrounds.Add(art); Backgrounds.Add(defaultArt);
                }
            }
            else if (type == typeof(FullAbilityCard))
            {
                var header = Image(Rect, "original header", new Vector2(294, 80), new Vector2(0, 185));
                var body = Image(Rect, "original default/action backing", new Vector2(294, 370), new Vector2(0, -40));
                Field(Owner, "headerImage", header); Field(Owner, "buttonsHolderImage", body);
                Backgrounds.Add(header); Backgrounds.Add(body);
            }
            else if (type == typeof(UIPerkAttackModifier) || type == typeof(UIScenarioAttackModifier))
            {
                var body = go.AddComponent<Image>(); body.color = new Color(.04f, .15f, 1, 1);
                if (type == typeof(UIPerkAttackModifier))
                { var nativeButton = go.AddComponent<ExtendedButton>(); nativeButton.targetGraphic = body; Field(Owner, "button", nativeButton); }
                Backgrounds.Add(body);
            }
            else
            {
                var body = Image(Rect, "original native printed background", new Vector2(294, 450), Vector2.zero);
                string field = type == typeof(ItemCardUI) ? "cardBackground" : type == typeof(MonsterRoundCardUI) ? "background"
                    : type == typeof(MonsterBaseUI) ? "backgroundImage" : "miniAbilityBackgroundImage";
                Field(Owner, field, body); Backgrounds.Add(body);
                if (type == typeof(MonsterBaseUI))
                {
                    var opacity = Image(Rect, "original text opacity background", new Vector2(250, 200), new Vector2(0, -20));
                    Field(Owner, "textBackgroundOpacityImage", opacity); Backgrounds.Add(opacity);
                }
            }
            Title = Text(Rect, "original title", "ORIGINAL CARD", new Vector2(270, 56), new Vector2(0, 135));
            Field(Owner, type == typeof(MonsterRoundCardUI) ? "initiativeText"
                : type == typeof(UIPerkAttackModifier) || type == typeof(UIScenarioAttackModifier) ? "textValue" : "titleText", Title);
            var content = (RectTransform)Go("native CreateLayout result input", Rect).transform;
            content.sizeDelta = new Vector2(250, 200); content.anchoredPosition = new Vector2(0, -20);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight = false;
            layout.childControlWidth = false; layout.childForceExpandHeight = false; layout.childForceExpandWidth = false; layout.spacing = 4;
            for (int line = 0; line < 3; line++)
            {
                var t = Text(content, "native action text " + line, "Move " + (line + 2), new Vector2(250, 40), Vector2.zero);
                t.color = Color.green; t.fontSize = 22;
            }
            Button = Image(Rect, "original native control", new Vector2(60, 30), new Vector2(0, -180)).gameObject.AddComponent<Button>();
            Button.onClick.AddListener(() => Clicks++);
            Witness = go.AddComponent<NativePoseWitness>();
            Position = Rect.localPosition; Scale = Rect.localScale; Rotation = Rect.localRotation;
            Size = Rect.sizeDelta; Minimum = Rect.anchorMin; Maximum = Rect.anchorMax; Pivot = Rect.pivot;
        }
        internal void Restored()
        {
            Check(Rect.localPosition == Position && Rect.localScale == Scale && Rect.localRotation == Rotation,
                "card capture restores the original root pose and scale before native input");
            Check(Rect.sizeDelta == Size && Rect.anchorMin == Minimum && Rect.anchorMax == Maximum && Rect.pivot == Pivot,
                "card depth never changes original card layout anchors size or pivot");
        }
    }

    private static Image Image(RectTransform parent, string name, Vector2 size, Vector2 position)
    {
        GameObject go = Go(name, parent); go.layer = 5;
        var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
        Image image = go.AddComponent<Image>(); image.color = new Color(.04f, .15f, 1f, 1f); return image;
    }
    private static TextMeshProUGUI Text(RectTransform parent, string name, string value, Vector2 size, Vector2 position)
    {
        GameObject go = Go(name, parent); go.layer = 5;
        var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
        var text = go.AddComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset;
        text.text = value; text.fontSize = 26; text.color = Color.white; text.enableWordWrapping = false;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text;
    }
    private readonly struct Ink
    {
        internal readonly Vector2 Center;
        internal readonly int Count;
        internal Ink(Vector2 center, int count) { Center = center; Count = count; }
    }
    private static Ink White(Texture2D image)
    {
        double x = 0, y = 0, coverage = 0; int n = 0; Color32[] p = image.GetPixels32();
        for (int iy = 0; iy < image.height; iy++) for (int ix = 0; ix < image.width; ix++)
        {
            Color32 c = p[iy * image.width + ix];
            // The controlled original artwork has red=10 and the other action
            // ink is green. Integrate the title's continuous white coverage:
            // thresholded thin TMP stems change their pixel count asymmetrically
            // under subpixel projection, moving a binary centroid by >2 pixels.
            double weight = Math.Max(0, (c.r - 10d) / 245d);
            if (weight <= 0) continue;
            x += (ix + .5) * weight; y += (iy + .5) * weight; coverage += weight; n++;
        }
        Check(n > 30, "the complete native TMP card title remains visible in the final head raster");
        return new Ink(new Vector2((float)(x / coverage / image.width), (float)(y / coverage / image.height)), n);
    }
    private static Vector3 OnPlane(Camera camera, Vector2 uv, Transform plane)
    {
        Ray ray = camera.ViewportPointToRay(uv); var p = new Plane(plane.forward, plane.position);
        Check(p.Raycast(ray, out float distance), "independent original-plane calibration intersects the viewing ray"); return ray.GetPoint(distance);
    }

    private sealed partial class Scene : IDisposable
    {
        private readonly int _path;
        private readonly RectTransform _root, _glass, _screen;
        private readonly Canvas _canvas;
        private readonly Camera _ui, _head;
        private readonly RenderTexture _left, _right, _final;
        private readonly RawImage _pane;
        private readonly MenuDepthDisplay? _display;
        private readonly Material? _desktopMaterial;
        private readonly Camera.CameraCallback _selection;
        private readonly Camera.CameraCallback _finished;
        private readonly Action<Camera> _coordinator;
        private readonly Card[] _cards;
        private bool _rightEye;
        private readonly string _name;
        private int _request, _completed, _traced = -1;
        internal Scene(int path, bool headRaster = false)
        {
            MenuDepthDriver.Shutdown(); _path = path; _name = new[] { "direct", "supersampled", "desktop" }[path];
            var root = Go("original flat menu capture"); root.layer = 5; _root = (RectTransform)root.transform;
            _root.sizeDelta = new Vector2(1200, 900); _root.localScale = Vector3.one * .001f;
            _canvas = root.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.WorldSpace;
            _head = Go("actual head camera").AddComponent<Camera>(); _head.enabled = true; _head.fieldOfView = 52;
            _head.nearClipPlane = .01f; _head.farClipPlane = 500; _head.aspect = 4f / 3f;
            _head.clearFlags = CameraClearFlags.SolidColor; _head.backgroundColor = Color.black; _head.cullingMask = 1 << (path == 0 ? 5 : 6);
            _ui = Go("original UI camera").AddComponent<Camera>(); _ui.enabled = false; _ui.nearClipPlane = .01f; _ui.farClipPlane = 1000;
            _ui.orthographic = true; _ui.orthographicSize = .45f; _ui.transform.position = Vector3.back;
            _ui.clearFlags = CameraClearFlags.SolidColor; _ui.backgroundColor = Color.black; _ui.cullingMask = 1 << 5;
            _ui.stereoTargetEye = StereoTargetEyeMask.None; _head.stereoTargetEye = StereoTargetEyeMask.None;
            _left = Target(1024, 768); _right = Target(1024, 768); _final = Target(960, 720);
            _head.targetTexture = _final; _ui.targetTexture = _left;
            _screen = (RectTransform)Go("physical original unit-quad screen").transform; _screen.localScale = new Vector3(1.2f, .9f, 1f);
            _glass = (RectTransform)Go("physical original glass").transform; _glass.sizeDelta = new Vector2(1.2f, .9f);
            _glass.gameObject.layer = 6; var glassCanvas = _glass.gameObject.AddComponent<Canvas>(); glassCanvas.renderMode = RenderMode.WorldSpace;
            _pane = _glass.gameObject.AddComponent<RawImage>(); _pane.texture = _left;
            if (path == 1) _display = new MenuDepthDisplay(_pane, _left, _left, headRaster: headRaster);
            if (headRaster) { _ui.allowHDR = _head.allowHDR = false; _ui.allowMSAA = _head.allowMSAA = false; }
            if (path == 2)
            {
                _desktopMaterial = MenuDepthDisplay.CreateDisplayMaterial(_left, _right); _pane.material = _desktopMaterial;
                _ui.orthographic = true; _ui.orthographicSize = 5; _ui.transform.position = new Vector3(50, 30, 90);
                _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = _ui; _canvas.planeDistance = 7;
            }
            else CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = _root });
            _cards = new[] { new Card(typeof(FullAbilityCard), _root, 0), new Card(typeof(MiniAbilityCard), _root, 1), new Card(typeof(ItemCardUI), _root, 2),
                new Card(typeof(MonsterRoundCardUI), _root, 2), new Card(typeof(MonsterBaseUI), _root, 2),
                new Card(typeof(UIPerkAttackModifier), _root, 2), new Card(typeof(UIScenarioAttackModifier), _root, 2) };
            foreach (Card card in _cards) card.Rect.gameObject.layer = 5;
            _selection = camera =>
            {
                if (camera != _head) return;
                if (_path != 0) Shader.SetGlobalFloat("_GvrMenuDepthEye", _rightEye ? 1f : 0f);
                if (_traced == _request) return;
                _traced = _request;
                foreach (Card card in _cards)
                {
                    MenuDepthCardPlane marker = card.Rect.GetComponentInChildren<MenuDepthCardPlane>(true);
                    File.AppendAllText(Path.Combine(_output, "head-callback-state.txt"), _name + "," + card.Owner.GetType().Name
                        + ",active=" + card.Rect.gameObject.activeInHierarchy + ",pose=" + card.Rect.localPosition
                        + ",markerReady=" + (marker != null && marker.IsReady) + ",markerCull=" + (marker != null && marker.Graphic.canvasRenderer.cull)
                        + ",alpha=" + (marker != null ? marker.Graphic.canvasRenderer.GetInheritedAlpha() : -1f) + ",headMatch=" + ReferenceEquals(VRRigDriver.HeadCamera, _head)
                        + ",rootScale=" + _root.localScale.ToString("F6") + ",rootWorld=" + _root.position.ToString("F6")
                        + ",cardWorld=" + card.Rect.position.ToString("F6") + ",cardZ=" + card.Rect.TransformVector(Vector3.forward).ToString("F6")
                        + ",cardX=" + card.Rect.TransformVector(Vector3.right).ToString("F6") + ",head=" + _head.transform.position.ToString("F6") + "\n");
                }
            };
            Camera.onPreRender += _selection;
            _coordinator = camera =>
            {
                if (camera != _head) return;
                Vector3 eye = _head.transform.position - (_rightEye ? _head.transform.right * .064f : Vector3.zero);
                if (_path == 1 && headRaster)
                    _display!.RenderHead(_ui, _root, _root.rect, _left,
                        new MenuDepthEye(false, eye, _head), new MenuDepthEye(true, eye + _head.transform.right * .064f, _head));
                else if (_path == 1)
                    _display!.Render(_ui, _root, _root.rect, _left, _left,
                        new MenuDepthEye(false, eye, _head), new MenuDepthEye(true, eye + _head.transform.right * .064f, _head));
                else if (_path == 2)
                { CaptureNative(eye, _left); CaptureNative(eye + _head.transform.right * .064f, _right); }
            };
            MenuDepthSources.BeforeHeadCapture += _coordinator;
            _finished = camera => { if (camera == _head) _completed = _request; };
            Camera.onPostRender += _finished;
            VRRigDriver.HeadCamera = path == 0 ? _head : null;
            // The real head also owns offscreen coordinator dispatch in these
            // explicit XR-input fixtures; native screen-space sources are still
            // excluded from the direct path by their actual Canvas render mode.
            VRRigDriver.HeadCamera = _head;
            MenuDepthDriver.Tick();
        }

        private static RenderTexture Target(int width, int height) { var t = new RenderTexture(width, height, 24); t.Create(); return t; }

        internal IEnumerator Prove()
        {
            foreach (Card card in _cards)
            {
                card.Rect.gameObject.SetActive(true);
                MenuDepthDriver.Tick();
                var marker = card.Rect.GetComponentInChildren<MenuDepthCardPlane>();
                Check(marker != null && marker.IsReady, "exact native full mini and item card roots receive an active plane source");
                Check(marker!.DepthGraphics.Count == card.Backgrounds.Count,
                    "native card depth silhouettes use the exact original background graphics including split ability backing");
                _head.transform.SetPositionAndRotation(new Vector3(.25f, .08f, -.60f), Quaternion.LookRotation(new Vector3(-.25f, -.08f, .60f), Vector3.up));
                ++_request; while (_completed != _request) yield return null;
                Texture2D first = Read(card, true, 99); card.Restored();
                int dimensions = card.Witness.Dimensions, parents = card.Witness.Parents;
                Vector3[] eyes = { new Vector3(.25f, .08f, -.60f), new Vector3(-.27f, -.03f, -.68f), new Vector3(.1f, .05f, -1.4f) };
                for (int pose = 0; pose < eyes.Length; pose++)
                {
                    _head.transform.SetPositionAndRotation(eyes[pose], Quaternion.LookRotation(-eyes[pose], Vector3.up));
                    marker.enabled = false; ++_request; while (_completed != _request) yield return null;
                    Texture2D flat = Read(card, false, pose); Ink baseline = White(flat);
                    Vector3 point = OnPlane(_head, baseline.Center, _path == 0 ? _root : _glass);
                    float width = _path == 2 ? _ui.WorldToViewportPoint(card.Rect.TransformPoint(new Vector3(card.Rect.rect.xMax, 0))).x
                        - _ui.WorldToViewportPoint(card.Rect.TransformPoint(new Vector3(card.Rect.rect.xMin, 0))).x : card.Rect.rect.width * card.Rect.TransformVector(Vector3.right).magnitude;
                    float depth = (card.Owner is ItemCardUI || card.Owner is FullAbilityCard ? .104f : .056f) * width * (_path == 2 ? 1.2f : 1f);
                    Vector3 expected = _head.WorldToViewportPoint(point - Vector3.forward * depth);
                    marker.enabled = true; ++_request; while (_completed != _request) yield return null;
                    Texture2D pixels = Read(card, true, pose); Ink actual = White(pixels);
                    float error = Vector2.Scale(actual.Center - new Vector2(expected.x, expected.y), new Vector2(pixels.width, pixels.height)).magnitude;
                    File.AppendAllText(Path.Combine(_output, "geometry.csv"), _name + "," + card.Owner.GetType().Name + "," + pose + "," + baseline.Center.ToString("F6") + "," + actual.Center.ToString("F6") + "," + expected.ToString("F6") + ",depth=" + depth.ToString("F6") + ",error=" + error + "\n");
                    Check(error < 1.8f, "the complete original card text follows the independently specified raised physical plane");
                    if ((card.Owner is ItemCardUI || card.Owner is FullAbilityCard) && pose == 0)
                    {
                        Vector3 prior = _head.WorldToViewportPoint(point - Vector3.forward * (depth * (.035f / .104f)));
                        float priorDisplacement = Vector2.Scale(new Vector2(prior.x, prior.y) - baseline.Center,
                            new Vector2(pixels.width, pixels.height)).magnitude;
                        float displacement = Vector2.Scale(actual.Center - baseline.Center, new Vector2(pixels.width, pixels.height)).magnitude;
                        Check(displacement > priorDisplacement * 1.6f && Error(actual, prior, pixels) > 2f,
                            "native full/item card relief is visibly stronger than the prior 3.5-percent plane");
                    }
                    if (pose == 0)
                    {
                        Ink firstInk = White(first);
                        Check(Vector2.Scale(firstInk.Center - new Vector2(expected.x, expected.y), new Vector2(first.width, first.height)).magnitude < 1.8f,
                            "the first actual automatic head frame already renders the complete raised card");
                        Object.DestroyImmediate(first);
                    }
                    Check(Vector2.Scale(actual.Center - baseline.Center, new Vector2(pixels.width, pixels.height)).magnitude > .4f,
                        "oblique head motion visibly lifts original card content off the flat menu plane");
                    Object.DestroyImmediate(flat); Object.DestroyImmediate(pixels); card.Restored();
                }
                Check(card.Witness.Dimensions == dimensions && card.Witness.Parents == parents,
                    "capture-scoped card depth invokes no native dimension or parent-change callbacks");
                Check(card.Clicks == 0, "capturing original card controls does not invoke their actions");
                card.Button.onClick.Invoke(); Check(card.Clicks == 1, "the unchanged original card button still invokes its original callback once");
                card.Rect.gameObject.SetActive(false); MenuDepthDriver.Tick();
                Check(!marker.IsReady, "an inactive native card never retains a live depth capture offer");
            }
            IEnumerator lifecycle = ProveLifecycle();
            while (lifecycle.MoveNext()) yield return lifecycle.Current;
            IEnumerator safety = ProveSafety();
            while (safety.MoveNext()) yield return safety.Current;
        }

        private Vector3 Expected(Card card, Ink baseline)
            => Expected(card, baseline, card.Owner is ItemCardUI || card.Owner is FullAbilityCard ? .104f : .056f);

        private Vector3 Expected(Card card, Ink baseline, float ratio)
        {
            Vector3 point = OnPlane(_head, baseline.Center, _path == 0 ? _root : _glass);
            float width = _path == 2 ? (_ui.WorldToViewportPoint(card.Rect.TransformPoint(new Vector3(card.Rect.rect.xMax, 0))).x
                - _ui.WorldToViewportPoint(card.Rect.TransformPoint(new Vector3(card.Rect.rect.xMin, 0))).x) * 1.2f
                : card.Rect.rect.width * card.Rect.TransformVector(Vector3.right).magnitude;
            return _head.WorldToViewportPoint(point - Vector3.forward * (ratio * width));
        }
        private static float Error(Ink ink, Vector3 expected, Texture2D pixels) =>
            Vector2.Scale(ink.Center - new Vector2(expected.x, expected.y), new Vector2(pixels.width, pixels.height)).magnitude;

        private IEnumerator ProveLifecycle()
        {
            _head.transform.SetPositionAndRotation(new Vector3(.25f, .08f, -.60f), Quaternion.LookRotation(new Vector3(-.25f, -.08f, .60f), Vector3.up));
            foreach (Type type in new[] { typeof(FullAbilityCard), typeof(ItemCardUI) })
            {
                // Native pool activation, late field/TMP input and readonly previews
                // arrive after the last menu Tick. The real capture pump must admit
                // them before their first draw without an extra global/root scan.
                var card = new Card(type, _root, 3); card.Title.text = "LATE NATIVE CARD";
                card.Owner.enabled = false; card.Rect.gameObject.SetActive(true);
                int censuses = MenuDepthDriver.RootCensuses, visits = MenuDepthDriver.BehaviourVisits;
                ++_request; while (_completed != _request) yield return null;
                var marker = card.Rect.GetComponentInChildren<MenuDepthCardPlane>();
                Check(marker != null && marker.IsReady, "late pooled readonly native cards bind before their first capture without another menu tick");
                Check(marker!.DepthGraphics.Count == card.Backgrounds.Count, "action-only native full-card halves retain every exact printed target Graphic");
                Texture2D first = Read(card, true, 100);
                marker.enabled = false; ++_request; while (_completed != _request) yield return null;
                Texture2D flat = Read(card, false, 100); Vector3 expected = Expected(card, White(flat));
                Check(Error(White(first), expected, first) < 1.8f, "new pooled full and item popups already lift all late original text on their first draw");
                Object.DestroyImmediate(first); Object.DestroyImmediate(flat); card.Restored(); marker.enabled = true;

                // Native pooled art references can be filled or replaced after its
                // owner became visible. An actual Graphic birth renews just this
                // owner's typed silhouettes; the marker and native contents survive.
                Image replacement = Image(card.Rect, "late original art replacement", new Vector2(35, 35), new Vector2(-115, -200));
                if (type == typeof(ItemCardUI)) Field(card.Owner, "cardBackground", replacement);
                else Field(card.Owner, "headerImage", replacement);
                ++_request; while (_completed != _request) yield return null;
                bool found = false; foreach (Graphic background in marker.DepthGraphics) found |= background == replacement;
                Check(found, "late native background assignment refreshes exact depth silhouettes before capture");
                Check(card.Rect.GetComponentsInChildren<MenuDepthCardPlane>().Length == 1, "pooled reuse never creates a second card extrusion");
                Check(MenuDepthDriver.RootCensuses == censuses && MenuDepthDriver.BehaviourVisits == visits,
                    "late cards and graphics never repeat a whole window hierarchy census");
                card.Restored();
                if (type == typeof(ItemCardUI))
                {
                    Object.DestroyImmediate(replacement.gameObject);
                    Check(!MenuDepthSources.HasContent(_root), "a destroyed last native backing is pruned without leaving a visible source");
                    replacement = Image(card.Rect, "new native backing after pruning", new Vector2(294, 450), Vector2.zero);
                    replacement.transform.SetAsFirstSibling(); Field(card.Owner, "cardBackground", replacement);
                    ++_request; while (_completed != _request) yield return null;
                    Check(MenuDepthSources.HasContent(_root), "a live pooled card re-enters capture membership when its destroyed native art is replaced");
                    Texture2D renewed = Read(card, true, 111);
                    marker.enabled = false; ++_request; while (_completed != _request) yield return null;
                    Texture2D renewedFlat = Read(card, false, 111);
                    Check(Error(White(renewed), Expected(card, White(renewedFlat)), renewed) < 1.8f,
                        "a replaced original item backing restores complete card depth on its first new capture");
                    Object.DestroyImmediate(renewed); Object.DestroyImmediate(renewedFlat); marker.enabled = true;
                }

                card.Rect.gameObject.SetActive(false); ++_request; while (_completed != _request) yield return null;
                Check(!marker.IsReady, "a hidden pooled card contributes no capture source"); card.Restored();
                card.Title.text = "REUSED ORIGINAL CARD"; card.Rect.gameObject.SetActive(true);
                ++_request; while (_completed != _request) yield return null;
                Check(marker.IsReady && card.Rect.GetComponentsInChildren<MenuDepthCardPlane>().Length == 1,
                    "native pooled cards reuse the same complete presentation after reveal");
                Text(card.Rect, "late native CreateLayout TMP child", "NEW ORIGINAL EFFECT", new Vector2(270, 40), new Vector2(0, 80));
                ++_request; while (_completed != _request) yield return null;
                Texture2D reusedRaised = Read(card, true, 110);
                marker.enabled = false; ++_request; while (_completed != _request) yield return null;
                Texture2D reusedFlat = Read(card, false, 110);
                Check(Error(White(reusedRaised), Expected(card, White(reusedFlat)), reusedRaised) < 1.8f,
                    "reused cards lift newly generated native TMP children without another owner or Image birth");
                Object.DestroyImmediate(reusedRaised); Object.DestroyImmediate(reusedFlat); marker.enabled = true;

                var physical = (RectTransform)Go("independent physical board hand or NPC card canvas").transform;
                physical.localScale = Vector3.one * .001f; physical.gameObject.layer = 5;
                physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                card.Rect.SetParent(physical, false);
                ++_request; while (_completed != _request) yield return null;
                Check(!marker.IsReady, "reparenting a pooled native card to a physical canvas revokes flat-menu extrusion"); card.Restored();
                var untouched = new Card(type, physical, 4); untouched.Rect.gameObject.SetActive(true);
                ++_request; while (_completed != _request) yield return null;
                Check(untouched.Rect.GetComponentInChildren<MenuDepthCardPlane>() == null,
                    "new native board hand NPC or card-bake cards never acquire a menu extrusion");
                untouched.Rect.gameObject.SetActive(false); card.Rect.SetParent(_root, false);
                ++_request; while (_completed != _request) yield return null;
                Check(marker.IsReady, "an original popup regains its single depth source when returned to its flat window"); card.Restored();

                // Scope failure and ordinary visibility must leave native state
                // intact even though a readonly native controller stays disabled.
                foreach (Graphic background in marker.DepthGraphics) background.enabled = false;
                Check(!marker.IsReady, "active native card controllers with no printed graphics are not visible sources");
                foreach (Graphic background in marker.DepthGraphics) background.enabled = true;
                var group = card.Rect.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0;
                Check(!marker.IsReady, "a transparent pooled native card is not a capture source"); group.alpha = 1;
                MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(false, _head.transform.position, _head, true));
                try { throw new InvalidOperationException("named original caller failure"); }
                catch (InvalidOperationException) { }
                finally { MenuDepthSources.AfterCapture(); }
                card.Restored(); Check(card.Clicks == 0, "birth reuse and capture cleanup do not invoke native card controls");
                card.Rect.gameObject.SetActive(false);
            }
        }

        private Texture2D Read(Card card, bool raised, int pose)
        {
                RenderTexture previous = RenderTexture.active; RenderTexture.active = _final;
                var image = new Texture2D(_final.width, _final.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply(); RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(_output, _name + "-" + card.Owner.GetType().Name + "-" + pose + "-" + (raised ? "raised" : "flat") + ".png"), image.EncodeToPNG());
                return image;
        }
        private void CaptureNative(Vector3 eye, RenderTexture target)
        {
            Check(MenuDepthScreenProjection.TryMap(_screen, _ui, _root, eye, out Vector3 nativeEye),
                "the exact native screen mapper supplies the card capture eye");
            _ui.targetTexture = target;
            MenuDepthSources.BeforeCapture(_root, new MenuDepthEye(false, nativeEye, _head, true));
            try { Canvas.ForceUpdateCanvases(); _ui.Render(); }
            finally { MenuDepthSources.AfterCapture(); }
        }

        public void Dispose()
        {
            Camera.onPreRender -= _selection; _display?.Dispose();
            Camera.onPostRender -= _finished; MenuDepthSources.BeforeHeadCapture -= _coordinator;
            if (_desktopMaterial != null) MenuDepthDisplay.ReleaseDisplayMaterial(_desktopMaterial);
            MenuDepthDriver.Shutdown(); CanvasConversion.ActivePanels.Clear(); VRRigDriver.HeadCamera = null;
            foreach (RenderTexture target in new[] { _left, _right, _final }) { target.Release(); Object.DestroyImmediate(target); }
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
        }
    }
}

public sealed class NativePoseWitness : MonoBehaviour
{
    public int Dimensions, Parents;
    private void OnRectTransformDimensionsChange() => Dimensions++;
    private void OnTransformParentChanged() => Parents++;
}
