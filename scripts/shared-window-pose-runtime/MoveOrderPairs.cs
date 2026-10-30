using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

// Two isolated copies of the actual static runtime, scheduled on one thread. The snapshot port
// changes no production decisions: it swaps process-local statics just as two game processes do.
internal static class MoveOrderPairs
{
    private const SharedWindowKind Kind = SharedWindowKind.QuestConfirm;
    private static readonly Type[] Types = {typeof(SharedWindowMoves),typeof(RemoteMapStory),
        typeof(RemoteStorySync),typeof(SharedWindowSpawnAuthority),typeof(SharedWindows),typeof(PanelLayout),
        typeof(MapRoomDriver),typeof(FFSNetwork),typeof(NetPlayerActors),typeof(NetSession),typeof(Time),
        typeof(VersionGuard),typeof(MapStoryLifecycle),typeof(ModalFallback)};
    private sealed class Fields
    {
        private readonly List<(FieldInfo Field, object? Value)> _values = new();
        internal Fields(Type type, object? target)
        {
            foreach (var field in type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|
                (target==null?BindingFlags.Static:BindingFlags.Instance)))
            {
                if(field.IsLiteral)continue;object? value=field.GetValue(target);
                if(value is IDictionary dict) {var copy=new List<DictionaryEntry>();foreach(DictionaryEntry row in dict)copy.Add(row);value=copy;}
                else if(value is HashSet<uint> set)value=new HashSet<uint>(set);
                else if(value is HashSet<int> ids)value=new HashSet<int>(ids);
                else if(value is Array a)value=a.Clone();
                else if(value is IList list) {var copy=new List<object?>();foreach(var row in list)copy.Add(row);value=copy;}
                else if(field.IsInitOnly && value!=null && (value.GetType()==typeof(SharedWindowPoseTrack)
                    || value.GetType()==typeof(RewardPoseHandshake) || value.GetType()==typeof(RewardPoseHandoff)
                    || value.GetType().Name=="Local"))value=new Fields(value.GetType(),value);
                _values.Add((field,value));
            }
        }
        internal void Restore(object? target)
        {
            foreach(var row in _values)
            {
                object? current=row.Field.GetValue(target);
                if(row.Value is List<DictionaryEntry> dict && current is IDictionary destination)
                {destination.Clear();foreach(var entry in dict)destination.Add(entry.Key,entry.Value);}
                else if(row.Value is HashSet<int> ids && current is HashSet<int> destinationIds)
                {destinationIds.Clear();destinationIds.UnionWith(ids);}
                else if(row.Value is HashSet<uint> set && current is HashSet<uint> destinationSet)
                {destinationSet.Clear();destinationSet.UnionWith(set);}
                else if(row.Value is Array a && row.Field.IsInitOnly && current is Array destinationArray)Array.Copy(a,destinationArray,a.Length);
                else if(row.Value is List<object?> list && current is IList destinationList)
                {destinationList.Clear();foreach(var entry in list)destinationList.Add(entry);}
                else if(row.Value is Fields nested)nested.Restore(current);
                else if(!row.Field.IsInitOnly)row.Field.SetValue(target,row.Value);
            }
        }
    }
    private sealed class Node
    {
        private readonly int _id;
        private Fields[] _state=Array.Empty<Fields>();
        internal Node(int id)
        {
            _id=id;Proof.Configure(id,id==1);ModalFallback.ResetRoomProof();RemoteMapStory.Fresh();RemoteStorySync.Fresh();SharedWindowMoves.Reset();
            SharedWindowMoves.Bind(()=>true);MapRoomDriver.Active=true;
            Proof.Snap(SharedWindows.Grabs[Kind],Proof.HostPose.Position,Proof.HostPose.Rotation,1);
            RemoteMapStory.SetNativeKey(Kind);RemoteMapStory.Publish(Kind);Time.frameCount+=3;RemoteMapStory.Publish(Kind);Save();
        }
        private void Save(){_state=new Fields[Types.Length];for(int i=0;i<Types.Length;i++)_state[i]=new Fields(Types[i],null);}
        internal void Do(Action action){for(int i=0;i<Types.Length;i++)_state[i].Restore(null);action();Save();}
        internal PresenceState Publish()
        {
            PresenceState result=default;Do(()=>{
                var entry=RemoteMapStory.Publish(Kind);
                result=new PresenceState{HasSharedWindow=true,SharedWindowEntries=new[]{entry},SharedWindowCount=1};
                result.HasMapRoom=true;result.MapRoomFlags=(byte)(NetProtocol.MapRoomInRoomBit|(_id==1?NetProtocol.MapRoomHostBit:0));
                RemoteMapStory.SampleMotionForProof(ref result);
                SharedWindowSpawnAuthority.SampleContext(ref result);SharedWindowMoves.Sample(ref result);
                if(result.WindowMoves!=null)
                {
                    var bytes=new byte[183];int at=0;
                    Proof.Check(SharedWindowMoveOrder.Write(bytes,ref at,result.WindowMoves)
                        &&SharedWindowMoveOrder.TryRead(bytes,2,bytes[1],out result.WindowMoves),"paired packet executes actual123 writer and reader");
                }
                result.WindowPoseContext=Proof.Wire(result.WindowPoseContext);result.WindowScales=Proof.WireScales(result.WindowScales);
            });return result;
        }
        internal void Receive(int sender,PresenceState packet,float delay=0)
        {Do(()=>{Time.unscaledTime+=delay;Time.frameCount++;SharedWindowMoves.Observe(sender,in packet);RemoteMapStory.Observe(sender,in packet);RemoteMapStory.Apply(Kind);});}
        internal void Grab(Vector3 position)
        {Do(()=>{Proof.Check(SharedWindowInteractionBridge.CanBegin!(Kind),"actual shared admission grants a fresh gesture for node "+_id);SharedWindowInteractionBridge.Began!(Kind);var grab=SharedWindows.Grabs[Kind];grab.IsGrabbed=true;grab.Root.position=position;});}
        internal void Move(Vector3 position){Do(()=>SharedWindows.Grabs[Kind].Root.position=position);}
        internal void Drop(){Do(()=>{SharedWindows.Grabs[Kind].IsGrabbed=false;SharedWindowInteractionBridge.Released!(Kind);});}
        internal void Settle(){Do(()=>{Time.unscaledTime+=.3f;Time.frameCount+=3;RemoteMapStory.Apply(Kind);});}
        internal Vector3 Position(){Vector3 p=default;Do(()=>p=SharedWindows.Grabs[Kind].Root.position);return p;}
        internal void Automatic(Vector3 position)
        {Do(()=>{RemoteMapStory.CanArrangeSharedWindows();Time.unscaledTime+=.4f;Time.frameCount+=4;
            Proof.Check(RemoteMapStory.BeginSharedReflow(Kind),"released manual pose still permits later opening-time room making");
            SharedWindows.Grabs[Kind].Root.position=position;});}
        internal bool Reflow(){bool active=false;Do(()=>active=RemoteMapStory.ReflowForProof(Kind));return active;}
        internal void EndAutomatic(){Do(()=>RemoteMapStory.EndSharedReflow(Kind));}
        internal bool MayArrange(){bool allowed=false;Do(()=>{RemoteMapStory.CanArrangeSharedWindows();Time.unscaledTime+=.4f;allowed=RemoteMapStory.CanArrangeSharedWindows();});return allowed;}
        internal void NewNativeOpening(uint key)
        {Do(()=>{SharedWindowMoves.Close(Kind);RemoteMapStory.CurrentQuest=new UIQuestPopup{Key=key};RemoteMapStory.SetNativeKey(Kind);
            RemoteMapStory.Publish(Kind);Time.frameCount+=3;RemoteMapStory.Publish(Kind);});}
    }
    internal static void Run()
    {
        InitialClockGuards();
        for(int schedule=0;schedule<6;schedule++)
        {
            var a=new Node(1);var b=new Node(2);var c=new Node(3);var initial=a.Publish();
            b.Receive(1,initial);b.Settle();c.Receive(1,initial);c.Settle();
            a.Grab(new Vector3(1,3,5));b.Grab(new Vector3(7,11,13));
            var first=a.Publish();var second=b.Publish();c.Receive(2,second);
            if((schedule&1)==0){a.Receive(2,second,5);b.Receive(1,first,.01f);}
            else {b.Receive(1,first,5);a.Receive(2,second,.01f);}
            a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"crossed simultaneous real samplers and resolvers converge despite opposite arrival clocks");
            a.Drop();var loserDrop=a.Publish();b.Receive(1,loserDrop);
            b.Move(new Vector3(41,43,47));var newerHeld=b.Publish();a.Receive(2,newerHeld);c.Receive(2,newerHeld);
            b.Drop();var earlyRelease=b.Publish();a.Receive(2,earlyRelease);
            b.Move(new Vector3(53,59,61));var released=b.Publish();a.Receive(2,released);a.Settle();
            a.Receive(2,earlyRelease);a.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"reordered release tween samples never rewind the final shared pose");
            // A duplicate and a delayed held sample must not undo the accepted release.
            a.Receive(2,second);a.Receive(2,released);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"losing drop, duplicate and reordered held cannot take a released shared pose back");
            a.Automatic(new Vector3(17,19,23));var automatic=a.Publish();
            a.Receive(2,second);Proof.Check(a.Reflow(),"stale rejected77 held and byte-stamp cannot cancel a later accepted real reflow");
            // The third real receiver sees the causally later Auto before its parent's Release.
            c.Receive(1,automatic);c.Settle();c.Receive(2,released);c.Settle();
            b.Receive(1,automatic);b.Settle();a.Settle();
            Proof.Check(Proof.Same(a.Position(),c.Position()),"auto carries its observed release basis even when that release arrives later");
            Proof.Check(Proof.Same(a.Position(),b.Position()),"actual post-manual room making converges through123 and the live resolver");a.EndAutomatic();
            b.Grab(new Vector3(29,31,37));var regrab=b.Publish();a.Receive(2,regrab);
            a.Receive(2,released);b.Receive(1,automatic);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"fresh regrab outranks delayed automatic motion and the preceding release");
        }
        AutomaticCrossing();LiveHeldScope();RegisterEdges();ActiveAllKinds();AutomaticAuthorReturn();RewardPlacementHandover();
    }
    private static void AutomaticCrossing()
    {
        var a=new Node(1);var b=new Node(2);var initial=a.Publish();b.Receive(1,initial);b.Settle();
        b.Grab(new Vector3(11,13,17));b.Drop();var priorRelease=b.Publish();a.Receive(2,priorRelease);a.Settle();
        a.Automatic(new Vector3(19,23,29));var firstAuto=a.Publish();a.EndAutomatic();
        a.Automatic(new Vector3(31,37,41));var secondAuto=a.Publish();
        // Neither automatic operation has reached the other player. This genuine new grip
        // is causally after the manual release, although its own counter is below Auto's.
        var destination=new Vector3(43,47,53);b.Grab(destination);var fresh=b.Publish();
        Proof.Check(secondAuto.WindowMoves!.TryGet(3,303,out var automatic)&&fresh.WindowMoves!.TryGet(3,303,out var manual)
            &&automatic.Claim>manual.Claim,"crossed reflow control really has a higher automatic local clock");
        a.Receive(2,fresh);b.Receive(1,secondAuto);b.Receive(1,firstAuto);a.Receive(2,priorRelease);a.Settle();b.Settle();
        Proof.Check(Proof.Same(a.Position(),destination)&&Proof.Same(b.Position(),destination),
            "fresh manual claim outranks two delayed automatic operations on an older release basis");
        Proof.Check(!a.Reflow(),"a genuine newer manual winner interrupts the existing real reflow");
    }
    private static void LiveHeldScope()
    {
        var a=new Node(1);var b=new Node(2);var c=new Node(3);var initial=a.Publish();b.Receive(1,initial);c.Receive(1,initial);
        b.Grab(new Vector3(3,5,7));var oldHolder=b.Publish();a.Receive(2,oldHolder);
        c.Grab(new Vector3(11,13,17));c.Drop();var newRelease=c.Publish();a.Receive(3,newRelease);a.Settle();
        Proof.Check(a.MayArrange(),"old holder cannot block room making after another accepted releaser without another old-holder packet");
        // Reproduce a fresh cached Held receipt, then replace the native opening without any
        // further packet from that sender. The new opening must not inherit its old grip mask.
        var d=new Node(1);var e=new Node(2);initial=d.Publish();e.Receive(1,initial);
        e.Grab(new Vector3(19,23,29));d.Receive(2,e.Publish());d.NewNativeOpening(909);
        Proof.Check(d.MayArrange(),"closed or replaced native opening cannot inherit a cached77 held mask");
    }
    // Native window discovery and already-admitted visibility are inputs. Each type below then
    // executes its actual producer,123/119/121 codecs, type-specific observer and resolver.
    private sealed class ActiveNode
    {
        private readonly int _id;
        private readonly SharedWindowKind _kind;
        private readonly Type[] _types;
        private Fields[] _state=Array.Empty<Fields>();
        internal ActiveNode(int id,SharedWindowKind kind,bool flatHost=false,int[]? roster=null)
        {
            _id=id;_kind=kind;
            var types=new List<Type>(Types);types.AddRange(new[]{typeof(RewardShowcase),typeof(RewardShowcasePlacement),typeof(ModalFallback)});_types=types.ToArray();
            Proof.Configure(id,id==1,knownHost:!flatHost);VersionGuard.Fresh(roster??(flatHost?new[]{2,3}:new[]{1,2}));
            RemoteMapStory.Fresh();RemoteStorySync.Fresh();RemoteMapStory.FreshReward();RemoteMapStory.ResetVideoPose();SharedWindowMoves.Reset();
            SharedWindowMoves.Bind(()=>true);ModalFallback.ResetRoomProof();MapRoomDriver.Active=kind is SharedWindowKind.MapStory or SharedWindowKind.QuestConfirm or SharedWindowKind.Encounter;
            PanelLayout.WorldScale=id==1?7.22f:23.39f;PanelLayout.Origin=new Vector3(id*17,3,-id*11);PanelLayout.Yaw=Quaternion.Euler(0,id*37,0);
            var grab=SharedWindows.Grabs[kind];grab.ApplySharedReferenceWorldScale(id==1?198.12f:11.47f);
            Proof.Snap(grab,Proof.HostPose.Position,Proof.HostPose.Rotation,1);
            RewardShowcase.Window=new object();RewardShowcase.ContentKey=606;
            RewardShowcasePlacement.LocalRevealPending=false;RewardShowcasePlacement.LocalPlacementFailed=false;
            MapStoryLifecycle.Current=new object();MapStoryLifecycle.Ledger=new MapStoryOpeningLedger((uint)(500+id));
            MapStoryLifecycle.Ledger.Open(MapStoryLifecycle.Current,ContentKey,ContentKey,1,new[]{id==1?2:1});
            Produce();Time.frameCount+=3;Produce();Save();
        }
        private uint ContentKey=>_kind switch {SharedWindowKind.ScenarioStory=>RemoteStorySync.HashDialog(RemoteStorySync.CurrentBox!.dialogs),
            SharedWindowKind.Video=>777,SharedWindowKind.RewardShowcase=>RewardShowcase.ContentKey,_=>RemoteMapStory.Key(_kind)};
        private void Save(){_state=new Fields[_types.Length];for(int i=0;i<_types.Length;i++)_state[i]=new Fields(_types[i],null);}
        internal void Do(Action action){for(int i=0;i<_types.Length;i++)_state[i].Restore(null);action();Save();}
        private PresenceState Produce()
        {
            PresenceState result=default;
            if(_kind==SharedWindowKind.ScenarioStory)RemoteStorySync.Sample(ref result);
            else if(_kind==SharedWindowKind.Video)
            {
                var entry=new SharedWindowEntry{Flags=NetProtocol.SharedOpenBit};RemoteMapStory.SampleVideoPose(ContentKey,_id==1,ref entry);
                result.HasVideoWindow=true;result.VideoWindow=new FixtureVideo{Window=entry};
            }
            else if(_kind==SharedWindowKind.RewardShowcase)RemoteMapStory.SampleReward(ref result,_id);
            else
            {
                RemoteMapStory.SetNativeKey(_kind);var entry=RemoteMapStory.Publish(_kind);
                result.HasSharedWindow=true;result.SharedWindowCount=1;result.SharedWindowEntries=new[]{entry};
                result.HasMapRoom=true;result.MapRoomFlags=(byte)(NetProtocol.MapRoomInRoomBit|(_id==1?NetProtocol.MapRoomHostBit:0));RemoteMapStory.SampleMotionForProof(ref result);
            }
            result.MapStoryLifecycleEntries=MapStoryLifecycle.Ledger.Sample(MapStoryLifecycle.Current);result.HasMapStoryLifecycle=true;
            SharedWindowSpawnAuthority.SampleContext(ref result);SharedWindowMoves.Sample(ref result);
            return result;
        }
        internal PresenceState Publish()
        {
            PresenceState result=default;Do(()=>{
                result=Produce();
                if(result.WindowMoves!=null)
                {
                    var bytes=new byte[183];int at=0;
                    Proof.Check(SharedWindowMoveOrder.Write(bytes,ref at,result.WindowMoves)
                        &&SharedWindowMoveOrder.TryRead(bytes,2,bytes[1],out result.WindowMoves),"active "+_kind+" serializes its actual123 companion");
                }
                result.WindowPoseContext=Proof.Wire(result.WindowPoseContext);result.WindowScales=Proof.WireScales(result.WindowScales);
                if(result.HasRewardPoseHandshake)
                {
                    var bytes=new byte[59];int at=0;Proof.Check(RewardPoseHandshakeCodec.Write(bytes,ref at,in result.RewardPoseHandshake)
                        &&RewardPoseHandshakeCodec.TryRead(bytes,2,bytes[1],out result.RewardPoseHandshake),"active reward uses its actual native-role handshake codec");
                }
            });return result;
        }
        private void Apply()
        {
            if(_kind==SharedWindowKind.ScenarioStory)RemoteStorySync.Apply();
            else if(_kind==SharedWindowKind.Video)RemoteMapStory.ResolveVideoPose(ContentKey);
            else if(_kind==SharedWindowKind.RewardShowcase)RemoteMapStory.ResolveReward(_id);
            else RemoteMapStory.Apply(_kind);
        }
        internal void Receive(int sender,PresenceState packet)
        {
            Do(()=>{Time.frameCount++;if(packet.HasMapStoryLifecycle&&packet.MapStoryLifecycleEntries!=null)MapStoryLifecycle.Ledger.Observe(sender,packet.MapStoryLifecycleEntries);
                SharedWindowMoves.Observe(sender,in packet);
                if(_kind==SharedWindowKind.ScenarioStory)RemoteStorySync.Observe(sender,in packet);
                else if(_kind==SharedWindowKind.Video)VideoPoseReceipt.Observe(sender,in packet);
                else if(_kind==SharedWindowKind.RewardShowcase)RemoteMapStory.ObserveReward(sender,in packet);
                else RemoteMapStory.Observe(sender,in packet);
                Apply();});
        }
        internal void Grab(RigPose pose)
        {Do(()=>{Proof.Check(SharedWindowMoves.Active&&SharedWindowInteractionBridge.CanBegin!(_kind),"active "+_kind+" admits its actual local claim");
            SharedWindowInteractionBridge.Began!(_kind);var grab=SharedWindows.Grabs[_kind];grab.IsGrabbed=true;grab.Root.position=pose.Position;grab.Root.rotation=pose.Rotation;});}
        internal void Move(RigPose pose){Do(()=>{var grab=SharedWindows.Grabs[_kind];grab.Root.position=pose.Position;grab.Root.rotation=pose.Rotation;});}
        internal void Drop(){Do(()=>{SharedWindows.Grabs[_kind].IsGrabbed=false;SharedWindowInteractionBridge.Released!(_kind);});}
        internal void Settle(){Do(()=>{Time.unscaledTime+=.3f;Time.frameCount+=3;Apply();});}
        internal RigPose Pose(){RigPose value=default;Do(()=>value=new RigPose{Position=SharedWindows.Grabs[_kind].Root.position,Rotation=SharedWindows.Grabs[_kind].Root.rotation});return value;}
        internal float Reference(){float value=0;Do(()=>value=SharedWindows.Grabs[_kind].SharedReferenceWorldScale);return value;}
        internal void Roster(params int[] ids){Do(()=>VersionGuard.Fresh(ids));}
        internal void NativeReward(bool visible,bool pending=false)
        {Do(()=>{RewardShowcase.Window=visible?new object():null;RewardShowcasePlacement.LocalRevealPending=pending;});}
        internal int PlacementAuthor(){int author=0;Do(()=>author=RemoteMapStory.MoveOpeningAuthor(ContentKey));return author;}
        internal bool Follows(int sender){bool follows=false;Do(()=>follows=SharedWindowMoves.MayFollow(sender,_kind,ContentKey));return follows;}
        internal void NewRewardKey(uint key,bool pending=false)
        {Do(()=>{RewardShowcase.ContentKey=key;RewardShowcase.Window=new object();RewardShowcasePlacement.LocalRevealPending=pending;
            SharedWindows.Grabs[_kind].IsGrabbed=false;Produce();Time.frameCount+=3;Produce();});}


    }
    private static void ActiveAllKinds()
    {
        foreach(SharedWindowKind kind in Enum.GetValues<SharedWindowKind>())
        {
            if(kind==SharedWindowKind.None)continue;
            var a=new ActiveNode(1,kind);var b=new ActiveNode(2,kind);var initial=a.Publish();b.Receive(1,initial);b.Settle();
            Proof.Check(Proof.Same(a.Pose().Position,b.Pose().Position)&&MathF.Abs(b.Reference()-198.12f)<.001f,
                "active "+kind+" initial producer/observer agrees on world pose and captured reference");
            a.Grab(new RigPose{Position=new Vector3(3,7,11),Rotation=Quaternion.Euler(0,41,0)});
            b.Grab(new RigPose{Position=new Vector3(13,17,19),Rotation=Quaternion.Euler(0,83,0)});
            var crossedA=a.Publish();var crossedB=b.Publish();a.Receive(2,crossedB);b.Receive(1,crossedA);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Pose().Position,b.Pose().Position)&&Proof.Same(a.Pose().Rotation,b.Pose().Rotation),
                "active "+kind+" crossed producer/observer/resolver claims converge");
            a.Drop();b.Drop();var oldRelease=b.Publish();a.Receive(2,oldRelease);
            var endpoint=new RigPose{Position=new Vector3(23,29,31),Rotation=Quaternion.Euler(0,127,0)};
            b.Move(endpoint);var finalRelease=b.Publish();a.Receive(2,finalRelease);a.Settle();a.Receive(2,oldRelease);a.Settle();
            Proof.Check(Proof.Same(a.Pose().Position,endpoint.Position)&&Proof.Same(a.Pose().Rotation,endpoint.Rotation),
                "active "+kind+" final release survives reordered type-specific observer samples");
            var next=new RigPose{Position=new Vector3(37,41,43),Rotation=Quaternion.Euler(0,173,0)};a.Grab(next);var nextClaim=a.Publish();b.Receive(1,nextClaim);
            b.Receive(1,crossedA);a.Receive(2,crossedB);a.Drop();var finish=a.Publish();b.Receive(1,finish);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Pose().Position,next.Position)&&Proof.Same(b.Pose().Position,next.Position)
                &&Proof.Same(a.Pose().Rotation,b.Pose().Rotation)&&MathF.Abs(a.Reference()-198.12f)<.001f&&MathF.Abs(a.Reference()-b.Reference())<.001f,
                "active "+kind+" winner change retains the same final world pose and reference");
        }
    }
    internal static void AutomaticAuthorReturn()
    {
        var a=new Node(1);var b=new Node(2);var initial=a.Publish();b.Receive(1,initial);b.Settle();
        // The host's opening-time Auto is not delivered during this network gap. The observer
        // legitimately elects its single fresh map-VR surrogate after the old host census expires.
        a.Automatic(new Vector3(71,73,79));var hostAuto=a.Publish();
        b.Do(()=>{Time.unscaledTime+=6;Time.frameCount+=10;});
        b.Automatic(new Vector3(83,89,97));var survivorAuto=b.Publish();
        int writes=0;b.Do(()=>{ModalFallback.StartRoomProof(Kind,new Vector3(101,103,107));Time.unscaledTime+=.05f;ModalFallback.TickRoomProof();writes=SharedWindows.Grabs[Kind].RoomMakingWrites;});
        Proof.Check(writes>0,"actual started room-making tween reaches its production frame writer before authority loss");
        Proof.Check(hostAuto.WindowMoves!.TryGet(3,303,out var hostOrder)&&survivorAuto.WindowMoves!.TryGet(3,303,out var survivorOrder)
            &&hostOrder.Claim==survivorOrder.Claim&&hostOrder.ParentClaim==survivorOrder.ParentClaim,
            "actual auto-author return control crosses equal claims on the same causal basis");
        a.Receive(2,survivorAuto);b.Receive(1,a.Publish());
        Proof.Check(!b.Reflow(),"native census retires the started local reflow before its next animation tick");
        // The first returning host packet refreshes the actual map membership census. Subsequent
        // fresh source samples must converge without inventing another opening or manual claim.
        b.Receive(1,a.Publish());a.Receive(2,b.Publish());a.Settle();b.Settle();
        bool stopped=false;int afterWrites=0;b.Do(()=>{Time.unscaledTime+=.05f;ModalFallback.TickRoomProof();afterWrites=SharedWindows.Grabs[Kind].RoomMakingWrites;stopped=!ModalFallback.RoomProofRunning;});
        Proof.Check(stopped&&afterWrites==writes&&!b.Reflow(),"actual room-making write boundary retires the no-longer-eligible automatic author");
        int authorA=0,authorB=0;a.Do(()=>authorA=RemoteMapStory.SharedReflowAuthor());b.Do(()=>authorB=RemoteMapStory.SharedReflowAuthor());
        Proof.Check(authorA==1&&authorB==1,"both actual reflow facades independently re-elect the returning native host");
        Proof.Check(Proof.Same(a.Position(),b.Position()),"actual automatic author handover converges after the native host returns");
        AutomaticManualBasis();
    }
    private static void AutomaticManualBasis()
    {
        var host=new Node(1);var hand=new Node(3);var survivor=new Node(2);var initial=host.Publish();hand.Receive(1,initial);survivor.Receive(1,initial);
        hand.Grab(new Vector3(109,113,127));var oldHeld=hand.Publish();host.Receive(3,oldHeld);survivor.Receive(3,oldHeld);
        var delayedHeld=hand.Publish();hand.Drop();var release=hand.Publish();host.Receive(3,release);
        host.Automatic(new Vector3(131,137,139));var oldAuto=host.Publish();survivor.Receive(1,oldAuto);
        survivor.Do(()=>{Time.unscaledTime+=6;Time.frameCount+=10;});survivor.Receive(3,delayedHeld);
        Proof.Check(survivor.MayArrange(),"a delayed held receipt cannot resurrect the released manual basis after automatic author loss");
        survivor.Automatic(new Vector3(149,151,157));var nextAuto=survivor.Publish();
        Proof.Check(nextAuto.WindowMoves!.TryGet(3,303,out var ordered)&&release.WindowMoves!.TryGet(3,303,out var manual)
            &&ordered.ParentClaim==manual.Claim&&ordered.ParentAuthor==3,"automatic author handover retains its nonzero accepted manual release basis");
        host.Receive(2,nextAuto);survivor.Receive(1,host.Publish());survivor.Receive(1,host.Publish());host.Settle();survivor.Settle();
        Proof.Check(Proof.Same(host.Position(),survivor.Position()),"automatic author return converges with a nonzero manual basis");
        hand.Grab(new Vector3(163,167,173));var fresh=hand.Publish();host.Receive(3,fresh);survivor.Receive(3,fresh);host.Settle();survivor.Settle();
        Proof.Check(Proof.Same(host.Position(),hand.Position())&&Proof.Same(survivor.Position(),hand.Position()),
            "a genuine new manual claim remains authoritative through automatic publisher changes");
    }
    internal static void RewardPlacementHandover()
    {
        const SharedWindowKind kind=SharedWindowKind.RewardShowcase;
        var a=new ActiveNode(1,kind);var b=new ActiveNode(2,kind);var initial=a.Publish();b.Receive(1,initial);b.Settle();
        var endpoint=new RigPose{Position=new Vector3(59,61,67),Rotation=Quaternion.Euler(0,149,0)};
        b.Grab(endpoint);var held=b.Publish();a.Receive(2,held);a.Settle();
        Proof.Check(held.WindowMoves!.TryGet(6,606,out var original),"reward manual mover has a genuine original opening claim");
        a.NativeReward(false);a.Receive(2,b.Publish());var decline=a.Publish();b.Receive(1,decline);
        Proof.Check(b.PlacementAuthor()==2,"actual reward actor-role handshake promotes the remaining native publisher");
        var survivor=b.Publish();Proof.Check(survivor.WindowMoves!.TryGet(6,606,out var relayed)
            &&relayed.OpeningAuthor==original.OpeningAuthor&&relayed.Opening==original.Opening&&relayed.Claim==original.Claim,
            "placement-author handover preserves the simultaneous accepted manual claim and opening");
        // A newly joined VR participant did not receive the old publisher's original pose.
        // Native discovery reports no reward initially, then opens its exact606 copy later.
        a.Roster(1,2,3);b.Roster(1,2,3);
        var c=new ActiveNode(3,kind,roster:new[]{1,2,3});c.NativeReward(false);c.Publish();
        c.Receive(2,survivor);var unavailable=c.Publish();b.Receive(3,unavailable);
        c.NativeReward(true,pending:true);var request=c.Publish();a.Receive(3,request);c.Receive(1,a.Publish());
        Proof.Check(c.PlacementAuthor()==2,"late reward receiver independently elects the current actual native survivor");
        var relay=b.Publish();
        foreach(string invalid in new[]{"sender","key","origin"})
        {
            PresenceState malformed=relay;var row=relay.WindowMoves!.Entries[0];int sender=2;
            if(invalid=="sender")sender=4;
            else if(invalid=="key")row.ContentKey++;
            else row.OpeningAuthor=99;
            malformed.WindowMoves=new SharedWindowMoveOrder(new[]{row});c.Receive(sender,malformed);c.Settle();
            Proof.Check(!c.Follows(sender),"reward relay rejects missing matching "+invalid+" evidence");
        }
        var noHandover=new ActiveNode(3,kind,roster:new[]{1,2,3});noHandover.Receive(2,relay);
        Proof.Check(!noHandover.Follows(2),"reward relay without positive original-participant handover is rejected");
        var departed=new ActiveNode(3,kind,roster:new[]{1,2,3});departed.Roster(2,3);departed.Receive(2,relay);departed.Settle();
        Proof.Check(departed.Follows(2)&&Proof.Same(departed.Pose().Position,endpoint.Position),
            "reward relay accepts positively observed original-participant departure in the same native key");
        // A membership history from606 is not evidence about the new707 chest. Neither fresh
        // receiver has adopted a scope, so a retired-opening watermark cannot mask this check.
        var nextSource=new ActiveNode(2,kind,roster:new[]{1,2,3});var nextReceiver=new ActiveNode(3,kind,roster:new[]{1,2,3});
        nextSource.Roster(2,3);nextReceiver.Roster(2,3);nextSource.NewRewardKey(707);nextReceiver.NewRewardKey(707,pending:true);
        PresenceState wrongHistory=nextSource.Publish();var historical=wrongHistory.WindowMoves!.Entries[0];historical.OpeningAuthor=1;
        wrongHistory.WindowMoves=new SharedWindowMoveOrder(new[]{historical});nextReceiver.Receive(2,wrongHistory);
        Proof.Check(!nextReceiver.Follows(2),"reward relay cannot borrow participant departure evidence from a different native key");
        c.Receive(2,b.Publish());c.Settle();
        Proof.Check(Proof.Same(c.Pose().Position,endpoint.Position)&&Proof.Same(c.Pose().Rotation,endpoint.Rotation)
            &&MathF.Abs(c.Reference()-198.12f)<.001f,
            "reward survivor relay adopts the existing opening for a late native receiver");
        RewardLateManualMover();
    }
    private static void RewardLateManualMover()
    {
        const SharedWindowKind kind=SharedWindowKind.RewardShowcase;
        var a=new ActiveNode(1,kind);var b=new ActiveNode(2,kind);b.Receive(1,a.Publish());b.Settle();
        // Both moves are genuine native bar claims. Two claims ensure that the late player's
        // cold counter cannot win accidentally by an equal-clock higher-ID tie.
        for(int move=0;move<2;move++)
        {
            a.Grab(new RigPose{Position=new Vector3(7+move*12,11,13),Rotation=Quaternion.Euler(0,47+move*23,0)});
            b.Receive(1,a.Publish());a.Drop();b.Receive(1,a.Publish());b.Settle();
        }
        var endpoint=a.Pose();
        a.NativeReward(false);a.Receive(2,b.Publish());b.Receive(1,a.Publish());
        Proof.Check(b.PlacementAuthor()==2,"reward follower becomes the actual surviving publisher without a fabricated manual claim");
        var relayed=b.Publish();Proof.Check(relayed.WindowMoves!.TryGet(6,606,out var initial)
            &&initial.Phase==SharedWindowMovePhase.Initial&&initial.Claim==0,
            "reward successor relays an initial opening rather than impersonating the departed manual mover");
        a.Roster(1,2,3);b.Roster(1,2,3);
        var c=new ActiveNode(3,kind,roster:new[]{1,2,3});c.NativeReward(false);c.Publish();c.Receive(2,relayed);
        b.Receive(3,c.Publish());c.NativeReward(true,pending:true);var request=c.Publish();a.Receive(3,request);c.Receive(1,a.Publish());
        c.Receive(2,b.Publish());c.Settle();
        Console.WriteLine($"LATE_REWARD before grab source=({endpoint.Position.x},{endpoint.Position.y},{endpoint.Position.z}) survivor=({b.Pose().Position.x},{b.Pose().Position.y},{b.Pose().Position.z}) late=({c.Pose().Position.x},{c.Pose().Position.y},{c.Pose().Position.z}) follows={c.Follows(2)} author={c.PlacementAuthor()} sourceAngle={Quaternion.Angle(c.Pose().Rotation,endpoint.Rotation)}");
        Proof.Check(Proof.Same(c.Pose().Position,endpoint.Position)&&Proof.Same(c.Pose().Rotation,endpoint.Rotation),
            "late reward view adopts the surviving publisher's last actual manual endpoint");
        // The native reveal consumer now admits that valid, ready shared pose. A real grip is
        // possible only after this declared presentation boundary has made the view visible.
        c.NativeReward(true);c.Publish();
        var moved=new RigPose{Position=new Vector3(179,181,191),Rotation=Quaternion.Euler(0,197,0)};
        c.Grab(moved);var held=c.Publish();b.Receive(3,held);c.Drop();b.Receive(3,c.Publish());b.Settle();
        c.Receive(2,b.Publish());c.Settle();
        Proof.Check(Proof.Same(b.Pose().Position,moved.Position)&&Proof.Same(c.Pose().Position,moved.Position)
            &&Proof.Same(b.Pose().Rotation,c.Pose().Rotation)&&MathF.Abs(b.Reference()-c.Reference())<.001f,
            "a late genuine reward grip converges after the former manual publisher hands off to a follower");
    }
    private static void RegisterEdges()
    {
        for(byte kind=1;kind<=6;kind++)
        {
            var a=new SharedWindowMoveRegister(1,kind);var b=new SharedWindowMoveRegister(2,kind);
            a.Open(99,1);b.Open(99,1);a.Sample(out var opening);
            var forged=opening;forged.OpeningAuthor=3;
            Proof.Check(!b.Observe(1,in forged,1,0),"placement sender cannot invent a different opening author");
            b.Observe(1,in opening,1,0);
            for(int i=0;i<260;i++)
            {
                Proof.Check(a.Begin(i*2),"genuine later gesture advances past the observed peer clock");a.Sample(out var held);
                b.Observe(1,in held,1,i*2);a.Release();a.Sample(out var release);b.Observe(1,in release,1,i*2+.1f);
                Proof.Check(b.Begin(i*2+.2f),"released peer never remains a permanent high-counter winner");b.Sample(out var newer);
                a.Observe(2,in newer,1,i*2+.2f);b.Release();b.Sample(out var done);a.Observe(2,in done,1,i*2+.3f);
            }
            a.Close();b.Close();a.Open(99,1);b.Open(99,1);a.Sample(out var reopened);b.Observe(1,in reopened,1,600);
            Proof.Check(!b.Observe(1,in opening,1,601),"same-key native reopening rejects a delayed previous opening");
            Proof.Check(b.Begin(602),"new opening has independent gesture ownership");b.Sample(out var fresh);
            a.Observe(2,in fresh,1,602);b.Release();b.Sample(out var lateRelease);
            Proof.Check(a.Begin(606),"owner loss permits a new grip after the bounded stationary lease");a.Sample(out var takeover);
            b.Observe(1,in takeover,1,606);a.Observe(2,in lateRelease,1,607);
            Proof.Check(a.LocalWins&&b.Winner==1,"owner-loss takeover and late prior release converge");
        }
        var wrapA=new SharedWindowMoveRegister(1,3);var wrapB=new SharedWindowMoveRegister(2,3);
        foreach(var register in new[]{wrapA,wrapB})
        {
            // Model a long-lived process just before wrap, rather than injecting a counter
            // more than half the serial-number space away from an already observed low clock.
            typeof(SharedWindowMoveRegister).GetField("_clock",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(register,uint.MaxValue-3);
            typeof(SharedWindowMoveRegister).GetField("_sequence",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(register,uint.MaxValue-3);
        }
        wrapA.Open(88,1);wrapB.Open(88,1);wrapA.Sample(out var start);wrapB.Observe(1,in start,1,0);
        Proof.Check(wrapB.Begin(0),"actual second controller begins the near-wrap claim");wrapB.Sample(out var high);
        Proof.Check(high.Claim==uint.MaxValue-1&&wrapA.Observe(2,in high,1,0),"actual near-wrap held producer reaches the first receiver");
        Proof.Check(wrapA.Begin(4),"real expired grip advances a near-wrap claim clock");
        wrapA.Release();wrapA.Sample(out var end);
        Proof.Check(wrapB.Observe(1,in end,1,4),"first nonzero manual receipt can follow the initial zero sentinel near wrap");
        var zeroAfterClaim=start;zeroAfterClaim.Sequence=uint.MaxValue;
        Proof.Check(!wrapB.Observe(1,in zeroAfterClaim,1,4.1f),"newer initial zero cannot replace an accepted nonzero claim at wrap");
        Proof.Check(wrapB.Begin(5),"real second controller may regrab through clock wrap");wrapB.Sample(out var beforeWrap);
        Proof.Check(wrapA.Observe(2,in beforeWrap,1,5),"wrapped manual claim follows the real preceding sample serially");
        wrapB.Sample(out var maximum);Proof.Check(wrapA.Observe(2,in maximum,1,5.1f),"actual sample sequence reaches uint maximum serially");
        wrapB.Sample(out var wrapped);Proof.Check(wrapA.Observe(2,in wrapped,1,5.2f),"actual sample sequence wraps without an out-of-range serial jump");
        Proof.Check(wrapped.Claim==1&&wrapped.Sequence==1&&wrapA.Winner==2&&wrapB.LocalWins,"actual controller register accepts the wrapped new gesture and sample");
        Proof.Check(SharedWindowMoveRegister.Newer(1,uint.MaxValue)&&!SharedWindowMoveRegister.Newer(uint.MaxValue,1),
            "wide sample and claim comparison survives unsigned wrap");
        Proof.Check(SharedWindowMoveRegister.Compare(0,0,uint.MaxValue,2)<0,"zero automatic basis stays older across wrap");
    }
    private static void InitialClockGuards()
    {
        var author=new SharedWindowMoveRegister(1,3);author.Open(741,1);author.Sample(out var opening);
        foreach(string invalid in new[]{"publisher","key","origin","invalid-opening","duplicate"})
        {
            var viewer=new SharedWindowMoveRegister(2,3);viewer.Open(741,1);
            Proof.Check(viewer.Observe(1,in opening,1,0),"valid initial scope is observed before the clock-poison control");
            var rejected=opening;rejected.Sequence=500;int sender=1;
            if(invalid=="publisher")sender=3;
            else if(invalid=="key")rejected.ContentKey++;
            else if(invalid=="origin")rejected.OpeningAuthor=99;
            else if(invalid=="invalid-opening")rejected.Opening=0;
            else rejected.Sequence=opening.Sequence;
            Proof.Check(!viewer.Observe(sender,in rejected,1,1),"invalid initial metadata is rejected before any clock hint: "+invalid);
            Proof.Check(viewer.Begin(2),"a rejected initial does not revoke genuine local admission");viewer.Sample(out var actual);
            Proof.Check(actual.Claim==opening.Sequence+1,"only a validated initial receipt advances the next genuine claim: "+invalid);
        }
        var reader=new SharedWindowMoveRegister(2,3);reader.Open(741,1);reader.Observe(1,in opening,1,0);
        reader.Begin(1);reader.Sample(out var first);
        Proof.Check(first.Claim==opening.Sequence+1,"a valid opening's initial clock hint precedes its first genuine later claim");
        var released=opening;released.Phase=SharedWindowMovePhase.Released;released.Claim=90;released.Sequence=2;
        Proof.Check(author.Observe(3,in released,1,2),"current publisher observes an actual earlier participant's released claim");
        author.Sample(out var hinted);
        Proof.Check(hinted.Phase==SharedWindowMovePhase.Initial&&hinted.Claim==0&&hinted.Sequence>released.Claim,
            "initial publisher's sequence carries the observed manual clock without a fabricated claim");
        var cold=new SharedWindowMoveRegister(4,3);cold.Open(741,1);cold.Observe(1,in hinted,1,3);
        cold.Begin(4);cold.Sample(out var novel);
        Proof.Check(novel.Claim>hinted.Sequence&&author.Observe(4,in novel,1,4)&&author.Winner==4,
            "a cold genuine grab after a claimless publisher hint outranks the earlier released owner");
        var old=hinted;old.Sequence++;
        Proof.Check(!author.Observe(3,in old,1,5),"an old participant cannot republish the current author's initial hint");
        var reversed=released;reversed.Claim--;reversed.Sequence++;
        Proof.Check(!author.Observe(3,in reversed,1,5),"a newer packet cannot lower an already nonzero manual claim");
        foreach(uint boundary in new[]{0x7ffffffeu,0x80000000u,0x80000001u,uint.MaxValue-2})
        {
            var longLived=new SharedWindowMoveRegister(1,3);
            typeof(SharedWindowMoveRegister).GetField("_clock",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(longLived,boundary-1);
            typeof(SharedWindowMoveRegister).GetField("_sequence",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(longLived,boundary-1);
            longLived.Open(817,1);longLived.Sample(out var firstHint);
            var successor=new SharedWindowMoveRegister(2,3);successor.Open(817,1);
            Proof.Check(firstHint.Sequence==boundary&&successor.Observe(1,in firstHint,1,0),"cold participant observes its first serial receipt near a clock boundary");
            successor.Open(817,2);successor.Sample(out var promoted);
            Proof.Check(SharedWindowMoveRegister.Newer(promoted.Sequence,firstHint.Sequence),
                "first publisher sample after cold promotion advances the observed nonzero clock serially");
            var later=new SharedWindowMoveRegister(3,3);later.Open(817,2);
            Proof.Check(later.Observe(2,in promoted,2,1,allowOpeningRelay:true)&&later.Begin(2),"authorized successor relay permits a later genuine grip at clock boundaries");
            later.Sample(out var novelAfterPromotion);
            Proof.Check(SharedWindowMoveRegister.Newer(novelAfterPromotion.Claim,promoted.Sequence),"genuine grip follows the cold successor's initial hint serially");
        }
    }
}
