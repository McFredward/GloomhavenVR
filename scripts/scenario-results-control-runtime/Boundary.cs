using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Declared ports: native player/entity/model, original Results callback permission,
// transport installation/socket, existing unrelated request handlers and log sink.
// The production request, reliable sender, native receipt helper, whole receive
// prefix and whole module Init/Shutdown bodies are imported unchanged by the runner.
internal static class FFSNetwork
{ public static bool IsOnline=true,IsHost=true,IsClient,HasDesynchronized; }
namespace FFSNet
{
 internal enum GameActionType { NONE }
 internal enum ActionPhaseType { NONE }
 internal sealed class NetworkPlayer
 { public int PlayerID { get; set; } public bool HasCharacters { get; set; } }
 internal static class PlayerRegistry
 {
  public static List<NetworkPlayer> AllPlayers=new();
  public static NetworkPlayer? MyPlayer { get; set; }
  public static NetworkPlayer? HostPlayer { get; set; }
  public static int HostPlayerID => HostPlayer?.PlayerID??0;
  public static bool OtherClientsAreJoining,LoadingInFromJoiningClient,IsSwitchingCharacter;
  public static ICollection? JoiningPlayers { get; set; }
  public static ICollection? ConnectingUsers { get; set; }
 }
 internal sealed class GameAction
 {
  public int ActionTypeID { get; set; } public int PlayerID { get; set; }
  public int TargetPlayerID { get; set; } public int DataInt { get; set; }
  public int DataInt2 { get; set; } public bool SupplementaryDataBoolean { get; set; }
  public object? SupplementaryDataToken { get; set; }
 }
 internal static class Synchronizer
 {
  internal static int Sends; internal static GameAction? Last; internal static bool Unreliable,HostOnly,Throw;
  public static void SendSideAction(GameActionType actionType,object? token,bool canBeUnreliable,bool sendToHostOnly,
   int targetPlayerID,int dataInt,int dataInt2,bool dataBoolean)
  {
   if(Throw)throw new InvalidOperationException("socket failure");
   ++Sends;Unreliable=canBeUnreliable;HostOnly=sendToHostOnly;
   Last=new(){ActionTypeID=(int)actionType,PlayerID=PlayerRegistry.MyPlayer!.PlayerID,
    TargetPlayerID=targetPlayerID,DataInt=dataInt,DataInt2=dataInt2,SupplementaryDataBoolean=dataBoolean,
    SupplementaryDataToken=token};
  }
  public static void AutoExecuteServerAuthGameAction() { throw new Exception("unexpected queue use"); }
 }
}
namespace HarmonyLib
{
 internal sealed class Harmony { internal void PatchAll(Type t) { } }
 internal static class AccessTools
 { internal static bool Missing;internal static MethodInfo? Method(string name)=>Missing?null:typeof(FFSNet.Synchronizer).GetMethod("SendSideAction"); }
}
namespace UnityEngine
{
 internal enum HideFlags { HideAndDontSave }
 internal sealed class GameObject
 { internal GameObject(string name) { } internal HideFlags hideFlags;internal T AddComponent<T>() where T:new()=>new(); }
 internal static class Object { internal static void Destroy(object o) { } internal static void DontDestroyOnLoad(object o) { } }
}
namespace GloomhavenVR.Core
{
 internal static class VRSession { internal static HarmonyLib.Harmony? Harmony=new(); }
 internal static class VRLog
 { internal static void Note(string s,string m) { } internal static void Info(string s,string m) { }
   internal static void Warn(string s,string m) { } internal static void Error(string s,string m) { }
   internal static void Alert(string s,string m) { } }
}
namespace GloomhavenVR.WorldUI
{
 internal static class MultiplayerCheats { internal const int NativeAction=127; }
 internal static class ScenarioResultsNative
 {
  internal const byte ContinueReady=1,ReturnToMap=2,RetryScenario=3;
  internal static Func<byte,uint,bool>? SubmitContinuation;
  internal static bool Permission=true,Spent,Throw;internal static int Callbacks,Attempts;internal static byte Last;
  internal static bool TryInvokeContinuation(byte operation)
  { ++Attempts;if(Throw)throw new Exception("callback boundary failure");if(!Permission||Spent)return false;
    Spent=true;Last=operation;++Callbacks;return true; }
 }
}
namespace GloomhavenVR.Net.Desync
{
 internal static class DispatchGuard
 { internal static void Run(string n,Action a){try{a();}catch{}}
   internal static bool Run(string n,Func<bool> a,bool onThrow){try{return a();}catch{return onThrow;}} }
 internal static class DesyncWatch { internal static void Install() { } internal static void Uninstall() { } }
 internal sealed class DesyncWatchDriver { }
}
namespace GloomhavenVR.Net
{
 internal static class VersionGuard { internal static int SessionEpoch; }
 internal static class NetSession { internal static bool FlatNetMode; }
 internal static class ScenarioResultsPose { internal static uint Key;internal static int Author=1;internal static bool NativeVisible=true; }
 internal static class SharedWindowSpawnAuthority { internal static void Bind(Func<bool> f) { } }
 internal static class WorldAnchor { internal static object Instance=new(); }
 internal sealed class NetAvatarDriver { internal void Configure(INetTransport t,object a) { } }
 internal static class HeadMaskLibrary { internal static void Reset() { } }
 internal interface INetTransport { bool IsOnline { get; } void Install();void Uninstall(); }
 internal sealed class CustomToken { public byte[]? Data { get; set; } }
 internal sealed partial class FfsNetTransport:INetTransport
 {
  private static FfsNetTransport? _active; private bool _installed,_degraded;
  internal static bool InstallSucceeds=true;internal static int Payloads;
  private PropertyInfo? _gaActionTypeId=typeof(FFSNet.GameAction).GetProperty("ActionTypeID"),
   _gaSupplementaryToken=typeof(FFSNet.GameAction).GetProperty("SupplementaryDataToken"),
   _gaPlayerId=typeof(FFSNet.GameAction).GetProperty("PlayerID"),_customDataProp=typeof(CustomToken).GetProperty("Data");
  internal FfsNetTransport(HarmonyLib.Harmony h) { }
  public bool IsOnline=>FFSNetwork.IsOnline;
  public void Install(){_installed=InstallSucceeds;_degraded=!InstallSucceeds;if(_installed)_active=this;}
  public void Uninstall(){_installed=false;if(_active==this)_active=null;}
  internal static bool Dispatch(object action)=>ReceivePrefix(action);
  private void RaiseReceived(int sender,byte[] b,int n)=>++Payloads;
 }
 internal sealed partial class NetModule
 {
  internal static Switch Enabled=new();private string Name=>"Net";
  private UnityEngine.GameObject? _watchGo,_driverGo;private INetTransport? _transport;private NetAvatarDriver? _driver;
  internal static void BindConfig() { }
 }
 internal sealed class Switch { internal bool Value=true; }
 internal static class EncounterChoice { internal static bool TryHandleSideAction(object a)=>a is FFSNet.GameAction g&&g.ActionTypeID==99; }
 internal static class EnemyInfoContinue
 { internal static bool TryHandleSideAction(object a)=>a is FFSNet.GameAction g&&g.DataInt==1&&g.SupplementaryDataBoolean; }
 internal static class AssignmentChoice
 { internal static bool TryHandleSideAction(object a)=>a is FFSNet.GameAction g&&g.DataInt==2&&g.SupplementaryDataBoolean; }
 internal sealed class ClientButtonLocker_TryLockButton_Patch { }
 internal sealed class UIEventPanel_ContinueEvent_Patch { }
 internal sealed class UIEventPanel_CompleteEvent_Patch { }
 internal sealed class UIEventPanel_ClientContinueRoadEvent_Patch { }
 internal sealed class UIDistributeReward_Distribute_Patch { }
 internal sealed class UIDistributePointsPopup_Hide_AssignmentPatch { }
 internal sealed class UIDistributePointsSlot_EnableAddPoints_Patch { }
 internal sealed class UIDistributePointsSlot_EnableRemovePoints_Patch { }
 internal sealed class UIDistributeReward_SetButtonInteractable_Patch { }
 internal sealed class UIDistributePointsPopup_RefreshAssignedPoints_Patch { }
 internal sealed class UIDistributePointsSlot_AddPoint_Patch { }
 internal sealed class UIDistributePointsSlot_RemovePoint_Patch { }
 internal sealed class UIDistributeReward_OnConfirmClick_Patch { }
}
