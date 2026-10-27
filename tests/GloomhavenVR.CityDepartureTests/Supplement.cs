using System;
using System.Reflection;
using UnityEngine.UI;

// Only compile/platform access not needed by the predecessor quest fixture. The
// shared NativeDepartureFixture's OnPlayerLeft, ACK and Proceed bodies are unchanged.
internal sealed partial class UIReadyToggle
{ public T GetComponent<T>() where T:class=>(window as T)!; }
internal sealed partial class UIMapMultiplayerController
{
    internal EReadyUpToggleStates m_State;
    internal void ConfirmCityEvent() { FFSNet.PlayerRegistry.MyPlayer.state.LatestProcessedActionID++; }
    internal void ResetUI() { Singleton<UIReadyToggle>.Instance.ToggleVisibility(false); }
    internal void ClearHostSelectedCityEvent() { Singleton<UIReadyToggle>.Instance.ToggleVisibility(false); }
    private void ProxyHostCancelledCityEvent() { Singleton<UIReadyToggle>.Instance.ToggleVisibility(false); }
    private void CancelPreviewCityEvent() { Singleton<UIReadyToggle>.Instance.ToggleVisibility(false); }
}
internal sealed class MapChoreographer
{
    internal void DeterminePlayerToggleInteractability() { }
    internal void ProxySelectedCityEvent(FFSNet.GameAction action)
    { Singleton<UIReadyToggle>.Instance.Initialize(readyUpToggleState:EReadyUpToggleStates.CityEvents);Singleton<UIMapMultiplayerController>.Instance.m_State=EReadyUpToggleStates.CityEvents; }
}
internal sealed class UIEventPanel { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] internal void StartEvent() { } }
namespace FFSNet
{
    internal sealed partial class GameAction { public int ActionID; }
    internal sealed class NativePlayerState {private int _id;public int LatestProcessedActionID {get{NativeProbeFaults.Read("city.counter");return _id;}set=>_id=value;} }
    internal sealed partial class NetworkPlayer { public NativePlayerState state {get;}=new(); }
}
#if !REAL_HARMONY
namespace HarmonyLib
{
    internal enum MethodType { Getter }
    internal sealed partial class HarmonyPatch
    { public HarmonyPatch() { } public HarmonyPatch(Type type,string name,MethodType method) { } }
}
#endif
namespace GloomhavenVR.Core
{
    internal static partial class VRLog
    { internal static void Info(string scope,string text)=>NativeProbeFaults.Read("city.info-log");internal static void Warn(string scope,string text)=>Alert(scope,text); }
}
namespace GloomhavenVR.Net
{ internal static class SharedMapRunIdentity { internal static uint Key {get{NativeProbeFaults.Read("city.run");return 8193;}}internal static uint Add(uint hash,string value)=>hash+1; } }
namespace MapRuleLibrary.PhaseManager { internal enum EMapPhaseType { InHQ,AtLinkedScenario } }
namespace MapRuleLibrary.Adventure
{
    internal sealed class CityMapState {private MapRuleLibrary.PhaseManager.EMapPhaseType _phase;internal MapRuleLibrary.PhaseManager.EMapPhaseType CurrentMapPhaseType {get{NativeProbeFaults.Read("city.phase");return _phase;}set=>_phase=value;} }
    internal static class AdventureState { internal static CityMapState MapState=new(); }
}
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver {internal static bool Active=true;} }
