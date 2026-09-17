using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string message){_assertions++;if(!value)throw new Exception(message);}
    private static void Near(float a,float b,string message)=>Check(MathF.Abs(a-b)<0.0001f,message+": "+a+" != "+b);
    private static ConvertedPanel Start(bool appear,out WindowMaterialiseRunner runner,Action? done=null)
    {
        var p=new ConvertedPanel();runner=new(p,appear,done??(()=>{}));
        MrBacking.BeginWindowMaterialise(p);runner.Frame(0);return p;
    }
    private static void Main()
    {
        MrBacking.ResetTest();
        var p=new ConvertedPanel();var runner=new WindowMaterialiseRunner(p,true,()=>{});
        bool measuredBeforeAlpha=false;PanelInkBounds.OnMeasure=()=>measuredBeforeAlpha=runner.Alpha==1;
        MrBacking.BeginWindowMaterialise(p);runner.Frame(0);
        Check(measuredBeforeAlpha,"geometry sampled before first native alpha");
        Check(MrBacking.Active(p),"initial reveal owns backing before normal Tick");
        Check(!MrBacking.Visible(p),"initial reveal begins empty");Near(runner.Alpha,0,"native reveal starts empty");
        Rect frozen=MrBacking.Bounds(p);PanelInkBounds.Bounds=new(0,0,1000,1000);
        for(int i=1;i<=100;i++)
        {
            float k=i/100f;runner.Frame(k);
            WindowMaterialiseField.Progresses(k,true,out float progress,out _);
            Near(MrBacking.Progress(p),progress,"appearance consumes exact native front");
            Near(MrBacking.Bounds(p).width,frozen.width,"no remeasure from shrinking native alpha");
            Check(MrBacking.Visible(p),"appearance backing exists throughout visible frames");
        }
        runner.Finish("completed",true);
        Check(!MrBacking.Active(p)&&!MrBacking.Closed(p),"appearance releases without closed latch");
        Check(MrBacking.Layout(p).Present(frozen,true,4,0,0.15f,out Rect shown),"normal layout already seeded");
        Near(shown.width,frozen.width,"appearance does not shrink and grow a second time");
        Near(runner.Alpha,0.7f,"original native alpha restored");

        MrBacking.ResetTest();int done=0;p=Start(false,out runner,()=>done++);
        Check(MrBacking.Visible(p),"vanish starts complete");
        for(int i=0;i<=100;i++)
        {
            float k=i/100f;runner.Frame(k);WindowMaterialiseField.Progresses(k,false,out float progress,out _);
            Near(MrBacking.Progress(p),progress,"vanish consumes exact native front");
            if(k>=WindowMaterialiseField.ElementSpan)
            { Check(!MrBacking.Visible(p),"no same-frame backing ghost in debris tail");Near(runner.Alpha,0,"native elements absent in debris tail"); }
        }
        Material? fade=MrBacking.Fade(p);runner.Finish("completed",true);runner.Finish("OnDestroy",true);
        Check(done==1,"native callback exactly once");Check(MrBacking.Closed(p)&&!MrBacking.Active(p),"close latched until explicit reopen");
        Check(!MrBacking.Visible(p),"finish cannot resurrect backing");Check(fade==null,"closed material released");
        Check(MrBackingMaterialise.Disposals==1,"close releases owned animation mesh");
        MrBacking.ApplyWindowMaterialise(p,0);Check(!MrBacking.Visible(p),"late stale apply cannot reopen closed backing");
        MrBacking.BeginWindowMaterialise(p);MrBacking.ApplyWindowMaterialise(p,0.3f);
        Check(MrBacking.Active(p)&&!MrBacking.Closed(p)&&MrBacking.Visible(p),"explicit reopen clears close latch");

        // Off at start is distinct from switching off an already-running effect: the lifecycle
        // must exist even while there is no MR plate, otherwise enabling MR resurrects a wall.
        MrBacking.ResetTest();MixedReality.BackingsWanted=false;p=Start(false,out runner);
        Check(!MrBacking.Visible(p),"MR-off begin allocates no visible plate");
        runner.Frame(0.8f);MixedReality.BackingsWanted=true;runner.Frame(0.81f);
        Check(MrBacking.Active(p),"MR-off begin still registers lifecycle");
        Near(MrBacking.Progress(p),1,"MR-on in tail retains native front");
        Check(!MrBacking.Visible(p),"enabling MR during debris tail stays empty");
        runner.Finish("completed",true);Check(MrBacking.Closed(p),"MR-off origin still closes");

        MrBacking.ResetTest();MixedReality.BackingsWanted=false;p=Start(true,out runner);
        Check(!MrBacking.Ready,"first MR-off reveal has no initial material");
        MixedReality.BackingsWanted=true;runner.Frame(0.2f);
        Check(MrBacking.Visible(p)&&MrBacking.Ready,"first MR-on runner frame prepares material before regular Tick");
        Check(MrBacking.Plate(p)!.Renderer.sharedMaterial!=null,"first backing is not an unmaterialed primitive");
        runner.Frame(0.8f);runner.Finish("completed",true);

        MrBacking.ResetTest();MixedReality.BackingsWanted=false;GrabbableModal.Cached=true;
        PanelInkBounds.Throw=true;p=Start(false,out runner);
        Check(MrBacking.Active(p),"off-start cached geometry avoids a new native ink walk");
        Near(MrBacking.Bounds(p).width,GrabbableModal.CachedRect.width,"uses original cached geometry");
        Check(MrBacking.Plate(p)==null,"off-start does not allocate a GPU plate");
        runner.Finish("completed while off",true);
        Check(MrBacking.EntryCount==0,"off-only episode does not leak metadata");

        MrBacking.ResetTest();p=Start(true,out runner);runner.Frame(0.2f);
        Material? offFade=MrBacking.Fade(p);Transform? offPlate=MrBacking.Plate(p);
        MixedReality.BackingsWanted=false;runner.Finish("cancelled while off",true);
        Check(MrBacking.EntryCount==0&&offFade==null&&offPlate==null,"off-end destroys all decoration resources");

        MrBacking.ResetTest();p=new(){MrBackingSuppressed=true};MrBacking.BeginWindowMaterialise(p);
        MrBacking.ApplyWindowMaterialise(p,0);Check(MrBacking.EntryCount==0,"suppressed panel never gains backing state");
        p=new(){HostRect=null};MrBacking.BeginWindowMaterialise(p);
        Check(MrBacking.EntryCount==0,"missing host never gains backing state");

        MrBacking.ResetTest();p=Start(false,out runner);runner.Frame(0.2f);
        MixedReality.BackingsWanted=false;runner.Frame(0.3f);Check(!MrBacking.Visible(p),"MR-off mid effect hides immediately");
        MixedReality.BackingsWanted=true;runner.Frame(0.4f);Check(MrBacking.Visible(p),"MR-on resumes same active field");
        Near(MrBacking.Progress(p),0.4f/WindowMaterialiseField.ElementSpan,"MR toggle preserves progress");
        p.RenderHidden=true;runner.Frame(0.41f);Check(!MrBacking.Visible(p),"local render veil respected");
        p.RenderHidden=false;p.OwnerRenderHidden=true;runner.Frame(0.42f);Check(!MrBacking.Visible(p),"remote owner veil respected");
        p.OwnerRenderHidden=false;p.HostGo!.activeInHierarchy=false;runner.Frame(0.43f);Check(!MrBacking.Visible(p),"inactive host cannot draw backing");
        p.HostGo=null;runner.Frame(0.44f);Check(!MrBacking.Visible(p),"destroyed host cannot draw backing");

        MrBacking.ResetTest();PanelInkBounds.Valid=false;p=Start(true,out runner);runner.Frame(0.4f);
        Check(!MrBacking.Visible(p),"missing ink never falls back to huge transparent host");
        runner.Finish("cancelled",true);Check(!MrBacking.Active(p),"cancel releases animation ownership");

        foreach(bool hideThrows in new[]{false,true})foreach(bool logThrows in new[]{false,true})
        {
            MrBacking.ResetTest();done=0;p=Start(false,out runner,()=>done++);
            MrBacking.Plate(p)!.gameObject.ThrowOnSet=hideThrows;
            MrBackingMaterialise.ThrowRestore=true;GloomhavenVR.Core.VRLog.Throw=logThrows;
            runner.Finish("teardown",true);runner.Finish("recovery",true);
            Check(done==1,"backing restore/hide/log failure cannot block native callback");
            Check(MrBacking.Closed(p)&&!MrBacking.Active(p),"fault permanently disarms animation");
            Check(runner.Restores==1&&runner.Returns==1,"callback path cleans native state once");
        }
        MrBacking.ResetTest();done=0;p=Start(false,out runner,()=>done++);MrBackingMaterialise.ThrowApply=true;
        runner.Frame(0.2f);Check(!MrBacking.Active(p)&&!MrBacking.Visible(p),"draw failure disables only decoration");
        runner.Finish("completed",true);Check(done==1,"draw failure preserves close continuation");
        MrBacking.ResetTest();PanelInkBounds.Throw=true;done=0;p=Start(false,out runner,()=>done++);
        runner.Finish("completed",true);Check(done==1,"measurement failure preserves close continuation");
        Console.WriteLine($"MR backing animation: {_assertions} runtime assertions passed.");
    }
}
