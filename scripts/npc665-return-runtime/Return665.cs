using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator NativeFlight658(string variant)
    {
        string[] scenarios=variant=="old-retirement"?new[]{"close-before-stock"}:
            variant=="old-terminal-preservation"?new[]{"terminal-cold"}:
            variant=="old-origin-lifetime"?new[]{"late-stock-origin"}:
            variant=="old-superseded-epoch"?new[]{"no-gap-reoffer"}:
            variant.StartsWith("old-")?new[]{"pre-withdraw"}:
            new[]{"pre-withdraw", "close-before-stock", "lost-first", "terminal-cold", "new-private-session", "late-stock-origin", "no-gap-reoffer", "no-flight-stock-close", "no-flight-lost-close", "owner-disconnect"};
        foreach (string scenario in scenarios)
        { IEnumerator step=OfferedReturn665(scenario);while(step.MoveNext())yield return step.Current; }
    }
    private static IEnumerator OfferedReturn665(string scenario)
    {
        TownServiceSync.ResetNetwork();TownServiceEnhancementHandoff.Returning.Clear();TownServicePresentation.Ritual=null;
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer=1; FlightTime655.Now=100f;
        Transform owner=Go("665 owner shared").transform, observer=Go("665 observer shared").transform;
        observer.position=Vector3.right*8f; TownServiceMirror.SharedFrameForRemote=_=>observer;
        Transform card=Go("665 actual native ability",owner).transform;
        card.localPosition=new Vector3(.4f,1.2f,.1f);card.localRotation=Quaternion.Euler(15f,-21f,8f);
        VRCard native=card.gameObject.AddComponent<VRCard>();
        Transform front=NativeRow632(card,"offered native ability");
        front.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        front.localPosition=new Vector3(0f,0f,-.002f);front.localScale=Vector3.one*.0005f;
        native.FixtureBacking(new Vector2(.14f,.22f));
        Transform body=card.Find("Visual/Backing");body.localPosition=Vector3.forward*.002f;
        string bodyAddress=TownServiceAbilityBody.Key(native)+"|";
        TownServiceMirror.CardReturnSampler sampler=native.CaptureNativeTownReturn655;
        TownServiceMirror.BeginSession(3,665,owner,owner);
        TownServiceMirror.RegisterTemplate(3,1,body,address:bodyAddress);
        TownServiceMirror.RegisterTemplate(3,2,front,address:"face.6651|");
        TownServiceMirror.RegisterModule(81,1,body,address:bodyAddress);
        TownServiceMirror.RegisterModule(82,2,front,address:"face.6651|");
        TownServiceSync.UseProductionPublish=true;
        TownServicePresentation.Active=false;TownServicePresentation.Service=3;
        var handoff=new TownServiceEnhancementHandoff { Card=native,Face=front,OfferedCardId=6651 };
        TownServicePresentation.Ritual=new TownServiceRitual { Handoff=handoff };
        TownServicePublicMerchant.StationRoot=owner;
        TownServiceMirror.RegisterTemplate(1,1,body,address:bodyAddress);
        TownServiceMirror.RegisterTemplate(1,2,front,address:"face.6651|");
        TownServiceMirror.ResolveTemplate=(service,template,address)=>{ NativeTemplates.Resolve(service,template,address);return true;};
        var stockTick=typeof(TownServiceSync).GetMethod("TickStock",PrivateStatic)!;
        stockTick.Invoke(null,new object[]{owner});
        ushort stockFront=TownServiceSync.StockModuleId(front),stockBody=TownServiceSync.StockModuleId(body);
        Check(stockFront!=82&&stockBody!=81,"actual Stock publisher assigns identities independently of private offered modules");
        Canvas.ForceUpdateCanvases();yield return null;
        FastCapture warm=CaptureFast();
        int originalsAt=scenario=="close-before-stock"?24:scenario=="terminal-cold"?75:0;
        Receive(2,warm.Artwork.Where(bytes=> {TownServiceCodec.TryRead(bytes,bytes.Length,out var f);return originalsAt==0||!f!.VisitorStock||f.Module==TownServiceFrame.ManifestModule;}).Select(bytes=>scenario=="late-stock-origin"?TownServiceMirror.WithoutStockOrigin665(bytes):bytes));
        DeliverMotion(2,warm);NetPlayerActors.Peer=3; TownServiceMirror.InteractionOwner(3); FlightTime655.Now+=.13f; TownServiceMirror.TickRemote(_=>observer);
        File.WriteAllText(Path.Combine(_output,"warm-frames.txt"),string.Join("\n",warm.Artwork.Select(b=> {TownServiceCodec.TryRead(b,b.Length,out var f);return $"stock={f!.VisitorStock} service={f.Service} module={f.Module} parent={f.ParentModule} visible={f.Visible} nodes={f.Nodes.Length} census={string.Join(",",f.Modules)}";})));
        File.WriteAllText(Path.Combine(_output,"fixture-source-warnings.txt"),string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
        int stock=((Dictionary<int,int>)typeof(TownServiceMirror).GetField("StockKeys",PrivateStatic)!.GetValue(null)!)[2];
        TownServiceBinding originalBody=Remote(2,81)!,originalFront=Remote(2,82)!;
        Check(originalBody!=null&&originalFront!=null&&originalBody.Root.gameObject.activeInHierarchy&&originalFront.Root.gameObject.activeInHierarchy,
            "actual private offered original body and native front are already rendered");
        var originalImages=originalFront.Root.GetComponentsInChildren<Image>(true);
        var originalSprites=originalImages.Select(image=>image.sprite).ToArray();
        var originalTextures=originalImages.Select(image=>image.mainTexture).ToArray();
        var originalMaterials=originalImages.Select(image=>image.material).ToArray();
        Check(originalImages.Length>0&&originalSprites.Any(sprite=>sprite!=null),"actual native offered prefab carries original sprite artwork before the return");
        if(originalsAt==0)Check(!Remote(stock,stockBody)!.Root.gameObject.activeInHierarchy&&!Remote(stock,stockFront)!.Root.gameObject.activeInHierarchy,
            "separate stock originals remain prepared and hidden while offered");
        Check(!sampler(front,owner,null,out uint prep,out _)&&prep==0,"actual inactive native sampler exposes preparation revision zero");
        if(scenario.StartsWith("no-flight")||scenario=="owner-disconnect") {
            handoff.Card=null;TownServiceMirror.UnregisterModule(81);TownServiceMirror.UnregisterModule(82);
            FlightTime655.Now+=1f/90f;FastCapture closing=CaptureFast();
            Receive(2,closing.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return !f!.VisitorStock;}));
            TownServiceMirror.TickRemote(_=>observer);
            Check(TownServiceMirror.VisibleParts665("face.6651|")==1&&TownServiceMirror.VisibleParts665(bodyAddress)==1,
                "close without a native flight keeps only the bounded preannounced original");
            Check(!sampler(front,owner,null,out prep,out _)&&prep==0,"cancel without flight creates no transport revision or authority");
            if(scenario=="owner-disconnect")TownServiceMirror.RemovePeer(2);
            else {
                stockTick.Invoke(null,new object[]{owner});FlightTime655.Now+=1f/90f;FastCapture stockClose=CaptureFast();
                if(scenario=="no-flight-stock-close")Receive(2,stockClose.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return f!.VisitorStock;}));
                else FlightTime655.Now+=NetProtocol.StaleTimeoutSeconds+.01f;
                TownServiceMirror.TickRemote(_=>observer);
            }
            Check(TownServiceMirror.VisibleParts665("face.6651|")==0&&TownServiceMirror.VisibleParts665(bodyAddress)==0,
                "no-flight closure timeout and owner disconnect retire parked originals without transport authority");
            File.AppendAllText(Path.Combine(_output,"scenarios.txt"),scenario+": no flight, exact original retired\n");yield break;
        }
        native.BeginNative655(owner.TransformPoint(new Vector3(-.5f,1.1f,.2f)),.65f);
        Check(sampler(front,owner,null,out uint flight,out _)&&flight==1,"actual native flight bumps preparation zero to revision one");
        if(scenario=="no-gap-reoffer") {
            handoff.Card=null;TownServiceEnhancementHandoff.Returning.Add(new TownServiceEnhancementHandoff.ReturnPresentation
                {Card=native,Face=front,Body=body,StationRoot=owner,CardId=6651});stockTick.Invoke(null,new object[]{owner});
            for(int i=0;i<6;i++){FlightTime655.Now+=1f/90f;native.StepNative655();}
            var delayed=new FastCapture();TownServiceMirror.CaptureMotion((bytes,length,identity)=>delayed.Motion.Add(bytes));
            Check(delayed.Motion.Any(b=>TownServiceMotionCodec.TryRead(b,b.Length,out var p)&&p!.Entries.Any(e=>e.Kind==10)),
                "no-gap reoffer retains an actual old native113 event before cancellation");
            native.Cancel660(new GloomhavenVR.Hands.VRHand());native.Prepare665();
            card.localPosition=new Vector3(.4f,1.2f,.1f);card.localScale=Vector3.one;
            TownServiceEnhancementHandoff.Returning.Clear();handoff.Card=native;stockTick.Invoke(null,new object[]{owner});
            FlightTime655.Now+=1f/90f;FastCapture nextPreparation=CaptureFast();
            Receive(2,nextPreparation.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return !f!.VisitorStock;}));
            TownServiceMirror.TickRemote(_=>observer);
            Check(ReferenceEquals(originalBody,Remote(2,81))&&ReferenceEquals(originalFront,Remote(2,82))&&TownServiceMirror.OriginRevision665(2,82)==1,
                "no-gap reoffer uses the same private original with the actual new inactive preparation epoch");
            DeliverMotion(2,delayed);TownServiceMirror.TickRemote(_=>observer);
            Check(ReferenceEquals(originalBody,Remote(2,81))&&ReferenceEquals(originalFront,Remote(2,82))
                &&!Remote(stock,stockBody)!.Root.gameObject.activeInHierarchy&&!Remote(stock,stockFront)!.Root.gameObject.activeInHierarchy,
                "older exact native113 cannot expose a stock copy while a newer private preparation is already rendered");
            Receive(2,nextPreparation.Artwork);DeliverMotion(2,nextPreparation);TownServiceMirror.TickRemote(_=>observer);
            native.BeginNative655(owner.TransformPoint(new Vector3(-.5f,1.1f,.2f)),.65f);
            Check(sampler(front,owner,null,out flight,out _)&&flight==2,"actual reoffered native flight bumps preparation one to revision two");
            handoff.Card=null;TownServiceEnhancementHandoff.Returning.Add(new TownServiceEnhancementHandoff.ReturnPresentation
                {Card=native,Face=front,Body=body,StationRoot=owner,CardId=6651});stockTick.Invoke(null,new object[]{owner});
            TownServiceMirror.UnregisterModule(81);TownServiceMirror.UnregisterModule(82);
            for(int i=0;i<6;i++){FlightTime655.Now+=1f/90f;native.StepNative655();}
            TownServiceMirror.CaptureMotion((bytes,length,identity)=>{
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"reoffer revision2 native event decodes");
                TownServiceMirror.ReceiveMotion(2,packet!);
            });TownServiceMirror.TickRemote(_=>observer);
            Check(ReferenceEquals(originalBody,Remote(stock,stockBody))&&ReferenceEquals(originalFront,Remote(stock,stockFront))
                &&TownServiceMirror.VisibleParts665("face.6651|")==1&&TownServiceMirror.VisibleParts665(bodyAddress)==1,
                "actual next native revision2 transports the current reoffered original without a stock copy");
            File.AppendAllText(Path.Combine(_output,"scenarios.txt"),"no-gap-reoffer: actual delayed revision1 rejected; same private binding prep1 retained and transported by native flight2\n");
            yield break;
        }
        handoff.Card=null;TownServiceEnhancementHandoff.Returning.Add(new TownServiceEnhancementHandoff.ReturnPresentation
        { Card=native,Face=front,Body=body,StationRoot=owner,CardId=6651 });
        stockTick.Invoke(null,new object[]{owner});
        TownServiceMirror.UnregisterModule(81);TownServiceMirror.UnregisterModule(82);
        FastCapture withdrawn=CaptureFast();
        var privateWithdrawal=withdrawn.Artwork.Where(bytes=> {TownServiceCodec.TryRead(bytes,bytes.Length,out var f);return !f!.VisitorStock;}).ToArray();
        if(scenario=="close-before-stock"||scenario=="terminal-cold"||scenario=="new-private-session")Receive(2,privateWithdrawal);
        Check(originalFront.Root!=null&&originalBody.Root!=null&&originalFront.Root.gameObject.activeInHierarchy&&originalBody.Root.gameObject.activeInHierarchy,
            "same preannounced original survives source closure before stock receipt");
        object originalFrontHost=TownServiceMirror.Host665(2,82)??TownServiceMirror.HeldHost665(2,82)!;
        object originalBodyHost=TownServiceMirror.Host665(2,81)??TownServiceMirror.HeldHost665(2,81)!;
        Vector3 offeredFrontPosition=originalFront.Root.position,offeredBodyPosition=originalBody.Root.position;
        int firstReceipt=scenario=="late-stock-origin"?18:scenario=="lost-first"?12:Math.Max(6,originalsAt);
        bool dropped=false;int droppedEvents=0,acceptedCohorts=0,firstActivation=0;
        var trace=new System.Text.StringBuilder("frame,privateFront,stockFront,sameFront,privateBody,stockBody,sameBody,offeredBodyDelta,offeredFrontDelta\n");
        List<byte[]>? terminalOriginals=null;float started=FlightTime655.Now;
        for(int frame=1;frame<=108;frame++) {
            FlightTime655.Now=started+frame/90f;FlightTime655.Delta=1f/90f;native.StepNative655();
            if(frame==61&&scenario=="terminal-cold")Check(TownServiceMirror.TerminalGuards665(2),"exact pending native terminal rejects changed hand visibility pose canvas revision session and prior activation");
            if(frame==66&&scenario=="terminal-cold") {
                TownServiceMirror.RequestFullRefresh();FastCapture ended=CaptureFast();
                terminalOriginals=ended.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return f!.VisitorStock&&f.Module!=TownServiceFrame.ManifestModule;}).ToList();
                Check(terminalOriginals.Count==2&&terminalOriginals.All(b=>TownServiceMirror.StockOriginPresent665(b)),"actual post-end stock originals retain exact origin until stock lifecycle retires");
            }
            if(frame==originalsAt&&originalsAt!=0)Receive(2,(terminalOriginals??warm.Artwork).Where(bytes=>{TownServiceCodec.TryRead(bytes,bytes.Length,out var f);return f!.VisitorStock&&f.Module!=TownServiceFrame.ManifestModule;}));
            if(frame==18&&scenario=="late-stock-origin") {
                TownServiceMirror.RequestFullRefresh();FastCapture originRepair=CaptureFast();
                Receive(2,originRepair.Artwork);DeliverMotion(2,originRepair);
            }
            if(frame==17&&scenario=="new-private-session") {
                NetPlayerActors.Peer=1;TownServiceMirror.BeginSession(1,667,owner,owner);
                FastCapture switched=CaptureFast();Receive(2,switched.Artwork);DeliverMotion(2,switched);NetPlayerActors.Peer=3;
            }
            if(frame%6==0)TownServiceMirror.CaptureMotion((bytes,length,identity)=> {
                Check(TownServiceMotionCodec.TryRead(bytes,length,out var packet),"actual 113 native return packet decodes");
                if(scenario=="lost-first"&&!dropped&&packet!.Entries.Any(e=>e.Kind==10)) {dropped=true;droppedEvents++;return;}
                if(packet!.Entries.Any(e=>e.Kind==10))acceptedCohorts++;
                TownServiceMirror.ReceiveMotion(2,packet!);
            });
            TownServiceMirror.TickRemote(_=>observer);
            TownServiceBinding? flownFront=Remote(stock,stockFront),flownBody=Remote(stock,stockBody);
            trace.AppendLine($"{frame},{originalFront.Root.gameObject.activeInHierarchy},{flownFront?.Root.gameObject.activeInHierarchy},{ReferenceEquals(originalFront,flownFront)},{originalBody.Root.gameObject.activeInHierarchy},{flownBody?.Root.gameObject.activeInHierarchy},{ReferenceEquals(originalBody,flownBody)},{(originalBody.Root.position-offeredBodyPosition).magnitude},{(originalFront.Root.position-offeredFrontPosition).magnitude}");
            File.WriteAllText(Path.Combine(_output,"offered-return-identity-"+scenario+".csv"),trace.ToString());
            bool activated=ReferenceEquals(originalFront,flownFront)&&ReferenceEquals(originalBody,flownBody);
            if(activated&&firstActivation==0)firstActivation=frame;
            if(frame>=firstReceipt)Check(ReferenceEquals(originalFront,flownFront)&&ReferenceEquals(originalBody,flownBody),
                "same visible offered binding starts its independent stock native return");
            Check(originalFront.Root.gameObject.activeInHierarchy&&originalBody.Root.gameObject.activeInHierarchy,
                "same original front and body remain visible on every observer render");
            for(int image=0;image<originalImages.Length;image++)Check(originalImages[image]!=null
                &&ReferenceEquals(originalSprites[image],originalImages[image].sprite)
                &&ReferenceEquals(originalTextures[image],originalImages[image].mainTexture)
                &&ReferenceEquals(originalMaterials[image],originalImages[image].material),
                "same native offered sprite texture and material survive every return render");
            if(activated) {
                Check(ReferenceEquals(originalFrontHost,TownServiceMirror.Host665(stock,stockFront))&&ReferenceEquals(originalBodyHost,TownServiceMirror.Host665(stock,stockBody)),
                    "same visible offered Host starts its independent stock native return");
                Check(Remote(2,81)==null&&Remote(2,82)==null,"consumed private pending original never rebuilds after stock adoption");
                Vector3 bodyDelta=observer.InverseTransformPoint(originalBody.Root.position)-owner.InverseTransformPoint(body.position);
                Vector3 frontDelta=observer.InverseTransformPoint(originalFront.Root.position)-owner.InverseTransformPoint(front.position);
                Check(bodyDelta.magnitude<.00005f&&frontDelta.magnitude<.00005f,"actual native body and front world poses agree on every return render scenario="+scenario+" frame="+frame);
                Check(Quaternion.Angle(Quaternion.Inverse(observer.rotation)*originalBody.Root.rotation,Quaternion.Inverse(owner.rotation)*body.rotation)<.05f
                    &&Quaternion.Angle(Quaternion.Inverse(observer.rotation)*originalFront.Root.rotation,Quaternion.Inverse(owner.rotation)*front.rotation)<.05f,
                    "actual original body and front rotations follow the same native return on every render");
                Check((originalBody.Root.lossyScale-body.lossyScale).magnitude<.00002f
                    &&(originalFront.Root.lossyScale-front.lossyScale).magnitude<.00000002f,
                    "actual original body and front scales follow the same native return on every render");
            }
            else Check((originalFront.Root.position-offeredFrontPosition).magnitude<.00005f&&(originalBody.Root.position-offeredBodyPosition).magnitude<.00005f,
                "offered original remains at its coherent visible picture until complete return authority arrives scenario="+scenario+" frame="+frame);
            Check(TownServiceMirror.VisibleParts665("face.6651|")==1&&TownServiceMirror.VisibleParts665(bodyAddress)==1,
                "exactly one visible original front and body on every return render");
            yield return null;
        }
        Check(scenario!="lost-first"||droppedEvents==1,"loss scenario drops exactly one actual initial native return event");
        Check(firstActivation>0&&acceptedCohorts>0,"same originals activate only after actual complete native cohort receipt");
        File.AppendAllText(Path.Combine(_output,"scenarios.txt"),$"{scenario}: firstActivation={firstActivation}, droppedEvents={droppedEvents}, acceptedCohorts={acceptedCohorts}\n");
        if(scenario=="pre-withdraw") {
            Receive(2,privateWithdrawal);TownServiceEnhancementHandoff.Returning.Clear();
            stockTick.Invoke(null,new object[]{owner});FlightTime655.Now+=1f/90f;FastCapture stockClosed=CaptureFast();
            Receive(2,stockClosed.Artwork);TownServiceMirror.TickRemote(_=>observer);yield return null;
            TownServiceMirror.RegisterModule(81,1,body,address:bodyAddress);TownServiceMirror.RegisterModule(82,2,front,address:"face.6651|");
            handoff.Card=native;stockTick.Invoke(null,new object[]{owner});FlightTime655.Now+=1f/90f;
            FastCapture reoffer=CaptureFast();
            Receive(2,reoffer.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return !f!.VisitorStock&&f.Module==TownServiceFrame.ManifestModule;}));
            Receive(2,warm.Artwork.Where(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return !f!.VisitorStock&&f.Module!=TownServiceFrame.ManifestModule;}));
            TownServiceMirror.TickRemote(_=>observer);
            Check(Remote(2,81)==null&&Remote(2,82)==null,"late consumed origin cannot rebuild between reoffer census and its new preparation header");
            Receive(2,reoffer.Artwork);DeliverMotion(2,reoffer);TownServiceMirror.TickRemote(_=>observer);
            Check(Remote(2,81)!=null&&Remote(2,82)!=null,"same-session reoffer accepts its new exact preparation epoch");
            Check(!sampler(front,owner,null,out uint preparedAgain,out _)&&preparedAgain==1,"actual inactive native sampler preserves revision one for reoffer");
            Check(TownServiceMirror.OriginRevision665(2,82)==1,"new offered original carries exact actual preparation revision one");
            // A stationary low-priority fixture has no numeric root yet. Exercise
            // an actual owner-side yaw change after the real send interval.
            TownServiceMirror.SetPriority(82,true);
            card.localRotation*=Quaternion.Euler(0f,1f,0f);
            FlightTime655.Now+=.1f;FastCapture currentOffered=CaptureFast();
            Receive(2,currentOffered.Artwork);DeliverMotion(2,currentOffered);TownServiceMirror.TickRemote(_=>observer);
            File.WriteAllText(Path.Combine(_output,"current-offered-numeric.txt"),string.Join("\n",currentOffered.Motion.SelectMany(b=>{
                TownServiceMotionCodec.TryRead(b,b.Length,out var packet);return packet!.Entries.Select(e=>$"kind={e.Kind} lane={e.Lane} module={e.Module} sample={packet.SampleTime}");})));
            bool? epochGuard=TownServiceMirror.OfferedEpochGuard665(2,82);
            if(epochGuard.HasValue)Check(epochGuard.Value,"new offered preparation rejects old yaw samples and accepts its actual current root");
            float floor=TownServiceMirror.OriginFloor665(2,82);FlightTime655.Now+=.8f;
            FastCapture heartbeat=CaptureFast();Receive(2,heartbeat.Artwork);DeliverMotion(2,heartbeat);TownServiceMirror.TickRemote(_=>observer);
            Check(TownServiceMirror.OriginFloor665(2,82)==floor,"same preparation heartbeat preserves the offered motion lifetime floor");
            for(int epoch=2;epoch<=132;epoch++) {
                native.BeginNative655(card.position,.65f);native.Prepare665();FlightTime655.Now+=1f/90f;
                FastCapture prepared=CaptureFast();Receive(2,prepared.Artwork);DeliverMotion(2,prepared);TownServiceMirror.TickRemote(_=>observer);
                Check(TownServiceMirror.OriginRevision665(2,82)==(uint)epoch,"exact native bump becomes the next inactive preparation epoch");
                Check(TownServiceMirror.OriginCount665(2)==2,"reoffer without unregister evicts obsolete origins without disposing current originals");
                Check(TownServiceMirror.VisibleParts665("face.6651|")==1&&TownServiceMirror.VisibleParts665(bodyAddress)==1,
                    "reoffer without unregister retains one current original throughout bounded origin eviction");
            }
            File.AppendAllText(Path.Combine(_output,"scenarios.txt"),"reoffer: late consumed full baseline suppressed; epochs 1..132; origin cache=2; heartbeat floor stable\n");
        }
    }
}
