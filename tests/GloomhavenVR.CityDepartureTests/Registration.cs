#if REAL_HARMONY
using System;
using System.Linq;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;

internal static class Registration
{
    private static int _assertions;
    private static void Check(bool valid,string message)
    { _assertions++;if(!valid)throw new InvalidOperationException(message); }
    internal static int Run()
    {
        try
        {
            CityFlowSetup.Create();
            MapCityEventReadyUp.Install();
            foreach(var target in new[]{
                (typeof(UIReadyToggle),"Initialize"),(typeof(UIReadyToggle),"Reset"),(typeof(UIReadyToggle),"InputToggle"),
                (typeof(UIMapMultiplayerController),"ConfirmCityEvent"),(typeof(UIMapMultiplayerController),"ResetUI"),
                (typeof(UIMapMultiplayerController),"ClearHostSelectedCityEvent"),(typeof(UIMapMultiplayerController),"ProxyHostCancelledCityEvent"),
                (typeof(UIMapMultiplayerController),"CancelPreviewCityEvent"),(typeof(MapChoreographer),"ProxySelectedCityEvent"),
                (typeof(UIMapMultiplayerController),"ToggleReadyUpUI"),
                (typeof(UIEventPanel),"StartEvent"),(typeof(UIReadyToggle),"get_ShouldBeVisible")})
            {
                MethodInfo original=target.Item1.GetMethod(target.Item2,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
                var info=HarmonyLib.Harmony.GetPatchInfo(original);
                Check(info!=null&&info.Owners.Contains("gloomhavenvr.quest.departure.fixture"),"actual HarmonyX registers exact city target: "+target.Item2);
                if(target.Item2=="ConfirmCityEvent")
                    Check(info!.Prefixes.Single().PatchMethod.DeclaringType==info.Postfixes.Single().PatchMethod.DeclaringType,
                        "host proposal prefix/postfix use one real Harmony __state scope");
            }
            VRSession.IsRunning=true;FFSNetwork.IsOnline=true;FFSNetwork.IsClient=false;ActionProcessor.CurrentPhase=ActionPhaseType.MapHQ;
            PlayerRegistry.MyPlayer=new(){PlayerID=1};PlayerRegistry.MyPlayer.state.LatestProcessedActionID=16;
            PlayerRegistry.AllPlayers=new(){PlayerRegistry.MyPlayer,new(){PlayerID=2}};PlayerRegistry.OnPlayerLeft=null;
            var ready=new UIReadyToggle();Singleton<UIReadyToggle>.Instance=ready;
            var controller=new UIMapMultiplayerController();Singleton<UIMapMultiplayerController>.Instance=controller;
            controller.m_State=EReadyUpToggleStates.CityEvents;
            ready.Initialize(readyUpToggleState:EReadyUpToggleStates.CityEvents);
            Check(ready.DepartureValidationOption,"actual Harmony prefix mutates only existing native city departure option");
            controller.ConfirmCityEvent();uint key=MapCityEventReadyUp.ContentKey;
            Check(key!=0,"actual host prefix/postfix capture original action counter");
            Check(PlayerRegistry.MyPlayer.state.LatestProcessedActionID==17,"capture never mutates original action counter");
            controller.ToggleReadyUpUI(true,EReadyUpToggleStates.CityEvents);
            var adventure=new AdventureMapUIManager();adventure.SelectLocation();
            NewPartyDisplayUI.PartyDisplay.Change(true,NewPartyDisplayUI.DisplayType.Character);
            Check(controller.m_State==EReadyUpToggleStates.CityEvents&&ready.IsVisible,
                "native party-panel focus cannot overwrite city role through EnableTravelOptions");
            NewPartyDisplayUI.PartyDisplay.Change(false,NewPartyDisplayUI.DisplayType.Character);
            Check(controller.m_State==EReadyUpToggleStates.CityEvents&&MapCityEventReadyUp.ContentKey==key,
                "native party-panel close preserves the same shared city proposal");
            ready._allPlayersReady=true;ready.UpdateVisiblity();MapCityEventReadyUp.Tick();
            adventure.EnableTravelOptions(true);
            Check(controller.m_State==EReadyUpToggleStates.CityEvents&&!ready.IsVisible&&!MapCityEventReadyUp.Active,
                "native ACK wait remains hidden while its city role is protected");
            ready._allPlayersReady=false;MapCityEventReadyUp.Tick();
            Check(MapCityEventReadyUp.ContentKey==key&&ready.IsVisible,"native ACK interruption retains exact city occurrence");
            controller.m_State=EReadyUpToggleStates.Quests;ready._requestVisible=true;MapCityEventReadyUp.Tick();
            Check(controller.m_State==EReadyUpToggleStates.CityEvents&&MapCityEventReadyUp.ContentKey==key,
                "actual city tick repairs presentation role without changing native votes");
            MapRoomDriver.Active=false;adventure.EnableTravelOptions(false);MapCityEventReadyUp.Tick();
            Check(controller.m_State==EReadyUpToggleStates.Quests&&!ready.IsVisible,"native map-off focus refresh retains original behavior");
            MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();
            Check(controller.m_State==EReadyUpToggleStates.CityEvents&&MapCityEventReadyUp.ContentKey==key,
                "native map reentry restores retained city proposal");
            ready.Reset();Check(!MapCityEventReadyUp.Active,"actual native Reset hook retires city occurrence");
            ready.Initialize(readyUpToggleState:EReadyUpToggleStates.Quests);controller.ToggleReadyUpUI(true,EReadyUpToggleStates.Quests);
            Check(controller.m_State==EReadyUpToggleStates.Quests&&!MapCityEventReadyUp.Active,
                "genuine native quest initialization permits original quest readiness presentation");

            FFSNetwork.IsClient=true;var map=new MapChoreographer();Singleton<MapChoreographer>.Instance=map;
            map.ProxySelectedCityEvent(new GameAction{ActionID=17,ActionTypeID=(int)GameActionType.SelectCityEvent});
            Check(MapCityEventReadyUp.ContentKey==key,"actual client postfix captures same original action key");
            ready.ToggleVisibility(true);Check(ready.IsVisible&&!ready.IsInteractable,"actual getter patch shows disabled observer without native permission");
            typeof(UIReadyToggle).GetMethod("InputToggle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ready,new object[]{true});
            Check(!ready.ToggledOn&&Synchronizer.Actions.Count==0,"actual native gamepad entry cannot ready a disabled observer");
            controller.ClearHostSelectedCityEvent();Check(!MapCityEventReadyUp.Active&&!ready.IsVisible,"actual host-withdrawal hook retires observer");
            map.ProxySelectedCityEvent(new GameAction{ActionID=18,ActionTypeID=(int)GameActionType.SelectCityEvent});
            new UIEventPanel().StartEvent();Check(!MapCityEventReadyUp.Active,"actual full event StartEvent retires preflight identity");

            NativeProbeFaults.Arm("city.run");int nativeFlow=0;
            map.ProxySelectedCityEvent(new GameAction{ActionID=19,ActionTypeID=(int)GameActionType.SelectCityEvent});nativeFlow++;
            Check(nativeFlow==1&&NativeProbeFaults.Triggered==1&&!MapCityEventReadyUp.Active,"actual network callback survives failed city run provenance");
            NativeProbeFaults.Arm("city.info-log");
            map.ProxySelectedCityEvent(new GameAction{ActionID=20,ActionTypeID=(int)GameActionType.SelectCityEvent});
            Check(NativeProbeFaults.Triggered==1&&MapCityEventReadyUp.Active,"actual network callback retains valid capture despite logging failure");
            ready.ToggleVisibility(true);NativeProbeFaults.Arm("visible");ready.Reset();
            Check(NativeProbeFaults.Triggered==1&&!MapCityEventReadyUp.Active&&!ready.IsVisible,"actual Reset body survives failed mod visibility recompute");

            NativeProbeFaults.Arm("phase");ready.Initialize(readyUpToggleState:EReadyUpToggleStates.CityEvents);
            Check(NativeProbeFaults.Triggered==1&&!ready.DepartureValidationOption&&ready.readyUpToggleState==EReadyUpToggleStates.CityEvents,
                "actual native Initialize continues with original option after failed phase probe");
            map.ProxySelectedCityEvent(new GameAction{ActionID=21,ActionTypeID=(int)GameActionType.SelectCityEvent});ready.ToggleVisibility(true);
            NativeProbeFaults.Arm("city.phase");bool observed=ready.ShouldBeVisible;
            Check(NativeProbeFaults.Triggered==1&&!observed,"actual observer postfix preserves native result after failed phase probe");
            Check(ready.ShouldBeVisible,"actual observer getter retries its retained native occurrence");
            NativeProbeFaults.Arm("interactable");
            typeof(UIReadyToggle).GetMethod("InputToggle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ready,new object[]{true});
            Check(NativeProbeFaults.Triggered==1&&!ready.ToggledOn&&Synchronizer.Actions.Count==0,"actual input prefix denies uncertain exact-city native permission");
            typeof(MapCityEventReadyUp).GetField("_warned",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);
            NativeProbeFaults.Arm("city.phase");NativeProbeFaults.AlsoArm("log.failure");observed=ready.ShouldBeVisible;
            Check(NativeProbeFaults.Triggered==2&&!observed,"actual getter survives both native probe and diagnostic failure");

            FFSNetwork.IsClient=false;ready.Initialize(readyUpToggleState:EReadyUpToggleStates.CityEvents);
            int previous=PlayerRegistry.MyPlayer.state.LatestProcessedActionID;NativeProbeFaults.Arm("local-player");controller.ConfirmCityEvent();
            Check(NativeProbeFaults.Triggered==1&&PlayerRegistry.MyPlayer.state.LatestProcessedActionID==previous+1,
                "actual native host action continues after missing local-player counter probe");
            NativeProbeFaults.Reset();controller.ConfirmCityEvent();ready.ToggleVisibility(true);
            NativeProbeFaults.Arm("network.client");
            typeof(UIMapMultiplayerController).GetMethod("CancelPreviewCityEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(controller,new object[0]);
            Check(NativeProbeFaults.Triggered==1&&!MapCityEventReadyUp.Active&&!ready.IsVisible,"actual native cancel survives failed host-role probe without resurrection");
            System.Console.WriteLine($"City actual HarmonyX: {_assertions} assertions passed; 12 original targets under Unity Mono.");return 0;
        }
        catch(Exception ex){System.Console.Error.WriteLine(ex);return 1;}
    }
}
#endif
