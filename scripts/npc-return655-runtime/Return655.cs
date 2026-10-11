using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

internal static class FlightTime655 { internal static float Now = 100f, Delta = 1f / 90f; }

public static partial class MirrorProgram
{
    private sealed class DelayedFlight655
    {
        internal int Due;
        internal FastCapture Capture = null!;
    }
    // Packet grouping is the explicit transport boundary: unchanged production
    // entries and source timestamps are encoded separately to model different
    // datagram arrival times for detached front/body originals.
    private static FastCapture SplitFlight655(FastCapture captured, bool body)
    {
        var split = new FastCapture();
        foreach (byte[] bytes in captured.Motion)
        {
            Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet), "source clock decodes before independent datagram grouping");
            var result = new TownServiceMotionPacket { Sequence=packet!.Sequence, SampleTime=packet.SampleTime };
            foreach (TownServiceMotionEntry entry in packet.Entries)
                if ((entry.Module == 11) == body) result.Entries.Add(entry);
            if (result.Entries.Count != 0) split.Motion.Add(TownServiceMotionCodec.TryWritePacked(result)!);
        }
        return split;
    }
    private static IEnumerator PublisherReturn655()
    {
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1;
        FlightTime655.Now=100f;
        Transform world=Go("Actual merchant StockSync source").transform; world.localScale=Vector3.one*.8f;
        world.rotation=Quaternion.Euler(3f,29f,0f);
        Transform observer=Go("Actual merchant StockSync observer").transform;
        observer.SetPositionAndRotation(new Vector3(8f,0f,0f),world.rotation); observer.localScale=world.localScale;
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("Actual owned returning ItemChip",world).transform;
        card.localPosition=new Vector3(.4f,1.4f,-.2f); card.localRotation=Quaternion.Euler(15f,-21f,8f);
        NativeMerchant655 native=card.gameObject.AddComponent<NativeMerchant655>();
        Transform face=Source(card),body=Source(card); body.localPosition=Vector3.forward*.002f;
        var chip=card.gameObject.AddComponent<ItemsPile.ItemChip>();
        chip.NativeItemCard=face.gameObject.AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
        chip.Item=new ItemsPile.Item { ID=1129 }; chip.InspectionBody=body;
        var hand=new VRHand { Side=HandSide.Left,WorldScale=.8f };
        hand.Rig.Root=Go("Actual native fan destination",world).transform;
        hand.Rig.Root.localPosition=new Vector3(.1f,.2f,.1f); hand.Rig.GrabAnchor=Go("Other grab mount",hand.Rig.Root).transform;
        var right=new VRHand { Side=HandSide.Right,WorldScale=.8f }; right.Rig.Root=hand.Rig.Root;
        right.Rig.GrabAnchor=Go("Right grab mount",world).transform;
        VRHands.Left=hand; VRHands.Right=right;
        Transform remoteHand=Go("Approved native fan holder on observer",observer).transform;
        remoteHand.localPosition=hand.Rig.Root.localPosition; remoteHand.localRotation=hand.Rig.Root.localRotation;
        NetAvatarDriver.MotionHandFrames[2]=new[] { remoteHand,remoteHand };
        GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog=null;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.StationRoot=world;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.Session=655;
        GloomhavenVR.WorldUI.TownServicePresentation.Active=false;
        GloomhavenVR.WorldUI.TownServicePresentation.Service=1;
        GloomhavenVR.WorldUI.TownServicePresentation.Ritual=null;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active=false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.HasParkedOffer=false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.PreparedPurchase=null;
        GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Add(chip);
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish=true;
        TownServiceMirror.ResolveTemplate=(service,template,address)=>
        { GloomhavenVR.WorldUI.NativeTemplates.Resolve(service,template,address); return true; };
        MethodInfo tick=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetMethod("TickStock",PrivateStatic)!;
        tick.Invoke(null,new object[] { world });
        ushort faceId=GloomhavenVR.WorldUI.TownServiceSync.StockModuleId(face);
        ushort bodyId=GloomhavenVR.WorldUI.TownServiceSync.StockModuleId(body);
        Canvas.ForceUpdateCanvases(); yield return null;
        FastCapture initial=CaptureFast(); int clocks=0,roots=0;
        foreach(byte[] bytes in initial.Motion)
            if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet))
                foreach(var entry in packet!.Entries)
                    if(entry.Module==faceId || entry.Module==bodyId)
                    { if(entry.Kind==8 && entry.Hand==3)clocks++; if(entry.Kind==1 && entry.Hand==3)roots++; }
        Check(clocks==2 && roots==2,"actual StockSync publishes matching root and native clock before the first return render roots="+roots+" clocks="+clocks);
        Receive(2,initial.Artwork); DeliverMotion(2,initial); TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;
        TownServiceBinding clone=Remote(keys[2],faceId)!,cloneBody=Remote(keys[2],bodyId)!;
        Check(clone!=null && cloneBody!=null,"actual StockSync restores both original native return partitions");
        for(int frame=1;frame<=24;frame++)
        {
            FlightTime655.Now=100f+frame/90f; native.StepNative655();
            if(frame%7==0) { tick.Invoke(null,new object[] {world}); FastCapture update=CaptureFast(); Receive(2,update.Artwork); DeliverMotion(2,update); }
            TownServiceMirror.TickRemote(_=>observer);
            Check(Vector3.Distance(clone.Root.position,observer.TransformPoint(world.InverseTransformPoint(face.position)))<.0005f,
                "actual publisher merchant flight follows source native ItemChip on every rendered frame");
            Check(Vector3.Distance(cloneBody.Root.position,observer.TransformPoint(world.InverseTransformPoint(body.position)))<.0005f,
                "actual publisher native body follows the same source curve");
            yield return null;
        }
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish=false;
        GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Clear(); GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork();
        VRHands.Left=null; VRHands.Right=null; NetAvatarDriver.MotionHandFrames.Clear(); TownServiceMirror.Shutdown();
    }
    private static IEnumerator Return655(bool merchant, bool split)
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        FlightTime655.Now = 100f;
        Transform shared = Go("Source shared map").transform;
        shared.rotation = Quaternion.Euler(3f, 31f, 0f); shared.localScale = Vector3.one * .8f;
        Transform observer = Go("Observer shared map").transform;
        observer.SetPositionAndRotation(new Vector3(8f, .2f, -.1f), shared.rotation);
        observer.localScale = shared.localScale;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform source = Go("Actual native card root", shared).transform;
        source.localPosition = new Vector3(.4f, 1.4f, -.2f);
        source.localRotation = Quaternion.Euler(15f, -21f, 8f);
        Transform face = Source(source); face.localPosition = new Vector3(.01f, -.02f, .003f);
        face.localRotation = Quaternion.Euler(7f, -8f, 11f);
        Transform body = Source(source); body.localPosition = new Vector3(.01f, -.02f, .006f);
        body.localRotation = face.localRotation;
        VRCard? ability = null; NativeMerchant655? item = null;
        if (merchant) item = source.gameObject.AddComponent<NativeMerchant655>();
        else { ability = source.gameObject.AddComponent<VRCard>();
            ability.BeginNative655(shared.TransformPoint(new Vector3(-.5f, 1.1f, .2f)), .55f); }
        bool Sample(Transform original, Transform world, VRHand? holder, out uint revision, out float[] values)
        {
            if (merchant) return item!.TryTownReturnMotion(original, world, holder, out revision, out values);
            return ability!.CaptureNativeTownReturn655(original, world, holder, out revision, out values);
        }
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 655, shared, shared);
            TownServiceMirror.RegisterTemplate(1, 1, face, address: "face.2129|");
            TownServiceMirror.RegisterTemplate(1, 2, body, address: "map.cardbody|");
            TownServiceMirror.RegisterModule(10, 1, face, address: "face.2129|");
            TownServiceMirror.RegisterModule(11, 2, body, address: "map.cardbody|");
            TownServiceMirror.RegisterCardReturn(face, Sample); TownServiceMirror.RegisterCardReturn(body, Sample);
        }
        Canvas.ForceUpdateCanvases(); yield return null;
        FastCapture baseline = CaptureFast(); Receive(2, baseline.Artwork);
        TownServiceMirror.TickRemote(_ => observer);
        var keys = (Dictionary<int, int>)typeof(TownServiceMirror).GetField("StockKeys", PrivateStatic)!.GetValue(null)!;
        int peer = keys[2]; TownServiceBinding clone = Remote(peer, 10)!, cloneBody = Remote(peer, 11)!;
        Check(clone != null && cloneBody != null, "original face/body are prepared through actual capture and codec");
        var queued = new List<DelayedFlight655> { new() { Due=6, Capture=split ? SplitFlight655(baseline,false) : baseline } };
        if (split) queued.Add(new DelayedFlight655 { Due=9, Capture=SplitFlight655(baseline,true) });
        int[] delays = { 10, 2, 8, 4, 7, 1, 5 };
        int sampled = 0, delivered = 0, rendered = 0;
        var facePositions = new List<Vector3> { shared.InverseTransformPoint(face.position) };
        var bodyPositions = new List<Vector3> { shared.InverseTransformPoint(body.position) };
        var rotations = new List<Quaternion> { Quaternion.Inverse(shared.rotation) * face.rotation };
        var scales = new List<Vector3> { face.lossyScale };
        var trace = new System.Text.StringBuilder("render,ownerFrame,packets,faceError,bodyError,visible\n");
        float maxError = 0f;
        for (int frame = 1; frame <= 48; frame++)
        {
            FlightTime655.Now = 100f + frame / 90f; FlightTime655.Delta=1f/90f;
            if (merchant) item!.StepNative655(); else ability!.StepNative655();
            facePositions.Add(shared.InverseTransformPoint(face.position)); bodyPositions.Add(shared.InverseTransformPoint(body.position));
            rotations.Add(Quaternion.Inverse(shared.rotation) * face.rotation); scales.Add(face.lossyScale);
            if (frame % 7 == 0)
            { FastCapture capture = CaptureFast(); int due=frame+delays[sampled++ % delays.Length];
              queued.Add(new DelayedFlight655 { Due=due, Capture=split ? SplitFlight655(capture,false) : capture });
              if (split) queued.Add(new DelayedFlight655 { Due=due+3, Capture=SplitFlight655(capture,true) }); }
            // Genuine caption updates happen at independent source instants. They must
            // neither restart the clock nor remove its actual still-live originals.
            if (frame == 12 || frame == 26 || frame == 37)
            {
                face.Find("Name").GetComponent<TMPro.TMP_Text>().text="Native caption "+frame;
                Receive(2, CaptureFast().Artwork);
            }
            for (int i=0; i<queued.Count;)
            {
                if (queued[i].Due > frame) { i++; continue; }
                DeliverMotion(2, queued[i].Capture); delivered++; queued.RemoveAt(i);
            }
            TownServiceMirror.TickRemote(_ => observer);
            if (frame >= 6)
            {
                int ownerFrame=frame-6;
                float error=Vector3.Distance(clone.Root.position, observer.TransformPoint(facePositions[ownerFrame]));
                float bodyError=split && frame<9 ? 0f : Vector3.Distance(cloneBody.Root.position, observer.TransformPoint(bodyPositions[ownerFrame]));
                maxError=Mathf.Max(maxError,error,bodyError); rendered++;
                trace.AppendLine(frame+","+ownerFrame+","+delivered+","+error+","+bodyError+","+clone.Root.gameObject.activeInHierarchy);
                Check(error < .0005f && bodyError < .0005f,
                    "native return keeps the source curve under variable packet delay face="+error+" body="+bodyError+" frame="+frame);
                Check(clone.Root.gameObject.activeInHierarchy && cloneBody.Root.gameObject.activeInHierarchy,
                    "both native originals stay visible across delayed flight and current caption headers");
                Check(Quaternion.Angle(clone.Root.rotation,observer.rotation*rotations[ownerFrame])<.05f,
                    "rolling clock preserves exact native child orientation");
                Check(Vector3.Distance(clone.Root.lossyScale,scales[ownerFrame])<.0005f,
                    "rolling clock preserves native face dimensions under variable transport delay");
            }
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output,"return655.csv"),trace.ToString());
        File.WriteAllText(Path.Combine(_output,"return655-summary.txt"),"maxError="+maxError+"; deliveries="+delivered+"; renders="+rendered+"\n");
        Check(delivered>=5 && rendered==43,"variable nonzero transport delay is exercised across every observer render");
        // Regrab is a new native author, not another sample of the previous return.
        // Stop the actual native sampler first, as the production grabbers do.
        TownServiceMirror.RegisterCardReturn(face, (Transform f, Transform w, VRHand? h, out uint r, out float[] v)
            => { r=0; v=Array.Empty<float>(); return false; });
        var hand = new VRHand { Side=HandSide.Right, WorldScale=.8f };
        hand.Rig.Root=Go("Current native tracked hand", shared).transform;
        hand.Rig.Root.SetPositionAndRotation(face.position,face.rotation);
        Transform remoteHand=Go("Approved rendered rig hand",observer).transform;
        remoteHand.localPosition=shared.InverseTransformPoint(hand.Rig.Root.position);
        remoteHand.localRotation=Quaternion.Inverse(shared.rotation)*hand.Rig.Root.rotation;
        NetAvatarDriver.MotionHandFrames[2]=new[] { remoteHand,remoteHand };
        TownServiceMirror.RegisterMotionHand(face,hand);
        FlightTime655.Now+=.08f; DeliverMotion(2,CaptureFast()); TownServiceMirror.TickRemote(_=>observer);
        Vector3 before=clone.Root.position; remoteHand.position+=new Vector3(.15f,0f,0f);
        FlightTime655.Now+=.01f; TownServiceMirror.TickRemote(_=>observer);
        Check(Vector3.Distance(clone.Root.position,before)>.1f,"new actual regrab overrides the bounded previous flight immediately");
        using(TownServiceMirror.UseStockLane()) { TownServiceMirror.UnregisterModule(10); TownServiceMirror.UnregisterModule(11); TownServiceMirror.EndSession(); }
        FlightTime655.Now+=.08f; Receive(2,CaptureFast().Artwork); TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(peer,10)==null,"true native source removal retires the old return and original");
        NetAvatarDriver.MotionHandFrames.Clear(); TownServiceMirror.Shutdown();
    }
}
