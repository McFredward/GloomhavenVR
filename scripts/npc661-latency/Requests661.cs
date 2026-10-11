using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator OriginalRequests661()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer=10;
        Transform owner=Go("661 original request owner").transform;
        Transform observer=Go("661 exact observer").transform;
        owner.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        var sources=new List<Transform>();
        for(ushort id=1;id<=4;id++)
        {
            Transform source=Source(owner); sources.Add(source);
            if(id==2)
            {
                // Legitimate native text makes this one original genuinely span
                // multiple compressed module pages; this is a queue challenge,
                // not a hardware/card-density performance input.
                var content661=new byte[1024];new System.Random(661).NextBytes(content661);
                source.Find("Name").GetComponent<TMPro.TMP_Text>().text=Convert.ToBase64String(content661);
            }
            TownServiceMirror.RegisterTemplate(3,id,source,address:"enchant.inventory."+id+"|");
            TownServiceMirror.PrepareNativeTemplateBasis(3,"enchant.inventory."+id+"|");
        }
        TownServiceMirror.BeginSession(3,661,owner,owner);
        for(ushort id=1;id<=4;id++)
        { TownServiceMirror.RegisterModule(id,id,sources[id-1],address:"enchant.inventory."+id+"|");TownServiceMirror.SetPriority(id,id==1); }
        TownServiceMirror.SetLocalTransactionActive(3,true);
        var emitted=new List<(TownServiceFrame Full,TownServiceFrame Wire)>();
        void Publish(byte[] bytes,int length,object? identity)
        {
            if(identity is not TownServiceFrame original||original.Module==TownServiceFrame.ManifestModule)return;
            Check(TownServiceCodec.TryRead(bytes,length,out var wire),"request proof decodes actual compact/full publication");
            emitted.Add((original,wire!));
        }
        var ownerDriver=new NetAvatarDriver();
        ownerDriver.FixtureTransport661.Published=Publish;
        NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
        TownServiceMirror.Capture((bytes,length,identity)=>
        {
            if(identity is TownServiceFrame frame)
                Check(ownerDriver.FixtureTransport661.TrySendTownPresentation(bytes,length,frame),"initial originals use actual FFS enqueue before a real rejection request");
        });NetPlayerActors.Peer=10;SetNativeSenderActive629(false);
        var compact=emitted.ToArray();
        Check(compact.Length==4&&compact.All(x=>x.Wire.NativeTemplateBasisKey!=0),"four actual immutable sources use native compact originals");
        Check(ownerDriver.FixtureTransport661.RequestedMarkerCount661==0,"ordinary compact originals never invent a known rejection marker");
        var flight661=new TownServiceLaneSendQueue(0);
        byte[] initialFlight661=TownServiceCodec.Write(compact[1].Wire);
        flight661.Enqueue(initialFlight661,initialFlight661.Length,compact[1].Full);
        var flightQueues661=(IDictionary)typeof(TownServiceLaneSendQueue).GetField("_queues",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(flight661)!;
        var oldModule661=(ExtrasSendQueue)flightQueues661[(ushort)2]!;
        byte[]? oldFirstPage661=oldModule661.Next(0);
        Check(oldFirstPage661!=null&&oldModule661.HasInFlight,"real compact native original begins an unfinished module stream before its rejection repair");
        // Declare the existing active-module cursor, as the lane does after an
        // ordinary module stream starts. No source, payload or marker is faked.
        typeof(TownServiceLaneSendQueue).GetField("_normalActive",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(flight661,(ushort)2);
        TownServiceFrame afterFlight661=TownServiceDelta.Copy(compact[1].Full);afterFlight661.Sequence+=100;afterFlight661.Pose[0]+=.025f;
        TownServiceFrame flightDelta661=TownServiceDelta.Create(compact[1].Full,afterFlight661);
        byte[] flightDeltaBytes661=TownServiceCodec.Write(flightDelta661);
        flight661.Enqueue(flightDeltaBytes661,flightDeltaBytes661.Length,flightDelta661);
        var type=typeof(TownServiceMirror);
        var modules=(IDictionary)type.GetProperty("Local",PrivateStatic)!.GetValue(null)!;
        object? Repair(ushort id)
        { object module=modules[id]!;return module.GetType().GetField("NativeRepair",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(module); }
        float After(ushort id)=>(float)Repair(id)!.GetType().GetField("After",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Repair(id))!;
        var templates=(Dictionary<string,GameObject>)type.GetField("Templates",PrivateStatic)!.GetValue(null)!;
        foreach(var entry in compact)
        {
            string key=(string)type.GetMethod("TemplateKey",PrivateStatic)!.Invoke(null,new object[]{(byte)3,entry.Full.Template,entry.Full.TemplateAddress})!;
            ((RectTransform)templates[key].transform.Find("Filled")).sizeDelta+=new Vector2(37,19);
        }
        type.GetMethod("ResetNativeTemplateState",PrivateStatic)!.Invoke(null,null);
        foreach(var entry in compact)
        { Check(float.IsPositiveInfinity(After(entry.Full.Module)),"local fragments cannot start a receipt or repair grace");
          Check(!TownServiceMirror.TryExpandNativeTemplateState(entry.Wire,out _)
              &&TownServiceMirror.ReceiveParsed(2,entry.Wire),"genuinely different omitted native geometry is retained unexpanded before painting"); }

        var observerDriver=new NetAvatarDriver();
        observerDriver.FixtureTransport661.Installed=false;
        observerDriver.FixtureSend661();
        Check(observerDriver.FixtureTransport661.Errors.Count==1,"actual driver reports declined request admission without losing pending originals");
        observerDriver.FixtureTransport661.Installed=true;
        observerDriver.FixtureSend661();
        byte[] request=observerDriver.FixtureTransport661.TakeMetadata661();
        Check(TownServiceOriginalRequestCodec.TryRead(request,request.Length,out var decoded)
            &&decoded!.Entries.Length==4&&decoded.OriginPeer==2&&decoded.Session==661&&decoded.Lane==0,
            "actual driver and bounded metadata scheduler publish each exact rejected original");
        observerDriver.FixtureSend661();
        Check(observerDriver.FixtureTransport661.TakeMetadata661(false).Length==0,"one admitted reliable request never becomes repeated full or request spam");
        NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
        foreach(var wrong in new[]{
            TownServiceOriginalRequestCodec.Write(3,3,661,0,new[]{new TownServiceOriginalReceiptEntry(1,compact[0].Full.Sequence)}),
            TownServiceOriginalRequestCodec.Write(2,3,662,0,new[]{new TownServiceOriginalReceiptEntry(1,compact[0].Full.Sequence)}),
            TownServiceOriginalRequestCodec.Write(2,3,661,0,new[]{new TownServiceOriginalReceiptEntry(1,compact[0].Full.Sequence+1)})})
            Check(TownServiceMirror.ReceiveOriginalRequest(10,wrong,wrong.Length),"wrong-origin/session/sequence request is harmless");
        TownServiceMirror.CaptureRequestedOriginalRepairs(Publish);
        Check(emitted.Count==4,"unrelated request cannot accelerate or encode any owner original");
        Check(!TownServiceMirror.ReceiveOriginalRequest(11,request,request.Length),"unhandshaken sender cannot request owner originals");
        Check(TownServiceMirror.ReceiveOriginalRequest(10,request,request.Length),"actual compatible observer request marks only its current exact original");
        TownServiceMirror.UnregisterModule(4);
        TownServiceFrame future661=TownServiceDelta.Copy(compact[0].Full);
        future661.Sequence+=100;future661.Pose[0]+=.015f;
        TownServiceFrame dependent661=TownServiceDelta.Create(compact[0].Full,future661);
        byte[] dependentBytes661=TownServiceCodec.Write(dependent661);
        Check(ownerDriver.FixtureTransport661.TrySendTownPresentation(dependentBytes661,dependentBytes661.Length,dependent661),
            "actual queue retains a newer artwork revision naming the requested original baseline");
        const BindingFlags queueFlags661=BindingFlags.Instance|BindingFlags.NonPublic;
        object ffsScheduler661=typeof(FfsNetTransport).GetField("_extrasQueue",queueFlags661)!.GetValue(ownerDriver.FixtureTransport661)!;
        object ffsTown661=typeof(ExtrasSendScheduler).GetField("_town",queueFlags661)!.GetValue(ffsScheduler661)!;
        object ffsLane661=typeof(TownServiceSendQueue).GetField("_private",queueFlags661)!.GetValue(ffsTown661)!;
        var queueTrace661=new System.Text.StringBuilder();
        void QueueState661(string phase)
        {
            queueTrace661.AppendLine(phase);
            foreach(string field in new[]{"_normalActive","_priorityActive","_priorityCursor","_cursor","_priorityTurns"})
                queueTrace661.AppendLine(field+"="+typeof(TownServiceLaneSendQueue).GetField(field,queueFlags661)!.GetValue(ffsLane661));
            var priorities=(IEnumerable)typeof(TownServiceLaneSendQueue).GetField("_priority",queueFlags661)!.GetValue(ffsLane661)!;
            queueTrace661.Append("priority=");foreach(object id in priorities)queueTrace661.Append(id+",");queueTrace661.AppendLine();
            var queues=(IDictionary)typeof(TownServiceLaneSendQueue).GetField("_queues",queueFlags661)!.GetValue(ffsLane661)!;
            foreach(DictionaryEntry entry in queues)
            {
                var queue=(ExtrasSendQueue)entry.Value;queueTrace661.Append(entry.Key+" pending=");
                var pending=(IEnumerable)typeof(ExtrasSendQueue).GetField("_pending",queueFlags661)!.GetValue(queue)!;
                foreach(object item in pending)
                { var frame= item.GetType().GetField("Identity",queueFlags661)!.GetValue(item) as TownServiceFrame;
                  queueTrace661.Append(frame==null?"null,":frame.Sequence+"/"+frame.BaseSequence+","); }
                queueTrace661.AppendLine(" active="+queue.HasInFlight);
            }
            File.WriteAllText(Path.Combine(_output,"queue-state661.txt"),queueTrace661.ToString());
        }
        QueueState661("after dependent enqueue");
        int originalCount661=emitted.Count;
        int repairAttempts661=0;
        ownerDriver.FixtureTransport661.AdmissionFailure661=frame=>
        {
            repairAttempts661++;
            if(frame.Module<3)throw new InvalidOperationException("declared SDK admission failure for exact requested source "+frame.Module);
        };
        int before=emitted.Count;
        ownerDriver.FixtureTransport661.Published=(bytes,length,identity)=>
        {
            Publish(bytes,length,identity);
            if(identity is TownServiceFrame frame&&frame.Module==2)flight661.Enqueue(bytes,length,frame);
        };
        ownerDriver.FixtureSend661();
        int firstPass=repairAttempts661;
        Check(firstPass>=1&&firstPass<=2,"expired pre-encode CPU budget still performs at least one requested repair");
        ownerDriver.FixtureSend661();ownerDriver.FixtureSend661();
        Check(emitted.Skip(before).Any(entry=>entry.Full.Module==3),
            "persistently failing first two originals cannot starve the next valid requested source during retry cooldown");
        ownerDriver.FixtureTransport661.AdmissionFailure661=null;
        float cooled661=Time.unscaledTime+.27f;
        while(Time.unscaledTime<cooled661)yield return null;
        ownerDriver.FixtureSend661();ownerDriver.FixtureSend661();ownerDriver.FixtureSend661();
        var replies=emitted.Skip(originalCount661).ToArray();
        Check(replies.Length==3&&replies.All(x=>x.Wire.NativeTemplateBasisKey==0&&x.Wire.BaseSequence==0),
            "per-render driver repairs all three live sources before the blocked 15Hz capture interval");
        foreach(var reply in replies)
        {
            var exact=compact.Single(x=>x.Full.Module==reply.Full.Module);
            Check(ReferenceEquals(reply.Full,exact.Full)&&reply.Wire.Sequence==exact.Wire.Sequence,
                "requested full repair keeps the exact immutable owner object and original sequence");
            AssertNativeEqual623(exact.Full,reply.Wire);
        }
        Check(!replies.Any(x=>x.Full.Module==4),"withdrawn original cannot be revived by an earlier reliable request");
        ownerDriver.FixtureSend661();Check(emitted.Count==originalCount661+3,"completed requested repairs do not repeat between capture ticks");
        Check(ownerDriver.FixtureTransport661.RequestedMarkerCount661==3,
            "only three actually accepted live private originals mark their existing module queues");
        var ordinary661=new TownServiceLaneSendQueue(0);
        byte[] ordinaryBytes661=TownServiceCodec.Write(compact[0].Full);
        ordinary661.Enqueue(ordinaryBytes661,ordinaryBytes661.Length,compact[0].Full);
        var markerField661=typeof(TownServiceLaneSendQueue).GetField("_requestedOriginals",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Check(((IDictionary)markerField661.GetValue(ordinary661)!).Count==0,
            "already repaired ordinary high-priority originals cannot acquire a stale accepted-request marker");
        var fragments661=new TownServiceFragments();var queued661=new List<TownServiceFrame>();int empty661=0;
        QueueState661("before drain");
        for(int turn661=0;turn661<256&&empty661<8;turn661++)
        {
            double clock661=turn661*.05;
            byte[]? batch661=ownerDriver.FixtureTransport661.NextOriginalBatch661(clock661);
            QueueState661("turn "+turn661+" batch="+(batch661?.Length??0));
            if(batch661==null){empty661++;continue;}empty661=0;
            Check(batch661.Length<=PresentationBatch.MaxSize,"known repair queue preserves the global864-byte event cap");
            foreach(byte[] page661 in PresentationBatch.TryRead(batch661,batch661.Length,out var pages661)?pages661!:new[]{batch661})
            {
                if(TownServiceFragments.Stream(page661,page661.Length)<0)continue;
                byte[]? packet661=fragments661.Accept(2,page661,page661.Length,clock661);if(packet661==null)continue;
                foreach(byte[] child661 in TownServiceCodec.TryReadBundle(packet661,packet661.Length,out var children661)?children661!:new[]{packet661})
                { Check(TownServiceCodec.TryRead(child661,child661.Length,out var frame661),"actual known repair queue drains only valid original records");queued661.Add(frame661!); }
            }
        }
        Check(empty661==8&&ownerDriver.FixtureTransport661.RequestedMarkerCount661==0,
            "finite known repairs drain their markers and return to an idle bounded queue");
        foreach(var reply661 in replies)
            Check(queued661.Count(frame=>frame.Module==reply661.Full.Module&&frame.Sequence==reply661.Full.Sequence
                &&frame.BaseSequence==0&&frame.NativeTemplateBasisKey==0)==1,
                "each exact requested full source completes once without a second speculative bundle copy");
        File.WriteAllText(Path.Combine(_output,"queue661.txt"),string.Join("\n",queued661.Select(frame=>"module="+frame.Module+" sequence="+frame.Sequence+" base="+frame.BaseSequence+" native="+frame.NativeTemplateBasisKey)));
        Check(queued661.Any(frame=>frame.Module==dependent661.Module&&frame.Sequence==dependent661.Sequence
            &&frame.BaseSequence==compact[0].Full.Sequence),"prior queued dependent revision survives the requested full repair");
        var flightFragments661=new TownServiceFragments();var flightFrames661=new List<TownServiceFrame>();
        Check(flightFragments661.Accept(2,oldFirstPage661!,oldFirstPage661!.Length,0)==null,
            "first compact fragment cannot manufacture a complete repair identity");
        int flightIdle661=0;
        for(int turn661=1;turn661<128&&flightIdle661<8;turn661++)
        {
            double clock661=turn661*.05;
            byte[]? page661=flight661.NextUrgent(clock661)??flight661.Next(clock661);
            if(page661==null){flightIdle661++;continue;}flightIdle661=0;
            Check(page661.Length<=PresentationBatch.MaxSize,"old compact and requested full preserve the same864-byte page cap");
            byte[]? packet661=flightFragments661.Accept(2,page661,page661.Length,clock661);if(packet661==null)continue;
            foreach(byte[] child661 in TownServiceCodec.TryReadBundle(packet661,packet661.Length,out var children661)?children661!:new[]{packet661})
            {
                Check(TownServiceCodec.TryRead(child661,child661.Length,out var frame661),"old compact/full/dependent queue drains exact valid native records");flightFrames661.Add(frame661!);
                if(frame661!.NativeTemplateBasisKey!=0)
                    Check(((IDictionary)markerField661.GetValue(flight661)!).Count==1,
                        "old compact completion leaves the requested matching full source marked at its pending head");
            }
        }
        Check(flightIdle661==8&&((IDictionary)markerField661.GetValue(flight661)!).Count==0,
            "ordinary known repair remains marked through old compact completion and then quiesces after its real full source");
        Check(flightFrames661.Count(frame=>frame.NativeTemplateBasisKey!=0)==1
            &&flightFrames661.Count(frame=>frame.BaseSequence==0&&frame.NativeTemplateBasisKey==0)==1
            &&flightFrames661.Any(frame=>frame.Sequence==flightDelta661.Sequence&&frame.BaseSequence==compact[1].Full.Sequence),
            "old compact flight, exact full repair and its newer dependent delta all finish once in the retained module stream");

        NetPlayerActors.Peer=10;SetNativeSenderActive629(false);
        foreach(var reply in replies)
        {
            byte[] sparse=TownServiceCodec.Write(compact.Single(x=>x.Full.Module==reply.Full.Module).Wire);
            byte[] full=TownServiceCodec.Write(reply.Wire);
            Check(observerDriver.FixtureQueue638(2,sparse)&&observerDriver.FixtureQueue638(2,full),
                "actual main-thread coalescer accepts a compact and its same-sequence full repair together");
        }
        observerDriver.FixtureApply638();
        TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>
        {
            NetPlayerActors.Peer=2;
            Check(TownServiceMirror.ReceiveOriginalReceipt(10,bytes,length),"only actually retained full originals produce genuine receipts");
            NetPlayerActors.Peer=10;
        });
        var retained=(Dictionary<int,Dictionary<ushort,TownServiceFrame>>)type.GetField("ReceivedBaselines",PrivateStatic)!.GetValue(null)!;
        foreach(var reply in replies)AssertNativeEqual623(reply.Full,retained[2][reply.Full.Module]);
        NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
        Check(TownServiceMirror.ReceiveOriginalRequest(10,request,request.Length),"late reliable request after full original receipt is harmless");
        int beforeAck661=emitted.Count;
        ownerDriver.FixtureSend661();Check(emitted.Count==beforeAck661,"fresh real original receipts cannot trigger another full repair");
        TownServiceMirror.BeginSession(3,662,owner,owner);
        TownServiceMirror.RegisterModule(1,1,sources[0],address:compact[0].Full.TemplateAddress);
        TownServiceMirror.Capture(Publish);
        var closing=emitted.Last(x=>x.Full.Module==1);
        Check(closing.Wire.NativeTemplateBasisKey!=0,"new exact source is genuinely compact before an accepted request closes");
        byte[] closeRequest=TownServiceOriginalRequestCodec.Write(2,3,662,0,
            new[]{new TownServiceOriginalReceiptEntry(1,closing.Full.Sequence)});
        Check(TownServiceMirror.ReceiveOriginalRequest(10,closeRequest,closeRequest.Length),"compatible request is accepted while its exact source is still active");
        int beforeClose=emitted.Count;
        TownServiceMirror.EndSession();ownerDriver.FixtureSend661();
        Check(emitted.Count==beforeClose,"request accepted before native EndSession cannot enqueue a withdrawn original on the next render");
        TownServiceMirror.BeginSession(3,663,owner,owner);
        Check(TownServiceMirror.ReceiveOriginalRequest(10,closeRequest,closeRequest.Length),"delayed old-session request after reopening is harmless");
        ownerDriver.FixtureSend661();Check(emitted.Count==beforeClose,"reopened session cannot reuse its predecessor requested original");
        NetPlayerActors.Peer=10;SetNativeSenderActive629(false);
        var currentCensus661=new TownServiceFrame { Service=3,Session=670,Module=TownServiceFrame.ManifestModule,
            Sequence=5000,Visible=true,TransactionActive=true,Modules=new ushort[]{1,2,3},RequiredVisibleModules=new ushort[]{1,2,3} };
        Check(TownServiceMirror.ReceiveParsed(2,currentCensus661),"actual current census precedes a new refused-original batch");
        foreach(var entry661 in compact.Take(3))
        { var current661=TownServiceDelta.Copy(entry661.Wire);current661.Session=670;current661.Sequence=4999;
          Check(TownServiceMirror.ReceiveParsed(2,current661),"new current native mismatch is retained for exact original recovery"); }
        Check(TownServiceMirror.ReceiveParsed(2,compact[0].Wire),"late old-session rejected original is harmless before expansion admission");
        byte[]? currentRequest661=null;
        TownServiceMirror.CaptureOriginalRequests((bytes,length)=>currentRequest661=bytes);
        Check(currentRequest661!=null&&TownServiceOriginalRequestCodec.TryRead(currentRequest661,currentRequest661.Length,out var current661Request)
            &&current661Request!.Session==670&&current661Request.Entries.Length==3&&current661Request.Entries.All(entry=>entry.Sequence==4999),
            "late rejected old-session compact cannot erase the current complete request batch");
        var futureSource661=TownServiceDelta.Copy(compact[0].Wire);futureSource661.Session=671;futureSource661.Sequence=5002;
        Check(TownServiceMirror.ReceiveParsed(2,futureSource661),"genuinely newer refused source is retained even before its future census");
        var currentLate661=TownServiceDelta.Copy(compact[0].Wire);currentLate661.Session=670;currentLate661.Sequence=4999;
        Check(TownServiceMirror.ReceiveParsed(2,currentLate661),"late current-census member cannot overwrite a newer uncensused source");
        var oldOtherModule661=TownServiceDelta.Copy(compact[1].Wire);oldOtherModule661.Session=670;oldOtherModule661.Sequence=5001;
        Check(TownServiceMirror.ReceiveParsed(2,oldOtherModule661),"different old-session module may arrive after its census and before the future census");
        byte[]? futureRequest661=null;TownServiceMirror.CaptureOriginalRequests((bytes,length)=>futureRequest661=bytes);
        Check(futureRequest661!=null&&TownServiceOriginalRequestCodec.TryRead(futureRequest661,futureRequest661.Length,out var future661Request)
            &&future661Request!.Session==671&&future661Request.Entries.Length==1&&future661Request.Entries[0].Sequence==5002,
            "future-census source identity survives a late older module with the same peer/lane/module key");
        TownServiceMirror.RemovePeer(2);TownServiceMirror.ResetOriginalReceipts();
        NetPlayerActors.Peer=10;SetNativeSenderActive629(false);
        File.WriteAllText(Path.Combine(_output,"requests661.txt"),
            "Actual compact capture -> basis refusal -> actual driver request -> actual FFS enqueue -> bounded reliable metadata scheduler -> exact immutable per-render full repair -> same-sequence real avatar queue -> retained original receipt.\n"
            +"SDK online/installed/compatible-peer state and side-action delivery are declared ports; gameplay grant/face work outside this original path is omitted.\n"
            +"firstPass="+firstPass+" repairs="+replies.Length+" requestBytes="+request.Length+"\n");
        TownServiceMirror.Shutdown();yield break;
    }
}
