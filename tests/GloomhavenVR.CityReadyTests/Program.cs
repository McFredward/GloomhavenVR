using System;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.PhaseManager;

internal static partial class Program
{
    private static int _assertions;
    private static void Check(bool condition,string name)
    { _assertions++;if(!condition)throw new InvalidOperationException(name); }
    private static UIReadyToggle Ready=>Singleton<UIReadyToggle>.Instance;
    private static UIMapMultiplayerController Controller=>Singleton<UIMapMultiplayerController>.Instance;
    private static MapChoreographer Map=>Singleton<MapChoreographer>.Instance;
    private static void Fresh(bool host=true,bool participant=true)
    {
        Faults.Reset();
        MapCityEventReadyUp.Reset();FFSNetwork.IsOnline=true;FFSNetwork.IsHost=host;MapRoomDriver.Active=true;
        Singleton<UIMapMultiplayerController>.Instance=new();Singleton<UIReadyToggle>.Instance=new();
        Singleton<MapChoreographer>.Instance=new(){Participant=participant};PlayerRegistry.MyPlayer=new();
        AdventureState.MapState=new(){CurrentMapPhaseType=EMapPhaseType.InHQ};SharedMapRunIdentity.Key=8193;
        Ready.readyUpToggleState=Controller.m_State=EReadyUpToggleStates.CityEvents;
        ActionProcessor.CurrentPhase=ActionPhaseType.MapHQ;
    }
    private static void ClientOffer(int id=17)
    { Hooks.Invoke("AfterClientProposal",new GameAction{ActionID=id,ActionTypeID=(int)GameActionType.SelectCityEvent}); }
    private static void HostOffer(int id=17)
    {
        PlayerRegistry.MyPlayer!.state.LatestProcessedActionID=id-1;object?[] args={null};Hooks.Invoke("BeforeHostProposal",args);
        PlayerRegistry.MyPlayer.state.LatestProcessedActionID=id;Hooks.Invoke("AfterHostProposal",args[0]);
    }
    private static int Main()
    {
        try
        {
            foreach(var target in new[]{
                ("BeforeReadyInitialize",typeof(UIReadyToggle),"Initialize"),
                ("BeforeReadyReset",typeof(UIReadyToggle),"Reset"),
                ("BeforeControllerReset",typeof(UIMapMultiplayerController),"ResetUI"),
                ("BeforeHostWithdrawal",typeof(UIMapMultiplayerController),"ClearHostSelectedCityEvent"),
                ("BeforeHostCancellation",typeof(UIMapMultiplayerController),"ProxyHostCancelledCityEvent"),
                ("BeforeEventCard",typeof(UIEventPanel),"StartEvent")})
                Check(Hooks.HasExactlyOneTarget(target.Item1,target.Item2,target.Item3),"native retirement has one exact Harmony target: "+target.Item1);
            Fresh();HostOffer();uint hostKey=MapCityEventReadyUp.ContentKey;
            Check(hostKey!=0,"host native proposal produces a shared occurrence");
            MapCityEventReadyUp.Tick();Check(Ready.Window.IsOpen,"host proposal visible");
            Check(Ready.AutomaticVotes==0&&Ready.InputVotes==0,"presentation must never consent automatically");
            Check(Map.PermissionChecks==1,"native permission re-evaluated exactly once");
            for(int i=0;i<8;i++)MapCityEventReadyUp.Tick();
            Check(Map.PermissionChecks==1&&Controller.ShowRequests==1,"steady state does not repeat native show or permission work");
            Check(ReferenceEquals(MapCityEventReadyUp.Window,Ready.Window),"exact original native window is the shared identity");

            for(int peer=1;peer<=3;peer++)
            {
                Fresh(false);ClientOffer();Check(MapCityEventReadyUp.ContentKey==hostKey,"all four peers share native action opening key");
                MapCityEventReadyUp.Tick();Check(Ready.Window.IsOpen,"every remote participant sees city offer");
                Check(Ready.InputToggle(),"native participant can explicitly consent");
                Check(Ready.InputVotes==1&&Ready.AutomaticVotes==0,"one explicit personal vote, no replay");
            }
            Fresh(false);ClientOffer(18);Check(MapCityEventReadyUp.ContentKey!=hostKey,"new city occurrence cannot reuse old pose key");
            Fresh(false);SharedMapRunIdentity.Key++;ClientOffer();Check(MapCityEventReadyUp.ContentKey!=hostKey,"different saved run cannot reuse city pose key");

            Fresh(false,false);ClientOffer();MapCityEventReadyUp.Tick();
            Check(Ready.Window.IsOpen,"unassigned client observes the public offer");
            Check(!Ready.IsInteractable&&!Ready.ToggleInteractable,"observer reveal never grants native permission");
            Check(!Ready.InputToggle()&&Ready.InputVotes==0,"disabled observer cannot vote through gamepad or VR input");
            object blocker=new();Ready._visibilityRequests.Add(blocker);Ready.UpdateVisiblity();
            Check(!Ready.Window.IsOpen,"native visibility blocker outranks shared observation");
            Ready._visibilityRequests.Clear();Ready.UpdateVisiblity();Check(Ready.Window.IsOpen,"native unblock restores observation");
            Map.Participant=true;Map.DeterminePlayerToggleInteractability();
            Check(Ready.InputToggle()&&Ready.InputVotes==1,"native assignment change enables one explicit vote");
            Map.Participant=false;Map.DeterminePlayerToggleInteractability();
            Check(!Ready.InputToggle()&&Ready.Window.IsOpen,"unassignment disables input while public offer remains visible");

            Fresh(false);ClientOffer();MapCityEventReadyUp.Tick();Hooks.Invoke("BeforeCancelPreview");Controller.ToggleReadyUpUI(false,EReadyUpToggleStates.CityEvents);
            Check(!Ready.Window.IsOpen,"native local unready initially hides preview");MapCityEventReadyUp.Tick();
            Check(Ready.Window.IsOpen&&MapCityEventReadyUp.Active,"client unready must not withdraw host proposal");
            Check(Ready.InputVotes==0&&Ready.AutomaticVotes==0,"reveal after unready never casts another vote");
            Hooks.Invoke("BeforeHostWithdrawal");Controller.ToggleReadyUpUI(false,EReadyUpToggleStates.CityEvents);MapCityEventReadyUp.Tick();
            Check(!Ready.Window.IsOpen&&!MapCityEventReadyUp.Active,"host withdrawal cannot resurrect on next tick");

            Fresh();HostOffer();MapCityEventReadyUp.Tick();Hooks.Invoke("BeforeCancelPreview");Controller.ToggleReadyUpUI(false,EReadyUpToggleStates.CityEvents);MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active&&!Ready.Window.IsOpen,"host cancel retires shared offer");
            Fresh(false);ClientOffer();MapCityEventReadyUp.Tick();Ready.Reset();MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active&&!Ready.Window.IsOpen,"native all-ready reset retires preflight before full event");
            Fresh(false);ClientOffer();Hooks.Invoke("BeforeEventCard");
            Check(!MapCityEventReadyUp.Active,"full UIEventPanel entry cannot retain preflight identity");
            foreach(string retirement in new[]{"BeforeControllerReset","BeforeHostCancellation"})
            {
                Fresh(false);ClientOffer();MapCityEventReadyUp.Tick();Hooks.Invoke(retirement);MapCityEventReadyUp.Tick();
                Check(!MapCityEventReadyUp.Active,"exact native retirement seam clears city lease: "+retirement);
            }

            foreach(EReadyUpToggleStates state in new[]{EReadyUpToggleStates.Quests,EReadyUpToggleStates.NotSet,EReadyUpToggleStates.Reward,EReadyUpToggleStates.Retirement,EReadyUpToggleStates.TownRecords})
            {
                Fresh(false,false);ClientOffer();MapCityEventReadyUp.Tick();Ready.Initialize(state);Controller.m_State=state;MapCityEventReadyUp.Tick();
                Check(!MapCityEventReadyUp.Active&&MapCityEventReadyUp.Window==null,"native phase reuse retires exact city ownership");
                Check(!Ready.ShouldBeVisible,"unrelated readiness retains original disabled visibility");
                Check(Ready.InputToggle(),"unrelated native input behavior remains unchanged");
            }
            Fresh(false);ClientOffer();MapCityEventReadyUp.Tick();Singleton<UIReadyToggle>.Instance=new(){readyUpToggleState=EReadyUpToggleStates.CityEvents};MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active,"replacement native ready singleton cannot inherit old city lease");
            Fresh(false);ClientOffer();MapCityEventReadyUp.Tick();Singleton<UIMapMultiplayerController>.Instance=new(){m_State=EReadyUpToggleStates.CityEvents};MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active,"replacement controller cannot inherit city lease");
            Fresh(false);ClientOffer();AdventureState.MapState!.CurrentMapPhaseType=EMapPhaseType.Travel;MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active,"native phase exit retires city proposal");
            Fresh(false);AdventureState.MapState!.CurrentMapPhaseType=EMapPhaseType.AtLinkedScenario;ClientOffer();MapCityEventReadyUp.Tick();
            Check(MapCityEventReadyUp.Active&&Ready.Window.IsOpen,"native linked-scenario proposal remains supported");
            FFSNetwork.IsOnline=false;MapCityEventReadyUp.Tick();Check(!MapCityEventReadyUp.Active,"session exit retires city proposal");

            Fresh(false,false);ClientOffer();MapCityEventReadyUp.Tick();MapRoomDriver.Active=false;MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active&&!Ready.Window.IsOpen,"leaving 3D map restores native disabled visibility");
            MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();Check(MapCityEventReadyUp.Active&&Ready.Window.IsOpen,"map rebuild preserves genuine native pending proposal");
            Fresh();object?[] before={null};Hooks.Invoke("BeforeHostProposal",before);PlayerRegistry.MyPlayer!.state.LatestProcessedActionID=3;Hooks.Invoke("AfterHostProposal",before[0]);
            Check(!MapCityEventReadyUp.Active,"ambiguous native action counter cannot invent an opening");
            Check(VRLog.Warnings.Count==1&&VRLog.Warnings[0].Contains("before=0, after=3"),"ambiguous eligible host occurrence records useful normal-level failure context");
            Hooks.Invoke("AfterHostProposal",before[0]);Check(VRLog.Warnings.Count==1,"ambiguous host occurrence warning is bounded");
            Fresh(false);Hooks.Invoke("AfterClientProposal",new GameAction{ActionID=17,ActionTypeID=(int)GameActionType.SelectQuest});
            Check(!MapCityEventReadyUp.Active,"unrelated native proposal cannot acquire city identity");
            Fresh(false);ClientOffer();Ready._allPlayersReady=true;Ready.UpdateVisiblity();MapCityEventReadyUp.Tick();
            Check(!MapCityEventReadyUp.Active&&!Ready.Window.IsOpen,"native all-ready gate suppresses observation immediately");
            MapCityEventReadyUp.Install();MapCityEventReadyUp.Install();Check(VRSession.Harmony!.Installed==1,"installation is idempotent");
            Check(VRLog.Warnings.Count==1,"all other native transitions reconcile without exception");
            RoleProof();
            FaultProof();
            Console.WriteLine($"City readiness: {_assertions} assertions passed.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
