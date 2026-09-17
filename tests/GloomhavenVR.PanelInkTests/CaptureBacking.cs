using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.WorldUI
{
    internal static partial class PanelSupersample
    {
        private sealed class Entry
        {
            internal ConvertedPanel Panel=null!;internal RectTransform? DisplayRect;
            internal GameObject? DisplayGo;internal Canvas? DisplayCanvas;internal RawImage? DisplayImage;
        }
        private static readonly List<Entry> Entries=new();
        internal static void ResetTest()=>Entries.Clear();
        internal static void CaptureTest(ConvertedPanel panel,GameObject display)
            => Entries.Add(new(){Panel=panel,DisplayRect=display.transform,DisplayGo=display,
                DisplayCanvas=display.GetComponent<Canvas>(),DisplayImage=display.GetComponent<RawImage>()});
    }
}
static partial class Program
{
    private static void TestCaptureBacking()
    {
        PanelSupersample.ResetTest();
        var host=new GameObject("CaptureOwner");var canvas=host.AddComponent<Canvas>();
        host.transform.localPosition=new(40,60,0);host.transform.localScale=new(2,2,1);
        var root=new GameObject("NativeRoot");root.transform.SetParent(host.transform);
        var center=new GameObject("Visible");center.transform.SetParent(root.transform);
        center.transform.rect=new(-10,-10,20,20);center.AddComponent<Image>().canvas=canvas;
        var outlier=new GameObject("Outlier");outlier.transform.SetParent(root.transform);
        var art=outlier.AddComponent<Image>();art.canvas=canvas;
        var panel=new ConvertedPanel{HostRect=host.transform,Target=root.transform};
        var display=new GameObject("ActualDisplay");display.transform.SetParent(host.transform);
        display.transform.rect=new(-100,-80,200,160);display.AddComponent<Canvas>();display.AddComponent<RawImage>();
        Check(!PanelSupersample.TryGetBackingCaptureRect(panel,out _),"before capture there is no invented host crop");
        PanelSupersample.CaptureTest(panel,display);
        Check(PanelSupersample.TryGetBackingCaptureRect(panel,out Rect frame)&&frame.xMin==-100&&frame.yMin==-80,
            "actual display footprint is transformed back into host coordinates");
        Rect[] outside={new(-220,-10,20,20),new(200,-10,20,20),new(-10,-200,20,20),new(-10,180,20,20)};
        Rect[] overflow={new(-80,-10,20,20),new(60,-10,20,20),new(-10,-60,20,20),new(-10,40,20,20)};
        var pieces=new List<Rect>();var witnesses=new List<Graphic>();
        for(int direction=0;direction<4;direction++)
        {
            outlier.transform.rect=outside[direction];
            Check(PanelInkBounds.TryMeasure(panel,out var ink,backingGeometry:true,backingPieces:pieces,visibleWitnesses:witnesses)
                && ink.Rect.width==20&&ink.Rect.height==20&&ink.Graphics==1,
                "fully cropped outlier cannot grow MR backing in direction "+direction);
            Check(pieces.Count==2&&witnesses.Count==1,"animation retains raw cropped pieces while live visibility excludes them");
            Check(MrBackingCaptureBounds.Union(pieces,true,frame,out Rect clipped)&&clipped.width==20&&clipped.height==20,
                "animation clips each piece before union in direction "+direction);
            Check(MrBackingCaptureBounds.Union(pieces,false,default,out Rect raw)&&(raw.width>20||raw.height>20),
                "disengaged capture preserves genuine native overflow");
            outlier.transform.rect=overflow[direction];
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)&&(ink.Rect.width>20||ink.Rect.height>20),
                "visible overflow inside actual capture remains backed in direction "+direction);
            art.canvasRenderer.OwnAlpha=0;
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)&&ink.Rect.width==20&&ink.Rect.height==20,
                "own renderer alpha zero cannot grow backing in direction "+direction);
            art.canvasRenderer.OwnAlpha=1;
        }
        outlier.transform.rect=new(90,-10,30,20);
        Check(PanelInkBounds.TryMeasure(panel,out var partial,backingGeometry:true)&&partial.Rect.xMax==100,
            "partial capture overlap retains only actually displayed part");
        var direct=new ConvertedPanel{HostRect=host.transform,Target=root.transform};
        Check(PanelInkBounds.TryMeasure(direct,out var clone,frameOverride:new(-5,-5,10,10),backingGeometry:true)&&clone.Rect.xMax==120,
            "direct remote clone does not mistake emulated layout frame for a capture crop");
        art.canvasRenderer.OwnAlpha=0;
        var originals=new Dictionary<CanvasRenderer,float>{{art.canvasRenderer,.8f}};
        Check(PanelInkBounds.TryMeasure(direct,out clone,backingGeometry:true,backingOriginalAlpha:originals)&&clone.Rect.xMax==120,
            "materialise geometry uses original owned alpha while current effect alpha is zero");
        originals[art.canvasRenderer]=0;
        Check(PanelInkBounds.TryMeasure(direct,out clone,backingGeometry:true,backingOriginalAlpha:originals)&&clone.Rect.width==20,
            "native originally hidden graphic remains excluded from effect snapshot");
        var visibility=new MrBackingVisibility{Root=root.transform};visibility.Witnesses.Add(art);
        Check(!visibility.VisibleNow,"cached live witness respects own renderer alpha immediately");
        art.canvasRenderer.OwnAlpha=.4f;art.canvasRenderer.Alpha=.5f;art.color=new(){a=.5f};
        Check(Math.Abs(visibility.AlphaNow-.1f)<.0001f,"own group and graphic alpha combine without replacing each other");
        var late=new GameObject("LateNativeText");late.transform.SetParent(root.transform);
        var text=late.AddComponent<TMPro.TMP_Text>();text.text="Late";text.canvas=canvas;
        Check(PanelInkBounds.TryMeasure(direct,out var pending,backingGeometry:true)&&pending.PendingPaint==1,
            "partial valid picture still records a missing native text mesh");
        text.canvasRenderer.OwnAlpha=0;originals[text.canvasRenderer]=1;
        Check(PanelInkBounds.TryMeasure(direct,out pending,backingGeometry:true,backingOriginalAlpha:originals)&&pending.PendingPaint==1,
            "pending mesh remains admitted while the effect writes its own alpha zero");
        text.mesh=PaintedRect(-200,-20,-100,20);
        Check(PanelInkBounds.TryMeasure(direct,out pending,backingGeometry:true,backingOriginalAlpha:originals)
            && pending.PendingPaint==0&&pending.Rect.xMin==-200,
            "late native mesh joins complete snapshot using the runner original alpha");
        originals[text.canvasRenderer]=0;
        Check(PanelInkBounds.TryMeasure(direct,out pending,backingGeometry:true,backingOriginalAlpha:originals)&&pending.Rect.xMin==-10,
            "late original-zero mesh remains excluded even after native mesh becomes ready");
        display.activeSelf=false;
        Check(!PanelSupersample.TryGetBackingCaptureRect(panel,out _),"inactive display has no claimed capture footprint");
        PanelSupersample.ResetTest();
    }
}
