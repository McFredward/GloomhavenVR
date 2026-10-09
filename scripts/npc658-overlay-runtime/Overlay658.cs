using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static IEnumerator NativeOverlay658()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform owner = Go("655 native paint owner").transform;
        Transform observer = Go("655 actual paint observer").transform;
        GameObject bank = Go("655 inert original bank"); bank.SetActive(false);
        LazyTemplateProbe.Close(); LazyTemplateProbe.Open(bank);
        var physical = Rect("655 physical offered print", owner, Vector2.zero, new Vector2(325.1f, 449.5f));
        physical.localScale = Vector3.one * .001f; physical.localPosition=new Vector3(.4f,0,0);
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image("655 opaque physical print", physical, Vector2.zero, physical.rect.size, new Color(.34f,.09f,.05f,1));
        TownServiceDepthOrder.Bind(physical);
        CanvasConversion.Panels(.69f); CanvasConversion.Tick(new Vector3(.6f,0,-.65f));
        // Actual converted town surfaces belong to the native panel ladder,
        // whereas their separately printed card belongs to furniture. These
        // are the production orders, not arbitrary overlay-on-top test values.
        var converted = Rect("655 converted original highlighter", owner, Vector2.zero, new Vector2(680,660));
        converted.localScale = Vector3.one * .001f;
        Canvas originalCanvas = converted.gameObject.AddComponent<Canvas>();
        originalCanvas.renderMode = RenderMode.WorldSpace;
        TownServiceDepthOrder.Bind(converted);
        var holder = (RectTransform)NativeRow632(converted, "", "highlight");
        holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
        holder.Find("Aura/Types/Buy").gameObject.SetActive(true);
        holder.Find("Aura/Types/Sell").gameObject.SetActive(false);
        Check(holder.GetComponentsInChildren<Canvas>(true).Length==0,
            "actual native highlighter aura and ability rows inherit one original canvas paint tier");
        var area = (RectTransform)holder.Find("Enhancement Ability Highlight Variant");
        area.Find("Frame").GetComponent<Image>().material=new Material(Shader.Find("NativePass658"));
        var native = Go("655 native ability card", holder.Find("CardHolder")).AddComponent<AbilityCardUI>();
        var print = (RectTransform)Go("FullAbilityCard", native.transform).transform;
        print.sizeDelta = physical.rect.size; native.fullAbilityCard = print;
        var highlighter = holder.gameObject.AddComponent<UIEnhancementCardHighlighter>(); highlighter.Card = native;
        // Native UILevelUpCardHolder.PlaceNewCard(..., immediately:true), called
        // by ShowCard without its flat opening animation, populates this gate.
        // This is card-population input, never a forced overlay hover alpha.
        holder.Find("CardHolder").GetComponent<CanvasGroup>().alpha = 1f;
        var target = (RectTransform)Go("655 actual target", print).transform;
        target.sizeDelta = new Vector2(270,105); target.anchoredPosition = new Vector2(0,65);
        area.SetParent(target); area.pivot = target.pivot; area.sizeDelta = target.rect.size;
        area.position = target.position; area.gameObject.SetActive(true);
        var controller = area.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        CanvasGroup fill = area.Find("Image").GetComponent<CanvasGroup>();
        controller.NativeInput655(fill, 2); controller.OnHovered(true);
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = native.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>();
        mask.Mask(); mask.SendMessage("LateUpdate");
        var originals = new List<(ushort Id, Transform Source, Func<Transform,bool>? Exclude)>();
        void Register(ushort id, Transform source, string address, Func<Transform,bool>? exclude = null)
        {
            Transform copy = Object.Instantiate(source.gameObject, owner, false).transform;
            if (exclude != null)
                foreach (Transform node in source.GetComponentsInChildren<Transform>(true))
                    if (node != source && exclude(node))
                    { Transform removed=copy.Find(Relative639(source,node)); if(removed!=null)Object.DestroyImmediate(removed.gameObject); }
            Transform frozen = LazyTemplateProbe.FreezeOriginal646(copy,"paint655."+id);
            TownServiceMirror.RegisterTemplate(3,id,frozen,address:address);
            Object.DestroyImmediate(copy.gameObject);
            originals.Add((id,source,exclude));
        }
        Transform aura = holder.Find("Aura");
        Register(1, physical, "face.655|");
        Register(2, holder, "enchant.holder|", node=>node==aura || node==area || node==native.transform);
        Register(3, aura, "enchant.holder|Aura#0");
        Register(4, area, "enchant.highlight|paint655");
        TownServiceMirror.BeginSession(3,655,owner,owner);
        foreach(var original in originals)
        { TownServiceMirror.RegisterModule(original.Id,original.Id,original.Source,original.Exclude,
            original.Id==1?"face.655|":original.Id==2?"enchant.holder|":original.Id==3?"enchant.holder|Aura#0":"enchant.highlight|paint655");
          TownServiceMirror.SetPriority(original.Id,true); }
        TownServiceMirror.RegisterOfferedFrame(holder,physical);
        TownServiceMirror.SetLocalTransactionActive(3,true);
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        float nativeMotionBegan=Time.unscaledTime;
        var receiver = new NetAvatarDriver(); var captured = new List<TownServiceFrame>(); var motions = new List<byte[]>();
        void Capture()
        {
            NetPlayerActors.Peer=2; SetNativeSenderActive629(true);
            try { typeof(TownServiceMirror).GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[] {
                (Action<byte[],int,object?>)((bytes,length,identity)=> { if(identity is TownServiceFrame f)captured.Add(f);else motions.Add(bytes); }), true }); }
            finally { SetNativeSenderActive629(false); NetPlayerActors.Peer=10; }
        }
        void Deliver(IEnumerable<TownServiceFrame> frames)
        { foreach(var frame in frames)Check(receiver.FixtureQueue638(2,TownServiceCodec.Write(frame)),"actual native property enters avatar receiver"); }
        Canvas.ForceUpdateCanvases(); Render639(owner); Capture();
        File.WriteAllText(Path.Combine(_output,"native-initial655.txt"),string.Join(",",captured.Select(x=>x.Module+":"+x.Visible+":"+x.ParentAlpha))+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
        TownServiceFrame initialManifest = captured.Single(x=>x.Module==TownServiceFrame.ManifestModule);
        TownServiceFrame initialArea = captured.Single(x=>x.Module==4);
        // Real packets can arrive in either order. Native partition creation
        // order must not decide whether an opaque card erases an original fill.
        Deliver(captured.Where(x=>x.Module!=1)); receiver.FixtureApply638(); yield return null;
        Deliver(captured.Where(x=>x.Module==1));
        foreach(var bytes in motions)Check(receiver.FixtureQueueMotion638(2,bytes),"actual offered affinity reaches observer");
        receiver.FixtureApply638(); yield return null; receiver.FixtureApply638();
        Color32[]? previewPixels=null;
        for(int state=0;state<4;state++)
        for(int hover=0;hover<2;hover++)
        {
            captured.Clear(); motions.Clear(); controller.NativeInput655(fill,state); controller.OnHovered(hover!=0);
            mask.SendMessage("LateUpdate"); Canvas.ForceUpdateCanvases(); Capture();
            File.AppendAllText(Path.Combine(_output,"native-transitions655.txt"),"state="+state+" hover="+hover+" frames="+string.Join(",",captured.Select(x=>x.Module+":"+x.Sequence+":"+x.SampleTime+":"+x.BaseSequence))+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages)+"\n");
            Deliver(captured); foreach(var bytes in motions)Check(receiver.FixtureQueueMotion638(2,bytes),"actual current authored motion reaches observer");
            receiver.FixtureApply638();
            // Late old manifest/header has no authority over these newer native
            // controller outputs, even while the original hovering card moves.
            if(state==1) { Deliver(new[]{initialManifest,initialArea}); receiver.FixtureApply638(); }
            float settle=Time.unscaledTime+1f, nextCapture=Time.unscaledTime;
            int numericUpdates=0;
            for(int frame=0;;frame++)
            {
                if(Time.unscaledTime>=nextCapture)
                {
                    float nativeClock=Time.unscaledTime-nativeMotionBegan;
                    physical.SetPositionAndRotation(new Vector3(.4f,.012f*Mathf.Sin(nativeClock*2f),0),Quaternion.identity);
                    mask.SendMessage("LateUpdate");
                    nextCapture=Time.unscaledTime+1f/15f; captured.Clear(); motions.Clear(); Capture();
                    Deliver(captured);
                    foreach(var bytes in motions)
                    { Check(receiver.FixtureQueueMotion638(2,bytes),"continuous owner native numeric update reaches observer"); numericUpdates++; }
                }
                receiver.FixtureApply638(); Canvas.ForceUpdateCanvases();
                CanvasConversion.Tick(new Vector3(.6f,0,-.65f));
                TownServiceDepthOrder.Refresh(Remote(2,1)!.Root.parent);
                TownServiceDepthOrder.Refresh(Remote(2,2)!.Root.parent);
                TownServiceDepthOrder.Refresh(Remote(2,4)!.Root.parent);
                Transform remoteArea=Remote(2,4)!.Root;
                foreach(var graphic in area.GetComponentsInChildren<Graphic>(true))
                {
                    string path=Relative639(area,graphic.transform);
                    var same=(path.Length==0?remoteArea:remoteArea.Find(path)).GetComponent<Graphic>();
                    Check(same.enabled==graphic.enabled && same.color==graphic.color,
                        "each original enhancement graphic retains native enabled and color output");
                }
                CheckOfferedCorners629(physical,area,Remote(2,1)!.Root,(RectTransform)remoteArea,
                    "native offered area remains on its exact physical print during continuous independent numeric hover");
                var observerFill=remoteArea.Find("Image").GetComponent<CanvasGroup>();
                float expected=state==1?.7f:state==0?0f:hover!=0?.2f:0f;
                Check(Mathf.Abs(fill.alpha-expected)<.00001f,"actual native OnHovered produces its selected/preview/selectable fill");
                // Native alpha transitions are interpolated by the actual observer;
                // render the settled state rather than claiming zero network delay.
                if(Mathf.Abs(observerFill.alpha-fill.alpha)>.00001f && Time.unscaledTime<settle) { yield return null; continue; }
                File.AppendAllText(Path.Combine(_output,"native-transitions655.txt"),"settled state="+state+" hover="+hover+" owner="+fill.alpha+" observer="+observerFill.alpha+" active="+remoteArea.gameObject.activeInHierarchy+" now="+Time.unscaledTime+" deadline="+settle+" numeric="+numericUpdates+"\n");
                Check(Mathf.Abs(observerFill.alpha-fill.alpha)<.00001f,"each original enhancement fill settles to exact native controller output");
                float convergeUntil=Time.unscaledTime+.8f;while(Time.unscaledTime<convergeUntil){yield return null;receiver.FixtureApply638();}
                captured.Clear();motions.Clear();Capture();Deliver(captured);foreach(var bytes in motions)receiver.FixtureQueueMotion638(2,bytes);
                convergeUntil=Time.unscaledTime+.8f;while(Time.unscaledTime<convergeUntil){yield return null;receiver.FixtureApply638();}
                File.AppendAllText(Path.Combine(_output,"physical-pose658.txt"),"owner="+physical.position.ToString("F7")+" observer="+Remote(2,1)!.Root.position.ToString("F7")+" ownerRotation="+physical.rotation.ToString("F7")+" observerRotation="+Remote(2,1)!.Root.rotation.ToString("F7")+"\n");
                foreach(Vector3 eye in new[]{new Vector3(.6f,0,-.65f),new Vector3(.3f,.15f,-.75f),new Vector3(-.28f,-.13f,-.8f)})
                {
                    CanvasConversion.Tick(eye); for(int tick=0;tick<8;tick++)CanvasConversion.Tick(eye);
                    TownServiceDepthOrder.Refresh(Remote(2,1)!.Root.parent);
                    TownServiceDepthOrder.Refresh(Remote(2,2)!.Root.parent);
                    Color32[] source=RenderNative655(owner,eye), painted=RenderNative655(observer,eye);
                    Check(Remote(2,4)!.Root.GetComponentInParent<Canvas>().sortingOrder > Remote(2,1)!.Root.GetComponentInParent<Canvas>().sortingOrder,
                        "original native frame remains above its physical print across actual panel ranks");
                    int changed=source.Zip(painted,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>12?1:0).Sum();
                    string label="state"+state+"-hover"+hover+"-eye"+eye.x;
                    File.WriteAllBytes(Path.Combine(_output,"native-owner-"+label+".png"),source.TextureBytes639());
                    File.WriteAllBytes(Path.Combine(_output,"native-observer-"+label+".png"),painted.TextureBytes639());
                    File.AppendAllText(Path.Combine(_output,"native-paint655.txt"),label+" changed="+changed+" ownerPrint="+physical.GetComponent<Canvas>().sortingOrder+" ownerNative="+originalCanvas.sortingOrder
                        +" observerPrint="+Remote(2,1)!.Root.GetComponentInParent<Canvas>().sortingOrder+" observerNative="+Remote(2,4)!.Root.GetComponentInParent<Canvas>().sortingOrder+"\n");
                    Check(source.Count(pixel=>pixel.r>35&&pixel.r>pixel.g*1.3f&&pixel.r>pixel.b*1.3f)>500,
                        "actual perspective paint includes a meaningful visible physical offered-print boundary");
                    Check(painted.Count(pixel=>pixel.r>150&&pixel.g>100&&pixel.b<120)>100,
                        "enabled native frame draw-state adapter paints a continuous border above the full physical front");
                    if(eye.x==.6f&&state==0&&hover==0)previewPixels=source;
                    if(eye.x==.6f&&state==1&&hover==0)
                        Check(previewPixels!=null&&source.Zip(previewPixels,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>12?1:0).Sum()>100,
                            "actual selected native fill paints visible original pixels beyond the preview state");
                    Check(changed<150,"native selected/hover output paints exactly like owner with reordered native originals");
                }
                yield return null; break;
            }
        }
        if (typeof(TownServiceDepthOrder).GetMethod("BindOffered",PrivateStatic)!=null) {
        // Exercise teardown and replacement on real active roots rather than
        // assuming their retained original bank is always destroyed immediately.
        // The actual native area may share its canvas with other original
        // partitions. Use an independent real canvas for individual lifetime
        // edges; the real complete observer set is checked by ResetNetwork below.
        Transform inkHost=Go("independent submitted original lifetime probe",observer).transform;
        var inkCanvas=inkHost.gameObject.AddComponent<Canvas>();inkCanvas.renderMode=RenderMode.WorldSpace;
        inkCanvas.sortingOrder=123;TownServiceDepthOrder.Bind(inkHost);
        Transform oldPrint=Remote(2,1)!.Root;
        Canvas paperCanvas=oldPrint.GetComponentInParent<Canvas>();
        BindOrder658(inkHost,oldPrint);
        int independent=inkCanvas.sortingOrder;
        UnbindOrder658(inkHost);
        int restored=inkCanvas.sortingOrder;
        paperCanvas.sortingOrder=331;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==restored,"withdrawn original no longer follows its previous physical print");
        BindOrder658(inkHost,oldPrint);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==332,"same original can reopen on its exact print without stale order");
        var second=(RectTransform)Go("replacement physical print",observer).transform;
        second.sizeDelta=oldPrint.GetComponent<RectTransform>().rect.size;
        var secondCanvas=second.gameObject.AddComponent<Canvas>();secondCanvas.renderMode=RenderMode.WorldSpace;secondCanvas.sortingOrder=441;
        BindOrder658(inkHost,second);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==442,"physical print replacement replaces the former relation immediately");
        paperCanvas.sortingOrder=551;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==442,"replaced physical print cannot regain overlay order ownership");
        inkHost.gameObject.SetActive(false);Canvas.ForceUpdateCanvases();int closed=inkCanvas.sortingOrder;
        secondCanvas.sortingOrder=661;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==closed,"native inactive partition retires its exact submitted relation");
        inkHost.gameObject.SetActive(true);BindOrder658(inkHost,second);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==662,"reopened native partition retains the current replacement print");
        Object.DestroyImmediate(second.gameObject);Canvas.ForceUpdateCanvases();int destroyed=inkCanvas.sortingOrder;
        paperCanvas.sortingOrder=771;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==destroyed,"destroyed print cannot leave a stale overlay ordering callback");
        // Explicit native graphic visibility/alpha is not authored by this helper.
        var frameGraphic=Remote(2,4)!.Root.Find("Frame").GetComponent<Graphic>();frameGraphic.enabled=false;
        var nativeGroup=Remote(2,4)!.Root.Find("Image").GetComponent<CanvasGroup>();nativeGroup.alpha=0f;
        BindOrder658(inkHost,oldPrint);Canvas.ForceUpdateCanvases();
        Check(!frameGraphic.enabled&&nativeGroup.alpha==0f,"valid native hide and hover opacity survive offered paint-order reassertion");
        UnbindOrder658(inkHost);
        // Two partitions may inherit the very same converted native canvas.
        var sharedRoot=Go("same native canvas partition",inkHost).transform;
        BindOrder658(inkHost,oldPrint);BindOrder658(sharedRoot,oldPrint);Canvas.ForceUpdateCanvases();
        UnbindOrder658(sharedRoot);paperCanvas.sortingOrder=881;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==882,"withdrawing one shared-canvas partition retains the surviving exact print relation");
        int sharedOriginal=123;
        UnbindOrder658(inkHost);
        Check(inkCanvas.sortingOrder==sharedOriginal,"last shared-canvas relation restores the first independent original order");
        BindOrder658(inkHost,oldPrint);BindOrder658(sharedRoot,oldPrint);
        UnbindOrder658(inkHost);Check(inkCanvas.sortingOrder==882,"removing the parent first retains a shared-canvas descendant relation");
        UnbindOrder658(sharedRoot);Check(inkCanvas.sortingOrder==sharedOriginal,"reversed shared-canvas removal restores the same independent original order");
        var conflicting=Go("delayed different physical print",observer).AddComponent<Canvas>();conflicting.renderMode=RenderMode.WorldSpace;conflicting.sortingOrder=1141;
        BindOrder658(inkHost,oldPrint);BindOrder658(sharedRoot,conflicting.transform);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==882,"native canvas parent owns one plate despite a delayed conflicting descendant print");
        BindOrder658(sharedRoot,conflicting.transform);BindOrder658(inkHost,oldPrint);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==882,"packet and dictionary iteration do not select the shared canvas print owner");
        BindOrder658(inkHost,conflicting.transform);Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==1142,"exact parent print replacement changes its whole native canvas tier immediately");
        UnbindOrder658(sharedRoot);UnbindOrder658(inkHost);
        Object.DestroyImmediate(conflicting.gameObject);
        int beforeLayer=inkCanvas.sortingOrder;
        int alternate=SortingLayer.NameToID("Native658Alternate");
        Check(alternate!=0,"distinct original native sorting layer exists in the real Unity fixture");
        inkCanvas.sortingLayerID=alternate;BindOrder658(inkHost,oldPrint);paperCanvas.sortingOrder=991;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==beforeLayer&&inkCanvas.sortingLayerID==alternate,
            "different authored sorting layers retain original priority without an offered tier override");
        inkCanvas.sortingLayerID=paperCanvas.sortingLayerID;BindOrder658(inkHost,oldPrint);
        mask.Restore();TownServiceMirror.ResetNetwork();
        var orders=(System.Collections.IDictionary)typeof(TownServiceDepthOrder).GetField("Offered",PrivateStatic)!.GetValue(null)!;
        Check(orders.Count==0,"actual native network reset releases all offered roots without another canvas render");
        int resetOrder=inkCanvas.sortingOrder;paperCanvas.sortingOrder=1101;Canvas.ForceUpdateCanvases();
        Check(inkCanvas.sortingOrder==resetOrder,"retained original bank cannot revive submitted order after native reset");
        }
        mask.Restore(); TownServiceMirror.Shutdown(); LazyTemplateProbe.Close();
    }
    private static void BindOrder658(Transform root,Transform print) => typeof(TownServiceDepthOrder).GetMethod("BindOffered",PrivateStatic)!.Invoke(null,new object[]{root,print});
    private static void UnbindOrder658(Transform root) => typeof(TownServiceDepthOrder).GetMethod("UnbindOffered",PrivateStatic)!.Invoke(null,new object[]{root});
    private static Color32[] RenderNative655(Transform root,Vector3 eye)
    {
        foreach(var go in Objects)if(go!=null)Layer(go.transform,30);Layer(root,9);
        foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<9;_camera.orthographic=false;_camera.fieldOfView=60f;
        _camera.transform.SetPositionAndRotation(root.position+eye,Quaternion.LookRotation(new Vector3(.4f,0,0)-eye,Vector3.up));
        _camera.clearFlags=CameraClearFlags.SolidColor;_camera.backgroundColor=new Color(.025f,.03f,.04f,1);
        var rt=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);var image=new Texture2D(768,768,TextureFormat.RGBA32,false);
        try{_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();return image.GetPixels32();}
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }

}
