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
        var terminal=TerminalOverlap660();while(terminal.MoveNext())yield return terminal.Current;
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
    private static IEnumerator StagedLayout660(bool drop)
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
        FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        int peer=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
        native.BeginNative655(new Vector3(.42f,1.1f,.12f),.7f);
        Vector3 oldRelation=parts[0].InverseTransformPoint(parts[1].position);bool partialObserved=false,repaired=false,dropped=false;
        var inbound=new List<(int due,byte[] bytes)>();
        for(int frame=1;frame<=84;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;native.StepNative655();
            if(frame==24)NativeMerchant655.Canonicalize660(parts[1],canvas);
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
            {
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"staged layout actual bounded receipt decodes");
                if(drop && !dropped && packet!.Entries.Any(e=>e.Kind==10 && e.ReturnSampleTime>=100f+24f/90f
                    && e.ReturnParts.Any(p=>e.ReturnMembers[p.Index]==2))) { dropped=true;return; }
                // One late partition also exercises original sequence rejection;
                // the next complete native snapshot must still repair its loss.
                int delay=drop && frame%18==0?12:0;
                inbound.Add((frame+delay,(byte[])bytes.Clone()));
            });
            foreach(var delivery in inbound.Where(p=>p.due<=frame).ToArray())
            {TownServiceMotionCodec.TryRead(delivery.bytes,delivery.bytes.Length,out var packet);TownServiceMirror.ReceiveMotion(2,packet!);inbound.Remove(delivery);}
            TownServiceMirror.TickRemote(_=>observer);
            Transform body=Remote(peer,1)!.Root,front=Remote(peer,2)!.Root;
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
        TownServiceMirror.Shutdown();
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
        var trace=new System.Text.StringBuilder("frame,terminalComplete,rootError,centerError,pivotX,pivotY\n");
        native.BeginNative655(new Vector3(.42f,1.1f,.12f),.65f);
        for(int frame=1;frame<=108;frame++)
        {
            FlightTime655.Now=100f+frame/90f;FlightTime655.Delta=1f/90f;
            bool before=native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _);
            native.StepNative655();
            if(before && !native.CaptureNativeTownReturn655(parts[1],owner,null,out _,out _))
            {
                changed=true;changedAt=FlightTime655.Now;NativeMerchant655.Canonicalize660(parts[1],canvas);
                finalRoot=parts[0].InverseTransformPoint(parts[1].position);
                finalCenter=parts[0].InverseTransformPoint(parts[1].TransformPoint(((RectTransform)parts[1]).rect.center));
            }
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=>
            {
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"terminal-overlap actual bounded receipt decodes");
                foreach(TownServiceMotionEntry entry in packet!.Entries)
                {
                    if(entry.Kind==10)
                    {
                        if(changed && entry.ReturnSampleTime<changedAt)oldOverlap=true;
                        bool exactTerminal=entry.Numbers[0]==entry.Numbers[1];
                        for(int n=0;n<10;n++)exactTerminal &= entry.Numbers[4+n]==entry.Numbers[14+n];
                        if(exactTerminal)
                        {
                            if(terminalTime!=entry.ReturnSampleTime){terminalParts.Clear();terminalTime=entry.ReturnSampleTime;}
                            foreach(var part in entry.ReturnParts)terminalParts.Add(part.Index);
                            terminalComplete=terminalParts.Count==entry.ReturnMembers.Length;
                        }
                    }
                    if(changed && entry.Kind==1 && !terminalComplete)
                        earlyOrdinary=true;
                }
                TownServiceMirror.ReceiveMotion(2,packet);
            });
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
        Check(changed && oldOverlap && terminalComplete,"new final native cohort finishes after prior frozen snapshot despite concurrent terminal child and rect change");
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
