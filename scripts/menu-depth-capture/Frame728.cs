using System;
using System.IO;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthProgram
{
    // Renderer bounds/XR observations are explicit inputs in this capture port.
    // The production coordinator now owns fixed head rasters independently of
    // the native mono allocation. Bounds jitter must never resize that photo
    // or change native layout, hit geometry or synchronized window placement.
    private static void Frame728(string output)
    {
        using (var scene = new Scene())
        {
            MakeImage(scene.Panel,"original flat UI outside old capture",new Vector2(60,60),new Vector2(-360,0),Color.blue,0);
            scene.Capture();
            using (TextureOwner baseline = scene.Eye(false,"728-original-capture"))
            {
                Rect original = scene.Entry.Frame;
                Vector3 position = scene.Panel.position, scale = scene.Panel.localScale;
                var bounds = new Bounds728(scene.Image);
                MenuDepthSources.Register(bounds);
                try
                {
                    var ordinary = new List<NonBounds728>();
                    for (int i=0;i<64;i++) { var source=new NonBounds728(scene.Image); ordinary.Add(source); MenuDepthSources.Register(source); }
                    try
                    {
                        var collected = new List<Graphic>(); MenuDepthSources.CollectReadyForegroundGraphics(collected);
                        Check(collected.Count==1 && collected[0]==scene.Image,
                            "foreground collection admits only actual footprint sources among many native cards");
                        foreach (NonBounds728 source in ordinary) Check(source.Reads==0,
                            "ordinary card graphics and readiness are never touched by foreground collection");
                    }
                    finally { foreach (NonBounds728 source in ordinary) MenuDepthSources.Unregister(source); }
                    Canvas.ForceUpdateCanvases(); scene.Capture();
                    Rect sampled = PanelSupersample.AllocationFrame(scene.Entry);
                    Check(sampled == original,
                        "fixed head raster never expands the original mono photograph frame");
                    Check(scene.Entry.Rt.width == 640 && scene.Entry.Rt.height == 360,
                        "actual original mono dimensions remain unchanged when foreground bounds grow");
                    Check(scene.Entry.Frame == original && scene.Panel.position == position && scene.Panel.localScale == scale,
                        "fixed head capture never changes native layout input or synchronized window geometry");
                    Check(PanelSupersample.ExtraBytes == MenuDepthDisplay.HeadCost(scene.Head,scene.Entry.Rt)
                        && PanelSupersample.ExtraBytes <= MenuDepthHeadRaster.WindowBudget(scene.Entry.Rt.descriptor,MenuDepthHeadRaster.Size(scene.Head)),
                        "both actual head targets charge their exact independently bounded head-raster cost");
                    using (TextureOwner expanded = scene.Eye(false,"728-expanded-capture"))
                    {
                        Color32[] a = baseline.Image.GetPixels32(), b = expanded.Image.GetPixels32();
                        double difference = 0;
                        for (int i=0;i<a.Length;i++) difference += Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)+Math.Abs(a[i].a-b[i].a);
                        Check(difference / (a.Length*4) < .3,
                            "registered foreground bounds cannot change final head colour or native text");
                        File.WriteAllText(Path.Combine(output,"728-frame-allocation.txt"),"native="+original+";sampled="+sampled
                            +";RT="+scene.Entry.Rt.width+"x"+scene.Entry.Rt.height+";meanChannelDifference="+difference/(a.Length*4)+"\n");
                    }
                    RenderTexture held = scene.Entry.Rt;
                    for (int tick=0;tick<6;tick++)
                    {
                        bounds.Bounds = new Rect(-395+tick*.2f,-180,790,360);
                        scene.Capture();
                        Check(ReferenceEquals(held,scene.Entry.Rt),"small tracked-bound jitter reuses the unchanged native mono allocation");
                    }
                    MenuDepthSources.Unregister(bounds); scene.Source.Ready=false; scene.Capture();
                    Check(PanelSupersample.AllocationFrame(scene.Entry)==original,
                        "retiring spatial content retires its visual sampling frame");
                }
                finally { MenuDepthSources.Unregister(bounds); }
            }
        }
    }

    private sealed class NonBounds728 : IMenuDepthSource
    {
        private readonly Graphic _graphic;
        internal int Reads;
        internal NonBounds728(Graphic graphic) => _graphic=graphic;
        public Graphic Graphic { get { Reads++; return _graphic; } }
        public bool IsReady { get { Reads++; return true; } }
        public void BeforeCapture(MenuDepthEye eye) { }
        public void AfterCapture() { }
    }

    private sealed class Bounds728 : IMenuDepthBoundsSource
    {
        public Graphic Graphic { get; }
        internal Rect Bounds = new(-400,-180,800,360);
        internal Bounds728(Graphic graphic) => Graphic=graphic;
        public bool IsReady => true;
        public bool TryGetForegroundBounds(MenuDepthEye eye,out Rect bounds) { bounds=Bounds; return true; }
        public void BeforeCapture(MenuDepthEye eye) { }
        public void AfterCapture() { }
    }
}
