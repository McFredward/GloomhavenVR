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
    private static IEnumerator NativePaint655()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform owner = Go("655 native paint owner").transform;
        Transform observer = Go("655 actual paint observer").transform;
        GameObject bank = Go("655 inert original bank"); bank.SetActive(false);
        LazyTemplateProbe.Close(); LazyTemplateProbe.Open(bank);
        var physical = Rect("655 physical offered print", owner, Vector2.zero, new Vector2(325.1f, 449.5f));
        physical.localScale = Vector3.one * .001f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image("655 opaque physical print", physical, Vector2.zero, physical.rect.size, new Color(.34f,.09f,.05f,1));
        TownServiceDepthOrder.Bind(physical);
        CanvasConversion.Panels(); CanvasConversion.Tick(new Vector3(0,0,-5));
        // Actual converted town surfaces belong to the native panel ladder,
        // whereas their separately printed card belongs to furniture. These
        // are the production orders, not arbitrary overlay-on-top test values.
        var converted = Rect("655 converted original highlighter", owner, Vector2.zero, new Vector2(680,660));
        converted.localScale = Vector3.one * .001f;
        Canvas originalCanvas = converted.gameObject.AddComponent<Canvas>();
        originalCanvas.renderMode = RenderMode.WorldSpace;
        originalCanvas.sortingOrder = (int)typeof(CanvasConversion).GetField("PanelOrderBase", PrivateStatic)!.GetRawConstantValue();
        var holder = (RectTransform)NativeRow632(converted, "", "highlight");
        holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
        holder.Find("Aura/Types/Buy").gameObject.SetActive(true);
        holder.Find("Aura/Types/Sell").gameObject.SetActive(false);
        var area = (RectTransform)holder.Find("Enhancement Ability Highlight Variant");
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
                    nextCapture=Time.unscaledTime+1f/15f; captured.Clear(); motions.Clear(); Capture();
                    Deliver(captured);
                    foreach(var bytes in motions)
                    { Check(receiver.FixtureQueueMotion638(2,bytes),"continuous owner native numeric update reaches observer"); numericUpdates++; }
                }
                receiver.FixtureApply638(); Canvas.ForceUpdateCanvases();
                CanvasConversion.Tick(new Vector3(Mathf.Sin(frame*.01f)*.12f,0,-5));
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
                var observerFill=remoteArea.Find("Image").GetComponent<CanvasGroup>();
                float expected=state==1?.7f:state==0?0f:hover!=0?.2f:0f;
                Check(Mathf.Abs(fill.alpha-expected)<.00001f,"actual native OnHovered produces its selected/preview/selectable fill");
                // Native alpha transitions are interpolated by the actual observer;
                // render the settled state rather than claiming zero network delay.
                if(Mathf.Abs(observerFill.alpha-fill.alpha)>.00001f && Time.unscaledTime<settle) { yield return null; continue; }
                File.AppendAllText(Path.Combine(_output,"native-transitions655.txt"),"settled state="+state+" hover="+hover+" owner="+fill.alpha+" observer="+observerFill.alpha+" active="+remoteArea.gameObject.activeInHierarchy+" now="+Time.unscaledTime+" deadline="+settle+" numeric="+numericUpdates+"\n");
                Check(Mathf.Abs(observerFill.alpha-fill.alpha)<.00001f,"each original enhancement fill settles to exact native controller output");
                foreach(Vector3 eye in new[]{new Vector3(0,0,-.65f),new Vector3(.22f,.15f,-.75f),new Vector3(-.28f,-.13f,-.8f)})
                {
                    CanvasConversion.Tick(eye); for(int tick=0;tick<8;tick++)CanvasConversion.Tick(eye);
                    TownServiceDepthOrder.Refresh(Remote(2,1)!.Root.parent);
                    TownServiceDepthOrder.Refresh(Remote(2,2)!.Root.parent);
                    Color32[] source=RenderNative655(owner,eye), painted=RenderNative655(observer,eye);
                    int changed=source.Zip(painted,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>12?1:0).Sum();
                    string label="state"+state+"-hover"+hover+"-eye"+eye.x;
                    File.WriteAllBytes(Path.Combine(_output,"native-owner-"+label+".png"),source.TextureBytes639());
                    File.WriteAllBytes(Path.Combine(_output,"native-observer-"+label+".png"),painted.TextureBytes639());
                    File.AppendAllText(Path.Combine(_output,"native-paint655.txt"),label+" changed="+changed+" ownerPrint="+physical.GetComponent<Canvas>().sortingOrder+" ownerNative="+originalCanvas.sortingOrder
                        +" observerPrint="+Remote(2,1)!.Root.GetComponentInParent<Canvas>().sortingOrder+" observerNative="+Remote(2,4)!.Root.GetComponentInParent<Canvas>().sortingOrder+"\n");
                    Check(source.Count(pixel=>pixel.r>35&&pixel.r>pixel.g*1.3f&&pixel.r>pixel.b*1.3f)>500,
                        "actual perspective paint includes a meaningful visible original physical card");
                    if(eye.x==0&&state==0&&hover==0)previewPixels=source;
                    if(eye.x==0&&state==1&&hover==0)
                        Check(previewPixels!=null&&source.Zip(previewPixels,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>12?1:0).Sum()>100,
                            "actual selected native fill paints visible original pixels beyond the preview state");
                    Check(changed<150,"native selected/hover output paints exactly like owner with reordered native originals");
                }
                yield return null; break;
            }
        }
        mask.Restore(); TownServiceMirror.Shutdown(); LazyTemplateProbe.Close();
    }
    private static Color32[] RenderNative655(Transform root,Vector3 eye)
    {
        foreach(var go in Objects)if(go!=null)Layer(go.transform,30);Layer(root,9);
        foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<9;_camera.orthographic=false;_camera.fieldOfView=60f;
        _camera.transform.SetPositionAndRotation(root.position+eye,Quaternion.LookRotation(-eye,Vector3.up));
        _camera.clearFlags=CameraClearFlags.SolidColor;_camera.backgroundColor=new Color(.025f,.03f,.04f,1);
        // The read-only game FlexFrame shader contains D3D11 bytecode only.
        // The established GL reader substitutes TMP material for this original
        // Frame, which is opaque and hides its fill. Isolate the real fill's GPU
        // output for this per-graphic proof; never capture this readback mask.
        Graphic[] excluded=root.GetComponentsInChildren<Graphic>(true).Where(g=>g.name=="Frame"&&g.enabled).ToArray();
        foreach(var graphic in excluded)graphic.enabled=false;
        var rt=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);var image=new Texture2D(768,768,TextureFormat.RGBA32,false);
        try{_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();return image.GetPixels32();}
        finally{foreach(var graphic in excluded)if(graphic!=null)graphic.enabled=true;RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }

}
