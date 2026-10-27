using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real Unity Camera.Render callbacks exercise the production registry and the
// production layer acquire/release methods. Sources here are explicit callback
// probes, including a deliberate exception; they do not implement rendering.
public static class Routing724
{
    private static int _count;
    private static void Check(bool value, string text) { _count++; if (!value) throw new Exception(text); }

    public static int Run(string output)
    {
        _count = 0;
        Camera? previousHead = VRRigDriver.HeadCamera;
        int previousExcluded = MenuDepthSources.ExcludedDirectLayers;
        var head = new GameObject("724 actual head callback").AddComponent<Camera>(); head.enabled = false;
        head.orthographic = true; head.orthographicSize = 2; head.transform.position = new Vector3(0, 0, -2);
        head.clearFlags = CameraClearFlags.SolidColor; head.backgroundColor = Color.clear;
        var target = new RenderTexture(32, 32, 24); target.Create(); head.targetTexture = target;
        VRRigDriver.HeadCamera = head;
        var root = new GameObject("724 native window", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); root.layer = 8;
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(2, 2);
        var graphic = new GameObject("724 native source", typeof(RectTransform)).AddComponent<Image>();
        graphic.gameObject.layer = 8; graphic.transform.SetParent(root.transform, false); graphic.rectTransform.sizeDelta = Vector2.one;
        var source = new Probe(graphic); MenuDepthSources.Register(source);
        try
        {
            MenuDepthSources.ExcludedDirectLayers = 0; head.cullingMask = 1 << 8;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0; Canvas.ForceUpdateCanvases(); head.Render();
            Check(source.Starts == 0 && !MenuDepthSources.HasContent(root.transform),
                "fully hidden pooled native consumers never borrow the model scene or opt in their window");
            group.alpha = .0001f; Canvas.ForceUpdateCanvases(); head.Render();
            Check(source.Starts == 1,
                "every positive inherited-alpha fade frame retains its native depth source lease");
            Check(source.Restores == 1, "every begun direct source lease restores before the head callback completes");
            graphic.color = new Color(1, 1, 1, 0); Canvas.ForceUpdateCanvases(); head.Render();
            Check(source.Starts == 1, "a zero-alpha native Graphic is not directly prepared");
            graphic.color = Color.white; group.alpha = 1; canvas.enabled = false; Canvas.ForceUpdateCanvases(); head.Render();
            Check(source.Starts == 1, "a disabled original Canvas never borrows the shared native scene");
            canvas.enabled = true;
            var nested = new GameObject("724 nested screen canvas", typeof(RectTransform), typeof(Canvas)); nested.transform.SetParent(root.transform, false);
            graphic.transform.SetParent(nested.transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = head; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); head.Render();
            Check(source.Starts == 1, "screen-space consumers with nested canvases are never mistaken for direct world-space previews");
            canvas.renderMode = RenderMode.WorldSpace; graphic.transform.SetParent(root.transform, false); Object.DestroyImmediate(nested);
            Canvas.ForceUpdateCanvases();

            PanelSupersample.ResetLayerProof();
            int first = PanelSupersample.TakeLayerProof();
            MenuDepthSources.ExcludedDirectLayers = 1 << first; // Existing first InstallHooks snapshot.
            int second = PanelSupersample.TakeLayerProof();
            Check(MenuDepthSources.ExcludedDirectLayers == ((1 << first) | (1 << second)),
                "the second actual private-layer reservation updates direct-capture exclusion ownership immediately");
            graphic.gameObject.layer = second; head.cullingMask = (1 << first) | (1 << second); head.Render();
            Check(source.Starts == 1, "a later supersampled window cannot also prepare its source through the head's direct route");
            PanelSupersample.ReturnLayerProof(first);
            Check(MenuDepthSources.ExcludedDirectLayers == 1 << second,
                "releasing one actual capture layer retains exclusion of every other live private window");
            PanelSupersample.ReturnLayerProof(second);
            Check(MenuDepthSources.ExcludedDirectLayers == 0, "the last actual layer release clears direct exclusion ownership");

            graphic.gameObject.layer = 8; head.cullingMask = 1 << 8;
            source.Fail = true;
            int logStart = VRLog.Messages.Count;
            for (int i = 0; i < 65; i++) head.Render();
            string[] warnings = VRLog.Messages.Skip(logStart).Where(line => line.StartsWith("MENU DEPTH DIRECT CAPTURE:")).ToArray();
            Check(warnings.Length == 8, "actual repeated callback failures retain the first-four and power-of-two warning budget");
            Check(warnings[0].Contains("Nested menu-depth preparation") && warnings[0].Contains("724 native source")
                && warnings[0].Contains("724 actual head callback") && warnings[0].Contains("direct source preparation"),
                "bounded native callback failures identify exact message, source, phase and head camera");
            Check(source.Restores == source.Starts,
                "a real recursive preparation failure restores every begun direct source exactly once");
            File.WriteAllLines(Path.Combine(output, "724-routing.log"), VRLog.Messages.Skip(logStart));
            source.Fail = false; head.Render();
            Check(source.Restores == source.Starts, "a source can render again after the failed callback scope was restored");
        }
        finally
        {
            MenuDepthSources.Unregister(source); MenuDepthSources.ExcludedDirectLayers = previousExcluded;
            VRRigDriver.HeadCamera = previousHead; Object.DestroyImmediate(root); head.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(head.gameObject);
        }
        return _count;
    }

    private sealed class Probe : IMenuDepthSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => true;
        internal int Starts, Restores;
        internal bool Fail;
        internal Probe(Graphic graphic) { Graphic = graphic; }
        public void BeforeCapture(MenuDepthEye eye)
        {
            Starts++;
            if (Fail) MenuDepthSources.BeforeCapture(Graphic.transform, eye);
        }
        public void AfterCapture() => Restores++;
    }
}
