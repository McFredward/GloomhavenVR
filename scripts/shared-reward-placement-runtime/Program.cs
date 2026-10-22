using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;
internal static class Program
{
    private static int count;
    private static void Check(bool yes,string why){count++;if(!yes)throw new Exception(why);}
    private static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.000001f;
    private static (UIWindow window,ConvertedPanel panel,GrabbableModal grab) Reset(bool author)
    {
        ModalFallback.Reset();RemoteMapStory.Reset();RewardShowcasePlacement.ClearInitialPose();RewardShowcasePlacement.ClearInitialAuthority();
        SharedWindows.Online=true;SharedWindowSpawnBridge.IsInitialAuthor=author;PanelLayout.WorldScale=12;SharedWindowSize.ReferenceWorldScale=12;
        CanvasConversion.WorldCamera=new();CanvasConversion.WorldCamera.transform.position=new(10,5,20);
        VersionGuard.Builds.Clear();VersionGuard.Roster(1,2,3,4);for(int i=1;i<=4;i++)VersionGuard.Builds[i]=NetProtocol.ModBuild;
        NetPlayerActors.Id=author?1:2;Time.unscaledTime=10;Time.frameCount++;PostQuestRewardSync.Permission=true;RewardShowcase.ResetCallback();
        var window=new UIWindow();var panel=new ConvertedPanel();var grab=new GrabbableModal();
        RewardShowcase.Window=window;ModalFallback.Register(window,panel,grab);return(window,panel,grab);
    }
    private static void Main()
    {
        var s=Reset(true);Check(RemoteMapStory.Owner(42,1)==1,"Native host remains the initial reward author");
        RewardShowcasePlacement.SetInitialAuthority(42,true);
        Check(ModalFallback.Preview(s.panel,out var first),"Native host can compute its hidden first seat");ModalFallback.Capture(s.panel);
        CanvasConversion.WorldCamera!.transform.position=new(100,50,200);
        Check(RewardShowcasePlacement.TryReveal(s.panel),"Native host reveals after final fitted spawn");
        Check(Same(s.grab.Root.position,first)&&s.grab.Snaps==1,"Final fit must replay the captured host gaze rather than a later HMD pose");
        Check(s.panel.SharedSpawnPoseReady&&ModalFallback.Preserved(s.panel),"First accepted reward spawn retires the legacy fit anchor");
        s.grab.Root.position=new(-9,8,-7);var manual=s.grab.Root.position;s.panel.HostGo.transform.position=manual;
        Check(ModalFallback.EnsureRewardHostSpawn(s.panel)&&Same(s.grab.Root.position,manual)&&s.grab.Snaps==1,"An established reward pose must preserve a manual move");

        s=Reset(false);var peers=new List<int>();NetAvatarDriver.CollectRewardPosePeers(peers);
        Check(peers.Count==3&&peers.Contains(1)&&!peers.Contains(2),"Stable compatible session census includes the cold host without any avatar");
        VersionGuard.Builds[3]=NetProtocol.ModBuild-1;NetAvatarDriver.CollectRewardPosePeers(peers);
        Check(!peers.Contains(3)&&peers.Contains(1),"Incompatible participants cannot block reward placement");
        Check(RemoteMapStory.Owner(42,2)==1,"Cold follower must not become author merely because host pose is missing");
        RewardShowcasePlacement.SetInitialAuthority(42,false);
        Check(!RewardShowcasePlacement.TryReveal(s.panel),"Cold follower stays hidden until a ready elected pose exists");
        Check(s.grab.Snaps==0&&s.grab.Placements==0,"Cold follower cannot expose its private provisional pose");
        Check(RewardShowcase.CanConfirm&&RewardShowcase.TryConfirm()&&s.window.NativeContinues==1,"Presentation waiting must never gate the original native Continue callback");
        Check(!RewardShowcase.TryConfirm()&&s.window.NativeContinues==1,"Native input frame latch still prevents duplicate Continue");
        Time.frameCount++;PostQuestRewardSync.Permission=false;
        Check(!RewardShowcase.CanConfirm&&!RewardShowcase.TryConfirm(),"Genuine native Continue authority must remain enforced while hidden");
        PostQuestRewardSync.Permission=true;
        RewardShowcasePlacement.SetInitialPose(42,new(7,8,9),Quaternion.identity,1.5f,ready:false);
        Check(!RewardShowcasePlacement.TryReveal(s.panel),"An unfinished elected fit must not reveal a follower");
        RewardShowcasePlacement.SetInitialPose(42,new(7,8,9),Quaternion.identity,1.5f,ready:true);
        Check(RewardShowcasePlacement.TryReveal(s.panel)&&Same(s.grab.Root.position,new(7,8,9)),"Ready elected reward pose is applied before follower reveal");
        Check(s.grab.Root.localScale.x==1.5f&&ModalFallback.Preserved(s.panel),"Follower keeps the shared grab factor and retires any private replay anchor");

        s=Reset(false);RemoteMapStory.Owner(42,2);RemoteMapStory.Refresh(2);
        var absentHost=new RewardPoseHandshake();absentHost.Observe(2,RemoteMapStory.Request(),0);absentHost.Refresh(1,new(){2},0,10);
        RemoteMapStory.Observe(1,absentHost.Sample());RemoteMapStory.Refresh(2);
        Check(RemoteMapStory.Owner(42,2)==2,"Explicit opening-qualified host decline promotes the real native-window participant");
        RewardShowcasePlacement.SetInitialAuthority(42,true);
        Check(RewardShowcasePlacement.IsInitialAuthor&&!SharedWindowSpawnBridge.IsInitialAuthor,"Declined-host reward surrogate uses reward election rather than generic host authority");
        Check(RewardShowcasePlacement.TryReveal(s.panel)&&s.grab.Snaps==1,"Elected reward surrogate must compute and reveal a real gaze spawn");
        Check(Same(s.grab.Root.position,new(10,5,36.8f)),"Surrogate spawn follows its actual head instead of the origin placeholder");
        RemoteMapStory.Visible(2,42);
        NetPlayerActors.Id=1;
        Check(RemoteMapStory.Owner(42,1)==2,"A late lower-id native host must follow the already visible reward pose");

        s=Reset(false);RewardShowcasePlacement.SetInitialAuthority(42,false);
        RewardShowcasePlacement.SetInitialPose(43,new(7,8,9),Quaternion.identity,1);
        Check(!RewardShowcasePlacement.TryReveal(s.panel)&&s.grab.Placements==0,"A pose for another chest cannot release this reward reveal");
        RewardShowcasePlacement.SetInitialPose(42,new(7,8,9),Quaternion.identity,1);Time.unscaledTime+=3;
        Check(!RewardShowcasePlacement.TryReveal(s.panel),"Expired unrefreshed pose cannot place a fresh native reward");
        s=Reset(true);CanvasConversion.WorldCamera=null;RewardShowcasePlacement.SetInitialAuthority(42,true);
        Check(!RewardShowcasePlacement.TryReveal(s.panel)&&!s.panel.SharedSpawnPoseReady,"Missing author camera must not falsely finalize the origin placeholder");
        Check(RewardShowcase.TryConfirm(),"A missing placement camera must not add a native continuation blocker");
        s=Reset(false);RewardShowcasePlacement.SetInitialAuthority(42,false);SharedWindowSpawnBridge.IsInitialAuthor=true;
        VersionGuard.Roster(); // Actual VersionGuard.Reset clears this census when transport goes offline.
        RemoteMapStory.ResolveReward(2);
        Check(RewardShowcasePlacement.IsInitialAuthor&&RewardShowcasePlacement.TryReveal(s.panel),"Offline census reset must retire the prior follower authority through actual reward resolution");
        s=Reset(false);RewardShowcasePlacement.SetInitialAuthority(42,false);SharedWindowSpawnBridge.IsInitialAuthor=true;
        RemoteMapStory.ResolveReward(0); // Degraded transport exposes no local player id.
        Check(RewardShowcasePlacement.IsInitialAuthor&&RewardShowcasePlacement.TryReveal(s.panel),"Degraded transport with no identity must release stale reward follower authority");
        s=Reset(false);RewardShowcasePlacement.SetInitialAuthority(42,false);SharedWindowSpawnBridge.IsInitialAuthor=true;
        RewardShowcasePlacement.ClearInitialAuthority();
        Check(RewardShowcasePlacement.IsInitialAuthor&&RewardShowcasePlacement.TryReveal(s.panel),"Driver teardown authority reset must restore usable local reward placement");
        Console.WriteLine($"Shared reward placement: {count} production-bound assertions passed.");
    }
}
