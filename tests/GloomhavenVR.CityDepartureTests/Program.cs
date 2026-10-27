using System;
using System.Linq;
using System.Reflection;
using FFSNet;
using MEC;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;

internal static class Program
{
    private static int _assertions,_proceeded;
    private static void Check(bool value,string message)
    { _assertions++;if(!value)throw new InvalidOperationException(message); }
    private static bool Apply(bool requested=false,UIReadyToggle.EReadyUpType type=UIReadyToggle.EReadyUpType.Participant,EReadyUpToggleStates state=EReadyUpToggleStates.CityEvents)
    {
        object[] args={requested,type,state};
        typeof(MapCityEventReadyUp).GetNestedType("CityProposalSeams",BindingFlags.NonPublic)!
            .GetMethod("BeforeReadyInitialize",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
        return (bool)args[0];
    }
    private static UIReadyToggle Setup(bool apply=true,bool client=false)
    {
        NativeProbeFaults.Reset();VRSession.IsRunning=true;FFSNetwork.IsOnline=true;FFSNetwork.IsClient=client;
        ActionProcessor.CurrentPhase=ActionPhaseType.MapHQ;Timing.Reset();Synchronizer.Actions.Clear();Timekeeper.instance.m_GlobalClock.time=0;
        PlayerRegistry.MyPlayer=new(){PlayerID=1};PlayerRegistry.AllPlayers=new(){PlayerRegistry.MyPlayer,new(){PlayerID=2},new(){PlayerID=3}};
        PlayerRegistry.OnPlayerLeft=null;_proceeded=0;var toggle=new UIReadyToggle();Singleton<UIReadyToggle>.Instance=toggle;
        Singleton<UIMapMultiplayerController>.Instance=new();bool validation=apply&&Apply();
        toggle.Initialize(show:false,onAllPlayersReady:()=>_proceeded++,validateReadyUpOnPlayerLeft:validation,
            readyUpType:UIReadyToggle.EReadyUpType.Participant,readyUpToggleState:EReadyUpToggleStates.CityEvents);
        toggle.SetInteractable(true);toggle.ToggleVisibility(true);toggle.PlayersReady.Add(PlayerRegistry.MyPlayer);toggle.PlayersReady.Add(PlayerRegistry.AllPlayers[1]);toggle.SetLocalReadySnapshot(true);
        return toggle;
    }
    private static void Leave(NetworkPlayer player)
    { PlayerRegistry.AllPlayers.Remove(player);PlayerRegistry.OnPlayerLeft?.Invoke(player); }
    private static void AckAll()
    {
        foreach(var player in PlayerRegistry.AllPlayers)
            foreach(var peer in PlayerRegistry.Participants)
                if(!player.PlayersACKedMyLatestControllableState.Contains(peer))player.PlayersACKedMyLatestControllableState.Add(peer);
    }
    private static int Main()
    {
#if REAL_HARMONY
        return Registration.Run();
#else
        try
        {
            var old=Setup(false);Leave(PlayerRegistry.AllPlayers[2]);
            Check(Timing.Starts==0&&_proceeded==0,"old native city departure flag strands a sufficient quorum before ACK");
            old.ReadyUp(false,false);Check(Synchronizer.Actions.Count==0,"old native full roster also refuses ordinary Cancel");
            var ready=Setup();Check(ready.DepartureValidationOption,"native city departure validation option is enabled");
            Leave(PlayerRegistry.AllPlayers[2]);
            Check(Timing.Starts==1,"native city departure option starts original ACK coroutine after participant departure");
            Check(_proceeded==0&&!Synchronizer.Actions.Any(x=>x.Type==GameActionType.ReadyProceed),"city departure cannot skip original peer ACK");
            Check(Synchronizer.Actions.Count(x=>x.Type==GameActionType.AllPlayersReady)==2,"native remaining peers receive their original state ACK requests");
            Check(!Synchronizer.Actions.Any(x=>x.Type==GameActionType.ReadyUpPlayer||x.Type==GameActionType.UnreadyPlayer),"mod departure option creates no votes");
            AckAll();Timing.Advance();
            Check(_proceeded==1&&Synchronizer.Actions.Count(x=>x.Type==GameActionType.ReadyProceed)==1,"original native ACK completion opens event exactly once");
            Timing.Advance();Check(_proceeded==1,"repeated scheduler ticks cannot replay city completion");

            ready=Setup();Leave(PlayerRegistry.AllPlayers[1]);
            Check(Timing.Starts==0&&_proceeded==0,"ready peer departure does not proceed while another participant is unready");
            Check(ready.PlayersReady.Count==1&&ready.PlayersReady[0]==PlayerRegistry.MyPlayer,"native handler alone removes departed readiness");
            Check(ready.IsVisible,"native remaining Cancel remains visible after ready-peer departure");
            ready=Setup(client:true);Leave(PlayerRegistry.AllPlayers[2]);
            Check(Timing.Starts==0&&_proceeded==0,"native client never authors quorum progression");
            Check(Synchronizer.Actions.Count==0,"native client departure observation emits no invented network action");

            foreach(ActionPhaseType phase in Enum.GetValues<ActionPhaseType>())
            foreach(EReadyUpToggleStates state in Enum.GetValues<EReadyUpToggleStates>())
            foreach(UIReadyToggle.EReadyUpType type in Enum.GetValues<UIReadyToggle.EReadyUpType>())
            foreach(bool running in new[]{false,true})
            foreach(bool online in new[]{false,true})
            {
                NativeProbeFaults.Reset();VRSession.IsRunning=running;FFSNetwork.IsOnline=online;ActionProcessor.CurrentPhase=phase;
                bool eligible=running&&online&&type==UIReadyToggle.EReadyUpType.Participant&&state==EReadyUpToggleStates.CityEvents
                    &&(phase==ActionPhaseType.MapHQ||phase==ActionPhaseType.MapAtLinkedScenario);
                Check(Apply(false,type,state)==eligible,"city departure scope excludes unrelated native modes and phases");
                Check(Apply(true,type,state),"existing native true departure option is never removed");
            }
            Setup();NativeProbeFaults.Arm("phase");
            Check(!Apply(),"unknown native phase preserves false departure option");
            Check(NativeProbeFaults.Triggered==1,"native phase failure is actually exercised");
            NativeProbeFaults.Reset();System.Console.WriteLine($"City native departure: {_assertions} assertions passed.");return 0;
        }
        catch(Exception ex){System.Console.Error.WriteLine(ex);return 1;}
#endif
    }
}
