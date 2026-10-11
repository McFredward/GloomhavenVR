using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureVisibilityAllocations(MrBackingVisibility visibility)
    {
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) _=visibility.AlphaNow;
        return GC.GetAllocatedBytesForCurrentThread()-before;
    }

    private static void TestBackingVisibility()
    {
        // Run the same native-picture contract for the local owner and the inert remote clone.
        // Geometry is sampled exactly once; every subsequent assertion reads current state.
        for (int observer=0; observer<2; observer++)
        {
            var host=new GameObject(observer==0 ? "Owner" : "RemoteClone");
            var canvas=host.AddComponent<Canvas>();
            var root=new GameObject("NativeRoot");root.transform.SetParent(host.transform);
            var scope=new GameObject("PortraitRow");scope.transform.SetParent(root.transform);
            scope.transform.rect=Rect.MinMaxRect(-60,-60,60,60);scope.AddComponent<RectMask2D>();
            var clipped=new GameObject("ClippedAway");clipped.transform.SetParent(scope.transform);
            clipped.transform.rect=Rect.MinMaxRect(90,0,100,20);clipped.AddComponent<Image>().canvas=canvas;
            var faint=new GameObject("Faint");faint.transform.SetParent(scope.transform);
            faint.AddComponent<Image>().canvasRenderer.Alpha=0;
            var portrait=new GameObject("Portrait");portrait.transform.SetParent(scope.transform);
            portrait.transform.rect=Rect.MinMaxRect(-30,-40,30,40);
            var picture=portrait.AddComponent<Image>();picture.canvas=canvas;
            var sibling=new GameObject("OutOfScope");sibling.transform.SetParent(root.transform);
            sibling.AddComponent<Image>().canvas=canvas;
            var transient=new GameObject("ExcludedTransient");transient.transform.SetParent(scope.transform);
            transient.AddComponent<Image>().canvas=canvas;
            var excluded=new HashSet<Transform>{transient.transform};
            var panel=new ConvertedPanel{HostRect=host.transform,Target=root.transform};
            var visibility=new MrBackingVisibility{Root=scope.transform};
            Check(PanelInkBounds.TryMeasure(panel,out var ink,excludedRoots:excluded,
                contentRoot:scope.transform,visibleWitnesses:visibility.Witnesses),"witness collection uses the original ink walk");
            Check(visibility.Witnesses.Count==1 && ReferenceEquals(visibility.Witnesses[0],picture),
                "only admitted scoped unclipped non-transient graphics are visibility witnesses");
            Check(ink.Rect.width==60 && ink.Rect.height==80 && visibility.VisibleNow && visibility.AlphaNow==1,
                "collecting witnesses preserves the native picture and geometry");
            picture.color=new Color{a=.5f};picture.canvasRenderer.Alpha=.4f;
            Check(Math.Abs(visibility.AlphaNow-.2f)<.0001f,"backing alpha follows current native effective alpha");
            picture.canvasRenderer.Alpha=.099f;
            Check(!visibility.VisibleNow,"visibility uses the same native ink alpha cutoff");
            picture.canvasRenderer.Alpha=.1f;
            Check(visibility.VisibleNow,"ink alpha cutoff includes the exact boundary");
            picture.color=new Color{a=1};picture.canvasRenderer.Alpha=0;
            Check(!visibility.VisibleNow && visibility.AlphaNow==0,"native fade hides backing without waiting for geometry sampling");
            picture.canvasRenderer.Alpha=1;picture.enabled=false;
            Check(!visibility.VisibleNow,"native disabled graphic immediately hides backing");
            picture.enabled=true;scope.activeSelf=false;
            Check(!visibility.VisibleNow,"native hidden ancestor immediately hides backing");
            scope.activeSelf=true;canvas.enabled=false;
            Check(!visibility.VisibleNow,"native disabled canvas immediately hides backing");
            canvas.enabled=true;picture.canvasRenderer.cull=true;
            Check(!visibility.VisibleNow,"native renderer cull immediately hides backing");
            picture.canvasRenderer.cull=false;picture.canvas=null;
            Check(!visibility.VisibleNow,"unattached graphic cannot leave an opaque backing");
            picture.canvas=canvas;
            var foreign=new GameObject("OtherOwner");portrait.transform.parent=foreign.transform;
            Check(!visibility.VisibleNow,"reparented native content no longer backs its old owner");
            portrait.transform.parent=scope.transform;
            Check(visibility.VisibleNow && visibility.AlphaNow==1,"native restoration has no independent delay or viewer animation");
            var labelObject=new GameObject("Label");labelObject.transform.SetParent(scope.transform);
            var label=labelObject.AddComponent<TMPro.TMP_Text>();label.canvas=canvas;label.text="Present";
            visibility.Witnesses.Add(label);picture.enabled=false;
            Check(visibility.VisibleNow,"another live contributor retains the backing");
            label.text="";
            Check(!visibility.VisibleNow,"cleared TMP label cannot keep stale backing geometry alive");
            label.text=" \t\n";
            Check(!visibility.VisibleNow,"whitespace-only native label is empty ink");
            var legacyObject=new GameObject("LegacyLabel");legacyObject.transform.SetParent(scope.transform);
            var legacy=legacyObject.AddComponent<Text>();legacy.canvas=canvas;legacy.text="Present";
            visibility.Witnesses.Add(legacy);
            Check(visibility.VisibleNow,"legacy native text can contribute to visibility");
            legacy.text="";
            Check(!visibility.VisibleNow,"cleared legacy label immediately removes its backing");
            picture.enabled=true;picture.color=new Color{a=.35f};label.text="Present";label.color=new Color{a=.6f};
            Check(Math.Abs(visibility.AlphaNow-.6f)<.0001f,"backing follows the most visible admitted native contributor");
            // Warm the entire measurement boundary, not only one getter call. This fixture
            // runs under tiered .NET, unlike the game's Mono runtime; first-use/runtime
            // bookkeeping must not be charged to a steady-state visibility frame.
            // Fixed warmup only: never retry a failed measurement or tolerate allocated bytes.
            for(int warmup=0;warmup<3;warmup++) _=MeasureVisibilityAllocations(visibility);
            long allocated=MeasureVisibilityAllocations(visibility);
            Check(allocated==0,"cached live visibility allocates nothing per frame: "+allocated+" bytes");
            Check(picture.enabled && scope.activeSelf && canvas.enabled && label.text=="Present"
                && picture.color.a==.35f && picture.canvasRenderer.Alpha==1,
                "backing visibility never writes native content or interaction state");
            scope.activeSelf=false;
            Check(!PanelInkBounds.TryMeasure(panel,out _,excludedRoots:excluded,contentRoot:scope.transform,
                visibleWitnesses:visibility.Witnesses) && visibility.Witnesses.Count==0,
                "empty geometry sample clears stale witness references");
            visibility.Witnesses.Add(picture);
            Check(!PanelInkBounds.TryMeasure(panel,out _,contentRoot:foreign.transform,
                visibleWitnesses:visibility.Witnesses) && visibility.Witnesses.Count==0,
                "invalid scope clears old witnesses before returning");
            visibility.Witnesses.Add(picture);visibility.Reset();
            Check(visibility.Witnesses.Count==0 && visibility.Root==null && !visibility.VisibleNow,
                "teardown releases cached native references");
        }
    }
}
