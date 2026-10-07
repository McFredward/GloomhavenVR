using System;
using System.Reflection;
using System.Linq;
using FFSNet;
using MEC;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;

internal static class DepartureProgram
{
    private static int _assertions;
    private static void Check(bool value,string reason) { _assertions++; if (!value) throw new InvalidOperationException(reason); }
    private static UIReadyToggle Setup(bool apply=true,bool client=false,bool hostReady=true)
    {
        NativeProbeFaults.Reset(); VRLog.Alerts.Clear(); VRLog.AlertAttempts=0;
        VRSession.IsRunning=true; FFSNetwork.IsOnline=true; FFSNetwork.IsClient=client; FFSNetwork.Desyncs=0;
        ActionProcessor.CurrentPhase=ActionPhaseType.MapHQ; Timing.Reset(); Synchronizer.Actions.Clear(); Timekeeper.instance.m_GlobalClock.time=0;
        StoryComposite.PointOfNoReturn=false;
        PlayerRegistry.MyPlayer=new() { PlayerID=1 };
        PlayerRegistry.AllPlayers=new() { PlayerRegistry.MyPlayer,new() { PlayerID=2 },new() { PlayerID=3 } };
        PlayerRegistry.OnPlayerLeft=null;
        var toggle=new UIReadyToggle(); Singleton<UIReadyToggle>.Instance=toggle;
        Singleton<UIMapMultiplayerController>.Instance=new();
        bool validate=false;
        if (apply) validate=Apply(false,UIReadyToggle.EReadyUpType.Participant,EReadyUpToggleStates.Quests);
        toggle.Initialize(show:false,onAllPlayersReady:()=>Proceeded++,validateReadyUpOnPlayerLeft:validate,
            readyUpType:UIReadyToggle.EReadyUpType.Participant,readyUpToggleState:EReadyUpToggleStates.Quests);
        toggle.SetInteractable(true); toggle.ToggleVisibility(true);
        if (hostReady) toggle.PlayersReady.Add(PlayerRegistry.MyPlayer);
        toggle.PlayersReady.Add(PlayerRegistry.AllPlayers[1]);
        toggle.SetLocalReadySnapshot(hostReady);
        Proceeded=0;
        return toggle;
    }
    private static int Proceeded;
    private static bool Apply(bool requested,UIReadyToggle.EReadyUpType type,EReadyUpToggleStates state)
    {
        object[] args={requested,type,state};
        typeof(MapQuestDepartureValidation.InitializeSeam).GetMethod("BeforeInitialize",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
        return (bool)args[0];
    }
    private static void Leave(NetworkPlayer player)
    {
        PlayerRegistry.AllPlayers.Remove(player);
        object[] args={Singleton<UIReadyToggle>.Instance,false};
        DepartureHooks.Call(typeof(MapQuestDepartureValidation.PlayerLeftSeam),"BeforePlayerLeft",args);
        PlayerRegistry.OnPlayerLeft?.Invoke(player);
        DepartureHooks.Call(typeof(MapQuestDepartureValidation.PlayerLeftSeam),"AfterPlayerLeft",new object[] { Singleton<UIReadyToggle>.Instance,args[1] });
    }
    private static void AckAll()
    {
        foreach (var player in PlayerRegistry.AllPlayers)
            foreach (var peer in PlayerRegistry.Participants)
                if (!player.PlayersACKedMyLatestControllableState.Contains(peer)) player.PlayersACKedMyLatestControllableState.Add(peer);
    }
    private static bool Continues(Action action)
    {
        try { action(); return true; }
        catch (Exception) { return false; }
    }
    private static Exception? OriginalException(Action action)
    {
        try { action(); return null; }
        catch (Exception error)
        {
            while (error is TargetInvocationException invocation && invocation.InnerException!=null) error=invocation.InnerException;
            return error;
        }
    }
    private static void ProbeDeparture(UIReadyToggle toggle)
    {
        typeof(MapQuestDepartureValidation.PlayerLeftSeam).GetMethod("BeforePlayerLeft",BindingFlags.NonPublic|BindingFlags.Static)!
            .Invoke(null,new object[] { toggle,false });
    }
    private static void FaultProof()
    {
        foreach (string property in new[] { "network.online","phase" })
        foreach (bool requested in new[] { false,true })
        {
            var toggle=Setup(client:true); NativeProbeFaults.Arm(property);
            Check(Continues(()=>toggle.NativeInitialize(requested)),"native Initialize continues after failed departure probe");
            Check(NativeProbeFaults.Triggered==1 && toggle.DepartureValidationOption==requested,
                "failed initialization probe preserves the native caller option");
            Check(!toggle.ToggledOn && !toggle.IsInteractable,"original native Initialize still resets its own toggle after probe failure");
        }
        foreach (string property in new[] { "network.online","network.client","phase" })
        {
            var toggle=Setup(client:true); var departed=PlayerRegistry.AllPlayers[2]; toggle.PlayersReady.Add(departed);
            NativeProbeFaults.Arm(property);
            Check(Continues(()=>Leave(departed)),"native OnPlayerLeft continues after failed prefix probe");
            Check(NativeProbeFaults.Triggered==1 && toggle.PlayersReady.Count==2 && !toggle.PlayersReady.Contains(departed),
                "original departure removal still executes after failed prefix probe");
            toggle.ExplicitInput(false);
            Check(Synchronizer.Actions.Count==0,"failed departure prefix cannot authorize later withdrawal");
        }
        {
            var toggle=Setup(client:true); var departed=PlayerRegistry.AllPlayers[2]; toggle.PlayersReady.Add(departed);
            // Original OnReadiedPlayersChanged reads visibility once before the mod postfix.
            NativeProbeFaults.Arm("visible",skip:1);
            Check(Continues(()=>Leave(departed)),"native OnPlayerLeft continues after failed postfix probe");
            Check(NativeProbeFaults.Triggered==1 && toggle.PlayersReady.Count==2,"postfix failure retains original native departure removal");
            toggle.ExplicitInput(false);
            Check(Synchronizer.Actions.Count==0,"failed departure postfix cannot authorize later withdrawal");
        }
        foreach (string property in new[] { "network.online","network.client","phase","proposal","quest.id",
                     "ready-roster","participants","local-player","player.id","visible","interactable","can-toggle" })
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); NativeProbeFaults.Arm(property);
            Check(Continues(()=>toggle.ExplicitInput(false)),"native InputToggle continues after failed departure probe");
            Check(NativeProbeFaults.Triggered==1 && !toggle.ToggledOn && Synchronizer.Actions.Count==0,
                "failed explicit-input probe retains original native input and full-count refusal");
            Check(VRLog.Alerts.Count==1 && VRLog.Alerts[0].Contains("InvalidOperationException") && VRLog.Alerts[0].Contains(property),
                "unexpected native probe failure records bounded normal-level type and message");
            toggle.ExplicitInput(false);
            Check(Synchronizer.Actions.Count==0,"failed explicit-input probe clears its departure authorization");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true);
            NativeProbeFaults.Arm("progress");
            Check(Continues(()=>toggle.ExplicitInput(false)),"native InputToggle returns after failed progress-state probe");
            Check(NativeProbeFaults.Triggered==1 && toggle.IsProgressingBar,"native input started its own asynchronous progress before finalizer probe failed");
            int ended=0; toggle.SetProgressCallbacks(end:_=>ended++); toggle.CompleteProgress();
            Check(ended==1 && Synchronizer.Actions.Count==0,"failed input finalizer clears delayed withdrawal while native completion still runs");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true); toggle.ExplicitInput(false);
            int ended=0; toggle.SetProgressCallbacks(end:_=>ended++); NativeProbeFaults.Arm("proposal");
            Check(Continues(()=>toggle.CompleteProgress()),"native progress completion continues after failed departure probe");
            Check(NativeProbeFaults.Triggered==1 && ended==1 && Synchronizer.Actions.Count==0,
                "failed completion probe preserves original callback but cannot mutate native withdrawal option");
            toggle.ExplicitInput(false); Check(Synchronizer.Actions.Count==0,"failed completion probe expires its delayed authorization");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true); toggle.ExplicitInput(false);
            toggle.SetProgressCallbacks(end:_=>NativeProbeFaults.Arm("proposal"));
            Check(Continues(()=>toggle.CompleteProgress()),"native ReadyUp continues after failed departure probe");
            Check(NativeProbeFaults.Triggered==1 && Synchronizer.Actions.Count==0,"failed ready probe leaves native auto-validation false");
            toggle.SetProgressCallbacks(); toggle.ExplicitInput(false);
            Check(Synchronizer.Actions.Count==0,"failed ready probe clears its scoped withdrawal authorization");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); NativeProbeFaults.Arm("proposal");
            Check(Continues(()=>toggle.NativeReadyUp(false,true)),"failed mod ready probe preserves a caller's existing native authorization");
            Check(NativeProbeFaults.Triggered==1 && Synchronizer.Actions.Single().AutoValidate,
                "probe failure preserves incoming true auto-validation without manufacturing it");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]);
            for (int i=0;i<3;i++) { NativeProbeFaults.Arm("phase"); ProbeDeparture(toggle); }
            Check(VRLog.AlertAttempts==1 && VRLog.Alerts.Count==1,"repeated native probe failures report once per initialization lifetime");
            toggle.NativeReset(); NativeProbeFaults.Arm("phase"); ProbeDeparture(toggle);
            Check(VRLog.AlertAttempts==2,"original Reset opens one new bounded probe-report lifetime");
            NativeProbeFaults.Arm("phase"); toggle.NativeInitialize(false);
            Check(VRLog.AlertAttempts==3,"original Initialize opens one new bounded probe-report lifetime");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); NativeProbeFaults.Arm("phase"); NativeProbeFaults.AlsoArm("log.failure");
            Check(Continues(()=>toggle.ExplicitInput(false)),"failed diagnostic logger cannot escape native input");
            Check(NativeProbeFaults.Triggered==2 && VRLog.AlertAttempts==1 && Synchronizer.Actions.Count==0,
                "failed diagnostic logger still clears authorization and preserves native full-count refusal");
            NativeProbeFaults.Arm("phase"); ProbeDeparture(toggle);
            Check(VRLog.AlertAttempts==1,"failed diagnostic logger is never retried within the same lifetime");
        }
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); NativeProbeFaults.Arm("log");
            Check(Continues(()=>toggle.ExplicitInput(false)),"failed admitted-withdrawal logger cannot block original native consent");
            Check(NativeProbeFaults.Triggered==1 && Synchronizer.Actions.Single().AutoValidate,
                "successful native probes authorize the original action even when its normal note fails");
        }
        foreach (bool delayed in new[] { false,true })
        {
            var toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true);
            var original=new InvalidOperationException("Original native progress callback failure");
            if (delayed) { toggle.ExplicitInput(false); toggle.SetProgressCallbacks(end:_=>throw original); }
            else toggle.SetProgressCallbacks(start:_=>throw original);
            Check(ReferenceEquals(OriginalException(delayed ? ()=>toggle.CompleteProgress() : ()=>toggle.ExplicitInput(false)),original),
                "departure finalizer preserves the exact original native exception");
            toggle.SetProgressCallbacks(); if (delayed) toggle.CompleteProgress(); else toggle.ExplicitInput(false);
            Check(Synchronizer.Actions.Count==0,"original native input or completion failure expires mod withdrawal permission");
        }
        foreach (Type seam in new[] { typeof(MapQuestDepartureValidation.ExplicitInputSeam),typeof(MapQuestDepartureValidation.ProgressEndSeam) })
        {
            var original=new InvalidOperationException("Native exception return sentinel");
            string name=seam==typeof(MapQuestDepartureValidation.ExplicitInputSeam) ? "AfterExplicitInput" : "AfterProgressEnd";
            object?[] parameters=name=="AfterExplicitInput" ? new object?[] { Singleton<UIReadyToggle>.Instance,true,original } : new object?[] { true,original };
            Check(ReferenceEquals(seam.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,parameters),original),
                "finalizer returns the original native exception unchanged");
        }
    }
    private static void Main()
    {
        var toggle=Setup(apply:false); Leave(PlayerRegistry.AllPlayers[2]);
        Check(Timing.Starts==0 && Proceeded==0,"old native false departure flag leaves a sufficient quorum without ACK progression");
        toggle.ReadyUp(false,false);
        Check(Synchronizer.Actions.Count==0,"ordinary native Cancel is refused by the full-roster guard after departure");

#if REAL_HARMONY
        MapQuestDepartureValidation.Install();
        foreach (string method in new[] { "Initialize","OnPlayerLeft","InputToggle","OnEndAnimationProgressBar","CancelProgress","ReadyUp","Reset" })
        {
            var original=method=="ReadyUp" ? typeof(UIReadyToggle).GetMethod(method,new[] { typeof(bool),typeof(bool) })!
                : typeof(UIReadyToggle).GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
            var info=HarmonyLib.Harmony.GetPatchInfo(original);
            Check(info!=null && info.Owners.Contains("gloomhavenvr.quest.departure.fixture"),"actual HarmonyX installs each of the seven production departure targets");
            if (method=="InputToggle" || method=="OnEndAnimationProgressBar")
                Check(info!.Prefixes.Single().PatchMethod.DeclaringType==info.Finalizers.Single().PatchMethod.DeclaringType,
                    "actual HarmonyX shares scoped __state between same-class input/progress prefix and finalizer");
        }
        DepartureHooks.RealInstalled=true;
        System.Console.WriteLine("Actual HarmonyX 2.7.0: seven production targets and two scoped-state pairs installed under Unity Mono.");
#endif

        foreach (var phase in new[] { ActionPhaseType.MapHQ,ActionPhaseType.MapAtLinkedScenario })
        {
            toggle=Setup(); ActionProcessor.CurrentPhase=phase;
            Leave(PlayerRegistry.AllPlayers[2]);
            Check(Timing.Starts==1,"native quest departure option starts original ACK coroutine after participant departure");
            Check(Synchronizer.Actions.Count(x=>x.Type==GameActionType.AllPlayersReady)==2,"original native barrier requests both remaining controllable states");
            Check(Proceeded==0 && !Synchronizer.Actions.Any(x=>x.Type==GameActionType.ReadyProceed),"departure never proceeds before required native controllable ACKs");
            Timing.Advance(); Check(Proceeded==0,"missing peer ACK remains a native wait across ticks");
            AckAll(); Timing.Advance();
            Check(Proceeded==1 && Synchronizer.Actions.Count(x=>x.Type==GameActionType.ReadyProceed)==1,"native ACK completion alone emits one ReadyProceed and continuation");
            Check(toggle.PlayersReady.Count==0,"original native Proceed owns readiness reset");
        }
        toggle=Setup(); Leave(PlayerRegistry.AllPlayers[2]); Timekeeper.instance.m_GlobalClock.time=31; Timing.Advance();
        Check(FFSNetwork.Desyncs==1 && Proceeded==0,"original missing-ACK timeout remains active without forced progression");
        toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]);
        Check(Timing.Starts==0 && Proceeded==0,"VR client cannot start the Flat host ACK coroutine");
        toggle=Setup(hostReady:false); Leave(PlayerRegistry.AllPlayers[2]);
        Check(Timing.Starts==0,"unready native host cannot proceed on another participant departure");
        toggle=Setup(); toggle.WaitForPlayerBeforeProceeding(PlayerRegistry.AllPlayers[1]); Leave(PlayerRegistry.AllPlayers[2]);
        Check(Timing.Starts==0,"an awaited remaining native player prevents departure progression");
        toggle=Setup(); PlayerRegistry.MyPlayer.IsParticipant=false; Leave(PlayerRegistry.AllPlayers[2]);
        Check(Timing.Starts==0,"a spectator native host cannot proceed for participant readiness");

        foreach (int exclusion in Enumerable.Range(0,6))
        {
            Setup();
            var type=UIReadyToggle.EReadyUpType.Participant; var state=EReadyUpToggleStates.Quests;
            if (exclusion==0) VRSession.IsRunning=false;
            if (exclusion==1) FFSNetwork.IsOnline=false;
            if (exclusion==2) type=UIReadyToggle.EReadyUpType.Player;
            if (exclusion==3) state=EReadyUpToggleStates.Reward;
            if (exclusion==4) ActionProcessor.CurrentPhase=ActionPhaseType.MapLoadoutScreen;
            if (exclusion==5) ActionProcessor.CurrentPhase=ActionPhaseType.ScenarioEnded;
            Check(!Apply(false,type,state),"departure validation excludes VR-off/offline/player/nonquest/nonmap phases");
            Check(Apply(true,type,state),"native caller's existing true option is always preserved");
        }
        toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]);
        Check(toggle.IsVisible && toggle.IsInteractable && toggle.ToggledOn,"the original local Cancel remains visibly enabled after unready departure");
        toggle.ReadyUp(false); Check(Synchronizer.Actions.Count==0,"non-input ReadyUp never inherits departure withdrawal authorization");
        toggle.ExplicitInput(false);
        Check(Synchronizer.Actions.Count(x=>x.Type==GameActionType.UnreadyPlayer&&x.AutoValidate)==1,"real local Cancel emits unchanged native auto-validated withdrawal after Flat host departure");
        Check(toggle.PlayersReady.Count==2 && Timing.Starts==0,"VR client does not invent validated readiness or a Flat host coroutine");
        toggle.ReadyUp(false); Check(Synchronizer.Actions.Count==1,"departure withdrawal permission is one-shot and never a standing bypass");

        toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true);
        toggle.ExplicitInput(false);
        Check(Synchronizer.Actions.Count==0 && toggle.IsProgressingBar,"native asynchronous Cancel waits for its actual animation completion");
        toggle.ReadyUp(false); Check(Synchronizer.Actions.Count==0,"unrelated ReadyUp during Cancel progress cannot use the user input scope");
        toggle.CompleteProgress();
        Check(Synchronizer.Actions.Count(x=>x.Type==GameActionType.UnreadyPlayer&&x.AutoValidate)==1,"original delayed Cancel completion retains exactly its genuine user authorization");

        toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); toggle.EnableCancelProgress(true); toggle.ExplicitInput(false);
        toggle.AbortProgress(); toggle.CompleteProgress();
        Check(Synchronizer.Actions.Count==0,"aborted native Cancel progress loses its departure authorization");
        foreach (int refusal in Enumerable.Range(0,12))
        {
            toggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]);
            if (refusal==0) StoryComposite.PointOfNoReturn=true;
            if (refusal==1) ActionProcessor.CurrentPhase=ActionPhaseType.MapLoadoutScreen;
            if (refusal==2) Singleton<UIMapMultiplayerController>.Instance.HostSelectedQuest=new() { ID="Quest_B" };
            if (refusal==3) Singleton<UIMapMultiplayerController>.Instance=new();
            if (refusal==4) FFSNetwork.IsClient=false;
            if (refusal==5) FFSNetwork.IsOnline=false;
            if (refusal==6) VRSession.IsRunning=false;
            if (refusal==7) toggle.readyUpToggleState=EReadyUpToggleStates.Reward;
            if (refusal==8) toggle.ToggleVisibility(false);
            if (refusal==9) toggle.SetInteractable(false);
            if (refusal==10) toggle.CanBeToggled=false;
            if (refusal==11) { Apply(false,UIReadyToggle.EReadyUpType.Participant,EReadyUpToggleStates.Quests); }
            toggle.ExplicitInput(false);
            Check(!Synchronizer.Actions.Any(x=>x.Type==GameActionType.UnreadyPlayer),"changed context or denied input cannot borrow a departure withdrawal");
        }
        toggle=Setup(client:true); PlayerRegistry.AllPlayers[2].IsParticipant=false; Leave(PlayerRegistry.AllPlayers[2]); toggle.ExplicitInput(false);
        Check(Synchronizer.Actions.Any(x=>x.Type==GameActionType.UnreadyPlayer&&x.AutoValidate),"spectator departure may authorize the same measured blocked genuine Cancel state");
        toggle=Setup(client:true); var departed=PlayerRegistry.AllPlayers[2]; toggle.PlayersReady.Add(departed);
        typeof(UIReadyToggle).GetMethod("OnReadiedPlayersChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(toggle,new object[] { departed,true });
        Check(!toggle.IsVisible,"native all-ready ACK waiting hides the original Cancel before ready departure");
        Leave(departed); toggle.ExplicitInput(false);
        Check(Synchronizer.Actions.Any(x=>x.Type==GameActionType.UnreadyPlayer&&x.AutoValidate),"ready departure may authorize the newly visible blocked genuine native Cancel");

        // Two native process contexts, represented by their separate original toggle objects.
        // The Flat host does not apply the mod initialization/admission policy. Every received
        // request below passes through the original native ProxySetReadyState/UnreadyPlayer.
        var clientToggle=Setup(client:true); Leave(PlayerRegistry.AllPlayers[2]); clientToggle.ExplicitInput(false);
        var withdrawal=Synchronizer.Actions.Single(x=>x.Type==GameActionType.UnreadyPlayer);
        var clientPlayer=PlayerRegistry.MyPlayer; var flatHost=PlayerRegistry.AllPlayers[1];
        FFSNetwork.IsClient=false; VRSession.IsRunning=false; PlayerRegistry.MyPlayer=flatHost;
        var hostToggle=new UIReadyToggle(); Singleton<UIReadyToggle>.Instance=hostToggle;
        hostToggle.Initialize(show:false,onAllPlayersReady:()=>Proceeded++,validateReadyUpOnPlayerLeft:false,
            readyUpType:UIReadyToggle.EReadyUpType.Participant,readyUpToggleState:EReadyUpToggleStates.Quests);
        hostToggle.PlayersReady.Add(flatHost); hostToggle.PlayersReady.Add(clientPlayer); hostToggle.SetLocalReadySnapshot(true);
        var nativeUnready=new GameAction { PlayerID=clientPlayer.PlayerID,ActionTypeID=21,SupplementaryDataBoolean=false };
        bool forward=false; hostToggle.ProxySetReadyState(nativeUnready,ref forward);
        Check(!forward && hostToggle.PlayersReady.Count==2,"unmodified Flat host rejects ordinary withdrawal at its original full-count guard");
        nativeUnready.SupplementaryDataBoolean=withdrawal.AutoValidate; hostToggle.ProxySetReadyState(nativeUnready,ref forward);
        Check(forward && hostToggle.PlayersReady.Count==1 && hostToggle.PlayersReady.Contains(flatHost),"unmodified Flat host accepts only the native auto-validated explicit VR withdrawal");
        Check(Timing.Starts==0 && Proceeded==0,"Flat host withdrawal resets readiness without auto-readying or forcing progression");
        FFSNetwork.IsClient=true; VRSession.IsRunning=true; PlayerRegistry.MyPlayer=clientPlayer; Singleton<UIReadyToggle>.Instance=clientToggle;
        clientToggle.ProxySetReadyState(nativeUnready,ref forward);
        Check(clientToggle.PlayersReady.Count==1 && !clientToggle.ToggledOn,"native validated withdrawal reaches the originating VR client's own toggle");
        clientToggle.ExplicitInput(true); clientToggle.CompleteProgress(true);
        var acceptance=Synchronizer.Actions.Last(x=>x.Type==GameActionType.ReadyUpPlayer);
        Check(acceptance.PlayerId==clientPlayer.PlayerID && acceptance.Token2 is ReadyUpToken,"a fresh genuine local Accept produces the original native ready token");
        FFSNetwork.IsClient=false; VRSession.IsRunning=false; PlayerRegistry.MyPlayer=flatHost; Singleton<UIReadyToggle>.Instance=hostToggle;
        hostToggle.ProxySetReadyState(new GameAction { PlayerID=acceptance.PlayerId,ActionTypeID=20,SupplementaryDataToken2=acceptance.Token2 },ref forward);
        Check(forward && Timing.Starts==1 && Proceeded==0,"unmodified Flat host starts its original ACK barrier only after the new native Accept");
        Timing.Advance(); Check(Proceeded==0,"unmodified Flat host still waits for the missing controllable-state ACK");
        hostToggle.ServerACKSyncedStateRevision(new GameAction { PlayerID=clientPlayer.PlayerID,SupplementaryDataIDMax=flatHost.PlayerID });
        Timing.Advance();
        Check(Proceeded==1 && Synchronizer.Actions.Count(x=>x.Type==GameActionType.ReadyProceed)==1,"original Flat host ACK receiver alone admits ReadyProceed and continuation");
        FaultProof();
        System.Console.WriteLine($"Native quest departure: {_assertions} assertions passed; original OnPlayerLeft/ACK/Proceed/Reset/ReadyUp bodies executed.");
    }
}
