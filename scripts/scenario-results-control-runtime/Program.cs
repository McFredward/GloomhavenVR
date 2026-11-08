using System;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;

internal static class Program
{
 private static int _assertions;
 private static void Check(bool condition,string description)
 { ++_assertions;if(!condition)throw new Exception("ASSERT: "+description); }
 private static void Reset(bool host=true,uint opening=0xF0000042)
 {
  ++VersionGuard.SessionEpoch;
  PlayerRegistry.AllPlayers=new(){new(){PlayerID=1},new(){PlayerID=2,HasCharacters=false}};
  PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[host?0:1];PlayerRegistry.HostPlayer=PlayerRegistry.AllPlayers[0];
  PlayerRegistry.JoiningPlayers=null;PlayerRegistry.ConnectingUsers=null;
  FFSNetwork.IsOnline=true;FFSNetwork.IsHost=host;FFSNetwork.IsClient=!host;FFSNetwork.HasDesynchronized=false;
  NetSession.FlatNetMode=false;ScenarioResultsPose.Key=opening;ScenarioResultsPose.Author=1;ScenarioResultsPose.NativeVisible=true;
  NativeCheatQueue.NoteVersionReceipt(host?2:1,NetProtocol.ModBuild);
  ScenarioResultsNative.Permission=true;ScenarioResultsNative.Spent=false;ScenarioResultsNative.Throw=false;
  ScenarioResultsNative.Callbacks=ScenarioResultsNative.Attempts=0;ScenarioResultsNative.Last=0;
  ScenarioResultsNative.SubmitContinuation=null;Synchronizer.Sends=0;Synchronizer.Last=null;Synchronizer.Throw=false;
  FfsNetTransport.Payloads=0;FfsNetTransport.InstallSucceeds=true;NetModule.Enabled.Value=true;
  GloomhavenVR.Core.VRSession.Harmony=new();
 }
 private static GameAction Action(int tag=3,uint? key=null)=>new()
 { ActionTypeID=NetProtocol.SentinelActionTypeId,TargetPlayerID=NetProtocol.SentinelTargetPlayerId,
   SupplementaryDataBoolean=true,DataInt=tag,DataInt2=unchecked((int)(key??ScenarioResultsPose.Key)),PlayerID=2 };
 private static void HostRequest(GameAction? a=null)
 { Check(ScenarioResultsControlRequests.TryHandleSideAction(a??Action()),"owned marker is consumed"); }
 private static void SendAndDispatch()
 {
  foreach(byte operation in new byte[]{1,2,3})
  {
   Reset(false);uint key=ScenarioResultsPose.Key;
   Check(ScenarioResultsControlRequests.Request(operation,key),"valid visitor request sends without a controlled character");
   GameAction sent=Synchronizer.Last!;
   Check(Synchronizer.Sends==1&&!Synchronizer.Unreliable&&Synchronizer.HostOnly,"uses reliable host-only native send");
   Check(sent.SupplementaryDataToken==null&&sent.ActionTypeID==60123&&sent.TargetPlayerID==int.MaxValue&&sent.SupplementaryDataBoolean,"complete native sentinel without token");
   Check(sent.DataInt==operation+2&&unchecked((uint)sent.DataInt2)==key,"entire unsigned opening survives native int field");
   Reset();var transport=new FfsNetTransport(new());transport.Install();
   Check(!FfsNetTransport.Dispatch(sent),"actual receive prefix consumes request");
   Check(ScenarioResultsNative.Callbacks==1&&ScenarioResultsNative.Last==operation,"current host reaches original continuation boundary once");
   Check(FfsNetTransport.Payloads==0,"control never reaches presentation decode");
   Check(!FfsNetTransport.Dispatch(sent)&&ScenarioResultsNative.Callbacks==1,"duplicate invokes no second original callback");
  }
 }
 private static void Refusals()
 {
  (string,Action)[] deny={
   ("offline",()=>FFSNetwork.IsOnline=false),("native nonhost",()=>FFSNetwork.IsHost=false),
   ("native client",()=>FFSNetwork.IsClient=true),("flat mode",()=>NetSession.FlatNetMode=true),
   ("desync",()=>FFSNetwork.HasDesynchronized=true),("closed native opening",()=>ScenarioResultsPose.NativeVisible=false),
   ("wrong common author",()=>ScenarioResultsPose.Author=2),
   ("wrong native local host",()=>{PlayerRegistry.AllPlayers.Add(new(){PlayerID=3});PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers[2];}),
   ("missing current receipt",()=>NativeCheatQueue.NoteVersionReceipt(2,0)),
   ("departed peer",()=>PlayerRegistry.AllPlayers.RemoveAt(1)),
   ("reused native ID",()=>PlayerRegistry.AllPlayers[1]=new(){PlayerID=2}),
   ("session retired",()=>++VersionGuard.SessionEpoch),
   ("native control permission",()=>ScenarioResultsNative.Permission=false)
  };
  foreach(var (name,change) in deny)
  { Reset();change();HostRequest();Check(ScenarioResultsNative.Callbacks==0,"request refuses "+name); }
  Reset();HostRequest(Action(key:ScenarioResultsPose.Key-1));Check(ScenarioResultsNative.Callbacks==0,"request refuses stale opening");
  Reset();HostRequest(Action(key:0));Check(ScenarioResultsNative.Callbacks==0,"request refuses zero opening");
  Reset();var self=Action();self.PlayerID=1;HostRequest(self);Check(ScenarioResultsNative.Callbacks==0,"request refuses local host sender");
  Reset();var unknown=Action();unknown.PlayerID=0;HostRequest(unknown);Check(ScenarioResultsNative.Callbacks==0,"request refuses unknown sender");
  Reset();ScenarioResultsNative.Permission=false;HostRequest();ScenarioResultsNative.Permission=true;HostRequest();
  Check(ScenarioResultsNative.Callbacks==1,"refused press remains retryable when native permission returns");
  Reset();ScenarioResultsNative.Throw=true;HostRequest();ScenarioResultsNative.Throw=false;HostRequest();
  Check(ScenarioResultsNative.Callbacks==1,"callback exception is contained and leaves retry available");
  Reset();PlayerRegistry.AllPlayers[1]=new(){PlayerID=2};NativeCheatQueue.NoteVersionReceipt(2,NetProtocol.ModBuild);HostRequest();
  Check(ScenarioResultsNative.Callbacks==1,"fresh receipt restores a rejoined native entity");
 }
 private static void MarkerAndExistingDispatch()
 {
  Reset();Check(!ScenarioResultsControlRequests.TryHandleSideAction(null),"null is not owned");
  foreach(var (name,change) in new (string,Action<GameAction>)[]{
   ("wrong action type",a=>a.ActionTypeID=42),("wrong target",a=>a.TargetPlayerID=0),
   ("unmarked action",a=>a.SupplementaryDataBoolean=false),("unknown operation",a=>a.DataInt=6),
   ("enemy marker",a=>a.DataInt=1),("assignment marker",a=>a.DataInt=2)})
  { var a=Action();change(a);Check(!ScenarioResultsControlRequests.TryHandleSideAction(a),"marker preserves "+name); }
  Check(ScenarioResultsNative.Callbacks==0,"foreign markers never invoke Results callback");
  var transport=new FfsNetTransport(new());transport.Install();
  Check(FfsNetTransport.Dispatch(new GameAction{ActionTypeID=42}),"real native action reaches vanilla");
  Check(!FfsNetTransport.Dispatch(new GameAction{ActionTypeID=99}),"existing encounter request remains consumed");
  Check(!FfsNetTransport.Dispatch(Action(1))&&!FfsNetTransport.Dispatch(Action(2)),"existing sentinel requests remain consumed");
  var payload=Action(0);payload.SupplementaryDataBoolean=false;payload.SupplementaryDataToken=new CustomToken{Data=new byte[]{1}};
  Check(!FfsNetTransport.Dispatch(payload)&&FfsNetTransport.Payloads==1,"existing presentation payload reaches unchanged decoder seam");
 }
 private static void SendRefusals()
 {
  foreach(var (name,change) in new (string,Action)[]{
   ("unmodded host",()=>NativeCheatQueue.NoteVersionReceipt(1,0)),
   ("reused host ID",()=>PlayerRegistry.AllPlayers[0]=new(){PlayerID=1}),
   ("old session",()=>++VersionGuard.SessionEpoch),("flat mode",()=>NetSession.FlatNetMode=true),
   ("desync",()=>FFSNetwork.HasDesynchronized=true),("wrong author",()=>ScenarioResultsPose.Author=2),
   ("closed native window",()=>ScenarioResultsPose.NativeVisible=false),("no local native identity",()=>PlayerRegistry.MyPlayer=null)})
  { Reset(false);change();Check(!ScenarioResultsControlRequests.Request(1,ScenarioResultsPose.Key)&&Synchronizer.Sends==0,"send refuses "+name); }
  Reset();Check(!ScenarioResultsControlRequests.Request(1,ScenarioResultsPose.Key),"host keeps original native controls");
  Reset(false);Check(!ScenarioResultsControlRequests.Request(0,ScenarioResultsPose.Key),"send refuses unknown operation");
  Check(!ScenarioResultsControlRequests.Request(1,0)&&!ScenarioResultsControlRequests.Request(1,ScenarioResultsPose.Key-1),"send refuses zero or stale opening");
  Synchronizer.Throw=true;Check(!ScenarioResultsControlRequests.Request(1,ScenarioResultsPose.Key),"send exception does not escape");
  Synchronizer.Throw=false;Check(ScenarioResultsControlRequests.Request(1,ScenarioResultsPose.Key),"send failure leaves press retryable");
 }
 private static void Mailbox()
 {
  Reset();var module=new NetModule();module.Init();
  Check(ScenarioResultsNative.SubmitContinuation!=null,"enabled installed receive hook binds mailbox");
  module.Shutdown();Check(ScenarioResultsNative.SubmitContinuation==null,"shutdown clears owned mailbox");
  Reset();NetModule.Enabled.Value=false;new NetModule().Init();Check(ScenarioResultsNative.SubmitContinuation==null,"disabled net does not bind mailbox");
  Reset();GloomhavenVR.Core.VRSession.Harmony=null;new NetModule().Init();Check(ScenarioResultsNative.SubmitContinuation==null,"missing receive patcher does not bind mailbox");
  Reset();FfsNetTransport.InstallSucceeds=false;new NetModule().Init();Check(ScenarioResultsNative.SubmitContinuation==null,"failed receive installation does not bind mailbox");
  Reset();module=new NetModule();module.Init();Func<byte,uint,bool> replacement=(_,_)=>true;
  ScenarioResultsNative.SubmitContinuation=replacement;module.Shutdown();
  Check(ScenarioResultsNative.SubmitContinuation==replacement,"shutdown preserves a replaced mailbox");
  Reset();module=new NetModule();module.Init();var successor=new NetModule();successor.Init();
  replacement=ScenarioResultsNative.SubmitContinuation!;module.Shutdown();
  Check(ReferenceEquals(ScenarioResultsNative.SubmitContinuation,replacement),"old module cannot clear successor mailbox");
  successor.Shutdown();Check(ScenarioResultsNative.SubmitContinuation==null,"successor can clear its own mailbox");
 }
 private static int Main()
 { try{SendAndDispatch();Refusals();MarkerAndExistingDispatch();SendRefusals();Mailbox();Console.WriteLine($"{_assertions} assertions passed.");return 0;}
   catch(Exception e){Console.WriteLine(e);return 1;} }
}
