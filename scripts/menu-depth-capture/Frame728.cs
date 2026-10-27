using System;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthProgram
{
    // Renderer bounds/XR observations are explicit inputs in this capture port.
    // The production coordinator, expanded projection/carrier and ResolveRate
    // allocation decision execute; the separate character raster proves that
    // actual model pixels provide the same footprint and surface-depth contract.
    private static void Frame728(string output)
    {
        using (var scene = new Scene())
        {
            scene.Head.orthographicSize = 1.2f;
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
                    Canvas.ForceUpdateCanvases(); scene.Capture();
                    Rect sampled = PanelSupersample.AllocationFrame(scene.Entry);
                    Check(sampled.width > original.width && sampled.height == original.height,
                        "actual foreground footprint expands only the private sampling frame");
                    Check(scene.Entry.Rt.width >= sampled.width && scene.Entry.Rt.height >= sampled.height,
                        "the real ResolveRate decision preserves original capture density when overspill grows");
                    Check(scene.Entry.Frame == original && scene.Panel.position == position && scene.Panel.localScale == scale,
                        "expanded capture never changes native layout input or synchronized window geometry");
                    Check(scene.Entry.VramBytes + PanelSupersample.ExtraBytes <= 160L*1024*1024,
                        "both actually created expanded eye targets remain inside the native panel memory ceiling");
                    using (TextureOwner expanded = scene.Eye(false,"728-expanded-capture"))
                    {
                        Color32[] a = baseline.Image.GetPixels32(), b = expanded.Image.GetPixels32();
                        double difference = 0;
                        for (int i=0;i<a.Length;i++) difference += Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)+Math.Abs(a[i].a-b[i].a);
                        Check(difference / (a.Length*4) < .3,
                            "actual expanded head raster preserves native colours text and old window clipping");
                        File.WriteAllText(Path.Combine(output,"728-frame-allocation.txt"),"native="+original+";sampled="+sampled
                            +";RT="+scene.Entry.Rt.width+"x"+scene.Entry.Rt.height+";meanChannelDifference="+difference/(a.Length*4)+"\n");
                    }
                    RenderTexture held = scene.Entry.Rt;
                    for (int tick=0;tick<6;tick++)
                    {
                        bounds.Bounds = new Rect(-395+tick*.2f,-180,790,360);
                        scene.Capture();
                        Check(ReferenceEquals(held,scene.Entry.Rt),"small tracked-bound jitter reuses the expanded capture allocation");
                    }
                    MenuDepthSources.Unregister(bounds); scene.Source.Ready=false; scene.Capture();
                    Check(PanelSupersample.AllocationFrame(scene.Entry)==original,
                        "retiring spatial content retires its visual sampling frame");
                }
                finally { MenuDepthSources.Unregister(bounds); }
            }
        }
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
