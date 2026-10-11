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
    // Actual source samplers, original bodies and the engine renderer are bound;
    // catalogue/game rules are boundary inputs. No observer transform is corrected.
    private static IEnumerator NativeFlight658()
    {
        // This exercises a quiet immutable header through the terminal native
        // frame. Only actual numeric capture/codec/playback moves the observer.
        foreach (bool merchant in new[] { false, true })
        foreach (int delay in new[] { 0, 6 })
        {
            TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
            Transform owner=Go("Return660 owner shared").transform, observer=Go("Return660 observer shared").transform;
            observer.position=Vector3.right*8f; TownServiceMirror.SharedFrameForRemote=_=>observer;
            Transform card=Go("Native660 card",owner).transform; card.localPosition=new Vector3(.4f,1.2f,.1f);
            card.localRotation=Quaternion.Euler(15f,-21f,8f);
            VRCard? ability=merchant?null:card.gameObject.AddComponent<VRCard>();
            NativeMerchant655? item=merchant?card.gameObject.AddComponent<NativeMerchant655>():null;
            ItemsPile.ItemChip? actualChip=merchant?card.gameObject.AddComponent<ItemsPile.ItemChip>():null;
            RectTransform faceCanvas=(RectTransform)Go("Actual hosted FaceCanvas",card).transform;
            faceCanvas.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;faceCanvas.sizeDelta=new Vector2(390,250);faceCanvas.localScale=Vector3.one*.0005f;faceCanvas.localPosition=Vector3.forward*-.002f;
            Transform front=Source(merchant?faceCanvas:card), body=TownServiceCardBody.Create(card).transform;
            body.localScale=new Vector3(.195f,.125f,1f);
            using(var assets=new TownServiceBinding(body))assets.Read(TownServiceMirror.Assets);
            if(merchant) Object.DestroyImmediate(front.GetComponent<Canvas>());
            front.localPosition=new Vector3(0f,0f,-.002f); body.localPosition=new Vector3(0f,0f,.002f);
            front.localScale=Vector3.one*.0005f;
            if(merchant) { ((RectTransform)front).pivot=new Vector2(.2f,.8f); front.localScale=Vector3.one; }
            VRHand? hand=null;
            if(merchant)
            {
                hand=new VRHand { Side=HandSide.Left,HasPose=true,WorldScale=1f };
                hand.Rig.Root=Go("Source destination hand",owner).transform;
                hand.Rig.Root.localPosition=new Vector3(.08f,.04f,.03f);
                hand.Rig.GrabAnchor=Go("Source grab anchor",hand.Rig.Root).transform;
                VRHands.Left=hand;VRHands.Right=null;
                Transform remoteHand=Go("Observer destination hand",observer).transform;
                remoteHand.localPosition=hand.Rig.Root.localPosition;
                NetAvatarDriver.MotionHandFrames[2]=new[]{remoteHand,remoteHand};
                card.SetParent(hand.Rig.Root,true);
            }
            item?.Prepare660();
            TownServiceMirror.CardReturnSampler sample=merchant?actualChip!.TryTownReturnMotion:ability!.CaptureNativeTownReturn655;
            Check(sample.Target is VRCard || sample.Target is ItemsPile.ItemChip,"actual return registration targets only the declared native owner types");
            using(TownServiceMirror.UseStockLane())
            {
                TownServiceMirror.BeginSession(1,660,owner,owner);
                TownServiceMirror.RegisterTemplate(1,1,body,address:"map.cardbody|660");
                TownServiceMirror.RegisterTemplate(1,2,front,address:"face.6601|");
                TownServiceMirror.RegisterModule(11,1,body,address:"map.cardbody|660");
                TownServiceMirror.RegisterModule(12,2,front,address:"face.6601|");
                TownServiceMirror.PrepareCardReturn(body,sample,hand);TownServiceMirror.PrepareCardReturn(front,sample,hand);
            }
            Canvas.ForceUpdateCanvases();yield return null;
            FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
            int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
            if(merchant) NativeMerchant655.Canonicalize660(front,faceCanvas);
            item?.Begin660(); ability?.BeginNative655(new Vector3(-.5f,1.1f,.2f),.55f);
            var queued=new List<(int due,byte[] bytes)>();
            var states=new Dictionary<int,(Vector3 body,Vector3 front,Vector3 center)>();
            var trace=new System.Text.StringBuilder("frame,merchant,bodyError,frontError,coherence,visible\n");
            for(int frame=1;frame<=150;frame++)
            {
                FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
                if(merchant)item!.StepNative655();else ability!.StepNative655();
                if(hand!=null && frame>75)
                {
                    hand.Rig.Root.localPosition+=new Vector3(.0005f,.0002f,0f);
                    NetAvatarDriver.MotionHandFrames[2][0].localPosition=hand.Rig.Root.localPosition;
                }
                states[frame]=(body.position,front.position,front.TransformPoint(((RectTransform)front).rect.center));
                if(frame%6==0) TownServiceMirror.CaptureMotion((bytes,length,identity)=>
                { Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"terminal native receipt decodes");
                  queued.Add((frame+delay,(byte[])bytes.Clone())); });
                foreach(var delivery in queued.Where(entry=>entry.due<=frame).ToArray())
                { TownServiceMotionCodec.TryRead(delivery.bytes,delivery.bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);queued.Remove(delivery); }
                TownServiceMirror.TickRemote(_=>observer);
                Transform paintedBody=Remote(peer,11)!.Root,paintedFront=Remote(peer,12)!.Root;
                if(frame>=6+delay)
                {
                    Vector3 liveHandOffset=hand!=null?hand.Rig.Root.position-owner.TransformPoint(new Vector3(.08f,.04f,.03f)):Vector3.zero;
                    Vector3 delayedHandOffset=hand!=null && frame-delay>75?owner.TransformVector(new Vector3(.0005f,.0002f,0f)*(frame-delay-75)):Vector3.zero;
                    Vector3 handCorrection=liveHandOffset-delayedHandOffset;
                    float bodyError=Vector3.Distance(paintedBody.position,observer.TransformPoint(owner.InverseTransformPoint(states[frame-delay].body+handCorrection)));
                    float frontError=Vector3.Distance(paintedFront.position,observer.TransformPoint(owner.InverseTransformPoint(states[frame-delay].front+handCorrection)));
                    float coherence=Vector3.Distance(paintedFront.position,paintedBody.TransformPoint(body.InverseTransformPoint(front.position)));
                    Vector3 sourceCenter=states[frame-delay].center;
                    Vector3 renderedCenter=paintedFront.TransformPoint(((RectTransform)paintedFront).rect.center);
                    float printError=Vector3.Distance(renderedCenter,observer.TransformPoint(owner.InverseTransformPoint(sourceCenter+handCorrection)));
                    Check(bodyError<.00005f && frontError<.00005f && coherence<.00005f,
                        "terminal original roots retain the exact native final picture frame="+frame+" merchant="+merchant+" body="+bodyError+" front="+frontError+" coherence="+coherence);
                    Check(printError<.00005f,"actual pooled face canonicalization keeps its printed center on its native body frame="+frame+" error="+printError);
                    trace.AppendLine(frame+","+merchant+","+bodyError+","+frontError+","+coherence+","+paintedBody.gameObject.activeInHierarchy);
                    if(delay==0)
                    {
                        Color32[] sourcePixels=FlightPixels658(owner,8,"owner660-"+merchant+"-"+frame);
                        Color32[] observerPixels=FlightPixels658(observer,9,"observer660-"+merchant+"-"+frame);
                        int different=0,union=0;
                        for(int p=0;p<sourcePixels.Length;p++)
                        {
                            if(Math.Max(sourcePixels[p].r+sourcePixels[p].g+sourcePixels[p].b,observerPixels[p].r+observerPixels[p].g+observerPixels[p].b)<100)continue;
                            union++; if(Math.Abs(sourcePixels[p].r-observerPixels[p].r)+Math.Abs(sourcePixels[p].g-observerPixels[p].g)+Math.Abs(sourcePixels[p].b-observerPixels[p].b)>60) different++;
                        }
                        Check(union>50 && different<=Math.Max(5,union*.025f),"unadjusted owner and observer body/front pixels agree on every visible render frame="+frame+" merchant="+merchant+" union="+union+" different="+different);
                    }
                    File.WriteAllText(Path.Combine(_output,"terminal660-"+merchant+"-delay"+delay+".csv"),trace.ToString());
                    Check(paintedBody.gameObject.activeInHierarchy && paintedFront.gameObject.activeInHierarchy,"return originals remain visible through native completion");
                }
                else Check(!paintedBody.gameObject.activeInHierarchy && !paintedFront.gameObject.activeInHierarchy,
                    "prepared body and front remain hidden on every render before their first native receipt");
                yield return null;
            }
            TownServiceMirror.Shutdown();
        }
        foreach(bool drop in new[]{false,true})
        { var staged=StagedLayout660(drop);while(staged.MoveNext())yield return staged.Current; }
        var completePrior=StagedLayout660(true,completePrior:true);while(completePrior.MoveNext())yield return completePrior.Current;
        var coldPrior=StagedLayout660(true,completePrior:true,originalsAt:31);while(coldPrior.MoveNext())yield return coldPrior.Current;
        var terminal=TerminalOverlap660();while(terminal.MoveNext())yield return terminal.Current;
        var late64=LateTerminal64_660();while(late64.MoveNext())yield return late64.Current;
        var interrupted=Interruption660();while(interrupted.MoveNext())yield return interrupted.Current;
        ExternalPoseChannels660();
        yield break;
    }
    private static void ExternalPoseChannels660()
    {
        Transform host=Go("External physical pose").transform,root=Go("Physical native root",host).transform;
        Transform child=Go("Independent native child",root).transform;
        CanvasGroup group=host.gameObject.AddComponent<CanvasGroup>();Image graphic=root.gameObject.AddComponent<Image>();
        graphic.color=Color.white;var motion=new TownServiceMotion(host,new[]{root,child});
        motion.BeforeApply(10f);motion.AfterApply(10f,.1f);
        motion.BeforeApply(10.01f);group.alpha=.2f;graphic.color=Color.red;child.localPosition=Vector3.up*.2f;
        motion.AfterApply(10.01f,.1f);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(bool applyTarget in new[]{false,true})
        {
            var before=new Dictionary<string,Array>();
            foreach(string name in new[]{"_from","_to","_nodeStarted","_nodeDuration","_nodeSampleTime"})
                before[name]=(Array)((Array)typeof(TownServiceMotion).GetField(name,flags)!.GetValue(motion)!).Clone();
            root.position+=Vector3.right*.05f;motion.AdoptExternalRootPose(applyTarget);
            foreach(var entry in before)
            {
                Array after=(Array)typeof(TownServiceMotion).GetField(entry.Key,flags)!.GetValue(motion)!;
                for(int i=0;i<after.Length;i++)
                    if(entry.Key=="_from" || entry.Key=="_to")
                    {
                        object oldState=entry.Value.GetValue(i)!,newState=after.GetValue(i)!;
                        foreach(var field in oldState.GetType().GetFields(flags))
                            if(i>=2 || field.Name=="Active" || field.Name=="Alpha" || field.Name=="Color" || field.Name=="Rendered")
                                Check(Equals(field.GetValue(oldState),field.GetValue(newState)),"external root pose adoption preserves every child, alpha, color and visibility state");
                    }
                    else Check(Equals(entry.Value.GetValue(i),after.GetValue(i)),"external root pose adoption never restarts a native child or property clock");
            }
        }
        motion.Tick(10.06f);
        Check(group.alpha>.2f && group.alpha<1f && child.localPosition.y>0f && child.localPosition.y<.2f,
            "independent alpha and native child continue through external pose adoption");
    }
    private static IEnumerator Interruption660()
    {
        foreach(int interruptionFrame in new[]{48,78})
        {
            TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;FlightTime655.Now=100f;
            Transform owner=Go("Interrupted native source").transform,observer=Go("Interrupted native observer").transform;
            observer.position=Vector3.right*8f;TownServiceMirror.SharedFrameForRemote=_=>observer;
            Transform card=Go("Actual interrupted card",owner).transform;card.localPosition=new Vector3(.1f,1f,.03f);
            VRCard native=card.gameObject.AddComponent<VRCard>();Transform front=Source(card);
            VRHand grab=new VRHand { Side=HandSide.Right,HasPose=true,WorldScale=1f };
            grab.Rig.Root=Go("New physical holder",owner).transform;
            grab.Rig.GrabAnchor=Go("New grab anchor",grab.Rig.Root).transform;
            VRHands.Left=null;VRHands.Right=grab;
            using(TownServiceMirror.UseStockLane())
            {
                TownServiceMirror.BeginSession(1,6602,owner,owner);
                TownServiceMirror.RegisterTemplate(1,1,front,address:"face.6603|");
                TownServiceMirror.RegisterModule(1,1,front,address:"face.6603|");
                TownServiceMirror.PrepareCardReturn(front,native.CaptureNativeTownReturn655);
            }
            Canvas.ForceUpdateCanvases();yield return null;
            FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
            native.BeginNative655(new Vector3(.42f,1.1f,.12f),.55f);bool canceledReceipt=false;
            for(int frame=1;frame<=interruptionFrame+6;frame++)
            {
                FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
                if(frame<interruptionFrame)native.StepNative655();
                if(frame==interruptionFrame)
                {
                    native.Cancel660(grab);card.SetParent(grab.Rig.GrabAnchor,true);
                    TownServiceMirror.RegisterMotionHand(card,grab);
                }
                if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
                {
                    Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"interrupted actual native receipt decodes");
                    if(frame>=interruptionFrame)
                    {
                        Check(!packet!.Entries.Any(e=>e.Kind==10 || e.Kind==8),"native grab never publishes a synthetic terminal flight");
                        Check(packet.Entries.Any(e=>e.Kind==1 && e.Module==1 && e.Hand==2),"new holder replaces the settled destination-hand affinity");
                        canceledReceipt=true;
                    }
                    TownServiceMirror.ReceiveMotion(2,packet!);
                });
                TownServiceMirror.TickRemote(_=>observer);
                if(frame>=interruptionFrame)Check(TownServiceMirror.Cohorts658(2)==0,"ordinary native regrab retires the prior cohort on its first receipt");
                yield return null;
            }
            Check(canceledReceipt,"native near-end and post-arrival ownership changes publish their real roots");
        }
        TownServiceMirror.Shutdown();
    }
    private static IEnumerator StagedLayout660(bool drop,bool completePrior=false,int originalsAt=0)
    {
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;FlightTime655.Now=100f;
        Transform owner=Go("Staged layout source").transform,observer=Go("Staged layout observer").transform;
        observer.position=Vector3.right*8f;TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("Native24-part physical source",owner).transform;card.localPosition=new Vector3(.1f,1f,.03f);
        VRCard native=card.gameObject.AddComponent<VRCard>();var parts=new Transform[24];
        var random=new System.Random(660);RectTransform canvas=(RectTransform)Go("Actual repair canvas",card).transform;
        canvas.sizeDelta=new Vector2(400,260);canvas.localScale=Vector3.one*.0005f;
        for(int i=0;i<parts.Length;i++)
        {
            parts[i]=i==0?TownServiceCardBody.Create(card).transform:Go("Native child "+i,card).transform;
            if(i==0){parts[i].localScale=new Vector3(.2f,.13f,1f);using(var b=new TownServiceBinding(parts[i]))b.Read(TownServiceMirror.Assets);}
            else
            {
                var rect=(RectTransform)parts[i];rect.sizeDelta=new Vector2(400,260);rect.pivot=new Vector2(.2f,.8f);
                rect.localPosition=new Vector3((float)random.NextDouble()*.06f,(float)random.NextDouble()*.04f,(float)random.NextDouble()*.03f);
                rect.localRotation=Quaternion.Euler((float)random.NextDouble()*5f,(float)random.NextDouble()*9f,(float)random.NextDouble()*13f);
                rect.localScale=Vector3.one*((float)random.NextDouble()*.0001f+.0004f);
            }
        }
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,6601,owner,owner);
            for(int i=0;i<parts.Length;i++)
            {
                string address=i==0?"map.cardbody|660staged":"face.6602|part"+i;
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.RegisterModule((ushort)(i+1),(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.PrepareCardReturn(parts[i],native.CaptureNativeTownReturn655);
            }
        }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture warm=CaptureFast();
        Receive(2,originalsAt==0?warm.Artwork:warm.Artwork.Where(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var frame)&&frame!.Module==TownServiceFrame.ManifestModule));
        DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
        native.BeginNative655(new Vector3(.42f,1.1f,.12f),.7f);
        Vector3[] oldRelations=parts.Select(part=>parts[0].InverseTransformPoint(part.position)).ToArray();
        Vector3 oldRelation=oldRelations[1];bool partialObserved=false,repaired=false,dropped=false;
        int droppedPackets=0,latePackets=0;
        int overlapAt=0;bool canonicalized=false;float initialSample=-1f,changedSample=-1f;
        byte[]? delayedInitialFinal=null;var initialMembers=new HashSet<byte>();
        var inbound=new List<(int due,byte[] bytes)>();
        for(int frame=1;frame<=84;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;native.StepNative655();
            if(completePrior && delayedInitialFinal!=null && !canonicalized)
            { NativeMerchant655.Canonicalize660(parts[1],canvas);canonicalized=true; }
            if(!completePrior && frame==24)
            {
                Check(TownServiceMirror.ReturnClocks658(2)==parts.Length&&!TownServiceMirror.PendingCohort660(2),
                    "changed rect loss/late dependency starts from a complete actual prior native cohort");
                NativeMerchant655.Canonicalize660(parts[1],canvas);
            }
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
            {
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"staged layout actual bounded receipt decodes");
                if(completePrior)
                {
                    TownServiceMotionEntry? header=packet!.Entries.FirstOrDefault(e=>e.Kind==10);
                    if(header!=null)
                    {
                        if(initialSample<0f)initialSample=header.ReturnSampleTime;
                        if(header.ReturnSampleTime==initialSample)
                        {
                            foreach(var part in header.ReturnParts)initialMembers.Add(part.Index);
                            if(initialMembers.Count==header.ReturnMembers.Length)
                            { delayedInitialFinal=(byte[])bytes.Clone();latePackets++;return; }
                        }
                        else
                        {
                            if(overlapAt==0)
                            {
                                Check(canonicalized && delayedInitialFinal!=null && TownServiceMirror.ReturnClocks658(2)==0,
                                    "actual final original subset waits until the newer changed physical instant");
                                overlapAt=frame;changedSample=header.ReturnSampleTime;
                                if(originalsAt>0)originalsAt=frame+1;
                                // Both receipts are drained before the one render:
                                // prior becomes complete, then latest is partial.
                                inbound.Add((frame,delayedInitialFinal!));
                            }
                            if(!dropped && header.ReturnSampleTime==changedSample
                                && header.ReturnParts.Any(p=>p.Index==header.ReturnMembers.Length-1))
                            { dropped=true;droppedPackets++;return; }
                        }
                    }
                    inbound.Add((frame,(byte[])bytes.Clone()));return;
                }
                if(drop && !dropped && packet!.Entries.Any(e=>e.Kind==10 && e.ReturnSampleTime>=100f+24f/90f
                    && e.ReturnParts.Any(p=>e.ReturnMembers[p.Index]==2))) { dropped=true;droppedPackets++;return; }
                // The previous three-subset schedule delayed every final
                // partition repeatedly, replacing it before a render could
                // activate the prior picture. Bound this fixture's loss instead.
                // This case specifies one changed lost packet and one late packet,
                // after a genuine complete initial cohort, not permanent loss.
                int delay=drop && frame>=24&&frame%18==0 && latePackets==0?12:0;
                if(delay!=0)latePackets++;
                inbound.Add((frame+delay,(byte[])bytes.Clone()));
            });
            foreach(var delivery in inbound.Where(p=>p.due<=frame).ToArray())
            {TownServiceMotionCodec.TryRead(delivery.bytes,delivery.bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);inbound.Remove(delivery);}
            if(frame==originalsAt)Receive(2,warm.Artwork);
            TownServiceMirror.TickRemote(_=>observer);
            if(completePrior && overlapAt!=0 && frame==overlapAt && originalsAt>frame)
                Check(TownServiceMirror.ReturnClocks658(2)==0 && CompletePriorCount660(2)==1,
                    "one complete immutable native prior waits for its exact cold originals");
            if(originalsAt>frame){yield return null;continue;}
            Transform body=Remote(peer,1)!.Root,front=Remote(peer,2)!.Root;
            if(completePrior && overlapAt!=0 && frame==Math.Max(overlapAt,originalsAt))
                Check(TownServiceMirror.ReturnClocks658(2)==parts.Length && TownServiceMirror.PendingCohort660(2)
                    && ((RectTransform)front).pivot==new Vector2(.2f,.8f) && CompletePriorCount660(2)==0,
                    "completed prior native cohort survives a newer partial before one render tick");
            if(completePrior && overlapAt!=0 && frame>=overlapAt)
                for(int i=0;i<parts.Length;i++)
                {
                    Transform rendered=Remote(peer,(ushort)(i+1))!.Root;
                    Check(rendered.gameObject.activeInHierarchy,"complete prior originals never disappear while the latest assembles");
                    Vector3 relation=i==1 && ((RectTransform)front).pivot==new Vector2(.2f,.8f)
                        ?oldRelations[i]:parts[0].InverseTransformPoint(parts[i].position);
                    Check(Vector3.Distance(rendered.position,body.TransformPoint(relation))<.00005f,
                        "retained complete picture and native continuation keep every original coherent frame="+frame+" member="+i);
                }
            if(frame<12){Check(body.gameObject.activeInHierarchy==front.gameObject.activeInHierarchy,"every first staged native partition remains hidden or appears together");}
            if(frame>=24 && !repaired && TownServiceMirror.PendingCohort660(2))
            {
                partialObserved=true;
                Check(((RectTransform)front).pivot==new Vector2(.2f,.8f),"future native rect dependency waits for its complete physical cohort");
                Check(Vector3.Distance(front.position,body.TransformPoint(oldRelation))<.00005f,"incomplete replacement preserves the prior coherent physical geometry");
            }
            if(frame>=24 && !TownServiceMirror.PendingCohort660(2) && ((RectTransform)front).pivot==Vector2.one*.5f)
            {
                repaired=true;
                float repairError=Vector3.Distance(front.position,body.TransformPoint(parts[0].InverseTransformPoint(parts[1].position)));
                Check(repairError<.00005f,"complete native dependency and changed child geometry activate on the same render frame="+frame+" error="+repairError+" remote="+front.position+" source="+parts[1].position);
            }
            yield return null;
        }
        Check(partialObserved && repaired && (!drop || dropped),"actual 24-part budget stages then repairs changed native rect dependency after loss and late receipt");
        Check(drop?droppedPackets==1&&latePackets==1:droppedPackets==0&&latePackets==0,
            "native rect repair has exactly its declared single-loss/single-delay transport contract");
        File.WriteAllText(Path.Combine(_output,"staged-layout660-"+drop+(completePrior?"-complete-prior":"")+(originalsAt>0?"-cold"+originalsAt:"")+".txt"),
            "partialObserved="+partialObserved+" repaired="+repaired+" dropped="+dropped
            +" droppedPackets="+droppedPackets+" latePackets="+latePackets+" overlapAt="+overlapAt+" originalsAt="+originalsAt+"\n");
        TownServiceMirror.Shutdown();
    }
    private static int CompletePriorCount660(int owner)
    {
        var peers=(System.Collections.IDictionary)typeof(TownServiceMirror).GetField("MotionPeers",PrivateStatic)!.GetValue(null)!;
        if(!peers.Contains(owner))return 0;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        object peer=peers[owner]!;var assemblies=(System.Collections.IDictionary)peer.GetType().GetField("ReturnCohorts",flags)!.GetValue(peer)!;
        int count=0;
        foreach(object current in assemblies.Values)
        {
            object? prior=current.GetType().GetField("CompletePrior",flags)?.GetValue(current);
            if(prior==null)continue;
            Check(prior.GetType().GetField("CompletePrior",flags)!.GetValue(prior)==null,"retained physical snapshots never form an unbounded predecessor chain");
            foreach(string field in new[]{"Parts","Roots"})
                foreach(object? value in (Array)prior.GetType().GetField(field,flags)!.GetValue(prior)!)
                    Check(value!=null,"retained native predecessor contains every child and complete root recipe");
            count++;
        }
        return count;
    }
    private static IEnumerator TerminalOverlap660()
    {
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;FlightTime655.Now=100f;
        Transform owner=Go("Terminal overlap native source").transform,observer=Go("Terminal overlap observer").transform;
        observer.position=Vector3.right*8f;TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("Actual terminal-overlap VRCard",owner).transform;card.localPosition=new Vector3(.1f,1f,.03f);
        VRCard native=card.gameObject.AddComponent<VRCard>();var parts=new Transform[24];var random=new System.Random(660);
        RectTransform canvas=(RectTransform)Go("Actual terminal canonicalization canvas",card).transform;
        canvas.sizeDelta=new Vector2(400,260);canvas.localScale=Vector3.one*.0005f;
        for(int i=0;i<parts.Length;i++)
        {
            parts[i]=i==0?TownServiceCardBody.Create(card).transform:Go("Actual terminal original "+i,card).transform;
            if(i==0){parts[i].localScale=new Vector3(.2f,.13f,1f);using(var b=new TownServiceBinding(parts[i]))b.Read(TownServiceMirror.Assets);}
            else
            {
                var rect=(RectTransform)parts[i];rect.sizeDelta=new Vector2(400,260);rect.pivot=new Vector2(.2f,.8f);
                rect.localPosition=new Vector3((float)random.NextDouble()*.06f,(float)random.NextDouble()*.04f,(float)random.NextDouble()*.03f);
                rect.localRotation=Quaternion.Euler((float)random.NextDouble()*5f,(float)random.NextDouble()*9f,(float)random.NextDouble()*13f);
                rect.localScale=Vector3.one*((float)random.NextDouble()*.0001f+.0004f);
            }
        }
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,6603,owner,owner);
            for(int i=0;i<parts.Length;i++)
            {
                string address=i==0?"map.cardbody|660terminal":"face.6604|part"+i;
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.RegisterModule((ushort)(i+1),(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.PrepareCardReturn(parts[i],native.CaptureNativeTownReturn655);
            }
        }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
        Vector3 priorRoot=parts[0].InverseTransformPoint(parts[1].position);
        Vector3 priorCenter=parts[0].InverseTransformPoint(parts[1].TransformPoint(((RectTransform)parts[1]).rect.center));
        Vector3 finalRoot=Vector3.zero,finalCenter=Vector3.zero;bool changed=false,oldOverlap=false,terminalComplete=false,earlyOrdinary=false;
        float changedAt=0f,terminalTime=-1f;var terminalParts=new HashSet<byte>();
        var activeInstants=new HashSet<float>();byte[]? lateActive=null;int delayedSubsets=0;bool sourceAckOverlap=false;
        var trace=new System.Text.StringBuilder("frame,terminalComplete,rootError,centerError,pivotX,pivotY\n");
        void DeliverTerminal(TownServiceMotionPacket packet)
        {
            foreach(TownServiceMotionEntry entry in packet.Entries)
                {
                    if(entry.Kind==10)
                    {
                        if(changed && entry.ReturnSampleTime<changedAt && activeInstants.Contains(entry.ReturnSampleTime))oldOverlap=true;
                        bool exactTerminal=entry.Numbers[0]==entry.Numbers[1];
                        for(int n=0;n<10;n++)exactTerminal &= entry.Numbers[4+n]==entry.Numbers[14+n];
                        if(exactTerminal)
                        {
                            if(terminalTime!=entry.ReturnSampleTime){terminalParts.Clear();terminalTime=entry.ReturnSampleTime;}
                            foreach(var part in entry.ReturnParts)terminalParts.Add(part.Index);
                            terminalComplete=terminalParts.Count==entry.ReturnMembers.Length;
                        }
                    }
                    // Frozen Kind1 canvas recipes now accompany their exact
                    // Kind10 physical subset. They are not ordinary roots;
                    // require the complete same-packet identity and payload.
                    if(changed && entry.Kind==1 && !terminalComplete
                        && !FrozenReturnRoot660(packet,entry))
                        earlyOrdinary=true;
                }
                TownServiceMirror.ReceiveMotion(2,packet);
        }
        for(int frame=1;frame<=108;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
            // Keep the native start phase, duration and 15Hz capture. The
            // explicitly delayed actual preterminal packet below proves overlap
            // independently of the codec's current bounded partition count.
            if(frame==3)native.BeginNative655(new Vector3(.42f,1.1f,.12f),.65f);
            bool before=native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _);
            if(frame>=3)native.StepNative655();
            if(before && !native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _))
            {
                changed=true;changedAt=FlightTime655.Now;NativeMerchant655.Canonicalize660(parts[1],canvas);
                finalRoot=parts[0].InverseTransformPoint(parts[1].position);
                finalCenter=parts[0].InverseTransformPoint(parts[1].TransformPoint(((RectTransform)parts[1]).rect.center));
            }
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
            {
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"terminal-overlap actual bounded receipt decodes");
                foreach(TownServiceMotionEntry entry in packet!.Entries.Where(e=>e.Kind==10))
                {
                    if(before&&!changed&&entry.ReturnSampleTime==packet.SampleTime)activeInstants.Add(entry.ReturnSampleTime);
                    if(changed&&entry.ReturnSampleTime<changedAt)sourceAckOverlap=true;
                    // Delay exactly one genuinely fresh native preterminal subset.
                    // Its delivery crosses the actual terminal edge regardless of
                    // whether the bounded snapshot has two, three or more parts.
                    if(!changed&&before&&delayedSubsets==0&&entry.ReturnSampleTime==packet.SampleTime
                        &&entry.Numbers[0]>=entry.Numbers[1]-TownServiceMotionCodec.SendInterval*1.5f)
                    {lateActive=(byte[])bytes.Clone();delayedSubsets++;return;}
                }
                DeliverTerminal(packet);
            });
            if(changed&&lateActive!=null&&FlightTime655.Now>changedAt)
            {TownServiceMotionCodec.TryRead(lateActive,lateActive.Length,out var delayed);DeliverTerminal(delayed!);lateActive=null;}
            TownServiceMirror.TickRemote(_=>observer);
            Transform body=Remote(peer,1)!.Root,front=Remote(peer,2)!.Root;
            Check(body.gameObject.activeInHierarchy==front.gameObject.activeInHierarchy,"terminal-overlap originals retain visibility together on every frame");
            if(body.gameObject.activeInHierarchy)
            {
                Vector3 relation=terminalComplete?finalRoot:priorRoot,center=terminalComplete?finalCenter:priorCenter;
                float rootError=Vector3.Distance(front.position,body.TransformPoint(relation));
                float centerError=Vector3.Distance(front.TransformPoint(((RectTransform)front).rect.center),body.TransformPoint(center));
                Check(rootError<.00005f && centerError<.00005f,"terminal overlap keeps one native physical picture through exact terminal acknowledgement frame="+frame+" root="+rootError+" center="+centerError);
                Check(((RectTransform)front).pivot==(terminalComplete?Vector2.one*.5f:new Vector2(.2f,.8f)),"terminal rect dependency activates only with its exact final child geometry");
                if(terminalComplete)
                    Check(Vector3.Distance(body.position,observer.TransformPoint(owner.InverseTransformPoint(parts[0].position)))<.00005f,
                        "completed terminal-overlap body arrives at the actual native endpoint");
                trace.AppendLine(frame+","+terminalComplete+","+rootError+","+centerError+","+((RectTransform)front).pivot.x+","+((RectTransform)front).pivot.y);
            }
            Check(!earlyOrdinary,"terminal overlap never substitutes ordinary roots for an unacknowledged final native cohort");
            File.WriteAllText(Path.Combine(_output,"terminal-overlap660.csv"),trace.ToString());
            yield return null;
        }
        Check(delayedSubsets==1&&lateActive==null&&activeInstants.Count>0,
            "one actual active frozen native subset crosses the sampler terminal edge before delivery");
        File.WriteAllText(Path.Combine(_output,"terminal-overlap660-phase.txt"),"changedAt="+changedAt+" delayedSubsets="+delayedSubsets
            +" activeInstants="+string.Join(",",activeInstants)+" deliveryOverlap="+oldOverlap+" sourceAckOverlap="+sourceAckOverlap+"\n");
        Check(changed && oldOverlap && terminalComplete,"new final native cohort finishes after prior frozen snapshot despite concurrent terminal child and rect change");
        TownServiceMirror.Shutdown();
    }
    private static bool FrozenReturnRoot660(TownServiceMotionPacket packet,TownServiceMotionEntry root)
    {
        if(root.Kind!=1 || root.Pose.Length!=10 || !root.HasCanvasUpdate
            || root.HasCanvasFrame && (root.CanvasPose.Length!=10 || root.CanvasRect.Length!=4 || root.CanvasSettings.Length!=5))return false;
        foreach(TownServiceMotionEntry header in packet.Entries)
        {
            if(header.Kind!=10 || root.Lane!=header.Lane || root.Service!=header.Service
                || root.Session!=header.Session || root.PublicClaim!=header.PublicClaim || root.Hand!=header.Hand)continue;
            int index=Array.BinarySearch(header.ReturnMembers,root.Module);
            if(index<0 || root.Structure!=header.ReturnStructures[index])continue;
            foreach(TownServiceReturnPart part in header.ReturnParts)
                if(part.Index==index && part.Child.Length==10 && part.Visible==root.Visible && part.ParentAlpha==root.ParentAlpha)return true;
        }
        return false;
    }
    private static IEnumerator LateTerminal64_660()
    {
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;FlightTime655.Now=100f;
        Transform owner=Go("Native64 terminal owner").transform,observer=Go("Native64 terminal observer").transform;
        observer.position=Vector3.right*8f;TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("Actual64 native card",owner).transform;card.localPosition=new Vector3(.1f,1f,.03f);
        VRCard native=card.gameObject.AddComponent<VRCard>();var parts=new Transform[64];var random=new System.Random(661);
        RectTransform canvas=(RectTransform)Go("Actual64 reclaim canvas",card).transform;
        canvas.sizeDelta=new Vector2(400,260);canvas.localScale=Vector3.one*.0005f;
        for(int i=0;i<parts.Length;i++)
        {
            parts[i]=i==0?TownServiceCardBody.Create(card).transform:Go("Actual64 child "+i,card).transform;
            if(i==0){parts[i].localScale=new Vector3(.2f,.13f,1f);using(var b=new TownServiceBinding(parts[i]))b.Read(TownServiceMirror.Assets);}
            else
            {
                var rect=(RectTransform)parts[i];rect.sizeDelta=new Vector2(400,260);rect.pivot=new Vector2(.2f,.8f);
                rect.localPosition=new Vector3((float)random.NextDouble()*.06f,(float)random.NextDouble()*.04f,(float)random.NextDouble()*.03f);
                rect.localRotation=Quaternion.Euler((float)random.NextDouble()*5f,(float)random.NextDouble()*9f,(float)random.NextDouble()*13f);
                rect.localScale=Vector3.one*((float)random.NextDouble()*.0001f+.0004f);
            }
        }
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,6604,owner,owner);
            for(int i=0;i<parts.Length;i++)
            {
                string address=i==0?"map.cardbody|660late64":"face.6605|part"+i;
                TownServiceMirror.RegisterTemplate(1,(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.RegisterModule((ushort)(i+1),(ushort)(i+1),parts[i],address:address);
                TownServiceMirror.PrepareCardReturn(parts[i],native.CaptureNativeTownReturn655);
            }
        }
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture warm=CaptureFast();var oldRelations=parts.Select(part=>parts[0].InverseTransformPoint(part.position)).ToArray();
        var headers=new Dictionary<float,TownServiceMotionEntry>();var packets=new Dictionary<float,List<byte[]>>();
        var members=new Dictionary<float,HashSet<byte>>();float terminalSource=-1f,changedAt=-1f;bool sourceOverlap=false;
        native.BeginNative655(new Vector3(.42f,1.1f,.12f),.65f);
        for(int frame=1;frame<=240;frame++)
        {
            // The actual native Update caps a stalled owner's frame delta at
            // .05s. A real .5s wall-clock hitch therefore leaves the first
            // admitted recipe's extrapolated floor expired before the later
            // authoritative terminal source instant; no endpoint is invented.
            FlightTime655.Now=100f+frame/90f+(frame>=31?.5f:0f);
            FlightTime655.Delta=frame==31?.5f:1f/90f;
            bool active=native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _);
            if(active)native.StepNative655();
            if(active&&!native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _))
            {changedAt=FlightTime655.Now;NativeMerchant655.Canonicalize660(parts[1],canvas);}
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
            {
                Check(length<=864&&TownServiceMotionCodec.TryRead(bytes,length,out _),"native64 actual terminal partitions retain the event budget");
                TownServiceMotionCodec.TryRead(bytes,length,out var packet);
                foreach(TownServiceMotionEntry header in packet!.Entries.Where(e=>e.Kind==10))
                {
                    float sample=header.ReturnSampleTime;
                    if(!headers.ContainsKey(sample)){headers[sample]=header;packets[sample]=new List<byte[]>();members[sample]=new HashSet<byte>();}
                    packets[sample].Add((byte[])bytes.Clone());foreach(var part in header.ReturnParts)members[sample].Add(part.Index);
                    bool final=header.Numbers[0]==header.Numbers[1];
                    for(int n=0;n<10;n++)final &= header.Numbers[4+n]==header.Numbers[14+n];
                    if(changedAt>=0f && !final && sample<changedAt)sourceOverlap=true;
                    if(final && terminalSource<0f)terminalSource=sample;
                }
            });
            if(terminalSource>=0f&&members[terminalSource].Count==parts.Length)break;
            yield return null;
        }
        Check(changedAt>=0f&&sourceOverlap&&terminalSource>=0f&&members[terminalSource].Count==parts.Length,
            "native64 terminal survives acknowledgement of the source snapshot frozen across its real terminal branch");
        List<byte[]> finalPackets=packets[terminalSource];TownServiceMotionEntry terminal=headers[terminalSource];
        TownServiceMotionCodec.TryRead(finalPackets[finalPackets.Count-1],finalPackets[finalPackets.Count-1].Length,out var finalPacket);
        Check(finalPackets.Count>4&&finalPacket!.SampleTime-terminalSource>.25f,
            "actual native64 terminal geometry needs more than the existing native grace to finish");
        float activeSource=headers.Where(pair=>pair.Value.Numbers[0]<pair.Value.Numbers[1]).Max(pair=>pair.Key);
        foreach(bool future in new[]{false,true})
        {
            // Separate source/observer actors replay immutable real source packets.
            // ResetNetwork preserves exact template/material originals, not poses.
            TownServiceMirror.ResetNetwork();TownServiceMirror.SharedFrameForRemote=_=>observer;
            float activatedAt;
            var trace=new System.Text.StringBuilder("time,phase,member,error,visible,pivotX,pivotY\n");
            if(!future)
            {
                FlightTime655.Now=terminalSource+.55f;
                Receive(2,warm.Artwork.Where(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var frame)&&frame!.Module==TownServiceFrame.ManifestModule));
                DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
                int missing=finalPackets.Count/2;
                for(int i=0;i<finalPackets.Count;i++)
                {
                    if(i==missing)continue;
                    TownServiceMotionCodec.TryRead(finalPackets[i],finalPackets[i].Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);
                    TownServiceMirror.TickRemote(_=>observer);FlightTime655.Now+=TownServiceMotionCodec.SendInterval;
                    Check(TownServiceMirror.ReturnClocks658(2)==0,"late cold terminal remains atomic while one actual partition is lost");
                    yield return null;
                }
                Receive(2,warm.Artwork);TownServiceMirror.TickRemote(_=>observer);
                int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
                for(int i=0;i<parts.Length;i++)Check(!Remote(peer,(ushort)(i+1))!.Root.gameObject.activeInHierarchy,
                    "fully cold originals stay hidden together before the missing exact terminal member is repaired");
                FlightTime655.Now+=.1f;TownServiceMotionCodec.TryRead(finalPackets[missing],finalPackets[missing].Length,out var recovered);
                TownServiceMirror.ReceiveMotion(2,recovered!);TownServiceMirror.TickRemote(_=>observer);activatedAt=FlightTime655.Now;
                Check(TownServiceMirror.ReturnClocks658(2)==parts.Length&&!TownServiceMirror.PendingCohort660(2),
                    "fully late native64 terminal activates its exact final geometry after cold originals and one retransmitted partition");
            }
            else
            {
                float firstReceipt=activeSource+Mathf.Max(.8f,finalPacket!.SampleTime-activeSource+.2f);
                FlightTime655.Now=firstReceipt;Receive(2,warm.Artwork);DeliverMotion(2,warm);
                foreach(byte[] bytes in packets[activeSource]){TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);}
                TownServiceMirror.TickRemote(_=>observer);
                int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
                Check(TownServiceMirror.ReturnClocks658(2)==parts.Length,"future terminal starts from a complete actual earlier source picture");
                float due=terminalSource+(firstReceipt-activeSource);
                FlightTime655.Now+=.02f;
                foreach(byte[] bytes in finalPackets){TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);}
                bool expiredPrior=false;
                for(float time=FlightTime655.Now;time<due;time+=1f/90f)
                {
                    FlightTime655.Now=time;TownServiceMirror.TickRemote(_=>observer);
                    float priorAge=headers[activeSource].Numbers[0]+time-firstReceipt;
                    expiredPrior |= priorAge>headers[activeSource].Numbers[1]+.25f;
                    Check(TownServiceMirror.PendingCohort660(2)&&((RectTransform)Remote(peer,2)!.Root).pivot==new Vector2(.2f,.8f),
                        "future terminal never applies its final rect or canvas before the retained source instant");
                    AssertNative64Picture660(peer,parts,owner,observer,oldRelations,false,time,"future-pending",trace);
                    yield return null;
                }
                File.WriteAllText(Path.Combine(_output,"native64-terminal-future-pending.csv"),trace.ToString());
                File.WriteAllText(Path.Combine(_output,"native64-terminal-future-clock.txt"),"firstReceipt="+firstReceipt+" activeSource="+activeSource
                    +" priorAge="+headers[activeSource].Numbers[0]+" due="+due+" expiredPrior="+expiredPrior+" ownerHitchFrame=31 ownerHitchSeconds=.5 nativeDeltaCap=.05\n");
                Check(expiredPrior,"actual previous native progress expires while the complete terminal source instant is still future");
                FlightTime655.Now=due;TownServiceMirror.TickRemote(_=>observer);activatedAt=due;
                Check(!TownServiceMirror.PendingCohort660(2)&&((RectTransform)Remote(peer,2)!.Root).pivot==Vector2.one*.5f,
                    "complete future terminal applies all final native geometry exactly at its retained source instant");
            }
            int key=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
            for(int frame=0;frame<=36;frame++)
            {
                FlightTime655.Now=activatedAt+frame/90f;TownServiceMirror.TickRemote(_=>observer);
                AssertNative64Picture660(key,parts,owner,observer,oldRelations,true,FlightTime655.Now,"final",trace);
                if(frame>=24)Check(TownServiceMirror.Cohorts658(2)==0,"authoritative terminal retires once after its existing bounded activation grace");
                yield return null;
            }
            File.WriteAllText(Path.Combine(_output,"native64-terminal-"+(future?"future":"cold")+".csv"),trace.ToString());
            File.WriteAllText(Path.Combine(_output,"native64-terminal-source.txt"),"nativeChangedAt="+changedAt+" sourceAckOverlap="+sourceOverlap
                +" terminalSource="+terminalSource+" parts="+parts.Length+" packets="+finalPackets.Count+" lastSource="+finalPacket!.SampleTime+"\n");
        }
        TownServiceMirror.Shutdown();
    }
    private static void AssertNative64Picture660(int peer,Transform[] parts,Transform owner,Transform observer,
        Vector3[] prior,bool final,float time,string phase,System.Text.StringBuilder trace)
    {
        Transform body=Remote(peer,1)!.Root;
        for(int i=0;i<parts.Length;i++)
        {
            Transform rendered=Remote(peer,(ushort)(i+1))!.Root;
            Vector3 expected=final?observer.TransformPoint(owner.InverseTransformPoint(parts[i].position)):body.TransformPoint(prior[i]);
            float error=Vector3.Distance(rendered.position,expected);
            Check(rendered.gameObject.activeInHierarchy&&error<.00005f,
                "actual native64 body and every front retain one visible physical picture phase="+phase+" member="+i+" error="+error);
            Vector2 pivot=rendered is RectTransform rect?rect.pivot:Vector2.zero;
            if(final&&i>0)
            {
                RectTransform source=(RectTransform)parts[i],clone=(RectTransform)rendered;
                Check(Vector3.Distance(clone.TransformPoint(clone.rect.center),observer.TransformPoint(owner.InverseTransformPoint(source.TransformPoint(source.rect.center))))<.00005f,
                    "final native64 printed center and rect agree with the actual source on every frame");
            }
            trace.AppendLine(time+","+phase+","+i+","+error+","+rendered.gameObject.activeInHierarchy+","+pivot.x+","+pivot.y);
        }
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
