#pragma warning disable CS0649
using System;
using System.Collections;
using System.Collections.Generic;
using FFSNet;
using UnityEngine.Events;

// Explicit model, GUI animation, clocks and callback scheduling boundaries. The
// original native StoryController and ActionProcessor method bodies run unchanged.
internal sealed class DialogLineDTO { internal string text="",character=""; }
internal sealed class MapMessage { internal string ID="MapMessage"; }
internal sealed class ScenarioStamp { internal string MatchSessionID="run-A"; }
internal sealed class CLevelMessage
{
 internal string MessageName="YMLDefined_ScenarioStart"; internal CLevelTrigger DisplayTrigger=new(0);
}
internal sealed partial class CLevelTrigger
{
 internal enum ELevelMessagePredefinedDisplayTrigger { ScenarioStart,ScenarioEnd,RoomRevealed }
 internal bool IsTriggeredByDismiss,IsUIEventTypeTrigger,EventTriggerPlayedMessageReqOR,EventTriggerNotPlayedMessageReqOR;
 internal int EventTriggerTypeInt,EventTriggerSubTypeInt,EventTriggerContextTypeInt,EventTriggerContextSubTypeInt,EventTriggerRound,EventTriggerContextIndex;
 internal string EventTriggerActorName="",EventTriggerActorGuid="",EventTriggerContextId="",EventTriggerPlayedMessageReq="",EventTriggerNotPlayedMessageReq="",EventAliveActorGUID="";
 internal object? EventTriggerTile,EventFilter;
 internal CLevelTrigger(int kind):this((ELevelMessagePredefinedDisplayTrigger)kind) { }
}
internal sealed class UICharacterStoryBox
{
 internal int currentDialogIndex; internal List<DialogLineDTO>? dialogs; internal Action? Finished; internal int ShowLineCalls;
 internal void Show(List<DialogLineDTO>? pages,Action onFinish,bool navigateToPreviousStateWhenHidden)
 { dialogs=pages;Finished=onFinish;currentDialogIndex=0; }
 internal void ShowLine(int target)
 { ++ShowLineCalls;if(target>=(dialogs?.Count??0))Finished?.Invoke();else currentDialogIndex=target; }
}
internal sealed class MapStoryController
{
 internal sealed class MapDialogInfo:StoryController.DialogInfo { }
 internal MapDialogInfo? m_CurrentMessage; internal UnityEngine.UI.UIWindow window=new();internal UICharacterStoryBox dialogBox=new();
}
internal static class CoroutineHelper { internal static void RunNextFrame(Action work)=>work(); }
internal sealed class UINavigation { internal NavigationState StateMachine=new(); }
internal sealed class NavigationState { internal void ToNonMenuPreviousState(object filter) { } }
internal static class AutoTestController { internal static AutoTestInstance s_Instance=new(); }
internal sealed class AutoTestInstance { internal bool AutotestStarted; }
internal static class PhaseManager { internal static CPhase.PhaseType PhaseType=CPhase.PhaseType.SelectAbilityCardsOrLongRest; }
internal static class CPhase { internal enum PhaseType { SelectAbilityCardsOrLongRest } }
internal enum EGameState { Scenario,Map }
internal enum EGameMode { Campaign,Guildmaster,SingleScenario,Autotest }
internal enum EResult { None,Win,Lose }
internal sealed class GlobalData
{
 internal EGameState CurrentGameState=EGameState.Scenario;internal EGameMode GameMode=EGameMode.Campaign;
 internal PartyData CurrentAdventureData=new(); internal bool CurrentlyPlayingCustomLevel;
}
internal sealed class PartyData { internal AdventureMapData AdventureMapState=new(); }
internal sealed class AdventureMapData { internal int ChestApplications;internal void ApplyRoundChestRewards()=>++ChestApplications; }
internal sealed class ErrorMessage { internal void ShowMessageDefaultTitle(string code,string action,string stack,Action callback,string error)=>throw new Exception(error); }
internal static class UnityGameEditorRuntime { internal static void ErrorHandlingUnloadSceneAndLoadMainMenu() { } }
internal sealed class ReadyButton { internal void Toggle(bool active) { } }
internal sealed class PhaseBannerHandler { internal void Disable() { } }
internal sealed class CardsHandManager
{ internal static CardsHandManager Instance=new();internal bool IsFullDeckPreviewAllowed;internal void DisableHands() { } }
internal sealed class ScenarioAchievementProgress { }
internal static class SimpleLog { internal static void AddToSimpleLog(string message) { } }
internal sealed class LevelEventsController
{
 internal static LevelEventsController s_Instance=>Singleton<LevelEventsController>.Instance;
 internal static bool s_EventsControllerActive=true;internal int Completes;internal bool LastSuccess;
 internal void MessageWasDismissed(CLevelMessage message) { }
 internal void CompleteLevel(bool completionSuccess,bool userQuit,UnityAction action)
 { ++Completes;LastSuccess=completionSuccess;action(); }
}
internal enum EControllerInputAreaType { None }
internal sealed class ControllerInputAreaManager
{
 internal static ControllerInputAreaManager Instance=new();
 internal void SetDefaultFocusArea(EControllerInputAreaType area) { }
 internal void FocusArea(EControllerInputAreaType area) { }
}
internal sealed class UIResultsManager
{
 internal enum ERetryOptionType { None,Only,Additional }
 internal void Show(float delay,Action<EResult> onFinish,EResult result)=>UINewAdventureResultsManager.Implementation.Show(delay,onFinish,result);
}
internal sealed class UINewAdventureResultsManager
{
 internal static UINewAdventureResultsManager Implementation=new();internal int NativeShows;internal bool ResultVisible;internal EResult Result;
 internal Action<UnityAction>? FinishedAnimation;
 internal void Show(float delay,Action<EResult> onFinish,EResult result,UIResultsManager.ERetryOptionType retry=UIResultsManager.ERetryOptionType.None,
  UnityAction? retryCallback=null,List<ScenarioAchievementProgress>? progress=null,Action<UnityAction>? onFinishedResultAnimation=null)
 { ++NativeShows;ResultVisible=true;Result=result;FinishedAnimation=onFinishedResultAnimation; }
}
internal static class NativeClock
{
 internal static void Run(IEnumerator routine)
 { while(routine.MoveNext())if(routine.Current is IEnumerator nested)Run(nested); }
}
namespace UnityEngine
{
 internal struct Vector3 { }
 internal struct Quaternion { }
 internal static class Time { internal static float unscaledTime; }
}
namespace UnityEngine.UI
{
 internal sealed class UIWindow
 { internal bool IsOpen,IsVisible;internal int Shows,Hides;internal void Show(bool instant=false){++Shows;IsOpen=IsVisible=true;}internal void Hide(){++Hides;IsOpen=IsVisible=false;} }
}
namespace UnityEngine.Events { internal delegate void UnityAction(); }
namespace GloomhavenVR.WorldUI
{
 internal enum SharedWindowKind { None,ScenarioStory,MapStory,QuestConfirm,Encounter,Video,RewardShowcase,Results }
 internal static class SharedWindows
 { internal static bool ParticipatesHere(SharedWindowKind kind)=>FFSNetwork.IsOnline; }
 internal sealed class ConvertedPanel
 { internal bool SharedSpawnPoseReady;internal SharedWindowKind SharedDesignKind=SharedWindowKind.ScenarioStory; }
 internal static class SharedWindowSize
 { internal static bool UsesSharedWorldFrame(ConvertedPanel? panel)=>panel!=null&&FFSNetwork.IsOnline; }
 internal static class ModalFallback
 {
  internal static bool ApplySharedInitialPose(ConvertedPanel panel,UnityEngine.Vector3 position,UnityEngine.Quaternion rotation,float factor)
  { panel.SharedSpawnPoseReady=true;return true; }
 }
}
namespace GloomhavenVR.Net
{
 internal struct PresenceState
 { internal bool HasMapStoryLifecycle;internal MapStoryOpening[]? MapStoryLifecycleEntries; }
}
