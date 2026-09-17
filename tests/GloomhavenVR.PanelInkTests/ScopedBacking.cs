using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestScopedBoardBacking()
    {
        // Reproduce the native initiative split: the fitted row is tiny, its screen-layout parent
        // and input/reveal siblings remain huge. No transform or visibility state may be changed.
        for (int observer = 0; observer < 2; observer++)
        {
            var host = new GameObject(observer == 0 ? "OwnerHost" : "InertCloneHost");
            host.transform.rect = Rect.MinMaxRect(-90, -95, 90, 95);
            var target = new GameObject("InitiativeRoot");
            target.transform.SetParent(host.transform);
            target.transform.rect = Rect.MinMaxRect(-960, -1080, 960, 540);
            target.AddComponent<Image>();
            var clippingParent = new GameObject("NativeMask");
            clippingParent.transform.SetParent(target.transform);
            clippingParent.transform.rect = Rect.MinMaxRect(-110, -110, 110, 110);
            var holder = new GameObject("OriginalPortraitHolder");
            holder.transform.SetParent(clippingParent.transform);
            holder.transform.rect = Rect.MinMaxRect(-90, -95, 90, 95);
            var portrait = new GameObject("OriginalPortrait");
            portrait.transform.SetParent(holder.transform);
            portrait.transform.rect = holder.transform.rect;
            var picture = portrait.AddComponent<Image>();
            var sibling = new GameObject("FullscreenSibling");
            sibling.transform.SetParent(target.transform);
            sibling.transform.rect = Rect.MinMaxRect(-800,-1600,800,400);
            sibling.AddComponent<Image>();
            var panel = new ConvertedPanel { HostRect=host.transform, Target=target.transform };
            Check(PanelInkBounds.TryMeasure(panel,out var ink,contentRoot:holder.transform),
                "scoped full-frame portrait is real content");
            Check(ink.Graphics==1 && ink.Plates==0 && ink.Rect.width==180 && ink.Rect.height==190,
                "scoped initiative excludes ancestor screen artwork and fullscreen siblings");
            // The initial host can still have its old screen extent. Explicit scope must work
            // both before and after native fitting; classification may not invent a tall plate.
            host.transform.rect = Rect.MinMaxRect(-960,-540,960,540);
            Check(PanelInkBounds.TryMeasure(panel,out ink,contentRoot:holder.transform)
                && ink.Rect.height==190 && ink.Rect.width==180,
                "oversized host does not enlarge actual initiative row content");
            clippingParent.AddComponent<RectMask2D>();
            clippingParent.transform.rect = Rect.MinMaxRect(-40,-45,40,45);
            Check(PanelInkBounds.TryMeasure(panel,out ink,contentRoot:holder.transform)
                && ink.Rect.width==80 && ink.Rect.height==90,
                "scoped portrait retains ancestor clipping");
            Check(!PanelInkBounds.TryMeasure(panel,out _,contentRoot:new GameObject("ForeignHolder").transform),
                "foreign or detached scope cannot fall back to the giant parent");
            holder.activeSelf=false;
            Check(!PanelInkBounds.TryMeasure(panel,out _,contentRoot:holder.transform),
                "hidden scoped row cannot leave an opaque parent backing");
            holder.activeSelf=true;
            picture.canvasRenderer.Alpha=0;
            Check(!PanelInkBounds.TryMeasure(panel,out _,contentRoot:holder.transform),
                "scoped measurement preserves native inherited alpha");
            picture.canvasRenderer.Alpha=1;
            Check(target.activeSelf && sibling.activeSelf && target.transform.rect.height==1620,
                "MR measurement must not alter original layout or visibility");
        }
    }
}
