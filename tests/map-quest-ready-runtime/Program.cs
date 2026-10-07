using System;
using System.Reflection;
using FFSNet;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.MapState;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string reason) { _assertions++; if (!value) throw new InvalidOperationException(reason); }
    private static (UIReadyToggle Toggle,UIMapMultiplayerController Controller,CQuestState Quest) Setup(bool client=true)
    {
        MapQuestReadyUp.Reset(); FFSNetwork.IsOnline = true; FFSNetwork.IsClient = client;
        MapRoomDriver.Active = true; StoryComposite.PointOfNoReturn = false; Time.unscaledTime += 1f;
        PlayerRegistry.MyPlayer = new NetworkPlayer { PlayerID=1 };
        PlayerRegistry.AllPlayers = new() { PlayerRegistry.MyPlayer,new NetworkPlayer { PlayerID=2 } };
        var toggle = new UIReadyToggle(); var quest = new CQuestState { ID="Quest_A" };
        var controller = new UIMapMultiplayerController { HostSelectedQuest=quest };
        Singleton<UIReadyToggle>.Instance = toggle; Singleton<UIMapMultiplayerController>.Instance = controller;
        Singleton<MapChoreographer>.Instance = new();
        Singleton<UIQuestPopupManager>.Instance = new();
        toggle.Initialize(show:false,readyUpToggleState:EReadyUpToggleStates.Quests);
        toggle.SetInteractable(true);
        MapLocationInteractor.Decision = "Location_A"; MapLocationInteractor.SelectedAt = Time.unscaledTime;
        return (toggle,controller,quest);
    }
    private static void Capture(CQuestState quest,Action callback,bool popup=true)
    {
        if (popup)
            typeof(MapQuestReadyUp.ClientQuestPromptSeam).GetMethod("AfterPopupPrompt",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[] { quest,callback });
        else
        {
            var presenter=new UIGuildmasterConfirmActionButtonPresenter();
            presenter.ShowQuestSelectedAction(quest,callback);
            typeof(MapQuestReadyUp.ClientQuestPromptSeam).GetMethod("AfterButtonPrompt",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[] { presenter,quest,callback });
        }
    }
    private static void Main()
    {
        // Every host/client VR membership layout from two through four players. Flat clients
        // consume their native prompt themselves; each VR client executes the full production
        // dispatcher. Native host ready-up/ACK progress is deliberately not simulated here.
        int layouts=0;
        for (int players=2;players<=4;players++)
        for (int mask=1;mask<(1<<players);mask++)
        {
            layouts++;
            for (int slot=1;slot<players;slot++)
            {
                var s=Setup(); Action callback=s.Controller.Callback();
                bool vr=(mask&(1<<slot))!=0;
                if (vr) { Capture(s.Quest,callback,slot%2==0); MapQuestReadyUp.TickPendingClientPrompt(); }
                else callback();
                Check(s.Controller.Previews==1 && s.Toggle.IsVisible,"each Flat or VR client can see its native proposal once");
                Check(s.Toggle.ReadyActions==0,"presentation never accepts a quest for a player");
                s.Toggle.ReadyUp(true,false);
                Check(s.Toggle.ReadyActions==1,"manual Accept still uses the native ReadyUp request");
            }
        }

        var state=Setup(); Singleton<UIQuestPopupManager>.Instance.PreviewVisible=true;
        Capture(state.Quest,state.Controller.Callback(),false); MapQuestReadyUp.TickPendingClientPrompt();
        Check(!Singleton<UIQuestPopupManager>.Instance.PreviewVisible && Singleton<UIQuestPopupManager>.Instance.PreviewHides==1 && state.Controller.Previews==1,
            "desktop native click wrapper closes hover preview before selecting the accepted card");
        state=Setup(); state.Toggle.ToggleVisibility(true);
        state.Toggle.Initialize(show:false,readyUpToggleState:EReadyUpToggleStates.Quests);
        state.Toggle.SetInteractable(true);
        Check(state.Toggle.IsVisible,"native Initialize keeps the old requested visibility");
        Action exact=state.Controller.Callback(); Capture(state.Quest,exact);
        MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==1,"visible sticky toggle must not swallow native quest preview");
        for (int i=0;i<100;i++) MapQuestReadyUp.TickPendingClientPrompt();
        Capture(state.Quest,exact,true); MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==1,"the same captured native callback is consumed only once");

        state=Setup(); exact=state.Controller.Callback(); MapRoomDriver.Active=false; Capture(state.Quest,exact);
        MapQuestReadyUp.TickPendingClientPrompt(); Check(state.Controller.Previews==0,"inactive room leaves Flat prompt untouched");
        MapQuestReadyUp.Reset(); MapRoomDriver.Active=true; MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==1,"room reset preserves the pending native proposal");
        foreach (bool popup in new[] { false,true })
        {
            state=Setup(); MapRoomDriver.Active=false; exact=state.Controller.Callback();
            if (popup)
            {
                var native=new UIGuildmasterConfirmActionPopup(); native.SetCallback(exact);
                Capture(state.Quest,exact,true);
                typeof(UIGuildmasterConfirmActionPopup).GetMethod("Confirm",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(native,null);
                typeof(MapQuestReadyUp.ClientQuestPromptSeam).GetMethod("AfterNativePopupClick",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[] { native });
            }
            else
            {
                var presenter=new UIGuildmasterConfirmActionButtonPresenter(); presenter.ShowQuestSelectedAction(state.Quest,exact);
                typeof(MapQuestReadyUp.ClientQuestPromptSeam).GetMethod("AfterButtonPrompt",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[] { presenter,state.Quest,exact });
                typeof(UIGuildmasterConfirmActionButton).GetMethod("OnClicked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(presenter.Button,null);
                typeof(MapQuestReadyUp.ClientQuestPromptSeam).GetMethod("AfterNativeButtonClick",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[] { presenter.Button });
            }
            state.Toggle.PlayersReady.Add(PlayerRegistry.MyPlayer); MapQuestReadyUp.Reset();
            MapRoomDriver.Active=true; MapQuestReadyUp.TickPendingClientPrompt();
            Check(state.Controller.Previews==1 && state.Toggle.PlayersReady.Count==1,"native 2D prompt already answered must not replay on room entry");
        }
        state=Setup(); Capture(state.Quest,state.Controller.Callback());
        Singleton<UIMapMultiplayerController>.Instance=null!; MapQuestReadyUp.TickPendingClientPrompt();
        Singleton<UIMapMultiplayerController>.Instance=state.Controller; MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==1,"native initialization absence waits rather than discarding the prompt");

        state=Setup(); Capture(state.Quest,state.Controller.Callback());
        state.Controller.HostSelectedQuest=new CQuestState { ID="Quest_A" }; MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==1,"same controller with rebuilt quest identity retains its registered callback");
        state=Setup(); Capture(state.Quest,state.Controller.Callback());
        Singleton<UIMapMultiplayerController>.Instance=new() { HostSelectedQuest=state.Quest }; MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Controller.Previews==0,"replacement native controller invalidates its predecessor callback");
        foreach (int refusal in new[] { 0,1,2,3,4,5 })
        {
            state=Setup(); Capture(state.Quest,state.Controller.Callback());
            if (refusal==0) state.Controller.HostSelectedQuest=null;
            if (refusal==1) state.Controller.HostSelectedQuest=new CQuestState { ID="Quest_B" };
            if (refusal==2) StoryComposite.PointOfNoReturn=true;
            if (refusal==3) FFSNetwork.IsClient=false;
            if (refusal==4) FFSNetwork.IsOnline=false;
            if (refusal==5) state.Toggle.readyUpToggleState=EReadyUpToggleStates.CityEvents;
            MapQuestReadyUp.TickPendingClientPrompt();
            Check(state.Controller.Previews==0,"cancelled/replaced/committed/host/offline/nonquest prompts cannot advance");
        }
        state=Setup(); Capture(state.Quest,()=>throw new Exception("native preview failure"));
        MapQuestReadyUp.TickPendingClientPrompt(); MapQuestReadyUp.TickPendingClientPrompt();
        Check(state.Toggle.ReadyActions==0,"native callback failure is contained without accepting");

        state=Setup(); var window=new UIWindow(); state.Toggle.ToggleVisibility(false);
        MapQuestReadyUp.TickClientConfirmReveal(state.Toggle,window);
        Check(state.Toggle.IsVisible && state.Controller.Reveals==1,"early native visibility request reaches a participant");
        for (int i=0;i<100;i++) MapQuestReadyUp.TickClientConfirmReveal(state.Toggle,window);
        Check(state.Controller.Reveals==1,"settled early reveal does not repeat native HUD callbacks");
        state.Toggle.ToggleVisibility(false); MapQuestReadyUp.TickClientConfirmReveal(state.Toggle,window);
        Check(!state.Toggle.IsVisible,"native deliberate hide remains authoritative after early reveal");
        state=Setup(); PlayerRegistry.MyPlayer.IsParticipant=false;
        MapQuestReadyUp.TickClientConfirmReveal(state.Toggle,window);
        Check(!state.Toggle.IsVisible,"spectator cannot gain a native Accept button");
        PlayerRegistry.MyPlayer.IsParticipant=true; Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();
        Check(state.Toggle.IsVisible,"native character assignment reveals the sticky pending request");
        object blocker=new(); state.Toggle.Block(blocker);
        Check(!state.Toggle.IsVisible,"native visibility block is never bypassed");
        state.Toggle.Unblock(blocker); Check(state.Toggle.IsVisible,"native unblock restores the pending Accept");
        state.Toggle.MarkAllReady(true); MapQuestReadyUp.TickClientConfirmReveal(state.Toggle,window);
        Check(!state.Toggle.IsVisible,"all-ready native continuation owns hiding its button");

        state=Setup(); state.Toggle.PlayersReady.Add(PlayerRegistry.MyPlayer);
        state.Toggle.PlayersReady.Add(PlayerRegistry.AllPlayers[1]);
        MapQuestReadyUp.OnQuestDecisionChanged("Location_A","Location_B");
        Check(state.Toggle.UnreadyActions==1 && state.Toggle.LastAutoValidate,"full ready roster withdraws through native auto-validation");
        Check(state.Toggle.PlayersReady.Count==2,"presentation never writes the replicated ready collection");
        StoryComposite.PointOfNoReturn=true; MapQuestReadyUp.OnQuestDecisionChanged("Location_B","Location_C");
        Check(state.Toggle.UnreadyActions==1,"committed quest cannot be un-readied by a map edge");
        state.Toggle.readyUpToggleState=EReadyUpToggleStates.Reward; StoryComposite.PointOfNoReturn=false;
        MapQuestReadyUp.OnQuestDecisionChanged("Location_B","Location_C");
        Check(state.Toggle.UnreadyActions==1,"reused reward readiness is not withdrawn by quest browsing");

        state=Setup(); GameObject current=state.Toggle.window.gameObject;
        MapQuestReadyUp.TickClaim(current,false,null,null,false,false);
        Check(ReadyToggleParkClaim.Claimed && ReferenceEquals(current,ReadyToggleParkClaim.ClaimedObject),"native toggle intent precedes its first Show");
        MapQuestReadyUp.TickClaim(current,true,null,null,false,false); Time.unscaledTime+=.6f;
        MapQuestReadyUp.TickClaim(current,true,null,null,false,false);
        Check(!ReadyToggleParkClaim.Claimed,"visible but unparkable control releases its fallback float");
        MapQuestReadyUp.TickClaim(current,true,current,window,true,false);
        Check(ReadyToggleParkClaim.Claimed,"actual parked current singleton owns its float claim");
        MapQuestReadyUp.TickClaim(new GameObject(),true,current,window,true,false);
        Check(!ReferenceEquals(current,ReadyToggleParkClaim.ClaimedObject),"stale parked singleton cannot claim the current confirm");
        MapQuestReadyUp.TickClaim(current,true,current,window,true,true);
        Check(!ReadyToggleParkClaim.Claimed,"parking standdown leaves the genuine native control reachable");
        MapQuestReadyUp.TickClaim(current,true,current,window,true,false); Time.unscaledTime+=1.1f;
        Check(!ReadyToggleParkClaim.Claimed,"lost parker expires without an explicit reset");
        MapQuestReadyUp.TickClaim(current,true,current,window,true,false); MapQuestReadyUp.Reset();
        Check(!ReadyToggleParkClaim.Claimed,"room reset relinquishes presentation claims immediately");
        MapQuestReadyUp.TickClaim(current,true,current,window,true,false); current.Destroyed=true;
        Check(!ReadyToggleParkClaim.Claimed,"destroyed native target immediately relinquishes its claim");
        System.Console.WriteLine($"Map quest readiness: {_assertions} assertions; {layouts} host/VR membership layouts; full production dispatcher and native visibility/Initialize/ReadyUp/presenter bodies passed.");
    }
}
