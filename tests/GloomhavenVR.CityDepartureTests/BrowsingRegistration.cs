#if REAL_HARMONY
using System;
using FFSNet;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Adventure;
using UnityEngine.EventSystems;

internal static partial class Registration
{
    private static MapLocation StartBrowseCase(int action,bool mode=true)
    {
        NativeProbeFaults.Reset();MapCityEventReadyUp.Reset();Singleton<UIReadyToggle>.Instance.Reset();
        FFSNetwork.IsOnline=true;FFSNetwork.IsClient=true;MapRoomDriver.Active=mode;ActionProcessor.CurrentPhase=ActionPhaseType.MapHQ;
        CityFlowSetup.Create();Singleton<UIEventPanel>.Instance=null!;
        Singleton<UIMapMultiplayerController>.Instance.HostSelectedQuest=null;
        var marker=new MapLocation();Singleton<AdventureMapUIManager>.Instance.Browse(marker);Synchronizer.Actions.Clear();
        Singleton<MapChoreographer>.Instance.ProxySelectedCityEvent(new GameAction{ActionID=action,ActionTypeID=(int)GameActionType.SelectCityEvent});
        return marker;
    }
    private static void BrowsingAndCompletionProof()
    {
        var marker=StartBrowseCase(70);var adventure=Singleton<AdventureMapUIManager>.Instance;
        var controller=Singleton<UIMapMultiplayerController>.Instance;var ready=Singleton<UIReadyToggle>.Instance;
        MapCityEventReadyUp.Tick();
        Check(adventure.LocationToTravel==null&&marker.Deselects==1&&!adventure.SelectedPopupVisible,
            "actual CancelTravel clears only original speculative local quest presentation");
        Check(controller.m_State==EReadyUpToggleStates.CityEvents&&Singleton<HelpBox>.Instance.Last=="GUI_ACCEPT_CITY_EVENT_TIP",
            "original city help and readiness restore after native travel-warning cleanup");
        Check(Synchronizer.Actions.Count==0,"native speculative cleanup does not fabricate readiness or game actions");
        var quest=new CQuestState();var log=new QuestManager();var next=new MapLocation();log.Bind(quest,next);
        log.UserPick(quest);next.OnPointerClick(new PointerEventData());
        Check(next.Selects==0&&adventure.LocationToTravel==null,"actual native quest-log and pointer inputs cannot privately browse during city decision");
        ready._allPlayersReady=true;MapCityEventReadyUp.Tick();log.UserPick(quest);next.OnPointerClick(new PointerEventData());
        Check(next.Selects==0&&!MapCityEventReadyUp.Active&&MapCityEventReadyUp.OwnsMapDecision,
            "actual input guards retain ownership throughout hidden native ACK wait");
        next.Select();next.Deselect();Check(next.Selects==1&&next.Deselects==1,"native scripted Select and Deselect remain unpatched");

        marker=StartBrowseCase(71);adventure=Singleton<AdventureMapUIManager>.Instance;
        controller.HostSelectedQuest=new NativeQuest();MapCityEventReadyUp.Tick();
        Check(ReferenceEquals(adventure.LocationToTravel,marker)&&marker.Deselects==0,"actual accepted host quest is immune to speculative cleanup");
        marker=StartBrowseCase(72);adventure=Singleton<AdventureMapUIManager>.Instance;adventure.SetLock(true);MapCityEventReadyUp.Tick();
        Check(ReferenceEquals(adventure.LocationToTravel,marker)&&marker.Deselects==0,"actual native map lock retains its cleanup permission");
        adventure.SetLock(false);MapCityEventReadyUp.Tick();Check(adventure.LocationToTravel==null&&marker.Deselects==1,"native unlock admits one original cleanup");
        marker=StartBrowseCase(73,false);adventure=Singleton<AdventureMapUIManager>.Instance;MapCityEventReadyUp.Tick();
        Check(ReferenceEquals(adventure.LocationToTravel,marker)&&MapCityEventReadyUp.OwnsMapDecision&&!MapCityEventReadyUp.BlocksQuestBrowsing,
            "mode-independent ownership never clears private 2D browsing");
        MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();Check(adventure.LocationToTravel==null&&marker.Deselects==1,"actual 3D reentry cleans speculative browsing before city presentation");
        marker=StartBrowseCase(74);NativeProbeFaults.Arm("proposal");MapCityEventReadyUp.Tick();
        Check(NativeProbeFaults.Triggered==1&&marker.Deselects==0,"failed actual host-proposal probe cannot authorize cleanup");
        MapCityEventReadyUp.Tick();Check(marker.Deselects==1,"valid original permission retries pending cleanup after failed probe");
        marker=StartBrowseCase(75);NativeProbeFaults.Arm("cancel-travel");MapCityEventReadyUp.Tick();
        Check(NativeProbeFaults.Triggered==1&&marker.Deselects==0&&Synchronizer.Actions.Count==0,"actual native cancellation failure remains inside helper boundary");
        MapCityEventReadyUp.Tick();Check(marker.Deselects==0,"failed native cancel is not repeated every frame");

        foreach(bool campaign in new[]{true,false})
        {
            NativeProbeFaults.Reset();MapCityEventReadyUp.Reset();ready.Reset();CityFlowSetup.Create();MapRoomDriver.Active=true;
            AdventureState.MapState.IsCampaign=campaign;
            Singleton<CampaignRewardsManager>.Instance=new();Singleton<UIDistributeRewardManager>.Instance=new();Singleton<UIRewardsManager>.Instance=new();
            var panel=new UIEventPanel();Singleton<UIEventPanel>.Instance=panel;int completed=0;
            panel.StartEvent();panel.ConfigureCompletion(()=>completed++);panel.NativeHideAndFinish();
            Check(!panel.myWindow.IsOpen&&panel.eventData!=null&&MapCityEventReadyUp.OwnsMapDecision&&MapCityEventReadyUp.BlocksQuestBrowsing,
                "original hidden event retains ownership while applied effects or Guildmaster rewards await completion");
            quest=new();log=new();next=new();log.Bind(quest,next);log.UserPick(quest);next.OnPointerClick(new PointerEventData());
            Check(next.Selects==0,"both actual native input paths remain blocked during hidden event reward effects");
            if(campaign)Singleton<CampaignRewardsManager>.Instance.Release();else Singleton<UIRewardsManager>.Instance.Release();
            Check(panel.eventData!=null&&MapCityEventReadyUp.OwnsMapDecision&&completed==0,
                "original deferred distribution keeps decision owned after applied effects close");
            Singleton<UIDistributeRewardManager>.Instance.Release();
            Check(panel.eventData==null&&!MapCityEventReadyUp.OwnsMapDecision&&completed==1,
                "original OnDistributedRewards releases logical event exactly at native completion");
            log.UserPick(quest);Check(next.Selects==1,"native quest browsing resumes after real event completion");
            Singleton<UIDistributeRewardManager>.Instance.Release();Check(completed==1,"reward delivery boundary cannot replay native completion");
        }
    }
}
#endif
