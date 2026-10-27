using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.WorldUI.MapRoom;

// The full production helper is compiled unchanged. This deterministic boundary supplies
// native singleton/permission/lifecycle events; it is not a Photon or headset renderer.
internal static class Singleton<T> where T:class
{
    private static T _instance=null!;
    internal static T Instance {get{Faults.Read("singleton");return _instance;}set=>_instance=value;}
    internal static bool IsInitialized=>_instance!=null;
}
internal enum EReadyUpToggleStates { NotSet,Quests,CityEvents,Reward,Retirement,TownRecords }
internal sealed class UIReadyToggle
{
    internal enum EReadyUpType { Participant,Player }
    internal EReadyUpToggleStates readyUpToggleState;
    internal bool _requestVisible,_allPlayersReady;
    internal readonly HashSet<object> _visibilityRequests=new();
    private bool _interactable;
    internal bool IsInteractable {get{Faults.Read("permission");return _interactable;}set=>_interactable=value;}
    internal bool ToggleInteractable;
    internal readonly UnityEngine.UI.UIWindow Window=new();
    internal int ShowCount,HideCount,InputVotes,AutomaticVotes;
    internal bool NativeShouldBeVisible=>_requestVisible&&_interactable&&!_allPlayersReady&&_visibilityRequests.Count==0;
    internal bool ShouldBeVisible
    {
        get { object?[] args={this,NativeShouldBeVisible};Hooks.Invoke("ObserveDisabledCityOffer",args);return (bool)args[1]!; }
    }
    internal T GetComponent<T>() where T:class {Faults.Read("component");return (Window as T)!;}
    internal void UpdateVisiblity() { Faults.Read("update-visibility");bool show=ShouldBeVisible;if(show!=Window.IsOpen) { Window.IsOpen=show;if(show)ShowCount++;else HideCount++; } }
    internal void Initialize(EReadyUpToggleStates state)
    { Hooks.Invoke("BeforeReadyInitialize",false,EReadyUpType.Participant,state);readyUpToggleState=state;IsInteractable=ToggleInteractable=false;_allPlayersReady=false;UpdateVisiblity(); }
    internal void Reset()
    { Hooks.Invoke("BeforeReadyReset");_requestVisible=false;IsInteractable=ToggleInteractable=false;UpdateVisiblity(); }
    internal bool InputToggle()
    { bool accepted=(bool)Hooks.Invoke("PreserveNativeInputPermission",this)!;if(accepted)InputVotes++;return accepted; }
}
internal sealed class UIMapMultiplayerController
{
    internal EReadyUpToggleStates m_State;
    internal int ShowRequests;
    internal void ToggleReadyUpUI(bool show,EReadyUpToggleStates state)
    { Faults.Read("show-request");m_State=state;var t=Singleton<UIReadyToggle>.Instance;t._requestVisible=show;t.UpdateVisiblity();if(show)ShowRequests++; }
    internal void OnSelectedCityEvent() { Singleton<UIReadyToggle>.Instance.AutomaticVotes++; }
    internal void ConfirmCityEvent() { }
    internal void ResetUI() { }
    internal void ClearHostSelectedCityEvent() { }
}
internal sealed class MapChoreographer
{
    internal bool Participant=true;
    internal int PermissionChecks;
    internal Action? DuringPermissionRefresh;
    internal void DeterminePlayerToggleInteractability()
    { PermissionChecks++;Faults.Read("permission-refresh");var t=Singleton<UIReadyToggle>.Instance;t.IsInteractable=t.ToggleInteractable=Participant;t.UpdateVisiblity();DuringPermissionRefresh?.Invoke(); }
    internal void ProxySelectedCityEvent() { }
}
internal sealed class UIEventPanel { internal void StartEvent() { } }
namespace UnityEngine.UI { internal sealed class UIWindow { internal bool IsOpen; } }
namespace FFSNet
{
    internal static class FFSNetwork
    {
        private static bool _online=true,_host=true;
        internal static bool IsOnline {get{Faults.Read("online");return _online;}set=>_online=value;}
        internal static bool IsHost {get{Faults.Read("host");return _host;}set=>_host=value;}
        internal static bool IsClient=>IsOnline&&!IsHost;
    }
    internal enum ActionPhaseType { MapHQ,MapAtLinkedScenario,MapLoadoutScreen,ScenarioEnded }
    internal static class ActionProcessor {private static ActionPhaseType _phase;internal static ActionPhaseType CurrentPhase {get{Faults.Read("action-phase");return _phase;}set=>_phase=value;} }
    internal enum GameActionType { SelectCityEvent=7,SelectQuest=8 }
    internal sealed class GameAction { internal int ActionID,ActionTypeID; }
    internal sealed class PlayerState {private int _id;public int LatestProcessedActionID {get{Faults.Read("action-counter");return _id;}set=>_id=value;} }
    internal sealed class NetworkPlayer { public PlayerState state {get;}=new(); }
    internal static class PlayerRegistry {private static NetworkPlayer? _player=new();internal static NetworkPlayer? MyPlayer {get{Faults.Read("my-player");return _player;}set=>_player=value;} }
}
namespace MapRuleLibrary.PhaseManager { internal enum EMapPhaseType { InHQ,AtLinkedScenario,MapEvent,Travel } }
namespace MapRuleLibrary.Adventure
{
    internal sealed class MapState {private MapRuleLibrary.PhaseManager.EMapPhaseType _phase;internal MapRuleLibrary.PhaseManager.EMapPhaseType CurrentMapPhaseType {get{Faults.Read("map-phase");return _phase;}set=>_phase=value;} }
    internal static class AdventureState {private static MapState? _map=new();internal static MapState? MapState {get{Faults.Read("map-state");return _map;}set=>_map=value;} }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]internal sealed class HarmonyPatch:Attribute
    { internal readonly Type? Target;internal readonly string? Name;public HarmonyPatch(){} public HarmonyPatch(Type type,string name){Target=type;Name=name;}public HarmonyPatch(Type type,string name,MethodType method){Target=type;Name=name;} }
    internal sealed class HarmonyPostfix:Attribute { }
    internal sealed class HarmonyPrefix:Attribute { }
    internal enum MethodType { Getter }
    internal sealed class Harmony {internal int Installed;internal void PatchAll(Type type)=>Installed++;}
    internal static class AccessTools {internal static PropertyInfo? Property(Type type,string name)=>type.GetProperty(name);}
}
namespace GloomhavenVR.Core
{
    internal static class VRSession {internal static bool IsRunning=true;internal static HarmonyLib.Harmony? Harmony=new();}
    internal static class VRLog
    {
        internal static int WarningAttempts;
        internal static void Info(string scope,string text)=>Faults.Read("log-info");
        internal static void Warn(string scope,string text){WarningAttempts++;Faults.Read("log-warn");Warnings.Add(text);}
        internal static readonly List<string> Warnings=new();
    }
}
namespace GloomhavenVR.Net
{
    internal static class SharedMapRunIdentity
    { private static uint _key=8193;internal static uint Key {get{Faults.Read("map-run");return _key;}set=>_key=value;}internal static uint Add(uint hash,string value){unchecked{foreach(char c in value)hash=(hash^c)*16777619u;return (hash^0xffffu)*16777619u;}} }
}
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver {private static bool _active=true;internal static bool Active {get{Faults.Read("room-active");return _active;}set=>_active=value;} } }
internal static class Faults
{
    private static readonly HashSet<string> Armed=new();
    internal static int Triggered;
    internal static void Reset(){Armed.Clear();Triggered=0;}
    internal static void Arm(params string[] faults){Armed.Clear();foreach(string fault in faults)Armed.Add(fault);Triggered=0;}
    internal static void Read(string name){if(Armed.Remove(name)){Triggered++;throw new InvalidOperationException("Injected native failure: "+name);}}
}
internal static class Hooks
{
    private static readonly Type Seam=typeof(MapCityEventReadyUp).GetNestedType("CityProposalSeams",BindingFlags.NonPublic)!;
    internal static object? Invoke(string method,params object?[] args)=>Seam.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
    internal static bool HasExactlyOneTarget(string method,Type target,string name)
    {
        object[] attributes=Seam.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch),false);
        return attributes.Length==1&&attributes[0] is HarmonyLib.HarmonyPatch patch&&patch.Target==target&&patch.Name==name;
    }
}
