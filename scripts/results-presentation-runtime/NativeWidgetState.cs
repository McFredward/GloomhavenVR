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
        observer.Manager.statsPanel.SetActive(false);
        foreach (Graphic graphic in observer.Manager.statsPanel.GetComponentsInChildren<Graphic>(true))
        { graphic.enabled = false; graphic.canvasRenderer.SetAlpha(0); }
        var privateGroup = observer.Manager.statsPanel.AddComponent<CanvasGroup>();
        privateGroup.alpha = 0;
        var sender = new ScenarioResultsPresentation();
        var receiver = new ScenarioResultsPresentation();
        Select(author);
        var heading = State(221, 1, ResultsClock.Now);
        sender.Capture(heading);
        Select(observer); receiver.Apply(heading); receiver.Tick();
        var prior = observer.Panel.ExtraRenderRoots.Single();

        // Native/mod bootstrap can grow rendering components independently on
        // each peer without changing a module's transform structure identity.
        // The old fixture omitted Canvas altogether and force-enabled every
        // imported pane, so none of these receiver differences was observable.
        Canvas dynamicCanvas = author.Manager.statsPanel.AddComponent<Canvas>();
        dynamicCanvas.renderMode = RenderMode.WorldSpace; dynamicCanvas.worldCamera = camera;
        dynamicCanvas.overrideSorting = false;
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
        Canvas[] canvases = picture.GetComponentsInChildren<Canvas>(true);
        Check(canvases.Length == 1 && canvases[0].isActiveAndEnabled,
            "the common source Canvas remains active despite private native pane disablement");
        File.WriteAllLines(Path.Combine(evidence, "native-widget-canvases.txt"), canvases.Select(c =>
            c.name + " mode=" + c.renderMode + " root=" + c.rootCanvas.name + " camera=" + c.worldCamera
            + " enabled=" + c.enabled + " sorting=" + c.sortingOrder + " override=" + c.overrideSorting));
        camera.cullingMask = 1 << 19;
        byte[] remote = Image(camera, "native-widget-state-observer.png");
        int differences = native.Where((value, index) => Math.Abs(value - remote[index]) > 4).Count();
        File.WriteAllText(Path.Combine(evidence, "native-widget-pixel-difference.txt"), differences.ToString());
        Check(differences < native.Length * .003,
            "complete original pixels survive private native inactive graphics zero alpha and a newly source-authored Canvas");
        Check(observer.Manager.statsPanel.activeSelf == false && privateGroup.alpha == 0,
            "common playback preserves private original controller visibility and alpha");
        sender.Reset(); receiver.Reset();
        Object.DestroyImmediate(author.Canvas.gameObject); Object.DestroyImmediate(observer.Canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
        return count;
    }
}
