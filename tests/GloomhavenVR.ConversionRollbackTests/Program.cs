using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
internal static class Program
{
    internal static readonly List<RectTransform> Targets=new();
    private static int _assertions;
    private static void Check(bool value,string reason){_assertions++;if(!value)throw new InvalidOperationException(reason);}
    private static ConvertedPanel NewPanel()
    {
        var home=new GameObject("native home");var target=new GameObject("native decision");
        target.Add<UIWindow>();target.Add<CanvasGroup>();target.transform.parent=home.transform;
        Targets.Add(target.transform);
        return new ConvertedPanel { Target=target.transform,OriginalParent=home.transform,OriginalSizeDelta=new Vector2(300,400),TargetWasPersistent=true,TargetHomeScene=99 };
    }
    private static void Adopt(ConvertedPanel p)
    {
        p.HostGo=new GameObject("mod host");p.HostCanvas=p.HostGo.Add<Canvas>();p.Target.parent=p.HostGo.transform;
        var canvas=p.Target.gameObject.Add<Canvas>();canvas.enabled=false;
        // Attachment boundary: include the native menu's recorded mode lease in
        // every failure path. Actual Unity adapter/pixels run in desktop-render.
        p.NativeMenuCanvas=canvas;p.OriginalNativeMenuCanvasMode=RenderMode.ScreenSpaceCamera;
        canvas.renderMode=RenderMode.WorldSpace;
        p.HiddenCanvases.Add(canvas);p.AdoptedCanvases.Add(new NestedCanvasRecord { Canvas=canvas,OriginalWorldCamera=new GameObject("native camera").Add<Camera>(),OriginalOverrideSorting=true,ConcededOverrideSorting=true,OriginalSortingOrder=6 });
        p.Relayered.Add(new LayerRecord { Transform=p.Target,OriginalLayer=5 });p.Target.gameObject.layer=27;
        var bg=p.Target.gameObject.Add<Graphic>();bg.enabled=false;p.HiddenBackgrounds.Add(bg);p.KeepBackgroundHidden=true;
        var added=p.Target.gameObject.Add<RectMask2D>();p.AddedScrollMasks.Add(added);
        p.Flattened.Add(new FlattenRecord { Transform=p.Target,OriginalLocalZ=8 });
    }
    private static void NativeRestored(ConvertedPanel p, bool construction = true)
    {
        Check(ReferenceEquals(p.Target.parent,p.OriginalParent),"Failed conversion restores the native hierarchy");
        Check(!p.Target.gameObject.Destroyed,"Rollback must preserve native objects");
        Check(p.Target.GetComponent<UIWindow>()!.IsOpen,"Rollback must not close the native decision");
        Check(p.Target.GetComponent<CanvasGroup>()!.interactable,"Rollback preserves native button permission");
        Check(p.Target.sizeDelta.x==300,"Rollback restores original layout");
        // Destroyed Unity objects compare equal to null; ownership still needs
        // checking after successful destruction, including every native receipt.
        if(!ReferenceEquals(p.HostGo,null))
        {
            Check(p.HostGo.Destroyed,"Empty mod host is destroyed after native detach");
            var canvas=p.Target.GetComponent<Canvas>();
            if(canvas!=null)
            {
                Check(canvas.enabled,"Failed reveal restores the native canvas");
                Check(canvas.worldCamera!=null && canvas.worldCamera.gameObject.name=="native camera","Rollback restores original native camera");
                Check(canvas.renderMode==RenderMode.ScreenSpaceCamera && p.NativeMenuCanvas==null,
                    "Rollback restores original menu camera mode after native ownership");
                Check(canvas.overrideSorting && canvas.sortingOrder==6,"Rollback restores native canvas ordering");
                Check(p.Target.gameObject.layer==5,"Rollback restores native render layer");
                if(construction)Check(p.Target.GetComponent<Graphic>()!.enabled,"Failed menu conversion restores its native background");
                Check(p.Target.GetComponent<RectMask2D>()!.Destroyed,"Rollback removes only its added scroll mask");
            }
        }
    }
    private static void BenchmarkLayerSweep()
    {
        // Diagnostic only: compares the extracted production method against the
        // previous method in the same managed fixture. Unity's native component
        // and canvas costs are absent, so this is not a headset-frame estimate.
        const int runs=150;
        long ticks=0;
        for(int run=0;run<runs;run++)
        {
            var panel=new ConvertedPanel { HostGo=new GameObject("original enhancement inventory") };
            panel.HostGo.layer=5;
            for(int i=0;i<549;i++)
            {
                var child=new GameObject("native card option") { layer=5 };
                panel.HostGo.transform.AddChild(child.transform);
            }
            long before=Stopwatch.GetTimestamp();
            CanvasConversion.SweepForTest(panel,initial:true);
            ticks+=Stopwatch.GetTimestamp()-before;
        }
        Console.WriteLine($"Layer sweep 549 nodes: {ticks*1000.0/Stopwatch.Frequency/runs:F3} ms/run over {runs} runs.");
    }
    private static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--layer-bench") { BenchmarkLayerSweep();return; }
        // Inject at each conversion ownership boundary. The executed scope, rollback,
        // Release and host-destruction guard are production methods extracted unchanged.
        for(int phase=0;phase<4;phase++)
        {
            var p=NewPanel();var failure=new InvalidOperationException("original conversion failure");Exception? seen=null;
            try
            {
                using var transaction=CanvasConversion.Transaction(p);
                if(phase==1)p.HostGo=new GameObject("allocated host");
                if(phase>=2)Adopt(p);
                if(phase>=3)CanvasConversion.Active.Add(p);
                throw failure;
            }
            catch(Exception ex){seen=ex;}
            Check(ReferenceEquals(seen,failure),"Rollback preserves the original construction exception");
            NativeRestored(p);
            Check(!CanvasConversion.Pending(p.Target) && !CanvasConversion.Active.Contains(p),"Successful rollback removes failed ownership");
        }
        var committed=NewPanel();
        using(var transaction=CanvasConversion.Transaction(committed)){Adopt(committed);CanvasConversion.CompleteTransaction(transaction);}
        Check(ReferenceEquals(committed.Target.parent,committed.HostGo!.transform),"Successful conversion commits without rollback");
        CanvasConversion.Release(committed);
        var menuPinFailure=NewPanel();menuPinFailure.HostGo=new GameObject("partial menu host");
        var pinCanvas=menuPinFailure.Target.gameObject.Add<Canvas>();pinCanvas.renderMode=RenderMode.ScreenSpaceCamera;
        menuPinFailure.Target.ThrowParentCount=1;
        Exception? pinError=null;
        try
        {
            using var transaction=CanvasConversion.Transaction(menuPinFailure);
            // Verbatim production pin/parent/pose block: SetParent faults AFTER
            // the mode was changed, before Convert could return its panel.
            CanvasConversion.AttachMenuFrameForTest(menuPinFailure);
        }
        catch(Exception error){pinError=error;}
        Check(pinError?.Message=="reparent fault","Actual menu handoff fault follows the recorded native mode pin");
        Check(pinCanvas.renderMode==RenderMode.ScreenSpaceCamera && menuPinFailure.NativeMenuCanvas==null,
            "Construction exception before Convert returns restores native menu camera mode");
        Check(ReferenceEquals(menuPinFailure.Target.parent,menuPinFailure.OriginalParent)
            && !CanvasConversion.Pending(menuPinFailure.Target),"Construction rollback restores native home before retiring menu receipt");
        foreach(bool enrolled in new[]{false,true})
        {
            var p=NewPanel();Adopt(p);CanvasConversion.Active.Add(p);var grab=new GrabbableModal();
            var wp=new ModalFallback.WindowPanel{Panel=p};if(enrolled)ModalFallback.Converted.Add(wp);
            bool result=ModalFallback.AttachForTest(p.Target.GetComponent<UIWindow>()!,p,grab,enrolled?wp:null,()=>throw new InvalidOperationException("attachment failure"));
            Check(!result,"Failed modal attachment requests native desktop fallback");
            Check(grab.Destroyed,"Failed attachment destroys its partial mod chrome");
            Check(!ModalFallback.Converted.Contains(wp),"Failed modal enrollment must be removed");
            NativeRestored(p);
        }
        var cameraFailure=NewPanel();Adopt(cameraFailure);CanvasConversion.CameraRestoreFaults=1;
        CanvasConversion.RollbackFailedConversion(cameraFailure);
        Check(CanvasConversion.Pending(cameraFailure.Target),"Failed restoration retains rollback ownership");
        Check(cameraFailure.AdoptedCanvases.Count==1,"Failed camera restoration retains original camera snapshots");
        Check(cameraFailure.NativeMenuCanvas!=null,"Failed camera restoration retains original menu mode receipt");
        CanvasConversion.Retry();NativeRestored(cameraFailure);
        Check(!CanvasConversion.Pending(cameraFailure.Target),"Normal frame retries incomplete native restoration");
        var detachFailure=NewPanel();Adopt(detachFailure);detachFailure.Target.ThrowParentCount=1;
        CanvasConversion.RollbackFailedConversion(detachFailure);
        Check(CanvasConversion.Pending(detachFailure.Target) && !detachFailure.HostGo!.Destroyed,"Failed native detach must retain live host and restore record");
        CanvasConversion.Retry();NativeRestored(detachFailure);
        var chromeFailure=NewPanel();Adopt(chromeFailure);var persistentGrab=new GrabbableModal{Failures=3};
        CanvasConversion.RollbackFailedConversion(chromeFailure,persistentGrab);NativeRestored(chromeFailure);
        int attempts=CanvasConversion.ReleaseAttempts;chromeFailure.Target.sizeDelta=new Vector2(999,999);
        for(int i=0;i<3;i++)CanvasConversion.Retry();
        Check(CanvasConversion.ReleaseAttempts==attempts,"Chrome retries must not repeat successful native restoration");
        Check(chromeFailure.Target.sizeDelta.x==999,"Chrome retry must preserve later native layout changes");
        Check(persistentGrab.Destroyed && !CanvasConversion.Pending(chromeFailure.Target),"Chrome-only retry releases remaining ownership");
        var hostFailure=NewPanel();Adopt(hostFailure);hostFailure.HostGo!.DestroyFaults=1;
        CanvasConversion.RollbackFailedConversion(hostFailure);
        Check(CanvasConversion.Pending(hostFailure.Target) && !hostFailure.HostGo.Destroyed,"Failed mod-host cleanup retains ownership after native restore");
        int hostAttempts=CanvasConversion.ReleaseAttempts;hostFailure.Target.sizeDelta=new Vector2(777,777);
        CanvasConversion.Retry();
        Check(CanvasConversion.ReleaseAttempts==hostAttempts && hostFailure.Target.sizeDelta.x==777,"Host cleanup retry must not replay old native layout");
        Check(hostFailure.HostGo.Destroyed && !CanvasConversion.Pending(hostFailure.Target),"Host-only cleanup retry finishes remaining ownership");
        var refused=NewPanel();Adopt(refused);refused.Target.RefuseParent=true;
        CanvasConversion.RollbackFailedConversion(refused);
        Check(!refused.HostGo!.Destroyed && !refused.Target.gameObject.Destroyed && CanvasConversion.Deferred>0,"Refused detach must defer host destruction without deleting native content");
        Check(CanvasConversion.Pending(refused.Target) && refused.AdoptedCanvases.Count==1,"Silently refused detach must retain native restoration ownership");
        CanvasConversion.Retry();
        Check(CanvasConversion.Pending(refused.Target) && refused.AdoptedCanvases.Count==1 && !refused.HostGo.Destroyed,"Still-refused retry must retain native home snapshot and live host");
        refused.Target.RefuseParent=false;CanvasConversion.Retry();NativeRestored(refused);
        Check(!CanvasConversion.Pending(refused.Target),"Retry must restore original native parent after silent detach refusal");
        var detachedOnly=NewPanel();Adopt(detachedOnly);detachedOnly.Target.RefuseSpecificParent=detachedOnly.OriginalParent;
        CanvasConversion.RollbackFailedConversion(detachedOnly);
        Check(detachedOnly.Target.parent==null && detachedOnly.HostGo!.Destroyed,"Safety detach may preserve native content at scene root");
        Check(CanvasConversion.Pending(detachedOnly.Target) && detachedOnly.AdoptedCanvases.Count==1,"Scene-root detach must not complete a pending original-home restore");
        detachedOnly.Target.RefuseSpecificParent=null;CanvasConversion.Retry();NativeRestored(detachedOnly);
        Check(!CanvasConversion.Pending(detachedOnly.Target),"Retry must restore live native home after safety detach");
        var normalRefusal=NewPanel();Adopt(normalRefusal);normalRefusal.Target.RefuseParent=true;
        CanvasConversion.Release(normalRefusal);
        Check(CanvasConversion.Pending(normalRefusal.Target) && normalRefusal.NativeMenuCanvas!=null,
            "Normal menu release retains refused native home and mode receipt");
        int pendingMenus=CanvasConversion.PendingCountForTest;CanvasConversion.Release(normalRefusal);
        Check(CanvasConversion.PendingCountForTest==pendingMenus,
            "Normal refused menu release never duplicates the existing retry owner");
        normalRefusal.Target.RefuseParent=false;CanvasConversion.Retry();NativeRestored(normalRefusal,construction:false);
        Check(!CanvasConversion.Pending(normalRefusal.Target),"Normal refused menu home recovers through existing native retry");
        var normalDetached=NewPanel();Adopt(normalDetached);normalDetached.Target.RefuseSpecificParent=normalDetached.OriginalParent;
        CanvasConversion.Release(normalDetached);
        Check(normalDetached.HostGo!.Destroyed && normalDetached.Target.parent==null && normalDetached.NativeMenuCanvas!=null
            && CanvasConversion.Pending(normalDetached.Target),"Normal persistent menu receipt survives safety detach and destroyed host");
        normalDetached.Target.RefuseSpecificParent=null;CanvasConversion.Retry();NativeRestored(normalDetached,construction:false);
        Check(!CanvasConversion.Pending(normalDetached.Target),"Retained menu receipt restores original home after host destruction");
        var ordinaryRefusal=NewPanel();Adopt(ordinaryRefusal);ordinaryRefusal.NativeMenuCanvas=null;
        ordinaryRefusal.Target.RefuseParent=true;CanvasConversion.Release(ordinaryRefusal);
        Check(!CanvasConversion.Pending(ordinaryRefusal.Target),"Ordinary release without menu mode lease preserves generic deferred ownership");
        ordinaryRefusal.Target.RefuseParent=false;CanvasConversion.Release(ordinaryRefusal);
        // Run the production relayer on an enchantress-sized original subtree.
        // Initial ownership records every original layer once. A later native
        // relayer must not overwrite that snapshot, while a newly pooled child
        // must still acquire one. The second assertion rejects an unguarded
        // recurring sweep even if the initial fast path looks correct.
        var layerPanel=new ConvertedPanel { HostGo=new GameObject("large original enhancement list") };
        layerPanel.HostGo.layer=5;
        var originalNodes=new List<GameObject>();
        for(int i=0;i<549;i++)
        {
            var child=new GameObject("native slot " + i) { layer=5 };
            layerPanel.HostGo.transform.AddChild(child.transform);originalNodes.Add(child);
        }
        CanvasConversion.SweepForTest(layerPanel,initial:true);
        Check(layerPanel.Relayered.Count==550,"Initial large conversion records each original layer once");
        Check(originalNodes.TrueForAll(node=>node.layer==27),"Initial large conversion moves every original UI node");
        originalNodes[20].layer=13;
        CanvasConversion.SweepForTest(layerPanel,initial:false);
        Check(layerPanel.Relayered.Count==550 && layerPanel.Relayered[21].OriginalLayer==5,
            "Recurring sweep preserves the first original layer after native relayering");
        var pooled=new GameObject("pooled native slot") { layer=9 };
        layerPanel.HostGo.transform.AddChild(pooled.transform);
        CanvasConversion.SweepForTest(layerPanel,initial:false);
        Check(layerPanel.Relayered.Count==551 && layerPanel.Relayered[550].OriginalLayer==9 && pooled.layer==27,
            "Recurring sweep records a newly pooled child and its native layer");
        pooled.layer=7;
        CanvasConversion.SweepForTest(layerPanel,initial:false);
        Check(layerPanel.Relayered.Count==551 && layerPanel.Relayered[550].OriginalLayer==9,
            "Recurring sweep never replaces pooled child's first native layer");
        Console.WriteLine($"Conversion rollback production harness: {_assertions} assertions passed.");
    }
}
