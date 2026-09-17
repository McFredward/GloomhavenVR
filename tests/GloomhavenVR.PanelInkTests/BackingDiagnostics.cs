using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static void TestBackingDiagnostics()
    {
        var host=new GameObject("DiagnosticHost");host.transform.rect=new Rect(-100,-50,200,100);
        var canvas=host.AddComponent<Canvas>();
        var target=new GameObject("PooledNativeWindow");target.transform.SetParent(host.transform);
        target.transform.rect=host.transform.rect;
        var background=target.AddComponent<RawImage>();background.canvas=canvas;
        var top=new GameObject("ActualTopGlyph");top.transform.SetParent(target.transform);
        top.AddComponent<CanvasGroup>().alpha=0; // model a stale renderer/group disagreement for evidence only
        var text=top.AddComponent<TMPro.TMP_Text>();text.text="Top";text.canvas=canvas;
        text.mesh=PaintedRect(-20,100,20,110);
        var left=new GameObject("ActualLeftImage");left.transform.SetParent(target.transform);
        left.transform.rect=new Rect(-200,-10,20,20);var picture=left.AddComponent<Image>();picture.canvas=canvas;
        var panel=new ConvertedPanel{HostRect=host.transform,Target=target.transform};
        VRLog.Enabled=false;
        Check(PanelInkBounds.TryMeasure(panel,out var baseline,backingGeometry:true),"diagnostic fixture has valid native geometry");
        Check(ReferenceEquals(baseline.MrTop,text) && ReferenceEquals(baseline.MrLeft,picture)
            && baseline.MrTopRect.yMax==110 && baseline.MrLeftRect.xMin==-200,
            "MR diagnostic extrema come from the actual clipped contributing graphics");
        VRLog.Lines.Clear();VRLog.Enabled=true;
        Check(PanelInkBounds.TryMeasure(panel,out var ink,backingGeometry:true)
            && ink.Rect.xMin==baseline.Rect.xMin && ink.Rect.yMax==baseline.Rect.yMax,
            "enabling diagnostics does not change native bounds");
        Check(VRLog.Lines.Count==1 && VRLog.Lines[0].Contains("MR BOUNDS SOURCE:")
            && VRLog.Lines[0].Contains("TOP=TMP_Text") && VRLog.Lines[0].Contains("ActualTopGlyph")
            && VRLog.Lines[0].Contains("LEFT=Image") && VRLog.Lines[0].Contains("ActualLeftImage")
            && VRLog.Lines[0].Contains("ownA=") && VRLog.Lines[0].Contains("inheritedA=")
            && VRLog.Lines[0].Contains("CanvasGroup alpha=0")
            && VRLog.Lines[0].Contains("NativeShader") && VRLog.Lines[0].Contains("materialColorA=0.75"),"first MR trace identifies original extrema and native rendering evidence");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) MrBackingBoundsTrace.Observe(panel,ink,true);
        Check(VRLog.Lines.Count==1 && GC.GetAllocatedBytesForCurrentThread()==before,
            "unchanged diagnostics emit no duplicate records or allocations");
        text.mesh=PaintedRect(-20,200,20,210);
        Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true) && ink.Rect.yMax==210 && VRLog.Lines.Count==2,
            "material extent change produces a fresh trace without altering the extent");
        for(int i=0;i<20;i++)
        {
            text.mesh=PaintedRect(-20,300+i*100,20,310+i*100);
            Check(PanelInkBounds.TryMeasure(panel,out ink,backingGeometry:true),"diagnostic budget never blocks later measurements");
        }
        Check(VRLog.Lines.Count==8,"each converted panel has an eight-record diagnostic ceiling");
        var reopened=new ConvertedPanel{HostRect=host.transform,Target=target.transform};
        Check(PanelInkBounds.TryMeasure(reopened,out _,backingGeometry:true) && VRLog.Lines.Count==9,
            "same pooled native window gets independent evidence for a new conversion");
        text.ThrowOnMaterial=true;
        var throwing=new ConvertedPanel{HostRect=host.transform,Target=target.transform};
        Check(PanelInkBounds.TryMeasure(throwing,out ink,backingGeometry:true) && ink.Valid && ink.Rect.yMax==2210,
            "throwing diagnostic getter cannot invalidate successful native geometry");
        text.ThrowOnMaterial=false;VRLog.ThrowOnNote=true;
        Check(PanelInkBounds.TryMeasure(new ConvertedPanel{HostRect=host.transform,Target=target.transform},out ink,backingGeometry:true)
            && ink.Valid,"logging failure cannot alter visibility or geometry");
        VRLog.ThrowOnNote=false;VRLog.Enabled=false;VRLog.Lines.Clear();
    }
}
