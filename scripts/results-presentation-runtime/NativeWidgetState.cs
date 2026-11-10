// Author/observer conversion-created Canvas and private native draw-state probe.
// This exercises production capture/codec/clone/application and Unity raw-camera
// pixels; actual supersample/coordinator/head composition is a separate fixture.
using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class ProgressionTests
{
    private static void WidgetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) WidgetLayer(child, layer);
    }

    private static int NativeWidgetStateRun()
    {
        FFSNetwork.IsOnline = true;
        FFSNetwork.IsClient = false;
        var camera = new GameObject("NativeWidgetStateCamera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -2);
        camera.orthographic = true; camera.orthographicSize = .7f; camera.aspect = 16f / 9f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var author = Make(camera, "WidgetAuthor");
        var observer = Make(camera, "WidgetObserver");
        WidgetLayer(author.Canvas, 18); WidgetLayer(observer.Canvas, 19);
        author.Root.anchorMin = Vector2.zero; author.Root.anchorMax = Vector2.one;
        author.Root.sizeDelta = Vector2.zero;
        author.Host.sizeDelta = new Vector2(1474, 157);
        author.Manager.statsPanel.SetActive(false);
        var authorViewport = (RectTransform)author.Manager.statsPanel.transform.Find("Scroll View/Viewport");
        var observerViewport = (RectTransform)observer.Manager.statsPanel.transform.Find("Scroll View/Viewport");
        authorViewport.gameObject.AddComponent<RectMask2D>();
        observerViewport.gameObject.AddComponent<RectMask2D>();
        observerViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0);
        observerViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0);
        ((RectTransform)observer.Manager.statsPanel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0);
        ((RectTransform)observer.Manager.statsPanel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0);
        observer.Manager.statsPanel.SetActive(false);
        foreach (Graphic graphic in observer.Manager.statsPanel.GetComponentsInChildren<Graphic>(true))
        {
            graphic.enabled = false; graphic.canvasRenderer.SetAlpha(0);
            graphic.canvasRenderer.cull = true;
            graphic.canvasRenderer.EnableRectClipping(UnityEngine.Rect.zero);
        }
        var privateGroup = observer.Manager.statsPanel.AddComponent<CanvasGroup>();
        privateGroup.alpha = 0;
        var sender = new ScenarioResultsPresentation();
        var receiver = new ScenarioResultsPresentation();
        Select(author);
        var heading = State(221, 1, ResultsClock.Now);
        sender.Capture(heading);
        Select(observer); receiver.Apply(heading); receiver.Tick();
        var prior = observer.Panel.ExtraRenderRoots.Single();
        var headingRoot = (RectTransform)prior.GetChild(0);
        Check(author.Root.anchorMin == Vector2.zero && author.Root.anchorMax == Vector2.one
            && headingRoot.anchorMin == Vector2.zero && headingRoot.anchorMax == Vector2.one,
            "the source and common native roots really stretch during the heading frame");
        File.WriteAllLines(Path.Combine(evidence, "native-widget-heading-graphics.txt"), prior.GetComponentsInChildren<Graphic>(true).Select(g =>
            g.name + " active=" + g.gameObject.activeInHierarchy + " enabled=" + g.enabled + " culled=" + g.canvasRenderer.cull
            + " alpha=" + g.canvasRenderer.GetInheritedAlpha() + " clipping=" + g.canvasRenderer.hasRectClipping));

        // Native/mod bootstrap can grow rendering components independently on
        // each peer without changing a module's transform structure identity.
        // The old fixture omitted Canvas altogether and force-enabled every
        // imported pane, so none of these receiver differences was observable.
        Canvas dynamicCanvas = author.Manager.statsPanel.AddComponent<Canvas>();
        dynamicCanvas.renderMode = RenderMode.WorldSpace; dynamicCanvas.worldCamera = camera;
        dynamicCanvas.overrideSorting = false;
        author.Host.sizeDelta = new Vector2(1474, 1024);
        author.Manager.statsPanel.SetActive(true);
        author.Manager.statsPanel.GetComponentInChildren<Graphic>(true).canvasRenderer.SetAlpha(.65f);
        ResultsClock.Now += .3f; ResultsClock.Frame++;
        Select(author); var full = State(221, 2, ResultsClock.Now); sender.Capture(full);
        Check(full.Parts.Any(p => p.Nodes.Any(n => n.Values.ContainsKey(GloomhavenVR.Net.TownServices.TownServiceProperty.Canvas))),
            "runtime source Canvas is included in the actual complete capture");
        byte[] packet = ScenarioResultsCodec.Write(full);
        Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var received) && received != null,
            "runtime Canvas and full native draw state cross the actual codec");
        File.WriteAllBytes(Path.Combine(evidence, "native-widget-state.bin"), packet);
        camera.cullingMask = 1 << 18;
        byte[] native = Image(camera, "native-widget-state-author.png");
        Select(observer); receiver.Apply(received!); receiver.Tick();
        Check(observer.Panel.ExtraRenderRoots.Single() == prior,
            "runtime Canvas addition updates the existing transform-shape picture");
        Transform picture = observer.Panel.ExtraRenderRoots.Single();
        File.WriteAllText(Path.Combine(evidence, "native-widget-immediate-frame.txt"),
            "authorHost=" + author.Host.rect.size + " observerHost=" + observer.Host.rect.size
            + " authorRoot=" + author.Root.rect.size + " observerRoot=" + headingRoot.rect.size
            + " mount=" + ((RectTransform)picture).rect.size + " heading=" + heading.FrameSize + " full=" + full.FrameSize);
        camera.cullingMask = 1 << 19;
        Image(camera, "native-widget-state-observer-immediate.png");
        // Same-shape network samples deliberately interpolate numeric root
        // geometry for at most250ms; measure the settled final geometry as well.
        ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
        Check(observer.Host.rect.size == author.Host.rect.size && headingRoot.rect.size == author.Root.rect.size
            && ((RectTransform)picture).rect.size == heading.FrameSize && heading.FrameSize != full.FrameSize,
            "the unchanged candidate mount cannot clip a stretched root when the common frame grows from heading to Stats");
        Canvas[] canvases = picture.GetComponentsInChildren<Canvas>(true);
        Check(canvases.Length == 1 && canvases[0].isActiveAndEnabled,
            "the common source Canvas remains active despite private native pane disablement");
        File.WriteAllLines(Path.Combine(evidence, "native-widget-canvases.txt"), canvases.Select(c =>
            c.name + " mode=" + c.renderMode + " root=" + c.rootCanvas.name + " camera=" + c.worldCamera
            + " enabled=" + c.enabled + " sorting=" + c.sortingOrder + " override=" + c.overrideSorting));
        camera.cullingMask = 1 << 19;
        byte[] remote = Image(camera, "native-widget-state-observer.png");
        File.WriteAllLines(Path.Combine(evidence, "native-widget-full-graphics.txt"), picture.GetComponentsInChildren<Graphic>(true).Select(g =>
            g.name + " active=" + g.gameObject.activeInHierarchy + " enabled=" + g.enabled + " culled=" + g.canvasRenderer.cull
            + " alpha=" + g.canvasRenderer.GetInheritedAlpha() + " clipping=" + g.canvasRenderer.hasRectClipping));
        int differences = native.Where((value, index) => Math.Abs(value - remote[index]) > 4).Count();
        File.WriteAllText(Path.Combine(evidence, "native-widget-pixel-difference.txt"), differences.ToString());
        Check(differences < native.Length * .003,
            "complete original pixels survive private native inactive graphics zero alpha and a newly source-authored Canvas");
        Check(observer.Manager.statsPanel.activeSelf == false && privateGroup.alpha == 0,
            "common playback preserves private original controller visibility and alpha");
        // Export actual sampler output to the independent production publisher/
        // transport lane. A routine enabled flag changes the property-key set:
        // disabled originals retain transform/Graphic/Renderer state but do not
        // publish dormant material/artwork. This is a full-basis change even
        // though the transform/module structure and entire Stats stage persist.
        Graphic toggled = author.Manager.statsPanel.GetComponentsInChildren<Graphic>(true).First(g => g is Image && g.enabled);
        toggled.enabled = false;
        Select(author); ResultsClock.Now += .01f; ResultsClock.Frame++;
        var disabled = State(221, 3, ResultsClock.Now); sender.Capture(disabled);
        byte[] disabledPacket = ScenarioResultsCodec.Write(disabled);
        Check(full.Parts.Length == disabled.Parts.Length && full.Parts.Zip(disabled.Parts, (a, b) =>
            a.Structure == b.Structure && a.Address == b.Address && a.Nodes.Length == b.Nodes.Length).All(same => same)
            && !ScenarioResultsDelta.Compatible(full, disabled) && packet.Length > 800 && disabledPacket.Length > 800,
            "one actual native Graphic enabled transition preserves the complete Stats shape but requires a multi-fragment new full basis");
        File.WriteAllBytes(Path.Combine(evidence, "sampler-enabled-full.bin"), packet);
        File.WriteAllBytes(Path.Combine(evidence, "sampler-disabled-full.bin"), disabledPacket);
        int changedKeySets = full.Parts.Zip(disabled.Parts, (a, b) => a.Nodes.Zip(b.Nodes, (x, y) =>
            x.Values.Count != y.Values.Count || x.Values.Keys.Except(y.Values.Keys).Any()).Count(changed => changed)).Sum();
        File.WriteAllText(Path.Combine(evidence, "sampler-stage-transition.json"),
            "{\"enabled_bytes\":" + packet.Length + ",\"disabled_bytes\":" + disabledPacket.Length
            + ",\"same_part_shape\":true,\"compatible\":false,\"changed_node_key_sets\":" + changedKeySets
            + ",\"key\":221,\"author\":1,\"toggled_graphic\":\"" + toggled.name.Replace("\"", "\\\"") + "\"}");
        sender.Reset(); receiver.Reset();
        Object.DestroyImmediate(author.Canvas.gameObject); Object.DestroyImmediate(observer.Canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
        return count;
    }
}
