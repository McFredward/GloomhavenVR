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
            MapCityEventReadyUp.Install();
            foreach(var target in new[]{
                (typeof(UIReadyToggle),"Initialize"),(typeof(UIReadyToggle),"Reset"),(typeof(UIReadyToggle),"InputToggle"),
                (typeof(UIMapMultiplayerController),"ConfirmCityEvent"),(typeof(UIMapMultiplayerController),"ResetUI"),
                (typeof(UIMapMultiplayerController),"ClearHostSelectedCityEvent"),(typeof(UIMapMultiplayerController),"ProxyHostCancelledCityEvent"),
                (typeof(UIMapMultiplayerController),"CancelPreviewCityEvent"),(typeof(MapChoreographer),"ProxySelectedCityEvent"),
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
            ready.Reset();Check(!MapCityEventReadyUp.Active,"actual native Reset hook retires city occurrence");

            FFSNetwork.IsClient=true;var map=new MapChoreographer();Singleton<MapChoreographer>.Instance=map;
            map.ProxySelectedCityEvent(new GameAction{ActionID=17,ActionTypeID=(int)GameActionType.SelectCityEvent});
            Check(MapCityEventReadyUp.ContentKey==key,"actual client postfix captures same original action key");
            ready.ToggleVisibility(true);Check(ready.IsVisible&&!ready.IsInteractable,"actual getter patch shows disabled observer without native permission");
            typeof(UIReadyToggle).GetMethod("InputToggle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ready,new object[]{true});
            Check(!ready.ToggledOn&&Synchronizer.Actions.Count==0,"actual native gamepad entry cannot ready a disabled observer");
            controller.ClearHostSelectedCityEvent();Check(!MapCityEventReadyUp.Active&&!ready.IsVisible,"actual host-withdrawal hook retires observer");
            map.ProxySelectedCityEvent(new GameAction{ActionID=18,ActionTypeID=(int)GameActionType.SelectCityEvent});
            new UIEventPanel().StartEvent();Check(!MapCityEventReadyUp.Active,"actual full event StartEvent retires preflight identity");
            System.Console.WriteLine($"City actual HarmonyX: {_assertions} assertions passed; 11 original targets under Unity Mono.");return 0;
        }
        catch(Exception ex){System.Console.Error.WriteLine(ex);return 1;}
    }
}
#endif
