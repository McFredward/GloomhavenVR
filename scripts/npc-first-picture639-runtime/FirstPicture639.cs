using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using Object=UnityEngine.Object;

public static partial class MirrorProgram
{
    private sealed class PictureOriginal639
    {
        internal ushort Id;
        internal string Address="";
        internal Transform Source=null!;
        internal Func<Transform,bool>? Exclude;
        internal TownServiceNode[] Expected=Array.Empty<TownServiceNode>();
        internal bool Required=true;
    }
    private static readonly bool PrewarmObserver639 = true;
    private static Sprite[] _ownerAtlasWrappers639=Array.Empty<Sprite>();
    private static IEnumerator FirstPicture639(bool mage)
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer=10;
        GloomhavenVR.Core.VRLog.Messages.Clear();
        Transform owner=Go("639 current exact owner").transform;
        Transform observer=Go("639 actual observer").transform;
        var canvas=owner.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=_camera;
        byte service=mage?(byte)3:(byte)1;
        var originals=new List<PictureOriginal639>();
        void Add(Transform source,string address,Func<Transform,bool>? exclude=null,bool required=true)
        {
            ushort id=(ushort)(originals.Count+1);
            originals.Add(new PictureOriginal639{Id=id,Address=address,Source=source,Exclude=exclude,Required=required});
            Transform nativeObserver=Object.Instantiate(source.gameObject,observer,false).transform;
            foreach(var text in nativeObserver.GetComponentsInChildren<TMP_Text>(true))
            { text.text="different observer default";text.fontSize+=3; }
            // Template defaults must differ: sparse local-template assumptions are
            // not allowed to pass because the test copied the owner's final values.
            foreach(var image in nativeObserver.GetComponentsInChildren<Image>(true))image.color=new Color(.18f,.23f,.42f,.72f);
            if(exclude!=null)
                foreach(var node in source.GetComponentsInChildren<Transform>(true))
                {
                    if(node==source||!exclude(node))continue;
                    var removed=nativeObserver.Find(Relative639(source,node));
                    if(removed!=null)Object.DestroyImmediate(removed.gameObject);
                }
            TownServiceMirror.RegisterTemplate(service,id,nativeObserver,address:address);
            TownServiceMirror.PrepareNativeTemplateBasis(service,address);
            Object.DestroyImmediate(nativeObserver.gameObject);
        }
        var inventory=mage?NativeRow632(owner,"Original enhancement inventory","inventory"):null;
        var rows=new List<Transform>();var inventoryParts=new List<Transform>();
        if(mage)
        {
            inventory!.localScale=Vector3.one*.001f;inventory.localPosition=new Vector3(.36f,0,0);
            Transform content=inventory.Find("Content/Scroll View/Viewport/Content");
            foreach(var group in inventory.Find("Tooltip").GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
            inventory.Find("Tooltip/TextMeshPro Text").GetComponent<TMP_Text>().text="Poison enhancement —75 gold";
            for(int i=0;i<14;i++)
            {
                var row=NativeRow632(content,"Original enhancement "+i);
                row.localScale=Vector3.one;row.localPosition=new Vector3(0,260-i*72,0);
                // Native model population is an explicit input; each complete
                // serialized 26-node row and all original renderers are retained.
                rows.Add(row);
            }
            foreach(string path in new[]{"Header","Header/Buy","Header/Sell","Tooltip","Content/Scroll View/Scrollbar Vertical", "Content/Scroll View/Scrollbar Vertical/Sliding Area/Handle","Content/Scroll View"})
                inventoryParts.Add(inventory.Find(path));
            Add(inventory,"enchant.inventory|",node=>rows.Contains(node)||inventoryParts.Contains(node));
            foreach(var part in inventoryParts)
                Add(part,"enchant.inventory|"+Relative639(inventory,part),node=>(inventoryParts.Contains(node)&&node!=part)||rows.Contains(node));
            foreach(var row in rows)Add(row,"enchant.row|"+originals.Count);
        }
        Transform confirmation=NativeRow632(owner,"Current confirmation",mage?"mage-confirm":"merchant-confirm");
        confirmation.localScale=Vector3.one*.00055f;confirmation.localPosition=new Vector3(0,-.37f,0);
        confirmation.Find("Blur").gameObject.SetActive(false);
        foreach(var group in confirmation.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        confirmation.GetComponent<Image>().enabled=false;
        var confirmParts=new List<Transform>();
        foreach(string path in mage?new[]{"Content/Header","Content/Information","Content/Name","Content/Image"}:new[]{"Content/Header","Content/Information"})
            confirmParts.Add(confirmation.Find(path));
        // Original interactive button visuals remain in their exact subtrees.
        Transform information=confirmation.Find("Content/Information");
        confirmParts.Add(information.Find("Buttons/Yes"));confirmParts.Add(information.Find("Buttons/No"));
        foreach(var part in confirmParts)
            Add(part,(mage?"enhance.confirm.part.":"item.confirm.part.")+originals.Count+"|",
                node=>confirmParts.Contains(node)&&node!=part);
        Transform physical=Rect("Complete original offered print",owner,Vector2.zero,new Vector2(325.1f,449.5f));
        physical.localScale=Vector3.one*.00049f;physical.localPosition=new Vector3(-.24f,.02f,0);
        physical.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        physical.gameObject.AddComponent<CanvasGroup>();
        var face=Image("Original rendered card face",physical,Vector2.zero,new Vector2(325.1f,449.5f),new Color(.28f,.23f,.16f,1));
        // Full card-model construction is a game boundary. The source content is
        // frozen as complete actual uGUI output, never reconstructed by observer.
        var title=Rect("Native card title",physical,new Vector2(0,150),new Vector2(300,55)).gameObject.AddComponent<TextMeshProUGUI>();
        title.font=TMP_Settings.defaultFontAsset;title.text=mage?"Poison dart":"Running boots";title.fontSize=30;
        title.color=Color.white;title.alignment=TextAlignmentOptions.Center;
        var ability=Rect("Native card complete ability",physical,new Vector2(0,35),new Vector2(280,145)).gameObject.AddComponent<TextMeshProUGUI>();
        ability.font=TMP_Settings.defaultFontAsset;ability.text=mage?"Attack 3\nRange 3\nPoison":"Move +2\nUse after movement";ability.fontSize=25;
        ability.color=Color.white;ability.alignment=TextAlignmentOptions.Center;
        Image? poison=null; Sprite? ownerPoison=null; AssetBundle? atlasBank=null;
        string atlasPath=Path.Combine(Path.GetDirectoryName(_output)!,"original-atlas.bundle");
        if(!File.Exists(atlasPath))atlasPath=Path.Combine(_output,"original-atlas.bundle");
        if(mage&&File.Exists(atlasPath))
        {
            atlasBank=AssetBundle.LoadFromFile(atlasPath);
            Check(atlasBank!=null,"actual original SpriteAtlas bank loads through Unity");
            SpriteAtlas atlas=atlasBank!.LoadAllAssets<SpriteAtlas>().First(x=>x.name=="BattleOverlayCanvas");
            var nativeSprites=new Sprite[atlas.spriteCount];atlas.GetSprites(nativeSprites);
            ownerPoison=nativeSprites.First(x=>x.name.Replace("(Clone)","")=="Poison"&&x.pivot==Vector2.zero);
            _ownerAtlasWrappers639=nativeSprites;
            Check(ownerPoison.packed&&ownerPoison.texture!=null,"real original atlas packing and pixel stream remain bound");
            Check(ownerPoison!=null,"owner materializes the actual native Poison sprite");
            ownerPoison!.name="Poison";
            poison=Image("Native Poison icon",physical,new Vector2(0,-100),new Vector2(75,75),Color.white);poison.sprite=ownerPoison;
        }
        Add(physical,mage?"face.63901|":"itemface.63901|");
        RectTransform? aura=null,ring=null;
        Transform? nativeHolder=null;TownServiceNativeEnhancementCardMask? nativeMask=null;var nativeAreas=new List<RectTransform>();
        if(mage)
        {
            Transform converted=Rect("Actual original highlighter canvas",owner,Vector2.zero,new Vector2(680,660));
            converted.localScale=Vector3.one*.001f;converted.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            RectTransform holder=(RectTransform)NativeRow632(converted,"","highlight");
            nativeHolder=holder;aura=(RectTransform)holder.Find("Aura");ring=(RectTransform)aura.Find("Highlight");
            holder.Find("CardHolder").GetComponent<CanvasGroup>().alpha=1;
            ring.GetComponent<CanvasGroup>().alpha=1;aura.Find("Types/Buy").gameObject.SetActive(true);
            holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
            RectTransform firstArea=(RectTransform)holder.Find("Enhancement Ability Highlight Variant");
            RectTransform secondArea=(RectTransform)Object.Instantiate(firstArea.gameObject,holder,false).transform;
            var areas=new[]{firstArea,secondArea};nativeAreas.AddRange(areas);
            var native=Go("Actual pooled ability card",holder.Find("CardHolder")).AddComponent<AbilityCardUI>();
            RectTransform nativePrint=(RectTransform)Go("FullAbilityCard",native.transform).transform;nativePrint.sizeDelta=((RectTransform)physical).sizeDelta;
            native.fullAbilityCard=nativePrint;
            var highlighter=holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();highlighter.Card=native;
            for(int i=0;i<areas.Length;i++)
            {
                RectTransform target=(RectTransform)Go("Actual native target "+i,nativePrint).transform;
                target.sizeDelta=new Vector2(270,105);target.anchoredPosition=new Vector2(0,80-i*150);
                areas[i].gameObject.AddComponent<UIEnhancementButtonHighlight>();areas[i].SetParent(target);
                areas[i].pivot=target.pivot;areas[i].sizeDelta=target.rect.size;areas[i].position=target.position;
                areas[i].gameObject.SetActive(true);areas[i].Find("Image").GetComponent<CanvasGroup>().alpha=.15f;
            }
            TownServiceEnhancementHandoff.PhysicalCardFace=(RectTransform)physical;
            nativeMask=native.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>();nativeMask.Mask();nativeMask.SendMessage("LateUpdate");
            Add(holder,"enchant.holder|",node=>node==aura||areas.Contains(node));
            Add(aura,"enchant.holder|Aura#0");
            foreach(var area in areas)Add(area,"enchant.highlight|"+originals.Count);
            TownServiceMirror.RegisterOfferedFrame(holder,physical);
        }
        if(mage)for(int i=0;i<26;i++)
        {
            Transform prepared=NativeRow632(owner,"Prepared hidden native row "+i);
            prepared.gameObject.SetActive(false);
            Add(prepared,"enchant.row|prepared."+i,required:false);
        }
        Check(!mage||originals.Count==59&&originals.Count(x=>x.Required)==33,
            "paired hardware density contains33 actual required originals and59 prepared hierarchies");
        Check(!mage||originals.Where(x=>x.Address.StartsWith("enchant.row|",StringComparison.Ordinal))
            .All(x=>x.Source.GetComponentsInChildren<Transform>(true).Length==26),
            "first-picture workload retains every complete native26-node enhancement row");
        TownServiceMirror.BeginSession(service,639,owner,owner);
        foreach(var original in originals)
        {
            TownServiceMirror.RegisterModule(original.Id,original.Id,original.Source,original.Exclude,original.Address);
            TownServiceMirror.SetPriority(original.Id,true);
        }
        TownServiceMirror.SetLocalTransactionActive(service,true);
        TownServiceMirror.RegisterMotionOffering(physical,true);
        var scheduler=new ExtrasSendScheduler(0,3,4);var fragments=new TownServiceFragments();
        var captured=new List<TownServiceFrame>();var motion=new Queue<byte[]>();
        Action<byte[],int,object?> publish=(bytes,length,identity)=>{
            if(identity is TownServiceFrame original){captured.Add(original);scheduler.Enqueue(bytes,length,identity:identity);}
            else motion.Enqueue(bytes);
        };
        var receiver=new NetAvatarDriver();TownServiceMirror.SharedFrameForRemote=_=>observer;
        Action capture=()=>{
            SetNativeSenderActive629(true);
            try{typeof(TownServiceMirror).GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[]{publish,true});}
            finally{SetNativeSenderActive629(false);}
        };
        // Recreate the observer's initially empty registry before the loading
        // seam. Scan materializes separate native atlas wrappers; source-capture
        // Key registration cannot replace their exact already registered aliases.
        TownServiceMirror.Assets.Clear();
        var assetPreparation=System.Diagnostics.Stopwatch.StartNew();
        if(PrewarmObserver639)TownServiceMirror.Assets.Scan();
        foreach(var original in originals)
            TownServiceMirror.PrepareNativeTemplateBasis(service,original.Address);
        assetPreparation.Stop();
        // Ordinary original UI is rendered during loading and already uses the
        // game's full TMP shader before an interaction. Measure this real owner
        // draw separately; it does not preload any observer-only atlas members.
        var preparation=System.Diagnostics.Stopwatch.StartNew();
        Canvas.ForceUpdateCanvases();Render639(owner);

        preparation.Stop();
        // The deadline begins with the real owner capture, including its native
        // property generation, not with the last fragment or admission callback.
        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;
        double clock=0;int events=0,members=0,totalBytes=0;float ready=-1;
        double firstAssembly=-1;bool actualColdSprite=false;
        double captureCpu=0,decodeCpu=0,applyCpu=0,canvasCpu=0,validateCpu=0,renderCpu=0;
        double stage=watch.Elapsed.TotalSeconds;capture();captureCpu+=watch.Elapsed.TotalSeconds-stage;

        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),"registered="+string.Join(",",originals.Select(x=>x.Id+":"+x.Address))+"\nsent="+string.Join(",",captured.Select(x=>x.Module+":"+x.Nodes.Length))+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
        foreach(var original in originals.Where(x=>x.Required))
        {
            var emitted=captured.LastOrDefault(frame=>frame.Module==original.Id);
            Check(emitted!=null,"every original source required by the visible picture is captured: "+original.Address);
            original.Expected=emitted!.Nodes;
        }
        if(ownerPoison!=null)
        {
            // One engine models two machines: the owner-only wrapper must leave
            // Resources' global object census before the observer attempts resolve.
            // The native atlas remains loaded; its dormant entries are real data.
            poison!.sprite=null;
            foreach(var sprite in _ownerAtlasWrappers639)if(sprite!=null)Object.DestroyImmediate(sprite);
            _ownerAtlasWrappers639=Array.Empty<Sprite>();
            if(!PrewarmObserver639)TownServiceMirror.Assets.Clear();
            actualColdSprite=true;
        }
        while(watch.Elapsed.TotalSeconds<5f&&ready<0)
        {
            float now=Time.unscaledTime;clock=watch.Elapsed.TotalSeconds;
            FillOtherQueues639(scheduler);
            if(now>=nextCapture)
            {
                nextCapture=now+1f/15f;
                if(!actualColdSprite)capture();
                else typeof(TownServiceMirror).GetMethod("CaptureMotion",PrivateStatic)!.Invoke(null,new object[]{publish});
            }
            byte[]? batch=scheduler.NextBatch(clock);
            if(batch!=null)
            {
                events++;totalBytes+=batch.Length;
                Check(batch.Length<=PresentationBatch.MaxSize,"actual first picture preserves864-byte event cap");
                foreach(var page in PresentationBatch.TryRead(batch,batch.Length,out var pages)?pages!:new[]{batch})
                {
                    if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                    stage=watch.Elapsed.TotalSeconds;var packet=fragments.Accept(2,page,page.Length,clock);decodeCpu+=watch.Elapsed.TotalSeconds-stage;if(packet==null)continue;
                    if(firstAssembly<0)firstAssembly=watch.Elapsed.TotalSeconds;
                    foreach(var child in TownServiceCodec.TryReadBundle(packet,packet.Length,out var children)?children!:new[]{packet})
                    {Check(receiver.FixtureQueue638(2,child),"actual receive queue accepts original member");members++;}
                }
            }
            while(motion.Count!=0)Check(receiver.FixtureQueueMotion638(2,motion.Dequeue()),"actual queue accepts independent numeric pose/hover");
            stage=watch.Elapsed.TotalSeconds;receiver.FixtureApply638();applyCpu+=watch.Elapsed.TotalSeconds-stage;
            stage=watch.Elapsed.TotalSeconds;Canvas.ForceUpdateCanvases();canvasCpu+=watch.Elapsed.TotalSeconds-stage;
            stage=watch.Elapsed.TotalSeconds;bool complete=originals.Where(x=>x.Required).All(original=>VisibleComplete639(original));validateCpu+=watch.Elapsed.TotalSeconds-stage;
            if(complete)
            {
                // Rendering is inside the deadline. Admission/root activation
                // cannot alone certify that visible text, sprites and list exist.
                stage=watch.Elapsed.TotalSeconds;var pixels=Render639(observer);renderCpu+=watch.Elapsed.TotalSeconds-stage;
                int ink=pixels.Count(pixel=>pixel.r>30||pixel.g>30||pixel.b>30);
                Check(ink>1500,"actual observer frame contains substantial complete-card/list/confirmation ink");
                ready=(float)watch.Elapsed.TotalSeconds;
                File.WriteAllBytes(Path.Combine(_output,"first-complete-original-picture.png"),pixels.TextureBytes639());
            }
            yield return null;
        }
        string route="CaptureCore(fast:true)->actual ExtrasSendScheduler(real50ms)->TownServiceFragments->actual QueueTownService/ApplyTownServices->native binding validation/apply/mount->Canvas rebuild->Camera.Render/ReadPixels";
        File.WriteAllText(Path.Combine(_output,"first-picture639-cost.txt"),route+"\nservice="+service+" required="+originals.Count(x=>x.Required)+" prepared="+originals.Count
            +" exactNativeRowNodes="+(mage?26:0)+" firstAssembly="+firstAssembly+" firstFullRenderedSeconds="+ready
            +" events="+events+" wireBytes="+totalBytes+" members="+members+" actualColdSprite="+actualColdSprite+"\n"
            +"assetLoadingPreparation="+assetPreparation.Elapsed.TotalSeconds+" observerPrewarmed="+PrewarmObserver639+" ownerDrawPreparation="+preparation.Elapsed.TotalSeconds+" captureCpu="+captureCpu+" decodeCpu="+decodeCpu+" applyCpu="+applyCpu+" canvasCpu="+canvasCpu+" validateCpu="+validateCpu+" actualRenderCpu="+renderCpu+"\n"
            +string.Join("\n",GloomhavenVR.Core.VRLog.Messages)+"\n");
        Check(ready>=0&&ready<=1.000,"all exact visible originals render within1s wall clock");
        foreach(var original in originals.Where(x=>x.Required))AssertComplete639(original);
        if(poison!=null)
        {
            var remotePrint=Remote(2,originals.First(x=>x.Source==physical).Id)!;
            Sprite remotePoison=remotePrint.Root.Find("Native Poison icon").GetComponent<Image>().sprite;
            Check(remotePoison!=null&&remotePoison.texture!=null&&remotePoison.packed&&remotePoison.pivot==Vector2.zero,
                "complete actual remote print uses exact native zero-pivot packed Poison, never its centered sibling");
            poison.sprite=atlasBank!.LoadAsset<Sprite>("native/sprite/4688");
        }
        if(mage)
        {
            var continuation=ContinuousOriginal639(originals,physical,ring!,nativeAreas,nativeMask!,capture,publish,scheduler,fragments,receiver,watch,motion);
            while(continuation.MoveNext())yield return continuation.Current;
        }
        TownServiceEnhancementHandoff.PhysicalCardFace=null;
        TownServiceMirror.Shutdown();if(atlasBank!=null)atlasBank.Unload(true);
    }
    private static IEnumerator ContinuousOriginal639(List<PictureOriginal639> originals,Transform physical,
        RectTransform ring,List<RectTransform> areas,TownServiceNativeEnhancementCardMask mask,Action capture,
        Action<byte[],int,object?> publish,ExtrasSendScheduler scheduler,TownServiceFragments fragments,
        NetAvatarDriver receiver,System.Diagnostics.Stopwatch watch,Queue<byte[]> motion)
    {
        ushort printId=originals.First(x=>x.Source==physical).Id;
        PictureOriginal639 aura=originals.First(x=>ring.IsChildOf(x.Source)&&x.Source!=physical);
        var values=new System.Text.StringBuilder("actual post-admission owner animation/hover and original mount\n");
        float began=Time.unscaledTime,next=began;int checks=0;float remoteRingTravel=0,lastRing=0;
        bool sampled=false;
        while(Time.unscaledTime-began<1.25f)
        {
            float age=Time.unscaledTime-began;
            physical.localRotation=Quaternion.Euler(6*Mathf.Sin(age*2),42*Mathf.Sin(age*1.7f),3);
            physical.localPosition=new Vector3(-.24f,.02f+.025f*Mathf.Sin(age*3),0);
            ring.localRotation=Quaternion.Euler(0,0,age*125f);
            foreach(RectTransform area in areas)
                area.Find("Image").GetComponent<CanvasGroup>().alpha=.15f+.25f*(.5f+.5f*Mathf.Sin(age*4));
            mask.SendMessage("LateUpdate");
            if(Time.unscaledTime>=next)
            {next=Time.unscaledTime+1f/15f;capture();}
            byte[]? batch=scheduler.NextBatch(watch.Elapsed.TotalSeconds);
            if(batch!=null)foreach(var page in PresentationBatch.TryRead(batch,batch.Length,out var pages)?pages!:new[]{batch})
            {
                if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                var packet=fragments.Accept(2,page,page.Length,watch.Elapsed.TotalSeconds);if(packet==null)continue;
                foreach(var child in TownServiceCodec.TryReadBundle(packet,packet.Length,out var children)?children!:new[]{packet})
                    Check(receiver.FixtureQueue638(2,child),"post-admission changed original graphics remain on the actual receiver path");
            }
            while(motion.Count!=0)Check(receiver.FixtureQueueMotion638(2,motion.Dequeue()),"owner-authored continuous native poses/hover receive unchanged");
            receiver.FixtureApply638();Canvas.ForceUpdateCanvases();
            var remotePrint=Remote(2,printId)!;var remoteAura=Remote(2,aura.Id)!;
            var remoteRing=(RectTransform)remoteAura.Root.Find("Highlight");
            Check(remoteRing!=null&&remoteRing.gameObject.activeInHierarchy&&remoteRing.GetComponent<Image>().sprite!=null,
                "the complete native ring stays visible throughout continuously changing owner pose");
            Check(Vector3.Dot(remoteRing.forward,remotePrint.Root.forward)>.999f,
                "the actual native ring and complete physical print share one observer plane at every intermediate frame");
            var corners=new Vector3[4];remoteRing.GetWorldCorners(corners);
            float horizontal=(corners[3]-corners[0]).magnitude,vertical=(corners[1]-corners[0]).magnitude;
            Check(Math.Abs(horizontal-vertical)<.0006f,
                "native ring retains equal physical side lengths while the offered card continuously rotates");
            foreach(var area in areas)
            {
                var remote=Remote(2,originals.First(x=>x.Source==area).Id)!;
                Check(remote.Root.gameObject.activeInHierarchy&&Vector3.Dot(remote.Root.forward,remotePrint.Root.forward)>.999f,
                    "every original selectable card area stays visible and on the same current card plane");
            }
            float angle=remoteRing.localEulerAngles.z;
            if(sampled)remoteRingTravel+=Math.Abs(Mathf.DeltaAngle(lastRing,angle));
            sampled=true;lastRing=angle;checks++;
            values.AppendLine(age+" printYaw="+remotePrint.Root.eulerAngles.y+" ring="+angle+" width="+horizontal+" height="+vertical);
            yield return null;
        }
        Check(checks>=8&&remoteRingTravel>45,
            "actual intermediate frames show a smoothly progressing native rotating ring, not a static admitted flag");
        File.WriteAllText(Path.Combine(_output,"continuous-original639.txt"),values.ToString());
    }
    private static void FillOtherQueues639(ExtrasSendScheduler scheduler)
    {
        foreach(string field in new[]{"_presence","_animation","_plumes","_board","_appearance","_prompt","_itemAppearance","_mapTooltip"})
        {
            var queue=(ExtrasSendQueue)typeof(ExtrasSendScheduler).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scheduler)!;
            int limit=(int)typeof(ExtrasSendQueue).GetField("_snapshotLimit",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
            byte type=(byte)typeof(ExtrasSendQueue).GetField("_payloadType",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
            var bytes=new byte[limit];new System.Random(639).NextBytes(bytes);
            bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=type;
            queue.Enqueue(bytes,bytes.Length);
        }
    }
    private static bool VisibleComplete639(PictureOriginal639 original)
    {
        var remote=Remote(2,original.Id);if(remote==null||!remote.Root.gameObject.activeInHierarchy)return false;
        try
        {
            // Resolve every active sprite/font/material via the exact incoming
            // owner contract; successful opaque roots with missing innards fail.
            var expected=new TownServiceFrame{Structure=remote.Structure,Nodes=original.Expected};
            remote.Validate(expected,TownServiceMirror.Assets);
            foreach(var graphic in remote.Root.GetComponentsInChildren<Graphic>(true))
                if(graphic.enabled&&graphic.gameObject.activeInHierarchy&&graphic.color.a>.01f&&graphic is TMP_Text text
                    && text.text=="different observer default")return false;
            return true;
        }
        catch(InvalidDataException){return false;}
    }
    private static void AssertComplete639(PictureOriginal639 original)
    {
        var target=Remote(2,original.Id)!;
        using var sourceBinding=new TownServiceBinding(original.Source,original.Exclude);
        Check(target.Structure==sourceBinding.Structure,
            "complete source/observer original hierarchy remains exact: "+original.Address);
        foreach(var text in original.Source.GetComponentsInChildren<TMP_Text>(true))
        {
            if(!text.enabled||!text.gameObject.activeInHierarchy||(original.Exclude!=null&&Excluded639(text.transform,original.Source,original.Exclude)))continue;
            string path=Relative639(original.Source,text.transform);
            var clone=(path.Length==0?target.Root:target.Root.Find(path))?.GetComponent<TMP_Text>();
            Check(clone!=null&&clone.text==text.text&&Math.Abs(clone.fontSize-text.fontSize)<.001f,
                "all original option/card/confirmation text and size are present: "+original.Address+"/"+path);
        }
        foreach(var image in original.Source.GetComponentsInChildren<Image>(true))
        {
            if(!image.gameObject.activeInHierarchy||(original.Exclude!=null&&Excluded639(image.transform,original.Source,original.Exclude)))continue;
            string path=Relative639(original.Source,image.transform);
            var clone=(path.Length==0?target.Root:target.Root.Find(path))?.GetComponent<Image>();
            Check(clone!=null&&clone.enabled==image.enabled&&clone.color==image.color,
                "all original row/area/ring/confirmation image state is present: "+original.Address+"/"+path);
            if(image.sprite!=null)Check(clone!.sprite!=null&&clone.sprite.texture!=null,
                "all nonempty original artwork resolves to genuine loaded pixels: "+original.Address+"/"+path);
        }
    }
    private static bool Excluded639(Transform node,Transform root,Func<Transform,bool> exclude)
    {for(var current=node;current!=null&&current!=root;current=current.parent)if(exclude(current))return true;return false;}
    private static string Relative639(Transform root,Transform node)
    {string path="";while(node!=root){path=node.name+(path.Length==0?"":"/"+path);node=node.parent;}return path;}
    private static Color32[] Render639(Transform observer)
    {
        foreach(var go in Objects)if(go!=null)Layer(go.transform,30);Layer(observer,9);
        foreach(var canvas in observer.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<9;_camera.orthographic=true;_camera.orthographicSize=.95f;
        _camera.transform.SetPositionAndRotation(observer.position+new Vector3(0,-.1f,-5),observer.rotation);
        _camera.clearFlags=CameraClearFlags.SolidColor;_camera.backgroundColor=new Color(.025f,.03f,.04f,1);
        var rt=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);var image=new Texture2D(768,768,TextureFormat.RGBA32,false);
        try{_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;image.ReadPixels(new UnityEngine.Rect(0,0,768,768),0,0);image.Apply();return image.GetPixels32();}
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
internal static class PicturePixels639
{
    internal static byte[] TextureBytes639(this Color32[] pixels)
    {var texture=new Texture2D(768,768,TextureFormat.RGBA32,false);try{texture.SetPixels32(pixels);texture.Apply();return texture.EncodeToPNG();}finally{UnityEngine.Object.DestroyImmediate(texture);}}
}
