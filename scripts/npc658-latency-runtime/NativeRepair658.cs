using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MirrorProgram
{
    private static void FillRepairQueues658(ExtrasSendScheduler scheduler)
    {
        foreach(string field in new[]{"_presence","_animation","_plumes","_board","_appearance","_prompt","_itemAppearance","_mapTooltip"})
        {
            var queue=(ExtrasSendQueue)typeof(ExtrasSendScheduler).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scheduler)!;
            int limit=(int)typeof(ExtrasSendQueue).GetField("_snapshotLimit",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
            byte payload=(byte)typeof(ExtrasSendQueue).GetField("_payloadType",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
            var bytes=new byte[limit];new System.Random(658).NextBytes(bytes);
            bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=payload;queue.Enqueue(bytes,bytes.Length);
        }
    }
    private static IEnumerator NativeRepair658()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer=10;
        Transform owner=Go("658 full repair owner").transform, observer=Go("658 full repair observer").transform;
        owner.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        Transform source=Source(owner);
        for(int i=0;i<8;i++)Image("Original native decoration "+i,source,new Vector2(i,-i),new Vector2(20,30),Color.white).sprite=_fill.sprite;
        Transform face=Image("Native card material",owner,Vector2.zero,new Vector2(140,220),Color.yellow).transform;
        Canvas.ForceUpdateCanvases();
        const string ui="enchant.inventory|", card="face.658|";
        TownServiceMirror.RegisterTemplate(3,1,source,address:ui);
        TownServiceMirror.RegisterTemplate(3,2,face,address:card);
        TownServiceMirror.PrepareNativeTemplateBasis(3,ui);TownServiceMirror.PrepareNativeTemplateBasis(3,card);
        TownServiceMirror.BeginSession(3,658,owner,owner);
        TownServiceMirror.RegisterModule(1,1,source,address:ui);TownServiceMirror.RegisterModule(2,2,face,address:card);
        TownServiceMirror.SetPriority(1,true);TownServiceMirror.SetPriority(2,true);
        TownServiceMirror.SetLocalTransactionActive(3,true);
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        TownServiceMirror.CollectOriginalReceiptPeers=peers=>{peers.Clear();peers.Add(NetPlayerActors.Peer==2?10:2);};
        var type=typeof(TownServiceMirror);
        var modules=(IDictionary)type.GetProperty("Local",PrivateStatic)!.GetValue(null)!;
        object module=modules[(ushort)1]!;
        object? Repair(object value)=>value.GetType().GetField("NativeRepair",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value);
        float After(object value)=>(float)Repair(value)!.GetType().GetField("After",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Repair(value))!;
        TownServiceFrame? Retained(object value)=>(TownServiceFrame?)Repair(value)!.GetType().GetField("Original",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Repair(value));
        var scheduler=new ExtrasSendScheduler(0,3,4);var fragments=new TownServiceFragments();
        var emitted=new List<(TownServiceFrame Source,TownServiceFrame Wire,double At,float Unscaled)>();
        var complete=new Dictionary<ushort,double>();var completeAfter=new Dictionary<ushort,float>();var timer=System.Diagnostics.Stopwatch.StartNew();
        Action<TownServiceFrame>? actualCompletion=TownServiceDelivery.Completed;
        TownServiceDelivery.Completed=frame=>{actualCompletion?.Invoke(frame);if(frame.Module<3&&!complete.ContainsKey(frame.Module)){complete[frame.Module]=timer.Elapsed.TotalSeconds;if(frame.Module==1)completeAfter[1]=After(module);}};
        void Capture()
        {
            NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
            try{type.GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[]{(Action<byte[],int,object?>)((bytes,length,identity)=>{
                if(identity is not TownServiceFrame original)return;
                Check(TownServiceCodec.TryRead(bytes,length,out var wire),"actual repair publication uses original codec");
                emitted.Add((original,wire!,timer.Elapsed.TotalSeconds,Time.unscaledTime));scheduler.Enqueue(bytes,length,identity:original);
            }),false});}
            finally{SetNativeSenderActive629(false);NetPlayerActors.Peer=10;}
        }
        void Acknowledge()
        {
            NetPlayerActors.Peer=10;
            type.GetMethod("CaptureOriginalReceipts",PrivateStatic)!.Invoke(null,new object[]{(Action<byte[],int>)((bytes,length)=>{
                NetPlayerActors.Peer=2;
                Check((bool)type.GetMethod("ReceiveOriginalReceipt",PrivateStatic)!.Invoke(null,new object[]{10,bytes,length})!,"actual exact original receipt accepts retained fallback identity");
                NetPlayerActors.Peer=10;
            })});
        }
        try
        {
            Capture();
            File.WriteAllText(Path.Combine(_output,"native-repair658-capture.txt"),"nodes="+source.GetComponentsInChildren<Transform>(true).Length+"\n"+string.Join("\n",emitted.Select(x=>"module="+x.Source.Module+" bytes="+TownServiceCodec.Write(x.Wire).Length))+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
            var original=emitted.Single(x=>x.Source.Module==1);
            Check(original.Wire.NativeTemplateBasisKey!=0&&ReferenceEquals(Retained(module),original.Source),
                "actual owner retains its full exact original behind the compact native packet");
            var templates=(Dictionary<string,GameObject>)type.GetField("Templates",PrivateStatic)!.GetValue(null)!;
            string Key(ushort template,string address)=>(string)type.GetMethod("TemplateKey",PrivateStatic)!.Invoke(null,new object[]{(byte)3,template,address})!;
            var native=templates[Key(1,ui)].transform;
            ((RectTransform)native.Find("Filled")).sizeDelta+=new Vector2(37,19);
            var nativeFace=templates[Key(2,card)].transform.GetComponent<Image>();
            var differingMaterial=new Material(nativeFace.material){color=Color.red};Assets.Add(differingMaterial);nativeFace.material=differingMaterial;
            type.GetMethod("ResetNativeTemplateState",PrivateStatic)!.Invoke(null,null);
            Check(!TownServiceMirror.TryExpandNativeTemplateState(original.Wire,out _),"numeric mismatch rejects an omitted native basis before paint");
            Check(!TownServiceMirror.TryExpandNativeTemplateState(emitted.Single(x=>x.Source.Module==2).Wire,out _),"root material mismatch rejects an omitted native basis before paint");
            NetPlayerActors.Peer=2;
            var wrong=TownServiceOriginalReceiptCodec.Write(2,3,658,0,new[]{new TownServiceOriginalReceiptEntry(1,original.Source.Sequence+100)});
            Check(TownServiceMirror.ReceiveOriginalReceipt(10,wrong,wrong.Length),"stale exact-source receipt is harmless");NetPlayerActors.Peer=10;
            double nextCapture=0;int pages=0,received=0;bool changed=false,fullSeen=false;
            while(timer.Elapsed.TotalSeconds<3&&(!fullSeen||Remote(2,1)==null
                ||changed&&Remote(2,1)!.Root.Find("Name").GetComponent<TMP_Text>().text!=_text.text))
            {
                if(timer.Elapsed.TotalSeconds>=nextCapture){nextCapture=timer.Elapsed.TotalSeconds+1f/15f;Capture();}
                FillRepairQueues658(scheduler);byte[]? batch=scheduler.NextBatch(timer.Elapsed.TotalSeconds);
                if(batch!=null)
                {
                    pages++;Check(batch.Length<=PresentationBatch.MaxSize,"fallback retains864-byte global event budget");
                    foreach(var page in PresentationBatch.TryRead(batch,batch.Length,out var pp)?pp!:new[]{batch})
                    {
                        if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                        var packet=fragments.Accept(2,page,page.Length,timer.Elapsed.TotalSeconds);if(packet==null)continue;
                        foreach(var child in TownServiceCodec.TryReadBundle(packet,packet.Length,out var cc)?cc!:new[]{packet})
                        {
                            Check(TownServiceCodec.TryRead(child,child.Length,out var decoded),"completed actual original bundle decodes");
                            if(decoded!.Module<3){received++;if(decoded.BaseSequence==0&&decoded.NativeTemplateBasisKey==0)fullSeen=true;}
                            Check(TownServiceMirror.ReceiveParsed(2,decoded),"actual full/sparse/delta repair enters ordered admission");
                        }
                    }
                }
                if(!complete.ContainsKey(1))Check(float.IsPositiveInfinity(After(module)),"first fragment never arms the original repair grace");
                if(!fullSeen)
                {
                    Check(Remote(2,1)==null&&Remote(2,2)==null,"mismatched basis cannot paint a partial original picture");
                    Acknowledge();
                    NetPlayerActors.Peer=2;
                    Check(!(bool)type.GetMethod("HasReceivedOriginal",PrivateStatic)!.Invoke(null,new[]{module})!,"unexpanded sparse originals cannot acknowledge a full owner dependency");NetPlayerActors.Peer=10;
                }
                if(complete.ContainsKey(1)&&!changed)
                {
                    _text.text="Current owner delta over retained original";_fill.color=Color.cyan;changed=true;Capture();
                }
                TownServiceMirror.TickRemote(_=>observer);yield return null;
            }
            var fallback=emitted.FirstOrDefault(x=>x.Source.Module==1&&x.Wire.NativeTemplateBasisKey==0&&x.Wire.BaseSequence==0);
            Check(fallback.Source!=null&&fullSeen&&Remote(2,1)!=null&&Remote(2,2)!=null,
                "mismatched originals receive their retained full source without periodic repair debt");
            Check(ReferenceEquals(fallback.Source,original.Source)&&fallback.Wire.Sequence==original.Wire.Sequence,
                "full fallback retains the exact immutable source, sequence and delta dependency");
            Check(fallback.Unscaled>=completeAfter[1]-.001f && completeAfter[1]>0f,
                "full fallback starts after actual compressed bundle completion and its150ms grace");
            Check(emitted.Count(x=>x.Source.Module==1&&x.Wire.NativeTemplateBasisKey==0&&x.Wire.BaseSequence==0)==1,
                "missing receipt produces one retained full original, never repeated full spam");
            var retained=(Dictionary<int,Dictionary<ushort,TownServiceFrame>>)type.GetField("ReceivedBaselines",PrivateStatic)!.GetValue(null)!;
            AssertNativeEqual623(original.Source,retained[2][1]);
            Acknowledge();NetPlayerActors.Peer=2;
            Check((bool)type.GetMethod("HasReceivedOriginal",PrivateStatic)!.Invoke(null,new[]{module})!,"only the exact real retained baseline completes original receipt");NetPlayerActors.Peer=10;
            for(float until=Time.unscaledTime+.4f;Time.unscaledTime<until;){TownServiceMirror.TickRemote(_=>observer);yield return null;}
            Check(Remote(2,1)!.Root.Find("Name").GetComponent<TMP_Text>().text==_text.text,
                "the waiting cumulative owner delta expands over the late exact full fallback");
            Check(Remote(2,1)!.Root.Find("Filled").GetComponent<Image>().color==Color.cyan,
                "changed visible owner numeric output survives the retained full repair");
            var changedFaceMaterial=new Material(face.GetComponent<Image>().material){color=Color.green};Assets.Add(changedFaceMaterial);face.GetComponent<Image>().material=changedFaceMaterial;
            var changedFace=NativeFrame623(face,2,2,card);
            Check(TownServiceMirror.TryWriteNativeTemplateState(changedFace,out var changedBytes)&&TownServiceCodec.TryRead(changedBytes,changedBytes.Length,out var changedSparse)
                &&TownServiceMirror.TryExpandNativeTemplateState(changedSparse!,out var expanded),"changed owner root material overrides a different exact native default");
            Check(TownServiceCodec.TryRead(changedBytes,changedBytes.Length,out var rootSparse)&&TownServiceMirror.TryExpandNativeTemplateState(rootSparse!,out var rootFull),"changed material expansion succeeds independently of an old packet");
            TownServiceMirror.TryExpandNativeTemplateState(rootSparse!,out rootFull);AssertNativeEqual623(changedFace,rootFull);
            Transform added=Image("658 topology inactive FX",source,Vector2.zero,new Vector2(17,19),Color.magenta).transform;
            added.GetComponent<Image>().sprite=_fill.sprite;added.gameObject.SetActive(false);
            source.localPosition=new Vector3(7,8,9);source.localRotation=Quaternion.Euler(10,20,30);
            int beforeTopology=emitted.Count;Capture();
            TownServiceFrame replacement=emitted.Skip(beforeTopology).First(x=>x.Source.Module==1).Source;
            var binding=(TownServiceBinding)module.GetType().GetField("Binding",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(module)!;
            int addedIndex=Array.IndexOf(binding.Nodes,added);
            Check(replacement.BaseSequence==0 && replacement.Nodes[addedIndex].Values.ContainsKey(TownServiceProperty.Graphic)
                && replacement.Nodes[addedIndex].Values.ContainsKey(TownServiceProperty.Image),
                "first topology replacement captures inactive original graphics");
            Check(replacement.Nodes[0].Values[TownServiceProperty.Transform].Numbers.Take(10).SequenceEqual(new float[]{0,0,0,0,0,0,1,1,1,1}),
                "first topology replacement uses the canonical full source root TRS");
            TownServiceMirror.UnregisterModule(1);Capture();
            Check(!modules.Contains((ushort)1),"retired source module releases its retained full repair lifetime");
            File.WriteAllText(Path.Combine(_output,"native-repair658.txt"),"Actual CaptureCore->bounded saturated scheduler->lossless pooled/compressed fragments->ReceiveParsed->TickRemote.\nseconds="+timer.Elapsed.TotalSeconds+" pages="+pages+" received="+received+" fullBytes="+TownServiceCodec.Write(original.Source).Length+" sparseBytes="+TownServiceCodec.Write(original.Wire).Length+"\n"+string.Join("\n",emitted.Where(x=>x.Source.Module<3).Select(x=>"module="+x.Source.Module+" sequence="+x.Source.Sequence+" baseline="+x.Source.BaseSequence+" basis="+x.Wire.NativeTemplateBasisKey+" published="+x.At))+"\n");
            IEnumerator dependency=NativeQueuedRepair658(original.Source,original.Wire,observer);
            while(dependency.MoveNext())yield return dependency.Current;
        }
        finally{TownServiceDelivery.Completed=actualCompletion;TownServiceMirror.Shutdown();}
    }

    private static TownServiceFrame ArtworkRevision658(TownServiceFrame original,ulong sequence)
    {
        TownServiceFrame current=TownServiceDelta.Copy(original);current.Sequence=sequence;current.HighPriority=true;
        TownServiceValue text=current.Nodes.First(x=>x.Values.ContainsKey(TownServiceProperty.TmpText)).Values[TownServiceProperty.TmpText];
        var bytes=new byte[4000];new System.Random((int)sequence).NextBytes(bytes);text.Text[0]=Convert.ToBase64String(bytes);
        TownServiceFrame delta=TownServiceDelta.Create(original,current);delta.HighPriority=true;return delta;
    }

    private static List<TownServiceFrame> QueuedOriginals658(ExtrasSendQueue queue)
    {
        var waiting=(IList)typeof(ExtrasSendQueue).GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
        return waiting.Cast<object>().Select(x=>(TownServiceFrame)x.GetType().GetField("Identity",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(x)!).ToList();
    }

    private static IEnumerator NativeQueuedRepair658(TownServiceFrame actualFull,TownServiceFrame actualSparse,Transform observer)
    {
        TownServiceMirror.ResetNetwork();
        var full=TownServiceDelta.Copy(actualFull);full.Session=659;full.Sequence=100;full.HighPriority=true;
        var sparse=TownServiceDelta.Copy(actualSparse);sparse.Session=659;sparse.Sequence=100;
        var delta=ArtworkRevision658(full,101);byte[] deltaBytes=TownServiceCodec.Write(delta),fullBytes=TownServiceCodec.Write(full);
        // The primitive preserves already fragmented module pages as well as its
        // bounded pending full+latest3 dependency. The integration below uses the
        // real global scheduler with an older fragmented atomic bundle instead.
        var active=new ExtrasSendQueue(1,TownServiceCodec.MessageType,TownServiceCodec.FragmentType,preserveFirst:true,snapshotLimit:TownServiceFrame.MaxBytes);
        active.Enqueue(deltaBytes,deltaBytes.Length,delta);active.Next(0);
        Check(active.HasInFlight,"continuous repair challenge has real older active fragment pages");
        active.PrependTownOriginal(fullBytes,full,preserveInFlight:true);
        for(ulong seq=102;seq<=108;seq++){var next=ArtworkRevision658(full,seq);var bytes=TownServiceCodec.Write(next);active.Enqueue(bytes,bytes.Length,next);}
        var preserved=QueuedOriginals658(active);
        Check(active.HasInFlight&&preserved.Count==4&&ReferenceEquals(preserved[0],full)
            &&preserved.Skip(1).Select(x=>x.Sequence).SequenceEqual(new ulong[]{106,107,108}),
            "older active fragments retain exactly full plus latest3 dependent artwork revisions");
        var replaced=TownServiceDelta.Copy(full);replaced.Sequence=200;replaced.HighPriority=true;
        active.SupersedeTownOriginal(replaced);
        Check(!active.HasInFlight&&QueuedOriginals658(active).Count==0,
            "a genuinely newer full source replaces older repair and dependent artwork");

        var scheduler=new ExtrasSendScheduler(0,3,4);var fragments=new TownServiceFragments();
        var received=new List<TownServiceFrame>();var completed=new List<TownServiceFrame>();
        var timer=System.Diagnostics.Stopwatch.StartNew();int events=0;
        Action<TownServiceFrame>? originalCompleted=TownServiceDelivery.Completed;
        TownServiceDelivery.Completed=frame=>{originalCompleted?.Invoke(frame);completed.Add(frame);};
        void Enqueue(TownServiceFrame frame,TownServiceFrame identity){byte[] bytes=TownServiceCodec.Write(frame);scheduler.Enqueue(bytes,bytes.Length,identity:identity);}
        void Turn()
        {
            FillRepairQueues658(scheduler);byte[]? batch=scheduler.NextBatch(timer.Elapsed.TotalSeconds);if(batch==null)return;
            events++;Check(batch.Length<=PresentationBatch.MaxSize,"continuous repair retains global864-byte event bound");
            foreach(byte[] page in PresentationBatch.TryRead(batch,batch.Length,out var pages)?pages!:new[]{batch})
            {
                if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                byte[]? packet=fragments.Accept(2,page,page.Length,timer.Elapsed.TotalSeconds);if(packet==null)continue;
                foreach(byte[] child in TownServiceCodec.TryReadBundle(packet,packet.Length,out var children)?children!:new[]{packet})
                {Check(TownServiceCodec.TryRead(child,child.Length,out var frame),"continuous repair decodes actual completed transport");received.Add(frame!);Check(TownServiceMirror.ReceiveParsed(2,frame!),"continuous repair uses actual ordered admission");}
            }
            TownServiceMirror.TickRemote(_=>observer);
        }
        try
        {
            InteractionManifest(2,3,659,1,modules:new ushort[]{1,2});
            Enqueue(sparse,full);
            while(timer.Elapsed.TotalSeconds<2&&!completed.Any(x=>ReferenceEquals(x,full))){Turn();yield return null;}
            Check(completed.Any(x=>ReferenceEquals(x,full))&&Remote(2,1)==null,
                "early compact completion cannot display or acknowledge a still incomplete native picture");
            Enqueue(delta,delta);
            var town=(TownServiceSendQueue)typeof(ExtrasSendScheduler).GetField("_town",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scheduler)!;
            var lane=(TownServiceLaneSendQueue)typeof(TownServiceSendQueue).GetField("_private",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(town)!;
            var bundle=(ExtrasSendQueue)typeof(TownServiceLaneSendQueue).GetField("_urgentBundle",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(lane)!;
            while(timer.Elapsed.TotalSeconds<2&&!bundle.HasInFlight){Turn();yield return null;}
            Check(bundle.HasInFlight,"older cumulative artwork is actually fragmented before full repair queues");
            for(ulong seq=102;seq<=105;seq++){var next=ArtworkRevision658(full,seq);Enqueue(next,next);}
            Enqueue(full,full);
            for(ulong seq=106;seq<=110;seq++){var next=ArtworkRevision658(full,seq);Enqueue(next,next);}
            var queues=(IDictionary)typeof(TownServiceLaneSendQueue).GetField("_queues",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(lane)!;
            var pending=QueuedOriginals658((ExtrasSendQueue)queues[(ushort)1]!);
            Check(pending.Count==4&&ReferenceEquals(pending[0],full),
                "actual retained full repair survives continuous same-baseline coalescing");
            var other=TownServiceDelta.Copy(full);other.Module=2;other.Sequence=500;other.HighPriority=true;Enqueue(other,other);
            bool fullArrived=false;ulong seqNext=111;double nextChange=timer.Elapsed.TotalSeconds;
            while(timer.Elapsed.TotalSeconds<5&&(!fullArrived||Remote(2,1)==null||Remote(2,2)==null))
            {
                if(timer.Elapsed.TotalSeconds>=nextChange)
                {nextChange=timer.Elapsed.TotalSeconds+1f/15f;var next=ArtworkRevision658(full,seqNext++);Enqueue(next,next);}
                Turn();fullArrived=received.Any(x=>x.Module==1&&x.BaseSequence==0&&x.NativeTemplateBasisKey==0);yield return null;
            }
            Check(fullArrived&&Remote(2,1)!=null&&Remote(2,2)!=null,
                "retained full dependency completes a multi-module native picture under continuous artwork");
            Check(received.Count(x=>x.Module==1&&x.BaseSequence==0&&x.NativeTemplateBasisKey==0)==1,
                "early completion with no receipt never repeats full originals ahead of other required modules");
            Check(completed.FindIndex(x=>x.Module==1&&x.Sequence==101)
                <completed.FindLastIndex(x=>ReferenceEquals(x,full)),
                "older fragmented artwork completes before the queued exact full dependency callback");
            var baselines=(Dictionary<int,Dictionary<ushort,TownServiceFrame>>)typeof(TownServiceMirror).GetField("ReceivedBaselines",PrivateStatic)!.GetValue(null)!;
            AssertNativeEqual623(full,baselines[2][1]);
            var newest=TownServiceDelta.Copy(full);newest.Sequence=1000;newest.HighPriority=true;Enqueue(newest,newest);
            Check(QueuedOriginals658((ExtrasSendQueue)queues[(ushort)1]!).All(x=>x.Sequence>=1000),
                "genuine source replacement takes precedence over queued repair revisions");
            TownServiceDelivery.Retire(false,false,3,659,1);lane.RetireSources(false,false);
            Check(!queues.Contains((ushort)1),"source removal retires queued exact repair and its dependencies");
            File.WriteAllText(Path.Combine(_output,"native-queued-repair658.txt"),
                "Actual saturated global scheduler, early compact completion, older fragmented artwork, full+latest3 dependency, continued15Hz changes.\n"
                +"seconds="+timer.Elapsed.TotalSeconds+" events="+events+" revisions="+(seqNext-101)+" fulls=1\n"
                +string.Join("\n",completed.Select(x=>"module="+x.Module+" seq="+x.Sequence+" base="+x.BaseSequence))+"\n");
        }
        finally{TownServiceDelivery.Completed=originalCompleted;}
    }
}
