using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;

internal static class Program
{
 private static int _assertions;
 private static void Check(bool value,string message)
 { ++_assertions;if(!value)throw new InvalidOperationException(message); }
 private static MapStoryOpeningLedger Ledger=>(MapStoryOpeningLedger)typeof(MapStoryLifecycle).GetField("_ledger",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
 private static void Reset()
 {
  MapStoryLifecycle.Reset();VersionGuard.SessionEpoch++;VersionGuard.Versions.Clear();
  PlayerRegistry.AllPlayers=new(){new(){PlayerID=1},new(){PlayerID=2}};PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[0];PlayerRegistry.HostPlayer=PlayerRegistry.MyPlayer;
  FFSNetwork.IsOnline=false;FFSNetwork.IsHost=true;FFSNetwork.HasDesynchronized=false;
  ScenarioManager.Scenario=new();AdventureState.MapState=new();
  Singleton<StoryController>.Instance=new();Singleton<MapStoryController>.Instance=new();
  Singleton<UINavigation>.Instance=new();Singleton<LevelEventsController>.Instance=new();Singleton<PhaseBannerHandler>.Instance=new();
  SaveData.Instance=new();SceneController.Instance=new();Choreographer.s_Choreographer=new();UINewAdventureResultsManager.Implementation=new();
  ActionProcessor.Locked=false;ActionProcessor.ReadyToProcessNextAction=true;ActionProcessor.ActionQueue.Clear();ActionProcessor.SetState(ActionProcessorStateType.ProcessFreely,ActionPhaseType.StartOfRound);
  Calls.ApplyState=0;Synchronizer.ClientReady=0;StoryController.DisplayDelayInEffect=false;
 }
 private static StoryController.DialogInfo Start(string name="YMLDefined_ScenarioStart",int trigger=0)
  =>new(){LevelMsg=new(){MessageName=name,DisplayTrigger=new(trigger)},DialogPages=new(){new(){text="INTRO_KEY_1"},new(){text="INTRO_KEY_2"}}};
 private static uint Semantic(StoryController.DialogInfo message)
 {
  uint value=SharedMapRunIdentity.Add(SharedMapRunIdentity.Key,"scenario-story");
  value=SharedMapRunIdentity.Add(value,message.LevelMsg?.MessageName);
  value=SharedMapRunIdentity.Add(value,RemoteStorySync.HashDialog(message.DialogPages).ToString("X8",System.Globalization.CultureInfo.InvariantCulture));
  return value==0?1u:value;
 }
 private static StoryController.DialogInfo CompleteHostStart(string name="YMLDefined_ScenarioStart",int trigger=0,bool complete=true)
 {
  var message=Start(name,trigger);var controller=Singleton<StoryController>.Instance;
  controller.Begin(message);MapStoryLifecycle.NativeOpened(controller,message);
  if(complete){controller.Complete();MapStoryLifecycle.NativeFinished(message);}
  return message;
 }
 private static void Online(bool receipt=true)
 {
  FFSNetwork.IsOnline=true;FFSNetwork.IsHost=true;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[0];
  VersionGuard.Versions[2]=NetProtocol.ModBuild;
  if(receipt)NativeCheatQueue.NoteVersionReceipt(2,NetProtocol.ModBuild);
 }
 private static (MapStoryOpeningLedger Guest,object Subject,MapStoryOpening Request) Request(StoryController.DialogInfo message,uint epoch=200)
 {
  var guest=new MapStoryOpeningLedger(epoch);var subject=new object();
  guest.Open(subject,Semantic(message),RemoteStorySync.HashDialog(message.DialogPages),checked((byte)message.DialogPages!.Count),new[]{1});
  guest.Update(subject,0,new[]{1});return(guest,subject,guest.Sample(subject)[0]);
 }
 private static void Observe(MapStoryOpening request)
 {
  var presence=new PresenceState{HasMapStoryLifecycle=true,MapStoryLifecycleEntries=new[]{request}};
  MapStoryLifecycle.Observe(2,in presence,1);
 }
 private static void LateVisitor()
 {
  Reset();var source=CompleteHostStart();Check(Ledger.Sample(null).Length==0,"offline native start leaves the ordinary wire ledger empty");
  Online();var guest=Request(source);Observe(guest.Request);var completed=Ledger.Sample(null);
  Check(completed.Length==1&&completed[0].Finished&&completed[0].Participants.SequenceEqual(new[]{2}),"offline successful native start enrolls only its matching live visitor");
  var packet=new byte[MapStoryLifecycleCodec.MaxPayload+2];int at=0;Check(MapStoryLifecycleCodec.Write(packet,ref at,completed),"late visitor receipt retains unchanged bounded83 wire grammar");
  Check(MapStoryLifecycleCodec.TryRead(packet,2,at-2,out var received),"late visitor receipt round trips actual83 codec");
  guest.Guest.Observe(1,received);
  Check(guest.Guest.Resolve(guest.Subject,2,-1,true)==-1,"first native story page must appear before applying terminal receipt");
  // The guest's true native intro stands behind the missing shared reveal. It
  // still owns real native locks; no fake result or forced processor phase is used.
  PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];FFSNetwork.IsHost=false;
  var controller=new StoryController();var local=Start();Singleton<StoryController>.Instance=controller;
  controller.Begin(local);ActionProcessor.QueueUpAction(new(){ActionTypeID=(int)GameActionType.DebugWinScenario,PlayerID=1});
  VersionGuard.Versions[1]=NetProtocol.ModBuild;
  SharedWindowSpawnBridge.InitialAuthor=SharedWindowSpawnAuthority.IsInitialAuthor;
  SharedWindowSpawnBridge.TryInitialPose=MissingInitial;
  Check(!SharedWindowSpawnBridge.TryReveal(new ConvertedPanel()),"late native intro awaits absent already-closed host reveal while retaining real native locks");
  Check(ActionProcessor.Locked&&Choreographer.s_Choreographer!.Blockers==1&&!ActionProcessor.DrainOne(),"real native story lock keeps queued DebugWin untouched before completion");
  int target=guest.Guest.Resolve(guest.Subject,2,0,true);Check(target==2,"matched83 completion targets this native intro final page");
  controller.dialogBox.ShowLine(target);
  Check(!controller.window.IsOpen&&!ActionProcessor.Locked&&Choreographer.s_Choreographer!.Blockers==0,"native OnFinishShow and ShowNext release exact story and action locks");
  Check(SceneController.Instance!.GameLoadedAndClientReady&&Synchronizer.ClientReady==1&&Calls.ApplyState==1,"native completion sends the guest reliable ready message and reapplies controlled actor state");
  Check(ActionProcessor.DrainOne()&&ActionProcessor.ActionQueue.Count==0,"original native dispatcher now consumes queued DebugWin exactly once");
  Check(Choreographer.s_Choreographer!.NativeWins==1&&SceneController.Instance.SafeEnds==1&&UINewAdventureResultsManager.Implementation.NativeShows==1,"original ClientDebugWin and WinScenario coroutine reach original results Show seam");
  Check(UINewAdventureResultsManager.Implementation.ResultVisible&&UINewAdventureResultsManager.Implementation.Result==EResult.Win,"native results opening becomes available to the unchanged shared picture producer");
  UINewAdventureResultsManager.Implementation.FinishedAnimation!(()=>{});
  Check(Singleton<LevelEventsController>.Instance.Completes==1&&Singleton<LevelEventsController>.Instance.LastSuccess,"original win continuation retains native complete-level story callback");
  Check(guest.Guest.Resolve(guest.Subject,2,1,true)==-1&&!ActionProcessor.DrainOne(),"receipt replay cannot repeat story completion or win action");
  Check(!FFSNetwork.HasDesynchronized,"native continuation creates no action phase or desync exception");
 }
 private static void Refusals()
 {
  var cases=new (string Name,Action<StoryController.DialogInfo,MapStoryOpening> Change)[]
  {
   ("non-host",(_,_)=>FFSNetwork.IsHost=false),
   ("offline",(_,_)=>FFSNetwork.IsOnline=false),
   ("missing current native version receipt",(_,_)=>VersionGuard.SessionEpoch++),
   ("native entity reused",(_,_)=>PlayerRegistry.AllPlayers[1]=new(){PlayerID=2}),
   ("non-party sender",(_,_)=>PlayerRegistry.AllPlayers.RemoveAt(1)),
   ("incompatible build",(_,_)=>VersionGuard.Versions[2]--),
   ("wrong native local player",(_,_)=>PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1]),
   ("changed native scenario episode",(_,_)=>ScenarioManager.Scenario=new()),
   ("unloaded native scenario",(_,_)=>ScenarioManager.Scenario=null),
   ("left native scenario",(_,_)=>SaveData.Instance.Global.CurrentGameState=EGameState.Map),
   ("nonreciprocal request",(_,r)=>{r.Participants=Array.Empty<int>();r.TotalParticipants=0;}),
   ("already completed request",(_,r)=>r.Finished=true),
   ("bidirectional request",(_,r)=>r.Bidirectional=true),
   ("different saved run",(_,r)=>r.SemanticKey++),
   ("different original content",(_,r)=>r.ContentKey++),
   ("different page count",(_,r)=>r.PageCount++),
   ("bad request epoch",(_,r)=>r.Epoch=0)
  };
  foreach(var c in cases)
  {
   Reset();var source=CompleteHostStart();Online();var req=Request(source);c.Change(source,req.Request);Observe(req.Request);
   Check(!Ledger.Sample(null).Any(x=>x.Participants.Contains(2)),"late start refuses "+c.Name);
   Check(Ledger.Sample(null).Length==0,"refused late start creates no ordinary ledger occurrence: "+c.Name);
  }
  foreach(int trigger in new[]{1,2})
  {Reset();var source=CompleteHostStart(trigger:trigger);Online();Observe(Request(source).Request);Check(!Ledger.Sample(null).Any(x=>x.Participants.Contains(2)),"ordinary/end/room story never acquires retroactive start permission "+trigger);}
  Reset();var active=CompleteHostStart(complete:false);Online();Observe(Request(active).Request);
  Check(!Ledger.Sample(null).Any(x=>x.Finished),"unfinished native start is never a completed receipt");
  Reset();var old=CompleteHostStart();var next=CompleteHostStart(complete:false);Online();Observe(Request(old).Request);
  Check(!Ledger.Sample(null).Any(x=>x.Finished&&x.Participants.Contains(2)),"latest repeated start cannot inherit the previous completed occurrence");
  Reset();var staleSource=CompleteHostStart();Online();var stale=Request(staleSource,111);Observe(stale.Request);MapStoryLifecycle.ForgetPeer(2);
  Observe(stale.Request);Check(!Ledger.Sample(null).Any(x=>x.Participants.Contains(2)),"retired peer epoch cannot reenroll a stale request");
  Reset();var a=CompleteHostStart("INTRO_A");var b=CompleteHostStart("INTRO_B");Online();var ra=Request(a);
  Observe(ra.Request);ra.Guest.Finish(ra.Subject);
  var secondSubject=new object();ra.Guest.Open(secondSubject,Semantic(b),RemoteStorySync.HashDialog(b.DialogPages),2,new[]{1});
  ra.Guest.Update(secondSubject,0,new[]{1});Observe(ra.Guest.Sample(secondSubject)[0]);
  Check(Ledger.Sample(null).Count(x=>x.Finished&&x.Participants.Contains(2))==2,"distinct native start messages retain their own source occurrences");
  Reset();var noCheat=CompleteHostStart();VROptionsTab.CheatsEnabledNow=false;Online();Observe(Request(noCheat).Request);
  Check(Ledger.Sample(null).Any(x=>x.Finished&&x.Participants.Contains(2)),"ordinary progression recovery remains available with Cheats disabled");
  VROptionsTab.CheatsEnabledNow=true;
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 private static (WeakReference Subject,MapStoryOpening Request) CollectableStart(bool online)
 {
  FFSNetwork.IsOnline=online;
  var source=CompleteHostStart();var request=Request(source).Request;
  Singleton<StoryController>.Instance.m_CurrentMessage=null;
  return(new WeakReference(source),request);
 }
 private static void CompletedSubjectCollection()
 {
  foreach(bool online in new[]{false,true})
  {
   Reset();var completed=CollectableStart(online);
   GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
   Check(!completed.Subject.IsAlive,"native completed intro subject can be collected before later join");
   Online();Observe(completed.Request);
   Check(Ledger.Sample(null).Any(x=>x.Finished&&x.Participants.Contains(2)),"bounded completed start proof survives native subject GC "+online);
  }
 }
 private static void OrdinaryTraffic()
 {
  Reset();CompleteHostStart();Online();Calls.NativePlayerReads=0;
  var request=Request(Start("ORDINARY_OTHER_MESSAGE")).Request;
  for(int i=0;i<1000;i++)Observe(request);
  Check(Calls.NativePlayerReads==0,"ordinary83 traffic performs no native roster or player-reflection reads");
 }
 private static bool MissingInitial(SharedWindowKind kind,out UnityEngine.Vector3 p,out UnityEngine.Quaternion q,out float factor)
 {p=default;q=default;factor=1;return false;}
 private static void FlatHost()
 {
  Reset();Online();FFSNetwork.IsHost=false;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];
  VersionGuard.Versions[1]=0;
  SharedWindowSpawnBridge.InitialAuthor=SharedWindowSpawnAuthority.IsInitialAuthor;
  SharedWindowSpawnBridge.TryInitialPose=MissingInitial;
  var panel=new ConvertedPanel();var story=Singleton<StoryController>.Instance;story.Begin(Start());
  Check(SharedWindowSpawnAuthority.AuthorId==2&&SharedWindowSpawnBridge.TryReveal(panel)&&panel.SharedSpawnPoseReady,
   "unmodded flat host retains one usable native VR intro via the existing surrogate author");
  Check(story.window.IsOpen&&story.IsVisible,"unmodded host keeps the actual local story available to read");
  story.Complete();Check(!ActionProcessor.Locked&&SceneController.Instance!.GameLoadedAndClientReady,
   "unmodded host local reader continues through the original native callback without83");
 }
 private static MapStoryOpening[] Sample(int local)
 {
  var state=new PresenceState();MapStoryLifecycle.Sample(ref state,local);
  return state.MapStoryLifecycleEntries??Array.Empty<MapStoryOpening>();
 }
 private static void SessionBoundaries()
 {
  // The host retains the exact successful intro across an online/offline edge,
  // but old sender tokens and bindings never survive the native session epoch.
  Reset();Online();var message=CompleteHostStart();var old=Sample(1).Single();
  VersionGuard.SessionEpoch++;Online();Check(!MapStoryLifecycle.SendDue&&Sample(1).Length==0,
   "new native session drops ordinary story ledger and move bindings");
  Observe(Request(message,301).Request);var current=Sample(1);
  Check(current.Length==1&&current[0].Epoch!=old.Epoch&&current[0].Finished&&current[0].Participants.Contains(2),
   "new session retains only current native episode completed proof under a fresh ledger epoch");

  // Same-process guest reconnects with the same native playerID and same host
  // source token after consuming it once. Only native session retirement clears
  // its prior one-to-one binding; a real new request authorizes the current peer.
  Reset();Online();FFSNetwork.IsHost=false;PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];VersionGuard.Versions[1]=NetProtocol.ModBuild;
  var first=Start();var story=Singleton<StoryController>.Instance;story.Begin(first);MapStoryLifecycle.NativeOpened(story,first);
  var firstRequest=Sample(2).First(x=>!x.Finished);var host=new MapStoryOpeningLedger(500);var hostSubject=new object();
  host.Open(hostSubject,Semantic(first),RemoteStorySync.HashDialog(first.DialogPages),2,new[]{2});host.Update(hostSubject,1,new[]{2});host.Finish(hostSubject);
  host.Observe(2,new[]{firstRequest});var hostDone=host.Sample(null);
  var presence=new PresenceState{HasMapStoryLifecycle=true,MapStoryLifecycleEntries=hostDone};MapStoryLifecycle.Observe(1,in presence,2);MapStoryLifecycle.Resolve();MapStoryLifecycle.NativeFinished(first);
  Check(!story.window.IsOpen&&!ActionProcessor.Locked,"first native guest opening consumed the exact host token normally");
  host.ForgetPeer(2);VersionGuard.SessionEpoch++;ScenarioManager.Scenario=new();
  PlayerRegistry.AllPlayers[1]=new(){PlayerID=2};PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];
  SceneController.Instance=new();Choreographer.s_Choreographer=new();story=new();Singleton<StoryController>.Instance=story;
  var rejoin=Start();story.Begin(rejoin);MapStoryLifecycle.NativeOpened(story,rejoin);var nextRequest=Sample(2).First(x=>!x.Finished);
  Check(nextRequest.Epoch!=firstRequest.Epoch,"same-process reconnect publishes a fresh native story epoch");
  host.Observe(2,new[]{nextRequest});host.Observe(2,new[]{firstRequest});
  Check(!host.IsCurrentLiveRequest(2,1,firstRequest),"late previous-session request cannot claim the new native peer opening");
  Check(host.EnrollCompletedScenarioStart(hostDone[0].Token,2,1,nextRequest),"fresh reciprocal request reenrolls same-ID native guest into current host opening");
  var newDone=host.Sample(null);Check(newDone[0].Epoch==hostDone[0].Epoch&&newDone[0].Token==hostDone[0].Token,
   "rejoining reader can consume the same still-current host occurrence once for its new session");
  presence.MapStoryLifecycleEntries=newDone;MapStoryLifecycle.Observe(1,in presence,2);MapStoryLifecycle.Resolve();
  Check(!story.window.IsOpen&&!ActionProcessor.Locked&&SceneController.Instance.GameLoadedAndClientReady,
   "same-ID reconnect completes native intro and ready state without replaying old guest bindings");

  Reset();var pending=CompleteHostStart(complete:false);VersionGuard.SessionEpoch++;Online();Observe(Request(pending).Request);
  Check(Sample(1).All(x=>!x.Finished),"session edge never promotes an unfinished intro into completed proof");
  var pendingController=Singleton<StoryController>.Instance;
  Check(pendingController.window.Shows==1&&pendingController.window.Hides==0&&pendingController.dialogBox.ShowLineCalls==0
   &&pendingController.IsVisible&&ActionProcessor.Locked&&Choreographer.s_Choreographer!.Blockers==1,
   "unfinished native intro remains untouched during ledger session retirement");
  var active=new PresenceState();MapStoryLifecycle.Sample(ref active,1);
  Check(active.MapStoryLifecycleEntries?.Any(x=>!x.Finished)==true,"still-open native start registers normally after session retirement");
  Reset();pending=CompleteHostStart(complete:false);VersionGuard.SessionEpoch++;
  Singleton<StoryController>.Instance.Complete();MapStoryLifecycle.NativeFinished(pending);Online();Observe(Request(pending).Request);
  Check(Sample(1).Any(x=>x.Finished&&x.Participants.Contains(2)),"native success exactly on session boundary retains its proven completed start");
  foreach(bool onlineAtFinish in new[]{false,true})
  {
   Reset();pending=CompleteHostStart(complete:false);VersionGuard.SessionEpoch++;
   _=MapStoryLifecycle.SendDue; // Read-only query may be the first epoch observer.
   if(onlineAtFinish)Online();
   Singleton<StoryController>.Instance.Complete();MapStoryLifecycle.NativeFinished(pending);
   Online();Observe(Request(pending).Request);
   Check(Sample(1).Any(x=>x.Finished&&x.Participants.Contains(2)),
    "query-before-finish retains exact native start success "+onlineAtFinish);
  }
  Reset();pending=CompleteHostStart(complete:false);var successor=Start("ROOM_SUCCESSOR",2);
  var successorController=Singleton<StoryController>.Instance;successorController.Queue(successor);
  pending.OnClosedAction=()=>{VersionGuard.SessionEpoch++;_=MapStoryLifecycle.SendDue;};
  successorController.Complete(); // Original ShowNext opens B before A's Harmony success postfix.
  MapStoryLifecycle.NativeOpened(successorController,successor);MapStoryLifecycle.NativeFinished(pending);
  Check(ReferenceEquals(successorController.m_CurrentMessage,successor)&&successorController.IsVisible&&ActionProcessor.Locked,
   "synchronous genuine successor stays open and blocked before predecessor completion publication");
  Online();Observe(Request(pending).Request);
  Check(Sample(1).Any(x=>x.Finished&&x.Participants.Contains(2)&&x.SemanticKey==Semantic(pending)),
   "query retirement and native successor opening preserve exact predecessor start success");
  Check(successorController.IsVisible&&ActionProcessor.Locked&&Choreographer.s_Choreographer!.Blockers==1,
   "completed start enrollment never confirms or releases the genuine successor story");
  Reset();var replacementSource=CompleteHostStart();Online();PlayerRegistry.AllPlayers[1]=new(){PlayerID=2};NativeCheatQueue.NoteVersionReceipt(2,NetProtocol.ModBuild);
  Observe(Request(replacementSource).Request);Check(Sample(1).Any(x=>x.Finished&&x.Participants.Contains(2)),
   "new same-ID native entity requires and accepts its own fresh version receipt");
  Reset();var throwing=Start();throwing.OnClosedAction=()=>throw new System.IO.IOException("native callback fault");
  var failedController=Singleton<StoryController>.Instance;failedController.Begin(throwing);MapStoryLifecycle.NativeOpened(failedController,throwing);
  try { failedController.Complete();MapStoryLifecycle.NativeFinished(throwing); } catch(System.IO.IOException) { }
  Online();Observe(Request(throwing).Request);Check(Sample(1).All(x=>!x.Finished),"thrown original native callback never becomes successful start completion proof");

  Reset();Online();CompleteHostStart(complete:false);Sample(1);
  VersionGuard.SessionEpoch++;PlayerRegistry.AllPlayers[1]=new(){PlayerID=3};PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[1];FFSNetwork.IsHost=false;
  var unconfirmed=Singleton<StoryController>.Instance;int pageCalls=unconfirmed.dialogBox.ShowLineCalls;
  MapStoryLifecycle.Resolve();Check((int)typeof(MapStoryLifecycle).GetField("_localPlayerId",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)! == 0,
   "new session drops prior native local player id before current packet admission");
  Check(!MapStoryLifecycle.MatchesPose(false,1,500,1)&&unconfirmed.window.IsOpen&&unconfirmed.dialogBox.ShowLineCalls==pageCalls,
   "resolve and pose queries defer before the new session supplies its native local id");
  MapStoryLifecycle.Sample(ref active,3);
  Check((int)typeof(MapStoryLifecycle).GetField("_localPlayerId",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)! == 3,
   "fresh native packet context adopts a changed positive local id after session retirement");
 }
 private static void Main()
 {LateVisitor();Refusals();CompletedSubjectCollection();OrdinaryTraffic();FlatHost();SessionBoundaries();System.Console.WriteLine($"Scenario start visitor runtime: {_assertions} assertions passed.");}
}
