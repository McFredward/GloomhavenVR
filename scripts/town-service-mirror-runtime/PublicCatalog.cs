using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

public static partial class MirrorProgram
{
    private static IEnumerator PublicCatalogLanes()
    {
        DecisionFacingMotion();
        IEnumerator cabinet = CabinetFirstPress();
        while (cabinet.MoveNext()) yield return cabinet.Current;
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("dual lane owner").transform, observer = Go("dual lane observer").transform;
        Transform service = Go("private native service", owner).transform;
        Transform catalog = Go("public cabinet", owner).transform;
        Transform cassette = Go("Cassette", catalog).transform;
        Transform shutter = Go("Shutter", catalog).transform;
        Transform upper = Go("Upper", shutter).transform, lower = Go("Lower", upper).transform;
        upper.localPosition = new Vector3(0,.265f,0); lower.localPosition = new Vector3(0,-.265f,-.02f);
        TownServiceMirror.RegisterTemplate(2,1,service,address:"temple|");
        TownServiceMirror.RegisterTemplate(1,1,catalog,address:"merchant.rack|");
        TownServiceMirror.BeginSession(2,900,owner,service);
        TownServiceMirror.RegisterModule(10,1,service,address:"temple|");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1,901,owner,catalog);
            TownServiceMirror.RegisterModule(10,1,catalog,address:"merchant.rack|");
            TownServiceMirror.SetRack(10,new TownRackState {Cassette=true,Turn=1,From=0,To=1,Page=1,Elapsed=.425f});
            TownCassetteMotion.Apply(catalog,.5f);
        }
        var snapshots=Capture();
        Check(snapshots.Count(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var frame)&&frame!.Module==10)==2,
            "private service and public stock both publish identical module IDs without overwrite");
        Check(TownServiceMirror.RemoteSessions.Count==0,"local public stock creates no visitor workspace");
        NetPlayerActors.Peer=3; Receive(2,snapshots); TownServiceMirror.InteractionOwner(2);
        for(float until=Time.unscaledTime+.13f;Time.unscaledTime<until;)yield return null;
        TownServiceMirror.InteractionOwner(2); TownServiceMirror.TickRemote(_=>observer);
        Check(TownServiceMirror.PublicAuthor==2,"one lowest live stock author is elected");
        Check(TownServiceMirror.RemoteSessions.Count==1,"remote public stock is excluded from visitor census");
        Check(Remote(2,10)!=null&&Remote(-2,10)!=null,"private native service coexists with shared public cabinet");
        Transform remote=Remote(-2,10)!.Root;
        Check(Math.Abs(remote.Find("Cassette").localPosition.z-.32f)<.001f,
            "late public observer reconstructs cassette withdrawal from explicit clock");
        Check(Quaternion.Angle(remote.Find("Shutter/Upper").localRotation,Quaternion.identity)<.1f,
            "late public observer sees fully closed shutter at identity exchange");
        float start=Time.unscaledTime;
        while(Time.unscaledTime-start<.5f) {TownServiceMirror.TickRemote(_=>observer);yield return null;}
        Check(remote.Find("Cassette").localPosition==Vector3.zero,
            "public observer completes cassette extension without another owner packet");
        Check(TownServiceMirror.PublicRack?.Elapsed==TownRackState.TurnDuration&&TownServiceMirror.PublicRack?.Page==1,
            "authority handoff retains completed observer clock instead of rewinding stale owner sample");
        TownServiceMirror.EndSession(); Receive(2,Capture()); TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(2,10)==null&&Remote(-2,10)!=null,
            "closing private native window leaves public cabinet continuously visible");
        Transform prompt=Go("merchant purchase decision",owner).transform;
        var confirm=prompt.gameObject.AddComponent<UnityEngine.UI.Button>();
        int purchases=0;confirm.onClick.AddListener(()=>purchases++);
        TownServiceMirror.RegisterTemplate(1,2,prompt,address:"item.confirm.part.2|");
        TownServiceMirror.BeginSession(1,902,owner,prompt);
        TownServiceMirror.RegisterModule(11,2,prompt,address:"item.confirm.part.2|");
        Receive(2,Capture());TownServiceMirror.InteractionOwner(1);
        for(float until=Time.unscaledTime+.13f;Time.unscaledTime<until;)yield return null;
        TownServiceMirror.InteractionOwner(1);TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(2,11)!=null&&Remote(-2,10)!=null,
            "every peer sees the buyer's original purchase confirmation beside the same public cabinet");
        var copiedConfirm=Remote(2,11)!.Root.GetComponent<UnityEngine.UI.Button>();
        Check(copiedConfirm==null||!copiedConfirm.interactable,
            "remote purchase controls are visible but cannot execute the buyer's gameplay callback");
        Check(purchases==0,"observing the shared purchase decision never purchases an item");
        TownServiceMirror.ClaimPublicCatalog();
        Check(TownServiceMirror.IsPublicAuthor,"physical cabinet interaction promotes local presentation authority");
        TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(-2,10)==null,"authority promotion removes obsolete observer cabinet rather than duplicating it");
        using(TownServiceMirror.UsePublicLane()) TownServiceMirror.EndSession();
        Check(TownServiceMirror.PublicAuthor==2,"inactive local stock never blocks another player's active cabinet");
        TownServiceMirror.RemovePeer(2);
        Check(TownServiceMirror.PublicAuthor==int.MaxValue,"departed public owner leaves no stale invisible authority");
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;
        RollerLateJoin();
        IEnumerator secondary = SecondaryMerchantInspection();
        while (secondary.MoveNext()) yield return secondary.Current;
        IEnumerator donation = TempleCommitClock();
        while (donation.MoveNext()) yield return donation.Current;
    }

    private static IEnumerator TempleCommitClock()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("temple commit owner").transform;
        TownServiceMirror.BeginSession(2, 906, owner, owner);
        TownServiceMirror.SetLocalTempleDonationAvailable(true);
        TownServiceMirror.MarkLocalTempleDonationCommitted();
        for (float until = Time.unscaledTime + .2f; Time.unscaledTime < until;) yield return null;
        var packets = Capture();
        TownServiceFrame? manifest = null;
        foreach (byte[] packet in packets)
            if (TownServiceCodec.TryRead(packet, packet.Length, out var frame)
                && frame!.Module == TownServiceFrame.ManifestModule) manifest = frame;
        Check(manifest != null && manifest.HasTempleDonationCommitAge && manifest.TempleDonationCommitAge >= .18f,
            "native committed donation publishes its original effect age in the manifest");
        NetPlayerActors.Peer = 3; Receive(2, packets);
        var states = new List<TownTempleDonationState>(); TownServiceMirror.CollectTempleDonationStates(states);
        TownTempleDonationState remote = states.Single(state => state.Peer == 2);
        Check(remote.HasCommitAge && remote.Revision == 1 && remote.TransitionAge >= .18f,
            "remote donation keeps its owner's commit age instead of starting a new blessing on receipt");
        Receive(2, packets); TownServiceMirror.CollectTempleDonationStates(states);
        Check(states.Single(state => state.Peer == 2).TransitionAge >= remote.TransitionAge,
            "repeated donation manifests never rewind the shared blessing clock");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }

    private static IEnumerator CabinetFirstPress()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("first press owner").transform, observer = Go("first press observer").transform;
        Transform rack = Go("public rack", owner).transform;
        Go("Cassette", rack);
        var clip = AudioClip.Create("native cabinet mechanism boundary", 88200, 1, 44100, false);
        Assets.Add(clip); GloomhavenVR.WorldUI.TownServiceAssets.Cabinet = clip;
        var audio = new GloomhavenVR.WorldUI.TownServiceCabinetAudio(rack);
        using var initial = new TownServiceBinding(rack);
        TownServiceMirror.RegisterTemplate(1, 1, rack, address: "merchant.rack|");
        audio.Begin(1, .04f);
        using var pressed = new TownServiceBinding(rack);
        Check(pressed.Structure == initial.Structure && pressed.Nodes.Length == initial.Nodes.Length
            && rack.Find("Town.MerchantCabinet.Foley") == null,
            "first category sound does not change original rack topology");
        AudioSource source = owner.Find("Town.MerchantCabinet.Foley").GetComponent<AudioSource>();
        Check(source.isPlaying && source.time >= .039f,
            "synchronized first category epoch starts the spatial mechanism at the authored phase");
        owner.position = new Vector3(2f, 3f, 4f); audio.Tick();
        Check(Vector3.Distance(source.transform.position, rack.position) < .0001f,
            "uncaptured cabinet sound origin follows the shared map mechanism");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, 903, owner, rack);
            TownServiceMirror.RegisterModule(10, 1, rack, address: "merchant.rack|");
            TownServiceMirror.SetRack(10, new TownRackState { Cassette = true, Turn = 1, From = 0,
                To = 256, Page = 0, Elapsed = .04f, Members = new[] { new TownRackMember(99, 256, false) } });
        }
        var packets = Capture(); NetPlayerActors.Peer = 3;
        Receive(2, packets); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(-2, 10) != null && Remote(-2, 10)!.Structure == initial.Structure,
            "first peer category press rebuilds the same public rack instead of an empty cabinet");
        for (float until = Time.unscaledTime + TownRackState.TurnDuration + .05f; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(TownServiceMirror.PublicRack?.Elapsed == TownRackState.TurnDuration
            && TownServiceMirror.PublicRack?.Page == 256,
            "missing observer artwork never permanently disables the local public input proxy");
        Transform warmRack = Go("late-loaded cabinet sound", owner).transform;
        var warmAudio = new GloomhavenVR.WorldUI.TownServiceCabinetAudio(warmRack);
        GloomhavenVR.WorldUI.TownServiceAssets.Cabinet = null;
        warmAudio.Begin(2, .04f);
        for (float until = Time.unscaledTime + .12f; Time.unscaledTime < until;) yield return null;
        GloomhavenVR.WorldUI.TownServiceAssets.Cabinet = clip; warmAudio.Tick();
        AudioSource warmSource = (AudioSource)typeof(GloomhavenVR.WorldUI.TownServiceCabinetAudio)
            .GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(warmAudio)!;
        Check(warmSource != null && warmSource.isPlaying && warmSource.time >= .15f,
            "late-loaded cabinet clip joins its pending owner epoch at the current sound phase");
        warmAudio.Dispose();
        audio.Dispose(); GloomhavenVR.WorldUI.TownServiceAssets.Cabinet = null;
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        yield return null;
        Check(source == null, "cabinet teardown destroys its external playback emitter");
    }

    private static void DecisionFacingMotion()
    {
        Transform prompt = Go("decision rotation probe").transform;
        var facing = new TownServiceMotion(prompt, Array.Empty<Transform>(), "item.confirm.part.2|");
        facing.BeforeApply(0f);
        prompt.localRotation = Quaternion.Euler(0f, 90f, 0f);
        facing.AfterApply(0f, .2f);
        facing.Tick(.12f);
        float between = Quaternion.Angle(Quaternion.identity, prompt.localRotation);
        Check(between > 35f && between < 80f,
            "palm decision rotates continuously between 5 Hz owner samples");
        facing.Tick(.25f);
        Check(Quaternion.Angle(prompt.localRotation, Quaternion.Euler(0f, 90f, 0f)) < .1f,
            "palm decision reaches its exact authored target on a bounded clock");
        UnityEngine.Object.DestroyImmediate(prompt.gameObject);

        Transform crank = Go("discrete crank rotation probe").transform;
        var discrete = new TownServiceMotion(crank, Array.Empty<Transform>(), "merchant.crank|");
        discrete.BeforeApply(0f);
        crank.localRotation = Quaternion.Euler(0f, 90f, 0f);
        discrete.AfterApply(0f, .2f);
        discrete.Tick(.12f);
        Check(Quaternion.Angle(crank.localRotation, Quaternion.Euler(0f, 90f, 0f)) < .1f,
            "discrete cabinet controls retain their prior prompt response");
        UnityEngine.Object.DestroyImmediate(crank.gameObject);
    }

    private static IEnumerator SecondaryMerchantInspection()
    {
        // Four simulated players: one elected merchant visitor, a second visitor
        // opening/holding their own original item fan, a public stock author, and
        // an observer. The observer must see all three without a duplicate stand.
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 2;
        Transform electedFrame = Go("merchant first visitor frame").transform;
        Transform electedZone = Rect("elected palm", electedFrame, Vector2.zero, new Vector2(80, 90));
        TownServiceMirror.RegisterTemplate(1, 1, electedZone, address:"merchant.offering|");
        TownServiceMirror.BeginSession(1, 1102, electedFrame, electedFrame, 6f);
        TownServiceMirror.RegisterModule(10, 1, electedZone, address:"merchant.offering|");
        List<byte[]> electedPackets = Capture();

        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 4;
        Transform visitorFrame = Go("merchant second visitor frame").transform;
        Transform heldFace = Rect("original held item face", visitorFrame, new Vector2(12, 35), new Vector2(65, 95));
        heldFace.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.25f, .72f, .84f);
        Transform heldBody = Rect("original held item backing", visitorFrame, new Vector2(12, 35), new Vector2(65, 95));
        Transform fanFace = Rect("original second item in open fan", visitorFrame, new Vector2(-45, 30), new Vector2(65, 95));
        fanFace.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.84f, .47f, .25f);
        Transform secondZone = Rect("second palm", visitorFrame, Vector2.zero, new Vector2(80, 90));
        Transform duplicateMount = Rect("private catalog mount", visitorFrame, Vector2.zero, new Vector2(80, 90));
        Transform duplicateStockFace = Rect("private catalog card", duplicateMount, Vector2.zero, new Vector2(65, 95));
        TownServiceMirror.RegisterTemplate(1, 1, heldFace, address:"item.41|");
        TownServiceMirror.RegisterTemplate(1, 1, heldBody, address:"inspectionbody.3dcccccd.3e99999a.p|");
        TownServiceMirror.RegisterTemplate(1, 1, fanFace, address:"item.42|");
        TownServiceMirror.RegisterTemplate(1, 1, secondZone, address:"merchant.offering|");
        TownServiceMirror.RegisterTemplate(1, 1, duplicateMount, address:"merchant.cardmount|");
        TownServiceMirror.RegisterTemplate(1, 1, duplicateStockFace, address:"item.43|");
        TownServiceMirror.BeginSession(1, 1104, visitorFrame, visitorFrame, 1f);
        TownServiceMirror.RegisterModule(11, 1, heldFace, address:"item.41|");
        TownServiceMirror.RegisterModule(12, 1, heldBody, address:"inspectionbody.3dcccccd.3e99999a.p|");
        TownServiceMirror.RegisterModule(13, 1, secondZone, address:"merchant.offering|");
        TownServiceMirror.RegisterModule(14, 1, fanFace, address:"item.42|");
        TownServiceMirror.RegisterModule(15, 1, duplicateMount, address:"merchant.cardmount|");
        TownServiceMirror.RegisterModule(16, 1, duplicateStockFace, address:"item.43|");
        List<byte[]> visitorPackets = Capture();

        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 5;
        Transform stockFrame = Go("merchant stock author frame").transform;
        Transform stock = Rect("public original cabinet", stockFrame, Vector2.zero, new Vector2(270, 180));
        TownServiceMirror.RegisterTemplate(1, 1, stock, address:"merchant.rack|");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, 1205, stockFrame, stockFrame);
            TownServiceMirror.RegisterModule(10, 1, stock, address:"merchant.rack|");
        }
        List<byte[]> stockPackets = Capture();

        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 3;
        Transform observer = Go("merchant three-peer observer").transform;
        TownServiceMirror.RegisterTemplate(1, 1, electedZone, address:"merchant.offering|");
        TownServiceMirror.RegisterTemplate(1, 1, heldFace, address:"item.41|");
        TownServiceMirror.RegisterTemplate(1, 1, heldBody, address:"inspectionbody.3dcccccd.3e99999a.p|");
        TownServiceMirror.RegisterTemplate(1, 1, fanFace, address:"item.42|");
        TownServiceMirror.RegisterTemplate(1, 1, stock, address:"merchant.rack|");
        Receive(2, electedPackets); Receive(4, visitorPackets); Receive(5, stockPackets);
        TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        Check(TownServiceMirror.InteractionOwner(1) == 2, "older merchant visitor authors the one shared palm");
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(-5, 10) != null && Remote(2, 10) != null,
            "observer retains public cabinet and elected shared merchant interaction");
        Check(Remote(4, 11) != null && Remote(4, 12) != null,
            "non-elected visitor's original held item face and backing remain visible to third player");
        Check(Remote(4, 14) != null,
            "non-elected visitor's opened original item fan remains visible to third player");
        Check(Remote(4, 13) == null && Remote(4, 15) == null && Remote(4, 16) == null,
            "non-elected visitor does not create a second palm or duplicate private cabinet cards");
        TownServiceMirror.RemovePeer(2); TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        Check(TownServiceMirror.InteractionOwner(1) == 4,
            "merchant interaction authorship transfers to the remaining visitor");
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(4, 11) != null && Remote(4, 12) != null && Remote(4, 14) != null
            && Remote(4, 13) != null && Remote(-5, 10) != null,
            "handover keeps the visitor's held original item, open fan and one public cabinet");
        Check(Remote(4, 15) == null && Remote(4, 16) == null,
            "handover never exposes a second private cabinet over public stock");

        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 6;
        Transform late = Go("late merchant observer").transform;
        TownServiceMirror.RegisterTemplate(1, 1, secondZone, address:"merchant.offering|");
        TownServiceMirror.RegisterTemplate(1, 1, heldFace, address:"item.41|");
        TownServiceMirror.RegisterTemplate(1, 1, heldBody, address:"inspectionbody.3dcccccd.3e99999a.p|");
        TownServiceMirror.RegisterTemplate(1, 1, fanFace, address:"item.42|");
        TownServiceMirror.RegisterTemplate(1, 1, stock, address:"merchant.rack|");
        Receive(4, visitorPackets); Receive(5, stockPackets);
        TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => late);
        Check(Remote(4, 11) != null && Remote(4, 12) != null && Remote(4, 14) != null
            && Remote(4, 13) != null && Remote(-5, 10) != null,
            "late fourth observer reconstructs current cabinet, held item, fan and merchant palm");
        Check(Remote(4, 15) == null && Remote(4, 16) == null,
            "late observer never reconstructs a duplicate visitor cabinet");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }
    private static void RollerLateJoin()
    {
        foreach (int direction in new[] {-1, 1}) foreach (float phase in new[] {.12f, .5f, .82f})
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer=1;
            Transform owner=Go("roller owner").transform, observer=Go("roller observer").transform;
            owner.localScale=Vector3.one*1.4f; observer.localScale=owner.localScale;
            observer.rotation=Quaternion.Euler(0,71,0);
            Transform cabinet=Go("roller cabinet",owner).transform, cassette=Go("Cassette",cabinet).transform;
            for(int row=0;row<3;row++)
            {
                Transform holder=Go("Row"+row,cassette).transform;
                for(int card=0;card<4;card++)
                {
                    Transform face=Go("OriginalCard"+card,holder).transform;
                    face.localPosition=new Vector3((card-1.5f)*.18f,0,-.025f);
                }
            }
            Transform indicator=Go("PageIndicator",cabinet).transform;
            var caption=Rect("Caption",indicator,Vector2.zero,new Vector2(145,110)).gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            caption.text="wrong later page";
            TownServiceMirror.RegisterTemplate(1,1,cabinet,address:"merchant.rack|");
            using(TownServiceMirror.UsePublicLane())
            {
                TownServiceMirror.BeginSession(1,950,owner,cabinet);
                TownServiceMirror.RegisterModule(10,1,cabinet,address:"merchant.rack|");
                TownServiceMirror.SetRack(10,new TownRackState{Cassette=true,ScrollDirection=(sbyte)direction,
                    PageCount=2,Turn=1,From=0,To=1,Page=(ushort)(phase<.5f?0:1),Elapsed=phase*TownRackState.TurnDuration});
                TownCassetteMotion.Apply(cabinet,phase,direction);
            }
            var captured=Capture();NetPlayerActors.Peer=3;Receive(2,captured);TownServiceMirror.TickRemote(_=>observer);
            Transform mirrored=Remote(-2,10)!.Root;
            for(int row=0;row<3;row++)
            {
                Transform original=cabinet.Find("Cassette/Row"+row), copy=mirrored.Find("Cassette/Row"+row);
                Check(Vector3.Distance(original.localPosition,copy.localPosition)<.0001f
                    &&Quaternion.Angle(original.localRotation,copy.localRotation)<.01f,
                    "late observer reconstructs exact owner holder translation and hinge angle in either scroll direction");
                for(int card=0;card<4;card++) foreach(Vector3 corner in new[]{new Vector3(-.07f,-.056f,0),new Vector3(.07f,.056f,0)})
                {
                    Vector3 expected=owner.InverseTransformPoint(original.Find("OriginalCard"+card).TransformPoint(corner));
                    Vector3 actual=observer.InverseTransformPoint(copy.Find("OriginalCard"+card).TransformPoint(corner));
                    Check(Vector3.Distance(expected,actual)<.0002f,
                        "remote original card corners follow owner roller at intermediate poses without viewer-facing changes");
                }
            }
            Check(mirrored.Find("PageIndicator/Caption").GetComponent<TMPro.TMP_Text>().text=="↑\n"+(phase<.5f?1:2)+" / 2\n↓",
                "page counter follows displayed clock instead of an unrelated late native text sample");
            Check(TownServiceMirror.PublicRack?.ScrollDirection==direction&&TownServiceMirror.PublicRack?.PageCount==2,
                "public authority handoff retains scroll direction and complete page count");
        }
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;
    }

}

public static partial class MirrorProgram
{
    private static void InspectionPublisher()
    {
        var owner = Go("inspection owner").transform;
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork();
        GloomhavenVR.WorldUI.TownServicePresentation.Active = false;
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = true;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Session = 500;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.StationRoot = owner;
        for(int i=0;i<512;i++)
        {
            var chip=Go("owned item",owner).AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
            chip.Item=new GloomhavenVR.Cards.ItemsPile.Item {ID=1000+i};
            chip.NativeItemCard=Go("original item face",chip.transform).AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
            chip.NativeItemCard.CardID=1000+i;
            chip.InspectionBody=Go("original item backing",chip.transform).transform;
            GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(chip);
        }
        Transform offeringSeat = Go("offering outside fan", owner).transform;
        offeringSeat.localPosition = new Vector3(.7f, .17f, -.5f);
        var offered = GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips[0];
        offered.transform.SetParent(offeringSeat, false); offered.TownOffering = true;
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        var calls=GloomhavenVR.WorldUI.TownServiceSync.Calls;
        Check(calls.Count==1024,"closed native shop publishes all 512 owned faces and original backings exactly once");
        Check(calls.Count(c=>c.Source==offered.NativeItemCard!.transform)==1
            && calls.Count(c=>c.Source==offered.InspectionBody)==1,
            "original offered face and body publish after leaving the wrist fan hierarchy");
        foreach(var chip in GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips)
        {
            Check(calls.Count(c=>c.Source==chip.NativeItemCard!.transform)==1&&calls.Count(c=>c.Source==chip.InspectionBody)==1,
                "owned item publisher uses each actual original face and backing");
            Check(!calls.Any(c=>c.Source==chip.transform),"owned item mount is not duplicated as a second physical card");
        }
        object instance=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("Private",PrivateStatic)!.GetValue(null)!;
        var generation=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("_generation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var priority = (List<Transform>)typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("PriorityRoots",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        Check(priority.Contains(offered.NativeItemCard!.transform), "floating owned offering has animation publication priority");
        var prompt = new GloomhavenVR.WorldUI.TownServicePalmConfirmation.Entry { Service = 1, Seat = offeringSeat };
        for (int part = 0; part < 4; part++)
        {
            var surface = new GloomhavenVR.WorldUI.TownServiceSurface { Id = (ushort)(60 + part) };
            surface.Panel.Target = Go("original merchant decision " + part, offeringSeat).transform;
            prompt.Surfaces.Add(surface);
        }
        GloomhavenVR.WorldUI.TownServicePalmConfirmation.Active.Add(prompt);
        calls.Clear();GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        foreach (var surface in prompt.Surfaces)
        {
            Check(calls.Count(c => c.Key == "item.confirm.part." + (surface.Id - 60) && c.Source == surface.Panel.Target) == 1,
                "inspection-only merchant retains each original palm confirmation part exactly once");
            Check(priority.Contains(surface.Panel.Target), "inspection-only palm confirmation receives motion priority");
        }
        GloomhavenVR.WorldUI.TownServicePalmConfirmation.Active.Clear();
        object before=generation.GetValue(instance)!;
        GloomhavenVR.WorldUI.TownServicePresentation.Active=true;
        GloomhavenVR.WorldUI.TownServicePresentation.Service=1;
        GloomhavenVR.WorldUI.TownServicePresentation.Session=5000;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,owner);
        Check(before.Equals(generation.GetValue(instance)),"opening native purchase confirmation retains the owned fan publication lifetime");
        GloomhavenVR.WorldUI.TownServicePresentation.Active=false;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        Check(before.Equals(generation.GetValue(instance)),"closing native shop does not restart owned item fan baselines");
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active=false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleCount==0,"leaving merchant retires owned inspection publication");
    }
}
