using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestBackingDepth()
    {
        for (int observer = 0; observer < 2; observer++)
        {
            var host = new GameObject(observer == 0 ? "OwnerHost" : "ClonePivot");
            var canvas = host.AddComponent<Canvas>();
            var target = new GameObject("InitiativeModule");
            target.transform.SetParent(host.transform);
            var holder = new GameObject("InitiativeTrack");
            holder.transform.SetParent(target.transform);
            holder.transform.localPosition = new Vector3(0, 0, 24);
            var entry = new GameObject("OriginalPortrait");
            entry.transform.SetParent(holder.transform);
            entry.transform.localPosition = new Vector3(0, 0, 7);
            entry.transform.rect = new Rect(-60, -65, 120, 130);
            var image = entry.AddComponent<RawImage>();
            image.canvas = canvas;
            var sibling = new GameObject("ExcludedScreenSibling");
            sibling.transform.SetParent(target.transform);
            sibling.transform.localPosition = new Vector3(0, 0, 900);
            sibling.AddComponent<RawImage>().canvas = canvas;
            var panel = new ConvertedPanel { Target = target.transform, HostRect = host.transform };
            Check(PanelInkBounds.TryMeasure(panel, out var ink, contentRoot: holder.transform, backingGeometry: true)
                && ink.BackZ == 31, "MR backing retains complete native child depth chain");
            Check(PanelInkBounds.TryMeasure(panel, out ink, contentRoot: holder.transform)
                && ink.BackZ == 0, "ordinary layout ignores MR backing depth");
            var hidden = new GameObject("HiddenPortrait");
            hidden.transform.SetParent(holder.transform);
            hidden.transform.localPosition = new Vector3(0, 0, 1000);
            var hiddenImage = hidden.AddComponent<RawImage>();
            hiddenImage.canvas = canvas; hiddenImage.color = new Color { a = 0 };
            Check(PanelInkBounds.TryMeasure(panel, out ink, contentRoot: holder.transform, backingGeometry: true)
                && ink.BackZ == 31, "invisible graphics cannot push the MR depth behind unrelated content");
            entry.transform.localPosition = new Vector3(0, 0, -40);
            Check(PanelInkBounds.TryMeasure(panel, out ink, contentRoot: holder.transform, backingGeometry: true)
                && ink.BackZ == 0, "already proud UI preserves the original backing plane");
            image.canvasRenderer.cull = true;
            Check(!PanelInkBounds.TryMeasure(panel, out ink, contentRoot: holder.transform, backingGeometry: true)
                && ink.BackZ == 0, "missing ink cannot retain previous depth");
            Check(holder.transform.localPosition.z == 24 && entry.transform.localPosition.z == -40,
                "MR depth sampling never writes native row transforms");
        }
    }
}
