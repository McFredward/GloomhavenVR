using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestMirrorBackingInk()
    {
        var pivot = new GameObject("NativeLayoutPivot");
        pivot.transform.rect = new Rect(-1000,-540,2000,1080);
        var root = new GameObject("OriginalWidgetClone");
        root.transform.SetParent(pivot.transform);
        root.transform.rect = new Rect(-250,-150,500,300);
        var background = root.AddComponent<Image>();
        var label = new GameObject("VisibleContent");
        label.transform.SetParent(root.transform);
        label.transform.rect = new Rect(-40,-10,80,20);
        label.AddComponent<Image>();
        var descriptor = new ConvertedPanel { HostRect=pivot.transform, Target=root.transform };
        Check(PanelInkBounds.TryMeasure(descriptor,out var ink) && ink.Graphics==2 && ink.Plates==0,
            "existing default ink retains its original host frame semantics");
        Check(PanelInkBounds.TryMeasure(descriptor,out ink,frameOverride:root.transform.rect)
            && ink.Graphics==1 && ink.Plates==1 && ink.Rect.width==80,
            "mirror backdrop classification uses the sampled owner frame, not native parent size");
        descriptor.ContentGraphic=background;
        Check(PanelInkBounds.TryMeasure(descriptor,out ink,frameOverride:root.transform.rect)
            && ink.Graphics==2 && ink.Plates==0,
            "native full-frame content exemption survives frame override");
        descriptor.ContentGraphic=null;
        var tooltip = new GameObject("NeutralizedTooltip");
        tooltip.transform.SetParent(root.transform);
        var tipImage = new GameObject("TooltipImage");
        tipImage.transform.SetParent(tooltip.transform);
        tipImage.transform.rect = new Rect(450,-50,150,100);
        tipImage.AddComponent<Image>();
        var excluded = new HashSet<Transform> { tooltip.transform };
        Check(PanelInkBounds.TryMeasure(descriptor,out ink,frameOverride:root.transform.rect,excludedRoots:excluded)
            && ink.Rect.xMax==40 && ink.Graphics==1,
            "neutralized remote hover branch must not inflate the window backing");
        Check(tooltip.activeSelf && tipImage.activeSelf,
            "backing measurement never suppresses original mirrored hover presentation");
        label.activeSelf = false;
        Check(!PanelInkBounds.TryMeasure(descriptor,out ink,frameOverride:root.transform.rect,excludedRoots:excluded),
            "remote frame and excluded tooltip alone do not invent a visible window");
    }
}
