using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine.UI;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    // Three independent physical originals create contention for the real numeric
    // budget. Each card's native return registration is shared by its front/body.
    // The source geometry and engine renderer are real; catalogue/game rules are
    // boundary inputs. No observer transform is corrected by the test.
    private static IEnumerator NativeFlight658()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
        Transform owner=Go("Native658 source map").transform, observer=Go("Native658 observer map").transform;
        observer.position=Vector3.right*8f; TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("Actual native physical return").transform;card.SetParent(owner,false);card.localPosition=new Vector3(0,1,0);
        VRCard native=card.gameObject.AddComponent<VRCard>();
        Transform prefab=NativeRow632(card,"","ability-card"), full=prefab.Find("Full");
        full.SetParent(card,false);Object.DestroyImmediate(prefab.gameObject);
        full.gameObject.SetActive(true);full.localScale=Vector3.one*.00049f;full.localPosition=new Vector3(0,0,-.001f);
        full.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        full.Find("Header/Title text").GetComponent<TMP_Text>().text="Poison dart";
        for(int half=0;half<2;half++)
        {
            Transform content=full.Find(half==0?"Top button/Content":"Bottom button/Content");
            var layout=(RectTransform)NativeRow632(content,"","text-container");layout.name="Layout Parent";
            layout.sizeDelta=new Vector2(294,190);layout.anchoredPosition=Vector2.zero;
            for(int row=0;row<2;row++)
            {
                var rowRoot=(RectTransform)NativeRow632(layout,"","text-container");rowRoot.name="Row Container "+row;
                rowRoot.sizeDelta=new Vector2(270,70);rowRoot.anchoredPosition=new Vector2(0,45-row*75);
                Transform text=NativeRow632(rowRoot,"","preview-text");((RectTransform)text).sizeDelta=new Vector2(235,55);
                text.GetComponent<TMP_Text>().text=half==0?(row==0?"Attack 3":"Range 3 — Poison"):(row==0?"Move 4":"Gain 1 experience");
                Transform enhancementContainer=NativeRow632(rowRoot,"","enhancement-container");
                ((RectTransform)enhancementContainer).anchoredPosition=new Vector2(120,0);
                NativeRow632(enhancementContainer,"","enhancement");
            }
            NativeRow632(layout,"","xp-container");NativeRow632(layout,"","duration-res");
        }
        Transform body=TownServiceCardBody.Create(card).transform;
        body.localScale=new Vector3(((RectTransform)full).rect.width*.00049f,((RectTransform)full).rect.height*.00049f,1f);
        using(var assets=new TownServiceBinding(body))assets.Read(TownServiceMirror.Assets);
        bool moving=false;
        bool Sample(Transform source,Transform shared,VRHand? hand,out uint revision,out float[] values)
        {if(moving)return native.CaptureNativeTownReturn655(source,shared,hand,out revision,out values);revision=0;values=Array.Empty<float>();return false;}
        var parts=LazyTemplateProbe.NativePrintPartitions658(full);
        var sources=new List<Transform>{body};sources.AddRange(parts.Select(part=>part.Original));
        var ids=new List<ushort>();
        var extraCards=new Transform[2];var extraNatives=new VRCard[2];
        for(int i=0;i<2;i++)
        { GameObject clone=Object.Instantiate(card.gameObject,owner,false);Objects.Add(clone);extraCards[i]=clone.transform;
          extraCards[i].localPosition=new Vector3(0,1f+(i==0?.32f:-.32f),0);extraNatives[i]=clone.GetComponent<VRCard>(); }
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,658,owner,owner);
            for(int i=0;i<2;i++)
            {
                int slot=i;Transform extraBody=extraCards[i].Find("PhysicalCardBody");
                bool ExtraSample(Transform source,Transform shared,VRHand? hand,out uint revision,out float[] values)
                {if(moving)return extraNatives[slot].CaptureNativeTownReturn655(source,shared,hand,out revision,out values);revision=0;values=Array.Empty<float>();return false;}
                ushort id=(ushort)(10+i);TownServiceMirror.RegisterTemplate(1,id,extraBody,address:"map.cardbody|nativeextra"+i);
                TownServiceMirror.RegisterModule(id,id,extraBody,address:"map.cardbody|nativeextra"+i);TownServiceMirror.PrepareCardReturn(extraBody,ExtraSample);
                TownServiceMirror.PrepareCardReturn(extraCards[i].Find("Full"),ExtraSample);
            }
            for(int i=0;i<sources.Count;i++)
            {
                ushort id=(ushort)(30+i);ids.Add(id);Func<Transform,bool>? exclude=i==0?null:parts[i-1].Excluded.Contains;
                string address=i==0?"map.cardbody|658native":"face.65801|"+parts[i-1].Path;
                TownServiceMirror.RegisterTemplate(1,id,sources[i],exclude,address);
                TownServiceMirror.RegisterModule(id,id,sources[i],exclude,address);
                TownServiceMirror.PrepareCardReturn(sources[i],Sample);
            }
            for(int i=0;i<2;i++)
            { ushort id=(ushort)(20+i);Transform print=extraCards[i].Find("Full");
              TownServiceMirror.RegisterTemplate(1,id,print,address:"face."+(65802+i)+"|nativeextra");
              TownServiceMirror.RegisterModule(id,id,print,address:"face."+(65802+i)+"|nativeextra"); }
        }
        GameObject offered=Object.Instantiate(card.gameObject,owner,false);Objects.Add(offered);offered.name="Actual offered native print";
        using(TownServiceMirror.UseStockLane())
        { TownServiceMirror.RegisterTemplate(1,200,offered.transform,address:"face.65801|offer");
          TownServiceMirror.RegisterModule(200,200,offered.transform,address:"face.65801|offer"); }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;int peer=keys[2];
        Check(sources.Count>=14,"actual Full and shipped native action prototypes produce a large original partition cohort");
        foreach(ushort id in ids)Check(Remote(peer,id)!=null && !Remote(peer,id)!.Root.gameObject.activeInHierarchy,"prepared native originals are cached hidden while the offered picture is separate id="+id+" exists="+(Remote(peer,id)!=null));
        File.WriteAllText(Path.Combine(_output,"native-print658.txt"),"nodes="+full.GetComponentsInChildren<Transform>(true).Length+" printParts="+parts.Count+" plus actual production CardBody/CardMesh\n");
        Check(Remote(peer,200)!=null && Remote(peer,200)!.Root.gameObject.activeInHierarchy,"the separate exact offered print is visible before the prepared return exists="+(Remote(peer,200)!=null)+" logs="+string.Join(" / ",GloomhavenVR.Core.VRLog.Messages));
        offered.SetActive(false);using(TownServiceMirror.UseStockLane())TownServiceMirror.UnregisterModule(200);
        moving=true;native.BeginNative655(owner.TransformPoint(new Vector3(.27f,1,.02f)),.55f);
        for(int i=0;i<2;i++)extraNatives[i].BeginNative655(owner.TransformPoint(new Vector3(.27f,1f+(i==0?.32f:-.32f),.02f)),.55f);
        Vector3 prior=Vector3.zero;float largestStep=0;TownServiceMotionEntry? receipt=null;
        var receiptParts=new SortedDictionary<byte,TownServiceReturnPart>();
        var receiptRoots=new Dictionary<ushort,TownServiceMotionEntry>();
        var sourceBindings=sources.Select((source,index)=>new TownServiceBinding(source,index==0?null:parts[index-1].Excluded.Contains)).ToArray();
        for(int frame=1;frame<=42;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;native.StepNative655();foreach(var extra in extraNatives)extra.StepNative655();
            if(frame%6==0)
            {
                if(frame==6)full.Find("Header/Title text").GetComponent<TMP_Text>().text="Poison dart — returning";
                FastCapture sample=CaptureFast();
                foreach(byte[] bytes in sample.Motion)if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet))
                    foreach(var entry in packet!.Entries)if(entry.Kind==10 && entry.Module==ids[0] && (receipt==null || receipt.ReturnSampleTime==entry.ReturnSampleTime))
                    {
                        receipt??=entry;
                        foreach(var part in entry.ReturnParts)
                        {
                            receiptParts[part.Index]=part;
                            ushort member=entry.ReturnMembers[part.Index];
                            TownServiceMotionEntry root=packet.Entries.Single(candidate=>candidate.Kind==1
                                && candidate.Lane==entry.Lane && candidate.Service==entry.Service && candidate.Session==entry.Session
                                && candidate.PublicClaim==entry.PublicClaim && candidate.Module==member
                                && candidate.Structure==entry.ReturnStructures[part.Index] && candidate.Hand==entry.Hand);
                            Check(root.Visible==part.Visible && root.ParentAlpha==part.ParentAlpha,
                                "actual budget publishes each native subset beside its exact complete root recipe");
                            receiptRoots[member]=root;
                        }
                    }
                // Both receipt orders exercise exact source header preservation:
                // repair metadata cannot independently reopen/move one surface.
                if(frame%12==0){DeliverMotion(2,sample);Receive(2,sample.Artwork);}
                else {Receive(2,sample.Artwork);TownServiceMirror.TickRemote(_=>observer);
                    foreach(ushort id in ids)if(frame==6)Check(!Remote(peer,id)!.Root.gameObject.activeInHierarchy,"repair metadata before113 retains hidden prepared originals instead of reopening gray surfaces");
                    DeliverMotion(2,sample);}
            }
            TownServiceMirror.TickRemote(_=>observer);
            Transform remoteBody=Remote(peer,ids[0])!.Root;
            if(frame>=6)
                for(int i=0;i<2;i++)
                {
                    Transform extraBody=extraCards[i].Find("PhysicalCardBody"),extraFront=extraCards[i].Find("Full");
                    Transform paintedBody=Remote(peer,(ushort)(10+i))!.Root,paintedFront=Remote(peer,(ushort)(20+i))!.Root;
                    Vector3 expected=paintedBody.TransformPoint(extraBody.InverseTransformPoint(extraFront.position));
                    Check(paintedBody.gameObject.activeInHierarchy==paintedFront.gameObject.activeInHierarchy
                        && Vector3.Distance(expected,paintedFront.position)<.00005f,
                        "each complete native front/body stays coherent between independent return receipts");
                }
            if(frame>=12)
            {
                Check(Vector3.Distance(remoteBody.position,observer.TransformPoint(owner.InverseTransformPoint(body.position)))<.00005f,
                    "absolute body trajectory follows the actual native source instant before picture normalization");
                Check(remoteBody.gameObject.activeInHierarchy,"actual prepared body opens from its native clock, without waiting for artwork repair");
                for(int index=0;index<ids.Count;index++)
                {
                    var sourceBinding=sourceBindings[index];var painted=Remote(peer,ids[index])!;
                    Check(painted.Root.gameObject.activeInHierarchy==sources[index].gameObject.activeInHierarchy,"native original partition retains source visibility");
                    Check(sourceBinding.Bindings.SequenceEqual(painted.Bindings),"native node identity remains exact within each original print partition");
                    for(int node=0;node<sourceBinding.Nodes.Length;node++)
                    {
                        Transform a=sourceBinding.Nodes[node],b=painted.Nodes[node];
                        if(node!=0)Check(a.gameObject.activeSelf==b.gameObject.activeSelf,"native original child keeps authored active state id="+ids[index]+" node="+a.name);
                        Graphic g=a.GetComponent<Graphic>(),h=b.GetComponent<Graphic>();
                        if(g!=null)Check(h!=null && g.enabled==h.enabled && Mathf.Abs(g.color.a-h.color.a)<.00001f,"native print graphic keeps enabled state and exact authored alpha");
                        CanvasGroup c=a.GetComponent<CanvasGroup>(),d=b.GetComponent<CanvasGroup>();
                        if(c!=null)Check(d!=null && Mathf.Abs(c.alpha-d.alpha)<.00001f,"native print keeps its authored group alpha");
                        Canvas e=a.GetComponent<Canvas>(),f=b.GetComponent<Canvas>();
                        if(e!=null)Check(f!=null && e.enabled==f.enabled,"native print canvas retains native enablement");
                    }
                }
                for(int i=1;i<sources.Count;i++)
                {
                    Transform remote=Remote(peer,ids[i])!.Root;
                    Vector3 expected=remoteBody.TransformPoint(body.InverseTransformPoint(sources[i].position));
                    Check(Vector3.Distance(expected,remote.position)<.00005f,"every actual native print partition follows the body's same source clock");
                }
                if(frame>12)largestStep=Mathf.Max(largestStep,Vector3.Distance(prior,remoteBody.position));prior=remoteBody.position;
            }
            if(frame==12 || frame==24 || frame==36)
            {
                foreach(var extra in extraCards)extra.gameObject.SetActive(false);
                foreach(ushort extraId in new ushort[]{10,11,20,21})Remote(peer,extraId)!.Root.parent.gameObject.SetActive(false);
                Color32[] pixels=FlightPixels658(observer,9,"native658-observer-"+frame);
                Vector3 savedPosition=card.position,savedScale=card.localScale;Quaternion savedRotation=card.rotation;
                card.position=remoteBody.position-observer.position+owner.position;card.rotation=remoteBody.rotation;
                card.localScale=new Vector3(remoteBody.lossyScale.x/body.localScale.x,remoteBody.lossyScale.y/body.localScale.y,remoteBody.lossyScale.z/body.localScale.z);
                Color32[] original=FlightPixels658(owner,8,"native658-owner-same-instant-"+frame);
                card.position=savedPosition;card.rotation=savedRotation;card.localScale=savedScale;
                foreach(var extra in extraCards)extra.gameObject.SetActive(true);
                foreach(ushort extraId in new ushort[]{10,11,20,21})Remote(peer,extraId)!.Root.parent.gameObject.SetActive(true);
                int union=0,different=0;
                for(int p=0;p<pixels.Length;p++)
                {
                    int ownerInk=original[p].r+original[p].g+original[p].b,remoteInk=pixels[p].r+pixels[p].g+pixels[p].b;
                    if(Math.Max(ownerInk,remoteInk)<100)continue;
                    union++;if(Math.Abs(original[p].r-pixels[p].r)+Math.Abs(original[p].g-pixels[p].g)+Math.Abs(original[p].b-pixels[p].b)>60)different++;
                }
                File.AppendAllText(Path.Combine(_output,"native-pixels658.txt"),frame+": ownerInk="+FlightInk658(original)+" observerInk="+FlightInk658(pixels)+" union="+union+" different="+different+"\n");
                Check(FlightInk658(pixels)>1000 && union>1000 && different<union*.02f,"actual combined print/body render matches owner at the same native instant");
            }
            yield return null;
        }
        Check(largestStep<.012f,"native return advances smoothly each render between fifteen-Hz numeric receipts");
        Check(receipt!=null && receiptParts.Count==receipt.ReturnMembers.Length,"actual native large-cohort capture supplies the complete immutable lifecycle input");
        Check(receiptRoots.Count==receipt!.ReturnMembers.Length,"actual native lifecycle input retains every budget-published companion root");
        receipt!.ReturnParts=receiptParts.Values.ToArray();
        for(int frame=43;frame<=54;frame++)
        {FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;native.StepNative655();foreach(var extra in extraNatives)extra.StepNative655();}
        FastCapture ended=CaptureFast();bool standalone=false;
        foreach(byte[] bytes in ended.Motion)if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet))
            foreach(var entry in packet!.Entries){Check(entry.Kind!=10,"actual ended native sampler stops cohort publication");standalone|=entry.Kind==1;}
        DeliverMotion(2,ended);Receive(2,ended.Artwork);TownServiceMirror.TickRemote(_=>observer);
        Check(standalone && TownServiceMirror.Cohorts658(2)==0 && TownServiceMirror.ReturnClocks658(2)==0,
            "actual native sampler completion resumes standalone originals and retires every live return");
        Lifecycle658(receipt,receiptRoots,observer,peer);
        foreach(var binding in sourceBindings)binding.Dispose();
        TownServiceMirror.Shutdown();
    }
    // Record113 and its root recipes are one physical sample. Loss/reorder probes
    // replay the exact roots captured from FillPacked, never guessed DTO poses.
    private static void AddNativeRoots658(TownServiceMotionPacket packet,TownServiceMotionEntry receipt,
        IReadOnlyDictionary<ushort,TownServiceMotionEntry> roots)
    {
        if(receipt.Kind!=10)return;
        foreach(var part in receipt.ReturnParts)
        {
            ushort member=receipt.ReturnMembers[part.Index];
            Check(roots.TryGetValue(member,out var root) && root.Kind==1 && root.Lane==receipt.Lane
                && root.Service==receipt.Service && root.Session==receipt.Session && root.PublicClaim==receipt.PublicClaim
                && root.Module==member && root.Structure==receipt.ReturnStructures[part.Index] && root.Hand==receipt.Hand
                && root.Visible==part.Visible && root.ParentAlpha==part.ParentAlpha,
                "lifecycle replays each exact budget root in its subset's same bounded packet");
            packet.Entries.Add(root!);
        }
    }
    private static void Lifecycle658(TownServiceMotionEntry receipt,IReadOnlyDictionary<ushort,TownServiceMotionEntry> roots,Transform observer,int stockPeer)
    {
        TownServiceMotionEntry Copy(params TownServiceReturnPart[] parts)=>new() { Kind=10,Lane=receipt.Lane,Service=receipt.Service,
            Session=receipt.Session,PublicClaim=receipt.PublicClaim,Module=receipt.Module,Structure=receipt.Structure,
            Hand=receipt.Hand,Revision=receipt.Revision,ReturnSampleTime=receipt.ReturnSampleTime,Numbers=receipt.Numbers,
            ReturnMembers=receipt.ReturnMembers,ReturnStructures=receipt.ReturnStructures,ReturnParts=parts };
        void Send(ulong sequence,TownServiceMotionEntry entry,float? time=null)
        {
            var packet=new TownServiceMotionPacket{Sequence=sequence,SampleTime=time??receipt.ReturnSampleTime};packet.Entries.Add(entry);
            AddNativeRoots658(packet,entry,roots);
            byte[] encoded=TownServiceMotionCodec.TryWritePacked(packet)??throw new InvalidOperationException("native lifecycle packet exceeds its unchanged event");
            Check(TownServiceMotionCodec.TryRead(encoded,encoded.Length,out var read),"lifecycle receives actual validated113 bytes");
            TownServiceMirror.ReceiveMotion(2,read!);
        }
        void Reset()
        {TownServiceMirror.ForgetRemoteMotion(2);FlightTime655.Now=receipt.ReturnSampleTime;TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);}
        Reset();Vector3 parked=Remote(stockPeer,receipt.Module)!.Root.position;
        Send(1000,Copy(receipt.ReturnParts[0]));TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
        Check(TownServiceMirror.ReturnClocks658(2)==0 && Remote(stockPeer,receipt.Module)!.Root.position==parked,
            "loss of later native subset keeps every part's previous physical picture");
        Send(1002,Copy(receipt.ReturnParts.Skip(1).ToArray()));Send(1001,Copy(receipt.ReturnParts[0]));
        TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
        Check(TownServiceMirror.ReturnClocks658(2)==receipt.ReturnMembers.Length,"loss recovery and per-member reorder activate the complete native cohort together");
        foreach(bool partial in new[]{false,true})foreach(byte hand in new byte[]{0,1,2,3,4})
        {
            Reset();Send(1100,Copy(partial?new[]{receipt.ReturnParts[0]}:receipt.ReturnParts));TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
            var newRoot=new TownServiceMotionEntry{Kind=1,Lane=receipt.Lane,Service=receipt.Service,Session=receipt.Session,
                Module=receipt.Module,Structure=receipt.Structure,ParentModule=TownServiceFrame.ManifestModule,Hand=hand,Visible=true,ParentAlpha=1,
                Pose=new[]{.8f,1f,.1f,0f,0f,0f,1f,1f,1f,1f}};
            Transform tracked=Go("Actual interruption hand "+hand).transform;tracked.position=observer.position;
            NetAvatarDriver.MotionHandFrames[2]=new[]{tracked,tracked};
            // A reordered older standalone root cannot kill the live return.
            Send(1099,newRoot,receipt.ReturnSampleTime);TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
            Check(TownServiceMirror.Cohorts658(2)==1,"older reordered root retains the exact physical return");
            Send(1101,newRoot,receipt.ReturnSampleTime+.02f);TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
            Check(TownServiceMirror.Cohorts658(2)==0 && TownServiceMirror.ReturnClocks658(2)==0,
                "newer native root immediately interrupts "+(partial?"incomplete":"activated")+" return hand="+hand);
            Send(1100,Copy(receipt.ReturnParts));TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
            Check(TownServiceMirror.Cohorts658(2)==0,"late native subset cannot resurrect an interrupted return");
            Vector3 position=Remote(stockPeer,receipt.Module)!.Root.position;FlightTime655.Now+=.08f;TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
            Check(Vector3.Distance(position,Remote(stockPeer,receipt.Module)!.Root.position)<.00005f,"retired clock no longer drags the held or stationary original");
        }
        Reset();Send(1200,Copy(receipt.ReturnParts[0]));TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
        FlightTime655.Now+=receipt.Numbers[1]+.3f;TownServiceMirror.ApplyRemoteMotion(FlightTime655.Now);
        Check(TownServiceMirror.Cohorts658(2)==0,"incomplete lost cohort expires on its exact native lifetime");
        Reset();Send(1300,Copy(receipt.ReturnParts[0]));TownServiceMirror.ResetMotionNetwork();
        Check(TownServiceMirror.Cohorts658(2)==0 && TownServiceMirror.ReturnClocks658(2)==0,"network reset retires all native pending state");
        NetAvatarDriver.MotionHandFrames.Clear();
        Census658(receipt,roots,observer,stockPeer);
    }
    private static void Census658(TownServiceMotionEntry native,IReadOnlyDictionary<ushort,TownServiceMotionEntry> roots,Transform observer,int stockPeer)
    {
        var owners=(IDictionary)typeof(TownServiceMirror).GetField("Remote",PrivateStatic)!.GetValue(null)!;
        var modules=(IDictionary)owners[stockPeer]!;
        var originals=new Dictionary<ushort,TownServiceFrame>();
        foreach(ushort id in modules.Keys)
        { object module=modules[id]!;originals.Add(id,TownServiceDelta.Copy((TownServiceFrame)module.GetType().GetField("LastFrame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(module)!)); }
        ushort[] all=originals.Keys.OrderBy(id=>id).ToArray();ulong artworkSequence=10000,motionSequence=2000;float source=native.ReturnSampleTime;
        TownServiceMotionEntry Copy(TownServiceReturnPart[] parts)=>new(){Kind=10,Lane=native.Lane,Service=native.Service,Session=native.Session,
            PublicClaim=native.PublicClaim,Module=native.Module,Structure=native.Structure,Hand=native.Hand,Revision=native.Revision,
            ReturnSampleTime=source,Numbers=native.Numbers,ReturnMembers=native.ReturnMembers,ReturnStructures=native.ReturnStructures,ReturnParts=parts};
        void Send(TownServiceMotionEntry entry)
        {var packet=new TownServiceMotionPacket{Sequence=++motionSequence,SampleTime=source};packet.Entries.Add(entry);AddNativeRoots658(packet,entry,roots);byte[] bytes=TownServiceMotionCodec.TryWritePacked(packet)!;
            Check(bytes!=null && TownServiceMotionCodec.TryRead(bytes,bytes.Length,out _),"native census control uses actual bounded113 bytes");TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var decoded);TownServiceMirror.ReceiveMotion(2,decoded!);}
        void Manifest(ushort[] census,float time)
        {var frame=new TownServiceFrame{VisitorStock=true,Service=native.Service,Session=native.Session,PublicClaim=native.PublicClaim,
            Module=TownServiceFrame.ManifestModule,Sequence=++artworkSequence,SampleTime=time,Visible=true,Modules=census,
            Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f}};
            byte[] bytes=TownServiceCodec.Write(frame);Check(TownServiceMirror.Receive(2,bytes,bytes.Length),"native census received through production original codec");}
        void Restore()
        {
            Manifest(all,source);
            foreach(var original in originals.Values)
            {var frame=TownServiceDelta.Copy(original);frame.Sequence=++artworkSequence;frame.BaseSequence=0;frame.NativeTemplateBasisKey=0;frame.SampleTime=source;
                byte[] bytes=TownServiceCodec.Write(frame);Check(TownServiceMirror.Receive(2,bytes,bytes.Length),"exact original restores its declared native identity");}
            TownServiceMirror.TickRemote(_=>observer);
        }
        ushort[] old=all.Where(id=>id!=native.ReturnMembers.Last()).ToArray();
        foreach(bool numericFirst in new[]{false,true})foreach(bool equalTime in new[]{false,true})
        {
            source+=1;FlightTime655.Now=source;TownServiceMirror.ForgetRemoteMotion(2);Restore();
            if(numericFirst)Send(Copy(new[]{native.ReturnParts[0]}));
            Manifest(old,source-(equalTime?0f:.01f));
            if(!numericFirst)Send(Copy(new[]{native.ReturnParts[0]}));
            TownServiceMirror.ApplyRemoteMotion(source);
            Check(TownServiceMirror.Cohorts658(2)==1 && TownServiceMirror.ReturnClocks658(2)==0,
                "older or equal-time census waits for changed membership in either receive order");
            Restore();Send(Copy(native.ReturnParts.Skip(1).ToArray()));TownServiceMirror.ApplyRemoteMotion(source);
            Check(TownServiceMirror.ReturnClocks658(2)==native.ReturnMembers.Length,"new native manifest and originals complete the retained early subset");
        }
        source+=1;FlightTime655.Now=source;TownServiceMirror.ForgetRemoteMotion(2);Restore();
        Send(Copy(new[]{native.ReturnParts[0]}));Manifest(old,source+.01f);TownServiceMirror.ApplyRemoteMotion(source);
        Check(TownServiceMirror.Cohorts658(2)==0,"strictly newer census proves native member withdrawal and retires its incomplete return");
        Send(Copy(native.ReturnParts.Skip(1).ToArray()));TownServiceMirror.ApplyRemoteMotion(source);
        Check(TownServiceMirror.ReturnClocks658(2)==0 && TownServiceMirror.Cohorts658(2)==0,"late subset cannot resurrect a newer native census withdrawal");
        source+=1;FlightTime655.Now=source;TownServiceMirror.ForgetRemoteMotion(2);Restore();Send(Copy(native.ReturnParts));TownServiceMirror.ApplyRemoteMotion(source);
        var hidden=new TownServiceMotionEntry{Kind=1,Lane=native.Lane,Service=native.Service,Session=native.Session,Module=native.Module,Structure=native.Structure,ParentModule=TownServiceFrame.ManifestModule,
            Visible=false,ParentAlpha=0f,Pose=new[]{0f,1f,0f,0f,0f,0f,1f,1f,1f,1f}};Send(hidden);TownServiceMirror.ApplyRemoteMotion(source);
        Check(TownServiceMirror.Cohorts658(2)==0 && TownServiceMirror.ReturnClocks658(2)==0 && !Remote(stockPeer,native.Module)!.Root.gameObject.activeInHierarchy,
            "actual newer source-visible-false root immediately hides and retires the entire return");
        TownServiceMirror.ForgetRemoteMotion(2);Restore();Send(Copy(new[]{native.ReturnParts[0]}));TownServiceMirror.RemovePeer(2);
        Check(TownServiceMirror.Cohorts658(2)==0 && TownServiceMirror.ReturnClocks658(2)==0 && Remote(stockPeer,native.Module)==null,
            "peer retirement releases incomplete native cohort and all originals");
    }
    private static IEnumerator SlowClock658()
    {
        foreach(int ownerCadence in new[]{6,11,1,0})foreach(int curve in new[]{2,1,3})
        {
            Transform shared=Go("Actual slow native source frame").transform;
            Transform original=Go("Actual slow native original",shared).transform;original.localPosition=new Vector3(.1f,1f,.2f);
            Transform painted=Go("Actual slow native receiver",shared).transform;
            VRCard? ability=null;NativeMerchant655? item=null;
            if(curve==2){ability=original.gameObject.AddComponent<VRCard>();ability.BeginNative655(new Vector3(.38f,1.1f,.1f),.55f);}
            else {item=original.gameObject.AddComponent<NativeMerchant655>();if(curve==3)item.BeginCollapse658(new Vector3(.4f,1.1f,.1f));}
            TownServiceMirror.ClockProbe658? clock=null;int sourceFrames=0,lastOwner=0;float maxReceiptError=0;int transientReceipts=0,settledReceipts=0;
            var csv=new System.Text.StringBuilder("render,ownerFrames,sourceAge,renderedAge,receiptError\n");
            for(int frame=1;frame<=100;frame++)
            {
                float now=100f+frame/90f;
                bool update=ownerCadence==1?(frame<=10 || frame>=19):ownerCadence==0 || frame%ownerCadence==0;
                if(update)
                {
                    FlightTime655.Delta=ownerCadence==0 && frame>=11 && frame<=19?0f:(frame-lastOwner)/90f;lastOwner=frame;
                    if(curve==2)ability!.StepNative655();else if(curve==1)item!.StepNative655();else item!.StepCollapse658();
                    uint revision;float[] numbers;
                    bool active=curve==2?ability!.CaptureNativeTownReturn655(original,shared,null,out revision,out numbers):item!.TryTownReturnMotion(original,shared,null,out revision,out numbers);
                    if(!active)break;
                    var entry=new TownServiceMotionEntry{Kind=8,Lane=2,Service=1,Session=658,Module=1,Structure=1,Revision=revision,Numbers=numbers};
                    float before=float.NegativeInfinity;
                    if(clock!=null){var prior=clock.Current(now,out float ageBefore);before=prior.Numbers[2]==1f?-prior.Numbers[1]+ageBefore:ageBefore;}
                    if(clock==null)clock=new(entry,now);else clock.Observe(entry,now);
                    var current=clock.Current(now,out float age);float after=current.Numbers[2]==1f?-current.Numbers[1]+age:age;
                    Check(after+1e-5f>=before,"clamped native age never rewinds at an exact receipt cadence="+ownerCadence+" curve="+curve);
                    TownCardReturnMotion.Apply(painted,shared,shared,0,current.Numbers,age);
                    float error=Vector3.Distance(painted.position,original.position);sourceFrames++;
                    csv.AppendLine(frame+","+sourceFrames+","+numbers[0]+","+age+","+error);
                    File.WriteAllText(Path.Combine(_output,"native-clock658-"+ownerCadence+"-"+curve+".csv"),csv.ToString());
                    float authored=curve==1?-numbers[1]:numbers[0];
                    bool settled=after-authored<.00002f;
                    if(sourceFrames>=4)
                    {
                        maxReceiptError=Mathf.Max(maxReceiptError,error);
                        if(ownerCadence>=6 || settled)
                        { Check(error<.0001f,"actual native step pose at settled authored phase cadence="+ownerCadence+" curve="+curve+" frame="+frame+" error="+error);settledReceipts++; }
                        else
                        {
                            // A newly observed hitch/pause can contradict an already
                            // painted prediction. Preserve that phase briefly; exact
                            // source steps catch up without changing native duration.
                            Check(after-authored<=.0612f,"unforeseeable owner hitch has a bounded authored-phase lead");transientReceipts++;
                            Check(frame<27,"held prediction catches the actual native source within eight resumed frames");
                        }
                    }
                }
                else if(clock!=null){var entry=clock.Current(now,out float age);TownCardReturnMotion.Apply(painted,shared,shared,0,entry.Numbers,age);}
                yield return null;
            }
            Check(sourceFrames>=4 && settledReceipts>0,"real clamped native steps cover complete calibration and eventual actual trajectory");
            if(ownerCadence<=1)Check(transientReceipts>0,"unexpected hitch or pause records its causal transient instead of claiming universal exact parity");
            File.WriteAllText(Path.Combine(_output,"native-clock658-"+ownerCadence+"-"+curve+".csv"),csv.ToString());
        }
    }
    private static IEnumerator Capacity658()
    {
        foreach(int count in new[]{13,14,17,24,40,64})
        {
            var live=new List<TownServiceMotionPending>();
            Transform shared=Go("Exact capacity source frame").transform;
            Transform physical=Go("Exact capacity physical root",shared).transform;
            Matrix4x4 from=physical.localToWorldMatrix;
            Matrix4x4 to=Matrix4x4.TRS(new Vector3(.2f,.1f,.4f),Quaternion.Euler(10f,50f,30f),Vector3.one*.2f);
            var random=new System.Random(658);
            for(int i=0;i<count;i++)
            {
                Transform child=Go("Exact native return part "+i,physical).transform;
                child.localPosition=new Vector3((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble());
                child.localRotation=Quaternion.Euler((float)random.NextDouble()*50f,(float)random.NextDouble()*30f,(float)random.NextDouble()*90f);
                child.localScale=new Vector3((float)random.NextDouble()+.1f,(float)random.NextDouble()+.1f,(float)random.NextDouble()+.1f);
                float[] values=TownCardReturnMotion.Capture(child,physical,shared,null,.07f,.55f,2,0f,from,Quaternion.identity,to,Quaternion.Euler(10f,50f,30f),Vector3.up*.08f);
                var root=new TownServiceMotionEntry{Kind=1,Lane=2,Service=1,Session=658,Module=(ushort)(i+1),Structure=(uint)(658+i),Visible=true,ParentAlpha=1f,
                    Pose=new[]{child.position.x,child.position.y,child.position.z,child.rotation.x,child.rotation.y,child.rotation.z,child.rotation.w,child.lossyScale.x,child.lossyScale.y,child.lossyScale.z}};
                live.Add(new TownServiceMotionPending{Entry=root});
                live.Add(new TownServiceMotionPending{Entry=new TownServiceMotionEntry{Kind=8,Lane=2,Service=1,Session=658,Module=(ushort)(i+1),Structure=root.Structure,Revision=12,Numbers=values}});
            }
            int a=0,b=0,c=0;var admitted=new HashSet<int>();int events=0;
            while(admitted.Count<count && events<12)
            {
                var packet=new TownServiceMotionPacket{Sequence=(ulong)(events+1),SampleTime=100f+events/15f};
                byte[] encoded=TownServiceMotionBudget.FillPacked(packet,live,new List<TownServiceMotionPending>(),new List<TownServiceMotionPending>(),ref a,ref b,ref c,packet.SampleTime);
                Check(encoded.Length>0 && encoded.Length<=864,"bounded native cohort makes finite progress for count="+count);
                Check(TownServiceMotionCodec.TryRead(encoded,encoded.Length,out var parsed),"capacity packet decodes exact bounded fragments");
                foreach(var entry in parsed!.Entries)if(entry.Kind==10)
                {
                    Check(entry.ReturnSampleTime==100f,"oversized native cohort retains its original source instant across later packet clocks");
                    foreach(var part in entry.ReturnParts)admitted.Add(part.Index);
                }
                foreach(var source in live)if(source.Entry.Kind==8)source.Entry.Numbers[14]+=.017f;
                File.AppendAllText(Path.Combine(_output,"capacity658.txt"),count+" parts event "+events+" => "+encoded.Length+" bytes; complete="+admitted.Count+"\n");events++;
            }
            Check(admitted.Count==count && events<=8,"entire oversized native cohort completes without source/module trimming count="+count);
        }
        // Keep this return alive and dirty for longer than the short native
        // lifetime to prove fairness while admission competes, rather than relying
        // on eventual source removal to give fan/ordinary modules a turn.
        MixedCapacity658();
        yield break;
    }
    private static void MixedCapacity658()
    {
        var random=new System.Random(658113);
        float Next()=>.01f+(float)random.NextDouble();
        float[] Pose()
        { Quaternion q=Quaternion.Euler(Next()*170,Next()*120,Next()*90);return new[]{Next(),Next(),Next(),q.x,q.y,q.z,q.w,Next(),Next(),Next()}; }
        var groups=new[]{new List<TownServiceMotionPending>(),new List<TownServiceMotionPending>(),new List<TownServiceMotionPending>()};
        for(int group=0;group<3;group++)for(int index=0;index<40;index++)
        {
            ushort id=(ushort)(200+group*40+index);
            groups[group].Add(new TownServiceMotionPending{Entry=new TownServiceMotionEntry{Kind=1,Lane=2,Service=1,Session=658,Module=id,Structure=1,
                Visible=true,ParentAlpha=Mathf.Min(.999f,Next()),Hand=(byte)(group==0?1:group==1?3:0),Pose=Pose(),HasCanvasUpdate=true,HasCanvasFrame=true,CanvasOnHand=group!=2,
                CanvasPose=Pose(),CanvasRect=new[]{Next()*400,Next()*300,Next(),Next()},CanvasSettings=new[]{Next()*200,Next(),0f,1f,0f}}});
        }
        for(int index=0;index<17;index++)
        {
            ushort id=(ushort)(index+1);var child=Pose();
            float[] values=new float[38];values[0]=.07f;values[1]=.35f;values[2]=2f;values[10]=values[13]=values[20]=values[23]=1f;
            values[11]=values[12]=values[21]=values[22]=1f;values[14]=.4f;values[15]=1f;values[25]=.08f;Array.Copy(child,0,values,28,10);
            groups[0].Add(new TownServiceMotionPending{Entry=new TownServiceMotionEntry{Kind=1,Lane=2,Service=1,Session=658,Module=id,Structure=1,Visible=true,ParentAlpha=1f,Pose=child}});
            groups[0].Add(new TownServiceMotionPending{Entry=new TownServiceMotionEntry{Kind=8,Lane=2,Service=1,Session=658,Module=id,Structure=1,Revision=12,Numbers=values}});
        }
        int a=0,b=0,c=0;var all=new HashSet<ushort>();var cohort=new HashSet<byte>();float first=float.NaN;int complete=0;
        for(int tick=0;tick<150;tick++)
        {
            float now=100f+tick/15f;
            foreach(var group in groups)foreach(var slot in group){slot.Dirty=true;if(slot.Entry.Kind==8)slot.Entry.Numbers[14]+=.001f;}
            var packet=new TownServiceMotionPacket{Sequence=(ulong)(tick+1),SampleTime=now};
            byte[] bytes=TownServiceMotionBudget.FillPacked(packet,groups[0],groups[1],groups[2],ref a,ref b,ref c,now);
            Check(bytes.Length>0 && bytes.Length<=864 && TownServiceMotionCodec.TryRead(bytes,bytes.Length,out _),"mixed random motion remains a valid bounded event");
            foreach(var entry in packet.Entries)
                if(entry.Kind==10)
                { if(float.IsNaN(first))first=entry.ReturnSampleTime; if(entry.ReturnSampleTime==first)foreach(var part in entry.ReturnParts)cohort.Add(part.Index); }
                else if(entry.Module>=200)all.Add(entry.Module);
            if(cohort.Count==17 && complete==0)complete=tick+1;
            if(tick==4)Check(complete>0,"all large-cohort children complete by .267s within actual remaining .35-.07=.28s native lifetime");
        }
        Check(all.Count==120,"all continuously contending live/fan/ordinary originals progress while the return remains active");
        File.WriteAllText(Path.Combine(_output,"mixed-capacity658.txt"),"17 exact random children; first complete events="+complete+"; all120 finite progress during150 continuously dirty return ticks\n");
    }
    private static IEnumerator Flight658(bool prepared)
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
        Transform owner=Go("Flight658 source shared frame").transform;
        Transform observer=Go("Flight658 observer shared frame").transform;
        observer.position=Vector3.right*8f;
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        var faces=new Transform[3]; var bodies=new Transform[3]; var natives=new VRCard[3];
        bool moving=false;
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,658,owner,owner);
            for(int i=0;i<3;i++)
            {
                Transform card=Go("Physical native card "+i,owner).transform;
                card.localPosition=new Vector3(0f,1f+(i-1)*.32f,0f);
                natives[i]=card.gameObject.AddComponent<VRCard>();
                faces[i]=Source(card); faces[i].localPosition=new Vector3(0f,0f,-.0012f);
                faces[i].localRotation=Quaternion.identity; faces[i].localScale=Vector3.one*.0005f;
                // This is the declared physical-body mesh boundary, with the
                // native item-card thickness and exact captured material channel.
                GameObject body=GameObject.CreatePrimitive(PrimitiveType.Cube); Objects.Add(body);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.name="Original card body "+i; body.transform.SetParent(card,false);
                body.transform.localPosition=new Vector3(0f,0f,.0012f);
                body.transform.localScale=new Vector3(.195f,.125f,.0022f); bodies[i]=body.transform;
                Material original=new Material(Shader.Find("Unlit/Color")){color=new Color(.32f,.29f,.25f,1f)};
                Assets.Add(original); body.GetComponent<MeshRenderer>().sharedMaterial=original;
                TownServiceMirror.Assets.Register("flight658/body-material-"+i,original);
                TownServiceMirror.Assets.Register("flight658/body-mesh-"+i,body.GetComponent<MeshFilter>().sharedMesh);
                int slot=i;
                bool Sample(Transform source,Transform frame,VRHand? hand,out uint revision,out float[] values)
                {
                    if(moving)return natives[slot].CaptureNativeTownReturn655(source,frame,hand,out revision,out values);
                    revision=0;values=Array.Empty<float>();return false;
                }
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+1),bodies[i],address:"map.cardbody|"+i);
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+4),faces[i],address:"face."+(6580+i)+"|");
                // Body modules precede print modules, reproducing the actual
                // independently published originals rather than an interleaved toy.
                if(prepared)
                { TownServiceMirror.PrepareCardReturn(bodies[i],Sample);TownServiceMirror.PrepareCardReturn(faces[i],Sample); }
                else
                { TownServiceMirror.RegisterCardReturn(bodies[i],Sample);TownServiceMirror.RegisterCardReturn(faces[i],Sample); }
            }
        }
        using(TownServiceMirror.UseStockLane())
        {
            for(int i=0;i<3;i++)TownServiceMirror.RegisterModule((ushort)(10+i),(ushort)(i+1),bodies[i],address:"map.cardbody|"+i);
            for(int i=0;i<3;i++)TownServiceMirror.RegisterModule((ushort)(13+i),(ushort)(i+4),faces[i],address:"face."+(6580+i)+"|");
        }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture baseline=CaptureFast();Receive(2,baseline.Artwork);DeliverMotion(2,baseline);
        TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;
        int peer=keys[2];
        for(int i=0;i<3;i++)
            Check(Remote(peer,(ushort)(10+i))!=null && Remote(peer,(ushort)(13+i))!=null,"exact front/body baselines exist before the native return");
        moving=true;
        for(int i=0;i<3;i++)natives[i].BeginNative655(owner.TransformPoint(new Vector3(.28f,1f+(i-1)*.32f,0f)),.55f);
        var trace=new System.Text.StringBuilder("frame,card,sourceAge,bodyDistance,faceDistance,coherence,frontVisible,bodyVisible\n");
        for(int frame=1;frame<=36;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
            for(int i=0;i<3;i++)natives[i].StepNative655();
            if(frame%7==0)
            {
                // The real fast stream shares this event with changing native
                // hover/material numbers. Incompressible numeric property input
                // exercises actual compression trimming, not a fake packet split.
                var random=new System.Random(frame);
                foreach(Transform print in faces)
                    foreach(var graphic in print.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        graphic.color=new Color((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble(),1f);
                // Numeric publication must open prepared originals without a
                // slower immutable-artwork/repair receipt authoring a second pose.
                if(prepared)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
                {Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"actual numeric packet decodes");TownServiceMirror.ReceiveMotion(2,packet!);});
                else DeliverMotion(2,CaptureFast());
            }
            TownServiceMirror.TickRemote(_=>observer);
            if(frame<7){yield return null;continue;}
            for(int i=0;i<3;i++)
            {
                Transform body=Remote(peer,(ushort)(10+i))!.Root,face=Remote(peer,(ushort)(13+i))!.Root;
                float bodyDistance=Vector3.Distance(body.position,observer.TransformPoint(owner.InverseTransformPoint(bodies[i].position)));
                float faceDistance=Vector3.Distance(face.position,observer.TransformPoint(owner.InverseTransformPoint(faces[i].position)));
                Vector3 expectedOffset=body.rotation * (Vector3.forward * (.0024f * body.lossyScale.x/.195f));
                float coherence=Vector3.Distance(body.position-face.position,expectedOffset);
                trace.AppendLine(frame+","+i+","+((frame-7)/90f)+","+bodyDistance+","+faceDistance+","+coherence+","+face.gameObject.activeInHierarchy+","+body.gameObject.activeInHierarchy);
                if(prepared && i==0 && frame==7)
                    Check(face.gameObject.activeInHierarchy && body.gameObject.activeInHierarchy,"prepared original opens on the first exact native flight");
                Check(coherence<.00005f && face.gameObject.activeInHierarchy==body.gameObject.activeInHierarchy,
                    "each moving original retains its printed front on the same first render card="+i+" frame="+frame+" error="+coherence);
            }
            if(frame is 8 or 15 or 22 or 29)
            {
                // Compare the whole physical original after moving the source back
                // to the same delayed owner instant. World-pose coherence above is
                // evaluated before this screenshot and cannot be hidden by cameras.
                Color32[] picture=FlightPixels658(observer,9,"flight658-observer-"+frame);
                Check(FlightInk658(picture)>1200,"combined original flight renders visible native print and body pixels");
            }
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output,"flight658-coherence.csv"),trace.ToString());
        TownServiceMirror.Shutdown();
    }
    private static int FlightInk658(Color32[] pixels)
    {int count=0;foreach(Color32 pixel in pixels)if(pixel.r>40 && pixel.g>40 && pixel.b>40)count++;return count;}
    private static Color32[] FlightPixels658(Transform shared,int layer,string name)
    {
        foreach(GameObject fixture in Objects)if(fixture!=null)Layer(fixture.transform,30);
        Layer(shared,layer);
        foreach(Canvas canvas in shared.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<layer;_camera.transform.SetPositionAndRotation(shared.position+new Vector3(0f,1f,-5f),Quaternion.identity);
        _camera.orthographicSize=.65f;Canvas.ForceUpdateCanvases();
        var rt=new RenderTexture(512,384,24,RenderTextureFormat.ARGB32);var image=new Texture2D(512,384,TextureFormat.RGBA32,false);
        try
        {_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,512,384),0,0);image.Apply();
         File.WriteAllBytes(Path.Combine(_output,name+".png"),image.EncodeToPNG());return image.GetPixels32();}
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
}
