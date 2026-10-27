using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.WorldUI.MapRoom;

// The full production helper is compiled unchanged. This deterministic boundary supplies
// native singleton/permission/lifecycle events; it is not a Photon or headset renderer.
internal static class Singleton<T> where T:class
{ internal static T Instance=null!;internal static bool IsInitialized=>Instance!=null; }
internal enum EReadyUpToggleStates { NotSet,Quests,CityEvents,Reward,Retirement,TownRecords }
internal sealed class UIReadyToggle
{
    internal EReadyUpToggleStates readyUpToggleState;
    internal bool _requestVisible,_allPlayersReady;
    internal readonly HashSet<object> _visibilityRequests=new();
    internal bool IsInteractable,ToggleInteractable;
    internal readonly UnityEngine.UI.UIWindow Window=new();
    internal int ShowCount,HideCount,InputVotes,AutomaticVotes;
    internal bool NativeShouldBeVisible=>_requestVisible&&IsInteractable&&!_allPlayersReady&&_visibilityRequests.Count==0;
    internal bool ShouldBeVisible
    {
        get { object?[] args={this,NativeShouldBeVisible};Hooks.Invoke("ObserveDisabledCityOffer",args);return (bool)args[1]!; }
    }
    internal T GetComponent<T>() where T:class=> (Window as T)!;
    internal void UpdateVisiblity() { bool show=ShouldBeVisible;if(show!=Window.IsOpen) { Window.IsOpen=show;if(show)ShowCount++;else HideCount++; } }
    internal void Initialize(EReadyUpToggleStates state)
    { Hooks.Invoke("BeforeReadyInitialize");readyUpToggleState=state;IsInteractable=ToggleInteractable=false;_allPlayersReady=false;UpdateVisiblity(); }
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
    { m_State=state;var t=Singleton<UIReadyToggle>.Instance;t._requestVisible=show;t.UpdateVisiblity();if(show)ShowRequests++; }
    internal void OnSelectedCityEvent() { Singleton<UIReadyToggle>.Instance.AutomaticVotes++; }
    internal void ConfirmCityEvent() { }
    internal void ResetUI() { }
    internal void ClearHostSelectedCityEvent() { }
}
internal sealed class MapChoreographer
{
    internal bool Participant=true;
    internal int PermissionChecks;
    internal void DeterminePlayerToggleInteractability()
    { PermissionChecks++;var t=Singleton<UIReadyToggle>.Instance;t.IsInteractable=t.ToggleInteractable=Participant;t.UpdateVisiblity(); }
    internal void ProxySelectedCityEvent() { }
}
internal sealed class UIEventPanel { internal void StartEvent() { } }
namespace UnityEngine.UI { internal sealed class UIWindow { internal bool IsOpen; } }
namespace FFSNet
{
    internal static class FFSNetwork { internal static bool IsOnline=true,IsHost=true;internal static bool IsClient=>IsOnline&&!IsHost; }
    internal enum GameActionType { SelectCityEvent=7,SelectQuest=8 }
    internal sealed class GameAction { internal int ActionID,ActionTypeID; }
    internal sealed class PlayerState { public int LatestProcessedActionID {get;set;} }
    internal sealed class NetworkPlayer { public PlayerState state {get;}=new(); }
    internal static class PlayerRegistry { internal static NetworkPlayer? MyPlayer=new(); }
}
namespace MapRuleLibrary.PhaseManager { internal enum EMapPhaseType { InHQ,AtLinkedScenario,MapEvent,Travel } }
namespace MapRuleLibrary.Adventure
{
    internal sealed class MapState { internal MapRuleLibrary.PhaseManager.EMapPhaseType CurrentMapPhaseType; }
    internal static class AdventureState { internal static MapState? MapState=new(); }
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
    internal static class VRSession {internal static HarmonyLib.Harmony? Harmony=new();}
    internal static class VRLog {internal static void Info(string scope,string text){}internal static void Warn(string scope,string text)=>Warnings.Add(text);internal static readonly List<string> Warnings=new();}
}
namespace GloomhavenVR.Net
{
    internal static class SharedMapRunIdentity
    { internal static uint Key=8193;internal static uint Add(uint hash,string value){unchecked{foreach(char c in value)hash=(hash^c)*16777619u;return (hash^0xffffu)*16777619u;}} }
}
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver {internal static bool Active=true;} }
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
