using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestSampleWatch()
    {
        PanelSupersample.ResetTest();
        var host=new GameObject("WatchHost");var canvas=host.AddComponent<Canvas>();
        var target=new GameObject("NativeContent");target.transform.SetParent(host.transform);
        var body=target.AddComponent<Image>();body.canvas=canvas;target.transform.rect=new(-20,-20,40,40);
        var edge=new GameObject("ExtremalContent");edge.transform.SetParent(target.transform);
        edge.transform.rect=new(50,60,10,10);var graphic=edge.AddComponent<Image>();graphic.canvas=canvas;
        var panel=new ConvertedPanel{HostRect=host.transform,Target=target.transform};
        var watch=new MrBackingSampleWatch();
        Check(!watch.NeedsSample(panel,0,4),"unknown MR cache uses normal initial measurement");
        Check(PanelInkBounds.TryMeasure(panel,out var ink,backingGeometry:true),"watch fixture paints native content");
        watch.Remember(panel,ink);
        Check(!watch.NeedsSample(panel,1,4),"unchanged extrema do not trigger additional geometry sampling");
        graphic.canvasRenderer.OwnAlpha=0;
        Check(watch.NeedsSample(panel,2,4),"hidden extreme invalidates MR without waiting for grab cadence");
        Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)&&ink.Rect.xMax==20,"remeasure removes hidden edge while body remains visible");
        watch.Remember(panel,ink);
        Check(!watch.NeedsSample(panel,5,4)&&watch.NeedsSample(panel,6,4),"edge shrink gets one prompt independent confirmation sample");
        watch.Remember(panel,ink);
        Check(!watch.NeedsSample(panel,7,4),"confirmed change does not create perpetual fast sampling");
        var display=new GameObject("Display");display.transform.SetParent(host.transform);
        display.transform.rect=new(-100,-100,200,200);display.AddComponent<Canvas>();display.AddComponent<RawImage>();
        PanelSupersample.CaptureTest(panel,display);
        Check(watch.NeedsSample(panel,8,4),"capture engagement invalidates a previously uncaptured MR cache");
        watch.Remember(panel,ink);
        display.transform.rect=new(-10,-10,20,20);
        Check(watch.NeedsSample(panel,9,4),"actual display crop change invalidates cached MR bounds");
        Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)&&ink.Rect.width==20,"crop refresh uses displayed footprint");
        watch.Remember(panel,ink);watch.NeedsSample(panel,13,4);
        target.activeSelf=false;
        Check(watch.NeedsSample(panel,14,4),"disabled extremal hierarchy invalidates cached MR geometry");
        watch.Reset();
        Check(!watch.NeedsSample(panel,100,4),"reset releases old witnesses and pending confirmations");
        PanelSupersample.ResetTest();
    }
}
