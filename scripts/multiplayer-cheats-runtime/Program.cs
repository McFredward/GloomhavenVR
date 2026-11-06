using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using MapRuleLibrary.MapState;
using ScenarioRuleLibrary;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string why) { ++_checks; if (!value) throw new Exception(why); }
    private static void Reset()
    {
        GloomhavenVR.Core.VRLog.Throw=false; ++VersionGuard.SessionEpoch; VersionGuard.Versions.Clear(); VersionGuard.Versions[2] = NetProtocol.ModBuild;
        PlayerRegistry.AllPlayers = new() { new() { PlayerID = 1 }, new() { PlayerID = 2 } };
        PlayerRegistry.HostPlayer = PlayerRegistry.MyPlayer = PlayerRegistry.AllPlayers[0];
        MultiplayerCheats.NoteVersionReceipt(2,NetProtocol.ModBuild);
        PlayerRegistry.JoiningPlayers.Clear(); PlayerRegistry.ConnectingUsers.Clear();
        PlayerRegistry.OtherClientsAreJoining = PlayerRegistry.LoadingInFromJoiningClient = PlayerRegistry.IsSwitchingCharacter = false;
        FFSNetwork.IsOnline = FFSNetwork.IsHost = true; FFSNetwork.HasDesynchronized = false; FFSNetwork.LastDesync = null;
        VROptionsTab.CheatsEnabledNow = true; VROptionsTab.ThrowBackup=false; NetSession.FlatNetMode = false;
        ActionProcessor.SetState(ActionProcessorStateType.ProcessFreely, ActionPhaseType.MapHQ);
        ActionProcessor.ActionQueue.Clear(); ActionProcessor.ReadyToProcessNextAction = true; ActionProcessor.Locked=false;
        AdventureState.MapState = new(); ScenarioManager.CurrentScenarioState = null; ScenarioManager.Scenario = new();
        SceneController.Instance = new(); SaveData.Instance = new(); Calls.Trace.Clear(); Calls.OnGold=null; Singleton<MapChoreographer>.Instance=new();
        ScenarioRuleClient.IsProcessingOrMessagesQueued = false;
        GameState.WaitingForPlayerToSelectDamageResponse = GameState.WaitingForPlayerActorToAvoidDamageResponse = false;
        Singleton<StoryController>.Instance = null!; Choreographer.s_Choreographer = new();
    }
    private static bool Gold(string who = "Alice") => MultiplayerCheats.TryQueue(MultiplayerCheatKind.Gold, "Brute", characterName: who);
    private static CMapParty Party => AdventureState.MapState!.MapParty;
    private static GameAction AcceptedGold()
    {
        Reset(); Check(Gold(), "host gold accepted for queue");
        Check(Party.CheckCharacters.All(c => c.Gold == 0), "enqueue does not mutate ahead of native queue");
        Check(ActionProcessor.ActionQueue.Peek().TargetPhaseID == 0, "native targetNONE prevents wrong-phase deadline");
        Check(ActionProcessor.DrainOne(), "original native queue drains gold");
        Check(Party.CheckCharacters[0].Gold == 100 && Party.CheckCharacters[1].Gold == 0, "exact class and character name chosen");
        Check(SaveData.Instance!.Saves == 1, "host saves once");
        Check(PlayerRegistry.AllPlayers[1].Inbox.Count == 1, "native host finish forwards accepted action once");
        Check(string.Join(",",Calls.Trace) == "native-queue,native-log:1,native-forward:2", "original native queue logging and forwarding order");
        return PlayerRegistry.AllPlayers[1].Inbox.Single();
    }
    private static void Client(GameAction action)
    {
        FFSNetwork.IsHost = false; PlayerRegistry.MyPlayer = PlayerRegistry.AllPlayers[1]; MultiplayerCheats.NoteVersionReceipt(1,NetProtocol.ModBuild);
        AdventureState.MapState = new(); SaveData.Instance = new();
        VROptionsTab.CheatsEnabledNow = false; NetSession.FlatNetMode = true;
        Check(action.ExecutePatched(), "accepted native client commit applies with local Cheats off and FlatNet selected");
        Check(action.IsValid, "accepted replica retains original validity");
        Check(Party.CheckCharacters[0].Gold == 100 && Party.CheckCharacters[1].Gold == 0, "replica credits exact host target, not its local selection");
        Check(SaveData.Instance.Saves == 0, "replica never writes a private save");
        Check(action.ExecutePatched() && Party.CheckCharacters[0].Gold == 100, "duplicate native delivery does not grant twice");
        ActionProcessor.CurrentPhase = ActionPhaseType.StartOfRound;
        Check(action.ExecutePatched() && Party.CheckCharacters[0].Gold == 100, "duplicate after phase transition remains harmless");
    }
    private static void Refusals()
    {
        foreach (var refusal in new (string Name,Action Change)[]
        {
            ("guest",()=>FFSNetwork.IsHost=false), ("disabled",()=>VROptionsTab.CheatsEnabledNow=false),
            ("flat mode",()=>NetSession.FlatNetMode=true), ("unmodded",()=>MultiplayerCheats.NoteVersionReceipt(2,0)),
            ("join",()=>PlayerRegistry.JoiningPlayers.Add(new object())),
            ("connecting",()=>PlayerRegistry.ConnectingUsers.Add(new object())),
            ("joining flag",()=>PlayerRegistry.OtherClientsAreJoining=true),
            ("switching",()=>PlayerRegistry.IsSwitchingCharacter=true),
            ("halted",()=>ActionProcessor.SetState(ActionProcessorStateType.Halted,ActionPhaseType.MapHQ)),
            ("desynced",()=>FFSNetwork.HasDesynchronized=true),
            ("loading",()=>SceneController.Instance!.IsLoading=true),
            ("story",()=>Singleton<StoryController>.Instance=new() { IsVisible=true }),
            ("damage",()=>GameState.WaitingForPlayerToSelectDamageResponse=true),
            ("rules busy",()=>ScenarioRuleClient.IsProcessingOrMessagesQueued=true),
            ("message queue",()=>Choreographer.s_Choreographer!.m_MessageQueue.Enqueue(new object()))
        })
        {
            Reset(); refusal.Change();
            Check(!Gold(), "enqueue refusal: " + refusal.Name);
            Check(ActionProcessor.ActionQueue.Count == 0 && Party.CheckCharacters.All(c=>c.Gold==0), "refusal has no mutation or queued action: "+refusal.Name);
        }
        foreach (var change in new (string Name,Action Change)[]
        {
            ("disabled",()=>VROptionsTab.CheatsEnabledNow=false), ("host",()=>FFSNetwork.IsHost=false),
            ("peer changed",()=>MultiplayerCheats.NoteVersionReceipt(2,0)), ("join",()=>PlayerRegistry.JoiningPlayers.Add(new object())),
            ("new flat peer",()=>PlayerRegistry.AllPlayers.Add(new() { PlayerID=3 })),
            ("map replaced",()=>AdventureState.MapState=new()), ("map phase",()=>ActionProcessor.CurrentPhase=ActionPhaseType.StartOfRound),
            ("save unavailable",()=>SaveData.Instance=null!)
        })
        {
            Reset(); Check(Gold(), "queued before changed " + change.Name); var pending = Synchronizer.Last!;
            change.Change();
            // Losing actual host role after enqueue means a foreign original host
            // command is no longer a valid local origin. No local click can migrate authority.
            if(change.Name=="host") { FFSNetwork.IsHost=true; pending.PlayerID=2; }
            Check(ActionProcessor.DrainOne(), "changed command retires without stalling: "+change.Name);
            Check(!pending.IsValid && Party.CheckCharacters.All(c=>c.Gold==0), "changed command rejected before apply: "+change.Name);
            Check(PlayerRegistry.AllPlayers[1].Inbox.Count==0, "rejected native command never forwarded: "+change.Name);
        }
    }
    private static void OtherCheats()
    {
        Reset(); Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Unlock),"unlock queues");
        Check(ActionProcessor.DrainOne(),"unlock drains");
        Check(!Singleton<MapChoreographer>.Instance.LastSave,"native map refresh never writes a receiving private save");
        Check(AdventureState.MapState!.AllQuests.Take(2).All(q=>q.QuestState==CQuestState.EQuestState.Unlocked)
            && AdventureState.MapState.AllQuests[2].QuestState==CQuestState.EQuestState.Locked,"same native non-Job unlocks, including blocked quests");
        Reset(); Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Requirements,enabled:true),"requirements queues");
        Check(ActionProcessor.DrainOne(),"requirements drains");
        Check(GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.Enabled && MultiplayerCheats.SharedRequirementsAllowed,"host requirements common lease");
        GameAction gate = PlayerRegistry.AllPlayers[1].Inbox.Single();
        FFSNetwork.IsHost=false; PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1]; MultiplayerCheats.NoteVersionReceipt(1,NetProtocol.ModBuild); AdventureState.MapState=new();
        VROptionsTab.CheatsEnabledNow=false;
        Check(gate.ExecutePatched() && MultiplayerCheats.SharedRequirementsAllowed,"client requirements lease ignores private opt-in");
        var requirements=new RequirementCheckResult();
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(requirements.missingAmountCharacters==0 && requirements.missingRequiredCharacters.Count==0
            && requirements.missingRequiredItems.Count==0 && requirements.missingRequiredPersonalQuests.Count==0
            && requirements.startingLocationState==RequirementCheckResult.StartingLocatinState.Valid,
            "shared native requirements override reaches the real result");
        Check(requirements.IsQuestStateLocked,"requirements override leaves native quest lock intact");
        PlayerRegistry.AllPlayers.Add(new() { PlayerID=3 });
        Check(!MultiplayerCheats.SharedRequirementsAllowed,"requirements lease ends on exact roster change");
        requirements=new RequirementCheckResult();
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(requirements.missingAmountCharacters==2 && !GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.Enabled,
            "unshared stale requirements lease restores original verdict without a fabricated choice");
        ++VersionGuard.SessionEpoch;
        Check(!MultiplayerCheats.SharedRequirementsAllowed,"requirements lease cannot leak into another session");
        Reset(); Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Requirements,enabled:true),"online requirements re-enabled");
        Check(ActionProcessor.DrainOne(),"online requirements accepted");
        FFSNetwork.IsOnline=false; AdventureState.MapState=new();
        requirements=new RequirementCheckResult();
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(!GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.Enabled && requirements.missingAmountCharacters==2,
            "replicated online requirement override never leaks into another single-player campaign");
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.Set(true);
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(requirements.missingAmountCharacters==0,"explicit original offline requirement toggle still works");
        Reset();Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Requirements,enabled:true),"requirements queued before receiver flags");
        Check(ActionProcessor.DrainOne(),"source requirements commit accepted");
        gate=PlayerRegistry.AllPlayers[1].Inbox.Single();FFSNetwork.IsHost=false;
        PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];MultiplayerCheats.NoteVersionReceipt(1,NetProtocol.ModBuild);
        AdventureState.MapState=new();PlayerRegistry.OtherClientsAreJoining=true;
        Check(gate.ExecutePatched(),"accepted requirements may arrive before a private native join flag settles");
        requirements=new RequirementCheckResult();GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(requirements.missingAmountCharacters==2 && GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.Enabled,
            "transient native admission returns ordinary verdict without forgetting accepted shared enable");
        PlayerRegistry.OtherClientsAreJoining=false;
        GloomhavenVR.WorldUI.Patches.ScenarioGateCheat.OverrideRequirements(new(),requirements);
        Check(requirements.missingAmountCharacters==0 && MultiplayerCheats.SharedRequirementsAllowed,
            "accepted shared enable resumes after observer join flags settle");
        Reset(); Party.GoldModeFixture();
        Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Gold,"Brute",true,characterName:"Alice"),"party purse queues");
        Check(ActionProcessor.DrainOne() && Party.PartyGold==100 && Party.CheckCharacters.All(c=>c.Gold==0),"party gold uses native shared purse exactly once");
    }
    private static void Reveal()
    {
        Reset(); ScenarioManager.CurrentScenarioState = new(); ActionProcessor.CurrentPhase=ActionPhaseType.StartOfRound;
        int initial=DebugMenu.Reveals;
        Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Reveal),"reveal queued at safe host point");
        ScenarioRuleClient.IsProcessingOrMessagesQueued=true;
        Check(!ActionProcessor.DrainOne() && !ActionProcessor.IsProcessing && ActionProcessor.ActionQueue.Count==1,"busy reveal stays at original head before dequeue");
        Check(DebugMenu.Reveals==initial,"held reveal does not touch native state");
        ScenarioRuleClient.IsProcessingOrMessagesQueued=false; Choreographer.s_Choreographer!.m_MessageQueue.Enqueue(new object());
        Check(!ActionProcessor.DrainOne() && !ActionProcessor.IsProcessing,"native message queue can drain while reveal remains unprocessed");
        Choreographer.s_Choreographer.m_MessageQueue.Clear();
        Check(ActionProcessor.DrainOne() && DebugMenu.Reveals==initial+1,"original retry applies reveal exactly once after drain");
        Check(ActionProcessor.ActionQueue.Count==0 && !ActionProcessor.IsProcessing,"native queue remains usable after reveal");
        Reset(); ScenarioManager.CurrentScenarioState=new(); ActionProcessor.CurrentPhase=ActionPhaseType.StartOfRound;
        Check(MultiplayerCheats.TryQueue(MultiplayerCheatKind.Reveal),"second reveal queued");
        ScenarioRuleClient.IsProcessingOrMessagesQueued=true; PlayerRegistry.JoiningPlayers.Add(new object());
        Check(ActionProcessor.DrainOne() && ActionProcessor.ActionQueue.Count==0,"invalid joining command is not trapped by visual wait guard");
        Reset(); var ordinary=new GameAction { ActionTypeID=(int)GameActionType.PingHex,PlayerID=1 };
        ScenarioRuleClient.IsProcessingOrMessagesQueued=true; ActionProcessor.QueueUpAction(ordinary);
        Check(ActionProcessor.DrainOne() && ordinary.IsValid,"ordinary native action not blocked by reveal guard");
        Reset(); ScenarioRuleClient.IsProcessingOrMessagesQueued=true;
        ActionProcessor.QueueUpAction(new GameAction { ActionTypeID=(int)GameActionType.ConfirmAction,PlayerID=1 });
        Check(!ActionProcessor.DrainOne() && ActionProcessor.ActionQueue.Count==1 && !ActionProcessor.IsProcessing,
            "original native ConfirmAction busy guard remains unchanged");
        ScenarioRuleClient.IsProcessingOrMessagesQueued=false;
        Check(ActionProcessor.DrainOne(),"original native ConfirmAction drains normally after its own guard");
    }
    private static void NativePermits()
    {
        foreach(var mode in new[]{ActionProcessorStateType.ProcessOneAndHalt,ActionProcessorStateType.ProcessOneAndSwitchBack})
        {
            Reset(); Check(Gold(),"host command queued before permit changed");
            ActionProcessor.QueueUpAction(new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1});
            ActionProcessor.SetState(mode,ActionPhaseType.MapHQ);
            Check(ActionProcessor.DrainOne() && Party.CheckCharacters.All(c=>c.Gold==0),"stale host command cannot consume a one-action permit");
            Check(ActionProcessor.ActionQueue.Count==0 && Calls.Trace.Count(t=>t.StartsWith("native-forward:"))==1,
                "only following original choice consumes native one-action permit");
        }
        Reset();Check(Gold(),"issued head queued before native world retirement");
        ActionProcessor.QueueUpAction(new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1});
        AdventureState.MapState=new();ActionProcessor.SetState(ActionProcessorStateType.ProcessOneAndHalt,ActionPhaseType.MapHQ);
        Check(MultiplayerCheats.UnavailableReason()=="cheat_mp_busy","UI query observes replacement native world before stale head executes");
        Check(ActionProcessor.DrainOne() && ActionProcessor.ActionQueue.Count==0 && Calls.Trace.Count(t=>t.StartsWith("native-forward:"))==1,
            "context reset preserves issued ownership until exact stale head withdrawal");
        foreach(var mode in new[]{ActionProcessorStateType.ProcessOneAndHalt,ActionProcessorStateType.ProcessOneAndSwitchBack})
        {
            GameAction accepted=AcceptedGold(); FFSNetwork.IsHost=false;
            PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1]; MultiplayerCheats.NoteVersionReceipt(1,NetProtocol.ModBuild); AdventureState.MapState=new();
            ActionProcessor.SetState(mode,ActionPhaseType.MapHQ);
            ActionProcessor.QueueUpAction(accepted);
            ActionProcessor.QueueUpAction(new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1});
            Check(ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==100,"accepted receiver command applies in native one-action mode");
            Check(ActionProcessor.CurrentState.StateType==mode && ActionProcessor.ActionQueue.Count==1
                && ActionProcessor.CurrentPhase==ActionPhaseType.MapHQ && !ActionProcessor.IsProcessing,
                "accepted receiver command leaves original permit and phase untouched");
            Check(ActionProcessor.DrainOne() && ActionProcessor.ActionQueue.Count==0,"following original native choice still consumes its permit");
            Check(mode==ActionProcessorStateType.ProcessOneAndHalt
                ? ActionProcessor.CurrentState.StateType==ActionProcessorStateType.Halted && !ActionProcessor.ReadyToProcessNextAction
                : ActionProcessor.CurrentState.StateType==ActionProcessorStateType.ProcessFreely && ActionProcessor.ReadyToProcessNextAction,
                "original native struct state and readiness reflect only the genuine successor");
        }
        GameAction halted=AcceptedGold();FFSNetwork.IsHost=false;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];AdventureState.MapState=new();
        ActionProcessor.SetState(ActionProcessorStateType.Halted,ActionPhaseType.MapHQ);ActionProcessor.QueueUpAction(halted);
        Check(!ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==0 && ActionProcessor.ActionQueue.Count==1
            && !ActionProcessor.ReadyToProcessNextAction,"actual native Halted permit is preserved");
        ActionProcessor.SetState(ActionProcessorStateType.ProcessFreely,ActionPhaseType.MapHQ);
        Check(ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==100,"accepted receiver command resumes after original native unhalt");
        GameAction received=AcceptedGold(); FFSNetwork.IsHost=false;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1]; MultiplayerCheats.NoteVersionReceipt(1,NetProtocol.ModBuild);AdventureState.MapState=new();
        ActionProcessor.QueueUpAction(received); ActionProcessor.Locked=true;
        Check(!ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==0,"native lock still blocks receiver command");
        ActionProcessor.Locked=false;ActionProcessor.ReadyToProcessNextAction=false;
        Check(!ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==0,"native readiness still blocks receiver command");
        ActionProcessor.ReadyToProcessNextAction=true;
        bool nestedProcessed=true;
        bool nestedAttempted=false;
        Calls.OnGold=()=>{ if(!nestedAttempted) { nestedAttempted=true; nestedProcessed=ActionProcessor.DrainOne(); } };
        ActionProcessor.QueueUpAction(new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1});
        Check(ActionProcessor.DrainOne() && !nestedProcessed && ActionProcessor.ActionQueue.Count==1,
            "receiver native model callback cannot process a successor reentrantly");
        Calls.OnGold=null;Check(ActionProcessor.DrainOne(),"native successor drains after receiver apply finally releases guard");
        Reset();PlayerRegistry.AllPlayers[1]=new() {PlayerID=2};
        Check(!Gold(),"reused native id cannot reuse a prior weak version receipt");
        MultiplayerCheats.NoteVersionReceipt(2,NetProtocol.ModBuild);
        Check(Gold(),"new current native identity admitted after its own version advertisement");
        PlayerRegistry.AllPlayers[1]=new() {PlayerID=2};MultiplayerCheats.NoteVersionReceipt(2,NetProtocol.ModBuild);
        Check(ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==0,
            "same-id replacement invalidates an already queued host cohort");
    }
    private static void Storage()
    {
        Reset(); VROptionsTab.ThrowBackup=true;
        Check(!MultiplayerCheats.TryQueue(MultiplayerCheatKind.Unlock) && ActionProcessor.ActionQueue.Count==0,
            "backup throw happens before queue with no shared mutation");
        Check(AdventureState.MapState!.AllQuests[0].QuestState==CQuestState.EQuestState.Locked,
            "backup failure leaves native campaign untouched");
        Reset(); GloomhavenVR.Core.VRLog.Throw=true;
        Check(Gold() && ActionProcessor.DrainOne() && PlayerRegistry.AllPlayers[1].Inbox.Count==1,
            "diagnostic sink failure cannot split native forwarding");
        Reset(); SaveData.Instance.ThrowSave=true;
        Check(Gold(),"gold queues before optional storage failure");
        Check(ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==100
            && PlayerRegistry.AllPlayers[1].Inbox.Count==1,"save exception cannot split the admitted native commit");
        Client(PlayerRegistry.AllPlayers[1].Inbox.Single());
        Check(Party.CheckCharacters[0].Gold==100,"replica remains in parity after host save failure");
        var ordinary=new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1};
        ActionProcessor.QueueUpAction(ordinary);
        Check(ActionProcessor.DrainOne() && !ActionProcessor.IsProcessing,
            "next original native action drains after storage failure");
    }
    private static void NativeFailure()
    {
        GameAction accepted=AcceptedGold();FFSNetwork.IsHost=false;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];AdventureState.MapState=new();
        Calls.OnGold=()=>throw new InvalidOperationException("fixture original rule failure");
        ActionProcessor.QueueUpAction(accepted);
        Check(!ActionProcessor.DrainOne() && FFSNetwork.HasDesynchronized && FFSNetwork.LastDesync!=null && ActionProcessor.ActionQueue.Count==0,
            "accepted receiver model failure uses original native desync and retires its exact head");
        Calls.OnGold=null;Check(!ActionProcessor.DrainOne() && Party.CheckCharacters[0].Gold==0,"failed accepted model cannot be applied again on the next native retry");
        Reset();ActionProcessor.QueueUpAction(new GameAction {ActionTypeID=(int)GameActionType.PingHex,PlayerID=1});
        Check(ActionProcessor.DrainOne(),"receiver reentrancy guard is released after real native failure");
    }
    private static void Codec()
    {
        var data=new MultiplayerCheatCommand {Kind=MultiplayerCheatKind.Gold,Sequence=1,Roster=2,MapSeed=3,Mode=4,Character="Brute",CharacterName="Alice"}.Write();
        Check(Convert.ToHexString(data)=="474843310101010000000000000002000000000000000300000004000000000000000000050042727574650500416C6963650000","independent fixed native cheat payload golden");
        Check(MultiplayerCheatCommand.TryRead(data,out var c) && c!.CharacterName=="Alice","exact unicode-capable character name survives payload");
        for(int n=0;n<data.Length;n++) Check(!MultiplayerCheatCommand.TryRead(data.Take(n).ToArray(),out _),"truncated payload rejected at "+n);
        Check(!MultiplayerCheatCommand.TryRead(data.Concat(new byte[]{0}).ToArray(),out _),"trailing bytes rejected");
        var bad=(byte[])data.Clone();bad[0]^=1;Check(!MultiplayerCheatCommand.TryRead(bad,out _),"foreign magic rejected");
        bad=(byte[])data.Clone();bad[^1]=2;Check(!MultiplayerCheatCommand.TryRead(bad,out _),"noncanonical boolean rejected");
        Check(!MultiplayerCheatCommand.TryRead(new byte[513],out _),"oversized event attachment rejected");
        Reset(); var foreign=new GameAction {ActionTypeID=127,PlayerID=2,CustomBinaryData=data};
        Check(foreign.ExecutePatched() && !foreign.IsValid && Party.CheckCharacters.All(c=>c.Gold==0),"foreign native origin cannot spoof queued host cheat");
    }
    internal static void Main()
    {
        Client(AcceptedGold()); Refusals(); OtherCheats(); Reveal(); NativePermits(); Storage(); NativeFailure(); Codec();
        System.Console.WriteLine("Multiplayer cheats: "+_checks+" production/queued-native assertions passed.");
    }
    private static void GoldModeFixture(this CMapParty party) => AdventureState.MapState!.GoldMode=EGoldMode.PartyGold;
}
