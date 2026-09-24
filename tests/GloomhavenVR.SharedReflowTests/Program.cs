using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
namespace GloomhavenVR.Net;
internal static partial class RemoteMapStory
{
    private static int _checks;
    private static void Check(bool condition,string why) { _checks++;if(!condition)throw new Exception(why); }
    private static void Fresh(int id=2,bool host=false) {
        MapStoryLifecycle.Fresh();ResetReflow();StoryLocal.Reset();QuestLocal.Reset();EncounterLocal.Reset();SharedWindows.Grabs.Clear();
        FFSNetwork.IsOnline=true;FFSNetwork.IsHost=host;NetPlayerActors.Id=id;MapRoomDriver.Active=true;
        Time.unscaledTime=10;Time.frameCount=100;
        foreach(var kind in new[]{SharedWindowKind.MapStory,SharedWindowKind.QuestConfirm,SharedWindowKind.Encounter}) {
            var grab=new GrabbableModal();SharedWindows.Grabs[kind]=grab;
            var local=ReflowLocal(kind)!;local.Key=11;local.Grab=grab;local.HaveBaseline=true;local.PageCount=3;local.FinishedUntil=70;
        }
    }
    private static PresenceState Peer(byte held=0,byte automatic=0,bool host=false,bool known=true)=>new() {
        HasMapRoom=true,MapRoomFlags=(byte)(1|(host?2:0)),HasSharedWindowMotion=known,
        SharedWindowHeldMask=held,SharedWindowReflowMask=automatic,
        HasMapStoryLifecycle=true,MapStoryLifecycleEntries=MapStoryLifecycle.Sender.Sample(MapStoryLifecycle.Remote)
    };
    private static bool Settled() {CanArrangeSharedWindows();Time.unscaledTime+=0.4f;return CanArrangeSharedWindows();}
    private static void Main() {
        Fresh();Check(!CanArrangeSharedWindows(),"First census must settle");Check(Settled(),"Only VR participant may arrange with flat host");
        Fresh(3);var peer=Peer();ObserveReflow(2,in peer);Check(!Settled(),"Lowest VR map participant must win");
        Fresh(3,true);ObserveReflow(2,in peer);Check(Settled(),"Participating native host must win");
        Fresh(1);peer=Peer(host:true);ObserveReflow(4,in peer);Check(!Settled(),"Peer native host must win");
        peer.HasMapRoom=false;ObserveReflow(4,in peer);Check(Settled(),"Flat or disabled-map host must not block VR author");
        Fresh(0);Check(!Settled(),"Unknown local ID must refuse authority");
        Fresh(3);peer=Peer();ObserveReflow(1,in peer);Check(!Settled(),"Fresh lower peer must win");Time.unscaledTime+=6;Check(Settled(),"Stale peer must not block forever");
        for(byte bit=1;bit<=4;bit*=2) {
            Fresh();Check(Settled(),"Fresh election");peer=Peer(held:bit);ObserveReflow(3,in peer);
            Check(!CanArrangeSharedWindows(),"Stationary remote grip must block room making");
            peer=Peer();ObserveReflow(3,in peer);Check(CanArrangeSharedWindows(),"Explicit release permits room making");
        }
        Fresh();peer=Peer(known:false);ObserveReflow(3,in peer);Check(!Settled(),"Missing metadata must not mean released");
        foreach(var kind in new[]{SharedWindowKind.MapStory,SharedWindowKind.QuestConfirm,SharedWindowKind.Encounter}) {
            Fresh();Check(Settled(),"Fresh election");var local=ReflowLocal(kind)!;var grab=SharedWindows.Grabs[kind];
            grab.IsGrabbed=true;Check(!BeginSharedReflow(kind),"Local stationary grip must win");grab.IsGrabbed=false;
            Check(BeginSharedReflow(kind),"Known fitted frame must admit animation");byte stamp=local.PoseStamp;
            Check(BeginSharedReflow(kind)&&local.PoseStamp==stamp,"Repeated begin must not restart ownership");
            Check(SharedReflowMoving&&SharedReflowSendDue,"Animation must request fast cadence and first sample");
            PresenceState sent=default;SampleReflow(ref sent);
            Check(sent.HasSharedWindowMotion&&sent.SharedWindowReflowMask==ReflowBit(kind),"Wire mask must name animated kind");
            Check(!SharedReflowSendDue,"Sampling must clear edge, never flood at frame rate");
            grab.Position=new Vector3{x=12};EndSharedReflow(kind);
            Check(!SharedReflowMoving&&SharedReflowSendDue&&local.PoseOwned&&local.FramePos.x==12,"Endpoint must be owned and sent immediately");
            SampleReflow(ref sent);Check(sent.SharedWindowReflowMask==0&&!SharedReflowSendDue,"End must publish zero flag and return to idle");
            Check(BeginSharedReflow(kind),"A new opening correction may begin");int revision=SharedReflowRevision(kind);
            grab.Position=new Vector3{x=17};
            peer=Peer(held:ReflowBit(kind));ObserveReflow(3,in peer);
            Check(!local.Reflow&&!local.PoseOwned&&SharedReflowRevision(kind)!=revision,"Remote grip must cancel animation ownership");
            Check(local.FramePos.x==17,"Cancellation must baseline unsent tween remainder to prevent echo");
            EndSharedReflow(kind);Check(!local.PoseOwned,"Cancellation cleanup must not reclaim remote window");
        }
        Fresh();Check(Settled(),"Fresh election");Check(BeginSharedReflow(SharedWindowKind.MapStory),"Story starts");
        var stamps=new Dictionary<int,byte>{{3,1}};var entry=new SharedWindowEntry{ContentKey=11,Flags=2,PoseStamp=2};
        peer=Peer(automatic:1);ObserveReflow(3,in peer);ReceiveStoryPose(3,in peer,in entry,stamps);
        Check(StoryLocal.Reflow,"Automatic samples must not impersonate a manual grab");
        peer=Peer();ObserveReflow(3,in peer);ReceiveStoryPose(3,in peer,in entry,stamps);
        Check(!StoryLocal.Reflow,"New manual movement stamp must cancel animation");
        Fresh();Check(Settled(),"Fresh election");Check(BeginSharedReflow(SharedWindowKind.MapStory),"Story starts");
        stamps.Clear();peer=Peer();ObserveReflow(3,in peer);
        ReceiveStoryPose(3,in peer,in entry,stamps);
        Check(!StoryLocal.Reflow,"First remote drag between samples must interrupt automatic motion");
        Fresh();Check(Settled(),"Fresh election");var changed=new GrabbableModal();SharedWindows.Grabs[SharedWindowKind.MapStory]=changed;
        Check(!BeginSharedReflow(SharedWindowKind.MapStory),"Replacement identity must settle before claiming");Time.frameCount+=3;
        Check(BeginSharedReflow(SharedWindowKind.MapStory),"Settled replacement may arrange");
        SharedWindows.Grabs[SharedWindowKind.MapStory]=new();EndSharedReflow(SharedWindowKind.MapStory);
        Check(!StoryLocal.PoseOwned,"End must never claim a different window");
        Fresh();Check(Settled(),"Fresh election");Check(BeginSharedReflow(SharedWindowKind.MapStory),"Story starts");
        Check(SampleFinished(true,new object(),11)==1&&SendBuffer[0].Flags==6,"Visible finished story must carry FINISHED plus pose, never OPEN");
        EndSharedReflow(SharedWindowKind.MapStory);
        Check(SampleFinished(true,new object(),100)==1&&SendBuffer[0].Flags==6,"Visible finished endpoint must survive linger expiry");
        SharedWindows.Grabs[SharedWindowKind.MapStory].Visible=false;
        Check(SampleFinished(true,new object(),100)==0,"Invisible story must fall silent after linger");
        Fresh();Check(Settled(),"Fresh election");var staleGrip=Peer(held:1);var stalePose=Peer();
        MapStoryLifecycle.Reopen();Check(BeginSharedReflow(SharedWindowKind.MapStory),"Repeated story starts");
        ObserveReflow(3,in staleGrip);
        Check(StoryLocal.Reflow,"Prior opening grip must not cancel repeated story animation");
        Check(CanArrangeSharedWindows(),"Prior opening grip must not block current layout ownership");
        ObserveReflow(3,in stalePose);stamps.Clear();ReceiveStoryPose(3,in stalePose,in entry,stamps);
        Check(StoryLocal.Reflow,"Prior opening manual pose must not cancel repeated story animation");
        peer=Peer();ObserveReflow(3,in peer);ReceiveStoryPose(3,in peer,in entry,stamps);
        Check(!StoryLocal.Reflow,"Current opening manual pose must cancel repeated story animation");
        Fresh();Check(Settled(),"Fresh election");peer=Peer(held:1);ObserveReflow(3,in peer);
        Check(!CanArrangeSharedWindows(),"Current cached story grip blocks arrangement");
        MapStoryLifecycle.Reopen();
        Check(CanArrangeSharedWindows(),"Cached grip must expire when native opening changes before next packet");
        peer=Peer(held:2);peer.HasMapStoryLifecycle=false;ObserveReflow(3,in peer);
        Check(!CanArrangeSharedWindows(),"Quest grip remains authoritative without story provenance");
        Console.WriteLine($"Shared window reflow: {_checks} production assertions passed.");
    }
}
