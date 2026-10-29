using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class Proof
{
    internal static int Checks;
    internal static void Check(bool value,string message) {Checks++;if(!value)throw new Exception(message);}
    internal static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.00001f;
    internal static bool Same(Quaternion a,Quaternion b)=>Quaternion.Angle(a,b)<.08f;
    internal static readonly RigPose HostPose=new() {Position=new Vector3(31.3f,42.7f,-19.2f),Rotation=Quaternion.Euler(0,123,0)};
    internal static readonly RigPose HandPose=new() {Position=new Vector3(-21.7f,18.3f,55.1f),Rotation=Quaternion.Euler(0,17,0)};
    internal static void Configure(int id,bool host=false,bool knownHost=true)
    {
        FFSNetwork.IsOnline=true;FFSNetwork.IsHost=host;NetPlayerActors.Id=id;
        NetSession.FlatNetMode=false;VersionGuard.Fresh(knownHost?new[]{1,2,3,4}:new[]{2,3,4});
        SharedWindowSpawnAuthority.Bind(()=>true);SharedWindowMoves.Bind(()=>false);ModalFallback.Ready=true;
        PanelLayout.Available=true;PanelLayout.Origin=new Vector3(-81,9,33);
        PanelLayout.Yaw=Quaternion.Euler(0,277,0);PanelLayout.WorldScale=23.39f;
        MapRoomDriver.Active=false;Time.unscaledTime=10;Time.frameCount=100;
        SharedWindows.Grabs.Clear();SharedWindows.Windows.Clear();
        foreach(SharedWindowKind kind in Enum.GetValues<SharedWindowKind>())
        {
            if(kind==SharedWindowKind.None)continue;
            SharedWindows.Grabs[kind]=new GrabbableModal();
            SharedWindows.Windows[kind]=new UnityEngine.UI.UIWindow();
        }
    }
    internal static SharedWindowPoseContext Context(int sender,int kind,bool initial)
        =>new() {Host=sender==1,Initial=initial?SharedWindowPoseContext.KindBit(kind):(byte)0,
            Origin=new Vector3(8,3,-14),Yaw=91,Scale=7.22f};
    internal static RigPose Relative(RigPose world,SharedWindowPoseContext context)
    {
        Quaternion yaw=Quaternion.Euler(0,context.Yaw,0);
        return new RigPose {Position=Quaternion.Inverse(yaw)*(world.Position-context.Origin)/context.Scale,
            Rotation=Quaternion.Inverse(yaw)*world.Rotation};
    }
    internal static SharedWindowPoseContext? Wire(SharedWindowPoseContext? context)
    {
        if(!context.HasValue)return null;
        var bytes=new byte[26];int at=1;
        Check(SharedWindowPoseContext.Write(bytes,ref at,context.Value)&&at==25&&bytes[1]==119&&bytes[2]==22,
            "actual additive context writer emits the unchanged 22-byte source frame");
        Check(SharedWindowPoseContext.TryRead(bytes,3,22,out SharedWindowPoseContext read),
            "actual context parser reads the same packet's source frame");
        return read;
    }
    internal static SharedWindowScaleContext? WireScales(SharedWindowScaleContext? scales)
    {
        if(!scales.HasValue)return null;
        var bytes=new byte[SharedWindowScaleContext.MaxPayloadBytes+2];int at=0;
        Check(SharedWindowScaleContext.Write(bytes,ref at,scales.Value)&&bytes[0]==121,
            "actual additive121 writer emits the captured window references");
        Check(SharedWindowScaleContext.TryRead(bytes,2,bytes[1],out SharedWindowScaleContext read),
            "actual additive121 parser reads the same presence cohort");
        return read;
    }
    internal static SharedWindowScaleContext? ScalePacket(int kind,float reference=198.12f)
    {
        var scales=new SharedWindowScaleContext();scales.Set(kind,reference);
        return WireScales(scales);
    }
    internal static MapStoryOpening[] Opening(int sender,uint key,bool reset=false)
    {
        if(reset)
        {
            MapStoryLifecycle.Current=new object();MapStoryLifecycle.Ledger=new MapStoryOpeningLedger(112);
            MapStoryLifecycle.Ledger.Open(MapStoryLifecycle.Current,key,key,1,Array.FindAll(new[]{1,2,3,4},id=>id!=NetPlayerActors.Id));
        }
        var remote=new MapStoryOpeningLedger((uint)(700+sender));var subject=new object();
        remote.Open(subject,key,key,1,Array.FindAll(new[]{1,2,3,4},id=>id!=sender));
        MapStoryOpening[] entries=remote.Sample(subject);
        MapStoryLifecycle.Ledger.Observe(sender,entries);
        return entries;
    }
    internal static PresenceState Story(int sender,RigPose pose,bool initial,byte stamp=1,
        SharedWindowPoseContext? context=null,bool contextMissing=false,uint? contentKey=null,
        float reference=198.12f,bool scalesMissing=false)
    {
        uint key=contentKey??RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs);
        SharedWindowPoseContext source=context??Context(sender,1,initial);
        RigPose relative=Relative(pose,source);
        return new PresenceState {HasStorySync=true,StoryFlags=3,StoryKey=key,StoryPage=0,StoryPageCount=1,
            StoryPoseStamp=stamp,StorySizeCode=127,StoryPose=relative,
            WindowPoseContext=contextMissing?null:Wire(source),WindowScales=scalesMissing?null:ScalePacket(1,reference),HasMapStoryLifecycle=true,
            MapStoryLifecycleEntries=Opening(sender,key)};
    }
    internal static void Snap(GrabbableModal grab,Vector3 pos,Quaternion rot,float factor)
    {grab.PlaceFrameAt(pos,rot);grab.Root.localScale=Vector3.one*factor;grab.Visible=true;}
}

namespace GloomhavenVR.Net
{
    internal static partial class RemoteStorySync
    {
        internal static void Fresh()
        {
            Peers.Clear();PeerPoseStamp.Clear();PeerPoseChangedAt.Clear();PoseTrack.Reset();
            _poseTrackPeer=_followingPeer=0;_localKey=0;_localPageCount=_localPoseStamp=_followedStamp=0;
            _haveFrameBaseline=_localPoseOwned=_localMoving=_followedStampValid=false;
            _lastFramePos=default;_lastFrameRot=Quaternion.identity;_lastFrameSize=1;
            _localMoveSettleAt=_finishedUntil=0;CurrentBox=new UICharacterStoryBox();
        }
        internal static bool Owns=>_localPoseOwned||_localMoving;
        internal static void Apply()=>ResolvePose(HashDialog(CurrentBox!.dialogs));
    }
    internal static partial class RemoteMapStory
    {
        internal static void FreshReward()=>ResetRewardPose();
        internal static void Fresh()
        {
            StoryLocal.Reset();QuestLocal.Reset();EncounterLocal.Reset();ResetReflow();
            StoryPeers.Clear();QuestPeers.Clear();EncounterPeers.Clear();
            StoryStamp.Clear();QuestStamp.Clear();EncounterStamp.Clear();
            StoryStampAt.Clear();QuestStampAt.Clear();EncounterStampAt.Clear();
            CurrentBox=new UICharacterStoryBox();CurrentQuest=new UIQuestPopup();CurrentEvent=new UIEventPanel();
        }
        private static Local ProofLocal(SharedWindowKind kind)=>kind switch {SharedWindowKind.MapStory=>StoryLocal,SharedWindowKind.QuestConfirm=>QuestLocal,_=>EncounterLocal};
        internal static uint Key(SharedWindowKind kind)=>kind switch {SharedWindowKind.MapStory=>RemoteStorySync.HashDialog(CurrentBox!.dialogs),SharedWindowKind.QuestConfirm=>CurrentQuest!.Key,_=>CurrentEvent!.Key};
        internal static void SetNativeKey(SharedWindowKind kind) => ProofLocal(kind).Key=Key(kind);
        internal static void SampleMotionForProof(ref PresenceState presence) => SampleReflow(ref presence);
        internal static bool ReflowForProof(SharedWindowKind kind) => ProofLocal(kind).Reflow;
        internal static bool Owns(SharedWindowKind kind)=>ProofLocal(kind).PoseOwned||ProofLocal(kind).Moving;
        internal static bool Adopted(SharedWindowKind kind)=>ReferenceEquals(ProofLocal(kind).Grab,SharedWindows.Grabs[kind])&&ProofLocal(kind).SwapFrame==int.MinValue;
        internal static SharedWindowEntry Publish(SharedWindowKind kind)
        {
            Local local=ProofLocal(kind);TrackFrame(kind,local,false);
            var result=new SharedWindowEntry {Kind=(byte)((int)kind-1),ContentKey=Key(kind),Flags=1};
            WritePose(kind,local,ref result);return result;
        }
        internal static void Apply(SharedWindowKind kind)
        {
            Local local=ProofLocal(kind);
            ResolvePose(kind,local,Key(kind),kind switch {SharedWindowKind.MapStory=>StoryPeers,SharedWindowKind.QuestConfirm=>QuestPeers,_=>EncounterPeers},
                kind switch {SharedWindowKind.MapStory=>StoryStampAt,SharedWindowKind.QuestConfirm=>QuestStampAt,_=>EncounterStampAt});
        }
        internal static PresenceState Peer(int sender,SharedWindowKind kind,RigPose pose,bool initial,byte stamp=1,
            bool missing=false,byte? explicitFrame=null,uint? key=null,float reference=198.12f,bool scalesMissing=false)
        {
            SharedWindowPoseContext context=Proof.Context(sender,(int)kind,initial);
            RigPose relative=Proof.Relative(pose,context);
            byte frame=explicitFrame??NetProtocol.SharedFrameSeatAnchor;
            if(frame==NetProtocol.SharedFrameParchment)
                relative=new RigPose {Position=(pose.Position-MapRoomDriver.Center)/MapRoomDriver.Scale,Rotation=pose.Rotation};
            var entry=new SharedWindowEntry {Kind=(byte)((int)kind-1),ContentKey=key??Key(kind),Flags=3,
                PoseStamp=stamp,SizeCode=127,Frame=frame,Pose=relative};
            return new PresenceState {HasSharedWindow=true,SharedWindowCount=1,SharedWindowEntries=new[]{entry},
                WindowPoseContext=missing?null:Proof.Wire(context),WindowScales=scalesMissing?null:Proof.ScalePacket((int)kind,reference),HasMapStoryLifecycle=true,
                MapStoryLifecycleEntries=Proof.Opening(sender,entry.ContentKey)};
        }
    }
}

internal static class Program
{
    private static void Main()
    {
        Authority();ContextCases();Scenario();Map();Video();ReferencePipeline();MoveOrderPairs.Run();
        Console.WriteLine($"Shared window pose runtime: {Proof.Checks} production assertions passed.");
    }
    private static void Authority()
    {
        foreach(int viewer in new[]{1,2,3,4})
        {
            Proof.Configure(viewer,viewer==1);
            Proof.Check(SharedWindowSpawnAuthority.AuthorId==1,"compatible native host is the single initial placement author");
            Proof.Check(SharedWindowSpawnAuthority.IsInitialAuthor()==(viewer==1),"observers never author their own initial window");
        }
        foreach(int viewer in new[]{2,3,4})
        {
            Proof.Configure(viewer,knownHost:false);
            Proof.Check(SharedWindowSpawnAuthority.AuthorId==2,"flat host uses one compatible VR surrogate from the actual roster collector");
            Proof.Check(SharedWindowSpawnAuthority.IsInitialAuthor()==(viewer==2),"flat host does not make every VR visitor an author");
        }
        Proof.Configure(0,knownHost:false);
        VersionGuard.Fresh();
        Proof.Check(!SharedWindowSpawnAuthority.IsInitialAuthor(),"unknown local player identity cannot reveal a provisional personal spawn");
        Proof.Configure(3);SharedWindowSpawnAuthority.Bind(()=>false);
        Proof.Check(SharedWindowSpawnAuthority.IsInitialAuthor(),"disabled transport preserves usable local native UI");
        SharedWindowSpawnAuthority.Bind(()=>true);NetSession.FlatNetMode=true;
        Proof.Check(SharedWindowSpawnAuthority.IsInitialAuthor(),"explicit flat-net mode preserves local native UI");
        Proof.Configure(3);
        foreach(int kind in new[]{1,2,3,4,5,6})
        {
            var context=Proof.Context(1,kind,true);
            Proof.Check(SharedWindowSpawnAuthority.AcceptInitial(1,context,kind),"current host initial state is accepted for its own window kind");
            Proof.Check(!SharedWindowSpawnAuthority.AcceptInitial(1,context,kind==6?1:kind+1),"current host initial state is limited to its own window kind");
            Proof.Check(!SharedWindowSpawnAuthority.AcceptInitial(2,context,kind),"another sender cannot claim host initial placement");
            Proof.Check(!SharedWindowSpawnAuthority.AcceptInitial(3,Proof.Context(3,kind,true),kind),"another sender cannot claim host initial placement");
            context.Host=false;Proof.Check(!SharedWindowSpawnAuthority.AcceptInitial(1,context,kind),"native-host provenance must agree with its sender");
        }
    }
    private static void ContextCases()
    {
        foreach(float sourceZoom in new[]{7.22f,12.22f,23.39f,100f})
        foreach(float sourceYaw in new[]{0f,91f,180f,277f})
        {
            var context=Proof.Context(1,1,true);context.Scale=sourceZoom;context.Yaw=sourceYaw;
            RigPose relative=Proof.Relative(Proof.HostPose,context);
            SharedWindowPoseContext decoded=Proof.Wire(context)!.Value;
            Proof.Check(decoded.ToWorld(relative,out RigPose actual)&&Proof.Same(actual.Position,Proof.HostPose.Position)&&Proof.Same(actual.Rotation,Proof.HostPose.Rotation),
                "record119 reconstructs the sender's canonical world pose independent of every observer's seat and zoom");
        }
        var invalid=Proof.Context(1,1,true);invalid.Scale=float.NaN;
        Proof.Check(!invalid.ToWorld(Proof.HostPose,out _),"invalid context never produces an accepted world pose");
        var state=new PresenceState {HasStorySync=true,StoryFlags=3,StoryInitialPose=true,
            HasSharedWindow=true,SharedWindowCount=3,SharedWindowEntries=new[]{
                new SharedWindowEntry{Kind=1,Flags=3,InitialPose=true},new SharedWindowEntry{Kind=2,Flags=3,InitialPose=true},new SharedWindowEntry{Kind=3,Flags=3,InitialPose=true}},
            HasVideoWindow=true,VideoWindow=new FixtureVideo{Window=new SharedWindowEntry{Flags=3,InitialPose=true}},
            HasRewardWindow=true,RewardWindow=new RewardWindowState{Window=new SharedWindowEntry{Flags=3,InitialPose=true}}};
        Proof.Configure(1,true);SharedWindowSpawnAuthority.SampleContext(ref state);
        Proof.Check(state.WindowPoseContext.HasValue&&state.WindowPoseContext.Value.Initial==63&&state.WindowPoseContext.Value.Host,
            "actual presence sampler tags all six initial window kinds atomically with the sender frame");
        state=default;SharedWindowSpawnAuthority.SampleContext(ref state);
        Proof.Check(!state.WindowPoseContext.HasValue,"idle presence does not invent a window context record");
    }
    private static void Scenario()
    {
        Proof.Configure(1,true);RemoteStorySync.Fresh();
        var host=SharedWindows.Grabs[SharedWindowKind.ScenarioStory];Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
        var sampled=new PresenceState();RemoteStorySync.Sample(ref sampled);SharedWindowSpawnAuthority.SampleContext(ref sampled);
        Proof.Check(sampled.StoryInitialPose&&(sampled.StoryFlags&2)!=0&&sampled.WindowPoseContext.HasValue,"native host publishes its first untouched shared window without inventing a manual drag");
        Proof.Configure(2);RemoteStorySync.Fresh();
        uint key=RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs);Proof.Opening(1,key,true);
        var follower=SharedWindows.Grabs[SharedWindowKind.ScenarioStory];follower.Visible=false;
        var empty=new PresenceState();RemoteStorySync.Sample(ref empty);
        Proof.Check((empty.StoryFlags&2)==0&&!RemoteStorySync.Owns,"hidden provisional follower frame cannot publish a manual move");
        sampled.WindowPoseContext=Proof.Wire(sampled.WindowPoseContext);sampled.WindowScales=Proof.WireScales(sampled.WindowScales);sampled.HasMapStoryLifecycle=true;sampled.MapStoryLifecycleEntries=Proof.Opening(1,key);
        RemoteStorySync.Observe(1,sampled);
        Proof.Check(RemoteStorySync.TryInitialPose(out Vector3 pos,out Quaternion rot,out float factor)&&Proof.Same(pos,Proof.HostPose.Position)&&Proof.Same(rot,Proof.HostPose.Rotation)&&MathF.Abs(factor-1.27f)<.0001f,
            "actual scenario sampler and receiver hand the hidden observer the exact host-authored world pose");
        Proof.Snap(follower,pos,rot,factor);empty=default;RemoteStorySync.Sample(ref empty);
        Proof.Check((empty.StoryFlags&2)==0&&!RemoteStorySync.Owns,"first visible shared snap is adopted as a baseline and never echoed as the observer's manual move");
        // A later observer can receive the current manual owner before the host's untouched
        // initialization. The election must have the same result in either receipt order.
        RemoteStorySync.Fresh();
        var hand=Proof.Story(3,Proof.HandPose,false,5);RemoteStorySync.Observe(3,hand);
        Time.unscaledTime+=1;var repeated=Proof.Story(1,Proof.HostPose,true,22);RemoteStorySync.Observe(1,repeated);
        Proof.Check(RemoteStorySync.TryInitialPose(out pos,out rot,out factor)&&Proof.Same(pos,Proof.HandPose.Position),"a current manual peer pose outranks a newer host initial pose for late joiners");
        Proof.Snap(follower,pos,rot,factor);RemoteStorySync.Apply();Time.unscaledTime+=.3f;RemoteStorySync.Apply();
        Proof.Check(Proof.Same(follower.Root.position,Proof.HandPose.Position),"live scenario pose election also keeps the manual mover ahead of repeated host initialization");
        follower.IsGrabbed=true;follower.Root.position=new Vector3(5,13,8);empty=default;RemoteStorySync.Sample(ref empty);
        Proof.Check(RemoteStorySync.Owns&&(empty.StoryFlags&2)!=0&&!empty.StoryInitialPose,"a genuine local grab publishes a manual pose after initial adoption");
        follower.IsGrabbed=false;Time.unscaledTime+=.3f;empty=default;RemoteStorySync.Sample(ref empty);RemoteStorySync.Apply();
        Proof.Check(Proof.Same(follower.Root.position,new Vector3(5,13,8)),"host initial placement never overwrites a locally owned window");
        foreach(string invalid in new[]{"missing","wrong-key","wrong-opening","stale","wrong-author"})
        {
            Proof.Configure(2);RemoteStorySync.Fresh();key=RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs);Proof.Opening(1,key,true);
            var peer=Proof.Story(invalid=="wrong-author"?3:1,Proof.HostPose,true,contextMissing:invalid=="missing",contentKey:invalid=="wrong-key"?key+3:key);
            if(invalid=="wrong-opening")peer.MapStoryLifecycleEntries![0].Token=55;
            RemoteStorySync.Observe(invalid=="wrong-author"?3:1,peer);
            if(invalid=="stale")Time.unscaledTime+=6;
            Proof.Check(!RemoteStorySync.TryInitialPose(out _,out _,out _),"missing or foreign initial scenario provenance stays pending: "+invalid);
        }
    }
    private static void Map()
    {
        foreach(SharedWindowKind kind in new[]{SharedWindowKind.MapStory,SharedWindowKind.QuestConfirm,SharedWindowKind.Encounter})
        foreach(bool parchment in new[]{false,true})
        {
            Proof.Configure(2);RemoteMapStory.Fresh();MapRoomDriver.Active=parchment;MapRoomDriver.Center=new Vector3(57,2,-28);
            uint key=RemoteMapStory.Key(kind);Proof.Opening(1,key,true);
            var follower=SharedWindows.Grabs[kind];follower.Visible=false;
            var initial=RemoteMapStory.Peer(1,kind,Proof.HostPose,true,explicitFrame:parchment?NetProtocol.SharedFrameParchment:NetProtocol.SharedFrameSeatAnchor);
            RemoteMapStory.Observe(1,initial);
            Proof.Check(RemoteMapStory.TryInitialPose(kind,out Vector3 pos,out Quaternion rot,out float factor)&&Proof.Same(pos,Proof.HostPose.Position)&&Proof.Same(rot,Proof.HostPose.Rotation),
                "map initial handoff reconstructs the same sender pose in parchment and normalized fallback frames");
            Proof.Check(RemoteMapStory.Adopted(kind),"map initial adoption retains the exact native subject and skips identity-settle suppression");
            Proof.Snap(follower,pos,rot,factor);SharedWindowEntry outgoing=RemoteMapStory.Publish(kind);
            Proof.Check(!RemoteMapStory.Owns(kind)&&(outgoing.Flags&2)==0,"map first shared snap retains its exact subject baseline and is never republished as a manual move");
            RemoteMapStory.Fresh();
            var hand=RemoteMapStory.Peer(3,kind,Proof.HandPose,false,5,explicitFrame:parchment?NetProtocol.SharedFrameParchment:NetProtocol.SharedFrameSeatAnchor);
            RemoteMapStory.Observe(3,hand);Time.unscaledTime+=1;initial=RemoteMapStory.Peer(1,kind,Proof.HostPose,true,7,explicitFrame:parchment?NetProtocol.SharedFrameParchment:NetProtocol.SharedFrameSeatAnchor);
            RemoteMapStory.Observe(1,initial);
            Proof.Check(RemoteMapStory.TryInitialPose(kind,out pos,out rot,out factor)&&Proof.Same(pos,Proof.HandPose.Position),"map late join follows the actual manual mover ahead of a newer host initial stamp");
            Proof.Snap(follower,pos,rot,factor);RemoteMapStory.Apply(kind);Time.unscaledTime+=.3f;RemoteMapStory.Apply(kind);
            Proof.Check(Proof.Same(follower.Root.position,Proof.HandPose.Position),"live map election preserves the manual pose in its actual decoded coordinate frame");
            if(kind is SharedWindowKind.QuestConfirm or SharedWindowKind.Encounter)
            {
                Time.frameCount += 10;
                SharedWindows.Grabs[kind] = new GrabbableModal();
                Proof.Check(!RemoteMapStory.TryInitialPose(kind,out _,out _,out _),
                    "same-key reopened quest or encounter rejects cached prior-float pose");
                var reopened = RemoteMapStory.Peer(1,kind,Proof.HostPose,true,9,
                    explicitFrame:parchment?NetProtocol.SharedFrameParchment:NetProtocol.SharedFrameSeatAnchor);
                RemoteMapStory.Observe(1,reopened);
                Proof.Check(RemoteMapStory.TryInitialPose(kind,out pos,out rot,out factor)&&Proof.Same(pos,Proof.HostPose.Position),
                    "same-key reopened quest or encounter accepts ordinary fresh author snapshot");
            }
            foreach(string invalid in new[]{"missing","wrong-key","stale","wrong-author"})
            {
                Proof.Configure(2);RemoteMapStory.Fresh();MapRoomDriver.Active=parchment;key=RemoteMapStory.Key(kind);Proof.Opening(1,key,true);
                var peer=RemoteMapStory.Peer(invalid=="wrong-author"?3:1,kind,Proof.HostPose,true,missing:invalid=="missing",key:invalid=="wrong-key"?key+8:key);
                RemoteMapStory.Observe(invalid=="wrong-author"?3:1,peer);
                if(invalid=="stale")Time.unscaledTime+=6;
                Proof.Check(!RemoteMapStory.TryInitialPose(kind,out _,out _,out _),"missing or foreign map initial provenance stays pending: "+kind+" "+invalid);
            }
        }
    }
    private static PresenceState Decode(PresenceState state)
    {
        state.WindowPoseContext=Proof.Wire(state.WindowPoseContext);
        state.WindowScales=Proof.WireScales(state.WindowScales);
        return state;
    }
    private static void ReferencePipeline()
    {
        Proof.Configure(1,true);
        var all=new PresenceState {HasStorySync=true,StoryFlags=3,StoryInitialPose=true,
            HasSharedWindow=true,SharedWindowCount=3,SharedWindowEntries=new[]{
                new SharedWindowEntry{Kind=1,Flags=3},new SharedWindowEntry{Kind=2,Flags=3},new SharedWindowEntry{Kind=3,Flags=3}},
            HasVideoWindow=true,VideoWindow=new FixtureVideo{Window=new SharedWindowEntry{Flags=3}},
            HasRewardWindow=true,RewardWindow=new RewardWindowState{Window=new SharedWindowEntry{Flags=3}}};
        foreach(var pair in SharedWindows.Grabs)pair.Value.ApplySharedReferenceWorldScale(11.47f+(int)pair.Key*31.625f);
        SharedWindowSize.ReferenceWorldScale=999f;MapRoomDriver.Active=false;
        SharedWindowSpawnAuthority.SampleContext(ref all);all=Decode(all);
        foreach(var pair in SharedWindows.Grabs)
            Proof.Check(all.WindowScales!.Value.Reference((int)pair.Key)==pair.Value.SharedReferenceWorldScale,
                "the actual presence sampler publishes each posed grab's captured reference rather than ambient geometry");
        ScenarioReference();MapReference();VideoReference();RewardReference();MissingReference();
        SharedWindowSize.ReferenceWorldScale=12;
    }
    private static void ScenarioReference()
    {
        Proof.Configure(1,true);RemoteStorySync.Fresh();
        var host=SharedWindows.Grabs[SharedWindowKind.ScenarioStory];host.ApplySharedReferenceWorldScale(198.12f);
        Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
        var sent=new PresenceState();RemoteStorySync.Sample(ref sent);SharedWindowSpawnAuthority.SampleContext(ref sent);sent=Decode(sent);
        Proof.Configure(2);RemoteStorySync.Fresh();var follower=SharedWindows.Grabs[SharedWindowKind.ScenarioStory];follower.Visible=false;
        uint key=RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs);Proof.Opening(1,key,true);
        sent.HasMapStoryLifecycle=true;sent.MapStoryLifecycleEntries=Proof.Opening(1,key);RemoteStorySync.Observe(1,sent);
        Proof.Check(RemoteStorySync.TryInitialPose(out Vector3 pos,out Quaternion rot,out float factor)
            &&follower.SharedReferenceWorldScale==198.12f&&!follower.Visible,
            "scenario121 adoption changes the pending grab reference before its first physical snap or reveal");
        Proof.Snap(follower,pos,rot,factor);
        var manual=Proof.Story(3,Proof.HandPose,false,5,reference:123.45f);Time.unscaledTime+=1;RemoteStorySync.Observe(3,manual);
        RemoteStorySync.Apply();Time.unscaledTime+=.3f;RemoteStorySync.Apply();
        Proof.Check(follower.SharedReferenceWorldScale==123.45f&&follower.ReferenceAtLastPlacement==123.45f,
            "live scenario placement adopts the winning packet reference before writing its physical pose");
        follower.IsGrabbed=true;follower.Root.position=new Vector3(3,8,13);var echoed=new PresenceState();
        RemoteStorySync.Sample(ref echoed);SharedWindowSpawnAuthority.SampleContext(ref echoed);echoed=Decode(echoed);
        Proof.Check((echoed.StoryFlags&2)!=0&&!echoed.StoryInitialPose&&echoed.WindowScales!.Value.Reference(1)==123.45f,
            "a scenario observer republishes its adopted reference when a genuine later hand claims the window");
    }
    private static void MapReference()
    {
        foreach(var kind in new[]{SharedWindowKind.MapStory,SharedWindowKind.QuestConfirm,SharedWindowKind.Encounter})
        {
            Proof.Configure(1,true);RemoteMapStory.Fresh();MapRoomDriver.Active=true;MapRoomDriver.Center=new Vector3(57,2,-28);
            var host=SharedWindows.Grabs[kind];host.ApplySharedReferenceWorldScale(198.12f);Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
            var entry=RemoteMapStory.Publish(kind);
            Proof.Check((entry.Flags&2)!=0&&entry.Frame==NetProtocol.SharedFrameSeatAnchor,
                "current map writer uses source Seat0 even while a parchment frame is available");
            var sent=new PresenceState{HasSharedWindow=true,SharedWindowCount=1,SharedWindowEntries=new[]{entry}};
            SharedWindowSpawnAuthority.SampleContext(ref sent);sent=Decode(sent);
            Proof.Configure(2);RemoteMapStory.Fresh();var follower=SharedWindows.Grabs[kind];follower.Visible=false;
            uint key=RemoteMapStory.Key(kind);Proof.Opening(1,key,true);sent.HasMapStoryLifecycle=true;sent.MapStoryLifecycleEntries=Proof.Opening(1,key);
            RemoteMapStory.Observe(1,sent);
            Proof.Check(RemoteMapStory.TryInitialPose(kind,out Vector3 pos,out Quaternion rot,out float factor)
                &&Proof.Same(pos,Proof.HostPose.Position)&&follower.SharedReferenceWorldScale==198.12f&&!follower.Visible,
                "map121 adoption reconstructs Seat0 without a local parchment and sets the pending grab reference before reveal");
            Proof.Snap(follower,pos,rot,factor);
            var manual=RemoteMapStory.Peer(3,kind,Proof.HandPose,false,5,reference:123.45f);
            Time.unscaledTime+=1;RemoteMapStory.Observe(3,manual);RemoteMapStory.Apply(kind);Time.unscaledTime+=.3f;RemoteMapStory.Apply(kind);
            Proof.Check(follower.SharedReferenceWorldScale==123.45f&&follower.ReferenceAtLastPlacement==123.45f,
                "live map placement adopts the winning packet reference before writing its physical pose");
            follower.IsGrabbed=true;follower.Root.position=new Vector3(3,8,13);entry=RemoteMapStory.Publish(kind);
            var echoed=new PresenceState{HasSharedWindow=true,SharedWindowCount=1,SharedWindowEntries=new[]{entry}};
            SharedWindowSpawnAuthority.SampleContext(ref echoed);echoed=Decode(echoed);
            Proof.Check((entry.Flags&2)!=0&&!entry.InitialPose&&echoed.WindowScales!.Value.Reference((int)kind)==123.45f,
                "a map observer republishes the adopted reference on its later genuine manual move");
        }
    }
    private static void VideoReference()
    {
        Proof.Configure(1,true);RemoteMapStory.ResetVideoPose();MapRoomDriver.Active=true;
        var host=SharedWindows.Grabs[SharedWindowKind.Video];host.ApplySharedReferenceWorldScale(198.12f);
        Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
        var entry=new SharedWindowEntry{Flags=1};RemoteMapStory.SampleVideoPose(901,false,ref entry);
        Proof.Check(entry.Frame==NetProtocol.SharedFrameSeatAnchor,"current video writer uses Seat0 while the host is in the3D map");
        var sent=new PresenceState{HasVideoWindow=true,VideoWindow=new FixtureVideo{Window=entry}};
        SharedWindowSpawnAuthority.SampleContext(ref sent);sent=Decode(sent);
        Proof.Configure(2);RemoteMapStory.ResetVideoPose();var follower=SharedWindows.Grabs[SharedWindowKind.Video];follower.Visible=false;
        var outgoing=new SharedWindowEntry{Flags=1};RemoteMapStory.SampleVideoPose(901,true,ref outgoing);
        VideoPoseReceipt.Observe(1,sent);
        Proof.Check(RemoteMapStory.TryVideoSharedInitialPose(out Vector3 pos,out Quaternion rot,out float factor)
            &&Proof.Same(pos,Proof.HostPose.Position)&&follower.SharedReferenceWorldScale==198.12f&&!MapRoomDriver.Active&&!follower.Visible,
            "video121 adoption resolves a3D-map host for a2D-map observer before the first reveal");
        Proof.Snap(follower,pos,rot,factor);outgoing=new SharedWindowEntry{Flags=1};RemoteMapStory.SampleVideoPose(901,true,ref outgoing);
        Proof.Check((outgoing.Flags&2)==0,"reference adoption never turns the first movie snap into a manual echo");
        var context=Proof.Context(3,5,false);var manual=new SharedWindowEntry{ContentKey=901,Flags=3,Frame=0,
            Pose=Proof.Relative(Proof.HandPose,context),PoseStamp=5,SizeCode=127};
        Time.unscaledTime+=1;RemoteMapStory.ObserveVideoPose(3,manual,Proof.Wire(context),Proof.ScalePacket(5,123.45f)!.Value.Reference(5));
        RemoteMapStory.ResolveVideoPose(901);Time.unscaledTime+=.3f;RemoteMapStory.ResolveVideoPose(901);
        Proof.Check(follower.SharedReferenceWorldScale==123.45f&&follower.ReferenceAtLastPlacement==123.45f,
            "live video placement adopts the winning packet reference before writing its physical pose");
        follower.IsGrabbed=true;follower.Root.position=new Vector3(3,8,13);outgoing=new SharedWindowEntry{Flags=1};RemoteMapStory.SampleVideoPose(901,true,ref outgoing);
        var echoed=new PresenceState{HasVideoWindow=true,VideoWindow=new FixtureVideo{Window=outgoing}};
        SharedWindowSpawnAuthority.SampleContext(ref echoed);echoed=Decode(echoed);
        Proof.Check((outgoing.Flags&2)!=0&&!outgoing.InitialPose&&echoed.WindowScales!.Value.Reference(5)==123.45f,
            "a video observer republishes the adopted render reference independently of decoder ownership");
    }
    private static void RewardReference()
    {
        Proof.Configure(1,true);RemoteMapStory.FreshReward();MapRoomDriver.Active=true;RewardShowcase.ContentKey=606;RewardShowcase.Window=new();
        RewardShowcasePlacement.LocalRevealPending=false;
        var host=SharedWindows.Grabs[SharedWindowKind.RewardShowcase];host.ApplySharedReferenceWorldScale(198.12f);
        Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
        var sent=new PresenceState();RemoteMapStory.SampleReward(ref sent,1);SharedWindowSpawnAuthority.SampleContext(ref sent);sent=Decode(sent);
        Proof.Check(sent.HasRewardWindow&&(sent.RewardWindow.Window.Flags&2)!=0
            &&sent.RewardWindow.Window.Frame==NetProtocol.RewardFrameScenario,
            "the actual reward sampler keeps WorldFrame2 even while parchment geometry is available");
        Proof.Configure(2);RemoteMapStory.FreshReward();RewardShowcasePlacement.LocalRevealPending=true;
        var follower=SharedWindows.Grabs[SharedWindowKind.RewardShowcase];follower.Visible=false;
        var empty=new PresenceState();RemoteMapStory.SampleReward(ref empty,2);
        SharedWindows.Grabs.Remove(SharedWindowKind.RewardShowcase);
        RemoteMapStory.ObserveReward(1,sent);RemoteMapStory.ResolveReward(2);
        Proof.Check(RewardShowcasePlacement.CachedKey==606&&RewardShowcasePlacement.CachedReference==198.12f
            &&Proof.Same(RewardShowcasePlacement.CachedPosition,Proof.HostPose.Position),
            "actual reward121 receive and initial publication retain reference and WorldFrame2 pose before conversion exists");
        SharedWindows.Grabs[SharedWindowKind.RewardShowcase]=follower;
        // Actual WorldUI cache-to-grab application executes separately in the Unity geometry proof.
        follower.ApplySharedReferenceWorldScale(RewardShowcasePlacement.CachedReference);
        Proof.Snap(follower,RewardShowcasePlacement.CachedPosition,RewardShowcasePlacement.CachedRotation,RewardShowcasePlacement.CachedFactor);
        RewardShowcasePlacement.LocalRevealPending=false;empty=default;RemoteMapStory.SampleReward(ref empty,2);
        Proof.Check((empty.RewardWindow.Window.Flags&2)==0,"the first cached reward snap does not acquire false manual movement ownership");
        Time.frameCount+=3;RemoteMapStory.ResolveReward(2);
        var manualContext=Proof.Context(3,6,false);
        var manual=new PresenceState{HasRewardWindow=true,RewardWindow=new RewardWindowState{Moved=true,Ready=true,
            Window=new SharedWindowEntry{ContentKey=606,Flags=3,Frame=NetProtocol.RewardFrameScenario,Pose=Proof.HandPose,SizeCode=127,PoseStamp=5}},
            WindowPoseContext=Proof.Wire(manualContext),WindowScales=Proof.ScalePacket(6,123.45f)};
        Time.unscaledTime+=1;RemoteMapStory.ObserveReward(3,manual);RemoteMapStory.ResolveReward(2);Time.unscaledTime+=.3f;RemoteMapStory.ResolveReward(2);
        Proof.Check(follower.SharedReferenceWorldScale==123.45f&&RewardShowcasePlacement.CachedReference==123.45f,
            "live reward election carries the manual winner's reference into both physical placement and future reveal cache");
        follower.IsGrabbed=true;follower.Root.position=new Vector3(3,8,13);var echoed=new PresenceState();RemoteMapStory.SampleReward(ref echoed,2);
        SharedWindowSpawnAuthority.SampleContext(ref echoed);echoed=Decode(echoed);
        Proof.Check((echoed.RewardWindow.Window.Flags&2)!=0&&echoed.WindowScales!.Value.Reference(6)==123.45f,
            "a reward observer republishes the adopted reference with its genuine later manual move");
    }
    private static void MissingReference()
    {
        foreach(string invalid in new[]{"missing","wrong-kind","invalid-float"})
        {
            SharedWindowScaleContext? scales=null;
            if(invalid=="wrong-kind")scales=Proof.ScalePacket(6);
            if(invalid=="invalid-float")
            {
                var context=new SharedWindowScaleContext();context.Set(1,198.12f);var bytes=new byte[7];int at=0;
                Proof.Check(SharedWindowScaleContext.Write(bytes,ref at,context),"malformed reference control starts with an actual valid121 record");
                bytes[3]=0;bytes[4]=0;bytes[5]=192;bytes[6]=127;
                Proof.Check(!SharedWindowScaleContext.TryRead(bytes,2,5,out _),"malformed121 float is rejected before any window receives it");
            }
            Proof.Configure(2);RemoteStorySync.Fresh();uint key=RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs);Proof.Opening(1,key,true);
            var story=Proof.Story(1,Proof.HostPose,true);story.WindowScales=scales;RemoteStorySync.Observe(1,story);
            Proof.Check(!RemoteStorySync.TryInitialPose(out _,out _,out _),"scenario missing or invalid121 remains pending: "+invalid);
            foreach(var kind in new[]{SharedWindowKind.MapStory,SharedWindowKind.QuestConfirm,SharedWindowKind.Encounter})
            {
                Proof.Configure(2);RemoteMapStory.Fresh();Proof.Opening(1,RemoteMapStory.Key(kind),true);
                var map=RemoteMapStory.Peer(1,kind,Proof.HostPose,true);map.WindowScales=scales;RemoteMapStory.Observe(1,map);
                Proof.Check(!RemoteMapStory.TryInitialPose(kind,out _,out _,out _),"map missing or invalid121 remains pending: "+kind+" "+invalid);
            }
            Proof.Configure(2);RemoteMapStory.ResetVideoPose();var empty=new SharedWindowEntry{Flags=1};RemoteMapStory.SampleVideoPose(777,true,ref empty);
            var video=new SharedWindowEntry{ContentKey=777,Flags=3,Frame=0,Pose=Proof.Relative(Proof.HostPose,Proof.Context(1,5,true)),SizeCode=127};
            RemoteMapStory.ObserveVideoPose(1,video,Proof.Wire(Proof.Context(1,5,true)),scales?.Reference(5)??0);
            Proof.Check(!RemoteMapStory.TryVideoSharedInitialPose(out _,out _,out _),"video missing or invalid121 remains pending: "+invalid);
            Proof.Configure(2);RemoteMapStory.FreshReward();RewardShowcasePlacement.LocalRevealPending=true;
            var reward=new PresenceState{HasRewardWindow=true,RewardWindow=new RewardWindowState{Ready=true,
                Window=new SharedWindowEntry{ContentKey=606,Flags=3,Frame=2,Pose=Proof.HostPose,SizeCode=127}},
                WindowPoseContext=Proof.Wire(Proof.Context(1,6,true)),WindowScales=invalid=="wrong-kind"?Proof.ScalePacket(1):scales};
            RemoteMapStory.ObserveReward(1,reward);RemoteMapStory.ResolveReward(2);
            Proof.Check(RewardShowcasePlacement.CachedKey==0,"reward missing or invalid121 cannot release its initial placement cache: "+invalid);
        }
    }
    private static void Video()
    {
        Proof.Configure(1,true);RemoteMapStory.ResetVideoPose();
        var host=SharedWindows.Grabs[SharedWindowKind.Video];Proof.Snap(host,Proof.HostPose.Position,Proof.HostPose.Rotation,1.27f);
        SharedWindowEntry entry=new() {Flags=1};RemoteMapStory.SampleVideoPose(77,false,ref entry);
        var sent=new PresenceState {HasVideoWindow=true,VideoWindow=new FixtureVideo {Window=entry}};
        SharedWindowSpawnAuthority.SampleContext(ref sent);
        Proof.Check(entry.InitialPose&&(entry.Flags&2)!=0&&sent.WindowPoseContext!.Value.IsInitial(5),
            "host video placement is published even while its decoder mirrors a different source");
        Proof.Configure(2);RemoteMapStory.ResetVideoPose();
        var follower=SharedWindows.Grabs[SharedWindowKind.Video];follower.Visible=false;
        SharedWindowEntry empty=new() {Flags=1};RemoteMapStory.SampleVideoPose(77,true,ref empty);
        Proof.Check((empty.Flags&2)==0,"a native decoder source cannot invent physical window ownership before the host pose");
        RemoteMapStory.ObserveVideoPose(1,entry,Proof.Wire(sent.WindowPoseContext),Proof.WireScales(sent.WindowScales)!.Value.Reference(5));
        Proof.Check(RemoteMapStory.TryVideoSharedInitialPose(out Vector3 pos,out Quaternion rot,out float factor)
            &&Proof.Same(pos,Proof.HostPose.Position)&&Proof.Same(rot,Proof.HostPose.Rotation),
            "video's independent decoder election still receives the host's exact initial physical pose");
        Proof.Snap(follower,pos,rot,factor);empty=new SharedWindowEntry {Flags=1};RemoteMapStory.SampleVideoPose(77,true,ref empty);
        Proof.Check((empty.Flags&2)==0,"video first shared snap remains a baseline rather than decoder-owned manual movement");
        var manualContext=Proof.Context(3,5,false);
        var hand=new SharedWindowEntry {ContentKey=77,Flags=3,Pose=Proof.Relative(Proof.HandPose,manualContext),
            Frame=NetProtocol.SharedFrameSeatAnchor,PoseStamp=5,SizeCode=127};
        RemoteMapStory.ResetVideoPose();empty=new SharedWindowEntry {Flags=1};RemoteMapStory.SampleVideoPose(77,true,ref empty);
        RemoteMapStory.ObserveVideoPose(3,hand,Proof.Wire(manualContext),Proof.ScalePacket(5,198.12f)!.Value.Reference(5));Time.unscaledTime+=1;
        entry.PoseStamp=7;RemoteMapStory.ObserveVideoPose(1,entry,Proof.Wire(sent.WindowPoseContext),Proof.WireScales(sent.WindowScales)!.Value.Reference(5));
        Proof.Check(RemoteMapStory.TryVideoSharedInitialPose(out pos,out rot,out factor)&&Proof.Same(pos,Proof.HandPose.Position),
            "late video observer follows a real manual move ahead of newer host initialization");
        RemoteMapStory.ResetVideoPose();empty=new SharedWindowEntry {Flags=1};RemoteMapStory.SampleVideoPose(99,true,ref empty);
        RemoteMapStory.ObserveVideoPose(1,entry,Proof.Wire(sent.WindowPoseContext),Proof.WireScales(sent.WindowScales)!.Value.Reference(5));
        Proof.Check(!RemoteMapStory.TryVideoSharedInitialPose(out _,out _,out _),"video initial pose cannot leak into another playback opening key");
    }
}
