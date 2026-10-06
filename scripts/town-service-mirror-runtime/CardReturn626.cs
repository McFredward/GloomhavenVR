using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

public static partial class MirrorProgram
{
    // Original stock publisher, capture, bounded queue/fragmentation and receiver
    // are production. Native gameplay outcomes and art construction are explicit
    // boundaries here; native outcome order/actual chip curves run in handoff.
    private static IEnumerator PreparedNativeReturns629()
    {
        for (int mode = 0; mode < 6; mode++)
        {
            int kind = mode % 3; bool early = mode >= 3;
            IEnumerator pending = PreparedNativeReturn629(kind, early); while (pending.MoveNext()) yield return pending.Current;
        }
    }

    private static IEnumerator PreparedNativeReturn629(int kind, bool early)
    {
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
        NetPlayerActors.Peer = 1;
        var world = Go("Cold prepared return shared " + kind).transform;
        world.localScale = Vector3.one * .8f; world.rotation = Quaternion.Euler(3f, 29f, 0f);
        var observer = Go("Cold prepared observer " + kind).transform;
        observer.SetPositionAndRotation(new Vector3(8f, 0f, 0f), world.rotation); observer.localScale = world.localScale;
        var native = Go("Actual offered native card " + kind, world).transform;
        native.localPosition = new Vector3(.2f, 1.1f, -.1f);
        Transform face = Source(native), body = Source(native);
        body.localPosition = Vector3.forward * .002f;
        GloomhavenVR.Cards.VRCard? ability = null;
        GloomhavenVR.Cards.ItemsPile.ItemChip? item = null;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog = null;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.StationRoot = world;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.Session = 1290;
        GloomhavenVR.WorldUI.TownServicePresentation.Active = false;
        GloomhavenVR.WorldUI.TownServicePresentation.Service = (byte)(kind != 1 ? 1 : 3);
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.HasParkedOffer = kind != 1;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.PreparedPurchase = null;
        GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        if (kind != 1)
        {
            item = native.gameObject.AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
            item.NativeItemCard = face.gameObject.AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
            item.Item = new GloomhavenVR.Cards.ItemsPile.Item { ID = 1129 }; item.TownOffering = true;
            item.InspectionBody = body;
            if (kind == 2)
            { item.TownOffering = false; native.localScale = Vector3.zero;
              GloomhavenVR.WorldUI.TownServiceMerchantHandoff.PreparedPurchase = item; }
            else GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(item);
            GloomhavenVR.WorldUI.TownServicePresentation.Ritual = null;
        }
        else
        {
            ability = native.gameObject.AddComponent<GloomhavenVR.Cards.VRCard>();
            body.SetParent(Go("Visual", native).transform, true); body.name = "Backing";
            GloomhavenVR.WorldUI.TownServicePresentation.Ritual = new GloomhavenVR.WorldUI.TownServiceRitual
            { Handoff = new GloomhavenVR.WorldUI.TownServiceEnhancementHandoff { Card = native, Face = face, OfferedCardId = 2129 } };
        }
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = true;
        TownServiceMirror.ResolveTemplate = (service, template, address) =>
        { GloomhavenVR.WorldUI.NativeTemplates.Resolve(service, template, address); return true; };
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        MethodInfo tick = typeof(GloomhavenVR.WorldUI.TownServiceSync).GetMethod("TickStock", PrivateStatic)!;
        tick.Invoke(null, new object[] { world });
        ushort faceId = GloomhavenVR.WorldUI.TownServiceSync.StockModuleId(face);
        ushort bodyId = GloomhavenVR.WorldUI.TownServiceSync.StockModuleId(body);
        Canvas.ForceUpdateCanvases();
        for (float until = Time.unscaledTime + .075f; Time.unscaledTime < until;) yield return null;
        FastCapture prepared = CaptureFast();
        float began = 0f;
        Action beginReturn = () =>
        {
            began = Time.unscaledTime;
            if (kind == 2) native.localScale = Vector3.one;
            Vector3 from = native.position, to = world.TransformPoint(new Vector3(-.4f, 1f, .3f));
            Quaternion rotation = native.rotation; Vector3 scale = native.lossyScale;
            float[] values = TownCardReturnMotion.Capture(face, native, world, null, .01f, .35f,
            kind == 2 ? (byte)1 : kind == 0 ? (byte)3 : (byte)2, kind == 2 ? 20f : 1.7f,
            Matrix4x4.TRS(from, rotation, scale), rotation, Matrix4x4.TRS(to, rotation, scale * .4f), rotation, Vector3.up * .02f);
            if (kind != 1)
            {
            item!.TownOffering = false; item.ReturnNumbers = values; item.ReturnStarted = Time.unscaledTime - .01f;
            GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
            GloomhavenVR.WorldUI.TownServiceMerchantHandoff.HasParkedOffer = false;
            if (kind == 2)
            { GloomhavenVR.WorldUI.TownServiceMerchantHandoff.PreparedPurchase = null;
              GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(item); }
            else GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Add(item);
            }
            else
            {
            ability!.ReturnNumbers = values; ability.ReturnStarted = Time.unscaledTime - .01f;
            GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Add(new GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.ReturnPresentation
            { Card = ability, Face = face, Body = body, StationRoot = world, CardId = 2129 });
            GloomhavenVR.WorldUI.TownServicePresentation.Ritual = null;
            }
            // Begin another private service immediately: cosmetic originals must
            // remain independent without holding the old NPC interaction lease.
            TownServiceMirror.BeginSession((byte)(kind != 1 ? 3 : 1), 4000, world, world);
        };
        if (early) beginReturn();
        var queue = new TownServiceLaneSendQueue(TownServiceFragments.StockLaneMarker); var fragments = new TownServiceFragments();
        int packets = 0, turns = 0; double wireNow = 0;
        foreach (byte[] bytes in prepared.Artwork)
        {
            Check(TownServiceCodec.TryRead(bytes, bytes.Length, out var frame), "actual cold source artwork decodes before bounded queue");
            if (frame!.VisitorStock) queue.Enqueue(bytes, bytes.Length, frame);
        }
        for (; turns < 16; turns++)
        {
            wireNow += .050001; byte[]? page = queue.Next(wireNow); if (page == null) break;
            Check(page.Length <= ExtrasFragments.MaxDatagramBytes, "cold exact return preparation respects existing datagram budget");
            byte[]? complete = fragments.Accept(2, page, page.Length, wireNow); if (complete == null) continue;
            byte[][] members = TownServiceCodec.TryReadBundle(complete, complete.Length, out byte[][]? bundle) ? bundle! : new[] { complete };
            foreach (byte[] member in members) { Check(TownServiceMirror.Receive(2, member, member.Length), "bounded cold preparation received from actual StockSync"); packets++; }
            if (early)
            {
                for (float until = Time.unscaledTime + .050001f; Time.unscaledTime < until;) yield return null;
                tick.Invoke(null, new object[] { world });
                FastCapture active = CaptureFast();
                foreach (byte[] bytes in active.Artwork)
                { Check(TownServiceCodec.TryRead(bytes, bytes.Length, out var frame), "cold running source remains a complete queued original"); if (frame!.VisitorStock) queue.Enqueue(bytes, bytes.Length, frame); }
                DeliverMotion(2, active); TownServiceMirror.TickRemote(_ => observer);
            }

        }
        DeliverMotion(2, prepared); TownServiceMirror.TickRemote(_ => observer);
        var keys = (Dictionary<int, int>)typeof(TownServiceMirror).GetField("StockKeys", PrivateStatic)!.GetValue(null)!;
        Check(keys.ContainsKey(2), "actual StockSync cold source creates a visitor session packets=" + packets + " artwork=" + prepared.Artwork.Count);
        int stock = keys[2]; TownServiceBinding? copyFace = Remote(stock, faceId), copyBody = Remote(stock, bodyId);
        Check(packets > 0 && turns < 16 && copyFace != null && copyBody != null,
            "actual offered source prepares inert exact face and body before native return starts");
        if (!early) Check(!copyFace!.Root.gameObject.activeInHierarchy && !copyBody!.Root.gameObject.activeInHierarchy,
            "prepared offered original never duplicates its still-live private palm picture");
        else Check(Time.unscaledTime - began < .35f && copyFace!.Root.gameObject.activeInHierarchy
            && copyBody!.Root.gameObject.activeInHierarchy,
            "immediate cold cancellation reveals exact originals within their still-live native return");
        Sprite originalSprite = copyFace!.Root.Find("Filled").GetComponent<UnityEngine.UI.Image>().sprite;
        if (!early)
        { beginReturn(); for (float until = Time.unscaledTime + .075f; Time.unscaledTime < until;) yield return null; }
        tick.Invoke(null, new object[] { world }); FastCapture release = CaptureFast();
        // Only the real numeric packets are delivered: no second art/baseline
        // may be needed to reveal the already prepared complete source.
        DeliverMotion(2, release); TownServiceMirror.TickRemote(_ => observer);
        Check(ReferenceEquals(Remote(stock, faceId), copyFace) && copyFace.Root.gameObject.activeInHierarchy
            && ReferenceEquals(Remote(stock, bodyId), copyBody) && copyBody!.Root.gameObject.activeInHierarchy,
            "first real numeric return reveals inert same originals after private NPC switch without more artwork");
        Check(ReferenceEquals(originalSprite, copyFace.Root.Find("Filled").GetComponent<UnityEngine.UI.Image>().sprite),
            "first numeric reveal preserves exact prepared native card front");
        Vector3 last = copyFace.Root.position;
        for (int rendered = 0; rendered < 4; rendered++)
        { yield return null; TownServiceMirror.TickRemote(_ => observer);
          Check((copyFace.Root.position - last).sqrMagnitude > 1e-10f, "prepared native return advances every observer render between packets"); last = copyFace.Root.position; }
        System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "prepared-return-wire.txt"),
            $"kind={kind} early={early} coldQueueTurns={turns} completeFrames={packets} reveal=first-numeric-turn extraArtwork=0\n");
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.PreparedPurchase = null;
        GloomhavenVR.WorldUI.TownServiceCardFlights.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
    }

    private static IEnumerator RetiredMerchantReturn629()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        Transform shared = Go("Retired native merchant shared frame").transform;
        shared.rotation = Quaternion.Euler(0f, 43f, 0f); shared.localScale = Vector3.one * .7f;
        Transform observer = Go("Observer independent of current NPC").transform;
        observer.SetPositionAndRotation(new Vector3(7f, 0f, 0f), shared.rotation); observer.localScale = shared.localScale;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform original = Go("Retained owned original", shared).transform;
        original.localPosition = new Vector3(-.3f, 1.2f, .2f); original.localRotation = Quaternion.Euler(9f, 31f, 2f);
        Transform face = Source(original), body = Source(original);
        face.localPosition = new Vector3(.002f, 0f, -.001f); body.localPosition = new Vector3(0f, 0f, .001f);
        TownServiceMirror.RegisterTemplate(1, 11, face, address: "item.11|");
        TownServiceMirror.RegisterTemplate(1, 12, body, address: "inspectionbody.11|");
        Vector3 from = original.position, target = shared.TransformPoint(new Vector3(-.35f, 1.1f, .15f));
        Quaternion rotation = original.rotation; Vector3 scale = original.lossyScale;
        var token = new GloomhavenVR.WorldUI.TownServiceToken();
        token.ReturnStarted = Time.unscaledTime;
        token.ReturnSamples[face] = TownCardReturnMotion.Capture(face, original, shared, null, 0f, .35f, 3, 1.7f,
            Matrix4x4.TRS(from, rotation, scale), rotation,
            Matrix4x4.TRS(target, rotation, scale * .12f), rotation, Vector3.zero);
        token.ReturnSamples[body] = TownCardReturnMotion.Capture(body, original, shared, null, 0f, .35f, 3, 1.7f,
            Matrix4x4.TRS(from, rotation, scale), rotation,
            Matrix4x4.TRS(target, rotation, scale * .12f), rotation, Vector3.zero);
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 129, shared, shared);
            TownServiceMirror.RegisterModule(11, 11, face, address: "item.11|");
            TownServiceMirror.RegisterModule(12, 12, body, address: "inspectionbody.11|");
            TownServiceMirror.RegisterCardReturn(face, token.TryCardReturnMotion);
            TownServiceMirror.RegisterCardReturn(body, token.TryCardReturnMotion);
        }
        Canvas.ForceUpdateCanvases(); yield return null;
        FastCapture capture = CaptureFast(); Receive(2, capture.Artwork); DeliverMotion(2, capture);
        TownServiceMirror.TickRemote(_ => observer);
        var keys = (Dictionary<int, int>)typeof(TownServiceMirror).GetField("StockKeys", PrivateStatic)!.GetValue(null)!;
        int stock = keys[2]; TownServiceBinding? copyFace = Remote(stock, 11), copyBody = Remote(stock, 12);
        Check(copyFace != null && copyBody != null && copyBody.Root.gameObject.activeInHierarchy,
            "exact terminal item body survives independent stock admission without a live NPC occupation");
        Check(!TownServiceMirror.StockItemHeldByOther(11), "owned terminal original never vacates matching public cabinet stock");
        TownServiceMirror.BeginSession(3, 130, shared, shared); // real private lane switch while old original is still returning
        FastCapture switched = CaptureFast(); Receive(2, switched.Artwork); DeliverMotion(2, switched);
        for (float until = Time.unscaledTime + .075f; Time.unscaledTime < until;) yield return null;
        token.ReturnNumbers = token.ReturnSamples[face];
        token.ReturnStarted = Time.unscaledTime - .07f;
        FastCapture flight = CaptureFast(); Receive(2, flight.Artwork); DeliverMotion(2, flight);
        TownServiceMirror.TickRemote(_ => observer);
        Vector3 previous = copyFace!.Root.position;
        for (int frame = 0; frame < 4; frame++)
        {
            yield return null; TownServiceMirror.TickRemote(_ => observer);
            Check(ReferenceEquals(Remote(stock, 11), copyFace) && ReferenceEquals(Remote(stock, 12), copyBody),
                "private mage session cannot retire independent merchant terminal originals mid-flight");
            Check((copyFace.Root.position - previous).sqrMagnitude > .0000000001f,
                "retired merchant original keeps smooth per-render-frame motion after private NPC switch");
            previous = copyFace.Root.position;
        }
        using (TownServiceMirror.UseStockLane())
        { TownServiceMirror.UnregisterModule(11); TownServiceMirror.UnregisterModule(12); }
        for (float until = Time.unscaledTime + .075f; Time.unscaledTime < until;) yield return null;
        FastCapture ended = CaptureFast(); Receive(2, ended.Artwork); DeliverMotion(2, ended); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(stock, 11) == null && Remote(stock, 12) == null,
            "retired exact originals leave observer census when their native owner completes the terminal flight");
        TownServiceMirror.Shutdown();
    }

    private static IEnumerator CardReturns626()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1;
        Transform shared=Go("Card return actual shared frame").transform;
        shared.rotation=Quaternion.Euler(0f,19f,0f);
        Transform observer=Go("Observer of original return").transform;
        observer.SetPositionAndRotation(new Vector3(9f,0f,0f),shared.rotation);
        TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform mount=Go("Actual stock moving root",shared).transform;
        mount.localPosition=new Vector3(.4f,1.2f,-.3f);
        mount.localRotation=Quaternion.Euler(21f,37f,-11f);
        Transform face=Source(mount); face.localRotation=Quaternion.Euler(6f,-9f,13f);
        face.localPosition=new Vector3(.025f,-.033f,.009f);
        Func<Transform,bool> boundary=node=>node==face;
        TownServiceMirror.RegisterTemplate(1,1,mount,boundary,address:"merchant.heldstock|");
        TownServiceMirror.RegisterTemplate(1,2,face,address:"item.611|");
        var token=new GloomhavenVR.WorldUI.TownServiceToken();
        using(TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1,991,shared,shared);
            TownServiceMirror.RegisterModule(17,1,mount,boundary,address:"merchant.heldstock|");
            TownServiceMirror.RegisterModule(10,2,face,address:"item.611|");
            TownServiceMirror.RegisterCardReturn(mount,token.TryCardReturnMotion);
            TownServiceMirror.RegisterCardReturn(face,token.TryCardReturnMotion);
        }
        Canvas.ForceUpdateCanvases();yield return null;
        NetAvatarDriver.PeerHeldStock[2]=611;
        FastCapture warm=CaptureFast();Receive(2,warm.Artwork);DeliverMotion(2,warm);
        TownServiceMirror.TickRemote(_=>observer);
        var keys=(Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!;
        int stock=keys[2]; TownServiceBinding copy=Remote(stock,10)!, root=Remote(stock,17)!;
        Check(copy!=null&&root!=null,"stock original is fully prepared before its short return begins");
        Check(!root.Root.gameObject.activeInHierarchy,
            "prepared stock original stays hidden while the canonical avatar holds its one card");
        NetAvatarDriver.PeerHeldStock.Remove(2);
        Vector3 from=mount.position; Quaternion fromRotation=mount.rotation; Vector3 size=mount.lossyScale;
        Vector3 target=shared.TransformPoint(new Vector3(-.6f,.9f,.2f));
        Quaternion targetRotation=shared.rotation*Quaternion.Euler(75f,0f,0f);
        Matrix4x4 fromMatrix=Matrix4x4.TRS(from,fromRotation,size),toMatrix=Matrix4x4.TRS(target,targetRotation,size*.72f);
        for(float until=Time.unscaledTime+.075f;Time.unscaledTime<until;)yield return null;
        token.ReturnStarted=Time.unscaledTime-.07f;
        token.ReturnNumbers=TownCardReturnMotion.Capture(face,mount,shared,null,.07f,.35f,0,0f,
            fromMatrix,fromRotation,toMatrix,targetRotation,Vector3.zero);
        token.ReturnSamples[mount]=TownCardReturnMotion.Capture(mount,mount,shared,null,.07f,.35f,0,0f,
            fromMatrix,fromRotation,toMatrix,targetRotation,Vector3.zero);
        FastCapture returned=CaptureFast();Receive(2,returned.Artwork);DeliverMotion(2,returned);
        float received=Time.unscaledTime;TownServiceMotionEntry? clock=null;
        foreach(byte[] bytes in returned.Motion) if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out var packet))
            foreach(var entry in packet!.Entries)if(entry.Kind==8&&entry.Module==10)clock=entry;
        Check(clock!=null&&clock.Revision==5,"a release publishes the warm original return on its first numeric turn");
        TownServiceMirror.TickRemote(_=>observer);
        Check(root.Root.gameObject.activeInHierarchy,"release exposes the already prepared same stock original immediately");
        float[] data=clock!.Numbers;Vector3 previous=copy.Root.position;
        for(int step=0;step<5;step++)
        {
            yield return null;TownServiceMirror.TickRemote(_=>observer);
            float age=data[0]+Time.unscaledTime-received;
            float t=Mathf.Clamp01(age/data[1]),ease=t*t*(3f-2f*t);
            Matrix4x4 owner=Matrix4x4.TRS(Vector3.Lerp(from,target,ease),Quaternion.Slerp(fromRotation,targetRotation,ease),
                Vector3.Lerp(size,size*.72f,ease));
            Matrix4x4 child=Matrix4x4.TRS(new Vector3(data[28],data[29],data[30]),
                new Quaternion(data[31],data[32],data[33],data[34]),new Vector3(data[35],data[36],data[37]));
            Vector3 actual=observer.TransformPoint(shared.InverseTransformPoint((owner*child).MultiplyPoint3x4(Vector3.zero)));
            Check(Vector3.Distance(copy.Root.position,actual)<.0003f,
                "native card return advances on every rendered frame without another packet actual="+copy.Root.position.ToString("F6")+" expected="+actual.ToString("F6")+" age="+age+" root="+root.Root.position.ToString("F6"));
            Quaternion rotation=observer.rotation*Quaternion.Inverse(shared.rotation)*Quaternion.Slerp(fromRotation,targetRotation,ease)
                *new Quaternion(data[31],data[32],data[33],data[34]);
            Check(Quaternion.Angle(copy.Root.rotation,rotation)<.03f,
                "the exact original card face follows its authored root-relative rotation");
            Check(Vector3.Distance(previous,copy.Root.position)>.00001f,"short native return does not stop between sparse town samples");
            previous=copy.Root.position;
        }
        DeliverMotion(2,warm);TownServiceMirror.TickRemote(_=>observer);
        Check(root.Root.gameObject.activeInHierarchy,"reordered older held metadata cannot revive the retired avatar duplicate");
        NetAvatarDriver.PeerHeldStock.Clear();TownServiceMirror.Shutdown();
    }
}
