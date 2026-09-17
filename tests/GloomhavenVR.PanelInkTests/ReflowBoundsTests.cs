using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestReflowBounds()
    {
        PanelSupersample.ResetTest();
        var host = new GameObject("RoomMakingHost");
        host.transform.rect = new Rect(-960, -540, 1920, 1080);
        var canvas = host.AddComponent<Canvas>();
        var root = new GameObject("Window"); root.transform.SetParent(host.transform);
        var image = root.AddComponent<Image>(); image.canvas = canvas;
        root.transform.rect = new Rect(200, -40, 80, 80);
        var label = new GameObject("TallLayout"); label.transform.SetParent(root.transform);
        label.transform.rect = new Rect(-900, -1000, 1800, 2000);
        var text = label.AddComponent<TMPro.TMP_Text>(); text.canvas = canvas; text.text = "Quest";
        text.mesh = PaintedRect(220, -20, 260, 20);
        var panel = new ConvertedPanel { HostRect = host.transform, Target = root.transform };
        Check(WindowReflowBounds.TryRead(panel, out Rect bounds, out bool pending)
            && !pending && bounds.xMin == 200 && bounds.xMax == 280 && bounds.height == 80,
            "opening occupancy excludes transparent host and empty label layout");
        image.canvasRenderer.OwnAlpha = 0;
        Check(WindowReflowBounds.TryRead(panel, out bounds, out pending)
            && bounds.xMin == 220 && bounds.width == 40,
            "opening occupancy excludes a hidden native image");
        text.mesh = null;
        Check(!WindowReflowBounds.TryRead(panel, out _, out pending) && pending,
            "missing visible text mesh requests retry without inventing a host footprint");
        text.text = "";
        Check(!WindowReflowBounds.TryRead(panel, out _, out pending) && !pending,
            "empty native window has no visual occupancy");
        image.canvasRenderer.OwnAlpha = 1;
        var display = new GameObject("DisplayedCrop"); display.transform.SetParent(host.transform);
        display.transform.rect = new Rect(230, -20, 30, 40);
        display.AddComponent<Canvas>(); display.AddComponent<RawImage>();
        PanelSupersample.CaptureTest(panel, display);
        Check(WindowReflowBounds.TryRead(panel, out bounds, out pending)
            && bounds.xMin == 230 && bounds.width == 30 && bounds.height == 40,
            "opening occupancy uses the displayed crop rather than unclipped artwork");
        PanelSupersample.ResetTest();
        Check(host.transform.rect.width == 1920 && label.transform.rect.width == 1800,
            "visual occupancy does not resize native or hit layouts");
    }
}
