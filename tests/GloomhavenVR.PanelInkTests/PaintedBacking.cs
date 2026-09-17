using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static Mesh PaintedRect(float x0,float y0,float x1,float y1)
    {
        var mesh=new Mesh();
        mesh.Points.Add(new Vector3(x0,y0,0));mesh.Points.Add(new Vector3(x0,y1,0));
        mesh.Points.Add(new Vector3(x1,y1,0));mesh.Points.Add(new Vector3(x1,y0,0));
        for(int i=0;i<4;i++) mesh.Colors.Add(new Color32{a=255});
        return mesh;
    }

    private static void TestPaintedBacking()
    {
        TestNativeImageAndLegacyGeometry();
        for(int observer=0;observer<2;observer++)
        {
            var host=new GameObject(observer==0 ? "LocalMerchant" : "RemoteMerchant");
            host.transform.rect=Rect.MinMaxRect(-960,-540,960,540);
            var canvas=host.AddComponent<Canvas>();
            var native=new GameObject("OriginalArtwork");native.transform.SetParent(host.transform);
            native.transform.rect=host.transform.rect;
            var art=native.AddComponent<Image>();art.canvas=canvas;
            var title=new GameObject("NativeLabel");title.transform.SetParent(native.transform);
            title.transform.rect=Rect.MinMaxRect(-400,-100,400,2160);
            var text=title.AddComponent<TMPro.TMP_Text>();text.text="Merchant";text.canvas=canvas;
            text.mesh=PaintedRect(-150,400,150,450);
            var panel=new ConvertedPanel{HostRect=host.transform,Target=native.transform};
            var witnesses=new List<Graphic>();
            Check(PanelInkBounds.TryMeasure(panel,out var legacy) && legacy.Rect.yMax==2160 && legacy.Plates==1,
                "legacy grab and hit geometry retains its existing rectangle contract");
            Check(PanelInkBounds.TryMeasure(panel,out var ink,visibleWitnesses:witnesses,backingGeometry:true)
                && ink.Rect.yMax==540 && ink.Rect.yMin==-540 && ink.Rect.width==1920 && ink.Plates==0,
                "painted merchant bounds exclude a tall empty label layout rectangle");
            Check(witnesses.Count==2 && ink.Graphics==2,"painted backdrop remains original measured content");
            for(int reopening=0;reopening<4;reopening++)
            {
                title.transform.rect=Rect.MinMaxRect(-400,-100,400,1370+790*reopening);
                Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.yMax==540,
                    "reopening layout drift cannot enlarge a painted merchant backing");
            }
            var hover=new GameObject("OriginalItemTooltip");hover.transform.SetParent(native.transform);
            hover.transform.rect=Rect.MinMaxRect(-100,700,100,1300);
            var hint=hover.AddComponent<TMPro.TMP_Text>();hint.text="Item";hint.canvas=canvas;
            hint.mesh=PaintedRect(-100,900,100,1100);
            TransientFamilies.Hover=hover.transform;
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.yMax==540,
                "painted tooltip text cannot re-enter the owner backing union");
            Check(hint.mesh.Reads==0,"known tooltip family never triggers painted mesh readback");
            TransientFamilies.Hover=null;hover.activeSelf=false;
            // Genuine unmasked glyph overflow is content: damage prompts and VR notes still fit.
            text.mesh=PaintedRect(-1150,600,1150,660);
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)
                && ink.Rect.xMin==-1150 && ink.Rect.xMax==1150 && ink.Rect.yMax==660,
                "genuine painted text outside the window remains backed");
            // Native aspect fitting can draw a picture smaller OR taller than its layout box.
            text.enabled=false;
            native.transform.rect=Rect.MinMaxRect(-960,-725,960,725);
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.height==1450,
                "wide-host merchant artwork retains its full actual painted height");
            native.transform.rect=Rect.MinMaxRect(-960,-540,960,540);
            art.preserveAspect=true;art.overrideSprite=new Sprite{rect=new Rect(0,0,600,400)};
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.width==1620 && ink.Rect.height==1080,
                "preserved-aspect native image uses native aspect fitting instead of transparent layout margins");
            var mask=native.AddComponent<Mask>();mask.showMaskGraphic=false;
            text.enabled=true;text.mesh=PaintedRect(-1200,400,1200,900);
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)
                && ink.Rect.xMin==-960 && ink.Rect.xMax==960 && ink.Rect.yMax==540 && ink.Rect.yMin==400,
                "nonpainting stencil keeps descendant clipping without adding its own layout extent");
            mask.enabled=false;
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.yMax==900,
                "disabled stencil neither clips children nor hides original artwork");
            text.enabled=false;
            var empty=new GameObject("PendingPicture");empty.transform.SetParent(host.transform);
            empty.transform.rect=Rect.MinMaxRect(-1000,-1000,1000,3000);
            var pending=empty.AddComponent<TMPro.TMP_Text>();pending.text="Ready";pending.canvas=canvas;
            panel.Target=empty.transform;
            Check(!PanelInkBounds.TryMeasure(panel,out _,visibleWitnesses:witnesses,backingGeometry:true)
                && witnesses.Count==0,"missing native mesh cannot reuse the previous picture or invent a host backing");
            pending.mesh=PaintedRect(-10,-10,10,10);
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.width==20,
                "later native mesh readiness is measurable without rebuilding or modifying the window");
            for(int i=0;i<4;i++) pending.mesh.Colors[i]=new Color32{a=0};
            Check(!PanelInkBounds.TryMeasure(panel,out _,backingGeometry:true),"fully transparent mesh has no painted backing");
            pending.mesh=PaintedRect(100,100,200,200);
            pending.mesh.Points.Add(new Vector3(-5000,-5000,0));
            pending.mesh.Colors.Add(new Color32{a=0});
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.xMin==100 && ink.Rect.yMin==100,
                "unused transparent TMP vertices cannot enlarge the painted union");
            var nested=empty.AddComponent<Canvas>();nested.enabled=false;
            // Unity owns canvas resolution; an enabled drawing canvas is not rejected merely
            // because another disabled canvas component exists in its ancestors.
            Check(PanelInkBounds.TryMeasure(panel,out _,backingGeometry:true),"painted query respects Unity actual drawing canvas without blanket ancestor suppression");
            nested.enabled=true;canvas.enabled=false;
            Check(!PanelInkBounds.TryMeasure(panel,out _,backingGeometry:true),"disabled associated native canvas cannot paint a backing");
            canvas.enabled=true;
            pending.mesh=PaintedRect(-10,-20,10,20);
            host.transform.localPosition=new Vector3(20,30,0);host.transform.localScale=new Vector3(2,3,1);
            empty.transform.angleDegrees=90;empty.transform.localPosition=new Vector3(40,60,0);
            Mesh originalMesh=pending.mesh;
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true)
                && Math.Abs(ink.Rect.xMin-20)<.001 && Math.Abs(ink.Rect.xMax-60)<.001
                && Math.Abs(ink.Rect.yMin-50)<.001 && Math.Abs(ink.Rect.yMax-70)<.001,
                "painted vertices retain the complete native transform chain");
            Check(ReferenceEquals(pending.mesh,originalMesh)
                && empty.transform.rect.yMax==3000 && pending.enabled && pending.color.a==1,
                "MR measurement does not mutate native layout or visibility");
            _=MrBackingPaintedBounds.TryMeasure(host.transform,pending,out _);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++) _=MrBackingPaintedBounds.TryMeasure(host.transform,pending,out _);
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"painted mesh readback reuses buffers without per-sample allocations");
            panel.Target=native.transform;
            Check(!PanelInkBounds.TryMeasure(panel,out _,contentRoot:empty.transform,backingGeometry:true),
                "painted query keeps the explicit content owner boundary");
        }
    }
    private static void TestNativeImageAndLegacyGeometry()
    {
        var host=new GameObject("NativeTypes");var canvas=host.AddComponent<Canvas>();
        var go=new GameObject("NativeImage");go.transform.SetParent(host.transform);
        go.transform.rect=new Rect(0,0,400,200);
        var image=go.AddComponent<Image>();image.canvas=canvas;
        image.overrideSprite=new Sprite{rect=new Rect(0,0,200,100),Padding=new Vector4(10,5,20,10)};
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out var rect)
            && rect.xMin==20 && rect.yMin==10 && rect.xMax==360 && rect.yMax==180,
            "native active sprite packed padding determines painted image dimensions");
        image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;image.fillAmount=.5f;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out rect) && rect.xMax==190,
            "horizontal fill uses the original active sprite drawing interval");
        image.fillOrigin=1;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out rect) && rect.xMin==190,
            "opposite fill origin retains the correct painted edge");
        image.fillMethod=Image.FillMethod.Vertical;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out rect) && rect.yMin==95,
            "vertical fill retains its actual native extent");
        image.fillAmount=0;
        Check(!MrBackingPaintedBounds.TryMeasure(host.transform,image,out _),"empty filled image has no painted backing");
        Sprite activeSprite=image.overrideSprite!;image.overrideSprite=null;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out rect) && rect.width==400 && rect.height==200,
            "image without an active sprite retains the native solid quad despite zero fill");
        image.overrideSprite=activeSprite;
        image.type=Image.Type.Sliced;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,image,out rect) && rect.width==400 && rect.height==200,
            "sliced original artwork conservatively retains its own drawing rectangle");
        var movie=new GameObject("OriginalMovie");movie.transform.SetParent(host.transform);
        movie.transform.rect=new Rect(-50,-25,100,50);var raw=movie.AddComponent<RawImage>();raw.canvas=canvas;
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,raw,out rect) && rect.width==100 && rect.height==50,
            "native raw image remains backed before any text mesh exists");
        var label=new GameObject("Legacy");label.transform.SetParent(host.transform);
        var legacy=label.AddComponent<Text>();legacy.canvas=canvas;legacy.text="Two glyphs";legacy.pixelsPerUnit=2;
        foreach(var p in PaintedRect(20,40,40,60).Points)
            legacy.cachedTextGenerator.verts.Add(new UIVertex{position=p,color=new Color32{a=255}});
        foreach(var p in PaintedRect(60,40,80,60).Points)
            legacy.cachedTextGenerator.verts.Add(new UIVertex{position=p,color=new Color32{a=255}});
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,legacy,out rect)
            && rect.xMin==10 && rect.xMax==40 && rect.yMin==20 && rect.yMax==30,
            "Unity 2021 legacy cached text retains the final real glyph and native pixel scale");
        for(int i=0;i<4;i++) legacy.cachedTextGenerator.verts.Add(new UIVertex{position=new Vector3(-9000,-9000,0),color=new Color32{a=255}});
        Check(MrBackingPaintedBounds.TryMeasure(host.transform,legacy,out rect) && rect.xMin==10 && rect.yMin==20,
            "degenerate legacy generator quad cannot invent an opaque rectangle");
        legacy.cachedTextGenerator.verts.Clear();
        Check(!MrBackingPaintedBounds.TryMeasure(host.transform,legacy,out _),"missing legacy text geometry defers only its backing");
    }

}
