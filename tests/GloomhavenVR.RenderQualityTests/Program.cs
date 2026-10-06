using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.XR;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string why)
    { _assertions++; if(!value) throw new InvalidOperationException(why); }
    private static void Near(float actual,float expected,string why) => Check(MathF.Abs(actual-expected)<0.0005f,why);
    private static void Tick(float time) { Time.unscaledTime=time; RenderQuality.Tick(); }
    private static void FrameTick(float time) { Time.unscaledTime=time; RenderQuality.TickFromUpdate(); }
    private static XRDisplaySubsystem Start(float saved, int samples=0, bool rejectStartup=false)
    {
        RenderQuality.EndSession(); XRSettings.Reset(); SubsystemManager.Displays.Clear(); VRLog.Lines.Clear();
        Camera.current=null; VRRigDriver.HeadCamera=new(); VRSession.IsRunning=false; Time.unscaledTime=0;
        RenderQuality.Bind(); RenderQuality.EyeResolutionScale!.Value=saved; RenderQuality.MsaaLevel!.Value=samples;
        // Selection is resource-free even if called without a provider. The bootstrap's
        // actual successful-loader boundary is independently bound by the source checker.
        RenderQuality.PrepareSession();
        Check(XRSettings.ResourceWritesBeforeInitialization==0 && XRSettings.AllocationWrites==0
            && XRSettings.ViewportWrites==0 && QualitySettings.AntiAliasingWrites==0,
            "quality selection never touches native resources before provider initialization");
        XRSettings.ProviderInitialized=true;
        var display=new XRDisplaySubsystem { RejectStartupAllocation=rejectStartup }; SubsystemManager.Displays.Add(display);
        RenderQuality.PrepareSession(); // selection after Initialize must recognize a fresh stopped display
        RenderQuality.PrepareDisplays(); display.running=true; XRSettings.Live=true; VRSession.IsRunning=true;
        return display;
    }
    private static void Main()
    {
        var display=Start(1);
        Near(XRSettings.eyeTextureResolutionScale,1,"startup retains native capacity");
        Check(display.AllocationWrites==1,"display allocation selected before first start");
        Tick(0); RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f);
        RenderQuality.EyeResolutionScale.Value=.85f; Tick(.2f); Tick(.54f);
        Near(XRSettings.renderViewportScale,1,"intermediate resolution requests wait for slider quiet");
        Tick(.56f); Near(XRSettings.renderViewportScale,.85f,"latest resolution applies after quiet period");
        Check(XRSettings.LiveAllocationWrites==0 && display.AllocationWrites==1,"live resolution never recreates XR allocation");
        Check(VRLog.Lines.Count(x=>x.StartsWith("Eye resolution live change requested:"))==1,"one committed resolution transition for slider burst");
        int writes=XRSettings.ViewportWrites; for(int i=0;i<100;i++) Tick(.6f+i*.02f);
        Check(XRSettings.ViewportWrites==writes,"stable setting never repeats viewport writes");
        // Native scene/profile setup can reset a previously accepted viewport while
        // the persisted request is unchanged, as observed in the Frame633 run.
        XRSettings.renderViewportScale=1; writes=XRSettings.ViewportWrites;
        Tick(2.7f); Near(XRSettings.renderViewportScale,.85f,"unchanged accepted viewport is restored after native reset");
        Check(XRSettings.ViewportWrites==writes+1 && XRSettings.LiveAllocationWrites==0,
            "viewport reset repair uses one viewport setter and no allocation");
        XRSettings.renderViewportScale=1; writes=XRSettings.ViewportWrites;
        Tick(2.8f); Check(XRSettings.ViewportWrites==writes,"repeated native viewport resets have bounded repair spacing");
        Tick(3.71f); Near(XRSettings.renderViewportScale,.85f,"next spaced viewport repair restores unchanged request");
        RenderQuality.EyeResolutionScale.Value=1; Tick(3.8f); Tick(4.16f);
        Near(XRSettings.renderViewportScale,1,"return to native releases reduced viewport");
        RenderQuality.EyeResolutionScale.Value=1.5f; Tick(4.3f); Tick(4.66f);
        Near(XRSettings.eyeTextureResolutionScale,1,"above-capacity request keeps live allocation");
        Near(XRSettings.renderViewportScale,1,"above-capacity request uses whole existing target");
        Near(RenderQuality.EyeResolutionScale.Value,1.5f,"above-capacity request persists for next VR start");
        Check(VRLog.Lines.Any(x=>x.Contains("above-capacity request saved for next VR restart")),"capacity limit is explicitly reported");
        RenderQuality.RequestEyeTargetDiagnostics("fixture settled"); for(int i=0;i<30;i++) Tick(5+i*.02f);
        Check(VRLog.Lines.Any(x=>x.Contains("Larger allocation saved for next VR restart")),"diagnostic preserves requested versus effective distinction");
        Check(VRLog.Lines.Any(x=>x.Contains("native viewport")),"diagnostic queries native render parameters independently of getter");

        display=Start(1.5f,2); Tick(0);
        Near(XRSettings.eyeTextureResolutionScale,1.5f,"saved larger request selects next startup allocation");
        RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f); Tick(.46f);
        Near(XRSettings.renderViewportScale,.8f/1.5f,"viewport uses requested scale divided by startup capacity");
        RenderQuality.EyeResolutionScale.Value=1; Tick(.5f); Tick(.86f);
        Near(XRSettings.renderViewportScale,1f/1.5f,"native effective scale restores within larger startup allocation");
        Check(XRSettings.LiveAllocationWrites==0,"larger session also retains allocation while reducing");
        int initialMsaa=display.MsaaWrites;
        RenderQuality.MsaaLevel!.Value=4; Tick(1); RenderQuality.MsaaLevel.Value=8; Tick(1.1f); Tick(1.44f);
        Check(display.MsaaWrites==initialMsaa && QualitySettings.antiAliasing==2,"MSAA intermediate cycles do not recreate surfaces");
        Tick(1.46f); Check(display.MsaaWrites==initialMsaa+1 && display.Samples==8,"latest MSAA cycle commits once");
        RenderQuality.MsaaLevel.Value=0; Tick(1.5f); Tick(1.86f);
        Check(display.Samples==8,"consecutive MSAA resource changes have minimum spacing");
        QualitySettings.antiAliasing=4; Tick(2);
        Check(QualitySettings.antiAliasing==8,"native quality swap restores committed MSAA while request waits");
        Tick(2.47f); Check(display.Samples==1 && QualitySettings.antiAliasing==0,"waiting MSAA request eventually commits off");

        display=Start(1); Camera.current=new(); RenderQuality.EyeResolutionScale!.Value=.7f; RenderQuality.MsaaLevel!.Value=4;
        Tick(.1f); Tick(.5f); Near(XRSettings.renderViewportScale,1,"rendering camera blocks resolution writes");
        Check(display.Samples==1,"rendering camera blocks MSAA writes"); Camera.current=null; Tick(.51f);
        Near(XRSettings.renderViewportScale,.7f,"camera boundary release applies waiting viewport");
        Check(display.Samples==4,"camera boundary release applies waiting MSAA");

        display=Start(1); Camera.current=new();
        RenderQuality.EyeResolutionScale!.Value=.8f; RenderQuality.MsaaLevel!.Value=4;
        FrameTick(.1f); FrameTick(.5f);
        Near(XRSettings.renderViewportScale,.8f,"known Update phase commits viewport despite stale Camera.current");
        Check(display.Samples==4,"known Update phase commits MSAA despite stale Camera.current");
        Check(XRSettings.LiveAllocationWrites==0 && display.AllocationWrites==1,
            "known Update phase never recreates live XR allocation");
        QualitySettings.antiAliasing=0; FrameTick(.6f);
        Check(QualitySettings.antiAliasing==4,"known Update restores committed MSAA after native profile swap");
        writes=XRSettings.ViewportWrites; int msaaWrites=display.MsaaWrites;
        for(int i=0;i<100;i++) FrameTick(.7f+i*.02f);
        Check(XRSettings.ViewportWrites==writes && display.MsaaWrites==msaaWrites,
            "known Update phase has no repeated stable resource writes");

        // The stale-camera exception applies to the known Update entry only. Actual unmarked
        // rendering remains blocked after the entry returns; permission belongs to one call.
        RenderQuality.EyeResolutionScale.Value=.7f; RenderQuality.MsaaLevel.Value=8;
        Tick(3); Tick(3.4f);
        Near(XRSettings.renderViewportScale,.8f,"unmarked render call remains guarded after known Update");
        Check(display.Samples==4,"unmarked render call cannot inherit Update MSAA permission");
        display=Start(1); VRRigDriver.HeadCamera!.actualRenderingPath=RenderingPath.DeferredShading;
        RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f); Tick(.5f);
        Near(XRSettings.renderViewportScale,1,"deferred camera cannot take unsupported viewport path");
        int refusals=VRLog.Lines.Count(x=>x.StartsWith("Eye resolution live change refused:"));
        for(int i=0;i<20;i++) Tick(.6f+i*.02f);
        Check(refusals==1 && VRLog.Lines.Count(x=>x.StartsWith("Eye resolution live change refused:"))==1,"deferred refusal is bounded per distinct request");
        VRRigDriver.HeadCamera.actualRenderingPath=RenderingPath.Forward; Tick(2);
        Near(XRSettings.renderViewportScale,.8f,"return to supported rendering applies waiting request");

        display=Start(1); XRSettings.RefuseViewport=true; RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f); Tick(.5f);
        Near(XRSettings.renderViewportScale,1,"provider refusal never pretends effective resolution changed");
        Check(XRSettings.LiveAllocationWrites==0,"provider refusal never uses hazardous allocation fallback");
        for(int i=0;i<30;i++) Tick(.6f+i*.02f);
        Check(VRLog.Lines.Any(x=>x.Contains("REFUSED or still pending")),"provider refusal remains visible in readback");
        writes=XRSettings.ViewportWrites; for(int i=0;i<100;i++) Tick(2+i*.02f);
        Check(XRSettings.ViewportWrites==writes,"provider refusal never causes per-frame setter storm");
        XRSettings.RefuseViewport=false; RenderQuality.EyeResolutionScale.Value=.75f; Tick(5); Tick(5.36f);
        Near(XRSettings.renderViewportScale,.75f,"new request can recover after provider refusal");
        XRSettings.renderViewportScale=1; XRSettings.RefuseViewport=true;
        Tick(7); writes=XRSettings.ViewportWrites;
        for(int i=0;i<100;i++) Tick(7.1f+i*.02f);
        Check(XRSettings.ViewportWrites==writes,"refused native reset repair disarms automatic retries");
        Check(XRSettings.LiveAllocationWrites==0,"refused native reset repair retains session allocation");

        display=Start(1); RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f); Tick(.5f);
        int allocations=XRSettings.AllocationWrites;
        RenderQuality.AdoptRunningSession(); Tick(1); Tick(1.36f);
        Check(XRSettings.AllocationWrites==allocations && display.AllocationWrites==1,"hot reload adopts live allocation without setter");
        Near(XRSettings.renderViewportScale,.8f,"hot reload retains effective scaled view");
        RenderQuality.PrepareSession();
        Check(XRSettings.AllocationWrites==allocations && display.AllocationWrites==1,"existing display blocks startup allocation setters");
        RenderQuality.PrepareDisplays();
        Check(XRSettings.AllocationWrites==allocations && display.AllocationWrites==1,"unexpected running display keeps its original capacity");
        VRSession.IsRunning=false; writes=XRSettings.ViewportWrites; RenderQuality.EyeResolutionScale.Value=.6f; Tick(2);
        Check(XRSettings.ViewportWrites==writes,"stopped session tick never writes XR render state");
        display.running=false; XRSettings.Live=false; SubsystemManager.Displays.Clear(); RenderQuality.EndSession();
        RenderQuality.EyeResolutionScale.Value=.8f; RenderQuality.PrepareSession();
        Near(XRSettings.eyeTextureResolutionScale,1,"new reduced session reserves native restoration capacity");
        display=new XRDisplaySubsystem(); SubsystemManager.Displays.Add(display); RenderQuality.PrepareDisplays();
        Near(XRSettings.renderViewportScale,.8f,"saved reduction starts reduced without live allocation change");
        Near(display.scaleOfAllViewports,.8f,"saved reduction reaches stopped provider before first start");
        Check(display.AllocationWrites==1 && display.ViewportWrites==1 && !display.running,
            "fresh initialized stopped display receives startup quality exactly once");
        Check(XRSettings.LiveAllocationWrites==0,"complete lifecycle has no live allocation setter");
        display=Start(1.5f, rejectStartup:true);
        Check(VRSession.IsRunning && display.running,"managed startup validation refusal does not abort valid VR session");
        Check(VRLog.Lines.Count(x=>x.StartsWith("XR startup setting refused"))==1,"startup validation refusals are explicit and bounded");
        Near(display.scaleOfAllRenderTargets,1,"refused startup allocation retains native display capacity");
        RenderQuality.EyeResolutionScale!.Value=.8f; Tick(.1f); Tick(.5f);
        Near(XRSettings.renderViewportScale,.8f,"refused startup allocation uses actual accepted capacity for later viewport");
        Check(XRSettings.LiveAllocationWrites==0,"startup refusal never schedules live allocation fallback");
        display=Start(.8f); XRSettings.renderViewportScale=1; Camera.current=new();
        Tick(.5f); Near(XRSettings.renderViewportScale,1,"unmarked camera call cannot repair a startup viewport reset");
        FrameTick(.51f); Near(XRSettings.renderViewportScale,.8f,"accepted startup viewport reset is repaired by known Update");
        Check(XRSettings.LiveAllocationWrites==0 && display.AllocationWrites==1,
            "startup viewport reset repair retains the original session allocation");
        Console.WriteLine($"RenderQuality production lifecycle: {_assertions} assertions passed.");
    }
}
