using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static class ConversionMeasureProgram
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }
    private static RectTransform Rect(string name, Transform? parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(200, 200);
        return rect;
    }
    public static int Run()
    {
        count = 0;
        var host = Rect("MeasureFixture.Host", null);
        host.position = new Vector3(2, 1, -3);
        host.rotation = Quaternion.Euler(0, 45, 0);
        host.localScale = new Vector3(.001f, .002f, .0015f);
        var canvas = host.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var target = Rect("Target", host);
        var outer = Rect("OuterViewport", target);
        var outerMask = outer.gameObject.AddComponent<RectMask2D>();
        var inner = Rect("InnerViewport", outer);
        inner.sizeDelta = new Vector2(80, 120);
        var innerMask = inner.gameObject.AddComponent<RectMask2D>();
        var row = Rect("Row", inner);
        var image = Rect("Picture", row).gameObject.AddComponent<Image>();
        image.canvasRenderer.cull = false;
        var sibling = Rect("Sibling", row);
        var panel = new ConvertedPanel { Target = target, HostRect = host };
        try
        {
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == inner,
                "nearest active mask wins for the first nested item");
            Check(CanvasConversion.Find(panel, sibling) == inner,
                "ancestor memo preserves nearest mask for a row sibling");
            Check(CanvasConversion.Measure(panel, image, out Vector2 min, out Vector2 max)
                && Mathf.Abs(min.x + 40) < .002f && Mathf.Abs(max.x - 40) < .002f
                && Mathf.Abs(min.y + 60) < .002f && Mathf.Abs(max.y - 60) < .002f,
                "transformed host produces the same local graphic corners");

            innerMask.enabled = false;
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == outer,
                "next pass observes live disabled nearest mask");
            innerMask.enabled = true;
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Measure(panel, image, out _, out max)
                && Mathf.Abs(max.x - 40) < .002f, "fresh clip bounds preserve current viewport");
            inner.sizeDelta = new Vector2(160, 120);
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Measure(panel, image, out _, out max)
                && Mathf.Abs(max.x - 80) < .002f, "next pass observes animated viewport growth");

            image.rectTransform.SetParent(outer, false);
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == outer,
                "returned pooled item forgets previous clipper ancestry in next query");
            var boundary = host.gameObject.AddComponent<RectMask2D>();
            outerMask.enabled = false;
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == null,
                "converted target boundary prevents foreign host clip admission");
            boundary.enabled = false;
            var stencil = outer.gameObject.AddComponent<Mask>();
            outer.gameObject.AddComponent<Image>();
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == outer,
                "functioning native stencil remains a valid enclosing clipper");
            stencil.graphic.enabled = false;
            CanvasConversion.BeginContentQuery();
            Check(CanvasConversion.Find(panel, image.rectTransform) == null,
                "disabled stencil picture does not become a phantom clipper");
        }
        finally { UnityEngine.Object.DestroyImmediate(host.gameObject); }
        return count;
    }
}
