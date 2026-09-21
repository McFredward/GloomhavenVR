using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class GameplayFixture : MonoBehaviour
{
    internal static int Awakes, Enables;
    private void Awake() { Awakes++; }
    private void OnEnable() { Enables++; }
}

public static class MirrorProgram
{
    private static readonly List<GameObject> Objects = new();
    private static readonly List<Object> Assets = new();
    private static readonly Dictionary<ushort, byte[]> Baselines = new();
    private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _assertions;
    private static string _output = "";
    private static Camera _camera = null!;
    private static TextMeshProUGUI _text = null!;
    private static Image _fill = null!;
    private static CanvasGroup _group = null!;
    private static RectMask2D _clip = null!;
    private static void Check(bool good, string message)
    { _assertions++; if (!good) throw new InvalidOperationException(message); }
    private static GameObject Go(string name, Transform? parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false); else Objects.Add(go);
        return go;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)Go(name, parent).transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static Image Image(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    { var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>(); image.color = color; return image; }
    private static Transform Source(Transform shared)
    {
        var root = Rect("Original", shared, new Vector2(25, -10), new Vector2(400, 260));
        root.localScale = Vector3.one * .01f;
        root.localRotation = Quaternion.Euler(0, 0, 7);
        // Rect anchoredPosition is in the shared frame's units; keep the module comfortably near origin.
        root.localPosition = new Vector3(.15f, -.1f, 0);
        var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _camera;
        root.gameObject.AddComponent<GraphicRaycaster>();
        _group = root.gameObject.AddComponent<CanvasGroup>(); _group.alpha = .83f;
        root.gameObject.AddComponent<GameplayFixture>(); root.gameObject.AddComponent<BoxCollider>();
        Image("Background", root, Vector2.zero, new Vector2(390, 250), new Color(.12f, .18f, .23f, .95f));
        _text = Rect("Name", root, new Vector2(0, 86), new Vector2(360, 50)).gameObject.AddComponent<TextMeshProUGUI>();
        _text.font = TMP_Settings.defaultFontAsset;
        _text.text = "Owner A <b>sample</b>"; _text.fontSize = 30; _text.alignment = TextAlignmentOptions.Center;
        _text.color = new Color(.95f, .84f, .62f, 1);
        _text.enableWordWrapping = true;
        var legacy = Rect("Price", root, new Vector2(-88, 38), new Vector2(150, 40)).gameObject.AddComponent<Text>();
        legacy.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); legacy.text = "5 gold";
        legacy.fontSize = 24; legacy.alignment = TextAnchor.MiddleCenter; legacy.color = new Color(.9f, .75f, .3f, 1);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "fixture checker", filterMode = FilterMode.Point };
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow }); texture.Apply(); Assets.Add(texture);
        TownServiceMirror.Assets.Register("fixture/checker", texture);
        var sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 1); sprite.name = "fixture sprite"; Assets.Add(sprite);
        TownServiceMirror.Assets.Register("fixture/sprite", sprite);
        _fill = Image("Filled", root, new Vector2(94, 33), new Vector2(110, 32), new Color(.3f, .9f, .6f, .85f));
        _fill.sprite = sprite; _fill.type = UnityEngine.UI.Image.Type.Filled; _fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; _fill.fillAmount = .65f;
        _fill.gameObject.AddComponent<Button>();
        var raw = Rect("Raw", root, new Vector2(109, -47), new Vector2(96, 63)).gameObject.AddComponent<RawImage>();
        raw.texture = texture; raw.uvRect = new UnityEngine.Rect(1, 0, -1, 1); raw.color = new Color(1, 1, 1, .7f);

        var viewport = Rect("Viewport", root, new Vector2(-92, -50), new Vector2(100, 72));
        _clip = viewport.gameObject.AddComponent<RectMask2D>();
        _clip.padding = new Vector4(5, 4, 3, 2); _clip.softness = new Vector2Int(2, 3);
        Image("Oversized", viewport, new Vector2(24, 8), new Vector2(130, 120), new Color(.2f, .75f, .95f, .8f));
        var stencil = Image("Stencil", root, new Vector2(65, -96), new Vector2(60, 24), Color.white);
        stencil.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        Image("StencilChild", stencil.transform, new Vector2(24, 0), new Vector2(70, 20), Color.magenta);
        Image("OrderBack", root, new Vector2(144, 90), new Vector2(32, 25), Color.red);
        Image("OrderFront", root, new Vector2(152, 91), new Vector2(32, 25), Color.blue);
        var mesh = new Mesh { name = "fixture original handle mesh" };
        mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds(); Assets.Add(mesh);
        var material = new Material(Shader.Find("Unlit/Color")) { name = "fixture handle material", color = new Color(.8f, .3f, .15f, 1) }; Assets.Add(material);
        TownServiceMirror.Assets.Register("fixture/handle-material", material);
        var handle = Go("HandleMesh", root); handle.transform.localPosition = new Vector3(-166, 91, -1); handle.transform.localScale = new Vector3(20, 15, 1);
        handle.AddComponent<MeshFilter>().sharedMesh = mesh; handle.AddComponent<MeshRenderer>().sharedMaterial = material;
        return root;
    }

    private static TownServiceBinding? Remote(int peer, ushort module = 10)
    {
        var owners = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        if (!owners.Contains(peer)) return null;
        var modules = (IDictionary)owners[peer]!;
        if (!modules.Contains(module)) return null;
        object value = modules[module]!;
        return (TownServiceBinding)value.GetType().GetField("Binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    }
    private static List<byte[]> Capture()
    {
        var packets = new List<byte[]>();
        TownServiceMirror.Capture((packet, length) =>
        {
            Check(length == packet.Length, "capture returns complete immutable packets");
            Check(TownServiceCodec.TryRead(packet, length, out TownServiceFrame? frame), "captured packet decodes");
            Check(frame != null, "decoded frame exists"); packets.Add(packet);
            if (frame!.BaseSequence == 0 && frame.Module != TownServiceFrame.ManifestModule) Baselines[frame.Module] = packet;
        });
        return packets;
    }
    private static void Receive(int peer, IEnumerable<byte[]> packets)
    { foreach (byte[] packet in packets) Check(TownServiceMirror.Receive(peer, packet, packet.Length), "mirror accepts encoded owner packet"); }
    private static void Layer(Transform root, int layer)
    { foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer; }
    private static Color32[] Render(Transform root, int layer, string name)
    {
        // Isolate this fixture surface, including cases where two owners share a frame.
        // Pose/layer/camera contracts are asserted before this camera-only image isolation.
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(root, layer);
        foreach (Canvas ancestor in root.GetComponentsInParent<Canvas>(true)) ancestor.gameObject.layer = layer;
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = _camera;
        _camera.cullingMask = 1 << layer;
        _camera.transform.SetPositionAndRotation(root.position - root.forward * 10, root.rotation);
        _camera.orthographicSize = 1.55f * root.lossyScale.y / .01f;
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(512, 384, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(512, 384, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = rt; _camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new UnityEngine.Rect(0, 0, 512, 384), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally { RenderTexture.active = null; _camera.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
    }
    private static void ComparePixels(Transform source, Transform target, string name)
    {
        // Camera recentering below must never hide a wrong world pose.
        Transform sourceFrame = source, targetFrame = target;
        while (sourceFrame.parent != null) sourceFrame = sourceFrame.parent;
        while (targetFrame.parent != null) targetFrame = targetFrame.parent;
        Vector3 expectedPosition = targetFrame.TransformPoint(sourceFrame.InverseTransformPoint(source.position));
        Quaternion expectedRotation = targetFrame.rotation * Quaternion.Inverse(sourceFrame.rotation) * source.rotation;
        Vector3 s = source.lossyScale, aScale = sourceFrame.lossyScale, bScale = targetFrame.lossyScale;
        Vector3 expectedScale = new Vector3(s.x / aScale.x * bScale.x, s.y / aScale.y * bScale.y, s.z / aScale.z * bScale.z);
        Check(Vector3.Distance(target.position, expectedPosition) < .00002f, "world position before image recenter: " + name);
        Check(Quaternion.Angle(target.rotation, expectedRotation) < .02f, "world rotation before image recenter: " + name);
        Check(Vector3.Distance(target.lossyScale, expectedScale) < .00002f, "world scale before image recenter: " + name);
        Color32[] a = Render(source, 8, name + "-owner"), b = Render(target, 9, name + "-observer");
        foreach (Transform root in new[] { source, target })
            foreach (RectMask2D mask in root.GetComponentsInChildren<RectMask2D>(true))
            {
                var graphic = mask.GetComponentInChildren<Graphic>();
                File.AppendAllText(Path.Combine(_output, "clipping.txt"), root.name + " " + mask.name + " canvasRect=" + mask.canvasRect
                    + " graphicCanvas=" + graphic.canvas.name + " rootCanvas=" + graphic.canvas.rootCanvas.name
                    + " rootScale=" + graphic.canvas.rootCanvas.transform.lossyScale + " cull=" + graphic.canvasRenderer.cull + "\n");
            }
        foreach (Transform root in new[] { source, target })
        {
            File.AppendAllText(Path.Combine(_output, "siblings.txt"), name + " " + root.name + ": ");
            for (int i = 0; i < root.childCount; i++) File.AppendAllText(Path.Combine(_output, "siblings.txt"), i + "=" + root.GetChild(i).name + "; ");
            File.AppendAllText(Path.Combine(_output, "siblings.txt"), "\n");
        }
        ComparePixels(a, b, name);
    }
    private static void ComparePixels(Color32[] a, Color32[] b, string name, bool blank = false)
    {
        int ink = 0, changed = 0, max = 0; long total = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(a[0])) ink++;
            int difference = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
            if (difference != 0) changed++; total += difference; max = Math.Max(max, difference);
        }
        File.AppendAllText(Path.Combine(_output, "pixels.txt"), name + ": ink=" + ink + ", changed=" + changed + ", total-channel-error=" + total + ", max=" + max + "\n");
        Check(blank ? ink == 0 : ink > 15000, "render contains expected UI pixels");
        // Same engine and assets should render identically. Allow only sub-byte average error
        // from relocating the camera/shared frame, not missing text, masks or whole widgets.
        Check(total <= a.Length / 50 && max <= 20, "owner and observer rendered UI match: " + name);
    }
    private static void Inert(TownServiceBinding binding, int awakes, int enables)
    {
        Check(binding.Root.GetComponentsInChildren<GameplayFixture>(true).Length == 0, "clone has no gameplay controller");
        Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables, "cloning never executes gameplay Awake or OnEnable");
        Check(binding.Root.GetComponentsInChildren<Selectable>(true).Length == 0, "clone has no native selectable callback");
        Check(binding.Root.GetComponentsInChildren<Collider>(true).Length == 0, "clone has no physics input collider");
        foreach (Graphic graphic in binding.Root.GetComponentsInChildren<Graphic>(true)) Check(!graphic.raycastTarget, "clone graphic raycasts are disabled");
        foreach (CanvasGroup group in binding.Root.GetComponentsInChildren<CanvasGroup>(true))
            Check(!group.interactable && !group.blocksRaycasts, "clone group input is disabled");
    }

    private static IEnumerator Motion(Transform source, Transform shared, Transform observer, TownServiceBinding copy)
    {
        Vector3 start = source.localPosition, startScale = source.localScale;
        Quaternion startRotation = source.localRotation;
        float startAlpha = _group.alpha;
        Color startColor = _text.color;
        Vector2 startSize = _fill.rectTransform.sizeDelta;
        Action<float> phase = t =>
        {
            source.localPosition = Vector3.Lerp(start, start + new Vector3(.4f, .12f, 0), t);
            source.localScale = Vector3.Lerp(startScale, startScale * 1.2f, t);
            source.localRotation = Quaternion.Slerp(startRotation, startRotation * Quaternion.Euler(0, 0, 12), t);
            _group.alpha = Mathf.Lerp(startAlpha, .92f, t);
            _text.color = Color.Lerp(startColor, new Color(.9f, .3f, .5f, .95f), t);
            _fill.rectTransform.sizeDelta = Vector2.Lerp(startSize, startSize + new Vector2(12, 8), t);
        };
        phase(1); yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        var owners = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        object module = ((IDictionary)owners[1]!)[(ushort)10]!;
        object motion = module.GetType().GetField("Motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        Type type = motion.GetType();
        float began = (float)type.GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        float duration = (float)type.GetField("_duration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        Check(duration > 0 && duration <= .1f, "motion uses bounded native sample interval");
        var tick = type.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (float t in new[] { .5f, 1f })
        {
            // Production motion exposes its time argument; sample deterministic phases through
            // that method while the source fixture writes the matching native animation phase.
            float sampleTime = began + duration * t + (t == 1f ? .001f : 0f);
            float sampledPhase = Mathf.Clamp01((sampleTime - began) / duration);
            Check(Mathf.Abs(sampledPhase - t) < .0001f, "deterministic animation phase is representable by Unity float time");
            tick.Invoke(motion, new object[] { sampleTime }); phase(sampledPhase);
            Check(Vector3.Distance(copy.Root.position, observer.TransformPoint(shared.InverseTransformPoint(source.position))) < .00002f,
                "motion phase preserves owner world position");
            Check(Quaternion.Angle(copy.Root.rotation, observer.rotation * Quaternion.Inverse(shared.rotation) * source.rotation) < .02f,
                "motion phase preserves owner world rotation");
            Check(Vector3.Distance(copy.Root.lossyScale, source.lossyScale * 1.25f) < .00002f, "motion phase preserves owner scale");
            Check(Mathf.Abs(copy.Root.GetComponent<CanvasGroup>().alpha - _group.alpha) < .00002f, "motion phase preserves owner alpha");
            ComparePixels(source, copy.Root, t == .5f ? "animation-half" : "animation-end");
        }
    }

    private static IEnumerator Lifecycle(Transform source, Transform shared, Transform observer, TownServiceBinding copy)
    {
        for (float wait = Time.unscaledTime + .11f; Time.unscaledTime < wait;) yield return null;
        Vector3 origin = source.localPosition;
        RectTransform child = (RectTransform)source.Find("Name");
        source.localPosition = origin + new Vector3(.7f, .2f, 0); _group.alpha = .24f;
        child.anchoredPosition += new Vector2(35, -12);
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        var owners = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        object module = ((IDictionary)owners[1]!)[(ushort)10]!;
        object motion = module.GetType().GetField("Motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        Type type = motion.GetType();
        float began = (float)type.GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        float duration = (float)type.GetField("_duration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        Check(Mathf.Abs(duration - .1f) < .00001f, "lifecycle probe starts a real 100ms interpolation");
        for (float quarter = began + duration * .25f; Time.unscaledTime < quarter;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        TownServiceMirror.TickRemote(_ => observer);
        var copyChild = (RectTransform)copy.Root.Find("Name");
        Check(Vector2.Distance(copyChild.anchoredPosition, child.anchoredPosition) > 10, "child is still between old and target positions before close");
        source.gameObject.SetActive(false); Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(!copy.Root.gameObject.activeInHierarchy, "hidden frame immediately hides the tweening module");
        source.localPosition = origin + new Vector3(-.35f, .08f, 0); _group.alpha = .91f;
        // Keep the child's owner-authored target unchanged: the binding legitimately skips it.
        // Cancel must restore the previous complete target before dropping interpolation state.
        source.gameObject.SetActive(true); Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        Check(Time.unscaledTime - began < .1f, "reopen occurs before the previous tween duration expires");
        Check(copy.Root.gameObject.activeInHierarchy, "fresh visible frame immediately reopens module");
        Action verify = () =>
        {
            Check(Vector2.Distance(copyChild.anchoredPosition, child.anchoredPosition) < .0001f,
                "reopen restores unchanged child target after interrupted tween");
            Check(Vector3.Distance(copy.Root.position, observer.TransformPoint(shared.InverseTransformPoint(source.position))) < .00002f,
                "old tween cannot overwrite reopened root pose");
            Check(Mathf.Abs(copy.Root.GetComponent<CanvasGroup>().alpha - _group.alpha) < .00001f,
                "old tween cannot overwrite reopened alpha");
        };
        verify();
        for (float until = Time.unscaledTime + .15f; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); verify(); yield return null; }
        ComparePixels(source, copy.Root, "interrupted-tween-reopen");
    }

    private static void Measure(string name, int iterations, Action action)
    {
        action();
        long calibration = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[8192]; GC.KeepAlive(probe);
        bool counterSupported = GC.GetAllocatedBytesForCurrentThread() > calibration;
        long heap = GC.GetTotalMemory(false); int collections = GC.CollectionCount(0);
        long bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        clock.Stop(); bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        File.AppendAllText(Path.Combine(_output, "cost.txt"), name + ": iterations=" + iterations
            + ", total-ms=" + clock.Elapsed.TotalMilliseconds + ", thread-allocated-bytes=" + (counterSupported ? bytes.ToString() : "unavailable (zero calibration)")
            + ", heap-delta-bytes=" + (GC.GetTotalMemory(false) - heap) + ", gen0-collections=" + (GC.CollectionCount(0) - collections) + "\n");
    }
    private static void Cost(Transform source, Transform shared, Transform observer)
    {
        foreach (int peer in new[] { 1, 2, 3, 4 }) TownServiceMirror.RemovePeer(peer);
        var roots = new List<Transform> { source };
        for (int i = 1; i < 4; i++)
        {
            Transform clone = Object.Instantiate(source.gameObject, shared, false).transform;
            clone.localPosition += new Vector3(i * 5, 0, 0); roots.Add(clone);
        }
        for (int count = 2; count <= 4; count += 2)
        {
            TownServiceMirror.BeginSession(1, (uint)(800 + count), shared, source);
            for (int i = 0; i < count; i++) TownServiceMirror.RegisterModule((ushort)(10 + i), 1, roots[i]);
            Action<byte[], int> discard = (_, __) => { };
            Measure(count + " modules unchanged capture/read", 200, () => TownServiceMirror.Capture(discard));
            TownServiceMirror.RequestFullRefresh();
            Action<byte[], int> deliver = (packet, length) =>
            { for (int peer = 1; peer <= count; peer++) TownServiceMirror.Receive(peer, packet, length); };
            TownServiceMirror.Capture(deliver); TownServiceMirror.TickRemote(_ => observer);
            int revision = 0;
            Measure(count + " modules x " + count + " owners changed capture/codec/receive/apply", 50, () =>
            {
                revision++;
                for (int i = 0; i < count; i++) roots[i].Find("Name").GetComponent<TMP_Text>().text = "Bench " + revision + " owner " + i;
                TownServiceMirror.Capture(deliver); TownServiceMirror.TickRemote(_ => observer);
            });
            Measure(count + " modules x " + count + " owners unchanged playback", 1000, () => TownServiceMirror.TickRemote(_ => observer));
            for (int i = 0; i < count; i++) roots[i].gameObject.SetActive(false);
            Measure(count + " inactive modules capture", 200, () => TownServiceMirror.Capture(discard));
            for (int i = 0; i < count; i++) roots[i].gameObject.SetActive(true);
        }
        foreach (int peer in new[] { 1, 2, 3, 4 }) TownServiceMirror.RemovePeer(peer);
        for (int i = 1; i < roots.Count; i++) Object.DestroyImmediate(roots[i].gameObject);
        TownServiceMirror.EndSession();
        Measure("closed owner capture", 1000, () => TownServiceMirror.Capture((_, __) => { }));
    }

    private static IEnumerator Full(Transform source, Transform shared, Transform observer, TownServiceBinding copy,
        List<byte[]> baseline, int awakes, int enables)
    {
        // Dynamic presentation on existing original widgets must invalidate the sampler cache.
        source.Find("OrderBack").SetAsLastSibling();
        var shadow = source.Find("OrderFront").gameObject.AddComponent<Shadow>();
        shadow.effectColor = Color.yellow; shadow.effectDistance = new Vector2(4, -3);
        source.Find("Stencil").GetComponent<Mask>().showMaskGraphic = true;
        yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(copy.Root.Find("OrderBack").GetSiblingIndex() == source.Find("OrderBack").GetSiblingIndex(), "sibling reorder survives sampling");
        Check(copy.Root.Find("OrderFront").GetComponent<Shadow>() != null, "component addition invalidates sample cache");
        ComparePixels(source, copy.Root, "dynamic-order-component-mask");

        // Disabled root Canvas is presentation state, not permission to show a wrapper Canvas.
        source.GetComponent<Canvas>().enabled = false;
        yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(!copy.Root.GetComponent<Canvas>().enabled, "false Canvas remains disabled");
        ComparePixels(Render(source, 8, "disabled-owner"), Render(copy.Root, 9, "disabled-observer"), "disabled", true);
        source.GetComponent<Canvas>().enabled = true;
        yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }

        // Cumulative deltas must survive losing an intermediate update.
        _text.text = "LOST intermediate"; yield return null;
        List<byte[]> lost = Capture(); Check(lost.Count > 0, "lost delta was actually produced");
        _text.text = "Survived packet loss"; _fill.fillAmount = .91f; yield return null;
        List<byte[]> afterLoss = Capture(); Receive(1, afterLoss); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(copy.Root.Find("Name").GetComponent<TMP_Text>().text == _text.text, "cumulative delta converges after packet loss");
        ComparePixels(source, copy.Root, "packet-loss");
        foreach (byte[] packet in afterLoss)
        {
            TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? frame);
            if (frame!.Module != TownServiceFrame.ManifestModule)
            { Check(frame.BaseSequence != 0, "late baseline probe uses an actual delta"); Receive(2, new[] { packet }); }
        }
        foreach (byte[] packet in baseline)
        {
            TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? frame);
            if (frame!.Module == TownServiceFrame.ManifestModule) Receive(2, new[] { packet });
        }
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2) == null, "delta waits for its missing baseline");
        Receive(2, new[] { Baselines[10] });
        TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(Remote(2) != null, "late matching baseline creates pending owner module");
        Check(Remote(2)!.Root.Find("Name").GetComponent<TMP_Text>().text == _text.text, "late baseline expands the newer pending delta");
        ComparePixels(source, Remote(2)!.Root, "late-baseline");
        TownServiceMirror.RemovePeer(2);


        // Four independently captured owners share templates/assets, never mutable widget state.
        var frames = new Dictionary<int, Transform> { [1] = observer };
        var references = new Dictionary<int, Color32[]>();
        var texts = new Dictionary<int, string>();
        List<byte[]> ownerFour = null!;
        for (int peer = 1; peer <= 4; peer++)
        {
            if (peer != 1) { frames[peer] = Go("Observer " + peer).transform; frames[peer].position = new Vector3(peer * 12, 0, 0); }
            _text.text = "Owner " + peer + " unique"; texts[peer] = _text.text;
            _text.color = new Color(.15f * peer, 1 - .12f * peer, .3f + .1f * peer, 1);
            _fill.fillAmount = peer * .21f;
            TownServiceMirror.BeginSession(1, (uint)(200 + peer), shared, source);
            TownServiceMirror.RegisterModule(10, 1, source);
            yield return null;
            references[peer] = Render(source, 8, "owner-" + peer);
            List<byte[]> packets = Capture(); if (peer == 4) ownerFour = packets;
            Receive(peer, packets); TownServiceMirror.TickRemote(id => frames[id]);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(id => frames[id]); yield return null; }
        }
        for (int peer = 1; peer <= 4; peer++)
        {
            var remote = Remote(peer)!;
            Check(remote.Root.Find("Name").GetComponent<TMP_Text>().text == texts[peer], "owners retain independent text");
            Inert(remote, awakes, enables);
            ComparePixels(references[peer], Render(remote.Root, 9, "observer-" + peer), "four-owner-" + peer);
        }
        int transforms = Object.FindObjectsOfType<Transform>(true).Length;
        long allocation = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) TownServiceMirror.TickRemote(id => frames[id]);
        timer.Stop(); allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Check(Object.FindObjectsOfType<Transform>(true).Length == transforms, "four-owner steady ticks create no hierarchy objects");
        File.WriteAllText(Path.Combine(_output, "cost.txt"), "1000 ticks, 4 owners: " + timer.Elapsed.TotalMilliseconds + " ms (allocation counter requires calibration; see measurements below)\n");

        TownServiceBinding oldFour = Remote(4)!;
        _text.text = "Reopened owner 4";
        TownServiceMirror.BeginSession(1, 304, shared, source); TownServiceMirror.RegisterModule(10, 1, source);
        yield return null;
        Receive(4, Capture()); TownServiceMirror.TickRemote(id => frames[id]);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(id => frames[id]); yield return null; }
        Check(!ReferenceEquals(oldFour, Remote(4)), "reopen retires previous session binding");
        Receive(4, ownerFour); TownServiceMirror.TickRemote(id => frames[id]);
        Check(Remote(4)!.Root.Find("Name").GetComponent<TMP_Text>().text == _text.text, "late old session cannot overwrite reopened owner");
        TownServiceMirror.EndSession(); Receive(4, Capture()); TownServiceMirror.TickRemote(id => frames[id]);
        Check(Remote(4) == null && Remote(3) != null, "close removes only closed owner");
        TownServiceMirror.RemovePeer(3); Check(Remote(3) == null && Remote(2) != null, "peer loss removes only lost owner");
        var sessions = (IDictionary)typeof(TownServiceMirror).GetField("Sessions", PrivateStatic)!.GetValue(null)!;
        object session = sessions[2]!;
        session.GetType().GetField("ReceivedTime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, Time.unscaledTime - 11);
        TownServiceMirror.TickRemote(id => frames[id]);
        Check(Remote(2) == null && Remote(1) != null, "stale owner timeout preserves other owners");

        Cost(source, shared, observer);

        // A newly registered UI-only template drops the separate furniture handle.
        Object.DestroyImmediate(source.Find("HandleMesh").gameObject);
        // A child row is a separate network module, but must inherit its original parent
        // Canvas and group context rather than acquiring another clipping coordinate system.
        Transform row = source.Find("Viewport");
        Func<Transform, bool> excludeRow = node => node == row;
        TownServiceMirror.RegisterTemplate(1, 3, source, excludeRow);
        TownServiceMirror.RegisterTemplate(1, 4, row);
        TownServiceMirror.BeginSession(1, 390, shared, source);
        TownServiceMirror.RegisterModule(10, 3, source, excludeRow);
        TownServiceMirror.RegisterModule(11, 4, row);
        yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(Remote(1, 11) != null, "nested row module is instantiated");
        Check(Remote(1, 11)!.Root.IsChildOf(Remote(1)!.Root), "nested row retains native parent module");
        ComparePixels(source, Remote(1)!.Root, "nested-row-module");

        // Detach a real original sub-section from a larger root Canvas. Its local animated
        // scale differs from the Canvas pixel coordinate system; no substitute layout is allowed.
        foreach (int peer in new[] { 1, 2, 3, 4 }) TownServiceMirror.RemovePeer(peer);
        var outer = Rect("Native outer Canvas", shared, Vector2.zero, new Vector2(900, 600));
        outer.localScale = Vector3.one * .01f; outer.localRotation = Quaternion.Euler(0, 0, -3);
        outer.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Object.DestroyImmediate(source.GetComponent<GraphicRaycaster>());
        Object.DestroyImmediate(source.GetComponent<Canvas>());
        source.SetParent(outer, false); source.localScale = Vector3.one * .8f;
        source.localPosition = new Vector3(40, 20, 0); source.localRotation = Quaternion.Euler(0, 0, 10);
        TownServiceMirror.RegisterTemplate(1, 2, source);
        TownServiceMirror.BeginSession(1, 401, shared, source); TownServiceMirror.RegisterModule(10, 2, source);
        yield return null;
        Receive(1, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(Remote(1) != null, "standalone section without own Canvas mirrors");
        ComparePixels(source, Remote(1)!.Root, "outer-canvas-animated-scale");

    }

    private static void PublisherRouting()
    {
        // Sink/provenance mapping are fixtures; production Tick chooses every published input.
        var shared = Go("publisher-frame").transform;
        var window = Go("suppressed-native-merchant").AddComponent<GloomhavenVR.WorldUI.PublisherWindow>();
        var catalog = new GloomhavenVR.WorldUI.TownServiceCatalog { NavigationRoot = Go("page-navigation").transform };
        GloomhavenVR.WorldUI.TownServicePresentation.Window = window;
        GloomhavenVR.WorldUI.TownServicePresentation.Catalog = catalog;
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Clear();
        var exit = new GloomhavenVR.WorldUI.TownServiceSurface { Id = 40 };
        exit.Panel.Target = Go("original-exit").transform;
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Add(exit);
        foreach (string key in new[] { "merchant.buy", "merchant.sell", "merchant.filter.all", "merchant.filter.owned",
            "merchant.filter.head", "merchant.filter.body", "merchant.filter.hands", "merchant.filter.legs", "merchant.filter.small" })
        {
            var control = new GloomhavenVR.WorldUI.TownServiceCatalog.Control { Key = key };
            control.Surface.Panel.Target = Go(key).transform; catalog.Controls.Add(control);
        }
        for (int i = 0; i < 7; i++) catalog.Entries.Add(new GloomhavenVR.WorldUI.TownServiceCatalog.Entry
        {
            ItemId = 101 + i, Current = i < 6, CardRoot = Go("original-card-" + i).transform,
            RowSource = Go("original-row-" + i).transform, RowContent = Go("inert-price-" + i).transform
        });
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, shared);
        var calls = GloomhavenVR.WorldUI.TownServiceSync.Calls;
        Check(!calls.Exists(c => c.Key == "merchant" || c.Key == "merchant.inventory" || c.Source == window.transform),
            "physical counter does not publish suppressed flat merchant window");
        Check(calls.FindAll(c => c.Key.StartsWith("item.")).Count == 6 && !calls.Exists(c => c.Key == "item.107"),
            "physical counter publishes only six current item cards");
        Check(calls.FindAll(c => c.Key == "merchant.row").Count == 6, "physical counter publishes every native price row");
        foreach (var entry in catalog.Entries)
        {
            if (!entry.Current) continue;
            var row = calls.Find(c => c.Source == entry.RowContent);
            Check(row != null && row.Provenance == entry.RowSource && row.CloneOf != null
                && row.CloneOf(entry.RowSource) == entry.RowContent,
                "counter price clone retains original row provenance map");
        }
        foreach (var control in catalog.Controls)
            Check(calls.Exists(c => c.Key == control.Key && c.Source == control.Surface.Panel.Target),
                "counter publishes original filter and mode control: " + control.Key);
        Check(calls.Exists(c => c.Key == "merchant.exit" && c.Source == exit.Panel.Target), "counter exit keeps distinct original provenance");
        Check(calls.Exists(c => c.Key == "merchant.catalognav" && c.Source == catalog.NavigationRoot), "counter page navigation is published");
        catalog.Entries.RemoveRange(2, 5);
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, shared);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleCount == 15 && GloomhavenVR.WorldUI.TownServiceSync.SourceCount == 15,
            "publisher page shrink retires old cards and price modules");
        GloomhavenVR.WorldUI.TownServicePresentation.Catalog = null;
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, shared);
        Check(calls.Exists(c => c.Key == "merchant" && c.Source == window.transform), "ordinary merchant route still publishes native window");
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Clear();
    }

    private static IEnumerator CounterPlayback()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        var shared = Go("counter-owner-frame").transform;
        var observer = Go("counter-observer-frame").transform; observer.position = new Vector3(8, 0, 0);
        var counter = Rect("counter-opening", shared, Vector2.zero, new Vector2(900, 600));
        counter.localScale = Vector3.one * .01f;
        var opening = counter.gameObject.AddComponent<CanvasGroup>(); opening.alpha = 0;
        var cards = new List<RectTransform>();
        for (int i = 0; i < 6; i++)
        {
            var card = Rect("counter-card-" + i, counter, new Vector2((i % 3 - 1) * 135, i / 3 * 190 - 95), new Vector2(110, 160));
            card.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Image("original-art", card, Vector2.zero, new Vector2(110, 160), new Color(.2f + i * .1f, .7f - i * .07f, .45f, 1));
            cards.Add(card);
            TownServiceMirror.RegisterTemplate(1, 1, card, address: "counter." + i);
        }
        TownServiceMirror.BeginSession(1, 1100, shared, counter);
        for (ushort i = 0; i < 6; i++) TownServiceMirror.RegisterModule((ushort)(i + 1), 1, cards[i], address: "counter." + i);
        yield return null;
        Receive(3, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (int i = 0; i < 6; i++) Check(Remote(3, (ushort)(i + 1)) != null, "all six counter cards have observer modules");
        opening.alpha = .37f;
        yield return null;
        var packets = Capture();
        int faded = 0;
        foreach (var packet in packets)
        {
            TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? frame);
            if (frame!.Module == TownServiceFrame.ManifestModule) continue;
            Check(Mathf.Abs(frame.ParentAlpha - .37f) < .0001f, "counter opening transports inherited parent alpha"); faded++;
        }
        Check(faded == 6, "counter opening updates all six original cards");
        Receive(3, packets); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        for (int i = 0; i < 6; i++) ComparePixels(cards[i], Remote(3, (ushort)(i + 1))!.Root, "counter-partial-opening-" + i);
        for (ushort i = 2; i < 6; i++) { cards[i].gameObject.SetActive(false); TownServiceMirror.UnregisterModule((ushort)(i + 1)); }
        opening.alpha = 1f;
        yield return null;
        Receive(3, Capture()); TownServiceMirror.TickRemote(_ => observer);
        for (int i = 2; i < 6; i++) Check(Remote(3, (ushort)(i + 1)) == null, "page shrink removes retired remote cards");
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        for (int i = 0; i < 2; i++) ComparePixels(cards[i], Remote(3, (ushort)(i + 1))!.Root, "counter-page-shrink-" + i);
    }

    public static IEnumerator Run(string output, string variant, string suite)
    {
        _output = Path.Combine(output, variant + "-evidence"); Directory.CreateDirectory(_output); _assertions = 0;
        try
        {
            var cameraGo = Go("Camera"); _camera = cameraGo.AddComponent<Camera>(); _camera.enabled = false;
            _camera.orthographic = true; _camera.nearClipPlane = .01f; _camera.farClipPlane = 100;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.025f, .03f, .04f, 1);
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = _camera;
            Transform shared = Go("Owner frame").transform; shared.gameObject.AddComponent<CanvasGroup>().alpha = .71f;
            Transform source = Source(shared);
            Transform observer = Go("Observer frame").transform; observer.position = new Vector3(12, 0, 0);
            observer.localScale = Vector3.one * 1.25f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables;
            try { TownServiceMirror.RegisterTemplate(1, 1, source); }
            finally { Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables, "inactive template never executes gameplay callbacks"); }
            TownServiceMirror.BeginSession(1, 101, shared, source);
            TownServiceMirror.RegisterModule(10, 1, source);
            List<byte[]> baseline = Capture(); Check(baseline.Count == 2, "first capture emits module and manifest");
            Receive(1, baseline); TownServiceMirror.TickRemote(_ => observer);
            var copy = Remote(1); Check(copy != null, "owner packet creates inert observer module");
            Inert(copy!, awakes, enables);
            Check(copy!.Root.gameObject.layer == 9, "observer uses configured presentation layer before rendering");
            Check(copy.Root.GetComponent<Canvas>().worldCamera == _camera, "observer Canvas receives configured head camera before rendering");
            Check(copy.Root.Find("HandleMesh").GetComponent<MeshFilter>().sharedMesh == source.Find("HandleMesh").GetComponent<MeshFilter>().sharedMesh,
                "observer retains original handle mesh");
            Vector3 expected = observer.TransformPoint(shared.InverseTransformPoint(source.position));
            Check(Vector3.Distance(copy!.Root.position, expected) < .00001f, "observer uses owner pose in shared frame");
            Check(Quaternion.Angle(copy.Root.rotation, observer.rotation * Quaternion.Inverse(shared.rotation) * source.rotation) < .001f, "observer uses owner world rotation");
            Check(Vector3.Distance(copy.Root.lossyScale, source.lossyScale * 1.25f) < .00001f, "observer uses owner root scale");
            Check(copy.Root.GetComponent<CanvasGroup>().alpha == _group.alpha, "root CanvasGroup alpha matches owner");
            Check(copy.Root.Find("Name").GetComponent<TMP_Text>().text == _text.text, "owner TMP text survives codec and playback");
            Check(copy.Root.Find("Viewport").GetComponent<RectMask2D>().padding == _clip.padding, "owner RectMask padding survives playback");
            yield return null;
            ComparePixels(source, copy.Root, "baseline");

            if (suite == "lifecycle")
            {
                IEnumerator lifecycle = Lifecycle(source, shared, observer, copy);
                while (lifecycle.MoveNext()) yield return lifecycle.Current;
                File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
                yield break;
            }
            _text.text = "Owner A <i>changed</i>"; _text.color = new Color(.45f, .95f, .65f, .7f);
            _fill.fillAmount = .31f; _group.alpha = .55f; _clip.padding = new Vector4(18, 8, 11, 3);
            source.Find("HandleMesh").GetComponent<MeshRenderer>().enabled = false;
            yield return null;
            List<byte[]> change = Capture(); Check(change.Count > 0, "native UI changes emit another packet");
            Receive(1, change); TownServiceMirror.TickRemote(_ => observer);
        for (float settle = Time.unscaledTime + .13f; Time.unscaledTime < settle;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
            Check(!copy.Root.Find("HandleMesh").GetComponent<MeshRenderer>().enabled, "handle mesh enabled state follows owner");
            ComparePixels(source, copy.Root, "changed");
            IEnumerator motionCheck = Motion(source, shared, observer, copy);
            while (motionCheck.MoveNext()) yield return motionCheck.Current;
            if (suite == "full")
            {
                IEnumerator extended = Full(source, shared, observer, copy, baseline, awakes, enables);
                while (extended.MoveNext()) yield return extended.Current;
                IEnumerator counter = CounterPlayback();
                while (counter.MoveNext()) yield return counter.Current;
                PublisherRouting();
            }
            File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
        }
        finally
        {
            TownServiceMirror.Shutdown();
            foreach (var go in Objects) if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in Assets) if (asset != null) Object.DestroyImmediate(asset);
            foreach (var orphan in Object.FindObjectsOfType<GameplayFixture>(true)) if (orphan != null) Object.DestroyImmediate(orphan.gameObject);
            File.WriteAllLines(Path.Combine(_output, "production-log.txt"), GloomhavenVR.Core.VRLog.Messages);
            Objects.Clear(); Assets.Clear(); Baselines.Clear(); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        }
    }
}
