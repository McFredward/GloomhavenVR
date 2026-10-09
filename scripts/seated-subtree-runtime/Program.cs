using System;
using System.Collections;
using System.Collections.Generic;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string scope,string message){} } }
namespace GloomhavenVR.Cards { }
namespace GloomhavenVR.Hands.Interact
{
    internal sealed class PokeInteractor { }
    internal sealed class PokeOnlyTarget:MonoBehaviour { }
    internal interface IDepthPortraitPicker { bool TryPickPortrait(Vector3 origin,Vector3 direction,out GameObject portrait,out Vector3 point); }
    internal static class DepthPortraitPicks { internal static bool TryGet(Canvas canvas,out IDepthPortraitPicker picker){picker=null!;return false;} }
    internal sealed class RayInteractor { internal Vector3? UiHitOverride=null; }
    internal enum HandSide { Left }
    internal sealed class VRHand { internal RayInteractor Ray=new(); }
    internal static class VRHands { internal static VRHand? Get(HandSide side)=>null; }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel
    {
        internal Canvas HostCanvas=null!;
        internal GameObject HostGo=>HostCanvas.gameObject;
        internal int BaseSortingOrder=1000;
        internal readonly List<NestedCanvasRecord> AdoptedCanvases=new();
        internal readonly List<Canvas> PreCapturedCanvases=new();
        internal readonly List<Camera?> PreCapturedCameras=new();
        internal readonly List<LayerRecord> Relayered=new();
        internal readonly List<FlattenRecord> Flattened=new();
        internal readonly List<Transform> FlattenWalk=new();
        internal readonly List<OrderFollower> OrderFollowers=new();
        internal readonly List<Graphic> HiddenBackgrounds=new();
        internal readonly List<Canvas> HiddenCanvases=new();
        internal readonly List<bool> HiddenCanvasWasPreStart=new();
        internal readonly List<Renderer> HiddenRenderers=new();
        internal bool AdoptedOrderRebaseDirty=false,RenderHidden=false;
    }
    internal struct NestedCanvasRecord { internal Canvas Canvas; internal Camera? OriginalWorldCamera;internal bool OriginalOverrideSorting;internal int OriginalSortingOrder;internal GraphicRaycaster? AddedRaycaster;internal bool KeepOverrideSorting,ConcededOverrideSorting; }
    internal struct LayerRecord { internal Transform Transform;internal int OriginalLayer; }
    internal struct FlattenRecord { internal Transform Transform;internal float OriginalLocalZ;internal Quaternion OriginalLocalRotation; }
    internal struct OrderFollower { internal Canvas? Canvas;internal Renderer? Renderer;internal int Offset; }
    internal static partial class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> Active=new();
        // Unrelated native verdict is a boundary: all fixture confirmation windows are shown.
        private static bool RevealRestoreWithheld(ConvertedPanel panel,Canvas canvas)=>false;
        private static void AdoptNestedCanvases(ConvertedPanel panel)
        { foreach(var record in panel.AdoptedCanvases){record.Canvas.worldCamera=panel.HostCanvas.worldCamera;UguiPokeSurfaces.RegisterNested(panel.HostCanvas,record.Canvas);} }
    }
}

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool ok,string message){_checks++;if(!ok)throw new Exception(message);}
    private static Canvas CanvasAt(string name,Camera camera,Vector3 position)
    {
        var obj=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        obj.transform.position=position;
        var rect=(RectTransform)obj.transform;rect.sizeDelta=new Vector2(2f,2f);
        var canvas=obj.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
        UguiPokeSurfaces.Register(canvas);return canvas;
    }
    private static Image ImageAt(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent,false);
        var rect=(RectTransform)obj.transform;rect.sizeDelta=size;rect.anchoredPosition=position;
        return obj.GetComponent<Image>();
    }
    private static void ReleaseOriginals(ConvertedPanel owner)
    {
        // Exact writer operations from CanvasConversion.Release / per-frame sorting and
        // flatten maintenance, scoped to the snapshots this proof hands across the edge.
        foreach(var record in owner.AdoptedCanvases)
        {record.Canvas.overrideSorting=record.OriginalOverrideSorting;record.Canvas.sortingOrder=record.OriginalSortingOrder;record.Canvas.worldCamera=record.OriginalWorldCamera;if(record.AddedRaycaster!=null)UnityEngine.Object.Destroy(record.AddedRaycaster);}
        foreach(var record in owner.Relayered)record.Transform.gameObject.layer=record.OriginalLayer;
        foreach(var record in owner.Flattened)
        {var p=record.Transform.localPosition;record.Transform.localPosition=new Vector3(p.x,p.y,record.OriginalLocalZ);record.Transform.localRotation=record.OriginalLocalRotation;}
        foreach(var canvas in owner.HiddenCanvases)canvas.enabled=true;
        foreach(var follower in owner.OrderFollowers)if(follower.Canvas!=null)follower.Canvas.sortingOrder=999;
        foreach(var graphic in owner.HiddenBackgrounds)graphic.enabled=true;
    }
    public static int Checks=>_checks;
    public static IEnumerator Run()
    {
        var objects=new List<GameObject>();
        try
        {
            var eventObject=new GameObject("EventSystem",typeof(EventSystem));objects.Add(eventObject);
            var cameraObject=new GameObject("VR event camera",typeof(Camera));objects.Add(cameraObject);
            var eye=cameraObject.GetComponent<Camera>();eye.transform.position=new Vector3(0,0,-5);eye.orthographic=true;eye.orthographicSize=2;eye.pixelRect=new Rect(0,0,800,800);
            var nativeObject=new GameObject("Original native UI camera",typeof(Camera));objects.Add(nativeObject);
            var nativeCamera=nativeObject.GetComponent<Camera>();
            var oldEyeObject=new GameObject("Old converted event camera",typeof(Camera));objects.Add(oldEyeObject);
            var oldEye=oldEyeObject.GetComponent<Camera>();oldEye.transform.position=new Vector3(3,0,-5);oldEye.orthographic=true;oldEye.orthographicSize=2;oldEye.enabled=false;
            var captureObject=new GameObject("Destination supersample capture camera",typeof(Camera));objects.Add(captureObject);
            var capture=captureObject.GetComponent<Camera>();capture.transform.position=new Vector3(10,0,-5);capture.cullingMask=1<<20;capture.enabled=false;
            var old=CanvasAt("Enchantress host",oldEye,new Vector3(3,0,0));objects.Add(old.gameObject);
            var destination=CanvasAt("Temple host",eye,Vector3.zero);objects.Add(destination.gameObject);
            old.sortingOrder=148;destination.sortingOrder=196;
            var oldPanel=new ConvertedPanel{HostCanvas=old};var newPanel=new ConvertedPanel{HostCanvas=destination};
            CanvasConversion.Active.Add(oldPanel);CanvasConversion.Active.Add(newPanel);
            ImageAt(old.transform,"Enchantress original paper",Vector2.zero,new Vector2(.1f,.1f));
            ImageAt(destination.transform,"Temple original paper",Vector2.zero,new Vector2(1.9f,1.9f));
            var native=CanvasAt("Shared native enhancement confirmation",oldEye,Vector3.zero);objects.Add(native.gameObject);
            UguiPokeSurfaces.Unregister(native);
            native.transform.SetParent(old.transform,false);((RectTransform)native.transform).sizeDelta=new Vector2(1.8f,1.8f);
            native.overrideSorting=false;native.sortingOrder=111;
            var image=ImageAt(native.transform,"Native confirm image",new Vector2(-.4f,-.4f),new Vector2(.45f,.2f));
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
            int clicks=0;button.onClick.AddListener(()=>clicks++);
            var cancelImage=ImageAt(native.transform,"Native cancel image",new Vector2(.4f,-.4f),new Vector2(.45f,.2f));
            var cancel=cancelImage.gameObject.AddComponent<Button>();cancel.targetGraphic=cancelImage;
            int cancelled=0;cancel.onClick.AddListener(()=>cancelled++);
            var record=new NestedCanvasRecord{Canvas=native,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=true,OriginalSortingOrder=40,AddedRaycaster=null,KeepOverrideSorting=false,ConcededOverrideSorting=false};
            oldPanel.AdoptedCanvases.Add(record);oldPanel.PreCapturedCanvases.Add(native);oldPanel.PreCapturedCameras.Add(nativeCamera);
            foreach(var t in native.GetComponentsInChildren<Transform>(true))
            {oldPanel.Relayered.Add(new LayerRecord{Transform=t,OriginalLayer=5});t.gameObject.layer=21;}
            var mark=ImageAt(native.transform,"Native marking",Vector2.zero,new Vector2(.2f,.2f));
            oldPanel.Relayered.Add(new LayerRecord{Transform=mark.transform,OriginalLayer=5});mark.gameObject.layer=21;
            oldPanel.Flattened.Add(new FlattenRecord{Transform=mark.transform,OriginalLocalZ=.25f,OriginalLocalRotation=Quaternion.Euler(0,0,15)});
            oldPanel.FlattenWalk.Add(mark.transform);
            oldPanel.OrderFollowers.Add(new OrderFollower{Canvas=native,Renderer=null,Offset=2});
            UguiPokeSurfaces.RegisterNested(old,native);
            native.transform.SetParent(destination.transform,false);
            CanvasConversion.TransferSeatedSubtree((RectTransform)native.transform,newPanel);
            foreach(var t in native.GetComponentsInChildren<Transform>(true))t.gameObject.layer=20;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var pointer=new UguiPointer();
            var screen=RectTransformUtility.WorldToScreenPoint(eye,image.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out var hit)&&ReferenceEquals(hit.gameObject,image.gameObject),"The production UguiPointer resolves the original confirm button immediately after seating");
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,pointerCurrentRaycast=hit};
            ExecuteEvents.ExecuteHierarchy(hit.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(clicks==1,"The immediate geometric hit dispatches the original confirmation callback");
            Check(!ReferenceEquals(native.worldCamera,capture)&&ReferenceEquals(native.worldCamera,eye),"Supersample capture camera cannot replace the physical host event camera");
            Check(oldPanel.AdoptedCanvases.Count==0&&newPanel.AdoptedCanvases.Count==1,"Only the destination owns the shared native canvas");
            Check(UguiPokeSurfaces.NestedOf(old)==null,"The old window no longer raycasts the moved native subtree");
            Check(UguiPokeSurfaces.NestedOf(destination)?.Count==1,"The destination immediately raycasts its moved native buttons");
            Check(newPanel.AdoptedCanvases[0].OriginalOverrideSorting&&newPanel.AdoptedCanvases[0].OriginalSortingOrder==40,"The true native sorting state survives handoff");
            Check(newPanel.PreCapturedCameras.Count==1&&ReferenceEquals(newPanel.PreCapturedCameras[0],nativeCamera),"The native camera provenance follows the moved subtree");
            Check(oldPanel.Flattened.Count==0&&oldPanel.FlattenWalk.Count==0,"The old window cannot flatten moved native descendants");
            Check(oldPanel.OrderFollowers.Count==0&&newPanel.OrderFollowers.Count==1,"Only the physical host owns the moved native order follower");
            ReleaseOriginals(oldPanel);
            Check(image.gameObject.layer==20&&button.gameObject.layer==20,"Old release cannot relayer the moved original image and button");
            Check(Mathf.Abs(mark.transform.localPosition.z)<.001f,"Old release cannot restore foreign z into the current confirmation");
            Check(ReferenceEquals(native.worldCamera,eye),"Old release cannot replace the destination ray event camera");
            yield return null;
            Canvas.ForceUpdateCanvases();
            screen=RectTransformUtility.WorldToScreenPoint(eye,image.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,image.gameObject),"The production UguiPointer resolves the original confirm button geometrically");
            data.pointerCurrentRaycast=hit;
            ExecuteEvents.ExecuteHierarchy(hit.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(clicks==2,"Old-owner release leaves original confirmation callback dispatch intact");
            screen=RectTransformUtility.WorldToScreenPoint(eye,cancelImage.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,cancelImage.gameObject),"The production UguiPointer resolves the original cancel button geometrically");
            data.pointerCurrentRaycast=hit;
            ExecuteEvents.ExecuteHierarchy(hit.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(cancelled==1,"The geometric laser hit dispatches one original cancellation callback");
            destination.GetComponent<GraphicRaycaster>().enabled=false;
            Check(!pointer.TryRaycast(destination,screen,out _),"The native disabled host raycaster remains authoritative");
            destination.GetComponent<GraphicRaycaster>().enabled=true;
            var gate=native.gameObject.AddComponent<CanvasGroup>();gate.blocksRaycasts=false;
            Check(!pointer.TryRaycast(destination,screen,out hit)||!ReferenceEquals(hit.gameObject,cancelImage.gameObject),"Native CanvasGroup raycast denial is never bypassed");
            gate.blocksRaycasts=true;
            // The moved ordinary confirmation receives a stable tier just above its
            // paper. Existing close chrome and native dropdown precedence stay higher.
            var xCanvas=CanvasAt("Original modal X",eye,Vector3.zero);objects.Add(xCanvas.gameObject);
            xCanvas.transform.SetParent(destination.transform,false);UguiPokeSurfaces.Unregister(xCanvas);
            xCanvas.overrideSorting=true;xCanvas.sortingOrder=1100;
            var xImage=ImageAt(xCanvas.transform,"Original X image",new Vector2(-.4f,-.4f),new Vector2(.45f,.2f));
            UguiPokeSurfaces.RegisterNested(destination,xCanvas);
            yield return null;
            Canvas.ForceUpdateCanvases();
            screen=RectTransformUtility.WorldToScreenPoint(eye,xImage.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,xImage.gameObject),"Original modal X1100 still outranks the seated confirmation input tier");
            var dropdown=CanvasAt("Dropdown List",eye,Vector3.zero);objects.Add(dropdown.gameObject);
            dropdown.transform.SetParent(native.transform,false);UguiPokeSurfaces.Unregister(dropdown);
            dropdown.overrideSorting=true;dropdown.sortingOrder=4000;
            var dropImage=ImageAt(dropdown.transform,"Original dropdown image",new Vector2(-.4f,-.4f),new Vector2(.45f,.2f));
            newPanel.AdoptedCanvases.Add(new NestedCanvasRecord{Canvas=dropdown,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=true,OriginalSortingOrder=4000,KeepOverrideSorting=true});
            UguiPokeSurfaces.RegisterNested(destination,dropdown);
            var blocker=CanvasAt("Blocker",eye,Vector3.zero);objects.Add(blocker.gameObject);
            blocker.transform.SetParent(native.transform,false);UguiPokeSurfaces.Unregister(blocker);
            blocker.overrideSorting=true;blocker.sortingOrder=3999;
            ImageAt(blocker.transform,"Original dropdown blocker image",new Vector2(-.4f,-.4f),new Vector2(.45f,.2f));
            newPanel.AdoptedCanvases.Add(new NestedCanvasRecord{Canvas=blocker,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=true,OriginalSortingOrder=3999,KeepOverrideSorting=true});
            UguiPokeSurfaces.RegisterNested(destination,blocker);
            var concession=CanvasAt("Native sorting concession",eye,Vector3.zero);objects.Add(concession.gameObject);
            concession.transform.SetParent(native.transform,false);UguiPokeSurfaces.Unregister(concession);
            concession.overrideSorting=true;concession.sortingOrder=197;
            newPanel.AdoptedCanvases.Add(new NestedCanvasRecord{Canvas=concession,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=true,OriginalSortingOrder=1190,ConcededOverrideSorting=true});
            UguiPokeSurfaces.RegisterNested(destination,concession);
            CanvasConversion.TransferSeatedSubtree((RectTransform)native.transform,newPanel);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,dropImage.gameObject),"The native dropdown4000 still outranks blocker3999 and modal X1100");
            dropdown.overrideSorting=false;blocker.overrideSorting=false;
            Check(!CanvasConversion.BaseSortingOrderOf(dropdown.gameObject,out _)&&!CanvasConversion.BaseSortingOrderOf(blocker.gameObject,out _)&&!UguiPokeSurfaces.TrySeatedHostOf(dropdown,out _,out _)&&!UguiPokeSurfaces.TrySeatedHostOf(blocker,out _,out _),"Dropdown overlays retain the existing authored input comparison");
            Check(!CanvasConversion.BaseSortingOrderOf(concession.gameObject,out _)&&concession.sortingOrder==197&&concession.overrideSorting,"Native sorting concessions retain their original ordering contract");
            UguiPokeSurfaces.UnregisterNested(destination,xCanvas);xCanvas.gameObject.SetActive(false);
            UguiPokeSurfaces.UnregisterNested(destination,dropdown);dropdown.gameObject.SetActive(false);
            UguiPokeSurfaces.UnregisterNested(destination,blocker);blocker.gameObject.SetActive(false);
            UguiPokeSurfaces.UnregisterNested(destination,concession);concession.gameObject.SetActive(false);
            newPanel.AdoptedCanvases.RemoveAt(newPanel.AdoptedCanvases.Count-1);
            newPanel.AdoptedCanvases.RemoveAt(newPanel.AdoptedCanvases.Count-1);
            newPanel.AdoptedCanvases.RemoveAt(newPanel.AdoptedCanvases.Count-1);
            native.overrideSorting=true;
            Check(!CanvasConversion.BaseSortingOrderOf(native.gameObject,out _),"A later native sorting override retires the ordinary seated input comparison");
            native.overrideSorting=false;
            var concededRecord=newPanel.AdoptedCanvases[0];concededRecord.ConcededOverrideSorting=true;newPanel.AdoptedCanvases[0]=concededRecord;
            Check(!CanvasConversion.BaseSortingOrderOf(native.gameObject,out _),"A later recorded native concession retires the seated input comparison");
            concededRecord.ConcededOverrideSorting=false;newPanel.AdoptedCanvases[0]=concededRecord;
            Check(CanvasConversion.BaseSortingOrderOf(native.gameObject,out var seatedOrder)&&seatedOrder==1001,"The ordinary shared confirmation resumes its host stable input tier");
            cancel.interactable=false;
            screen=RectTransformUtility.WorldToScreenPoint(eye,cancelImage.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,cancelImage.gameObject),"A disabled native Button still paints and raycasts normally");
            data.pointerCurrentRaycast=hit;ExecuteEvents.ExecuteHierarchy(hit.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(cancelled==1,"The native Button interactability veto remains authoritative");
            cancel.interactable=true;
            newPanel.HiddenCanvases.Add(native);newPanel.HiddenCanvasWasPreStart.Add(false);native.enabled=false;
            native.transform.SetParent(old.transform,false);
            CanvasConversion.TransferSeatedSubtree((RectTransform)native.transform,oldPanel);
            Check(native.enabled&&newPanel.HiddenCanvases.Count==0,"Only the physical host restores a moved native reveal snapshot");
            CanvasConversion.TransferSeatedSubtree((RectTransform)native.transform,null);
            Check(ReferenceEquals(native.worldCamera,nativeCamera),"Native home restores the original camera after final release");
            Check(native.overrideSorting&&native.sortingOrder==40,"Native home restores original sort mode and order");
            Check(image.gameObject.layer==5&&cancelImage.gameObject.layer==5,"Native home restores original layers for both decision buttons");
            Check(Mathf.Abs(mark.transform.localPosition.z-.25f)<.001f&&Quaternion.Angle(mark.transform.localRotation,Quaternion.Euler(0,0,15))<.001f,"Native home restores the original descendant geometry");
            Check(oldPanel.AdoptedCanvases.Count==0&&oldPanel.Relayered.Count==0&&UguiPokeSurfaces.NestedOf(old)==null&&!CanvasConversion.BaseSortingOrderOf(native.gameObject,out _),"Final native release leaves no stale converter or pointer ownership");
            // Item confirmation is a different pooled consumer: its seat root is an
            // ordinary RectTransform and only a descendant canvas belongs to the ledgers.
            var itemRoot=new GameObject("UIItemConfirmationBox native root",typeof(RectTransform));objects.Add(itemRoot);
            itemRoot.transform.SetParent(old.transform,false);((RectTransform)itemRoot.transform).sizeDelta=new Vector2(1.8f,1.8f);
            var itemCanvas=CanvasAt("Item native buttons canvas",oldEye,Vector3.zero);objects.Add(itemCanvas.gameObject);
            itemCanvas.transform.SetParent(itemRoot.transform,false);UguiPokeSurfaces.Unregister(itemCanvas);
            var itemImage=ImageAt(itemCanvas.transform,"Native item purchase image",new Vector2(0,.35f),new Vector2(.4f,.2f));
            var itemButton=itemImage.gameObject.AddComponent<Button>();itemButton.targetGraphic=itemImage;
            int purchases=0;itemButton.onClick.AddListener(()=>purchases++);
            // This raycaster was added by conversion; ownership must move too, or the
            // old release destroys the destination's actual input component.
            var itemRaycaster=itemCanvas.GetComponent<GraphicRaycaster>();
            var itemOverlay=CanvasAt("Item authored upper nested canvas",oldEye,Vector3.zero);objects.Add(itemOverlay.gameObject);
            itemOverlay.transform.SetParent(itemRoot.transform,false);UguiPokeSurfaces.Unregister(itemOverlay);
            itemOverlay.overrideSorting=false;itemOverlay.sortingOrder=18;
            var itemOverlayImage=ImageAt(itemOverlay.transform,"Original authored upper item image",new Vector2(0,.35f),new Vector2(.4f,.2f));
            oldPanel.AdoptedCanvases.Add(new NestedCanvasRecord{Canvas=itemOverlay,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=false,OriginalSortingOrder=18});
            UguiPokeSurfaces.RegisterNested(old,itemOverlay);
            itemOverlay.gameObject.SetActive(false);
            oldPanel.AdoptedCanvases.Add(new NestedCanvasRecord{Canvas=itemCanvas,OriginalWorldCamera=nativeCamera,OriginalOverrideSorting=false,OriginalSortingOrder=17,AddedRaycaster=itemRaycaster});
            UguiPokeSurfaces.RegisterNested(old,itemCanvas);
            foreach(var t in itemRoot.GetComponentsInChildren<Transform>(true))
            {oldPanel.Relayered.Add(new LayerRecord{Transform=t,OriginalLayer=5});t.gameObject.layer=21;}
            var itemBacking=ImageAt(itemCanvas.transform,"Native item hidden backing",new Vector2(.7f,.7f),new Vector2(.1f,.1f));
            itemBacking.enabled=false;oldPanel.HiddenBackgrounds.Add(itemBacking);
            itemRoot.transform.SetParent(destination.transform,false);
            CanvasConversion.TransferSeatedSubtree((RectTransform)itemRoot.transform,newPanel);
            foreach(var t in itemRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=20;
            ReleaseOriginals(oldPanel);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Check(itemRaycaster!=null&&itemRaycaster.enabled,"Old-owner release cannot destroy the moved conversion-added item raycaster");
            Check(!itemBacking.enabled,"Old-owner release cannot reveal the moved hidden original item backing");
            screen=RectTransformUtility.WorldToScreenPoint(eye,itemImage.rectTransform.position);
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,itemImage.gameObject),"The ordinary item confirmation seat resolves its original button geometrically");
            data.pointerCurrentRaycast=hit;ExecuteEvents.ExecuteHierarchy(hit.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(purchases==1,"Item seating retains the original native button callback");
            Check(CanvasConversion.BaseSortingOrderOf(itemCanvas.gameObject,out var lowerTier)&&CanvasConversion.BaseSortingOrderOf(itemOverlay.gameObject,out var upperTier)&&lowerTier==1001&&upperTier==1002,"Distinct authored nested item orders survive the moved input tier");
            itemOverlay.gameObject.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Check(pointer.TryRaycast(destination,screen,out hit)&&ReferenceEquals(hit.gameObject,itemOverlayImage.gameObject),"The original authored upper item image wins the overlapping native raycast");
            itemRoot.transform.SetParent(old.transform,false);
            CanvasConversion.TransferSeatedSubtree((RectTransform)itemRoot.transform,oldPanel);
            CanvasConversion.TransferSeatedSubtree((RectTransform)itemRoot.transform,null);
            yield return null;
            Check(itemCanvas.GetComponent<GraphicRaycaster>()==null,"Native item home removes only its conversion-added raycaster");
            Check(itemImage.gameObject.layer==5&&itemBacking.enabled,"Native item home restores original layer and backing state");
            yield break;
        }
        finally
        {CanvasConversion.Active.Clear();UguiPokeSurfaces.Clear();for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)UnityEngine.Object.DestroyImmediate(objects[i]);}
    }
}
