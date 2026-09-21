using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string description) { _assertions++;if(!value)throw new Exception(description); }
    private static void Near(float value,float wanted,string description)=>Check(Math.Abs(value-wanted)<.001f,description+$" {value} != {wanted}");
    private static void Reset()
    {
        TownServicePopulation.Reset();RemoteTownResidents.Reset();TownServiceMirror.RemoteSessions.Clear();
        WorldUIConfig.ImmersiveTownServices.Value=true;MapRoomDriver.Active=true;MapRoomDriver.FrameReady=true;
        MapRoomDriver.Center=new(10,20,30);MapRoomDriver.Scale=2;NetPlayerActors.Local=10;
        TownServicePresentation.Active=false;TownServicePresentation.Service=0;TownServicePresentation.SessionAge=0;
        TownServiceStation.Ready=true;TownServiceStation.Missing.Clear();TownServiceStation.Creates=0;TownServiceStation.Disposals=0;
        Time.unscaledTime=100;Time.unscaledDeltaTime=.3f;TownServiceStation.Floor=4;
    }
    private static void Tick(float dt=.3f) { Time.unscaledDeltaTime=dt;Time.unscaledTime+=dt;TownServicePopulation.Tick(); }
    private static TownResidentsState State(float tag=7)
    {
        var state=new TownResidentsState{Active=true};
        for(int n=0;n<3;n++)state.Set(n,new TownResidentPose{Pose=new RigPose{Position=new Vector3(n+1,tag,n+2),Rotation=Quaternion.identity},Scale=1,Age=tag,Visibility=255,ActorFloorOffset=-.03f*(n+1),FurnitureBottom=-.06f*(n+1)});
        return state;
    }
    private static void Observe(int peer,TownResidentsState state)=>RemoteTownResidents.Observe(peer,new PresenceState{HasTownResidents=true,TownResidents=state});
    private static void AllVisible()
    {
        Check(TownServiceStation.Live.Count==3,"all three permanent residents");
        for(byte s=1;s<=3;s++)
        {Check(TownServicePopulation.Available(s),"resident ready affordance");Near(TownServiceStation.Live[s].Visibility,1,"resident fully visible");Check(TownServiceVisitTarget.Live[s].Enabled,"ready resident input");}
    }
    private static void Main()
    {
        Reset();Tick();AllVisible();Check(TownServicePopulation.Published.Active,"ready population advertised");
        for(int i=0;i<8;i++)Tick(1);AllVisible();Check(TownServiceStation.Creates==3,"no visitor-dependent respawn or repeated create");
        Check(TownServiceStation.Live.Values.All(s=>s.Clip=="Idle"),"no greeting without a visit");
        TownServicePresentation.Active=true;TownServicePresentation.Service=2;TownServicePresentation.SessionAge=.4f;Tick(.1f);
        Check(TownServiceStation.Live[2].Clip=="Greeting","native visit greets");Check(TownServiceStation.Live[1].Clip=="Idle","unvisited resident remains idle");
        TownServicePresentation.Active=false;Tick();AllVisible();
        WorldUIConfig.ImmersiveTownServices.Value=false;Tick();Check(TownServiceStation.Live.Count==0,"opt out removes unvisited residents");Check(!TownServicePopulation.Published.Active,"opt out withdraws authority");
        WorldUIConfig.ImmersiveTownServices.Value=true;Tick();AllVisible();Check(TownServiceStation.Creates==6,"re-enable re-creates all residents");
        MapRoomDriver.Active=false;Tick();Check(TownServiceStation.Live.Count==0&&TownServiceVisitTarget.Live.Count==0,"leaving map destroys residents and input");Check(TownServicePopulation.Frame==null,"leaving map clears common frame");

        Reset();TownServiceStation.Ready=false;Tick();Check(TownServiceStation.Live.Count==3,"assets preparing residents allocated");
        Check(!TownServicePopulation.Published.Active,"not ready never advertised as authority");
        for(byte s=1;s<=3;s++) {Check(!TownServicePopulation.Available(s),"not ready never replaces native affordance");Near(TownServiceStation.Live[s].Visibility,0,"counter hidden until original dressing ready");Check(!TownServiceVisitTarget.Live[s].Enabled,"no input before assets");}
        TownServiceStation.Ready=true;Tick(.05f);Check(TownServiceStation.Live[1].Visibility<1,"ready entrance retains animated fade");Check(!TownServiceVisitTarget.Live[1].Enabled,"no input during entrance");Tick(.3f);AllVisible();

        Reset();TownServiceStation.Missing.Add(2);Tick();Check(!TownServicePopulation.Available(2),"missing bundle no fake resident");Check(!TownServicePopulation.Published.Active,"partial population cannot author");int attempts=TownServiceStation.Creates;Tick(.2f);Check(TownServiceStation.Creates==attempts,"asset retry bounded");TownServiceStation.Missing.Clear();Tick(2.1f);AllVisible();
        Reset();MapRoomDriver.FrameReady=false;Tick();Check(TownServiceStation.Live.Count==0,"unavailable map frame cannot spawn");MapRoomDriver.FrameReady=true;Tick();AllVisible();

        Reset();Observe(5,State(5));Observe(2,State(2));Check(RemoteTownResidents.TryAuthor(out var chosen,out var elapsed),"remote author elected");Near(chosen.Merchant.Age,2,"lowest fresh player wins");Near(elapsed,0,"new packet elapsed zero");
        Tick(.5f);Near(TownServiceStation.Live[1].Root.position.y,24,"author pose mapped through shared frame");Near(TownServiceStation.Live[1].Age,2.5f,"author animation extrapolated");
        for(byte s=1;s<=3;s++)
        {
            Near(TownServiceStation.Live[s].ActorFloorOffset,-.03f*s,"observer applies author's sole height");
            Near(TownServiceStation.Live[s].FurnitureBottom,-.06f*s,"observer applies author's furniture contact");
            Near(TownServicePopulation.Published.At(s-1).ActorFloorOffset,-.03f*s,"republished sole height stays authored");
            Near(TownServicePopulation.Published.At(s-1).FurnitureBottom,-.06f*s,"republished support height stays authored");
        }
        Check(TownServiceStation.Live.Values.All(s=>s.LastAuthor==false),"followers never choose their local floor");TownServiceStation.Floor=-100;Tick(.2f);Near(TownServiceStation.Live[1].Root.position.y,24,"viewer environment floor cannot overwrite author");
        RemoteTownResidents.Forget(2);Check(RemoteTownResidents.TryAuthor(out chosen,out _),"next peer after departure");Near(chosen.Merchant.Age,5,"next lowest wins");
        Observe(5,new TownResidentsState{Active=false});Check(!RemoteTownResidents.TryAuthor(out _,out _),"opt-out withdraws immediately");Tick();Check(TownServiceStation.Live.Values.All(s=>s.LastAuthor==true),"authority handover restores local placement");Near(TownServiceStation.Live[1].Root.position.y,-100,"new author applies current environment floor");
        Observe(2,State());Time.unscaledTime+=NetProtocol.StaleTimeoutSeconds+.01f;Check(!RemoteTownResidents.TryAuthor(out _,out _),"stale author expires");
        Observe(2,State());RemoteTownResidents.Observe(2,default);Check(!RemoteTownResidents.TryAuthor(out _,out _),"missing resident presence withdraws prior author");
        Observe(2,State());NetPlayerActors.Local=1;Check(!RemoteTownResidents.TryAuthor(out _,out _),"lower local player retains authority");WorldUIConfig.ImmersiveTownServices.Value=false;Check(RemoteTownResidents.TryAuthor(out _,out _),"opted-out local cannot claim authority");

        Reset();WorldUIConfig.ImmersiveTownServices.Value=false;Observe(2,State());Tick();Check(TownServiceStation.Live.Count==0,"remote permanent residents alone do not override opted-out viewer");
        TownServiceMirror.RemoteSessions[2]=new TownServiceSessionInfo{Peer=2,Active=true,Service=1,ReceivedTime=Time.unscaledTime,SessionAge=.3f};Tick(.1f);
        Check(TownServiceStation.Live.Count==1&&TownServiceStation.Live.ContainsKey(1),"actual remote visitor only opens its station for opted-out viewer");Check(!TownServiceVisitTarget.Live[1].Enabled,"opted-out viewer has no immersive visit input");Check(TownServiceVisitTarget.Live[1].Visible,"visible remote resident still occludes behind UI for opted-out viewer");Check(!TownServicePopulation.Published.Active,"observer of remote visit never advertises enabled population");
        Time.unscaledTime+=NetProtocol.StaleTimeoutSeconds+.1f;Tick();Check(TownServiceStation.Live.Count==0,"stale visitor station retires");Check(!TownServicePopulation.HasRemoteVisitors,"stale visitor does not keep remote presence alive");
        Reset();var presence=default(PresenceState);MapRoomDriver.Active=false;RemoteTownResidents.Sample(ref presence);Check(!presence.HasTownResidents,"no resident presence outside map");MapRoomDriver.Active=true;Tick();RemoteTownResidents.Sample(ref presence);Check(presence.HasTownResidents&&presence.TownResidents.Active,"map presence publishes all prepared residents");
        TownServicePopulation.Reset();Check(!TownServicePopulation.Published.Active,"reset withdraws published population");
        Reset();NetPlayerActors.Local=10;
        Tick(500f);
        var face=new TownFaceState{Active=true,Epoch=42,Sequence=1,Clock=20};face.Set(0,new TownFacePose{HeadYaw=22});
        var authority=new PresenceState{HasTownResidents=true,TownResidents=State(),HasTownFace=true,TownFace=face};
        RemoteTownResidents.Observe(2,in authority);Tick();
        Check(!TownServiceStation.Live[1].FaceAuthor&&TownServiceStation.Live[1].FaceReceived,"follower applies received face without local attention election");
        Near(TownServiceStation.Live[1].FacePose.HeadYaw,22,"follower face retains authority angles");
        Near(TownServicePopulation.PublishedFaces.Clock,20,"follower clock uses author even when local history is far ahead");
        NetPlayerActors.Local=1;Tick();
        Check(TownServiceStation.Live[1].FaceAuthor&&TownServiceStation.Live[1].FaceSeeds==1,"new lower ID seeds existing authority before authoring");
        Near(TownServiceStation.Live[1].FacePose.HeadYaw,22,"handover retains existing head angle");
        Check(TownServicePopulation.PublishedFaces.Clock>=20 && TownServicePopulation.PublishedFaces.Clock<21,"handover face clock never regresses");
        WorldUIConfig.ImmersiveTownServices.Value=false;
        TownServiceMirror.RemoteSessions[3]=new TownServiceSessionInfo{Peer=3,Active=true,Service=1,ReceivedTime=Time.unscaledTime};
        RemoteTownResidents.Forget(2);RemoteTownFaces.Forget(2);Tick();
        Check(!TownServiceStation.Live[1].FaceAuthor&&!TownServicePopulation.IsFaceAuthor,"opted out observer never authors even without eligible peer");
        Check(!TownServicePopulation.PublishedFaces.Active,"opted out observer never publishes face ownership");
        Console.WriteLine($"Town residents: {_assertions} production lifecycle/authority assertions passed");
    }
}
